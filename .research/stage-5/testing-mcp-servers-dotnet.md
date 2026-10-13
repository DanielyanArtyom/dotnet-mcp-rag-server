# Testing an ASP.NET Core-hosted MCP server in .NET (in-process, end to end)

Sources current as of **2026-10-09**. SDK source read at tag **`v2.2.0`** (commit `6fa3825`, the current NuGet release of `ModelContextProtocol` / `.Core` / `.AspNetCore`, published 2026-08-13; no newer version on NuGet as of today) and compared against `main` at `3338e88` (2026-10-05). Every claim below that says "verified by spike" was run on 2026-10-09 in a throwaway project (`ModelContextProtocol.AspNetCore` 2.2.0 + `Microsoft.AspNetCore.Mvc.Testing` 10.0.12 + xUnit 2.9.3, .NET SDK 10.0.401, i.e. the same test stack Lumen uses). The spike was not added to the repo.

Abbreviation: `T/` = `https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.2.0/`.

---

## 0. Big change since `.research/mcp-csharp-sdk.md` was written: the 2026-07-28 protocol revision

This changes how every test below behaves, so it comes first.

- MCP protocol revision **`2026-07-28`** removed the `initialize` handshake (SEP-2575) and the `Mcp-Session-Id` header (SEP-2567). **Streamable HTTP no longer has sessions.** Clients start with `server/discover`, and every request carries `MCP-Protocol-Version` plus the new required headers `Mcp-Method` (and `Mcp-Name` for `tools/call`). Source: [`T/src/Common/McpProtocolVersions.cs`](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.2.0/src/Common/McpProtocolVersions.cs) (doc comment on `July2026ProtocolVersion`), [`T/src/Common/McpHttpHeaders.cs`](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.2.0/src/Common/McpHttpHeaders.cs).
- SDK 2.2.0 supports `2024-11-05`, `2025-03-26`, `2025-06-18`, `2025-11-25` (initialize handshake) **and** `2026-07-28` (per-request metadata). The client prefers `2026-07-28` by default (same file).
- **`HttpServerTransportOptions.SessionMode` now defaults to `HttpServerSessionMode.Stateless`** ([`T/src/ModelContextProtocol.AspNetCore/HttpServerTransportOptions.cs`](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.2.0/src/ModelContextProtocol.AspNetCore/HttpServerTransportOptions.cs) line 92). The old `bool Stateless` is still there as a shorthand (true means `Stateless`, false means `Stateful`). A third mode, `StatefulForInitializeClients`, gives sessions to old clients and serves `2026-07-28` clients statelessly ([`T/src/ModelContextProtocol.AspNetCore/HttpServerSessionMode.cs`](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.2.0/src/ModelContextProtocol.AspNetCore/HttpServerSessionMode.cs)).
- The stateful-only knobs (`IdleTimeout`, `MaxIdleSessionCount`, `EventStreamStore`, `SessionMigrationHandler`, `PerSessionExecutionContext`) are `[Obsolete]` with diagnostic **`MCP9006`**, and `EnableLegacySse` is off by default and obsolete with **`MCP9004`** ([`T/docs/list-of-diagnostics.md`](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.2.0/docs/list-of-diagnostics.md) line 47; [`T/docs/concepts/stateless/stateless.md`](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.2.0/docs/concepts/stateless/stateless.md), sections "The 2026-07-28 protocol revision" and "Migrating from legacy SSE").
- The SDK's own docs recommend that every server set `SessionMode` **explicitly**, so a future default change can't silently change behaviour (stateless.md, "Forward and backward compatibility").
- **Client-side fallback.** Over HTTP, the client sends its first request with `MCP-Protocol-Version: 2026-07-28`. If the server answers HTTP 400 without a structured `-32022/-32021/-32020` JSON-RPC error, the client falls back to `initialize` on the same endpoint. A **Stateful** server refuses `2026-07-28` with `-32022 UnsupportedProtocolVersion`, so the client downgrades to `2025-11-25` and gets a session (stateless.md, same section).
  - **Verified by spike:** against a Stateless server, `client.NegotiatedProtocolVersion == "2026-07-28"` and `SessionId == null`.
  - **Verified by spike:** against a Stateful server, the negotiated version is `"2025-11-25"` and `SessionId` is a non-null id.

Consequence for the test design: a "connect, then call a tool" test written today against a default (stateless) server never sends `initialize` and never sees a session id. Older blog posts and snippets that hand-craft `initialize` plus `Mcp-Session-Id` describe the legacy path.

---

## 1. Creating an MCP client against `WebApplicationFactory<Program>`

### How the SDK's own tests do it (no TestServer)

The SDK's ASP.NET Core tests **do not use `WebApplicationFactory` or `TestServer` anywhere**. A grep of the whole repo (v2.2.0 and `main`) finds no `UseTestServer`, `Microsoft.AspNetCore.TestHost` or `WebApplicationFactory`. Instead they run **real Kestrel over an in-memory connection listener**:

