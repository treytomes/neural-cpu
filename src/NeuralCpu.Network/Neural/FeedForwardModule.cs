using TorchSharp;
using TorchSharp.Modules;
using static TorchSharp.torch;
using static TorchSharp.torch.nn;

namespace NeuralCpu.Network.Neural;

/// <summary>
/// Feed-forward neural network module with optional layer normalization and dropout.
/// Forms the building blocks of expert vertical stacks.
/// </summary>
public sealed class FeedForwardModule : FreezableModule
{
    #region Fields

    private readonly Linear _linear1;
    private readonly Linear _linear2;
    private readonly LayerNorm? _layerNorm;
    private readonly Dropout? _dropout;
    private readonly bool _useReLU;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a feed-forward module.
    /// </summary>
    /// <param name="name">Module name for debugging.</param>
    /// <param name="inputDim">Input dimension.</param>
    /// <param name="hiddenDim">Hidden layer dimension.</param>
    /// <param name="outputDim">Output dimension.</param>
    /// <param name="useLayerNorm">Whether to apply layer normalization after first linear layer.</param>
    /// <param name="dropoutRate">Dropout rate (0.0 = no dropout).</param>
    /// <param name="useReLU">Whether to use ReLU activation (false = GELU).</param>
    public FeedForwardModule(
        string name,
        long inputDim,
        long hiddenDim,
        long outputDim,
        bool useLayerNorm = true,
        double dropoutRate = 0.1,
        bool useReLU = false)
        : base(name)
    {
        _linear1 = Linear(inputDim, hiddenDim);
        _linear2 = Linear(hiddenDim, outputDim);
        _layerNorm = useLayerNorm ? LayerNorm(hiddenDim) : null;
        _dropout = dropoutRate > 0 ? Dropout(dropoutRate) : null;
        _useReLU = useReLU;

        RegisterComponents();
    }

    #endregion

    #region Methods

    public override Tensor forward(Tensor input)
    {
        // First linear transformation
        var x = _linear1.forward(input);

        // Optional layer normalization
        if (_layerNorm is not null)
        {
            x = _layerNorm.forward(x);
        }

        // Activation function (GELU or ReLU)
        x = _useReLU ? functional.relu(x) : functional.gelu(x);

        // Optional dropout
        if (_dropout is not null)
        {
            x = _dropout.forward(x);
        }

        // Second linear transformation
        x = _linear2.forward(x);

        return x;
    }

    #endregion
}
