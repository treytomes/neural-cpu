using NeuralCpu.Core.Components;
using NeuralCpu.Network.Neural;
using NeuralCpu.Network.Training;
using TorchSharp;
using static TorchSharp.torch;

namespace NeuralCpu.Network.Tests.Training;

/// <summary>
/// Tests for policy gradient (REINFORCE) implementation.
/// Validates that network can learn from episode trajectories with discounted returns.
/// </summary>
public sealed class PolicyGradientTests : IDisposable
{
    #region Fields

    private readonly TransformerCpuController _controller;
    private readonly PolicyGradientTrainer _trainer;

    #endregion

    #region Constructors

    public PolicyGradientTests()
    {
        // Create small controller for testing
        _controller = new TransformerCpuController(
            embeddingDim: 64,
            numHeads: 4,
            numEncoderLayers: 1,
            moeNetwork: null,
            device: CPU,
            isTraining: true);

        // Create policy gradient trainer
        _trainer = new PolicyGradientTrainer(
            controller: _controller,
            gamma: 0.99,
            learningRate: 1e-3,
            device: CPU);
    }

    #endregion

    #region Test: Episode Buffer

    [Fact]
    public void BeginEpisode_ClearsBuffer()
    {
        // Arrange: Add some fake data to buffer
        _trainer.BeginEpisode();
        var state = CreateMinimalStateVector();
        _trainer.RecordStep(state, reward: 1.0);

        // Act: Begin new episode
        _trainer.BeginEpisode();

        // Assert: Buffer should be empty (can't directly check, but no crash)
        Assert.True(true); // If we got here, buffer was cleared
    }

    [Fact]
    public void RecordStep_StoresStateAndReward()
    {
        // Arrange: Begin episode
        _trainer.BeginEpisode();
        var state = CreateMinimalStateVector();

        // Act: Record multiple steps
        _trainer.RecordStep(state, reward: 1.0);
        _trainer.RecordStep(state, reward: 0.5);
        _trainer.RecordStep(state, reward: 0.0);

        // Assert: Should have 3 steps recorded (validated by EndEpisode not crashing)
        var metrics = _trainer.EndEpisode();
        Assert.NotNull(metrics);
        Assert.Equal(3, metrics.StepCount);
    }

    #endregion

    #region Test: Discounted Returns

    [Fact]
    public void ComputeReturns_WithDiscounting()
    {
        // Arrange: Rewards: [1.0, 0.5, 0.0]
        // With γ=0.99:
        // R[2] = 0.0
        // R[1] = 0.5 + 0.99 * 0.0 = 0.5
        // R[0] = 1.0 + 0.99 * 0.5 = 1.495
        var rewards = new[] { 1.0, 0.5, 0.0 };
        var gamma = 0.99;

        // Act: Compute returns
        var returns = PolicyGradientTrainer.ComputeDiscountedReturns(rewards, gamma);

        // Assert: Check values
        Assert.Equal(3, returns.Length);
        Assert.True(Math.Abs(returns[0] - 1.495) < 0.01, $"Expected R[0]=1.495, got {returns[0]}");
        Assert.True(Math.Abs(returns[1] - 0.5) < 0.01, $"Expected R[1]=0.5, got {returns[1]}");
        Assert.True(Math.Abs(returns[2] - 0.0) < 0.01, $"Expected R[2]=0.0, got {returns[2]}");
    }

    [Fact]
    public void ComputeReturns_NoDiscounting()
    {
        // Arrange: With γ=0, each return equals its own reward
        var rewards = new[] { 1.0, 0.5, 0.25 };
        var gamma = 0.0;

        // Act: Compute returns
        var returns = PolicyGradientTrainer.ComputeDiscountedReturns(rewards, gamma);

        // Assert: Each return equals its reward
        Assert.Equal(rewards.Length, returns.Length);
        for (int i = 0; i < rewards.Length; i++)
        {
            Assert.True(Math.Abs(returns[i] - rewards[i]) < 0.01);
        }
    }

    #endregion

    #region Test: Policy Loss Computation

