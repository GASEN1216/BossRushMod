using System;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// COMPAT：把官方拾荒者 preset 改造成天空岛自己的敌人档次。
    ///
    /// 外观与身份都是**可失败的装饰**，任一失败都不影响这场战斗能不能打完（口径同战役终章 Boss）：
    /// 基础数值在创建角色前由 SkyIslandCombatPreset 按 Wiki ×1.5 准备；
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

        /// <summary>只作用于模型，不改碰撞体。具名剧情对手保持原体型。</summary>
        internal static float ModelScale(SkyIslandEnemyTier tier)
        {
            if (tier == SkyIslandEnemyTier.Storm) return 1.9f;
            if (tier == SkyIslandEnemyTier.Elite) return 1.18f;
            return 1f;
        }

        /// <summary>
        /// 官方 AI 的强制追踪距离（forceTracePlayerDistance）。布局 v2 把岛距从 40~250 米压到 30~50 米，
        /// 旧值 100~140 米会让敌人越过整座桥追进风铃集这类安全枢纽；按岛群压缩比例同步收紧，档次顺序不变。
        /// </summary>
        internal static float TraceDistance(SkyIslandEnemyTier tier)
        {
            if (tier == SkyIslandEnemyTier.Storm) return 100f;
            if (tier == SkyIslandEnemyTier.Scav) return 70f;
            return 85f;
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
        /// `Champion` 只接身份与 AI 追踪距离：折翎和钟守有自己的脸和名字，染色与放大会毁掉他们的辨识度。
        /// </summary>
        internal static void Apply(CharacterMainControl character, SkyIslandEnemyTier tier)
        {
            if (character == null || !MarkApplied(character)) return;
            bool decorate = tier != SkyIslandEnemyTier.Champion;
            // Scav 刻意**不**改名：它的 `showName` 一直是 false，「云沿拾荒者」这个名字玩家从来看不到，
            // 改 nameKey 却会把击杀记进 `Count/Kills/BossRush_SkyIsland_Enemy_Scav`，
            // 于是官方拾荒者击杀数与 `RequireEnemyKilled` 解锁都不推进。自动组改成按出击刷新之后
            // 这批击杀会反复产生，白打的代价更大。精英以上仍用自己的 key（模组内容本就该独立计数）。
            if (decorate && tier != SkyIslandEnemyTier.Scav)
                ApplyName(character, tier, NameKey(tier), NameCn(tier), NameEn(tier));
            if (tier == SkyIslandEnemyTier.Scav) return;
            if (!decorate) return;
            ApplyModelScale(character, tier);
            ApplyTint(character, tier);
        }

        /// <summary>
        /// 一次性标记挡住重复挂身份和外观。战斗数值已在克隆 preset 上准备，不在这里叠加。
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

        /// <summary>具名剧情对手：名字用角色自己的，数值已在生成前准备。</summary>
        internal static void ApplyStoryChampion(CharacterMainControl character, string id, string nameCn, string nameEn)
        {
            if (character == null || !MarkApplied(character)) return;
            ApplyName(character, SkyIslandEnemyTier.Champion, "BossRush_SkyIsland_Foe_" + id, nameCn, nameEn);
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

        /// <summary>
        /// 档次染色的强度：只往档次色靠 35%（风暴档约 (0.78,0.87,0.90)）。
        /// 旧写法把档次色直接当乘色，风暴档红通道只剩 36%，暖色的鸭子和装备整只变成暗青灰，像没打光的占位模型（VB-26）。
        /// </summary>
        internal const float TintStrength = 0.35f;

        /// <summary>乘到模型材质上的颜色：白色往档次色靠 <see cref="TintStrength"/>。<see cref="Tint"/> 本身仍是档次的识别色（噬风的预警圈用它）。</summary>
        internal static Color ModelTint(SkyIslandEnemyTier tier)
        {
            return Color.Lerp(Color.white, Tint(tier), TintStrength);
        }

        private static void ApplyTint(CharacterMainControl character, SkyIslandEnemyTier tier)
        {
            try
            {
                if (colorBlock == null) colorBlock = new MaterialPropertyBlock();
                Color tint = ModelTint(tier);
                // 只染角色模型下的网格（身体与挂点上的装备模型）。粒子、拖尾、线（枪口火、弹道拖尾、特效）不染：
                // 它们的 _TintColor 被乘成青灰就是一团脏色。
                Transform model = character.characterModel != null ? character.characterModel.transform : character.transform;
                Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < renderers.Length; i++)
                {
                    Renderer renderer = renderers[i];
                    if (renderer == null || !(renderer is SkinnedMeshRenderer || renderer is MeshRenderer)) continue;
                    renderer.GetPropertyBlock(colorBlock);
                    Material shared = renderer.sharedMaterial;
                    if (shared != null && shared.HasProperty(ColorProperty)) colorBlock.SetColor(ColorProperty, tint);
                    else if (shared != null && shared.HasProperty(TintColorProperty)) colorBlock.SetColor(TintColorProperty, tint);
                    else colorBlock.SetColor(BaseColorProperty, tint);
                    renderer.SetPropertyBlock(colorBlock);
                }
            }
            catch (Exception e) { Debug.LogWarning("[SkyIslandEnemy] 染色失败：" + e.Message); }
            ApplyTierRing(character, tier);
        }

        /// <summary>
        /// 档次的脚下标识（VB-26）：一圈贴地细环，档次色、0.45 不透明，半径随模型放大。挂在角色身上跟着走，
        /// 建一次、没有每帧成本；角色销毁时一起没了。染色压淡之后，玩家靠它一眼分出精英 / 风暴。
        /// </summary>
        private static void ApplyTierRing(CharacterMainControl character, SkyIslandEnemyTier tier)
        {
            try
            {
                Color tint = Tint(tier);
                LineRenderer ring = SkyIslandGroundRing.Create(character.transform, Vector3.up * SkyIslandGroundRing.GroundLift);
                ring.gameObject.name = "SkyIslandTierRing";
                SkyIslandGroundRing.SetShape(ring, 0.7f * ModelScale(tier), 0.12f, new Color(tint.r, tint.g, tint.b, 0.45f));
            }
            catch (Exception e) { Debug.LogWarning("[SkyIslandEnemy] 档次脚环失败：" + e.Message); }
        }

        /// <summary>
        /// AI 追踪距离保持天空岛布局口径；反应与射击延迟由生成前的基准统一设置。
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
