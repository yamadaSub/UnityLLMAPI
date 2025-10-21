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