- [`T/tests/ModelContextProtocol.AspNetCore.Tests/Utils/KestrelInMemoryTest.cs`](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.2.0/tests/ModelContextProtocol.AspNetCore.Tests/Utils/KestrelInMemoryTest.cs) builds `WebApplication.CreateEmptyBuilder`, calls `UseKestrelCore()`, registers a custom `IConnectionListenerFactory` ([`KestrelInMemoryTransport.cs`](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.2.0/tests/ModelContextProtocol.AspNetCore.Tests/Utils/KestrelInMemoryTransport.cs), pipe-backed [`KestrelInMemoryConnection.cs`](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.2.0/tests/ModelContextProtocol.AspNetCore.Tests/Utils/KestrelInMemoryConnection.cs)), and points a `SocketsHttpHandler.ConnectCallback` at it. The `HttpClient` has `BaseAddress = http://localhost:5000/` and `Timeout = 60s`.
- The client is created with the public `HttpClientTransport` ctor that **takes an existing `HttpClient`**. From [`T/tests/ModelContextProtocol.AspNetCore.Tests/MapMcpTests.cs`](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.2.0/tests/ModelContextProtocol.AspNetCore.Tests/MapMcpTests.cs) lines 27-43:

```csharp
await using var transport = new HttpClientTransport(new HttpClientTransportOptions
{
    Endpoint = new Uri($"http://localhost:5000{path}"),
    TransportMode = UseStreamableHttp ? HttpTransportMode.StreamableHttp : HttpTransportMode.Sse,
}, HttpClient, LoggerFactory);

return await McpClient.CreateAsync(transport, clientOptions, LoggerFactory, TestContext.Current.CancellationToken);
```

The same suite is parametrised three ways ([`MapMcpStreamableHttpTests.cs`](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.2.0/tests/ModelContextProtocol.AspNetCore.Tests/MapMcpStreamableHttpTests.cs), [`MapMcpStatelessTests.cs`](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.2.0/tests/ModelContextProtocol.AspNetCore.Tests/MapMcpStatelessTests.cs), [`MapMcpSseTests.cs`](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.2.0/tests/ModelContextProtocol.AspNetCore.Tests/MapMcpSseTests.cs)) by overriding `UseStreamableHttp` and `Stateless`.

### Using `WebApplicationFactory` instead (works, verified by spike)

