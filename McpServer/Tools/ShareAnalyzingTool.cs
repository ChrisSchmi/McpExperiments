using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Server;

namespace CodingAgent.Tools;

/// <summary>
/// Hochpräzises Tool zur Aktienanalyse. 
/// Nutzt externe Quellen (Yahoo/Google) ohne API-Key.
/// </summary>
[McpServerToolType]
public class ShareAnalyzingTool
{
    private readonly LlmConfig _config;
    private readonly ILogger<ShareAnalyzingTool> _logger;
    private readonly HttpClient _httpClient;

    public ShareAnalyzingTool(
        IOptions<LlmConfig> config, 
        ILogger<ShareAnalyzingTool> logger, 
        HttpClient httpClient)
    {
        _config = config?.Value ?? throw new ArgumentNullException(nameof(config));
        // Korrigierte Logger-Initialisierung
        _logger = logger ?? NullLogger<ShareAnalyzingTool>.Instance;
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

        // Yahoo Finance benötigt zwingend einen realistischen User-Agent
        if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
        {
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
        }
    }

    [McpServerTool(Name = "get_market_data")]
    [Description("MANDATORY: Use this tool to retrieve REAL-TIME stock metrics (PE ratio, dividend yield, price). DO NOT use internal knowledge or outdated training data. If the tool returns no data, report an error.")]
    public async Task<string> GetMarketDataAsync(
        [Description("The stock symbol/ticker (e.g., 'AAPL', 'SAP.DE', 'ASML.AS').")] string symbol)
    {
        _logger.LogInformation("Fetching real-time market data for: {Symbol}", symbol);
        try
        {
            // Öffentliche Yahoo Finance API v7
            //string url = $"https://query1.finance.yahoo.com/v7/finance/quote?symbols={symbol}";

            // 1. Cookie generieren (durch Aufruf einer beliebigen Yahoo-Seite)
            await _httpClient.GetAsync("https://fc.yahoo.com"); 

            // 2. Crumb abrufen
            string crumb = await _httpClient.GetStringAsync("https://query1.finance.yahoo.com/v1/test/getcrumb");

            // 3. Kursdaten mit Cookie und Crumb abrufen
            //string url = $"https://yahoo.com{symbol}&crumb={crumb}";
            string url = $"https://query1.finance.yahoo.com/v7/finance/quote?symbols={symbol}&crumb={crumb}";

            return await _httpClient.GetStringAsync(url);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Yahoo Finance fetch failed for {Symbol}", symbol);
            return $"Error: Could not retrieve real-time data for {symbol}. {ex.Message}";
        }
    }

    [McpServerTool(Name = "search_company_news")]
    [Description("MANDATORY: Retrieve LATEST news and ESG/sustainability facts from the CURRENT YEAR. Use this to identify recent scandals, management changes, or dividend announcements. Ignore results older than 12 months.")]
    public async Task<string> SearchNewsAsync(
        [Description("Specific search query, e.g., 'Munich Re Nachhaltigkeitsbericht' or 'Microsoft Dividend News'.")] string query)
    {
        _logger.LogInformation("Searching latest news for: {Query}", query);
        try
        {
            // Automatischer Fokus auf das aktuelle Jahr (2026)
            string currentYear = DateTime.Now.Year.ToString();
            string searchUrl = $"https://news.google.com/rss/search?q={Uri.EscapeDataString(query + " " + currentYear)}&hl=de&gl=DE&ceid=DE:de";
            
            var xml = await _httpClient.GetStringAsync(searchUrl);
            var matches = Regex.Matches(xml, @"<title>(.*?)</title>").Cast<Match>().Skip(1).Take(10);
            
            return $"LATEST WEB RESULTS ({currentYear}):\n- " + string.Join("\n- ", matches.Select(m => m.Groups[1].Value));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Google News RSS fetch failed for {Query}", query);
            return $"Error fetching latest news: {ex.Message}";
        }
    }

    [McpServerTool(Name = "submit_final_analysis")]
    [Description("Final step: Submits the structured 1-10 analysis. ALL scores must be strictly derived from the tool outputs above. Categorize into Business, Fundamentals, Dividend, Sustainability, and Valuation.")]
    public string SubmitAnalysis(
        [Description("The official name of the company.")] string companyName,
        [Description("Structured analysis report based EXCLUSIVELY on the gathered real-time data.")] StockAnalysisReport report)
    {
        _logger.LogInformation("Submitting final analysis for {Company}", companyName);

        var options = new JsonSerializerOptions 
        { 
            WriteIndented = true, 
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping 
        };

        return JsonSerializer.Serialize(new 
        { 
            Header = new { Company = companyName, AnalysisDate = DateTime.Now.ToString("g"), DataSource = "Live Web & Market Tools" },
            Report = report 
        }, options);
    }
}

#region Data Structures

public class StockAnalysisReport
{
    [Description("Score 1-10 and justification for the business model and moat.")]
    public ScoreDetail BusinessModel { get; set; } = new();

    [Description("Score 1-10 and justification for financial health (debt, margins).")]
    public ScoreDetail Fundamentals { get; set; } = new();

    [Description("Score 1-10 and justification for dividend safety and growth.")]
    public ScoreDetail Dividend { get; set; } = new();

    [Description("Score 1-10 and justification for ESG and future viability.")]
    public ScoreDetail Sustainability { get; set; } = new();

    [Description("Score 1-10 and justification for current valuation (PE vs History).")]
    public ScoreDetail Valuation { get; set; } = new();

    [Description("The weighted total score from 1 to 10.")]
    public double TotalScore { get; set; }

    [Description("Extracted key metrics (e.g., P/E Ratio: 15.4).")]
    public Dictionary<string, string> KeyMetrics { get; set; } = new();

    [Description("Final verdict: Strong Buy, Hold, or Avoid.")]
    public string Conclusion { get; set; } = string.Empty;
}

public class ScoreDetail 
{ 
    public int Score { get; set; } 
    public string Justification { get; set; } = string.Empty; 
}

#endregion