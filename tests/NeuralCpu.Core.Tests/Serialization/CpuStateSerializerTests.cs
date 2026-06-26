using Microsoft.Extensions.Logging.Abstractions;
using NeuralCpu.Core.Components;
using NeuralCpu.Core.Events;
using NeuralCpu.Core.Serialization;

namespace NeuralCpu.Core.Tests.Serialization;

/// <summary>
/// Tests for CPU state serialization and deserialization.
/// </summary>
public sealed class CpuStateSerializerTests : IDisposable
{
    private readonly IEventBus _eventBus;
    private readonly Clock _clock;
    private readonly RegisterFile _registerFile;
    private readonly Alu _alu;
    private readonly Stack _stack;
    private readonly Memory _memory;

    public CpuStateSerializerTests()
    {
        _eventBus = new EventBus();
        _clock = new Clock(_eventBus, NullLogger<Clock>.Instance);
        _registerFile = new RegisterFile(_eventBus, NullLogger<RegisterFile>.Instance);
        _alu = new Alu(_eventBus, _registerFile, NullLogger<Alu>.Instance);
        _stack = new Stack(_eventBus, _registerFile, NullLogger<Stack>.Instance);
        _memory = new Memory(_eventBus, _registerFile, NullLogger<Memory>.Instance);
    }

    [Fact]
    public void Capture_Should_Create_Snapshot_Of_Initial_State()
    {
        // Act
        var state = CpuStateSerializer.Capture(_clock, _registerFile, _stack, _memory);

        // Assert
        Assert.NotNull(state);
        Assert.Equal(0UL, state.CycleCount);
        Assert.Equal(ClockPhase.Low, state.Phase);
        Assert.Equal(1, state.FrequencyHz);

        // All registers should be zero
        Assert.Equal(7, state.Registers.Count);
        Assert.Equal(0UL, state.Registers[RegisterId.R0]);
        Assert.Equal(0UL, state.Registers[RegisterId.R1]);
        Assert.Equal(0UL, state.Registers[RegisterId.R2]);
        Assert.Equal(0UL, state.Registers[RegisterId.R3]);
        Assert.Equal(0UL, state.Registers[RegisterId.PC]);
        Assert.Equal(0UL, state.Registers[RegisterId.SP]);
        Assert.Equal(0UL, state.Registers[RegisterId.Flags]);

        // Stack should be empty
        Assert.Empty(state.Stack);

        // Memory should be empty (sparse representation)
        Assert.Empty(state.Memory);
        Assert.Equal(131072, state.MemoryCapacityWords); // Default 128K words
    }

    [Fact]
    public void Capture_Should_Include_Register_Values()
    {
        // Arrange - write some register values
        _eventBus.Publish(new RegisterWriteCommand(RegisterId.R0, 0x1234));
        _eventBus.Publish(new RegisterWriteCommand(RegisterId.R1, 0x5678));
        _eventBus.Publish(new RegisterWriteWithFlagsCommand(RegisterId.R2, 0x0)); // Sets Zero flag
        _clock.Step(); // Process writes

        // Act
        var state = CpuStateSerializer.Capture(_clock, _registerFile, _stack, _memory);

        // Assert
        Assert.Equal(0x1234UL, state.Registers[RegisterId.R0]);
        Assert.Equal(0x5678UL, state.Registers[RegisterId.R1]);
        Assert.Equal(0x0UL, state.Registers[RegisterId.R2]);
        Assert.Equal(1UL, state.Registers[RegisterId.PC]); // Auto-incremented by Step()
        Assert.NotEqual(0UL, state.Registers[RegisterId.Flags]); // Zero flag set
    }

