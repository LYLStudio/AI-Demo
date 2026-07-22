### 測試驗證總覽

下面是一組 **可直接執行的完整測試步驟與範例檔案**，用來驗證我先前提供的單一 Java 檔案（`HawkConsoleTool.java`）功能：EMS 連線、非阻塞監控、microagent 探查、非阻塞呼叫與 polling、REST 熱更新、JWT RS256 發行與驗證、refresh/revoke、以及以 role 控制的 filter 更新。請依序執行每一步並觀察主控台輸出與 REST 回應來驗證系統行為。

---

### 前置準備

- **必要 Jar**：把下列 jar 放到同一個資料夾（例如 `lib/`）並在編譯/執行時加入 classpath  
  **hawk-console-api.jar**, **tibjms.jar**（或 EMS client jars）, **gson.jar**。  
- **修改程式內 Hawk 參數**：在 `hawkProps()` 裡設定正確的 EMS broker URL、user、password、domain。  
- **產生或準備 RSA 金鑰**（選一）：  
  - 使用 OpenSSL 產生（建議測試用）：  
    ```bash
    openssl genpkey -algorithm RSA -out private.pem -pkeyopt rsa_keygen_bits:2048
    openssl rsa -pubout -in private.pem -out public.pem
    ```
  - 或讓程式在啟動時自動產生暫時金鑰（僅測試用，不適合生產）。  
- **範例檔案**：建立 `filter.json`（範例內容見下方）。

---

### 編譯與啟動範例

1. **編譯**
   ```bash
   javac -cp "lib/*:." HawkConsoleTool.java
   ```
2. **啟動（使用自備金鑰）**
   ```bash
   java -cp "lib/*:." HawkConsoleTool 8080 /path/to/private.pem /path/to/public.pem
   ```
   或 **讓程式自動產生金鑰（測試）**
   ```bash
   java -cp "lib/*:." HawkConsoleTool 8080
   ```
3. **啟動後觀察主控台輸出**  
   - 會顯示 Console 建立嘗試、已註冊的 agent、microagent descriptor、以及 REST server 啟動訊息。  
   - 若程式自動產生金鑰，主控台會印出一個 **Sample access token**（可用於快速測試）。

---

### 範例 filter.json（觸發 invoke 與 poll）

建立 `filter.json`，內容如下（範例會對 agent 上名為 `MyApp` 的 microagent 呼叫 `getStatus`，並每 15 秒 poll `getMetrics`）：

```json
{
  "rules": [
    { "agentNameContains": "prod", "microAgentName": "MyApp" }
  ],
  "actions": [
    { "type": "invoke", "microAgent": "MyApp", "method": "getStatus" },
    { "type": "poll", "microAgent": "MyApp", "method": "getMetrics", "periodSec": 15 }
  ]
}
```

---

### REST 驗證與操作範例

#### 1. 取得 Access Token 與 Refresh Token（登入）
- **請求**
  ```bash
  curl -s -X POST http://localhost:8080/login \
    -H "Content-Type: application/json" \
    -d '{"username":"admin","password":"adminpass"}'
  ```
- **預期回應**
  ```json
  {
    "access_token": "<JWT_ACCESS_TOKEN>",
    "token_type": "Bearer",
    "expires_in": 3600,
    "refresh_token": "<REFRESH_TOKEN>"
  }
  ```

#### 2. 使用 Access Token 取得目前 filter
- **請求**
  ```bash
  curl -s -H "Authorization: Bearer <JWT_ACCESS_TOKEN>" http://localhost:8080/filter
  ```
- **預期回應**：目前 filter JSON（或 `{}` 若尚未設定）

#### 3. 使用 Access Token 更新 filter（需 admin 角色）
- **請求**
  ```bash
  curl -s -X PUT http://localhost:8080/filter \
    -H "Authorization: Bearer <JWT_ACCESS_TOKEN>" \
    -H "Content-Type: application/json" \
    --data-binary @filter.json
  ```
- **預期回應**
  ```json
  {"status":"ok"}
  ```
- **主控台行為**：收到更新後會立即對已知 agents 做篩選並執行 actions（會在主控台印出 `FILTER MATCH`、`Invoked`、或 `POLL ... => ...` 訊息）。

#### 4. 使用 Refresh Token 換取新 Access Token
- **請求**
  ```bash
  curl -s -X POST http://localhost:8080/refresh \
    -H "Content-Type: application/json" \
    -d '{"refresh_token":"<REFRESH_TOKEN>"}'
  ```
- **預期回應**
  ```json
  {
    "access_token": "<NEW_JWT_ACCESS_TOKEN>",
    "token_type": "Bearer",
    "expires_in": 3600
  }
  ```

#### 5. 撤銷 Refresh Token
- **請求**
  ```bash
  curl -s -X POST http://localhost:8080/revoke \
    -H "Content-Type: application/json" \
    -d '{"refresh_token":"<REFRESH_TOKEN>"}'
  ```
