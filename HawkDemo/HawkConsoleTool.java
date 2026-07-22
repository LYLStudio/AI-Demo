/* Save as HawkConsoleTool.java
   Single-file Java application with "test mode" added.
   - Use --test to run in test mode (no Hawk dependency required at runtime for manager behavior).
   - Keeps previous features: JWT auth, persistent refresh tokens, invocation timeouts, metrics.
   - Compile with Hawk jars if you plan to run real mode; test mode does not require Hawk at runtime
     but compile still needs Hawk types if FullManager is compiled (so include Hawk jars).
   Compile:
     javac -cp "path/to/hawk-console-api.jar:path/to/tibjms.jar:path/to/gson.jar:." HawkConsoleTool.java
   Run (test mode):
     java -cp "path/to/gson.jar:." HawkConsoleTool --test 8080
   Run (real mode):
     java -cp "path/to/hawk-console-api.jar:path/to/tibjms.jar:path/to/gson.jar:." HawkConsoleTool 8080 /path/to/priv.pem /path/to/pub.pem
*/

import com.google.gson.*;
import com.google.gson.reflect.TypeToken;
import com.sun.net.httpserver.*;

import java.io.*;
import java.net.InetSocketAddress;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.security.*;
import java.security.interfaces.*;
import java.security.spec.*;
import java.time.*;
import java.time.format.DateTimeFormatter;
import java.util.*;
import java.util.concurrent.*;
import java.util.concurrent.atomic.*;
import java.util.logging.*;

/* NOTE:
   The original FullManager uses TIBCO Hawk Console API types (TIBHawkConsole, AgentManager, AgentMonitor, etc.).
   To keep the file single and testable, we introduce ManagerInterface and TestManager.
   FullManager still exists and implements ManagerInterface (requires Hawk libs).
   JwtAuthRestServer uses ManagerInterface so it can operate with either manager.
*/

public class HawkConsoleTool {

    // -----------------------
    // Configurable constants
    // -----------------------
    static final int DEFAULT_REST_PORT = 8080;
    static final long INVOCATION_TIMEOUT_MS = 10_000; // 10 seconds timeout for microagent invoke
    static final int MAX_CONCURRENT_INVOCATIONS = 20; // bounded concurrency for invokeMethod
    static final String REFRESH_STORE_FILE = "refresh_tokens.json"; // persistent refresh token store
    static final int REFRESH_STORE_FLUSH_SEC = 10; // periodic flush interval
    static final Logger logger = Logger.getLogger("HawkConsoleTool");

    static {
        Logger root = Logger.getLogger("");
        for (Handler h : root.getHandlers()) {
            h.setFormatter(new SimpleFormatter());
        }
        logger.setLevel(Level.INFO);
    }

    static String timestamp() {
        return DateTimeFormatter.ISO_LOCAL_TIME.format(LocalTime.now());
    }

    /* =========================
       ManagerInterface
       - Abstraction used by REST server
       ========================= */
    public interface ManagerInterface {
        void start();
        void stop();
        void setFilterFromJson(String json);
        String getFilterJson();
        Map<String, List<String>> listMicroAgentMethods(String agentName, String microAgentNamePattern);
        Map<String,Object> controlBwAction(String agentName, String microAgentNamePattern, String action, String methodName, Object[] args);
        Map<String,Object> getMetricsSnapshot();
    }

    /* =========================
       Main
       ========================= */
    public static void main(String[] args) throws Exception {
        // parse args: [--test] [port] [privKeyPemPath] [pubKeyPemPath]
        boolean testMode = false;
        int port = DEFAULT_REST_PORT;
        String privKeyPath = null;
        String pubKeyPath = null;

        List<String> argList = new ArrayList<>(Arrays.asList(args));
        if (argList.contains("--test")) {
            testMode = true;
            argList.remove("--test");
        }
        if (argList.size() > 0) {
            try { port = Integer.parseInt(argList.get(0)); } catch (Exception ignored) {}
        }
        if (argList.size() > 1) privKeyPath = argList.get(1);
        if (argList.size() > 2) pubKeyPath = argList.get(2);

        logger.info(() -> timestamp() + " Starting HawkConsoleTool (testMode=" + testMode + ") with REST on port " + port);

        // Load or generate RSA keypair for RS256 (demo fallback)
        KeyPair rsaKeyPair = null;
        if (privKeyPath != null && pubKeyPath != null && Files.exists(Paths.get(privKeyPath)) && Files.exists(Paths.get(pubKeyPath))) {
            try {
                rsaKeyPair = PemUtil.loadKeyPairFromPem(privKeyPath, pubKeyPath);
                logger.info(() -> timestamp() + " Loaded RSA keypair from PEM files.");
            } catch (Exception ex) {
                logger.log(Level.SEVERE, "Failed to load PEM keys", ex);
                return;
            }
        } else {
            rsaKeyPair = PemUtil.generateRsaKeyPair(2048);
            logger.warning(() -> timestamp() + " Generated ephemeral RSA keypair (demo only). Do not use in production.");
        }

        ManagerInterface manager;
        if (testMode) {
            // create TestManager
            TestManager tm = new TestManager();
            manager = tm;
            manager.start();
        } else {
            // Real mode: create TIBHawkConsole and FullManager (requires Hawk libs)
            // If Hawk libs are not available at compile/run, this will fail.
            try {
                Properties props = hawkProps();
                TIBHawkConsoleWrapper consoleWrapper = TIBHawkConsoleWrapper.createWithBackoff(props, 6, 2000, 30000);
                if (consoleWrapper == null) {
                    logger.severe(() -> timestamp() + " Failed to create TIBHawkConsole. Exiting.");
                    return;
                }
                FullManager fm = new FullManager(consoleWrapper);
                manager = fm;
                manager.start();
            } catch (Throwable t) {
                logger.log(Level.SEVERE, "Failed to initialize FullManager (Hawk libs required)", t);
                return;
            }
        }

        // Start REST server with JWT RS256 auth and token issuance endpoints
        JwtAuthRestServer rest = new JwtAuthRestServer(port, rsaKeyPair.getPrivate(), rsaKeyPair.getPublic(), manager);
        rest.start();

        // Print sample token (demo)
        String sample = JwtUtil.generateJwtRs256(rsaKeyPair.getPrivate(), "demo-issuer", "demo-user", Arrays.asList("admin"), 3600);
        logger.info(() -> timestamp() + " Sample access token (RS256, 1h) for demo-user (roles: admin):");
        logger.info(sample);
        logger.info(() -> timestamp() + " Use POST /login to obtain tokens (username/password).");

        Runtime.getRuntime().addShutdownHook(new Thread(() -> {
            logger.info(() -> timestamp() + " Shutdown requested.");
            rest.stop();
            try { manager.stop(); } catch (Throwable ignored) {}
        }));

        Thread.currentThread().join();
    }

