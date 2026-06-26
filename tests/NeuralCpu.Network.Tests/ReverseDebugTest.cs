using Microsoft.Extensions.Logging;
using NeuralCpu.Core;
using NeuralCpu.Core.Components;
using NeuralCpu.Core.Configuration;
using NeuralCpu.Core.Events;

namespace NeuralCpu.Network.Tests;

/// <summary>
/// Debug Reverse task execution
/// </summary>
public sealed class ReverseDebugTest : IDisposable
{
    private readonly IEventBus _eventBus;
    private readonly Clock _clock;
    private readonly RegisterFile _registerFile;
    private readonly Alu _alu;
    private readonly Stack _stack;
    private readonly Memory _memory;
    private readonly CycleMockNetwork _mockNetwork;
    private readonly StateVectorBuilder _stateVectorBuilder;
    private readonly TaskRunner _taskRunner;

    public ReverseDebugTest()
    {
        var loggerFactory = LoggerFactory.Create(builder =>
            builder.AddConsole().SetMinimumLevel(LogLevel.Trace));

        var options = new TaskRunnerOptions
        {
            MaxCyclesPerEpisode = 100,
            InputBufferAddress = 0x00000000,
            InputLengthAddress = 0x00001000,
            OutputBufferAddress = 0x00002000,
            OutputLengthAddress = 0x00003000
        };

        var stateVectorOptions = new StateVectorOptions
        {
            EventHistorySize = 64,
            StackSnapshotSize = 8,
            MemorySnapshotSize = 16
        };

        _eventBus = new EventBus();
        _clock = new Clock(_eventBus, loggerFactory.CreateLogger<Clock>());
        _registerFile = new RegisterFile(_eventBus, loggerFactory.CreateLogger<RegisterFile>());
        _alu = new Alu(_eventBus, _registerFile, loggerFactory.CreateLogger<Alu>());
        _stack = new Stack(_eventBus, _registerFile, loggerFactory.CreateLogger<Stack>());
        _memory = new Memory(_eventBus, _registerFile, loggerFactory.CreateLogger<Memory>());

        _mockNetwork = new CycleMockNetwork(loggerFactory.CreateLogger<CycleMockNetwork>(), options);
        _stateVectorBuilder = new StateVectorBuilder(_registerFile, _stack, _memory, stateVectorOptions, options);
        _taskRunner = new TaskRunner(_eventBus, _clock, _registerFile, _stack, _memory, _mockNetwork, _stateVectorBuilder, options, loggerFactory.CreateLogger<TaskRunner>());
    }

    [Fact]
    public void Debug_Reverse_With_Two_Items()
    {
        // Arrange
        _mockNetwork.CurrentTask = MockNetworkTask.Reverse;
        var task = new TaskDefinition
        {
            Name = "Reverse (Debug)",
            Input = new ulong[] { 10, 20 },
            ExpectedOutput = new ulong[] { 20, 10 }
        };

        // Act
        var result = _taskRunner.RunEpisode(task);

        // Assert
        Assert.True(result.Success,
            $"Failed. Output: [{string.Join(", ", result.Output)}], Cycles: {result.CycleCount}, Halted: {result.Halted}");
    }

    public void Dispose()
    {
        _clock.Dispose();
        _registerFile.Dispose();
        _alu.Dispose();
        _stack.Dispose();
        _memory.Dispose();
    }
}
