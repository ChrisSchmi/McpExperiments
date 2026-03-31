using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;

namespace CodingAgent.Tools;

[McpServerToolType]
public class CodingAgentTool
{
    private const int MaxFileReadChars = 50000;
    private const int DefaultMaxSearchResults = 100;

    private readonly LlmConfig _config;
    private readonly HttpClient _httpClient;
    private readonly ILogger<CodingAgentTool>? _logger;

    public CodingAgentTool(IOptions<LlmConfig> config, HttpClient httpClient, ILogger<CodingAgentTool>? logger = null)
    {
        _config = config?.Value ?? throw new ArgumentNullException(nameof(config));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger;
    }

    private string[] AllowedRoots => [Path.GetFullPath(_config.AllowedCodeDirectory)];

    [McpServerTool(Name = "list_allowed_directories")]
    [Description("Returns the list of allowed root directories. Use this to find your starting point.")]
    public object ListAllowedDirectories() => AllowedRoots;

    [McpServerTool(Name = "list_directory")]
    [Description("Lists files in a directory. 'path' must be relative to the allowed root (e.g., 'src' or '').")]
    public object ListDirectory([Description("Relative path.")] string path = "")
    {
        try
        {
            var fullPath = ResolvePath(path);
            if (fullPath == null || !Directory.Exists(fullPath))
            {
                return new { 
                    error = "Path not found or access denied.", 
                    triedPath = path,
                    availableRoots = AllowedRoots 
                };
            }

            var entries = Directory.EnumerateFileSystemEntries(fullPath)
                .Select(entry => (object)(Directory.Exists(entry) ? 
                    new { name = Path.GetFileName(entry), type = "dir" } :
                    new { name = Path.GetFileName(entry), type = "file", size = new FileInfo(entry).Length }))
                .ToList();

            return new { path, entries };
        }
        catch (Exception ex)
        {
            return new { error = $"Error: {ex.Message}" };
        }
    }

    [McpServerTool(Name = "read_file")]
    [Description("Reads a file. Path must be relative to the allowed root. Example: 'McpServer/Tools/Tool.cs'")]
    public async Task<object> ReadFile([Description("Relative path to the file.")] string path, CancellationToken ct = default)
    {
        try
        {
            var fullPath = ResolvePath(path);
            if (fullPath == null || !File.Exists(fullPath))
            {
                return new { 
                    error = "File not found.", 
                    suggestion = "Use list_directory to verify the path exists relative to the root.",
                    requestedPath = path 
                };
            }

            var content = await File.ReadAllTextAsync(fullPath, Encoding.UTF8, ct);
            return new { 
                path, 
                content = content.Length > MaxFileReadChars ? content[..MaxFileReadChars] + "\n[Truncated]" : content 
            };
        }
        catch (Exception ex)
        {
            return new { error = ex.Message };
        }
    }

    private string? ResolvePath(string path)
    {
        // Bereinige den Pfad von führenden Slashes, die das LLM oft mitschickt
        path = path.TrimStart('/', '\\');
        if (string.IsNullOrWhiteSpace(path)) path = ".";

        foreach (var root in AllowedRoots)
        {
            try
            {
                var normalizedRoot = Path.GetFullPath(root);
                var combinedPath = Path.GetFullPath(Path.Combine(normalizedRoot, path));

                // Sicherheitscheck: Bleibt der Pfad innerhalb des Roots?
                if (combinedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
                {
                    return combinedPath;
                }
            }
            catch { }
        }
        return null;
    }
}