    [Fact]
    public void Capture_Should_Include_Stack_Contents()
    {
        // Arrange - push some values
        _eventBus.Publish(new PushImmediateCommand(0x1111));
        _clock.Step();
        _eventBus.Publish(new PushImmediateCommand(0x2222));
        _clock.Step();
        _eventBus.Publish(new PushImmediateCommand(0x3333));
        _clock.Step();

        // Act
        var state = CpuStateSerializer.Capture(_clock, _registerFile, _stack, _memory);

        // Assert
        Assert.Equal(3, state.Stack.Length);
        Assert.Equal(0x3333UL, state.Stack[0]); // Top of stack
        Assert.Equal(0x2222UL, state.Stack[1]);
        Assert.Equal(0x1111UL, state.Stack[2]); // Bottom
        Assert.Equal(3UL, state.Registers[RegisterId.SP]); // Stack depth
    }

    [Fact]
    public void Capture_Should_Include_Memory_Contents_Sparse()
    {
        // Arrange - write some non-zero memory values
        _eventBus.Publish(new RegisterWriteCommand(RegisterId.R0, 0xABCD));
        _clock.Step();
        _eventBus.Publish(new StoreCommand(0x100, RegisterId.R0));
        _clock.Step();
        _eventBus.Publish(new RegisterWriteCommand(RegisterId.R0, 0xDEADBEEF));
        _clock.Step();
        _eventBus.Publish(new StoreCommand(0x200, RegisterId.R0));
        _clock.Step();
        _eventBus.Publish(new RegisterWriteCommand(RegisterId.R0, 0xCAFEBABE));
        _clock.Step();
        _eventBus.Publish(new StoreCommand(0x300, RegisterId.R0));
        _clock.Step();

        // Act
        var state = CpuStateSerializer.Capture(_clock, _registerFile, _stack, _memory);

        // Assert - only non-zero memory locations (sparse representation)
        Assert.Equal(3, state.Memory.Count);
        Assert.Equal(0xABCDUL, state.Memory[0x100]);
        Assert.Equal(0xDEADBEEFUL, state.Memory[0x200]);
        Assert.Equal(0xCAFEBABEUL, state.Memory[0x300]);
    }

    [Fact]
    public void Capture_Should_Reflect_Clock_State()
    {
        // Arrange
        _clock.FrequencyHz = 10;
        _clock.Step();
        _clock.Step();
        _clock.Step();

        // Act
        var state = CpuStateSerializer.Capture(_clock, _registerFile, _stack, _memory);

        // Assert
        Assert.Equal(3UL, state.CycleCount);
        Assert.Equal(10, state.FrequencyHz);
        Assert.Equal(ClockPhase.Low, state.Phase); // Step() ends on falling edge
    }

    [Fact]
    public void ToJson_Should_Produce_Valid_JSON()
    {
        // Arrange - create interesting state
        _eventBus.Publish(new RegisterWriteCommand(RegisterId.R0, 0x42));
        _eventBus.Publish(new PushImmediateCommand(0x1234));
        _clock.Step();

        var state = CpuStateSerializer.Capture(_clock, _registerFile, _stack, _memory);

        // Act
        var json = CpuStateSerializer.ToJson(state);

        // Assert
        Assert.NotNull(json);
        Assert.Contains("\"cycleCount\"", json);
        Assert.Contains("\"registers\"", json);
        Assert.Contains("\"stack\"", json);
        Assert.Contains("\"memory\"", json);
        Assert.Contains("\"r0\"", json.ToLowerInvariant());
    }

    [Fact]
    public void FromJson_Should_Deserialize_State()
    {
        // Arrange
        _eventBus.Publish(new RegisterWriteCommand(RegisterId.R0, 0x1234));
        _eventBus.Publish(new RegisterWriteCommand(RegisterId.R1, 0x5678));
        _eventBus.Publish(new PushImmediateCommand(0xABCD));
        _clock.Step();

        var originalState = CpuStateSerializer.Capture(_clock, _registerFile, _stack, _memory);
        var json = CpuStateSerializer.ToJson(originalState);

        // Act
        var deserializedState = CpuStateSerializer.FromJson(json);

        // Assert
        Assert.Equal(originalState.CycleCount, deserializedState.CycleCount);
        Assert.Equal(originalState.Phase, deserializedState.Phase);
        Assert.Equal(originalState.FrequencyHz, deserializedState.FrequencyHz);
        Assert.Equal(originalState.Registers[RegisterId.R0], deserializedState.Registers[RegisterId.R0]);
        Assert.Equal(originalState.Registers[RegisterId.R1], deserializedState.Registers[RegisterId.R1]);
        Assert.Equal(originalState.Stack.Length, deserializedState.Stack.Length);
        Assert.Equal(originalState.Stack[0], deserializedState.Stack[0]);
        Assert.Equal(originalState.MemoryCapacityWords, deserializedState.MemoryCapacityWords);
    }

