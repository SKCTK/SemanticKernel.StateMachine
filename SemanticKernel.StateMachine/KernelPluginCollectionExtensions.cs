using Microsoft.SemanticKernel;
using Stateless;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace SemanticKernel.StateMachine;

/// <summary>
/// Extension methods for KernelPluginCollection to work with StateMachine plugins
/// </summary>
public static class KernelPluginCollectionExtensions
{
    /// <summary>
    /// Default plugin name constant
    /// </summary>
    public const string DefaultPluginName = "StateMachinePlugin";
    
    /// <summary>
    /// Gets the StateMachine plugin instance from the kernel
    /// </summary>
    /// <param name="pluginCollection">The plugin collection to get the plugin from</param>
    /// <param name="pluginName">Optional custom name of the plugin to retrieve. If not specified, the default name will be used.</param>
    /// <returns>The plugin instance</returns>
    public static KernelPlugin GetStateMachinePlugin(
        this KernelPluginCollection pluginCollection, 
        string pluginName = DefaultPluginName)
    {
        ArgumentNullException.ThrowIfNull(pluginCollection);
        
        if (string.IsNullOrWhiteSpace(pluginName)) throw new ArgumentException("Plugin name cannot be null or empty", nameof(pluginName));
        
        if (!pluginCollection.TryGetPlugin(pluginName, out var pluginInstance)) 
        {
            throw new InvalidOperationException($"StateMachine plugin with name '{pluginName}' not found. Add it first with UseStateMachine<TState, TTrigger>()");
        }
        
        return pluginInstance;
    }

    /// <summary>
    /// Removes a plugin from the plugin collection by name.
    /// </summary>
    /// <param name="pluginCollection">The plugin collection.</param>
    /// <param name="pluginName">The name of the plugin to remove.</param>
    /// <returns>True if the plugin was found and removed; otherwise, false.</returns>
    public static bool Remove(this KernelPluginCollection pluginCollection, string pluginName)
    {
        ArgumentNullException.ThrowIfNull(pluginCollection);
        if (string.IsNullOrWhiteSpace(pluginName)) throw new ArgumentException("Plugin name cannot be null or empty", nameof(pluginName));
        var plugin = pluginCollection.FirstOrDefault(p => p.Name == pluginName);
        if (plugin != null)
        {
            return pluginCollection.Remove(plugin);
        }
        return false;
    }
    
    /// <summary>
    /// Adds a StateMachinePlugin for the specified state machine to the kernel
    /// </summary>
    /// <typeparam name="TState">The state type</typeparam>
    /// <typeparam name="TTrigger">The trigger type</typeparam>
    /// <param name="kernel">The kernel to add the plugin to</param>
    /// <param name="stateMachine">The state machine instance</param>
    /// <param name="pluginName">Optional custom name for the plugin. If not specified, the default name will be used.</param>
    /// <returns>The kernel for chaining</returns>
    public static Kernel AddStateMachine<TState, TTrigger>(
        this Kernel kernel,
        StateMachine<TState, TTrigger> stateMachine,
        string pluginName = DefaultPluginName)
        where TState : notnull
        where TTrigger : notnull
    {
        ArgumentNullException.ThrowIfNull(kernel);
        ArgumentNullException.ThrowIfNull(stateMachine);
        
        if (string.IsNullOrWhiteSpace(pluginName)) throw new ArgumentException("Plugin name cannot be null or empty", nameof(pluginName));
        
        var plugin = new StateMachinePlugin<TState, TTrigger>(stateMachine);
        kernel.Plugins.AddFromObject(plugin, pluginName);
        return kernel;
    }
    
    /// <summary>
    /// Gets the current state of the state machine
    /// </summary>
    /// <param name="kernel">The kernel containing the state machine plugin</param>
    /// <param name="pluginName">Optional custom name of the state machine plugin. If not specified, the default name will be used.</param>
    /// <returns>The current state as a string</returns>
    public static async Task<string> GetCurrentStateAsync(
        this Kernel kernel,
        string pluginName = DefaultPluginName)
    {
        ArgumentNullException.ThrowIfNull(kernel);
        
        if (string.IsNullOrWhiteSpace(pluginName)) throw new ArgumentException("Plugin name cannot be null or empty", nameof(pluginName));

        var result = await kernel.InvokeAsync(pluginName, nameof(StateMachinePlugin<object, object>.GetCurrentState));
        return result.GetValue<string>() ?? string.Empty;
    }

    /// <summary>
    /// Attempts to fire a trigger on the state machine
    /// </summary>
    /// <param name="kernel">The kernel containing the state machine plugin</param>
    /// <param name="triggerName">The name of the trigger to fire</param>
    /// <param name="pluginName">Optional custom name of the state machine plugin. If not specified, the default name will be used.</param>
    /// <returns>The result of the transition attempt as a string</returns>
    public static async Task<string> TryFireTriggerAsync(
        this Kernel kernel,
        string triggerName,
        string pluginName = DefaultPluginName)
    {
        ArgumentNullException.ThrowIfNull(kernel);
        
        if (string.IsNullOrWhiteSpace(pluginName)) throw new ArgumentException("Plugin name cannot be null or empty", nameof(pluginName));
        if (string.IsNullOrWhiteSpace(triggerName)) throw new ArgumentException("Trigger name cannot be null or empty", nameof(triggerName));

        var result = await kernel.InvokeAsync(pluginName, nameof(StateMachinePlugin<object, object>.Transition), new() { { "triggerName", triggerName } });
        return result.GetValue<string>() ?? string.Empty;
    }
    
    /// <summary>
    /// Attempts to directly fire a trigger on the state machine without checking if it's valid
    /// </summary>
    /// <param name="kernel">The kernel containing the state machine plugin</param>
    /// <param name="triggerName">The name of the trigger to fire</param>
    /// <param name="pluginName">Optional custom name of the state machine plugin. If not specified, the default name will be used.</param>
    /// <returns>The result of the fire attempt as a string</returns>
    public static async Task<string> FireTriggerAsync(
        this Kernel kernel,
        string triggerName,
        string pluginName = DefaultPluginName)
    {
        ArgumentNullException.ThrowIfNull(kernel);
        
        if (string.IsNullOrWhiteSpace(pluginName)) throw new ArgumentException("Plugin name cannot be null or empty", nameof(pluginName));
        if (string.IsNullOrWhiteSpace(triggerName)) throw new ArgumentException("Trigger name cannot be null or empty", nameof(triggerName));

        var result = await kernel.InvokeAsync(pluginName, nameof(StateMachinePlugin<object, object>.FireTrigger), new() { { "triggerName", triggerName } });
        return result.GetValue<string>() ?? string.Empty;
    }

    /// <summary>
    /// Gets the machine documentation for the state machine
    /// </summary>
    /// <param name="kernel">The kernel containing the state machine plugin</param>
    /// <param name="pluginName">Optional custom name of the state machine plugin. If not specified, the default name will be used.</param>
    /// <returns>The documentation as a string</returns>
    public static async Task<string> GetStateMachineDocumentation(
        this Kernel kernel,
        string pluginName = DefaultPluginName)
    {
        ArgumentNullException.ThrowIfNull(kernel);
        
        if (string.IsNullOrWhiteSpace(pluginName)) throw new ArgumentException("Plugin name cannot be null or empty", nameof(pluginName));

        var result = await kernel.InvokeAsync(pluginName, nameof(StateMachinePlugin<object, object>.GetStateMachineDocumentation));
        return result.GetValue<string>() ?? string.Empty;
    }
}
