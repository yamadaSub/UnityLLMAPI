using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityLLMAPI.Embedding;
using UnityLLMAPI.Schema;

namespace UnityLLMAPI.Chat
{
    internal sealed class CodexAppServerClient : IProviderClient
    {
        private const string ClientName = "unityllmapi";
        private const string ClientTitle = "UnityLLMAPI";
        private const string ClientVersion = "1.0.0";
        private const string InternalSkillInputKey = "__codexSkillInput";

        private static readonly HashSet<string> AllowedTurnStartKeys = new HashSet<string>
        {
            "cwd",
            "approvalPolicy",
            "sandboxPolicy",
            "model",
            "effort",
            "summary",
            "personality",
            "outputSchema",
            "collaborationMode"
        };

        public AIProvider Provider => AIProvider.CodexAppServer;

        public async Task<RawChatResult> SendChatAsync(
            ModelSpec model,
            IReadOnlyList<Message> messages,
            ChatRequestOptions options,
            CancellationToken ct)
        {
            if (options?.Functions != null && options.Functions.Count > 0)
            {
                return await SendFunctionCallCompatibleAsync(model, messages, options, ct);
            }

            try
            {
                var result = await RunTurnAsync(model, messages, options, null, null, ct);
                return BuildRawChatResult(model, result);
            }
            catch (Exception ex)
            {
                Debug.LogError("Codex App Server チャットに失敗しました: " + ex.Message);
                return FailureChatResult(model, ex.Message);
            }
        }

        private async Task<RawChatResult> SendFunctionCallCompatibleAsync(
            ModelSpec model,
            IReadOnlyList<Message> messages,
            ChatRequestOptions options,
            CancellationToken ct)
        {
            try
            {
                Debug.Log("Codex App Server は LLMAPI Function Calling を直接実行しないため、互換用の構造化出力で関数名と引数を抽出します。");
                var functionOptions = BuildFunctionCallOptions(options);
                var functionMessages = BuildFunctionCallMessages(messages, options.Functions);
                var outputSchema = BuildFunctionCallOutputSchema(options.Functions);
                var result = await RunTurnAsync(model, functionMessages, functionOptions, outputSchema, null, ct);
                return BuildRawFunctionCallResult(model, result);
            }
            catch (Exception ex)
            {
                Debug.LogError("Codex App Server Function Calling 互換実行に失敗しました: " + ex.Message);
                return FailureChatResult(model, ex.Message);
            }
        }

        public async Task<RawChatStreamResult> SendChatStreamAsync(
            ModelSpec model,
            IReadOnlyList<Message> messages,
            ChatRequestOptions options,
            Action<string> onContentDelta,
            CancellationToken ct)
        {
            if (options?.Functions != null && options.Functions.Count > 0)
            {
                return FailureChatStreamResult(model, "Codex App Server mode does not support the Function Calling API.");
            }

            try
            {
                var result = await RunTurnAsync(model, messages, options, null, onContentDelta, ct);
                return new RawChatStreamResult
                {
                    Provider = Provider,
                    ModelId = model.ModelId,
                    IsSuccess = result.IsSuccess,
                    StatusCode = result.StatusCode,
                    ErrorMessage = result.ErrorMessage,
                    ResponseHeaders = new Dictionary<string, string>(),
                    RawText = result.RawEventLog,
                    Content = result.Content
                };
            }
            catch (Exception ex)
            {
                Debug.LogError("Codex App Server ストリーミングチャットに失敗しました: " + ex.Message);
                return FailureChatStreamResult(model, ex.Message);
            }
        }

        public async Task<RawChatResult> SendStructuredAsync(
            ModelSpec model,
            IReadOnlyList<Message> messages,
            string jsonSchema,
            ChatRequestOptions options,
            CancellationToken ct)
        {
            try
            {
                var outputSchema = ParseSchema(jsonSchema);
                var result = await RunTurnAsync(model, messages, options, outputSchema, null, ct);
                return BuildRawChatResult(model, result);
            }
            catch (Exception ex)
            {
                Debug.LogError("Codex App Server 構造化チャットに失敗しました: " + ex.Message);
                return FailureChatResult(model, ex.Message);
            }
        }

        public async Task<RawImageResult> GenerateImageAsync(
            ModelSpec model,
            ImageGenerationRequest request,
            CancellationToken ct)
        {
            try
            {
                var additionalBody = request?.AdditionalBody == null
                    ? new Dictionary<string, object>()
                    : new Dictionary<string, object>(request.AdditionalBody);
                var target = ResolveImageOutputTarget(additionalBody);
                var outputDirectory = Path.GetDirectoryName(target.AbsolutePath);
                if (!string.IsNullOrEmpty(outputDirectory))
                {
                    Directory.CreateDirectory(outputDirectory);
                }

                EnsureImageGenerationTurnDefaults(additionalBody, target);
                var skillLookup = await TryFindSkillAsync(target.ProjectRoot, "imagegen", ct);
                if (skillLookup.Checked && !skillLookup.Available)
                {
                    return FailureImageResult(
                        model,
                        "Codex App Server imagegen skill is not available for cwd: " + target.ProjectRoot,
                        skillLookup.RawEventLog);
                }

                if (skillLookup.Available && !string.IsNullOrWhiteSpace(skillLookup.Path))
                {
                    additionalBody[InternalSkillInputKey] = new Dictionary<string, object>
                    {
                        { "name", "imagegen" },
                        { "path", skillLookup.Path }
                    };
                }
                else if (!string.IsNullOrWhiteSpace(skillLookup.ErrorMessage))
                {
                    Debug.Log("Codex App Server skills/list の確認をスキップしました: " + skillLookup.ErrorMessage);
                }

                var previousImage = CaptureFileSnapshot(target.AbsolutePath);
                var imageMessages = BuildImageGenerationMessages(request?.Messages, target.PromptPath);
                var options = new ChatRequestOptions
                {
                    AdditionalBody = additionalBody,
                    TimeoutSeconds = request?.TimeoutSeconds ?? -1
                };

                var result = await RunTurnAsync(
                    model,
                    imageMessages,
                    options,
                    null,
                    null,
                    ct,
                    target.AbsolutePath,
                    target.ProjectRoot);
                if (!result.IsSuccess)
                {
                    return FailureImageResult(model, result.ErrorMessage, result.RawEventLog);
                }

                var generatedImage = CaptureFileSnapshot(target.AbsolutePath);
                if (!generatedImage.Exists)
                {
                    return FailureImageResult(
                        model,
                        "Codex App Server completed, but the requested image file was not created: " + target.AbsolutePath,
                        result.RawEventLog);
                }

                if (!WasFileCreatedOrUpdated(previousImage, generatedImage))
                {
                    return FailureImageResult(
                        model,
                        "Codex App Server completed, but the requested image file was not updated during this run: " + target.AbsolutePath,
                        result.RawEventLog);
                }

                var imageData = File.ReadAllBytes(target.AbsolutePath);
                var mimeType = GuessImageMimeType(target.AbsolutePath);
                Debug.Log(
                    "Codex App Server image generation completed: path=" +
                    target.AbsolutePath +
                    ", bytes=" +
                    imageData.Length +
                    ", mimeType=" +
                    mimeType +
                    ", lastWriteUtc=" +
                    generatedImage.LastWriteUtc.ToString("O"));

                return new RawImageResult
                {
                    Provider = Provider,
                    ModelId = model.ModelId,
                    IsSuccess = true,
                    StatusCode = 200,
                    ErrorMessage = null,
                    ResponseHeaders = new Dictionary<string, string>(),
                    RawJson = result.RawEventLog,
                    PromptFeedback = result.Content,
                    Images = new List<GeneratedImage>
                    {
                        new GeneratedImage
                        {
                            mimeType = mimeType,
                            data = imageData
                        }
                    }
                };
            }
            catch (Exception ex)
            {
                Debug.LogError("Codex App Server 画像生成に失敗しました: " + ex.Message);
                return FailureImageResult(model, ex.Message, string.Empty);
            }
        }

