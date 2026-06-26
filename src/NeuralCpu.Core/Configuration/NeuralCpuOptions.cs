namespace NeuralCpu.Core.Configuration;

/// <summary>
/// Configuration options for the Neural CPU.
/// </summary>
public sealed class NeuralCpuOptions
{
    /// <summary>Clock configuration.</summary>
    public ClockOptions Clock { get; set; } = new();

    /// <summary>Memory configuration.</summary>
    public MemoryOptions Memory { get; set; } = new();

    /// <summary>Stack configuration.</summary>
    public StackOptions Stack { get; set; } = new();

    /// <summary>Register file configuration.</summary>
    public RegisterOptions Registers { get; set; } = new();

    /// <summary>Logging configuration.</summary>
    public LoggingOptions Logging { get; set; } = new();

    /// <summary>UI configuration.</summary>
    public UIOptions UI { get; set; } = new();
}

/// <summary>
/// Clock component configuration.
/// </summary>
public sealed class ClockOptions
{
    /// <summary>Initial frequency in Hz (default: 1).</summary>
    public int InitialFrequencyHz { get; set; } = 1;

    /// <summary>Minimum frequency in Hz (default: 1).</summary>
    public int MinFrequencyHz { get; set; } = 1;

    /// <summary>Maximum frequency in Hz (default: 100).</summary>
    public int MaxFrequencyHz { get; set; } = 100;

    /// <summary>Clock shutdown timeout in seconds (default: 2).</summary>
    public int ShutdownTimeoutSeconds { get; set; } = 2;
}

/// <summary>
/// Memory component configuration.
/// </summary>
public sealed class MemoryOptions
{
    /// <summary>Memory capacity in words (ulongs). Default: 131072 (1MB).</summary>
    public int CapacityWords { get; set; } = 131072;

    /// <summary>Highlight duration for recent writes in seconds (default: 5).</summary>
    public int HighlightDurationSeconds { get; set; } = 5;
}

/// <summary>
/// Stack component configuration.
/// </summary>
public sealed class StackOptions
{
    /// <summary>Maximum stack size in items (default: 65536).</summary>
    public int MaxSize { get; set; } = 65536;
}

/// <summary>
/// Register file configuration.
/// </summary>
public sealed class RegisterOptions
{
    /// <summary>Default register value on reset (default: 0x0000000000000000).</summary>
    public ulong DefaultValue { get; set; } = 0x0000_0000_0000_0000;
}

/// <summary>
/// Logging configuration.
/// </summary>
public sealed class LoggingOptions
{
    /// <summary>Log file path pattern (default: "logs/neural-cpu-.log").</summary>
    public string FilePath { get; set; } = "logs/neural-cpu-.log";

    /// <summary>Rolling interval in days (default: 1).</summary>
    public int RollingIntervalDays { get; set; } = 1;

    /// <summary>File size limit in MB (default: 10).</summary>
    public int FileSizeLimitMB { get; set; } = 10;

    /// <summary>Retained file count limit (default: 30).</summary>
    public int RetainedFileCountLimit { get; set; } = 30;

    /// <summary>Minimum log level (default: "Debug").</summary>
    public string MinimumLevel { get; set; } = "Debug";
}

/// <summary>
/// UI configuration.
/// </summary>
public sealed class UIOptions
{
    /// <summary>Maximum event log items (default: 100).</summary>
    public int MaxEventLogItems { get; set; } = 100;

    /// <summary>Display update interval in milliseconds (default: 100).</summary>
    public int DisplayUpdateIntervalMs { get; set; } = 100;

    /// <summary>Memory view configuration.</summary>
    public MemoryViewOptions MemoryView { get; set; } = new();
}

/// <summary>
/// Memory view display configuration.
/// </summary>
public sealed class MemoryViewOptions
{
    /// <summary>Words per row (default: 4).</summary>
    public int WordsPerRow { get; set; } = 4;

    /// <summary>Number of rows to show (default: 16).</summary>
    public int RowsToShow { get; set; } = 16;

    /// <summary>Initial viewing address (default: 0x00000000).</summary>
    public ulong InitialAddress { get; set; } = 0x00000000;
}
