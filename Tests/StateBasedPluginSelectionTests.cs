using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.ComponentModel;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.AzureOpenAI;
using SemanticKernel.StateMachine;
using Stateless;
using Xunit;
using Xunit.Abstractions;
using Tests.Common;

namespace Tests;

/// <summary>
/// Tests that demonstrate how the state machine can dynamically enable/disable
/// or switch between sets of plugins based on state
/// </summary>
public class StateBasedPluginSelectionTests
{
    private readonly ITestOutputHelper _output;

    public StateBasedPluginSelectionTests(ITestOutputHelper output)
    {
        _output = output;
    }

    // Define states for an application flow
    public enum AppState
    {
        Authentication,
        DataEntry,
        Analysis,
        Reporting
    }

    // Define triggers for state transitions
    public enum AppTrigger
    {
        Authenticate,
        StartDataEntry,
        AnalyzeData,
        GenerateReport,
        Logout
    }

    /// <summary>
    /// Simple plugin to simulate authentication
    /// </summary>
    public class AuthPlugin
    {
        private bool _isAuthenticated;
        
        [KernelFunction, Description("Authenticate a user")]
        public string Login(
            [Description("The username to login with")] string username,
            [Description("The password for authentication")] string password)
        {
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                return "Error: Username and password are required";
            }
            
            // Simple authentication (just for testing)
            if (username == "testuser" && password == "password")
            {
                _isAuthenticated = true;
                return "Authentication successful";
            }
            
            return "Authentication failed: Invalid credentials";
        }
        
