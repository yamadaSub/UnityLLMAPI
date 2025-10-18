using UnityEngine;

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

    public string OpenAIApiKey => openAIApiKey;
    public string GrokApiKey => grokApiKey;
    public string GoogleApiKey => googleApiKey;

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
