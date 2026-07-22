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
