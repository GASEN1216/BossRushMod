// ============================================================================
// AstralStaffFx.cs - 星阙的全部表现（纯代码，无贴图资源、无模型）
// ============================================================================
// 组成：
//   AstralStaffHandVisual —— 细芯、双螺纹与端部光印，香槟金为主、少量冷青辅色。
//   AstralStaffBeanHud    —— 身后三颗棍势豆 + 蓄势时脚下的金环。
//   AstralStaffRibbon / AstralStaffBeam —— 弧形光带与多层光线，见 AstralStaffFxShapes.cs。
//   AstralStaffFx         —— 一次性爆发、命中四层（星芒 / 冲击环 / 斩痕 / 定向火花）、击杀星散、
//                            弧外缘星屑与灯光闪烁的静态入口。
//   AstralStaffSigil      —— 前摇的断环星纹收束与冲击后的符轮余韵。
// 手持光棍另有棍端星芒（1 粒）与两条挥击残光拖尾，只在棍端相对身体高速划动时出光。
// 预算：手持 8 条线 + 2 条拖尾 + 33 粒上限；HUD 3 颗星豆 + 1 条细环 + 32 粒上限；
// 同次攻击最多六个接触点，瞬时 owner 最多 64 个根对象，全部自毁或随换手立即撤销。
// ============================================================================

using System;
using UnityEngine;

namespace BossRush
{
    internal static partial class AstralStaffFx
    {
        internal static GameObject CreateTransient(string name)
        {
            Transform root = ResolveRoot();
            if (root == null) return null;
            GameObject go = new GameObject(name);
            go.transform.SetParent(root, false);
            return go;
        }

        internal static void PlayBurst(Vector3 position, BossRushFxBurst spec)
        {
            Transform root = ResolveRoot();
            if (root == null) return;
            ParticleSystem burst = BossRushFxKit.PlayBurst(position, spec);
            if (burst != null) burst.transform.SetParent(root, true);
        }

        /// <summary>
        /// 瞬时 owner 还剩多少空位（上限 64）。次要层（星屑、击杀余韵）先看余量，
        /// 群怪多杀时把位置让给重击本体，避免落点主表现被吃掉。
        /// </summary>
        internal static bool HasRoom(int reserve)
        {
            Transform root = ResolveRoot();
            return root != null && root.childCount + reserve < 64;
        }

        internal static Material Additive(BossRushParticleShape shape, float gain)
        {
            Material material = BossRushFxKit.GetShapeMaterial(shape, BossRushFxBlend.Additive, gain);
            if (material == null) material = BossRush.Common.Effects.RingParticleEffect.GetSharedParticleMaterial();
            return material;
        }

        internal static LineRenderer CreateLine(Transform parent, string name, Material material, float width, Color color, bool worldSpace)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            LineRenderer line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = material;
            line.useWorldSpace = worldSpace;
            line.positionCount = 2;
            line.widthMultiplier = width;
            line.startColor = color;
            line.endColor = color;
            line.numCapVertices = 4;
            line.textureMode = LineTextureMode.Stretch;
            line.alignment = LineAlignment.View;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            line.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            line.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            return line;
        }

        /// <summary>点状能量碎屑。只用于蓄豆等自反馈，真正命中另走定向压缩闪。</summary>
        internal static void PlayHitSpark(Vector3 position, float scale)
        {
            try
            {
                BossRushFxBurst sparks = BossRushFxKit.Sparks(AstralStaffConfig.Gold, Mathf.RoundToInt(6f * scale));
                sparks.SpeedMin = 3.5f * scale;
                sparks.SpeedMax = 6f * scale;
                sparks.Stretch = 0.045f;
                PlayBurst(position, sparks);

                BossRushFxBurst stars = BossRushFxKit.Sparks(AstralStaffConfig.Cyan, Mathf.RoundToInt(2f * scale));
                stars.Shape = BossRushParticleShape.Star;
                stars.SizeMin = 0.055f;
                stars.SizeMax = 0.11f;
                stars.Stretch = 0f;
                stars.SpeedMin = 1.5f;
                stars.SpeedMax = 3f;
                stars.LifeMin = 0.3f;
                stars.LifeMax = 0.5f;
                PlayBurst(position, stars);
            }
            catch { /* 表现失败不影响伤害 */ }
        }

