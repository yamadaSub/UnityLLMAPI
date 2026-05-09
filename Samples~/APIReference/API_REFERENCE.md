# UnityLLMAPI API Reference

This file is a compact reference for UnityLLMAPI's public API surface.
It focuses on the APIs that are used most often from Unity scripts and includes
small code examples that are independent from the executable MonoBehaviour
examples.

## Setup

Set provider credentials before running API requests:

- OpenAI: `OPENAI_API_KEY`
- Grok: `GROK_API_KEY`
- Anthropic: `ANTHROPIC_API_KEY`
- Gemini: `GOOGLE_API_KEY`

In the Unity Editor you can also use:

- `Tools > UnityLLMAPI > Configure API Keys`

## Endpoint And Model Metadata

Use `AIManager.GetModelSpec(...)` to inspect the static registry entry for an
`AIModelType`. Use `AIManager.GetResolvedModelSpec(...)` when you need the
effective provider after endpoint overrides are applied.

```csharp
using UnityLLMAPI.Chat;

var staticSpec = AIManager.GetModelSpec(AIModelType.GPT5_5);
var effectiveSpec = AIManager.GetResolvedModelSpec(AIModelType.GPT5_5);

if (effectiveSpec.Capabilities.HasFlag(AICapabilities.Vision))
{
    // This model route accepts image input.
}
```

OpenAI/GPT routes always use their normal provider endpoints. Codex App Server
is used only by explicit App Server model routes such as
`AIModelType.GPT5_5AppServer`.

```csharp
AIManager.SetCodexAppServerBaseUrl("ws://127.0.0.1:4500");
```

`AIManager.UseCodexAppServer(...)` is kept only as a compatibility alias for
setting the Codex App Server URL. It no longer reroutes normal OpenAI models.

## Codex App Server Mode

`AIModelType.GPT5_5AppServer` routes GPT-5.5 requests through Codex App Server.
OpenAI/GPT model requests are not automatically routed through Codex App Server.
Use `AIModelType.GPT5_5AppServer` when App Server behavior is intended.

Configure one of:

- Editor: `Tools > UnityLLMAPI > Codex App Server`, and set/start the
  WebSocket endpoint such as `ws://127.0.0.1:4500`. Unity-managed Codex CLI
  processes run with a project-scoped `CODEX_HOME`, so app-server sessions do
  not share the Codex Desktop history directory.
- Runtime component: add `AIManagerBehaviour` and set the Codex App Server URL.
- Code:

```csharp
AIManager.SetCodexAppServerBaseUrl("ws://127.0.0.1:4500");
```

Codex App Server mode expects a WebSocket JSON-RPC endpoint. The client creates
a thread, starts a turn, reads `item/agentMessage/delta` and `item/completed`,
and completes on `turn/completed`. Structured output uses Codex App Server
`outputSchema`.
AIManager compatibility calls are single-shot: they do not persist or reuse the
returned App Server `threadId`. The client therefore starts threads as
`ephemeral: true` by default and sets `serviceName` to `unityllmapi`. To keep a
thread on disk for debugging, pass `initBody["thread"]["ephemeral"] = false`
explicitly.
The actual Codex model defaults to the app-server configuration; pass
`CodexAppServerModelType` through `CodexAppServerModelOptions.ApplyTo(...)`
only when you intentionally want a Codex-supported model override. This enum is
separate from `AIModelType`; `AIModelType.GPT5_5AppServer` remains the LLMAPI
compatible route model used for capability checks and provider routing.
Multimodal inputs are supported through `Message.parts`; `MessageContent.FromImage(...)`
and `FromImageUrl(...)` are converted to Codex App Server image input items.
Function Calling is supported through structured output compatibility: the
client asks Codex App Server for a function name and JSON argument string, then
maps that back to the existing `IJsonSchema` result. Unity logs an informational
message when this compatibility path is used. Image generation is
supported by invoking Codex's `$imagegen` skill and reading back the PNG file
that Codex saves under `Assets/...`. Embeddings are not supported by this mode.

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
- `AIModelType.GPT5_5AppServer`
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

