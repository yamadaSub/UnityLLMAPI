UnityLLMAPI

概要
- 実行時にのみAPIキーを解決し、アセット・シーン・Prefabへは一切保存しない方針のUnity向けLLM/Embeddings呼び出しパッケージです。
- キーはプロジェクト内に残さず、環境変数またはEditor専用のEditorUserSettingsから取得します。

キーの設定方法
- 解決順序（ランタイム）
  1) 環境変数
  2) Unity EditorUserSettings（Editorのみ）

- 設定する環境変数
  - `OPENAI_API_KEY`
  - `GROK_API_KEY` または `XAI_API_KEY`（どちらか片方で可）
  - `GOOGLE_API_KEY`

- すぐに設定する例
  - Windows（PowerShell / 現在のユーザー）
    `[System.Environment]::SetEnvironmentVariable("OPENAI_API_KEY","<your_key>","User")`
  - macOS/Linux（bash/zsh）
    `export OPENAI_API_KEY=<your_key>`

- Editorメニュー（EditorUserSettings）
  - Unityメニュー: `Tools > UnityLLMAPI > Configure API Keys`
  - 入力した値は以下のEditorUserSettingsキーに保存されます（ビルドやVCSには含まれません）
    - `UnityLLMAPI.OPENAI_API_KEY`
    - `UnityLLMAPI.GROK_API_KEY`
    - `UnityLLMAPI.GOOGLE_API_KEY`
  - 保存先はプロジェクト直下の UserSettings/EditorUserSettings.asset です（プロジェクト毎に分離され、VCS へは含めません）。
  - `パッケージ動作をテストする（Editor設定を無視）` トグルを ON にすると EditorUserSettings に保存された API キーを一時的に無視して、パッケージ配布時と同じ挙動を Unity Editor 内で確認できます。
- Grok(x.ai)は `GROK_API_KEY` が設定されていれば利用できます。

運用上の注意
- APIキーが見つからない状態でリクエストを送ると、必要な環境変数名を含むわかりやすいエラーログを出して処理を中断します。
- ローカル開発では環境変数の利用を推奨し、補助的にEditorUserSettingsを使用できます（Editor限定、ビルドには含まれません）。

本番運用の推奨構成（サーバープロキシ）
- クライアント（ゲーム/アプリ）にAPIキーを同梱しないでください。
- 代わりに自前のバックエンド経由でLLMリクエストを転送する構成を推奨します。
  - サーバー側で各プロバイダのAPIキーを安全に保管（環境変数やSecret Manager）。
  - クライアント用に最小限のエンドポイント（例: `/chat`, `/embeddings`）を公開。
  - 入力検証・スキーマ整形、認証、レート制限、課金/クオータ管理をサーバー側で実施。
  - 必要な情報のみをクライアントへ返却し、ログ・モニタリングもサーバー側で行う。

関連実装のメモ
- `Runtime/Chat/AIManager.cs` は `OpenAIApiKey` / `GrokApiKey` / `GoogleApiKey` を実行時に解決します。
  - 環境変数 →（Editor時のみ）`UnityEditor.EditorUserSettings` の順で解決。
  - いずれもアセットやシーンに保存されません。

サンプル
- `Samples~/Example` に簡単な使用例があります。動作には対応する環境変数またはEditorUserSettingsの設定が必要です。



## �r�W�����L�[�� / ���摜���̓��o�͂ւ̑�ӂ�

- `Message` �� `parts (List<MessageContent>)` �ɂ�鐶���ŁA�e�L�X�g�ƃC���[�W�����g�������܂��B`content` ���w�肵���ꍇ���A�ړI�Ƀe�L�X�g�p�[�g�Ƃ��Đ�p����܂��B
- `MessageContent.FromImageData(byte[], mimeType)` �� `MessageContent.FromImageUrl(string url)` �Ō摜�����͂܂��B�C���f�b�N�o�[�f�ŏo�p����Ƃ��́A���̃w�b�_�ł̃f�[�^�C�A���O���g�p���ĉ����܂��B
- ���摜�쐬�́A `AIManager.GenerateImagesAsync` (gemini-2.5-flash-image-preview ����) �������オ�C`ImageGenerationResponse` �����߂� `GeneratedImage` ������ ���摜�f�[�^��ɓ����܂��B
- `Samples~/Example/VisionSamples.cs` �� Unity ���C���g�E���W���[���ł̑g�p��`[ContextMenu]` �ŕ\����Ă��܂��B

### ���摜�ύX (Gemini 2.5 Flash Image Preview)

```csharp
var imageBytes = texture.EncodeToPNG();

var editMessages = new List<Message>
{
    new Message
    {
        role = MessageRole.User,
        parts = new List<MessageContent>
        {
            MessageContent.FromText("水彩画風にしてください。"),
            MessageContent.FromImageData(imageBytes, "image/png")
        }
    }
};

var imageResponse = await AIManager.GenerateImagesAsync(
    editMessages,
    AIModelType.Gemini25FlashImagePreview);

if (imageResponse?.images.Count > 0)
{
    var generated = imageResponse.images[0];
    System.IO.File.WriteAllBytes("edited.png", generated.data);
}
```

### ���摜�L�񋟂̑g�ݍ� (�r�W�����L�[���p)

```csharp
var photoBytes = photoTexture.EncodeToPNG();

var describeMessages = new List<Message>
{
    new Message
    {
        role = MessageRole.User,
        parts = new List<MessageContent>
        {
            MessageContent.FromText("この画像に写っている内容を説明してください。"),
            MessageContent.FromImageData(photoBytes, "image/png")
        }
    }
};

var description = await AIManager.SendMessageAsync(
    describeMessages,
    AIModelType.Gemini25Flash); // GPT4o など他のビジョン対応モデルにも差し替え可能
```

> `MessageContent.FromImageData` に渡すバイト列は `Texture2D.EncodeToPNG()` や `System.IO.File.ReadAllBytes()` など任意の手段で用意できます。URL を直接指定したい場合は `MessageContent.FromImageUrl("https://...")` を使用してください。

## Async API

- すべてのランタイムAPIは `Task` / `Task<T>` を返す構成になりました。追加パッケージは不要です。

- `await AIManager.SendMessageAsync(messages, AIModelType.GPT4o);` のように利用できます。



```csharp

var reply = await AIManager.SendMessageAsync(history, AIModelType.GPT4o);

var embedding = await EmbeddingManager.CreateEmbeddingAsync("hello world");

```



## Coroutine Integration

- `Runtime/Common/TaskCoroutineExtensions.cs` にコルーチン用のブリッジを用意しました。

- `StartCoroutine(AIManager.SendMessageAsync(...).AsCoroutine(result => { ... }))` の形で結果を受け取れます。



```csharp

yield return AIManager

    .SendMessageAsync(history, AIModelType.GPT4o)

    .AsCoroutine(result => Debug.Log(result),

                 error => Debug.LogError(error));

```


