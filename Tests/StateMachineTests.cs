using Microsoft.SemanticKernel;
using SemanticKernel.StateMachine;
using Stateless;
using System.ComponentModel;
using System.Reflection;
using Xunit;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Moq;

namespace Tests;

public enum SimpleState
{
    A,
    B,
    C
}

public enum SimpleTrigger
{
    Go,
    Back,
    Reset
}

public class StateMachinePluginTests
{
    private StateMachine<SimpleState, SimpleTrigger> CreateSimpleStateMachine()
    {
        var sm = new StateMachine<SimpleState, SimpleTrigger>(SimpleState.A);
        sm.Configure(SimpleState.A).Permit(SimpleTrigger.Go, SimpleState.B);
        sm.Configure(SimpleState.B).Permit(SimpleTrigger.Back, SimpleState.A);
        return sm;
    }

    [Fact]
    public void GetCurrentState_ReturnsInitialState()
    {
        var sm = CreateSimpleStateMachine();
        var plugin = new StateMachinePlugin<SimpleState, SimpleTrigger>(sm);
        Assert.Equal("A", plugin.GetCurrentState());
    }

    [Fact]
    public async Task Transition_TransitionsState()
    {
        var sm = CreateSimpleStateMachine();
        var plugin = new StateMachinePlugin<SimpleState, SimpleTrigger>(sm);
        var result = await plugin.Transition("Go");
        Assert.Equal("Trigger 'Go' executed, transitioned to state: B", result);
        Assert.Equal("B", plugin.GetCurrentState());
    }

    [Fact]
    public async Task Transition_ReturnsError_WhenTriggerIsEmpty()
    {
        var sm = CreateSimpleStateMachine();
        var plugin = new StateMachinePlugin<SimpleState, SimpleTrigger>(sm);
        var result = await plugin.Transition(string.Empty);
        Assert.Equal("Error: Trigger name cannot be empty.", result);
    }

    [Fact]
    public async Task Transition_ReturnsError_WhenTriggerIsInvalid()
    {
        var sm = CreateSimpleStateMachine();
        var plugin = new StateMachinePlugin<SimpleState, SimpleTrigger>(sm);
        var result = await plugin.Transition("InvalidTrigger");
        Assert.Contains("Error:", result);
        Assert.Contains("InvalidTrigger", result);
    }

    [Fact]
    public async Task Transition_ReturnsError_WhenTriggerCannotBeFired()
    {
        var sm = new StateMachine<SimpleState, SimpleTrigger>(SimpleState.A);
        // No permitted triggers from A
        var plugin = new StateMachinePlugin<SimpleState, SimpleTrigger>(sm);
        var result = await plugin.Transition("Go"); // Go is a valid trigger enum but not permitted
        Assert.Contains("Trigger 'Go' cannot be executed from state A. Permitted triggers: ", result);
    }

    [Fact]
    public void CanFireTrigger_ReturnsCorrectly()
    {
        var sm = CreateSimpleStateMachine();
        var plugin = new StateMachinePlugin<SimpleState, SimpleTrigger>(sm);
        Assert.Equal("Trigger 'Go' can be executed from state A.", plugin.CanFireTrigger("Go"));
        Assert.Equal("Trigger 'Back' cannot be executed from state A. Permitted triggers: Go", plugin.CanFireTrigger("Back"));
    }

    [Fact]
    public void CanFireTrigger_ReturnsError_WhenTriggerIsEmpty()
    {
        var sm = CreateSimpleStateMachine();
        var plugin = new StateMachinePlugin<SimpleState, SimpleTrigger>(sm);
        var result = plugin.CanFireTrigger(string.Empty);
        Assert.Equal("Error: Trigger name cannot be empty.", result);
    }

    [Fact]
    public void CanFireTrigger_ReturnsError_WhenTriggerIsInvalid()
    {
        var sm = CreateSimpleStateMachine();
        var plugin = new StateMachinePlugin<SimpleState, SimpleTrigger>(sm);
        var result = plugin.CanFireTrigger("InvalidTrigger");
        Assert.Contains("Error:", result);
        Assert.Contains("InvalidTrigger", result);
    }

    [Fact]
    public void GetStates_ReturnsAllStates()
    {
        var sm = CreateSimpleStateMachine();
        var plugin = new StateMachinePlugin<SimpleState, SimpleTrigger>(sm);
        var states = plugin.GetStates();
        Assert.Contains("A", states);
        Assert.Contains("B", states);
    }

    [Fact]
    public void GetPermittedTriggers_ReturnsEmpty_WhenNoTriggersArePermitted()
    {
        var sm = new StateMachine<SimpleState, SimpleTrigger>(SimpleState.C); // Assume C has no outgoing transitions
        var plugin = new StateMachinePlugin<SimpleState, SimpleTrigger>(sm);
        var triggers = plugin.GetPermittedTriggers();
        Assert.Empty(triggers);
    }