## Coroutine Requests

Use `AIRequest` when a Unity coroutine should start an LLM request, do other
work for a few frames, then only wait for the answer when it is actually needed.
`AIRequest<T>` is a `CustomYieldInstruction`, so it can be yielded directly.

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityLLMAPI.Chat;

IEnumerator RunEnemyPlan()
{
    var messages = new List<Message>
    {
        new Message { role = MessageRole.User, content = "Create a small enemy patrol plan." }
    };

    var request = AIRequest.SendStructured<EnemyPlan>(
        messages,
        AIModelType.Gemini25Flash,
        timeoutSeconds: 60);

    yield return PlayIntroAnimation();

    if (!request.TryGetResult(out var plan))
    {
        yield return request;
    }

    if (request.TryGetResult(out plan))
    {
        ApplyPlan(plan);
    }
    else
    {
        Debug.LogWarning(request.ErrorMessage);
    }

    request.Dispose();
}
```

Common factories:

- `AIRequest.SendMessage(...)`
- `AIRequest.SendMessageStream(...)`
- `AIRequest.SendStructured<T>(...)` / `AIRequest.Structured<T>(...)`
- `AIRequest.PopulateStructured(targetInstance, ...)`
- `AIRequest.SendStructuredWithRealTimeSchema(...)`
- `AIRequest.SendStructuredWithSchema(...)`
- `AIRequest.SendFunctionCall(...)`
- `AIRequest.GenerateImages(...)` / `AIRequest.GenerateImage(...)`

Useful request members:

- `yield return request`: wait until done; if already done, it resumes without
  waiting for the network call.
- `TryGetResult(out value)`: returns `true` only after a successful non-null
  result.
- `Status`, `IsDone`, `IsPending`, `IsCanceled`, `IsFaulted`, `ErrorMessage`:
  inspect progress and failure state.
- `Cancel()` / `Dispose()`: cancel a pending request. Stopping a coroutine alone
  does not cancel the underlying provider call.

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
`GPT5_5AppServer`, `Gemini25Flash`, and `Gemini31`.

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
    new AddNumbersFunction()
};

var result = await AIManager.SendFunctionCallMessageAsync(
    messages,
    functions,
    AIModelType.Gemini25Flash);
```

Example function schema:

```csharp
using System.Collections.Generic;
using UnityLLMAPI.Chat;
using UnityLLMAPI.Schema;

public class AddNumbersFunction : IJsonSchema
{
    public string Name => "add_numbers";

    public Dictionary<string, object> GenerateJsonSchema()
    {
        return new Dictionary<string, object>
        {
            { "type", "function" },
            { "name", Name },
            { "description", "Add two numbers." },
            {
                "parameters",
                new Dictionary<string, object>
                {
                    { "type", "object" },
                    {
                        "properties",
                        new Dictionary<string, object>
                        {
                            { "a", new Dictionary<string, object> { { "type", "number" } } },
                            { "b", new Dictionary<string, object> { { "type", "number" } } }
                        }
                    },
                    { "required", new[] { "a", "b" } }
                }
            }
        };
    }
}
```

## Image Generation / Editing

Use `GenerateImagesAsync(...)` or `GenerateImageAsync(...)` for Gemini image models.
When using Codex App Server image generation, call these methods with
`AIModelType.GPT5_5AppServer`; the client invokes `$imagegen`. Pass
`initBody["outputPath"]` as a Unity project relative path such as
`Assets/Generated/coin_icon.png`. Use `CodexAppServerModelType` only for the
optional Codex turn model override.

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

Codex App Server image generation uses the same `GenerateImagesAsync(...)`
entry point:

