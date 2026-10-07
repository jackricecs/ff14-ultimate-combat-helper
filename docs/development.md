# 开发指南

## 依赖
| 依赖 | 版本（2026-10） | 获取 |
|---|---|---|
| ACT | 3.8.5.288 | `tools/fetch-deps.*` 自动下载到 `thirdparty/ACT` |
| 国服解析插件 SDK | FFXIV_ACT_Plugin_CN 仓库 `FFXIV_ACT_Plugin_SDK_3.0.2.7.zip`（无强命名） | 同上 → `thirdparty/sdk-cn/SDK` |
| 国际服解析插件 SDK | ravahn/FFXIV_ACT_Plugin `Releases/FFXIV_ACT_Plugin_SDK_3.0.3.1.zip`（有强命名） | 同上 → `thirdparty/sdk-global/SDK` |
| .NET | Windows：.NET SDK 6+（构建 net48）或 VS2022；Linux：Mono `mcs` + `mono` | |

升级依赖版本：改 `tools/fetch-deps.ps1` 与 `tools/fetch-deps.sh` 顶部的版本变量，并更新本表。

## Windows
```powershell
./tools/fetch-deps.ps1
dotnet build tests/UCH.Core.Tests -c Release; ./tests/UCH.Core.Tests/bin/Release/UCH.Core.Tests.exe
dotnet build src/UCH.Plugin -c Release -p:SdkVariant=cn       # → artifacts/cn/UltimateCombatHelper.dll
dotnet build src/UCH.Plugin -c Release -p:SdkVariant=global   # → artifacts/global/
```
调试：用 VS 打开 `UltimateCombatHelper.sln`，`UCH.Plugin` 设为启动项目（会启动 `thirdparty/ACT` 里的 ACT），或附加到你自己的 ACT 进程。也可以 `-p:ActDir=D:\ACT` 指向自己的 ACT。

## Linux（无 Windows 时的开发与验证）
```bash
sudo apt install mono-mcs mono-runtime libmono-system-runtime-serialization4.0-cil && pip install dnfile
bash tools/build-linux.sh
```
`build-linux.sh` 会：下载依赖 → 用**真正的 .NET Framework 4.7 参考程序集**编译两个变体 → `audit_netfx_refs.py` 检查产物引用的每个 BCL 成员都存在于 .NET Framework → 运行单元测试。

> ⚠ 不要直接用 Mono 默认库 `mcs` 编译插件：Mono 的 mscorlib 含 .NET Core 才有的重载，编译能过但 ACT 加载时报 `MissingMethodException`（v0.1.0 踩过）。

## 发布
1. 更新 `CHANGELOG.md` 与 `src/UCH.Plugin/UchPlugin.cs` 中的 `AssemblyVersion`。
2. `git tag v0.2.0 && git push --tags` → GitHub Actions 构建并创建 Release（附 `-cn.zip` / `-global.zip`）。

## 新副本上线后
```bash
python3 tools/gen_zone_table.py      # 自动浅克隆 cactbot 并重新生成 ZoneTable.g.cs
```
提交并发补丁版本。

## 调试技巧
- 「总览」页显示角色 ID、当前副本与难度、场上敌人最大 HP 与是否"主要"。
- 想离线复现：ACT 可导入日志，但插件对 `isImport` 的日志不处理（避免误报）。写测试时直接把 Network 日志里的行喂给 `Rig.Feed()`。
- `tools/dump_dotnet_api.py <dll>` 可导出 ACT / SDK 的公开 API，解析插件升级后用来比对接口变化。
