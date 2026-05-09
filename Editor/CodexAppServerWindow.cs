// Editor-only manager for launching and monitoring Codex App Server from Unity.
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityLLMAPI.Chat;
using Debug = UnityEngine.Debug;

namespace UnityLLMAPI.Editor
{
    public class CodexAppServerWindow : EditorWindow
    {
        private string codexCliPath;
        private string host;
        private int port;
        private bool busy;
        private string currentOperation = string.Empty;
        private Vector2 logScroll;
        private CancellationTokenSource operationCts;

        [MenuItem("Tools/UnityLLMAPI/Codex App Server")]
        public static void Open()
        {
            var win = GetWindow<CodexAppServerWindow>(false, "Codex App Server", true);
            win.minSize = new Vector2(620, 460);
            win.Show();
        }

        private void OnEnable()
        {
            LoadSettings();
            CodexAppServerEditorController.StateChanged += Repaint;
            RunOperation("Refresh status", RefreshAllAsync);
        }

        private void OnDisable()
        {
            CodexAppServerEditorController.StateChanged -= Repaint;
            operationCts?.Cancel();
            operationCts?.Dispose();
            operationCts = null;
        }

        private void OnGUI()
        {
            DrawHeader();
            EditorGUILayout.Space(6);
            DrawSettings();
            EditorGUILayout.Space(8);
            DrawStatus();
            EditorGUILayout.Space(8);
            DrawActions();
            EditorGUILayout.Space(8);
            DrawLogs();
        }

        private void DrawHeader()
        {
            using (new EditorGUILayout.VerticalScope("box"))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    DrawStatusDot(GetOverallStatusKind(), 16);

                    using (new EditorGUILayout.VerticalScope())
                    {
                        EditorGUILayout.LabelField(GetOverallTitle(), EditorStyles.boldLabel);
                        EditorGUILayout.LabelField(GetOverallDetail(), EditorStyles.miniLabel);
                    }

                    GUILayout.FlexibleSpace();

                    using (new EditorGUI.DisabledScope(busy))
                    {
                        if (GUILayout.Button("Refresh", GUILayout.Width(84), GUILayout.Height(26)))
                        {
                            RunOperation("Refresh status", RefreshAllAsync);
                        }
                    }
                }

