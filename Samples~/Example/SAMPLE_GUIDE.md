# UnityLLMAPI Example Sample Guide

This guide describes the executable MonoBehaviour samples under
`Samples~/Example`. For API-level details, import the separate `API Reference`
sample and open `Samples~/APIReference/API_REFERENCE.md`.

## Running The Samples

1. Import the `Example Usage` sample from Package Manager.
2. Add the target sample MonoBehaviour to a GameObject in a scene.
3. Assign any required Texture, AudioClip, VideoClip, or prompt fields.
4. Run the sample from the Inspector ContextMenu.

Set API keys before running network-backed samples:

- OpenAI: `OPENAI_API_KEY`
- Grok: `GROK_API_KEY`
- Anthropic: `ANTHROPIC_API_KEY`
- Gemini: `GOOGLE_API_KEY`

In the Unity Editor you can also use:

- `Tools > UnityLLMAPI > Configure API Keys`

## Included Scripts

- `ExampleUsage.cs`
  - Basic chat
  - Streaming chat
  - Structured output with JSON Schema
  - Real-time schema updates
  - Function calling
- `VisionSamples.cs`
  - Vision prompts with images
  - Gemini image editing / generation
- `EmbeddingSample.cs`
  - Word2Vec-style nearest-neighbor search
  - Multimodal embeddings with Gemini Embedding 2
  - AudioClip / VideoClip query examples that pick the nearest word from `CorpusWords`
  - Cosine similarity comparison against the same corpus
- `CodexAppServerSample.cs`
  - Codex App Server text chat
  - Codex App Server multimodal image input
- `CodexAppServerImageGenSample.cs`
  - Codex App Server `$imagegen` asset generation
  - Optional placement of the generated PNG as a child SpriteRenderer

## Codex App Server Samples

Start `codex app-server` with a WebSocket listener, then configure the URL once
through Editor settings, `AIManagerBehaviour`, or `CODEX_APP_SERVER_BASE_URL`.
The Codex samples use the shared URL resolver and do not expose a per-sample
`serverUrl` field.

Run:

- `CodexAppServerSample` -> `Run Codex App Server Chat`
- `CodexAppServerSample` -> `Describe Image With Codex App Server`
- `CodexAppServerImageGenSample` -> `Generate Image Asset`

`CodexAppServerImageGenSample` writes the generated file to
`relativeOutputPath`, defaulting to `Assets/Generated/codex_coin_icon.png`.
`placeInScene` is disabled by default; enable it only when you also want a
`Codex Generated Image` child GameObject with a SpriteRenderer.

## Embedding Sample Notes

`EmbeddingSample.cs` exposes `multimodalText`, which is included together with
the image, audio, or video part when those queries run. Leave it empty if you
want to validate media-only retrieval.

The sample builds embeddings for `CorpusWords`, embeds the assigned input, and
logs the nearest match from that corpus.

For multimodal embedding runs:

- Assign `multimodalImageTexture`, then run `Run Multimodal Query / Image`.
- Assign `multimodalAudioClip`, then run `Run Multimodal Query / Audio`.
- Assign `multimodalVideoClip`, then run `Run Multimodal Query / Video`.

`AudioClip` samples require readable sample data. In practice, set the clip's
importer Load Type to `Decompress On Load`.

`VideoClip` direct embedding depends on access to the source file and is mainly
intended for Editor workflows. Use raw file bytes or file URIs in player builds.

## Vision Sample Notes

`VisionSamples.cs` saves generated images to the configured output path. By
default the sample writes under `Assets`, so call `AssetDatabase.Refresh()` in
Editor workflows when you need Unity to import the new PNG immediately.
