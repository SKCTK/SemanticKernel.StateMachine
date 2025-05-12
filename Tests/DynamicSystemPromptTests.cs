using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.AzureOpenAI;
using SemanticKernel.StateMachine;
using Stateless;
using Xunit;
using Xunit.Abstractions;
using Tests.Common;
using System.Linq;

namespace Tests;

/// <summary>
/// Tests demonstrating how a StateMachine can dynamically update system prompts
/// based on the current state of conversation or application flow.
/// </summary>
public class DynamicSystemPromptTests
{
    private readonly ITestOutputHelper _output;

    public DynamicSystemPromptTests(ITestOutputHelper output)
    {
        _output = output;
    }

    // Define states for conversation context
    public enum ConversationState
    {
        Professional,
        Casual,
        Technical,
        Creative
    }

    // Define triggers to change conversation style
    public enum ConversationTrigger
    {
        BeProfessional,
        BeCasual,
        BeTechnical,
        BeCreative
    }

    /// <summary>
    /// Helper class to manage state-dependent system prompts
    /// </summary>
    private class PromptManager
    {
        private readonly Dictionary<ConversationState, string> _statePrompts = new()
        {
            [ConversationState.Professional] = "You are a professional assistant. Use formal language, be concise and business-oriented in your responses.",
            [ConversationState.Casual] = "You are a friendly, casual assistant. Use informal language, emojis occasionally, and keep things conversational.",
            [ConversationState.Technical] = "You are a technical expert. Provide detailed technical information with code examples when appropriate.",
            [ConversationState.Creative] = "You are a creative writing assistant. Use vivid imagery, metaphors, and expressive language in your responses."
        };

        public string GetPromptForState(ConversationState state)
        {
            return _statePrompts[state];
        }
    }

    /// <summary>
    /// Creates a kernel with a configured state machine for conversation styles
    /// </summary>
    private Kernel CreateKernelWithConversationStateMachine()
    {
        // Create kernel with Azure OpenAI
        var builder = Kernel.CreateBuilder();
        builder.AddAzureOpenAIChatCompletion(
            deploymentName: Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT") ?? Tests.Common.TestOpenAIConfig.DefaultDeployment,
            endpoint: Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") ?? Tests.Common.TestOpenAIConfig.DefaultEndpoint,
            apiKey: Environment.GetEnvironmentVariable("AZURE_OPENAI_KEY") ?? Tests.Common.TestOpenAIConfig.DefaultApiKey
        );

        // Create state machine for conversation styles
        var sm = new StateMachine<ConversationState, ConversationTrigger>(ConversationState.Professional);
        
        // Configure transitions
        sm.Configure(ConversationState.Professional)
            .Permit(ConversationTrigger.BeCasual, ConversationState.Casual)
            .Permit(ConversationTrigger.BeTechnical, ConversationState.Technical)
            .Permit(ConversationTrigger.BeCreative, ConversationState.Creative);

        sm.Configure(ConversationState.Casual)
            .Permit(ConversationTrigger.BeProfessional, ConversationState.Professional)
            .Permit(ConversationTrigger.BeTechnical, ConversationState.Technical)
            .Permit(ConversationTrigger.BeCreative, ConversationState.Creative);

        sm.Configure(ConversationState.Technical)
            .Permit(ConversationTrigger.BeProfessional, ConversationState.Professional)
            .Permit(ConversationTrigger.BeCasual, ConversationState.Casual)
            .Permit(ConversationTrigger.BeCreative, ConversationState.Creative);

        sm.Configure(ConversationState.Creative)
            .Permit(ConversationTrigger.BeProfessional, ConversationState.Professional)
            .Permit(ConversationTrigger.BeCasual, ConversationState.Casual)
            .Permit(ConversationTrigger.BeTechnical, ConversationState.Technical);

        // Add the state machine to kernel
        builder.AddStateMachine(sm, "ConversationStyle");
        
        return builder.Build();
    }

