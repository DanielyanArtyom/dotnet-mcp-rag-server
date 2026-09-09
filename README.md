# Lumen — Multi-tenant RAG & MCP server on .NET

ASP.NET Core service that ingests documents, indexes them with vector
search in PostgreSQL (pgvector), and exposes retrieval as an MCP
server. Multi-tenant end to end — each tenant's data is isolated down
to the raw SQL layer, not just the ORM.

Pet project, work in progress. Full write-up, diagram, and demo GIF
land at the end — see roadmap below.

## What's coming

1. Skeleton — Onion Architecture, multi-tenancy via `TenantId`
2. Ingest & chunking — document parsing pipeline
3. Embeddings + storage — Azure OpenAI + pgvector
4. RAG endpoint — retrieval, generation, eval-driven tuning
5. MCP server — search/documents exposed as MCP tools
6. Deploy — Azure App Service, managed identity
7. Polish — diagram, demo, docker-compose

Design rationale and decisions live in [`DECISIONS.md`](DECISIONS.md).
