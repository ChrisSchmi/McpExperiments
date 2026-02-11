using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.AspNetCore;  // ✅ For MapMcp()
using ModelContextProtocol.Server;

var builder = WebApplication.CreateBuilder(args);

// Logging auf stderr
builder.Logging.AddConsole(consoleLogOptions =>
{
    consoleLogOptions.LogToStandardErrorThreshold = LogLevel.Trace;
});

var options = new McpServerOptions()
{
    ServerInfo = new ModelContextProtocol.Protocol.Implementation()
    {
        Name = "Christians MCP Server",
        Version = "1.0.0"
    },

};

// MCP Services 
builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();

var app = builder.Build();

// ✅ HTTP MCP Endpoint 
app.MapMcp("/mcp");


Console.WriteLine("🚀 MCP Server on http://localhost:5000/mcp");
Console.WriteLine("Press Ctrl+C to exit");

await app.RunAsync();