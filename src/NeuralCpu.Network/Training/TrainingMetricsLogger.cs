using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NeuralCpu.Network.Training;

/// <summary>
/// Logs training metrics to CSV and JSON for analysis and visualization.
/// Records episode-level metrics (success, reward, loss) for plotting learning curves.
/// </summary>
public sealed class TrainingMetricsLogger : IDisposable
{
    private readonly string _outputDirectory;
    private readonly StreamWriter _csvWriter;
    private readonly List<EpisodeMetrics> _allMetrics;
    private readonly string _runId;

    public TrainingMetricsLogger(string outputDirectory, string? runId = null)
    {
        _outputDirectory = outputDirectory;
        _runId = runId ?? DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        _allMetrics = new List<EpisodeMetrics>();

        // Create output directory
        Directory.CreateDirectory(_outputDirectory);

        // Create CSV file with header
        var csvPath = Path.Combine(_outputDirectory, $"metrics_{_runId}.csv");
        _csvWriter = new StreamWriter(csvPath, append: false);
        _csvWriter.WriteLine("RunId,TaskName,Episode,Success,Reward,CycleCount,PolicyLoss,Timestamp");
    }

    /// <summary>
    /// Logs metrics for a single episode.
    /// </summary>
    public void LogEpisode(string taskName, int episode, bool success, double reward, int cycleCount, double policyLoss)
    {
        var timestamp = DateTimeOffset.UtcNow;

        var metrics = new EpisodeMetrics(
            RunId: _runId,
            TaskName: taskName,
            Episode: episode,
            Success: success,
            Reward: reward,
            CycleCount: cycleCount,
            PolicyLoss: policyLoss,
            Timestamp: timestamp);

        _allMetrics.Add(metrics);

        // Write to CSV
        _csvWriter.WriteLine($"{_runId},{taskName},{episode},{success},{reward:F4},{cycleCount},{policyLoss:F6},{timestamp:O}");
        _csvWriter.Flush(); // Ensure data is written immediately
    }

    /// <summary>
    /// Logs summary statistics for a completed task.
    /// </summary>
    public void LogTaskSummary(string taskName, int totalEpisodes, int successCount, double avgCyclesToSuccess)
    {
        var summary = new TaskSummary(
            RunId: _runId,
            TaskName: taskName,
            TotalEpisodes: totalEpisodes,
            SuccessCount: successCount,
            SuccessRate: (double)successCount / totalEpisodes,
            AvgCyclesToSuccess: avgCyclesToSuccess,
            Timestamp: DateTimeOffset.UtcNow);

        // Write task summary to separate JSON file
        var summaryPath = Path.Combine(_outputDirectory, $"summary_{_runId}_{taskName}.json");
        var json = JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(summaryPath, json);
    }

    /// <summary>
    /// Saves all metrics to JSON for post-training analysis.
    /// </summary>
    public void SaveToJson()
    {
        var jsonPath = Path.Combine(_outputDirectory, $"metrics_{_runId}.json");
        var json = JsonSerializer.Serialize(_allMetrics, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(jsonPath, json);
    }

    /// <summary>
    /// Gets all logged metrics.
    /// </summary>
    public IReadOnlyList<EpisodeMetrics> GetAllMetrics() => _allMetrics.AsReadOnly();

    /// <summary>
    /// Gets metrics for a specific task.
    /// </summary>
    public IReadOnlyList<EpisodeMetrics> GetTaskMetrics(string taskName) =>
        _allMetrics.Where(m => m.TaskName == taskName).ToList().AsReadOnly();

    public void Dispose()
    {
        _csvWriter?.Dispose();
        SaveToJson(); // Save JSON on dispose
    }
}

/// <summary>
/// Metrics for a single training episode.
/// </summary>
public record EpisodeMetrics(
    string RunId,
    string TaskName,
    int Episode,
    bool Success,
    double Reward,
    int CycleCount,
    double PolicyLoss,
    DateTimeOffset Timestamp);

/// <summary>
/// Summary statistics for a completed task.
/// </summary>
public record TaskSummary(
    string RunId,
    string TaskName,
    int TotalEpisodes,
    int SuccessCount,
    double SuccessRate,
    double AvgCyclesToSuccess,
    DateTimeOffset Timestamp);
