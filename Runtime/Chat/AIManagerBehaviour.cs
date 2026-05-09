using UnityEngine;

namespace UnityLLMAPI.Chat
{
    /// <summary>
    /// Optional component to explicitly provide API keys at runtime.
    /// </summary>
    [DisallowMultipleComponent]
    public class AIManagerBehaviour : MonoBehaviour
    {
        [Header("API Keys")]
        [Tooltip("OpenAI API key used for GPT models.")]
        [SerializeField] private string openAIApiKey;

        [Tooltip("Grok/X.AI API key used for Grok models.")]
        [SerializeField] private string grokApiKey;

        [Tooltip("Google API key used for Gemini models.")]
        [SerializeField] private string googleApiKey;

        [Tooltip("Anthropic API key used for Claude models.")]
        [SerializeField] private string anthropicApiKey;

        [Header("Codex App Server")]
        [Tooltip("Codex App Server WebSocket URL, e.g. ws://127.0.0.1:4500. Used only by explicit Codex App Server models such as GPT5_5AppServer.")]
        [SerializeField] private string codexAppServerBaseUrl;

        public string OpenAIApiKey => openAIApiKey;
        public string GrokApiKey => grokApiKey;
        public string GoogleApiKey => googleApiKey;
        public string AnthropicApiKey => anthropicApiKey;
        public bool OverrideOpenAIEndpointSettings => false;
        public OpenAIEndpointMode OpenAIEndpointMode => OpenAIEndpointMode.OpenAI;
        public string CodexAppServerBaseUrl => codexAppServerBaseUrl;

        private void OnEnable()
        {
            AIManager.RegisterBehaviour(this);
        }

        private void OnDisable()
        {
            AIManager.UnregisterBehaviour(this);
        }

        private void OnDestroy()
        {
            AIManager.UnregisterBehaviour(this);
        }
    }
}

