UnityLLMAPI
===========

Unity から複数の LLM / Embedding API を共通の API で扱うためのラッパーライブラリです。OpenAI / Grok / Gemini をまとめて、チャット、JSON Schema ベースの構造化応答、Function Calling、画像生成、Embedding を Unity のコードだけで呼び出せます。

## 1. 概要
- Unity スクリプトから LLM (テキスト / ビジョン) と Embedding を安全に叩くための補助パッケージです。
- 対応プロバイダと主なモデル (`AIModelType`):
  - OpenAI: `GPT4o`, `GPT5`, `GPT5_2`, `GPT5_4`, `GPT5Mini`
  - Grok (x.ai): `Grok2`, `Grok3`, `Grok4_1`, `Grok4_1Reasoning`
  - Anthropic: `ClaudeSonnet46`, `ClaudeOpus46`
  - Gemini: `Gemini25`, `Gemini25Pro`, `Gemini25Flash`, `Gemini25FlashLite`, `Gemini25FlashImage`（旧 `Gemini25FlashImagePreview`）、`Gemini31`, `Gemini3ProImage`, `Gemini31FlashImage`（Vision / 画像生成に対応）
- Embedding は OpenAI (text-embedding-3-small / -large)、Gemini Embedding 001 系、Gemini Embedding 2（マルチモーダル入力対応）をサポートします。

## 2. セットアップ

### パッケージの配置
- このリポジトリ一式を Unity プロジェクトに配置するだけで利用できます（asmdef 同梱）。

### API キーの取得と設定
- 利用するプロバイダごとに以下の環境変数を設定してください（いずれも Process / User / Machine の順で参照されます）。
  - OpenAI: `OPENAI_API_KEY`
  - Grok (x.ai): `GROK_API_KEY`
  - Anthropic (Claude): `ANTHROPIC_API_KEY`
  - Google (Gemini): `GOOGLE_API_KEY`
- 設定例
  - Windows (PowerShell): `Set-Item -Path Env:OPENAI_API_KEY -Value "<your_key>"`
  - macOS / Linux (bash / zsh): `export OPENAI_API_KEY=<your_key>`
- Unity Editor から設定する場合
  - メニュー `Tools > UnityLLMAPI > Configure API Keys` でプロジェクトスコープの EditorUserSettings に保存できます（`UnityLLMAPI.OPENAI_API_KEY` / `UnityLLMAPI.GROK_API_KEY` / `UnityLLMAPI.GOOGLE_API_KEY`）。Assets には保存されないため VCS にそのまま含められます。
- ランタイムでのキー解決順
  1. シーン上の `AIManagerBehaviour` コンポーネントに設定された値
  2. （Editor のみ）EditorUserSettings の `UnityLLMAPI.*` 値
  3. 環境変数（Process -> User -> Machine）

## 3. 全体の利用フロー
- メッセージを組み立てる：`Message`（`role` と `content`）と、必要に応じて `Message.parts` に `MessageContent`（テキスト / 画像）を設定。
- API を呼ぶ：`AIManager`（チャット / 構造化応答 / Function Calling / 画像生成）や `EmbeddingManager`（埋め込み生成）を使用。
- レスポンスを受け取る：通常チャットは `string`、構造化応答は型や `Dictionary<string, object>`、Function Calling は `IJsonSchema`、画像生成は `ImageGenerationResponse` で受領。
- ユーティリティ活用：`TextureEncodingUtility.TryGetPngBytes` で non-readable テクスチャも安全に PNG 化、`UnityWebRequestUtils.SendAsync` で `UnityWebRequest` を await 可能にするなど。

## 4. 基本のテキスト / マルチモーダルチャット
```csharp
using System.Collections.Generic;
using UnityEngine;
using UnityLLMAPI.Chat;

// System / User ロールを組み立てて送信
var messages = new List<Message>
{
    new Message { role = MessageRole.System, content = "あなたは親切な Unity アシスタントです。" },
    new Message { role = MessageRole.User,   content = "RuntimeInitializeOnLoadMethod の使い方を教えて。" }
};

var reply = await AIManager.SendMessageAsync(messages, AIModelType.Gemini25Flash);
Debug.Log(reply);
```

