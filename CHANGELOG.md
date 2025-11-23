# 変更履歴

このプロジェクトのすべての重要な変更はこのファイルに記録されます。

## 1.2.0

### 追加
- **Gemini 3 対応**:
    - テキスト生成 (`Gemini3`) および画像生成 (`Gemini3ProImagePreview`) モデルを追加。
- **構造化出力の改善**:
    - 独自のバリデーション属性 (`[SchemaRange]`, `[SchemaRegularExpression]`) を導入し、`System.ComponentModel.DataAnnotations` への依存を排除。
- **Embedding の最適化**:
    - ベクトル演算（コサイン類似度）の計算パフォーマンスを向上。

### 変更
- **サンプルコード**: 画像生成の保存先指定追加、不要パラメータ削除、ツールチップ修正。

## 1.1.0

### 追加
- **API キー管理の刷新**:
    - `AIManagerBehaviour` (Inspector)、`EditorUserSettings` (Toolsメニュー)、環境変数の優先順位でキーを解決する仕組みを導入。
- **Gemini 2.5 対応**:
    - `Gemini 2.5 Pro/Flash` および画像生成 (`Flash Image Preview`) への対応。
- **Function Calling**:
    - `IJsonSchema` を用いた関数呼び出しのサポート。
- **RealTime Schema**:
    - 動的なスキーマ更新機能の追加。

## 1.0.0

### 追加
- **初期リリース**:
    - OpenAI / Gemini のチャット機能、構造化出力、Embedding API のサポート。
