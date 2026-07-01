# GEMINI.md

本项目的协作约定统一维护在 **[AGENTS.md](AGENTS.md)**——构建门禁、TypeID 分配、本地化注入、刷怪安全网、事件生命周期、提交规范、验证方法与高风险边界都在那里。本文件只是指针，实质约定不在此重复（避免多处分叉）。

## 三条红线

1. **新增 `.cs` 必须手动加入 `compile_official.bat`**（无通配符，483 条显式列表），否则静默不编译。
2. **WSL/Linux 不能编译**（需 Windows .NET SDK + 游戏 DLL）；禁止仅凭 Linux 侧阅读断言"已验证"。
3. **提交信息用中文简短摘要**（如 `护士治疗更加全面`），禁止英文/conventional-commit；仅用户明确要求时才提交。

## 导航

- 深入架构：[docs/架构说明/](docs/架构说明/)
- 修复流水账：[docs/协作/FIX_TRACKER.md](docs/协作/FIX_TRACKER.md)
- 代码审查：[docs/代码审查/](docs/代码审查/)
- TypeID 表：[docs/Bossrush使用物品ID表.md](docs/Bossrush使用物品ID表.md)

> 验证：Windows 编译 + `python3 tests/*.py`（358 守卫）+ 游戏内 smoke。无 C# 单测框架。详见 [AGENTS.md](AGENTS.md)。
