using System.ComponentModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;
using System.Text;
using System.Linq;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;

namespace CodingAgent.Tools;

[McpServerToolType]
public class CodingAgentTool
{
    private const int MaxFileReadChars = 50000;
    private const int DefaultMaxSearchResults = 100;
    private const int MaxSearchResults = 500;

    private readonly LlmConfig _config;
    private readonly HttpClient _httpClient;
    private readonly ILogger<CodingAgentTool>? _logger;

    public CodingAgentTool(IOptions<LlmConfig> config, HttpClient httpClient, ILogger<CodingAgentTool>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(httpClient);

        _config = config.Value;
        _httpClient = httpClient;
        _logger = logger;
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

    [McpServerTool(Name = "list_directory")]
    [Description("Lists files and subdirectories within an allowed root directory.")]
    public string ListDirectory(
        [Description("Relative path inside an allowed root, e.g. 'src' or ''.")] string path = "")
    {
        try
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
        catch (UnauthorizedAccessException)
        {
            return JsonSerializer.Serialize(new { error = "Access denied" });
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error listing directory: {Path}", path);
            return JsonSerializer.Serialize(new { error = "Could not list directory" });
        }
    }

    [McpServerTool(Name = "get_file_info")]
    [Description("Returns metadata for a file or directory within an allowed root.")]
    public string GetFileInfo([Description("Relative path inside an allowed root.")] string path)
    {
        try
        {
            var fullPath = ResolvePath(path);
            if (fullPath is null) 
                return JsonSerializer.Serialize(new { error = "Access denied" });

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
        catch (UnauthorizedAccessException)
        {
            return JsonSerializer.Serialize(new { error = "Access denied" });
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error getting file info: {Path}", path);
            return JsonSerializer.Serialize(new { error = "Could not retrieve file info" });
        }
    }

    [McpServerTool(Name = "read_file")]
    [Description("Reads a text file within an allowed root directory.")]
    public async Task<string> ReadFile(
        [Description("Relative path inside an allowed root.")] string path,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
                return JsonSerializer.Serialize(new { error = "Path cannot be empty" });

            var fullPath = ResolvePath(path);
            if (fullPath is null || !File.Exists(fullPath))
                return JsonSerializer.Serialize(new { error = "File not found or access denied" });

            var content = await File.ReadAllTextAsync(fullPath, Encoding.UTF8, cancellationToken);
            var truncated = content.Length > MaxFileReadChars;
            var result = truncated ? content[..MaxFileReadChars] + "\n... [truncated]" : content;
            
            return JsonSerializer.Serialize(new { path, content = result, truncated });
        }
        catch (UnauthorizedAccessException)
        {
            return JsonSerializer.Serialize(new { error = "Access denied to file" });
        }
        catch (OperationCanceledException)
        {
            return JsonSerializer.Serialize(new { error = "Operation cancelled" });
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error reading file: {Path}", path);
            return JsonSerializer.Serialize(new { error = "Could not read file" });
        }
    }

    [McpServerTool(Name = "search_in_files")]
    [Description("Searches text inside files under an allowed root.")]
    public async Task<string> SearchInFiles(
        [Description("Relative root path inside an allowed root.")] string root,
        [Description("Search text.")] string query,
        [Description("Max matches.")] int maxResults = DefaultMaxSearchResults,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(query))
                return JsonSerializer.Serialize(new { error = "Query cannot be empty" });

            var fullRoot = ResolvePath(root);
            if (fullRoot is null || !Directory.Exists(fullRoot))
                return JsonSerializer.Serialize(new { error = "Root not found or access denied" });

            maxResults = Math.Clamp(maxResults, 1, MaxSearchResults);
            var matches = new List<object>();

            foreach (var file in EnumerateSearchableFiles(fullRoot))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (matches.Count >= maxResults) break;

                await SearchFileAsync(file, fullRoot, query, matches, maxResults, cancellationToken);
            }

            return JsonSerializer.Serialize(new { query, root, matches, count = matches.Count });
        }
        catch (OperationCanceledException)
        {
            return JsonSerializer.Serialize(new { error = "Search cancelled" });
        }
        catch (UnauthorizedAccessException)
        {
            return JsonSerializer.Serialize(new { error = "Access denied to root directory" });
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error searching in files under: {Root}", root);
            return JsonSerializer.Serialize(new { error = "Search operation failed" });
        }
    }

    private async Task SearchFileAsync(
        string file,
        string fullRoot,
        string query,
        List<object> matches,
        int maxResults,
        CancellationToken cancellationToken)
    {
        try
        {
            using (var reader = new StreamReader(file, Encoding.UTF8))
            {
                string? line;
                int lineNumber = 0;

                while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
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

                        if (matches.Count >= maxResults)
                            break;
                    }
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
            _logger?.LogDebug("Access denied to file: {File}", file);
        }
        catch (IOException ioEx)
        {
            _logger?.LogDebug(ioEx, "IO error reading file: {File}", file);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Error searching file: {File}", file);
        }
    }

    private IEnumerable<string> EnumerateSearchableFiles(string root)
    {
        try
        {
            return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Where(f => SearchableExtensions.Contains(Path.GetExtension(f)))
                .ToList();
        }
        catch (UnauthorizedAccessException)
        {
            _logger?.LogDebug("Access denied while enumerating files in: {Root}", root);
            return [];
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Error enumerating files in: {Root}", root);
            return [];
        }
    }

    /// <summary>
    /// Safely resolves a relative path within allowed root directories.
    /// Prevents path traversal attacks by validating the resolved path stays within the root.
    /// </summary>
    private string? ResolvePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            path = ".";

        foreach (var root in AllowedRoots)
        {
            try
            {
                // Normalize both paths to absolute, canonical form
                var normalizedRoot = Path.GetFullPath(root);
                var combinedPath = Path.GetFullPath(Path.Combine(normalizedRoot, path));

                // Ensure the resolved path is within the allowed root
                // Check both with trailing separator and exact match
                var withSeparator = normalizedRoot + Path.DirectorySeparatorChar;
                if (combinedPath.StartsWith(withSeparator, StringComparison.OrdinalIgnoreCase) ||
                    combinedPath.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase))
                {
                    return combinedPath;
                }

                _logger?.LogWarning("Path traversal attempt detected: {RequestedPath}", path);
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Error resolving path: {Path}", path);
            }
        }

        return null;
    }
}