画像を含む Vision チャット（`Message.parts` に画像パートを追加）:
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
            MessageContent.FromText("この画像に写っているものを説明してください。"),
            MessageContent.FromImage(texture) // Texture2D / RenderTexture。非 readable でも GPU 読み戻しで対応
        }
    }
};

var visionReply = await AIManager.SendMessageAsync(messages, AIModelType.GPT4o);
```
`MessageContent.FromImageData` や `MessageContent.FromImageUrl` も利用可能です。

### ストリーミング受信
`SendMessageStreamAsync` はストリーミング受信に対応し、`onContentDelta` で逐次出力できます。
※コールバックは `UnityWebRequest` の受信処理内で呼ばれるため、UI 更新などが必要な場合は適宜メインスレッドへディスパッチしてください。
```csharp
using System.Collections.Generic;
using UnityEngine;
using UnityLLMAPI.Chat;

var messages = new List<Message>
{
    new Message { role = MessageRole.System, content = "あなたは親切な Unity アシスタントです。" },
    new Message { role = MessageRole.User,   content = "設計方針を整理して、手順も示して。" }
};

var stream = await AIManager.SendMessageStreamAsync(
    messages,
    AIModelType.Gemini25Flash,
    onContentDelta: delta => Debug.Log(delta));

Debug.Log(stream?.Content);
```

## 5. JSON Schema ベースの構造化応答
`SendStructuredMessageAsync<T>` は指定した C# 型から JSON Schema を自動生成し、LLM の応答を `T` にデシリアライズします。属性で制約も付与できます。

```csharp
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using UnityLLMAPI.Chat;
using UnityLLMAPI.Schema;

public class Invoice
{
    [Description("請求書番号 (例: INV-001)")]
    [RegularExpression(@"^INV-\d{3}$")]
    public string InvoiceNumber;

    [Description("発行日 (YYYY-MM-DD)")]
    public string Date;

    [Description("合計金額")]
    [Range(0, 1000000)]
    [SchemaRange(0, 1000000)] // JSON Schema の minimum/maximum として提示
    public double TotalAmount;

    [Description("顧客リスト")]
    public List<Customer> Customers;
}

public class Customer
{
    [Description("顧客名")]
    public string Name;

    [Description("年齢")]
    [SchemaRange(0, 150)]
    public int Age;
}

var messages = new List<Message>
{
    new Message { role = MessageRole.System, content = "以下の請求書情報を JSON で抽出してください。" },
    new Message { role = MessageRole.User,   content = "請求書番号は INV-001、合計は 1500.50 USD、顧客は田中太郎(28)です。" }
};

var invoice = await AIManager.SendStructuredMessageAsync<Invoice>(messages, AIModelType.GPT4o);
```
`[Description]`, `[Range]`, `[RegularExpression]` のほか `[SchemaRange]`, `[SchemaRegularExpression]` など独自属性で JSON Schema に制約を載せられます。`SendStructuredMessageAsync(targetInstance, ...)` で既存インスタンスへ上書きも可能です。

## 6. RealTime Schema / Function Calling
- RealTime Schema: `RealTimeJsonSchema` による可変パラメータを LLM に渡し、実行時に更新された値を受け取れます。
```csharp
using System.Collections.Generic;
using UnityLLMAPI.Chat;
using UnityLLMAPI.Schema;

RealTimeJsonSchema<SchemaParameter> schemaTemplate = /* ScriptableObject 等で用意 */;
var messages = new List<Message>
{
    new Message
    {
        role = MessageRole.User,
        content = schemaTemplate.GenerateMarkDown("現在の状態") + "\n新しい値で更新してください。"
    }
};

var updated = await AIManager.SendStructuredMessageWithRealTimeSchemaAsync(messages, schemaTemplate, AIModelType.Gemini25Flash);
```

- Function Calling: 関数を `FunctionSchema<SchemaParameter>` で定義し、`SendFunctionCallMessageAsync` で LLM からの関数呼び出し結果を `IJsonSchema` として受け取ります。
```csharp
using System.Collections.Generic;
using UnityLLMAPI.Chat;
using UnityLLMAPI.Schema;

