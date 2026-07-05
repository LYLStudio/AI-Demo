# API examples

## Initialize
```bash
curl -X POST http://localhost:5000/mcp/initialize
```

## List tools
```bash
curl http://localhost:5000/mcp/tools
```

## Invoke a tool
```bash
curl -X POST http://localhost:5000/mcp/call -H 'Content-Type: application/json' -d '{"toolId":"search_docs","input":{"query":"mcp"},"user":"demo"}'
```
