// using System;
// using System.Collections.Generic;
// using System.Linq;
// using System.Threading;
// using System.Threading.Tasks;
// using ModelContextProtocol.Client;
// using ModelContextProtocol.Protocol;
// using System.IO;
// using System.Net;
// using System.Net.Http;

// var clientTransport = new HttpClientTransport(new HttpClientTransportOptions()
// { 
//     Endpoint = new Uri("http://localhost:5000/mcp"),
// });

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using OpenAI;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using System.ClientModel;

using var tracerProvider = Sdk.CreateTracerProviderBuilder()
    .AddHttpClientInstrumentation()
    .AddSource("*")
    .AddOtlpExporter()
    .Build();

using var metricsProvider = Sdk.CreateMeterProviderBuilder()
    .AddHttpClientInstrumentation()
    .AddMeter("*")
    .AddOtlpExporter()
    .Build();

using var loggerFactory = LoggerFactory.Create(builder => builder.AddOpenTelemetry(opt => opt.AddOtlpExporter()));

// Connect to an MCP server
Console.WriteLine("Connecting client to MCP server");

var openAIClient = new OpenAIClient(new ApiKeyCredential("ollama"), new OpenAIClientOptions()
{
   Endpoint = new Uri("http://localhost:11434/v1")  // Ollama Endpoint
}).GetChatClient("llama3.1:latest"); // phi3.5:latest


// Create a sampling client.
using IChatClient samplingClient = openAIClient.AsIChatClient()
    .AsBuilder()
    .UseOpenTelemetry(loggerFactory: loggerFactory, configure: o => o.EnableSensitiveData = true)
    .Build();

var mcpClient = await McpClient.CreateAsync(
    new HttpClientTransport(new()
    {
        Endpoint = new Uri("http://localhost:5000/mcp"),
    }),
    clientOptions: new()
    {
        Handlers = new()
        {
            SamplingHandler = samplingClient.CreateSamplingHandler()
        }
    },
    loggerFactory: loggerFactory);

// Get all available tools
Console.WriteLine("Tools available:");
var tools = await mcpClient.ListToolsAsync();
foreach (var tool in tools)
{
    Console.WriteLine($"  {tool}");
}

Console.WriteLine();

// Create an IChatClient that can use the tools.
using IChatClient chatClient = openAIClient.AsIChatClient()
    .AsBuilder()
    .UseFunctionInvocation()
    .UseOpenTelemetry(loggerFactory: loggerFactory, configure: o => o.EnableSensitiveData = true)
    .Build();

// Have a conversation, making all tools available to the LLM.
List<ChatMessage> messages = [];
while (true)
{
    Console.Write("Q: ");
    messages.Add(new(ChatRole.User, Console.ReadLine()));

    List<ChatResponseUpdate> updates = [];
    await foreach (var update in chatClient.GetStreamingResponseAsync(messages, new() { Tools = [.. tools] }))
    {
        Console.Write(update);
        updates.Add(update);
    }
    Console.WriteLine();

    messages.AddMessages(updates);
}