`HttpClientTransport` only needs *an* `HttpClient`. `factory.CreateClient()` returns one wired to the in-memory TestServer handler, with `BaseAddress = http://localhost`, auto-redirect on and cookies on ([Microsoft Learn, Integration tests in ASP.NET Core](https://learn.microsoft.com/aspnet/core/test/integration-tests?view=aspnetcore-10.0), ms.date 2026-09-18). Public API, from [`T/src/ModelContextProtocol.Core/Client/HttpClientTransport.cs`](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.2.0/src/ModelContextProtocol.Core/Client/HttpClientTransport.cs) line 45:

```csharp
public HttpClientTransport(HttpClientTransportOptions transportOptions, HttpClient httpClient,
                           ILoggerFactory? loggerFactory = null, bool ownsHttpClient = false)
```

The pattern below was verified by spike (stateless server, JWT bearer, `MapMcp("/mcp").RequireAuthorization()`):

```csharp
var http = factory.CreateClient();
var transport = new HttpClientTransport(new HttpClientTransportOptions
{
    Endpoint = new Uri(http.BaseAddress!, "/mcp"),          // must be absolute, http/https
    TransportMode = HttpTransportMode.StreamableHttp,        // skip AutoDetect's SSE fallback
    AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" },
}, http, ownsHttpClient: true);

await using var client = await McpClient.CreateAsync(transport,
    new McpClientOptions { DiscoverProbeTimeout = TimeSpan.FromSeconds(30) });
```

- **`Endpoint` must be absolute** with an http/https scheme, or the setter throws ([`T/src/ModelContextProtocol.Core/Client/HttpClientTransportOptions.cs`](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.2.0/src/ModelContextProtocol.Core/Client/HttpClientTransportOptions.cs) lines 15-35). A relative `"/mcp"` does not work, so build it from `http.BaseAddress`.
- **Authorization header.** Two options work:
  - `AdditionalHeaders`, which is copied onto every POST/GET/DELETE via `TryAddWithoutValidation` (`CopyAdditionalHeaders` in [`T/src/ModelContextProtocol.Core/Client/StreamableHttpClientSessionTransport.cs`](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.2.0/src/ModelContextProtocol.Core/Client/StreamableHttpClientSessionTransport.cs) lines 538-572).
  - `http.DefaultRequestHeaders.Authorization`, which the existing `TestAuth.AuthenticateAsync` sets. The transport sends through that same `HttpClient`, so default headers apply.

  Use one, not both. There's also an `OAuth` option (`ClientOAuthOptions`) for a real OAuth flow, which Lumen doesn't need.
- **Transport mode.** `HttpTransportMode` has `AutoDetect` (the default), `StreamableHttp` and `Sse` ([`T/src/ModelContextProtocol.Core/Client/HttpTransportMode.cs`](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.2.0/src/ModelContextProtocol.Core/Client/HttpTransportMode.cs)). `AutoDetect` tries Streamable HTTP. On a non-2xx without a JSON-RPC error body, it falls back to legacy SSE ([`T/src/ModelContextProtocol.Core/Client/AutoDetectingClientSessionTransport.cs`](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.2.0/src/ModelContextProtocol.Core/Client/AutoDetectingClientSessionTransport.cs) lines 62-118). On a 401 it therefore makes a pointless SSE attempt, then rethrows the original 401 as `HttpRequestException` with the SSE error as inner exception. **Verified by spike:** `StatusCode == Unauthorized`, `InnerException` is a second `HttpRequestException`. Pin `StreamableHttp` in tests. Lumen will not enable legacy SSE, so SSE mode is irrelevant.
- **No more `SseClientTransport` / `McpClientFactory`.** Neither type exists in v2.2.0 `src/`. The entry point is `McpClient.CreateAsync(IClientTransport, McpClientOptions?, ILoggerFactory?, CancellationToken)` ([`T/src/ModelContextProtocol.Core/Client/McpClient.Methods.cs`](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.2.0/src/ModelContextProtocol.Core/Client/McpClient.Methods.cs) line 40), and HTTP (both modes) goes through `HttpClientTransport`. Blog posts that use `McpClientFactory.CreateAsync` or `SseClientTransport` are pre-1.0.
- **Package for the test project:** only `ModelContextProtocol.Core` is needed for the client types. Lumen.IntegrationTests also gets it transitively once it references `Lumen.Api`, which will reference `ModelContextProtocol.AspNetCore`.

### A retired Microsoft sample to *not* copy

`dotnet/skills` used to ship an `mcp-csharp-test` skill with a WebApplicationFactory example. It was retired on 2026-07-29 ([commit `85cd1034`, PR #943](https://github.com/dotnet/skills/pull/943)). Its snippet POSTed `initialize` via `client.PostAsJsonAsync("/mcp", …)` **without** an `Accept: application/json, text/event-stream` header. That returns **406** (see §4), so the example was wrong even before 2026-07-28.

---

## 2. Calling tools and reading results

The signatures below are from [`McpClient.Methods.cs`](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.2.0/src/ModelContextProtocol.Core/Client/McpClient.Methods.cs) lines 189, 970, 1040.

```csharp
IList<McpClientTool> tools = await client.ListToolsAsync();          // McpClientTool : AIFunction, has .Name
CallToolResult r = await client.CallToolAsync("search",
    new Dictionary<string, object?> { ["query"] = "net sales", ["topK"] = 5 });
// overload: CallToolAsync(CallToolRequestParams, CancellationToken)
```

`CallToolResult` ([`T/src/ModelContextProtocol.Core/Protocol/CallToolResult.cs`](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.2.0/src/ModelContextProtocol.Core/Protocol/CallToolResult.cs)) has:

- `IList<ContentBlock> Content`. Text arrives as `TextContentBlock { Text }`.
- `JsonElement? StructuredContent`
- `bool? IsError`

The SDK's own assertion style, from [`AuthorizeAttributeTests.cs`](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.2.0/tests/ModelContextProtocol.AspNetCore.Tests/AuthorizeAttributeTests.cs) lines 56-58:

```csharp
Assert.False(result.IsError ?? false);
var content = Assert.Single(result.Content.OfType<TextContentBlock>());
```

- **Structured content.** With `[McpServerTool(UseStructuredContent = true)]` on a method that returns a record, the result carries both a `StructuredContent` JSON object and a text block holding the same JSON. **Verified by spike:** `structured={"tenant":"A","count":3}` and `text={"tenant":"A","count":3}`. The tool also advertises an `outputSchema` ([`T/src/ModelContextProtocol.Core/Server/McpServerToolAttribute.cs`](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.2.0/src/ModelContextProtocol.Core/Server/McpServerToolAttribute.cs) lines 262-286). For assertions, `r.StructuredContent!.Value.Deserialize<T>()` is the cleanest path.
- **How errors surface** ([`T/src/ModelContextProtocol.Core/Server/McpServerImpl.cs`](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.2.0/src/ModelContextProtocol.Core/Server/McpServerImpl.cs) lines 1689-1731):
  - Any exception thrown by a tool **except** `McpProtocolException`, request cancellation or `InputRequiredException` becomes `CallToolResult { IsError = true }`.
  - For a plain exception the text is the generic `"An error occurred invoking '<tool>'."`. The message is **hidden**. **Verified by spike.**
  - For an `McpException` the message is appended.
  - A thrown `McpProtocolException`, and authorization failures from `[Authorize]` filters, reach the client as a **thrown `McpProtocolException`**, not as `IsError`. See `AuthorizeAttributeTests.Authorize_Tool_RequiresAuthentication`, which asserts `ErrorCode == McpErrorCode.InvalidRequest` and message `"Request failed (remote): Access forbidden: This tool requires authorization."` (lines 32-46).
  - So an "invalid argument" test asserts on `IsError == true`, and a "wrong role" test asserts `ThrowsAsync<McpProtocolException>`.

---

## 3. Unit-testing tool classes vs in-memory transports vs HTTP

| Level | How | Pros | Cons |
|---|---|---|---|
| **Plain unit test of the tool class** | `new SearchTools(fakeSender, ...)`, then call the method directly | Fastest. No SDK in the loop. Fits `Lumen.UnitTests`' Moq style | Proves nothing about the MCP wire format, JSON schema/argument binding, auth, or **where the tenant comes from**. It can't be the isolation test |
| **`McpServerTool.InvokeAsync` directly** | `McpServerTool.Create(...)` plus a `RequestContext<CallToolRequestParams>`, as in [`T/tests/ModelContextProtocol.Tests/Server/McpServerToolTests.cs`](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.2.0/tests/ModelContextProtocol.Tests/Server/McpServerToolTests.cs) lines 474-481 | Tests argument binding and structured output without a transport | Constructing a `RequestContext` needs SDK internals and a server instance. Low value for Lumen |
| **In-memory stream transport** | Server: `.WithStreamServerTransport(clientToServer.Reader.AsStream(), serverToClient.Writer.AsStream())`. Client: `new StreamClientTransport(serverInput, serverOutput)`. This is the SDK's [`T/tests/ModelContextProtocol.Tests/ClientServerTestBase.cs`](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.2.0/tests/ModelContextProtocol.Tests/ClientServerTestBase.cs) | Full JSON-RPC round trip, real `McpClient`, milliseconds | **No HTTP pipeline at all:** no JWT, no `UseAuthentication`, no tenant middleware, no `HttpContext`. `ClientServerTestBase` is a *test-project file*, not a NuGet package, so you'd copy it. It also uses xUnit v3's `TestContext` |
| **HTTP via `WebApplicationFactory`** | §1 | Exercises the real `Program`: JWT validation, `TenantResolutionMiddleware`, `MapMcp().RequireAuthorization()`, EF global filters, raw-SQL search. The same infrastructure as every other Lumen isolation test | Slower (host start per factory), needs Postgres via Testcontainers. Nobody upstream tests it this way (see "Uncertain") |

For Lumen the tenant comes from the JWT through middleware, so **only the HTTP level can prove tenant isolation**. The stream-transport level is not worth adding: it would duplicate unit-test coverage without the auth pipeline.

---

## 4. Testing auth, and the raw HTTP shape of an MCP request

### What the SDK tests for

- **Endpoint-level auth.** `app.MapMcp(path).RequireAuthorization(...)`, as in [`T/tests/ModelContextProtocol.AspNetCore.Tests/OAuth/OAuthTestBase.cs`](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.2.0/tests/ModelContextProtocol.AspNetCore.Tests/OAuth/OAuthTestBase.cs) line 101. Without a token, `McpClient.CreateAsync` throws `HttpRequestException` with `StatusCode == HttpStatusCode.Unauthorized` ([`OAuth/AuthTests.cs`](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.2.0/tests/ModelContextProtocol.AspNetCore.Tests/OAuth/AuthTests.cs) `CannotAuthenticate_WithoutOAuthConfiguration`, lines 250-264). The raw `WWW-Authenticate` header is asserted in [`OAuth/McpAuthenticationHandlerTests.cs`](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.2.0/tests/ModelContextProtocol.AspNetCore.Tests/OAuth/McpAuthenticationHandlerTests.cs) lines 27-33. That test uses the SDK's `AddMcp()` auth scheme, which adds `resource_metadata="…"` for OAuth discovery. Lumen uses plain `AddJwtBearer`, so it only gets `Bearer` / `Bearer error="invalid_token"…`.
- **Per-tool `[Authorize]` / `[Authorize(Roles = "...")]`** needs `.AddAuthorizationFilters()` on the MCP builder. Without it, `tools/list` throws `InvalidOperationException` (`ListTools_WithoutAuthFilters_ThrowsInvalidOperationException`). Unauthorized tools are **filtered out of `tools/list`** (`ListTools_UserRole_DoesNotReturnAdminTools`), and calling one throws `McpProtocolException(InvalidRequest)` ([`AuthorizeAttributeTests.cs`](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.2.0/tests/ModelContextProtocol.AspNetCore.Tests/AuthorizeAttributeTests.cs) lines 182-196, 254-266, 357-372, 585-609). This is the "wrong role" test shape if Lumen exposes an Admin-only tool.

### Verified by spike: Lumen-style `AddJwtBearer` plus `RequireAuthorization()`

| Request | Result |
|---|---|
| No `Authorization` | `401`, `WWW-Authenticate: Bearer` |
| Garbage token, via `McpClient.CreateAsync` | throws `HttpRequestException`, `StatusCode = Unauthorized` |
| Token that expired **1 minute** ago | **`200`**, because the default JwtBearer `ClockSkew` is 5 minutes |
| Token that expired **10 minutes** ago | `401`, `WWW-Authenticate: Bearer error="invalid_token", error_description="The token expired at '…'"` |

So an "expired token" test must mint a token with `expires` more than 5 minutes in the past (Lumen's `JwtOptions` has no `ClockSkew` override, see `AddAuthentication` in `src/Lumen.Api/Extensions/WebApplicationBuilderExtensions.cs`), or set `ClockSkew = TimeSpan.Zero`.

### Raw HTTP: required headers and bodies

These come from [`T/tests/ModelContextProtocol.AspNetCore.Tests/StreamableHttpServerConformanceTests.cs`](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.2.0/tests/ModelContextProtocol.AspNetCore.Tests/StreamableHttpServerConformanceTests.cs).

- **`Accept: application/json, text/event-stream` is required on POST.** With a single specific `Accept` value the server returns **406** (`PostRequest_IsNotAcceptable_WithSingleSpecificAcceptHeader`, lines 117-128). **Verified by spike:** POST without `Accept` returns 406.
- `Content-Type: application/json` is required, otherwise **415** (lines 106-113).
- An invalid `MCP-Protocol-Version` header gives **400**. A missing one is accepted for `initialize` (lines 174-205).

**Modern (`2026-07-28`) shape.** This is what the client sends to a Stateless server. The `server/discover` body (line 1058):

```json
{"jsonrpc":"2.0","id":1,"method":"server/discover","params":{"_meta":{
  "io.modelcontextprotocol/protocolVersion":"2026-07-28",
  "io.modelcontextprotocol/clientInfo":{"name":"IntegrationTestClient","version":"1.0.0"},
  "io.modelcontextprotocol/clientCapabilities":{}}}}
```

A `tools/call` sends the same `_meta` inside `params`, plus the headers `MCP-Protocol-Version: 2026-07-28`, `Mcp-Method: tools/call` and `Mcp-Name: <tool>` (lines 1007-1021, `CallTool(..., includePerRequestMetadata: true)` at lines 1124-1131). A mismatched `Mcp-Method` gives **400** (lines 992-1005).

**Verified by spike:** a single raw `tools/call` like that, with a valid bearer token and **no prior `server/discover`**, returns `200`, `Content-Type: text/event-stream`, no `Mcp-Session-Id`, and the body:

```
event: message
data: {"result":{"content":[{"type":"text","text":"tenant=A"}],"resultType":"complete",...},"id":7,"jsonrpc":"2.0"}
```

**Legacy (`initialize`) shape** (line 1096):

```json
{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26","capabilities":{},"clientInfo":{"name":"IntegrationTestClient","version":"1.0.0"}}}
```

- Against a **Stateful** server, the reply carries `Mcp-Session-Id`, which must be echoed on later requests.
- Against a **Stateless** server, `initialize` still answers. **Verified by spike:** `200`, `protocolVersion "2025-11-25"`, and **no** session header.

**Responses are SSE, not plain JSON.** Even a single reply comes back as `text/event-stream` with `X-Accel-Buffering: no` (lines 95-104). 2.2.0 has no "JSON response mode" option on the server. To parse it in a raw test, use `System.Net.ServerSentEvents.SseParser` (in-box on .NET 10) the way `ReadSseAsync` does (lines 1074-1093).

**Takeaway:** for Lumen, write auth tests that need status codes or headers (401, `WWW-Authenticate`) as raw `HttpClient` POSTs. Write everything about tool behaviour through `McpClient`. Hand-rolled JSON-RPC beyond a 401 probe is brittle against protocol revisions.

---

## 5. Stateless vs stateful: effects on tests

| | Stateless (2.2.0 default) | Stateful |
|---|---|---|
| Negotiated version (SDK client) | `2026-07-28` (spike) | `2025-11-25` after a `-32022` downgrade (spike) |
| `client.SessionId` / `Mcp-Session-Id` | `null` / never sent | set / must be echoed |
| GET / DELETE endpoints, server→client requests, unsolicited notifications | not mapped / not available (stateless.md "What stateless mode changes") | available |
| User-change check across requests | n/a | a different user on the same session gets **403** ([`MapMcpTests.Messages_FromNewUser_AreRejected`](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.2.0/tests/ModelContextProtocol.AspNetCore.Tests/MapMcpTests.cs) lines 81-110; [`T/docs/concepts/httpcontext/httpcontext.md`](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.2.0/docs/concepts/httpcontext/httpcontext.md) note) |
| **DI scope tool services resolve from** | **`HttpContext.RequestServices`**, the same scope the auth and tenant middleware ran in, with `ScopeRequests = false` | a **fresh scope** from the *application* root provider per request (`ScopeRequests = true`) |

The last row is in [`T/src/ModelContextProtocol.AspNetCore/StreamableHttpHandler.cs`](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.2.0/src/ModelContextProtocol.AspNetCore/StreamableHttpHandler.cs) lines 547-559 and [`T/src/ModelContextProtocol.Core/Server/McpServerOptions.cs`](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.2.0/src/ModelContextProtocol.Core/Server/McpServerOptions.cs) line 87.

**This matters a lot for Lumen.** `TenantResolutionMiddleware` writes the tenant into the *scoped* `CurrentTenant` from `HttpContext.RequestServices`. **Verified by spike** (the same middleware pattern, with a tool that has `CurrentTenant` constructor-injected):

- **Stateless:** the tool sees `tenant=A`.
- **Stateful:** the tool sees `tenant=<null>`, because it resolved a new `CurrentTenant` from a different scope.

In Lumen, a null tenant would be `Guid.Empty`, which makes every EF query filter and the raw-SQL `WHERE c."TenantId" = @tenantId` return **zero rows**.

- **Danger:** a naive "tenant B never sees tenant A's chunks" test would **pass vacuously** in stateful mode.
- **Fix in the test:** the isolation test must also assert tenant B's search returns **its own** chunk (a positive control).
- **Fix in the server:** pin `SessionMode = HttpServerSessionMode.Stateless` explicitly.

`IHttpContextAccessor` / an injected `ClaimsPrincipal` parameter work in both modes (`Can_UseIHttpContextAccessor_InTool`, `ClaimsPrincipal_CanBeInjected_IntoToolMethod` in `MapMcpTests.cs` lines 46-136), but that is beside the point for Lumen's design.

---

## 6. Gotchas

- **TestServer and SSE buffering: not a problem.** TestServer's response body is a pipe. The client's `HttpResponseMessage` is released on the server's first write or flush (`ReturnResponseMessageAsync` in [aspnetcore `release/10.0` `src/Hosting/TestHost/src/HttpContextBuilder.cs`](https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Hosting/TestHost/src/HttpContextBuilder.cs); first-write hook in [`ResponseBodyPipeWriter.cs`](https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Hosting/TestHost/src/ResponseBodyPipeWriter.cs)). It does not wait for request completion. The MCP client reads with `HttpCompletionOption.ResponseHeadersRead` ([`McpHttpClient.cs`](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.2.0/src/ModelContextProtocol.Core/Client/McpHttpClient.cs)). In stateless mode every POST stream also *ends* after its single reply, so even full buffering would not hang. Long-lived GET streams (stateful mode only) are the only case where TestServer's semantics could matter, and Lumen won't have them. **Verified by spike:** all calls complete in under 100 ms each.
- **Timeouts.**
  - `McpClientOptions.InitializationTimeout` defaults to 60 s.
  - `DiscoverProbeTimeout` defaults to **5 s**, and **applies to HTTP too** when the client prefers `2026-07-28`. When it elapses the client silently falls back to `initialize` ([`McpClientOptions.cs`](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.2.0/src/ModelContextProtocol.Core/Client/McpClientOptions.cs) lines 74-140).
  - The SDK's own test helper raises it to 60 s "to avoid CI slowness spuriously tripping it (issue #1701)" (`ClientServerTestBase.cs` lines 96-98). Do the same. The first request into a cold `WebApplicationFactory` host on a slow CI box can be slow.
  - `HttpClientTransportOptions.ConnectionTimeout` defaults to 30 s and only matters for SSE.
- **Pin `McpClientOptions.ProtocolVersion = "2026-07-28"`** in tests that assert the modern path. A non-null value is also the *minimum*, so the client throws `McpException` instead of silently downgrading (stateless.md, "Opting out of fallback").
- **Disposal.**
  - `McpClient` is `IAsyncDisposable`, so use `await using`.
  - `HttpClientTransport` disposes the `HttpClient` only when `ownsHttpClient: true`.
  - In stateless mode there is no session and so no `DELETE` on dispose (`OwnsSession` only matters with a session id).
  - `WebApplicationFactory` disposal is already handled in each test class's `DisposeAsync`, as in `AskTenantIsolationTests`.
- **xUnit parallelisation.** All Lumen DB tests share `[Collection("Postgres collection")]`, so they run **serially** within that collection and share one container. A per-class `WebApplicationFactory` (the existing pattern) is fine. Isolation between tests comes from fresh tenant GUIDs and unique emails (`TestDataSeeder`), not from DB resets. Nothing MCP-specific changes this. Stateless MCP keeps no server-side state between tests.
- **xUnit v2 vs v3.** The SDK's tests use xUnit **v3** (`TestContext.Current.CancellationToken`, `Assert.SkipWhen`, from [`ModelContextProtocol.AspNetCore.Tests.csproj`](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.2.0/tests/ModelContextProtocol.AspNetCore.Tests/ModelContextProtocol.AspNetCore.Tests.csproj)). Lumen is on xUnit 2.9.3, so copy patterns but pass `CancellationToken`s yourself, e.g. a `CancellationTokenSource(TimeSpan.FromSeconds(60))`.
- **Version compatibility (.NET 10).**
  - `ModelContextProtocol.AspNetCore` 2.2.0 targets net8/9/10 and depends on `ModelContextProtocol` `[2.2.0]`.
  - `ModelContextProtocol.Core` 2.2.0 (net10.0) depends on `Microsoft.Extensions.AI.Abstractions` ≥ 10.8.3 and `Microsoft.Extensions.Logging.Abstractions` ≥ 10.0.10 ([nuspec on nuget.org](https://api.nuget.org/v3-flatcontainer/modelcontextprotocol.core/2.2.0/modelcontextprotocol.core.nuspec)).
  - Lumen's `Microsoft.Extensions.AI` 10.10.0 satisfies the lower bound.
  - The spike built and ran cleanly on SDK 10.0.401 / runtime 10.0.12 with `Mvc.Testing` 10.0.12.
- **`MCP9006` warning.** Setting any stateful knob triggers it. The SDK's test csproj suppresses it with `<NoWarn>`. Lumen only sets `SessionMode`, which is not obsolete, so it should get no warning.
- **`main` after 2.2.0** includes [#1855](https://github.com/modelcontextprotocol/csharp-sdk/commit/c40ee04) (2026-09-18): the client skips the SSE fallback when the `server/discover` probe gets a 400/404. Irrelevant if tests pin `StreamableHttp`, but it explains noisy logs with `AutoDetect` on 2.2.0.

---

## 7. Scripted manual verification: MCP Inspector CLI

The Inspector is now **v2** (`@modelcontextprotocol/inspector` **2.10.1**, published 2026-10-08 per the npm registry). One package provides Web, **CLI**, TUI and an experimental `mcpdo` ([README](https://github.com/modelcontextprotocol/inspector/blob/main/README.md)). v2 changed exit codes, argument order and the `--` separator relative to v1, so older blog snippets may be wrong ([`docs/cli-smoke-testing.md`](https://github.com/modelcontextprotocol/inspector/blob/main/docs/cli-smoke-testing.md)). It needs Node ≥ 22.19.0.

```bash
TOKEN=$(curl -s -X POST http://localhost:5xxx/api/auth/token -H 'Content-Type: application/json' \
  -d '{"email":"admin@acme.test","password":"Passw0rd!"}' | jq -r .token)

npx --yes @modelcontextprotocol/inspector@2.10.1 --cli \
  --transport http --server-url http://localhost:5xxx/mcp \
  --header "Authorization: Bearer $TOKEN" \
  --method tools/list --format json | jq -e '.result.tools | map(.name) | index("search")'

npx --yes @modelcontextprotocol/inspector@2.10.1 --cli \
  --transport http --server-url http://localhost:5xxx/mcp --header "Authorization: Bearer $TOKEN" \
  --method tools/call --tool-name search --tool-args-json '{"query":"total net sales","topK":3}' --format json
```

- **Probes and flags.**
  - `--method initialize` is a connect-only probe.
  - `--protocol-era legacy|auto|modern` forces the era.
  - `--connect-timeout <ms>` defaults to 15000.
  - **Prefer `--tool-args-json` over `--tool-arg k=v`.** The latter JSON-coerces values, so digit-only strings become numbers.

  Sources: [`clients/cli/README.md`](https://github.com/modelcontextprotocol/inspector/blob/main/clients/cli/README.md), cli-smoke-testing.md §1-4.
- **Exit codes:**

  | Code | Meaning |
  |---|---|
  | `0` | ok |
  | `1` | usage / unexpected |
  | `3` | auth required (401/403) |
  | `4` | unreachable |
  | `5` | tool `isError:true` or tool not found |

  Source: cli README "Exit codes & error envelopes". A no-token run should therefore exit **3**, a cheap manual auth check. Pin the exact version with `npx --yes` (the doc's own CI advice).
- **What the CLI is good for:** a manual cross-tenant smoke check. Get tokens for `admin@acme.test` and `admin@globex.test`, run the same `search` call with each, and compare `documentId`s. That fits the "user runs the experiment" rule in CLAUDE.md. It doesn't replace the integration test.

---

## Uncertain / could not verify

- **No upstream test uses `WebApplicationFactory`/TestServer with the MCP client.** The pattern in §1 rests on the public `HttpClientTransport(…, HttpClient, …)` API plus my own spike (stateless, a handful of calls). Neither the SDK nor Microsoft Learn documents it. Behaviour under heavy concurrency, or with stateful GET streams over TestServer, was not tested.
- **The retired `dotnet/skills` example** (§1) suggests Microsoft once considered WAF the HTTP testing path, but it was retired and was wrong on `Accept`. Treat it as no evidence either way.
- **GitHub issue search was unavailable** from this environment (API host unreachable), so I could not check for open issues about TestServer + MCP. The TestServer streaming analysis in §6 comes from reading aspnetcore `release/10.0` source, not from an issue thread.
- **I didn't check how `McpClientTool` (as an `AIFunction`) behaves when invoked through `IChatClient`.** It's irrelevant for these tests.
- **The Inspector CLI** commands above are from its README on `main`, which I did not run against a real server. The `--header` flag is documented for HTTP targets, and `--protocol-era` behaviour against a stateless .NET server is untested.
- **Structured content and `IsError` on `2026-07-28`.** The spike observed `resultType":"complete"` in raw results. I didn't investigate the MRTR `resultType` variants, which matter only for elicitation and similar features Lumen won't use.

---

## Implication for Lumen

1. **Server config:** `AddMcpServer().WithHttpTransport(o => o.SessionMode = HttpServerSessionMode.Stateless)`, set explicitly per the SDK's own advice, plus `app.MapMcp("/mcp").RequireAuthorization()`. This must come after `UseAuthentication()` → `UseMiddlewares()` (tenant) → `UseAuthorization()`, which `Program.cs` already orders that way. Stateless is not just "simpler": it is the only mode in which Lumen's middleware-populated scoped `CurrentTenant` reaches tool classes (§5, spike). Record that as a DECISIONS.md candidate once it's measured in the real code.

2. **New helper `tests/Lumen.IntegrationTests/Utils/TestMcp.cs`** (static, like `TestAuth`):

   ```csharp
   public static class TestMcp
   {
       public static async Task<McpClient> ConnectAsync(WebApplicationFactory<Program> factory, string token)
       {
           var http = factory.CreateClient();
           var transport = new HttpClientTransport(new HttpClientTransportOptions
           {
               Endpoint = new Uri(http.BaseAddress!, "/mcp"),
               TransportMode = HttpTransportMode.StreamableHttp,
               AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" },
           }, http, ownsHttpClient: true);

           return await McpClient.CreateAsync(transport, new McpClientOptions
           {
               ProtocolVersion = "2026-07-28",
               DiscoverProbeTimeout = TimeSpan.FromSeconds(60),
           });
       }

       public static async Task<HttpResponseMessage> PostRawToolsListAsync(HttpClient http, string? token) { /* §4 modern body + Accept/MCP-Protocol-Version/Mcp-Method headers */ }
   }
   ```

   Tokens come from the existing `TestAuth.LoginAsync`. `ModelContextProtocol.Client` / `.Protocol` go into `GlobalUsings.cs` once used in 2+ files (CLAUDE.md rule).

3. **`TenantIsolation/McpSearchTenantIsolationTests.cs`**, cloned from `AskTenantIsolationTests`:
   - `[Collection("Postgres collection")]`, factory via `TestWebApplicationFactory.Create`.
   - Seed two Admin tenants, upload identical HTML (`SharedText`) for each through the existing `UploadAsync` shape.
   - Connect as tenant B with `TestMcp.ConnectAsync`, `CallToolAsync("search", { ["query"] = SharedText, ["topK"] = 10 })`.
   - Assert `IsError != true`.
   - Deserialize `StructuredContent` (if the tool uses `UseStructuredContent = true`), or else the text JSON, into the source list.
   - **Assert non-empty and contains `documentB`**. This positive control is mandatory because a broken tenant scope yields an empty result that would otherwise pass (§5).
   - Assert every source's `DocumentId == documentB` and none is `documentA`.
   - Optionally, a negative-control twin: tenant A's MCP search returns `documentA` only.

4. **Auth tests** (`Auth/McpAuthTests.cs` or alongside):
   - Raw POST without a token gives 401 + `WWW-Authenticate: Bearer`.
   - A token expired more than 5 minutes ago gives 401 with `error="invalid_token"`. Mint it with `JwtSecurityTokenHandler` using `factory.Services.GetRequiredService<IOptions<JwtOptions>>()`, and mind the 5-minute `ClockSkew`.
   - `McpClient.CreateAsync` with a bad token throws `HttpRequestException { StatusCode: Unauthorized }`.
   - If an Admin-only MCP tool exists, a Member gets it filtered from `ListToolsAsync`, and calling it throws `McpProtocolException(InvalidRequest)`. That needs `.AddAuthorizationFilters()` on the MCP builder.

5. **Unit tests** for tool classes (argument validation, mapping) go in `Lumen.UnitTests` with mocks, as today. They don't need any SDK transport. Skip the stream/pipe transport level entirely.

6. **Manual check for the user** (per CLAUDE.md "Working with the user"): Inspector CLI v2.10.1 commands from §7 with the two seeded admins, then MCP Inspector UI and Claude Code CLI as the two clients already settled in CLAUDE.md.
