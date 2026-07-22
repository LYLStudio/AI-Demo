### 概要
下面提供一個 **完整的自動化測試腳本**，用來驗證你剛整合好的單一 Java 檔案（`HawkEmsJwtRsFull.java`）在 BusinessWorks 5.15/5.16 環境下的行為。腳本會：

- 啟動 Java 應用並把 stdout/stderr 寫入 `app.log`  
- 使用 `curl` 與 `jq` 自動登入並擷取 `access_token` 與 `refresh_token`  
- 呼叫 `/bw/methods` 列出目標 microagent 的 methods（方便確認 descriptor）  
- 呼叫 `/bw/control` 嘗試執行控制動作（disableStarter / enableStarter / stopInstance / startInstance 或指定 method）  
- 監控 `app.log` 是否出現 `FILTER MATCH`、`Invoked`、`POLL` 等關鍵字  
- 將 HTTP 回應、log 摘要與檢查結果寫入 `test_results.log`、`report.json` 與 `report.txt`，方便自動化或人工審閱

---

### 前置條件
1. 已編譯 Java 程式並可執行（檔名 `HawkEmsJwtRsFull` 或你實際使用的 class 名稱）。  
2. 必要 jar 在 `lib/`：`hawk-console-api.jar`、EMS client jar（例如 `tibjms.jar`）與 `gson.jar`。  
3. `jq` 已安裝（用於解析 JSON）。  
4. `filter.json`（若要測試 filter 行為）已準備好。  
5. 若要使用自備 RSA 金鑰，準備 `private.pem` 與 `public.pem`，或讓程式自動產生暫時金鑰（僅測試用）。  
6. 有可被 Hawk 探測到的 BW agent 與 microagent（例如 microagent 名稱包含 `bwengine` 或你環境的名稱）。

---

### 檔案：`run_bw_control_test.sh`
把下列內容存成 `run_bw_control_test.sh`，放在與 Java 程式同一目錄，並 `chmod +x run_bw_control_test.sh`。

