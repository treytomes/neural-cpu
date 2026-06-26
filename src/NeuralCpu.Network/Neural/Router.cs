using TorchSharp;
using TorchSharp.Modules;
using static TorchSharp.torch;
using static TorchSharp.torch.nn;

namespace NeuralCpu.Network.Neural;

/// <summary>
/// Router network that decides which experts to activate for a given input.
/// Implements top-K routing with load balancing.
/// </summary>
public sealed class Router : Module<Tensor, (Tensor expertOutputs, Tensor weights, Tensor expertIndices)>
{
    #region Fields

    private readonly Linear _gatingNetwork;
    private readonly int _numExperts;
    private readonly int _topK;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a router for expert selection.
    /// </summary>
    /// <param name="inputDim">Input dimension.</param>
    /// <param name="numExperts">Total number of experts.</param>
    /// <param name="topK">Number of experts to activate per input (typically 2 for redundancy).</param>
    public Router(long inputDim, int numExperts, int topK = 2)
        : base("Router")
    {
        if (topK > numExperts)
            throw new ArgumentException($"topK ({topK}) cannot exceed numExperts ({numExperts})");

        _numExperts = numExperts;
        _topK = topK;

        // Gating network produces logits for each expert
        _gatingNetwork = Linear(inputDim, numExperts);

        RegisterComponents();
    }

    #endregion

    #region Properties

    /// <summary>
    /// Gets the number of experts this router manages.
    /// </summary>
    public int NumExperts => _numExperts;

    /// <summary>
    /// Gets the number of experts activated per input (top-K).
    /// </summary>
    public int TopK => _topK;

    #endregion

    #region Methods

    /// <summary>
    /// Routes input to top-K experts.
    /// </summary>
    /// <param name="input">Input tensor (batch_size, input_dim).</param>
    /// <returns>
    /// - expertOutputs: Weighted combination tensor (batch_size, num_experts)
    /// - weights: Softmax weights for top-K experts (batch_size, topK)
    /// - expertIndices: Indices of selected experts (batch_size, topK)
    /// </returns>
    public override (Tensor expertOutputs, Tensor weights, Tensor expertIndices) forward(Tensor input)
    {
        // Compute gating logits (batch_size, num_experts)
        var logits = _gatingNetwork.forward(input);

        // Get top-K expert indices and their logits
        var (topKLogits, topKIndices) = logits.topk(_topK, dim: -1, largest: true, sorted: true);

        // Apply softmax to top-K logits to get weights (sum to 1 across top-K)
        var weights = functional.softmax(topKLogits, dim: -1);

        // Create one-hot encoding for expert selection (batch_size, num_experts)
        var batchSize = input.shape[0];
        var expertOutputs = zeros(batchSize, _numExperts, device: input.device);

        // Scatter weights to the selected expert positions
        // This creates a sparse tensor where only top-K experts have non-zero weights
        for (int k = 0; k < _topK; k++)
        {
            var indices = topKIndices[TensorIndex.Ellipsis, k].unsqueeze(-1);
            var weightsK = weights[TensorIndex.Ellipsis, k].unsqueeze(-1);
            expertOutputs.scatter_(dim: -1, index: indices, src: weightsK);
        }

        return (expertOutputs, weights, topKIndices);
    }

    #endregion
}