                if (busy)
                {
                    EditorGUILayout.LabelField("Running: " + currentOperation, EditorStyles.miniLabel);
                }
            }
        }

        private void DrawSettings()
        {
            DrawSectionTitle("Connection");
            using (new EditorGUILayout.VerticalScope("box"))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Codex CLI", GUILayout.Width(82));
                    codexCliPath = EditorGUILayout.TextField(codexCliPath);
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(86);
                    EditorGUILayout.LabelField("Resolved: " + CodexAppServerEditorController.ResolveCodexCliPath(codexCliPath), EditorStyles.miniLabel);
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Endpoint", GUILayout.Width(82));
                    host = EditorGUILayout.TextField(host, GUILayout.MinWidth(160));
                    GUILayout.Space(8);
                    EditorGUILayout.LabelField("Port", GUILayout.Width(32));
                    port = EditorGUILayout.IntField(Mathf.Clamp(port, 1, 65535), GUILayout.Width(68));
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("URL", GUILayout.Width(82));
                    EditorGUILayout.SelectableLabel(ResolvedUrl, EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight));
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Save", GUILayout.Width(92), GUILayout.Height(24)))
                    {
                        SaveSettings();
                    }

                    if (GUILayout.Button("Reload", GUILayout.Width(92), GUILayout.Height(24)))
                    {
                        LoadSettings();
                    }
                }
            }
        }

        private void DrawStatus()
        {
            DrawSectionTitle("Status");
            using (new EditorGUILayout.VerticalScope("box"))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    DrawStatusCard("Login", GetLoginTitle(), CodexAppServerEditorController.LoginStatus, GetLoginStatusKind());
                    DrawStatusCard("WebSocket", GetConnectionTitle(), CodexAppServerEditorController.ConnectionStatus, GetConnectionStatusKind());
                    DrawStatusCard("Process", GetProcessTitle(), CodexAppServerEditorController.ManagedProcessStatus, GetProcessStatusKind());
                }

                EditorGUILayout.Space(2);
                EditorGUILayout.LabelField(CodexAppServerEditorController.LastStatus, EditorStyles.wordWrappedMiniLabel);
            }
        }

        private void DrawActions()
        {
            DrawSectionTitle("Actions");
            using (new EditorGUILayout.VerticalScope("box"))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Server", EditorStyles.miniBoldLabel, GUILayout.Width(72));

                    using (new EditorGUI.DisabledScope(busy))
                    {
                        if (GUILayout.Button("Start Server", GUILayout.Height(30), GUILayout.MinWidth(150)))
                        {
                            SaveSettings();
                            RunOperation("Start app-server", StartServerAsync);
                        }

                        using (new EditorGUI.DisabledScope(!IsManagedProcessRunning()))
                        {
                            if (GUILayout.Button("Stop Managed", GUILayout.Height(30), GUILayout.MinWidth(150)))
                            {
                                CodexAppServerEditorController.StopManagedServer();
                            }
                        }
                    }
                }

                EditorGUILayout.Space(4);

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Checks", EditorStyles.miniBoldLabel, GUILayout.Width(72));

                    using (new EditorGUI.DisabledScope(busy))
                    {
                        if (GUILayout.Button("Login", GUILayout.Height(24)))
                        {
                            RunOperation("Check login", CodexAppServerEditorController.RefreshLoginStatusAsync);
                        }

                        if (GUILayout.Button("Connection", GUILayout.Height(24)))
                        {
                            RunOperation("Check connection", CheckConnectionAsync);
                        }

                        if (GUILayout.Button("Run codex login", GUILayout.Height(24), GUILayout.MinWidth(120)))
                        {
                            RunOperation("Run codex login", CodexAppServerEditorController.RunLoginAsync);
                        }
                    }
                }
            }
        }

        private void DrawLogs()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawSectionTitle("Logs");
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Clear", GUILayout.Width(70), GUILayout.Height(20)))
                {
                    CodexAppServerEditorController.ClearLog();
                }
            }

            using (new EditorGUILayout.VerticalScope("box"))
            {
                logScroll = EditorGUILayout.BeginScrollView(logScroll, GUILayout.MinHeight(120), GUILayout.ExpandHeight(true));
                foreach (var line in CodexAppServerEditorController.GetLogSnapshot())
                {
                    EditorGUILayout.LabelField(line, EditorStyles.wordWrappedMiniLabel);
                }
                EditorGUILayout.EndScrollView();
            }
        }

        private static void DrawSectionTitle(string title)
        {
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        }

        private static void DrawStatusCard(string label, string value, string detail, StatusKind statusKind)
        {
            using (new EditorGUILayout.VerticalScope("box", GUILayout.MinHeight(64), GUILayout.ExpandWidth(true)))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    DrawStatusDot(statusKind, 12);
                    EditorGUILayout.LabelField(label, EditorStyles.miniBoldLabel);
                }

                EditorGUILayout.LabelField(value, EditorStyles.boldLabel);
                EditorGUILayout.LabelField(detail, EditorStyles.wordWrappedMiniLabel);
            }
        }

        private static void DrawStatusDot(StatusKind statusKind, int size)
        {
            var style = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = size
            };
            style.normal.textColor = GetStatusColor(statusKind);
            GUILayout.Label("●", style, GUILayout.Width(size + 4), GUILayout.Height(size + 4));
        }

        private string GetOverallTitle()
        {
            if (busy)
            {
                return "Checking Codex App Server";
            }

            if (IsConnectionOnline() && IsLoginReady())
            {
                return "Ready";
            }

            if (!IsLoginReady())
            {
                return "Login needs attention";
            }

            return "Server offline";
        }

        private string GetOverallDetail()
        {
            if (busy)
            {
                return currentOperation;
            }

            return ResolvedUrl;
        }

        private StatusKind GetOverallStatusKind()
        {
            if (busy) return StatusKind.Busy;
            if (IsConnectionOnline() && IsLoginReady()) return StatusKind.Good;
            if (!IsLoginReady()) return StatusKind.Warning;
            return StatusKind.Error;
        }

        private static string GetLoginTitle()
        {
            return IsLoginReady() ? "ChatGPT" : "Check login";
        }

        private static StatusKind GetLoginStatusKind()
        {
            if (IsLoginReady()) return StatusKind.Good;
            return string.Equals(CodexAppServerEditorController.LoginStatus, "Not checked.", StringComparison.OrdinalIgnoreCase)
                ? StatusKind.Neutral
                : StatusKind.Warning;
        }

        private static string GetConnectionTitle()
        {
            return IsConnectionOnline() ? "Online" : "Offline";
        }

        private static StatusKind GetConnectionStatusKind()
        {
            if (IsConnectionOnline()) return StatusKind.Good;
            return string.Equals(CodexAppServerEditorController.ConnectionStatus, "Not checked.", StringComparison.OrdinalIgnoreCase)
                ? StatusKind.Neutral
                : StatusKind.Error;
        }

        private static string GetProcessTitle()
        {
            if (IsManagedProcessRunning())
            {
                return "Managed";
            }

            return IsConnectionOnline() ? "External" : "Stopped";
        }

        private static StatusKind GetProcessStatusKind()
        {
            if (IsManagedProcessRunning()) return StatusKind.Good;
            if (IsConnectionOnline()) return StatusKind.Good;
            return StatusKind.Neutral;
        }

        private static bool IsLoginReady()
        {
            return CodexAppServerEditorController.LoginStatus.IndexOf("Logged in", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsConnectionOnline()
        {
            return CodexAppServerEditorController.ConnectionStatus.StartsWith("Connected", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsManagedProcessRunning()
        {
            return CodexAppServerEditorController.ManagedProcessStatus.StartsWith("Running", StringComparison.OrdinalIgnoreCase);
        }

        private static Color GetStatusColor(StatusKind statusKind)
        {
            switch (statusKind)
            {
                case StatusKind.Good:
                    return new Color(0.25f, 0.82f, 0.34f);
                case StatusKind.Warning:
                    return new Color(1.0f, 0.68f, 0.22f);
                case StatusKind.Error:
                    return new Color(0.95f, 0.30f, 0.24f);
                case StatusKind.Busy:
                    return new Color(0.32f, 0.62f, 1.0f);
                default:
                    return new Color(0.55f, 0.60f, 0.66f);
            }
        }

        private string ResolvedUrl => CodexAppServerEditorController.BuildWebSocketUrl(host, port);

        private enum StatusKind
        {
            Neutral,
            Good,
            Warning,
            Error,
            Busy
        }

        private void LoadSettings()
        {
            var settings = CodexAppServerEditorController.LoadSettings();
            codexCliPath = settings.CodexCliPath;
            host = settings.Host;
            port = settings.Port;
        }

        private void SaveSettings()
        {
            CodexAppServerEditorController.SaveSettings(codexCliPath, host, port);
        }

        private Task RefreshAllAsync(CancellationToken cancellationToken)
        {
            SaveSettings();
            return Task.WhenAll(
                CodexAppServerEditorController.RefreshLoginStatusAsync(cancellationToken),
                CheckConnectionAsync(cancellationToken));
        }

        private Task CheckConnectionAsync(CancellationToken cancellationToken)
        {
            SaveSettings();
            return CodexAppServerEditorController.RefreshConnectionStatusAsync(ResolvedUrl, cancellationToken);
        }

        private Task StartServerAsync(CancellationToken cancellationToken)
        {
            SaveSettings();
            return CodexAppServerEditorController.StartServerAsync(ResolvedUrl, false, cancellationToken);
        }

        private void RunOperation(string operationName, Func<CancellationToken, Task> operation)
        {
            operationCts?.Cancel();
            operationCts?.Dispose();
            operationCts = new CancellationTokenSource();
            busy = true;
            currentOperation = operationName;
            Repaint();
            _ = RunOperationAsync(operationName, operation, operationCts.Token);
        }

        private async Task RunOperationAsync(string operationName, Func<CancellationToken, Task> operation, CancellationToken cancellationToken)
        {
            try
            {
                await operation(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                CodexAppServerEditorController.AppendLog(operationName + " canceled.");
            }
            catch (Exception ex)
            {
                CodexAppServerEditorController.AppendLog(operationName + " failed: " + ex.Message);
                Debug.LogWarning(operationName + " failed: " + ex.Message);
            }
            finally
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    busy = false;
                    currentOperation = string.Empty;
                    Repaint();
                }
            }
        }
    }

    [InitializeOnLoad]
    internal static class CodexAppServerEditorController
    {
        private const string PrefBaseUrl = "UnityLLMAPI.CODEX_APP_SERVER_BASE_URL";
        private const string PrefCliPath = "UnityLLMAPI.CODEX_CLI_PATH";
        private const string PrefHost = "UnityLLMAPI.CODEX_APP_SERVER_HOST";
        private const string PrefPort = "UnityLLMAPI.CODEX_APP_SERVER_PORT";
        private const string DefaultCliPath = "codex";
        private const string DefaultHost = "127.0.0.1";
        private const int DefaultPort = 4500;
        private const int MaxLogLines = 200;

        private static readonly object LogLock = new object();
        private static readonly List<string> LogLines = new List<string>();
        private static readonly SemaphoreSlim StartStopLock = new SemaphoreSlim(1, 1);
        private static readonly SynchronizationContext MainContext;
        private static Process managedProcess;

        public static event Action StateChanged;

        public static string LoginStatus { get; private set; } = "Not checked.";
        public static string ConnectionStatus { get; private set; } = "Not checked.";
        public static string LastStatus { get; private set; } = "Ready.";

        static CodexAppServerEditorController()
        {
            MainContext = SynchronizationContext.Current;
            CodexAppServerConnectionBootstrap.EnsureReadyAsync = EnsureReadyForRuntimeAsync;
            EditorApplication.quitting += StopManagedServer;
            AssemblyReloadEvents.beforeAssemblyReload += StopManagedServer;
        }

        public static CodexAppServerSettings LoadSettings()
        {
            var baseUrl = EditorUserSettings.GetConfigValue(PrefBaseUrl);
            var host = EditorUserSettings.GetConfigValue(PrefHost);
            var portText = EditorUserSettings.GetConfigValue(PrefPort);

            if ((string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(portText))
                && TryParseWebSocketUrl(baseUrl, out var parsedHost, out var parsedPort))
            {
                if (string.IsNullOrWhiteSpace(host))
                {
                    host = parsedHost;
                }

                if (string.IsNullOrWhiteSpace(portText))
                {
                    portText = parsedPort.ToString();
                }
            }

            if (!int.TryParse(portText, out var port))
            {
                port = DefaultPort;
            }

            return new CodexAppServerSettings
            {
                CodexCliPath = EditorUserSettings.GetConfigValue(PrefCliPath) ?? DefaultCliPath,
                Host = string.IsNullOrWhiteSpace(host) ? DefaultHost : host.Trim(),
                Port = Mathf.Clamp(port, 1, 65535)
            };
        }

        public static void SaveSettings(string cliPath, string serverHost, int serverPort)
        {
            var normalizedCli = string.IsNullOrWhiteSpace(cliPath) ? DefaultCliPath : cliPath.Trim();
            var normalizedHost = string.IsNullOrWhiteSpace(serverHost) ? DefaultHost : serverHost.Trim();
            var normalizedPort = Mathf.Clamp(serverPort, 1, 65535);
            var url = BuildWebSocketUrl(normalizedHost, normalizedPort);

            EditorUserSettings.SetConfigValue(PrefCliPath, normalizedCli);
            EditorUserSettings.SetConfigValue(PrefHost, normalizedHost);
            EditorUserSettings.SetConfigValue(PrefPort, normalizedPort.ToString());
            EditorUserSettings.SetConfigValue(PrefBaseUrl, url);
            LastStatus = "Saved Codex App Server URL: " + url;
            NotifyChanged();
        }

        public static string BuildWebSocketUrl(string serverHost, int serverPort)
        {
            var normalizedHost = string.IsNullOrWhiteSpace(serverHost) ? DefaultHost : serverHost.Trim();
            return "ws://" + normalizedHost + ":" + Mathf.Clamp(serverPort, 1, 65535);
        }

        public static string ManagedProcessStatus
        {
            get
            {
                var process = managedProcess;
                if (process == null)
                {
                    return "Not running.";
                }

                try
                {
                    return process.HasExited
                        ? "Exited with code " + process.ExitCode + "."
                        : "Running (pid " + process.Id + ").";
                }
                catch (Exception ex)
                {
                    return "Unknown: " + ex.Message;
                }
            }
        }

        public static async Task<string> EnsureReadyForRuntimeAsync(string requestedWebSocketUrl, CancellationToken cancellationToken)
        {
            var requestedUrl = NormalizeWebSocketUrl(requestedWebSocketUrl);
            var url = string.IsNullOrWhiteSpace(requestedUrl) ? LoadSettings().WebSocketUrl : requestedUrl;

            if (string.IsNullOrWhiteSpace(url))
            {
                return requestedWebSocketUrl;
            }

            if (await CanConnectAsync(url, 800, cancellationToken))
            {
                ConnectionStatus = "Connected: " + url;
                LastStatus = "Using existing Codex App Server: " + url;
                NotifyChanged();
                return url;
            }

            if (!IsLoopbackUrl(url))
            {
                LastStatus = "Auto-start skipped because URL is not loopback: " + url;
                NotifyChanged();
                return requestedWebSocketUrl;
            }

            var started = await StartServerAsync(url, false, cancellationToken);
            return started ? url : requestedWebSocketUrl;
        }

        public static async Task RefreshLoginStatusAsync(CancellationToken cancellationToken)
        {
            var result = await RunCodexCommandAsync(new[] { "login", "status" }, 6000, cancellationToken);
            var text = FirstNonEmptyLine(result.Stdout) ?? FirstNonEmptyLine(result.Stderr) ?? "(no output)";
            LoginStatus = result.ExitCode == 0 ? text : "Failed: " + text;
            AppendLog("codex login status exit=" + result.ExitCode + ": " + text);
            NotifyChanged();
        }

        public static async Task RunLoginAsync(CancellationToken cancellationToken)
        {
            AppendLog("Running codex login. Complete the browser login flow if prompted.");
            var result = await RunCodexCommandAsync(new[] { "login" }, 120000, cancellationToken);
            var text = FirstNonEmptyLine(result.Stdout) ?? FirstNonEmptyLine(result.Stderr) ?? "(no output)";
            LastStatus = result.ExitCode == 0 ? "codex login completed." : "codex login failed: " + text;
            AppendLog("codex login exit=" + result.ExitCode + ": " + text);
            await RefreshLoginStatusAsync(cancellationToken);
        }

        public static async Task RefreshConnectionStatusAsync(string url, CancellationToken cancellationToken)
        {
            var normalizedUrl = NormalizeWebSocketUrl(url);
            if (await CanConnectAsync(normalizedUrl, 2000, cancellationToken))
            {
                ConnectionStatus = "Connected: " + normalizedUrl;
                LastStatus = "Connected to Codex App Server.";
            }
            else
            {
                ConnectionStatus = "Not connected: " + normalizedUrl;
                LastStatus = "Codex App Server is not reachable.";
            }

            NotifyChanged();
        }

        public static async Task<bool> StartServerAsync(string url, bool forceStart, CancellationToken cancellationToken)
        {
            var normalizedUrl = NormalizeWebSocketUrl(url);
            if (string.IsNullOrWhiteSpace(normalizedUrl))
            {
                normalizedUrl = LoadSettings().WebSocketUrl;
            }

            if (!IsLoopbackUrl(normalizedUrl))
            {
                LastStatus = "Can only start loopback Codex App Server URLs from Unity: " + normalizedUrl;
                AppendLog(LastStatus);
                NotifyChanged();
                return false;
            }

            await StartStopLock.WaitAsync(cancellationToken);
            try
            {
                DisposeExitedManagedProcess();
                if (IsManagedProcessRunning())
                {
                    LastStatus = "Unity-managed Codex App Server is already running.";
                    NotifyChanged();
                    return true;
                }

                if (!forceStart && await CanConnectAsync(normalizedUrl, 1000, cancellationToken))
                {
                    ConnectionStatus = "Connected: " + normalizedUrl;
                    LastStatus = "Existing Codex App Server found; not starting another process.";
                    AppendLog(LastStatus);
                    NotifyChanged();
                    return true;
                }

                SaveUrlFromNormalizedUrl(normalizedUrl);
                StartManagedProcess(normalizedUrl);
            }
            finally
            {
                StartStopLock.Release();
            }

            for (var i = 0; i < 32; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (await CanConnectAsync(normalizedUrl, 350, cancellationToken))
                {
                    ConnectionStatus = "Connected: " + normalizedUrl;
                    LastStatus = "Started Unity-managed Codex App Server.";
                    AppendLog(LastStatus);
                    NotifyChanged();
                    return true;
                }

                if (!IsManagedProcessRunning())
                {
                    LastStatus = "Codex App Server process exited before it accepted WebSocket connections.";
                    AppendLog(LastStatus);
                    NotifyChanged();
                    return false;
                }

                await Task.Delay(250, cancellationToken);
            }

            LastStatus = "Timed out waiting for Codex App Server: " + normalizedUrl;
            AppendLog(LastStatus);
            NotifyChanged();
            return false;
        }

        public static void StopManagedServer()
        {
            var process = managedProcess;
            if (process == null)
            {
                LastStatus = "No Unity-managed Codex App Server process to stop.";
                NotifyChanged();
                return;
            }

            try
            {
                if (!process.HasExited)
                {
                    AppendLog("Stopping Unity-managed Codex App Server (pid " + process.Id + ").");
                    KillProcessTree(process);
                    process.WaitForExit(2000);
                }
            }
            catch (Exception ex)
            {
                AppendLog("Failed to stop Codex App Server: " + ex.Message);
            }
            finally
            {
                process.Dispose();
                if (ReferenceEquals(managedProcess, process))
                {
                    managedProcess = null;
                }

                LastStatus = "Unity-managed Codex App Server stopped.";
                NotifyChanged();
            }
        }

        public static void AppendLog(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            lock (LogLock)
            {
                LogLines.Add(DateTime.Now.ToString("HH:mm:ss") + " " + message);
                while (LogLines.Count > MaxLogLines)
                {
                    LogLines.RemoveAt(0);
                }
            }

            NotifyChanged();
        }

        public static IReadOnlyList<string> GetLogSnapshot()
        {
            lock (LogLock)
            {
                return LogLines.ToArray();
            }
        }

        public static void ClearLog()
        {
            lock (LogLock)
            {
                LogLines.Clear();
            }

            NotifyChanged();
        }

        public static string ResolveCodexCliPath(string configuredPath)
        {
            var value = string.IsNullOrWhiteSpace(configuredPath) ? DefaultCliPath : configuredPath.Trim().Trim('"');
            value = Environment.ExpandEnvironmentVariables(value);

            if (LooksLikePath(value))
            {
                return File.Exists(value) ? Path.GetFullPath(value) : value;
            }

            var pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            var extensions = Application.platform == RuntimePlatform.WindowsEditor
                ? new[] { ".exe", ".cmd", ".bat", ".ps1", string.Empty }
                : new[] { string.Empty };

            foreach (var directory in pathValue.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(directory))
                {
                    continue;
                }

                foreach (var extension in extensions)
                {
                    var candidate = Path.Combine(directory.Trim(), value + extension);
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }

            return value;
        }

        private static void StartManagedProcess(string normalizedUrl)
        {
            var settings = LoadSettings();
            var startInfo = CreateCodexStartInfo(
                settings.CodexCliPath,
                new[] { "app-server", "--listen", normalizedUrl },
                Directory.GetCurrentDirectory());

            var process = new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true
            };

            process.OutputDataReceived += (_, args) =>
            {
                if (!string.IsNullOrWhiteSpace(args.Data))
                {
                    AppendLog("[stdout] " + args.Data);
                }
            };

            process.ErrorDataReceived += (_, args) =>
            {
                if (!string.IsNullOrWhiteSpace(args.Data))
                {
                    AppendLog("[stderr] " + args.Data);
                }
            };

            process.Exited += (_, __) =>
            {
                var exitCode = -1;
                try
                {
                    exitCode = process.ExitCode;
                }
                catch
                {
                    // ignored
                }

                AppendLog("Unity-managed Codex App Server exited with code " + exitCode + ".");
                NotifyChanged();
            };

            AppendLog("Starting: " + startInfo.FileName + " " + startInfo.Arguments);
            if (!process.Start())
            {
                throw new InvalidOperationException("Failed to start Codex CLI process.");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            managedProcess = process;
            AppendLog("Started Unity-managed Codex App Server (pid " + process.Id + ").");
        }

        private static async Task<CommandResult> RunCodexCommandAsync(string[] arguments, int timeoutMilliseconds, CancellationToken cancellationToken)
        {
            var settings = LoadSettings();
            var startInfo = CreateCodexStartInfo(settings.CodexCliPath, arguments, Directory.GetCurrentDirectory());

            using (var process = new Process { StartInfo = startInfo })
            {
                AppendLog("Running: " + startInfo.FileName + " " + startInfo.Arguments);
                var stdout = new StringBuilder();
                var stderr = new StringBuilder();
                var outputLock = new object();

                process.OutputDataReceived += (_, args) =>
                {
                    if (args.Data == null)
                    {
                        return;
                    }

                    lock (outputLock)
                    {
                        stdout.AppendLine(args.Data);
                    }

                    if (!string.IsNullOrWhiteSpace(args.Data))
                    {
                        AppendLog("[codex stdout] " + args.Data);
                    }
                };

                process.ErrorDataReceived += (_, args) =>
                {
                    if (args.Data == null)
                    {
                        return;
                    }

                    lock (outputLock)
                    {
                        stderr.AppendLine(args.Data);
                    }

                    if (!string.IsNullOrWhiteSpace(args.Data))
                    {
                        AppendLog("[codex stderr] " + args.Data);
                    }
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                var exited = await Task.Run(() => process.WaitForExit(timeoutMilliseconds), cancellationToken);

                if (!exited)
                {
                    try
                    {
                        KillProcessTree(process);
                    }
                    catch
                    {
                        // ignored
                    }

                    return new CommandResult
                    {
                        ExitCode = -1,
                        Stdout = GetStringBuilderText(stdout, outputLock),
                        Stderr = "Timed out after " + timeoutMilliseconds + " ms."
                    };
                }

                process.WaitForExit(1000);
                return new CommandResult
                {
                    ExitCode = process.ExitCode,
                    Stdout = GetStringBuilderText(stdout, outputLock),
                    Stderr = GetStringBuilderText(stderr, outputLock)
                };
            }
        }

        private static string GetStringBuilderText(StringBuilder builder, object syncRoot)
        {
            lock (syncRoot)
            {
                return builder.ToString();
            }
        }

        private static ProcessStartInfo CreateCodexStartInfo(string configuredCliPath, string[] arguments, string workingDirectory)
        {
            var resolvedCliPath = ResolveCodexCliPath(configuredCliPath);
            var fileName = resolvedCliPath;
            var cliArguments = JoinArguments(arguments);

            if (Application.platform == RuntimePlatform.WindowsEditor
                && string.Equals(Path.GetExtension(resolvedCliPath), ".ps1", StringComparison.OrdinalIgnoreCase))
            {
                fileName = ResolvePowerShellPath();
                cliArguments = "-NoProfile -ExecutionPolicy Bypass -File " +
                               QuoteArgument(resolvedCliPath) +
                               (string.IsNullOrEmpty(cliArguments) ? string.Empty : " " + cliArguments);
            }

            return new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = cliArguments,
                WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? Directory.GetCurrentDirectory() : workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
        }

        private static void KillProcessTree(Process process)
        {
            if (process == null)
            {
                return;
            }

            if (Application.platform == RuntimePlatform.WindowsEditor)
            {
                try
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = "taskkill.exe",
                        Arguments = "/PID " + process.Id + " /T /F",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };

                    using (var killer = Process.Start(startInfo))
                    {
                        killer?.WaitForExit(2000);
                    }

                    return;
                }
                catch (Exception ex)
                {
                    AppendLog("taskkill failed; falling back to Process.Kill: " + ex.Message);
                }
            }

            process.Kill();
        }

        private static async Task<bool> CanConnectAsync(string url, int timeoutMilliseconds, CancellationToken cancellationToken)
        {
            var normalizedUrl = NormalizeWebSocketUrl(url);
            if (!Uri.TryCreate(normalizedUrl, UriKind.Absolute, out var uri)
                || (uri.Scheme != "ws" && uri.Scheme != "wss"))
            {
                return false;
            }

            using (var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            using (var socket = new ClientWebSocket())
            {
                timeoutCts.CancelAfter(Mathf.Max(timeoutMilliseconds, 1));
                try
                {
                    await socket.ConnectAsync(uri, timeoutCts.Token);
                    if (socket.State == WebSocketState.Open)
                    {
                        try
                        {
                            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "status check", CancellationToken.None);
                        }
                        catch
                        {
                            // ignored
                        }
                    }

                    return true;
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    return false;
                }
                catch
                {
                    return false;
                }
            }
        }

        private static bool IsManagedProcessRunning()
        {
            var process = managedProcess;
            if (process == null)
            {
                return false;
            }

            try
            {
                return !process.HasExited;
            }
            catch
            {
                return false;
            }
        }

        private static void DisposeExitedManagedProcess()
        {
            var process = managedProcess;
            if (process == null)
            {
                return;
            }

            try
            {
                if (!process.HasExited)
                {
                    return;
                }
            }
            catch
            {
                // Treat unknown process state as disposable before starting a replacement.
            }

            process.Dispose();
            if (ReferenceEquals(managedProcess, process))
            {
                managedProcess = null;
            }
        }

        private static string NormalizeWebSocketUrl(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var url = value.Trim();
            if (!url.Contains("://"))
            {
                url = "ws://" + url;
            }
            else if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                url = "ws://" + url.Substring("http://".Length);
            }
            else if (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                url = "wss://" + url.Substring("https://".Length);
            }

            return url;
        }

        private static bool IsLoopbackUrl(string url)
        {
            if (!Uri.TryCreate(NormalizeWebSocketUrl(url), UriKind.Absolute, out var uri))
            {
                return false;
            }

            return string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(uri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(uri.Host, "::1", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryParseWebSocketUrl(string value, out string parsedHost, out int parsedPort)
        {
            parsedHost = DefaultHost;
            parsedPort = DefaultPort;

            if (!Uri.TryCreate(NormalizeWebSocketUrl(value), UriKind.Absolute, out var uri))
            {
                return false;
            }

            parsedHost = uri.Host;
            parsedPort = uri.Port > 0 ? uri.Port : DefaultPort;
            return true;
        }

        private static void SaveUrlFromNormalizedUrl(string normalizedUrl)
        {
            if (!TryParseWebSocketUrl(normalizedUrl, out var parsedHost, out var parsedPort))
            {
                return;
            }

            var settings = LoadSettings();
            SaveSettings(settings.CodexCliPath, parsedHost, parsedPort);
        }

        private static bool LooksLikePath(string value)
        {
            return Path.IsPathRooted(value)
                   || value.Contains("/")
                   || value.Contains("\\");
        }

        private static string ResolvePowerShellPath()
        {
            var systemPath = Environment.GetFolderPath(Environment.SpecialFolder.System);
            var powershell = Path.Combine(systemPath, "WindowsPowerShell", "v1.0", "powershell.exe");
            return File.Exists(powershell) ? powershell : "powershell.exe";
        }

        private static string JoinArguments(IEnumerable<string> arguments)
        {
            if (arguments == null)
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            foreach (var argument in arguments)
            {
                if (builder.Length > 0)
                {
                    builder.Append(' ');
                }

                builder.Append(QuoteArgument(argument ?? string.Empty));
            }

            return builder.ToString();
        }

        private static string QuoteArgument(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "\"\"";
            }

            if (value.IndexOfAny(new[] { ' ', '\t', '\n', '\r', '"' }) < 0)
            {
                return value;
            }

            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        private static string FirstNonEmptyLine(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            using (var reader = new StringReader(text))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        return line.Trim();
                    }
                }
            }

            return null;
        }

        private static void NotifyChanged()
        {
            if (MainContext != null)
            {
                MainContext.Post(_ => StateChanged?.Invoke(), null);
            }
            else
            {
                EditorApplication.delayCall += () => StateChanged?.Invoke();
            }
        }

        private sealed class CommandResult
        {
            public int ExitCode;
            public string Stdout;
            public string Stderr;
        }
    }

    internal sealed class CodexAppServerSettings
    {
        public string CodexCliPath;
        public string Host;
        public int Port;
        public string WebSocketUrl => CodexAppServerEditorController.BuildWebSocketUrl(Host, Port);
    }
}
#endif
