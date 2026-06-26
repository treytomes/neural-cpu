using Microsoft.Extensions.Logging;
using NeuralCpu.Core;
using NeuralCpu.Core.Components;
using NeuralCpu.Core.Configuration;
using NeuralCpu.Core.Events;

namespace NeuralCpu.Network.Tests;

/// <summary>
/// Integration tests for MockNetwork executing tasks end-to-end.
/// These tests validate that the CPU-network interface works correctly
/// by running complete episodes with all CPU components.
/// </summary>
public sealed class MockNetworkIntegrationTests : IDisposable
{
    #region Fields

    private readonly IEventBus _eventBus;
    private readonly Clock _clock;
    private readonly RegisterFile _registerFile;
    private readonly Alu _alu;
    private readonly Stack _stack;
    private readonly Memory _memory;
    private readonly CycleMockNetwork _mockNetwork;
    private readonly StateVectorBuilder _stateVectorBuilder;
    private readonly TaskRunner _taskRunner;
    private readonly ILogger<CycleMockNetwork> _mockNetworkLogger;
    private readonly ILogger<TaskRunner> _taskRunnerLogger;

    #endregion

    #region Constructors

    public MockNetworkIntegrationTests()
    {
        // Create logger factory
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        _mockNetworkLogger = loggerFactory.CreateLogger<CycleMockNetwork>();
        _taskRunnerLogger = loggerFactory.CreateLogger<TaskRunner>();

        // Create default options
        var options = new TaskRunnerOptions
        {
            MaxCyclesPerEpisode = 1000,
            InputBufferAddress = 0x00000000, // Start of memory
            InputLengthAddress = 0x00001000, // 4K offset
            OutputBufferAddress = 0x00002000, // 8K offset
            OutputLengthAddress = 0x00003000  // 12K offset
        };

        var stateVectorOptions = new StateVectorOptions
        {
            EventHistorySize = 64,
            StackSnapshotSize = 8,
            MemorySnapshotSize = 16
        };

        // Create CPU components
        _eventBus = new EventBus();
        _clock = new Clock(_eventBus, loggerFactory.CreateLogger<Clock>());
        _registerFile = new RegisterFile(_eventBus, loggerFactory.CreateLogger<RegisterFile>());
        _alu = new Alu(_eventBus, _registerFile, loggerFactory.CreateLogger<Alu>());
        _stack = new Stack(_eventBus, _registerFile, loggerFactory.CreateLogger<Stack>());
        _memory = new Memory(_eventBus, _registerFile, loggerFactory.CreateLogger<Memory>());

        // Create network components
        _mockNetwork = new CycleMockNetwork(_mockNetworkLogger, options);
        _stateVectorBuilder = new StateVectorBuilder(_registerFile, _stack, _memory, stateVectorOptions, options);
        _taskRunner = new TaskRunner(_eventBus, _clock, _registerFile, _stack, _memory, _mockNetwork, _stateVectorBuilder, options, _taskRunnerLogger);
    }

    #endregion

    #region Tests

    [Fact]
    public void Echo_Task_Should_Copy_Input_To_Output()
    {
        // Arrange
        _mockNetwork.CurrentTask = MockNetworkTask.Echo;
        var task = new TaskDefinition
        {
            Name = "Echo",
            Input = new ulong[] { 1, 2, 3, 4, 5 },
            ExpectedOutput = new ulong[] { 1, 2, 3, 4, 5 }
        };

        // Act
        var result = _taskRunner.RunEpisode(task);

        // Assert
        Assert.True(result.Success, $"Echo task failed. Output: [{string.Join(", ", result.Output)}]");
        Assert.Equal(1.0, result.Reward);
        Assert.True(result.Halted);
        Assert.Equal(task.ExpectedOutput, result.Output);
    }

    [Fact]
    public void Echo_Task_Should_Handle_Empty_Input()
    {
        // Arrange
        _mockNetwork.CurrentTask = MockNetworkTask.Echo;
        var task = new TaskDefinition
        {
            Name = "Echo (Empty)",
            Input = Array.Empty<ulong>(),
            ExpectedOutput = Array.Empty<ulong>()
        };

        // Act
        var result = _taskRunner.RunEpisode(task);

        // Assert
        Assert.True(result.Success, "Echo task should handle empty input");
        Assert.Equal(1.0, result.Reward);
        Assert.True(result.Halted);
        Assert.Empty(result.Output);
    }