var functions = new List<IJsonSchema> { new AddNumbersFunction() };
var functionResult = await AIManager.SendFunctionCallMessageAsync(messages, functions, AIModelType.GPT4o);
if (functionResult is AddNumbersFunction add)
{
    Debug.Log($"呼び出された関数: {add.Name}");
    Debug.Log(add.GenerateMarkDown());
}
```

## 7. 画像生成
Gemini 2.5 Flash Image（GA）/ Gemini 3 Pro Image Preview / Gemini 3.1 Flash Image を使って、テキスト指示と既存画像から画像生成・編集ができます。

```csharp
using System.Collections.Generic;
using UnityLLMAPI.Chat;

var editMessages = new List<Message>
{
    new Message
    {
        role = MessageRole.User,
        parts = new List<MessageContent>
        {
            MessageContent.FromText("水彩画風にしてください。"),
            MessageContent.FromImage(texture) // 既存の Texture2D。非 readable でも GPU 読み戻しで PNG 化
        }
    }
};

// 画像モダリティを明示
var initBody = new Dictionary<string, object>
{
    { "generationConfig", new Dictionary<string, object>
        {
            { "responseModalities", new [] { "IMAGE" } }
        }
    }
};

var response = await AIManager.GenerateImagesAsync(
    editMessages,
    AIModelType.Gemini31FlashImage,
    initBody);

if (response?.images.Count > 0)
{
    var first = response.images[0];
    System.IO.File.WriteAllBytes("generated.png", first.data); // mimeType は first.mimeType で確認
}
```
`MessageContent.FromImageData` / `FromImageUrl` も利用可能です。生成結果は `ImageGenerationResponse` に `GeneratedImage`（`mimeType`, `data`）として格納されます。

## 8. 埋め込みベクトル（Embedding）
```csharp
using System.Collections.Generic;
using UnityEngine;
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
- モデル指定: `EmbeddingModelType.Gemini01`, `Gemini01_1536`, `Gemini01_768`, `OpenAISmall` (text-embedding-3-small), `OpenAILarge` (text-embedding-3-large)。
- `RankByCosine` でコサイン類似度の高い順に並べ替えられます（`SimilarityResult.Index`, `Score`）。

## 9. サンプルコードの案内
| ファイル | 何が試せるか |
| --- | --- |
| `Samples~/Example/ExampleUsage.cs` | 通常チャット、構造化レスポンス、RealTime Schema、Function Calling を Inspector の ContextMenu から実行 |
| `Samples~/Example/VisionSamples.cs` | Gemini 画像生成（編集）と Vision での画像説明のデモ。指示 + Texture2D を渡し、生成画像を保存 |
| `Samples~/Example/EmbeddingSample.cs` | word2vec 風の近傍探索、Gemini Embedding 2 のマルチモーダル入力例、AudioClip を `Decompress On Load` 前提で使う音声クエリと Editor 向け VideoClip クエリ、`CorpusWords` から最も近い語を選ぶ例、同一コーパスでのコサイン類似度比較 |
| `Samples~/Example/API_REFERENCE.md` | サンプルと主要 API の要点をまとめた簡易リファレンス |

各サンプルは MonoBehaviour をシーンに配置し、インスペクターの ContextMenu から実行できます。Vision サンプルはデフォルトで `Assets` 配下に PNG を保存します（必要に応じて `Application.persistentDataPath` などに変更してください）。

## 10. API クイックリファレンス

- **Message / MessageContent**
  - `Message.content`: テキストのみを送る場合の本文。
  - `Message.parts`: `MessageContent` のリスト。テキストと画像を混在させる場合はこちらを使用。
  - `MessageContent.FromText(string value)`: テキストパートを生成。
  - `MessageContent.FromImage(Texture texture, string mime = "image/png", bool allowGpuReadback = true, bool logWarnings = true)`: Texture から画像パートを生成（non-readable でも GPU 読み戻しで PNG 化）。
  - `MessageContent.FromImageData(byte[] data, string mime)`: バイト列を直接画像パートにする。
  - `MessageContent.FromImageUrl(string url, string mime = null)`: URL 参照の画像パートを作成。