    [Fact]
    public async Task StateMachine_DynamicallyChangesSystemPrompt()
    {
        // Arrange
        var kernel = CreateKernelWithConversationStateMachine();
        var promptManager = new PromptManager();
        var chat = kernel.GetRequiredService<IChatCompletionService>();
        var settings = new AzureOpenAIPromptExecutionSettings
        {
            FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
        };

        // Initial state is Professional
        var currentState = await kernel.GetCurrentStateAsync("ConversationStyle");
        Assert.Equal("Professional", currentState);
        
        // Create initial chat history with professional system prompt
        var history = new ChatHistory();
        history.AddSystemMessage(promptManager.GetPromptForState(ConversationState.Professional));
        history.AddUserMessage("Tell me about project management.");
        
        // Act & Assert - Professional response
        var professionalResponse = await chat.GetChatMessageContentAsync(history, settings, kernel);
        _output.WriteLine($"Professional response: {professionalResponse.Content}");
        Assert.DoesNotContain("😊", professionalResponse.Content);
        Assert.DoesNotContain("👋", professionalResponse.Content);
        
        // Change state to Casual
        await kernel.TryFireTriggerAsync("BeCasual", "ConversationStyle");
        currentState = await kernel.GetCurrentStateAsync("ConversationStyle");
        Assert.Equal("Casual", currentState);
        
        // Create new history with casual system prompt
        history = new ChatHistory();
        history.AddSystemMessage(promptManager.GetPromptForState(ConversationState.Casual));
        history.AddUserMessage("Tell me about project management.");
        
        // Act & Assert - Casual response should be different
        var casualResponse = await chat.GetChatMessageContentAsync(history, settings, kernel);
        _output.WriteLine($"Casual response: {casualResponse.Content}");
        
        Assert.NotEqual(professionalResponse.Content, casualResponse.Content);
    }

    [Fact]
    public async Task DynamicPromptManager_TracksStateMachine()
    {
        // Arrange
        var kernel = CreateKernelWithConversationStateMachine();
        kernel.FunctionInvocationFilters.Add(new FunctionTrackingFilter(_output));
        var promptManager = new PromptManager();
        var chat = kernel.GetRequiredService<IChatCompletionService>();
        
        // Create a custom chat history manager that updates based on state
        var dynamicHistory = new DynamicSystemPromptChatHistory(kernel, promptManager, "ConversationStyle");
        
        // Act - Test conversation across multiple state changes
        await TestConversationWithStateChange(kernel, chat, dynamicHistory, "Tell me about cloud computing.", ConversationTrigger.BeTechnical);
        await TestConversationWithStateChange(kernel, chat, dynamicHistory, "Can you suggest a team activity?", ConversationTrigger.BeCasual);
        await TestConversationWithStateChange(kernel, chat, dynamicHistory, "Write a short story about a robot.", ConversationTrigger.BeCreative);
        await TestConversationWithStateChange(kernel, chat, dynamicHistory, "Provide a business proposal outline.", ConversationTrigger.BeProfessional);
    }

    private async Task TestConversationWithStateChange(Kernel kernel, IChatCompletionService chat, 
        DynamicSystemPromptChatHistory history, string userMessage, ConversationTrigger nextTrigger)
    {
        // Get the current state before adding message
        var currentState = await kernel.GetCurrentStateAsync("ConversationStyle");
        _output.WriteLine($"Current state: {currentState}");
        
        // Add user message and get response with current system prompt
        history.AddUserMessage(userMessage);
        var response = await chat.GetChatMessageContentAsync(
            history.GetCurrentHistory(), 
            new AzureOpenAIPromptExecutionSettings { FunctionChoiceBehavior = FunctionChoiceBehavior.Auto() }, 
            kernel);
        
        _output.WriteLine($"User ({currentState}): {userMessage}");
        _output.WriteLine($"Assistant ({currentState}): {response.Content}");
        history.AddAssistantMessage(response.Content);
        
        // Transition to next state
        await kernel.TryFireTriggerAsync(nextTrigger.ToString(), "ConversationStyle");
        var newState = await kernel.GetCurrentStateAsync("ConversationStyle");
        _output.WriteLine($"Transitioned from {currentState} to {newState}");
        
        // Update system prompt based on new state
        history.UpdateSystemPromptForCurrentState();
    }

