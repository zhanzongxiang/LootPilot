# Contributing

感谢你关注 LootPilot。提交改动前请先通过 Issue 描述问题、使用场景和预期结果，较大的功能请等待维护者确认方向。

## 开发要求

- Windows 10/11 与 .NET 8 SDK。
- 不得引入读取游戏内存、进程注入、封包解析、输入自动化或规避反作弊的实现。
- 不要提交真实用户截图、账号信息、缓存、日志、构建产物或凭据。
- 新依赖必须说明用途和许可证；只有允许再分发的模型与素材才能进入仓库。

## 提交检查

```powershell
dotnet restore .\TarkovPriceOverlay.sln
dotnet build .\TarkovPriceOverlay.sln --configuration Release --no-restore
```

Pull Request 应包含变更目的、测试环境、验证步骤以及对性能、隐私与兼容性的影响。
