using NeuralCpu.Core.Components;

namespace NeuralCpu.Core.Events;

/// <summary>
/// Response to a <see cref="RegisterReadRequest"/> containing the register's current value.
/// </summary>
public sealed record RegisterReadResponse(
    RegisterId RegisterId,
    ulong Value,
    Guid RequestId)
{
    public DateTimeOffset Timestamp { get; } = DateTimeOffset.UtcNow;
}
