using NeuralCpu.Core.Components;

namespace NeuralCpu.Network.Neural;

/// <summary>
/// Validates EncodedEvent structures against CPU event requirements.
/// Used in DPO (Direct Preference Optimization) training to teach the network
/// to prefer valid events over invalid ones.
/// </summary>
public static class EventValidationDPO
{
    #region Validation

    /// <summary>
    /// Validates an EncodedEvent against event-specific requirements.
    /// </summary>
    /// <returns>Validation result with errors if invalid.</returns>
    public static ValidationResult Validate(EncodedEvent evt)
    {
        var errors = new List<string>();
        var req = GetRequirements(evt.Type);

        // Check required registers
        if (req.Reg1Required && evt.Reg1 == RegisterId.None)
        {
            errors.Add($"{evt.Type} requires Reg1 to be set (not None)");
        }

        if (req.Reg2Required && evt.Reg2 == RegisterId.None)
        {
            errors.Add($"{evt.Type} requires Reg2 to be set (not None)");
        }

        if (req.Reg3Required && evt.Reg3 == RegisterId.None)
        {
            errors.Add($"{evt.Type} requires Reg3 to be set (not None)");
        }

        // Check unused registers should be None
        if (!req.Reg1Required && !req.Reg1Optional && evt.Reg1 != RegisterId.None)
        {
            errors.Add($"{evt.Type} requires Reg1 to be None (unused)");
        }

        if (!req.Reg2Required && !req.Reg2Optional && evt.Reg2 != RegisterId.None)
        {
            errors.Add($"{evt.Type} requires Reg2 to be None (unused)");
        }

        if (!req.Reg3Required && !req.Reg3Optional && evt.Reg3 != RegisterId.None)
        {
            errors.Add($"{evt.Type} requires Reg3 to be None (unused)");
        }

        // Check read-only registers (PC, SP)
        if (req.Reg1Required && IsReadOnlyDestination(evt.Reg1))
        {
            errors.Add($"Cannot use {evt.Reg1} as destination register (read-only or managed)");
        }

        if (req.Reg2Required && req.IsReg2Destination && IsReadOnlyDestination(evt.Reg2))
        {
            errors.Add($"Cannot use {evt.Reg2} as destination register (read-only or managed)");
        }

        if (req.Reg3Required && IsReadOnlyDestination(evt.Reg3))
        {
            errors.Add($"Cannot use {evt.Reg3} as destination register (read-only or managed)");
        }

        return new ValidationResult(errors.Count == 0, errors);
    }

    /// <summary>
    /// Checks if a register is read-only or managed (can't be written to directly).
    /// </summary>
    private static bool IsReadOnlyDestination(RegisterId reg)
    {
        return reg switch
        {
            RegisterId.PC => true,  // PC is a time counter, not writable
            RegisterId.SP => true,  // SP is managed by stack operations
            _ => false
        };
    }

    #endregion

    #region Correction

    /// <summary>
    /// Corrects an invalid EncodedEvent to make it valid.
    /// Used in DPO training to generate "preferred" events from invalid ones.
    /// </summary>
    public static EncodedEvent Correct(EncodedEvent evt)
    {
        var req = GetRequirements(evt.Type);

        var reg1 = evt.Reg1;
        var reg2 = evt.Reg2;
        var reg3 = evt.Reg3;
        var value = evt.Value;

        // Fix missing required registers (set to R0 default)
        if (req.Reg1Required && reg1 == RegisterId.None)
        {
            reg1 = RegisterId.R0;
        }

        if (req.Reg2Required && reg2 == RegisterId.None)
        {
            reg2 = RegisterId.R0;
        }

        if (req.Reg3Required && reg3 == RegisterId.None)
        {
            reg3 = RegisterId.R0;
        }

        // Fix unused registers (set to None)
        if (!req.Reg1Required && !req.Reg1Optional && reg1 != RegisterId.None)
        {
            reg1 = RegisterId.None;
        }

        if (!req.Reg2Required && !req.Reg2Optional && reg2 != RegisterId.None)
        {
            reg2 = RegisterId.None;
        }

        if (!req.Reg3Required && !req.Reg3Optional && reg3 != RegisterId.None)
        {
            reg3 = RegisterId.None;
        }

        // Fix read-only destinations
        if (req.Reg1Required && IsReadOnlyDestination(reg1))
        {
            reg1 = RegisterId.R0; // Replace with R0
        }

        if (req.Reg2Required && req.IsReg2Destination && IsReadOnlyDestination(reg2))
        {
            reg2 = RegisterId.R0;
        }

        if (req.Reg3Required && IsReadOnlyDestination(reg3))
        {
            reg3 = RegisterId.R0;
        }

        return new EncodedEvent(evt.Type, reg1, reg2, reg3, evt.DeviceId, value);
    }

    #endregion

    #region Requirements

