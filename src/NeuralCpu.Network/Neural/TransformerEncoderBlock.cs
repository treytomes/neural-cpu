using TorchSharp;
using TorchSharp.Modules;
using static TorchSharp.torch;
using static TorchSharp.torch.nn;

namespace NeuralCpu.Network.Neural;

/// <summary>
/// Transformer encoder block: Multi-Head Attention → Add & Norm → Feed-Forward → Add & Norm.
/// Forms the core building block of transformer architectures.
/// </summary>
public sealed class TransformerEncoderBlock : FreezableModule
{
    #region Fields

    private readonly MultiHeadAttention _attention;
    private readonly FeedForwardModule _feedForward;
    private readonly LayerNorm _norm1;
    private readonly LayerNorm _norm2;
    private readonly Dropout? _dropout;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a transformer encoder block.
    /// </summary>
    /// <param name="name">Block name.</param>
    /// <param name="embeddingDim">Embedding dimension.</param>
    /// <param name="numHeads">Number of attention heads.</param>
    /// <param name="ffnHiddenDim">Feed-forward network hidden dimension.</param>
    /// <param name="dropoutRate">Dropout rate.</param>
    public TransformerEncoderBlock(
        string name,
        long embeddingDim,
        int numHeads,
        long ffnHiddenDim,
        double dropoutRate = 0.1)
        : base(name)
    {
        _attention = new MultiHeadAttention($"{name}_Attention", embeddingDim, numHeads, dropoutRate);
        _feedForward = new FeedForwardModule(
            $"{name}_FFN",
            embeddingDim,
            ffnHiddenDim,
            embeddingDim,
            useLayerNorm: false,  // We do layer norm separately
            dropoutRate: dropoutRate,
            useReLU: false);

        _norm1 = LayerNorm(embeddingDim);
        _norm2 = LayerNorm(embeddingDim);
        _dropout = dropoutRate > 0 ? Dropout(dropoutRate) : null;

        RegisterComponents();
    }

    #endregion

    #region Methods

    public override Tensor forward(Tensor input)
    {
        // Multi-head self-attention with residual connection and layer norm
        var attended = _attention.forward(input);
        if (_dropout is not null)
        {
            attended = _dropout.forward(attended);
        }
        var x = _norm1.forward(input.add(attended));  // Add & Norm

        // Feed-forward with residual connection and layer norm
        var ffnOutput = _feedForward.forward(x);
        if (_dropout is not null)
        {
            ffnOutput = _dropout.forward(ffnOutput);
        }
        x = _norm2.forward(x.add(ffnOutput));  // Add & Norm

        return x;
    }

    #endregion
}
