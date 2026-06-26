using NeuralCpu.Core.Components;

namespace NeuralCpu.Core.Events;

/// <summary>
/// Command to load a value from memory (register-indirect addressing) into a register.
/// The address is read from the AddressRegister.
/// </summary>
public sealed record LoadIndirectCommand(
    RegisterId AddressRegister,
    RegisterId Destination)
{
    public DateTimeOffset Timestamp { get; } = DateTimeOffset.UtcNow;
}
