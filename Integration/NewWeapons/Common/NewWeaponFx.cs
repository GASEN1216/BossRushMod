// ============================================================================
// NewWeaponFx.cs - 五把新武器的表现层门面（配色 / 音效路径 / 爆发环 / 电弧）
// ============================================================================
// 模块说明：
//   五把武器的运行时机制此前完全没有视听反馈——毒素叠满爆发、雷电蓄满释放、
//   法杖召唤、能量盾正面吸收这四个关键瞬间屏幕上什么都不会发生。本文件补齐这一层。
//
//   表现层独立成本类（不往 ModBehaviour partial 上加）；2026-09-23 起冰霜 / 雷霆套装的
//   SpawnSetBurst 也转调这里的爆发环，两边只维护一份实现。
//
//   配色取自各武器描述文案里的 color 标签（色相一致）；挥砍拖尾用的几种色 2026-09-23 按原版画风
//   降了饱和（VA-09：#7CFC00 霓虹草绿再进 HDR 是最典型的廉价色），爆发环同用这一套。
//
// 生命周期（AGENTS.md 4.12）：
//   电弧池惰性创建（只有雷电戒指真的释放过才会建），cleanup 时销毁；
//   爆发环是一次性对象，自行淡出后销毁，不进池也不常驻。
//   所有入口都要求调用方已经确认「主玩家实际手持/佩戴对应装备」，本类不自行扫描。
// ============================================================================

using System;
using System.IO;
using System.Reflection;
using UnityEngine;
using BossRush.Common.Effects;

namespace BossRush
{
    /// <summary>
    /// 五把新武器的音效文件名。缺文件时 PlaySoundEffect 静默返回，不影响玩法。
    ///
    /// 只存文件名、不在静态字段里拼绝对路径：`Assembly.Location` 对从字节数组加载的程序集返回空串，
    /// `Path.GetDirectoryName("")` 会抛 ArgumentException，而静态字段初始化器里抛出会变成
    /// TypeInitializationException 把整个类型永久毒化。更糟的是 `NewWeaponSfx.VenomBurst` 是在
    /// **调用方的栈帧**里求值的，异常会跳过紧随其后的 ShowXxxBubble()。
    /// 路径统一走带兜底的 ModBehaviour.GetModPath()（与 BossRushAudioManager 同款）。
    /// </summary>
    internal static class NewWeaponSfx
    {
        /// <summary>毒蛇匕首满 5 层「毒性爆发」</summary>
        public const string VenomBurst = "venom_burst.wav";
        /// <summary>召唤法杖「灵魂召唤」</summary>
        public const string SoulSummon = "soul_summon.wav";
        /// <summary>能量盾正面吸收成功</summary>
        public const string ShieldAbsorb = "shield_absorb.wav";
        /// <summary>雷电戒指满层释放</summary>
        public const string ThunderRelease = "thunder_release.wav";

        /// <summary>解析音效完整路径。mod 根目录取不到时返回 null，调用方静默跳过。</summary>
        public static string ResolvePath(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return null;

            string modPath = ModBehaviour.GetModPath();
            if (string.IsNullOrEmpty(modPath)) return null;

            // 与 SetBonusSfx 同款多参 Path.Combine，分隔符交给它处理，不手工拼
            return Path.Combine(modPath, "Assets", "Sounds", "NewWeapons", fileName);
        }
    }

    /// <summary>五把新武器的配色。取自各自描述文案里的 color 标签。</summary>
    internal static class NewWeaponPalette
    {
        // 毒蛇匕首：描述色 #7CFC00 / #ADFF2F 的色相，饱和度压到原版画风（霓虹草绿 → 毒液黄绿）
        public static readonly Color VenomCore = new Color(0.62f, 0.90f, 0.35f, 0.85f);
        public static readonly Color VenomFade = new Color(0.35f, 0.60f, 0.18f, 0.35f);

        // 冰霜长矛：#4FC3F7 / #81D4FA 同色相，饱和度 -20%
        public static readonly Color FrostCore = new Color(0.44f, 0.80f, 0.97f, 0.85f);
        public static readonly Color FrostFade = new Color(0.60f, 0.86f, 0.98f, 0.35f);

