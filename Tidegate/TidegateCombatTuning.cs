namespace BossRush
{
    /// <summary>
    /// COMPAT：潮闸镇普通敌人的战斗强度（owner 2026-11-08 发布前「提升敌人整体强度」）。
    /// 只按倍率改本次刷怪的克隆 preset，交给官方 CreateCharacterAsync 装配 Stat 与 AI；不换武器、不改护甲和掉落，
    /// 所以尸体可搜到的东西和整图经济不变。人数也不加（码头 / 老镇帧时间刚压下来，§4.12）。
    /// 沙老大有自己的固定数值（<see cref="TidegateSandBoss.Tune"/>），这里跳过。
    /// 水厂狙击手只加血：它站在玩家看不清的远处开火，伤害上限、反应与开火前摇维持 <c>TuneMarksman</c> 的公平下限。
    /// 不碰 aiCombatFactor（只管 NPC 互打的伤害比），拾荒者、守卫与猎犬之间的互打比例照旧。
    /// 克隆由 Spawn 每次从官方模板新建，每个克隆只调用一次，倍率不会复利。
    /// </summary>
    internal static class TidegateCombatTuning
    {
        /// <summary>镇街、渔港、芦苇、岬角拾荒者与淘沙帮的生命倍率。</summary>
        internal const float HealthFactor = 1.5f;
        /// <summary>水厂 / 灯塔守卫与突击队长：小队里最难啃的那一个。</summary>
        internal const float GuardHealthFactor = 1.8f;
        /// <summary>枪械与近战伤害倍率（官方工厂直接读 meleeDamageMultiplier，同天空岛 SkyIslandCombatPreset）。</summary>
        internal const float DamageFactor = 1.25f;
        /// <summary>反应时间与开火前摇除以它：发现玩家后更快还手。</summary>
        internal const float ReflexFactor = 1.3f;
        /// <summary>散布除以它：中距离更准，近距离差别不大。</summary>
        internal const float AccuracyFactor = 1.2f;

        internal static void Apply(CharacterRandomPreset clone, string kind, TidegateEnemyRole role)
        {
            if (clone == null || kind == "boss") return;
            bool sturdy = kind == "guard" || role == TidegateEnemyRole.Captain;
            clone.health *= sturdy ? GuardHealthFactor : HealthFactor;
            if (role == TidegateEnemyRole.Marksman) return;
            clone.damageMultiplier *= DamageFactor;
            clone.setMeleeDamageMultiplier = true;
            clone.meleeDamageMultiplier *= DamageFactor;
            clone.reactionTime /= ReflexFactor;
            clone.shootDelay /= ReflexFactor;
            clone.gunScatterMultiplier /= AccuracyFactor;
        }
    }
}