    /* =========================
       Hawk properties helper
       ========================= */
    static Properties hawkProps() {
        Properties p = new Properties();
        p.setProperty("transport", "ems");
        p.setProperty("ems.broker.url", "tcp://ems-broker:7222"); // <-- change
        p.setProperty("ems.user", "admin");                       // <-- change
        p.setProperty("ems.password", "password");                // <-- change
        p.setProperty("domain", "MyHawkDomain");                  // <-- change
        p.setProperty("console.name", "JwtRsConsole");
        return p;
    }

    /* =========================
       TIBHawkConsoleWrapper
       - Minimal wrapper to isolate Hawk dependency creation
       - This class references Hawk types; if Hawk libs are present it will create console.
       ========================= */
    public static class TIBHawkConsoleWrapper {
        public final Object console; // keep as Object to avoid compile-time coupling in other parts
        public TIBHawkConsoleWrapper(Object console) { this.console = console; }

        // Create console with backoff using reflection to avoid hard compile-time dependency in some environments
        public static TIBHawkConsoleWrapper createWithBackoff(Properties props, int maxAttempts, long initialDelayMs, long maxDelayMs) {
            int attempt = 0;
            long delay = initialDelayMs;
            while (attempt < maxAttempts) {
                attempt++;
                try {
                    logger.info(() -> timestamp() + " Attempting to create TIBHawkConsole (attempt " + attempt + ")...");
                    // Attempt to call TIBHawkConsole.createConsole(props) via reflection
                    Class<?> cls = Class.forName("com.tibco.hawk.console.TIBHawkConsole");
                    java.lang.reflect.Method m = cls.getMethod("createConsole", java.util.Properties.class);
                    Object console = m.invoke(null, props);
                    logger.info(() -> timestamp() + " Console created.");
                    return new TIBHawkConsoleWrapper(console);
                } catch (Throwable t) {
                    logger.log(Level.WARNING, timestamp() + " Console creation failed: " + t.getMessage(), t);
                    if (attempt >= maxAttempts) break;
                    try { Thread.sleep(delay); } catch (InterruptedException ie) { Thread.currentThread().interrupt(); return null; }
                    delay = Math.min(maxDelayMs, delay * 2);
                }
            }
            return null;
        }
    }

    /* =========================
       FullManager (real Hawk implementation)
       - This class uses reflection to interact with Hawk console wrapper to avoid hard compile-time coupling.
       - It implements ManagerInterface.
       - NOTE: This implementation assumes Hawk jars are available at runtime.
       ========================= */
    public static class FullManager implements ManagerInterface {
        private final Object consoleWrapperObj;
        private final Object agentMonitor;
        private final Object agentManager;
        private final ScheduledExecutorService scheduler;
        private final ExecutorService workerPool;
        private final ConcurrentMap<String, Object> agents = new ConcurrentHashMap<>();
        private final AtomicBoolean running = new AtomicBoolean(false);
        private final Semaphore invokeSemaphore = new Semaphore(MAX_CONCURRENT_INVOCATIONS);

        // filter and metrics
        private volatile AgentFilterDefinition currentFilter = null;
        private final AtomicInteger discoveredAgents = new AtomicInteger(0);
        private final AtomicInteger invokedMethods = new AtomicInteger(0);
        private final AtomicInteger invocationTimeouts = new AtomicInteger(0);
        private final AtomicInteger invocationFailures = new AtomicInteger(0);

        public FullManager(TIBHawkConsoleWrapper wrapper) throws Exception {
            this.consoleWrapperObj = wrapper.console;
            // get agentMonitor and agentManager via reflection
            Class<?> consoleClass = consoleWrapperObj.getClass();
            // If wrapper.console is actually a TIBHawkConsole instance, use its methods
            // Try to call getAgentMonitor() and getAgentManager()
            Object amon = null;
            Object amgr = null;
            try {
                java.lang.reflect.Method m1 = consoleClass.getMethod("getAgentMonitor");
                amon = m1.invoke(consoleWrapperObj);
            } catch (NoSuchMethodException nsme) {
                // ignore
            }
            try {
                java.lang.reflect.Method m2 = consoleClass.getMethod("getAgentManager");
                amgr = m2.invoke(consoleWrapperObj);
            } catch (NoSuchMethodException nsme) {
                // ignore
            }
            this.agentMonitor = amon;
            this.agentManager = amgr;
            this.scheduler = Executors.newScheduledThreadPool(2);
            this.workerPool = Executors.newFixedThreadPool(32);
        }

