using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.AzureOpenAI;
using SemanticKernel.StateMachine;
using Stateless;
using Xunit;
using Xunit.Abstractions;
using Tests.Common;

namespace Tests;

public class DynamicSystemPromptManagerTests
{
    private readonly ITestOutputHelper _output;

    public DynamicSystemPromptManagerTests(ITestOutputHelper output)
    {
        _output = output;
    }

    // Define a workflow with distinct personality states for an assistant
    public enum AssistantPersonality
    {
        Professional,
        Friendly,
        Technical,
        Creative
    }

    public enum PersonalityTrigger
    {
        BeProfessional,
        BeFriendly,
        BeTechnical,
        BeCreative
    }

    /// <summary>
    /// Class that manages system prompts based on personality state
    /// </summary>
    private class PersonalityPromptManager
    {
        private readonly Kernel _kernel;
        private readonly StateMachine<AssistantPersonality, PersonalityTrigger> _stateMachine;
        
        // Dictionary of personality-specific system prompts
        private readonly Dictionary<AssistantPersonality, string> _personalityPrompts = new()
        {
            [AssistantPersonality.Professional] = "You are a professional assistant. Use formal language and focus on being clear, accurate, and business-oriented.",
            [AssistantPersonality.Friendly] = "You are a friendly, casual assistant. Use warm, conversational language with a positive tone. Feel free to use appropriate emojis occasionally.",
            [AssistantPersonality.Technical] = "You are a technical expert assistant. Provide detailed technical information, reference relevant concepts, and include code examples when appropriate.",
            [AssistantPersonality.Creative] = "You are a creative assistant. Use imaginative language, metaphors, and think outside the box. Be expressive and inspiring in your responses."
        };
        
        public ChatHistory CurrentChatHistory { get; private set; } = new();
        
        public PersonalityPromptManager(Kernel kernel, StateMachine<AssistantPersonality, PersonalityTrigger> stateMachine)
        {
            _kernel = kernel;
            _stateMachine = stateMachine;
            
            // Wire up state entry actions
            _stateMachine.Configure(AssistantPersonality.Professional)
                .OnEntry(t => UpdateSystemPrompt(AssistantPersonality.Professional, t));
                
            _stateMachine.Configure(AssistantPersonality.Friendly)
                .OnEntry(t => UpdateSystemPrompt(AssistantPersonality.Friendly, t));
                
            _stateMachine.Configure(AssistantPersonality.Technical)
                .OnEntry(t => UpdateSystemPrompt(AssistantPersonality.Technical, t));
                
            _stateMachine.Configure(AssistantPersonality.Creative)
                .OnEntry(t => UpdateSystemPrompt(AssistantPersonality.Creative, t));
                
            // Initialize with current state
            UpdateSystemPrompt(_stateMachine.State, null);
        }
        
        private void UpdateSystemPrompt(AssistantPersonality personality, StateMachine<AssistantPersonality, PersonalityTrigger>.Transition transition)
        {
            // Get the new system prompt
            string newSystemPrompt = _personalityPrompts[personality];
            
            if (transition != null)
            {
                // Create a new chat history with the system prompt
                var newHistory = new ChatHistory();
                newHistory.AddSystemMessage(newSystemPrompt);
                
                // Add notification about personality transition
                string transitionMessage = $"Note: My personality is changing from {transition.Source} to {transition.Destination} mode.";
                newHistory.AddSystemMessage(transitionMessage);
                
                // Transfer recent user-assistant exchanges to maintain context
                var recentMessages = CurrentChatHistory
                    .Where(m => m.Role.ToString() == "user" || m.Role.ToString() == "assistant")
                    .TakeLast(4)  // Last 2 exchanges
                    .ToList();
                    
                foreach (var message in recentMessages)
                {
                    newHistory.Add(message);
                }
                
                CurrentChatHistory = newHistory;
            }
            else
            {
                // Initial state - just create a new history with system prompt
                CurrentChatHistory = new ChatHistory();
                CurrentChatHistory.AddSystemMessage(newSystemPrompt);
            }
        }
        
