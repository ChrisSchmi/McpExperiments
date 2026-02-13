// using ModelContextProtocol.Server;
// using System.ComponentModel;

// namespace AspNetCoreMcpServer.Tools;

// [McpServerToolType]
// public sealed class ContentForSummarizeTool
// {
//     [McpServerTool, Description("Delivers content downloaded from a specific URI so a LLM can summarize")]
//     public static async Task<string> DeliverDownloadedContent(
//         McpServer thisServer,
//         HttpClient httpClient,
//         [Description("The url from which to download the content to summarize")] string url,
//         CancellationToken cancellationToken)
//     {
//         ArgumentNullException.ThrowIfNull(thisServer);
//         ArgumentNullException.ThrowIfNull(httpClient);

//         string content = await httpClient.GetStringAsync(url);

//         //ChatMessage[] messages =
//         //[
//         //    new(ChatRole.User, "Briefly summarize the following downloaded content:"),
//         //    new(ChatRole.User, content),
//         //];
        
//         //ChatOptions options = new()
//         //{
//         //    MaxOutputTokens = 256,
//         //    Temperature = 0.3f,
//         //};

//         return content;
//     }
// }