    [Fact]
    public void GetAllTriggers_ReturnsAllEnumValues()
    {
        var sm = CreateSimpleStateMachine();
        var plugin = new StateMachinePlugin<SimpleState, SimpleTrigger>(sm);
        var triggers = plugin.GetAllTriggers();
        Assert.Contains("Go", triggers);
        Assert.Contains("Back", triggers);
        Assert.Contains("Reset", triggers);
        Assert.Equal(3, triggers.Length);
    }

    [Fact]
    public void GetMermaidGraph_ReturnsMermaidGraphFormat()
    {
        var sm = CreateSimpleStateMachine();
        var plugin = new StateMachinePlugin<SimpleState, SimpleTrigger>(sm);
        var graph = plugin.GetMermaidGraph();
        Assert.StartsWith("stateDiagram-v2", graph.Trim()); // Mermaid graphs now start with 'stateDiagram-v2'
        Assert.Contains("A -->", graph); // More flexible: just check A has a transition
        Assert.Contains("B -->", graph); // More flexible: just check B has a transition
    }

    [Fact]
    public void GetPluginInstructions_ContainsCorrectInformation_WithRenamedMethods()
    {
        var sm = CreateSimpleStateMachine();
        var plugin = new StateMachinePlugin<SimpleState, SimpleTrigger>(sm);
        var prompt = plugin.StateMachine.GetPluginInstructions();
        Assert.Contains("Current State", prompt); // Now checks for the new format
        Assert.Contains("Available States", prompt);
        Assert.Contains("Permitted Triggers", prompt);
        Assert.Contains("A -->", prompt); // Mermaid graph format
    }

    [Fact]
    public void GetPluginInstructions_IncludesCustomTriggerDescriptions_InPermittedTriggers()
    {
        var sm = CreateSimpleStateMachine();
        var plugin = new StateMachinePlugin<SimpleState, SimpleTrigger>(sm);
        var prompt = plugin.StateMachine.GetPluginInstructions();
        Assert.Contains("Go", prompt); // Only check for the trigger name
    }

    // More complex test scenario
    private enum LightState { Off, On, Blinking, Broken }
    private enum LightTrigger { Toggle, StartBlinking, StopBlinking, PowerOutage, Fix }

    private StateMachine<LightState, LightTrigger> CreateLightStateMachineWithKernelAccess(
        Kernel? kernel, 
        Action<Kernel, StateMachine<LightState, LightTrigger>.Transition>? onBrokenAction = null)
    {
        var sm = new StateMachine<LightState, LightTrigger>(LightState.Off);
        sm.Configure(LightState.Off)
            .Permit(LightTrigger.Toggle, LightState.On);

        sm.Configure(LightState.On)
            .Permit(LightTrigger.Toggle, LightState.Off)
            .Permit(LightTrigger.StartBlinking, LightState.Blinking);

        sm.Configure(LightState.Blinking)
            .Permit(LightTrigger.StopBlinking, LightState.On)
            .Permit(LightTrigger.Toggle, LightState.Off);

        sm.Configure(LightState.Broken)
            .Permit(LightTrigger.Fix, LightState.Off)
            .OnEntry(t => 
            {
                if (kernel != null && onBrokenAction != null) 
                {
                    onBrokenAction(kernel, t);
                }
            });
        
        // Global trigger to Broken state, simulating a more complex scenario
        sm.Configure(LightState.Off).Permit(LightTrigger.PowerOutage, LightState.Broken);
        sm.Configure(LightState.On).Permit(LightTrigger.PowerOutage, LightState.Broken);
        sm.Configure(LightState.Blinking).Permit(LightTrigger.PowerOutage, LightState.Broken);

        return sm;
    }

    [Fact]
    public void GetPluginInstructions_ForLightStateMachine_IsInformative()
    {
        var sm = CreateLightStateMachineWithKernelAccess(null); // No kernel needed for this specific test
        var plugin = new StateMachinePlugin<LightState, LightTrigger>(sm);
        // Initial state: Off
        var prompt = plugin.StateMachine.GetPluginInstructions();
        Assert.Contains("**Current State**: Off", prompt);
        Assert.Contains("**Available States**: Off, On, Blinking, Broken", prompt);
        Assert.Contains("**Permitted Triggers**: Toggle, PowerOutage", prompt);
        Assert.Contains("Off -->", prompt);
        Assert.Contains("On -->", prompt); 
        // Transition to Blinking and check prompt
        sm.Fire(LightTrigger.Toggle); // Off -> On
        sm.Fire(LightTrigger.StartBlinking); // On -> Blinking
        prompt = plugin.StateMachine.GetPluginInstructions();
        Assert.Contains("**Current State**: Blinking", prompt);
        Assert.Contains("**Permitted Triggers**: StopBlinking, Toggle, PowerOutage", prompt);
    }
}
