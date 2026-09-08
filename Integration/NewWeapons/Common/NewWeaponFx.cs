// ============================================================================
// NewWeaponFx.cs - 五把新武器的表现层门面（配色 / 音效路径 / 爆发环 / 电弧）
// ============================================================================
// 模块说明：
//   五把武器的运行时机制此前完全没有视听反馈——毒素叠满爆发、雷电蓄满释放、
//   法杖召唤、能量盾正面吸收这四个关键瞬间屏幕上什么都不会发生。本文件补齐这一层。
//
//   为什么不复用 SetBonusVisuals：那边的 SpawnSetBurst / SpawnSetArc 是
//   partial class ModBehaviour 的私有成员，静态的 XxxRuntime 调不到；
//   而 ModBehaviourPartialBudgetGuard 的文件数已经顶格（204/204），
//   不能再往宿主上加 partial。因此新武器的表现层独立成本类。
//
//   配色直接取自各武器描述文案里的 color 标签，保证「描述里写的颜色」与「屏幕上看到的颜色」一致。
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
        // 毒蛇匕首 #7CFC00 / #ADFF2F
        public static readonly Color VenomCore = new Color(0.486f, 0.988f, 0f, 0.85f);
        public static readonly Color VenomFade = new Color(0.678f, 1f, 0.184f, 0.35f);

        // 冰霜长矛 #4FC3F7 / #81D4FA
        public static readonly Color FrostCore = new Color(0.310f, 0.765f, 0.969f, 0.85f);
        public static readonly Color FrostFade = new Color(0.506f, 0.831f, 0.980f, 0.35f);

        // 召唤法杖 #BA68C8 / #CE93D8
        public static readonly Color SoulCore = new Color(0.729f, 0.408f, 0.784f, 0.85f);
        public static readonly Color SoulFade = new Color(0.808f, 0.576f, 0.847f, 0.35f);

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
        }
    }

    /// <summary>
    /// 一次性扩散光环。自带 Update 淡出并自毁，不依赖 ModBehaviour 的协程，
    /// 因此静态的 XxxRuntime 也能直接用。
    /// </summary>
    public class NewWeaponBurstFx : MonoBehaviour
    {
        private SpriteRenderer ringRenderer;
        private Light ringLight;
        private float elapsed;
        private float lifeTime;
        private float startScale;
        private float endScale;
        private Color startColor;
        private float startIntensity;

        internal static void Play(
            Vector3 position, Color color, float radius, float life, int shardCount, bool withLight)
        {
            Sprite sprite = BossRushProceduralSprites.GetCircleSprite();
            if (sprite == null) return;

            SpawnRing(position, color, radius, life, sprite, withLight);

            for (int i = 0; i < shardCount; i++)
            {
                float angle = (360f / shardCount) * i + UnityEngine.Random.Range(-15f, 15f);
                Vector3 direction = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                SpawnShard(position, color, sprite, direction, radius * 2.2f, life * 0.8f);
            }
        }

        private static void SpawnRing(
            Vector3 position, Color color, float radius, float life, Sprite sprite, bool withLight)
        {
            GameObject ring = new GameObject("NewWeaponBurst");
            ring.transform.position = position + Vector3.up * 0.3f;
            // 躺平贴地，与套装爆发环同款朝向
            ring.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

            float begin = Mathf.Max(0.2f, radius * 0.35f);
            ring.transform.localScale = new Vector3(begin, begin, 1f);

            SpriteRenderer sr = ring.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sortingOrder = 100;

            Light light = null;
            if (withLight)
            {
                light = ring.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = new Color(color.r, color.g, color.b);
                light.intensity = 3.5f;
                light.range = Mathf.Max(1f, radius * 1.5f);
                light.shadows = LightShadows.None;
            }

            NewWeaponBurstFx fx = ring.AddComponent<NewWeaponBurstFx>();
            fx.ringRenderer = sr;
            fx.ringLight = light;
            fx.lifeTime = Mathf.Max(0.05f, life);
            fx.startScale = begin;
            fx.endScale = Mathf.Max(begin, radius * 2f);
            fx.startColor = color;
            fx.startIntensity = light != null ? light.intensity : 0f;
        }

        private static void SpawnShard(Vector3 origin, Color color, Sprite sprite, Vector3 direction, float speed, float life)
        {
            GameObject shard = new GameObject("NewWeaponBurstShard");
            shard.transform.position = origin + Vector3.up * 0.7f;
            shard.transform.localScale = new Vector3(0.22f, 0.55f, 1f);

            SpriteRenderer sr = shard.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sortingOrder = 101;

            NewWeaponBurstShardFx fx = shard.AddComponent<NewWeaponBurstShardFx>();
            fx.Initialize(sr, direction * speed, life);
        }

        private void Update()
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / lifeTime);

            float scale = Mathf.Lerp(startScale, endScale, t);
            transform.localScale = new Vector3(scale, scale, 1f);

            float fade = 1f - t;
            if (ringRenderer != null)
            {
                ringRenderer.color = new Color(startColor.r, startColor.g, startColor.b, startColor.a * fade);
            }
            if (ringLight != null)
            {
                ringLight.intensity = startIntensity * fade;
            }

            if (t >= 1f)
            {
                Destroy(gameObject);
            }
        }
    }

    /// <summary>爆发碎片：沿固定方向飞出并淡出，自毁。</summary>
    public class NewWeaponBurstShardFx : MonoBehaviour
    {
        private SpriteRenderer shardRenderer;
        private Vector3 velocity;
        private float lifeTime;
        private float elapsed;
        private Color startColor;

        internal void Initialize(SpriteRenderer sr, Vector3 shardVelocity, float life)
        {
            shardRenderer = sr;
            velocity = shardVelocity;
            lifeTime = Mathf.Max(0.05f, life);
            startColor = sr != null ? sr.color : Color.white;
        }

        private void Update()
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / lifeTime);

            transform.position += velocity * Time.deltaTime;
            if (shardRenderer != null)
            {
                shardRenderer.color = new Color(startColor.r, startColor.g, startColor.b, startColor.a * (1f - t));
            }

            if (t >= 1f)
            {
                Destroy(gameObject);
            }
        }
    }
}