        // Add user message to current chat history
        public void AddUserMessage(string message)
        {
            CurrentChatHistory.AddUserMessage(message);
        }
        
        // Add assistant message to current chat history
        public void AddAssistantMessage(string message)
        {
            CurrentChatHistory.AddAssistantMessage(message);
        }
        
        // Get the current system prompt
        public string GetCurrentSystemPrompt()
        {
            return CurrentChatHistory
                .FirstOrDefault(m => m.Role.ToString().Equals("system", StringComparison.OrdinalIgnoreCase))
                ?.Content ?? string.Empty;
        }
    }

    private Kernel CreateKernelWithPersonalityStateMachine()
    {
        // Create kernel
        var builder = Kernel.CreateBuilder();
        builder.AddAzureOpenAIChatCompletion(
            deploymentName: Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT") ?? Tests.Common.TestOpenAIConfig.DefaultDeployment,
            endpoint: Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") ?? Tests.Common.TestOpenAIConfig.DefaultEndpoint,
            apiKey: Environment.GetEnvironmentVariable("AZURE_OPENAI_KEY") ?? Tests.Common.TestOpenAIConfig.DefaultApiKey
        );
        
        // Create state machine for assistant personalities - use the extracted method
        var stateMachine = CreateConfiguredPersonalityStateMachine();
        
        // Add state machine to kernel
        builder.AddStateMachine(stateMachine, "PersonalityMode");
        
        return builder.Build();
    }

    /// <summary>
    /// Creates a properly configured state machine for personality transitions
    /// </summary>
    private StateMachine<AssistantPersonality, PersonalityTrigger> CreateConfiguredPersonalityStateMachine(
        AssistantPersonality initialState = AssistantPersonality.Professional)
    {
        // Create state machine for assistant personalities
        var stateMachine = new StateMachine<AssistantPersonality, PersonalityTrigger>(initialState);
        
        // Configure state transitions
        stateMachine.Configure(AssistantPersonality.Professional)
            .Permit(PersonalityTrigger.BeFriendly, AssistantPersonality.Friendly)
            .Permit(PersonalityTrigger.BeTechnical, AssistantPersonality.Technical)
            .Permit(PersonalityTrigger.BeCreative, AssistantPersonality.Creative);
            
        stateMachine.Configure(AssistantPersonality.Friendly)
            .Permit(PersonalityTrigger.BeProfessional, AssistantPersonality.Professional)
            .Permit(PersonalityTrigger.BeTechnical, AssistantPersonality.Technical)
            .Permit(PersonalityTrigger.BeCreative, AssistantPersonality.Creative);
            
        stateMachine.Configure(AssistantPersonality.Technical)
            .Permit(PersonalityTrigger.BeProfessional, AssistantPersonality.Professional)
            .Permit(PersonalityTrigger.BeFriendly, AssistantPersonality.Friendly)
            .Permit(PersonalityTrigger.BeCreative, AssistantPersonality.Creative);
            
        stateMachine.Configure(AssistantPersonality.Creative)
            .Permit(PersonalityTrigger.BeProfessional, AssistantPersonality.Professional)
            .Permit(PersonalityTrigger.BeFriendly, AssistantPersonality.Friendly)
            .Permit(PersonalityTrigger.BeTechnical, AssistantPersonality.Technical);
            
        return stateMachine;
    }

