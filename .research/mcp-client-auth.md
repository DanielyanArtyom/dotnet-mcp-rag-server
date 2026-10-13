# MCP client auth — Claude Desktop vs Claude Code, for a self-hosted JWT-secured server

Question: for a self-hosted, remote (HTTP-transport) MCP server with custom-issued JWTs (`tenant_id` claim, not an external IdP), deployed to Azure App Service — what auth does each Claude client actually support today (checked 2026-09-09)?

## Decision

**Demo/test clients: MCP Inspector (dev-loop) + Claude Code CLI (`--header` auth).** Not Claude Desktop, for now — see below. Desktop-via-OAuth is a possible optional stretch goal, decided later rather than designed upfront.

## Findings

**(a) Does Claude Desktop require OAuth for authenticated custom connectors?**

Not per spec — Anthropic's own docs list a `static_headers` auth type (a fixed API key/bearer token) as a supported option. [Authentication for connectors — claude.com docs](https://claude.com/docs/connectors/building/authentication). **But it's officially in beta, and per multiple open GitHub issues (`anthropics/claude-ai-mcp` [#644](https://github.com/anthropics/claude-ai-mcp/issues/644) and [#685](https://github.com/anthropics/claude-ai-mcp/issues/685), both filed July–Sept 2026, unresolved as of this check) the "Add custom connector" UI in Claude.ai web and desktop app does not honor it today** — configuring a header still triggers an OAuth `authorization_code` redirect, or the header field isn't exposed at all. **In practice, OAuth (Dynamic Client Registration or CIMD) is the only reliably working authenticated path for the Desktop/web custom-connector UI right now.**

**(b) Minimal .NET path if building the OAuth route:**

Use **OpenIddict** (Apache-2.0, free regardless of org size — unlike Duende IdentityServer's commercial tiers for larger orgs). Minimum surface needed:
- `/.well-known/oauth-authorization-server` (RFC 8414)
- `/.well-known/oauth-protected-resource` (RFC 9728)
- `/authorize`
- `/token` (must accept `x-www-form-urlencoded`)
- `/register` (Dynamic Client Registration, RFC 7591, JSON) — or skip DCR via CIMD, or use pre-registered static client credentials

PKCE S256 is mandatory. **This is a real multi-day build, not a weekend add-on.**

**(c) MCP spec position:** Authorization is formally optional in MCP; OAuth 2.1 is the spec's answer only *if* the transport is protected. No first-class "API key" pattern exists in the spec itself.

**(d) Claude Code is meaningfully easier — confirmed working today:**

```
claude mcp add --transport http lumen <url> --header "Authorization: Bearer <token>"
```

Confirmed directly by GitHub issue #685 ("works successfully from Claude Code using header auth"), in contrast to the Desktop/web UI's broken `static_headers` support.

## Recommendation (adopted)

Use **Claude Code** as the demo/test client with `--header`, plus **MCP Inspector** for dev-loop debugging (Inspector's UI supports custom headers cleanly). Implement plain JWT bearer-token validation in ASP.NET Core (`AddAuthentication().AddJwtBearer()` + a custom `/token` issuing endpoint) — this still gives full hands-on JWT/`tenant_id`-claim practice, without building an OAuth *authorization server*. Only add OpenIddict later, as an explicit separate stretch goal, if there's a specific reason to demo through Desktop's OAuth flow.
