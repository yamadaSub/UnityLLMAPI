UnityLLMAPI
===========

Unity から大手 LLM / Embedding API を呼び出すための支援パッケージです。  
API キーの取得、リクエスト生成、JSON Schema ベースの構造化応答、関数呼び出し、Gemini 画像入出力、埋め込みベクトル計算までを同じコード体系で扱えます。

本ドキュメントではセットアップから主要な利用パターン、サンプルコードの場所までを俯瞰できるようにまとめています。

---

セットアップ
------------
1. **API キーを取得して環境に登録**
   - 利用可能なキー: `OPENAI_API_KEY` / `GROK_API_KEY` または `XAI_API_KEY` / `GOOGLE_API_KEY`
   - Windows (PowerShell):\
     `Set-Item -Path Env:OPENAI_API_KEY -Value "<your_key>"`
   - macOS / Linux (bash / zsh):\
     `export OPENAI_API_KEY=<your_key>`
2. **Unity Editor でのキー設定 (任意)**
   - メニュー `Tools > UnityLLMAPI > Configure API Keys` から EditorUserSettings に保存可能  
     （暗号化されずプロジェクト外に保存されるため、VCS へはコミット不要です）
3. **ランタイム用のキー解決順序**
   - `AIManagerBehaviour` (ヒエラルキー上のコンポーネント)
   - EditorUserSettings (`UnityLLMAPI.OPENAI_API_KEY` など)
   - 環境変数（Process → User → Machine）

---

全体のワークフロー
------------------
1. **Message / MessageContent を組み立てる**  
   - テキストは `Message.content` または `MessageContent.FromText()`  
   - 画像は `MessageContent.FromImage()`（Texture から自動で PNG 化）または `FromImageData` / `FromImageUrl`
2. **AIManager / EmbeddingManager の API を呼ぶ**  
   - `SendMessageAsync`、`SendStructuredMessageAsync`、`SendFunctionCallMessageAsync`、`GenerateImagesAsync` など
3. **レスポンスを処理する**  
   - テキスト応答は string、構造化応答は任意の型、Function 呼び出しは `IJsonSchema`、画像生成は `ImageGenerationResponse`
4. **必要に応じて補助ユーティリティを活用**  
   - `TextureEncodingUtility.TryGetPngBytes`：Texture→PNG の安全な変換  
   - `UnityWebRequestUtils.SendAsync`：全 API 呼び出しで共通化した await パターン

---

主要機能とポイント
------------------
### 1. テキスト / マルチモーダルチャット
```csharp
var messages = new List<Message>
{
    new Message { role = MessageRole.System, content = "あなたは Unity エンジニアのアシスタントです。" },
    new Message { role = MessageRole.User,   content = "RuntimeInitializeOnLoadMethod の使い方を教えて。" }
};
var reply = await AIManager.SendMessageAsync(messages, AIModelType.Gemini25Flash);
```

### 2. JSON Schema ベースの構造化応答
```csharp
var invoice = await AIManager.SendStructuredMessageAsync<Invoice>(messages, AIModelType.GPT4o);
```
指定した型に合わせて JSON Schema を自動生成し、応答をデシリアライズします。
`[Description]`, `[Range]`, `[RegularExpression]` などの属性を使用して、より詳細な制約を定義できます。

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
### 補足: 独自のバリデーション属性について
`[SchemaRange]` や `[SchemaRegularExpression]` は、LLM に渡す **JSON Schema の制約条件 (`minimum`, `maximum`, `pattern`) を生成するため** に使用します。
これにより、LLM が生成する構造化データの値の範囲やフォーマットを制御できます。

**使用例と生成される Schema:**
```csharp
public class UserProfile
{
    [Description("年齢")]
    [SchemaRange(0, 150)] // JSON Schema: "minimum": 0, "maximum": 150 に変換されます
    public int Age;

    [Description("ユーザーID (英小文字のみ)")]
    [SchemaRegularExpression(@"^[a-z]+$")] // JSON Schema: "pattern": "^[a-z]+$" に変換されます
    public string UserId;
}
```

