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
    private const int MaxFileReadChars = 20000; // Etwas kleiner für besseren Kontext-Flow
    private readonly LlmConfig _config;
    private readonly ILogger<CodingAgentTool> _logger;

    public CodingAgentTool(IOptions<LlmConfig> config, ILogger<CodingAgentTool> logger)
    {
        _config = config?.Value ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? new NullLogger<CodingAgentTool>();
    }

    private string RootPath => Path.GetFullPath(_config.AllowedCodeDirectory);

    /// <summary>
    /// Erzeugt eine standardisierte Antwort, die das LLM über seinen Standort informiert.
    /// </summary>
    private object WrapResponse(string absoluteCurrentPath, object data)
    {
        var relativePath = Path.GetRelativePath(RootPath, absoluteCurrentPath);
        if (relativePath == ".") relativePath = "/ (root)";

        return new
        {
            agent_context = new {
                cwd = relativePath,
                can_go_up = absoluteCurrentPath.Length > RootPath.Length,
                hint = $"You are in '{relativePath}'. You can use relative paths or '..' to go back."
            },
            payload = data
        };
    }
  

    private string[] AllowedRoots => [Path.GetFullPath(_config.AllowedCodeDirectory)];


    [McpServerTool(Name = "list_allowed_directories")]
    [Description("Returns the list of allowed root directories. Use this to find your starting point.")]
    public object ListAllowedDirectories() => AllowedRoots;



    [McpServerTool(Name = "navigate")]
    [Description("Navigates to a directory. Supports '..' to go up or relative paths.")]
    public object Navigate([Description("The current directory the AI is in.")] string currentDir, 
                            [Description("The target (e.g. 'src', '..', 'sub/folder')")] string target)
    {
        var baseDir = ResolvePath(currentDir) ?? RootPath;
        var newPath = Path.GetFullPath(Path.Combine(baseDir, target));

        if (!IsPathSafe(newPath) || !Directory.Exists(newPath))
        {
            return WrapResponse(baseDir, new { error = "Target directory not found or access denied." });
        }

        var files = Directory.EnumerateFileSystemEntries(newPath)
            .Select(p => new { name = Path.GetFileName(p), type = Directory.Exists(p) ? "dir" : "file" });

        return WrapResponse(newPath, new { message = "Directory changed.", content = files });
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

            // var entries = Directory.EnumerateFileSystemEntries(fullPath)
            //     .Select(entry => (object)(Directory.Exists(entry) ? 
            //         new { name = Path.GetFileName(entry), type = "dir" } :
            //         new { name = Path.GetFileName(entry), type = "file", size = new FileInfo(entry).Length }))
            //     .ToList();

            // return WithContext(path,new { entries });

            var entries = Directory.EnumerateFileSystemEntries(fullPath)
                .Select(entry => {
                    var isDir = Directory.Exists(entry);
                    var name = Path.GetFileName(entry);
                    var relPath = Path.GetRelativePath(_config.AllowedCodeDirectory, entry);
                    
                    if (isDir)
                    {
                        return (object)new 
                        { 
                            name, 
                            type = "dir", 
                            relativePath = relPath, // Wichtig für Tool-Input
                            fullPath = entry,
                            isEmpty = !Directory.EnumerateFileSystemEntries(entry).Any()
                        };
                    }
                    else
                    {
                        var fileInfo = new FileInfo(entry);
                        return (object)new 
                        { 
                            name, 
                            type = "file", 
                            size = fileInfo.Length,
                            extension = fileInfo.Extension,
                            relativePath = relPath,
                            fullPath = entry,
                            lastModified = fileInfo.LastWriteTime
                        };
                    }
                })
                .ToList();

            // Das entscheidende "Context-Wrapping"
            return WrapResponse(fullPath,
                new { 
                    context = new {
                        currentDirectoryName = Path.GetFileName(fullPath),
                        currentRelativePath = Path.GetRelativePath(_config.AllowedCodeDirectory, fullPath),
                        parentDirectory = Path.GetRelativePath(_config.AllowedCodeDirectory, Path.GetDirectoryName(fullPath) ?? _config.AllowedCodeDirectory),
                        isRoot = fullPath.Equals(Path.GetFullPath(_config.AllowedCodeDirectory), StringComparison.OrdinalIgnoreCase),
                        totalEntryCount = entries.Count
                    },
                    entries 
                });            
        }
        catch (Exception ex)
        {
            return WrapResponse(path,new { error = $"Error: {ex.Message}" });
        }
    }    

[McpServerTool(Name = "read_file_segment")]
[Description("Reads a file segment. You MUST provide the 'currentDirectory' you are in to maintain context.")]
public async Task<object> ReadFileSegment(
    [Description("The directory you are currently browsing (CWD).")] string currentDirectory,
    [Description("The name of the file or a relative path from the current directory.")] string path, 
    [Description("Character offset to start reading from")] int offset = 0)
{
    // 1. Wir kombinieren den aktuellen Standort der KI mit dem Dateinamen
    var absoluteBase = ResolvePath(currentDirectory) ?? RootPath;
    var targetPath = Path.Combine(absoluteBase, path);
    
    // 2. Wir validieren den finalen Pfad
    var finalPath = Path.GetFullPath(targetPath);

    if (!IsPathSafe(finalPath) || !File.Exists(finalPath))
    {
        return WrapResponse(absoluteBase, new { 
            error = "File not found.", 
            attemptedPath = finalPath,
            hint = "Ensure 'currentDirectory' is correct and 'path' is relative to it." 
        });
    }

    try 
    {
        var content = await File.ReadAllTextAsync(finalPath, Encoding.UTF8);
        var segment = content.Skip(offset).Take(MaxFileReadChars).ToArray();
        
        // Wir geben den Verzeichnisnamen der Datei als neuen Kontext zurück
        var fileDir = Path.GetDirectoryName(finalPath) ?? RootPath;

        return WrapResponse(fileDir, new {
            file = Path.GetFileName(finalPath),
            content = new string(segment),
            nextOffset = offset + segment.Length,
            totalSize = content.Length,
            isEndOfFile = (offset + segment.Length) >= content.Length
        });
    }
    catch (Exception ex)
    {
        return WrapResponse(absoluteBase, new { error = $"Read error: {ex.Message}" });
    }
}

    private string? ResolvePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path == "/") return RootPath;
        
        var combined = Path.GetFullPath(Path.Combine(RootPath, path.TrimStart('/', '\\')));
        return IsPathSafe(combined) ? combined : null;
    }

    private bool IsPathSafe(string path) => path.StartsWith(RootPath, StringComparison.OrdinalIgnoreCase);
}