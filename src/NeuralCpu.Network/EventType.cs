namespace NeuralCpu.Network;

/// <summary>
/// Enumeration of all CPU event types the network can observe and emit.
/// Used for structured event encoding with separate type and parameter fields.
/// </summary>
public enum EventType : byte
{
    None = 0,

    // Clock events
    ClockRising = 1,
    ClockFalling = 2,
    HardwareInterrupt = 3,
    SystemReset = 4,

    // Register operations
    RegisterWrite = 10,
    RegisterWriteWithFlags = 11,
    RegisterCopy = 12,
    RegisterRead = 13,
    RegisterChanged = 14,

    // ALU binary operations
    AluAdd = 20,
    AluSub = 21,
    AluMul = 22,
    AluDiv = 23,
    AluAnd = 24,
    AluNand = 25,
    AluOr = 26,
    AluNor = 27,
    AluXor = 28,
    AluXnor = 29,
    AluShl = 30,
    AluShr = 31,
    AluSar = 32,
    AluRol = 33,
    AluRor = 34,

    // ALU unary operations
    AluNot = 40,
    AluInc = 41,
    AluDec = 42,
    AluNeg = 43,
    AluAbs = 44,
    AluClz = 45,

    // Stack operations
    Push = 50,
    PushImmediate = 51,
    Pop = 52,
    Peek = 53,
    Dup = 54,

    // Memory operations
    LoadDirect = 60,
    LoadIndirect = 61,
    StoreDirect = 62,
    StoreIndirect = 63,

    // Device I/O operations
    InputRead = 70,
    OutputWrite = 71,
}
