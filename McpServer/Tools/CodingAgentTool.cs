using System.ComponentModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;
using System.Text;
using System.Linq;
using Microsoft.Extensions.Options;

namespace CodingAgent.Tools;

[McpServerToolType]
public class CodingAgentTool
{

    private readonly LlmConfig _config;
    private readonly HttpClient _httpClient;

    public CodingAgentTool(IOptions<LlmConfig> config, HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(httpClient);

        _config = config.Value;
        _httpClient = httpClient;
    }
    private string[] AllowedRoots => [Path.GetFullPath(_config.AllowedCodeDirectory)];


    private static readonly HashSet<string> SearchableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".csproj", ".sln", ".json", ".xml", ".config", ".md", ".txt", ".yml", ".yaml"
    };

    [McpServerTool(Name = "list_allowed_directories")]
    [Description("Lists the directories the server is allowed to access.")]
    public string ListAllowedDirectories()
        => JsonSerializer.Serialize(AllowedRoots);

    [McpServerTool(Name ="list_directory")]
    [Description("Lists files and subdirectories within an allowed root directory.")]
    public string ListDirectory(
        [Description("Relative path inside an allowed root, e.g. 'src' or ''.")] string path = "")
    {
        var fullPath = ResolvePath(path);
        if (fullPath is null || !Directory.Exists(fullPath))
            return JsonSerializer.Serialize(new { error = "Access denied or path not found" });

        var entries = Directory.EnumerateFileSystemEntries(fullPath)
            .Select(entry => (object)(Directory.Exists(entry) ? 
                new { name = Path.GetFileName(entry), type = "dir" } :
                new { name = Path.GetFileName(entry), type = "file", size = new FileInfo(entry).Length }))
            .ToList();

        return JsonSerializer.Serialize(new { path, entries });
    }

    [McpServerTool(Name ="get_file_info")]
    [Description("Returns metadata for a file or directory within an allowed root.")]
    public string GetFileInfo([Description("Relative path inside an allowed root.")] string path)
    {
        var fullPath = ResolvePath(path);
        if (fullPath is null) return JsonSerializer.Serialize(new { error = "Access denied" });

        if (File.Exists(fullPath))
        {
            var info = new FileInfo(fullPath);
            return JsonSerializer.Serialize(new
            {
                path,
                type = "file",
                size = info.Length,
                modified = info.LastWriteTimeUtc
            });
        }

        if (Directory.Exists(fullPath))
        {
            var info = new DirectoryInfo(fullPath);
            return JsonSerializer.Serialize(new
            {
                path,
                type = "dir",
                modified = info.LastWriteTimeUtc
            });
        }

        return JsonSerializer.Serialize(new { path, error = "not found" });
    }

    [McpServerTool(Name = "read_file")]
    [Description("Reads a text file within an allowed root directory.")]
    public async Task<string> ReadFile([Description("Relative path inside an allowed root.")] string path)
    {
        var fullPath = ResolvePath(path);
        if (fullPath is null || !File.Exists(fullPath))
            return JsonSerializer.Serialize(new { error = "File not found or access denied" });

        try
        {
            var content = await File.ReadAllTextAsync(fullPath, Encoding.UTF8);
            const int maxChars = 50000;
            var result = content.Length > maxChars ? content[..maxChars] + "\n... [truncated]" : content;
            return JsonSerializer.Serialize(new { path, content = result, truncated = content.Length > maxChars });
        }
        catch
        {
            return JsonSerializer.Serialize(new { error = "Could not read file" });
        }
    }

    [McpServerTool(Name ="search_in_files")]
    [Description("Searches text inside files under an allowed root.")]
    public string SearchInFiles(
        [Description("Relative root path inside an allowed root.")] string root,
        [Description("Search text.")] string query,
        [Description("Max matches.")] int maxResults = 100)
    {
        var fullRoot = ResolvePath(root);
        if (fullRoot is null || !Directory.Exists(fullRoot))
            return JsonSerializer.Serialize(new { error = "Root not found" });

        maxResults = Math.Clamp(maxResults, 1, 500);
        var matches = new List<object>();

        foreach (var file in EnumerateSearchableFiles(fullRoot))
        {
            if (matches.Count >= maxResults) break;

            try
            {
                var lineNumber = 0;
                foreach (var line in File.ReadLines(file, Encoding.UTF8))
                {
                    lineNumber++;
                    if (line.Contains(query, StringComparison.OrdinalIgnoreCase))
                    {
                        matches.Add(new
                        {
                            file = Path.GetRelativePath(fullRoot, file),
                            line = lineNumber,
                            content = line.TrimEnd()
                        });
                    }
                    if (matches.Count >= maxResults) break;
                }
            }
            catch { }
        }

        return JsonSerializer.Serialize(new { query, root, matches });
    }

    private IEnumerable<string> EnumerateSearchableFiles(string root)
    {
        try
        {
            return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Where(f => SearchableExtensions.Contains(Path.GetExtension(f)));
        }
        catch
        {
            return [];
        }
    }

    private string? ResolvePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) path = ".";
        foreach (var root in AllowedRoots)
        {
            var combined = Path.GetFullPath(Path.Combine(root, path));
            if (combined.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return combined;
        }
        return null;
    }
}