        [KernelFunction, Description("Check if user is authenticated")]
        public string IsAuthenticated()
        {
            return _isAuthenticated ? "User is authenticated" : "User is not authenticated";
        }
    }
    
    /// <summary>
    /// Simple plugin to simulate data entry operations
    /// </summary>
    public class DataEntryPlugin
    {
        private readonly Dictionary<string, string> _dataStore = new();
        
        [KernelFunction, Description("Store data in the system")]
        public string StoreData(
            [Description("The key for the data")] string key,
            [Description("The value to store")] string value)
        {
            if (string.IsNullOrEmpty(key))
            {
                return "Error: Key cannot be empty";
            }
            
            _dataStore[key] = value;
            return $"Data stored successfully with key: {key}";
        }
        
        [KernelFunction, Description("Get data from the system")]
        public string GetData(
            [Description("The key for the data to retrieve")] string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return "Error: Key cannot be empty";
            }
            
            if (_dataStore.TryGetValue(key, out var value))
            {
                return $"Retrieved data: {value}";
            }
            
            return $"No data found for key: {key}";
        }
    }
    
    /// <summary>
    /// Simple plugin to simulate analysis operations
    /// </summary>
    public class AnalysisPlugin
    {
        [KernelFunction, Description("Analyze the provided data")]
        public string AnalyzeData(
            [Description("The data to analyze")] string data)
        {
            if (string.IsNullOrEmpty(data))
            {
                return "Error: No data provided for analysis";
            }
            
            // Simple analysis for test purposes
            int wordCount = data.Split(new[] { ' ', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length;
            return $"Analysis complete. Word count: {wordCount}";
        }
    }

    /// <summary>
    /// Class that manages plugin availability based on state
    /// </summary>
    private class StateBasedPluginManager
    {
        private readonly Kernel _kernel;
        private readonly StateMachine<AppState, AppTrigger> _stateMachine;
        
        private readonly AuthPlugin _authPlugin = new();
        private readonly DataEntryPlugin _dataEntryPlugin = new();
        private readonly AnalysisPlugin _analysisPlugin = new();
        
        public StateBasedPluginManager(Kernel kernel, StateMachine<AppState, AppTrigger> stateMachine)
        {
            _kernel = kernel;
            _stateMachine = stateMachine;
            
            // Wire up state entry actions to update available plugins
            _stateMachine.Configure(AppState.Authentication)
                .OnEntry(UpdatePluginsForAuthentication);
                
            _stateMachine.Configure(AppState.DataEntry)
                .OnEntry(UpdatePluginsForDataEntry);
                
            _stateMachine.Configure(AppState.Analysis)
                .OnEntry(UpdatePluginsForAnalysis);
                
            _stateMachine.Configure(AppState.Reporting)
                .OnEntry(UpdatePluginsForReporting);
        }
        
        private void UpdatePluginsForAuthentication(StateMachine<AppState, AppTrigger>.Transition transition)
        {
            // Remove all existing plugins
            RemoveAllCustomPlugins();
            
            // Add only authentication plugin
            _kernel.Plugins.AddFromObject(_authPlugin, "Auth");
        }
        
        private void UpdatePluginsForDataEntry(StateMachine<AppState, AppTrigger>.Transition transition)
        {
            RemoveAllCustomPlugins();
            _kernel.Plugins.AddFromObject(_dataEntryPlugin, "DataEntry");
        }
        
        private void UpdatePluginsForAnalysis(StateMachine<AppState, AppTrigger>.Transition transition)
        {
            RemoveAllCustomPlugins();
            _kernel.Plugins.AddFromObject(_analysisPlugin, "Analysis");
        }
        
        private void UpdatePluginsForReporting(StateMachine<AppState, AppTrigger>.Transition transition)
        {
            RemoveAllCustomPlugins();
            // In a real app, we'd add reporting plugins here
        }
        
        private void RemoveAllCustomPlugins()
        {
            // This is a simplified approach - in a real app you might want to track which plugins
            // were added and remove only those, or use a more sophisticated approach
            try { _kernel.Plugins.FirstOrDefault(w=>w.Name == "Auth"); } catch { /* Ignore if not found */ }
            try { _kernel.Plugins.Remove("DataEntry"); } catch { /* Ignore if not found */ }
            try { _kernel.Plugins.Remove("Analysis"); } catch { /* Ignore if not found */ }
            try { _kernel.Plugins.Remove("Reporting"); } catch { /* Ignore if not found */ }
        }
    }

    private Kernel CreateKernelWithAppStateMachine()
    {
        // Create kernel
        var builder = Kernel.CreateBuilder();
        builder.AddAzureOpenAIChatCompletion(
            deploymentName: Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT") ?? Tests.Common.TestOpenAIConfig.DefaultDeployment,
            endpoint: Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") ?? Tests.Common.TestOpenAIConfig.DefaultEndpoint,
            apiKey: Environment.GetEnvironmentVariable("AZURE_OPENAI_KEY") ?? Tests.Common.TestOpenAIConfig.DefaultApiKey
        );
        
        // Create state machine
        var stateMachine = new StateMachine<AppState, AppTrigger>(AppState.Authentication);
        
        // Configure state transitions
        stateMachine.Configure(AppState.Authentication)
            .Permit(AppTrigger.Authenticate, AppState.DataEntry);
            
        stateMachine.Configure(AppState.DataEntry)
            .Permit(AppTrigger.AnalyzeData, AppState.Analysis)
            .Permit(AppTrigger.Logout, AppState.Authentication);
            
        stateMachine.Configure(AppState.Analysis)
            .Permit(AppTrigger.GenerateReport, AppState.Reporting)
            .Permit(AppTrigger.StartDataEntry, AppState.DataEntry)
            .Permit(AppTrigger.Logout, AppState.Authentication);
            
        stateMachine.Configure(AppState.Reporting)
            .Permit(AppTrigger.AnalyzeData, AppState.Analysis)
            .Permit(AppTrigger.StartDataEntry, AppState.DataEntry)
            .Permit(AppTrigger.Logout, AppState.Authentication);
            
        // Add state machine to kernel
        builder.AddStateMachine(stateMachine, "AppFlow");
        
        return builder.Build();
    }

    [Fact]
    public async Task StateMachine_DynamicallyChangesAvailablePlugins()
    {
        // Arrange
        var kernel = CreateKernelWithAppStateMachine();
        var stateMachine = new StateMachine<AppState, AppTrigger>(AppState.Authentication);
        var pluginManager = new StateBasedPluginManager(kernel, stateMachine);
        
        // Initialize with Authentication state
        pluginManager = new StateBasedPluginManager(kernel, stateMachine);
        
        // Verify Auth plugin is available in Authentication state
        var plugins = kernel.Plugins;
        Assert.True(plugins.TryGetPlugin("Auth", out var _), "Auth plugin should be available in Authentication state");
        Assert.False(plugins.TryGetPlugin("DataEntry", out var _), "DataEntry plugin should not be available yet");
        
        // Transition to DataEntry state
        stateMachine.Fire(AppTrigger.Authenticate);
        
        // Verify DataEntry plugin is now available and Auth is gone
        Assert.True(plugins.TryGetPlugin("DataEntry", out var _), "DataEntry plugin should be available after transition");
        Assert.False(plugins.TryGetPlugin("Auth", out var _), "Auth plugin should be removed after leaving Authentication state");
        
        // Transition to Analysis state
        stateMachine.Fire(AppTrigger.AnalyzeData);
        
        // Verify Analysis plugin is available
        Assert.True(plugins.TryGetPlugin("Analysis", out var _), "Analysis plugin should be available in Analysis state");
        Assert.False(plugins.TryGetPlugin("DataEntry", out var _), "DataEntry plugin should be removed after leaving DataEntry state");
    }

    [Fact]
    public async Task ChatCompletion_UsesStateAppropriatePlugins()
    {
        // This is a more complex test that would use chat completion
        // to verify the LLM is correctly using the state-appropriate plugins
        
        var kernel = CreateKernelWithAppStateMachine();
        var stateMachine = new StateMachine<AppState, AppTrigger>(AppState.Authentication);
        var pluginManager = new StateBasedPluginManager(kernel, stateMachine);
        
        var chat = kernel.GetRequiredService<IChatCompletionService>();
        var settings = new AzureOpenAIPromptExecutionSettings
        {
            FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
        };
        
        // Test in Authentication state
        var history = new ChatHistory();
        history.AddSystemMessage("You are an assistant that helps users with an application. Use available functions based on the current state.");
        history.AddUserMessage("I need to login with username 'testuser' and password 'password'");
        
        var response = await chat.GetChatMessageContentAsync(history, settings, kernel);
        _output.WriteLine($"Authentication state response: {response.Content}");
        
        // Transition to DataEntry
        stateMachine.Fire(AppTrigger.Authenticate);
        
        // Test in DataEntry state
        history = new ChatHistory();
        history.AddSystemMessage("You are an assistant that helps users with an application. Use available functions based on the current state.");
        history.AddUserMessage("Store my name as 'John Doe'");
        
        response = await chat.GetChatMessageContentAsync(history, settings, kernel);
        _output.WriteLine($"DataEntry state response: {response.Content}");
        
        // Verify response
        Assert.NotNull(response);
    }

    /// <summary>
    /// Manager class for state-based system prompts and plugin selection
    /// </summary>
    private class DynamicSystemPromptManager
    {
        private readonly Kernel _kernel;
        private readonly StateMachine<AppState, AppTrigger> _stateMachine;
        private readonly AuthPlugin _authPlugin = new();
        private readonly DataEntryPlugin _dataEntryPlugin = new();
        private readonly AnalysisPlugin _analysisPlugin = new();
        
        // Dictionary of state-specific system prompts
        private readonly Dictionary<AppState, string> _statePrompts = new()
        {
            [AppState.Authentication] = "You are an authentication assistant. Help users log in securely.",
            [AppState.DataEntry] = "You are a data entry assistant. Help users input and retrieve data accurately.",
            [AppState.Analysis] = "You are an analysis expert. Help users analyze their data and extract insights.",
            [AppState.Reporting] = "You are a reporting assistant. Help users create clear and informative reports."
        };
        
        public ChatHistory CurrentChatHistory { get; private set; } = new();
        
        public DynamicSystemPromptManager(Kernel kernel, StateMachine<AppState, AppTrigger> stateMachine)
        {
            _kernel = kernel;
            _stateMachine = stateMachine;
            
            // Wire up state entry actions
            _stateMachine.Configure(AppState.Authentication)
                .OnEntry(UpdateForAuthenticationState);
                
            _stateMachine.Configure(AppState.DataEntry)
                .OnEntry(UpdateForDataEntryState);
                
            _stateMachine.Configure(AppState.Analysis)
                .OnEntry(UpdateForAnalysisState);
                
            _stateMachine.Configure(AppState.Reporting)
                .OnEntry(UpdateForReportingState);
                
            // Initialize with current state
            InitializeForCurrentState();
        }
        
        private void InitializeForCurrentState()
        {
            // Set up initial state
            switch (_stateMachine.State)
            {
                case AppState.Authentication:
                    UpdateForAuthenticationState(null);
                    break;
                case AppState.DataEntry:
                    UpdateForDataEntryState(null);
                    break;
                case AppState.Analysis:
                    UpdateForAnalysisState(null);
                    break;
                case AppState.Reporting:
                    UpdateForReportingState(null);
                    break;
            }
        }
        
        private void UpdateForAuthenticationState(StateMachine<AppState, AppTrigger>.Transition transition)
        {
            // Update available plugins
            RemoveAllCustomPlugins();
            _kernel.Plugins.AddFromObject(_authPlugin, "Auth");
            
            // Update chat history with new system prompt
            UpdateChatHistoryForState(AppState.Authentication, transition);
        }
        
        private void UpdateForDataEntryState(StateMachine<AppState, AppTrigger>.Transition transition)
        {
            // Update available plugins
            RemoveAllCustomPlugins();
            _kernel.Plugins.AddFromObject(_dataEntryPlugin, "DataEntry");
            
            // Update chat history with new system prompt
            UpdateChatHistoryForState(AppState.DataEntry, transition);
        }
        
        private void UpdateForAnalysisState(StateMachine<AppState, AppTrigger>.Transition transition)
        {
            // Update available plugins
            RemoveAllCustomPlugins();
            _kernel.Plugins.AddFromObject(_analysisPlugin, "Analysis");
            
            // Update chat history with new system prompt
            UpdateChatHistoryForState(AppState.Analysis, transition);
        }
        
        private void UpdateForReportingState(StateMachine<AppState, AppTrigger>.Transition transition)
        {
            // Update available plugins
            RemoveAllCustomPlugins();
            // In a real app, we'd add reporting plugins here
            
            // Update chat history with new system prompt
            UpdateChatHistoryForState(AppState.Reporting, transition);
        }
        
        private void UpdateChatHistoryForState(AppState state, StateMachine<AppState, AppTrigger>.Transition transition)
        {
            // Get the new system prompt
            string newSystemPrompt = _statePrompts[state];
            
            // Add transition notification if this is not the initial state
            if (transition != null)
            {
                // Create a new chat history with the system prompt
                var newHistory = new ChatHistory();
                newHistory.AddSystemMessage(newSystemPrompt);
                
                // Add notification about state transition
                string transitionMessage = $"The system has transitioned from {transition.Source} to {transition.Destination} state. " +
                                          $"The available functionality has changed accordingly.";
                newHistory.AddSystemMessage(transitionMessage);
                
                // Transfer recent user messages (last 2) from old history to maintain context
                var recentMessages = CurrentChatHistory
                    .Where(m => m.Role.ToString() == "user" || m.Role.ToString() == "assistant")
                    .TakeLast(4)  // Take last 2 user-assistant exchanges
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
        
        private void RemoveAllCustomPlugins()
        {
            try { _kernel.Plugins.Remove("Auth"); } catch { /* Ignore if not found */ }
            try { _kernel.Plugins.Remove("DataEntry"); } catch { /* Ignore if not found */ }
            try { _kernel.Plugins.Remove("Analysis"); } catch { /* Ignore if not found */ }
            try { _kernel.Plugins.Remove("Reporting"); } catch { /* Ignore if not found */ }
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
    }

    private Kernel CreateKernelWithDynamicStateManagement()
    {
        // Create kernel
        var builder = Kernel.CreateBuilder();
        builder.AddAzureOpenAIChatCompletion(
            deploymentName: Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT") ?? Tests.Common.TestOpenAIConfig.DefaultDeployment,
            endpoint: Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") ?? Tests.Common.TestOpenAIConfig.DefaultEndpoint,
            apiKey: Environment.GetEnvironmentVariable("AZURE_OPENAI_KEY") ?? Tests.Common.TestOpenAIConfig.DefaultApiKey
        );
        
        // Create state machine
        var stateMachine = new StateMachine<AppState, AppTrigger>(AppState.Authentication);
        
        // Configure state transitions
        stateMachine.Configure(AppState.Authentication)
            .Permit(AppTrigger.Authenticate, AppState.DataEntry);
            
        stateMachine.Configure(AppState.DataEntry)
            .Permit(AppTrigger.AnalyzeData, AppState.Analysis)
            .Permit(AppTrigger.Logout, AppState.Authentication);
            
        stateMachine.Configure(AppState.Analysis)
            .Permit(AppTrigger.GenerateReport, AppState.Reporting)
            .Permit(AppTrigger.StartDataEntry, AppState.DataEntry)
            .Permit(AppTrigger.Logout, AppState.Authentication);
            
        stateMachine.Configure(AppState.Reporting)
            .Permit(AppTrigger.AnalyzeData, AppState.Analysis)
            .Permit(AppTrigger.StartDataEntry, AppState.DataEntry)
            .Permit(AppTrigger.Logout, AppState.Authentication);
            
        // Add state machine to kernel
        builder.AddStateMachine(stateMachine, "AppFlow");
        
        return builder.Build();
    }
    
    [Fact]
    public async Task OnEntryHandlers_UpdateSystemPromptAndPlugins()
    {
        // Arrange
        var kernel = CreateKernelWithDynamicStateManagement();
        var stateMachine = new StateMachine<AppState, AppTrigger>(AppState.Authentication);
        var dynamicManager = new DynamicSystemPromptManager(kernel, stateMachine);
        var chat = kernel.GetRequiredService<IChatCompletionService>();
        var settings = new AzureOpenAIPromptExecutionSettings { FunctionChoiceBehavior = FunctionChoiceBehavior.Auto() };
        
        // Add user message in Authentication state
        dynamicManager.AddUserMessage("I need to login with username 'testuser' and password 'password'");
        
        // Get response in Authentication state
        var authResponse = await chat.GetChatMessageContentAsync(
            dynamicManager.CurrentChatHistory, 
            settings, 
            kernel);
        _output.WriteLine($"Auth State Response: {authResponse.Content}");
        dynamicManager.AddAssistantMessage(authResponse.Content);
        
        // Verify Auth plugin is available
        Assert.True(kernel.Plugins.TryGetPlugin("Auth", out _), "Auth plugin should be available in Authentication state");
        
        // Get the system prompt from the first message
        var authSystemPrompt = dynamicManager.CurrentChatHistory.First().Content;
        _output.WriteLine($"Auth System Prompt: {authSystemPrompt}");
        Assert.Contains("authentication assistant", authSystemPrompt.ToLower());
        
        // Now transition to DataEntry state
        stateMachine.Fire(AppTrigger.Authenticate);
        
        // Verify chat history was updated with new system prompt
        var dataEntrySystemPrompt = dynamicManager.CurrentChatHistory.First().Content;
        _output.WriteLine($"DataEntry System Prompt: {dataEntrySystemPrompt}");
        Assert.Contains("data entry assistant", dataEntrySystemPrompt.ToLower());
        
        // Verify DataEntry plugin is now available and Auth is gone
        Assert.True(kernel.Plugins.TryGetPlugin("DataEntry", out _), "DataEntry plugin should be available in DataEntry state");
        Assert.False(kernel.Plugins.TryGetPlugin("Auth", out _), "Auth plugin should no longer be available");
        
        // Add user message in DataEntry state
        dynamicManager.AddUserMessage("Store my name as 'John Doe'");
        
        // Get response in DataEntry state
        var dataEntryResponse = await chat.GetChatMessageContentAsync(
            dynamicManager.CurrentChatHistory, 
            settings, 
            kernel);
        _output.WriteLine($"DataEntry State Response: {dataEntryResponse.Content}");
        dynamicManager.AddAssistantMessage(dataEntryResponse.Content);
        
        // Transition to Analysis state
        stateMachine.Fire(AppTrigger.AnalyzeData);
        
        // Verify chat history was updated with new system prompt
        var analysisSystemPrompt = dynamicManager.CurrentChatHistory.First().Content;
        _output.WriteLine($"Analysis System Prompt: {analysisSystemPrompt}");
        Assert.Contains("analysis expert", analysisSystemPrompt.ToLower());
        
        // Verify Analysis plugin is now available and DataEntry is gone
        Assert.True(kernel.Plugins.TryGetPlugin("Analysis", out _), "Analysis plugin should be available in Analysis state");
        Assert.False(kernel.Plugins.TryGetPlugin("DataEntry", out _), "DataEntry plugin should no longer be available");
        
        // Verify recent messages were carried over
        var recentUserMessage = dynamicManager.CurrentChatHistory
            .FirstOrDefault(m => m.Role.ToString() == "user" && m.Content.Contains("John Doe"));
        Assert.NotNull(recentUserMessage);
    }

    [Fact]
    public async Task MultiStepConversation_PreservesContextAcrossStateChanges()
    {
        // Arrange
        var kernel = CreateKernelWithDynamicStateManagement();
        var stateMachine = new StateMachine<AppState, AppTrigger>(AppState.Authentication);
        var dynamicManager = new DynamicSystemPromptManager(kernel, stateMachine);
        var chat = kernel.GetRequiredService<IChatCompletionService>();
        var settings = new AzureOpenAIPromptExecutionSettings { FunctionChoiceBehavior = FunctionChoiceBehavior.Auto() };
        
        // Authentication state conversation
        dynamicManager.AddUserMessage("I need to login with username 'testuser' and password 'password'");
        var response = await chat.GetChatMessageContentAsync(dynamicManager.CurrentChatHistory, settings, kernel);
        _output.WriteLine($"Auth State: {response.Content}");
        dynamicManager.AddAssistantMessage(response.Content);
        
        // Store a key piece of information while in authentication state
        dynamicManager.AddUserMessage("Please remember my account ID is ABC123");
        response = await chat.GetChatMessageContentAsync(dynamicManager.CurrentChatHistory, settings, kernel);
        _output.WriteLine($"Auth State (memory): {response.Content}");
        dynamicManager.AddAssistantMessage(response.Content);
        
        // Transition to DataEntry state
        stateMachine.Fire(AppTrigger.Authenticate);
        
        // Continue conversation in DataEntry state, referencing the account ID
        dynamicManager.AddUserMessage("Store my account details for the account ID I gave you earlier");
        response = await chat.GetChatMessageContentAsync(dynamicManager.CurrentChatHistory, settings, kernel);
        _output.WriteLine($"DataEntry State (recall): {response.Content}");
        dynamicManager.AddAssistantMessage(response.Content);
        
        // Verify the model remembered the account ID across state transitions
        Assert.Contains("ABC123", response.Content);
        
        // Transition to Analysis state
        stateMachine.Fire(AppTrigger.AnalyzeData);
        
        // Reference information from earlier states
        dynamicManager.AddUserMessage("Analyze the account details I stored earlier");
        response = await chat.GetChatMessageContentAsync(dynamicManager.CurrentChatHistory, settings, kernel);
        _output.WriteLine($"Analysis State (continuity): {response.Content}");
        
        // Verify that the response shows some continuity of conversation
        Assert.True(response.Content != null && response.Content.ToLower().Contains("account"), "Response should mention 'account'");
    }
}

