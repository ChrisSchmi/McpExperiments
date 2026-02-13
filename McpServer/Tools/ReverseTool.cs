using ModelContextProtocol.Server;
using System.ComponentModel;

namespace AspNetCoreMcpServer.Tools;

[McpServerToolType]
public sealed class ReverseTool
{
    [McpServerTool, Description("Echoes the input back to the client.")]
    public static string  Reverse(string message) => ReverseString(message);
    
    private static string ReverseString(string message)
    {
        var original = message;
        char[] charArray = original.ToCharArray();
        Array.Reverse(charArray);
        string reversed = new string(charArray); // "ollaH"
        return reversed;
    } 
}