        // 召唤法杖：#BA68C8 / #CE93D8 同色相，饱和度 -20%
        public static readonly Color SoulCore = new Color(0.74f, 0.48f, 0.78f, 0.85f);
        public static readonly Color SoulFade = new Color(0.82f, 0.63f, 0.85f, 0.35f);

        // 能量盾 #64B5F6 / #90CAF9
        public static readonly Color ShieldCore = new Color(0.392f, 0.710f, 0.965f, 0.85f);

        // 雷电戒指 #FFD54F / #FFECB3
        public static readonly Color ThunderCore = new Color(1f, 0.835f, 0.310f, 0.9f);
    }

    /// <summary>新武器表现层入口。所有方法自身吞异常，特效失败不影响玩法结算。</summary>
    internal static class NewWeaponFx
    {
        private static SetBonusArcPool arcPool;

        // ========== 爆发环 ==========

        /// <summary>
        /// 在 position 处生成一圈扩散并淡出的光环。radius 为最终半径，life 为存活秒数。
        /// shardCount &gt; 0 时额外抛出等角分布的放射碎片。
        /// </summary>
        /// <param name="withLight">
        /// 是否附带实时点光。稀有事件（毒爆发 / 雷电释放 / 召唤）开着更好看；
        /// 每次命中都会触发的表现（冰霜长矛霜环）必须关掉——一次命中一盏动态光不是免费的。
        /// </param>
        public static void PlayBurst(
            Vector3 position, Color color, float radius, float life, int shardCount, bool withLight = true)
        {
            try
            {
                NewWeaponBurstFx.Play(position, color, radius, life, shardCount, withLight);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[NewWeaponFx] 爆发环生成失败: " + e.Message);
            }
        }

        // ========== 电弧 ==========

        /// <summary>从 from 到 to 画一道折线电弧。池惰性创建，只有真的释放过才会存在。</summary>
        public static void PlayArc(Vector3 from, Vector3 to, Color color, float width, float life)
        {
            try
            {
                SetBonusArcPool pool = EnsureArcPool();
                if (pool == null) return;
                pool.Spawn(from, to, color, width, life);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[NewWeaponFx] 电弧生成失败: " + e.Message);
            }
        }

        private static SetBonusArcPool EnsureArcPool()
        {
            if (arcPool != null) return arcPool;
            try
            {
                // 不 DontDestroyOnLoad：SetBonusArcPool 的契约是「激活时惰性创建、停用即销毁」，
                // 让它跟随场景销毁最贴近该契约。过图后缓存引用在 Unity 里 == null 为真，下次会重建。
                arcPool = SetBonusArcPool.Create();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[NewWeaponFx] 创建电弧池失败: " + e.Message);
                arcPool = null;
            }
            return arcPool;
        }

        // ========== 音效 ==========

        /// <summary>
        /// 播放一条新武器音效，参数是 NewWeaponSfx 里的文件名常量。
        /// 路径解析失败或文件不存在时静默返回，绝不向调用方抛出——
        /// 调用点后面通常还跟着气泡提示等表现，不能被音效拖垮。
        /// </summary>
        public static void PlaySound(string fileName)
        {
            try
            {
                string path = NewWeaponSfx.ResolvePath(fileName);
                if (string.IsNullOrEmpty(path)) return;

                ModBehaviour inst = ModBehaviour.Instance;
                if (inst != null)
                {
                    inst.PlaySoundEffect(path);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[NewWeaponFx] 播放音效失败: " + e.Message);
            }
        }

        // ========== 状态气泡 ==========

        /// <summary>
        /// 在角色头顶弹一条状态气泡（官方 DialogueBubbles）。
        /// 三处调用点此前各写一份逐字相同的 try/catch 包装，现收敛到这里。
        ///
        /// 使用纪律：气泡是**状态提示**，不是命中反馈。只给「玩家需要据此改变操作」的瞬间用
        /// （例：雷能攒满，可以换把大伤害武器再开枪）。命中 / 爆发 / 释放这类一次性结果
        /// 靠爆发环、电弧与音效表达——它们不会糊在角色头顶，也不会和 NPC 对白抢位置。
        /// </summary>
        public static void ShowBubble(CharacterMainControl character, string textCN, string textEN, float duration = 1.5f)
        {
            if (character == null || character.transform == null) return;

            try
            {
                Duckov.UI.DialogueBubbles.DialogueBubblesManager.Show(
                    L10n.T(textCN, textEN),
                    character.transform,
                    duration,
                    false,
                    false,
                    -1f,
                    1.5f);
            }
            catch { /* 气泡失败不得影响玩法结算 */ }
        }

        // ========== 清理 ==========

        /// <summary>由 NewWeaponBootstrap 的 cleanup 路径调用。</summary>
        public static void ResetStaticCaches()
        {
            try
            {
                if (arcPool != null && arcPool.gameObject != null)
                {
                    UnityEngine.Object.Destroy(arcPool.gameObject);
                }
            }
            catch { /* best-effort fallback intentionally ignored */ }
            arcPool = null;

            NewWeaponSwingFx.ResetStaticCaches();
            BossRushProceduralSprites.ResetStaticCaches();
            // 共享特效层（BossRushFxKit）的清理在 BossRushUI.ResetStaticCaches，与 BossRushFxMaterials 同一条卸载路径。
        }
    }

