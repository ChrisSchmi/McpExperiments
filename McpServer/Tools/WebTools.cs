using System.ComponentModel;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Server;

[McpServerToolType]
public static class WebTools
{
    private static readonly HttpClient httpClient = new();

    [McpServerTool, Description("Ruft HTML-Inhalt einer Webseite ab")]
    public static async Task<string> FetchWebPage(string url)
    {
        var response = await httpClient.GetStringAsync(url);
        return $"Webseite-Inhalt ({url}):\n{response[..Math.Min(4000, response.Length)]}";
    }

    [McpServerTool, Description("Einfache Websuche über DuckDuckGo Instant Answer API")]
    public static async Task<string> WebSearch(string query)
    {
        var searchUrl = $"https://api.duckduckgo.com/?q={Uri.EscapeDataString(query)}&format=json&no_html=1&skip_disambig=1";
        var json = await httpClient.GetStringAsync(searchUrl);
        var doc = JsonSerializer.Deserialize<DuckDuckGoResponse>(json);
        
        return $"Ergebnisse für '{query}':\n- Abstract: {doc?.Abstract ?? "Keine Zusammenfassung"}\n- Hauptlink: {doc?.OfficialUrl ?? doc?.FirstURL}";
    }
}



public class DuckDuckGoResponse
{
    [JsonPropertyName("Abstract")]
    public string? Abstract { get; set; }
    
    [JsonPropertyName("AbstractText")]
    public string? AbstractText { get; set; }
    
    [JsonPropertyName("OfficialUrl")]
    public string? OfficialUrl { get; set; }
    
    [JsonPropertyName("FirstURL")]
    public string? FirstURL { get; set; }
    
    [JsonPropertyName("Results")]
    public DuckDuckGoResult[]? Results { get; set; }
    
    [JsonPropertyName("RelatedTopics")]
    public RelatedTopic[]? RelatedTopics { get; set; }
}

public class DuckDuckGoResult
{
    [JsonPropertyName("FirstURL")]
    public string? FirstURL { get; set; }
    
    [JsonPropertyName("Text")]
    public string? Text { get; set; }
}

public class RelatedTopic
{
    [JsonPropertyName("FirstURL")]
    public string? FirstURL { get; set; }
    
    [JsonPropertyName("Text")]
    public string? Text { get; set; }
}