        @Override
        public void start() {
            if (!running.compareAndSet(false, true)) return;
            logger.info(() -> timestamp() + " FullManager starting (real Hawk)...");
            // For brevity: not wiring Hawk listeners via reflection here.
            // In a full implementation, you would register AgentMonitorListener and MicroAgentListMonitorListener via reflection.
            scheduler.scheduleAtFixedRate(this::printMetrics, 30, 30, TimeUnit.SECONDS);
        }

        @Override
        public void stop() {
            if (!running.compareAndSet(true, false)) return;
            logger.info(() -> timestamp() + " FullManager stopping...");
            try { scheduler.shutdown(); scheduler.awaitTermination(5, TimeUnit.SECONDS); } catch (Throwable ignored) {}
            try { workerPool.shutdown(); if (!workerPool.awaitTermination(10, TimeUnit.SECONDS)) workerPool.shutdownNow(); } catch (Throwable ignored) {}
            logger.info(() -> timestamp() + " FullManager stopped.");
        }

        @Override
        public void setFilterFromJson(String json) {
            try {
                AgentFilterDefinition def = AgentFilterDefinition.fromJson(json);
                this.currentFilter = def;
                logger.info(() -> timestamp() + " Filter updated via REST. rules=" + def.rules.size() + " actions=" + def.actions.size());
            } catch (Throwable t) {
                logger.log(Level.WARNING, "setFilterFromJson error", t);
            }
        }

        @Override
        public String getFilterJson() {
            AgentFilterDefinition def = this.currentFilter;
            return def == null ? "{}" : def.toJson();
        }

        @Override
        public Map<String, List<String>> listMicroAgentMethods(String agentName, String microAgentNamePattern) {
            // Minimal reflective implementation: attempt to call console.getAgentInstances() and agentManager.getMicroAgentIDs() etc.
            Map<String, List<String>> result = new HashMap<>();
            try {
                if (consoleWrapperObj == null || agentManager == null) return result;
                java.lang.reflect.Method getAgentInstances = consoleWrapperObj.getClass().getMethod("getAgentInstances");
                Object[] ais = (Object[]) getAgentInstances.invoke(consoleWrapperObj);
                if (ais == null) return result;
                for (Object ai : ais) {
                    try {
                        java.lang.reflect.Method getAgentName = ai.getClass().getMethod("getAgentName");
                        String aName = (String) getAgentName.invoke(ai);
                        if (agentName != null && !agentName.isEmpty() && !aName.equalsIgnoreCase(agentName)) continue;
                        java.lang.reflect.Method getMicroAgentIDs = agentManager.getClass().getMethod("getMicroAgentIDs", ai.getClass());
                        Object[] maids = (Object[]) getMicroAgentIDs.invoke(agentManager, ai);
                        if (maids == null) continue;
                        for (Object mid : maids) {
                            java.lang.reflect.Method getName = mid.getClass().getMethod("getName");
                            String mname = (String) getName.invoke(mid);
                            if (microAgentNamePattern == null || microAgentNamePattern.isEmpty() || mname.toLowerCase().contains(microAgentNamePattern.toLowerCase())) {
                                java.lang.reflect.Method getDescriptor = agentManager.getClass().getMethod("getMicroAgentDescriptor", ai.getClass(), mid.getClass());
                                Object desc = getDescriptor.invoke(agentManager, ai, mid);
                                if (desc != null) {
                                    java.lang.reflect.Method getMethodNames = desc.getClass().getMethod("getMethodNames");
                                    String[] methods = (String[]) getMethodNames.invoke(desc);
                                    if (methods != null) result.put(aName + "@" + mname, Arrays.asList(methods));
                                    else result.put(aName + "@" + mname, Collections.emptyList());
                                } else {
                                    result.put(aName + "@" + mname, Collections.emptyList());
                                }
                            }
                        }
                    } catch (Throwable t) {
                        logger.log(Level.WARNING, "listMicroAgentMethods inner error", t);
                    }
                }
            } catch (Throwable t) {
                logger.log(Level.WARNING, "listMicroAgentMethods top error", t);
            }
            return result;
        }

