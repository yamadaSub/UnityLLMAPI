# UnityLLMAPI Sample API Reference

This file is a compact reference for the sample package under `Samples~/Example`.
It focuses on the APIs that are used most often from Unity scripts and includes
small code examples that match the current package surface.
Most sample MonoBehaviours in this package are intended to be run from the
Inspector ContextMenu.

## Included Sample Scripts

- `ExampleUsage.cs`
  - Basic chat
  - Structured output with JSON Schema
  - Real-time schema updates
  - Function calling
- `VisionSamples.cs`
  - Vision prompts with images
  - Gemini image editing / generation
- `EmbeddingSample.cs`
  - word2vec-style nearest-neighbor search
  - Multimodal embeddings with Gemini Embedding 2
  - AudioClip / VideoClip query examples that pick the nearest word from `CorpusWords`
  - Cosine similarity comparison against the same corpus

## Setup

Set your API keys before running the samples:

- OpenAI: `OPENAI_API_KEY`
- Grok: `GROK_API_KEY`
- Anthropic: `ANTHROPIC_API_KEY`
- Gemini: `GOOGLE_API_KEY`

In the Unity Editor you can also use:

- `Tools > UnityLLMAPI > Configure API Keys`

## Chat Basics

Use `AIManager.SendMessageAsync(...)` for a standard text response.

```csharp
using System.Collections.Generic;
using UnityEngine;
using UnityLLMAPI.Chat;

var messages = new List<Message>
{
    new Message { role = MessageRole.System, content = "You are a helpful Unity assistant." },
    new Message { role = MessageRole.User, content = "Explain what ScriptableObject is used for." }
};

var reply = await AIManager.SendMessageAsync(messages, AIModelType.Gemini25Flash);
Debug.Log(reply);
```

Useful chat-oriented models:

- `AIModelType.Gemini25Flash`
- `AIModelType.Gemini31`
- `AIModelType.ClaudeSonnet46`
- `AIModelType.ClaudeOpus46`
- `AIModelType.GPT4o`
- `AIModelType.GPT5`
- `AIModelType.GPT5_4`
- `AIModelType.GPT5_5`
- `AIModelType.Grok4_1`
- `AIModelType.Grok4_2`
- `AIModelType.Grok4_3`

## Streaming Chat

Use `AIManager.SendMessageStreamAsync(...)` when you want token-by-token updates.

```csharp
using System.Collections.Generic;
using UnityEngine;
using UnityLLMAPI.Chat;

var messages = new List<Message>
{
    new Message { role = MessageRole.User, content = "Write a short enemy AI state machine." }
};

var stream = await AIManager.SendMessageStreamAsync(
    messages,
    AIModelType.Gemini25Flash,
    onContentDelta: delta => Debug.Log(delta));

Debug.Log(stream?.Content);
```

## Vision Input

Use `Message.parts` when you need text and images in the same prompt.

```csharp
using System.Collections.Generic;
using UnityLLMAPI.Chat;

var messages = new List<Message>
{
    new Message
    {
        role = MessageRole.User,
        parts = new List<MessageContent>
        {
            MessageContent.FromText("Describe everything visible in this screenshot."),
            MessageContent.FromImage(texture)
        }
    }
};

var reply = await AIManager.SendMessageAsync(messages, AIModelType.ClaudeSonnet46);
```

Image helpers:

- `MessageContent.FromImage(Texture texture, ...)`
- `MessageContent.FromImageData(byte[] data, string mime)`
- `MessageContent.FromImageUrl(string url, string mime = null)`

Vision-capable chat models include `ClaudeSonnet46`, `ClaudeOpus46`, `GPT4o`,
`Gemini25Flash`, and `Gemini31`.

## Structured Output

Use `SendStructuredMessageAsync<T>(...)` to deserialize a schema-guided response into a C# type.

```csharp
using System;
using System.Collections.Generic;
using UnityLLMAPI.Chat;
using UnityLLMAPI.Schema;

[Serializable]
public class EnemyConfig
{
    public string name;
    public float speed;
    public int hp;
}

var messages = new List<Message>
{
    new Message { role = MessageRole.User, content = "Create a simple enemy config for a slime." }
};

var result = await AIManager.SendStructuredMessageAsync<EnemyConfig>(
    messages,
    AIModelType.Gemini25Flash);

Debug.Log(result?.name);
```