    /// <summary>
    /// 一次性元素爆发：贴地空心光环（EaseOut 扩散、(1-t)^1.5 淡出、HDR 加色）+ 中心一闪的亮芯
    /// + 可选粒子碎片 + 可选只在前半程衰减的点光。自带 Update 并自毁，不依赖 ModBehaviour 的协程，
    /// 因此静态的 XxxRuntime 与套装都能直接用。
    ///
    /// 2026-09-23 审美修（VA-03 / VA-04 / VA-10）：
    ///   - 旧版是 Sprites-Default 上的 LDR 纯色环，线性扩散线性淡出，推不到泛光也没有亮芯；
    ///   - 旧碎片是 0.22×0.55 m 的实心椭圆精灵，竖着平移、不朝运动方向、不减速，俯视下读成一坨坨色斑。
    ///     现在碎片交给 BossRushFxKit 的一次性粒子：拉伸公告板沿速度方向拉长，先快后慢、受一点重力；
    ///   - 灯在 0.5×life 内衰减完，不再陪着环一起慢慢暗到最后。
    /// </summary>
    public class NewWeaponBurstFx : MonoBehaviour
    {
        private SpriteRenderer ringRenderer;
        private SpriteRenderer coreRenderer;
        private Light ringLight;
        private float elapsed;
        private float lifeTime;
        private float startScale;
        private float endScale;
        private float coreScale;
        private Color startColor;
        private float startIntensity;

        internal static void Play(
            Vector3 position, Color color, float radius, float life, int shardCount, bool withLight)
        {
            Sprite ringSprite = BossRushProceduralSprites.GetRingSprite();
            if (ringSprite != null) SpawnRing(position, color, radius, life, ringSprite, withLight);

            if (shardCount > 0)
            {
                // 碎片：数量按旧的「片数」放大一点（粒子很小，3 片读不出来），速度随半径走
                BossRushFxBurst shards = BossRushFxKit.Sparks(color, Mathf.Clamp(shardCount * 2, 4, 14));
                shards.Shape = BossRushParticleShape.Shard;
                shards.SpeedMin = Mathf.Max(2.5f, radius * 1.8f);
                shards.SpeedMax = Mathf.Max(4f, radius * 3f);
                shards.LifeMin = Mathf.Clamp(life * 0.6f, 0.15f, 0.35f);
                shards.LifeMax = Mathf.Clamp(life * 1.1f, 0.22f, 0.5f);
                shards.SizeMin = 0.06f;
                shards.SizeMax = 0.09f;
                shards.Gravity = 0.5f;
                shards.Upward = true;
                BossRushFxKit.PlayBurst(position + Vector3.up * 0.5f, shards);
            }
        }