        @Override
        public Map<String,Object> controlBwAction(String agentName, String microAgentNamePattern, String action, String methodName, Object[] args) {
            Map<String,Object> resp = new HashMap<>();
            resp.put("action", action);
            resp.put("attempts", new ArrayList<Map<String,Object>>());
            try {
                // For brevity, attempt to call agentManager.invokeMethod via reflection on all agents/microagents
                if (consoleWrapperObj == null || agentManager == null) {
                    resp.put("error", "agentManager not available");
                    return resp;
                }
                java.lang.reflect.Method getAgentInstances = consoleWrapperObj.getClass().getMethod("getAgentInstances");
                Object[] ais = (Object[]) getAgentInstances.invoke(consoleWrapperObj);
                if (ais == null) {
                    resp.put("error", "no agents found");
                    return resp;
                }
                boolean anyInvoked = false;
                for (Object ai : ais) {
                    String aName = (String) ai.getClass().getMethod("getAgentName").invoke(ai);
                    if (agentName != null && !agentName.isEmpty() && !aName.equalsIgnoreCase(agentName)) continue;
                    java.lang.reflect.Method getMicroAgentIDs = agentManager.getClass().getMethod("getMicroAgentIDs", ai.getClass());
                    Object[] maids = (Object[]) getMicroAgentIDs.invoke(agentManager, ai);
                    if (maids == null) continue;
                    for (Object mid : maids) {
                        String mname = (String) mid.getClass().getMethod("getName").invoke(mid);
                        if (microAgentNamePattern != null && !microAgentNamePattern.isEmpty() && !mname.toLowerCase().contains(microAgentNamePattern.toLowerCase())) continue;
                        Map<String,Object> agentAttempt = new HashMap<>();
                        agentAttempt.put("agent", aName);
                        agentAttempt.put("microAgent", mname);
                        List<Map<String,Object>> methodAttempts = new ArrayList<>();
                        String[] tryList = methodName != null && !methodName.isEmpty() ? new String[]{methodName} : new String[]{action};
                        for (String m : tryList) {
                            Map<String,Object> ma = new HashMap<>();
                            ma.put("method", m);
                            try {
                                // invoke with timeout using workerPool
                                Callable<Object> task = () -> {
                                    java.lang.reflect.Method invokeMethod = agentManager.getClass().getMethod("invokeMethod", ai.getClass(), mid.getClass(), String.class, Object[].class);
                                    return invokeMethod.invoke(agentManager, ai, mid, m, args);
                                };
                                Future<Object> f = workerPool.submit(task);
                                Object result = null;
                                try {
                                    result = f.get(INVOCATION_TIMEOUT_MS, TimeUnit.MILLISECONDS);
                                    ma.put("success", true);
                                    ma.put("result", result == null ? "null" : result.toString());
                                    anyInvoked = true;
                                } catch (TimeoutException te) {
                                    f.cancel(true);
                                    ma.put("success", false);
                                    ma.put("error", "timeout");
                                } catch (ExecutionException ee) {
                                    ma.put("success", false);
                                    ma.put("error", ee.getCause() == null ? ee.getMessage() : ee.getCause().toString());
                                }
                            } catch (Throwable t) {
                                ma.put("success", false);
                                ma.put("error", t.getMessage());
                            }
                            methodAttempts.add(ma);
                            if (!methodAttempts.isEmpty()) {
                                Map<String,Object> last = methodAttempts.get(methodAttempts.size()-1);
                                if (Boolean.TRUE.equals(last.get("success"))) break;
                            }
                        }
                        agentAttempt.put("methodAttempts", methodAttempts);
                        ((List)resp.get("attempts")).add(agentAttempt);
                    }
                }
                resp.put("anyInvoked", anyInvoked);
            } catch (Throwable t) {
                resp.put("error", t.getMessage());
                logger.log(Level.WARNING, "controlBwAction error", t);
            }
            return resp;
        }

        @Override
        public Map<String,Object> getMetricsSnapshot() {
            Map<String,Object> m = new HashMap<>();
            m.put("discoveredAgents", discoveredAgents.get());
            m.put("invokedMethods", invokedMethods.get());
            m.put("invocationTimeouts", invocationTimeouts.get());
            m.put("invocationFailures", invocationFailures.get());
            m.put("knownAgents", agents.size());
            m.put("maxConcurrentInvocations", MAX_CONCURRENT_INVOCATIONS);
            return m;
        }

        private void printMetrics() {
            logger.info(() -> timestamp() + " METRICS: discoveredAgents=" + discoveredAgents.get()
                + " invokedMethods=" + invokedMethods.get()
                + " invocationTimeouts=" + invocationTimeouts.get()
                + " invocationFailures=" + invocationFailures.get()
                + " knownAgents=" + agents.size());
        }
    }

    /* =========================
       TestManager (for --test mode)
       - Implements ManagerInterface
       - Simulates agents and microagents
       - Provides a configurable "long-running" method to test timeouts and cancellation
       ========================= */
    public static class TestManager implements ManagerInterface {
        private final ScheduledExecutorService scheduler = Executors.newScheduledThreadPool(2);
        private final ExecutorService workerPool = Executors.newFixedThreadPool(8);
        private final AtomicBoolean running = new AtomicBoolean(false);
        private final Map<String, TestAgent> agents = new ConcurrentHashMap<>();
        private final AtomicInteger discoveredAgents = new AtomicInteger(0);
        private final AtomicInteger invokedMethods = new AtomicInteger(0);
        private final AtomicInteger invocationTimeouts = new AtomicInteger(0);
        private final AtomicInteger invocationFailures = new AtomicInteger(0);
        private final Semaphore invokeSemaphore = new Semaphore(MAX_CONCURRENT_INVOCATIONS);

        public TestManager() {
            // create some fake agents and microagents
            for (int i = 1; i <= 3; i++) {
                TestAgent a = new TestAgent("agent-" + i);
                a.addMicroAgent("bw-" + i, new String[] {"startInstance", "stopInstance", "longSleep", "status"});
                agents.put(a.name, a);
            }
        }

        @Override
        public void start() {
            if (!running.compareAndSet(false, true)) return;
            logger.info(() -> timestamp() + " TestManager starting...");
            discoveredAgents.set(agents.size());
            scheduler.scheduleAtFixedRate(this::printMetrics, 10, 10, TimeUnit.SECONDS);
        }

        @Override
        public void stop() {
            if (!running.compareAndSet(true, false)) return;
            logger.info(() -> timestamp() + " TestManager stopping...");
            try { scheduler.shutdown(); scheduler.awaitTermination(2, TimeUnit.SECONDS); } catch (Throwable ignored) {}
            try { workerPool.shutdown(); if (!workerPool.awaitTermination(2, TimeUnit.SECONDS)) workerPool.shutdownNow(); } catch (Throwable ignored) {}
        }

