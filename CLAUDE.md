# CLAUDE.md

本仓库的**权威协作约定见 [`AGENTS.md`](./AGENTS.md)**——构建门禁、TypeID 分配、本地化注入、刷怪安全网、事件生命周期、提交规范、验证方法、高风险边界全部在那里维护。本文件只是指针，**不重复约定**（避免多处分叉后读到过期版本）。

- 深入架构：`docs/架构说明/`（游戏模式状态机、刷怪与恢复、Harmony 契约稳定性、Config 归位、Hooks 分层）
- 修复流水账：`docs/协作/FIX_TRACKER.md`
- 代码审查方法与发现：`docs/代码审查/CODE_REVIEW.md`、`docs/代码审查/CODE_REVIEW_FINDINGS.md`
- TypeID 分配表：`docs/Bossrush使用物品ID表.md`

## 三条最易踩坑的红线

1. **新增 `.cs` 文件必须手动加入 `compile_official.bat`**（无通配符，483 条显式列表）。漏了就静默不编译、功能残缺、不报错。自检：`grep -n "你的文件.cs" compile_official.bat`。
2. **WSL/Linux 不能编译本 Mod**（需 Windows .NET SDK + 游戏 DLL）。禁止仅凭 Linux 侧阅读就断言"已验证"。编译走 `cmd.exe /c "... && compile_official.bat"`。
3. **提交信息用中文简短摘要**（如 `修复售货机 UI 崩溃`），禁止英文/conventional-commit。仅在用户明确要求时才提交。

> 验证方式：Windows 编译 + `python3 tests/*.py`（358 个守卫）+ 游戏内 smoke 测试。无 C# 单测框架，运行时行为需人工进游戏验证。详见 [`AGENTS.md`](./AGENTS.md) §6。
