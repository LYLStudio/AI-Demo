#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

printf 'Initializing server...\n'
curl -sS -X POST http://localhost:5209/mcp/initialize > ./tmp/mcp_init.json
cat ./tmp/mcp_init.json

printf '\nListing tools...\n'
curl -sS http://localhost:5209/mcp/tools > ./tmp/mcp_tools.json
cat ./tmp/mcp_tools.json

printf '\nSending sample query...\n'
curl -sS -X POST http://localhost:5209/mcp/chat -H 'Content-Type: application/json' -d '"What is MCP?"'