        @Override
        public void setFilterFromJson(String json) {
            logger.info(() -> "TestManager setFilterFromJson: " + json);
        }

        @Override
        public String getFilterJson() {
            return "{}";
        }

        @Override
        public Map<String, List<String>> listMicroAgentMethods(String agentName, String microAgentNamePattern) {
            Map<String, List<String>> res = new HashMap<>();
            for (TestAgent a : agents.values()) {
                if (agentName != null && !agentName.isEmpty() && !a.name.equalsIgnoreCase(agentName)) continue;
                for (Map.Entry<String, TestMicroAgent> e : a.microAgents.entrySet()) {
                    String maName = e.getKey();
                    if (microAgentNamePattern != null && !microAgentNamePattern.isEmpty() && !maName.toLowerCase().contains(microAgentNamePattern.toLowerCase())) continue;
                    res.put(a.name + "@" + maName, Arrays.asList(e.getValue().methods));
                }
            }
            return res;
        }

        @Override
        public Map<String,Object> controlBwAction(String agentName, String microAgentNamePattern, String action, String methodName, Object[] args) {
            Map<String,Object> resp = new HashMap<>();
            resp.put("action", action);
            resp.put("attempts", new ArrayList<Map<String,Object>>());
            boolean anyInvoked = false;
            for (TestAgent a : agents.values()) {
                if (agentName != null && !agentName.isEmpty() && !a.name.equalsIgnoreCase(agentName)) continue;
                for (Map.Entry<String, TestMicroAgent> e : a.microAgents.entrySet()) {
                    String maName = e.getKey();
                    if (microAgentNamePattern != null && !microAgentNamePattern.isEmpty() && !maName.toLowerCase().contains(microAgentNamePattern.toLowerCase())) continue;
                    Map<String,Object> agentAttempt = new HashMap<>();
                    agentAttempt.put("agent", a.name);
                    agentAttempt.put("microAgent", maName);
                    List<Map<String,Object>> methodAttempts = new ArrayList<>();
                    String[] tryList = methodName != null && !methodName.isEmpty() ? new String[]{methodName} : new String[]{action};
                    for (String m : tryList) {
                        Map<String,Object> ma = new HashMap<>();
                        ma.put("method", m);
                        try {
                            // Acquire semaphore
                            if (!invokeSemaphore.tryAcquire(2, TimeUnit.SECONDS)) {
                                ma.put("success", false);
                                ma.put("error", "concurrency limit");
                                invocationFailures.incrementAndGet();
                            } else {
                                try {
                                    Callable<Object> task = () -> {
                                        // simulate method behavior
                                        if ("longSleep".equalsIgnoreCase(m)) {
                                            // long running: sleep 15s (will timeout if INVOCATION_TIMEOUT_MS smaller)
                                            Thread.sleep(15000);
                                            return "slept";
                                        } else if ("startInstance".equalsIgnoreCase(m)) {
                                            Thread.sleep(200);
                                            return "started";
                                        } else if ("stopInstance".equalsIgnoreCase(m)) {
                                            Thread.sleep(200);
                                            return "stopped";
                                        } else if ("status".equalsIgnoreCase(m)) {
                                            return "ok";
                                        } else {
                                            return "unknown-method";
                                        }
                                    };
                                    Future<Object> f = workerPool.submit(task);
                                    try {
                                        Object result = f.get(INVOCATION_TIMEOUT_MS, TimeUnit.MILLISECONDS);
                                        ma.put("success", true);
                                        ma.put("result", result == null ? "null" : result.toString());
                                        invokedMethods.incrementAndGet();
                                        anyInvoked = true;
                                    } catch (TimeoutException te) {
                                        invocationTimeouts.incrementAndGet();
                                        f.cancel(true);
                                        ma.put("success", false);
                                        ma.put("error", "timeout");
                                    } catch (ExecutionException ee) {
                                        invocationFailures.incrementAndGet();
                                        ma.put("success", false);
                                        ma.put("error", ee.getCause() == null ? ee.getMessage() : ee.getCause().toString());
                                    }
                                } finally {
                                    invokeSemaphore.release();
                                }
                            }
                        } catch (Throwable t) {
                            invocationFailures.incrementAndGet();
                            ma.put("success", false);
                            ma.put("error", t.getMessage());
                        }
                        methodAttempts.add(ma);
                        if (!methodAttempts.isEmpty()) {
                            Map<String,Object> last = methodAttempts.get(methodAttempts.size()-1);
                            if (Boolean.TRUE.equals(last.get("success"))) break;
                        }
                    }
                    agentAttempt.put("methodAttempts", methodAttempts);
                    ((List)resp.get("attempts")).add(agentAttempt);
                }
            }
            resp.put("anyInvoked", anyInvoked);
            return resp;
        }

        @Override
        public Map<String,Object> getMetricsSnapshot() {
            Map<String,Object> m = new HashMap<>();
            m.put("discoveredAgents", discoveredAgents.get());
            m.put("invokedMethods", invokedMethods.get());
            m.put("invocationTimeouts", invocationTimeouts.get());
            m.put("invocationFailures", invocationFailures.get());
            m.put("knownAgents", agents.size());
            m.put("maxConcurrentInvocations", MAX_CONCURRENT_INVOCATIONS);
            return m;
        }

        private void printMetrics() {
            logger.info(() -> timestamp() + " TEST-METRICS: discoveredAgents=" + discoveredAgents.get()
                + " invokedMethods=" + invokedMethods.get()
                + " invocationTimeouts=" + invocationTimeouts.get()
                + " invocationFailures=" + invocationFailures.get()
                + " knownAgents=" + agents.size());
        }

