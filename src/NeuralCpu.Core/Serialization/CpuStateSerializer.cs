using System.Text.Json;
using NeuralCpu.Core.Components;

namespace NeuralCpu.Core.Serialization;

/// <summary>
/// Serializes and deserializes CPU state to/from JSON.
/// Supports full CPU state snapshots for checkpointing and testing.
/// </summary>
public static class CpuStateSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    /// <summary>
    /// Capture current CPU state from all components.
    /// </summary>
    public static CpuState Capture(Clock clock, RegisterFile registerFile, Stack stack, Memory memory)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(registerFile);
        ArgumentNullException.ThrowIfNull(stack);
        ArgumentNullException.ThrowIfNull(memory);

        // Capture registers
        var registers = new Dictionary<RegisterId, ulong>
        {
            [RegisterId.R0] = registerFile[RegisterId.R0],
            [RegisterId.R1] = registerFile[RegisterId.R1],
            [RegisterId.R2] = registerFile[RegisterId.R2],
            [RegisterId.R3] = registerFile[RegisterId.R3],
            [RegisterId.PC] = registerFile[RegisterId.PC],
            [RegisterId.SP] = registerFile[RegisterId.SP],
            [RegisterId.Flags] = registerFile[RegisterId.Flags]
        };

        // Capture stack
        var stackSnapshot = stack.GetSnapshot();

        // Capture memory (sparse - only non-zero words)
        var memoryDict = new Dictionary<ulong, ulong>();
        for (ulong addr = 0; addr < (ulong)memory.CapacityWords; addr++)
        {
            var value = memory[addr];
            if (value != 0)
            {
                memoryDict[addr] = value;
            }
        }

        return new CpuState
        {
            CycleCount = clock.CycleCount,
            Phase = clock.CurrentPhase,
            FrequencyHz = clock.FrequencyHz,
            Registers = registers,
            Stack = stackSnapshot,
            Memory = memoryDict,
            MemoryCapacityWords = memory.CapacityWords
        };
    }

    /// <summary>
    /// Restore CPU state to all components.
    /// NOTE: This only restores data, not pending operations or subscriptions.
    /// </summary>
    public static void Restore(CpuState state, Clock clock, RegisterFile registerFile, Stack stack, Memory memory)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(registerFile);
        ArgumentNullException.ThrowIfNull(stack);
        ArgumentNullException.ThrowIfNull(memory);

        // Verify memory capacity matches
        if (memory.CapacityWords != state.MemoryCapacityWords)
        {
            throw new InvalidOperationException(
                $"Memory capacity mismatch: expected {state.MemoryCapacityWords}, got {memory.CapacityWords}");
        }

        // Restore would require internal access to component state
        // For now, this is a design sketch - actual implementation would need:
        // - Internal setters or Restore methods on each component
        // - Or rebuild components from state (preferred for immutability)
        throw new NotImplementedException(
            "Restore requires component-level APIs for setting internal state. " +
            "Consider implementing IRestorable interface on components.");
    }

    /// <summary>
    /// Serialize CPU state to JSON string.
    /// </summary>
    public static string ToJson(CpuState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return JsonSerializer.Serialize(state, JsonOptions);
    }

    /// <summary>
    /// Deserialize CPU state from JSON string.
    /// </summary>
    public static CpuState FromJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        return JsonSerializer.Deserialize<CpuState>(json, JsonOptions)
            ?? throw new InvalidOperationException("Failed to deserialize CPU state");
    }

    /// <summary>
    /// Save CPU state to a file.
    /// </summary>
    public static void SaveToFile(CpuState state, string filePath)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var json = ToJson(state);
        File.WriteAllText(filePath, json);
    }

    /// <summary>
    /// Load CPU state from a file.
    /// </summary>
    public static CpuState LoadFromFile(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"State file not found: {filePath}", filePath);
        }

        var json = File.ReadAllText(filePath);
        return FromJson(json);
    }
}
