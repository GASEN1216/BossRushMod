# GitHub Copilot Instructions — BossRushMod

本仓库的权威协作约定见根目录 **[`AGENTS.md`](../AGENTS.md)**。本文件只是指针，实质约定不在此重复。

生成或修改代码时，请遵守以下硬约束（完整版见 AGENTS.md）：

- **新增 `.cs` 文件必须手动加入 `compile_official.bat`**（无通配符，483 条显式列表），否则不会被编译进 `Build/BossRush.dll`。
- **WSL/Linux 无法编译本 Mod**，编译需 Windows .NET SDK + 游戏 DLL（`Duckov_Data\Managed`）。
- **提交信息用中文简短摘要**（如 `修复售货机 UI 崩溃`），禁止英文 conventional-commit 格式。
- **TypeID 在 5000xx 区间严格递增、不复用**，发版前登记 `docs/Bossrush使用物品ID表.md`。
- **`DisplayNameRaw = "BossRush_<Name>"` 的物品必须注入对应本地化 key**，否则游戏内显示 `*BossRush_<Name>*`。
- **`CreateCharacter` 后若队伍非玩家敌对必须 `SetTeam(Teams.wolf)`**，否则 Boss 无法击杀、卡波次。
- **静态/全局事件订阅必须幂等 + 在销毁时退订**，防跨局泄漏与递归。
- **代码库中 ~807 个空 catch 是有意的宿主崩溃保险，不得成批清除。**

代码风格：4 空格缩进，Allman 大括号，PascalCase 公有 / camelCase 局部，命名空间 `BossRush`。

验证：Windows 编译 + `python3 tests/*.py`（358 守卫）+ 游戏内 smoke。无 C# 单测框架，运行时行为需人工验证。
