using UnityEngine;

namespace BossRush
{
    /// <summary>沙云的体积与受光层次：固定容量粒子场，不用发亮曲线描绘空气。</summary>
    internal sealed class SandstormChampionVolume : MonoBehaviour
    {
        private ParticleSystem _clouds;
        private ParticleSystem _grains;
        private ParticleSystem.Particle[] _cloudBuffer;
        private ParticleSystem.Particle[] _grainBuffer;
        private float _radius, _height, _age, _nextFrame;
        private float _opacity = 1f, _growth = 1f, _fury;
        private bool _body;

        internal static SandstormChampionVolume Create(Transform parent, float radius, float height, bool body)
        {
            GameObject go = new GameObject(body ? "SovereignSandVolume" : "RollingStormVolume");
            go.transform.SetParent(parent, false);
            var volume = go.AddComponent<SandstormChampionVolume>();
            volume._radius = radius;
            volume._height = height;
            volume._body = body;
            volume._cloudBuffer = new ParticleSystem.Particle[body ? 96 : 192];
            volume._grainBuffer = new ParticleSystem.Particle[body ? 48 : 80];
            volume._clouds = CreateField(go.transform, "LayeredSandMass", BossRushParticleShape.Wisp,
                BossRushFxBlend.Alpha, 1f, volume._cloudBuffer.Length, false);
            volume._grains = CreateField(go.transform, "SuspendedGoldGrains", BossRushParticleShape.Shard,
                BossRushFxBlend.Additive, 1.15f, volume._grainBuffer.Length, false);
            return volume;
        }

        internal static ParticleSystem CreateField(Transform parent, string name, BossRushParticleShape shape,
            BossRushFxBlend blend, float gain, int count, bool flat)
        {
            ParticleSystem ps = BossRushFxKit.CreateEmitter(name, parent, Vector3.zero,
                BossRushFxKit.GetShapeMaterial(shape, blend, gain), count, false);
            if (ps == null) return null;
            var emission = ps.emission;
            emission.enabled = false;
            var spawnShape = ps.shape;
            spawnShape.enabled = false;
            var main = ps.main;
            main.startSpeed = 0f;
            main.startLifetime = 1.2f;
            var color = ps.colorOverLifetime;
            color.enabled = true;
            color.color = BossRushFxKit.FadeGradient(Color.white, Color.white, Color.clear, 0f);
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = flat ? ParticleSystemRenderMode.HorizontalBillboard : ParticleSystemRenderMode.Billboard;
            renderer.sortMode = ParticleSystemSortMode.Distance;
            renderer.maxParticleSize = 1f;
            ps.Play();
            return ps;
        }

        internal void SetEnvelope(float opacity, float growth, float fury)
        {
            _opacity = Mathf.Clamp01(opacity);
            _growth = Mathf.Clamp01(growth);
            _fury = Mathf.Clamp01(fury);
        }

        internal static float SampleHeightDensity(float height)
        {
            // Unity SmoothStep 的前两项是输出值；先把顶部淡出区间归一化，不能当 shader smoothstep 使用。
            return Mathf.SmoothStep(0f, 1f, height / 0.1f)
                * (1f - Mathf.SmoothStep(0f, 1f, (height - 0.83f) / 0.17f));
        }

