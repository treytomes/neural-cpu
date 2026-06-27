using NeuralCpu.Core.Components;

namespace NeuralCpu.Network;

/// <summary>
/// Structured event representation for network encoding.
/// Each field has semantic meaning - position determines role (type, registers, value).
/// This allows the network to learn relationships like "42 written to R0" followed by "R0 now contains 42".
/// </summary>
public readonly struct EncodedEvent
{
    #region Properties

    /// <summary>Event type (what operation is this)</summary>
    public EventType Type { get; init; }

    /// <summary>First register operand (or None if not used)</summary>
    public RegisterId Reg1 { get; init; }

    /// <summary>Second register operand (or None if not used)</summary>
    public RegisterId Reg2 { get; init; }

    /// <summary>Third register operand (or None if not used)</summary>
    public RegisterId Reg3 { get; init; }

    /// <summary>Device ID for I/O operations (0-15, or 0 if not applicable)</summary>
    public byte DeviceId { get; init; }

    /// <summary>Immediate value, address, or data (0 if not used)</summary>
    public ulong Value { get; init; }

    #endregion

    #region Constructors

    public EncodedEvent(EventType type, RegisterId reg1 = RegisterId.None, RegisterId reg2 = RegisterId.None,
        RegisterId reg3 = RegisterId.None, byte deviceId = 0, ulong value = 0)
    {
        Type = type;
        Reg1 = reg1;
        Reg2 = reg2;
        Reg3 = reg3;
        DeviceId = deviceId;
        Value = value;
    }

    #endregion

    #region Methods

    public override string ToString()
    {
        var parts = new List<string> { Type.ToString() };

        if (Reg1 != RegisterId.None) parts.Add($"{Reg1}");
        if (Reg2 != RegisterId.None) parts.Add($"{Reg2}");
        if (Reg3 != RegisterId.None) parts.Add($"{Reg3}");
        if (DeviceId != 0) parts.Add($"Dev{DeviceId}");
        if (Value != 0) parts.Add($"0x{Value:X}");

        return string.Join(", ", parts);
    }

    #endregion
}
