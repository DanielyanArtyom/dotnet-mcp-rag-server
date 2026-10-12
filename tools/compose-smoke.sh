#!/usr/bin/env bash
# End-to-end check of `docker compose up`: health, demo data, search/ask, Swagger, MCP, restart and volume persistence.
set -euo pipefail
cd "$(dirname "$0")/.."
# Own project name and ports: the developer's compose stack (same volume names under project "dotnet-mcp-rag-server")
# may hold a corpus embedded with Azure; the smoke run must start from empty volumes and never touch it.
export COMPOSE_PROJECT_NAME=lumen-smoke API_PORT=55119 POSTGRES_PORT=55432
# Shell variables beat .env: a developer .env pointing at Azure must not make the smoke run call Azure.
export JWT_SECRET="$(openssl rand -hex 32)"
export EMBEDDINGS_PROVIDER=Fake EMBEDDINGS_ENDPOINT= EMBEDDINGS_DEPLOYMENT= EMBEDDINGS_API_KEY=
export CHAT_PROVIDER=Fake CHAT_ENDPOINT= CHAT_DEPLOYMENT= CHAT_API_KEY=
API=http://localhost:$API_PORT
created_env=no
if [ ! -f .env ]; then cp .env.example .env; created_env=yes; fi
trap 'docker compose down -v >/dev/null 2>&1 || true; [ "$created_env" = yes ] && rm -f .env' EXIT

fail() { echo "FAIL: $*" >&2; exit 1; }
wait_ready() { for _ in $(seq 1 180); do [ "$(curl -s -o /dev/null -w '%{http_code}' $API/health/ready)" = 200 ] && return 0; sleep 1; done; fail "health/ready never became 200"; }
token() { curl -sf -X POST $API/api/auth/token -H 'Content-Type: application/json' -d "{\"email\":\"$1\",\"password\":\"Passw0rd!\"}" | python3 -c 'import sys,json;print(json.load(sys.stdin)["token"])'; }

docker compose up -d --build
wait_ready
[ "$(curl -s -o /dev/null -w '%{http_code}' $API/health/live)" = 200 ] || fail "health/live"
[ "$(curl -s -o /dev/null -w '%{http_code}' $API/swagger/index.html)" = 200 ] || fail "swagger"

for pair in "admin@acme.test:Apple 10-K,Apple 10-Q" "admin@globex.test:Morgan 10-K,Morgan 10-Q"; do
  email=${pair%%:*}; expected=${pair#*:}
  T=$(token "$email")
  curl -sf $API/api/documents -H "Authorization: Bearer $T" | python3 -c '
import sys,json
docs=json.load(sys.stdin); want=sys.argv[1].split(",")
titles=sorted(d["title"] for d in docs)
assert titles==sorted(want), titles
assert all(d.get("ingestProfile") for d in docs), "missing ingest profile"
print("documents ok:", titles)' "$expected"
  curl -sf -X POST $API/api/search -H "Authorization: Bearer $T" -H 'Content-Type: application/json' -d '{"query":"total net sales","topK":3}' >/dev/null || fail "search $email"
  curl -sf -X POST $API/api/ask -H "Authorization: Bearer $T" -H 'Content-Type: application/json' -d '{"question":"What were total net sales?"}' >/dev/null || fail "ask $email"
  out=$(curl -sf -X POST $API/mcp -H "Authorization: Bearer $T" -H 'Content-Type: application/json' -H 'Accept: application/json, text/event-stream' \
    -d '{"jsonrpc":"2.0","id":1,"method":"tools/list"}') || fail "mcp tools/list $email"
  case "$out" in *search_filings*) ;; *) fail "mcp tools/list $email" ;; esac
done

docker compose restart api
wait_ready
logs=$(docker compose logs api)
case "$logs" in *"already exists"*) ;; *) fail "seeding was not skipped on restart" ;; esac

docker compose down
docker compose up -d
wait_ready
volume=lumen-smoke_lumen-documents
files=$(docker run --rm -v "$volume":/data alpine find /data/documents -type f -name '*.html' -exec stat -c '%u %n' {} +)
echo "$files"
[ "$(printf '%s\n' "$files" | grep -c '\.html$')" = 4 ] || fail "expected 4 raw .html files under /data/documents"
[ -z "$(printf '%s\n' "$files" | awk '$1 != 1654')" ] || fail "raw files not owned by UID 1654"

echo "SMOKE OK"
