using Microsoft.SemanticKernel;
using System.Threading.Tasks;
using System;
using Xunit.Abstractions;

namespace Tests.Common;

/// <summary>
/// A function invocation filter that logs details about each function call to the test output.
/// </summary>
public class FunctionTrackingFilter : IFunctionInvocationFilter
{
    private readonly ITestOutputHelper _testOutput;

    public FunctionTrackingFilter(ITestOutputHelper testOutput)
    {
        _testOutput = testOutput;
    }

    public async Task OnFunctionInvocationAsync(FunctionInvocationContext context, Func<FunctionInvocationContext, Task> next)
    {
        // Log before function invocation
        _testOutput.WriteLine($"FUNCTION CALLING: {context.Function.PluginName}.{context.Function.Name}");
        
        if (context.Arguments.Count > 0)
        {
            _testOutput.WriteLine("  Arguments:");
            foreach (var arg in context.Arguments)
            {
                _testOutput.WriteLine($"    {arg.Key}: {arg.Value}");
            }
        }

        // Call the actual function
        await next(context);

        // Log after function invocation
        var resultType = context.Result?.ValueType?.Name ?? "null";
        _testOutput.WriteLine($"FUNCTION COMPLETED: {context.Function.PluginName}.{context.Function.Name} → {resultType}");
        
        // If we have a simple result type, show its value
        if (context.Result?.ValueType != null && 
            (context.Result.ValueType == typeof(string) || 
             context.Result.ValueType.IsPrimitive))
        {
            _testOutput.WriteLine($"  Result: {context.Result.GetValue<object>()}");
        }
        
        _testOutput.WriteLine(string.Empty); // Add an empty line for readability
    }
}