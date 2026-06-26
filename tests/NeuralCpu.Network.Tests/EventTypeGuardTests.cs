using NeuralCpu.Core.Events;
using System.Reflection;

namespace NeuralCpu.Network.Tests;

/// <summary>
/// Guard tests to ensure EventType enum stays synchronized with actual event types in Core.
/// These tests fail when new events are added to Core without updating the EventType enum.
///
/// NOTE: EventType enum is more granular than Core events (e.g., AluAdd, AluSub map to AluCommand).
/// This is intentional - the enum represents semantic operations, not just type names.
/// </summary>
public sealed class EventTypeGuardTests
{
    [Fact]
    public void EventType_Enum_Should_Cover_All_Core_Events()
    {
        // Arrange: Get all event record types from NeuralCpu.Core.Events namespace
        var coreEventTypes = typeof(ClockPhaseChanged).Assembly
            .GetTypes()
            .Where(t => t.Namespace == "NeuralCpu.Core.Events"
                        && !t.IsAbstract
                        && !t.IsInterface
                        && !t.IsEnum // Exclude enums (like ClockPhase)
                        && !t.IsNested // Exclude nested/compiler-generated types
                        && !t.Name.Contains('<') // Exclude compiler-generated types
                        && !IsInfrastructureType(t.Name) // Exclude EventBus and other infrastructure
                        && (t.IsClass || t.IsValueType)) // Include records and structs
            .Select(t => t.Name)
            .OrderBy(n => n)
            .ToList();

        // Get all EventType enum values (except None)
        var enumValues = Enum.GetNames<EventType>()
            .Where(n => n != "None")
            .OrderBy(n => n)
            .ToList();

        // Act: Check that each Core event has at least one corresponding enum value
        var unmappedCoreEvents = coreEventTypes
            .Where(coreEvent => !HasEnumCoverageFor(coreEvent, enumValues))
            .ToList();

        // Assert: All Core events should have enum coverage
        Assert.True(
            unmappedCoreEvents.Count == 0,
            BuildErrorMessage(unmappedCoreEvents, coreEventTypes, enumValues));
    }

    /// <summary>
    /// Check if a Core event has enum coverage.
    /// Some events (like AluCommand) map to multiple enum values (AluAdd, AluSub, etc.).
    /// </summary>
    private static bool HasEnumCoverageFor(string coreEventName, List<string> enumValues)
    {
        // Special cases: Commands that map to multiple semantic operations
        if (coreEventName.Contains("AluCommand") || coreEventName.Contains("AluUnaryCommand") || coreEventName.Contains("AluImmediateCommand"))
            return enumValues.Any(e => e.StartsWith("Alu"));

        if (coreEventName.Contains("LoadCommand") || coreEventName.Contains("LoadIndirectCommand"))
            return enumValues.Any(e => e.StartsWith("Load"));

        if (coreEventName.Contains("StoreCommand") || coreEventName.Contains("StoreIndirectCommand"))
            return enumValues.Any(e => e.StartsWith("Store"));

        // ClockPhaseChanged maps to ClockRising/ClockFalling (semantic operations, not type match)
        if (coreEventName == "ClockPhaseChanged")
            return enumValues.Contains("ClockRising") || enumValues.Contains("ClockFalling");

        // Default: look for exact match after normalization
        var normalized = NormalizeEventName(coreEventName);
        return enumValues.Any(e => NormalizeEventName(e).Equals(normalized, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsInfrastructureType(string typeName)
    {
        // Infrastructure types that should not be in EventType enum
        var infrastructureTypes = new[] { "EventBus", "IEventBus", "Subject", "Unsubscriber" };
        return infrastructureTypes.Any(t => typeName.Contains(t, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EventType_Enum_Should_Have_Unique_Values()
    {
        // Arrange: Get all EventType enum values
        var enumValues = Enum.GetValues(typeof(EventType))
            .Cast<EventType>()
            .Select(e => (byte)e)
            .ToList();

        // Act: Find duplicates
        var duplicates = enumValues
            .GroupBy(v => v)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        // Assert: Should have no duplicate values
        Assert.Empty(duplicates);
    }

    [Fact]
    public void EventType_Enum_Should_Fit_In_Byte()
    {
        // Arrange: Get max EventType value
        var maxValue = Enum.GetValues(typeof(EventType))
            .Cast<EventType>()
            .Select(e => (byte)e)
            .Max();

        // Assert: Should fit in a byte (0-255)
        Assert.True(maxValue <= 255,
            $"EventType enum value {maxValue} exceeds byte range (max 255). Consider using ushort instead of byte.");
    }

    #region Helper Methods

    /// <summary>
    /// Normalize event names to handle common naming patterns.
    /// Strips "Command", "Request", "Response" suffixes for comparison.
    /// </summary>
    private static string NormalizeEventName(string name)
    {
        // Common suffixes to strip for comparison
        var suffixes = new[] { "Command", "Request", "Response", "Event" };

        foreach (var suffix in suffixes)
        {
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return name.Substring(0, name.Length - suffix.Length);
            }
        }

        return name;
    }

    private static string BuildErrorMessage(
        List<string> unmappedCoreEvents,
        List<string> allCoreEvents,
        List<string> allEnumValues)
    {
        var message = new System.Text.StringBuilder();
        message.AppendLine("EventType enum is missing coverage for some Core events!");
        message.AppendLine();

        if (unmappedCoreEvents.Any())
        {
            message.AppendLine("Core events without EventType enum coverage:");
            foreach (var evt in unmappedCoreEvents)
            {
                message.AppendLine($"  - {evt}");
            }
            message.AppendLine();
            message.AppendLine("Action: Add enum values to EventType.cs for these event types.");
            message.AppendLine("Note: One Core event may map to multiple enum values (e.g., AluCommand → AluAdd, AluSub, ...)");
            message.AppendLine();
        }

        message.AppendLine($"Core events found: {allCoreEvents.Count}");
        message.AppendLine($"EventType enum values: {allEnumValues.Count}");

        return message.ToString();
    }

    #endregion
}
