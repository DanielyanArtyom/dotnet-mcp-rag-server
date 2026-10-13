# MCP authorization and target clients — how they behave today, and the minimal auth design for Lumen's MCP endpoint

Question: for Stage 5 (Lumen exposed as a remote MCP server over Streamable HTTP, protected by Lumen's own self-issued HS256 JWT from `POST /api/auth/token`, no OAuth authorization server), what does the current MCP spec require, how do MCP Inspector and Claude Code actually authenticate, is the Claude Desktop exclusion still valid, and what is the smallest correct auth design?

Sources current as of 2026-10-09. This note supersedes the client-behaviour parts of `.research/mcp-client-auth.md` (2026-09-09); that note's conclusion still holds, but some details changed (new spec revision, Claude Code `headersHelper`, Inspector v2).

## TL;DR

- **Current spec revision is `2026-07-28`** (final shipped 2026-07-28; it replaces `2025-11-25`). The big change is that the protocol is now **stateless**: no `initialize` handshake, no `Mcp-Session-Id`, no GET stream. Authorization is still **OPTIONAL**, and when an HTTP server does protect itself the spec still assumes OAuth 2.1 (a resource server with RFC 9728 metadata).
- **Using a plain bearer JWT with no OAuth metadata is allowed but doesn't conform.** The spec says HTTP implementations "SHOULD conform" to the Authorization section. If they don't, clients that auto-discover OAuth can't connect, and nothing else breaks. Claude Code and MCP Inspector both handle it fine when you configure the header yourself.
- **Claude Code**: `claude mcp add --transport http lumen <url> --header "Authorization: Bearer …"`, or `.mcp.json` with `${VAR}` expansion. A configured `Authorization` header **turns off the OAuth fallback**. **`headersHelper`** (a shell command that prints a JSON object of headers) is the documented way to handle short-lived tokens. Claude Code re-runs it on every connection, and again on a 401/403, then retries the call once. That fixes the 60-minute JWT problem without OAuth.
- **MCP Inspector** is now v2 (`2.10.1`, 2026-10-08). `--header "Authorization: Bearer …"` works in web, CLI and TUI. The CLI (`--cli --method tools/list|tools/call`) returns documented exit codes (3 means auth required), so it works for scripted checks. The web UI's Node backend proxies MCP traffic, so Lumen doesn't need CORS for Inspector.
- **Claude Desktop / claude.ai: the exclusion still holds.** #644 and #685 are both still open with no staff reply. `static_headers` is documented as "Beta, for a limited set of organizations", entered only by an org Owner, and it is an org-wide credential rather than per-user. Hosted connectors also connect from Anthropic's cloud (`160.79.104.0/21`), so they could never reach `localhost` anyway.
- **The ModelContextProtocol C# SDK 2.2.0 doesn't validate `Origin`** (checked in the source). Lumen has to add that middleware itself. The spec requires it: "Servers MUST validate the Origin header … If present and invalid … MUST respond with HTTP 403".

---

## 1. MCP specification — current revision and what Authorization requires

### 1.1 Revision

- `https://modelcontextprotocol.io/specification/latest` resolves to the **2026-07-28** revision (schema at `schema/2026-07-28/schema.ts`). [Spec index](https://modelcontextprotocol.io/specification/latest), fetched 2026-10-09.
- Changelog vs `2025-11-25`. [Key Changes](https://modelcontextprotocol.io/specification/2026-07-28/changelog):
  - "Remove protocol-level sessions and the `Mcp-Session-Id` header from the Streamable HTTP transport" (SEP-2567).
  - "Make MCP stateless: remove the `initialize`/`notifications/initialized` handshake. Every request now carries its protocol version and client capabilities in `_meta`" (SEP-2575).
  - The HTTP GET endpoint is replaced by `subscriptions/listen`. `ping` and `logging/setLevel` are removed.
  - New required headers `Mcp-Method` / `Mcp-Name` (SEP-2243).
  - **Authorization-related changes:** `iss` validation per RFC 9207 (SEP-2468); **Dynamic Client Registration deprecated** in favour of Client ID Metadata Documents (PR #2858); client credentials are bound to their issuer (SEP-2352).
- C# SDK alignment: `ModelContextProtocol.AspNetCore` **2.0.0 was published 2026-07-28, 2.1.0 on 2026-08-05 and 2.2.0 on 2026-08-13** ([NuGet registration](https://api.nuget.org/v3/registration5-gz-semver2/modelcontextprotocol.aspnetcore/index.json)). The release notes say 2.0.0 "aligns with the MCP 2026-07-28 specification … stateless-by-default HTTP (`Stateless` defaults to `true`)" and 2.2.0 adds "hybrid stateful/stateless HTTP serving so clients using the 2025-11-25 and 2026-07-28 protocol revisions can share an endpoint". [csharp-sdk releases](https://github.com/modelcontextprotocol/csharp-sdk/releases), fetched 2026-10-09.

### 1.2 Is authorization required?

[Authorization §Protocol Requirements](https://modelcontextprotocol.io/specification/2026-07-28/basic/authorization):

> Authorization is **OPTIONAL** for MCP implementations. When supported:
> - Implementations using an HTTP-based transport **SHOULD** conform to this specification.
> - Implementations using an STDIO transport **SHOULD NOT** follow this specification, and instead retrieve credentials from the environment.

The Streamable HTTP transport adds: "Servers **SHOULD** implement proper authentication for all connections." [Streamable HTTP §Security & Endpoint](https://modelcontextprotocol.io/specification/2026-07-28/basic/transports/streamable-http)

### 1.3 What a protected server must do *if* it follows the Authorization spec

From [Authorization](https://modelcontextprotocol.io/specification/2026-07-28/basic/authorization), [Authorization Server Discovery](https://modelcontextprotocol.io/specification/2026-07-28/basic/authorization/authorization-server-discovery) and [Security Considerations](https://modelcontextprotocol.io/specification/2026-07-28/basic/authorization/security-considerations), all 2026-07-28:

| Requirement | Exact language |
|---|---|
| Role | "A protected *MCP server* acts as an OAuth 2.1 resource server". The AS "may be hosted with the resource server or a separate entity". |
| Protected Resource Metadata | "MCP servers **MUST** implement OAuth 2.0 Protected Resource Metadata (RFC9728)." The document "**MUST** include the `authorization_servers` field containing at least one authorization server." |
| Discovery pointer | The server MUST implement one of: (1) `resource_metadata` in `WWW-Authenticate` on 401 (RFC 9728 §5.1), or (2) a well-known URI, either path-based (`/.well-known/oauth-protected-resource/<mcp-path>`) or root (`/.well-known/oauth-protected-resource`). Clients MUST try the header first, then the path-based URI, then the root. |
| 401 shape | Example: `WWW-Authenticate: Bearer resource_metadata="https://mcp.example.com/.well-known/oauth-protected-resource", scope="files:read"`. Servers SHOULD include `scope`. |
| Errors | 401 = "Authorization required or token invalid"; 403 = "Invalid scopes or insufficient permissions"; 400 = malformed. "Invalid or expired tokens **MUST** receive a HTTP 401 response." Runtime insufficient scope: 403 + `WWW-Authenticate: Bearer error="insufficient_scope", scope=…, resource_metadata=…`. |
| Header usage | "authorization **MUST** be included in every HTTP request from client to server" and "Access tokens **MUST NOT** be included in the URI query string". |
| Audience | "MCP servers **MUST** validate that access tokens were issued specifically for them as the intended audience, according to RFC 8707 Section 2." / "**MUST** reject tokens that do not include them in the audience claim". |
| Resource indicators | Clients MUST send `resource=<canonical MCP server URI>` (RFC 8707) in authorization and token requests. Canonical URI examples: `https://mcp.example.com/mcp`. No fragment, and no trailing slash preferred. |
| No passthrough | "MCP servers **MUST NOT** accept or transit any other tokens." If the server calls upstream APIs, "The MCP server **MUST NOT** pass through the token it received from the MCP client." |
| Token lifetime | "Authorization servers **SHOULD** issue short-lived access tokens to reduce the impact of leaked tokens. For public clients, authorization servers **MUST** rotate refresh tokens". |
| Transport security | "All authorization server endpoints **MUST** be served over HTTPS." Redirect URIs must be localhost or HTTPS. |
| Refresh tokens | MCP servers "SHOULD NOT include `offline_access`" in their challenge or `scopes_supported`. |

[Security Best Practices — Token Passthrough](https://modelcontextprotocol.io/docs/2026-07-28/tutorials/security/security_best_practices#token-passthrough) defines passthrough as accepting "tokens from an MCP client without validating that the tokens were properly issued *to the MCP server*" and forwarding them downstream. Its mitigation: "MCP servers **MUST NOT** accept any tokens that were not explicitly issued for the MCP server."

Underlying RFCs:
- [RFC 6750 §3](https://www.rfc-editor.org/rfc/rfc6750#section-3) (Oct 2012): a resource server answers a missing or invalid token with `WWW-Authenticate: Bearer`. Error codes (§3.1): `invalid_request` → 400, `invalid_token` → 401 (expired, revoked, malformed), `insufficient_scope` → 403.
- [RFC 9728](https://www.rfc-editor.org/rfc/rfc9728) (Apr 2025): defines `/.well-known/oauth-protected-resource` (§3) and the `resource_metadata` parameter of `WWW-Authenticate` (§5.1). In the RFC `authorization_servers` is OPTIONAL; MCP makes it mandatory (above).

### 1.4 Is a non-OAuth bearer token without metadata permissible? Consequences

- **Permissible: yes.** Authorization as a whole is OPTIONAL, and conformance for HTTP servers is a **SHOULD**, not a MUST (§1.2). A Lumen JWT in `Authorization: Bearer` is still an RFC 6750 bearer token. It just isn't issued through an OAuth flow, and the server publishes no metadata.
- **There is no conformant halfway point.** A server can't publish RFC 9728 metadata without an authorization server, because MCP requires `authorization_servers` with at least one entry. "Spec-compliant OAuth resource server" therefore means building or adopting an AS (OpenIddict, Entra ID, and so on). A metadata document that points at nothing would be worse than having none.
- **Consequences:**
  1. Any client that relies on auto-discovery cannot connect. On a 401 it looks for `resource_metadata`, then probes `/.well-known/oauth-protected-resource[/mcp]`, then gives up ("Abort or use pre-configured values" in the discovery sequence diagram). That covers claude.ai/Desktop custom connectors (they require the 401 plus a pointer, see §4) and Inspector's OAuth path.
  2. Clients where you **pre-configure the header** (Claude Code `headers`/`headersHelper`, Inspector `--header`) work normally. Claude Code doesn't even try OAuth when an `Authorization` header is configured (§2.4).
  3. The audience MUST still applies in spirit. Lumen should validate `aud` (it already does: `ValidateAudience = true`, audience `Lumen`). Whether the MCP endpoint should require its own audience is open question Q2 (§7).
  4. **Do not use the C# SDK's `McpAuthenticationHandler` / `AddMcp()` auth scheme** without an AS. That handler exists to add `resource_metadata` to challenges and to serve `/.well-known/oauth-protected-resource` (source: `src/ModelContextProtocol.AspNetCore/Authentication/McpAuthenticationHandler.cs` at tag v2.2.0, `DefaultResourceMetadataPath = "/.well-known/oauth-protected-resource"`). Plain `JwtBearer` + `RequireAuthorization()` on `MapMcp()` is the right fit.
  5. With a plain JwtBearer challenge, Lumen returns `401` + `WWW-Authenticate: Bearer` (with `error="invalid_token"` on an expired token), which matches RFC 6750. Claude Code with a configured header reports this as a failed connection or a config-credential error. It does not attempt OAuth (§2.4).

---

## 2. Claude Code

Primary source: [Claude Code — Connect to MCP servers](https://code.claude.com/docs/en/mcp) (fetched 2026-10-09; the page has no date, but its version notes go up to v2.1.284), plus [env-vars](https://code.claude.com/docs/en/env-vars), [permissions](https://code.claude.com/docs/en/permissions) and [errors](https://code.claude.com/docs/en/errors).

### 2.1 Adding an HTTP server with a header

```bash
claude mcp add --transport http secure-api https://api.example.com/mcp \
  --header "Authorization: Bearer your-token"
```

- `--transport`/`--header` have short forms `-t`/`-H`. Scope is set with `--scope local|project|user`.
- The docs' own GitHub example is `claude mcp add --transport http github https://api.githubcopilot.com/mcp/ --header "Authorization: Bearer YOUR_GITHUB_PAT"`. Note: "`claude mcp add` … saves the configuration without validating credentials". Check with `/mcp` (`connected` vs `failed`, and the failure detail includes the HTTP status, e.g. 401).
- `claude mcp add-json <name> '<json>'` takes the same object as an `.mcp.json` entry. `"type": "streamable-http"` is accepted as an alias for `"http"`. An entry with `url` but no `type` is an error.

### 2.2 `.mcp.json` format and env-var expansion

```json
{
  "mcpServers": {
    "api-server": {
      "type": "http",
      "url": "${API_BASE_URL:-https://api.example.com}/mcp",
      "headers": { "Authorization": "Bearer ${API_KEY}" }
    }
  }
}
```

- `${VAR}` and `${VAR:-default}` are expanded in `command`, `args`, `env`, `url`, `headers` and the `headersHelper` path.
- An unset variable with no default stays as the literal `${VAR}` text, with a warning in `claude mcp list`.
- **Credential-looking variables read as empty** in a remote server's `url`/`headers` (`ANTHROPIC_API_KEY`, `ANTHROPIC_AUTH_TOKEN`, `AWS_BEARER_TOKEN_BEDROCK`, `HTTPS_PROXY`, `NPM_TOKEN`, …). The server then receives `Bearer ` and returns a 401. A name like `LUMEN_TOKEN` isn't on the documented list, but the list ends in "other credentials…" (see Uncertain).

### 2.3 Scopes

| Scope | Loads in | Shared | Stored in |
|---|---|---|---|
| local (default) | current project | no | `~/.claude.json` under the project path |
| project | current project | yes | `.mcp.json` at the repo root (committed) |
| user | all projects | no | `~/.claude.json` |

Precedence: local > project > user > plugin > claude.ai connectors > managed. Project-scope servers need an approval prompt. A literal token must never go in project scope. Use `${VAR}` or `headersHelper` there.

### 2.4 401 handling — does a 401 trigger OAuth and override the static header?

No, not when you configured the header. From "Authenticate with remote MCP servers":

> For a server whose `Authorization` header you configured, in `headers` or through a `headersHelper`, a `401` or `403` while connecting doesn't flag the server, because the credential to fix is the one you configured. Claude Code reports the connection as failed instead.

And for `headersHelper`: "When the helper's output includes an `Authorization` header, Claude Code uses that credential as the server's authentication and doesn't fall back to OAuth for the server."

Without a configured header, a 401/403 flags the server as needing authentication in `/mcp`, and the OAuth discovery flow starts from `WWW-Authenticate`. Against Lumen that would fail, because Lumen has no metadata.

Retries: "An authentication or not-found error" on first connect is **not retried**, "because it requires a configuration change". The exception is when `headersHelper` is the only source of `Authorization`.

### 2.5 Token expiry mid-session

- **Static header:** the tool call fails with `MCP server "<name>" rejected the Authorization header in its config (update it, then run /mcp to reconnect)`. That message format applies from v2.1.273; before that it read "requires re-authorization (token expired)". `/mcp` marks the server as needing auth. There is no automatic recovery: you update the config and reconnect. [errors — MCP server needs you to sign in again](https://code.claude.com/docs/en/errors)
- **`headersHelper`:** "If a tool call returns `401 Unauthorized` or `403 Forbidden`, Claude Code automatically re-runs the helper …, reconnects with the fresh headers, and retries the call once." The helper also runs "fresh on each connection, at session start and on reconnect". It isn't cached, so "your script is responsible for any token reuse".
- `headersHelper` contract: a shell command (an absolute path or on `PATH`) that writes a JSON object of string→string headers to stdout, with a **10 s timeout**. Its output overrides static `headers` of the same name. It gets the env vars `CLAUDE_CODE_MCP_SERVER_NAME` and `CLAUDE_CODE_MCP_SERVER_URL`.
- `headersHelper` trust rules: for project/local scope it runs only after the workspace trust dialog has been accepted. Before that the server connects with static `headers` only. A project- or plugin-supplied helper runs **without** credential env vars, so it must read secrets from a file or the keychain.
- The docs name this as the intended use: "If your MCP server uses an authentication scheme other than OAuth, such as Kerberos, short-lived tokens, or an internal SSO, use `headersHelper`".

### 2.6 Output limits, timeouts

- `MAX_MCP_OUTPUT_TOKENS`: **default 25,000 tokens**, with a **warning above 10,000 tokens** ("The warning threshold is fixed").
- Separately, a successful text result longer than **50,000 characters is saved to a file** unless the tool declares `_meta["anthropic/maxResultSizeChars"]` (max 500,000).
- `MCP_TIMEOUT`: server startup/connect timeout, **default 30,000 ms** per [env-vars](https://code.claude.com/docs/en/env-vars).
- `MCP_TOOL_TIMEOUT`: default **100,000,000 ms (~28 h)**.
- **For HTTP servers each request also has a 60 s per-request timer** (up to the first response byte). It only goes higher if `MCP_TOOL_TIMEOUT` or the per-server `"timeout"` is above 60,000.
- `CLAUDE_CODE_MCP_TOOL_IDLE_TIMEOUT`: default 5 min for network servers.
- Per-server `"timeout"` (ms, ≥1000) in `.mcp.json` overrides `MCP_TOOL_TIMEOUT`.

### 2.7 Tool naming and permissions

- Tools appear as `mcp__<server>__<tool>`, for example `mcp__lumen__search_documents`. Characters outside `A-Za-z0-9_-` become `_`.
- Allow rules: `mcp__lumen` or `mcp__lumen__*` (all tools of the server), or `mcp__lumen__search_documents`. "An unanchored allow glob such as `"*"` … or `"mcp__*"` is skipped". [permissions](https://code.claude.com/docs/en/permissions)
- Tool calls go through the normal permission prompt unless an allow rule matches.

### 2.8 Protocol revision support

Claude Code has two runtimes. "The v2 runtime is the same code on MCP TypeScript SDK 2.0, which adds MCP protocol revision 2026-07-28." It is on by default from v2.1.232 (feature-flag sessions) or v2.1.274. It "Asks HTTP and stdio servers whether they support the newer revision" and falls back otherwise. Override with `MCP_SDK_GENERATION=v1|v2` and `MCP_PROTOCOL_NEGOTIATION=auto|legacy`. The v2 runtime "Sends MCP OAuth credentials only to a token endpoint served over HTTPS or at `localhost`". That only affects OAuth, not static headers.

---

## 3. MCP Inspector

Sources: [inspector README (main)](https://github.com/modelcontextprotocol/inspector), [clients/cli/README.md](https://github.com/modelcontextprotocol/inspector/blob/main/clients/cli/README.md), [docs/environment-variables.md](https://github.com/modelcontextprotocol/inspector/blob/main/docs/environment-variables.md), [docs/mcp-server-configuration.md](https://github.com/modelcontextprotocol/inspector/blob/main/docs/mcp-server-configuration.md), [docs/cli-smoke-testing.md](https://github.com/modelcontextprotocol/inspector/blob/main/docs/cli-smoke-testing.md). All fetched 2026-10-09.

- **Version:** npm `@modelcontextprotocol/inspector` `latest` = **2.10.1** (published 2026-10-08). v1 lives on as `v1-latest` = 1.0.2 (security fixes only). Node ≥ 22.19.0 is required. v2 has four modes: web (default), `--cli`, `--tui`, and an experimental `mcpdo`. There is a v1 → v2 migration guide (CLI flags and env vars were renamed).
- **Headers:** `--header "Name: Value"` (repeatable) works in all modes. Example from the CLI README:
  ```bash
  npx @modelcontextprotocol/inspector --cli https://my-mcp-server.example.com --transport http --method tools/list --header "X-API-Key: your-api-key"
  ```
  In web mode `--header` requires an ad-hoc HTTP target; it can't be combined with `--catalog`/`--config`. A config/catalog file also takes a per-server `"headers": {…}` object.
- **CLI for scripted checks:** `--method tools/list | tools/call --tool-name X --tool-arg k=v | resources/list | resources/read | prompts/list …`, plus `--format json` and `-q`. Documented **exit codes**: `0` ok, `1` usage/unexpected, `3` "Server requires authentication (401/403, WWW-Authenticate, OAuth)", `4` unreachable, `5` tool error (`isError:true` or tool not found), `6` `--strict` schema problem. That means a negative auth test (no or expired token → exit 3) and a tenant smoke test (`tools/call` + `jq`) can both be shell scripts. `--protocol-era legacy|auto|modern` chooses which revision to negotiate (default for file entries: `"legacy"`).
- **Proxy auth token:** the web backend guards its own `/api/*` with `MCP_INSPECTOR_API_TOKEN` (a random token per launch, sent as `x-mcp-remote-auth: Bearer <token>`). `MCP_PROXY_AUTH_TOKEN` is the deprecated v1 name, and `DANGEROUSLY_OMIT_AUTH` turns the guard off. The backend binds `127.0.0.1` by default, and `ALLOWED_ORIGINS` is its own origin allow-list. **This token protects the Inspector, not Lumen.** It is unrelated to the bearer header sent to Lumen.
- **CORS:** "The backend proxies MCP connections" ([clients/web/README.md](https://github.com/modelcontextprotocol/inspector/blob/main/clients/web/README.md)). Requests to Lumen come from Node, not the browser, so Lumen needs no CORS policy for Inspector.
- **HTTPS locally:** Node doesn't trust a self-signed cert just because the browser does. Use `NODE_EXTRA_CA_CERTS=/path/ca.pem npx @modelcontextprotocol/inspector`; `NODE_TLS_REJECT_UNAUTHORIZED=0` works but is dev-only and process-wide.
- **OAuth tooling** (`--use-stored-auth`, `--relogin`, CIMD) is irrelevant unless Lumen ever adds an AS.

---

## 4. Claude Desktop / claude.ai custom connectors — re-check

- [anthropics/claude-ai-mcp #644](https://github.com/anthropics/claude-ai-mcp/issues/644), "Custom connector ignores configured request header, defaults to broken OAuth flow". Opened 2026-07-19, **still open** on 2026-10-09, labels `auth`, `bug`, `server-developer-report`. No visible staff response. The symptom is a redirect to `/authorize?…client_id=x-api-key…`: the header *name* is used as an OAuth `client_id`.
- [anthropics/claude-ai-mcp #685](https://github.com/anthropics/claude-ai-mcp/issues/685), "Enable static request headers for Cowork custom connectors". Opened 2026-07-24, **still open**, label `enhancement`, no visible staff response. It confirms the header auth "works in Claude Code" but isn't exposed in the Cowork dialog.
- [Authentication for connectors — claude.com docs](https://claude.com/docs/connectors/building/authentication), fetched 2026-10-09: `static_headers` is listed as "**Beta, for a limited set of organizations**". "Owners whose organization doesn't have access don't see the **Request headers** section". The credential is entered once by an **organization Owner** and sent "on every call from anyone in that organization", and the docs say: "don't use it to identify which user is calling. If your tools need to act as the individual user, use OAuth instead". OAuth DCR and CIMD are "Supported by default". Claude "requires a 401 to start sign-in" and needs a `resource_metadata` pointer or a well-known metadata document.
- The same page says hosted Claude traffic "originates from `160.79.104.0/21`" (Anthropic's cloud), so claude.ai/Desktop connectors can only reach a **publicly reachable** URL, never `localhost`.
- **Verdict: the exclusion still holds**, and for an additional reason. Even if `static_headers` reached Lumen's org, it would be one org-wide credential, which clashes with Lumen's per-user `sub`/`role` claims (it could at best map to a per-tenant service identity). The only general-purpose path is still a real OAuth AS (OpenIddict or similar) on a public URL. One nuance: per the spec, DCR is now *deprecated* and CIMD is preferred, so a future OpenIddict stretch goal should plan for CIMD (Claude selects CIMD only when AS metadata has `client_id_metadata_document_supported: true` and `"none"` in `token_endpoint_auth_methods_supported`).

---

## 5. Token lifetime — options (not decided)

Current Lumen config: `Jwt:ExpiryMinutes = 60` (`src/Lumen.Infrastructure/DependencyInjection/JwtOptions.cs` default and `src/Lumen.Api/appsettings.json`; `appsettings.Development.json` overrides only the signing secret). Validation uses `ValidateLifetime = true` and `ValidateAudience = true` with audience `Lumen` (`src/Lumen.Api/Extensions/WebApplicationBuilderExtensions.cs`). `ClockSkew` isn't set, so it is the JwtBearer default (5 min; see Uncertain). In effect a token lasts about 65 min, and a static header in a Claude Code config breaks after about an hour.

What the sources say: the MCP spec says ASes "SHOULD issue short-lived access tokens" (§1.3). Claude Code docs point to `headersHelper` for "short-lived tokens" (§2.5). Anthropic's connector docs say a static credential is an org credential, not a user identity (§4).

| Option | How | Pros | Cons |
|---|---|---|---|
| **A. Keep 60 min + Claude Code `headersHelper`** | A small script (e.g. `tools/mcp-token.sh`) POSTs email+password to `/api/auth/token`, optionally caches the JWT until shortly before `exp`, and prints `{"Authorization":"Bearer …"}`. The password comes from the macOS keychain or an env var. | No server change. Short tokens, as the spec recommends. Claude Code re-runs the helper on 401 and retries once, so expiry recovers on its own. Good interview story ("used the client's documented dynamic-credential hook"). | Only Claude Code has this hook; Inspector needs a fresh `--header` per run (easy to script). The password sits on the client machine. Trust-dialog rules apply for project/local scope. |
| **B. Longer-lived JWT for MCP only** | A `/api/auth/token` parameter or separate config (e.g. `Jwt:McpExpiryMinutes = 1440`) issuing a token with a distinct `aud` (e.g. `lumen-mcp`). | Simple. A static header works for a full demo day. A separate audience also scopes the token to the MCP endpoint, in the spirit of the audience MUST. | Goes against "short-lived" guidance. No revocation (stateless HS256 JWT): a leaked token is valid until `exp`. Needs a second audience accepted by the right endpoints. |
| **C. Personal access token (PAT) / "MCP token"** | A new `McpTokens` table (hashed opaque token, `UserId`, `TenantId`, `ExpiresAt`, `RevokedAt`), an Admin/Member endpoint to mint and revoke, and a custom auth handler on `/mcp` that looks up the hash. | Revocable, long-lived, per-user, auditable. The same pattern as GitHub PATs, which Claude Code's own docs use as their header example. Fits a static header well. | The most code: a new table, migration, handler, endpoints, tenant-scoping of lookups (the lookup must bypass the tenant filter the way `UserLookupService` does today, which adds a second `IgnoreQueryFilters`). Moves away from "pure JWT practice". |
| **D. Refresh tokens** | Issue a refresh token at `/api/auth/token`, plus a `/api/auth/refresh`. | Standard OAuth shape. | No MCP client uses a refresh token from a static config. Claude Code only refreshes via its OAuth flow, and a `headersHelper` could just re-login instead. Adds state for no client-side benefit. |
| **E. Full OAuth AS (OpenIddict)** | AS with PRM + RFC 8414 metadata + CIMD/DCR + PKCE + refresh. | Spec-conformant. Unlocks claude.ai/Desktop on a public URL. Short tokens with client-managed refresh. | The multi-day build already judged a stretch goal in `.research/mcp-client-auth.md`. |

---

## 6. Security: Origin / DNS rebinding, CORS, HTTPS

- **Spec requirement** ([Streamable HTTP §Security & Endpoint](https://modelcontextprotocol.io/specification/2026-07-28/basic/transports/streamable-http)):
  > 1. Servers **MUST** validate the `Origin` header on all incoming connections to prevent DNS rebinding attacks. If the `Origin` header is present and invalid, servers **MUST** respond with HTTP 403 Forbidden. The HTTP response body **MAY** comprise a JSON-RPC *error response* that has no `id`.
  > 2. When running locally, servers **SHOULD** bind only to localhost (127.0.0.1) rather than all network interfaces (0.0.0.0).
  > 3. Servers **SHOULD** implement proper authentication for all connections.
- **The C# SDK does not do this for you.** A search of `src/ModelContextProtocol.AspNetCore` at tag v2.2.0 finds no `Origin` header handling; the only hits are unrelated (`OriginalString`, "original" session params). Lumen needs its own small middleware on the `/mcp` path: if an `Origin` header is present and not in a configured allow-list, return 403. If it is absent, let it through, since non-browser clients like Claude Code and the Inspector's Node backend normally send none (see Uncertain).
- **Host filtering** gives extra protection against DNS rebinding. Kestrel doesn't validate `Host`. The host-filtering middleware is enabled by setting `AllowedHosts` to a semicolon list, and it is "disabled" with `*`. [Microsoft Learn — Host filtering](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/host-filtering), updated 2026-10-08. **Lumen currently has `"AllowedHosts": "*"`** (`src/Lumen.Api/appsettings.json`).
- **CORS:** no browser client talks to Lumen's `/mcp` directly. Inspector web proxies through its Node backend (§3), and Claude Code is a CLI. So keep no CORS policy, or explicitly deny. Adding CORS would widen the browser attack surface that the Origin check exists to close.
- **Bearer auth itself mitigates DNS rebinding.** A rebinding page in a browser cannot read or attach the user's bearer token (it isn't a cookie). The [Security Best Practices — Local MCP Server Compromise](https://modelcontextprotocol.io/docs/2026-07-28/tutorials/security/security_best_practices) section lists "Require an authorization token" as the mitigation for local HTTP servers. The Origin check is still a MUST.
- **HTTPS locally:** `launchSettings.json` exposes `http://localhost:5119` and `https://localhost:7090`. Static headers over `http://localhost` are fine with Claude Code; the HTTPS-only rule in the v2 runtime is about OAuth token endpoints. For the Inspector over `https://localhost`, Node must trust the ASP.NET dev cert (`NODE_EXTRA_CA_CERTS`). In Azure (Stage 6), App Service terminates TLS, so the deployed URL is HTTPS and RFC 6750's TLS requirement for bearer tokens is met.
- **State handles:** under 2026-07-28 there are no sessions. "MCP servers that implement authorization **MUST** verify all inbound requests" and "MUST NOT treat possession of a state handle as authentication". Since every POST carries `Authorization`, tenant resolution from the JWT per request fits this model directly.

---

## Uncertain / could not verify

- **Whether `LUMEN_TOKEN` (or any `*_TOKEN` name) counts as a "credential variable" that Claude Code reads as empty.** The docs list examples and then "Other credentials your environment carries" without the full rule. Verify empirically (if Lumen logs `Bearer ` with no token, that's the cause), or use `headersHelper` and sidestep it.
- **Whether `${VAR}` in `headers` is re-expanded on `/mcp` reconnect or only at startup.** Not stated. A rotated env var may need a Claude Code restart.
- **Mid-session expiry with a static header:** the errors page describes the tool-call failure message, but not whether Claude Code also tries a silent reconnect first. It doesn't seem to (the retry-on-401 text is specific to OAuth and `headersHelper`).
- **Whether Inspector's Node backend or Claude Code send an `Origin` header** on MCP POSTs. Not documented. Assumed absent; log `Origin` during the first manual run to confirm before tightening the rule to "absent or allow-listed".
- **Inspector v2 web UI**: the exact field and label for custom headers wasn't checked. The `customHeaders` module exists and the CLI flag is documented. The default `protocolEra` for ad-hoc (non-file) targets wasn't confirmed either; the docs show the file default `"legacy"`.
- **Comments on #644/#685** were read through GitHub's HTML rendering (no `gh` CLI available). "No staff response" means none was visible in that rendering.
- **JwtBearer default `ClockSkew` = 5 min** is from general ASP.NET Core knowledge, not re-fetched today.
- C# SDK release dates: the GitHub releases page rendering showed the wrong year; dates were taken from NuGet's registration API instead (authoritative).

---

## Implication for Lumen

**Recommended minimal design (fits the settled "self-issued JWT, no OAuth AS" decision):**

1. `app.MapMcp("/mcp").RequireAuthorization()` reuses the existing `JwtBearer` scheme. Both `Admin` and `Member` are allowed (MCP is query-only). **Don't** register the SDK's `AddMcp()` auth scheme or serve `/.well-known/oauth-protected-resource`: without an AS, a metadata document can't conform (`authorization_servers` MUST be non-empty) and would send OAuth-capable clients into a dead end.
2. Keep the 401 path as JwtBearer emits it (`WWW-Authenticate: Bearer error="invalid_token"…` on expiry). That satisfies RFC 6750 and the spec's "Invalid or expired tokens MUST receive a HTTP 401". Role failures → 403.
3. Tenant comes **only** from the validated JWT (`tenant_id` claim), per request. Never take it from a tool argument or a `Mcp-Param-*` header. Never forward the client's JWT anywhere (no-passthrough MUST). Lumen's only upstream is Azure OpenAI, which uses its own key or managed identity, so this already holds.
4. Add an **Origin-validation middleware** on `/mcp` (403 when present and not allow-listed). Set `AllowedHosts` to real hosts (`localhost;<app>.azurewebsites.net`) instead of `*`. Add no CORS policy for `/mcp`.
5. Demo clients:
   - Claude Code with `headersHelper` (option A), which gets a fresh token for each connection and recovers on 401:
     ```json
     { "mcpServers": { "lumen": { "type": "http", "url": "http://localhost:5119/mcp",
       "headersHelper": "/abs/path/tools/mcp/lumen-token.sh" } } }
     ```
     A static `--header "Authorization: Bearer $(curl … /api/auth/token | jq -r .token)"` is fine for a first smoke test, but it expires in about 60 min.
   - Inspector: `npx @modelcontextprotocol/inspector --cli http://localhost:5119/mcp --transport http --header "Authorization: Bearer $TOKEN" --method tools/list`. Assert exit `3` without a token, and use `tools/call` + `jq` for a two-tenant isolation smoke check.
6. **Tool output sizing**: keep tool results well under 10k tokens (the warning threshold) and 50k characters (the file-offload threshold). Ten chunks of about 250 tokens plus metadata is around 3–4k tokens, which is comfortable. Mind the **60 s per-request timer** if an `ask`-style tool (LLM generation) is exposed. A reasoning model at high effort could get close; set a per-server `"timeout"` if needed.
7. Protocol: SDK 2.2.0 is stateless by default and serves both 2025-11-25 and 2026-07-28 clients. Claude Code's v2 runtime will negotiate 2026-07-28; the Inspector can be forced with `--protocol-era`.

**Open design questions for the user:**

- **Q1 — Token strategy:** A (60 min + `headersHelper`), B (longer-lived MCP-audience JWT), or C (revocable PAT table)? A needs no server change. C is the most "production-shaped" but adds a second `IgnoreQueryFilters` site.
- **Q2 — Separate audience for MCP?** Should `/mcp` accept only tokens with `aud = lumen-mcp`, keeping REST tokens and MCP tokens apart, as the spec's audience-binding MUST intends? Or accept the existing `Lumen` audience everywhere?
- **Q3 — Which tools,** and does `ask` (LLM generation, 60 s timer, cost) get exposed over MCP, or only `search` + document listing/resources?
- **Q4 — Credential storage for the helper script:** macOS keychain (`security find-generic-password`) vs. an env var vs. a gitignored file. Seeded dev passwords are fine locally, but Stage 6 (Azure) needs something real.
- **Q5 — Revisit OpenIddict later?** With DCR deprecated and CIMD preferred in 2026-07-28, an eventual claude.ai/Desktop stretch goal would be "OpenIddict + CIMD on a public HTTPS URL". Record it in DECISIONS.md as a reconsider-trigger only, not a plan.
