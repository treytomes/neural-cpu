using TorchSharp;
using static TorchSharp.torch;
using static TorchSharp.torch.nn;

namespace NeuralCpu.Network.Neural;

/// <summary>
/// Base class for neural network modules that support freezing/unfreezing of weights.
/// When frozen, gradients are not computed for this module's parameters.
/// </summary>
public abstract class FreezableModule : Module<Tensor, Tensor>
{
    #region Fields

    private bool _isFrozen;

    #endregion

    #region Constructors

    protected FreezableModule(string name) : base(name)
    {
    }

    #endregion

    #region Properties

    /// <summary>
    /// Gets whether this module's weights are currently frozen.
    /// </summary>
    public bool IsFrozen => _isFrozen;

    #endregion

    #region Methods

    /// <summary>
    /// Freezes this module's parameters (disables gradient computation).
    /// Use this when you want to lock learned features and train deeper layers.
    /// </summary>
    public void Freeze()
    {
        if (_isFrozen)
            return;

        foreach (var param in parameters())
        {
            param.requires_grad = false;
        }

        _isFrozen = true;
    }

    /// <summary>
    /// Unfreezes this module's parameters (enables gradient computation).
    /// </summary>
    public void Unfreeze()
    {
        if (!_isFrozen)
            return;

        foreach (var param in parameters())
        {
            param.requires_grad = true;
        }

        _isFrozen = false;
    }

    #endregion
}