        /// <summary>
        /// 只由确认伤害调用。四层、按先后读：星芒亮芯（一两帧最亮）→ 朝镜头撑开的细冲击环 →
        /// 顺发力方向斜切的一道斩痕 → 沿来势锥形喷出、先快后慢的拉伸火花。tier 0 轻击，1–3 重击；
        /// 三档在尺寸、亮度、火花数上拉开，二档起加冷青星屑悬停做余韵。全部一次性、随手持 owner 撤销。
        /// </summary>
        internal static void PlayContact(Vector3 point, Vector3 direction, int tier)
        {
            try
            {
                direction.y = 0f;
                if (direction.sqrMagnitude < 0.0001f) direction = Vector3.forward;
                direction.Normalize();
                int level = Mathf.Clamp(tier, 0, 3);
                Vector3 side = Vector3.Cross(Vector3.up, direction);
                float scale = 0.62f + 0.2f * level;

                PlayBurst(point, BossRushFxKit.Glint(AstralStaffConfig.CoreWhite, 0.7f + 0.3f * level, 0.07f + 0.015f * level));
                Color ring = AstralStaffConfig.Gold;
                ring.a = 0.7f;
                PlayBurst(point, BossRushFxKit.Shockwave(ring, 0.32f + 0.1f * level, 3.4f + 0.45f * level,
                    0.13f + 0.025f * level, false));

                Vector3 diagonal = (side * 0.86f + Vector3.up * 0.5f).normalized;
                AstralStaffBeam.Create(new[] { point - diagonal * scale, point + diagonal * scale * 1.15f },
                    0.028f + 0.006f * level, 0.15f + 0.035f * level, AstralStaffConfig.Gold, 0.02f, 0.13f + 0.02f * level, false);

                BossRushFxBurst sparks = BossRushFxKit.Sparks(AstralStaffConfig.Gold, 8 + level * 4);
                sparks.Cone = 34f;
                sparks.Direction = direction + Vector3.up * 0.3f;
                sparks.SizeMin = 0.03f;
                sparks.SizeMax = 0.06f;
                sparks.SpeedMin = 6f + level * 1.5f;
                sparks.SpeedMax = 11f + level * 2.5f;
                sparks.LifeMin = 0.14f;
                sparks.LifeMax = 0.3f;
                sparks.Stretch = 0.06f;
                sparks.Drag = 7f;
                sparks.Gravity = 0.8f;
                PlayBurst(point, sparks);

                if (level >= 1 && HasRoom(24))
                {
                    BossRushFxBurst motes = BossRushFxKit.Sparks(AstralStaffConfig.Cyan, 2 + level * 2);
                    motes.Shape = BossRushParticleShape.Star;
                    motes.Stretch = 0f;
                    motes.SizeMin = 0.06f;
                    motes.SizeMax = 0.12f;
                    motes.SpeedMin = 0.8f;
                    motes.SpeedMax = 2.2f;
                    motes.LifeMin = 0.35f;
                    motes.LifeMax = 0.6f;
                    motes.Drag = 3f;
                    motes.Gravity = -0.1f;
                    motes.GrowTo = 0.3f;
                    motes.Spin = 120f;
                    PlayBurst(point, motes);
                }
            }
            catch { /* 命中表现失败不反向影响伤害与体力 */ }
        }

        /// <summary>
        /// 击杀余韵「星散」：只由确认致死的伤害调用。大星芒 + 冷青冲击环 + 一捧缓缓上浮的星点与一闪暖光，
        /// 和普通命中拉开层级，读得出「这一下打死了」。
        /// </summary>
        internal static void PlayKillBloom(Vector3 point, Vector3 direction)
        {
            try
            {
                if (!HasRoom(24)) return;
                PlayBurst(point, BossRushFxKit.Glint(AstralStaffConfig.CoreWhite, 1.5f, 0.12f));
                Color ring = AstralStaffConfig.Cyan;
                ring.a = 0.65f;
                PlayBurst(point, BossRushFxKit.Shockwave(ring, 0.5f, 5f, 0.3f, false));

                BossRushFxBurst stars = BossRushFxKit.Sparks(AstralStaffConfig.Gold, 12);
                stars.Shape = BossRushParticleShape.Star;
                stars.Upward = true;
                stars.Stretch = 0f;
                stars.ShapeRadius = 0.3f;
                stars.SizeMin = 0.07f;
                stars.SizeMax = 0.14f;
                stars.SpeedMin = 1.2f;
                stars.SpeedMax = 3.2f;
                stars.LifeMin = 0.55f;
                stars.LifeMax = 0.95f;
                stars.Drag = 2.2f;
                stars.Gravity = -0.15f;
                stars.GrowTo = 0.25f;
                stars.Spin = 160f;
                stars.End = AstralStaffConfig.CyanFade;
                PlayBurst(point, stars);
                PlayFlash(point + Vector3.up * 0.4f, AstralStaffConfig.Gold, 1.5f, 3.4f, 0.22f);
            }
            catch { /* 同上 */ }
        }

        /// <summary>
        /// 沿挥击弧外缘撒一串星屑：每颗按弧的切向带一点初速、先快后慢、边缩边淡，
        /// 让光弧有「被甩出去的余光」。一个发射器、手动 Emit，粒子死完由 Release 回收。
        /// </summary>
        internal static void PlayArcSparkle(Vector3 center, Vector3 direction, float degrees, float radius, int count)
        {
            GameObject go = null;
            try
            {
                if (count <= 0) return;
                go = CreateTransient("AstralStaff_ArcSparkle");
                if (go == null) return;
                direction.y = 0f;
                if (direction.sqrMagnitude < 0.0001f) direction = Vector3.forward;
                direction.Normalize();
                ParticleSystem ps = BossRushFxKit.CreateEmitter("Motes", go.transform, Vector3.zero,
                    Additive(BossRushParticleShape.GlowDot, BossRushFxKit.GainHot), count, true);
                if (ps == null) { UnityEngine.Object.Destroy(go); return; }
                ParticleSystem.ColorOverLifetimeModule color = ps.colorOverLifetime;
                color.enabled = true;
                color.color = BossRushFxKit.FadeGradient(AstralStaffConfig.CoreWhite, AstralStaffConfig.Gold, AstralStaffConfig.CyanFade, 0f);
                ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
                size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.15f));
                ParticleSystem.LimitVelocityOverLifetimeModule limit = ps.limitVelocityOverLifetime;
                limit.enabled = true;
                limit.limit = new ParticleSystem.MinMaxCurve(1000f);
                limit.drag = new ParticleSystem.MinMaxCurve(3.5f);
                limit.multiplyDragByParticleSize = false;
                limit.multiplyDragByParticleVelocity = false;
                ps.Play();

