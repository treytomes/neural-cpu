using TorchSharp;
using TorchSharp.Modules;
using static TorchSharp.torch;
using static TorchSharp.torch.nn;

namespace NeuralCpu.Network.Neural;

/// <summary>
/// Multi-head self-attention mechanism for transformers.
/// Learns which parts of a sequence are relevant to each other.
/// </summary>
public sealed class MultiHeadAttention : FreezableModule
{
    #region Fields

    private readonly int _numHeads;
    private readonly long _embeddingDim;
    private readonly long _headDim;
    private readonly Linear _queryProjection;
    private readonly Linear _keyProjection;
    private readonly Linear _valueProjection;
    private readonly Linear _outputProjection;
    private readonly Dropout? _dropout;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates multi-head attention module.
    /// </summary>
    /// <param name="name">Module name.</param>
    /// <param name="embeddingDim">Embedding dimension (must be divisible by numHeads).</param>
    /// <param name="numHeads">Number of attention heads.</param>
    /// <param name="dropoutRate">Dropout rate for attention weights.</param>
    public MultiHeadAttention(string name, long embeddingDim, int numHeads, double dropoutRate = 0.1)
        : base(name)
    {
        if (embeddingDim % numHeads != 0)
            throw new ArgumentException($"Embedding dimension ({embeddingDim}) must be divisible by number of heads ({numHeads})");

        _numHeads = numHeads;
        _embeddingDim = embeddingDim;
        _headDim = embeddingDim / numHeads;

        // Linear projections for Q, K, V
        _queryProjection = Linear(embeddingDim, embeddingDim);
        _keyProjection = Linear(embeddingDim, embeddingDim);
        _valueProjection = Linear(embeddingDim, embeddingDim);

        // Output projection
        _outputProjection = Linear(embeddingDim, embeddingDim);

        _dropout = dropoutRate > 0 ? Dropout(dropoutRate) : null;

        RegisterComponents();
    }

    #endregion

    #region Methods

    /// <summary>
    /// Forward pass with self-attention.
    /// </summary>
    /// <param name="input">Input tensor (batch_size, seq_len, embedding_dim).</param>
    /// <returns>Attended output (batch_size, seq_len, embedding_dim).</returns>
    public override Tensor forward(Tensor input)
    {
        var batchSize = input.shape[0];
        var seqLen = input.shape[1];

        // Project to Q, K, V
        var Q = _queryProjection.forward(input);  // (batch, seq_len, embedding_dim)
        var K = _keyProjection.forward(input);
        var V = _valueProjection.forward(input);

        // Reshape for multi-head attention: (batch, seq_len, num_heads, head_dim)
        Q = Q.view(batchSize, seqLen, _numHeads, _headDim).transpose(1, 2);  // (batch, num_heads, seq_len, head_dim)
        K = K.view(batchSize, seqLen, _numHeads, _headDim).transpose(1, 2);
        V = V.view(batchSize, seqLen, _numHeads, _headDim).transpose(1, 2);

        // Scaled dot-product attention
        // Scores = Q @ K^T / sqrt(head_dim)
        var scores = Q.matmul(K.transpose(-2, -1)) / Math.Sqrt(_headDim);  // (batch, num_heads, seq_len, seq_len)

        // Softmax over last dimension (attention weights)
        var attentionWeights = functional.softmax(scores, dim: -1);

        if (_dropout is not null)
        {
            attentionWeights = _dropout.forward(attentionWeights);
        }

        // Apply attention to values
        var attended = attentionWeights.matmul(V);  // (batch, num_heads, seq_len, head_dim)

        // Concatenate heads: (batch, seq_len, embedding_dim)
        attended = attended.transpose(1, 2).contiguous().view(batchSize, seqLen, _embeddingDim);

        // Final output projection
        var output = _outputProjection.forward(attended);

        return output;
    }

    /// <summary>
    /// Forward pass with cross-attention between query and key/value sequences.
    /// Used for dual-attention between event stream and CPU state.
    /// </summary>
    /// <param name="query">Query tensor (batch_size, query_len, embedding_dim).</param>
    /// <param name="keyValue">Key/Value tensor (batch_size, kv_len, embedding_dim).</param>
    /// <returns>Attended output (batch_size, query_len, embedding_dim).</returns>
    public Tensor CrossAttention(Tensor query, Tensor keyValue)
    {
        var batchSize = query.shape[0];
        var queryLen = query.shape[1];
        var kvLen = keyValue.shape[1];

        // Project query from query sequence, key/value from keyValue sequence
        var Q = _queryProjection.forward(query);
        var K = _keyProjection.forward(keyValue);
        var V = _valueProjection.forward(keyValue);

        // Reshape for multi-head attention
        Q = Q.view(batchSize, queryLen, _numHeads, _headDim).transpose(1, 2);
        K = K.view(batchSize, kvLen, _numHeads, _headDim).transpose(1, 2);
        V = V.view(batchSize, kvLen, _numHeads, _headDim).transpose(1, 2);

        // Scaled dot-product attention
        var scores = Q.matmul(K.transpose(-2, -1)) / Math.Sqrt(_headDim);
        var attentionWeights = functional.softmax(scores, dim: -1);

        if (_dropout is not null)
        {
            attentionWeights = _dropout.forward(attentionWeights);
        }

        var attended = attentionWeights.matmul(V);
        attended = attended.transpose(1, 2).contiguous().view(batchSize, queryLen, _embeddingDim);

        var output = _outputProjection.forward(attended);

        return output;
    }

    #endregion
}
