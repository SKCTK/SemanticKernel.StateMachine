using Microsoft.SemanticKernel;
using System;
using System.Threading.Tasks;

namespace Tests.Common;

/// <summary>
/// Function invocation filter that logs function calls for testing purposes
/// </summary>
public class LoggingFilter : IFunctionInvocationFilter
{
    private readonly Action<string> _logger;

    public LoggingFilter(Action<string> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
    
    public async Task OnFunctionInvocationAsync(FunctionInvocationContext context, Func<FunctionInvocationContext, Task> next)
    {
        // Log before function invocation
        var beforeMessage = $"FUNCTION CALLING: {context.Function.PluginName}.{context.Function.Name}";
        if (context.Arguments.Count > 0)
        {
            beforeMessage += "\n  Arguments:";
            foreach (var arg in context.Arguments)
            {
                beforeMessage += $"\n    {arg.Key}: {arg.Value}";
            }
        }
        _logger(beforeMessage);

        // Call the actual function
        await next(context);

        // Log after function invocation
        var resultType = context.Result?.ValueType?.Name ?? "null";
        var afterMessage = $"FUNCTION COMPLETED: {context.Function.PluginName}.{context.Function.Name} → {resultType}";
        
        // If we have a simple result type, show its value
        if (context.Result?.ValueType != null && 
            (context.Result.ValueType == typeof(string) || 
             context.Result.ValueType.IsPrimitive))
        {
            afterMessage += $"\n  Result: {context.Result.GetValue<object>()}";
        }
        
        _logger(afterMessage);
        _logger(string.Empty); // Add an empty line for readability
    }
}

/// <summary>
/// Extension methods for Kernel to assist with testing
/// </summary>
public static class TestKernelExtensions
{
    /// <summary>
    /// Adds a LoggingFilter to the kernel to log function calls during tests
    /// </summary>
    /// <param name="kernel">The kernel to add the filter to</param>
    /// <param name="loggingAction">The action to log function calls</param>
    /// <returns>The kernel for chaining</returns>
    public static Kernel UseFunctionTracking(
        this Kernel kernel,
        Action<string> loggingAction)
    {
        ArgumentNullException.ThrowIfNull(kernel);
        ArgumentNullException.ThrowIfNull(loggingAction);
        
        kernel.FunctionInvocationFilters.Add(new LoggingFilter(loggingAction));
        
        return kernel;
    }
    
}