                float sign = degrees < 0f ? -1f : 1f;
                ParticleSystem.EmitParams emit = new ParticleSystem.EmitParams();
                for (int i = 0; i < count; i++)
                {
                    float t = count > 1 ? i / (float)(count - 1) : 0.5f;
                    float angle = -degrees * 0.5f + degrees * t + UnityEngine.Random.Range(-4f, 4f);
                    Vector3 radial = Quaternion.Euler(0f, angle, 0f) * direction;
                    Vector3 tangent = Vector3.Cross(Vector3.up, radial) * sign;
                    emit.position = center + radial * (radius * UnityEngine.Random.Range(0.82f, 1.04f))
                        + Vector3.up * UnityEngine.Random.Range(-0.12f, 0.18f);
                    emit.velocity = tangent * UnityEngine.Random.Range(1.6f, 3.8f) + radial * 0.7f
                        + Vector3.up * UnityEngine.Random.Range(0.2f, 1.1f);
                    emit.startSize = UnityEngine.Random.Range(0.03f, 0.065f);
                    emit.startLifetime = UnityEngine.Random.Range(0.22f, 0.48f);
                    emit.startColor = Color.white;
                    ps.Emit(emit, 1);
                }
                BossRushFxKit.Release(go, 0f, 1f, false);
            }
            catch { if (go != null) UnityEngine.Object.Destroy(go); }
        }

        /// <summary>有厚薄变化的双层光轨；亮线只占外缘，不用铺满整片扇形。</summary>
        internal static void PlayStroke(Vector3 center, Vector3 direction, float degrees, float radius,
            Color accent, float reveal, float fade, Transform follow = null)
        {
            Color wash = accent;
            wash.a = 0.3f;
            AstralStaffRibbon.Play(center, direction, degrees, radius * 0.6f, radius,
                wash, reveal, 0.018f, fade, 0.01f, 0f, follow);
            Color edge = AstralStaffConfig.CoreWhite;
            edge.a = 0.78f;
            AstralStaffRibbon.Play(center + Vector3.up * 0.012f, direction, degrees, radius - 0.085f, radius + 0.015f,
                edge, reveal, 0.012f, fade * 0.62f, 0.1f, 0f, follow);
            Color echo = AstralStaffConfig.Cyan;
            echo.a = 0.28f;
            AstralStaffRibbon.Play(center - Vector3.up * 0.05f, direction, degrees * 0.9f, radius - 0.3f, radius - 0.25f,
                echo, reveal * 1.15f, 0.015f, fade * 1.4f, 0.4f, 0f, follow);
            PlayArcSparkle(center, direction, degrees, radius, Mathf.Clamp(Mathf.RoundToInt(Mathf.Abs(degrees) / 14f), 6, 26));
        }

        /// <summary>沙尘：贴地扩散的暖色烟。</summary>
        internal static void PlayDust(Vector3 position, int count, float speedScale)
        {
            try
            {
                BossRushFxBurst dust = BossRushFxKit.Dust(new Color(0.57f, 0.48f, 0.34f, 0.3f), count);
                dust.SpeedMin *= speedScale;
                dust.SpeedMax *= speedScale;
                dust.SizeMin = 0.28f;
                dust.SizeMax = 0.55f;
                dust.LifeMin = 0.45f;
                dust.LifeMax = 0.8f;
                PlayBurst(position, dust);
            }
            catch { /* 同上 */ }
        }

        /// <summary>灯光闪一下：瞬间拉满再淡出，自毁。</summary>
        internal static void PlayFlash(Vector3 position, Color color, float intensity, float range, float seconds)
        {
            GameObject go = null;
            try
            {
                go = CreateTransient("AstralStaff_Flash");
                if (go == null) return;
                go.transform.position = position;
                Light light = go.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = color;
                light.range = range;
                light.intensity = intensity;
                light.shadows = LightShadows.None;
                go.AddComponent<AstralStaffLightPulse>().Init(intensity, seconds);
            }
            catch { if (go != null) UnityEngine.Object.Destroy(go); }
        }
    }

    /// <summary>
    /// 程序化星阙符轮：断开的轨道与角形星纹，先向内收束，再短促扩散。
    /// 前摇用游戏时间对齐判定，余韵用未缩放时间；不使用整面白盘或屏幕覆盖。
    /// 一个符轮最多十二条细线，全部归当前手持的瞬时表现 owner。
    /// </summary>
    internal sealed class AstralStaffSigil : MonoBehaviour
    {
        private const int ArcPoints = 13;
        private LineRenderer[] _arcs;
        private LineRenderer[] _runes;
        private float _radius;
        private float _gather;
        private float _release;
        private float _elapsed;
        private bool _vertical;
        private Transform _follow;
        private Vector3 _offset;

        internal static void Play(Vector3 center, Vector3 direction, float radius, int tier,
            float gather, float release, Transform follow = null, bool vertical = false)
        {
            GameObject go = null;
            try
            {
                go = AstralStaffFx.CreateTransient("AstralStaff_Sigil");
                if (go == null) return;
                go.transform.position = center;
                direction.y = 0f;
                if (direction.sqrMagnitude < 0.0001f) direction = Vector3.forward;
                go.transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
                AstralStaffSigil effect = go.AddComponent<AstralStaffSigil>();
                effect._radius = radius;
                effect._gather = Mathf.Max(0.015f, gather);
                effect._release = Mathf.Max(0.05f, release);
                effect._vertical = vertical;
                effect._follow = follow;
                effect._offset = follow != null ? center - follow.position : Vector3.zero;
                int count = tier >= 3 ? 6 : Mathf.Clamp(tier + 2, 3, 4);
                effect._arcs = new LineRenderer[count];
                effect._runes = new LineRenderer[count];
                Material material = AstralStaffFx.Additive(BossRushParticleShape.TrailStrip, BossRushFxKit.GainSoft);
                for (int i = 0; i < count; i++)
                {
                    effect._arcs[i] = AstralStaffFx.CreateLine(go.transform, "Orbit", material, 0.022f, AstralStaffConfig.Gold, false);
                    effect._arcs[i].positionCount = ArcPoints;
                    effect._runes[i] = AstralStaffFx.CreateLine(go.transform, "StarRune", material, 0.018f, AstralStaffConfig.Gold, false);
                    effect._runes[i].positionCount = 5;
                }
                effect.Apply();
            }
            catch { if (go != null) Destroy(go); }
        }

        private Vector3 Plane(float x, float y)
        {
            return _vertical ? new Vector3(x, y, 0f) : new Vector3(x, 0f, y);
        }

        private void Update()
        {
            if (BossRushUI.IsGamePaused()) return;
            _elapsed += _elapsed < _gather ? Time.deltaTime : Time.unscaledDeltaTime;
            if (_follow != null) transform.position = _follow.position + _offset;
            Apply();
            if (_elapsed >= _gather + _release) Destroy(gameObject);
        }

        private void Apply()
        {
            float anticipation = Mathf.Clamp01(_elapsed / _gather);
            float tail = Mathf.Clamp01((_elapsed - _gather) / _release);
            float scale = _elapsed < _gather
                ? Mathf.Lerp(1.1f, 0.64f, anticipation * anticipation)
                : Mathf.Lerp(0.64f, 1.25f, 1f - (1f - tail) * (1f - tail));
            float alpha = Mathf.Min(1f, anticipation * 5f) * (1f - tail) * 0.56f;
            float rotation = anticipation * (2f - anticipation) * 0.42f;
            int count = _arcs.Length;
            for (int i = 0; i < count; i++)
            {
                float angle = i * Mathf.PI * 2f / count + rotation;
                float radius = _radius * scale;
                Color color = i % 3 == 1 ? AstralStaffConfig.Cyan : AstralStaffConfig.Gold;
                color.a = alpha;
                _arcs[i].startColor = color;
                _arcs[i].endColor = color;
                _runes[i].startColor = color;
                _runes[i].endColor = color;
                for (int p = 0; p < ArcPoints; p++)
                {
                    float a = angle + (p / (float)(ArcPoints - 1) - 0.5f) * Mathf.PI * 1.3f / count;
                    _arcs[i].SetPosition(p, Plane(Mathf.Sin(a) * radius, Mathf.Cos(a) * radius));
                }
                Vector3 radial = Plane(Mathf.Sin(angle), Mathf.Cos(angle));
                Vector3 tangent = Plane(Mathf.Cos(angle), -Mathf.Sin(angle));
                Vector3 at = radial * radius * 0.82f;
                float size = _radius * 0.08f;
                _runes[i].SetPosition(0, at - radial * size);
                _runes[i].SetPosition(1, at + tangent * size * 0.52f);
                _runes[i].SetPosition(2, at + radial * size);
                _runes[i].SetPosition(3, at - tangent * size * 0.52f);
                _runes[i].SetPosition(4, at - radial * size);
            }
        }
    }

    /// <summary>一次性灯光：EaseOut 淡出后销毁。用 unscaledDeltaTime，顿帧期间也照常淡。</summary>
    internal sealed class AstralStaffLightPulse : MonoBehaviour
    {
        private Light _light;
        private float _peak;
        private float _duration;
        private float _t;

        internal void Init(float peak, float duration)
        {
            _light = GetComponent<Light>();
            _peak = peak;
            _duration = Mathf.Max(0.05f, duration);
        }

        private void Update()
        {
            if (BossRushUI.IsGamePaused()) return;
            _t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(_t / _duration);
            if (_light != null) _light.intensity = _peak * (1f - k) * (1f - k);
            if (k >= 1f) Destroy(gameObject);
        }
    }

    // ========================================================================
    // 手上的光棍
    // ========================================================================

    /// <summary>
    /// 挂在官方手持代理上。代理挂在手部挂点下、随挥击动画转动，光棍沿挂点的一根本地轴伸出。
    /// 哪根轴朝外在不同挂点上不一致：挂上后的第一帧按「待机时最接近 前上方」选一次并缓存。
    /// 重击期间由 AstralStaffController 调 SetSurge 让光棍变粗变亮。
    /// </summary>
    internal sealed class AstralStaffHandVisual : MonoBehaviour
    {
        private const float GripBack = 0.42f;
        private const float GripFront = 1.18f;

        private Transform _root;
        private LineRenderer _core;
        private LineRenderer _glow;
        private LineRenderer _haze;
        private readonly LineRenderer[] _filigree = new LineRenderer[2];
        private readonly LineRenderer[] _caps = new LineRenderer[3];
        private ParticleSystem _motes;
        private ParticleSystem _tipStar;
        private readonly ParticleSystem.Particle[] _tipParticle = new ParticleSystem.Particle[1];
        private TrailRenderer _swingTrail;
        private TrailRenderer _swingHaze;
        private Vector3 _lastFront;
        private Vector3 _lastHolder;
        private bool _trailPrimed;
        private float _trailHold;
        private Light _light;
        private Vector3 _axis = Vector3.up;
        private bool _axisResolved;
        private float _surge;
        private float _surgeTarget;
        private float _time;
        private float _focus;
        private bool _charging;
        private SandstormChampionMinionMarker _summonedMinion;

        private static AstralStaffHandVisual current;

        /// <summary>当前主玩家手上的光棍（没拿星阙时为 null）。</summary>
        internal static AstralStaffHandVisual Current { get { return current; } }

        internal void BindSummonedMinion(SandstormChampionMinionMarker marker)
        {
            if (marker == null || marker.Character == null
                || !SandstormChampionMinionMarker.IsSummonedMinion(marker.Character)) return;
            _summonedMinion = marker;
            _axisResolved = false;
        }

        private void OnDestroy()
        {
            if (current == this) current = null;
        }

        private void Build()
        {
            _root = new GameObject("AstralStaff_Light").transform;
            _root.SetParent(transform, false);

            Material strip = AstralStaffFx.Additive(BossRushParticleShape.TrailStrip, BossRushFxKit.GainBright);
            _haze = AstralStaffFx.CreateLine(_root, "Haze", strip, 0.22f, new Color(0.42f, 0.7f, 0.77f, 0.09f), true);
            _glow = AstralStaffFx.CreateLine(_root, "Glow", strip, 0.085f, new Color(0.95f, 0.73f, 0.36f, 0.62f), true);
            _core = AstralStaffFx.CreateLine(_root, "Core", strip, 0.026f, AstralStaffConfig.CoreWhite, true);
            ApplyTaper(_haze);
            ApplyTaper(_glow);
            ApplyTaper(_core);

            Material fine = AstralStaffFx.Additive(BossRushParticleShape.TrailStrip, BossRushFxKit.GainSoft);
            for (int i = 0; i < _filigree.Length; i++)
            {
                _filigree[i] = AstralStaffFx.CreateLine(_root, "FineHelix", fine, 0.012f,
                    i == 0 ? AstralStaffConfig.Gold : AstralStaffConfig.Cyan, true);
                _filigree[i].positionCount = 19;
                ApplyTaper(_filigree[i]);
            }
            for (int i = 0; i < _caps.Length; i++)
            {
                _caps[i] = AstralStaffFx.CreateLine(_root, "FocusSeal", fine, 0.014f, AstralStaffConfig.Gold, true);
                _caps[i].positionCount = 13;
                _caps[i].loop = true;
            }

            Material dot = AstralStaffFx.Additive(BossRushParticleShape.GlowDot, BossRushFxKit.GainHot);
            _motes = BossRushFxKit.CreateEmitter("Motes", _root, Vector3.zero, dot, 32, true);
            if (_motes != null)
            {
                ParticleSystem.MainModule main = _motes.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.8f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.018f, 0.035f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.25f);
                ParticleSystem.EmissionModule emission = _motes.emission;
                emission.rateOverTime = 10f;
                ParticleSystem.ShapeModule shape = _motes.shape;
                shape.shapeType = ParticleSystemShapeType.Box;
                shape.scale = new Vector3(0.05f, 0.05f, GripBack + GripFront);
                ParticleSystem.ColorOverLifetimeModule col = _motes.colorOverLifetime;
                col.enabled = true;
                col.color = BossRushFxKit.FadeGradient(AstralStaffConfig.CoreWhite, AstralStaffConfig.Gold, AstralStaffConfig.CyanFade, 0.1f);
                _motes.Play();
            }

            // 棍端一颗缓转的星芒：静止时也有一个明确的「光源」焦点，不只是一根亮线。
            Material star = AstralStaffFx.Additive(BossRushParticleShape.Star, BossRushFxKit.GainHot);
            _tipStar = BossRushFxKit.CreateEmitter("TipStar", _root, Vector3.zero, star, 1, true);
            if (_tipStar != null)
            {
                ParticleSystem.EmissionModule emission = _tipStar.emission;
                emission.enabled = false;
                _tipStar.Play();
            }

            // 挥击残光：两条拖尾只在棍端相对身体高速划动时出光（官方轻击动画、回旋），走路不拖影。
            // 拖尾跟游戏时间走，顿帧时整条弧停在半空，正是命中那一下最好看的画面。
            _swingHaze = CreateSwingTrail("SwingHaze", strip, 0.26f, 0.2f,
                new Color(0.95f, 0.73f, 0.36f, 0.32f), new Color(0.42f, 0.7f, 0.77f, 0f));
            _swingTrail = CreateSwingTrail("SwingEdge", strip, 0.07f, 0.12f,
                new Color(1f, 0.97f, 0.88f, 0.95f), new Color(0.95f, 0.73f, 0.36f, 0f));
            _trailPrimed = false;

            GameObject lightGo = new GameObject("Glow");
            lightGo.transform.SetParent(_root, false);
            _light = lightGo.AddComponent<Light>();
            _light.type = LightType.Point;
            _light.color = AstralStaffConfig.Gold;
            _light.range = 1.8f;
            _light.intensity = 0.32f;
            _light.shadows = LightShadows.None;
        }

        private TrailRenderer CreateSwingTrail(string name, Material material, float width, float time, Color head, Color tail)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(_root, false);
            TrailRenderer trail = go.AddComponent<TrailRenderer>();
            trail.sharedMaterial = material;
            trail.time = time;
            trail.minVertexDistance = 0.035f;
            trail.widthMultiplier = width;
            trail.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.35f, 0.7f), new Keyframe(1f, 0f));
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(head, 0f), new GradientColorKey(tail, 1f) },
                new[] { new GradientAlphaKey(head.a, 0f), new GradientAlphaKey(head.a * 0.45f, 0.4f), new GradientAlphaKey(0f, 1f) });
            trail.colorGradient = gradient;
            trail.numCapVertices = 2;
            trail.alignment = LineAlignment.View;
            trail.textureMode = LineTextureMode.Stretch;
            trail.autodestruct = false;
            trail.emitting = false;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            trail.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            trail.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            return trail;
        }

        /// <summary>
        /// 拖尾出光门：棍端相对持有者的速度超过阈值（挥击）或重击涌动中才发射，短暂保持避免断续。
        /// 第一帧 / 重新激活时只记位置，不拿旧位置算速度。
        /// </summary>
        private void UpdateSwingTrails(Vector3 front, CharacterMainControl holder)
        {
            if (_swingTrail == null || _swingHaze == null) return;
            Vector3 holderPos = holder != null ? holder.transform.position : front;
            _swingTrail.transform.position = front;
            _swingHaze.transform.position = front;
            float dt = Time.deltaTime;
            bool emit = false;
            if (_trailPrimed && dt > 0.0001f)
            {
                Vector3 relative = (front - _lastFront) - (holderPos - _lastHolder);
                float speed = relative.magnitude / dt;
                if (speed > 7f || _surge > 0.55f) _trailHold = 0.06f;
                else _trailHold -= dt;
                emit = _trailHold > 0f;
            }
            else if (!_trailPrimed)
            {
                _swingTrail.Clear();
                _swingHaze.Clear();
                _trailHold = 0f;
            }
            _trailPrimed = true;
            _lastFront = front;
            _lastHolder = holderPos;
            if (_swingTrail.emitting != emit) _swingTrail.emitting = emit;
            if (_swingHaze.emitting != emit) _swingHaze.emitting = emit;
        }

        private void UpdateTipStar(Vector3 front, float focus, float luminance)
        {
            if (_tipStar == null) return;
            ParticleSystem.Particle p = _tipParticle[0];
            p.position = front;
            p.startSize = (0.2f + 0.05f * Mathf.Sin(_time * 4.1f) + _surge * 0.32f + focus * 0.08f) * luminance;
            p.rotation = _time * 38f;
            Color color = Color.Lerp(AstralStaffConfig.Gold, AstralStaffConfig.CoreWhite, 0.45f + _surge * 0.55f);
            color.a = (0.7f + _surge * 0.3f) * luminance;
            p.startColor = color;
            p.startLifetime = 10f;
            p.remainingLifetime = 10f;
            _tipParticle[0] = p;
            _tipStar.SetParticles(_tipParticle, 1);
        }

        private static void ApplyTaper(LineRenderer line)
        {
            if (line == null) return;
            line.widthCurve = new AnimationCurve(
                new Keyframe(0f, 0.35f), new Keyframe(0.18f, 1f), new Keyframe(0.85f, 1f), new Keyframe(1f, 0.55f));
        }

        /// <summary>重击出手时让光棍变粗变亮（0..1），之后自己回落。</summary>
        internal void SetSurge(float amount)
        {
            _surgeTarget = Mathf.Max(_surgeTarget, Mathf.Clamp01(amount));
        }

        internal void SetFocus(float amount, bool charging)
        {
            _focus = Mathf.Clamp(amount, 0f, AstralStaffConfig.MaxBeans);
            _charging = charging;
        }

        private void LateUpdate()
        {
            CharacterMainControl player = CharacterMainControl.Main;
            bool mainActive = player != null && player.Health != null && !player.Health.IsDead
                && player.CurrentHoldItemAgent != null && player.CurrentHoldItemAgent.gameObject == gameObject;
            // NPC 只接受本场召唤 owner 的显式绑定，普通 NPC、仓库和背包代理仍零构建。
            CharacterMainControl holder = mainActive ? player
                : (_summonedMinion != null && _summonedMinion.IsCombatActive ? _summonedMinion.Character : null);
            bool active = holder != null && holder.CurrentHoldItemAgent != null
                && holder.CurrentHoldItemAgent.gameObject == gameObject;
            if (!active)
            {
                if (_root != null && _root.gameObject.activeSelf) _root.gameObject.SetActive(false);
                _focus = 0f;
                _charging = false;
                _surge = 0f;
                _surgeTarget = 0f;
                _trailPrimed = false;
                if (current == this) current = null;
                return;
            }
            if (mainActive) current = this;
            else if (current == this) current = null;
            if (_root == null)
            {
                try { Build(); }
                catch (Exception e)
                {
                    ModBehaviour.DevLog(AstralStaffConfig.LogPrefix + " [WARNING] 光棍构建失败: " + e.Message);
                    if (_root != null) _root.gameObject.SetActive(false);
                    enabled = false;
                    return;
                }
            }
            if (!_root.gameObject.activeSelf) _root.gameObject.SetActive(true);
            if (_core == null || BossRushUI.IsGamePaused()) return;
            if (!_axisResolved) ResolveAxis(holder);

            float dt = Time.unscaledDeltaTime;
            _time += dt;
            _surge = Mathf.MoveTowards(_surge, _surgeTarget, dt * 8f);
            _surgeTarget = Mathf.MoveTowards(_surgeTarget, 0f, dt * 2.2f);

            Vector3 dir = transform.TransformDirection(_axis).normalized;
            Vector3 origin = transform.position;
            float stretch = 1f + _surge * 0.25f;
            Vector3 back = origin - dir * GripBack;
            Vector3 front = origin + dir * GripFront * stretch;

            SetLine(_core, back, front);
            SetLine(_glow, back, front);
            SetLine(_haze, back, front);

            float focus = mainActive ? _focus / AstralStaffConfig.MaxBeans : 0f;
            float pulse = 0.95f + 0.05f * Mathf.Sin(_time * 3.3f);
            float luminance = mainActive ? 1f : 0.72f;
            _glow.widthMultiplier = (0.085f + _surge * 0.075f + focus * 0.025f) * pulse;
            _haze.widthMultiplier = 0.22f + _surge * 0.13f;
            _core.widthMultiplier = 0.026f + _surge * 0.012f;
            Color core = AstralStaffConfig.CoreWhite;
            core.a = 0.9f * luminance;
            _core.startColor = core;
            _core.endColor = core;
            Color glow = AstralStaffConfig.Gold;
            glow.a = 0.62f * luminance;
            _glow.startColor = glow;
            _glow.endColor = glow;
            Color haze = AstralStaffConfig.Cyan;
            haze.a = 0.09f * luminance;
            _haze.startColor = haze;
            _haze.endColor = haze;

            Vector3 side = Vector3.Cross(dir, Vector3.up);
            if (side.sqrMagnitude < 0.0001f) side = Vector3.Cross(dir, Vector3.forward);
            side.Normalize();
            Vector3 normal = Vector3.Cross(dir, side).normalized;
            float phase = _time * (mainActive && _charging ? 3f : 0.8f);
            for (int i = 0; i < _filigree.Length; i++)
            {
                Color color = i == 0 ? AstralStaffConfig.Gold : AstralStaffConfig.Cyan;
                color.a = (0.32f + focus * 0.25f + _surge * 0.22f) * luminance;
                _filigree[i].startColor = color;
                _filigree[i].endColor = color;
                for (int p = 0; p < 19; p++)
                {
                    float f = p / 18f;
                    float angle = f * Mathf.PI * 3f + phase + i * Mathf.PI;
                    float radius = Mathf.Sin(f * Mathf.PI) * (0.045f + focus * 0.025f + _surge * 0.03f);
                    _filigree[i].SetPosition(p, Vector3.Lerp(back, front, f)
                        + (side * Mathf.Cos(angle) + normal * Mathf.Sin(angle)) * radius);
                }
            }
            for (int i = 0; i < _caps.Length; i++)
            {
                Vector3 at = i == 0 ? back + dir * 0.13f : i == 1 ? front - dir * 0.14f : origin + dir * 0.32f;
                float radius = (i == 2 ? 0.08f : 0.1f) + _surge * 0.035f + focus * 0.025f;
                Color color = AstralStaffConfig.Gold;
                color.a = (i == 2 ? focus * 0.7f : 0.56f) * luminance;
                _caps[i].startColor = color;
                _caps[i].endColor = color;
                for (int p = 0; p < 13; p++)
                {
                    float angle = p * Mathf.PI * 2f / 13f + phase * 0.35f;
                    _caps[i].SetPosition(p, at + (side * Mathf.Cos(angle) + normal * Mathf.Sin(angle)) * radius);
                }
            }

            if (_motes != null)
            {
                Transform mt = _motes.transform;
                mt.position = (back + front) * 0.5f;
                mt.rotation = Quaternion.LookRotation(dir);
                ParticleSystem.EmissionModule emission = _motes.emission;
                emission.rateOverTime = mainActive ? 10f + focus * 7f + _surge * 8f : 6f;
            }
            if (_light != null)
            {
                _light.transform.position = front;
                _light.intensity = (0.32f + _surge * 0.8f + focus * 0.18f) * pulse * luminance;
            }
            UpdateTipStar(front, focus, luminance);
            UpdateSwingTrails(front, holder);
        }

        private static void SetLine(LineRenderer line, Vector3 a, Vector3 b)
        {
            line.SetPosition(0, a);
            line.SetPosition(1, b);
        }

        private void ResolveAxis(CharacterMainControl holder)
        {
            _axisResolved = true;
            try
            {
                Vector3 want = Vector3.up;
                if (holder != null)
                {
                    Vector3 fwd = holder.transform.forward;
                    fwd.y = 0f;
                    want = (Vector3.up * 0.8f + fwd.normalized * 0.6f).normalized;
                }
                Vector3[] candidates = { Vector3.up, Vector3.down, Vector3.forward, Vector3.back, Vector3.right, Vector3.left };
                float best = float.MinValue;
                for (int i = 0; i < candidates.Length; i++)
                {
                    float score = Vector3.Dot(transform.TransformDirection(candidates[i]), want);
                    if (score > best)
                    {
                        best = score;
                        _axis = candidates[i];
                    }
                }
            }
            catch { _axis = Vector3.up; }
        }
    }

    // ========================================================================
    // 棍势豆 HUD
    // ========================================================================

    /// <summary>
    /// 身后肩高处三颗星豆 + 蓄势时脚下的金环。挂在 AstralStaffController 自己的物体上，
    /// 不拿星阙时整个停用（O(1)）。粒子位置每帧手动写（SetParticles），三颗豆不靠模拟。
    /// </summary>
    internal sealed class AstralStaffBeanHud : MonoBehaviour
    {
        private const int RingSegments = 48;

        private ParticleSystem _beans;
        private readonly ParticleSystem.Particle[] _particles = new ParticleSystem.Particle[AstralStaffConfig.MaxBeans];
        private LineRenderer _ring;
        private ParticleSystem _swirl;
        private float _time;
        private readonly float[] _fillPop = new float[AstralStaffConfig.MaxBeans];
        private bool _built;
        private bool _visible;

        internal void Build()
        {
            if (_built) return;
            _built = true;
            Material dot = AstralStaffFx.Additive(BossRushParticleShape.Star, BossRushFxKit.GainHot);
            _beans = BossRushFxKit.CreateEmitter("AstralStaff_Beans", transform, Vector3.zero, dot, AstralStaffConfig.MaxBeans, true);
            if (_beans != null)
            {
                ParticleSystem.EmissionModule emission = _beans.emission;
                emission.enabled = false;
            }

            Material strip = AstralStaffFx.Additive(BossRushParticleShape.TrailStrip, BossRushFxKit.GainBright);
            _ring = AstralStaffFx.CreateLine(transform, "AstralStaff_ChargeRing", strip, 0.03f, AstralStaffConfig.Gold, true);
            _ring.positionCount = RingSegments;
            _ring.loop = true;
            _ring.alignment = LineAlignment.TransformZ;
            _ring.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            _ring.enabled = false;

            Material glow = AstralStaffFx.Additive(BossRushParticleShape.GlowDot, BossRushFxKit.GainHot);
            _swirl = BossRushFxKit.CreateEmitter("AstralStaff_ChargeSwirl", transform, Vector3.zero, glow, 32, true);
            if (_swirl != null)
            {
                ParticleSystem.MainModule main = _swirl.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.8f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.045f);
                ParticleSystem.ShapeModule shape = _swirl.shape;
                shape.shapeType = ParticleSystemShapeType.Circle;
                shape.radius = 1.1f;
                shape.rotation = new Vector3(90f, 0f, 0f);
                ParticleSystem.VelocityOverLifetimeModule vel = _swirl.velocityOverLifetime;
                vel.enabled = true;
                vel.space = ParticleSystemSimulationSpace.Local;
                vel.orbitalY = new ParticleSystem.MinMaxCurve(4f);
                vel.radial = new ParticleSystem.MinMaxCurve(-1.2f);
                vel.y = new ParticleSystem.MinMaxCurve(1.6f);
                ParticleSystem.ColorOverLifetimeModule col = _swirl.colorOverLifetime;
                col.enabled = true;
                col.color = BossRushFxKit.FadeGradient(AstralStaffConfig.CoreWhite, AstralStaffConfig.Gold, AstralStaffConfig.GoldFade, 0.15f);
            }
            HideAll();
        }

        /// <summary>某颗豆刚满：让它弹一下。</summary>
        internal void PopBean(int index)
        {
            if (index >= 0 && index < _fillPop.Length) _fillPop[index] = 1f;
        }

        /// <summary>每帧由控制器调用。beans 为 0..3 的连续值（小数部分是正在长的那颗）。</summary>
        internal void Tick(CharacterMainControl player, float beans, float charge01, bool charging)
        {
            bool visible = !BossRushUI.IsOfficialHudHidden() && !BossRushUI.IsGamePaused();
            if (!visible) { HideAll(); return; }
            if (!_built)
            {
                try { Build(); }
                catch (Exception e)
                {
                    ModBehaviour.DevLog(AstralStaffConfig.LogPrefix + " [WARNING] HUD 构建失败: " + e.Message);
                    HideAll();
                    return;
                }
            }
            if (player == null || _beans == null) return;
            if (!_visible)
            {
                _visible = true;
                _beans.gameObject.SetActive(true);
                _beans.Play();
                if (_swirl != null) { _swirl.gameObject.SetActive(true); _swirl.Play(); }
            }
            float dt = Time.unscaledDeltaTime;
            _time += dt;

            Vector3 basePos = player.transform.position;
            Vector3 back = -player.transform.forward;
            back.y = 0f;
            if (back.sqrMagnitude < 0.001f) back = Vector3.back;
            back.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, back);

            for (int i = 0; i < AstralStaffConfig.MaxBeans; i++)
            {
                _fillPop[i] = Mathf.MoveTowards(_fillPop[i], 0f, dt * 3f);
                float fill = Mathf.Clamp01(beans - i);
                float side = (i - 1) * 0.42f;
                float bob = Mathf.Sin(_time * 2.4f + i * 1.7f) * 0.05f;
                Vector3 pos = basePos + Vector3.up * (1.75f + bob + (i == 1 ? 0.12f : 0f)) + back * 0.38f + right * side;

                bool full = fill >= 0.999f;
                float size = full ? 0.24f + 0.018f * Mathf.Sin(_time * 4f + i) : Mathf.Lerp(0.09f, 0.19f, fill);
                size += _fillPop[i] * 0.19f;
                Color color = full
                    ? Color.Lerp(AstralStaffConfig.Gold, AstralStaffConfig.CoreWhite, 0.45f + _fillPop[i] * 0.55f)
                    : new Color(0.42f, 0.7f, 0.77f, Mathf.Lerp(0.22f, 0.7f, fill));

                ParticleSystem.Particle p = _particles[i];
                p.position = pos;
                p.startSize = size;
                p.startColor = color;
                p.remainingLifetime = 10f;
                p.startLifetime = 10f;
                p.rotation = _time * (full ? 90f : 25f) + i * 40f;
                _particles[i] = p;
            }
            _beans.SetParticles(_particles, AstralStaffConfig.MaxBeans);

            // 蓄势环：细线内收，三档各多一层星纹，满豆稳定停驻而非无限加速。
            if (_ring != null)
            {
                _ring.enabled = charging;
                if (charging)
                {
                    float radius = 1.05f - 0.16f * charge01 + 0.015f * Mathf.Sin(_time * 5f);
                    Vector3 center = basePos + Vector3.up * 0.06f;
                    float spin = _time * 24f;
                    for (int s = 0; s < RingSegments; s++)
                    {
                        float a = (s / (float)RingSegments) * Mathf.PI * 2f + spin * Mathf.Deg2Rad;
                        float star = 1f + 0.035f * Mathf.Cos(a * 6f);
                        _ring.SetPosition(s, center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (radius * star));
                    }
                    float level = Mathf.Floor(Mathf.Clamp(beans, 0f, AstralStaffConfig.MaxBeans));
                    Color ringColor = Color.Lerp(AstralStaffConfig.Gold, AstralStaffConfig.CoreWhite, level * 0.08f);
                    ringColor.a = 0.26f + 0.075f * level + 0.08f * charge01;
                    _ring.startColor = ringColor;
                    _ring.endColor = ringColor;
                    _ring.widthMultiplier = 0.026f + 0.006f * level;
                }
            }

            if (_swirl != null)
            {
                _swirl.transform.position = basePos + Vector3.up * 0.1f;
                ParticleSystem.EmissionModule emission = _swirl.emission;
                emission.rateOverTime = charging ? 12f + 7f * Mathf.Floor(beans) : 0f;
            }
        }

        internal void HideAll()
        {
            _visible = false;
            if (_beans != null)
            {
                _beans.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                _beans.gameObject.SetActive(false);
            }
            if (_ring != null) _ring.enabled = false;
            if (_swirl != null)
            {
                _swirl.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                _swirl.gameObject.SetActive(false);
            }
        }
    }
}
