/*
using Microsoft.Extensions.AI;
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


    [McpServerTool(Name = "DeliverContentForSummarizeFromUrl"), Description("Deliver content downloaded from a specific URI so a LLM can summarize")]
    public static async Task<string> DeliverDownloadedContent(
        McpServer thisServer,
        HttpClient httpClient,
        [Description("The url from which to download the content to summarize")] string url,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(thisServer);
        ArgumentNullException.ThrowIfNull(httpClient);

        string content = await httpClient.GetStringAsync(url);

        //ChatMessage[] messages =
        //[
        //    new(ChatRole.User, "Briefly summarize the following downloaded content:"),
        //    new(ChatRole.User, content),
        //];
        
        //ChatOptions options = new()
        //{
        //    MaxOutputTokens = 256,
        //    Temperature = 0.3f,
        //};

        return content;
    }
}
*/