```csharp
var codexBody = new Dictionary<string, object>
{
    { "outputPath", "Assets/Generated/coin_icon.png" }
};
CodexAppServerModelOptions.ApplyTo(codexBody, CodexAppServerModelType.AppServerDefault);

var codexImages = await AIManager.GenerateImagesAsync(
    prompts,
    AIModelType.GPT5_5AppServer,
    codexBody);
```

Codex App Server image generation also uses an ephemeral thread by default.
Keep `outputPath` at the top level of `initBody`; thread options belong under
`initBody["thread"]`.

Image-capable generation models:

- `AIModelType.Gemini25FlashImage`
- `AIModelType.Gemini31FlashImage`
- `AIModelType.Gemini3ProImage`
- `AIModelType.GPT5_5AppServer` when a Codex App Server URL is configured

`ImageGenerationResponse.images` contains `GeneratedImage` values. Each image
stores `mimeType` and raw `data`; use `ToBase64()` or `ToDataUrl()` when you
need a text representation.

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

## Embedding Vector Utilities

`SerializableEmbedding` stores model metadata and vector values. It can be used
directly for similarity search and vector arithmetic.

```csharp
using System.Collections.Generic;
using UnityLLMAPI.Embedding;

var score = queryEmbedding.CosineSimilarity(corpus[0]);
var normalized = queryEmbedding.Normalized();
var centroid = SerializableEmbedding.Average(corpus, normalize: true);
```

Useful members:

- `Model`
- `Dimension`
- `Magnitude`
- `ToFloatArray()`
- `SetFromFloatArray(...)`
- `Clone()`
- `Dot(...)`
- `CosineSimilarity(...)`
- `Normalized()` / `NormalizeInPlace()`
- `Add(...)`, `Sub(...)`, `Scale(...)`
- `Average(...)`
- `WeightedSum(...)`

### Resource Handling Notes

- Images: `FromImage(Texture)` is convenient in Unity. For repeated requests, pre-encode to PNG bytes or keep your own cached bytes if you want to avoid repeated GPU readback / PNG encoding cost.
- Audio: if you already have file bytes, prefer `FromAudioData(...)` or `FromFileUri(...)`. `FromAudioClip(...)` is a Unity convenience helper and only works when PCM data is readable via `AudioClip.GetData`, which typically means `Load Type = Decompress On Load`.
- Video: for packaged/runtime content, prefer `FromVideoData(...)` or `FromFileUri(...)`. `FromVideoClip(...)` is mainly for Editor workflows where Unity still has access to the imported source file.
- Large media: inline data is convenient for small assets, but production code should avoid repeatedly embedding very large assets from scene references. Keep a stable raw-byte source such as `StreamingAssets`, downloaded files, Addressables payloads, or your own asset pipeline.

## Main Types

### Chat

- `Message`
- `MessageContent`
- `AIManager`
- `AIModelType`
- `ModelSpec`
- `AICapabilities`
- `AIProvider`
- `CodexAppServerModelType`
- `CodexAppServerModelOptions`

### Schema / Function Calling

- `IJsonSchema`
- `FunctionSchema<T>`
- `RealTimeJsonSchema<T>`

### Images

- `ImageGenerationResponse`
- `GeneratedImage`
- `ImageGenerationRequest`

### Provider-Level Results

- `RawChatResult`
- `RawChatStreamResult`
- `RawImageResult`
- `RawEmbeddingResult`
- `ChatRequestOptions`

### Embeddings

- `EmbeddingManager`
- `EmbeddingModelType`
- `EmbeddingInput`
- `EmbeddingPart`
- `SerializableEmbedding`

## Notes

- `Message.content` is enough for text-only prompts.
- `Message.parts` is the preferred path for multimodal prompts.
- `GeminiEmbedding2` is the default embedding model in the current package.
- `Gemini01*` models remain available for text-only compatibility scenarios.
- `Texture2D` and `RenderTexture` inputs can be converted through the built-in texture helpers.
