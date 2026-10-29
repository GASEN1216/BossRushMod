# PetNestLineageNames

运行：`python tools/run_runtime_regressions.py --filter PetNestLineageNames`。

直接编译生产 `PetNestLineageCatalog.cs`，逐字提取生产 `PetNestService.GetPetDisplayName` 及官方 `CharacterRandomPreset.DisplayName`。覆盖目录早于语言表、同一条目切换中英文、当前 Boss 筛选器排除已有崽、目录尚未构建、玩家昵称、可用显示名回落与缺词条占位串。名字解析不得修改身份或昵称。

宿主替身仅提供 Boss 池、preset 查找与本地化字典；缺词条和异常回落与已核实的官方 API / `LocalizationHelper` 一致。不会启动游戏或读写玩家存档，不证明实机字体与 HUD 刷新时机。
