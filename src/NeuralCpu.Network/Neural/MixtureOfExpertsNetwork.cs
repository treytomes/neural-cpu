using TorchSharp;
using TorchSharp.Modules;
using static TorchSharp.torch;
using static TorchSharp.torch.nn;

namespace NeuralCpu.Network.Neural;

/// <summary>
/// Mixture of Experts neural network for CPU control.
/// Architecture: Input → Router → Experts (vertical module stacks) → Output
/// Supports selective freezing for progressive training.
/// </summary>
public sealed class MixtureOfExpertsNetwork : Module<Tensor, Tensor>
{
    #region Fields

    private readonly Router _router;
    private readonly ModuleList<Expert> _experts;
    private readonly Linear _inputProjection;
    private readonly Linear _outputProjection;
    private readonly long _inputDim;
    private readonly long _hiddenDim;
    private readonly long _outputDim;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a Mixture of Experts network.
    /// </summary>
    /// <param name="inputDim">Input dimension (state vector size).</param>
    /// <param name="hiddenDim">Hidden dimension for expert processing.</param>
    /// <param name="outputDim">Output dimension (control signal size).</param>
    /// <param name="experts">Array of expert networks (one per domain).</param>
    /// <param name="topK">Number of experts to activate per input.</param>
    public MixtureOfExpertsNetwork(
        long inputDim,
        long hiddenDim,
        long outputDim,
        Expert[] experts,
        int topK = 2)
        : base("MixtureOfExperts")
    {
        _inputDim = inputDim;
        _hiddenDim = hiddenDim;
        _outputDim = outputDim;

        // Input projection to hidden dimension
        _inputProjection = Linear(inputDim, hiddenDim);

        // Router selects top-K experts
        _router = new Router(hiddenDim, experts.Length, topK);

        // Expert list
        _experts = ModuleList(experts);

        // Output projection from combined expert outputs
        _outputProjection = Linear(hiddenDim, outputDim);

        RegisterComponents();
    }

    #endregion

    #region Properties

    /// <summary>
    /// Gets the number of experts in this network.
    /// </summary>
    public int NumExperts => _experts.Count;

    /// <summary>
    /// Gets the expert domains.
    /// </summary>
    public string[] ExpertDomains => _experts.Select(e => e.Domain).ToArray();

    #endregion

    #region Methods

    /// <summary>
    /// Gets an expert by index.
    /// </summary>
    public Expert GetExpert(int index) => _experts[index];

    /// <summary>
    /// Gets an expert by domain name.
    /// </summary>
    public Expert? GetExpertByDomain(string domain)
    {
        return _experts.FirstOrDefault(e => e.Domain.Equals(domain, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Freezes the bottom N layers across all experts.
    /// Use this to lock vocabulary layers and train semantic/strategy layers.
    /// </summary>
    public void FreezeBottomLayers(int count)
    {
        foreach (var expert in _experts)
        {
            expert.FreezeBottomLayers(count);
        }
    }

    /// <summary>
    /// Saves the network weights to a file.
    /// </summary>
    public void Save(string filePath)
    {
        this.save(filePath);
    }

    /// <summary>
    /// Loads network weights from a file.
    /// </summary>
    public void Load(string filePath)
    {
        this.load(filePath);
    }

    public override Tensor forward(Tensor input)
    {
        // Project input to hidden dimension
        var hidden = _inputProjection.forward(input);

        // Router determines which experts to activate
        var (routerWeights, _, expertIndices) = _router.forward(hidden);

        // Process input through each expert and weight by router
        var batchSize = input.shape[0];
        var expertOutputs = zeros(batchSize, _hiddenDim, device: input.device);

        for (int i = 0; i < _experts.Count; i++)
        {
            // Get expert output
            var expertOut = _experts[i].forward(hidden);

            // Weight by router decision (batch_size, 1)
            var weight = routerWeights[TensorIndex.Ellipsis, i].unsqueeze(-1);

            // Accumulate weighted expert output
            expertOutputs = expertOutputs.add(expertOut.mul(weight));
        }

        // Project combined expert outputs to output dimension
        var output = _outputProjection.forward(expertOutputs);

        return output;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _router.Dispose();
            _experts.Dispose();
            _inputProjection.Dispose();
            _outputProjection.Dispose();
        }
        base.Dispose(disposing);
    }

    #endregion
}
