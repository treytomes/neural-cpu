namespace NeuralCpu.Core.Components;

/// <summary>
/// Identifies a specific register in the CPU.
/// </summary>
public enum RegisterId
{
    /// <summary>No register (used when register field is not applicable).</summary>
    None = -1,

    /// <summary>General purpose register 0.</summary>
    R0 = 0,

    /// <summary>General purpose register 1.</summary>
    R1,

    /// <summary>General purpose register 2.</summary>
    R2,

    /// <summary>General purpose register 3.</summary>
    R3,

    /// <summary>Program Counter - points to next instruction.</summary>
    PC,

    /// <summary>Stack Pointer - points to top of stack.</summary>
    SP,

    /// <summary>Flags register (Zero, Carry, Negative, Overflow, etc.).</summary>
    Flags
}
