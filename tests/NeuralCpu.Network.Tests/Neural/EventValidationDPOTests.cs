using NeuralCpu.Core.Components;
using NeuralCpu.Network.Neural;

namespace NeuralCpu.Network.Tests.Neural;

/// <summary>
/// Tests for EventValidationDPO - validates that EncodedEvent structures follow rules.
/// Milestone 2: Validation system catches invalid events and can correct them.
/// </summary>
public sealed class EventValidationDPOTests
{
    #region Test: Valid Events Pass Validation

    [Fact]
    public void Validate_RegisterWrite_RequiresReg1AndValue()
    {
        // Arrange: Valid RegisterWrite
        var evt = new EncodedEvent(EventType.RegisterWrite, RegisterId.R1, RegisterId.None, RegisterId.None, 123UL);

        // Act: Validate
        var result = EventValidationDPO.Validate(evt);

        // Assert: Should be valid
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_RegisterCopy_RequiresReg1AndReg2()
    {
        // Arrange: Valid RegisterCopy
        var evt = new EncodedEvent(EventType.RegisterCopy, RegisterId.R0, RegisterId.R2, RegisterId.None, 0UL);

        // Act: Validate
        var result = EventValidationDPO.Validate(evt);

        // Assert: Should be valid
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_AluBinary_RequiresAllThreeRegisters()
    {
        // Arrange: Valid ALU Add
        var evt = new EncodedEvent(EventType.AluAdd, RegisterId.R0, RegisterId.R1, RegisterId.R2, 0UL);

        // Act: Validate
        var result = EventValidationDPO.Validate(evt);

        // Assert: Should be valid
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_AluUnary_RequiresReg1AndReg2()
    {
        // Arrange: Valid ALU Inc (unary)
        var evt = new EncodedEvent(EventType.AluInc, RegisterId.R3, RegisterId.R1, RegisterId.None, 0UL);

        // Act: Validate
        var result = EventValidationDPO.Validate(evt);

        // Assert: Should be valid
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_Push_RequiresOnlyReg1()
    {
        // Arrange: Valid Push
        var evt = new EncodedEvent(EventType.Push, RegisterId.R2, RegisterId.None, RegisterId.None, 0UL);

        // Act: Validate
        var result = EventValidationDPO.Validate(evt);

        // Assert: Should be valid
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_PushImmediate_RequiresValue()
    {
        // Arrange: Valid PushImmediate
        var evt = new EncodedEvent(EventType.PushImmediate, RegisterId.None, RegisterId.None, RegisterId.None, 999UL);

        // Act: Validate
        var result = EventValidationDPO.Validate(evt);

        // Assert: Should be valid
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_LoadDirect_RequiresReg1AndValue()
    {
        // Arrange: Valid LoadDirect (address in value, dest in reg1)
        var evt = new EncodedEvent(EventType.LoadDirect, RegisterId.R1, RegisterId.None, RegisterId.None, 0x1000UL);

        // Act: Validate
        var result = EventValidationDPO.Validate(evt);

        // Assert: Should be valid
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    #endregion

    #region Test: Invalid Events Fail Validation

    [Fact]
    public void Validate_RegisterWrite_MissingReg1_Invalid()
    {
        // Arrange: RegisterWrite without destination register
        var evt = new EncodedEvent(EventType.RegisterWrite, RegisterId.None, RegisterId.None, RegisterId.None, 123UL);

        // Act: Validate
        var result = EventValidationDPO.Validate(evt);

        // Assert: Should be invalid
        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
        Assert.Contains("Reg1", result.Errors[0]);
        Assert.Contains("set", result.Errors[0]);
    }

    [Fact]
    public void Validate_RegisterCopy_MissingReg2_Invalid()
    {
        // Arrange: RegisterCopy with source but no destination
        var evt = new EncodedEvent(EventType.RegisterCopy, RegisterId.R0, RegisterId.None, RegisterId.None, 0UL);

        // Act: Validate
        var result = EventValidationDPO.Validate(evt);

        // Assert: Should be invalid
        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
        Assert.Contains("Reg2", result.Errors[0]);
        Assert.Contains("set", result.Errors[0]);
    }

    [Fact]
    public void Validate_AluBinary_MissingReg3_Invalid()
    {
        // Arrange: ALU Add without destination register
        var evt = new EncodedEvent(EventType.AluAdd, RegisterId.R0, RegisterId.R1, RegisterId.None, 0UL);

        // Act: Validate
        var result = EventValidationDPO.Validate(evt);

        // Assert: Should be invalid
        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
        Assert.Contains("Reg3", result.Errors[0]);
        Assert.Contains("set", result.Errors[0]);
    }

    [Fact]
    public void Validate_Push_WithUnusedReg2_Invalid()
    {
        // Arrange: Push with extra register (shouldn't have Reg2)
        var evt = new EncodedEvent(EventType.Push, RegisterId.R0, RegisterId.R1, RegisterId.None, 0UL);

        // Act: Validate
        var result = EventValidationDPO.Validate(evt);

        // Assert: Should be invalid
        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
        Assert.Contains("Reg2", result.Errors[0]);
        Assert.Contains("None", result.Errors[0]);
    }

    [Fact]
    public void Validate_PC_AsDestination_Invalid()
    {
        // Arrange: Try to write to PC (read-only)
        var evt = new EncodedEvent(EventType.RegisterWrite, RegisterId.PC, RegisterId.None, RegisterId.None, 100UL);

        // Act: Validate
        var result = EventValidationDPO.Validate(evt);

        // Assert: Should be invalid (PC is read-only)
        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
        Assert.Contains("PC", result.Errors[0]);
        Assert.Contains("read-only", result.Errors[0]);
    }

    #endregion

    #region Test: Correction Logic

    [Fact]
    public void Correct_RegisterWrite_MissingReg1_SetsR0()
    {
        // Arrange: Invalid RegisterWrite (no dest register)
        var evt = new EncodedEvent(EventType.RegisterWrite, RegisterId.None, RegisterId.None, RegisterId.None, 123UL);

        // Act: Correct
        var corrected = EventValidationDPO.Correct(evt);

        // Assert: Should set Reg1 to R0 (default)
        Assert.Equal(RegisterId.R0, corrected.Reg1);
        Assert.Equal(123UL, corrected.Value);
    }

    [Fact]
    public void Correct_AluBinary_MissingReg3_SetsR0()
    {
        // Arrange: Invalid ALU Add (no destination)
        var evt = new EncodedEvent(EventType.AluAdd, RegisterId.R1, RegisterId.R2, RegisterId.None, 0UL);

        // Act: Correct
        var corrected = EventValidationDPO.Correct(evt);

        // Assert: Should set Reg3 to R0
        Assert.Equal(RegisterId.R0, corrected.Reg3);
    }

    [Fact]
    public void Correct_Push_WithUnusedReg2_ClearsReg2()
    {
        // Arrange: Push with extra register
        var evt = new EncodedEvent(EventType.Push, RegisterId.R3, RegisterId.R1, RegisterId.None, 0UL);

        // Act: Correct
        var corrected = EventValidationDPO.Correct(evt);

        // Assert: Should clear Reg2 to None
        Assert.Equal(RegisterId.R3, corrected.Reg1); // Keep source
        Assert.Equal(RegisterId.None, corrected.Reg2); // Clear unused
    }

    [Fact]
    public void Correct_PC_AsDestination_ReplacesWithR0()
    {
        // Arrange: Try to write to PC
        var evt = new EncodedEvent(EventType.RegisterWrite, RegisterId.PC, RegisterId.None, RegisterId.None, 100UL);

        // Act: Correct
        var corrected = EventValidationDPO.Correct(evt);

        // Assert: Should replace PC with R0
        Assert.Equal(RegisterId.R0, corrected.Reg1);
    }

    [Fact]
    public void Correct_SP_AsDestination_ReplacesWithR0()
    {
        // Arrange: Try to write to SP (managed by stack)
        var evt = new EncodedEvent(EventType.RegisterWrite, RegisterId.SP, RegisterId.None, RegisterId.None, 50UL);

        // Act: Correct
        var corrected = EventValidationDPO.Correct(evt);

        // Assert: Should replace SP with R0
        Assert.Equal(RegisterId.R0, corrected.Reg1);
    }

    [Fact]
    public void Correct_AlreadyValid_NoChanges()
    {
        // Arrange: Valid event
        var evt = new EncodedEvent(EventType.AluAdd, RegisterId.R0, RegisterId.R1, RegisterId.R2, 0UL);

        // Act: Correct
        var corrected = EventValidationDPO.Correct(evt);

        // Assert: Should be unchanged
        Assert.Equal(evt, corrected);
    }

    #endregion

    #region Test: GetRequirements Helper

    [Fact]
    public void GetRequirements_RegisterWrite_CorrectSpec()
    {
        // Act: Get requirements
        var req = EventValidationDPO.GetRequirements(EventType.RegisterWrite);

        // Assert: Reg1 required, others None, value used
        Assert.True(req.Reg1Required);
        Assert.False(req.Reg2Required);
        Assert.False(req.Reg3Required);
        Assert.True(req.ValueUsed);
    }

    [Fact]
    public void GetRequirements_AluBinary_CorrectSpec()
    {
        // Act: Get requirements
        var req = EventValidationDPO.GetRequirements(EventType.AluAdd);

        // Assert: All three registers required, value not used
        Assert.True(req.Reg1Required);
        Assert.True(req.Reg2Required);
        Assert.True(req.Reg3Required);
        Assert.False(req.ValueUsed);
    }

    [Fact]
    public void GetRequirements_Dup_NoRegisters()
    {
        // Act: Get requirements
        var req = EventValidationDPO.GetRequirements(EventType.Dup);

        // Assert: No registers required
        Assert.False(req.Reg1Required);
        Assert.False(req.Reg2Required);
        Assert.False(req.Reg3Required);
        Assert.False(req.ValueUsed);
    }

    #endregion

    #region Test: All Actionable Event Types

    [Fact]
    public void GetRequirements_AllActionableTypes_HaveSpecifications()
    {
        // All 33 actionable event types should have requirements defined
        var actionableTypes = new[]
        {
            EventType.RegisterWrite, EventType.RegisterCopy, EventType.HardwareInterrupt,
            EventType.AluAdd, EventType.AluSub, EventType.AluMul, EventType.AluDiv,
            EventType.AluAnd, EventType.AluNand, EventType.AluOr, EventType.AluNor,
            EventType.AluXor, EventType.AluXnor, EventType.AluShl, EventType.AluShr,
            EventType.AluSar, EventType.AluRol, EventType.AluRor,
            EventType.AluNot, EventType.AluInc, EventType.AluDec, EventType.AluNeg,
            EventType.AluAbs, EventType.AluClz,
            EventType.Push, EventType.PushImmediate, EventType.Pop, EventType.Peek, EventType.Dup,
            EventType.LoadDirect, EventType.LoadIndirect, EventType.StoreDirect, EventType.StoreIndirect
        };

        foreach (var eventType in actionableTypes)
        {
            // Act: Get requirements (should not throw)
            var req = EventValidationDPO.GetRequirements(eventType);

            // Assert: Should return valid requirements
            Assert.NotNull(req);
        }
    }

    #endregion
}
