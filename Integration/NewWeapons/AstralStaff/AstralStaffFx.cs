// ============================================================================
// AstralStaffFx.cs - 星阙的全部表现（纯代码，无贴图资源、无模型）
// ============================================================================
// 组成：
//   AstralStaffHandVisual —— 细芯、双螺纹与端部光印，香槟金为主、少量冷青辅色。
//   AstralStaffBeanHud    —— 身后三颗棍势豆 + 蓄势时脚下的金环。
//   AstralStaffRibbon     —— 横扫 / 回旋 / 冲击波用的弧形光带（程序网格，显出→淡出）。
//   AstralStaffBeam       —— 星陨的巨型光棍、落点光柱、地裂纹（线条，淡出自毁）。
//   AstralStaffFx         —— 一次性爆发与灯光闪烁的静态入口。
//   AstralStaffSigil      —— 前摇的断环星纹收束与冲击后的符轮余韵。
// 预算：手持 8 条线 + 32 粒上限；HUD 3 颗星豆 + 1 条细环 + 32 粒上限；
// 同次攻击最多六个接触点，瞬时 owner 最多 64 个根对象，全部自毁或随换手立即撤销。
// ============================================================================

using System;
using UnityEngine;

namespace BossRush
{
    internal static class AstralStaffFx
    {
        internal static GameObject CreateTransient(string name)
        {
            AstralStaffController owner = AstralStaffController.Instance;
            Transform root = owner != null ? owner.GetTransientFxRoot() : null;
            if (root == null) return null;
            GameObject go = new GameObject(name);
            go.transform.SetParent(root, false);
            return go;
        }

