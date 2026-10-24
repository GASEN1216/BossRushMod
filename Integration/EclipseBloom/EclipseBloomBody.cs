using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    /// <summary>四瓣蚀日花冠的独立表现 owner；不改角色根、AI、碰撞或攻击时钟。</summary>
    internal sealed class EclipseBloomBody : MonoBehaviour
    {
        private sealed class Splinter
        {
            internal Transform Transform;
            internal Vector3 Position;
            internal Vector3 Velocity;
            internal Vector3 Spin;
            internal float Age;
            internal float Life;
            internal float Scale;
        }

        private CharacterMainControl _boss;
        private EclipseBloomAssetManager _assets;
        private EclipseBloomSectorVisual _sectors;
        private readonly List<Renderer> _hidden = new List<Renderer>();
        private readonly List<bool> _hiddenWasEnabled = new List<bool>();
        private readonly Transform[] _petals = new Transform[EclipseBloomTuning.PetalCount];
        private readonly bool[] _collapsing = new bool[EclipseBloomTuning.PetalCount];
        private readonly float[] _collapseStarted = new float[EclipseBloomTuning.PetalCount];
        private readonly Vector3[] _collapsePosition = new Vector3[EclipseBloomTuning.PetalCount];
        private readonly Quaternion[] _collapseRotation = new Quaternion[EclipseBloomTuning.PetalCount];
        private readonly Splinter[] _splinters = new Splinter[36];
        private readonly Transform[] _corona = new Transform[3];
        private readonly float[] _petalHit = new float[EclipseBloomTuning.PetalCount];
        private float _coreHit;
        private Transform _petalRing;
        private Transform _core;
        private Transform _coreCage;
        private Transform _groundSeal;
        private ParticleSystem _embers;
        private ParticleSystem _ash;
        private Light _light;
        private float _age;
        private float _phaseBlend = 1f;
        private float _rotationDegrees;
        private float _telegraphProgress;
        private float _flare;
        private float _deathAge;
        private float _nextBaseHide;
        private int _phase = 1;
        private int _intactMask = EclipseBloomTuning.AllPetalsMask;
        private int _brokenMask;
        private int _attackMask;
        private int _nextSplinter;
        private int _lastVisualFrame = -1;
        private bool _damaging;
        private bool _released;
        private bool _entrancePulse;
        private bool _deathBurst;

        internal static EclipseBloomBody Attach(CharacterMainControl boss)
        {
            if (boss == null) return null;
            GameObject go = new GameObject("EclipseBloom_Body");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, boss.gameObject.scene);
            go.transform.position = boss.transform.position;
            EclipseBloomBody body = go.AddComponent<EclipseBloomBody>();
            body._boss = boss;
            try
            {
                body.HideBaseRenderers();
                body.Build();
                // 所有持久 Renderer、灯和预建碎片池完成后才采样，破瓣用 SetActive 不会被迷雾恢复。
                ModEntityFogVisibility.Attach(go, boss.transform);
                body.PlayCoalescence();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[EclipseBloom] [WARNING] 外形构建失败: " + e.Message);
                UnityEngine.Object.Destroy(go);
                return null;
            }
            return body;
        }

        internal Vector3 GetPetalPosition(int index)
        {
            // Controller 在 Update 同步受击体，渲染在 LateUpdate；先消费本帧姿态，二者读取同一位置。
            AdvanceVisualFrame();
            if (index < 0 || index >= _petals.Length) return transform.position + Vector3.up;
            if (_petals[index] != null) return _petals[index].position;
            float x, z;
            EclipseBloomRules.GetSectorPoint(index, _rotationDegrees, EclipseBloomTuning.PetalOrbitRadius, 0f, out x, out z);
            return transform.position + new Vector3(x, EclipseBloomTuning.PetalCenterHeight, z);
        }

        internal Quaternion GetPetalRotation(int index)
        {
            AdvanceVisualFrame();
            return index >= 0 && index < _petals.Length && _petals[index] != null
                ? _petals[index].rotation : Quaternion.identity;
        }

        internal Vector3 GetCorePosition()
        {
            AdvanceVisualFrame();
            return _coreCage != null ? _coreCage.position : transform.position + Vector3.up;
        }

        internal Vector3 GetPetalScale(int index)
        {
            AdvanceVisualFrame();
            return index >= 0 && index < _petals.Length && _petals[index] != null
                ? _petals[index].lossyScale : Vector3.one;
        }

        internal float GetCoreRadius()
        {
            AdvanceVisualFrame();
            // 外壳径向基准 0.93 m，切面起伏不超过 4.5%；同步凝聚与死亡内缩包络。
            return _coreCage != null ? Mathf.Max(0.001f, _coreCage.lossyScale.x * 0.97f) : 0.97f;
        }

        internal void ApplyFrame(int phase, int intactMask, float rotationDegrees, int attackMask, float telegraphProgress, bool damaging)
        {
            if (_released) return;
            AdvanceVisualFrame();
            int nextPhase = Mathf.Clamp(phase, 1, 3);
            bool changedPhase = nextPhase != _phase;
            _phase = nextPhase;
            int lost = _intactMask & ~intactMask & EclipseBloomTuning.AllPetalsMask;
            for (int i = 0; i < _petals.Length; i++) if ((lost & (1 << i)) != 0) PlayPetalBreak(i);
            _intactMask &= intactMask & EclipseBloomTuning.AllPetalsMask;
            _rotationDegrees = rotationDegrees;
            _attackMask = attackMask & EclipseBloomTuning.AllPetalsMask;
            // 新一轮预警从 0 亮起的那一帧响一声起手音。
            if (!damaging && _telegraphProgress <= 0f && telegraphProgress > 0f)
                SkyIslandBossSfx.Play(null, SkyIslandBossCue.Telegraph, transform.position);
            _telegraphProgress = Mathf.Clamp01(telegraphProgress);
            bool erupting = damaging && !_damaging;
            if (erupting) _flare = 1f;
            _damaging = damaging;
            if (changedPhase)
            {
                _flare = 1f;
                PlayPhaseChange();
            }
            // 预警在帧入口立即更新，不等待表现平滑；刚破坏的区域当帧熄灭。
            if (_petalRing != null) _petalRing.localRotation = Quaternion.Euler(0f, _rotationDegrees, 0f);
            ApplySectors();
            if (erupting) PlayEruption();
        }

        /// <summary>
        /// 扇区开始出伤的那一帧：核心沿每个生效扇区的中线喷出一道熔金日焰，地面沿线翻起灰烬，镜头轻震。
        /// 预警期全部是「慢慢亮起来」，出伤必须有一记明确的爆发，玩家才分得清「已经在打了」。
        /// </summary>
        private void PlayEruption()
        {
            int eligible = _phase == 3 || _intactMask == 0 ? EclipseBloomTuning.AllPetalsMask : _intactMask & ~_brokenMask;
            int mask = eligible & _attackMask;
            if (mask == 0) return;
            Vector3 origin = transform.position + Vector3.up * 0.9f;
            for (int i = 0; i < EclipseBloomTuning.PetalCount; i++)
            {
                if ((mask & (1 << i)) == 0) continue;
                float x, z;
                EclipseBloomRules.GetSectorPoint(i, _rotationDegrees, 1f, 0f, out x, out z);
                Vector3 direction = new Vector3(x, 0f, z);
                BossRushFxBurst jet = BossRushFxKit.Sparks(EclipseBloomVisuals.Sun, 30);
                jet.Cone = EclipseBloomTuning.HalfAngleDegrees * 0.75f;
                jet.Direction = direction + Vector3.up * 0.06f;
                jet.ShapeRadius = 0.35f;
                jet.SpeedMin = 13f; jet.SpeedMax = 22f;
                jet.Drag = 1.6f; jet.LifeMin = 0.4f; jet.LifeMax = 0.65f;
                jet.SizeMin = 0.05f; jet.SizeMax = 0.11f; jet.Stretch = 0.05f;
                Burst(origin, jet);
                for (int step = 0; step < 3; step++)
                {
                    float radius = 3.5f + step * 3.6f;
                    Vector3 ground = transform.position + direction * radius + Vector3.up * 0.15f;
                    BossRushFxBurst ash = BossRushFxKit.Dust(new Color(0.95f, 0.42f, 0.08f, 0.32f), 5);
                    ash.Radial = false; ash.FlatOnGround = false; ash.Upward = true;
                    ash.ShapeRadius = 0.6f + step * 0.35f;
                    ash.SpeedMin = 1.5f; ash.SpeedMax = 3.2f;
                    ash.SizeMin = 0.5f; ash.SizeMax = 0.95f; ash.LifeMin = 0.45f; ash.LifeMax = 0.8f;
                    Burst(ground, ash);
                }
            }
            Burst(origin, BossRushFxKit.Glint(EclipseBloomVisuals.PaleGold, 1.9f, 0.16f));
            SandstormChampionAssetManager.ShakeNear(transform.position, _phase == 3 ? 0.26f : 0.18f, 18f);
            SkyIslandBossSfx.Play(null, SkyIslandBossCue.Impact, transform.position);
        }

        /// <summary>
        /// 核心中弹：一小簇向外崩的金屑，核心外壳短促一胀。频率由控制器限流。
        /// guarded 时花瓣仍在护核：改成一圈苍白的日冕护壁闪光、火星更少，读得出「这下没打疼」。
        /// </summary>
        internal void PlayCoreHit(Vector3 point, bool guarded)
        {
            if (_released || _coreCage == null) return;
            _coreHit = guarded ? 0.35f : 1f;
            Vector3 center = _coreCage.position;
            Vector3 outward = point - center;
            if (outward.sqrMagnitude < 0.0001f) outward = Vector3.up;
            if (guarded)
            {
                Burst(center + outward.normalized * GetCoreRadius(),
                    BossRushFxKit.Shockwave(new Color(1f, 0.92f, 0.7f, 0.55f), GetCoreRadius() * 1.4f, 1.25f, 0.18f, false));
            }
            BossRushFxBurst chips = BossRushFxKit.Sparks(guarded ? new Color(1f, 0.95f, 0.8f) : EclipseBloomVisuals.PaleGold, guarded ? 3 : 7);
            chips.Cone = 40f; chips.Direction = outward;
            chips.SpeedMin = 3f; chips.SpeedMax = 6.5f; chips.Gravity = 0.8f;
            chips.LifeMin = 0.2f; chips.LifeMax = 0.4f;
            Burst(center + outward.normalized * GetCoreRadius(), chips);
        }

        /// <summary>花瓣中弹：该瓣短暂提亮、微微一缩，掉几点熔金。</summary>
        internal void PlayPetalHit(int index, Vector3 point)
        {
            if (_released || index < 0 || index >= _petals.Length || (_brokenMask & (1 << index)) != 0) return;
            bool fresh = _petalHit[index] < 0.55f;
            _petalHit[index] = 1f;
            if (!fresh || _petals[index] == null) return;
            BossRushFxBurst chips = BossRushFxKit.Sparks(EclipseBloomVisuals.Sun, 5);
            chips.SpeedMin = 2f; chips.SpeedMax = 4.5f; chips.Gravity = 1.1f;
            chips.LifeMin = 0.25f; chips.LifeMax = 0.45f;
            Burst(point.sqrMagnitude > 0.0001f ? point : _petals[index].position, chips);
        }

        internal void PlayPetalBreak(int index)
        {
            if (_released || index < 0 || index >= _petals.Length) return;
            int bit = 1 << index;
            if ((_brokenMask & bit) != 0) return;
            _brokenMask |= bit;
            _intactMask &= ~bit;
            Vector3 at = GetPetalPosition(index);
            Vector3 outward = at - transform.position;
            outward.y = 0f;
            outward = outward.sqrMagnitude > 0.01f ? outward.normalized : Vector3.forward;
            SpawnSplinters(at, outward, 7, 0.65f);
            if (_petals[index] != null)
            {
                if (_phase == 3)
                {
                    _collapsing[index] = true;
                    _collapseStarted[index] = _age;
                    _collapsePosition[index] = _petals[index].localPosition;
                    _collapseRotation[index] = _petals[index].localRotation;
                }
                else _petals[index].gameObject.SetActive(false);
            }
            ApplySectors();
            _flare = 0.85f;
            BossRushFxBurst sparks = BossRushFxKit.Sparks(EclipseBloomVisuals.PaleGold, 32);
            sparks.Cone = 58f; sparks.Direction = outward + Vector3.up * 0.3f;
            sparks.SpeedMin = 3f; sparks.SpeedMax = 8f;
            sparks.LifeMin = 0.35f; sparks.LifeMax = 0.8f;
            sparks.Gravity = 0.65f; sparks.Drag = 1.3f;
            Burst(at, sparks);
            BossRushFxBurst ash = BossRushFxKit.Dust(EclipseBloomVisuals.Ash, 12);
            ash.FlatOnGround = false; ash.Radial = false; ash.Upward = true;
            ash.SizeMin = 0.25f; ash.SizeMax = 0.65f; ash.ShapeRadius = 0.3f;
            Burst(at, ash);
            Burst(at, BossRushFxKit.Glint(EclipseBloomVisuals.PaleGold, 1.2f, 0.18f));
        }

        /// <summary>死亡先内缩约半秒，随后黑日冲击和熔金碎屑；非死亡清理直接 Destroy 根对象。</summary>
        internal void Dissipate()
        {
            if (_released) return;
            _released = true;
            _deathAge = 0f;
            if (_sectors != null) _sectors.Hide();
            StopEmitter(_embers);
            StopEmitter(_ash);
            BossRushFxBurst inward = BossRushFxKit.Sparks(EclipseBloomVisuals.Sun, 45);
            inward.ShapeRadius = 2.3f; inward.ShellOnly = true;
            inward.SpeedMin = -5f; inward.SpeedMax = -3.4f;
            inward.LifeMin = 0.3f; inward.LifeMax = 0.5f;
            inward.Drag = 0.2f; inward.Stretch = 0.045f; inward.SizeMin = 0.025f; inward.SizeMax = 0.06f;
            Burst(transform.position + Vector3.up * 0.85f, inward);
        }

        private void Build()
        {
            _assets = new EclipseBloomAssetManager(_boss);
            _petalRing = new GameObject("FourPetalCrown").transform;
            _petalRing.SetParent(transform, false);
            for (int i = 0; i < _petals.Length; i++)
            {
                Transform petal = new GameObject("MetalPetal_" + i).transform;
                petal.SetParent(_petalRing, false);
                _petals[i] = petal;
                EclipseBloomAssetManager.AddMesh("FacetedLeafShell", petal, _assets.Petal, _assets.Obsidian);
                // 金属底脊独立于发光裂纹，保留厚度和受光面。
                Transform rib = EclipseBloomAssetManager.AddMesh("GoldUnderRib", petal, _assets.Petal, _assets.Gold).transform;
                rib.localPosition = new Vector3(0f, -0.045f, 0f);
                rib.localScale = new Vector3(0.14f, 1.06f, 0.95f);
                EclipseBloomVisuals.AddPetalInlay(petal);
            }
            _coreCage = new GameObject("BlackSunCage").transform;
            _coreCage.SetParent(transform, false);
            _core = EclipseBloomAssetManager.AddMesh("ObsidianSunCore", _coreCage, _assets.Core, _assets.Obsidian).transform;
            _core.localScale = Vector3.one * 0.93f;
            for (int i = 0; i < _corona.Length; i++)
            {
                Color color = i == 0 ? EclipseBloomVisuals.PaleGold : EclipseBloomVisuals.Sun;
                color.a = i == 0 ? 0.95f : 0.6f;
                _corona[i] = EclipseBloomVisuals.Line("RaggedSolarLimb_" + i, _coreCage,
                    EclipseBloomVisualGeometry.Circle(1.04f + i * 0.075f, 96, 0f, 0.045f + i * 0.025f),
                    color, i == 0 ? 0.045f : 0.018f, true).transform;
            }
            _groundSeal = new GameObject("GroundEclipseProjection").transform;
            _groundSeal.SetParent(transform, false);
            _groundSeal.localPosition = Vector3.up * 0.025f;
            Mesh shadowMesh = _assets.Own(EclipseBloomVisuals.CreateDisc("EclipseBloom_SoftGroundShadow", 2.6f));
            MeshRenderer shadow = EclipseBloomAssetManager.AddMesh("BlackPenumbra", _groundSeal, shadowMesh,
                BossRushFxMaterials.Get(BossRushFxBlend.Alpha));
            shadow.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            shadow.receiveShadows = false;
            EclipseBloomVisuals.Tint(shadow, new MaterialPropertyBlock(), new Color(0.016f, 0.008f, 0.025f, 0.5f));
            EclipseBloomVisuals.Line("InnerCoronaReflection", _groundSeal,
                EclipseBloomVisualGeometry.Circle(1.18f, 80, 0.005f, 0.025f), new Color(0.9f, 0.4f, 0.06f, 0.3f), 0.035f, true);
            EclipseBloomVisuals.Line("OuterCoronaReflection", _groundSeal,
                EclipseBloomVisualGeometry.Circle(1.8f, 80, 0.006f, 0.015f), new Color(0.75f, 0.27f, 0.035f, 0.2f), 0.018f, true);
            _sectors = new EclipseBloomSectorVisual(transform, _assets);
            BuildParticles();
            for (int i = 0; i < _splinters.Length; i++)
            {
                MeshRenderer renderer = EclipseBloomAssetManager.AddMesh("PooledMetalSplinter_" + i, transform,
                    _assets.Fragment, i % 4 == 0 ? _assets.Gold : _assets.Obsidian);
                _splinters[i] = new Splinter { Transform = renderer.transform };
                renderer.gameObject.SetActive(false);
            }
            GameObject lightGo = new GameObject("SolarLimbLight");
            lightGo.transform.SetParent(_coreCage, false);
            _light = lightGo.AddComponent<Light>();
            _light.type = LightType.Point; _light.color = EclipseBloomVisuals.Sun;
            _light.range = 4.5f; _light.intensity = 0f; _light.shadows = LightShadows.None;
            ApplyShape();
        }

        private void BuildParticles()
        {
            _embers = BossRushFxKit.CreateEmitter("OrbitingSolarCinders", transform, Vector3.up,
                BossRushFxKit.GetShapeMaterial(BossRushParticleShape.GlowDot, BossRushFxBlend.Additive, BossRushFxKit.GainHot), 72, false);
            EclipseBloomVisuals.ConfigureEmitter(_embers, new Color(1f, 0.56f, 0.15f, 0.85f), 0.025f, 0.065f, 1.1f, 2f);
            if (_embers != null)
            {
                var shape = _embers.shape;
                shape.shapeType = ParticleSystemShapeType.Circle;
                shape.radius = 1.45f; shape.rotation = new Vector3(90f, 0f, 0f);
                var velocity = _embers.velocityOverLifetime;
                velocity.enabled = true; velocity.space = ParticleSystemSimulationSpace.Local;
                velocity.orbitalY = new ParticleSystem.MinMaxCurve(1.1f);
                // Unity 要求线性速度三轴使用同一种曲线模式；零横向速度也显式使用随机区间。
                velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
                velocity.y = new ParticleSystem.MinMaxCurve(-0.08f, 0.3f);
                velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);
                var emission = _embers.emission; emission.rateOverTime = 25f;
                _embers.Play();
            }
            _ash = BossRushFxKit.CreateEmitter("DriftingBlackAsh", transform, Vector3.up * 1.5f,
                BossRushFxKit.GetShapeMaterial(BossRushParticleShape.Shard, BossRushFxBlend.Alpha, BossRushFxKit.GainSoft), 44, false);
            EclipseBloomVisuals.ConfigureEmitter(_ash, new Color(0.12f, 0.085f, 0.12f, 0.7f), 0.04f, 0.13f, 1.4f, 2.7f);
            if (_ash != null)
            {
                var shape = _ash.shape; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = 1.25f;
                var main = _ash.main; main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.3f); main.gravityModifier = 0.035f;
                var velocity = _ash.velocityOverLifetime;
                velocity.enabled = true; velocity.space = ParticleSystemSimulationSpace.Local;
                velocity.orbitalY = new ParticleSystem.MinMaxCurve(-0.45f);
                var rotation = _ash.rotationOverLifetime; rotation.enabled = true; rotation.z = new ParticleSystem.MinMaxCurve(-2f, 2f);
                var emission = _ash.emission; emission.rateOverTime = 12f;
                _ash.Play();
            }
        }

        private void PlayCoalescence()
        {
            BossRushFxBurst inward = BossRushFxKit.Sparks(EclipseBloomVisuals.Sun, 64);
            inward.ShapeRadius = 3f; inward.ShellOnly = true;
            inward.SpeedMin = -3.6f; inward.SpeedMax = -2.4f;
            inward.LifeMin = 0.6f; inward.LifeMax = 1f; inward.Drag = 0.2f;
            inward.SizeMin = 0.025f; inward.SizeMax = 0.07f;
            Burst(transform.position + Vector3.up * 0.8f, inward);
            BossRushFxBurst smoke = BossRushFxKit.Dust(EclipseBloomVisuals.Ash, 20);
            smoke.SpeedMin = 0.5f; smoke.SpeedMax = 1.3f; smoke.SizeMin = 0.5f; smoke.SizeMax = 0.95f;
            smoke.LifeMin = 1.2f; smoke.LifeMax = 2f; smoke.ShapeRadius = 1.5f;
            Burst(transform.position + Vector3.up * 0.08f, smoke);
        }

        private void PlayPhaseChange()
        {
            SandstormChampionAssetManager.ShakeNear(transform.position, 0.3f, 22f);
            SkyIslandBossSfx.Play(null, SkyIslandBossCue.Phase, transform.position);
            Burst(transform.position + Vector3.up * 0.08f,
                BossRushFxKit.Shockwave(new Color(1f, 0.48f, 0.08f, 0.6f), 1.4f, 3.1f, 0.65f, true));
            BossRushFxBurst jets = BossRushFxKit.Sparks(EclipseBloomVisuals.PaleGold, _phase == 3 ? 42 : 28);
            jets.Upward = true; jets.SpeedMin = 2f; jets.SpeedMax = 5f;
            jets.LifeMin = 0.55f; jets.LifeMax = 1.1f; jets.Gravity = 0.5f; jets.Drag = 0.8f;
            Burst(transform.position + Vector3.up * 0.6f, jets);
            if (_phase == 3)
                for (int i = 0; i < _petals.Length; i++)
                    if ((_intactMask & (1 << i)) != 0) SpawnSplinters(GetPetalPosition(i), Vector3.up, 3, 0.35f);
        }

        private void LateUpdate()
        {
            AdvanceVisualFrame();
        }

        private void AdvanceVisualFrame()
        {
            if (_lastVisualFrame == Time.frameCount) return;
            _lastVisualFrame = Time.frameCount;
            float dt = Mathf.Min(Time.deltaTime, EclipseBloomTuning.MaximumStepSeconds);
            if (dt <= 0f || BossRushUI.IsGamePaused()) return;
            if (!_released)
            {
                if (_boss == null) { Destroy(gameObject); return; }
                if (_boss.Health != null && _boss.Health.IsDead) Dissipate();
                else transform.position = _boss.transform.position;
            }
            if (!_released && _sectors != null) _sectors.PrepareGround();
            _age += dt;
            _flare = Mathf.MoveTowards(_flare, 0f, dt * 2f);
            _coreHit = Mathf.MoveTowards(_coreHit, 0f, dt * 7f);
            for (int i = 0; i < _petalHit.Length; i++) _petalHit[i] = Mathf.MoveTowards(_petalHit[i], 0f, dt * 6f);
            _phaseBlend = Mathf.MoveTowards(_phaseBlend, _phase, dt * 0.85f);
            if (!_released && !_entrancePulse && _age >= 1.12f)
            {
                _entrancePulse = true;
                _flare = 0.7f;
                Burst(transform.position + Vector3.up * 0.06f,
                    BossRushFxKit.Shockwave(new Color(1f, 0.58f, 0.15f, 0.5f), 1.1f, 3f, 0.7f, true));
            }
            if (_released)
            {
                _deathAge += dt;
                if (!_deathBurst && _deathAge >= 0.58f) PlayDeathBurst();
                if (_deathAge > 4f) { Destroy(gameObject); return; }
            }
            ApplyShape();
            UpdateSplinters(dt);
            if (!_released && _age >= _nextBaseHide)
            {
                _nextBaseHide = _age + 0.5f;
                for (int i = 0; i < _hidden.Count; i++) if (_hidden[i] != null) _hidden[i].enabled = false;
            }
        }

        private void ApplyShape()
        {
            if (_coreCage == null) return;
            float birth = Mathf.SmoothStep(0f, 1f, _age / 1.15f);
            float shrink = _released ? Mathf.Pow(1f - Mathf.Clamp01(_deathAge / 0.58f), 1.7f) : 1f;
            float pulse = 1f + Mathf.Sin(_age * (2f + _phaseBlend)) * 0.022f + _flare * 0.055f + _coreHit * 0.045f;
            float lower = Mathf.Clamp01(_phaseBlend - 2f);
            _coreCage.localPosition = Vector3.up * Mathf.Lerp(1.28f, 0.78f, lower);
            _coreCage.localScale = Vector3.one * Mathf.Max(0.001f, birth * shrink * pulse);
            _core.localRotation = Quaternion.Euler(13f + _age * 2f, _age * 9f, 8f + Mathf.Sin(_age * 0.6f) * 7f);
            for (int i = 0; i < _corona.Length; i++)
                _corona[i].localRotation = Quaternion.Euler(12f + i * 53f, _age * (i % 2 == 0 ? 7f : -9f), 15f + i * 17f);
            if (_petalRing != null) _petalRing.localRotation = Quaternion.Euler(0f, _rotationDegrees, 0f);
            for (int i = 0; i < _petals.Length; i++)
            {
                Transform petal = _petals[i];
                if (petal == null) continue;
                if ((_brokenMask & (1 << i)) != 0)
                {
                    if (_collapsing[i])
                    {
                        float fall = Mathf.Clamp01((_age - _collapseStarted[i]) / 0.9f);
                        Vector3 outward = _collapsePosition[i]; outward.y = 0f;
                        if (outward.sqrMagnitude > 0.001f) outward.Normalize();
                        petal.localPosition = _collapsePosition[i] + outward * (fall * 0.85f) + Vector3.down * (fall * fall * 0.75f);
                        petal.localRotation = _collapseRotation[i] * Quaternion.Euler(110f * fall, 0f, (i % 2 == 0 ? 55f : -55f) * fall);
                        petal.localScale = Vector3.one * (1f - Mathf.SmoothStep(0f, 1f, (fall - 0.45f) / 0.55f));
                        if (fall >= 1f || _deathBurst) { _collapsing[i] = false; petal.gameObject.SetActive(false); }
                    }
                    continue;
                }
                float unfold = Mathf.SmoothStep(0f, 1f, (_age - 0.24f - i * 0.09f) / 1.1f);
                Quaternion angle = Quaternion.Euler(0f, EclipseBloomRules.GetSectorCenterDegrees(i, 0f), 0f);
                float orbit = Mathf.Lerp(0.55f, EclipseBloomTuning.PetalOrbitRadius, unfold) * shrink;
                petal.localPosition = angle * new Vector3(0f, EclipseBloomTuning.PetalCenterHeight, orbit);
                // 翻瓣只绕叶片中心转动，受击中心始终在 1 m；阶段三外翻、向地面崩落。
                float open = Mathf.Clamp01(_phaseBlend - 1f);
                float pitch = Mathf.Lerp(-8f, 8f, open) + lower * 49f;
                float roll = Mathf.Lerp(0f, i % 2 == 0 ? 28f : -28f, open) * (1f - lower * 0.5f);
                petal.localRotation = angle * Quaternion.Euler(pitch, 0f, roll);
                petal.localScale = Vector3.one * Mathf.Max(0.001f, unfold * shrink * (1f - _petalHit[i] * 0.06f));
                if (_deathBurst) petal.gameObject.SetActive(false);
            }
            if (_groundSeal != null)
                _groundSeal.localScale = Vector3.one * (birth * (_released ? Mathf.Max(0f, 1f - _deathAge / 0.7f) : 1f));
            if (_light != null)
                _light.intensity = _deathBurst ? Mathf.Max(0f, 4f - (_deathAge - 0.58f) * 10f)
                    : (1.25f + _flare * 2.5f + _coreHit * 0.9f + _telegraphProgress * 0.25f) * birth * shrink;
        }

        private void PlayDeathBurst()
        {
            _deathBurst = true;
            Vector3 at = transform.position + Vector3.up * 0.78f;
            SpawnSplinters(at, Vector3.up, 18, 0.8f);
            Burst(at, BossRushFxKit.Glint(EclipseBloomVisuals.PaleGold, 2.3f, 0.2f));
            BossRushFxBurst blackSun = BossRushFxKit.Dust(new Color(0.025f, 0.01f, 0.035f, 0.75f), 32);
            blackSun.SpeedMin = 4f; blackSun.SpeedMax = 7f; blackSun.Drag = 2.5f;
            blackSun.SizeMin = 0.7f; blackSun.SizeMax = 1.2f; blackSun.LifeMin = 0.9f; blackSun.LifeMax = 1.8f;
            blackSun.FastGrow = true;
            Burst(transform.position + Vector3.up * 0.1f, blackSun);
            Burst(transform.position + Vector3.up * 0.065f,
                BossRushFxKit.Shockwave(new Color(1f, 0.49f, 0.08f, 0.7f), 0.6f, 10f, 0.8f, true));
            BossRushFxBurst embers = BossRushFxKit.Sparks(EclipseBloomVisuals.Sun, 76);
            embers.Upward = true; embers.SpeedMin = 3f; embers.SpeedMax = 8f;
            embers.Gravity = 0.75f; embers.Drag = 0.75f; embers.LifeMin = 1.2f; embers.LifeMax = 2.7f;
            embers.SizeMin = 0.03f; embers.SizeMax = 0.075f;
            Burst(at, embers);
            SandstormChampionAssetManager.ShakeNear(transform.position, 0.3f, 24f);
            SkyIslandBossSfx.Play(null, SkyIslandBossCue.Shatter, at);
            // 熔金余烬之外再升起一束细长的日光，作为「花冠已死」的收束。
            BossRushFxBurst soul = BossRushFxKit.Sparks(EclipseBloomVisuals.PaleGold, 36);
            soul.Cone = 6f; soul.Direction = Vector3.up; soul.ShapeRadius = 0.2f;
            soul.SpeedMin = 2.5f; soul.SpeedMax = 7f; soul.Drag = 0.7f; soul.Gravity = -0.15f;
            soul.LifeMin = 1.1f; soul.LifeMax = 1.8f; soul.SizeMin = 0.035f; soul.SizeMax = 0.075f;
            soul.Stretch = 0.05f; soul.GrowTo = 0.35f; soul.FadeIn = 0.05f;
            Burst(transform.position + Vector3.up * 0.3f, soul);
        }

        private void SpawnSplinters(Vector3 position, Vector3 outward, int count, float scale)
        {
            if (_assets == null) return;
            for (int i = 0; i < count; i++)
            {
                Splinter shard = _splinters[_nextSplinter++ % _splinters.Length];
                if (shard == null) continue;
                shard.Position = position + UnityEngine.Random.insideUnitSphere * 0.35f;
                shard.Velocity = outward * UnityEngine.Random.Range(1.5f, 3.5f)
                    + UnityEngine.Random.insideUnitSphere * 2f + Vector3.up * UnityEngine.Random.Range(1.5f, 4f);
                shard.Spin = UnityEngine.Random.onUnitSphere * UnityEngine.Random.Range(70f, 220f);
                shard.Age = 0f; shard.Life = UnityEngine.Random.Range(1.2f, 2.5f);
                shard.Scale = UnityEngine.Random.Range(0.65f, 1.25f) * scale;
                shard.Transform.position = shard.Position;
                shard.Transform.rotation = UnityEngine.Random.rotation;
                shard.Transform.localScale = Vector3.one * shard.Scale;
                shard.Transform.gameObject.SetActive(true);
            }
        }

        private void UpdateSplinters(float dt)
        {
            for (int i = 0; i < _splinters.Length; i++)
            {
                Splinter shard = _splinters[i];
                if (shard == null || !shard.Transform.gameObject.activeSelf) continue;
                shard.Age += dt;
                if (shard.Age >= shard.Life) { shard.Transform.gameObject.SetActive(false); continue; }
                shard.Velocity += Vector3.down * (6f * dt);
                shard.Velocity *= Mathf.Exp(-0.7f * dt);
                shard.Position += shard.Velocity * dt;
                if (shard.Position.y < transform.position.y + 0.05f)
                {
                    shard.Position.y = transform.position.y + 0.05f;
                    shard.Velocity.y = Mathf.Abs(shard.Velocity.y) * 0.18f;
                    shard.Velocity.x *= 0.85f; shard.Velocity.z *= 0.85f;
                }
                shard.Transform.position = shard.Position;
                shard.Transform.Rotate(shard.Spin * dt, Space.Self);
                shard.Transform.localScale = Vector3.one * (shard.Scale * (1f - Mathf.SmoothStep(0f, 1f, (shard.Age / shard.Life - 0.55f) / 0.45f)));
            }
        }

        private void Burst(Vector3 position, BossRushFxBurst spec)
        {
            ParticleSystem ps = BossRushFxKit.PlayBurst(position, spec);
            if (ps == null) return;
            ps.transform.SetParent(transform, true);
            ModEntityFogVisibility.Attach(ps.gameObject, _boss != null ? _boss.transform : transform);
        }

        private void ApplySectors()
        {
            if (_sectors == null) return;
            // 全破瓣或阶段三会进入独立核心攻击；它的 5 / 10 扇区由规则重新预警后才生效。
            int eligible = _phase == 3 || _intactMask == 0 ? EclipseBloomTuning.AllPetalsMask : _intactMask & ~_brokenMask;
            _sectors.Apply(eligible, _attackMask, _rotationDegrees, _telegraphProgress, _damaging);
        }

        internal bool HasGroundWarningAt(Vector3 position)
        {
            return !_released && _sectors != null && _sectors.CoversPoint(position);
        }

        private static void StopEmitter(ParticleSystem ps)
        {
            if (ps != null) ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
        }

        private void HideBaseRenderers()
        {
            Renderer[] renderers = _boss.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || renderer is ParticleSystemRenderer || renderer is LineRenderer || renderer is TrailRenderer) continue;
                _hidden.Add(renderer);
                _hiddenWasEnabled.Add(renderer.enabled);
                renderer.enabled = false;
            }
        }

        private void OnDestroy()
        {
            // 取消遭遇但角色仍存活时恢复底模；死亡尸体的原始外观仍交官方 owner。
            if (_boss != null && _boss.Health != null && !_boss.Health.IsDead)
                for (int i = 0; i < _hidden.Count; i++) if (_hidden[i] != null) _hidden[i].enabled = _hiddenWasEnabled[i];
            if (_assets != null) _assets.Dispose();
            _assets = null;
            _boss = null;
            _hidden.Clear(); _hiddenWasEnabled.Clear();
        }
    }
}