    [Fact]
    public async Task PersonalityTransitions_ChangeChatResponseStyle()
    {
        // Arrange
        var kernel = CreateKernelWithPersonalityStateMachine();
        // Create a properly configured state machine
        var stateMachine = CreateConfiguredPersonalityStateMachine();
        
        var personalityManager = new PersonalityPromptManager(kernel, stateMachine);
        var chat = kernel.GetRequiredService<IChatCompletionService>();
        var settings = new AzureOpenAIPromptExecutionSettings();
        
        // Get initial system prompt (Professional)
        var initialPrompt = personalityManager.GetCurrentSystemPrompt();
        _output.WriteLine($"Initial Prompt (Professional): {initialPrompt}");
        Assert.Contains("professional assistant", initialPrompt.ToLower());
        
        // Test with a consistent query across different personalities
        const string testQuery = "Tell me about cloud computing";
        
        // Get Professional response
        personalityManager.AddUserMessage(testQuery);
        var professionalResponse = await chat.GetChatMessageContentAsync(personalityManager.CurrentChatHistory, settings, kernel);
        _output.WriteLine($"Professional Response: {professionalResponse.Content}");
        personalityManager.AddAssistantMessage(professionalResponse.Content);
        
        // Change to Technical personality
        stateMachine.Fire(PersonalityTrigger.BeTechnical);
        
        // Verify system prompt changed
        var technicalPrompt = personalityManager.GetCurrentSystemPrompt();
        _output.WriteLine($"Technical Prompt: {technicalPrompt}");
        Assert.Contains("technical expert", technicalPrompt.ToLower());
        
        // Get Technical response to the same query
        personalityManager.AddUserMessage(testQuery);
        var technicalResponse = await chat.GetChatMessageContentAsync(personalityManager.CurrentChatHistory, settings, kernel);
        _output.WriteLine($"Technical Response: {technicalResponse.Content}");
        personalityManager.AddAssistantMessage(technicalResponse.Content);
        
        // Change to Friendly personality
        stateMachine.Fire(PersonalityTrigger.BeFriendly);
        
        // Verify system prompt changed
        var friendlyPrompt = personalityManager.GetCurrentSystemPrompt();
        _output.WriteLine($"Friendly Prompt: {friendlyPrompt}");
        Assert.Contains("friendly", friendlyPrompt.ToLower());
        
        // Get Friendly response to the same query
        personalityManager.AddUserMessage(testQuery);
        var friendlyResponse = await chat.GetChatMessageContentAsync(personalityManager.CurrentChatHistory, settings, kernel);
        _output.WriteLine($"Friendly Response: {friendlyResponse.Content}");
        
        // Verify responses are different due to personality changes
        Assert.NotEqual(professionalResponse.Content, technicalResponse.Content);
        Assert.NotEqual(technicalResponse.Content, friendlyResponse.Content);
        
        // Verify technical response has more technical content
        Assert.True(
            CountTechnicalTerms(technicalResponse.Content) > CountTechnicalTerms(friendlyResponse.Content),
            "Technical response should have more technical terms than friendly response"
        );
    }
    
    private int CountTechnicalTerms(string text)
    {
        // Simple heuristic - count technical terms related to cloud computing
        string[] technicalTerms = new[] { 
            "iaas", "paas", "saas", "virtualization", "infrastructure", "architecture", 
            "instance", "container", "kubernetes", "docker", "microservice", "scalability",
            "deployment", "api", "protocol", "bandwidth", "latency", "server", "virtual machine" 
        };
        
        return technicalTerms.Sum(term => 
            text.ToLower().Split(new[] { ' ', '.', ',', ':', ';', '(', ')', '[', ']', '\n', '\r' })
                .Count(word => word.Equals(term)));
    }

