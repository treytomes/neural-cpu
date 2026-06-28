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
        StateVectorBuilder stateVectorBuilder,
        TaskInputDevice inputDevice,
        TaskOutputDevice outputDevice,
        DeviceRegistry deviceRegistry,
        ILoggerFactory loggerFactory,
        int episodesPerTask = 100)
    {
        var allDemonstrations = new Dictionary<string, List<Demonstration>>();

        var taskLogger = loggerFactory.CreateLogger<BasicMotorSkillsTask>();
        var task = new BasicMotorSkillsTask(
            eventBus, clock, registerFile, stack, memory,
            expertController, stateVectorBuilder,
            taskLogger,
            maxCyclesPerEpisode: 50,
            inputDevice: inputDevice,
            outputDevice: outputDevice);

        var taskNames = new[] { "RegisterActivation", "StackGrowth", "ALUActivation", "InputRead", "OutputWrite" };

        foreach (var taskName in taskNames)
        {
            _logger.LogInformation("Collecting demonstrations for {TaskName}...", taskName);

            var demos = CollectForTask(task, expertController, taskName, episodesPerTask);
            allDemonstrations[taskName] = demos;

            _logger.LogInformation("Collected {Count} demonstrations for {TaskName}",
                demos.Count, taskName);
        }

        return allDemonstrations;
    }

    private List<Demonstration> CollectForTask(
        BasicMotorSkillsTask task,
        CycleMockNetwork controller,
        string taskName,
        int numEpisodes)
    {
        var demonstrations = new List<Demonstration>();
        int successCount = 0;
        int maxAttempts = numEpisodes * 2;

        for (int ep = 0; ep < maxAttempts && successCount < numEpisodes; ep++)
        {
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
                // Extract trajectory from controller's episode buffer
                var trajectory = controller.GetEpisodeBuffer();

                foreach (var step in trajectory)
                {
                    // step is (Tensor state, Tensor action, double reward)
                    // We need to extract the encoded event from the action tensor
                    var encodedEvent = controller.DecodeEncodedEvent(step.action);

                    // Build state vector from the state tensor
                    // For now, we'll use a simplified approach - store the raw state
                    var demo = new Demonstration(step.state, encodedEvent);
                    demonstrations.Add(demo);
                }

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
    /// Saves demonstrations to disk.
    /// </summary>
    public void SaveToDisk(Dictionary<string, List<Demonstration>> demonstrations, string directory)
    {
        Directory.CreateDirectory(directory);

        foreach (var (taskName, demos) in demonstrations)
        {
            var filePath = Path.Combine(directory, $"demonstrations_{taskName}.json");

            // Serialize demonstrations (note: Tensor serialization may need special handling)
            var serializableDemo = demos.Select(d => new
            {
                Action = d.Action,
                StateSize = d.State.shape.Length // Just store metadata for now
            }).ToList();

            var json = JsonSerializer.Serialize(serializableDemo, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(filePath, json);

            _logger.LogInformation("Saved {Count} demonstrations for {Task} to {Path}",
                demos.Count, taskName, filePath);
        }
    }
}

/// <summary>
/// Represents a demonstration: (state tensor, action) pair.
/// Note: State is a Tensor, not CpuStateVector, for simplicity.
/// </summary>
public record Demonstration(
    TorchSharp.torch.Tensor State,
    EncodedEvent Action);
