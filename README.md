UnityLLMAPI
===========

Unity から複数の LLM / Embedding API を共通の API で扱うためのラッパーライブラリです。OpenAI / Grok / Gemini をまとめて、チャット、JSON Schema ベースの構造化応答、Function Calling、画像生成、Embedding を Unity のコードだけで呼び出せます。

## 1. 概要
- Unity スクリプトから LLM (テキスト / ビジョン) と Embedding を安全に叩くための補助パッケージです。
- 対応プロバイダと主なモデル (`AIModelType`):
  - OpenAI: `GPT4o`, `GPT5`, `GPT5_2`, `GPT5Mini`, `GPT5Pro`
  - Grok (x.ai): `Grok2`, `Grok3`, `Grok4_1`, `Grok4_1Reasoning`
  - Gemini: `Gemini25`, `Gemini25Pro`, `Gemini25Flash`, `Gemini25FlashLite`, `Gemini25FlashImage`（旧 `Gemini25FlashImagePreview`）、`Gemini3`, `Gemini3ProImage`（Vision / 画像生成に対応）
- Embedding は OpenAI (text-embedding-3-small / -large) と Gemini Embedding 001 系をサポートします。

## 2. セットアップ

### パッケージの配置
- このリポジトリ一式を Unity プロジェクトに配置するだけで利用できます（asmdef 同梱）。

### API キーの取得と設定
- 利用するプロバイダごとに以下の環境変数を設定してください（いずれも Process / User / Machine の順で参照されます）。
  - OpenAI: `OPENAI_API_KEY`
  - Grok (x.ai): `GROK_API_KEY`
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
Gemini 2.5 Flash Image（GA）/ Gemini 3 Pro Image Preview を使って、テキスト指示と既存画像から画像生成・編集ができます。

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
    AIModelType.Gemini25FlashImage,
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
    EmbeddingModelType.Gemini01_1536); // Gemini 01 の出力次元 1,536

var corpusTexts = new List<string> { "Unity", "Unreal", "C#", "Shader Graph" };
var corpus = await EmbeddingManager.CreateEmbeddingsAsync(corpusTexts, EmbeddingModelType.Gemini01_1536);

var ranked = EmbeddingManager.RankByCosine(queryEmbedding, corpus);
```
- モデル指定: `EmbeddingModelType.Gemini01`, `Gemini01_1536`, `Gemini01_768`, `OpenAISmall` (text-embedding-3-small), `OpenAILarge` (text-embedding-3-large)。
- `RankByCosine` でコサイン類似度の高い順に並べ替えられます（`SimilarityResult.Index`, `Score`）。

## 9. サンプルコードの案内
| ファイル | 何が試せるか |
| --- | --- |
| `Samples~/Example/ExampleUsage.cs` | 通常チャット、構造化レスポンス、RealTime Schema、Function Calling を Inspector の ContextMenu から実行 |
| `Samples~/Example/VisionSamples.cs` | Gemini 画像生成（編集）と Vision での画像説明のデモ。指示 + Texture2D を渡し、生成画像を保存 |
| `Samples~/Example/EmbeddingSample.cs` | Embedding 生成、コサイン類似度ランキング、Hadamard 積 / 除算の例 |

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
- `GenerateImagesAsync` / `GenerateImageAsync`: Gemini での画像生成（Gemini 2.5 Flash Image / Gemini 3 Pro Image Preview）。

- **EmbeddingManager**
  - `CreateEmbeddingAsync(string text, EmbeddingModelType model = EmbeddingModelType.Gemini01)`: 単一テキストの埋め込み生成（Gemini / OpenAI）。
  - `CreateEmbeddingsAsync(IEnumerable<string> texts, EmbeddingModelType model = EmbeddingModelType.Gemini01)`: 複数テキストの埋め込み生成。
  - `EmbeddingModelType.Gemini01 / Gemini01_1536 / Gemini01_768`: Gemini Embedding 001 の出力次元を指定。
  - `EmbeddingModelType.OpenAISmall / OpenAILarge`: OpenAI text-embedding-3-small / -large を指定。
  - `RankByCosine`: コサイン類似度でコーパスをランキング。

## 11. 補足・注意点 / ライセンス
- 画像生成フォーマット（PNG / JPEG など）やモダリティが必要な場合は、`initBody` の `generationConfig` に `responseModalities` などを追加し、Gemini 側の要件に合わせてください。
- 非 readable な Texture を送る際は GPU 読み戻しが走るためコストが増えます。頻繁に使う場合は Texture を readable にするか、`TextureEncodingUtility.TryGetPngBytes` で一度 PNG 化して再利用してください。
- API キー未設定時は呼び出しで警告 / エラーが出ます。`AIManagerBehaviour`、Unity Editor の `Tools > UnityLLMAPI > Configure API Keys`、環境変数の順に設定を確認してください。
- ライセンス: MIT License（詳細は `LICENSE` を参照）。
