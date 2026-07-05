# Deployment Notes

## Resource estimate
- MCP server: 1 vCPU, 1 GB RAM
- Ollama: 4 vCPU, 8 GB RAM for larger models

## Monitoring
- Track /health and /mcp/tools response latency
- Capture audit logs from ./logs/audit.log
- Alert on failed tool calls and repeated schema validation failures

## Backup and resilience
- Store docs under version control or a mounted volume
- Rotate API keys via environment variables
- Run multiple replicas behind a load balancer if traffic grows
