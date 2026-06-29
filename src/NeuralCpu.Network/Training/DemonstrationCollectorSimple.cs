using System.Text.Json;
using Microsoft.Extensions.Logging;
using NeuralCpu.Core;
using NeuralCpu.Core.Components;
using NeuralCpu.Core.Events;
using NeuralCpu.Network.Neural;

namespace NeuralCpu.Network.Training;

/// <summary>
/// Simplified demonstration collector that works with TransformerCpuController's episode buffer.
/// </summary>
public sealed class DemonstrationCollectorSimple
{
    private readonly ILogger<DemonstrationCollectorSimple> _logger;

    public DemonstrationCollectorSimple(ILogger<DemonstrationCollectorSimple> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Collects demonstrations for all 5 motor skills tasks using CycleMockNetwork.
    /// </summary>
    public Dictionary<string, List<Demonstration>> CollectAllTasks(
        IEventBus eventBus,
        Clock clock,
        RegisterFile registerFile,
        Stack stack,
        Memory memory,
        CycleMockNetwork expertController,
        TransformerCpuController encoder,
        StateVectorBuilder stateVectorBuilder,
        TaskInputDevice inputDevice,
        TaskOutputDevice outputDevice,
        DeviceRegistry deviceRegistry,
        ILoggerFactory loggerFactory,
        int episodesPerTask = 100)
    {
        var allDemonstrations = new Dictionary<string, List<Demonstration>>();

        // Wrap expert with recording wrapper
        var recordingController = new RecordingMockNetwork(expertController, encoder);

        var taskLogger = loggerFactory.CreateLogger<BasicMotorSkillsTask>();
        var task = new BasicMotorSkillsTask(
            eventBus, clock, registerFile, stack, memory,
            recordingController, stateVectorBuilder,
            taskLogger,
            maxCyclesPerEpisode: 50,
            inputDevice: inputDevice,
            outputDevice: outputDevice);

        var taskNames = new[] { "RegisterActivation", "StackGrowth", "ALUActivation", "InputRead", "OutputWrite" };

        foreach (var taskName in taskNames)
        {
            _logger.LogInformation("Collecting demonstrations for {TaskName}...", taskName);

            var demos = CollectForTask(task, recordingController, taskName, episodesPerTask);
            allDemonstrations[taskName] = demos;

            _logger.LogInformation("Collected {Count} demonstrations for {TaskName}",
                demos.Count, taskName);
        }

        return allDemonstrations;
    }

    private List<Demonstration> CollectForTask(
        BasicMotorSkillsTask task,
        RecordingMockNetwork controller,
        string taskName,
        int numEpisodes)
    {
        var demonstrations = new List<Demonstration>();
        int successCount = 0;
        int maxAttempts = numEpisodes * 2;

        for (int ep = 0; ep < maxAttempts && successCount < numEpisodes; ep++)
        {
            // Clear previous recordings
            controller.ClearRecordings();

            // Run the task
            TaskResult result = taskName switch
            {
                "RegisterActivation" => task.RunRegisterActivationTask(),
                "StackGrowth" => task.RunStackGrowthTask(),
                "ALUActivation" => task.RunAluActivationTask(),
                "InputRead" => task.RunInputReadTask(),
                "OutputWrite" => task.RunOutputWriteTask(),
                _ => throw new ArgumentException($"Unknown task: {taskName}")
            };

            if (result.Success)
            {
                // Extract demonstrations from recording controller
                var episodeDemos = controller.GetDemonstrations();
                demonstrations.AddRange(episodeDemos);

                successCount++;

                if (successCount % 20 == 0)
                {
                    _logger.LogDebug("{Task}: {Success}/{Target} episodes collected",
                        taskName, successCount, numEpisodes);
                }
            }

            controller.Reset();
        }

        if (successCount < numEpisodes)
        {
            _logger.LogWarning("Only collected {Success}/{Target} successful episodes for {Task}",
                successCount, numEpisodes, taskName);
        }

        return demonstrations;
    }

    /// <summary>
    /// Saves demonstrations to disk (metadata only - state vectors not serialized).
    /// </summary>
    public void SaveToDisk(Dictionary<string, List<Demonstration>> demonstrations, string directory)
    {
        Directory.CreateDirectory(directory);

        foreach (var (taskName, demos) in demonstrations)
        {
            var filePath = Path.Combine(directory, $"demonstrations_{taskName}.json");

            // Serialize demonstrations metadata (CpuStateVector is not serialized)
            var serializableDemo = demos.Select(d => new
            {
                Action = d.Action,
                RegisterCount = d.State.Registers.Length,
                EventCount = d.State.EventHistory.Length
            }).ToList();

            var json = JsonSerializer.Serialize(serializableDemo, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(filePath, json);

            _logger.LogInformation("Saved {Count} demonstrations metadata for {Task} to {Path}",
                demos.Count, taskName, filePath);
        }
    }
}

/// <summary>
/// Represents a demonstration: (state vector, action) pair.
/// </summary>
public record Demonstration(
    CpuStateVector State,
    EncodedEvent Action);