    [Fact]
    public void Echo_Task_Should_Handle_Single_Item()
    {
        // Arrange
        _mockNetwork.CurrentTask = MockNetworkTask.Echo;
        var task = new TaskDefinition
        {
            Name = "Echo (Single)",
            Input = new ulong[] { 42 },
            ExpectedOutput = new ulong[] { 42 }
        };

        // Act
        var result = _taskRunner.RunEpisode(task);

        // Assert
        Assert.True(result.Success, "Echo task should handle single item");
        Assert.Equal(1.0, result.Reward);
        Assert.True(result.Halted);
        Assert.Equal(new ulong[] { 42 }, result.Output);
    }

    [Fact]
    public void Echo_Task_Should_Handle_Large_Input()
    {
        // Arrange
        _mockNetwork.CurrentTask = MockNetworkTask.Echo;
        var input = Enumerable.Range(0, 100).Select(i => (ulong)i).ToArray();
        var task = new TaskDefinition
        {
            Name = "Echo (Large)",
            Input = input,
            ExpectedOutput = input
        };

        // Act
        var result = _taskRunner.RunEpisode(task);

        // Assert
        Assert.True(result.Success, $"Echo task failed with large input. Output length: {result.Output.Length}");
        Assert.Equal(1.0, result.Reward);
        Assert.True(result.Halted);
        Assert.Equal(input, result.Output);
    }

    [Fact]
    public void Increment_Task_Should_Add_One_To_Each_Value()
    {
        // Arrange
        _mockNetwork.CurrentTask = MockNetworkTask.Increment;
        var task = new TaskDefinition
        {
            Name = "Increment",
            Input = new ulong[] { 10, 20, 30, 40, 50 },
            ExpectedOutput = new ulong[] { 11, 21, 31, 41, 51 }
        };

        // Act
        var result = _taskRunner.RunEpisode(task);

        // Assert
        Assert.True(result.Success, $"Increment task failed. Output: [{string.Join(", ", result.Output)}]");
        Assert.Equal(1.0, result.Reward);
        Assert.True(result.Halted);
        Assert.Equal(task.ExpectedOutput, result.Output);
    }

    [Fact]
    public void Increment_Task_Should_Handle_Zero()
    {
        // Arrange
        _mockNetwork.CurrentTask = MockNetworkTask.Increment;
        var task = new TaskDefinition
        {
            Name = "Increment (Zero)",
            Input = new ulong[] { 0, 0, 0 },
            ExpectedOutput = new ulong[] { 1, 1, 1 }
        };

        // Act
        var result = _taskRunner.RunEpisode(task);

        // Assert
        Assert.True(result.Success, "Increment task should handle zeros");
        Assert.Equal(1.0, result.Reward);
        Assert.True(result.Halted);
        Assert.Equal(task.ExpectedOutput, result.Output);
    }

    [Fact]
    public void Increment_Task_Should_Handle_Max_Value()
    {
        // Arrange
        _mockNetwork.CurrentTask = MockNetworkTask.Increment;
        var task = new TaskDefinition
        {
            Name = "Increment (Max)",
            Input = new ulong[] { ulong.MaxValue - 1, ulong.MaxValue },
            ExpectedOutput = new ulong[] { ulong.MaxValue, 0 } // Wraps around
        };

        // Act
        var result = _taskRunner.RunEpisode(task);

        // Assert
        Assert.True(result.Success, "Increment task should handle overflow (wrap)");
        Assert.Equal(1.0, result.Reward);
        Assert.True(result.Halted);
        Assert.Equal(task.ExpectedOutput, result.Output);
    }

