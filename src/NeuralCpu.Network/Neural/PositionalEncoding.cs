using TorchSharp;
using TorchSharp.Modules;
using static TorchSharp.torch;
using static TorchSharp.torch.nn;

namespace NeuralCpu.Network.Neural;

/// <summary>
/// Positional encoding for transformer sequences.
/// Injects position information using sine/cosine functions.
/// </summary>
public sealed class PositionalEncoding : Module<Tensor, Tensor>
{
    #region Fields

    private readonly Tensor _positionalEncoding;
    private readonly Dropout? _dropout;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates positional encoding module.
    /// </summary>
    /// <param name="embeddingDim">Dimension of embeddings.</param>
    /// <param name="maxSequenceLength">Maximum sequence length to support.</param>
    /// <param name="dropoutRate">Dropout rate (0.0 = no dropout).</param>
    public PositionalEncoding(long embeddingDim, int maxSequenceLength, double dropoutRate = 0.1)
        : base("PositionalEncoding")
    {
        // Create positional encoding matrix
        var position = arange(0, maxSequenceLength).unsqueeze(1);
        var divTerm = exp(arange(0, embeddingDim, 2) * (-Math.Log(10000.0) / embeddingDim));

        var pe = zeros(maxSequenceLength, embeddingDim);
        pe[TensorIndex.Colon, TensorIndex.Slice(0, null, 2)] = sin(position * divTerm);
        pe[TensorIndex.Colon, TensorIndex.Slice(1, null, 2)] = cos(position * divTerm);

        _positionalEncoding = pe.unsqueeze(0);  // Add batch dimension
        register_buffer("pe", _positionalEncoding);

        _dropout = dropoutRate > 0 ? Dropout(dropoutRate) : null;

        RegisterComponents();
    }

    #endregion

    #region Methods

    public override Tensor forward(Tensor x)
    {
        // x shape: (batch_size, seq_len, embedding_dim)
        var seqLen = x.shape[1];
        var pe = _positionalEncoding[TensorIndex.Colon, TensorIndex.Slice(0, seqLen), TensorIndex.Colon];

        x = x.add(pe);

        if (_dropout is not null)
        {
            x = _dropout.forward(x);
        }

        return x;
    }

    #endregion
}