    [Fact]
    public async Task OnEntryAction_PreservesConversationContext()
    {
        // Arrange
        var kernel = CreateKernelWithPersonalityStateMachine();
        var stateMachine = CreateConfiguredPersonalityStateMachine();
        
        var personalityManager = new PersonalityPromptManager(kernel, stateMachine);
        var chat = kernel.GetRequiredService<IChatCompletionService>();
        var settings = new AzureOpenAIPromptExecutionSettings();
        
        // Start a conversation with specific context
        personalityManager.AddUserMessage("My name is Alex and I'm working on a project about renewable energy.");
        var response = await chat.GetChatMessageContentAsync(personalityManager.CurrentChatHistory, settings, kernel);
        _output.WriteLine($"Professional Response: {response.Content}");
        personalityManager.AddAssistantMessage(response.Content);
        
        // Continue the conversation
        personalityManager.AddUserMessage("What are some key technologies I should look into?");
        response = await chat.GetChatMessageContentAsync(personalityManager.CurrentChatHistory, settings, kernel);
        _output.WriteLine($"Professional Response (continued): {response.Content}");
        personalityManager.AddAssistantMessage(response.Content);
        
        // Change to Technical personality
        stateMachine.Fire(PersonalityTrigger.BeTechnical);
        
        // Ask a question that requires remembering the context
        personalityManager.AddUserMessage("Can you explain how these technologies might fit into my project?");
        response = await chat.GetChatMessageContentAsync(personalityManager.CurrentChatHistory, settings, kernel);
        _output.WriteLine($"Technical Response (with context): {response.Content}");
        
        // Verify the response maintains the conversation context
        Assert.Contains("renewable energy", response.Content.ToLower());
        Assert.Contains("project", response.Content.ToLower());
    }
    
    [Fact]
    public async Task StateMachine_SwitchesBetweenConversationModes()
    {
        // Create extended test showing complete conversation flow with multiple state transitions
        var kernel = CreateKernelWithPersonalityStateMachine();
        var stateMachine = CreateConfiguredPersonalityStateMachine();
        
        var promptManager = new PersonalityPromptManager(kernel, stateMachine);
        var chat = kernel.GetRequiredService<IChatCompletionService>();
        var settings = new AzureOpenAIPromptExecutionSettings();
        
        // Start in Professional mode
        _output.WriteLine($"Starting in {stateMachine.State} mode");
        promptManager.AddUserMessage("I need help planning a software project.");
        var response = await chat.GetChatMessageContentAsync(promptManager.CurrentChatHistory, settings, kernel);
        _output.WriteLine($"{stateMachine.State} Response: {response.Content}");
        promptManager.AddAssistantMessage(response.Content);
        
        // Switch to Technical mode for technical details
        stateMachine.Fire(PersonalityTrigger.BeTechnical);
        _output.WriteLine($"Switching to {stateMachine.State} mode");
        promptManager.AddUserMessage("What tech stack would you recommend for this project?");
        response = await chat.GetChatMessageContentAsync(promptManager.CurrentChatHistory, settings, kernel);
        _output.WriteLine($"{stateMachine.State} Response: {response.Content}");
        promptManager.AddAssistantMessage(response.Content);
        
        // Switch to Friendly mode for team considerations
        stateMachine.Fire(PersonalityTrigger.BeFriendly);
        _output.WriteLine($"Switching to {stateMachine.State} mode");
        promptManager.AddUserMessage("How should I motivate my team during this project?");
        response = await chat.GetChatMessageContentAsync(promptManager.CurrentChatHistory, settings, kernel);
        _output.WriteLine($"{stateMachine.State} Response: {response.Content}");
        promptManager.AddAssistantMessage(response.Content);
        
        // Switch to Creative mode for brainstorming
        stateMachine.Fire(PersonalityTrigger.BeCreative);
        _output.WriteLine($"Switching to {stateMachine.State} mode");
        promptManager.AddUserMessage("Help me come up with a catchy name for this project.");
        response = await chat.GetChatMessageContentAsync(promptManager.CurrentChatHistory, settings, kernel);
        _output.WriteLine($"{stateMachine.State} Response: {response.Content}");
        promptManager.AddAssistantMessage(response.Content);
        
        // Back to Professional mode for finalizing
        stateMachine.Fire(PersonalityTrigger.BeProfessional);
        _output.WriteLine($"Switching back to {stateMachine.State} mode");
        promptManager.AddUserMessage("Summarize what we've discussed about the project so far.");
        response = await chat.GetChatMessageContentAsync(promptManager.CurrentChatHistory, settings, kernel);
        _output.WriteLine($"{stateMachine.State} Response: {response.Content}");
        
        // Verify the final summary contains elements from all conversation parts
        string summary = response.Content.ToLower();
        Assert.Contains("project", summary);
        Assert.Contains("tech", summary);
        Assert.Contains("team", summary);
        Assert.Contains("name", summary);
    }

