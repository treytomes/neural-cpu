using NeuralCpu.Core.Components;

namespace NeuralCpu.Core.Events;

/// <summary>
/// Request to read the current value of a register.
/// Responds with <see cref="RegisterReadResponse"/>.
/// </summary>
public sealed record RegisterReadRequest(
    RegisterId RegisterId,
    Guid RequestId)
{
    public DateTimeOffset Timestamp { get; } = DateTimeOffset.UtcNow;
}
