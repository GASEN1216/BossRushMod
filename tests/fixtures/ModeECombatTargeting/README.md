# Mode E 技能目标与猎犬友军回归

通过 `python tools/run_runtime_regressions.py --filter ModeECombatTargeting` 执行。逐次抽取龙裔生产目标解析、友方判定及 Mode E 生成后猎犬配置块，直接链接只读反编译 `Team.cs` / `Teams.cs`。覆盖友军技能攻击敌人、误指主人拒绝、敌方正常锁玩家、无目标、技能暂停目标、Mode H 看台排除、普通模式保留，以及猎犬对友军 / 敌军 / Mode F 的边界。

角色、生命和 AI 引用是显式装配的替身，不替代 Unity 行为树或第三方 Mod 的实机兼容验证。无玩家存档访问。

独立复审新增原样提取 `OnCollisionWithPlayer`。当友军龙裔已经锁定敌军时，同营玩家碰撞仍须在音效、击退、伤害和冷却写入之前拒绝；随后同一时间戳的敌对碰撞正常执行，Mode H 观众始终排除。音效、击退和伤害落点以计数替身记录，仅证明生产入口是否错误派发。隔离副本恢复旧的 `IsPlayerAlly()` 碰撞判据会实际转红，副本按字节还原并核对 SHA-256。