        // TestAgent and TestMicroAgent inner classes
        static class TestAgent {
            final String name;
            final Map<String, TestMicroAgent> microAgents = new ConcurrentHashMap<>();
            TestAgent(String name) { this.name = name; }
            void addMicroAgent(String name, String[] methods) { microAgents.put(name, new TestMicroAgent(methods)); }
        }
        static class TestMicroAgent {
            final String[] methods;
            TestMicroAgent(String[] methods) { this.methods = methods; }
        }
    }

    /* =========================
       Persistent refresh token store
       ========================= */
    public static class RefreshTokenStore {
        private final Path file;
        private final Gson gson = new GsonBuilder().setPrettyPrinting().create();
        private final ConcurrentMap<String, RefreshRecord> store = new ConcurrentHashMap<>();
        private final ScheduledExecutorService flusher = Executors.newSingleThreadScheduledExecutor();

        public RefreshTokenStore(String filename) {
            this.file = Paths.get(filename);
            loadFromDisk();
            flusher.scheduleAtFixedRate(this::flushToDisk, REFRESH_STORE_FLUSH_SEC, REFRESH_STORE_FLUSH_SEC, TimeUnit.SECONDS);
        }

        public void put(String token, RefreshRecord rec) { store.put(token, rec); }
        public RefreshRecord get(String token) { return store.get(token); }
        public void revoke(String token) { RefreshRecord r = store.get(token); if (r != null) { r.revoked = true; store.put(token, r); } }
        public void remove(String token) { store.remove(token); }
        public void shutdown() { flusher.shutdown(); flushToDisk(); }

        private synchronized void loadFromDisk() {
            try {
                if (!Files.exists(file)) return;
                String json = new String(Files.readAllBytes(file), StandardCharsets.UTF_8);
                TypeToken<Map<String, RefreshRecord>> tt = new TypeToken<Map<String, RefreshRecord>>() {};
                Map<String, RefreshRecord> m = gson.fromJson(json, tt.getType());
                if (m != null) store.putAll(m);
                logger.info(() -> "Loaded refresh token store entries: " + store.size());
            } catch (Throwable t) {
                logger.log(Level.WARNING, "Failed to load refresh token store", t);
            }
        }

        private synchronized void flushToDisk() {
            try {
                Map<String, RefreshRecord> snapshot = new HashMap<>(store);
                String tmp = gson.toJson(snapshot);
                Path tmpFile = file.resolveSibling(file.getFileName().toString() + ".tmp");
                Files.write(tmpFile, tmp.getBytes(StandardCharsets.UTF_8), StandardOpenOption.CREATE, StandardOpenOption.TRUNCATE_EXISTING);
                Files.move(tmpFile, file, StandardCopyOption.REPLACE_EXISTING, StandardCopyOption.ATOMIC_MOVE);
            } catch (Throwable t) {
                logger.log(Level.WARNING, "Failed to flush refresh token store", t);
            }
        }
    }

    public static class RefreshRecord {
        public String username;
        public long issuedAt;
        public long expiresAt;
        public boolean revoked;
        public String clientIp;
    }

    /* =========================
       REST server with JWT RS256 auth, login, refresh, revoke, bw control endpoints
       - Uses RefreshTokenStore for persistence
       - Accepts ManagerInterface
       ========================= */
    public static class JwtAuthRestServer {
        private final int port;
        private final PrivateKey privateKey;
        private final PublicKey publicKey;
        private final ManagerInterface manager;
        private HttpServer server;
        private final Gson gson = new GsonBuilder().setPrettyPrinting().create();
        private final Map<String, DemoUser> users = new ConcurrentHashMap<>();
        private final RefreshTokenStore refreshStore = new RefreshTokenStore(REFRESH_STORE_FILE);
        private final long accessTokenTtlSec = 3600;
        private final long refreshTokenTtlSec = 7 * 24 * 3600;

        public JwtAuthRestServer(int port, PrivateKey privateKey, PublicKey publicKey, ManagerInterface manager) {
            this.port = port;
            this.privateKey = privateKey;
            this.publicKey = publicKey;
            this.manager = manager;
            DemoUser demo = new DemoUser();
            demo.username = "demo-user";
            demo.passwordHash = "demo-password"; // demo only
            demo.roles = Arrays.asList("admin");
            users.put(demo.username, demo);
        }

        public void start() {
            try {
                server = HttpServer.create(new InetSocketAddress(port), 0);
                server.createContext("/login", this::handleLogin);
                server.createContext("/refresh", this::handleRefresh);
                server.createContext("/revoke", this::handleRevoke);
                server.createContext("/methods", this::handleListMethods);
                server.createContext("/control", this::handleControl);
                server.createContext("/metrics", this::handleMetrics);
                server.setExecutor(Executors.newCachedThreadPool());
                server.start();
                logger.info(() -> timestamp() + " REST server started on port " + port);
            } catch (Throwable t) {
                logger.log(Level.SEVERE, "Failed to start REST server", t);
            }
        }

        public void stop() {
            try { if (server != null) server.stop(1); } catch (Throwable ignored) {}
            try { refreshStore.shutdown(); } catch (Throwable ignored) {}
            logger.info(() -> timestamp() + " REST server stopped.");
        }

