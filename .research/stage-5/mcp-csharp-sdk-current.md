# MCP C# SDK: current state, and how to wire it into Lumen

Sources current as of **2026-10-09**. Primary sources only: NuGet registration API, GitHub releases, the repo source at tag [`v2.2.0`](https://github.com/modelcontextprotocol/csharp-sdk/tree/v2.2.0) (key files cross-checked against `main` @ `3338e88`, 2026-10-05), the repo's `docs/` folder (rendered at [csharp.sdk.modelcontextprotocol.io/v2/](https://csharp.sdk.modelcontextprotocol.io/v2/concepts/stateless/stateless.html)), and the open-issue list. This replaces the version facts in `.research/mcp-csharp-sdk.md` (2026-09-09); that note's API sketch is still broadly right, but it predates the 2.0 behaviour changes that matter most for us (stateless by default, DI scoping rules).

Short paths below (`StreamableHttpHandler.cs`, etc.) point to `src/ModelContextProtocol.AspNetCore/` or `src/ModelContextProtocol.Core/Server/` at tag `v2.2.0`.

## Question

Stage 5 exposes Lumen's retrieval as an MCP server over HTTP. Before designing it we need to know:

1. Which SDK version is current, what changed since 2.2.0, and which spec revisions it speaks.
2. Transport options: Streamable HTTP vs legacy SSE, stateless vs stateful, and what each means for Azure App Service scale-out.
3. Whether `MapMcp()` runs through the normal ASP.NET Core pipeline, and **whether a tool runs in the same DI scope as the HTTP request**. Lumen's tenant isolation depends on the scoped `CurrentTenant` that `TenantResolutionMiddleware` fills in.
4. How tools are defined: attributes, DI, return types, structured output, errors.
5. Resources and prompts.
6. Built-in authorization: Protected Resource Metadata, the `AddMcp` scheme, `[Authorize]` on tools.
7. Filters (hooks for logging and error mapping).
8. Known issues.

## Recommendation (summary)

- **Use `ModelContextProtocol.AspNetCore` 2.2.0** (the latest stable version; there is no newer release or prerelease). It needs `Microsoft.Extensions.AI.Abstractions >= 10.8.3`, and Lumen already has MEAI 10.10.0.
- **Set `SessionMode = HttpServerSessionMode.Stateless` explicitly.** It is already the default, but the docs recommend setting it. In stateless mode, tools resolve services from **`HttpContext.RequestServices` and no extra scope is created**, so the `CurrentTenant` filled in by `TenantResolutionMiddleware` is the same instance the tool's `IMediator` → handlers → `AppDbContext`/`ChunkSearchRepository` see. **Stateful mode breaks this**: each tool call gets a fresh scope from the root provider, so `TenantId` would be `Guid.Empty`. Stateless is also what makes multi-instance App Service work without sticky sessions.
- **`app.MapMcp("/mcp").RequireAuthorization()`**, mapped next to `MapControllers()`. MCP endpoints are ordinary minimal-API route endpoints, so `UseAuthentication` → `UseMiddlewares` (tenant) → `UseAuthorization` all run before the handler. The existing JwtBearer default scheme works unchanged.
- **Tools call MediatR, never repositories directly.** Use a non-static `[McpServerToolType]` class with constructor injection (`IMediator`, `IMapper`). The SDK creates one instance per call from the request's service provider, and this pattern also avoids issue #1904. `Search` and `Ask` are read-only tools. Ingest/delete should not be exposed in the first cut (if they are, mark them `[Authorize(Roles = "Admin")]` + `AddAuthorizationFilters()`).
- **Skip `AddMcp()` / Protected Resource Metadata for now.** Lumen has no OAuth authorization server, and our clients (Claude Code `--header`, MCP Inspector) send a static bearer token. PRM is only useful together with the deferred OpenIddict stretch goal.
- **Add a tenant-isolation integration test for the MCP path** that uses the SDK's own `McpClient` over `WebApplicationFactory`'s `HttpClient`. If someone later flips the endpoint to stateful mode, this test must catch it. Today it would show up as silent empty results, not a leak, because `Guid.Empty` matches no tenant.

## Findings

### 1. Versions, breaking changes, spec revisions

**1.1 Current packages.** From the NuGet registration API (`api.nuget.org/v3/registration5-gz-semver2/<id>/index.json`, queried 2026-10-09):

| Package | Latest | Published | Notable dependencies (net8.0+ group) |
|---|---|---|---|
| [`ModelContextProtocol.Core`](https://www.nuget.org/packages/ModelContextProtocol.Core) | **2.2.0** | 2026-08-13 | `Microsoft.Extensions.AI.Abstractions >= 10.8.3`, `System.Net.ServerSentEvents >= 10.0.10` |
| [`ModelContextProtocol`](https://www.nuget.org/packages/ModelContextProtocol) | **2.2.0** | 2026-08-13 | `.Core [2.2.0]`, `Microsoft.Extensions.Hosting.Abstractions >= 10.0.10`, `Microsoft.Extensions.Caching.Abstractions >= 10.0.10` |
| [`ModelContextProtocol.AspNetCore`](https://www.nuget.org/packages/ModelContextProtocol.AspNetCore) | **2.2.0** | 2026-08-13 | `ModelContextProtocol [2.2.0]` (exact pin) |

Release history: 1.4.0 (2026-06-04), 1.4.1 (2026-07-09), 2.0.0-preview.1…rc.2 (2026-06-26 … 2026-07-28), **2.0.0 (2026-07-28)**, 2.1.0 (2026-08-05), 2.2.0 (2026-08-13). Nothing has been published since 2.2.0. `main` @ `3338e88` (2026-10-05) still has `<VersionPrefix>2.2.0</VersionPrefix>` in `src/Directory.Build.props`. Reference only `ModelContextProtocol.AspNetCore`; it pulls in the other two.

**1.2 Breaking changes since 2.2.0: none.** 2.2.0 is the newest release. [v2.2.0 release notes](https://github.com/modelcontextprotocol/csharp-sdk/releases/tag/v2.2.0) (2026-08-13) contain no breaking changes. They add `HttpServerSessionMode` with hybrid `StatefulForInitializeClients` (#1796) and fix a header-decoding edge case. Unreleased `main` contains protocol-metadata validation changes in `StreamableHttpHandler.cs`/`McpServerImpl.cs` (checked by diffing the two). None of them touch the DI-scope or error-handling code quoted below.

The breaking changes that matter came in **2.0.0** ([release notes](https://github.com/modelcontextprotocol/csharp-sdk/releases/tag/v2.0.0), 2026-07-28):
- `HttpServerTransportOptions.Stateless` now **defaults to `true`** (#1610).
- Roots, Sampling and Logging APIs are deprecated, with warning `MCP9005` (#1651).
- Tasks moved to `ModelContextProtocol.Extensions.Tasks` (#1693).
- `UseStructuredContent` tools with non-object return types now emit the raw value (`structuredContent: 72`, not `{ "result": 72 }`) (#1568).
- `Tool.inputSchema` is required during deserialization (#1600).
- Several OAuth client hardening changes: PKCE S256 must be advertised, RFC 9207 issuer validation (#1605, #1700, #1613, #1591).

**1.3 Spec revisions.** [docs/versioning.md](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.2.0/docs/versioning.md): *"The 2.0.0 SDK implements the `2026-07-28` MCP specification revision while retaining compatibility with peers that negotiate `2025-11-25` and earlier."* `src/Common/McpProtocolVersions.cs` lists `2026-07-28`, `2025-11-25`, `2025-06-18`, `2025-03-26` and `2024-11-05`. The 2026-07-28 revision removes the `initialize` handshake (SEP-2575, replaced by `server/discover`) and the `Mcp-Session-Id` header (SEP-2567). Every request then carries `MCP-Protocol-Version` ([stateless.md §"The 2026-07-28 protocol revision"](https://csharp.sdk.modelcontextprotocol.io/v2/concepts/stateless/stateless.html)).

### 2. Transport: Streamable HTTP, SSE, stateless vs stateful

**2.1 Streamable HTTP is the only default transport.** Legacy SSE (`/sse` + `/message`) is off by default. `EnableLegacySse` is `[Obsolete]` (`MCP9004`), and turning it on in stateless mode throws at startup. The reason given is that SSE "requires in-memory session state" ([`HttpServerTransportOptions.cs`](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.2.0/src/ModelContextProtocol.AspNetCore/HttpServerTransportOptions.cs), [`McpEndpointRouteBuilderExtensions.cs`](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.2.0/src/ModelContextProtocol.AspNetCore/McpEndpointRouteBuilderExtensions.cs)). The XML docs also warn that SSE has "no built-in request backpressure".

**2.2 `HttpServerSessionMode`** ([`HttpServerSessionMode.cs`](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.2.0/src/ModelContextProtocol.AspNetCore/HttpServerSessionMode.cs)):

| Mode | Behaviour |
|---|---|
| `Stateless` (**default**) | `SessionId` is null and `Mcp-Session-Id` is unused. The GET, DELETE and `/sse` endpoints are not mapped. "Unsolicited server-to-client messages and all server-to-client requests are unsupported because any response might arrive at another ASP.NET Core application process. Client sampling, elicitation, and roots capabilities are disabled." Server ping is also gone. `ConfigureSessionOptions` runs **once per HTTP request**. |
| `Stateful` | An in-memory session per client, which requires session affinity. A `2026-07-28` request is refused with `-32022 UnsupportedProtocolVersion`, so the client downgrades to `initialize`. |
| `StatefulForInitializeClients` | Hybrid. `initialize`-handshake clients get sessions; `2026-07-28` clients are served statelessly on the same endpoint. |

The `bool Stateless` property is kept as a proxy over `SessionMode`.

**2.3 Stateful-only knobs are obsolete.** `IdleTimeout` (default 2 h, checked every 5 s), `MaxIdleSessionCount` (10,000), `EventStreamStore`, `SessionMigrationHandler` and `PerSessionExecutionContext` are all `[Obsolete]` with `MCP9006`, and "only apply to initialize-handshake back-compat" ([`HttpServerTransportOptions.cs`](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.2.0/src/ModelContextProtocol.AspNetCore/HttpServerTransportOptions.cs), [stateless.md](https://csharp.sdk.modelcontextprotocol.io/v2/concepts/stateless/stateless.html)). The 2.0.0 notes say "Skip the idle-tracking timer in stateless mode" (#1531).

**2.4 Scale-out implication.** The docs' comparison table puts it this way: stateless works in "Any topology — load balancer, serverless, multi-instance", while stateful "Requires session affinity (sticky sessions)", loses all sessions on restart and keeps up to 10,000 sessions × 2 h in memory ([stateless.md §Comparison](https://csharp.sdk.modelcontextprotocol.io/v2/concepts/stateless/stateless.html)). Stateless therefore needs no App Service ARR affinity and survives slot swaps and instance recycling. Clients that still send `initialize` also work in stateless mode, "for the lifetime of that single POST" (ibid.).

**2.5 Cancellation.** In stateless mode "the handler's `CancellationToken` is `HttpContext.RequestAborted`, so if a client disconnects mid-flight, the handler is cancelled immediately — identical to a standard ASP.NET Core minimal API or controller action." In stateful mode the token is tied to the session, not the request (ibid., §request backpressure / handler cancellation tokens). This matters for `/ask`-style tools that make a paid LLM call.

### 3. `MapMcp`, the middleware pipeline, and DI scope (the tenant-isolation question)

**3.1 `MapMcp(pattern = "")` returns a normal route group.** Source ([`McpEndpointRouteBuilderExtensions.cs`](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.2.0/src/ModelContextProtocol.AspNetCore/McpEndpointRouteBuilderExtensions.cs)):

```csharp
public static IEndpointConventionBuilder MapMcp(this IEndpointRouteBuilder endpoints, [StringSyntax("Route")] string pattern = "")
{
    ...
    var mcpGroup = endpoints.MapGroup(pattern);
    var streamableHttpGroup = mcpGroup.MapGroup("") ...;
    streamableHttpGroup.MapPost("", streamableHttpHandler.HandlePostRequestAsync) ...;
    if (options.SessionMode is not HttpServerSessionMode.Stateless) { /* MapGet, MapDelete, optional /sse,/message */ }
    return mcpGroup;
}
```

- **The default route is the root (`""`).** Lumen should pass `"/mcp"` so the endpoint doesn't take `/`.
- **The return value is an `IEndpointConventionBuilder` (a `RouteGroupBuilder`)**, so `.RequireAuthorization()`, `.RequireAuthorization("Policy")`, `.RequireCors(...)` and `.RequireRateLimiting(...)` all apply to every MCP endpoint. The official sample does exactly `app.MapMcp().RequireAuthorization().RequireCors("McpBrowserClient")` ([samples/ProtectedMcpServer/Program.cs](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.2.0/samples/ProtectedMcpServer/Program.cs)).
- **Every middleware registered before endpoint execution runs for MCP requests.** These are plain minimal-API endpoints, with the same `UseAuthentication()` → custom middleware → `UseAuthorization()` order as Lumen's controllers. The ProtectedMcpServer sample relies on this (`app.UseAuthentication(); app.UseAuthorization(); app.MapMcp().RequireAuthorization()`).

**3.2 DI scope per mode. This is the critical finding.** `StreamableHttpHandler.CreateSessionAsync` ([source](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.2.0/src/ModelContextProtocol.AspNetCore/StreamableHttpHandler.cs), ~line 545):

```csharp
var mcpServerServices = applicationServices;          // root provider by default
var mcpServerOptions = mcpServerOptionsSnapshot.Value;
if (serveStatelessly || ...)
{
    mcpServerOptions = mcpServerOptionsFactory.Create(Options.DefaultName);
    if (serveStatelessly)
    {
        // The session does not outlive the request in stateless mode.
        mcpServerServices = context.RequestServices;
        mcpServerOptions.ScopeRequests = false;
    }
    ...
}
var server = McpServer.Create(transport, mcpServerOptions, loggerFactory, mcpServerServices);
```

`McpServerImpl.InvokeHandlerAsync` ([source](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.2.0/src/ModelContextProtocol.Core/Server/McpServerImpl.cs)) creates a new scope only when `ScopeRequests` is true (`Services?.GetService<IServiceScopeFactory>()?.CreateAsyncScope()`). Otherwise it passes `Services` (here `HttpContext.RequestServices`) straight through. `McpServerOptions.ScopeRequests` defaults to `true` ([`McpServerOptions.cs`](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.2.0/src/ModelContextProtocol.Core/Server/McpServerOptions.cs)).

The docs say the same thing explicitly ([stateless.md §Service lifetimes and DI scopes](https://csharp.sdk.modelcontextprotocol.io/v2/concepts/stateless/stateless.html)):

| Mode | Service provider | ScopeRequests | Handler scope |
|---|---|---|---|
| Stateful HTTP | Application services | `true` (default) | New scope per handler invocation |
| **Stateless HTTP** | **`HttpContext.RequestServices`** | **`false` (forced)** | **Shared HTTP request scope** |

> "In stateless mode … **Scoped services** behave exactly like any other ASP.NET Core request-scoped service — middleware can set state on a scoped service and the tool handler will see it. The DI lifetime model is identical to a standard ASP.NET Core controller or minimal API endpoint."

The hybrid mode follows the same rule per request: `2026-07-28` requests use `RequestServices`, while `initialize`-session requests use the app provider plus a new scope (ibid., §Hybrid mode → Lifetimes).

**Consequence for Lumen:** in **Stateless** mode, `TenantResolutionMiddleware.InvokeAsync(HttpContext, CurrentTenant)` and the tool's `IMediator` → handler → `AppDbContext` / `ChunkSearchRepository` all resolve the **same** scoped `CurrentTenant`. In **Stateful** or the stateful half of hybrid mode, a tool would get a fresh `CurrentTenant` with `TenantId == Guid.Empty`. Global query filters and the raw-SQL `@tenantId` would then match nothing, so it fails closed but is silently broken. **Stateless mode is therefore a correctness requirement for Lumen, not just a scaling choice.**

**3.3 Identity inside a tool.** There are three supported routes:
- **`ClaimsPrincipal` parameter.** `RequestServiceProvider<T>` treats `RequestContext<T>`, `McpServer`, `IProgress<ProgressNotificationValue>` and `ClaimsPrincipal` as built-in injectable types (`serviceType == typeof(ClaimsPrincipal) ? request.User`) ([`RequestServiceProvider.cs`](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.2.0/src/ModelContextProtocol.Core/Server/RequestServiceProvider.cs)). The user is copied from `HttpContext.User` into `JsonRpcMessage.Context.User` when authenticated (`StreamableHttpHandler.ReadJsonRpcMessageAsync`). The docs call this "recommended" ([identity.md](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.2.0/docs/concepts/identity/identity.md)). Such parameters are excluded from the tool's input schema.
- **`IHttpContextAccessor`**, after `AddHttpContextAccessor()` ([httpcontext.md](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.2.0/docs/concepts/httpcontext/httpcontext.md)). In Streamable HTTP each tool call is its own request, so the context is fresh. It is stale only with legacy SSE.
- **`RequestContext<CallToolRequestParams>`** parameter (`.User`, `.Services`, `.Server`, `.Params`).

For Lumen, none of these is needed in the tool body. The tenant already comes from the scoped `CurrentTenant`, as it does for controllers.

**3.4 User binding (stateful only).** Sessions remember the `NameIdentifier`/`sub`/`UPN` claim of the initiating user and return 403 if a later request on the session carries a different user (`HasSameUserId`, `GetUserIdClaim` in `StreamableHttpHandler.cs`). Other claims are not re-validated ([httpcontext.md note](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.2.0/docs/concepts/httpcontext/httpcontext.md)). This does not matter in stateless mode, where every POST is authenticated on its own.

### 4. Defining tools

**4.1 Attributes.** `[McpServerToolType]` goes on the class and `[McpServerTool]` on the method. `McpServerToolAttribute` properties at v2.2.0: `Name`, `Title`, `Destructive`, `Idempotent`, `OpenWorld`, `ReadOnly`, `UseStructuredContent`, `OutputSchemaType`, `IconSource` ([`McpServerToolAttribute.cs`](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.2.0/src/ModelContextProtocol.Core/Server/McpServerToolAttribute.cs)). The four hint booleans become `tool.Annotations` (`ReadOnlyHint`, `DestructiveHint`, `IdempotentHint`, `OpenWorldHint`) and are emitted only when set ([`AIFunctionMcpServerTool.cs`](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.2.0/src/ModelContextProtocol.Core/Server/AIFunctionMcpServerTool.cs) `Create`). `[Description]` on the method sets the tool description; on a parameter it documents that schema property. The name defaults to a name derived from the method (`DeriveName`).

**4.2 Async methods and parameter binding.** Tools go through MEAI's `AIFunctionFactory.Create`, so `Task<T>`/`ValueTask<T>` returns and a `CancellationToken` parameter work. The token is bound by the factory and is the request-aborted token in stateless mode (§2.5). A parameter is treated as **DI-injected and left out of the schema** when its type is one of the augmented types (§3.3) or when `IServiceProviderIsService.IsService(type)` is true. `[FromKeyedServices]` also works. Otherwise it is a JSON argument ([`AIFunctionMcpServerTool.cs` `ConfigureParameterBinding`](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.2.0/src/ModelContextProtocol.Core/Server/AIFunctionMcpServerTool.cs)). This settles the 2.x async/DI question left unverified in the old note.

**4.3 Non-static tool classes (constructor injection).** `WithTools<T>()` registers each tool as a singleton `McpServerTool`. For instance methods the target is built **per invocation** with `ActivatorUtilities.CreateInstance(r.Services, type)`, where `r.Services` is the request's provider ([`McpServerBuilderExtensions.cs`](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.2.0/src/ModelContextProtocol/McpServerBuilderExtensions.cs) `WithTools<TToolType>` / `CreateTarget`). In stateless mode that provider is `HttpContext.RequestServices`. The tool class is not itself registered in DI, so it is effectively transient-per-call, and its scoped dependencies come from the HTTP request scope. Lumen's constructor style (traditional constructor + `private readonly` fields) works. The official sample does the same (`WeatherTools(IHttpClientFactory)`).

**4.4 `WithTools<T>()` vs `WithToolsFromAssembly()`.** The generic form is trimming/AOT-safe. `WithToolsFromAssembly(assembly?)` scans for `[McpServerToolType]` and is `[RequiresUnreferencedCode]` ("might not work in Native AOT") (same file). There is also `WithTools(object target)` for a pre-built singleton instance; avoid it here, because it would capture scoped services.

**4.5 Return types.** These come from `AIFunctionMcpServerTool.InvokeAsync`:
- `string` → one `TextContentBlock`
- `null` → empty content
- `AIContent` / `IEnumerable<AIContent>` (an `ErrorContent` sets `IsError`)
- `ContentBlock` / `IEnumerable<ContentBlock>`
- `CallToolResult` → returned unchanged
- any other object → serialized to JSON text

`UseStructuredContent = true` advertises an `outputSchema` and fills `structuredContent`. Non-object returns are emitted as raw values (2.0 change). For pre-`2026-07-28` clients the SDK wraps non-object schemas as `{ "result": … }` (`BuildLegacyWireProtocolTool`) ([tools.md §Structured content](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.2.0/docs/concepts/tools/tools.md)). `OutputSchemaType` overrides the inferred schema.

**4.6 Exceptions** (`McpServerImpl.CreateToolCallErrorResult` and the catch blocks around it, [source](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.2.0/src/ModelContextProtocol.Core/Server/McpServerImpl.cs)):

```csharp
new CallToolResult {
    IsError = true,
    Content = [new TextContentBlock {
        Text = exception is McpException
            ? $"An error occurred invoking '{request.Params?.Name}': {exception.Message}"
            : $"An error occurred invoking '{request.Params?.Name}'.",
    }],
};
```

- `McpProtocolException`, `OperationCanceledException` (when the token fired) and `InputRequiredException` (MRTR) are **re-thrown** as JSON-RPC errors.
- Any other exception becomes `isError: true`. **Only `McpException` messages reach the client.** Everything else gets a generic message so internal details don't leak ([tools.md §Error handling](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.2.0/docs/concepts/tools/tools.md)).
- **Lumen's `GlobalExceptionHandler` (`IExceptionHandler`) never sees tool exceptions**, because the SDK catches them first. For example, `ExternalServiceException` → 503 has no MCP equivalent. Mapping must be done in a call-tool filter (§7) or inside the tool.

Example in the current API:

```csharp
[McpServerToolType]
public sealed class RetrievalTools
{
    private readonly IMediator _mediator;
    public RetrievalTools(IMediator mediator) { _mediator = mediator; }

    [McpServerTool(Name = "search", Title = "Search filings", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Vector search over the caller's tenant's SEC filings. Returns ranked chunks.")]
    public async Task<string> SearchAsync(
        [Description("Natural-language query")] string query,
        [Description("Number of chunks, 1-50")] int topK = 10,
        CancellationToken cancellationToken = default)
    { /* _mediator.Send(new SearchQuery(...)) → format */ }
}
```

### 5. Resources and prompts

- **Attribute model** ([resources.md](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.2.0/docs/concepts/resources/resources.md)): `[McpServerResourceType]` + `[McpServerResource(UriTemplate = "...", Name = ..., Title = ..., MimeType = ...)]` ([`McpServerResourceAttribute.cs`](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.2.0/src/ModelContextProtocol.Core/Server/McpServerResourceAttribute.cs)). A fixed URI is a *direct* resource and shows up in `resources/list`. A URI with `{id}` (RFC 6570) is a *template* resource; it appears in `resources/templates/list` and its variables bind to method parameters. Registration: `WithResources<T>()` / `WithResourcesFromAssembly()`. Return `string`, `ResourceContents` (`TextResourceContents`/`BlobResourceContents`), etc. DI and `ClaimsPrincipal` injection work as for tools.
- **A per-tenant document list is not static.** Attribute-defined direct resources are fixed at startup. To list "this tenant's documents" in `resources/list` you would need `WithListResourcesHandler(...)` (a custom handler), or expose only a template `lumen://documents/{documentId}` plus a `list_documents` tool. Open issue [#1896](https://github.com/modelcontextprotocol/csharp-sdk/issues/1896) (2026-09-28): a template with several comma-separated variables in one expression binds the whole value to the first variable. Use one variable per expression.
- **Subscriptions / list-changed notifications need stateful mode** (stateless.md table). This doesn't matter for read-only documents.
- **Prompts:** `[McpServerPromptType]` + `[McpServerPrompt]`, `WithPrompts<T>()` / `WithPromptsFromAssembly()`, returning `ChatMessage`(s) or `GetPromptResult` ([identity.md](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.2.0/docs/concepts/identity/identity.md) shows `[McpServerPrompt]` returning `ChatMessage`).

### 6. Authorization features in the SDK

**6.1 `AddMcp()` authentication scheme with Protected Resource Metadata (RFC 9728): built in.** Sources: [`McpAuthenticationExtensions.cs`](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.2.0/src/ModelContextProtocol.AspNetCore/Authentication/McpAuthenticationExtensions.cs), [`McpAuthenticationOptions.cs`](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.2.0/src/ModelContextProtocol.AspNetCore/Authentication/McpAuthenticationOptions.cs), [`McpAuthenticationHandler.cs`](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.2.0/src/ModelContextProtocol.AspNetCore/Authentication/McpAuthenticationHandler.cs).
- `AuthenticationBuilder.AddMcp(options => …)` registers scheme `"McpAuth"` (`McpAuthenticationDefaults.AuthenticationScheme`).
- Its options **default `ForwardAuthenticate = "Bearer"`** (the comment says this is `JwtBearerDefaults.AuthenticationScheme`). `HandleAuthenticateAsync` returns `NoResult`, so it does not validate tokens itself. **It is designed to sit next to an existing JwtBearer scheme**, which is exactly Lumen's setup.
- It is an `IAuthenticationRequestHandler`, so it serves `GET /.well-known/oauth-protected-resource[/<resource path>]` with `options.ResourceMetadata` (`AuthorizationServers`, `ScopesSupported`, `ResourceDocumentation`, …). If no metadata is configured, that request throws `InvalidOperationException`.
- `HandleChallengeAsync` appends `WWW-Authenticate: Bearer resource_metadata="<abs PRM url>"`. You only get that header if `McpAuth` is the challenge scheme: either `options.DefaultChallengeScheme = McpAuthenticationDefaults.AuthenticationScheme` as in the sample, or a policy whose `AuthenticationSchemes` include `McpAuth`. The latter keeps controllers' challenges unchanged.

**6.2 `[Authorize]` on tools, prompts and resources: built in, opt-in.** `.AddAuthorizationFilters()` on the MCP builder ([`HttpMcpServerBuilderExtensions.cs`](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.2.0/src/ModelContextProtocol.AspNetCore/HttpMcpServerBuilderExtensions.cs), [`AuthorizationFilterSetup.cs`](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.2.0/src/ModelContextProtocol.AspNetCore/AuthorizationFilterSetup.cs)).
- It honors standard `Microsoft.AspNetCore.Authorization.[Authorize(Roles/Policy)]` and `[AllowAnonymous]`, at class level and method level. Class attributes are added to the metadata first (`CreateMetadata`).
- `tools/list` (and prompts/resources lists) **removes items the caller isn't authorized for**.
- `tools/call` on a forbidden tool throws `McpProtocolException("Access forbidden: This tool requires authorization.", McpErrorCode.InvalidRequest)`. That is a JSON-RPC error, not `isError`.
- Safety net: `WithHttpTransport()` always installs guard filters. If a tool has `[Authorize]` metadata and `AddAuthorizationFilters()` was **not** called, list/call throws `InvalidOperationException` instead of silently allowing access (`AuthorizationCallToolFilterGuardSetup.cs`).
- Lumen's JwtBearer sets `RoleClaimType = ClaimTypes.Role`, so `[Authorize(Roles = "Admin")]` would work as it does on controllers.
- Docs: [filters.md §Built-in authorization request filters](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.2.0/docs/concepts/filters.md), [identity.md](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.2.0/docs/concepts/identity/identity.md).

**6.3 Endpoint-level authorization** is plain ASP.NET Core: `MapMcp(...).RequireAuthorization()` (§3.1). An unauthenticated request gets 401 from the authorization middleware before the MCP handler runs. With Lumen's current JwtBearer default challenge, the 401 carries `WWW-Authenticate: Bearer` with no `resource_metadata`. That is fine for Claude Code `--header` and Inspector.

### 7. Filters and hooks

[filters.md](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.2.0/docs/concepts/filters.md):

- **`WithRequestFilters(f => f.AddCallToolFilter(next => async (ctx, ct) => …))`.** The same pattern exists for `AddListToolsFilter`, `AddReadResourceFilter`, `AddListResourcesFilter`, `AddGetPromptFilter`, `AddCompleteFilter`, etc. Filters run outermost-first in registration order.
- `ctx` is a `RequestContext<CallToolRequestParams>` exposing `.User`, `.Services` (in stateless mode, the request scope), `.Params.Name`/`.Arguments`, `.MatchedPrimitive` and `.Items`.
- **`WithMessageFilters(m => m.AddIncomingFilter(...) / AddOutgoingFilter(...))`** works at the raw JSON-RPC message level.
- Uses for Lumen:
  - (a) A call-tool filter that logs tool name, `tenant_id` and duration through NLog's `ILogger`.
  - (b) A call-tool filter that catches `ExternalServiceException` / `NotFoundException` and returns a `CallToolResult { IsError = true, Content = [...] }` with a safe message. This is the MCP counterpart of `GlobalExceptionHandler`. Alternatively, rethrow such errors as `McpException` so their message is passed through (§4.6).
- The authorization filters are alternate-result `tools/call` filters registered *before* the ordinary call-tool filters, so an unauthorized call never reaches our filters.
- **Telemetry:** the SDK emits `Activity` spans and metrics in every mode ([stateless.md §tracing](https://csharp.sdk.modelcontextprotocol.io/v2/concepts/stateless/stateless.html)).

### 8. Known issues (open as of 2026-10-09)

From [the open-issue list](https://github.com/modelcontextprotocol/csharp-sdk/issues?q=is%3Aissue+is%3Aopen+sort%3Aupdated-desc):

- **[#1904](https://github.com/modelcontextprotocol/csharp-sdk/issues/1904) (2026-10-02), "WithTools<T>() + MapMcp() keeps every host alive after disposal"**, related to [#1898](https://github.com/modelcontextprotocol/csharp-sdk/issues/1898) (2026-09-29).
  - Cause: a static cache in MEAI's `AIFunctionFactory` holds the parameter-binding delegate, which captures `McpServerToolCreateOptions.Services`, the root provider.
  - Reported impact: ~2 MB retained per host, with ~1,400 hosts in a test suite.
  - Workaround from the issue: inject dependencies through the **tool class constructor** instead of method parameters.
  - Relevance: every `WebApplicationFactory` host in Lumen's integration tests would leak a little. That is minor at our scale (tens of hosts), but it is a second reason to use constructor injection.
- **[#1899](https://github.com/modelcontextprotocol/csharp-sdk/issues/1899) (2026-10-01):** there is no sample for the `ClaimsPrincipal` injection pattern; PR #1906 is pending. This is documentation only.
- **[#1494](https://github.com/modelcontextprotocol/csharp-sdk/issues/1494) (2026-04-02):** a request for authentication architecture guidance. Still open, so there is no official multi-tenant auth guidance.
- **[#1896](https://github.com/modelcontextprotocol/csharp-sdk/issues/1896):** a resource-template variable binding bug (§5).
- **[#1785](https://github.com/modelcontextprotocol/csharp-sdk/issues/1785) "Tool Call Failed: Request timed out"** and **[#1842](https://github.com/modelcontextprotocol/csharp-sdk/issues/1842)** (instability on AWS AgentCore). These are deployment-specific and were not investigated further.
- **[#1901](https://github.com/modelcontextprotocol/csharp-sdk/issues/1901):** a client-side bug in 2.2.0 (`McpClient.ServerInfo` throws when the optional 2026-07-28 serverInfo is absent). This could affect our C# test client if it reads `ServerInfo`. Avoid touching that property in tests.
- No open issue title mentions .NET 10, stateless-mode DI scoping, or `HttpContext`. The packages target net8.0+ and depend on `*.Abstractions >= 10.0.10`, so they are .NET 10-native.

## Uncertain / could not verify

- **GitHub API search was unreachable** (DNS failures for `api.github.com` partway through). The open-issue list comes from one rendered page of the issues UI (25 most recently updated). Older open issues on these topics could exist.
- **Tool-instance disposal.** The SDK creates the tool class per call via `ActivatorUtilities`. Whether MEAI's `AIFunctionFactory` disposes that instance if it is `IDisposable` was not verified, because the MEAI source was not read. Lumen's tool class shouldn't own disposables anyway; scoped deps are disposed with the request scope.
- **Which protocol revision Claude Code CLI and MCP Inspector negotiate today** (`2026-07-28` vs `2025-11-25`) was not checked. In stateless mode both work: modern clients natively, `initialize` clients per POST. It only matters if we ever pick `Stateful`.
- **`MapMcp` was not run** against Lumen's pipeline in this research. The conclusion that middleware and scoped state are shared rests on reading the source (`context.RequestServices` + `ScopeRequests = false`) and the explicit doc statement, not on an experiment. The proposed integration test is how to confirm it.
- **Whether the `CancellationToken` parameter binds to `RequestAborted` in stateless mode** comes from the docs (stateless.md §backpressure). The binding path inside MEAI was not traced.

## Implication for Lumen

**Wiring** (the shape, not final code; it follows the existing `Extensions/` pattern):

```csharp
// WebApplicationBuilderExtensions
builder.Services.AddMcpServer()
    .WithHttpTransport(o => o.SessionMode = HttpServerSessionMode.Stateless) // REQUIRED for tenant scope sharing
    .AddAuthorizationFilters()   // only if any tool carries [Authorize(Roles=...)]
    .WithTools<RetrievalTools>()
    .WithRequestFilters(f => f.AddCallToolFilter(/* log + map ExternalServiceException */));

// Program.cs, after UseAuthentication → UseMiddlewares → UseAuthorization
app.MapControllers();
app.MapMcp("/mcp").RequireAuthorization();
```

Package: `ModelContextProtocol.AspNetCore` 2.2.0 in `Lumen.Api` only. Tools live in Api (as controllers do) and talk to Application through MediatR. DTO formatting stays in Api.

**Decide in design.md:**
1. **Tool surface.** Which tools to expose, in what shape:
   - `search` (`ReadOnly`, `Idempotent`)
   - `ask` (`ReadOnly`, but non-idempotent and costs an LLM call)
   - `list_documents` / `get_document`
   - Is any Admin-only write tool in scope? Recommendation: no.
2. **Return shape.**
   - Plain text formatted for an LLM (numbered chunks with document title and chunk id).
   - Or `UseStructuredContent = true` with typed output (sources list, `refused`/`truncated` flags for `ask`).
   - Or both. Structured output is what MCP Inspector displays nicely, and its shape after 2.0 is the raw value.
3. **Documents as resources vs tools.** A template resource `lumen://documents/{id}` is a natural fit for "read the whole filing". Listing per tenant needs a custom `WithListResourcesHandler` or a tool. Recommend tools first and resources as an optional extra.
4. **Error mapping.** A call-tool filter that maps `ExternalServiceException` to `isError: true` with a safe text, `NotFoundException` to an `McpException` message, and logs everything else. `GlobalExceptionHandler` will not see these exceptions.
5. **Endpoint authorization.** `RequireAuthorization()` with the default JwtBearer scheme, no PRM. Record "`AddMcp()` + PRM deferred with the OpenIddict stretch goal" as a DECISIONS.md item: we chose not to, and here is why.
6. **Tests.**
   - Unit-test the tool class against a mocked `IMediator`.
   - Add an integration test using `McpClient.CreateAsync(new HttpClientTransport(new() { Endpoint = …/mcp, AdditionalHeaders = { ["Authorization"] = "Bearer …" } }, factory.CreateClient()))`. The `HttpClientTransport(options, HttpClient, …)` constructor exists ([`HttpClientTransport.cs`](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.2.0/src/ModelContextProtocol.Core/Client/HttpClientTransport.cs)).
   - The test should cover: 401 without a token, `tools/list` contents, and an **MCP tenant-isolation test**: identical text in two tenants; tenant B's `search` tool never returns tenant A's chunks, and returns a non-empty result for its own data. The non-empty assertion is what catches an accidental switch to Stateful, which fails closed with empty results.
7. **Session mode is a correctness requirement.** Put a comment, or better a startup assertion, next to `SessionMode = Stateless` explaining that tenant isolation depends on the shared request scope. Under the CLAUDE.md comment rule this is a legitimate "invariant a future edit could silently break".
