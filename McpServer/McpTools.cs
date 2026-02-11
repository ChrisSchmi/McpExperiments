using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.VisualBasic;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Reflection.Metadata.Ecma335;

[McpServerToolType]
public static class McpTools
{
    [McpServerTool, Description("Echoes the message back to the client.")]
    public static string Echo(string message) => $"hello {message}";

    [McpServerTool, Description("Echoes the reverse message back to the client.")]
    public static string Reverse(string message) => ReverseString(message);



    private static string ReverseString(string message)
    {
        var original = message;
        char[] charArray = original.ToCharArray();
        Array.Reverse(charArray);
        string reversed = new string(charArray); // "ollaH"
        return reversed;
    } 
}
