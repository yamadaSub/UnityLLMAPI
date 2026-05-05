using System;
using System.Collections.Generic;

namespace UnityLLMAPI.Chat
{
    /// <summary>
    /// Optional model override for Codex App Server turns.
    /// This is intentionally separate from AIModelType, which is used by LLMAPI
    /// for provider routing and capability checks.
    /// </summary>
    public enum CodexAppServerModelType
    {
        AppServerDefault = 0,
        GPT5_5 = 1
    }

    public static class CodexAppServerModelOptions
    {
        public static string ToModelId(CodexAppServerModelType model)
        {
            switch (model)
            {
                case CodexAppServerModelType.AppServerDefault:
                    return null;
                case CodexAppServerModelType.GPT5_5:
                    return "gpt-5.5";
                default:
                    throw new ArgumentOutOfRangeException(nameof(model), model, null);
            }
        }

        public static string ToDisplayName(CodexAppServerModelType model)
            => ToModelId(model) ?? "app-server default";

        public static void ApplyTo(Dictionary<string, object> initBody, CodexAppServerModelType model)
        {
            if (initBody == null) throw new ArgumentNullException(nameof(initBody));

            var modelId = ToModelId(model);
            if (string.IsNullOrWhiteSpace(modelId))
            {
                initBody.Remove("model");
                return;
            }

            initBody["model"] = modelId;
        }
    }
}