        private static void SpawnRing(
            Vector3 position, Color color, float radius, float life, Sprite sprite, bool withLight)
        {
            GameObject ring = new GameObject("NewWeaponBurst");
            ring.transform.position = position + Vector3.up * 0.2f;
            // 躺平贴地
            ring.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

            float begin = Mathf.Max(0.2f, radius * 0.25f);
            ring.transform.localScale = new Vector3(begin, begin, 1f);

            SpriteRenderer sr = ring.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            // 加色 + 亮度档：环带在泛光里发亮，而不是一圈 LDR 塑料色；材质不可用时保留默认 Sprites-Default
            Material ringMaterial = BossRushFxKit.GetSpriteMaterial(sprite, BossRushFxBlend.Additive, BossRushFxKit.GainBright);
            if (ringMaterial != null) sr.sharedMaterial = ringMaterial;
            Color ringColor = new Color(color.r, color.g, color.b, Mathf.Clamp01(color.a) * 0.9f);
            sr.color = ringColor;
            sr.sortingOrder = 100;

            // 亮芯：中心一闪（前 35% 寿命内收掉），给爆发一个「源头」
            SpriteRenderer core = null;
            Sprite coreSprite = BossRushFxKit.GetSoftCircleSprite();
            Material coreMaterial = coreSprite != null
                ? BossRushFxKit.GetSpriteMaterial(coreSprite, BossRushFxBlend.Additive, BossRushFxKit.GainHot)
                : null;
            if (coreMaterial != null)
            {
                GameObject coreObject = new GameObject("Core");
                coreObject.transform.SetParent(ring.transform, false);
                core = coreObject.AddComponent<SpriteRenderer>();
                core.sprite = coreSprite;
                core.sharedMaterial = coreMaterial;
                core.color = new Color(
                    Mathf.Lerp(color.r, 1f, 0.55f), Mathf.Lerp(color.g, 1f, 0.55f), Mathf.Lerp(color.b, 1f, 0.55f), 0.8f);
                core.sortingOrder = 101;
            }

            Light light = null;
            if (withLight)
            {
                light = ring.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = new Color(color.r, color.g, color.b);
                light.intensity = 2.2f;
                light.range = Mathf.Clamp(radius * 1.2f, 1f, 3.5f);
                light.shadows = LightShadows.None;
            }

            NewWeaponBurstFx fx = ring.AddComponent<NewWeaponBurstFx>();
            fx.ringRenderer = sr;
            fx.coreRenderer = core;
            fx.ringLight = light;
            fx.lifeTime = Mathf.Max(0.05f, life);
            fx.startScale = begin;
            fx.endScale = Mathf.Max(begin, radius * 2f);
            // 亮芯在环本地空间里：环长大时芯按反比缩，世界尺寸从 0.9 m 左右收到 0
            fx.coreScale = Mathf.Clamp(radius * 0.7f, 0.5f, 1.4f);
            if (core != null)
            {
                float coreLocal = fx.coreScale / begin;
                core.transform.localScale = new Vector3(coreLocal, coreLocal, 1f);
            }
            fx.startColor = ringColor;
            fx.startIntensity = light != null ? light.intensity : 0f;
        }

        private void Update()
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / lifeTime);

            // 先快后慢地推开（与帧率无关：按已过时间取值，不做逐帧 Lerp 逼近）
            float scale = Mathf.Lerp(startScale, endScale, BossRushUI.EaseOut(t));
            transform.localScale = new Vector3(scale, scale, 1f);

            // 环在扩散后半程就淡掉，不拖到最后一帧才一起消失
            float fade = Mathf.Pow(1f - t, 1.5f);
            if (ringRenderer != null)
            {
                ringRenderer.color = new Color(startColor.r, startColor.g, startColor.b, startColor.a * fade);
            }
            if (coreRenderer != null)
            {
                float coreT = Mathf.Clamp01(t / 0.35f);
                float coreWorld = coreScale * (1f - coreT * coreT);
                float local = scale > 0.001f ? coreWorld / scale : 0f;
                coreRenderer.transform.localScale = new Vector3(local, local, 1f);
                Color c = coreRenderer.color;
                coreRenderer.color = new Color(c.r, c.g, c.b, 0.8f * (1f - coreT));
            }
            if (ringLight != null)
            {
                float lightT = Mathf.Clamp01(t * 2f);
                ringLight.intensity = startIntensity * (1f - lightT) * (1f - lightT);
            }

            if (t >= 1f)
            {
                Destroy(gameObject);
            }
        }
    }
}
