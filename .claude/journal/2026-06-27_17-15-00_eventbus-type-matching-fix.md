# Journal Entry - EventBus Type Matching Fix

**Date:** 2026-06-27 17:15:00
**Session:** Debugging and resolving BasicMotorSkillsTask test synchronization blocker

## What We Accomplished

**Fixed critical EventBus type matching issue** that was preventing all BasicMotorSkillsTask tests from passing:

1. **Root Cause Identified**: When `INetworkController.DecideNextAction()` returns `object`, calling `_eventBus.Publish(action)` uses C#'s generic type inference. The compiler sees `action` typed as `object` and infers `Publish<object>(action)`, which looks for subscribers to `typeof(object)`, not `typeof(RegisterWriteCommand)`.

2. **The Symptom**: RegisterWriteCommand and other events were published but never received by components like RegisterFile, even though subscriptions were correctly established.

3. **The Solution**: Added `PublishDynamic(object action)` helper method that uses reflection to call `Publish<T>` with the runtime type:
   ```csharp
   private void PublishDynamic(object action)
   {
       var actionType = action.GetType();
       var publishMethod = typeof(IEventBus).GetMethod(nameof(IEventBus.Publish))!
           .MakeGenericMethod(actionType);
       publishMethod.Invoke(_eventBus, new[] { action });
   }
   ```

4. **Impact**: All 9 BasicMotorSkillsTask tests now pass (previously 3 passing, 6 failing)

5. **Verification**: Full test suite runs clean (105 passing, 5 skipped DPO tests, 0 failures)

6. **Commit**: 9129c8d - "fix: resolve EventBus type matching issue in BasicMotorSkillsTask"

## Key Design Decisions

**1. Why reflection instead of dynamic dispatch?**
   - C#'s `dynamic` keyword would work (`dynamic dynAction = action; _eventBus.Publish(dynAction);`)
   - But reflection is more explicit and gives us control over exception handling
   - We unwrap `TargetInvocationException` to preserve original stack traces
   - Performance impact is negligible (called once per cycle, not in hot loops)

**2. Why not change INetworkController interface?**
   - Could make `DecideNextAction()` generic, but that would require the interface to know all possible event types
   - The `object` return type is actually correct - it allows the controller to return any CPU event
   - The issue was EventBus's strict type matching, not the interface design

**3. Why not fix EventBus instead?**
   - EventBus uses exact type matching by design (each `Subject<T>` is type-specific)
   - Could add dynamic dispatch inside EventBus.Publish(), but that affects all callers
   - Better to fix it at the call site where we know we're dealing with polymorphic actions

## Insights & Observations

**Debugging Process:**
1. Added extensive debug logging to ExecuteCycle()
2. Created isolated debug test with xUnit logger integration
3. Log output revealed: "Publishing action: RegisterWriteCommand" but no "Queued write to R0" from RegisterFile
4. This proved the event wasn't reaching subscribers, even though subscriptions existed
5. Realized the issue must be type-based routing in EventBus

**Why This Wasn't Caught Earlier:**
- TaskRunner doesn't have this issue because MockNetwork.DecideNextAction() returns `EncodedEvent`, which it then decodes into specific event types before publishing
- TaskRunner calls `_eventBus.Publish(decodedEvent)` where `decodedEvent` has a concrete type (RegisterWriteCommand, PushCommand, etc.)
- BasicMotorSkillsTask has DeterministicController returning `object` directly, which exposed the type matching issue

**Lesson Learned:**
When using EventBus with polymorphic actions:
- Either decode to concrete types before publishing (TaskRunner approach)
- Or use reflection to publish with runtime type (BasicMotorSkillsTask approach)
- Never call `Publish()` on a variable typed as `object` - the generic type parameter matters!

**Why Tests Failed But Console Didn't:**
- We never ran BasicMotorSkillsTask in the Console app yet
- If we had, we would have hit this same issue immediately
- Tests caught a real bug, not a test harness problem

## Next Steps

**Immediate (Milestone 5.5.3 completion):**
1. ✅ ~~Fix test synchronization~~ (DONE)
2. Integrate BasicMotorSkillsTask with PolicyGradientTrainer
3. Run training for 200 episodes per task:
   - RegisterActivationTask
   - StackGrowthTask
   - ALUActivationTask
   - InputReadTask
   - OutputWriteTask
4. Measure: Success rate per task, cycles to first success

**Milestone 5.5.4: Coordination Tasks**
- Multi-step sequences (RegisterCopy, RegisterSwap, ALUCompute)
- Device-to-register and register-to-device coordination

**Milestone 5.5.5: Goal-Oriented Tasks**
- Echo, Increment, Reverse (now achievable with device discovery)

**Milestone 5.5.6: Curriculum Training Orchestrator**
- Automated progression through curriculum stages

## Questions & Uncertainties

**1. Should we add PublishDynamic to EventBus itself?**
   - Would make it available to all callers: `_eventBus.PublishDynamic(action)`
   - But might encourage anti-pattern of publishing with `object` type
   - Current solution keeps the fix localized to where it's needed

**2. Should we add a warning/analyzer for `Publish(object)`?**
   - Could create a Roslyn analyzer that warns when `Publish<T>` is called with `T=object`
   - Would catch this issue at compile time in future
   - Might be overkill for a one-time issue

**3. Does BodyDiscoveryTask have the same issue?**
   - Need to verify: Does TransformerCpuController.DecideNextAction() return object?
   - If so, we'll hit the same problem during body discovery training
   - Should check and fix preemptively

**4. Performance impact of reflection per cycle?**
   - Reflection overhead: ~100-500ns per call (negligible)
   - Alternative: Cache MethodInfo per type in dictionary
   - Probably not worth optimizing until profiling shows it's a bottleneck

## Technical Notes

**EventBus Design:**
```csharp
// Internal structure
ConcurrentDictionary<Type, object> _subscriptions;
// typeof(RegisterWriteCommand) → Subject<RegisterWriteCommand>
// typeof(object) → Subject<object> (separate entry!)

// Publish<T> only notifies Subject<T>, not derived types
public void Publish<T>(T @event)
{
    if (_subscriptions.TryGetValue(typeof(T), out var subject))
    {
        ((Subject<T>)subject).OnNext(@event);
    }
}
```

**Type Inference Example:**
```csharp
object action = new RegisterWriteCommand(R0, 42);
_eventBus.Publish(action);
// Compiler infers: Publish<object>(action)
// Looks for subscribers to typeof(object)
// RegisterFile subscribed to typeof(RegisterWriteCommand)
// No match → event dropped silently
```

**Working Pattern:**
```csharp
object action = new RegisterWriteCommand(R0, 42);
PublishDynamic(action);
// Runtime type: typeof(RegisterWriteCommand)
// Uses reflection: Publish<RegisterWriteCommand>(action)
// Finds RegisterFile subscription → delivered!
```

---

**Status**: Milestone 5.5.3 unblocked, ready for training integration.

**Files Modified**: 
- src/NeuralCpu.Network/Training/BasicMotorSkillsTask.cs (+24 lines)
- TODO.md (marked 5.5.3 as complete)

**Test Results**: 105 passing, 5 skipped, 0 failures

*This journal will be read by future Claude instances to understand project evolution.*
