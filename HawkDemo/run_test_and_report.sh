#!/usr/bin/env bash
set -euo pipefail

# Config - 修改為你的環境
JAVA_CMD="java -cp \"lib/*:.\" HawkEmsJwtRsFull"   # 或實際的 class name
REST_PORT=8080
PRIVATE_PEM=""    # 若要用檔案啟動，填入 private.pem 路徑，否則程式會自動產生
PUBLIC_PEM=""     # 同上
APP_LOG="app.log"
RESULT_LOG="test_results.log"
REPORT_JSON="report.json"
REPORT_TXT="report.txt"
FILTER_FILE="filter.json"
SLEEP_AFTER_PUT=20   # 等待 poll/invoke 的秒數（視 poll 週期調整）
DEMO_USER="admin"
DEMO_PASS="adminpass"

# Cleanup previous logs
rm -f "$APP_LOG" "$RESULT_LOG" "$REPORT_JSON" "$REPORT_TXT"

echo "=== TEST RUN START $(date -u +"%Y-%m-%dT%H:%M:%SZ") ===" | tee "$RESULT_LOG"

# 1) Start Java app in background, redirect stdout/stderr to app.log
echo "[1] Starting Java application..." | tee -a "$RESULT_LOG"
if [[ -n "$PRIVATE_PEM" && -n "$PUBLIC_PEM" ]]; then
  eval $JAVA_CMD" $REST_PORT $PRIVATE_PEM $PUBLIC_PEM" > "$APP_LOG" 2>&1 &
else
  eval $JAVA_CMD" $REST_PORT" > "$APP_LOG" 2>&1 &
fi
APP_PID=$!
echo "App PID: $APP_PID" | tee -a "$RESULT_LOG"

# Wait for REST server to start (poll app.log for "REST server started" or similar)
echo "[1.a] Waiting for REST server to be ready..." | tee -a "$RESULT_LOG"
READY=false
for i in {1..30}; do
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

# 2) Login to get tokens
echo "[2] Logging in to obtain tokens..." | tee -a "$RESULT_LOG"
LOGIN_RESP=$(curl -s -X POST "http://localhost:${REST_PORT}/login" \
  -H "Content-Type: application/json" \
  -d "{\"username\":\"${DEMO_USER}\",\"password\":\"${DEMO_PASS}\"}" || true)

echo "[2.a] Raw login response: $LOGIN_RESP" >> "$RESULT_LOG"

ACCESS_TOKEN=$(echo "$LOGIN_RESP" | jq -r .access_token // empty)
REFRESH_TOKEN=$(echo "$LOGIN_RESP" | jq -r .refresh_token // empty)
EXPIRES_IN=$(echo "$LOGIN_RESP" | jq -r .expires_in // empty)

if [[ -z "$ACCESS_TOKEN" || -z "$REFRESH_TOKEN" ]]; then
  echo "ERROR: Failed to obtain tokens. Login response: $LOGIN_RESP" | tee -a "$RESULT_LOG"
  kill $APP_PID 2>/dev/null || true
  exit 2
fi
echo "Obtained access_token (len=${#ACCESS_TOKEN}) and refresh_token." | tee -a "$RESULT_LOG"

# 3) GET current filter
echo "[3] GET /filter" | tee -a "$RESULT_LOG"
GET_FILTER_RESP=$(curl -s -H "Authorization: Bearer $ACCESS_TOKEN" "http://localhost:${REST_PORT}/filter" || true)
echo "[3.a] GET response: $GET_FILTER_RESP" >> "$RESULT_LOG"

# 4) PUT new filter (upload filter.json)
if [[ ! -f "$FILTER_FILE" ]]; then
  echo "ERROR: $FILTER_FILE not found. Create it before running the test." | tee -a "$RESULT_LOG"
  kill $APP_PID 2>/dev/null || true
  exit 3
fi
echo "[4] PUT /filter with $FILTER_FILE" | tee -a "$RESULT_LOG"
PUT_RESP=$(curl -s -X PUT "http://localhost:${REST_PORT}/filter" \
  -H "Authorization: Bearer $ACCESS_TOKEN" \
  -H "Content-Type: application/json" \
  --data-binary @"$FILTER_FILE" || true)
echo "[4.a] PUT response: $PUT_RESP" >> "$RESULT_LOG"

# 5) Wait for actions to execute (invoke/poll) and capture app.log lines
echo "[5] Waiting ${SLEEP_AFTER_PUT}s for actions (poll/invoke) to appear in app.log..." | tee -a "$RESULT_LOG"
sleep "$SLEEP_AFTER_PUT"

# 6) Analyze app.log for expected markers
echo "[6] Scanning $APP_LOG for expected markers..." | tee -a "$RESULT_LOG"
MARKERS=("FILTER MATCH" "Invoked " "POLL ")
declare -A found
for m in "${MARKERS[@]}"; do
  if grep -q "$m" "$APP_LOG"; then
    found["$m"]="true"
  else
    found["$m"]="false"
  fi
done

# 7) Build report JSON
echo "[7] Building report..." | tee -a "$RESULT_LOG"
NOW=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
cat > "$REPORT_JSON" <<EOF
{
  "timestamp": "$NOW",
  "rest_port": $REST_PORT,
  "app_pid": $APP_PID,
  "login_response": $LOGIN_RESP,
  "get_filter_response": $GET_FILTER_RESP,
  "put_filter_response": $PUT_RESP,
  "markers_found": {
    "FILTER_MATCH": ${found["FILTER MATCH"]},
    "INVOKED": ${found["Invoked "]},
    "POLL": ${found["POLL "]}
  },
  "app_log_tail": $(tail -n 200 "$APP_LOG" | jq -R -s -c '.')
}
EOF

# 8) Create human-readable report
echo "[8] Creating human-readable report..." | tee -a "$RESULT_LOG"
{
  echo "=== TEST REPORT ==="
  echo "Timestamp: $NOW"
  echo "App PID: $APP_PID"
  echo ""
  echo "Login response (summary):"
  echo "$LOGIN_RESP" | jq .
  echo ""
  echo "GET /filter response:"
  echo "$GET_FILTER_RESP" | jq -c .
  echo ""
  echo "PUT /filter response:"
  echo "$PUT_RESP" | jq -c .
  echo ""
  echo "Markers found in app.log:"
  for m in "${MARKERS[@]}"; do
    echo "  - $m : ${found[$m]}"
  done
  echo ""
  echo "Last 200 lines of app.log:"
  tail -n 200 "$APP_LOG"
} > "$REPORT_TXT"

# 9) Save a concise test results log
echo "[9] Writing concise results to $RESULT_LOG" | tee -a "$RESULT_LOG"
{
  echo "TEST_RUN: $NOW"
  echo "APP_PID: $APP_PID"
  echo "ACCESS_TOKEN_LEN: ${#ACCESS_TOKEN}"
  echo "REFRESH_TOKEN_LEN: ${#REFRESH_TOKEN}"
  echo "PUT_RESPONSE: $PUT_RESP"
  echo "MARKERS:"
  for m in "${MARKERS[@]}"; do
    echo "$m=${found[$m]}"
  done
} >> "$RESULT_LOG"

# 10) Stop the app (optional)
echo "[10] Stopping application (PID $APP_PID)..." | tee -a "$RESULT_LOG"
kill "$APP_PID" 2>/dev/null || true
sleep 1
echo "Application stopped." | tee -a "$RESULT_LOG"

echo "=== TEST RUN COMPLETE ===" | tee -a "$RESULT_LOG"
echo "Report files: $REPORT_JSON, $REPORT_TXT, $RESULT_LOG, $APP_LOG"
