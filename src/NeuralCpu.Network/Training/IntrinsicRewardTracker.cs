namespace NeuralCpu.Network.Training;

/// <summary>
/// Tracks intrinsic rewards for body discovery and exploration.
/// Encourages the network to discover new event types and observe diverse state changes.
/// Implements diminishing returns to prevent reward hacking.
/// </summary>
public sealed class IntrinsicRewardTracker
{
    #region Fields

    private readonly HashSet<EventType> _discoveredEventTypes = new();
    private readonly Dictionary<EventType, int> _eventTypeCounts = new();
    private readonly double _registerChangeReward;
    private readonly double _stackOperationReward;
    private readonly double _memoryWriteReward;
    private readonly double _deviceAccessReward;
    private readonly double _newEventTypeReward;
    private readonly int _diminishingThreshold;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates an intrinsic reward tracker with configurable reward values.
    /// </summary>
    /// <param name="registerChangeReward">Reward for each register change (default +0.1)</param>
    /// <param name="stackOperationReward">Reward for each stack operation (default +0.2)</param>
    /// <param name="memoryWriteReward">Reward for each memory write (default +0.3)</param>
    /// <param name="deviceAccessReward">Reward for each device I/O access (default +0.4)</param>
    /// <param name="newEventTypeReward">Reward for discovering new event type (default +0.5)</param>
    /// <param name="diminishingThreshold">Count threshold for diminishing returns (default 10)</param>
    public IntrinsicRewardTracker(
        double registerChangeReward = 0.1,
        double stackOperationReward = 0.2,
        double memoryWriteReward = 0.3,
        double deviceAccessReward = 0.4,
        double newEventTypeReward = 0.5,
        int diminishingThreshold = 10)
    {
        _registerChangeReward = registerChangeReward;
        _stackOperationReward = stackOperationReward;
        _memoryWriteReward = memoryWriteReward;
        _deviceAccessReward = deviceAccessReward;
        _newEventTypeReward = newEventTypeReward;
        _diminishingThreshold = diminishingThreshold;
    }

    #endregion

    #region Methods

    /// <summary>
    /// Computes intrinsic reward for an action based on its event type and novelty.
    /// Applies diminishing returns after threshold to prevent reward hacking.
    /// </summary>
    public double ComputeReward(EventType eventType)
    {
        double reward = 0.0;

        // New event type discovery (one-time bonus)
        if (_discoveredEventTypes.Add(eventType))
        {
            reward += _newEventTypeReward;
        }

        // Increment usage count
        if (!_eventTypeCounts.ContainsKey(eventType))
            _eventTypeCounts[eventType] = 0;
        _eventTypeCounts[eventType]++;

        // Get usage count for diminishing returns
        int usageCount = _eventTypeCounts[eventType];

        // Base reward by category (with diminishing returns)
        double categoryReward = eventType switch
        {
            EventType.RegisterWrite or EventType.RegisterCopy => _registerChangeReward,
            EventType.Push or EventType.PushImmediate or EventType.Pop or EventType.Peek or EventType.Dup => _stackOperationReward,
            EventType.StoreDirect or EventType.StoreIndirect => _memoryWriteReward,
            EventType.InputRead or EventType.OutputWrite => _deviceAccessReward,
            _ when IsAluOperation(eventType) => _registerChangeReward, // ALU ops change registers
            _ => 0.0
        };

        // Apply diminishing returns: reward / sqrt(usageCount) after threshold
        if (usageCount > _diminishingThreshold)
        {
            categoryReward /= Math.Sqrt(usageCount - _diminishingThreshold + 1);
        }

        reward += categoryReward;

        return reward;
    }

    /// <summary>
    /// Gets exploration statistics for monitoring progress.
    /// </summary>
    public ExplorationStats GetStats()
    {
        return new ExplorationStats(
            DiscoveredEventTypes: _discoveredEventTypes.Count,
            TotalEventTypeOptions: GetActionableEventTypeCount(),
            MostUsedEventType: _eventTypeCounts.Any()
                ? _eventTypeCounts.OrderByDescending(kv => kv.Value).First().Key
                : EventType.None,
            TotalActions: _eventTypeCounts.Values.Sum());
    }

    /// <summary>
    /// Resets tracking state for a new training session.
    /// </summary>
    public void Reset()
    {
        _discoveredEventTypes.Clear();
        _eventTypeCounts.Clear();
    }

    private static bool IsAluOperation(EventType eventType)
    {
        return eventType switch
        {
            EventType.AluAdd or EventType.AluSub or EventType.AluMul or EventType.AluDiv or
            EventType.AluAnd or EventType.AluNand or EventType.AluOr or EventType.AluNor or
            EventType.AluXor or EventType.AluXnor or EventType.AluShl or EventType.AluShr or
            EventType.AluSar or EventType.AluRol or EventType.AluRor or
            EventType.AluNot or EventType.AluInc or EventType.AluDec or
            EventType.AluNeg or EventType.AluAbs or EventType.AluClz => true,
            _ => false
        };
    }

    private static int GetActionableEventTypeCount()
    {
        // Count of actionable event types (not observe-only)
        // RegisterWrite, RegisterCopy, HardwareInterrupt,
        // 15 ALU binary, 6 ALU unary,
        // 5 Stack, 4 Memory, 2 Device I/O = 35 total
        return 35;
    }

    #endregion
}

/// <summary>
/// Statistics about exploration coverage during body discovery.
/// </summary>
public record ExplorationStats(
    int DiscoveredEventTypes,
    int TotalEventTypeOptions,
    EventType MostUsedEventType,
    int TotalActions)
{
    /// <summary>
    /// Fraction of event types discovered (0.0 to 1.0).
    /// </summary>
    public double Coverage => TotalEventTypeOptions > 0
        ? (double)DiscoveredEventTypes / TotalEventTypeOptions
        : 0.0;
}