- **AIManager**
  - `SendMessageAsync`: 通常のチャット。
  - `SendStructuredMessageAsync<T>` / `SendStructuredMessageAsync<T>(T targetInstance, ...)`: JSON Schema ベースの構造化レスポンスを `T` で受け取る / 既存インスタンスに適用。
  - `SendStructuredMessageWithRealTimeSchemaAsync`: `RealTimeJsonSchema` を送り、実行時に更新された値を `IJsonSchema` として取得。
  - `SendStructuredMessageWithSchemaAsync`: 任意の JSON Schema (Dictionary) を指定して Dictionary で受け取る。
  - `SendFunctionCallMessageAsync`: LLM からの Function Calling 結果を `IJsonSchema` として受信。
- `GenerateImagesAsync` / `GenerateImageAsync`: Gemini での画像生成（Gemini 2.5 Flash Image / Gemini 3 Pro Image Preview / Gemini 3.1 Flash Image）。

- **EmbeddingManager**
  - `CreateEmbeddingAsync(string text, EmbeddingModelType model = EmbeddingModelType.GeminiEmbedding2, ..., int? outputDimensionality = null)`: 単一テキストの埋め込み生成（Gemini / OpenAI）。
  - `CreateEmbeddingsAsync(IEnumerable<string> texts, EmbeddingModelType model = EmbeddingModelType.GeminiEmbedding2, ..., int? outputDimensionality = null)`: 複数テキストの埋め込み生成。
  - `EmbeddingModelType.GeminiEmbedding2`: 既定の Gemini 埋め込みモデル。マルチモーダル入力と `128` から `3072` の出力次元指定に対応。
  - `CreateEmbeddingAsync(EmbeddingInput input, EmbeddingModelType model = EmbeddingModelType.GeminiEmbedding2, ..., int? outputDimensionality = null)`: マルチモーダル入力を含む単一埋め込み生成。
  - `CreateEmbeddingsAsync(IEnumerable<EmbeddingInput> inputs, EmbeddingModelType model = EmbeddingModelType.GeminiEmbedding2, ..., int? outputDimensionality = null)`: 複数のマルチモーダル入力の埋め込み生成。
  - `EmbeddingModelType.Gemini01 / Gemini01_1536 / Gemini01_768`: Gemini Embedding 001 系のテキスト専用モデル。
  - `EmbeddingModelType.OpenAISmall / OpenAILarge`: OpenAI text-embedding-3-small / -large を指定。
  - `RankByCosine`: コサイン類似度でコーパスをランキング。

## 11. 補足・注意点 / ライセンス
- 画像生成フォーマット（PNG / JPEG など）やモダリティが必要な場合は、`initBody` の `generationConfig` に `responseModalities` などを追加し、Gemini 側の要件に合わせてください。
- 非 readable な Texture を送る際は GPU 読み戻しが走るためコストが増えます。頻繁に使う場合は Texture を readable にするか、`TextureEncodingUtility.TryGetPngBytes` で一度 PNG 化して再利用してください。
- Gemini Embedding 2 のマルチモーダル入力で画像を扱う場合、Unity では `Texture` / PNG バイト列 / file URI のいずれかで渡せます。`Texture` をそのまま使う場合の主な制約は PNG エンコード可否とメモリ使用量です。
- 音声は raw bytes / file URI ベースなら特別な Unity 制約はありません。`AudioClip` から直接埋め込む helper は `AudioClip.GetData` に依存するため、クリップの Import Settings で `Load Type = Decompress On Load` が必要です。
- 動画は runtime では `FromVideoData` または `FromFileUri` を推奨します。`FromVideoClip` は Unity Editor で元ファイルにアクセスできるワークフロー向けで、player build 向けの API ではありません。
- 画像・音声・動画のいずれも、非常に大きいデータを `inline_data` で毎回送るとメモリとリクエスト負荷が増えます。runtime では `StreamingAssets`、ダウンロード済みファイル、Addressables、独自の file URI 管理など、raw bytes を安定して取得できる経路を用意してください。
- API キー未設定時は呼び出しで警告 / エラーが出ます。`AIManagerBehaviour`、Unity Editor の `Tools > UnityLLMAPI > Configure API Keys`、環境変数の順に設定を確認してください。
- ライセンス: MIT License（詳細は `LICENSE` を参照）。