        private void LateUpdate()
        {
            _age += Time.deltaTime;
            if (Time.time < _nextFrame) return;
            _nextFrame = Time.time + 1f / 30f;
            if (_opacity <= 0.001f)
            {
                if (_clouds != null) _clouds.Clear();
                if (_grains != null) _grains.Clear();
                return;
            }
            for (int i = 0; i < _cloudBuffer.Length; i++)
            {
                float phase = i * 0.618034f;
                float h = Mathf.Repeat(i * 0.754877f + _age * (_body ? 0.11f : 0.07f), 1f);
                float envelope = SampleHeightDensity(h);
                float width = _body ? 0.22f + 0.8f * Mathf.Sin(h * Mathf.PI * 0.86f) : 0.16f + 0.75f * h;
                float angle = phase * Mathf.PI * 2f + _age * (3.4f - h * 1.5f) * (1f + _fury * 0.2f);
                float spread = 0.3f + 0.7f * Mathf.Repeat(i * 0.56984f, 1f);
                float radius = _radius * width * spread;
                Vector3 center = new Vector3(Mathf.Sin(h * 8f + _age * 0.65f), 0f,
                    Mathf.Cos(h * 7f + _age * 0.5f)) * (_radius * 0.07f * h);
                var particle = _cloudBuffer[i];
                particle.position = center + new Vector3(Mathf.Cos(angle) * radius, h * _height * _growth,
                    Mathf.Sin(angle) * radius);
                particle.startSize = _radius * (_body ? 0.7f : 0.55f) * (0.5f + h) * (0.85f + spread * 0.4f);
                particle.rotation = phase * 360f + _age * (i % 2 == 0 ? 18f : -23f);
                // 暗部赭灰，受光面浅砂；亮度差在体积内部，不靠给所有轮廓描金边。
                float light = Mathf.Clamp01(0.48f + 0.38f * Mathf.Cos(angle - 0.8f) + h * 0.14f);
                Color shade = Color.Lerp(new Color(0.19f, 0.15f, 0.12f), new Color(0.77f, 0.66f, 0.47f), light);
                shade.a = envelope * _opacity * Mathf.SmoothStep(0f, 1f, _age / 0.45f) * (_body ? 0.72f : 0.58f);
                particle.startColor = shade;
                particle.startLifetime = particle.remainingLifetime = 1.2f;
                _cloudBuffer[i] = particle;
            }
            for (int i = 0; i < _grainBuffer.Length; i++)
            {
                float h = Mathf.Repeat(i * 0.618034f + _age * 0.21f, 1f);
                float angle = i * 2.39996f + _age * (5f - h * 2f);
                float radius = _radius * (0.2f + h * 0.72f);
                var particle = _grainBuffer[i];
                particle.position = new Vector3(Mathf.Cos(angle) * radius, h * _height * _growth, Mathf.Sin(angle) * radius);
                particle.startSize = (_body ? 0.025f : 0.04f) * (0.7f + Mathf.Repeat(i * 0.43f, 1f));
                particle.rotation = -angle * Mathf.Rad2Deg;
                particle.startColor = new Color(0.93f, 0.78f, 0.47f,
                    _opacity * Mathf.Sin(h * Mathf.PI) * (0.35f + 0.2f * Mathf.Sin(_age * 3f + i)));
                particle.startLifetime = particle.remainingLifetime = 0.7f;
                _grainBuffer[i] = particle;
            }
            if (_clouds != null) _clouds.SetParticles(_cloudBuffer, _cloudBuffer.Length);
            if (_grains != null) _grains.SetParticles(_grainBuffer, _grainBuffer.Length);
        }

        internal void Clear()
        {
            _opacity = 0f;
            if (_clouds != null) _clouds.Clear();
            if (_grains != null) _grains.Clear();
        }

        internal void Release()
        {
            enabled = false; // 停止刷新寿命，让已有砂云自己淡出；对象由外部 owner 的 Release 回收。
        }
    }

    /// <summary>直飞沙龙的砂质躯干、游动尾部和双翼；附着于弹幕，命中后停止刷新。</summary>
    internal sealed class SandstormSharkVisual : MonoBehaviour
    {
        private ParticleSystem _body, _fins;
        private readonly ParticleSystem.Particle[] _bodyBuffer = new ParticleSystem.Particle[24];
        private readonly ParticleSystem.Particle[] _finBuffer = new ParticleSystem.Particle[6];
        private float _age, _nextFrame;

        internal static SandstormSharkVisual Create(Transform parent)
        {
            var go = new GameObject("SandDragonBody");
            go.transform.SetParent(parent, false);
            var visual = go.AddComponent<SandstormSharkVisual>();
            visual._body = SandstormChampionVolume.CreateField(go.transform, "SculptedSandBody",
                BossRushParticleShape.Wisp, BossRushFxBlend.Alpha, 1f, visual._bodyBuffer.Length, false);
            visual._fins = SandstormChampionVolume.CreateField(go.transform, "SandDragonFins",
                BossRushParticleShape.Shard, BossRushFxBlend.Alpha, 1.3f, visual._finBuffer.Length, false);
            return visual;
        }

