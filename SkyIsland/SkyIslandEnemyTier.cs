namespace BossRush
{
    /// <summary>
    /// 天空岛敌人档次。内容表（`SkyIslandContent`）与运行时装饰（`SkyIslandEnemyTiers`）共用同一套身份，
    /// 因此单独放在这个**无依赖**文件里：隔离回归可以只链接它，不必把 Unity 与物品系统一起拖进来。
    ///
    /// 顺序即强度递增，但内容表按**字符串名**解析而不是序号，将来插档不会让旧表悄悄改语义。
    /// </summary>
    internal enum SkyIslandEnemyTier
    {
        /// <summary>云沿拾荒者：官方普通拾荒者原样，构成全图基础密度。</summary>
        Scav = 0,
        /// <summary>断风游猎：航标与瞭台的守卫，血厚、反应快，一眼能认出来。</summary>
        Elite = 1,
        /// <summary>折翎与钟守这类具名剧情对手：数值加强，但保留自己的脸与名字。</summary>
        Champion = 2,
        /// <summary>噬风：风灾留下的那一股，全图唯一的 Boss。</summary>
        Storm = 3,
        /// <summary>
        /// 头目：小环境里一招一式的守卫带队者，身上穿一件专属装备（`SkyIslandBossRules`）。
        /// 这两档追加在末尾，序号不代表强度——强度看档案按护甲折算的有效血量（内容表按字符串名解析，不认序号）。
        /// </summary>
        Chief = 4,
        /// <summary>岛主：守着自己那座岛的多阶段 Boss，每趟都在，穿全套专属装备（`SkyIslandBossRules`）。</summary>
        Lord = 5
    }
}
