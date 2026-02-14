using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace AspNetCoreMcpServer.Tools;

[McpServerToolType]
public sealed class RecipeIdeaTool
{
    private readonly LlmConfig _config;
    private readonly HttpClient _httpClient;

    public RecipeIdeaTool(IOptions<LlmConfig> config, HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(httpClient);

        _config = config.Value;
        _httpClient = httpClient;
    }

    [McpServerTool, Description("Returns a recipe idea based on the input.")]
    public async Task<string> RecipeIdea(
        [Description("Ingredients list")] string ingredients)
    {
        ArgumentNullException.ThrowIfNullOrWhiteSpace(ingredients);

        string ideas = string.Empty;

        var ingredientList = ingredients.Split(' ').Select(i => i.Trim()).ToArray();

        var language = await DetectLanguageAsync(ingredients);

        var prompt = "can you give me recipe suggestions using the following ingredients: " + string.Join(", ", ingredientList) + $" and give the answer in iso-languege '{language}'";

        var jsonContent = new 
        {
            model = _config.ModelName,  
            messages = new[] { new { role = "user", content = prompt } },
            max_tokens = 300
        };

        var response = await _httpClient.PostAsJsonAsync(
            _config.ApiUrl, 
            jsonContent);

        var llmResult = await response.Content.ReadFromJsonAsync<dynamic>();

        ideas = llmResult.ToString() ?? "Sorry, I couldn't generate a recipe idea at the moment.";

        return ideas;
    }

    private async Task<string> DetectLanguageAsync(string text)
    {
        var langPrompt = $"which language is it - onyl answert 'de' or 'en': '{text}'";
        
        var langRequest = new { model = _config.ModelName, messages = new[] { new { role = "user", content = langPrompt } } };
        var langResponse = await _httpClient.PostAsJsonAsync($"{_config.ApiUrl}", langRequest);
        var langResult = await langResponse.Content.ReadAsStringAsync();

        var objectResult = System.Text.Json.JsonSerializer.Deserialize<ChatCompletionResponse>(langResult);
        
        langResult = objectResult.Choices.FirstOrDefault()?.Message.Content ?? "en";
        
        return langResult.Trim().ToLower();
    }
}