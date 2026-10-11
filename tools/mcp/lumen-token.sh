#!/usr/bin/env bash
# Claude Code headersHelper for the Lumen MCP server: prints {"Authorization":"Bearer <jwt>"}.
# Claude Code reruns it on every connection and after a 401, so the 60-minute token never needs manual refresh.
set -euo pipefail

url="${LUMEN_URL:-http://localhost:5119}"
: "${LUMEN_EMAIL:?set LUMEN_EMAIL}"
: "${LUMEN_PASSWORD:?set LUMEN_PASSWORD}"

body=$(jq -n --arg email "$LUMEN_EMAIL" --arg password "$LUMEN_PASSWORD" '{email: $email, password: $password}')
token=$(curl -fsS -X POST "$url/api/auth/token" -H 'Content-Type: application/json' -d "$body" | jq -er '.token') \
  || { echo "lumen-token: login to $url failed" >&2; exit 1; }

jq -cn --arg token "$token" '{Authorization: ("Bearer " + $token)}'