    [Fact]
    public async Task FunctionTracking_StateMachine_ChangeStateViaPlugin()
    {
        // Create a function tracking logger
        var functionCalls = new List<string>();
        void LogFunction(string message) 
        {
            _output.WriteLine(message);
            functionCalls.Add(message);
        }

        // Create kernel with function tracking
        var builder = Kernel.CreateBuilder();
        builder.AddAzureOpenAIChatCompletion(
            deploymentName: Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT") ?? Tests.Common.TestOpenAIConfig.DefaultDeployment,
            endpoint: Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") ?? Tests.Common.TestOpenAIConfig.DefaultEndpoint,
            apiKey: Environment.GetEnvironmentVariable("AZURE_OPENAI_KEY") ?? Tests.Common.TestOpenAIConfig.DefaultApiKey
        );
        
        // Create and configure state machine
        var stateMachine = CreateConfiguredPersonalityStateMachine();
        
        // Add state machine to kernel as a plugin
        builder.AddStateMachine(stateMachine, "PersonalityMode");
        
        // Build the kernel and add function tracking
        var kernel = builder.Build();
        kernel.UseFunctionTracking(LogFunction); // Using the proper method now
        
        // Create personality manager
        var personalityManager = new PersonalityPromptManager(kernel, stateMachine);
        var chat = kernel.GetRequiredService<IChatCompletionService>();
        var settings = new AzureOpenAIPromptExecutionSettings();
        
        // Add initial prompt
        _output.WriteLine($"Starting in {stateMachine.State} mode");
        Assert.Equal(AssistantPersonality.Professional, stateMachine.State);
        
        var initialPrompt = personalityManager.GetCurrentSystemPrompt();
        _output.WriteLine($"Initial prompt: {initialPrompt}");
        Assert.Contains("professional assistant", initialPrompt.ToLower());
        
        // Change state via plugin - this demonstrates how an LLM would transition the state
        var pluginResult = await kernel.FireTriggerAsync("BeTechnical", "PersonalityMode");
        _output.WriteLine($"Plugin result: {pluginResult}");
        
        // Verify state changed and system prompt updated
        Assert.Equal(AssistantPersonality.Technical, stateMachine.State);
        var technicalPrompt = personalityManager.GetCurrentSystemPrompt();
        _output.WriteLine($"Technical prompt: {technicalPrompt}");
        Assert.Contains("technical expert", technicalPrompt.ToLower());
        
        // Get and log the permission instructions that would be included in system prompt for LLM
        var instructions = stateMachine.GetPluginInstructions();
        _output.WriteLine("============ PLUGIN INSTRUCTIONS ============");
        _output.WriteLine(instructions);
        _output.WriteLine("============================================");
        
        // Ask a technical question using new personality
        personalityManager.AddUserMessage("Explain how WebAssembly works at a low level.");
        var response = await chat.GetChatMessageContentAsync(personalityManager.CurrentChatHistory, settings, kernel);
        _output.WriteLine($"Technical Response: {response.Content}");
        
        // Verify function calls were logged
        Assert.Contains(functionCalls, call => call.Contains("FireTrigger"));
        
        // Display the tracked function calls
        _output.WriteLine("Function calls tracked:");
        foreach (var call in functionCalls.Where(c => c.StartsWith("FUNCTION")))
        {
            _output.WriteLine($"  {call}");
        }
    }
}
