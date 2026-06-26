using NeuralCpu.Core.Components;

namespace NeuralCpu.Core.Events;

/// <summary>
/// Command to load a value from memory (direct addressing) into a register.
/// </summary>
public sealed record LoadCommand(
    ulong Address,
    RegisterId Destination)
{
    public DateTimeOffset Timestamp { get; } = DateTimeOffset.UtcNow;
}
