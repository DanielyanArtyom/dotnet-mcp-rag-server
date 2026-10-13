# MCP C#/.NET SDK — current state

Sources current as of **2026-09-09**.

**The official Model Context Protocol C# SDK** lives at [github.com/modelcontextprotocol/csharp-sdk](https://github.com/modelcontextprotocol/csharp-sdk), owned by the `modelcontextprotocol` GitHub org, described in its own README as *"The official C# SDK for Model Context Protocol servers and clients. Maintained in collaboration with Microsoft."* — an official MCP-org project **and** an explicit Microsoft collaboration, not a purely Microsoft-internal SDK. Apache License 2.0.

## NuGet packages

| Package | Purpose | Latest version | Publish date |
|---|---|---|---|
| `ModelContextProtocol.Core` | Minimal-dependency client/low-level server APIs | 2.x (tracks main package) | — |
| **`ModelContextProtocol`** | Main package — hosting + DI extensions, references `.Core` | **2.2.0** | **2026-08-13** |
| **`ModelContextProtocol.AspNetCore`** | HTTP-based MCP server hosting (ASP.NET Core integration) | **2.2.0** | **2026-08-13** |
| `ModelContextProtocol.Extensions.Tasks` | Long-running tool invocations with status polling | — | — |
| `ModelContextProtocol.Extensions.Apps` | Interactive UI apps rendered inside MCP hosts | — | — |

Sources: [ModelContextProtocol — NuGet](https://www.nuget.org/packages/ModelContextProtocol/), [ModelContextProtocol.AspNetCore — NuGet](https://www.nuget.org/packages/ModelContextProtocol.AspNetCore) — both show **2.2.0**, published **2026-08-13** (synchronized release). Both target .NET 8/9/10 (`.Core` also targets .NET Standard 2.0).

Package name is literally **`ModelContextProtocol`** (not `Microsoft.ModelContextProtocol`), with ASP.NET Core hosting split into the companion `.AspNetCore` package.

**Primary namespaces:** `ModelContextProtocol.Server`, `ModelContextProtocol.Client`, `ModelContextProtocol.Protocol`, plus standard `Microsoft.Extensions.DependencyInjection` and `System.ComponentModel` (for `[Description]`). [Getting Started — MCP C# SDK docs](https://csharp.sdk.modelcontextprotocol.io/v1/concepts/getting-started.html).

## ASP.NET Core hosting — confirmed first-class

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddMcpServer()
    .WithHttpTransport(options => { options.Stateless = true; })
    .WithToolsFromAssembly();
var app = builder.Build();

app.MapMcp();

app.Run("http://localhost:3001");
```

- `AddMcpServer()` — DI registration on `IServiceCollection`, chained with `.WithHttpTransport(...)` (HTTP-hosted) or `.WithStdioServerTransport()` (stdio-hosted, per the original .NET team walkthrough) and `.WithToolsFromAssembly()` for tool auto-discovery.
- `MapMcp()` — endpoint-routing extension on `WebApplication`/`IEndpointRouteBuilder`, wires the MCP HTTP endpoint into the pipeline.

Confirmed from the SDK's own getting-started docs; pattern corroborated by [Build a Model Context Protocol (MCP) server in C# — .NET Blog](https://devblogs.microsoft.com/dotnet/build-a-model-context-protocol-mcp-server-in-csharp/) (2025-04-07 — predates the current 2.2.0 release and used a prerelease package, so treat its exact code as illustrative of the pattern, not a guarantee of today's exact API).

## Tools backed by async C# methods — confirmed supported

Attribute pair: `[McpServerToolType]` on the containing class, `[McpServerTool]` (optionally `[Description]`) on each tool method. Async/`Task`-returning methods are explicitly demonstrated:

```csharp
[McpServerTool, Description("Get a list of monkeys.")]
public static async Task<string> GetMonkeys(MonkeyService monkeyService)
{
    var monkeys = await monkeyService.GetMonkeys();
    return JsonSerializer.Serialize(monkeys);
}
```

[.NET Blog walkthrough](https://devblogs.microsoft.com/dotnet/build-a-model-context-protocol-mcp-server-in-csharp/) (2025-04-07). The SDK's current getting-started page shows only a synchronous `Echo` example but doesn't contradict async support; DI-driven parameter injection (`MonkeyService monkeyService` injected directly into the tool method) is consistent with the SDK's DI-aware tool invocation model.

**Uncertain / could not verify:** a fully up-to-date (2.2.0-era) async example directly from the SDK's own docs — the async claim rests on the April 2025 .NET Blog walkthrough plus the general async/DI support implied by current docs, not a same-version code sample. Worth a quick sanity check once actually wiring up Stage 5.

## Implication for Lumen

Wire up in `Program.cs` with `builder.Services.AddMcpServer().WithHttpTransport(...).WithToolsFromAssembly()`, `app.MapMcp()`. Tool classes get `[McpServerToolType]` + `[McpServerTool]`/`[Description]`, async `Task`-returning methods with constructor/parameter DI for services like the retrieval pipeline.
