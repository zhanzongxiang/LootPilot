# LootPilot

LootPilot 是一款面向 Windows 的屏幕物资识别与价格辅助工具。它通过截图、OCR、物品匹配和公开价格数据，把需要记忆或逐项查询的信息转化为直接的视觉结果。

> 当前版本：`0.11.0`（公开测试版）
> 主要适配：Windows 10/11、1920×1080、100% 游戏 UI 缩放

![LootPilot Logo](assets/LootPilot-logo.png)

## 工作方式与安全边界

```text
屏幕截图 → 本地 OCR → 物品匹配 → 价格 API → 本地结果显示
```

LootPilot 只处理屏幕上已经显示的像素，不读取游戏内存、不注入 DLL、不 Hook 游戏、不解析封包，也不模拟鼠标键盘操作。截图与 OCR 默认在本机处理；软件仅为获取物品价格访问配置中的第三方 API。

这不构成 Battlestate Games 或 BattlEye 的官方安全保证。使用者应自行确认当前游戏规则、服务器规则和赛事要求。

## 主要功能

- `Alt+Q`：识别鼠标附近或检查窗口中的单个物品。
- `Alt+W`：扫描仓库、容器及随身区域中的可见物品。
- 在 PvP / PvE 价格之间切换，并按模式维护本地缓存。
- 显示跳蚤市场价格、商人价格、扫描合计和最近扫描记录。
- 支持自定义快捷键、最低显示价格、主题和识别性能档位。

## 安装与运行

1. 从 [Releases](https://github.com/BYS-XSQ/LootPilot/releases) 下载最新的 `LootPilot-*-win-x64.zip`。
2. 完整解压压缩包，不要直接在压缩软件内运行。
3. 运行 `LootPilot.exe`。
4. 建议先使用默认快捷键，并在 1920×1080、无边框窗口或窗口化全屏下测试。

测试版目前未进行商业代码签名，Windows SmartScreen 或杀毒软件可能提示未知发布者。请只从本仓库 Release 下载，并核对 Release 中提供的 SHA-256；不要为了运行软件永久关闭安全软件。

## 隐私与联网

- 软件不会上传游戏截图或 OCR 图像。
- 默认访问 `api.eftarkov.com`，失败时回退到 `api.tarkov.dev` 获取价格数据。
- 用户设置、价格缓存和最近扫描记录保存在 `%LOCALAPPDATA%\LootPilot\`。
- `DebugSaveCaptures` 默认关闭；只有用户主动开启调试时才会在本地保存截图。
- 项目不包含账号系统、遥测或广告 SDK。

## 当前限制

- 主要基于 1920×1080 和 100% 游戏 UI 缩放标定，其他分辨率、比例与缩放仍需更多样本。
- 全背包扫描通常约 10 秒，具体取决于 CPU、画面和网络状态。
- 遮挡、小字体、压缩画面或复杂背景可能造成漏识别、错位或主动留空。
- 价格来自第三方服务，可能延迟、不可用或改变接口；本项目不保证实时性与准确性。
- 当前不是完整的图标分类器，也不会根据耐久、堆叠数量或武器改装状态修正所有价格。

## 从源码构建

需要 Windows 与 .NET 8 SDK：

```powershell
.\scripts\build.ps1
.\scripts\run.ps1
.\scripts\publish.ps1
```

`publish.ps1` 会在 `artifacts/` 下生成自包含的 `win-x64` 发布目录，并复制许可证和说明文件；最终用户无需另行安装 .NET 8 Desktop Runtime。

## 反馈

提交 Bug 时请附上 LootPilot 版本、Windows 版本、分辨率、显示缩放、游戏显示模式、复现步骤和错误信息。请勿上传包含账号、个人信息或其他敏感内容的完整截图。

- [报告问题](https://github.com/BYS-XSQ/LootPilot/issues)
- [贡献指南](CONTRIBUTING.md)
- [安全问题报告](SECURITY.md)

## 许可证与声明

源代码采用 [MIT License](LICENSE)。LootPilot 名称、Logo 与品牌视觉素材不包含在 MIT 授权中，未经许可不得用于暗示官方版本或合作关系。第三方组件与 OCR 模型遵循各自许可证，详见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。

LootPilot 是非官方社区项目，与 Battlestate Games、BattlEye、《Escape from Tarkov》及其关联方不存在隶属、背书或合作关系。相关名称和商标属于其各自权利人。