        private void LateUpdate()
        {
            _age += Time.deltaTime;
            if (Time.time < _nextFrame) return;
            _nextFrame = Time.time + 1f / 30f;
            for (int i = 0; i < _bodyBuffer.Length; i++)
            {
                float t = i / (float)(_bodyBuffer.Length - 1);
                float sway = Mathf.Sin(t * 7f - _age * 12f) * t * 0.3f;
                var p = _bodyBuffer[i];
                p.position = new Vector3(sway + Mathf.Sin(i * 2.4f) * (1f - t) * 0.18f,
                    Mathf.Cos(i * 2.4f) * (1f - t) * 0.15f, -t * 2.6f);
                p.startSize = Mathf.Lerp(1.1f, 0.16f, t);
                p.rotation = i * 137.5f + _age * 35f;
                p.startColor = Color.Lerp(new Color(0.88f, 0.72f, 0.44f, 0.95f),
                    new Color(0.47f, 0.32f, 0.16f, 0.65f), t);
                p.startLifetime = p.remainingLifetime = 0.3f;
                _bodyBuffer[i] = p;
            }
            for (int i = 0; i < _finBuffer.Length; i++)
            {
                float side = i % 2 == 0 ? -1f : 1f;
                float station = i / 2;
                var p = _finBuffer[i];
                p.position = new Vector3(side * (0.48f - station * 0.08f),
                    0.1f + 0.06f * Mathf.Sin(_age * 12f + station), -0.55f - station * 0.7f);
                p.startSize = 0.72f - station * 0.16f;
                p.rotation = side * (35f + station * 12f);
                p.startColor = new Color(0.96f, 0.83f, 0.55f, 0.85f);
                p.startLifetime = p.remainingLifetime = 0.3f;
                _finBuffer[i] = p;
            }
            if (_body != null) _body.SetParticles(_bodyBuffer, _bodyBuffer.Length);
            if (_fins != null) _fins.SetParticles(_finBuffer, _finBuffer.Length);
        }

        internal void Release()
        {
            enabled = false;
            if (_body != null) _body.Clear();
            if (_fins != null) _fins.Clear();
        }
    }

    /// <summary>可读的贴地砂痕边界。范围恒定，纹理和砂粒移动，不绘制悬空金属环。</summary>
    internal sealed class SandstormGroundField : MonoBehaviour
    {
        private ParticleSystem _dust, _edge;
        private readonly ParticleSystem.Particle[] _dustBuffer = new ParticleSystem.Particle[64];
        private readonly ParticleSystem.Particle[] _edgeBuffer = new ParticleSystem.Particle[96];
        private float _radius, _age, _nextFrame, _strength = 1f;

        internal static SandstormGroundField Create(Transform parent, float radius)
        {
            var go = new GameObject("GroundSandPressure");
            go.transform.SetParent(parent, false);
            var field = go.AddComponent<SandstormGroundField>();
            field._radius = radius;
            field._dust = SandstormChampionVolume.CreateField(go.transform, "GroundDust", BossRushParticleShape.Wisp,
                BossRushFxBlend.Alpha, 1f, field._dustBuffer.Length, true);
            field._edge = SandstormChampionVolume.CreateField(go.transform, "ErodedSandBoundary", BossRushParticleShape.Wisp,
                BossRushFxBlend.Additive, 1f, field._edgeBuffer.Length, true);
            return field;
        }

        internal void SetStrength(float value) { _strength = Mathf.Clamp01(value); }
        internal void Release() { enabled = false; }

        private void LateUpdate()
        {
            _age += Time.deltaTime;
            if (Time.time < _nextFrame) return;
            _nextFrame = Time.time + 1f / 30f;
            for (int i = 0; i < _dustBuffer.Length; i++)
            {
                float angle = i * 2.39996f + _age * 0.8f;
                float radius = _radius * Mathf.Sqrt((i + 0.5f) / _dustBuffer.Length);
                var p = _dustBuffer[i];
                p.position = new Vector3(Mathf.Cos(angle) * radius, 0.045f, Mathf.Sin(angle) * radius);
                p.rotation = angle * Mathf.Rad2Deg;
                p.startSize = _radius * 0.45f;
                p.startColor = new Color(0.48f, 0.36f, 0.21f, 0.2f * _strength);
                p.startLifetime = p.remainingLifetime = 0.7f;
                _dustBuffer[i] = p;
            }
            for (int i = 0; i < _edgeBuffer.Length; i++)
            {
                float angle = i * Mathf.PI * 2f / _edgeBuffer.Length + _age * 0.12f;
                var p = _edgeBuffer[i];
                p.position = new Vector3(Mathf.Cos(angle) * _radius, 0.06f, Mathf.Sin(angle) * _radius);
                p.rotation = angle * Mathf.Rad2Deg;
                p.startSize = Mathf.Max(0.13f, _radius * 0.12f);
                p.startColor = new Color(1f, 0.67f, 0.29f, _strength * (0.6f + 0.16f * Mathf.Sin(i * 0.8f + _age * 4f)));
                p.startLifetime = p.remainingLifetime = 0.5f;
                _edgeBuffer[i] = p;
            }
            if (_dust != null) _dust.SetParticles(_dustBuffer, _dustBuffer.Length);
            if (_edge != null) _edge.SetParticles(_edgeBuffer, _edgeBuffer.Length);
        }
    }
}