```bash
#!/usr/bin/env bash
set -euo pipefail

# -----------------------
# Configuration - EDIT
# -----------------------
JAVA_CLASS="HawkEmsJwtRsFull"        # Java main class name
CLASSPATH="lib/*:."                  # adjust as needed
REST_PORT=8080
PRIVATE_PEM=""                       # optional: path to private.pem
PUBLIC_PEM=""                        # optional: path to public.pem
APP_LOG="app.log"
RESULT_LOG="test_results.log"
REPORT_JSON="report.json"
REPORT_TXT="report.txt"
FILTER_FILE="filter.json"
SLEEP_AFTER_PUT=20                   # wait seconds for actions to run
DEMO_USER="admin"
DEMO_PASS="adminpass"
MICROAGENT_PATTERN="bwengine"       # microagent name pattern to search
AGENT_NAME=""                        # optional: restrict to specific agent name

# -----------------------
# Cleanup
# -----------------------
rm -f "$APP_LOG" "$RESULT_LOG" "$REPORT_JSON" "$REPORT_TXT"
echo "TEST START $(date -u +"%Y-%m-%dT%H:%M:%SZ")" | tee "$RESULT_LOG"

# -----------------------
# Start Java app
# -----------------------
echo "[1] Starting Java application..." | tee -a "$RESULT_LOG"
if [[ -n "$PRIVATE_PEM" && -n "$PUBLIC_PEM" ]]; then
  java -cp "$CLASSPATH" "$JAVA_CLASS" "$REST_PORT" "$PRIVATE_PEM" "$PUBLIC_PEM" > "$APP_LOG" 2>&1 &
else
  java -cp "$CLASSPATH" "$JAVA_CLASS" "$REST_PORT" > "$APP_LOG" 2>&1 &
fi
APP_PID=$!
echo "App PID: $APP_PID" | tee -a "$RESULT_LOG"

# Wait for REST server readiness
echo "[1.a] Waiting for REST server to be ready..." | tee -a "$RESULT_LOG"
READY=false
for i in {1..40}; do
  if grep -q -E "REST server started|Running. REST filter endpoint" "$APP_LOG" 2>/dev/null; then
    READY=true
    break
  fi
  sleep 1
done
if ! $READY; then
  echo "ERROR: REST server did not start within timeout. Check $APP_LOG" | tee -a "$RESULT_LOG"
  kill $APP_PID 2>/dev/null || true
  exit 1
fi
echo "REST server ready." | tee -a "$RESULT_LOG"

# -----------------------
# Login to obtain tokens
# -----------------------
echo "[2] Logging in to obtain tokens..." | tee -a "$RESULT_LOG"
LOGIN_RESP=$(curl -s -X POST "http://localhost:${REST_PORT}/login" \
  -H "Content-Type: application/json" \
  -d "{\"username\":\"${DEMO_USER}\",\"password\":\"${DEMO_PASS}\"}" || true)
echo "[2.a] Raw login response:" >> "$RESULT_LOG"
echo "$LOGIN_RESP" >> "$RESULT_LOG"

ACCESS_TOKEN=$(echo "$LOGIN_RESP" | jq -r .access_token // empty)
REFRESH_TOKEN=$(echo "$LOGIN_RESP" | jq -r .refresh_token // empty)
EXPIRES_IN=$(echo "$LOGIN_RESP" | jq -r .expires_in // empty)

if [[ -z "$ACCESS_TOKEN" || -z "$REFRESH_TOKEN" ]]; then
  echo "ERROR: Failed to obtain tokens. Login response: $LOGIN_RESP" | tee -a "$RESULT_LOG"
  kill $APP_PID 2>/dev/null || true
  exit 2
fi
echo "Obtained access_token and refresh_token." | tee -a "$RESULT_LOG"

# -----------------------
# List microagent methods
# -----------------------
echo "[3] Querying microagent methods for pattern '$MICROAGENT_PATTERN'..." | tee -a "$RESULT_LOG"
METHODS_RESP=$(curl -s -G "http://localhost:${REST_PORT}/bw/methods" \
  --data-urlencode "microAgentName=${MICROAGENT_PATTERN}" \
  --data-urlencode "agentName=${AGENT_NAME}" \
  -H "Authorization: Bearer $ACCESS_TOKEN" || true)
echo "[3.a] Methods response:" >> "$RESULT_LOG"
echo "$METHODS_RESP" >> "$RESULT_LOG"

# Save methods to file for inspection
echo "$METHODS_RESP" > bw_methods.json

# -----------------------
# Control action: disableStarter (try candidates)
# -----------------------
echo "[4] Attempting control action disableStarter..." | tee -a "$RESULT_LOG"
CONTROL_PAYLOAD=$(jq -n --arg ma "$MICROAGENT_PATTERN" --arg action "disableStarter" '{microAgentName:$ma, action:$action}')
CONTROL_RESP=$(curl -s -X POST "http://localhost:${REST_PORT}/bw/control" \
  -H "Authorization: Bearer $ACCESS_TOKEN" \
  -H "Content-Type: application/json" \
  -d "$CONTROL_PAYLOAD" || true)
echo "[4.a] Control response:" >> "$RESULT_LOG"
echo "$CONTROL_RESP" >> "$RESULT_LOG"

# -----------------------
# Optionally: explicit method call if known
# Example: setAutoStart(false)
# -----------------------
echo "[5] If you know exact method, call it explicitly (example setAutoStart false)..." | tee -a "$RESULT_LOG"
EXPLICIT_PAYLOAD=$(jq -n --arg ma "$MICROAGENT_PATTERN" --arg method "setAutoStart" --argjson args '[false]' '{microAgentName:$ma, method:$method, args:$args}')
EXPLICIT_RESP=$(curl -s -X POST "http://localhost:${REST_PORT}/bw/control" \
  -H "Authorization: Bearer $ACCESS_TOKEN" \
  -H "Content-Type: application/json" \
  -d "$EXPLICIT_PAYLOAD" || true)
echo "[5.a] Explicit call response:" >> "$RESULT_LOG"
echo "$EXPLICIT_RESP" >> "$RESULT_LOG"

# -----------------------
# Wait for actions to take effect and for polling to produce logs
# -----------------------
echo "[6] Waiting ${SLEEP_AFTER_PUT}s for actions/polling to appear in app.log..." | tee -a "$RESULT_LOG"
sleep "$SLEEP_AFTER_PUT"

# -----------------------
# Analyze app.log for expected markers
# -----------------------
echo "[7] Scanning $APP_LOG for markers..." | tee -a "$RESULT_LOG"
MARKERS=("FILTER MATCH" "Invoked " "POLL ")
declare -A found
for m in "${MARKERS[@]}"; do
  if grep -q "$m" "$APP_LOG"; then
    found["$m"]="true"
  else
    found["$m"]="false"
  fi
done

# -----------------------
# Build report JSON
# -----------------------
echo "[8] Building report JSON..." | tee -a "$RESULT_LOG"
NOW=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
tail200=$(tail -n 200 "$APP_LOG" | jq -R -s -c '.')
cat > "$REPORT_JSON" <<EOF
{
  "timestamp": "$NOW",
  "rest_port": $REST_PORT,
  "app_pid": $APP_PID,
  "login_response": $LOGIN_RESP,
  "methods_response": $(jq -c . <<<"$METHODS_RESP"),
  "control_response": $(jq -c . <<<"$CONTROL_RESP"),
  "explicit_call_response": $(jq -c . <<<"$EXPLICIT_RESP"),
  "markers_found": {
    "FILTER_MATCH": ${found["FILTER MATCH"]},
    "INVOKED": ${found["Invoked "]},
    "POLL": ${found["POLL "]}
  },
  "app_log_tail": $tail200
}
EOF

# -----------------------
# Human readable report
# -----------------------
echo "[9] Creating human-readable report..." | tee -a "$RESULT_LOG"
{
  echo "=== BW CONTROL TEST REPORT ==="
  echo "Timestamp: $NOW"
  echo "App PID: $APP_PID"
  echo ""
  echo "Login response:"
  echo "$LOGIN_RESP" | jq .
  echo ""
  echo "Methods response (bw_methods.json):"
  cat bw_methods.json | jq .
  echo ""
  echo "Control response (disableStarter attempt):"
  echo "$CONTROL_RESP" | jq .
  echo ""
  echo "Explicit call response (setAutoStart false):"
  echo "$EXPLICIT_RESP" | jq .
  echo ""
  echo "Markers found in app.log:"
  for m in "${MARKERS[@]}"; do
    echo "  - $m : ${found[$m]}"
  done
  echo ""
  echo "Last 200 lines of app.log:"
  tail -n 200 "$APP_LOG"
} > "$REPORT_TXT"

# -----------------------
# Save concise results
# -----------------------
echo "[10] Writing concise results to $RESULT_LOG" | tee -a "$RESULT_LOG"
{
  echo "REPORT_JSON: $REPORT_JSON"
  echo "REPORT_TXT: $REPORT_TXT"
  echo "APP_LOG: $APP_LOG"
  echo "BW_METHODS_FILE: bw_methods.json"
} >> "$RESULT_LOG"

# -----------------------
# Stop the app
# -----------------------
echo "[11] Stopping application (PID $APP_PID)..." | tee -a "$RESULT_LOG"
kill "$APP_PID" 2>/dev/null || true
sleep 1
echo "Application stopped." | tee -a "$RESULT_LOG"

echo "=== TEST RUN COMPLETE ===" | tee -a "$RESULT_LOG"
echo "Report files: $REPORT_JSON, $REPORT_TXT, $RESULT_LOG, $APP_LOG"
```