        public Task<RawEmbeddingResult> CreateEmbeddingAsync(
            ModelSpec model,
            IReadOnlyList<string> texts,
            CancellationToken ct)
        {
            throw new NotSupportedException("Codex App Server mode does not support embeddings.");
        }

        private static async Task<CodexTurnResult> RunTurnAsync(
            ModelSpec model,
            IReadOnlyList<Message> messages,
            ChatRequestOptions options,
            object outputSchema,
            Action<string> onContentDelta,
            CancellationToken ct,
            string allowedFileChangePath = null,
            string fileChangeRoot = null)
        {
            using var timeoutCts = CreateTimeoutTokenSource(options?.TimeoutSeconds ?? -1, ct);
            var token = timeoutCts?.Token ?? ct;

            var baseUrl = NormalizeWebSocketUrl(ApiKeyResolver.CodexAppServerBaseUrl);
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                var message = ApiKeyResolver.GetRequiredCodexAppServerUrlHint();
                Debug.LogError(message);
                return CodexTurnResult.Failure(message);
            }

            var state = new CodexTurnState(onContentDelta, allowedFileChangePath, fileChangeRoot);
            try
            {
                using var socket = await OpenConnectionAsync(baseUrl, token);

                var nextId = 1;
                await SendRequestAndWaitAsync(socket, nextId++, "initialize", BuildInitializeParams(), state, token);
                await SendNotificationAsync(socket, "initialized", new Dictionary<string, object>(), token);

                var threadResponse = await SendRequestAndWaitAsync(
                    socket,
                    nextId++,
                    "thread/start",
                    BuildThreadStartParams(options?.AdditionalBody),
                    state,
                    token);
                var threadId = ExtractThreadId(threadResponse) ?? state.ThreadId;
                if (string.IsNullOrEmpty(threadId))
                {
                    return state.ToResult(false, "Codex App Server did not return a thread id.");
                }

                state.ThreadId = threadId;
                var turnResponse = await SendRequestAndWaitAsync(
                    socket,
                    nextId++,
                    "turn/start",
                    BuildTurnStartParams(threadId, messages, options?.AdditionalBody, outputSchema),
                    state,
                    token);
                state.TurnId = ExtractTurnId(turnResponse) ?? state.TurnId;

                while (!state.IsTurnCompleted)
                {
                    var text = await ReceiveTextMessageAsync(socket, token);
                    if (text == null)
                    {
                        return state.ToResult(false, "Codex App Server closed the connection before turn/completed.");
                    }

                    state.RecordRaw(text);
                    var message = TryParseObject(text);
                    if (message == null) continue;
                    if (message["error"] != null)
                    {
                        state.ErrorMessage = message["error"]?.ToString(Formatting.None);
                    }
                    await HandleServerMessageAsync(socket, message, state, token);
                }

                await CloseSocketQuietlyAsync(socket);
            }
            catch (CodexAppServerConnectionException ex)
            {
                return state.ToResult(false, ex.Message);
            }
            catch (Exception ex)
            {
                return state.ToResult(false, ex.Message);
            }

            return state.ToResult(state.TurnSucceeded, state.ErrorMessage);
        }

        private static Dictionary<string, object> BuildInitializeParams()
        {
            return new Dictionary<string, object>
            {
                {
                    "clientInfo",
                    new Dictionary<string, object>
                    {
                        { "name", ClientName },
                        { "title", ClientTitle },
                        { "version", ClientVersion }
                    }
                },
                {
                    "capabilities",
                    new Dictionary<string, object>
                    {
                        { "experimentalApi", true }
                    }
                }
            };
        }

        private static Dictionary<string, object> BuildThreadStartParams(Dictionary<string, object> additionalBody)
        {
            var thread = TryGetDictionary(additionalBody, "thread");
            return thread == null
                ? new Dictionary<string, object>()
                : new Dictionary<string, object>(thread);
        }

        private static Dictionary<string, object> BuildTurnStartParams(
            string threadId,
            IReadOnlyList<Message> messages,
            Dictionary<string, object> additionalBody,
            object outputSchema)
        {
            var input = BuildInput(messages);
            var skillInput = BuildSkillInput(additionalBody);
            if (skillInput != null)
            {
                input.Add(skillInput);
            }

            var body = new Dictionary<string, object>
            {
                { "threadId", threadId },
                { "input", input }
            };

            MergeCodexTurnOptions(body, additionalBody);
            if (!body.ContainsKey("effort") && additionalBody != null && additionalBody.TryGetValue("reasoningEffort", out var effort))
            {
                body["effort"] = effort;
            }

            if (outputSchema != null && !body.ContainsKey("outputSchema"))
            {
                body["outputSchema"] = outputSchema;
            }

            return body;
        }

        private static ChatRequestOptions BuildFunctionCallOptions(ChatRequestOptions source)
        {
            return new ChatRequestOptions
            {
                AdditionalBody = CloneAdditionalBodyWithoutOutputSchema(source?.AdditionalBody),
                Functions = source?.Functions,
                TimeoutSeconds = source?.TimeoutSeconds ?? -1
            };
        }

        private static Dictionary<string, object> CloneAdditionalBodyWithoutOutputSchema(Dictionary<string, object> source)
        {
            if (source == null) return new Dictionary<string, object>();

            var clone = new Dictionary<string, object>(source);
            clone.Remove("outputSchema");

            var turn = TryGetDictionary(source, "turn");
            if (turn != null)
            {
                var turnClone = new Dictionary<string, object>(turn);
                turnClone.Remove("outputSchema");
                clone["turn"] = turnClone;
            }

            return clone;
        }

        private static List<Message> BuildFunctionCallMessages(
            IReadOnlyList<Message> messages,
            IReadOnlyList<IJsonSchema> functions)
        {
            var result = messages?.ToList() ?? new List<Message>();
            result.Insert(0, new Message
            {
                role = MessageRole.System,
                content = BuildFunctionCallInstruction(functions)
            });
            return result;
        }

        private static string BuildFunctionCallInstruction(IReadOnlyList<IJsonSchema> functions)
        {
            var functionSchemas = functions?
                .Where(function => function != null)
                .Select(function => function.GenerateJsonSchema())
                .ToList() ?? new List<Dictionary<string, object>>();

            return "Select exactly one function that should be called for the user's request. "
                   + "Return only JSON that matches the requested output schema. "
                   + "The `name` field must be one of the available function names. "
                   + "The `arguments` field must be a minified JSON object string containing only the selected function arguments. "
                   + "Do not execute the function yourself.\nAvailable functions:\n"
                   + JsonConvert.SerializeObject(functionSchemas, Formatting.Indented);
        }

        private static object BuildFunctionCallOutputSchema(IReadOnlyList<IJsonSchema> functions)
        {
            var names = functions?
                .Where(function => function != null && !string.IsNullOrEmpty(function.Name))
                .Select(function => function.Name)
                .Distinct(StringComparer.Ordinal)
                .ToArray() ?? Array.Empty<string>();

