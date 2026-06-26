namespace NeuralCpu.Network.Neural;

/// <summary>
/// Builder for constructing Mixture of Experts networks with common configurations.
/// </summary>
public sealed class MoENetworkBuilder
{
    #region Fields

    private long _inputDim;
    private long _hiddenDim = 256;
    private long _outputDim;
    private int _topK = 2;
    private int _expertDepth = 3;
    private bool _useLayerNorm = true;
    private double _dropoutRate = 0.1;
    private readonly List<string> _expertDomains = new();

    #endregion

    #region Methods

    /// <summary>
    /// Sets the input dimension (state vector size).
    /// </summary>
    public MoENetworkBuilder WithInputDim(long dim)
    {
        _inputDim = dim;
        return this;
    }

    /// <summary>
    /// Sets the hidden dimension for expert processing.
    /// </summary>
    public MoENetworkBuilder WithHiddenDim(long dim)
    {
        _hiddenDim = dim;
        return this;
    }

    /// <summary>
    /// Sets the output dimension (control signal size).
    /// </summary>
    public MoENetworkBuilder WithOutputDim(long dim)
    {
        _outputDim = dim;
        return this;
    }

    /// <summary>
    /// Sets the number of experts to activate per input (default 2).
    /// </summary>
    public MoENetworkBuilder WithTopK(int topK)
    {
        _topK = topK;
        return this;
    }

    /// <summary>
    /// Sets the depth of expert vertical stacks (number of modules, default 3).
    /// </summary>
    public MoENetworkBuilder WithExpertDepth(int depth)
    {
        _expertDepth = depth;
        return this;
    }

    /// <summary>
    /// Enables or disables layer normalization (default enabled).
    /// </summary>
    public MoENetworkBuilder WithLayerNorm(bool enabled)
    {
        _useLayerNorm = enabled;
        return this;
    }

    /// <summary>
    /// Sets the dropout rate (default 0.1).
    /// </summary>
    public MoENetworkBuilder WithDropout(double rate)
    {
        _dropoutRate = rate;
        return this;
    }

    /// <summary>
    /// Adds an expert for a specific domain.
    /// </summary>
    public MoENetworkBuilder AddExpert(string domain)
    {
        _expertDomains.Add(domain);
        return this;
    }

    /// <summary>
    /// Adds standard experts: ALU, Memory, Stack, Control.
    /// </summary>
    public MoENetworkBuilder WithStandardExperts()
    {
        _expertDomains.Clear();
        _expertDomains.AddRange(new[] { "ALU", "Memory", "Stack", "Control" });
        return this;
    }

    /// <summary>
    /// Builds the Mixture of Experts network.
    /// </summary>
    public MixtureOfExpertsNetwork Build()
    {
        if (_inputDim <= 0)
            throw new InvalidOperationException("Input dimension must be set");
        if (_outputDim <= 0)
            throw new InvalidOperationException("Output dimension must be set");
        if (_expertDomains.Count == 0)
            throw new InvalidOperationException("At least one expert must be added");

        // Create experts
        var experts = new Expert[_expertDomains.Count];
        for (int i = 0; i < _expertDomains.Count; i++)
        {
            experts[i] = CreateExpert(_expertDomains[i]);
        }

        return new MixtureOfExpertsNetwork(
            _inputDim,
            _hiddenDim,
            _outputDim,
            experts,
            _topK);
    }

    private Expert CreateExpert(string domain)
    {
        // Create vertical stack of modules
        var modules = new FreezableModule[_expertDepth];

        for (int depth = 0; depth < _expertDepth; depth++)
        {
            // Each layer in the stack
            var layerName = $"{domain}_Layer{depth}";

            modules[depth] = new FeedForwardModule(
                layerName,
                inputDim: _hiddenDim,
                hiddenDim: _hiddenDim * 2,  // Expand then compress
                outputDim: _hiddenDim,
                useLayerNorm: _useLayerNorm,
                dropoutRate: _dropoutRate,
                useReLU: false);  // Use GELU for smoother gradients
        }

        return new Expert(domain, modules);
    }

    #endregion
}