- **預期回應**
  ```json
  {"status":"revoked"}
  ```

---

### 驗證 microagent 呼叫與 polling

- **條件**：系統中必須有 agent 名稱包含 `prod` 且 microagent 名稱為 `MyApp`（或修改 `filter.json` 的條件以配合您環境）。  
- **觀察點**：在主控台（程式 stdout）應看到：
  - `Registered agent: <agentName>`
  - `MicroAgent added: MyApp on <agentName>`
  - `Descriptor for MyApp ... methods=[...] props=[...]`
  - `FILTER MATCH: <agentName> -> taking actions`
  - `Invoked MyApp.getStatus => <result>`（若 microagent method 回傳值會印出）
  - `POLL <agentName>:MyApp.getMetrics => <result>` 每 15 秒印一次（或您設定的 period）

---

### 測試案例清單（逐項驗證）

1. **Console 建立成功**：主控台顯示 `Console created.`  
2. **Agent 自動發現**：啟動 agent（或等待現有 agent），主控台顯示 `Agent alive` 與 `Registered agent`。  
3. **Microagent 列表與 descriptor**：主控台顯示 `MicroAgent added` 與 `Descriptor for ... methods=...`。  
4. **登入與 JWT 驗證**：`/login` 成功回傳 access + refresh token；使用 access token 呼叫 `/filter` 成功。  
5. **Role 控制**：使用非 admin 帳號（例如 `user/userpass`）嘗試 PUT /filter，應收到 `403 forbidden`。  
6. **Filter 更新即時生效**：PUT /filter 後主控台立即印出 `FILTER MATCH` 與 action 執行結果。  
7. **Polling 行為**：poll action 每隔設定秒數在主控台印出結果。  
8. **Refresh 與 Revoke**：使用 refresh 取得新 access token；revoke 後 refresh 失效。  
9. **錯誤情境**：傳入無效 JWT、過期 token、或不合法 JSON，REST 回應應為 401/400/500 對應錯誤訊息。

---

### 範例測試腳本（Bash）

把下列內容存成 `test_flow.sh`，修改 `ACCESS_TOKEN` 與 `REFRESH_TOKEN` 為實際值或使用 `jq` 自動擷取（需安裝 `jq`）：

```bash
#!/bin/bash
HOST="http://localhost:8080"
# 1. login
echo "Login..."
LOGIN=$(curl -s -X POST $HOST/login -H "Content-Type: application/json" -d '{"username":"admin","password":"adminpass"}')
echo $LOGIN | jq
ACCESS=$(echo $LOGIN | jq -r .access_token)
REFRESH=$(echo $LOGIN | jq -r .refresh_token)

# 2. get filter
echo "Get filter..."
curl -s -H "Authorization: Bearer $ACCESS" $HOST/filter | jq

# 3. put filter
echo "Put filter..."
curl -s -X PUT $HOST/filter -H "Authorization: Bearer $ACCESS" -H "Content-Type: application/json" --data-binary @filter.json | jq

# 4. wait and observe console logs for invoked/poll outputs
echo "Waiting 40s to observe polling..."
sleep 40

# 5. refresh token
echo "Refresh..."
REFR=$(curl -s -X POST $HOST/refresh -H "Content-Type: application/json" -d "{\"refresh_token\":\"$REFRESH\"}")
echo $REFR | jq

# 6. revoke
echo "Revoke..."
curl -s -X POST $HOST/revoke -H "Content-Type: application/json" -d "{\"refresh_token\":\"$REFRESH\"}" | jq
```

---

### 常見問題與排錯建議

- **無法連到 EMS broker**：檢查 `ems.broker.url`、防火牆、broker 是否啟動，並確認 EMS client jar 在 classpath。  
- **找不到 TIBHawkConsole 類別**：確認 `hawk-console-api.jar` 已加入 classpath，且版本與程式呼叫相容。  
- **JWT 驗證失敗**：確認使用 RS256 的公私鑰對正確，且 token 未過期；可使用程式啟動時印出的 sample token 測試。  
- **filter 未生效**：確認 filter 規則能匹配到已註冊 agent 的名稱或 microagent 名稱；檢查主控台是否印出 `FILTER MATCH`。  
- **microagent method 呼叫失敗**：檢查 microagent 是否支援該 method 名稱與參數，並查看主控台錯誤訊息。

---

### 結語與後續建議

這份測試範例覆蓋從 **身分驗證、授權、熱更新、到實際管理動作** 的完整流程，能作為驗證整個系統整合的基準。若你希望我把測試腳本改成更自動化（例如加入 `jq` 自動擷取 token、或把測試結果寫入 log 檔並產生報表），或把 REST 加上 TLS 與更嚴格的 JSON schema 驗證，我可以把變更直接整合回同一個檔案並提供更新後的測試步驟。你想先自動化哪一部分？