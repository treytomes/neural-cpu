namespace NeuralCpu.Core.Components;

/// <summary>
/// ALU operation codes.
/// </summary>
public enum AluOp
{
    // Arithmetic operations
    Add,
    Sub,
    Mul,
    Div,
    Inc,        // Increment by 1
    Dec,        // Decrement by 1

    // Bitwise logic operations
    Not,
    And,
    Nand,
    Or,
    Nor,
    Xor,
    Xnor,

    // Additional unary operations
    Neg,        // Two's complement negate
    Abs,        // Absolute value
    Clz,        // Count leading zeros

    // Shift operations
    Shl,        // Shift left (logical)
    Shr,        // Shift right (logical)
    Sar,        // Shift right (arithmetic - sign extend)

    // Rotation operations
    Rol,        // Rotate left
    Ror         // Rotate right
}
