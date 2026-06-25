namespace NeuralCpu.Core.Components;

/// <summary>
/// CPU status flags stored in the Flags register.
/// Each flag is represented by a single bit.
/// </summary>
[Flags]
public enum CpuFlags : ulong
{
    /// <summary>No flags set.</summary>
    None = 0,

    /// <summary>Zero flag - set when result is zero.</summary>
    Zero = 1 << 0,

    /// <summary>Carry flag - set when unsigned overflow occurs.</summary>
    Carry = 1 << 1,

    /// <summary>Negative flag - set when result is negative (MSB is 1).</summary>
    Negative = 1 << 2,

    /// <summary>Overflow flag - set when signed overflow occurs.</summary>
    Overflow = 1 << 3,

    /// <summary>Divide by zero flag - set when division by zero is attempted.</summary>
    DivideByZero = 1 << 4
}

/// <summary>
/// Helper methods for working with CPU flags.
/// </summary>
public static class CpuFlagsExtensions
{
    /// <summary>
    /// Checks if a specific flag is set in the flags register value.
    /// </summary>
    public static bool HasFlag(this ulong flagsValue, CpuFlags flag)
    {
        return (flagsValue & (ulong)flag) != 0;
    }

    /// <summary>
    /// Sets a specific flag in the flags register value.
    /// </summary>
    public static ulong SetFlag(this ulong flagsValue, CpuFlags flag, bool value)
    {
        if (value)
            return flagsValue | (ulong)flag;
        else
            return flagsValue & ~(ulong)flag;
    }

    /// <summary>
    /// Gets a formatted string representation of the flags (e.g., "ZCNOD" or "z-n--").
    /// </summary>
    public static string ToFlagString(this ulong flagsValue)
    {
        return $"{(flagsValue.HasFlag(CpuFlags.Zero) ? 'Z' : '-')}" +
               $"{(flagsValue.HasFlag(CpuFlags.Carry) ? 'C' : '-')}" +
               $"{(flagsValue.HasFlag(CpuFlags.Negative) ? 'N' : '-')}" +
               $"{(flagsValue.HasFlag(CpuFlags.Overflow) ? 'O' : '-')}" +
               $"{(flagsValue.HasFlag(CpuFlags.DivideByZero) ? 'D' : '-')}";
    }
}
