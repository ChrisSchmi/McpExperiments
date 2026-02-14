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

        var prompt =
        $"can you give me short recipe suggestions using the following ingredients: {string.Join(", ", ingredientList)}  and give the answer in iso-language '{language}'";
        prompt += " ";
        prompt += "if possible, give me 2-3 recipe ideas - if you see multiple recipes, based on the list, see them as unique task.";
        prompt += " ";
        prompt += "keep the answer short and concise and consider, that some people might not eat meat or don't eat some sorts of meat, so a vegetarian alternative additional idea would be good to have in the list.";
        prompt += " ";
        prompt += "if possible list the used ingredients for the recipe idea bewlow the title. ";

        var jsonContent = new 
        {
            model = _config.ModelName,  
            messages = new[] { new { role = "user", content = prompt } },
            max_tokens = 300
        };

        var response =
            await _httpClient.PostAsJsonAsync(_config.ApiUrl, jsonContent);

        var llmResult = await response.Content.ReadFromJsonAsync<dynamic>();

        ideas = llmResult?.ToString() ?? "Sorry, I couldn't generate a recipe idea at the moment.";

        return ideas;
    }

    private async Task<string> DetectLanguageAsync(string text)
    {
        var langPrompt = $"which language is it - only answer 'de' or 'en': '{text}'";
        
        var langRequest = new { model = _config.ModelName, messages = new[] { new { role = "user", content = langPrompt } } };
        var langResponse = await _httpClient.PostAsJsonAsync($"{_config.ApiUrl}", langRequest);
        var langResult = await langResponse.Content.ReadAsStringAsync();

        var objectResult = System.Text.Json.JsonSerializer.Deserialize<ChatCompletionResponse>(langResult);
        
        langResult = objectResult?.Choices.FirstOrDefault()?.Message.Content ?? "en";
        
        return langResult.Trim().ToLower();
    }
}