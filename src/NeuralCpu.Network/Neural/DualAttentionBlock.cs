using TorchSharp;
using TorchSharp.Modules;
using static TorchSharp.torch;
using static TorchSharp.torch.nn;

namespace NeuralCpu.Network.Neural;

/// <summary>
/// Dual-attention mechanism that creates bidirectional attention between two sequences.
///
/// Architecture:
/// 1. Events attend to CPU state (which events matter depends on current CPU state)
/// 2. CPU state attends to events (which state matters depends on recent events)
/// 3. Results are combined to create cross-modal understanding
///
/// This allows the network to discover emergent patterns where event sequences
/// and CPU architecture interact in non-obvious ways.
/// </summary>
public sealed class DualAttentionBlock : FreezableModule
{
    #region Fields

    private readonly MultiHeadAttention _eventsAttendToCpu;  // Events query, CPU key/value
    private readonly MultiHeadAttention _cpuAttendsToEvents;  // CPU query, Events key/value
    private readonly LayerNorm _eventNorm;
    private readonly LayerNorm _cpuNorm;
    private readonly LayerNorm _fusionNorm;
    private readonly Linear _fusionProjection;
    private readonly Dropout? _dropout;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a dual-attention block.
    /// </summary>
    /// <param name="name">Block name.</param>
    /// <param name="embeddingDim">Embedding dimension for both sequences.</param>
    /// <param name="numHeads">Number of attention heads.</param>
    /// <param name="dropoutRate">Dropout rate.</param>
    public DualAttentionBlock(
        string name,
        long embeddingDim,
        int numHeads,
        double dropoutRate = 0.1)
        : base(name)
    {
        // Two cross-attention heads: events→CPU and CPU→events
        _eventsAttendToCpu = new MultiHeadAttention($"{name}_Events2CPU", embeddingDim, numHeads, dropoutRate);
        _cpuAttendsToEvents = new MultiHeadAttention($"{name}_CPU2Events", embeddingDim, numHeads, dropoutRate);

        // Layer norms for each pathway
        _eventNorm = LayerNorm(embeddingDim);
        _cpuNorm = LayerNorm(embeddingDim);

        // Fusion layer combines both attention directions
        _fusionNorm = LayerNorm(embeddingDim * 2);
        _fusionProjection = Linear(embeddingDim * 2, embeddingDim);

        _dropout = dropoutRate > 0 ? Dropout(dropoutRate) : null;

        RegisterComponents();
    }

    #endregion

    #region Methods

    /// <summary>
    /// Applies dual attention between event sequence and CPU state.
    /// </summary>
    /// <param name="eventEmbeddings">Event embeddings (batch, event_seq_len, embedding_dim).</param>
    /// <param name="cpuEmbeddings">CPU state embeddings (batch, cpu_seq_len, embedding_dim).</param>
    /// <returns>
    /// Tuple of:
    /// - Attended event embeddings (batch, event_seq_len, embedding_dim)
    /// - Attended CPU embeddings (batch, cpu_seq_len, embedding_dim)
    /// - Fused representation (batch, embedding_dim)
    /// </returns>
    public (Tensor attendedEvents, Tensor attendedCpu, Tensor fusedRepresentation) Forward(
        Tensor eventEmbeddings,
        Tensor cpuEmbeddings)
    {
        // Path 1: Events attend to CPU state
        // "Which events are relevant given the current CPU state?"
        var eventsAttendedByCpu = _eventsAttendToCpu.CrossAttention(
            query: eventEmbeddings,
            keyValue: cpuEmbeddings);

        if (_dropout is not null)
        {
            eventsAttendedByCpu = _dropout.forward(eventsAttendedByCpu);
        }

        // Residual connection + layer norm
        var attendedEvents = _eventNorm.forward(eventEmbeddings.add(eventsAttendedByCpu));

        // Path 2: CPU state attends to events
        // "Which CPU state elements are relevant given recent events?"
        var cpuAttendedByEvents = _cpuAttendsToEvents.CrossAttention(
            query: cpuEmbeddings,
            keyValue: eventEmbeddings);

        if (_dropout is not null)
        {
            cpuAttendedByEvents = _dropout.forward(cpuAttendedByEvents);
        }

        // Residual connection + layer norm
        var attendedCpu = _cpuNorm.forward(cpuEmbeddings.add(cpuAttendedByEvents));

        // Fusion: Combine both attention directions
        // Take mean over sequence dimension to get fixed-size representations
        var eventMean = attendedEvents.mean(new long[] { 1 });  // (batch, embedding_dim)
        var cpuMean = attendedCpu.mean(new long[] { 1 });       // (batch, embedding_dim)

        // Concatenate both pathways
        var concatenated = cat(new[] { eventMean, cpuMean }, dim: -1);  // (batch, embedding_dim * 2)

        // Project back to embedding_dim
        var fused = _fusionNorm.forward(concatenated);
        fused = _fusionProjection.forward(fused);  // (batch, embedding_dim)

        return (attendedEvents, attendedCpu, fused);
    }

    public override Tensor forward(Tensor input)
    {
        // For compatibility with FreezableModule, but dual attention needs two inputs
        // This should not be called directly - use Forward(events, cpu) instead
        throw new NotSupportedException(
            "DualAttentionBlock requires two inputs (events, cpu). " +
            "Call Forward(eventEmbeddings, cpuEmbeddings) instead.");
    }

    #endregion
}
