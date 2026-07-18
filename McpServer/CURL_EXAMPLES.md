# MCP Server cURL 呼叫範例

**Server URL:** `http://localhost:5209/mcp`  
**Protocol:** JSON-RPC 2.0  
**Method:** POST with `Content-Type: application/json`

---

## 📌 基本請求格式

所有請求都使用相同的 endpoint：
```bash
curl -s http://localhost:5209/mcp \
  -H "Content-Type: application/json" \
  -d '<request_body>' | python3 -m json.tool
```

---

## 1️⃣ Initialize（初始化）

告知 server 你的 client 支援的協議版本。

```bash
curl -s http://localhost:5209/mcp \
  -H "Content-Type: application/json" \
  -d '{
    "jsonrpc": "2.0",
    "id": 1,
    "method": "initialize",
    "params": {
      "protocolVersion": "2025-06-18",
      "capabilities": {},
      "clientInfo": {
        "name": "MyClient",
        "version": "1.0.0"
      }
    }
  }' | python3 -m json.tool
```

**回應範例：**
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "result": {
    "protocolVersion": "2025-06-18",
    "serverInfo": { "name": "McpServer", "version": "1.0.0" },
    "capabilities": { "tools": { "listChanged": false }, "logging": {} }
  }
}
```

---

## 2️⃣ List Tools（列出所有工具）

查看所有可用的工具。

```bash
curl -s http://localhost:5209/mcp \
  -H "Content-Type: application/json" \
  -d '{
    "jsonrpc": "2.0",
    "id": 2,
    "method": "tools/list"
  }' | python3 -m json.tool
```

**可用的工具列表：**
| 工具 ID | 描述 |
|---------|------|
| `calculator` | 數學表達式計算 |
| `stock_info` | 台灣上市/上櫃股票資訊查詢 |
| `search_docs` | 文件索引搜尋 (RAG) |
| `read_file` | 讀取本地檔案 |
| `http_request` | HTTP 請求 |
| `run_query` | 執行資料庫查詢 |

---

## 3️⃣ Calculator（數學運算）

計算表達式結果。

### 簡單加法
```bash
curl -s http://localhost:5209/mcp \
  -H "Content-Type: application/json" \
  -d '{
    "jsonrpc": "2.0",
    "id": 3,
    "method": "tools/call",
    "params": {
      "name": "calculator",
      "arguments": {
        "expression": "1 + 2"
      }
    }
  }' | python3 -m json.tool
```

### 複合運算（包含乘除）
```bash
curl -s http://localhost:5209/mcp \
  -H "Content-Type: application/json" \
  -d '{
    "jsonrpc": "2.0",
    "id": 3,
    "method": "tools/call",
    "params": {
      "name": "calculator",
      "arguments": {
        "expression": "1 + 2 * 3"
      }
    }
  }' | python3 -m json.tool
```

**回應範例：**
```json
{
  "jsonrpc": "2.0",
  "id": "3",
  "result": {
    "content": [
      {
        "type": "text",
        "text": { "expression": "1 + 2 * 3", "result": 7 }
      }
    ]
  }
}
```

---

## 4️⃣ Stock Info（股票查詢）

查詢台灣上市/上櫃股票資訊。

### 查詢台積電 (2330)
```bash
curl -s http://localhost:5209/mcp \
  -H "Content-Type: application/json" \
  -d '{
    "jsonrpc": "2.0",
    "id": 4,
    "method": "tools/call",
    "params": {
      "name": "stock_info",
      "arguments": {
        "symbol": "2330"
      }
    }
  }' | python3 -m json.tool
```

### 指定市場查詢
```bash
curl -s http://localhost:5209/mcp \
  -H "Content-Type: application/json" \
  -d '{
    "jsonrpc": "2.0",
    "id": 4,
    "method": "tools/call",
    "params": {
      "name": "stock_info",
      "arguments": {
        "symbol": "2330",
        "market": "tse"
      }
    }
  }' | python3 -m json.tool
