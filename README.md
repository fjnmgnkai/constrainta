# こんすとれいんた～(ベータ版)
VRChat の constraint をインポートして再構築する Unity エディターツールです。<br>
Unity Editor tool to import and rebuild VRChat constraint setups.

---
## 導入方法（VCC / Community Repos）

### VCCにリポジトリを追加

以下のURLをコピーして、VCCの「Add Repository」に貼り付けてください：
```
https://raw.githubusercontent.com/fjnmgnkai/constrainta-vpm/main/vpm.json
```

> 💡 コードブロックの右上にある📋アイコンをクリックでコピーできます

#### 手順
1. VCC (VRChat Creator Companion) を開く
2. **Settings** → **Packages** → **Add Repository** をクリック
3. 上記URLを貼り付けて **Add** をクリック

![導入手順](https://private-user-images.githubusercontent.com/160860805/537525581-0fa7a557-0dba-4648-8e70-c16e306b257d.png)
* VCC の Repositories に次の URL を追加してください:
  + https://raw.githubusercontent.com/fjnmgnkai/constrainta-vpm/main/vpm.json
<img width="1583" height="946" alt="image" src="https://github.com/user-attachments/assets/0fa7a557-0dba-4648-8e70-c16e306b257d" />

## Install (UPM / Git)
- Add via Unity Package Manager (Git URL):
  - https://github.com/fjnmgnkai/constrainta.git#v1.0.0
<img width="1583" height="946" alt="image" src="https://github.com/user-attachments/assets/d76e5af9-8c79-4f26-8316-b4dfb28c87f3" />

## Requirements / 必要条件
- Unity: 2022.3 LTS 系を使用
- VRChat SDK: `com.vrchat.base` がプロジェクトにインストールされていること（vpmDependencies に `^3.10.0` を要求）

## Quick usage
- Unityツールバー > Tool > こんすとれいんた～ > こんすとれいんた～

- Open Window → ConstrainTA to import or rebuild constraints.

## Troubleshooting & Support
- Report issues at: https://github.com/fjnmgnkai/constrainta/issues

## License
- MIT