When the candidate list is only known at runtime, keep using the same
`SendStructuredMessageAsync(...)` pipeline and pass a target instance that carries
the dynamic enum subset:

```csharp
using System;
using System.Collections.Generic;
using UnityLLMAPI.Chat;
using UnityLLMAPI.Schema;

public enum EnemyAction
{
    Attack,
    Guard,
    Heal,
    Flee
}

[Serializable]
public class ActionChoice
{
    [SchemaIgnore]
    public EnemyAction[] availableActions;

    [DynamicAllowedValues(nameof(availableActions))]
    public EnemyAction selectedAction;
}

var choice = new ActionChoice
{
    availableActions = new[] { EnemyAction.Attack, EnemyAction.Heal, EnemyAction.Flee }
};

var messages = new List<Message>
{
    new Message
    {
        role = MessageRole.User,
        content = "Pick exactly one action for a badly injured enemy."
    }
};

await AIManager.SendStructuredMessageAsync(
    choice,
    messages,
    AIModelType.Gemini25Flash);

Debug.Log(choice.selectedAction);
```

Notes:

- `SchemaIgnore` keeps helper members like `availableActions` out of the generated schema.
- `DynamicAllowedValues` converts the referenced runtime values into a JSON Schema `enum`.
- Regular enum fields and `List<Enum>` fields are also emitted as string enums now.
- For enum targets, the runtime values must match the enum member names that `JsonConvert` will deserialize.

## Function Calling

Use `SendFunctionCallMessageAsync(...)` with `FunctionSchema<T>` implementations.

```csharp
using System.Collections.Generic;
using UnityLLMAPI.Chat;
using UnityLLMAPI.Schema;

var messages = new List<Message>
{
    new Message { role = MessageRole.User, content = "Add 3.5 and 2.5." }
};

var functions = new List<IJsonSchema>
{
    new ExampleUsage.AddNumbersFunction()
};

var result = await AIManager.SendFunctionCallMessageAsync(
    messages,
    functions,
    AIModelType.Gemini25Flash);
```

## Image Generation / Editing

Use `GenerateImagesAsync(...)` or `GenerateImageAsync(...)` for Gemini image models.

```csharp
using System.Collections.Generic;
using UnityLLMAPI.Chat;

var prompts = new List<Message>
{
    new Message
    {
        role = MessageRole.User,
        parts = new List<MessageContent>
        {
            MessageContent.FromText("Turn this into a watercolor painting."),
            MessageContent.FromImage(texture)
        }
    }
};

var initBody = new Dictionary<string, object>
{
    {
        "generationConfig",
        new Dictionary<string, object>
        {
            { "responseModalities", new[] { "IMAGE" } }
        }
    }
};

var response = await AIManager.GenerateImagesAsync(
    prompts,
    AIModelType.Gemini31FlashImage,
    initBody);
```

Image-capable generation models:

- `AIModelType.Gemini25FlashImage`
- `AIModelType.Gemini31FlashImage`
- `AIModelType.Gemini3ProImage`

## Text Embeddings

Use `EmbeddingManager.CreateEmbeddingAsync(...)` for a single text input and
`CreateEmbeddingsAsync(...)` for batches.

```csharp
using System.Collections.Generic;
using UnityLLMAPI.Embedding;

var queryEmbedding = await EmbeddingManager.CreateEmbeddingAsync(
    "Unity loves C#",
    EmbeddingModelType.GeminiEmbedding2,
    outputDimensionality: 1536);

var corpusTexts = new List<string> { "Unity", "Unreal", "C#", "Shader Graph" };
var corpus = await EmbeddingManager.CreateEmbeddingsAsync(
    corpusTexts,
    EmbeddingModelType.GeminiEmbedding2,
    outputDimensionality: 1536);

var ranked = EmbeddingManager.RankByCosine(queryEmbedding, corpus);
```

Embedding models:

- `EmbeddingModelType.GeminiEmbedding2`
- `EmbeddingModelType.Gemini01`
- `EmbeddingModelType.Gemini01_1536`
- `EmbeddingModelType.Gemini01_768`
- `EmbeddingModelType.OpenAISmall`
- `EmbeddingModelType.OpenAILarge`

