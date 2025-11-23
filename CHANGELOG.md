# 変更履歴

このプロジェクトの重要な変更を記録します。

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