    [Fact]
    public void Roundtrip_Should_Preserve_All_State()
    {
        // Arrange - create complex state
        _clock.FrequencyHz = 5;

        // Set registers
        _eventBus.Publish(new RegisterWriteCommand(RegisterId.R0, 0x1111));
        _eventBus.Publish(new RegisterWriteCommand(RegisterId.R1, 0x2222));
        _eventBus.Publish(new RegisterWriteCommand(RegisterId.R2, 0x3333));
        _eventBus.Publish(new RegisterWriteCommand(RegisterId.R3, 0x4444));
        _clock.Step();

        // Push stack values
        _eventBus.Publish(new PushCommand(RegisterId.R0));
        _clock.Step();
        _eventBus.Publish(new PushCommand(RegisterId.R1));
        _clock.Step();

        // Write memory
        _eventBus.Publish(new StoreCommand(0x100, RegisterId.R2));
        _clock.Step();
        _eventBus.Publish(new StoreCommand(0x200, RegisterId.R3));
        _clock.Step();

        var originalState = CpuStateSerializer.Capture(_clock, _registerFile, _stack, _memory);

        // Act - serialize and deserialize
        var json = CpuStateSerializer.ToJson(originalState);
        var roundtrippedState = CpuStateSerializer.FromJson(json);

        // Assert - verify all fields match
        Assert.Equal(originalState.CycleCount, roundtrippedState.CycleCount);
        Assert.Equal(originalState.Phase, roundtrippedState.Phase);
        Assert.Equal(originalState.FrequencyHz, roundtrippedState.FrequencyHz);

        // Registers
        foreach (var (regId, value) in originalState.Registers)
        {
            Assert.Equal(value, roundtrippedState.Registers[regId]);
        }

        // Stack
        Assert.Equal(originalState.Stack.Length, roundtrippedState.Stack.Length);
        for (int i = 0; i < originalState.Stack.Length; i++)
        {
            Assert.Equal(originalState.Stack[i], roundtrippedState.Stack[i]);
        }

        // Memory
        Assert.Equal(originalState.Memory.Count, roundtrippedState.Memory.Count);
        foreach (var (addr, value) in originalState.Memory)
        {
            Assert.True(roundtrippedState.Memory.ContainsKey(addr));
            Assert.Equal(value, roundtrippedState.Memory[addr]);
        }

        Assert.Equal(originalState.MemoryCapacityWords, roundtrippedState.MemoryCapacityWords);
    }

    [Fact]
    public void SaveToFile_And_LoadFromFile_Should_Roundtrip()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();

