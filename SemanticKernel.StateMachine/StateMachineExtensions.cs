using Stateless;
using Stateless.Graph;
using System.Linq;
using System.Collections.Generic;

namespace SemanticKernel.StateMachine;

/// <summary>
/// Extension methods for StateMachine
/// </summary>
public static class StateMachineExtensions
{
    /// <summary>
    /// Gets a system prompt for using this state machine, including available states and triggers.
    /// </summary>
    public static string GetPluginInstructions<TState, TTrigger>(
        this StateMachine<TState, TTrigger> stateMachine,
        bool includeGraph = true)
        where TState : notnull
        where TTrigger : notnull
    {
        var info = stateMachine.GetInfo();
        var permittedTriggers = stateMachine.PermittedTriggers.ToList();
        var availableStates = info.States.Select(s => s.ToString()).ToList();

        string prompt = $"# How to use State Machine plugin: \n" +
                        $"To change states, use function call {nameof(StateMachinePlugin<object, object>.Transition)} \n" +
                        $"To get current state use {nameof(StateMachinePlugin<object, object>.GetCurrentState)} \n" +
                        $"For forcing transitions without checking conditions, use function call {nameof(StateMachinePlugin<object, object>.FireTrigger)} \n" +
                        $"Current State: {stateMachine.State}\n";

        if (includeGraph)
        {
            prompt += "State Machine Graph:\n" +
                      "- Nodes represent the possible states of the machine.\n" +
                      "- Arrows represent transitions (triggers) between states.\n" +
                      "- Text on arrows indicates the trigger name." +
                      $"```mermaid\n{MermaidGraph.Format(info)}\n```";
        }

        return prompt;
    }
}