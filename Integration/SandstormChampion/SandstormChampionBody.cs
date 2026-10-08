// ============================================================================
// SandstormChampionBody.cs - 沙尘精外形（纯代码粒子）
// ============================================================================
// 外形由固定容量砂云体积、上升砂晶、绕体碎石、贴地尘裙与两点眼光组成。
// 砂云受光面与暗部独立着色，移除线框皇冠、完整螺旋和光线拖尾。
// 外形根对象跟随 Boss；冲刺砂雾在世界空间脱离，死亡停止刷新后自然消散。
// 第三阶段保留眼光，冲刺时短暂显形。每 0.5 秒复查底模，避免官方再次打开渲染器。
// 2026-10-08 史诗感打磨：出场时砂柱自地面向上长成、眼光随后点燃；眼睛带一圈柔光晕；
// Flare 让眼光在冲锋 / 转阶段时短促爆亮；死亡改为塌落砂环 + 沙瀑 + 贴地冲击 + 一束上升的金色光流。
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    internal sealed class SandstormChampionBody : MonoBehaviour
    {
        private CharacterMainControl _boss;
        private readonly List<Renderer> _hidden = new List<Renderer>();
        private float _nextHideCheck;
        private float _scale = 1f;
        private float _intensity;
        private float _intensityTarget;
        private float _visibility = 1f;
        private float _visibilityTarget = 1f;
        private float _time;
        private bool _released;
        private bool _eyesOnly;
        private bool _charging;
        private bool _minion;
        private SandstormChampionVolume _volume;
        private ParticleSystem _corona;
        private ParticleSystem _wake;

        private ParticleSystem _debris;
        private ParticleSystem _skirt;
        private ParticleSystem _eyes;
        private Light _eyeLight;
        // 两圈柔光晕 + 两颗亮核（光晕在前两位，亮核压在上面）。
        private readonly ParticleSystem.Particle[] _eyeParticles = new ParticleSystem.Particle[4];
        private float _flare;

        internal static SandstormChampionBody Attach(CharacterMainControl boss, float scale, bool minion = false)
        {
            GameObject go = new GameObject("SandstormChampion_Body");
            go.transform.position = boss.transform.position;
            SandstormChampionBody body = go.AddComponent<SandstormChampionBody>();
            body._boss = boss;
            body._scale = Mathf.Max(0.5f, scale);
            body._minion = minion;
            body.HideBaseRenderers();
            try { body.Build(); }
            catch (Exception e) { ModBehaviour.DevLog(SandstormChampionConfig.LogPrefix + "[WARNING] 外形构建失败: " + e.Message); }
            return body;
        }

        /// <summary>0..1：三阶段 1。</summary>
        internal void SetIntensity(float value)
        {
            _intensityTarget = Mathf.Clamp01(value);
        }

        /// <summary>瞬移前后淡出 / 淡入（0..1）。</summary>
        internal void SetVisibility(float value)
        {
            _visibilityTarget = Mathf.Clamp01(value);
        }

        /// <summary>眼光短促爆亮（0..1），之后自己回落：冲锋出手、转阶段咆哮时调用。</summary>
        internal void Flare(float strength)
        {
            _flare = Mathf.Max(_flare, Mathf.Clamp01(strength));
        }

        internal void SetEyesOnly(bool value)
        {
            _eyesOnly = value;
            if (value) ClearSandBody();
        }

        internal void SetCharge(bool value)
        {
            _charging = value;

            if (!value && _eyesOnly) ClearSandBody();
        }

        private void ClearSandBody()
        {
            if (_volume != null) _volume.Clear();
            if (_corona != null) _corona.Clear();
            if (_wake != null) _wake.Clear();
            if (_debris != null) _debris.Clear();
            if (_skirt != null) _skirt.Clear();
        }

        /// <summary>死亡：一圈沙爆，然后所有发射器停发、等粒子死完再销毁。</summary>
        internal void Dissipate()
        {
            if (_released) return;
            _released = true;
            if (_volume != null) _volume.Release();
            try
            {
                if (_minion) PlayMinionCollapse();
                else PlayDeathFinale();
            }
            catch (Exception e) { ModBehaviour.DevLog(SandstormChampionConfig.LogPrefix + "外形退场特效失败: " + e.Message); }
            if (_eyeLight != null)
            {
                // 最后一眼：灯先爆亮，再随整体退场慢慢熄灭。
                _eyeLight.intensity = 6f;
                _eyeLight.range = 9f * _scale;
            }
            BossRushFxKit.Release(gameObject, _minion ? 0.6f : 1.8f, 3.5f, false);
        }

        /// <summary>
        /// 冠军之影倒下：沙身向四周塌成一圈厚砂环，身体高度处的砂团往下泻，贴地冲击环推开，
        /// 两眼最后一闪；中心一束细而高的金色光流缓缓升起——星阙就立在这束光里。
        /// </summary>
        private void PlayDeathFinale()
        {
            Vector3 at = transform.position;
            float s = _scale;

            BossRushFxBurst collapse = BossRushFxKit.Dust(SandstormChampionConfig.Sand, 44);
            collapse.SpeedMin = 5f;
            collapse.SpeedMax = 11f;
            collapse.SizeMin = 1.2f * s;
            collapse.SizeMax = 2.4f * s;
            collapse.LifeMin = 1.4f;
            collapse.LifeMax = 2.2f;
            collapse.Drag = 2.6f;
            collapse.GrowTo = 2.2f;
            collapse.FastGrow = true;
            collapse.ShapeRadius = 1f * s;
            BossRushFxKit.PlayBurst(at + Vector3.up * 0.15f, collapse);

            BossRushFxBurst pour = BossRushFxKit.Dust(SandstormChampionConfig.SandDark, 26);
            pour.Radial = false;
            pour.FlatOnGround = false;
            pour.ShapeRadius = 0.9f * s;
            pour.SpeedMin = 0.4f;
            pour.SpeedMax = 1.8f;
            pour.Gravity = 0.9f;
            pour.Drag = 0.8f;
            pour.SizeMin = 0.7f * s;
            pour.SizeMax = 1.3f * s;
            pour.LifeMin = 1f;
            pour.LifeMax = 1.6f;
            pour.GrowTo = 1.6f;
            BossRushFxKit.PlayBurst(at + Vector3.up * (2.2f * s), pour);

            Color ring = SandstormChampionConfig.EyeColor;
            ring.a = 0.7f;
            BossRushFxKit.PlayBurst(at + Vector3.up * 0.08f, BossRushFxKit.Shockwave(ring, 2f * s, 7.5f, 0.85f, true));

            Vector3 eyes = at + Vector3.up * (2.25f * s);
            BossRushFxKit.PlayBurst(eyes, BossRushFxKit.Glint(SandstormChampionConfig.EyeColor, 2.6f * s, 0.28f));

            BossRushFxBurst soul = BossRushFxKit.Sparks(SandstormChampionConfig.EyeColor, 48);
            soul.Cone = 7f;
            soul.Direction = Vector3.up;
            soul.ShapeRadius = 0.22f;
            soul.SpeedMin = 2.5f;
            soul.SpeedMax = 8f;
            soul.SizeMin = 0.04f;
            soul.SizeMax = 0.085f;
            soul.LifeMin = 1.2f;
            soul.LifeMax = 2f;
            soul.Drag = 0.7f;
            soul.Gravity = -0.2f;
            soul.Stretch = 0.05f;
            soul.GrowTo = 0.35f;
            soul.FadeIn = 0.05f;
            BossRushFxKit.PlayBurst(at + Vector3.up * 0.2f, soul);

            BossRushFxBurst sparks = BossRushFxKit.Sparks(SandstormChampionConfig.Ember, 30);
            sparks.Upward = true;
            sparks.SpeedMin = 6f;
            sparks.SpeedMax = 11f;
            sparks.Gravity = 0.6f;
            BossRushFxKit.PlayBurst(at + Vector3.up * 1.5f, sparks);

            SandstormChampionAssetManager.ShakeNear(at, 0.45f, 30f);
        }

        /// <summary>沙卫散掉：一小圈塌砂与几粒火星，不抢 Boss 的戏。</summary>
        private void PlayMinionCollapse()
        {
            Vector3 at = transform.position;
            BossRushFxBurst dust = BossRushFxKit.Dust(SandstormChampionConfig.Sand, 14);
            dust.SpeedMin = 3f;
            dust.SpeedMax = 6f;
            dust.SizeMin = 0.7f * _scale;
            dust.SizeMax = 1.2f * _scale;
            dust.FastGrow = true;
            BossRushFxKit.PlayBurst(at + Vector3.up * 0.1f, dust);
            BossRushFxBurst sparks = BossRushFxKit.Sparks(SandstormChampionConfig.Ember, 10);
            sparks.Upward = true;
            BossRushFxKit.PlayBurst(at + Vector3.up * (1.2f * _scale), sparks);
        }

        private void HideBaseRenderers()
        {
            if (_boss == null) return;
            try
            {
                Renderer[] renderers = _boss.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < renderers.Length; i++)
                {
                    Renderer r = renderers[i];
                    if (r == null || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                    _hidden.Add(r);
                    r.enabled = false;
                }
            }
            catch (Exception e) { ModBehaviour.DevLog(SandstormChampionConfig.LogPrefix + "底模渲染器隐藏失败: " + e.Message); }
        }

        private void Build()
        {
            Transform root = transform;
            Material wisp = BossRushFxKit.GetShapeMaterial(BossRushParticleShape.Wisp, BossRushFxBlend.Alpha, BossRushFxKit.GainSoft);
            Material shard = BossRushFxKit.GetShapeMaterial(BossRushParticleShape.Shard, BossRushFxBlend.Alpha, BossRushFxKit.GainSoft);
            Material glow = BossRushFxKit.GetShapeMaterial(BossRushParticleShape.GlowDot, BossRushFxBlend.Additive, BossRushFxKit.GainHot);
            float s = _scale;
            _volume = SandstormChampionVolume.Create(root, 0.95f * s, 3f * s, true);
            // 头部是上升的破碎砂冠：不同高度的微小砂晶和雾，保留脸部负空间。
            _corona = BossRushFxKit.CreateEmitter("RisingSandDiadem", root, Vector3.up * (2.45f * s), shard, 24, false);
            if (_corona != null)
            {
                var main = _corona.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.85f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.35f * s, 0.8f * s);
                main.startSize = new ParticleSystem.MinMaxCurve(0.025f * s, 0.07f * s);
                main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                var shape = _corona.shape;
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.radius = 0.48f * s;
                shape.angle = 12f;
                shape.rotation = new Vector3(-90f, 0f, 0f);
                var color = _corona.colorOverLifetime;
                color.enabled = true;
                color.color = BossRushFxKit.FadeGradient(new Color(0.86f, 0.72f, 0.43f, 0.2f),
                    new Color(1f, 0.88f, 0.62f, 0.8f), new Color(0.44f, 0.35f, 0.2f, 0f), 0.18f);
                _corona.Play();
            }
            // 冲刺留下宽而短的砂雾尾迹，颗粒随运动脱离，不拖两根光线。
            _wake = BossRushFxKit.CreateEmitter("DashSandWake", root, Vector3.up * s, wisp, 64, true);
            if (_wake != null)
            {
                var main = _wake.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.22f, 0.45f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.6f * s, 1f * s);
                main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                var color = _wake.colorOverLifetime;
                color.enabled = true;
                color.color = BossRushFxKit.FadeGradient(new Color(0.82f, 0.7f, 0.5f, 0.55f),
                    new Color(0.5f, 0.4f, 0.28f, 0.32f), Color.clear, 0f);
                var size = _wake.sizeOverLifetime;
                size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.65f, 1f, 1.5f));
                _wake.Play();
            }

            // 碎石：绕身公转
            _debris = BossRushFxKit.CreateEmitter("Debris", root, Vector3.up * (1.2f * s), shard, 30, false);
            if (_debris != null)
            {
                ParticleSystem.MainModule main = _debris.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(1.6f, 2.4f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.08f * s, 0.18f * s);
                main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.45f, 0.33f, 0.2f, 0.95f), new Color(0.7f, 0.55f, 0.35f, 0.95f));
                ParticleSystem.EmissionModule emission = _debris.emission;
                emission.rateOverTime = 14f;
                ParticleSystem.ShapeModule shape = _debris.shape;
                shape.shapeType = ParticleSystemShapeType.Circle;
                shape.radius = 1.15f * s;
                shape.rotation = new Vector3(90f, 0f, 0f);
                ParticleSystem.VelocityOverLifetimeModule vel = _debris.velocityOverLifetime;
                vel.enabled = true;
                vel.space = ParticleSystemSimulationSpace.Local;
                vel.orbitalY = new ParticleSystem.MinMaxCurve(2.6f);
                vel.x = new ParticleSystem.MinMaxCurve(0f, 0f);
                vel.y = new ParticleSystem.MinMaxCurve(-0.3f, 0.6f);
                vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);
                ParticleSystem.RotationOverLifetimeModule rot = _debris.rotationOverLifetime;
                rot.enabled = true;
                rot.z = new ParticleSystem.MinMaxCurve(-6f, 6f);
                _debris.Play();
            }

            // 裙尘：贴地外扩（世界空间，移动时拖在身后）
            _skirt = BossRushFxKit.CreateEmitter("Skirt", root, Vector3.up * 0.1f, wisp, 40, true);
            if (_skirt != null)
            {
                ParticleSystem.MainModule main = _skirt.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.4f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 2.2f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.6f * s, 1f * s);
                main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                ParticleSystem.EmissionModule emission = _skirt.emission;
                emission.rateOverTime = 26f;
                ParticleSystem.ShapeModule shape = _skirt.shape;
                shape.shapeType = ParticleSystemShapeType.Circle;
                shape.radius = 0.6f * s;
                shape.rotation = new Vector3(90f, 0f, 0f);
                ParticleSystem.SizeOverLifetimeModule size = _skirt.sizeOverLifetime;
                size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.7f, 1f, 1.9f));
                ParticleSystem.ColorOverLifetimeModule col = _skirt.colorOverLifetime;
                col.enabled = true;
                col.color = BossRushFxKit.FadeGradient(SandstormChampionConfig.Sand, SandstormChampionConfig.SandDark,
                    new Color(0.6f, 0.45f, 0.28f, 0f), 0.2f);
                _skirt.Play();
            }

            // 眼：两点亮核（手动写粒子位置）+ 一盏暖灯
            _eyes = BossRushFxKit.CreateEmitter("Eyes", root, Vector3.zero, glow, _eyeParticles.Length, true);
            if (_eyes != null)
            {
                ParticleSystem.EmissionModule emission = _eyes.emission;
                emission.enabled = false;
                _eyes.Play();
            }
            if (_minion) return; // 沙卫仍有两点粒子眼，不给每名短命小弟再挂动态光。
            GameObject lightGo = new GameObject("EyeLight");
            lightGo.transform.SetParent(root, false);
            lightGo.transform.localPosition = new Vector3(0f, 2.2f * s, 0.4f);
            _eyeLight = lightGo.AddComponent<Light>();
            _eyeLight.type = LightType.Point;
            _eyeLight.color = SandstormChampionConfig.Ember;
            _eyeLight.range = 5f * s;
            _eyeLight.intensity = 1.4f;
            _eyeLight.shadows = LightShadows.None;
        }

        private void LateUpdate()
        {
            if (_released) return;
            if (_boss == null || _boss.Health == null || _boss.Health.IsDead)
            {
                Dissipate();
                return;
            }

            float dt = Time.deltaTime;
            _time += dt;
            _intensity = Mathf.MoveTowards(_intensity, _intensityTarget, dt * 0.8f);
            _visibility = Mathf.MoveTowards(_visibility, _visibilityTarget, dt * 6f);
            _flare = Mathf.MoveTowards(_flare, 0f, dt * 2.4f);

            Transform bt = _boss.transform;
            transform.position = bt.position;
            Vector3 fwd = _boss.modelRoot != null ? _boss.modelRoot.forward : bt.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(fwd.normalized, Vector3.up);

            ApplyRates();
            UpdateEyes(fwd);

            if (Time.time >= _nextHideCheck)
            {
                _nextHideCheck = Time.time + 0.5f;
                for (int i = 0; i < _hidden.Count; i++)
                {
                    Renderer r = _hidden[i];
                    if (r != null && r.enabled) r.enabled = false;
                }
            }
        }

        private void ApplyRates()
        {
            float v = _visibility * (_eyesOnly ? (_charging ? 0.7f : 0f) : 1f) * (_minion ? 0.55f : 1f);
            float k = 1f + _intensity * 0.6f;
            // 出场：砂柱在约 1.2 秒内自地面向上长成，而不是整团凭空出现。
            float rise = Mathf.SmoothStep(0f, 1f, _time / 1.2f);
            if (_volume != null) _volume.SetEnvelope(v, rise, _intensity);
            SetRate(_corona, 22f * k * v);
            SetRate(_wake, _charging ? 100f * _visibility : 0f);
            SetRate(_debris, 14f * k * v);
            SetRate(_skirt, 26f * k * v);
            if (_eyeLight != null)
            {
                _eyeLight.intensity = (1.4f + _intensity * 1.2f + 0.25f * Mathf.Sin(_time * 7f) + _flare * 4f)
                    * _visibility * (_eyesOnly ? 0.45f : 1f) * EyeIgnition();
                _eyeLight.range = (5f + _flare * 4f) * _scale;
                _eyeLight.color = Color.Lerp(SandstormChampionConfig.Ember, new Color(1f, 0.3f, 0.12f), _intensity);
            }
        }

        /// <summary>出场时眼光在砂柱长到头部后才点燃（0..1）。</summary>
        private float EyeIgnition()
        {
            return Mathf.SmoothStep(0f, 1f, (_time - 0.7f) / 0.45f);
        }

        private static void SetRate(ParticleSystem ps, float rate)
        {
            if (ps == null) return;
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = rate;
        }

        private void UpdateEyes(Vector3 fwd)
        {
            if (_eyes == null) return;
            if (fwd.sqrMagnitude < 0.001f) fwd = Vector3.forward;
            fwd.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            Vector3 head = transform.position + Vector3.up * (2.25f * _scale) + fwd * (0.42f * _scale);
            float flicker = 0.85f + 0.15f * Mathf.Sin(_time * 13f);
            float ignition = EyeIgnition();
            Color color = Color.Lerp(SandstormChampionConfig.EyeColor, new Color(1f, 0.35f, 0.15f, 1f), _intensity);
            color.a = _visibility * ignition;
            Color halo = Color.Lerp(SandstormChampionConfig.Ember, new Color(1f, 0.28f, 0.1f, 1f), _intensity);
            halo.a = _visibility * ignition * (0.2f + 0.32f * _flare);
            for (int i = 0; i < _eyeParticles.Length; i++)
            {
                bool isHalo = i < 2;
                ParticleSystem.Particle p = _eyeParticles[i];
                p.position = head + right * ((i % 2 == 0 ? -0.17f : 0.17f) * _scale);
                p.startSize = isHalo
                    ? (0.75f + 0.25f * _intensity + 0.9f * _flare) * _scale * (0.92f + 0.08f * flicker)
                    : (0.22f + 0.06f * _intensity + 0.12f * _flare) * _scale * flicker;
                p.startColor = isHalo ? halo : color;
                p.remainingLifetime = 10f;
                p.startLifetime = 10f;
                _eyeParticles[i] = p;
            }
            _eyes.SetParticles(_eyeParticles, _eyeParticles.Length);
        }
    }
}