        try
        {
            _eventBus.Publish(new RegisterWriteCommand(RegisterId.R0, 0xCAFEBABE));
            _eventBus.Publish(new PushImmediateCommand(0xDEADBEEF));
            _clock.Step();

            var originalState = CpuStateSerializer.Capture(_clock, _registerFile, _stack, _memory);

            // Act
            CpuStateSerializer.SaveToFile(originalState, tempFile);
            var loadedState = CpuStateSerializer.LoadFromFile(tempFile);

            // Assert
            Assert.Equal(originalState.CycleCount, loadedState.CycleCount);
            Assert.Equal(originalState.Registers[RegisterId.R0], loadedState.Registers[RegisterId.R0]);
            Assert.Equal(originalState.Stack[0], loadedState.Stack[0]);
        }
        finally
        {
            // Cleanup
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public void Capture_Should_Handle_Empty_Memory()
    {
        // Arrange - don't write anything to memory

        // Act
        var state = CpuStateSerializer.Capture(_clock, _registerFile, _stack, _memory);

        // Assert
        Assert.Empty(state.Memory);
    }

    [Fact]
    public void Capture_Should_Handle_Large_Stack()
    {
        // Arrange - push many values
        for (int i = 0; i < 100; i++)
        {
            _eventBus.Publish(new PushImmediateCommand((ulong)i));
            _clock.Step();
        }

        // Act
        var state = CpuStateSerializer.Capture(_clock, _registerFile, _stack, _memory);

        // Assert
        Assert.Equal(100, state.Stack.Length);
        Assert.Equal(99UL, state.Stack[0]); // Top of stack (last pushed)
        Assert.Equal(0UL, state.Stack[99]); // Bottom of stack (first pushed)
    }

    [Fact]
    public void LoadFromFile_Should_Throw_When_File_Not_Found()
    {
        // Arrange
        var nonExistentFile = Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid() + ".json");

        // Act & Assert
        Assert.Throws<FileNotFoundException>(() => CpuStateSerializer.LoadFromFile(nonExistentFile));
    }

    [Fact]
    public void Restore_Should_Restore_Clock_State()
    {
        // Arrange - advance clock to known state
        _clock.FrequencyHz = 100;
        _clock.Step();
        _clock.Step();
        _clock.Step();

        var originalState = CpuStateSerializer.Capture(_clock, _registerFile, _stack, _memory);

        // Modify clock state
        _clock.FrequencyHz = 1;
        _clock.Step();

        // Act - restore original state
        CpuStateSerializer.Restore(originalState, _clock, _registerFile, _stack, _memory);

        // Assert - clock state matches original
        Assert.Equal(3UL, _clock.CycleCount);
        Assert.Equal(ClockPhase.Low, _clock.CurrentPhase);
        Assert.Equal(100, _clock.FrequencyHz);
    }

    [Fact]
    public void Restore_Should_Restore_Register_Values()
    {
        // Arrange - set initial register values
        _eventBus.Publish(new RegisterWriteCommand(RegisterId.R0, 0x1111));
        _eventBus.Publish(new RegisterWriteCommand(RegisterId.R1, 0x2222));
        _eventBus.Publish(new RegisterWriteCommand(RegisterId.R2, 0x3333));
        _clock.Step();

        var originalState = CpuStateSerializer.Capture(_clock, _registerFile, _stack, _memory);

        // Modify registers
        _eventBus.Publish(new RegisterWriteCommand(RegisterId.R0, 0x9999));
        _eventBus.Publish(new RegisterWriteCommand(RegisterId.R1, 0x8888));
        _clock.Step();

        // Act - restore original state
        CpuStateSerializer.Restore(originalState, _clock, _registerFile, _stack, _memory);

        // Assert - registers match original
        Assert.Equal(0x1111UL, _registerFile[RegisterId.R0]);
        Assert.Equal(0x2222UL, _registerFile[RegisterId.R1]);
        Assert.Equal(0x3333UL, _registerFile[RegisterId.R2]);
        Assert.Equal(1UL, _registerFile[RegisterId.PC]); // PC from original state
    }

    [Fact]
    public void Restore_Should_Restore_Stack_Contents()
    {
        // Arrange - push values onto stack
        _eventBus.Publish(new PushImmediateCommand(0xAAAA));
        _clock.Step();
        _eventBus.Publish(new PushImmediateCommand(0xBBBB));
        _clock.Step();
        _eventBus.Publish(new PushImmediateCommand(0xCCCC));
        _clock.Step();

        var originalState = CpuStateSerializer.Capture(_clock, _registerFile, _stack, _memory);

        // Modify stack
        _eventBus.Publish(new PopCommand(RegisterId.R0));
        _clock.Step();

        // Act - restore original state
        CpuStateSerializer.Restore(originalState, _clock, _registerFile, _stack, _memory);

        // Assert - stack matches original
        var snapshot = _stack.GetSnapshot();
        Assert.Equal(3, snapshot.Length);
        Assert.Equal(0xCCCCUL, snapshot[0]); // Top
        Assert.Equal(0xBBBBUL, snapshot[1]);
        Assert.Equal(0xAAAAUL, snapshot[2]); // Bottom
    }