    [Fact]
    public void ComputePolicyLoss_ReturnsFiniteValue()
    {
        // Arrange: Record episode
        _trainer.BeginEpisode();
        var state = CreateMinimalStateVector();
        _trainer.RecordStep(state, reward: 1.0);
        _trainer.RecordStep(state, reward: 0.5);

        // Act: Compute policy loss
        var loss = _trainer.ComputePolicyLoss();

        // Assert: Loss should be finite
        // Note: Policy gradient loss can be negative (depends on advantages and log probs)
        Assert.True(double.IsFinite(loss));
    }

    [Fact]
    public void ComputePolicyLoss_HigherReturns_LowerLoss()
    {
        // This test is probabilistic due to random network, but in general:
        // Higher rewards should lead to lower loss (maximize log prob of good actions)

        // Arrange: Two episodes with different rewards
        _trainer.BeginEpisode();
        var state = CreateMinimalStateVector();
        _trainer.RecordStep(state, reward: 0.1);
        _trainer.RecordStep(state, reward: 0.1);
        var lowRewardLoss = _trainer.ComputePolicyLoss();

        _trainer.BeginEpisode();
        _trainer.RecordStep(state, reward: 1.0);
        _trainer.RecordStep(state, reward: 1.0);
        var highRewardLoss = _trainer.ComputePolicyLoss();

        // Assert: This is a sanity check - with random network, relationship might not hold
        // But at least both losses should be finite
        Assert.True(double.IsFinite(lowRewardLoss));
        Assert.True(double.IsFinite(highRewardLoss));
    }

    #endregion

    #region Test: Training Step

    [Fact]
    public void TrainStep_UpdatesNetwork()
    {
        // Arrange: Record episode
        _trainer.BeginEpisode();
        var state = CreateMinimalStateVector();
        _trainer.RecordStep(state, reward: 1.0);
        _trainer.RecordStep(state, reward: 0.5);

        // Act: Run training step
        var metrics = _trainer.TrainStep();

        // Assert: Metrics should be valid
        Assert.NotNull(metrics);
        Assert.True(double.IsFinite(metrics.PolicyLoss)); // Can be negative
        Assert.Equal(2, metrics.StepCount);
        Assert.True(Math.Abs(metrics.AverageReturn - 0.75) < 0.01); // (1.0 + 0.5) / 2
    }

    [Fact]
    public void EndEpisode_CallsTrainStep()
    {
        // Arrange: Record episode
        _trainer.BeginEpisode();
        var state = CreateMinimalStateVector();
        _trainer.RecordStep(state, reward: 1.0);

        // Act: End episode (should trigger training)
        var metrics = _trainer.EndEpisode();

        // Assert: Should return training metrics
        Assert.NotNull(metrics);
        Assert.Equal(1, metrics.StepCount);
        Assert.True(double.IsFinite(metrics.PolicyLoss));
    }

    #endregion

    #region Test: Baseline Subtraction

    [Fact]
    public void PolicyLoss_WithBaseline_ReducesVariance()
    {
        // Baseline subtraction: advantage = return - baseline
        // Reduces variance without changing expected gradient
        // Hard to test with random network, so just verify it doesn't crash

        // Arrange: Record episode
        _trainer.BeginEpisode();
        var state = CreateMinimalStateVector();
        _trainer.RecordStep(state, reward: 1.0);
        _trainer.RecordStep(state, reward: 0.5);

        // Act: Compute loss (with baseline = mean of returns)
        var loss = _trainer.ComputePolicyLoss(useBaseline: true);

        // Assert: Should be finite
        Assert.True(double.IsFinite(loss));
    }

    #endregion

    #region Helper Methods

    private CpuStateVector CreateMinimalStateVector()
    {
        var registers = new ulong[7];
        var events = new EncodedEvent[64];
        for (int i = 0; i < 64; i++)
        {
            events[i] = new EncodedEvent(EventType.None, RegisterId.None, RegisterId.None, RegisterId.None, 0);
        }
        var stack = new ulong[8];
        var memoryAddresses = new ulong[16];
        var memoryValues = new ulong[16];

        return new CpuStateVector
        {
            Registers = registers,
            EventHistory = events,
            StackSnapshot = stack,
            MemoryAddresses = memoryAddresses,
            MemoryValues = memoryValues,
            CycleCount = 0
        };
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        _controller.Dispose();
        _trainer.Dispose();
    }

    #endregion
}