```

**回應範例（摘要）：**
```json
{
  "jsonrpc": "2.0",
  "id": "4",
  "result": {
    "content": [
      {
        "type": "text",
        "text": {
          "symbol": "2330",
          "market": "tse_2330.tw",
          "data": [
            {
              "name": "base64|5Y+w56mN6Zu7|base64",
              "open": "2375.0000",
              "high": "2395.0000",
              "low": "2290.0000",
              "close": "2290.0000",
              "volume": "74514",
              "time": "13:30:00"
            }
          ]
        }
      }
    ]
  }
}
```

---

## 5️⃣ Search Docs（文件搜尋）

搜尋索引過的文檔內容。

```bash
curl -s http://localhost:5209/mcp \
  -H "Content-Type: application/json" \
  -d '{
    "jsonrpc": "2.0",
    "id": 5,
    "method": "tools/call",
    "params": {
      "name": "search_docs",
      "arguments": {
        "query": "如何部署 MCP Server"
      }
    }
  }' | python3 -m json.tool
```

---

## 6️⃣ Read File（讀取檔案）

讀取工作區內的本地檔案。

```bash
curl -s http://localhost:5209/mcp \
  -H "Content-Type: application/json" \
  -d '{
    "jsonrpc": "2.0",
    "id": 6,
    "method": "tools/call",
    "params": {
      "name": "read_file",
      "arguments": {
        "path": "/home/dev/AI/AI-Demo/README.md"
      }
    }
  }' | python3 -m json.tool
```

---

## 7️⃣ HTTP Request（HTTP 請求）

發起 HTTP 請求。

### GET 請求
```bash
curl -s http://localhost:5209/mcp \
  -H "Content-Type: application/json" \
  -d '{
    "jsonrpc": "2.0",
    "id": 7,
    "method": "tools/call",
    "params": {
      "name": "http_request",
      "arguments": {
        "url": "https://api.github.com/repos/dotnet/core",
        "method": "GET"
      }
    }
  }' | python3 -m json.tool
```

### POST 請求
```bash
curl -s http://localhost:5209/mcp \
  -H "Content-Type: application/json" \
  -d '{
    "jsonrpc": "2.0",
    "id": 7,
    "method": "tools/call",
    "params": {
      "name": "http_request",
      "arguments": {
        "url": "https://httpbin.org/post",
        "method": "POST"
      }
    }
  }' | python3 -m json.tool
```

---

## 🐛 錯誤處理

### Method not found
當呼叫不存在的 method：
```bash
curl -s http://localhost:5209/mcp \
  -H "Content-Type: application/json" \
  -d '{
    "jsonrpc": "2.0",
    "id": 99,
    "method": "unknown_method"
  }' | python3 -m json.tool
```

**回應：**
```json
{
  "jsonrpc": "2.0",
  "id": "99",
  "error": {
    "code": -32601,
    "message": "Method not found: unknown_method"
  }
}
```

### Invalid params
當工具呼叫缺少必要參數：
```bash
curl -s http://localhost:5209/mcp \
  -H "Content-Type: application/json" \
  -d '{
    "jsonrpc": "2.0",
    "id": 98,
    "method": "tools/call",
    "params": {
      "name": "calculator"
    }
  }' | python3 -m json.tool
```

**回應：**
```json
{
  "jsonrpc": "2.0",
  "id": "98",
  "error": {
    "code": -32602,
    "message": "Invalid params"
  }
}
```

---

## ⚡ 快速測試指令

```bash
# 測試所有端點
curl -s http://localhost:5209/health  # 健康檢查 (GET)

curl -s http://localhost:5209/mcp \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":1,"method":"initialize"}' | python3 -m json.tool

curl -s http://localhost:5209/mcp \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":2,"method":"tools/list"}' | python3 -m json.tool

curl -s http://localhost:5209/mcp \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"calculator","arguments":{"expression":"1+2*3"}}}' | python3 -m json.tool

curl -s http://localhost:5209/mcp \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"stock_info","arguments":{"symbol":"2330"}}}' | python3 -m json.tool
```