    [Fact]
    public void Restore_Should_Restore_Memory_Contents()
    {
        // Arrange - write memory values
        _eventBus.Publish(new RegisterWriteCommand(RegisterId.R0, 0xDEAD));
        _clock.Step();
        _eventBus.Publish(new StoreCommand(0x100, RegisterId.R0));
        _clock.Step();
        _eventBus.Publish(new RegisterWriteCommand(RegisterId.R0, 0xBEEF));
        _clock.Step();
        _eventBus.Publish(new StoreCommand(0x200, RegisterId.R0));
        _clock.Step();

        var originalState = CpuStateSerializer.Capture(_clock, _registerFile, _stack, _memory);

        // Modify memory
        _eventBus.Publish(new RegisterWriteCommand(RegisterId.R0, 0xCAFE));
        _clock.Step();
        _eventBus.Publish(new StoreCommand(0x100, RegisterId.R0));
        _clock.Step();

        // Act - restore original state
        CpuStateSerializer.Restore(originalState, _clock, _registerFile, _stack, _memory);

        // Assert - memory matches original
        Assert.Equal(0xDEADUL, _memory[0x100]);
        Assert.Equal(0xBEEFUL, _memory[0x200]);
    }

    [Fact]
    public void Restore_Should_Handle_Empty_Stack()
    {
        // Arrange - capture with empty stack
        var originalState = CpuStateSerializer.Capture(_clock, _registerFile, _stack, _memory);

        // Push items onto stack
        _eventBus.Publish(new PushImmediateCommand(0x1234));
        _clock.Step();

        // Act - restore empty stack
        CpuStateSerializer.Restore(originalState, _clock, _registerFile, _stack, _memory);

        // Assert - stack is empty
        Assert.Equal(0, _stack.Count);
    }

    [Fact]
    public void Restore_Should_Handle_Empty_Memory()
    {
        // Arrange - capture with empty memory
        var originalState = CpuStateSerializer.Capture(_clock, _registerFile, _stack, _memory);

        // Write memory
        _eventBus.Publish(new RegisterWriteCommand(RegisterId.R0, 0x5678));
        _clock.Step();
        _eventBus.Publish(new StoreCommand(0x100, RegisterId.R0));
        _clock.Step();

        // Act - restore empty memory
        CpuStateSerializer.Restore(originalState, _clock, _registerFile, _stack, _memory);

        // Assert - memory is zeroed
        Assert.Equal(0UL, _memory[0x100]);
    }

    [Fact]
    public void Restore_Should_Clear_Pending_Operations()
    {
        // Arrange - capture clean state
        var originalState = CpuStateSerializer.Capture(_clock, _registerFile, _stack, _memory);

        // Queue operations but don't execute them (no clock tick)
        _eventBus.Publish(new RegisterWriteCommand(RegisterId.R0, 0xFFFF));
        _eventBus.Publish(new PushImmediateCommand(0xAAAA));
        _eventBus.Publish(new StoreCommand(0x100, RegisterId.R0));

        // Act - restore state (should clear pending operations)
        CpuStateSerializer.Restore(originalState, _clock, _registerFile, _stack, _memory);

        // Tick clock to process any remaining operations
        _clock.Step();

        // Assert - pending operations were cleared, values remain at restored state
        Assert.Equal(0UL, _registerFile[RegisterId.R0]); // Not 0xFFFF
        Assert.Equal(0, _stack.Count); // Not 1
        Assert.Equal(0UL, _memory[0x100]); // Not 0xFFFF
    }