            var schema = new Dictionary<string, object>
            {
                { "type", "object" },
                {
                    "properties",
                    new Dictionary<string, object>
                    {
                        {
                            "name",
                            new Dictionary<string, object>
                            {
                                { "type", "string" },
                                { "enum", names },
                                { "description", "The selected function name." }
                            }
                        },
                        {
                            "arguments",
                            new Dictionary<string, object>
                            {
                                { "type", "string" },
                                { "description", "A minified JSON object string containing the selected function arguments." }
                            }
                        }
                    }
                },
                { "required", new[] { "name", "arguments" } },
                { "additionalProperties", false }
            };

            return NormalizeOutputSchema(schema);
        }

        private static List<Dictionary<string, object>> BuildInput(IReadOnlyList<Message> messages)
        {
            var inputs = new List<Dictionary<string, object>>();
            var text = BuildTextInput(messages);
            if (!string.IsNullOrWhiteSpace(text))
            {
                inputs.Add(new Dictionary<string, object>
                {
                    { "type", "text" },
                    { "text", text }
                });
            }

            foreach (var part in EnumerateParts(messages))
            {
                if (part.type == MessageContentType.ImageUrl && !string.IsNullOrWhiteSpace(part.uri))
                {
                    inputs.Add(BuildImageInput(part.uri));
                }
                else if (part.type == MessageContentType.ImageData && part.HasData)
                {
                    var mime = string.IsNullOrEmpty(part.mimeType) ? "image/png" : part.mimeType;
                    var dataUrl = $"data:{mime};base64,{Convert.ToBase64String(part.data)}";
                    inputs.Add(BuildImageInput(dataUrl));
                }
            }

            return inputs;
        }

        private static string BuildTextInput(IReadOnlyList<Message> messages)
        {
            if (messages == null || messages.Count == 0) return string.Empty;

            var lines = new List<string>();
            foreach (var message in messages)
            {
                if (message == null) continue;

                var role = message.role.ToString().ToLowerInvariant();
                var parts = message.EnumerateParts()
                    .Where(part => part != null && part.type == MessageContentType.Text && !string.IsNullOrEmpty(part.text))
                    .Select(part => part.text)
                    .ToList();

                if (parts.Count == 0 && !string.IsNullOrEmpty(message.content))
                {
                    parts.Add(message.content);
                }

                if (parts.Count == 0) continue;
                lines.Add($"{role}: {string.Join("\n", parts)}");
            }

            return string.Join("\n\n", lines);
        }

        private static IEnumerable<MessageContent> EnumerateParts(IReadOnlyList<Message> messages)
        {
            if (messages == null) yield break;

            foreach (var message in messages)
            {
                if (message == null) continue;
                foreach (var part in message.EnumerateParts())
                {
                    if (part == null) continue;
                    yield return part;
                }
            }
        }

        private static Dictionary<string, object> BuildImageInput(string uri)
        {
            if (Uri.TryCreate(uri, UriKind.Absolute, out var parsed)
                && string.Equals(parsed.Scheme, "file", StringComparison.OrdinalIgnoreCase))
            {
                return new Dictionary<string, object>
                {
                    { "type", "localImage" },
                    { "path", parsed.LocalPath }
                };
            }

            if (IsRootedPath(uri))
            {
                return new Dictionary<string, object>
                {
                    { "type", "localImage" },
                    { "path", uri }
                };
            }

            return new Dictionary<string, object>
            {
                { "type", "image" },
                { "url", uri }
            };
        }

        private static Dictionary<string, object> BuildSkillInput(Dictionary<string, object> additionalBody)
        {
            var skill = TryGetDictionary(additionalBody, InternalSkillInputKey);
            var name = TryGetString(skill, "name");
            var path = TryGetString(skill, "path");
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            return new Dictionary<string, object>
            {
                { "type", "skill" },
                { "name", name },
                { "path", path }
            };
        }

        private static CancellationTokenSource CreateTimeoutTokenSource(int timeoutSeconds, CancellationToken ct)
        {
            if (timeoutSeconds <= 0) return null;
            var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
            return timeoutCts;
        }

        private static bool IsRootedPath(string value)
        {
            try
            {
                return !string.IsNullOrWhiteSpace(value) && Path.IsPathRooted(value);
            }
            catch
            {
                return false;
            }
        }

        private static CodexImageOutputTarget ResolveImageOutputTarget(Dictionary<string, object> additionalBody)
        {
            var cwd = TryGetString(additionalBody, "cwd");
            var turn = TryGetDictionary(additionalBody, "turn");
            if (string.IsNullOrWhiteSpace(cwd))
            {
                cwd = TryGetString(turn, "cwd");
            }

            var projectRoot = string.IsNullOrWhiteSpace(cwd) ? ResolveUnityProjectRoot() : cwd;
            projectRoot = Path.GetFullPath(projectRoot);

            var requestedPath =
                TryGetString(additionalBody, "outputPath")
                ?? TryGetString(additionalBody, "imagePath")
                ?? TryGetString(additionalBody, "savePath");

            if (string.IsNullOrWhiteSpace(requestedPath))
            {
                requestedPath = Path.Combine(
                    "Assets",
                    "Generated",
                    "codex-imagegen-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + ".png");
                additionalBody["outputPath"] = requestedPath;
            }

            var absolutePath = IsRootedPath(requestedPath)
                ? Path.GetFullPath(requestedPath)
                : Path.GetFullPath(Path.Combine(projectRoot, requestedPath));
            EnsurePathIsWithinRoot(absolutePath, projectRoot, "Codex App Server image outputPath");

            return new CodexImageOutputTarget
            {
                ProjectRoot = projectRoot,
                AbsolutePath = absolutePath,
                PromptPath = BuildPromptPath(projectRoot, absolutePath, requestedPath)
            };
        }

        private static string ResolveUnityProjectRoot()
        {
            var dataPath = Application.dataPath;
            if (!string.IsNullOrWhiteSpace(dataPath))
            {
                return Path.GetFullPath(Path.Combine(dataPath, ".."));
            }

            return Directory.GetCurrentDirectory();
        }

        private static string BuildPromptPath(string projectRoot, string absolutePath, string requestedPath)
        {
            if (!IsRootedPath(requestedPath))
            {
                return ToCodexPath(requestedPath);
            }

            var relativePath = MakeRelativePath(projectRoot, absolutePath);
            return ToCodexPath(relativePath ?? absolutePath);
        }

        private static string MakeRelativePath(string root, string path)
        {
            if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(path)) return null;

            var normalizedRoot = Path.GetFullPath(root)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var normalizedPath = Path.GetFullPath(path);
            var comparison = Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            var rootWithSeparator = normalizedRoot + Path.DirectorySeparatorChar;
            if (normalizedPath.StartsWith(rootWithSeparator, comparison))
            {
                return normalizedPath.Substring(rootWithSeparator.Length);
            }

            var rootWithAltSeparator = normalizedRoot + Path.AltDirectorySeparatorChar;
            if (normalizedPath.StartsWith(rootWithAltSeparator, comparison))
            {
                return normalizedPath.Substring(rootWithAltSeparator.Length);
            }

            return null;
        }

        private static void EnsurePathIsWithinRoot(string path, string root, string description)
        {
            if (IsSameOrChildPath(root, path)) return;
            throw new ArgumentException(description + " must be inside the configured project root: " + root);
        }

        private static bool IsSameOrChildPath(string root, string path)
        {
            if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(path)) return false;

            var normalizedRoot = NormalizeComparablePath(root, null);
            var normalizedPath = NormalizeComparablePath(path, null);
            if (string.IsNullOrEmpty(normalizedRoot) || string.IsNullOrEmpty(normalizedPath)) return false;

            var comparison = GetPathComparison();
            if (string.Equals(normalizedRoot, normalizedPath, comparison)) return true;