        internal static void PlayBurst(Vector3 position, BossRushFxBurst spec)
        {
            AstralStaffController owner = AstralStaffController.Instance;
            Transform root = owner != null ? owner.GetTransientFxRoot() : null;
            if (root == null) return;
            ParticleSystem burst = BossRushFxKit.PlayBurst(position, spec);
            if (burst != null) burst.transform.SetParent(root, true);
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

        /// <summary>只由确认伤害调用：极短交叉亮芯、沿来势压出的光痕、少量惯性碎屑。</summary>
        internal static void PlayContact(Vector3 point, Vector3 direction, int tier)
        {
            try
            {
                direction.y = 0f;
                if (direction.sqrMagnitude < 0.0001f) direction = Vector3.forward;
                direction.Normalize();
                Vector3 side = Vector3.Cross(Vector3.up, direction);
                float scale = 0.6f + 0.18f * Mathf.Clamp(tier, 0, 3);
                Vector3 diagonal = (side * 0.86f + Vector3.up * 0.5f).normalized;
                AstralStaffBeam.Create(new[] { point - diagonal * scale, point + diagonal * scale },
                    0.022f, 0.12f, AstralStaffConfig.Gold, 0.018f, 0.12f, false);
                AstralStaffBeam.Create(new[] { point - Vector3.up * scale * 0.45f, point + Vector3.up * scale * 0.45f },
                    0.012f, 0.055f, AstralStaffConfig.Cyan, 0.016f, 0.1f, false);
                AstralStaffBeam.Create(new[] { point - direction * scale * 0.2f, point + direction * scale * 0.85f },
                    0.016f, 0.07f, AstralStaffConfig.Gold, 0.025f, 0.2f, false);
                BossRushFxBurst sparks = BossRushFxKit.Sparks(AstralStaffConfig.Gold, 6 + tier * 2);
                sparks.SizeMin = 0.028f;
                sparks.SizeMax = 0.052f;
                sparks.SpeedMin = 3f + tier;
                sparks.SpeedMax = 6f + tier * 1.5f;
                sparks.LifeMin = 0.12f;
                sparks.LifeMax = 0.24f;
                sparks.Stretch = 0.055f;
                sparks.Drag = 8f;
                PlayBurst(point, sparks);
            }
            catch { /* 命中表现失败不反向影响伤害与体力 */ }
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

            GameObject lightGo = new GameObject("Glow");
            lightGo.transform.SetParent(_root, false);
            _light = lightGo.AddComponent<Light>();
            _light.type = LightType.Point;
            _light.color = AstralStaffConfig.Gold;
            _light.range = 1.8f;
            _light.intensity = 0.32f;
            _light.shadows = LightShadows.None;
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

    // ========================================================================
    // 弧形光带（横扫 / 回旋 / 冲击波）
    // ========================================================================

    /// <summary>
    /// 一条贴着地面（或略高于地面）的扇形 / 环形光带。显出阶段沿弧方向推进，之后整体淡出并自毁。
    /// 网格只建一次，动画只改顶点色；自毁时销毁网格。
    /// </summary>
    internal sealed class AstralStaffRibbon : MonoBehaviour
    {
        private const int Segments = 40;

        private Mesh _mesh;
        private Color[] _colors;
        private float[] _segT;
        private Color _color;
        private float _reveal;
        private float _hold;
        private float _fade;
        private float _t;
        private float _innerAlpha;
        private float _growFrom;
        private bool _grow;
        private Transform _follow;

        /// <summary>
        /// center：圆心；forward：扇形中线方向；sweep：总角度（度，负值反向推进）；
        /// inner/outer：内外半径；reveal/hold/fade：三段时长（秒，按未缩放时间走，顿帧时光带照样展开）。
        /// growFrom&gt;0 时半径从 growFrom 倍长到 1 倍（冲击波）。
        /// </summary>
        internal static AstralStaffRibbon Play(Vector3 center, Vector3 forward, float sweep, float inner, float outer,
            Color color, float reveal, float hold, float fade, float innerAlpha, float growFrom, Transform follow)
        {
            GameObject go = null;
            try
            {
                go = AstralStaffFx.CreateTransient("AstralStaff_Ribbon");
                if (go == null) return null;
                go.transform.position = center;
                forward.y = 0f;
                if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
                go.transform.rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
                AstralStaffRibbon ribbon = go.AddComponent<AstralStaffRibbon>();
                ribbon.Init(sweep, inner, outer, color, reveal, hold, fade, innerAlpha, growFrom, follow);
                return ribbon;
            }
            catch (Exception e)
            {
                if (go != null) Destroy(go);
                ModBehaviour.DevLog(AstralStaffConfig.LogPrefix + " [WARNING] 光带失败: " + e.Message);
                return null;
            }
        }

        private void Init(float sweep, float inner, float outer, Color color, float reveal, float hold, float fade,
            float innerAlpha, float growFrom, Transform follow)
        {
            _color = color;
            _reveal = Mathf.Max(0.001f, reveal);
            _hold = Mathf.Max(0f, hold);
            _fade = Mathf.Max(0.01f, fade);
            _innerAlpha = innerAlpha;
            _growFrom = growFrom;
            _grow = growFrom > 0f;
            _follow = follow;

            int vertexCount = (Segments + 1) * 2;
            Vector3[] vertices = new Vector3[vertexCount];
            Vector2[] uvs = new Vector2[vertexCount];
            _colors = new Color[vertexCount];
            _segT = new float[Segments + 1];
            int[] triangles = new int[Segments * 6];
            float start = -sweep * 0.5f;
            for (int i = 0; i <= Segments; i++)
            {
                float t = i / (float)Segments;
                _segT[i] = t;
                float angle = (start + sweep * t) * Mathf.Deg2Rad;
                Vector3 dir = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                vertices[i * 2] = dir * inner;
                vertices[i * 2 + 1] = dir * outer;
                uvs[i * 2] = new Vector2(t, 0f);
                uvs[i * 2 + 1] = new Vector2(t, 1f);
            }
            for (int i = 0; i < Segments; i++)
            {
                int v = i * 2;
                int k = i * 6;
                triangles[k] = v;
                triangles[k + 1] = v + 1;
                triangles[k + 2] = v + 2;
                triangles[k + 3] = v + 1;
                triangles[k + 4] = v + 3;
                triangles[k + 5] = v + 2;
            }

            _mesh = new Mesh();
            _mesh.name = "AstralStaff_RibbonMesh";
            _mesh.MarkDynamic();
            _mesh.vertices = vertices;
            _mesh.uv = uvs;
            _mesh.triangles = triangles;
            _mesh.colors = _colors;
            _mesh.RecalculateBounds();
            Bounds bounds = _mesh.bounds;
            bounds.Expand(outer);
            _mesh.bounds = bounds;

            MeshFilter filter = gameObject.AddComponent<MeshFilter>();
            filter.sharedMesh = _mesh;
            MeshRenderer renderer = gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = AstralStaffFx.Additive(BossRushParticleShape.TrailStrip, BossRushFxKit.GainBright);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            Apply();
        }

        private void Update()
        {
            if (BossRushUI.IsGamePaused()) return;
            _t += Time.unscaledDeltaTime;
            if (_follow != null)
            {
                Vector3 p = _follow.position;
                transform.position = new Vector3(p.x, transform.position.y, p.z);
            }
            Apply();
            if (_t >= _reveal + _hold + _fade) Destroy(gameObject);
        }

        private void Apply()
        {
            if (_mesh == null) return;
            float revealT = Mathf.Clamp01(_t / _reveal);
            float headT = 1f - (1f - revealT) * (1f - revealT);
            float fadeK = 1f - Mathf.Clamp01((_t - _reveal - _hold) / _fade);
            if (_grow)
            {
                float g = Mathf.Lerp(_growFrom, 1f, 1f - Mathf.Pow(1f - Mathf.Clamp01(_t / (_reveal + _hold + _fade)), 3f));
                transform.localScale = new Vector3(g, 1f, g);
            }
            for (int i = 0; i < _segT.Length; i++)
            {
                float t = _segT[i];
                // 刚扫过的位置最亮（刀锋），越往后越暗；头部之前不可见
                float visible = _grow ? 1f : (t <= headT ? 1f : 0f);
                float trail = _grow ? 1f : 0.08f + 0.92f * Mathf.Exp(-Mathf.Max(0f, headT - t) * 6f);
                float edge = Mathf.Min(1f, Mathf.Min(t, 1f - t) * 8f + (_grow ? 1f : 0f));
                float a = _color.a * visible * trail * fadeK * edge;
                Color outer = new Color(_color.r, _color.g, _color.b, a);
                Color inner = new Color(_color.r, _color.g, _color.b, a * _innerAlpha);
                _colors[i * 2] = inner;
                _colors[i * 2 + 1] = outer;
            }
            _mesh.colors = _colors;
        }

        private void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
            _mesh = null;
        }
    }

    // ========================================================================
    // 线条特效（巨型光棍 / 光柱 / 地裂纹）
    // ========================================================================

    /// <summary>多层线条：白芯 + 主色辉光。淡出后自毁。位置可在生命期内由调用方逐帧改写。</summary>
    internal sealed class AstralStaffBeam : MonoBehaviour
    {
        private LineRenderer _core;
        private LineRenderer _glow;
        private float _coreWidth;
        private float _glowWidth;
        private Color _coreColor;
        private Color _glowColor;
        private float _life;
        private float _fade;
        private float _t;
        private bool _useGameTime;

        internal static AstralStaffBeam Create(Vector3[] points, float coreWidth, float glowWidth, Color glowColor,
            float life, float fade, bool flatOnGround, bool useGameTime = false)
        {
            GameObject go = null;
            try
            {
                go = AstralStaffFx.CreateTransient("AstralStaff_Beam");
                if (go == null) return null;
                AstralStaffBeam beam = go.AddComponent<AstralStaffBeam>();
                beam._useGameTime = useGameTime;
                beam.Init(points, coreWidth, glowWidth, glowColor, life, fade, flatOnGround);
                return beam;
            }
            catch (Exception e)
            {
                if (go != null) Destroy(go);
                ModBehaviour.DevLog(AstralStaffConfig.LogPrefix + " [WARNING] 光线失败: " + e.Message);
                return null;
            }
        }

        private void Init(Vector3[] points, float coreWidth, float glowWidth, Color glowColor, float life, float fade, bool flat)
        {
            Material strip = AstralStaffFx.Additive(BossRushParticleShape.TrailStrip, BossRushFxKit.GainBright);
            _coreWidth = coreWidth;
            _glowWidth = glowWidth;
            _glowColor = glowColor;
            _coreColor = Color.Lerp(glowColor, AstralStaffConfig.CoreWhite, 0.6f);
            _coreColor.a = Mathf.Min(0.85f, glowColor.a);
            _life = Mathf.Max(0.01f, life);
            _fade = Mathf.Max(0.01f, fade);
            _glow = AstralStaffFx.CreateLine(transform, "Glow", strip, glowWidth, glowColor, true);
            _core = AstralStaffFx.CreateLine(transform, "Core", strip, coreWidth, _coreColor, true);
            if (flat)
            {
                _glow.alignment = LineAlignment.TransformZ;
                _core.alignment = LineAlignment.TransformZ;
                transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            }
            SetPoints(points);
            Apply(1f);
        }

        internal void SetPoints(Vector3[] points)
        {
            if (points == null || _core == null) return;
            _core.positionCount = points.Length;
            _glow.positionCount = points.Length;
            _core.SetPositions(points);
            _glow.SetPositions(points);
        }

        internal void SetWidthScale(float scale)
        {
            if (_core == null) return;
            _core.widthMultiplier = _coreWidth * scale;
            _glow.widthMultiplier = _glowWidth * scale;
        }

        private void Update()
        {
            if (BossRushUI.IsGamePaused()) return;
            _t += _useGameTime ? Time.deltaTime : Time.unscaledDeltaTime;
            float k = _t <= _life ? 1f : 1f - Mathf.Clamp01((_t - _life) / _fade);
            Apply(k);
            if (_t >= _life + _fade) Destroy(gameObject);
        }

        private void Apply(float k)
        {
            if (_core == null) return;
            Color c = new Color(_coreColor.r, _coreColor.g, _coreColor.b, _coreColor.a * k);
            Color g = new Color(_glowColor.r, _glowColor.g, _glowColor.b, _glowColor.a * k);
            _core.startColor = c;
            _core.endColor = c;
            _glow.startColor = g;
            _glow.endColor = g;
        }
    }
}
