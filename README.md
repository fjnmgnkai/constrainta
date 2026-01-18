# ConstrainTA

ConstrainTA is a Unity editor tool to import and rebuild VRChat constraint setups.

Installation (Git):

- Use Unity Package Manager with the repository Git URL, or add to `manifest.json`:

```

```

Notes:

- Requires VRChat SDK; configured via `vpmDependencies` in `package.json`.
- Runtime and Editor code are separated via asmdef files.
```markdown
# ConstrainTA

VRChat の制約（constraints）をインポートして再構築するための Unity エディターツールです。

## インストール（埋め込み）
このプロジェクトはパッケージを `Packages/com.github.fjnmgnkai.constrainta` 配下に埋め込んでいます。

## インストール（Git URL 経由）
Unity Package Manager / VCC で次の Git URL から追加できます:
- https://github.com/<user>/ConstraintA.git#v0.1.0
- または `manifest.json` に追記:


## 注意事項
- VRChat SDK を含む Unity プロジェクトで使用してください（制約の型は SDK に依存します）。
- 診断用 UI はデフォルトで無効です。調査目的で有効化するには、Scripting Define Symbols に `CONSTRAINTA_DIAGNOSTICS` を追加してください。


# ConstrainTA

Unity Editor tool for importing and rebuilding VRChat constraints.

## Install (embedded)
This project embeds the package under `Packages/com.github.fjnmgnkai.constrainta`.

## Install (via Git URL)
Install via Unity Package Manager / VCC (Git URL):
- https://github.com/<user>/ConstraintA.git#v0.1.0
- Or add to manifest.json:
  "com.github.fjnmgnkai.constrainta": "https://github.com/<user>/ConstraintA.git#v0.1.0"

## Notes
- Requires a VRChat SDK project (constraints/types come from the SDK).
- Diagnostics UI is disabled by default. To enable it for investigation, add `CONSTRAINTA_DIAGNOSTICS` to Scripting Define Symbols.

## Quick steps to publish:
1. Ensure package.json at repo root (this folder) is correct.
2. Commit and push to GitHub.
3. Tag a release: git tag v0.1.0 && git push --tags
4. In VCC / Unity, add package by Git URL above.

## Project layout (recommended):
- Runtime/        -> runtime .cs files (non-Editor)
- Editor/         -> editor-only .cs files (ConstrainTAWindow.cs)
- Samples~/       -> optional sample scenes
