using Microsoft.SemanticKernel;
using Stateless;
using System;

namespace SemanticKernel.StateMachine;

/// <summary>
/// Extension methods for working with StateMachine in Semantic Kernel
/// </summary>
public static class KernelExtensions
{
    /// <summary>
    /// Adds a StateMachinePlugin for the specified state machine to the kernel builder
    /// </summary>
    /// <typeparam name="TState">The state type</typeparam>
    /// <typeparam name="TTrigger">The trigger type</typeparam>
    /// <param name="builder">The kernel builder</param>
    /// <param name="stateMachine">The state machine instance</param>
    /// <param name="pluginName">Optional custom name for the plugin. If not specified, the default name will be used.</param>
    /// <returns>The kernel builder for chaining</returns>
    public static IKernelBuilder AddStateMachine<TState, TTrigger>(
        this IKernelBuilder builder,
        StateMachine<TState, TTrigger> stateMachine,
        string pluginName = KernelPluginCollectionExtensions.DefaultPluginName)
        where TState : notnull
        where TTrigger : notnull
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(stateMachine);
        
        if (string.IsNullOrWhiteSpace(pluginName)) throw new ArgumentException("Plugin name cannot be null or empty", nameof(pluginName));
        
        var plugin = new StateMachinePlugin<TState, TTrigger>(stateMachine);
        builder.Plugins.AddFromObject(plugin, pluginName);
        return builder;
    }
}
