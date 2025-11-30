UnityLLMAPI
===========

Unity から LLM / Embedding API を呼び出すための支援パッケージです。
API キーの取得からリクエスト生成、JSON Schema ベースの構造化応答、Function Calling、Gemini 画像生成、埋め込みベクトル計算までを同じコード体系で扱えます。

この README ではセットアップ手順と主要な利用パターンを簡潔にまとめます。

---

セットアップ
------------
1. **API キーを取得して環境変数に登録**
   - 利用可能なキー: `OPENAI_API_KEY` / `GROK_API_KEY` または `XAI_API_KEY` / `GOOGLE_API_KEY`
   - Windows (PowerShell): `Set-Item -Path Env:OPENAI_API_KEY -Value "<your_key>"`
   - macOS / Linux (bash / zsh): `export OPENAI_API_KEY=<your_key>`
2. **Unity Editor でのキー設定 (任意)**
   - メニュー `Tools > UnityLLMAPI > Configure API Keys` から EditorUserSettings に保存可能
   - 暗号化されずプロジェクト外に保存されるため、VCS へはコミット不要です
3. **ランタイムでのキー解決順**
   - `AIManagerBehaviour` (ヒエラルキー上のコンポーネント)
   - EditorUserSettings (`UnityLLMAPI.OPENAI_API_KEY` など)
   - 環境変数 (Process → User → Machine)

---

全体ワークフロー
------------------
1. **Message / MessageContent を組み立てる**
   - テキスト: `Message.content` または `MessageContent.FromText()`
   - 画像: `MessageContent.FromImage()` (Texture から PNG 化) / `FromImageData` / `FromImageUrl`
2. **AIManager / EmbeddingManager の API を呼ぶ**
   - `SendMessageAsync`、`SendStructuredMessageAsync`、`SendFunctionCallMessageAsync`、`GenerateImagesAsync` など
3. **レスポンスを受け取る**
   - テキスト応答: `string`
   - 構造化応答: 任意の型 or `Dictionary<string, object>`
   - Function Calling: `IJsonSchema`
   - 画像生成: `ImageGenerationResponse`
4. **必要に応じて補助ユーティリティを活用**
   - `TextureEncodingUtility.TryGetPngBytes` で Texture→PNG 変換
   - `UnityWebRequestUtils.SendAsync` で共通の await パターンを利用

---

主な機能とポイント
------------------
### 1. テキスト / マルチモーダルチャット
```csharp
var messages = new List<Message>
{
    new Message { role = MessageRole.System, content = "あなたは Unity エンジニアのアシスタントです" },
    new Message { role = MessageRole.User,   content = "RuntimeInitializeOnLoadMethod の使い方を教えて" }
};
var reply = await AIManager.SendMessageAsync(messages, AIModelType.Gemini25Flash);
```

### 2. JSON Schema ベースの構造化応答
```csharp
var invoice = await AIManager.SendStructuredMessageAsync<Invoice>(messages, AIModelType.GPT4o);
```
指定した型に合わせて JSON Schema を自動生成し、応答をデシリアライズします。
`[Description]`, `[Range]`, `[RegularExpression]` などの属性で制約を定義できます。

```csharp
public class Invoice
{
    [Description("請求書番号 (例: INV-001)")]
    [RegularExpression(@"^INV-\d{3}$")]
    public string InvoiceNumber;

    [Description("合計金額")]
    [Range(0, 1000000)]
    public double TotalAmount;
}
```
#### 補足: 独自のバリデーション属性
`[SchemaRange]` や `[SchemaRegularExpression]` は JSON Schema の `minimum` / `maximum` / `pattern` を LLM に伝えるために利用できます。

### 3. RealTime Schema / Function Calling
- `SendStructuredMessageWithRealTimeSchemaAsync` で `RealTimeJsonSchema` の値を随時更新
- `SendFunctionCallMessageAsync` で LLM からの関数呼び出し結果を `IJsonSchema` として取得

### 4. 画像生成 (Gemini 2.5 Flash Image Preview / Gemini 3 Pro Image Preview)
```csharp
var editMessages = new List<Message>
{
    new Message
    {
        role = MessageRole.User,
        parts = new List<MessageContent>
        {
            MessageContent.FromText("水彩画風にしてください"),
            MessageContent.FromImage(texture) // Texture2D から自動で PNG 変換
        }
    }
};
var images = await AIManager.GenerateImagesAsync(editMessages);
```

### 5. 埋め込みベクトル
```csharp
var embedding = await EmbeddingManager.CreateEmbeddingAsync(
    "Unity loves C#",
    EmbeddingModelType.Gemini01_1536); // Gemini 01 の出力次元 1,536
var ranked = EmbeddingManager.RankByCosine(queryEmbedding, corpusEmbeddings);
```

---

サンプルコード
--------------
| ファイル | 内容 |
| --- | --- |
| `Samples~/Example/ExampleUsage.cs` | テキストチャット / 構造化応答 / RealTime Schema / Function Calling |
| `Samples~/Example/VisionSamples.cs` | Gemini 画像編集と Vision モデルでの画像解析 |
| `Samples~/Example/EmbeddingSample.cs` | Embedding の線形演算とコサイン類似度計算 |

どのサンプルも MonoBehaviour をシーンに配置し、インスペクターの ContextMenu から実行できます。画像系は `Application.persistentDataPath` に生成結果を保存します。

---

API クイックリファレンス
------------------------
### Message / MessageContent
- `Message.content`: テキストのみの簡易入力
- `Message.parts`: `MessageContent` のリスト。テキストと画像を混在させる場合はこちら
- `MessageContent.FromImage(Texture texture, string mime = "image/png")`: Texture から PNG に変換して画像パートを生成（非 readable も自動対応）
- `MessageContent.FromImageData(byte[] data, string mime)`: 既存バイト列から生成
- `MessageContent.FromImageUrl(string url)`: URL 経由で画像を参照

### AIManager
- `SendMessageAsync`: 通常チャット
- `SendStructuredMessageAsync<T>`: 構造化レスポンス (JSON Schema 自動生成)
- `SendStructuredMessageWithRealTimeSchemaAsync`: RealTimeJsonSchema の値更新
- `SendFunctionCallMessageAsync`: LLM からの関数呼び出し結果を `IJsonSchema` で受け取る
- `GenerateImagesAsync` / `GenerateImageAsync`: Gemini 画像生成

### EmbeddingManager
- `CreateEmbeddingAsync(string text, EmbeddingModelType model = EmbeddingModelType.Gemini01)`: OpenAI / Gemini の埋め込みを取得
- `EmbeddingModelType.Gemini01 / Gemini01_1536 / Gemini01_768`: Gemini Embedding 001 の出力次元オプション
- `RankByCosine`: 複数の埋め込みに対してコサイン類似度でランク付け

---

補足・注意点
------------
- 画像生成はデフォルトで PNG。JPEG などが必要な場合は `initBody` で `generationConfig` を追加し、Gemini 側の仕様に合わせてください。
- GPU 読み戻しは環境によってコストが大きくなる場合があります。頻繁に呼び出す場合はテクスチャをあらかじめ readable にしておくことを推奨します。
- API キーが未設定の場合はエラーログでヒントを表示します。まず `AIManagerBehaviour` の設定状態を確認してください。

---

ライセンス
----------
本パッケージは Unity プロジェクトでの利用を想定しています。詳細は同梱のライセンスファイルをご覧ください。
