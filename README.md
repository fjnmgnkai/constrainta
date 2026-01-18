# ConstrainTA

ConstrainTA is a Unity editor tool to import and rebuild VRChat constraint setups.

Installation (Git):

- Use Unity Package Manager with the repository Git URL, or add to `manifest.json`:

# ConstrainTA / ConstrainTA

日本語: VRChat の constraint をインポートして再構築する Unity エディターツールです。

English: Unity Editor tool to import and rebuild VRChat constraint setups.

---

## インストール（VCC / Community Repos）
- VCC の Repositories に次の URL を追加してください:
  - https://raw.githubusercontent.com/fjnmgnkai/constrainta-vpm/main/vpm.json

## Install (UPM / Git)
- Add via Unity Package Manager (Git URL):
  - https://github.com/fjnmgnkai/constrainta.git#v1.0.0

## Requirements / 必要条件
- Unity: 2022.3 LTS 系を推奨
- VRChat SDK: `com.vrchat.base` がプロジェクトにインストールされていること（vpmDependencies に `^3.6.0` を要求）

## Quick usage
- Open Window → ConstrainTA to import or rebuild constraints.
- The tool is editor-only; runtime code is separated into runtime folders.

## Notes
- If VCC reports "Compatible package version not found", ensure your project has the VRChat SDK package (`com.vrchat.base`) installed and that VCC's repository list has been refreshed.

## Troubleshooting & Support
- Report issues at: https://github.com/fjnmgnkai/constrainta/issues

## License
- MIT

---

_(This README is trimmed for distribution; see repository for developer notes and tests.)_
- https://github.com/<user>/ConstraintA.git#v0.1.0
