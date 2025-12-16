# 変更履歴

このプロジェクトの重要な変更を記録します。

## 1.2.4

### 変更
- Grok 4.1 モデル（`AIModelType.Grok4_1` / `grok-4-1-fast-non-reasoning`）を追加
- Grok 4.1 Reasoning モデル（`AIModelType.Grok4_1Reasoning` / `grok-4-1-fast-reasoning`）を追加
- GPT-5.2 モデル（`AIModelType.GPT5_2` / `gpt-5.2`）を追加
- OpenAI / Grok の Function Calling を `tools` / `tool_choice` 形式に対応（レスポンスの `tool_calls` もパース）

## 1.2.3

### 変更
- Gemini API の画像生成モデルを GA 版 `gemini-2.5-flash-image` へ移行（`gemini-2.5-flash-image-preview` は 2026-01-15 に提供終了予定）。
- それに伴いモデルタイプ名も変更

## 1.2.2

### 変更
- `AIManager` の God クラス化を是正
  - メッセージ DTO（`Message`, `MessageContent` 等）の定義
  - モデル列挙 (`AIModelType`) → 実際のモデル ID（例: `"gpt-4o"`）へのマッピング
  - OpenAI / Grok / Gemini など各 Provider 向けの HTTP リクエスト構築・送信処理
  - 構造化出力（JSON Schema）・Function Calling の処理
  - 画像生成 / Vision などの追加機能の実装

## 1.2.1

### 変更
- Runtime 側に `UnityLLMAPI.Chat` / `.Schema` / `.Embedding` / `.Common` の名前空間を追加し、公開 API の誤記だった `Emmbedding*` を `Embedding*` にリネームして名前衝突リスクを低減。
- `UnityWebRequestUtils` にキャンセル・タイムアウト対応のオーバーロードを追加し、EmbeddingManager にキャンセル/タイムアウト引数とバッチ生成 API `CreateEmbeddingsAsync` を追加。
- Schema 周りを拡張（`AllowedValues` / `SchemaMultipleOf` 属性追加、`IJsonSchema.ParseValueDict` へ改名、`UnityEditor` の using をガード、重複 using を整理）。
- サンプルと README の参照を新しい API/名前空間に合わせて更新。

## 1.2.0

### 追加
- **Gemini 3 対応**:
    - チャット生成 (`Gemini3`) および画像生成 (`Gemini3ProImagePreview`) モデルを追加。
- **構造化出力の改良**:
    - 独自のバリデーション属性 (`[SchemaRange]`, `[SchemaRegularExpression]`) を導入し、`System.ComponentModel.DataAnnotations` への依存を排除。
- **Embedding の最適化**:
    - ベクトル演算（コサイン類似度など）の計算パフォーマンスを向上。

### 変更
- **サンプルコード**: 画像生成の保存対応を追加し、不要パラメータ削除、ツールチップを調整。

## 1.1.0

### 追加
- **API キー管理の刷新**:
    - `AIManagerBehaviour`（Inspector）、`EditorUserSettings`（Tools メニュー）、環境変数の優先順でキーを解決する仕組みを導入。
- **Gemini 2.5 対応**:
    - `Gemini 2.5 Pro/Flash` および画像生成 (`Flash Image Preview`) への対応。
- **Function Calling**:
    - `IJsonSchema` を用いた関数呼び出しをサポート。
- **RealTime Schema**:
    - 動的なスキーマ更新機能を追加。

## 1.0.0

### 追加
- **初期リリース**:
    - OpenAI / Gemini のチャット機能、構造化出力、Embedding API のサポート。