    /// <summary>
    /// Gets the register and value requirements for a given event type.
    /// </summary>
    public static EventRequirements GetRequirements(EventType eventType)
    {
        return eventType switch
        {
            // Register operations
            EventType.RegisterWrite => new(
                Reg1Required: true,  // Destination
                Reg2Required: false,
                Reg3Required: false,
                ValueUsed: true),

            EventType.RegisterCopy => new(
                Reg1Required: true,  // Source
                Reg2Required: true,  // Destination
                Reg3Required: false,
                ValueUsed: false,
                IsReg2Destination: true),

            // ALU binary operations (15 total)
            EventType.AluAdd or EventType.AluSub or EventType.AluMul or EventType.AluDiv or
            EventType.AluAnd or EventType.AluNand or EventType.AluOr or EventType.AluNor or
            EventType.AluXor or EventType.AluXnor or EventType.AluShl or EventType.AluShr or
            EventType.AluSar or EventType.AluRol or EventType.AluRor
                => new(
                    Reg1Required: true,  // SourceA
                    Reg2Required: true,  // SourceB
                    Reg3Required: true,  // Destination
                    ValueUsed: false),

            // ALU unary operations (6 total)
            EventType.AluNot or EventType.AluInc or EventType.AluDec or
            EventType.AluNeg or EventType.AluAbs or EventType.AluClz
                => new(
                    Reg1Required: true,  // Source
                    Reg2Required: true,  // Destination
                    Reg3Required: false,
                    ValueUsed: false,
                    IsReg2Destination: true),

            // Stack operations
            EventType.Push => new(
                Reg1Required: true,  // Source register
                Reg2Required: false,
                Reg3Required: false,
                ValueUsed: false),

            EventType.PushImmediate => new(
                Reg1Required: false,
                Reg2Required: false,
                Reg3Required: false,
                ValueUsed: true),

            EventType.Pop or EventType.Peek => new(
                Reg1Required: true,  // Destination
                Reg2Required: false,
                Reg3Required: false,
                ValueUsed: false),

            EventType.Dup => new(
                Reg1Required: false,
                Reg2Required: false,
                Reg3Required: false,
                ValueUsed: false),

            // Memory operations
            EventType.LoadDirect => new(
                Reg1Required: true,  // Destination
                Reg2Required: false,
                Reg3Required: false,
                ValueUsed: true),    // Address in value

            EventType.LoadIndirect => new(
                Reg1Required: true,  // Address register
                Reg2Required: true,  // Destination
                Reg3Required: false,
                ValueUsed: false,
                IsReg2Destination: true),

            EventType.StoreDirect => new(
                Reg1Required: true,  // Source
                Reg2Required: false,
                Reg3Required: false,
                ValueUsed: true),    // Address in value

            EventType.StoreIndirect => new(
                Reg1Required: true,  // Address register
                Reg2Required: true,  // Source
                Reg3Required: false,
                ValueUsed: false),

            // Hardware interrupt
            EventType.HardwareInterrupt => new(
                Reg1Required: false,
                Reg2Required: false,
                Reg3Required: false,
                ValueUsed: true),    // Interrupt vector (byte)

            // Device I/O operations
            EventType.InputRead => new(
                Reg1Required: false,
                Reg2Required: false,
                Reg3Required: false,
                ValueUsed: false,
                DeviceIdUsed: true),  // DeviceId (0-15)

            EventType.OutputWrite => new(
                Reg1Required: false,
                Reg2Required: false,
                Reg3Required: false,
                ValueUsed: true,      // Value to write
                DeviceIdUsed: true),  // DeviceId (0-15)

            // Default: no requirements (observe-only events)
            _ => new(
                Reg1Required: false,
                Reg2Required: false,
                Reg3Required: false,
                ValueUsed: false)
        };
    }

    #endregion
}

/// <summary>
/// Result of validating an EncodedEvent.
/// </summary>
/// <param name="IsValid">True if event is valid.</param>
/// <param name="Errors">List of validation errors (empty if valid).</param>
public record ValidationResult(bool IsValid, List<string> Errors);

/// <summary>
/// Specifies register and value requirements for an event type.
/// </summary>
/// <param name="Reg1Required">Reg1 must not be None.</param>
/// <param name="Reg2Required">Reg2 must not be None.</param>
/// <param name="Reg3Required">Reg3 must not be None.</param>
/// <param name="ValueUsed">Value field is used by this event.</param>
/// <param name="Reg1Optional">Reg1 can be set or None (not enforced).</param>
/// <param name="Reg2Optional">Reg2 can be set or None (not enforced).</param>
/// <param name="Reg3Optional">Reg3 can be set or None (not enforced).</param>
/// <param name="IsReg2Destination">Reg2 is a destination register (check read-only).</param>
/// <param name="DeviceIdUsed">DeviceId field is used by this event (I/O operations).</param>
public record EventRequirements(
    bool Reg1Required,
    bool Reg2Required,
    bool Reg3Required,
    bool ValueUsed,
    bool Reg1Optional = false,
    bool Reg2Optional = false,
    bool Reg3Optional = false,
    bool IsReg2Destination = false,
    bool DeviceIdUsed = false);
