using System.ComponentModel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

[McpServerPromptType]
public static class RecipePrompts
{
    [McpServerPrompt, Description("Generiert Rezeptideen basierend auf Zutatenliste")]
    public static async Task<ChatMessage> RecipeIdea(IOptions<LlmConfig> config,
    HttpClient httpClient,
        [Description("Komma-separierte Zutatenliste")] string ingredients)
    {
        var ingredientList = ingredients.Split(',')
            .Select(i => i.Trim())
            .Where(i => !string.IsNullOrWhiteSpace(i));

        var language = await DetectLanguageAsync(config,httpClient,ingredients); // Vereinfachte Logik
        
        var prompt = $@"Erstelle Rezeptideen mit diesen Zutaten: {string.Join(", ", ingredientList)}
Antworte auf {language} und gib ein einzelnes, vollständiges Rezept mit Zutaten & Anleitung.";

        return new ChatMessage(ChatRole.User, prompt);
    }

    private static async Task<string> DetectLanguageAsync(IOptions<LlmConfig> config, HttpClient httpClient, string text)
    {
        var langPrompt = $"which language is it - only answer 'de' or 'en': '{text}'";
        
        var langRequest = new { model = config.Value.ModelName, messages = new[] { new { role = "user", content = langPrompt } } };
        var langResponse = await httpClient.PostAsJsonAsync($"{config.Value.ApiUrl}", langRequest);
        var langResult = await langResponse.Content.ReadAsStringAsync();

        var objectResult = System.Text.Json.JsonSerializer.Deserialize<ChatCompletionResponse>(langResult);
        
        langResult = objectResult.Choices.FirstOrDefault()?.Message.Content ?? "en";
        
        return langResult.Trim().ToLower();
    }
}
