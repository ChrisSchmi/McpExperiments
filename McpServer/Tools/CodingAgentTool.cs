using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CodingAgent.Tools;

[McpServerToolType]
public class CodingAgentTool
{
    private const int MaxFileReadChars = 50000;
    private const int DefaultMaxSearchResults = 100;

    private readonly LlmConfig _config;
    private readonly ILogger<CodingAgentTool> _logger;

    public CodingAgentTool(IOptions<LlmConfig> config, ILogger<CodingAgentTool> logger)
    {
        ArgumentNullException.ThrowIfNull(config, nameof(config));  

        _config = config?.Value ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? new NullLogger<CodingAgentTool>();
    }

    private string[] AllowedRoots => [Path.GetFullPath(_config.AllowedCodeDirectory)];

    private object WithContext(string currentWorkingDirectory, object payload) => new
    {
        currentWorkingDirectory = currentWorkingDirectory,
        payload
    };

    [McpServerTool(Name = "list_allowed_directories")]
    [Description("Returns the list of allowed root directories. Use this to find your starting point.")]
    public object ListAllowedDirectories() => AllowedRoots;

    [McpServerTool(Name = "change_directory")]
    [Description("Changes the current working directory. 'path' must be relative to the allowed root.")]
    public object ChangeDirectory([Description("Current working directory, where we navigated to.")]string currentWorkingDirectory, [Description("Name of the directory, in which we want to navigate, which is already in the current working directory.")] string targetDirectory)
    {
        var oldWorkingDirectory = ResolvePath(currentWorkingDirectory);
        var fullPath = ResolvePath(currentWorkingDirectory);
        if (fullPath == null || !Directory.Exists(fullPath))
        {
            return new { error = "Path not found or access denied.", triedPath = currentWorkingDirectory, availableRoots = AllowedRoots };
        }

        var targetPath = Path.Combine(fullPath, targetDirectory);

        targetPath = ResolvePath(targetPath);

        return WithContext(targetPath, new {
            oldWorkingDirectory = oldWorkingDirectory,
            newWorkingDirectory = targetPath,
            availableFilesInNewDirectory = Directory.Exists(targetPath) ? Directory.EnumerateFileSystemEntries(targetPath).Select(Path.GetFileName).ToList() : null,
            message = $"Changed directory from: {oldWorkingDirectory} to: {targetPath}" });
    }

    [McpServerTool(Name = "list_directory")]
    [Description("Lists files in given directory.")]
    public object ListDirectory([Description("path of the folder to show")] string path = "")
    {
        if(path.Equals(_config.AllowedCodeDirectory, StringComparison.OrdinalIgnoreCase))
        {
            path = "";
        }

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

            return WithContext(path,new { entries });
        }
        catch (Exception ex)
        {
            return WithContext(path,new { error = $"Error: {ex.Message}" });
        }
    }

    [McpServerTool(Name = "read_file")]
    [Description(
        "Reads the contents of a file from the provided, allowed directory and returns the full file content to the model. " +
        "Use this tool for requests such as 'read', 'show', or 'list', when a file should be found and its contents displayed." +
        "AFTER this tool runs, file content is available - NO other tools needed until the user asks explicitly for another file."
        )]
    public async Task<object> ReadFile([Description("Path to the file.")] string path, CancellationToken ct = default)
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

            string usedPath = Path.GetDirectoryName(path) ?? "";
            string fileName = Path.GetFileName(path);

            var content = await File.ReadAllTextAsync(fullPath, Encoding.UTF8, ct);
            
            return WithContext(usedPath,new {
                path = usedPath,
                file = fileName,
                contentLength =content.Length,
                content = content.Length > MaxFileReadChars ? content[..MaxFileReadChars] + "\n[Truncated]" : content });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading file at path: {Path}", path);
            return WithContext(path,new { error = $"Error: {ex.Message}" });
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
            catch(Exception ex)
            {
                _logger.LogError(ex, "Error resolving path: {Path} with root: {Root}", path, root);
            }
        }
        return null;
    }
}