using System;
using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// COMPAT：把官方拾荒者 preset 改造成天空岛自己的敌人档次。
    ///
    /// 三层都是**可失败的装饰**，任一失败都不影响这场战斗能不能打完（口径同战役终章 Boss）：
    /// 1. 数值：只改 `CharacterItem` 上的 stat，伤害倍率封顶 3；
    /// 2. 外观：只缩放 `characterModel`，**不动角色 transform**——碰撞体与导航半径保持官方口径，
    ///    体型变化不会带来物理/寻路成本或穿模；
    /// 3. 染色：走 `MaterialPropertyBlock`，绝不碰 `sharedMaterial`（会污染同款所有敌人）。
    /// </summary>
    internal static class SkyIslandEnemyTiers
    {
        private static readonly int ColorProperty = Shader.PropertyToID("_Color");
        private static readonly int TintColorProperty = Shader.PropertyToID("_TintColor");
        private static readonly int BaseColorProperty = Shader.PropertyToID("_BaseColor");
        private static MaterialPropertyBlock colorBlock;

        internal static float HealthMultiplier(SkyIslandEnemyTier tier)
        {
            if (tier == SkyIslandEnemyTier.Storm) return 18f;
            if (tier == SkyIslandEnemyTier.Champion) return 2.2f;
            if (tier == SkyIslandEnemyTier.Elite) return 2.6f;
            return 1f;
        }

        /// <summary>伤害倍率与官方 Boss 口径一致，封顶 3；这里刻意留在 2 以内，靠血量和相位制造压力。</summary>
        internal static float DamageMultiplier(SkyIslandEnemyTier tier)
        {
            if (tier == SkyIslandEnemyTier.Storm) return 1.8f;
            if (tier == SkyIslandEnemyTier.Champion) return 1.3f;
            if (tier == SkyIslandEnemyTier.Elite) return 1.35f;
            return 1f;
        }

        /// <summary>只作用于模型，不改碰撞体。具名剧情对手保持原体型。</summary>
        internal static float ModelScale(SkyIslandEnemyTier tier)
        {
            if (tier == SkyIslandEnemyTier.Storm) return 1.9f;
            if (tier == SkyIslandEnemyTier.Elite) return 1.18f;
            return 1f;
        }

        /// <summary>反应速度只给精英与 Boss 提升，普通敌人保持官方手感。</summary>
        internal static float ReactionSpeedup(SkyIslandEnemyTier tier)
        {
            if (tier == SkyIslandEnemyTier.Storm) return 1.7f;
            if (tier == SkyIslandEnemyTier.Champion) return 1.25f;
            if (tier == SkyIslandEnemyTier.Elite) return 1.3f;
            return 1f;
        }

        internal static float TraceDistance(SkyIslandEnemyTier tier)
        {
            if (tier == SkyIslandEnemyTier.Storm) return 140f;
            if (tier == SkyIslandEnemyTier.Scav) return 100f;
            return 120f;
        }

        internal static Color Tint(SkyIslandEnemyTier tier)
        {
            if (tier == SkyIslandEnemyTier.Storm) return new Color(0.36f, 0.62f, 0.72f, 1f);
            return new Color(0.62f, 0.70f, 0.78f, 1f);
        }

        internal static string NameCn(SkyIslandEnemyTier tier)
        {
            if (tier == SkyIslandEnemyTier.Storm) return "噬风";
            if (tier == SkyIslandEnemyTier.Elite) return "断风游猎";
            return "云沿拾荒者";
        }

        internal static string NameEn(SkyIslandEnemyTier tier)
        {
            if (tier == SkyIslandEnemyTier.Storm) return "Windeater";
            if (tier == SkyIslandEnemyTier.Elite) return "Galebreaker Ranger";
            return "Cloudedge Scavenger";
        }

        internal static string NameKey(SkyIslandEnemyTier tier)
        {
            return "BossRush_SkyIsland_Enemy_" + tier;
        }

        /// <summary>
        /// 把档次应用到已创建的角色。preset 已由生成流程克隆过，改它不会污染官方角色池。
        /// `Champion` 只吃数值与 AI：折翎和钟守有自己的脸和名字，染色与放大会毁掉他们的辨识度。
        /// </summary>
        internal static void Apply(CharacterMainControl character, SkyIslandEnemyTier tier)
        {
            if (character == null || !MarkApplied(character)) return;
            bool decorate = tier != SkyIslandEnemyTier.Champion;
            if (decorate) ApplyName(character, tier, NameKey(tier), NameCn(tier), NameEn(tier));
            if (tier == SkyIslandEnemyTier.Scav) return;
            ApplyStats(character, tier);
            if (!decorate) return;
            ApplyModelScale(character, tier);
            ApplyTint(character, tier);
        }

        /// <summary>
        /// 数值层是**乘法**（血量 `*=`、反应时间 `/=`），重复施加会复利：
        /// 噬风的 18 倍血跑两遍就是 324 倍。用角色身上的一次性标记挡住重入，
        /// 将来补位重生 / 重新绑定档次时也不会悄悄叠上去。
        /// </summary>
        private static bool MarkApplied(CharacterMainControl character)
        {
            if (character.GetComponent<SkyIslandEnemyTierMark>() != null)
            {
                Debug.LogWarning("[SkyIslandEnemy] 档次已施加过，跳过重复应用：" + character.name);
                return false;
            }
            character.gameObject.AddComponent<SkyIslandEnemyTierMark>();
            return true;
        }

        /// <summary>具名剧情对手：数值走 `Champion`，名字用角色自己的。</summary>
        internal static void ApplyStoryChampion(CharacterMainControl character, string id, string nameCn, string nameEn)
        {
            if (character == null || !MarkApplied(character)) return;
            ApplyName(character, SkyIslandEnemyTier.Champion, "BossRush_SkyIsland_Foe_" + id, nameCn, nameEn);
            ApplyStats(character, SkyIslandEnemyTier.Champion);
        }

        /// <summary>
        /// 改的是**本次生成克隆出来的 preset**（`SkyIslandEncounters` 里 Instantiate 过），
        /// 不会污染官方角色池。
        ///
        /// 两个必须记住的连带影响：
        /// 1. 血条名由 `HealthBar` 的 `if (!characterPreset.showName) return;` 门控，
        ///    普通拾荒者 preset 上它是 false——只改 `nameKey` 血条上根本不显示。
        ///    因此精英与 Boss 这两档要显式打开；`Scav` 保持匿名，否则满图都是名字牌。
        /// 2. `CharacterMainControl` 在主角击杀时会 `SavesCounter.AddKillCount(nameKey)`，
        ///    那是写进正式存档的 `Count/Kills/<key>`。改名意味着天空岛击杀记在自己的键下，
        ///    **不再计入**官方拾荒者击杀数，`RequireEnemyKilled` 解锁与击杀类任务也不会推进。
        ///    这是有意的（天空岛是 Mod 内容），但属于存档面，改 key 前先想清楚。
        /// </summary>
        private static void ApplyName(CharacterMainControl character, SkyIslandEnemyTier tier,
            string key, string nameCn, string nameEn)
        {
            try
            {
                LocalizationHelper.InjectLocalization(key, L10n.T(nameCn, nameEn));
                if (character.characterPreset == null) return;
                character.characterPreset.nameKey = key;
                if (tier != SkyIslandEnemyTier.Scav) character.characterPreset.showName = true;
            }
            catch (Exception e) { Debug.LogWarning("[SkyIslandEnemy] 变体名失败：" + e.Message); }
        }

        private static void ApplyStats(CharacterMainControl character, SkyIslandEnemyTier tier)
        {
            try
            {
                Item item = character.CharacterItem;
                if (item == null) return;
                float health = HealthMultiplier(tier);
                Stat maxHealth = item.GetStat("MaxHealth".GetHashCode());
                if (maxHealth != null) maxHealth.BaseValue *= health;
                // 先改上限再同步当前血量，否则角色以旧血量出场。
                if (character.Health != null) character.Health.SetHealth(character.Health.MaxHealth);
                float damage = Mathf.Min(DamageMultiplier(tier), 3f);
                Stat gun = item.GetStat("GunDamageMultiplier".GetHashCode());
                if (gun != null) gun.BaseValue *= damage;
                Stat melee = item.GetStat("MeleeDamageMultiplier".GetHashCode());
                if (melee != null) melee.BaseValue *= damage;
            }
            catch (Exception e) { Debug.LogWarning("[SkyIslandEnemy] 数值倍率失败：" + e.Message); }
        }

        private static void ApplyModelScale(CharacterMainControl character, SkyIslandEnemyTier tier)
        {
            try
            {
                float scale = ModelScale(tier);
                if (Mathf.Approximately(scale, 1f) || character.characterModel == null) return;
                // 只放大模型：角色 transform 不动，碰撞体、导航半径与官方一致。
                character.characterModel.transform.localScale = Vector3.one * scale;
            }
            catch (Exception e) { Debug.LogWarning("[SkyIslandEnemy] 体型缩放失败：" + e.Message); }
        }

        private static void ApplyTint(CharacterMainControl character, SkyIslandEnemyTier tier)
        {
            try
            {
                if (colorBlock == null) colorBlock = new MaterialPropertyBlock();
                Color tint = Tint(tier);
                Renderer[] renderers = character.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < renderers.Length; i++)
                {
                    Renderer renderer = renderers[i];
                    if (renderer == null) continue;
                    renderer.GetPropertyBlock(colorBlock);
                    Material shared = renderer.sharedMaterial;
                    if (shared != null && shared.HasProperty(ColorProperty)) colorBlock.SetColor(ColorProperty, tint);
                    else if (shared != null && shared.HasProperty(TintColorProperty)) colorBlock.SetColor(TintColorProperty, tint);
                    else colorBlock.SetColor(BaseColorProperty, tint);
                    renderer.SetPropertyBlock(colorBlock);
                }
            }
            catch (Exception e) { Debug.LogWarning("[SkyIslandEnemy] 染色失败：" + e.Message); }
        }

        /// <summary>
        /// AI 参数：反应时间越短反应越快，因此用除法；和官方 Boss 倍率写法一致。
        ///
        /// 用**独立**的标记组件，不能和 <see cref="SkyIslandEnemyTierMark"/> 共用：
        /// `AICharacterController` 常常就挂在角色本体上，共用一个标记会让先跑的
        /// `ApplyAi` 把后跑的 `Apply` 一起挡掉。
        /// </summary>
        internal static void ApplyAi(AICharacterController ai, SkyIslandEnemyTier tier)
        {
            if (ai == null) return;
            if (ai.GetComponent<SkyIslandEnemyAiMark>() != null)
            {
                Debug.LogWarning("[SkyIslandEnemy] AI 调参已施加过，跳过重复应用：" + ai.name);
                return;
            }
            ai.gameObject.AddComponent<SkyIslandEnemyAiMark>();
            try
            {
                ai.forceTracePlayerDistance = TraceDistance(tier);
                float speedup = ReactionSpeedup(tier);
                if (Mathf.Approximately(speedup, 1f)) return;
                ai.baseReactionTime /= speedup;
                ai.reactionTime /= speedup;
                ai.shootDelay /= speedup;
            }
            catch (Exception e) { Debug.LogWarning("[SkyIslandEnemy] AI 调参失败：" + e.Message); }
        }

        internal static void ResetStaticCaches() { colorBlock = null; }
    }

    /// <summary>档次（数值 / 外观 / 名字）已施加的一次性标记。随角色对象一起销毁，不需要额外清理。</summary>
    internal sealed class SkyIslandEnemyTierMark : MonoBehaviour { }

    /// <summary>AI 调参已施加的一次性标记。与档次标记分开，因为两者可能落在同一个 GameObject 上。</summary>
    internal sealed class SkyIslandEnemyAiMark : MonoBehaviour { }
}
