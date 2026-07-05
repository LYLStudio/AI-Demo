# McpServer

McpServer is a .NET 10 ASP.NET Core Web API that exposes MCP-style endpoints for tool discovery, tool invocation, and a simple RAG pipeline backed by a local Ollama instance.

## Features
- MCP endpoints: initialize, list tools, invoke tools, health
- Example tools: search_docs, read_file, http_request, run_query
- RAG ingestion pipeline for markdown/json/pdf content
- Ollama integration with local model gemma4:31b-mlx
- Basic audit logging and bearer-token security support

## Requirements
- .NET 10 SDK
- Optional: local Ollama at http://localhost:11434 with gemma4:31b-mlx

## Run locally
```bash
dotnet restore
DOTNET_ENVIRONMENT=Development dotnet run --project McpServer
```

## Ingest docs
```bash
dotnet run --project McpServer -- ingest --source ./docs --type markdown
```

## Test
```bash
dotnet test
```

## Configuration
Set environment variables or edit appsettings.json:
- Security__ApiKey
- Ollama__BaseUrl
- Ollama__Model
- Audit__Path