    [Fact]
    public void Reverse_Task_Should_Reverse_Sequence()
    {
        // Arrange
        _mockNetwork.CurrentTask = MockNetworkTask.Reverse;
        var task = new TaskDefinition
        {
            Name = "Reverse",
            Input = new ulong[] { 1, 2, 3, 4, 5 },
            ExpectedOutput = new ulong[] { 5, 4, 3, 2, 1 }
        };

        // Act
        var result = _taskRunner.RunEpisode(task);

        // Assert
        Assert.True(result.Success, $"Reverse task failed. Output: [{string.Join(", ", result.Output)}]");
        Assert.Equal(1.0, result.Reward);
        Assert.True(result.Halted);
        Assert.Equal(task.ExpectedOutput, result.Output);
    }

    [Fact]
    public void Reverse_Task_Should_Handle_Single_Item()
    {
        // Arrange
        _mockNetwork.CurrentTask = MockNetworkTask.Reverse;
        var task = new TaskDefinition
        {
            Name = "Reverse (Single)",
            Input = new ulong[] { 42 },
            ExpectedOutput = new ulong[] { 42 }
        };

        // Act
        var result = _taskRunner.RunEpisode(task);

        // Assert
        Assert.True(result.Success, "Reverse task should handle single item");
        Assert.Equal(1.0, result.Reward);
        Assert.True(result.Halted);
        Assert.Equal(new ulong[] { 42 }, result.Output);
    }

    [Fact]
    public void Reverse_Task_Should_Handle_Two_Items()
    {
        // Arrange
        _mockNetwork.CurrentTask = MockNetworkTask.Reverse;
        var task = new TaskDefinition
        {
            Name = "Reverse (Two)",
            Input = new ulong[] { 1, 2 },
            ExpectedOutput = new ulong[] { 2, 1 }
        };

        // Act
        var result = _taskRunner.RunEpisode(task);

        // Assert
        Assert.True(result.Success, "Reverse task should handle two items");
        Assert.Equal(1.0, result.Reward);
        Assert.True(result.Halted);
        Assert.Equal(task.ExpectedOutput, result.Output);
    }

    [Fact]
    public void Reverse_Task_Should_Handle_Large_Sequence()
    {
        // Arrange
        _mockNetwork.CurrentTask = MockNetworkTask.Reverse;
        var input = Enumerable.Range(0, 100).Select(i => (ulong)i).ToArray();
        var expected = input.Reverse().ToArray();
        var task = new TaskDefinition
        {
            Name = "Reverse (Large)",
            Input = input,
            ExpectedOutput = expected
        };

        // Act
        var result = _taskRunner.RunEpisode(task);

        // Assert
        Assert.True(result.Success, $"Reverse task failed with large sequence. Output length: {result.Output.Length}");
        Assert.Equal(1.0, result.Reward);
        Assert.True(result.Halted);
        Assert.Equal(expected, result.Output);
    }

    [Fact]
    public void All_Tasks_Should_Complete_Within_Cycle_Limit()
    {
        // Arrange
        var tasks = new[]
        {
            (MockNetworkTask.Echo, "Echo", new ulong[] { 1, 2, 3 }),
            (MockNetworkTask.Increment, "Increment", new ulong[] { 1, 2, 3 }),
            (MockNetworkTask.Reverse, "Reverse", new ulong[] { 1, 2, 3 })
        };

        // Act & Assert
        foreach (var (taskType, name, input) in tasks)
        {
            _mockNetwork.CurrentTask = taskType;
            var expected = taskType switch
            {
                MockNetworkTask.Echo => input,
                MockNetworkTask.Increment => input.Select(x => x + 1).ToArray(),
                MockNetworkTask.Reverse => input.Reverse().ToArray(),
                _ => throw new InvalidOperationException()
            };

            var task = new TaskDefinition
            {
                Name = name,
                Input = input,
                ExpectedOutput = expected
            };

            var result = _taskRunner.RunEpisode(task, maxCycles: 200);

            Assert.True(result.Halted, $"{name} task should halt before reaching cycle limit");
            Assert.True(result.CycleCount < 200, $"{name} task used {result.CycleCount} cycles (should be well under limit)");
        }
    }

    #endregion

    #region Dispose

    public void Dispose()
    {
        _clock.Dispose();
        _registerFile.Dispose();
        _alu.Dispose();
        _stack.Dispose();
        _memory.Dispose();
    }

    #endregion
}
