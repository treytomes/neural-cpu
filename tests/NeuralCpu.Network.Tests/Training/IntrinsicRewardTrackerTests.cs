using NeuralCpu.Network.Training;

namespace NeuralCpu.Network.Tests.Training;

/// <summary>
/// Unit tests for IntrinsicRewardTracker.
/// Tests reward computation, diminishing returns, and exploration tracking.
/// </summary>
public sealed class IntrinsicRewardTrackerTests
{
    [Fact]
    public void ComputeReward_FirstDiscovery_IncludesNewEventTypeBonus()
    {
        // Arrange
        var tracker = new IntrinsicRewardTracker(
            registerChangeReward: 0.1,
            newEventTypeReward: 0.5);

        // Act: First RegisterWrite
        var reward = tracker.ComputeReward(EventType.RegisterWrite);

        // Assert: Should get category reward (0.1) + discovery bonus (0.5)
        Assert.Equal(0.6, reward, precision: 3);
    }

    [Fact]
    public void ComputeReward_SecondSameEvent_NoDiscoveryBonus()
    {
        // Arrange
        var tracker = new IntrinsicRewardTracker(
            registerChangeReward: 0.1,
            newEventTypeReward: 0.5);

        // Act: First and second RegisterWrite
        var firstReward = tracker.ComputeReward(EventType.RegisterWrite);
        var secondReward = tracker.ComputeReward(EventType.RegisterWrite);

        // Assert: Second call should only get category reward
        Assert.Equal(0.6, firstReward, precision: 3);
        Assert.Equal(0.1, secondReward, precision: 3);
    }

    [Fact]
    public void ComputeReward_DifferentCategories_DifferentRewards()
    {
        // Arrange
        var tracker = new IntrinsicRewardTracker(
            registerChangeReward: 0.1,
            stackOperationReward: 0.2,
            memoryWriteReward: 0.3,
            deviceAccessReward: 0.4,
            newEventTypeReward: 0.0); // Disable discovery bonus for clarity

        // Act
        var registerReward = tracker.ComputeReward(EventType.RegisterWrite);
        var stackReward = tracker.ComputeReward(EventType.Push);
        var memoryReward = tracker.ComputeReward(EventType.StoreDirect);
        var deviceReward = tracker.ComputeReward(EventType.InputRead);

        // Assert: Each category has distinct reward
        Assert.Equal(0.1, registerReward, precision: 3);
        Assert.Equal(0.2, stackReward, precision: 3);
        Assert.Equal(0.3, memoryReward, precision: 3);
        Assert.Equal(0.4, deviceReward, precision: 3);
    }

    [Fact]
    public void ComputeReward_DiminishingReturns_AfterThreshold()
    {
        // Arrange
        var tracker = new IntrinsicRewardTracker(
            registerChangeReward: 0.1,
            newEventTypeReward: 0.0,
            diminishingThreshold: 5);

        // Act: Call RegisterWrite 10 times
        var rewards = new List<double>();
        for (int i = 0; i < 10; i++)
        {
            rewards.Add(tracker.ComputeReward(EventType.RegisterWrite));
        }

        // Assert: First 5 should be constant, then diminishing
        Assert.All(rewards.Take(5), r => Assert.Equal(0.1, r, precision: 3));

        // After threshold, rewards should decrease
        Assert.True(rewards[6] < rewards[5]);
        Assert.True(rewards[9] < rewards[6]);
    }

    [Fact]
    public void ComputeReward_AluOperations_TreatedAsRegisterChanges()
    {
        // Arrange
        var tracker = new IntrinsicRewardTracker(
            registerChangeReward: 0.1,
            newEventTypeReward: 0.0);

        // Act: Test various ALU operations
        var addReward = tracker.ComputeReward(EventType.AluAdd);
        var notReward = tracker.ComputeReward(EventType.AluNot);
        var clzReward = tracker.ComputeReward(EventType.AluClz);

        // Assert: All ALU ops should get register change reward
        Assert.Equal(0.1, addReward, precision: 3);
        Assert.Equal(0.1, notReward, precision: 3);
        Assert.Equal(0.1, clzReward, precision: 3);
    }

    [Fact]
    public void GetStats_InitialState_ZeroDiscovery()
    {
        // Arrange
        var tracker = new IntrinsicRewardTracker();

        // Act
        var stats = tracker.GetStats();

        // Assert
        Assert.Equal(0, stats.DiscoveredEventTypes);
        Assert.Equal(35, stats.TotalEventTypeOptions); // 35 actionable event types
        Assert.Equal(0.0, stats.Coverage);
        Assert.Equal(0, stats.TotalActions);
    }

    [Fact]
    public void GetStats_AfterExploration_TracksCoverage()
    {
        // Arrange
        var tracker = new IntrinsicRewardTracker();

        // Act: Discover 5 different event types
        tracker.ComputeReward(EventType.RegisterWrite);
        tracker.ComputeReward(EventType.Push);
        tracker.ComputeReward(EventType.StoreDirect);
        tracker.ComputeReward(EventType.InputRead);
        tracker.ComputeReward(EventType.AluAdd);
        tracker.ComputeReward(EventType.RegisterWrite); // Duplicate

        var stats = tracker.GetStats();

        // Assert
        Assert.Equal(5, stats.DiscoveredEventTypes);
        Assert.Equal(35, stats.TotalEventTypeOptions);
        Assert.Equal(5.0 / 35.0, stats.Coverage, precision: 3);
        Assert.Equal(6, stats.TotalActions); // 6 total actions (including duplicate)
    }

    [Fact]
    public void GetStats_MostUsedEventType_TracksPopular()
    {
        // Arrange
        var tracker = new IntrinsicRewardTracker();

        // Act: Use RegisterWrite 5 times, Push 2 times
        for (int i = 0; i < 5; i++)
            tracker.ComputeReward(EventType.RegisterWrite);
        for (int i = 0; i < 2; i++)
            tracker.ComputeReward(EventType.Push);

        var stats = tracker.GetStats();

        // Assert: RegisterWrite is most used
        Assert.Equal(EventType.RegisterWrite, stats.MostUsedEventType);
    }

    [Fact]
    public void Reset_ClearsAllState()
    {
        // Arrange
        var tracker = new IntrinsicRewardTracker();
        tracker.ComputeReward(EventType.RegisterWrite);
        tracker.ComputeReward(EventType.Push);

        // Act
        tracker.Reset();

        // Assert: Stats should be reset
        var stats = tracker.GetStats();
        Assert.Equal(0, stats.DiscoveredEventTypes);
        Assert.Equal(0, stats.TotalActions);

        // Reward should include discovery bonus again
        var reward = tracker.ComputeReward(EventType.RegisterWrite);
        Assert.True(reward > 0.1); // Should include discovery bonus
    }

    [Fact]
    public void ComputeReward_ObserveOnlyEvents_NoReward()
    {
        // Arrange
        var tracker = new IntrinsicRewardTracker();

        // Act: Observe-only events (ClockRising, RegisterChanged, etc.)
        var clockReward = tracker.ComputeReward(EventType.ClockRising);
        var registerChangedReward = tracker.ComputeReward(EventType.RegisterChanged);

        // Assert: These are not actionable, should not give category rewards
        // (Only discovery bonus if newEventTypeReward > 0, but we disabled it implicitly by not passing it)
        Assert.Equal(0.5, clockReward, precision: 3); // Only discovery bonus (default 0.5)
        Assert.Equal(0.5, registerChangedReward, precision: 3);
    }
}