    [Fact]
    public void Full_Restore_Cycle_Should_Preserve_All_State()
    {
        // Arrange - create complex CPU state
        _clock.FrequencyHz = 42;

        // Set registers
        _eventBus.Publish(new RegisterWriteCommand(RegisterId.R0, 0x1111));
        _eventBus.Publish(new RegisterWriteCommand(RegisterId.R1, 0x2222));
        _clock.Step();

        // Push stack
        _eventBus.Publish(new PushCommand(RegisterId.R0));
        _eventBus.Publish(new PushCommand(RegisterId.R1));
        _clock.Step();

        // Write memory
        _eventBus.Publish(new StoreCommand(0x100, RegisterId.R0));
        _eventBus.Publish(new StoreCommand(0x200, RegisterId.R1));
        _clock.Step();

        var originalState = CpuStateSerializer.Capture(_clock, _registerFile, _stack, _memory);

        // Completely trash the state
        _eventBus.Publish(new HardwareInterrupt(Clock.INTERRUPT_RESET));
        _clock.Step(); // Process reset
        _clock.FrequencyHz = 1;

        // Act - restore from snapshot
        CpuStateSerializer.Restore(originalState, _clock, _registerFile, _stack, _memory);

        // Assert - everything matches original
        Assert.Equal(3UL, _clock.CycleCount);
        Assert.Equal(42, _clock.FrequencyHz);
        Assert.Equal(0x1111UL, _registerFile[RegisterId.R0]);
        Assert.Equal(0x2222UL, _registerFile[RegisterId.R1]);
        Assert.Equal(2, _stack.Count);
        var stackSnapshot = _stack.GetSnapshot();
        Assert.Equal(0x2222UL, stackSnapshot[0]); // Top
        Assert.Equal(0x1111UL, stackSnapshot[1]); // Bottom
        Assert.Equal(0x1111UL, _memory[0x100]);
        Assert.Equal(0x2222UL, _memory[0x200]);
    }

    [Fact]
    public void Restore_Should_Throw_When_Memory_Capacity_Mismatch()
    {
        // Arrange - capture state with default capacity
        var originalState = CpuStateSerializer.Capture(_clock, _registerFile, _stack, _memory);

        // Create new components with different memory capacity
        var smallMemory = new Memory(_eventBus, _registerFile, NullLogger<Memory>.Instance, 1024);

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() =>
            CpuStateSerializer.Restore(originalState, _clock, _registerFile, _stack, smallMemory));

        Assert.Contains("Memory capacity mismatch", exception.Message);
    }

    [Fact]
    public void SaveToFile_Restore_LoadFromFile_Should_Roundtrip()
    {
        // Arrange - create state
        var tempFile = Path.GetTempFileName();

        try
        {
            _clock.FrequencyHz = 7;
            _eventBus.Publish(new RegisterWriteCommand(RegisterId.R0, 0xABCD));
            _eventBus.Publish(new PushImmediateCommand(0x1234));
            _clock.Step();
            _eventBus.Publish(new StoreCommand(0x50, RegisterId.R0));
            _clock.Step();

            var originalState = CpuStateSerializer.Capture(_clock, _registerFile, _stack, _memory);

            // Act - save, trash state, load, restore
            CpuStateSerializer.SaveToFile(originalState, tempFile);

            _eventBus.Publish(new HardwareInterrupt(Clock.INTERRUPT_RESET));
            _clock.Step();

            var loadedState = CpuStateSerializer.LoadFromFile(tempFile);
            CpuStateSerializer.Restore(loadedState, _clock, _registerFile, _stack, _memory);

            // Assert - matches original
            Assert.Equal(2UL, _clock.CycleCount);
            Assert.Equal(7, _clock.FrequencyHz);
            Assert.Equal(0xABCDUL, _registerFile[RegisterId.R0]);
            Assert.Equal(1, _stack.Count);
            Assert.Equal(0x1234UL, _stack.GetSnapshot()[0]);
            Assert.Equal(0xABCDUL, _memory[0x50]);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
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
