# ConstrainTA (GitHub package template)

このフォルダは Unity UPM 形式で GitHub に公開するためのテンプレートです。

構成:
- `package.json` - 必須マニフェスト
- `Runtime/` - 実行コード（ここには不要なファイルを追加しないでください）
- `Editor/` - エディタ拡張（必要な場合のみ）
- `README.md` - パッケージ説明

使い方:
1. このフォルダをリポジトリのルートに置いて GitHub に push します（`Runtime` や `Editor` に実際のコードを入れてください）。
2. リリースを作成し、生成されたソース zip をダウンロードして SHA256 を計算します。
3. VPM リポジトリ（別 repo）に `vpm.json` を用意して VCC に登録します。
