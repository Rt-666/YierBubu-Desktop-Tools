# 一二布布 Windows 桌面工具

两款独立运行的 Windows 小工具。桌宠陪你敲键盘，仿真遮屏在计算期间遮住显示器上的内容。

| 工具 | 功能 | 操作 |
|---|---|---|
| 键盘桌宠 | 一二、布布和双熊动画，部分款式随键鼠动作播放，空闲时眨眼和张望 | 右键小熊，打开「动画小屋」切换款式 |
| 仿真遮屏 | 用不透明夜景遮住全部或指定屏幕，右下角播放「一二工位摸鱼」 | `Ctrl + Alt + B` 开启，`Esc` 恢复 |

遮屏期间 Windows 会保持登录，程序会请求保持唤醒。遮罩用于挡住现场旁观视线，能操作电脑的人仍可退出遮罩。长时间跑仿真之前，可以用短算例确认在遮屏时输出文件仍在更新。

## 下载与运行

目前可在 GitHub 点击 **Code → Download ZIP** 下载源码。角色素材和包含这些素材的运行包暂未随仓库发布。源码可以编译，运行完整画面需按[本地素材说明](ASSETS.md)准备资源。

用 Windows 和 .NET 10 SDK，在仓库根目录执行：

```powershell
dotnet build windows/YierBubu.Windows -c Release
dotnet build SimulationCurtain -c Release
dotnet run --project windows/YierBubu.Tests -c Release
```

准备好资源后，可打包为无需单独安装 .NET 的 Windows x64 版本：

```powershell
dotnet publish windows/YierBubu.Windows -c Release -r win-x64 --self-contained true -o dist/KeyboardPet
dotnet publish SimulationCurtain -c Release -r win-x64 --self-contained true -o dist/SimulationCurtain
```

分别运行输出目录里的 `YierBubu.exe` 和 `SimulationCurtain.exe`。请保留各自的完整目录。

## 来源与许可

键盘桌宠基于 [Li-Jiangjiu/YierBubu](https://github.com/Li-Jiangjiu/YierBubu) 的 Windows 实现。上游基线提交为 `5faff2c60fc936612cce302a4fd5282dd9385b09`。代码沿用 [MIT 许可](LICENSE)，保留了[上游第三方声明](windows/THIRD_PARTY_NOTICES.md)。

一二布布角色原作黄小B。本项目是非官方个人作品。角色图片、动画和图标的权利独立于代码许可，来源和文件校验信息见[素材说明](ASSETS.md)。