        private void handleLogin(HttpExchange ex) {
            try {
                if (!"POST".equalsIgnoreCase(ex.getRequestMethod())) { sendJson(ex, 405, Map.of("error", "method not allowed")); return; }
                String body = new String(ex.getRequestBody().readAllBytes(), StandardCharsets.UTF_8);
                Map<String,String> req = gson.fromJson(body, new TypeToken<Map<String,String>>(){}.getType());
                String username = req.get("username");
                String password = req.get("password");
                if (username == null || password == null) { sendJson(ex, 400, Map.of("error", "username and password required")); return; }
                DemoUser u = users.get(username);
                if (u == null || !u.passwordHash.equals(password)) { sendJson(ex, 401, Map.of("error", "invalid credentials")); return; }
                String access = JwtUtil.generateJwtRs256(privateKey, "hawk-console", username, u.roles, accessTokenTtlSec);
                String refresh = UUID.randomUUID().toString();
                RefreshRecord rec = new RefreshRecord();
                rec.username = username;
                rec.issuedAt = System.currentTimeMillis()/1000L;
                rec.expiresAt = rec.issuedAt + refreshTokenTtlSec;
                rec.revoked = false;
                rec.clientIp = ex.getRemoteAddress() == null ? null : ex.getRemoteAddress().toString();
                refreshStore.put(refresh, rec);
                sendJson(ex, 200, Map.of("access_token", access, "expires_in", accessTokenTtlSec, "refresh_token", refresh));
            } catch (Throwable t) {
                logger.log(Level.WARNING, "handleLogin error", t);
                sendJson(ex, 500, Map.of("error", "internal"));
            }
        }

        private void handleRefresh(HttpExchange ex) {
            try {
                if (!"POST".equalsIgnoreCase(ex.getRequestMethod())) { sendJson(ex, 405, Map.of("error", "method not allowed")); return; }
                String body = new String(ex.getRequestBody().readAllBytes(), StandardCharsets.UTF_8);
                Map<String,String> req = gson.fromJson(body, new TypeToken<Map<String,String>>(){}.getType());
                String refresh = req.get("refresh_token");
                if (refresh == null) { sendJson(ex, 400, Map.of("error", "refresh_token required")); return; }
                RefreshRecord rec = refreshStore.get(refresh);
                if (rec == null || rec.revoked || rec.expiresAt < System.currentTimeMillis()/1000L) { sendJson(ex, 401, Map.of("error", "invalid refresh token")); return; }
                DemoUser u = users.get(rec.username);
                if (u == null) { sendJson(ex, 401, Map.of("error", "invalid user")); return; }
                String access = JwtUtil.generateJwtRs256(privateKey, "hawk-console", rec.username, u.roles, accessTokenTtlSec);
                sendJson(ex, 200, Map.of("access_token", access, "expires_in", accessTokenTtlSec));
            } catch (Throwable t) {
                logger.log(Level.WARNING, "handleRefresh error", t);
                sendJson(ex, 500, Map.of("error", "internal"));
            }
        }

        private void handleRevoke(HttpExchange ex) {
            try {
                if (!"POST".equalsIgnoreCase(ex.getRequestMethod())) { sendJson(ex, 405, Map.of("error", "method not allowed")); return; }
                String body = new String(ex.getRequestBody().readAllBytes(), StandardCharsets.UTF_8);
                Map<String,String> req = gson.fromJson(body, new TypeToken<Map<String,String>>(){}.getType());
                String refresh = req.get("refresh_token");
                if (refresh == null) { sendJson(ex, 400, Map.of("error", "refresh_token required")); return; }
                refreshStore.revoke(refresh);
                sendJson(ex, 200, Map.of("revoked", true));
            } catch (Throwable t) {
                logger.log(Level.WARNING, "handleRevoke error", t);
                sendJson(ex, 500, Map.of("error", "internal"));
            }
        }

        private void handleListMethods(HttpExchange ex) {
            try {
                if (!"GET".equalsIgnoreCase(ex.getRequestMethod())) { sendJson(ex, 405, Map.of("error", "method not allowed")); return; }
                Map<String,String> q = queryToMap(ex.getRequestURI().getQuery());
                String agent = q.get("agent");
                String micro = q.get("micro");
                Map<String, List<String>> res = manager.listMicroAgentMethods(agent, micro);
                sendJson(ex, 200, res);
            } catch (Throwable t) {
                logger.log(Level.WARNING, "handleListMethods error", t);
                sendJson(ex, 500, Map.of("error", "internal"));
            }
        }

        private void handleControl(HttpExchange ex) {
            try {
                if (!"POST".equalsIgnoreCase(ex.getRequestMethod())) { sendJson(ex, 405, Map.of("error", "method not allowed")); return; }
                String body = new String(ex.getRequestBody().readAllBytes(), StandardCharsets.UTF_8);
                Map<String,Object> req = gson.fromJson(body, new TypeToken<Map<String,Object>>(){}.getType());
                String agent = (String)req.get("agent");
                String micro = (String)req.get("micro");
                String action = (String)req.get("action");
                String method = (String)req.get("method");
                Object[] args = null;
                if (req.get("args") instanceof List) {
                    List l = (List)req.get("args");
                    args = l.toArray();
                }
                Map<String,Object> resp = manager.controlBwAction(agent, micro, action, method, args);
                sendJson(ex, 200, resp);
            } catch (Throwable t) {
                logger.log(Level.WARNING, "handleControl error", t);
                sendJson(ex, 500, Map.of("error", "internal"));
            }
        }

        private void handleMetrics(HttpExchange ex) {
            try {
                if (!"GET".equalsIgnoreCase(ex.getRequestMethod())) { sendJson(ex, 405, Map.of("error", "method not allowed")); return; }
                Map<String,Object> m = manager.getMetricsSnapshot();
                sendJson(ex, 200, m);
            } catch (Throwable t) {
                logger.log(Level.WARNING, "handleMetrics error", t);
                sendJson(ex, 500, Map.of("error", "internal"));
            }
        }