---

### 如何執行
1. 把 `run_bw_control_test.sh`、`filter.json` 與已編譯的 Java 程式放在同一目錄。  
2. 調整腳本頂端的 `CLASSPATH`、`JAVA_CLASS`、`REST_PORT`、`MICROAGENT_PATTERN` 與 `DEMO_USER`/`DEMO_PASS` 等參數以符合你的環境。  
3. 給予執行權限並執行：
   ```bash
   chmod +x run_bw_control_test.sh
   ./run_bw_control_test.sh
   ```
4. 完成後檢查 `report.json` 與 `report.txt`，以及 `app.log` 與 `test_results.log`。

---

### 預期結果與驗證要點
- `login` 成功並取得 `access_token` 與 `refresh_token`。  
- `/bw/methods` 回傳包含你目標 microagent 的 method 列表（或空陣列表示 descriptor 不可用）。  
- `/bw/control` 回傳每個嘗試 method 的結果（`success` 或 `error`），可用來判斷哪個 method 在你的 microagent 上有效。  
- 若 microagent 支援呼叫，主控台 `app.log` 會顯示 `Invoked ...` 或其他 microagent 回傳訊息；若有 poll action，會看到 `POLL ... => ...`。  
- `report.json` 包含所有 HTTP 回應與 `app.log` 最後 200 行，方便自動化比對或上傳至 CI artifact。

---

### 常見問題與排錯
- **無法取得 token**：檢查 `login` 回應與 `app.log`，確認 REST server 已啟動且使用者存在（預設 demo user 為 `admin/adminpass`）。  
- **/bw/methods 回傳空**：可能 microagent descriptor 未暴露，或 microagent 名稱不匹配 `MICROAGENT_PATTERN`，請用更寬鬆的 pattern 或在 BW 端確認 microagent 是否已註冊。  
- **control 呼叫失敗**：檢查 `control_response` 中每個 method 的 `error`，並用 `/bw/methods` 確認實際 method 名稱與參數型態，必要時用 `method` + `args` 明確呼叫。  
- **權限錯誤**：確保 token 中有 `admin` role（登入使用 `admin` demo user 或你的實際 admin 帳號）。

---

### 下一步建議
- 若測試成功，我可以幫你把腳本改為 CI job（例如 GitHub Actions）或把 `report.json` 自動上傳到 S3/Artifact 存放。  
- 若 `/bw/methods` 顯示 microagent 有方法但 `control` 嘗試仍失敗，我可以協助把 `controlBwAction` 的候選 method 清單擴充或加入更精細的參數型別推斷。  

需要我把這個腳本直接調整成你 CI 可用的版本，或把 `report.json` 自動上傳到指定位置嗎？