# 本地素材准备

使用你已有、可在本机使用的素材。下面的脚本仅从你指定的目录读取文件，验证 SHA-256 后复制到本地构建目录。Git 会忽略这些素材目录。

```powershell
.\Import-LocalAssets.ps1 -PetAssets '你的键盘桌宠安装目录\Assets' -CurtainGif '你的仿真遮屏安装目录\Assets\desk-break-hd.gif'
```

可以只提供其中一个参数。首次单独构建遮屏时，还需要将桌宠原版的 `YierBubu.ico` 放到本地 `windows/assets/`。

| 素材 | 本地位置 | 来源信息 |
|---|---|---|
| 桌宠三张基础 PNG 及托盘图标 | `windows/assets/` | [上游第三方声明](windows/THIRD_PARTY_NOTICES.md)和 [PNG 校验值](windows/YierBubu.Windows/Assets.cs) |
| 桌宠日常动画 | `windows/assets/Animations/` | [素材来源和校验值](windows/animation-catalog.json) |
| 单熊及双熊的键盘素材 | `windows/assets/Keyboards/` | [素材来源和校验值](windows/keyboard-catalog.json)。图集目录中 `sourceSha256` 校验来源 WebP，`sha256` 校验供程序读取的 PNG |
| 遮屏高清透明 GIF | `SimulationCurtain/Assets/desk-break-hd.gif` | [PotatoBoo 的 Tenor 动图页面](https://tenor.com/view/bubu-think-bubu-thinking-bubu-bubu-dudu-dudu-bubu-gif-72897942192232255)所列的 `gif_transparent` 版本，498 × 498 |

如需手动放置素材，将两份目录 JSON 分别复制为对应动画子目录下的 `catalog.json`。三张基础图和动画的名称、格式与校验值需要和目录一致。