### 3. RealTime Schema / Function Calling
- `SendStructuredMessageWithRealTimeSchemaAsync`：`RealTimeJsonSchema` の値を都度更新
  - `SchemaParameter` に `Min`, `Max`, `Pattern` を設定することで、Inspector 上で制約を定義可能です。
- `SendFunctionCallMessageAsync`：LLM からの関数呼び出し結果を `IJsonSchema` として取得

### 4. 画像入出力 (Gemini 2.5 Flash Image Preview / Gemini 3 Pro Image Preview)
```csharp
var editMessages = new List<Message>
{
    new Message
    {
        role = MessageRole.User,
        parts = new List<MessageContent>
        {
            MessageContent.FromText("水彩画風にしてください。"),
            MessageContent.FromImage(texture) // Texture2D から自動 PNG 変換
        }
    }
};
var images = await AIManager.GenerateImagesAsync(editMessages);
```

### 5. 埋め込みベクトル
```csharp
var embedding = await EmbeddingManager.CreateEmbeddingAsync(
    "Unity loves C#",
    EmmbeddingModelType.Gemini01_1536); // Gemini 01 の出力次元を 1,536 に指定
var ranked = EmbeddingManager.RankByCosine(queryEmbedding, corpusEmbeddings);
```

---

サンプルコード
--------------
| ファイル | 内容 |
| --- | --- |
| `Samples~/Example/ExampleUsage.cs` | テキストチャット / 構造化応答 / RealTime Schema / Function Calling |
| `Samples~/Example/VisionSamples.cs` | Gemini 画像編集・Vision モデルでの画像解析 |
| `Samples~/Example/EmbeddingSample.cs` | Embedding の線形演算とコサイン類似度計算 |

どのサンプルも MonoBehaviour をシーンに配置し、インスペクターの ContextMenu から実行できます。  
画像系は `Application.persistentDataPath` に生成結果を保存します。

---

API リファレンス（抜粋）
------------------------
### Message / MessageContent
- `Message.content`：テキストのみの簡易入力
- `Message.parts`：`MessageContent` のリスト。テキスト・画像を混在させる場合はこちらを使用
- `MessageContent.FromImage(Texture texture, string mime = "image/png")`：Texture を PNG に変換して画像パートを生成（非 readable も自動対応）
- `MessageContent.FromImageData(byte[] data, string mime)`：既存のバイト列から生成
- `MessageContent.FromImageUrl(string url)`：URL 経由で画像を参照

### AIManager
- `SendMessageAsync`：通常のチャット
- `SendStructuredMessageAsync<T>`：構造化レスポンス（JSON Schema）
- `SendStructuredMessageWithRealTimeSchemaAsync`：RealTimeJsonSchema の値更新
- `SendFunctionCallMessageAsync`：LLM からの関数呼び出し結果を受け取り、`IJsonSchema` を返す
- `GenerateImagesAsync` / `GenerateImageAsync`：Gemini 画像生成

### EmbeddingManager
- `CreateEmbeddingAsync(string text, EmmbeddingModelType model = EmmbeddingModelType.Gemini01)`：OpenAI / Gemini の埋め込みを取得（Gemini 01 はモデル種別で次元数を選択）
- `EmmbeddingModelType.Gemini01 / Gemini01_1536 / Gemini01_768`：Gemini Embedding 001 の出力次元オプション
- `RankByCosine`：複数の埋め込みに対してコサイン類似度でランク付け

---

補足・注意点
------------
- 画像生成ではデフォルトで PNG を扱います。JPEG 等が必要な場合は `initBody` で `"generationConfig"` を追加し、Gemini 側の仕様に合わせてください。
- GPU 読み戻しは環境によってコストが大きくなる場合があります。頻繁に呼び出す場合はテクスチャをあらかじめ readable にしておくことを推奨します。
- API キーが設定されていない場合はエラーログでヒントを表示します。まずは `AIManagerBehaviour` の設定状態を確認してください。

---

ライセンス
----------
本パッケージは Unity プロジェクト内での利用を想定しています。詳細はリポジトリのライセンスファイルをご覧ください。
