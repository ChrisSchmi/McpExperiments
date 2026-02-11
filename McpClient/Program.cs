using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.IO;
using System.Net;
using System.Net.Http;


var clientTransport = new HttpClientTransport(new HttpClientTransportOptions()
{ 
    Endpoint = new Uri("http://localhost:5000/mcp"),
});

var client = await McpClient.CreateAsync(clientTransport);

// Print the list of tools available from the server.
foreach (var tool in await client.ListToolsAsync())
{
    Console.WriteLine($"{tool.Name} ({tool.Description})");
}

Console.WriteLine(Environment.NewLine);
Console.WriteLine(Environment.NewLine);

// Execute a tool (this would normally be driven by LLM tool invocations).
var result = await client.CallToolAsync(
    "echo",
    new Dictionary<string, object?>() { ["message"] = "Hello MCP!" },
    cancellationToken:CancellationToken.None);

// echo always returns one and only one text content object
Console.WriteLine(result.Content.OfType<TextContentBlock>().First().Text);

Console.WriteLine(Environment.NewLine);
Console.WriteLine(Environment.NewLine);


var result2 = await client.CallToolAsync(
    "reverse",
    new Dictionary<string, object?>() { ["message"] = "Hello reverse!" },
    cancellationToken:CancellationToken.None);


    // echo always returns one and only one text content object
Console.WriteLine(result2.Content.OfType<TextContentBlock>().First().Text);