    /// <summary>
    /// Helper class that manages chat history with state-based system prompts
    /// </summary>
    private class DynamicSystemPromptChatHistory
    {
        private readonly Kernel _kernel;
        private readonly PromptManager _promptManager;
        private readonly string _pluginName;
        private readonly ChatHistory _history = new();

        public DynamicSystemPromptChatHistory(Kernel kernel, PromptManager promptManager, string pluginName)
        {
            _kernel = kernel;
            _promptManager = promptManager;
            _pluginName = pluginName;
            
            // Initialize with current state's system prompt
            UpdateSystemPromptForCurrentState().GetAwaiter().GetResult();
        }

        public async Task UpdateSystemPromptForCurrentState()
        {
            var stateStr = await _kernel.GetCurrentStateAsync(_pluginName);
            if (Enum.TryParse<ConversationState>(stateStr, out var state))
            {
                // Remove any existing system messages
                var existingSystemMessages = _history.Where(m => m.Role.ToString().Equals("system", StringComparison.OrdinalIgnoreCase)).ToList();
                foreach (var msg in existingSystemMessages)
                {
                    _history.Remove(msg);
                }
                
                // Add new system message based on current state
                _history.Insert(0, new ChatMessageContent(
                    AuthorRole.System,
                    _promptManager.GetPromptForState(state)
                ));
            }
        }

        public ChatHistory GetCurrentHistory() => _history;

        public void AddUserMessage(string message) => _history.AddUserMessage(message);
        
        public void AddAssistantMessage(string message) => _history.AddAssistantMessage(message);
    }

    [Fact]
    public async Task MultistepConversation_MaintainsCoherenceAcrossStateChanges()
    {
        // Arrange
        var kernel = CreateKernelWithConversationStateMachine();
        var promptManager = new PromptManager();
        var chat = kernel.GetRequiredService<IChatCompletionService>();
        var settings = new AzureOpenAIPromptExecutionSettings { FunctionChoiceBehavior = FunctionChoiceBehavior.Auto() };
        
        var history = new ChatHistory();
        
        // Start with professional state
        history.AddSystemMessage(promptManager.GetPromptForState(ConversationState.Professional));
        history.AddUserMessage("Let's discuss a software project plan.");
        
        var response = await chat.GetChatMessageContentAsync(history, settings, kernel);
        _output.WriteLine($"Professional: {response.Content}");
        history.AddAssistantMessage(response.Content);
        
        // Change to technical state
        await kernel.TryFireTriggerAsync("BeTechnical", "ConversationStyle");
        
        // Replace system message with technical prompt
        history.RemoveAt(0); // Remove old system message
        history.Insert(0, new ChatMessageContent(
            AuthorRole.System, 
            promptManager.GetPromptForState(ConversationState.Technical)
        ));
        
        // Continue conversation with technical context
        history.AddUserMessage("What technologies would you recommend for this project?");
        response = await chat.GetChatMessageContentAsync(history, settings, kernel);
        _output.WriteLine($"Technical: {response.Content}");
        history.AddAssistantMessage(response.Content);
        
        // Change to casual state
        await kernel.TryFireTriggerAsync("BeCasual", "ConversationStyle");
        
        // Replace system message with casual prompt
        history.RemoveAt(0); // Remove old system message
        history.Insert(0, new ChatMessageContent(
            AuthorRole.System, 
            promptManager.GetPromptForState(ConversationState.Casual)
        ));
        
        // Continue conversation with casual context
        history.AddUserMessage("Thanks! Now tell me how I should explain this to my team.");
        response = await chat.GetChatMessageContentAsync(history, settings, kernel);
        _output.WriteLine($"Casual: {response.Content}");
        
        // Test that conversation maintains coherence despite style changes
        Assert.NotNull(response);
        Assert.True(
            response.Content.Contains("team", StringComparison.OrdinalIgnoreCase) ||
            response.Content.Contains("explain", StringComparison.OrdinalIgnoreCase) ||
            response.Content.Contains("project", StringComparison.OrdinalIgnoreCase)
        );
    }
}