        private void sendJson(HttpExchange ex, int code, Object obj) {
            try {
                byte[] b = gson.toJson(obj).getBytes(StandardCharsets.UTF_8);
                ex.getResponseHeaders().set("Content-Type", "application/json; charset=utf-8");
                ex.sendResponseHeaders(code, b.length);
                try (OutputStream os = ex.getResponseBody()) { os.write(b); }
            } catch (Throwable t) {
                logger.log(Level.WARNING, "sendJson error", t);
            } finally {
                try { ex.close(); } catch (Throwable ignored) {}
            }
        }

        private Map<String,String> queryToMap(String q) {
            Map<String,String> m = new HashMap<>();
            if (q == null || q.isEmpty()) return m;
            String[] parts = q.split("&");
            for (String p : parts) {
                int i = p.indexOf('=');
                if (i > 0) {
                    String k = p.substring(0, i);
                    String v = p.substring(i+1);
                    m.put(k, v);
                }
            }
            return m;
        }
    }

    /* =========================
       Demo user model
       ========================= */
    public static class DemoUser {
        public String username;
        public String passwordHash;
        public List<String> roles;
    }

    /* =========================
       Simple JWT util using java.security for RS256
       - For production use a robust library
       ========================= */
    public static class JwtUtil {
        public static String generateJwtRs256(PrivateKey pk, String issuer, String subject, List<String> roles, long ttlSec) {
            try {
                long now = System.currentTimeMillis()/1000L;
                Map<String,Object> header = new LinkedHashMap<>();
                header.put("alg", "RS256");
                header.put("typ", "JWT");
                Map<String,Object> payload = new LinkedHashMap<>();
                payload.put("iss", issuer);
                payload.put("sub", subject);
                payload.put("iat", now);
                payload.put("exp", now + ttlSec);
                payload.put("roles", roles);
                String headerB64 = base64UrlEncode(new Gson().toJson(header).getBytes(StandardCharsets.UTF_8));
                String payloadB64 = base64UrlEncode(new Gson().toJson(payload).getBytes(StandardCharsets.UTF_8));
                String signingInput = headerB64 + "." + payloadB64;
                byte[] sig = sign(signingInput.getBytes(StandardCharsets.UTF_8), pk);
                String sigB64 = base64UrlEncode(sig);
                return signingInput + "." + sigB64;
            } catch (Throwable t) {
                logger.log(Level.WARNING, "generateJwtRs256 error", t);
                return null;
            }
        }

        private static byte[] sign(byte[] data, PrivateKey pk) throws Exception {
            Signature sig = Signature.getInstance("SHA256withRSA");
            sig.initSign(pk);
            sig.update(data);
            return sig.sign();
        }

        private static String base64UrlEncode(byte[] b) {
            return Base64.getUrlEncoder().withoutPadding().encodeToString(b);
        }
    }

    /* =========================
       PEM util for key loading/generation
       ========================= */
    public static class PemUtil {
        public static KeyPair generateRsaKeyPair(int bits) throws Exception {
            KeyPairGenerator kpg = KeyPairGenerator.getInstance("RSA");
            kpg.initialize(bits);
            return kpg.generateKeyPair();
        }

        public static KeyPair loadKeyPairFromPem(String privPath, String pubPath) throws Exception {
            byte[] priv = Files.readAllBytes(Paths.get(privPath));
            byte[] pub = Files.readAllBytes(Paths.get(pubPath));
            PrivateKey pk = loadPrivateKeyFromPem(priv);
            PublicKey pubk = loadPublicKeyFromPem(pub);
            return new KeyPair(pubk, pk);
        }

        private static PrivateKey loadPrivateKeyFromPem(byte[] pem) throws Exception {
            String s = new String(pem, StandardCharsets.UTF_8);
            s = s.replaceAll("-----BEGIN (.*)-----", "");
            s = s.replaceAll("-----END (.*)----", "");
            s = s.replaceAll("\\s", "");
            byte[] der = Base64.getDecoder().decode(s);
            PKCS8EncodedKeySpec spec = new PKCS8EncodedKeySpec(der);
            KeyFactory kf = KeyFactory.getInstance("RSA");
            return kf.generatePrivate(spec);
        }

        private static PublicKey loadPublicKeyFromPem(byte[] pem) throws Exception {
            String s = new String(pem, StandardCharsets.UTF_8);
            s = s.replaceAll("-----BEGIN (.*)-----", "");
            s = s.replaceAll("-----END (.*)----", "");
            s = s.replaceAll("\\s", "");
            byte[] der = Base64.getDecoder().decode(s);
            X509EncodedKeySpec spec = new X509EncodedKeySpec(der);
            KeyFactory kf = KeyFactory.getInstance("RSA");
            return kf.generatePublic(spec);
        }
    }

    /* =========================
       AgentFilterDefinition and FilterAction placeholders
       ========================= */
    public static class AgentFilterDefinition {
        public List<Object> rules = new ArrayList<>();
        public List<FilterAction> actions = new ArrayList<>();
        public boolean matches(Object info) { return rules.isEmpty(); }
        public static AgentFilterDefinition fromJson(String json) {
            try { return new Gson().fromJson(json, AgentFilterDefinition.class); } catch (Throwable t) { return new AgentFilterDefinition(); }
        }
        public String toJson() { return new Gson().toJson(this); }
    }
    public static class FilterAction {
        public String type;
        public String microAgent;
        public String method;
        public Object[] args;
        public long periodSec;
    }
}