## Multimodal Embeddings

`GeminiEmbedding2` supports multimodal inputs through `EmbeddingInput` and `EmbeddingPart`.

```csharp
using UnityLLMAPI.Embedding;

var input = EmbeddingInput.FromParts(
    EmbeddingPart.FromText("A red bird sitting on a branch."),
    EmbeddingPart.FromImage(texture));

var embedding = await EmbeddingManager.CreateEmbeddingAsync(
    input,
    EmbeddingModelType.GeminiEmbedding2,
    outputDimensionality: 1536);
```

Available embedding part helpers:

- `EmbeddingPart.FromText(string value)`
- `EmbeddingPart.FromImage(Texture texture, ...)`
- `EmbeddingPart.FromAudioData(byte[] bytes, string mime = "audio/mpeg")`
- `EmbeddingPart.FromAudioFileUri(string value, string mime = null)`
- `EmbeddingPart.FromAudioClip(AudioClip clip, ...)`
- `EmbeddingPart.FromVideoData(byte[] bytes, string mime = "video/mp4")`
- `EmbeddingPart.FromVideoFileUri(string value, string mime = null)`
- `EmbeddingPart.FromVideoClip(VideoClip clip, ...)`
- `EmbeddingPart.FromFileUri(string value, string mime = null)`
- `EmbeddingPart.FromInlineData(byte[] bytes, string mime = "application/octet-stream")`

Direct Unity assets can also be used:

```csharp
using UnityLLMAPI.Embedding;
using UnityEngine;
using UnityEngine.Video;

var audioEmbedding = await EmbeddingManager.CreateEmbeddingAsync(
    EmbeddingInput.FromParts(
        EmbeddingPart.FromAudioClip(audioClip)),
    EmbeddingModelType.GeminiEmbedding2);
```

`AudioClip` is encoded to WAV before upload and requires the clip importer Load Type to be `Decompress On Load`.
`VideoClip` direct embedding depends on access to the source file and is intended for Editor workflows. Use `FromVideoData` or `FromFileUri` in player builds.
`EmbeddingSample.cs` also exposes `multimodalText`, which is included together with the image, audio, or video part when those queries run. Leave it empty if you want to validate media-only retrieval.
The sample builds embeddings for `CorpusWords`, embeds the assigned input, and logs the nearest match from that corpus.

In `EmbeddingSample.cs`, assign `multimodalText`, `multimodalImageTexture`, `multimodalAudioClip`, or `multimodalVideoClip`,
then run `Run Multimodal Query / Image`, `Run Multimodal Query / Audio`, or
`Run Multimodal Query / Video` from the Inspector ContextMenu.

### Resource Handling Notes

- Images: `FromImage(Texture)` is convenient in Unity. For repeated requests, pre-encode to PNG bytes or keep your own cached bytes if you want to avoid repeated GPU readback / PNG encoding cost.
- Audio: if you already have file bytes, prefer `FromAudioData(...)` or `FromFileUri(...)`. `FromAudioClip(...)` is a Unity convenience helper and only works when sample data is readable via `AudioClip.GetData`, which typically means `Load Type = Decompress On Load`.
- Video: for packaged/runtime content, prefer `FromVideoData(...)` or `FromFileUri(...)`. `FromVideoClip(...)` is mainly for Editor workflows where Unity still has access to the imported source file.
- Large media: this sample uses inline data for simplicity, but production code should avoid repeatedly embedding very large assets from scene references. Keep a stable raw-byte source such as `StreamingAssets`, downloaded files, Addressables payloads, or your own asset pipeline.

## Main Types

### Chat

- `Message`
- `MessageContent`
- `AIManager`
- `AIModelType`

### Schema / Function Calling

- `IJsonSchema`
- `FunctionSchema<T>`
- `RealTimeJsonSchema<T>`

### Embeddings

- `EmbeddingManager`
- `EmbeddingModelType`
- `EmbeddingInput`
- `EmbeddingPart`

## Notes

- `Message.content` is enough for text-only prompts.
- `Message.parts` is the preferred path for multimodal prompts.
- `GeminiEmbedding2` is the default embedding model in the current package.
- `Gemini01*` models remain available for text-only compatibility scenarios.
- `Texture2D` and `RenderTexture` inputs can be converted through the built-in texture helpers.
