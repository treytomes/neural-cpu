using NeuralCpu.Core.Components;

namespace NeuralCpu.Core.Events;

/// <summary>
/// Command to store a register value to memory (register-indirect addressing).
/// The address is read from the AddressRegister.
/// </summary>
public sealed record StoreIndirectCommand(
    RegisterId AddressRegister,
    RegisterId Source)
{
    public DateTimeOffset Timestamp { get; } = DateTimeOffset.UtcNow;
}
