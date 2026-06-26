using Microsoft.Extensions.Logging;
using NeuralCpu.Core;
using NeuralCpu.Core.Components;
using NeuralCpu.Core.Configuration;
using NeuralCpu.Core.Events;

namespace NeuralCpu.Network.Tests;

/// <summary>
/// Simple debug test to understand execution flow
/// </summary>
public sealed class SimpleDebugTest : IDisposable
{
    private readonly IEventBus _eventBus;
    private readonly Clock _clock;
    private readonly RegisterFile _registerFile;
    private readonly Alu _alu;
    private readonly Stack _stack;
    private readonly Memory _memory;

    public SimpleDebugTest()
    {
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));

        _eventBus = new EventBus();
        _clock = new Clock(_eventBus, loggerFactory.CreateLogger<Clock>());
        _registerFile = new RegisterFile(_eventBus, loggerFactory.CreateLogger<RegisterFile>());
        _alu = new Alu(_eventBus, _registerFile, loggerFactory.CreateLogger<Alu>());
        _stack = new Stack(_eventBus, _registerFile, loggerFactory.CreateLogger<Stack>());
        _memory = new Memory(_eventBus, _registerFile, loggerFactory.CreateLogger<Memory>());
    }

    [Fact]
    public void Test_Basic_Load_Store_Sequence()
    {
        // Write a value to memory
        _memory[0x100] = 42;

        // Issue LOAD command
        _eventBus.Publish(new LoadCommand(0x100, RegisterId.R0));

        // Check register value BEFORE clock tick - should still be 0
        Assert.Equal(0ul, _registerFile[RegisterId.R0]);

        // Tick clock - this executes queued operations
        _clock.Step();

        // Check register value AFTER clock tick - should be 42
        Assert.Equal(42ul, _registerFile[RegisterId.R0]);

        // Now test STORE
        _eventBus.Publish(new StoreCommand(0x200, RegisterId.R0));

        // Memory should not change until clock ticks
        Assert.Equal(0ul, _memory[0x200]);

        _clock.Step();

        // Now memory should have the value
        Assert.Equal(42ul, _memory[0x200]);
    }

    [Fact]
    public void Test_Write_Then_Read_Register()
    {
        // Write value to R0
        _eventBus.Publish(new RegisterWriteCommand(RegisterId.R0, 99));

        // Before clock tick, R0 should still be 0
        Assert.Equal(0ul, _registerFile[RegisterId.R0]);

        // Tick clock
        _clock.Step();

        // After clock tick, R0 should be 99
        Assert.Equal(99ul, _registerFile[RegisterId.R0]);
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
