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
        GPT5_5 = 1,
        GPT5_6Sol = 2,
        GPT5_6Terra = 3,
        GPT5_6Luna = 4
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
                case CodexAppServerModelType.GPT5_6Sol:
                    return "gpt-5.6-sol";
                case CodexAppServerModelType.GPT5_6Terra:
                    return "gpt-5.6-terra";
                case CodexAppServerModelType.GPT5_6Luna:
                    return "gpt-5.6-luna";
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