            var rootWithSeparator = normalizedRoot + Path.DirectorySeparatorChar;
            if (normalizedPath.StartsWith(rootWithSeparator, comparison)) return true;

            var rootWithAltSeparator = normalizedRoot + Path.AltDirectorySeparatorChar;
            return normalizedPath.StartsWith(rootWithAltSeparator, comparison);
        }

        private static string ToCodexPath(string value)
            => string.IsNullOrEmpty(value) ? value : value.Replace('\\', '/');

        private static void EnsureImageGenerationTurnDefaults(
            Dictionary<string, object> additionalBody,
            CodexImageOutputTarget target)
        {
            if (!additionalBody.ContainsKey("cwd") && !TurnContainsKey(additionalBody, "cwd"))
            {
                additionalBody["cwd"] = target.ProjectRoot;
            }

            if (!additionalBody.ContainsKey("approvalPolicy") && !TurnContainsKey(additionalBody, "approvalPolicy"))
            {
                additionalBody["approvalPolicy"] = "never";
            }

            if (!additionalBody.ContainsKey("sandboxPolicy") && !TurnContainsKey(additionalBody, "sandboxPolicy"))
            {
                additionalBody["sandboxPolicy"] = new Dictionary<string, object>
                {
                    { "type", "workspaceWrite" },
                    { "writableRoots", new List<string> { target.ProjectRoot } },
                    { "networkAccess", true }
                };
            }
        }

        private static bool TurnContainsKey(Dictionary<string, object> additionalBody, string key)
        {
            var turn = TryGetDictionary(additionalBody, "turn");
            return turn != null && turn.ContainsKey(key);
        }

        private static List<Message> BuildImageGenerationMessages(
            IReadOnlyList<Message> messages,
            string outputPath)
        {
            var sourceText = BuildTextInput(messages);
            if (string.IsNullOrWhiteSpace(sourceText))
            {
                sourceText = "Generate one image.";
            }

            var builder = new StringBuilder();
            if (sourceText.IndexOf("$imagegen", StringComparison.OrdinalIgnoreCase) < 0)
            {
                builder.Append("$imagegen ");
            }

            builder.Append(sourceText.Trim());
            builder.AppendLine();
            builder.AppendLine();
            builder.Append("Create exactly one final image and save it as a PNG file at ");
            builder.Append(ToCodexPath(outputPath));
            builder.Append(". Do not only describe the image; write the file to that path.");

            var parts = new List<MessageContent>
            {
                MessageContent.FromText(builder.ToString())
            };

            foreach (var part in EnumerateParts(messages))
            {
                if (part.type == MessageContentType.ImageUrl || part.type == MessageContentType.ImageData)
                {
                    parts.Add(part);
                }
            }

            return new List<Message>
            {
                new Message
                {
                    role = MessageRole.User,
                    parts = parts
                }
            };
        }

        private static string GuessImageMimeType(string path)
        {
            var extension = Path.GetExtension(path)?.ToLowerInvariant();
            switch (extension)
            {
                case ".jpg":
                case ".jpeg":
                    return "image/jpeg";
                case ".webp":
                    return "image/webp";
                default:
                    return "image/png";
            }
        }

        private static async Task<CodexSkillLookupResult> TryFindSkillAsync(
            string cwd,
            string skillName,
            CancellationToken ct)
        {
            var baseUrl = NormalizeWebSocketUrl(ApiKeyResolver.CodexAppServerBaseUrl);
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                return CodexSkillLookupResult.Skipped(ApiKeyResolver.GetRequiredCodexAppServerUrlHint());
            }

            var state = new CodexTurnState(null, null, cwd);
            try
            {
                using var socket = await OpenConnectionAsync(baseUrl, ct);

                var nextId = 1;
                await SendRequestAndWaitAsync(socket, nextId++, "initialize", BuildInitializeParams(), state, ct);
                await SendNotificationAsync(socket, "initialized", new Dictionary<string, object>(), ct);

                var response = await SendRequestAndWaitAsync(
                    socket,
                    nextId++,
                    "skills/list",
                    new Dictionary<string, object>
                    {
                        { "cwds", new List<string> { cwd } },
                        { "forceReload", true }
                    },
                    state,
                    ct);

                await CloseSocketQuietlyAsync(socket);
                return ParseSkillLookupResponse(response, state.RawEventLog, skillName);
            }
            catch (CodexAppServerConnectionException ex)
            {
                return CodexSkillLookupResult.Skipped(ex.Message);
            }
            catch (Exception ex)
            {
                return CodexSkillLookupResult.Skipped(ex.Message);
            }
        }

        private static CodexSkillLookupResult ParseSkillLookupResponse(
            JObject response,
            string rawEventLog,
            string skillName)
        {
            var entries = response?["result"]?["data"] as JArray;
            if (entries == null)
            {
                return CodexSkillLookupResult.Skipped("skills/list did not return a data array.");
            }

            foreach (var skill in entries
                         .OfType<JObject>()
                         .SelectMany(entry => (entry["skills"] as JArray)?.OfType<JObject>() ?? Enumerable.Empty<JObject>()))
            {
                var name = skill["name"]?.ToString();
                if (!string.Equals(name, skillName, StringComparison.OrdinalIgnoreCase)) continue;

                return new CodexSkillLookupResult
                {
                    Checked = true,
                    Available = string.Equals(skill["enabled"]?.ToString(), "true", StringComparison.OrdinalIgnoreCase),
                    Path = skill["path"]?.ToString(),
                    RawEventLog = rawEventLog
                };
            }

            return new CodexSkillLookupResult
            {
                Checked = true,
                Available = false,
                RawEventLog = rawEventLog
            };
        }

        private static void MergeCodexTurnOptions(Dictionary<string, object> body, Dictionary<string, object> additionalBody)
        {
            if (body == null || additionalBody == null) return;

            foreach (var kv in additionalBody)
            {
                if (kv.Key == "thread" || kv.Key == "reasoningEffort") continue;
                if (AllowedTurnStartKeys.Contains(kv.Key))
                {
                    var normalized = NormalizeTurnOption(kv.Key, kv.Value);
                    if (kv.Key == "model" && normalized == null) continue;
                    body[kv.Key] = normalized;
                }
            }

            var turn = TryGetDictionary(additionalBody, "turn");
            if (turn == null) return;

            foreach (var kv in turn)
            {
                if (AllowedTurnStartKeys.Contains(kv.Key))
                {
                    var normalized = NormalizeTurnOption(kv.Key, kv.Value);
                    if (kv.Key == "model" && normalized == null) continue;
                    body[kv.Key] = normalized;
                }
            }
        }

        private static object NormalizeTurnOption(string key, object value)
        {
            if (key == "outputSchema")
            {
                return NormalizeOutputSchema(value);
            }

            if (key == "model")
            {
                return NormalizeCodexModel(value);
            }

            return value;
        }

        private static object NormalizeCodexModel(object value)
        {
            if (value is CodexAppServerModelType typedModel)
            {
                return CodexAppServerModelOptions.ToModelId(typedModel);
            }

            if (value is Enum enumValue)
            {
                Debug.Log("Codex App Server model option ignored because it is not CodexAppServerModelType: " + enumValue.GetType().Name + "." + enumValue);
                return null;
            }

            if (value is int numeric
                && Enum.IsDefined(typeof(CodexAppServerModelType), numeric))
            {
                return CodexAppServerModelOptions.ToModelId((CodexAppServerModelType)numeric);
            }

            if (value is string text
                && Enum.TryParse(text, true, out CodexAppServerModelType parsed)
                && Enum.IsDefined(typeof(CodexAppServerModelType), parsed))
            {
                return CodexAppServerModelOptions.ToModelId(parsed);
            }

            return value;
        }

        private static Dictionary<string, object> TryGetDictionary(Dictionary<string, object> source, string key)
        {
            if (source == null || !source.TryGetValue(key, out var value) || value == null) return null;
            if (value is Dictionary<string, object> dictionary) return dictionary;
            if (value is JObject jobject) return jobject.ToObject<Dictionary<string, object>>();
            return null;
        }

        private static string TryGetString(Dictionary<string, object> source, string key)
        {
            if (source == null || !source.TryGetValue(key, out var value) || value == null) return null;
            if (value is string text) return text;
            if (value is JValue jvalue) return jvalue.Type == JTokenType.Null ? null : jvalue.ToString();
            return value.ToString();
        }

        private static object ParseSchema(string jsonSchema)
        {
            if (string.IsNullOrWhiteSpace(jsonSchema)) return null;
            try
            {
                var token = JToken.Parse(jsonSchema);
                return NormalizeOutputSchema(token);
            }
            catch
            {
                return null;
            }
        }

        private static object NormalizeOutputSchema(object schema)
        {
            if (schema == null) return null;

            try
            {
                var token = schema as JToken ?? JToken.FromObject(schema);
                token = UnwrapNamedJsonSchema(token).DeepClone();
                AddStrictObjectSchemaFields(token);
                return token.ToObject<Dictionary<string, object>>();
            }
            catch
            {
                return schema;
            }
        }

        private static JToken UnwrapNamedJsonSchema(JToken token)
        {
            if (token is JObject obj && obj["schema"] is JObject wrappedSchema)
            {
                return wrappedSchema;
            }

            return token;
        }

        private static void AddStrictObjectSchemaFields(JToken token)
        {
            if (token is JObject obj)
            {
                if (IsObjectSchema(obj))
                {
                    obj["additionalProperties"] = false;
                }

                foreach (var property in obj.Properties().ToList())
                {
                    AddStrictObjectSchemaFields(property.Value);
                }
            }
            else if (token is JArray array)
            {
                foreach (var item in array)
                {
                    AddStrictObjectSchemaFields(item);
                }
            }
        }

        private static bool IsObjectSchema(JObject obj)
        {
            var type = obj["type"];
            if (type == null)
            {
                return obj["properties"] is JObject;
            }

            if (type.Type == JTokenType.String)
            {
                return string.Equals(type.ToString(), "object", StringComparison.Ordinal);
            }

            return type is JArray array && array.Any(item =>
                item.Type == JTokenType.String
                && string.Equals(item.ToString(), "object", StringComparison.Ordinal));
        }

        private static async Task<JObject> SendRequestAndWaitAsync(
            ClientWebSocket socket,
            int id,
            string method,
            object parameters,
            CodexTurnState state,
            CancellationToken ct)
        {
            await SendJsonAsync(socket, new JObject
            {
                { "id", id },
                { "method", method },
                { "params", parameters == null ? new JObject() : JObject.FromObject(parameters) }
            }, ct);

            while (true)
            {
                var text = await ReceiveTextMessageAsync(socket, ct);
                if (text == null)
                {
                    throw new InvalidOperationException("Codex App Server closed the connection.");
                }

                state.RecordRaw(text);
                var message = TryParseObject(text);
                if (message == null) continue;

                if (message["method"] == null && IsMatchingJsonRpcId(message["id"], id))
                {
                    if (message["error"] != null)
                    {
                        throw new InvalidOperationException("Codex App Server request failed: " + message["error"]?.ToString(Formatting.None));
                    }

                    return message;
                }

                await HandleServerMessageAsync(socket, message, state, ct);
            }
        }

        private static bool IsMatchingJsonRpcId(JToken actual, int expected)
        {
            if (actual == null || actual.Type == JTokenType.Null) return false;
            if (actual.Type == JTokenType.Integer) return actual.Value<int>() == expected;
            return string.Equals(actual.ToString(), expected.ToString(), StringComparison.Ordinal);
        }

        private static Task SendNotificationAsync(
            ClientWebSocket socket,
            string method,
            object parameters,
            CancellationToken ct)
        {
            return SendJsonAsync(socket, new JObject
            {
                { "method", method },
                { "params", parameters == null ? new JObject() : JObject.FromObject(parameters) }
            }, ct);
        }

        private static async Task SendJsonAsync(ClientWebSocket socket, JObject body, CancellationToken ct)
        {
            var json = body.ToString(Formatting.None);
            var bytes = Encoding.UTF8.GetBytes(json);
            await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
        }

        private static async Task<string> ReceiveTextMessageAsync(ClientWebSocket socket, CancellationToken ct)
        {
            var buffer = new byte[8192];
            using var stream = new MemoryStream();
            while (true)
            {
                var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return null;
                }

                if (result.MessageType != WebSocketMessageType.Text)
                {
                    throw new InvalidOperationException("Codex App Server returned a non-text WebSocket message.");
                }

                stream.Write(buffer, 0, result.Count);
                if (result.EndOfMessage)
                {
                    return Encoding.UTF8.GetString(stream.ToArray());
                }
            }
        }

        private static async Task HandleServerMessageAsync(
            ClientWebSocket socket,
            JObject message,
            CodexTurnState state,
            CancellationToken ct)
        {
            if (message == null) return;

            var method = message["method"]?.ToString();
            if (!string.IsNullOrEmpty(method) && message["id"] != null)
            {
                await RespondToServerRequestAsync(socket, message, method, state, ct);
                return;
            }

            HandleNotification(message, state);
        }

        private static async Task RespondToServerRequestAsync(
            ClientWebSocket socket,
            JObject message,
            string method,
            CodexTurnState state,
            CancellationToken ct)
        {
            JObject result;
            switch (method)
            {
                case "item/commandExecution/requestApproval":
                    result = new JObject { { "decision", "decline" } };
                    state.RecordClientNote("Declined command execution approval request.");
                    break;
                case "item/fileChange/requestApproval":
                    if (TryApproveAllowedFileChange(message, state, out var fileChangeNote))
                    {
                        result = new JObject { { "decision", "accept" } };
                        state.RecordClientNote(fileChangeNote);
                    }
                    else
                    {
                        result = new JObject { { "decision", "decline" } };
                        state.RecordClientNote(fileChangeNote);
                    }
                    break;
                case "execCommandApproval":
                case "applyPatchApproval":
                    result = new JObject { { "decision", "denied" } };
                    state.RecordClientNote("Denied legacy approval request.");
                    break;
                case "item/tool/requestUserInput":
                    result = new JObject { { "answers", new JObject() } };
                    state.RecordClientNote("Returned empty answers for user input request.");
                    break;
                case "item/tool/call":
                    result = new JObject
                    {
                        { "success", false },
                        { "output", "UnityLLMAPI Codex App Server client does not provide dynamic tools." }
                    };
                    state.RecordClientNote("Rejected dynamic tool call request.");
                    break;
                default:
                    await SendJsonAsync(socket, BuildJsonRpcError(message["id"], -32601, "Unsupported Codex App Server request: " + method), ct);
                    state.RecordClientNote("Rejected unsupported server request: " + method);
                    return;
            }

            await SendJsonAsync(socket, BuildJsonRpcResponse(message["id"], result), ct);
        }

        private static bool TryApproveAllowedFileChange(JObject message, CodexTurnState state, out string note)
        {
            note = "Declined file change approval request.";
            if (state == null || !state.AllowFileChangeApprovals)
            {
                return false;
            }

            var itemId = message["params"]?["itemId"]?.ToString()
                         ?? message["itemId"]?.ToString();
            var requestedPaths = state.GetFileChangePaths(itemId);
            if (requestedPaths.Count == 0)
            {
                requestedPaths = ExtractPathLikeValues(message["params"] ?? message)
                    .Select(path => NormalizeComparablePath(path, state.FileChangeRoot))
                    .Where(path => !string.IsNullOrEmpty(path))
                    .Distinct(GetPathStringComparer())
                    .ToList();
            }

            if (requestedPaths.Count == 0)
            {
                note = "Declined file change approval request because no requested path was found.";
                return false;
            }

            var comparison = GetPathComparison();
            var allPathsAllowed = requestedPaths.All(path => string.Equals(path, state.AllowedFileChangePath, comparison));
            if (allPathsAllowed)
            {
                note = "Accepted file change approval request for image generation: " + string.Join(", ", requestedPaths);
                return true;
            }

            note = "Declined file change approval request. Expected only " +
                   state.AllowedFileChangePath +
                   ", requested " +
                   string.Join(", ", requestedPaths);
            return false;
        }

        private static IEnumerable<string> ExtractPathLikeValues(JToken token)
        {
            if (token == null) yield break;

            if (token is JObject obj)
            {
                foreach (var property in obj.Properties())
                {
                    if (IsPathLikeKey(property.Name))
                    {
                        foreach (var value in ExtractStringLeaves(property.Value))
                        {
                            yield return value;
                        }
                    }
                    else
                    {
                        foreach (var value in ExtractPathLikeValues(property.Value))
                        {
                            yield return value;
                        }
                    }
                }
            }
            else if (token is JArray array)
            {
                foreach (var item in array)
                {
                    foreach (var value in ExtractPathLikeValues(item))
                    {
                        yield return value;
                    }
                }
            }
        }

        private static IEnumerable<string> ExtractStringLeaves(JToken token)
        {
            if (token == null) yield break;

            if (token.Type == JTokenType.String)
            {
                var value = token.ToString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    yield return value;
                }
                yield break;
            }

            foreach (var child in token.Children())
            {
                foreach (var value in ExtractStringLeaves(child))
                {
                    yield return value;
                }
            }
        }

        private static bool IsPathLikeKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return false;

            var normalized = key.Replace("_", string.Empty)
                .Replace("-", string.Empty)
                .ToLowerInvariant();
            return normalized == "path"
                   || normalized == "filepath"
                   || normalized == "absolutepath"
                   || normalized == "relativepath"
                   || normalized == "targetpath"
                   || normalized == "outputpath"
                   || normalized == "grantroot";
        }

        private static JObject BuildJsonRpcResponse(JToken id, JToken result)
        {
            return new JObject
            {
                { "id", id?.DeepClone() ?? JValue.CreateNull() },
                { "result", result ?? new JObject() }
            };
        }

        private static JObject BuildJsonRpcError(JToken id, int code, string message)
        {
            return new JObject
            {
                { "id", id?.DeepClone() ?? JValue.CreateNull() },
                {
                    "error",
                    new JObject
                    {
                        { "code", code },
                        { "message", message }
                    }
                }
            };
        }

        private static void HandleNotification(JObject message, CodexTurnState state)
        {
            var method = message["method"]?.ToString();
            if (string.IsNullOrEmpty(method))
            {
                HandleLegacyEvent(message, state);
                return;
            }

            var parameters = message["params"] as JObject;
            switch (method)
            {
                case "thread/started":
                    state.ThreadId = parameters?["thread"]?["id"]?.ToString() ?? parameters?["threadId"]?.ToString() ?? state.ThreadId;
                    break;
                case "turn/started":
                    if (IsCurrentThread(parameters, state.ThreadId))
                    {
                        state.TurnId = parameters?["turn"]?["id"]?.ToString() ?? parameters?["turnId"]?.ToString() ?? state.TurnId;
                    }
                    break;
                case "item/started":
                    if (IsCurrentTurn(parameters, state))
                    {
                        var item = parameters?["item"] as JObject;
                        if (IsFileChangeItem(item))
                        {
                            state.RecordFileChangeItem(item);
                        }
                    }
                    break;
                case "item/agentMessage/delta":
                    if (IsCurrentTurn(parameters, state))
                    {
                        state.AppendAgentDelta(parameters?["delta"]?.ToString());
                    }
                    break;
                case "item/completed":
                    if (IsCurrentTurn(parameters, state))
                    {
                        var item = parameters?["item"] as JObject;
                        if (IsAgentMessageItem(item))
                        {
                            state.SetCompletedAgentMessage(ExtractAgentMessageText(item));
                        }
                    }
                    break;
                case "rawResponseItem/completed":
                    break;
                case "turn/completed":
                    if (IsCurrentTurn(parameters, state))
                    {
                        state.IsTurnCompleted = true;
                        state.TurnSucceeded = IsSuccessfulTurn(parameters);
                        state.ErrorMessage = parameters?["turn"]?["error"]?.ToString(Formatting.None);
                    }
                    break;
                case "error":
                    state.ErrorMessage = parameters?["message"]?.ToString() ?? parameters?.ToString(Formatting.None);
                    break;
                default:
                    var legacyEvent = parameters?["event"] as JObject
                                      ?? parameters?["msg"] as JObject
                                      ?? parameters?["message"] as JObject;
                    if (legacyEvent != null)
                    {
                        HandleLegacyEvent(legacyEvent, state);
                    }
                    break;
            }
        }

        private static void HandleLegacyEvent(JObject evt, CodexTurnState state)
        {
            var type = evt?["type"]?.ToString();
            if (string.IsNullOrEmpty(type)) return;

            switch (type)
            {
                case "agent_message_delta":
                case "agent_message_content_delta":
                    if (IsCurrentTurn(evt, state))
                    {
                        state.AppendAgentDelta(evt["delta"]?.ToString());
                    }
                    break;
                case "agent_message":
                    state.SetCompletedAgentMessage(evt["message"]?.ToString());
                    break;
                case "item_completed":
                    if (IsCurrentTurn(evt, state))
                    {
                        var item = evt["item"] as JObject;
                        if (IsAgentMessageItem(item))
                        {
                            state.SetCompletedAgentMessage(ExtractAgentMessageText(item));
                        }
                    }
                    break;
                case "task_complete":
                case "turn_complete":
                case "turn_completed":
                    state.SetCompletedAgentMessage(evt["last_agent_message"]?.ToString());
                    state.IsTurnCompleted = true;
                    state.TurnSucceeded = true;
                    break;
                case "turn_aborted":
                    state.IsTurnCompleted = true;
                    state.TurnSucceeded = false;
                    state.ErrorMessage = evt["reason"]?.ToString() ?? "Codex App Server turn was aborted.";
                    break;
                case "stream_error":
                case "error":
                    state.ErrorMessage = evt["message"]?.ToString() ?? evt.ToString(Formatting.None);
                    break;
            }
        }

        private static bool IsCurrentThread(JObject parameters, string threadId)
        {
            if (string.IsNullOrEmpty(threadId)) return true;
            var parameterThreadId = parameters?["threadId"]?.ToString()
                                    ?? parameters?["thread_id"]?.ToString()
                                    ?? parameters?["conversationId"]?.ToString()
                                    ?? parameters?["thread"]?["id"]?.ToString();
            return string.IsNullOrEmpty(parameterThreadId) || parameterThreadId == threadId;
        }

        private static bool IsCurrentTurn(JObject parameters, CodexTurnState state)
        {
            if (!IsCurrentThread(parameters, state.ThreadId)) return false;
            if (string.IsNullOrEmpty(state.TurnId)) return true;

            var turnId = parameters?["turnId"]?.ToString()
                         ?? parameters?["turn_id"]?.ToString()
                         ?? parameters?["turn"]?["id"]?.ToString();
            return string.IsNullOrEmpty(turnId) || turnId == state.TurnId;
        }

        private static bool IsSuccessfulTurn(JObject parameters)
        {
            var status = parameters?["turn"]?["status"]?.ToString()
                         ?? parameters?["status"]?.ToString();
            return string.IsNullOrEmpty(status)
                   || string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(status, "finished", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsAgentMessageItem(JObject item)
        {
            var type = item?["type"]?.ToString();
            return string.Equals(type, "agentMessage", StringComparison.Ordinal)
                   || string.Equals(type, "AgentMessage", StringComparison.Ordinal);
        }

        private static bool IsFileChangeItem(JObject item)
        {
            var type = item?["type"]?.ToString();
            return string.Equals(type, "fileChange", StringComparison.Ordinal)
                   || string.Equals(type, "FileChange", StringComparison.Ordinal);
        }

        private static string ExtractAgentMessageText(JObject item)
        {
            if (item == null) return null;

            var text = item["text"]?.ToString();
            if (!string.IsNullOrEmpty(text)) return text;

            var content = item["content"] as JArray;
            if (content == null || content.Count == 0) return null;

            var builder = new StringBuilder();
            foreach (var part in content.OfType<JObject>())
            {
                var partText = part["text"]?.ToString();
                if (!string.IsNullOrEmpty(partText))
                {
                    builder.Append(partText);
                }
            }

            return builder.Length == 0 ? null : builder.ToString();
        }

        private static string ExtractThreadId(JObject response)
        {
            return response?["result"]?["thread"]?["id"]?.ToString()
                   ?? response?["result"]?["threadId"]?.ToString();
        }

        private static string ExtractTurnId(JObject response)
        {
            return response?["result"]?["turn"]?["id"]?.ToString()
                   ?? response?["result"]?["turnId"]?.ToString();
        }

        private static JObject TryParseObject(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            try
            {
                return JObject.Parse(text);
            }
            catch
            {
                return null;
            }
        }

        private static string NormalizeWebSocketUrl(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
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

        private static async Task<ClientWebSocket> OpenConnectionAsync(string baseUrl, CancellationToken ct)
        {
            var socket = new ClientWebSocket();
            try
            {
                await socket.ConnectAsync(new Uri(baseUrl), ct);
                return socket;
            }
            catch (Exception ex)
            {
                socket.Dispose();
                throw new CodexAppServerConnectionException(BuildConnectionFailureMessage(baseUrl, ex), ex);
            }
        }

        private static string BuildConnectionFailureMessage(string baseUrl, Exception exception)
        {
            var reason = exception == null ? "不明なエラー" : exception.Message;
            return "Codex App Server に接続できません: " +
                   baseUrl +
                   "。`codex app-server` が起動していることと、設定された WebSocket URL が正しいことを確認してください。詳細: " +
                   reason;
        }

        private static FileSnapshot CaptureFileSnapshot(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return FileSnapshot.Missing;

            try
            {
                var info = new FileInfo(path);
                if (!info.Exists) return FileSnapshot.Missing;

                return new FileSnapshot
                {
                    Exists = true,
                    Length = info.Length,
                    LastWriteUtc = info.LastWriteTimeUtc
                };
            }
            catch
            {
                return FileSnapshot.Missing;
            }
        }

        private static bool WasFileCreatedOrUpdated(FileSnapshot before, FileSnapshot after)
        {
            if (after == null || !after.Exists) return false;
            if (before == null || !before.Exists) return true;

            return after.Length != before.Length || after.LastWriteUtc > before.LastWriteUtc;
        }

        private static string NormalizeComparablePath(string value, string root)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;

            try
            {
                var path = value.Trim();
                if (Uri.TryCreate(path, UriKind.Absolute, out var uri)
                    && string.Equals(uri.Scheme, "file", StringComparison.OrdinalIgnoreCase))
                {
                    path = uri.LocalPath;
                }

                if (!Path.IsPathRooted(path) && !string.IsNullOrWhiteSpace(root))
                {
                    path = Path.Combine(root, path);
                }

                return Path.GetFullPath(path)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return value.Trim()
                    .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
                    .TrimEnd(Path.DirectorySeparatorChar);
            }
        }

        private static StringComparison GetPathComparison()
            => Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

        private static StringComparer GetPathStringComparer()
            => Path.DirectorySeparatorChar == '\\'
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;

        private static async Task CloseSocketQuietlyAsync(ClientWebSocket socket)
        {
            try
            {
                if (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "completed", CancellationToken.None);
                }
            }
            catch (WebSocketException)
            {
                // Some app-server builds close the socket without a full close handshake after turn/completed.
            }
            catch (InvalidOperationException)
            {
                // The socket may already be closed by the remote endpoint.
            }
        }

        private static RawChatResult BuildRawChatResult(ModelSpec model, CodexTurnResult result)
        {
            return new RawChatResult
            {
                Provider = AIProvider.CodexAppServer,
                ModelId = model.ModelId,
                IsSuccess = result.IsSuccess,
                StatusCode = result.StatusCode,
                ErrorMessage = result.ErrorMessage,
                ResponseHeaders = new Dictionary<string, string>(),
                RawJson = result.RawEventLog,
                Body = BuildOpenAiCompatibleBody(result.Content)
            };
        }

        private static RawChatResult BuildRawFunctionCallResult(ModelSpec model, CodexTurnResult result)
        {
            return new RawChatResult
            {
                Provider = AIProvider.CodexAppServer,
                ModelId = model.ModelId,
                IsSuccess = result.IsSuccess,
                StatusCode = result.StatusCode,
                ErrorMessage = result.ErrorMessage,
                ResponseHeaders = new Dictionary<string, string>(),
                RawJson = result.RawEventLog,
                Body = BuildOpenAiFunctionCallCompatibleBody(result.Content)
            };
        }

        private static JObject BuildOpenAiCompatibleBody(string content)
        {
            return new JObject
            {
                {
                    "choices",
                    new JArray
                    {
                        new JObject
                        {
                            {
                                "message",
                                new JObject
                                {
                                    { "role", "assistant" },
                                    { "content", content ?? string.Empty }
                                }
                            }
                        }
                    }
                }
            };
        }

        private static JObject BuildOpenAiFunctionCallCompatibleBody(string content)
        {
            var call = TryParseFunctionCallOutput(content);
            if (call == null)
            {
                return BuildOpenAiCompatibleBody(content);
            }

            return new JObject
            {
                {
                    "choices",
                    new JArray
                    {
                        new JObject
                        {
                            {
                                "message",
                                new JObject
                                {
                                    { "role", "assistant" },
                                    { "content", null },
                                    {
                                        "tool_calls",
                                        new JArray
                                        {
                                            new JObject
                                            {
                                                { "id", "call_codex_app_server" },
                                                { "type", "function" },
                                                {
                                                    "function",
                                                    new JObject
                                                    {
                                                        { "name", call.Name },
                                                        { "arguments", call.ArgumentsJson }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            },
                            { "finish_reason", "tool_calls" }
                        }
                    }
                }
            };
        }

        private static CodexFunctionCallOutput TryParseFunctionCallOutput(string content)
        {
            var obj = TryParseFunctionCallObject(content);
            if (obj == null) return null;

            var name = obj["name"]?.ToString()
                       ?? obj["function"]?.ToString()
                       ?? obj["functionName"]?.ToString();
            if (string.IsNullOrEmpty(name)) return null;

            var arguments = obj["arguments"] ?? obj["argumentsJson"] ?? obj["args"];
            var argumentsJson = NormalizeFunctionArguments(arguments);
            if (argumentsJson == null) return null;

            return new CodexFunctionCallOutput
            {
                Name = name,
                ArgumentsJson = argumentsJson
            };
        }

        private static JObject TryParseFunctionCallObject(string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return null;

            try
            {
                return JObject.Parse(content);
            }
            catch
            {
                var start = content.IndexOf('{');
                var end = content.LastIndexOf('}');
                if (start < 0 || end <= start) return null;

                try
                {
                    return JObject.Parse(content.Substring(start, end - start + 1));
                }
                catch
                {
                    return null;
                }
            }
        }

        private static string NormalizeFunctionArguments(JToken arguments)
        {
            if (arguments == null || arguments.Type == JTokenType.Null)
            {
                return "{}";
            }

            if (arguments.Type == JTokenType.String)
            {
                var text = arguments.ToString();
                if (string.IsNullOrWhiteSpace(text)) return "{}";

                try
                {
                    var parsed = JToken.Parse(text);
                    return parsed is JObject
                        ? parsed.ToString(Formatting.None)
                        : null;
                }
                catch
                {
                    return null;
                }
            }

            return arguments is JObject
                ? arguments.ToString(Formatting.None)
                : null;
        }

        private static RawChatResult FailureChatResult(ModelSpec model, string message)
        {
            return new RawChatResult
            {
                Provider = AIProvider.CodexAppServer,
                ModelId = model.ModelId,
                IsSuccess = false,
                StatusCode = 0,
                ErrorMessage = message,
                ResponseHeaders = new Dictionary<string, string>(),
                RawJson = string.Empty,
                Body = null
            };
        }

        private static RawChatStreamResult FailureChatStreamResult(ModelSpec model, string message)
        {
            return new RawChatStreamResult
            {
                Provider = AIProvider.CodexAppServer,
                ModelId = model.ModelId,
                IsSuccess = false,
                StatusCode = 0,
                ErrorMessage = message,
                ResponseHeaders = new Dictionary<string, string>(),
                RawText = string.Empty,
                Content = string.Empty
            };
        }

        private static RawImageResult FailureImageResult(ModelSpec model, string message, string rawJson)
        {
            return new RawImageResult
            {
                Provider = AIProvider.CodexAppServer,
                ModelId = model.ModelId,
                IsSuccess = false,
                StatusCode = 0,
                ErrorMessage = message,
                ResponseHeaders = new Dictionary<string, string>(),
                RawJson = rawJson ?? string.Empty,
                Images = new List<GeneratedImage>(),
                PromptFeedback = null
            };
        }

        private sealed class CodexImageOutputTarget
        {
            public string ProjectRoot { get; set; }
            public string AbsolutePath { get; set; }
            public string PromptPath { get; set; }
        }

        private sealed class CodexSkillLookupResult
        {
            public bool Checked { get; set; }
            public bool Available { get; set; }
            public string Path { get; set; }
            public string RawEventLog { get; set; }
            public string ErrorMessage { get; set; }

            public static CodexSkillLookupResult Skipped(string errorMessage)
            {
                return new CodexSkillLookupResult
                {
                    Checked = false,
                    Available = false,
                    Path = null,
                    RawEventLog = string.Empty,
                    ErrorMessage = errorMessage
                };
            }
        }

        private sealed class CodexAppServerConnectionException : Exception
        {
            public CodexAppServerConnectionException(string message, Exception innerException)
                : base(message, innerException)
            {
            }
        }

        private sealed class FileSnapshot
        {
            public static readonly FileSnapshot Missing = new FileSnapshot();

            public bool Exists { get; set; }
            public long Length { get; set; }
            public DateTime LastWriteUtc { get; set; }
        }

        private sealed class CodexTurnState
        {
            private readonly StringBuilder content = new StringBuilder();
            private readonly StringBuilder rawEvents = new StringBuilder();
            private readonly Dictionary<string, List<string>> fileChangePathsByItemId =
                new Dictionary<string, List<string>>(StringComparer.Ordinal);
            private readonly Action<string> onContentDelta;
            private string completedAgentMessage;

            public CodexTurnState(Action<string> onContentDelta, string allowedFileChangePath, string fileChangeRoot)
            {
                this.onContentDelta = onContentDelta;
                FileChangeRoot = NormalizeComparablePath(fileChangeRoot, null);
                AllowedFileChangePath = NormalizeComparablePath(allowedFileChangePath, FileChangeRoot);
            }

            public string ThreadId { get; set; }
            public string TurnId { get; set; }
            public bool IsTurnCompleted { get; set; }
            public bool TurnSucceeded { get; set; }
            public string ErrorMessage { get; set; }
            public bool AllowFileChangeApprovals => !string.IsNullOrEmpty(AllowedFileChangePath);
            public string AllowedFileChangePath { get; }
            public string FileChangeRoot { get; }
            public string RawEventLog => rawEvents.ToString();

            public void AppendAgentDelta(string delta)
            {
                if (string.IsNullOrEmpty(delta)) return;
                content.Append(delta);
                onContentDelta?.Invoke(delta);
            }

            public void SetCompletedAgentMessage(string text)
            {
                if (!string.IsNullOrEmpty(text))
                {
                    completedAgentMessage = text;
                }
            }

            public void RecordRaw(string json)
            {
                if (string.IsNullOrEmpty(json)) return;
                rawEvents.AppendLine(json);
            }

            public void RecordClientNote(string note)
            {
                if (string.IsNullOrEmpty(note)) return;
                rawEvents.AppendLine("[client] " + note);
            }

            public void RecordFileChangeItem(JObject item)
            {
                var itemId = item?["id"]?.ToString();
                if (string.IsNullOrWhiteSpace(itemId)) return;

                var paths = ExtractPathLikeValues(item["changes"])
                    .Select(path => NormalizeComparablePath(path, FileChangeRoot))
                    .Where(path => !string.IsNullOrEmpty(path))
                    .Distinct(GetPathStringComparer())
                    .ToList();
                if (paths.Count == 0) return;

                fileChangePathsByItemId[itemId] = paths;
                RecordClientNote("Observed file change item " + itemId + ": " + string.Join(", ", paths));
            }

            public List<string> GetFileChangePaths(string itemId)
            {
                if (string.IsNullOrWhiteSpace(itemId)) return new List<string>();
                return fileChangePathsByItemId.TryGetValue(itemId, out var paths)
                    ? new List<string>(paths)
                    : new List<string>();
            }

            public CodexTurnResult ToResult(bool success, string errorMessage)
            {
                return new CodexTurnResult
                {
                    IsSuccess = success,
                    StatusCode = success ? 200 : 0,
                    ErrorMessage = success ? null : errorMessage,
                    RawEventLog = rawEvents.ToString(),
                    Content = completedAgentMessage ?? content.ToString()
                };
            }
        }

        private sealed class CodexTurnResult
        {
            public bool IsSuccess { get; set; }
            public long StatusCode { get; set; }
            public string ErrorMessage { get; set; }
            public string RawEventLog { get; set; }
            public string Content { get; set; }

            public static CodexTurnResult Failure(string message)
            {
                return new CodexTurnResult
                {
                    IsSuccess = false,
                    StatusCode = 0,
                    ErrorMessage = message,
                    RawEventLog = string.Empty,
                    Content = string.Empty
                };
            }
        }

        private sealed class CodexFunctionCallOutput
        {
            public string Name { get; set; }
            public string ArgumentsJson { get; set; }
        }
    }
}
