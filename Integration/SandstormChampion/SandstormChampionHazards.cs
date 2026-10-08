// ============================================================================
// SandstormChampionHazards.cs - 冠军之影的场上危险物（纯代码表现）
// ============================================================================
//   SandstormDashWarning —— 冲刺前贴地的预警带：宽带 + 白芯 + 沿线流动的亮点，到点闪一下。
//   SandstormOrb         —— 一击可破的追踪沙泡 / 可击破直飞沙鲨；撞墙、命中或到时消散。
//   SandstormCycloneSeed —— 双生 / 单枚贴地追踪沙圈；接触或到时才开始沙柱地圈预警。
//   SandstormTornado     —— 固定落地点的沙卷柱 / 沙暴核；自身 TTL 内分别喷出 6 / 12 只沙鲨。
//   SandstormAmbience    —— 三阶段的漫天沙暴：跟着玩家的大范围横飞沙尘，把场子压暗。
// 伤害一律回调给控制器（SandstormChampionController.HurtPlayer），倍率与死亡处理只在一处。
// 全部对象受控制器的上限与清单管理，Boss 死亡 / 销毁时统一收掉。
// ============================================================================

using System;
using UnityEngine;
using UnityEngine.Events;

namespace BossRush
{
    internal sealed class SandstormDashWarning : MonoBehaviour
    {
        private LineRenderer _band;
        private LineRenderer _core;
        private ParticleSystem _flow;
        private float _duration;
        private float _t;
        private bool _flashing;
        private float _flashT;
        private float _width;

        internal static SandstormDashWarning Create(Vector3 from, Vector3 to, float width, float duration)
        {
            GameObject go = null;
            try
            {
                go = new GameObject("SandstormChampion_DashWarning");
                SandstormDashWarning w = go.AddComponent<SandstormDashWarning>();
                w.Init(from, to, width, duration);
                return w;
            }
            catch (Exception e)
            {
                if (go != null) UnityEngine.Object.Destroy(go);
                ModBehaviour.DevLog(SandstormChampionConfig.LogPrefix + "[WARNING] 预警失败: " + e.Message);
                return null;
            }
        }

        private void Init(Vector3 from, Vector3 to, float width, float duration)
        {
            _duration = Mathf.Max(0.05f, duration);
            _width = width;
            Material strip = BossRushFxKit.GetShapeMaterial(BossRushParticleShape.Wisp, BossRushFxBlend.Alpha, BossRushFxKit.GainSoft);
            transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            Vector3 a = from + Vector3.up * 0.07f;
            Vector3 b = to + Vector3.up * 0.07f;
            _band = MakeFlatLine("Band", strip, width, a, b);
            _core = MakeFlatLine("Core", strip, width * 0.12f, a, b);

            Material dot = BossRushFxKit.GetShapeMaterial(BossRushParticleShape.GlowDot, BossRushFxBlend.Additive, BossRushFxKit.GainHot);
            _flow = BossRushFxKit.CreateEmitter("Flow", null == transform ? null : transform, Vector3.zero, dot, 40, true);
            if (_flow != null)
            {
                Vector3 dir = b - a;
                float length = dir.magnitude;
                _flow.transform.position = a;
                _flow.transform.rotation = Quaternion.LookRotation(dir.sqrMagnitude > 0.001f ? dir.normalized : Vector3.forward, Vector3.up);
                ParticleSystem.MainModule main = _flow.main;
                main.startLifetime = Mathf.Clamp(length / 16f, 0.2f, 1.2f);
                main.startSpeed = 16f;
                main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.14f);
                main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.75f, 0.35f, 0.9f));
                ParticleSystem.EmissionModule emission = _flow.emission;
                emission.rateOverTime = 34f;
                ParticleSystem.ShapeModule shape = _flow.shape;
                shape.shapeType = ParticleSystemShapeType.Box;
                shape.scale = new Vector3(width * 0.8f, 0.02f, 0.05f);
                _flow.Play();
            }
            Apply(0f);
        }

        private LineRenderer MakeFlatLine(string name, Material material, float width, Vector3 a, Vector3 b)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(transform, false);
            LineRenderer line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = material;
            line.useWorldSpace = true;
            line.alignment = LineAlignment.TransformZ;
            line.positionCount = 2;
            line.SetPosition(0, a);
            line.SetPosition(1, b);
            line.widthMultiplier = width;
            line.numCapVertices = 6;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            return line;
        }

        /// <summary>前摇期间改指向（冲锋预判）：重写两条带的端点与流动亮点的发射朝向。</summary>
        internal void Retarget(Vector3 from, Vector3 to)
        {
            if (_flashing) return;
            Vector3 a = from + Vector3.up * 0.07f;
            Vector3 b = to + Vector3.up * 0.07f;
            if (_band != null) { _band.SetPosition(0, a); _band.SetPosition(1, b); }
            if (_core != null) { _core.SetPosition(0, a); _core.SetPosition(1, b); }
            if (_flow != null)
            {
                Vector3 dir = b - a;
                _flow.transform.position = a;
                if (dir.sqrMagnitude > 0.001f) _flow.transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
            }
        }

        /// <summary>出手：亮一下然后淡掉销毁。</summary>
        internal void Flash()
        {
            if (_flashing) return;
            _flashing = true;
            _flashT = 0f;
            if (_flow != null) _flow.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (_flashing)
            {
                _flashT += dt;
                float k = 1f - Mathf.Clamp01(_flashT / 0.22f);
                SetColors(new Color(1f, 0.92f, 0.7f, 0.9f * k), new Color(1f, 1f, 1f, k));
                if (_band != null) _band.widthMultiplier = _width * (1f + (1f - k) * 0.4f);
                if (_flashT >= 0.22f) Destroy(gameObject);
                return;
            }
            _t += dt;
            Apply(Mathf.Clamp01(_t / _duration));
            if (_t > _duration + 1.5f) Destroy(gameObject);
        }

        private void Apply(float k)
        {
            float pulse = 0.75f + 0.25f * Mathf.Sin(_t * (10f + 20f * k));
            Color band = SandstormChampionConfig.WarningColor;
            band.a = Mathf.Lerp(0.12f, 0.38f, k) * pulse;
            Color core = new Color(0.94f, 0.76f, 0.45f, Mathf.Lerp(0.2f, 0.65f, k));
            SetColors(band, core);
        }

        private void SetColors(Color band, Color core)
        {
            if (_band != null) { _band.startColor = band; _band.endColor = new Color(band.r, band.g, band.b, band.a * 0.6f); }
            if (_core != null) { _core.startColor = core; _core.endColor = core; }
        }
    }

    // ========================================================================

    internal sealed class SandstormOrb : MonoBehaviour
    {
        private SandstormChampionController _owner;
        private Vector3 _velocity;
        private float _life;
        private float _homingUntil;
        private bool _dead;
        private ParticleSystem _core;
        private ParticleSystem _trail;
        private int _wallMask;
        private bool _bubble;
        private HealthSimpleBase _health;
        private Collider _hitCollider;
        private bool _subscribed;
        private ParticleSystem _shell;
        private SandstormSharkVisual _sharkVisual;
        private readonly ParticleSystem.Particle[] _shellParticle = new ParticleSystem.Particle[1];
        private float _nextShellAt;

        internal bool IsDead { get { return _dead; } }

        internal static SandstormOrb Spawn(SandstormChampionController owner, Vector3 position, Vector3 direction,
            float speedScale, bool homing = true)
        {
            GameObject go = null;
            try
            {
                go = new GameObject("SandstormChampion_Orb");
                go.SetActive(false);
                go.transform.position = position;
                SandstormOrb orb = go.AddComponent<SandstormOrb>();
                orb.Init(owner, direction, speedScale, homing);
                go.SetActive(true);
                orb.IgnoreCharacterContact(CharacterMainControl.Main);
                orb.IgnoreCharacterContact(owner.GetComponent<CharacterMainControl>());
                return orb;
            }
            catch (Exception e)
            {
                if (go != null) UnityEngine.Object.Destroy(go);
                ModBehaviour.DevLog(SandstormChampionConfig.LogPrefix + "[WARNING] 沙珠失败: " + e.Message);
                return null;
            }
        }

        private void Init(SandstormChampionController owner, Vector3 direction, float speedScale, bool homing)
        {
            _owner = owner;
            _bubble = homing;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.001f) direction = Vector3.forward;
            _velocity = direction.normalized * SandstormChampionConfig.OrbSpeed * speedScale;
            transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
            _homingUntil = Time.time + (homing ? SandstormChampionConfig.OrbHomingSeconds : 0f);
            _wallMask = Duckov.Utilities.GameplayDataSettings.Layers.wallLayerMask;
            // 官方弹丸与近战都从 DamageReceiver 层拿同物体接收体；失活时装好再激活，
            // HealthSimpleBase.Awake 才能拿到 dmgReceiver。泡一击破，沙鲨可击破且无角色/掉落身份。
            int layer = LayerMask.NameToLayer("DamageReceiver");
            if (layer >= 0) gameObject.layer = layer;
            SphereCollider collider = gameObject.AddComponent<SphereCollider>();
            collider.radius = SandstormChampionConfig.OrbHitRadius;
            collider.isTrigger = false;
            _hitCollider = collider;
            Rigidbody rigidbody = gameObject.AddComponent<Rigidbody>();
            rigidbody.isKinematic = true;
            rigidbody.useGravity = false;
            DamageReceiver receiver = gameObject.AddComponent<DamageReceiver>();
            receiver.useSimpleHealth = true;
            receiver.OnHurtEvent = new UnityEvent<DamageInfo>();
            receiver.OnDeadEvent = new UnityEvent<DamageInfo>();
            _health = gameObject.AddComponent<HealthSimpleBase>();
            _health.team = owner.HazardTeam;
            _health.maxHealthValue = homing ? 1f : SandstormChampionConfig.SharkHealth;
            _health.dmgReceiver = receiver;
            receiver.simpleHealth = _health;
            _health.OnDeadEvent += OnShotDown;
            _subscribed = true;

            Material glow = BossRushFxKit.GetShapeMaterial(BossRushParticleShape.GlowDot, BossRushFxBlend.Additive, BossRushFxKit.GainHot);
            Material wisp = BossRushFxKit.GetShapeMaterial(BossRushParticleShape.Wisp, BossRushFxBlend.Alpha, BossRushFxKit.GainSoft);
            _core = BossRushFxKit.CreateEmitter("Core", transform, Vector3.zero, homing ? wisp : glow, 12, false);
            if (_core != null)
            {
                ParticleSystem.MainModule main = _core.main;
                main.startLifetime = homing ? 0.55f : 0.25f;
                main.startSize = new ParticleSystem.MinMaxCurve(homing ? 0.22f : 0.3f, homing ? 0.42f : 0.4f);
                main.startColor = homing
                    ? new ParticleSystem.MinMaxGradient(new Color(0.35f, 0.29f, 0.2f, 0.16f), new Color(0.68f, 0.57f, 0.39f, 0.3f))
                    : new ParticleSystem.MinMaxGradient(new Color(1f, 0.7f, 0.3f, 0.9f), new Color(1f, 0.9f, 0.6f, 0.9f));
                ParticleSystem.EmissionModule emission = _core.emission;
                emission.rateOverTime = homing ? 18f : 40f;
                if (homing)
                {
                    ParticleSystem.ShapeModule shape = _core.shape;
                    shape.shapeType = ParticleSystemShapeType.Sphere;
                    shape.radius = 0.27f;
                    ParticleSystem.VelocityOverLifetimeModule velocity = _core.velocityOverLifetime;
                    velocity.enabled = true;
                    velocity.orbitalY = 3.5f;
                    SandstormChampionAssetManager.ConfigureDustNoise(_core, 0.18f);
                    ParticleSystem.ColorOverLifetimeModule fade = _core.colorOverLifetime;
                    fade.enabled = true;
                    fade.color = BossRushFxKit.FadeGradient(Color.clear, Color.white, Color.clear, 0.3f);
                }
                _core.Play();
            }
            if (!homing && _core != null)
            {
                ParticleSystemRenderer renderer = _core.GetComponent<ParticleSystemRenderer>();
                if (renderer != null)
                {
                    renderer.renderMode = ParticleSystemRenderMode.Stretch;
                    renderer.velocityScale = 0.08f;
                    renderer.lengthScale = 2.5f;
                }
            }
            _shell = SandstormChampionVolume.CreateField(transform, homing ? "SandPearlMembrane" : "SandSharkHead",
                homing ? BossRushParticleShape.Bubble : BossRushParticleShape.Pearl, BossRushFxBlend.Alpha, 1f, 1, false);
            if (!homing) _sharkVisual = SandstormSharkVisual.Create(transform);
            UpdateBubbleShell();
            _trail = BossRushFxKit.CreateEmitter("Trail", transform, Vector3.zero, wisp, 40, true);
            if (_trail != null)
            {
                ParticleSystem.MainModule main = _trail.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.6f);
                main.startSize = homing ? new ParticleSystem.MinMaxCurve(0.3f, 0.5f)
                    : new ParticleSystem.MinMaxCurve(0.55f, 0.85f);
                main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                ParticleSystem.EmissionModule emission = _trail.emission;
                emission.rateOverTime = 0f;
                emission.rateOverDistance = 7f;
                ParticleSystem.ShapeModule shape = _trail.shape;
                shape.radius = 0.2f;
                ParticleSystem.ColorOverLifetimeModule col = _trail.colorOverLifetime;
                col.enabled = true;
                col.color = BossRushFxKit.FadeGradient(SandstormChampionConfig.SandLight, SandstormChampionConfig.Sand,
                    new Color(0.7f, 0.5f, 0.3f, 0f), 0f);
                ParticleSystem.SizeOverLifetimeModule size = _trail.sizeOverLifetime;
                size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.2f));
                _trail.Play();
            }
            // 弹幕以自发光粒子读形，不给每颗沙珠叠加动态点光源。
        }

        private void Update()
        {
            if (_dead) return;
            float dt = Time.deltaTime;
            _life += dt;
            if (Time.time >= _nextShellAt) UpdateBubbleShell();
            if (_life >= SandstormChampionConfig.OrbLifetime || _owner == null || !_owner.IsFighting)
            {
                Pop(false);
                return;
            }

            CharacterMainControl player = CharacterMainControl.Main;
            Vector3 pos = transform.position;
            if (player != null && Time.time < _homingUntil)
            {
                Vector3 to = player.transform.position + Vector3.up * 1f - pos;
                to.y = 0f;
                if (to.sqrMagnitude > 0.01f)
                {
                    float speed = _velocity.magnitude;
                    Vector3 newDir = Vector3.RotateTowards(_velocity.normalized, to.normalized,
                        SandstormChampionConfig.OrbTurnDegrees * Mathf.Deg2Rad * dt, 0f);
                    _velocity = newDir * speed;
                }
            }

            Vector3 step = _velocity * dt;
            if (_wallMask != 0 && step.sqrMagnitude > 0.00001f && Physics.Raycast(pos, step.normalized,
                step.magnitude + 0.2f, _wallMask, QueryTriggerInteraction.Ignore))
            {
                Pop(false);
                return;
            }
            transform.position = pos + step;
            if (_velocity.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(_velocity.normalized, Vector3.up);

            if (player != null)
            {
                Vector3 point = player.transform.position + Vector3.up;
                float t = step.sqrMagnitude > 0.00001f ? Mathf.Clamp01(Vector3.Dot(point - pos, step) / step.sqrMagnitude) : 0f;
                Vector3 d = point - (pos + step * t);
                if (d.sqrMagnitude <= SandstormChampionConfig.OrbHitRadius * SandstormChampionConfig.OrbHitRadius
                    && !BossSkillDamageRules.IsDodging(player))
                {
                    _owner.HurtPlayer(_bubble ? SandstormChampionConfig.OrbDamage : SandstormChampionConfig.SharkDamage, transform.position);
                    Pop(true);
                }
            }
        }

        internal void Pop(bool hit)
        {
            if (_dead) return;
            _dead = true;
            if (_hitCollider != null) _hitCollider.enabled = false;
            if (_shell != null) _shell.Clear();
            if (_sharkVisual != null) _sharkVisual.Release();
            try
            {
                BossRushFxBurst sparks = BossRushFxKit.Sparks(_bubble ? new Color(0.94f, 0.82f, 0.6f, 0.85f)
                    : SandstormChampionConfig.Ember, hit ? 16 : 8);
                if (_bubble) { sparks.SpeedMin = 1.8f; sparks.SpeedMax = 4.5f; sparks.SizeMin = 0.025f; sparks.SizeMax = 0.055f; }
                BossRushFxKit.PlayBurst(transform.position, sparks);
                BossRushFxBurst dust = BossRushFxKit.Dust(SandstormChampionConfig.Sand, hit ? 8 : 4);
                dust.FlatOnGround = false;
                BossRushFxKit.PlayBurst(transform.position, dust);
                if (_bubble)
                {
                    // 薄膜破开：一圈很快撑开就散的细环，像肥皂泡那一下「啵」。
                    Color film = new Color(0.98f, 0.9f, 0.72f, 0.6f);
                    BossRushFxKit.PlayBurst(transform.position, BossRushFxKit.Shockwave(film, 0.9f, 2.2f, 0.16f, false));
                }
                else
                {
                    // 沙鲨散架：头部一记火星亮芯，砂身沿来向向前溃散。
                    BossRushFxKit.PlayBurst(transform.position, BossRushFxKit.Glint(SandstormChampionConfig.Ember, 1.1f, 0.09f));
                    BossRushFxBurst body = BossRushFxKit.Dust(SandstormChampionConfig.Sand, 10);
                    body.Radial = false;
                    body.FlatOnGround = false;
                    body.Cone = 40f;
                    body.Direction = _velocity.sqrMagnitude > 0.01f ? _velocity : transform.forward;
                    body.SpeedMin = 2f;
                    body.SpeedMax = 6f;
                    body.Drag = 4f;
                    body.SizeMin = 0.4f;
                    body.SizeMax = 0.8f;
                    BossRushFxKit.PlayBurst(transform.position, body);
                }
            }
            catch { }
            if (gameObject.activeSelf) BossRushFxKit.Release(gameObject, 0.1f, 1f, false);
            else Destroy(gameObject); // 官方 simple health 死亡先停用物体，不能留下不会 Update 的释放计时器。
        }

        private void IgnoreCharacterContact(CharacterMainControl character)
        {
            if (_hitCollider == null || character == null) return;
            Collider other = character.GetComponent<Collider>();
            if (other != null) Physics.IgnoreCollision(_hitCollider, other, true);
        }

        private void UpdateBubbleShell()
        {
            if (_shell == null) return;
            _nextShellAt = Time.time + 1f / 30f;
            // 有面高光、暗侧和薄膜的珍珠沙泡，去掉绕球运行的三根轨道线。
            var particle = _shellParticle[0];
            particle.position = Vector3.zero;
            particle.startSize = _bubble ? 1.12f + 0.025f * Mathf.Sin(_life * 7f) : 0.95f;
            particle.rotation = _bubble ? -12f + Mathf.Sin(_life * 2f) * 5f : 0f;
            particle.startColor = new Color(0.91f, 0.79f, 0.56f, _bubble ? 0.85f : 0.95f);
            particle.startLifetime = particle.remainingLifetime = 0.6f;
            _shellParticle[0] = particle;
            _shell.SetParticles(_shellParticle, 1);
        }

        private void OnShotDown(DamageInfo damageInfo)
        {
            if (!_dead && _owner != null) _owner.ReportOrbShotDown(_bubble);
            Pop(true);
        }

        private void OnDestroy()
        {
            if (_subscribed && _health != null) _health.OnDeadEvent -= OnShotDown;
            _subscribed = false;
            _owner = null;
        }
    }

    // ========================================================================

    /// <summary>贴地追踪沙圈：接触玩家或计时结束才凝成固定沙柱；墙体只阻挡，不提前引爆。</summary>
    internal sealed class SandstormCycloneSeed : MonoBehaviour
    {
        private SandstormChampionController _owner;
        private bool _cyclone;
        private bool _dead;
        private float _life;
        private int _wallMask;
        private SandstormGroundField _field;
        internal bool IsDead { get { return _dead; } }

        internal static SandstormCycloneSeed Spawn(SandstormChampionController owner, Vector3 at, Vector3 target, bool cyclone)
        {
            GameObject go = new GameObject(cyclone ? "SandstormChampion_HomingSeed" : "SandstormChampion_TwinTrackingRing");
            try
            {
                // 双生圈与大圈都从施法者处起步，target 只用于朝向，绝不作为出生点。
                Vector3 initial = GroundPoint(at);
                Vector3 safe;
                if (!SpawnPositionHelper.TryResolveReachableFrom(initial, CharacterMainControl.Main.transform.position,
                    2f, 0.04f, out safe))
                {
                    Destroy(go);
                    return null;
                }
                go.transform.position = safe;
                Vector3 heading = target - safe;
                heading.y = 0f;
                if (heading.sqrMagnitude > 0.01f) go.transform.rotation = Quaternion.LookRotation(heading, Vector3.up);
                SandstormCycloneSeed seed = go.AddComponent<SandstormCycloneSeed>();
                seed._owner = owner;
                seed._cyclone = cyclone;
                seed._wallMask = Duckov.Utilities.GameplayDataSettings.Layers.wallLayerMask;
                seed._field = SandstormGroundField.Create(go.transform, SandstormChampionConfig.SeedContactRadius);
                Material material = BossRushFxKit.GetShapeMaterial(BossRushParticleShape.Wisp,
                    BossRushFxBlend.Alpha, BossRushFxKit.GainSoft);
                ParticleSystem dust = BossRushFxKit.CreateEmitter("PursuitDust", go.transform, Vector3.up * 0.15f, material, 64, false);
                if (dust != null)
                {
                    ParticleSystem.MainModule main = dust.main;
                    main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.65f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.7f);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.24f, 0.55f);
                    main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                    ParticleSystem.ShapeModule shape = dust.shape;
                    shape.shapeType = ParticleSystemShapeType.Circle;
                    shape.radius = SandstormChampionConfig.SeedContactRadius * 0.8f;
                    shape.rotation = new Vector3(90f, 0f, 0f);
                    ParticleSystem.EmissionModule emission = dust.emission;
                    emission.rateOverTime = 70f;
                    ParticleSystem.VelocityOverLifetimeModule velocity = dust.velocityOverLifetime;
                    velocity.enabled = true;
                    velocity.orbitalY = 5f;
                    ParticleSystem.ColorOverLifetimeModule color = dust.colorOverLifetime;
                    color.enabled = true;
                    color.color = BossRushFxKit.FadeGradient(new Color(0.58f, 0.46f, 0.3f, 0f),
                        new Color(0.76f, 0.64f, 0.44f, 0.5f), new Color(0.4f, 0.34f, 0.26f, 0f), 0.25f);
                    SandstormChampionAssetManager.ConfigureDustNoise(dust, 0.35f);
                    dust.Play();
                }
                return seed;
            }
            catch (Exception e)
            {
                Destroy(go);
                ModBehaviour.DevLog(SandstormChampionConfig.LogPrefix + "追踪沙圈失败: " + e.Message);
                return null;
            }
        }

        private void Update()
        {
            if (_dead) return;
            if (_owner == null || !_owner.IsFighting) { Destroy(gameObject); return; }
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null) { Destroy(gameObject); return; }
            _life += Time.deltaTime;
            Vector3 from = transform.position;
            Vector3 target = player.transform.position;
            Vector3 flatTarget = new Vector3(target.x, from.y, target.z);
            float speed = _cyclone ? SandstormChampionConfig.CycloneSeedSpeed : SandstormChampionConfig.TwinSeedSpeed;
            Vector3 next = Vector3.MoveTowards(from, flatTarget, speed * Time.deltaTime);
            // 碰墙时留在墙前，仍须满足接触或到时；不会隔墙落在玩家脚下。
            if (_wallMask != 0 && Physics.Linecast(from + Vector3.up * 0.45f, next + Vector3.up * 0.45f,
                _wallMask, QueryTriggerInteraction.Ignore)) next = from;
            next = GroundPoint(next);
            transform.position = next;
            if (_field != null) _field.SetStrength(0.75f + 0.18f * Mathf.Sin(_life * 6f));
            Vector3 step = next - from;
            step.y = 0f;
            Vector3 offset = target - from;
            offset.y = 0f;
            float fraction = step.sqrMagnitude > 0.00001f ? Mathf.Clamp01(Vector3.Dot(offset, step) / step.sqrMagnitude) : 0f;
            Vector3 closest = from + step * fraction;
            Vector3 distance = target - closest;
            distance.y = 0f;
            bool contact = distance.sqrMagnitude <= SandstormChampionConfig.SeedContactRadius * SandstormChampionConfig.SeedContactRadius
                && Mathf.Abs(target.y - next.y) <= 2f;
            if (contact && _wallMask != 0 && Physics.Linecast(next + Vector3.up * 0.45f,
                target + Vector3.up * 0.45f, _wallMask, QueryTriggerInteraction.Ignore)) contact = false;
            if (contact || _life >= SandstormChampionConfig.CycloneSeedLifetime) Land(next);
        }

        private static Vector3 GroundPoint(Vector3 point)
        {
            RaycastHit hit;
            int mask = Duckov.Utilities.GameplayDataSettings.Layers.groundLayerMask;
            if (mask != 0 && Physics.Raycast(point + Vector3.up * 2f, Vector3.down, out hit, 5f, mask, QueryTriggerInteraction.Ignore))
                point.y = hit.point.y + 0.04f;
            return point;
        }

        private void Land(Vector3 target)
        {
            if (_dead) return;
            _dead = true;
            if (_owner != null) _owner.ResolveCycloneSeed(target, _cyclone);
            BossRushFxBurst dust = BossRushFxKit.Dust(SandstormChampionConfig.Sand, 16);
            dust.SpeedMin = 1.5f;
            dust.SpeedMax = 3f;
            BossRushFxKit.PlayBurst(transform.position, dust);
            Color ring = SandstormChampionConfig.WarningColor;
            ring.a = 0.6f;
            BossRushFxKit.PlayBurst(transform.position + Vector3.up * 0.05f,
                BossRushFxKit.Shockwave(ring, 0.8f, 3.5f, 0.3f, true));
            Destroy(gameObject);
        }
    }

    internal sealed class SandstormTornado : MonoBehaviour
    {
        private SandstormChampionController _owner;
        private float _radius;
        private float _lifetime;
        private float _life;
        private float _nextTick;
        private bool _dead;
        private bool _cyclone;
        private float _armedAt;
        private float _nextVolley;
        private int _volleys;
        private bool _erupted;
        private GameObject _warningRing;
        private SandstormGroundField _groundWarning;
        private SandstormChampionVolume _volume;


        internal bool IsDead { get { return _dead; } }

        internal static SandstormTornado Spawn(SandstormChampionController owner, Vector3 position, bool cyclone)
        {
            GameObject go = null;
            try
            {
                go = new GameObject(cyclone ? "SandstormChampion_Cyclone" : "SandstormChampion_Tornado");
                go.transform.position = position;
                SandstormTornado t = go.AddComponent<SandstormTornado>();
                t.Init(owner, cyclone);
                return t;
            }
            catch (Exception e)
            {
                if (go != null) UnityEngine.Object.Destroy(go);
                ModBehaviour.DevLog(SandstormChampionConfig.LogPrefix + "[WARNING] 沙卷柱失败: " + e.Message);
                return null;
            }
        }

        private void Init(SandstormChampionController owner, bool cyclone)
        {
            _owner = owner;
            _cyclone = cyclone;
            _radius = cyclone ? SandstormChampionConfig.CycloneRadius : SandstormChampionConfig.TornadoRadius;
            float height = cyclone ? SandstormChampionConfig.CycloneHeight : SandstormChampionConfig.TornadoHeight;
            _lifetime = cyclone ? SandstormChampionConfig.CycloneLifetime : SandstormChampionConfig.TornadoLifetime;
            _armedAt = Time.time + SandstormChampionConfig.TornadoTelegraphSeconds;
            _nextTick = _armedAt;
            _nextVolley = _armedAt + 0.4f;
            _warningRing = SandstormChampionAssetManager.CreateNovaWarning(transform.position, _radius);
            _warningRing.transform.SetParent(transform, true);
            _groundWarning = _warningRing.GetComponent<SandstormGroundField>();
            _volume = SandstormChampionVolume.Create(transform, _radius, height, false);
            _volume.SetEnvelope(0f, 0f, 0f);
        }

        private void Update()
        {
            if (_dead) return;
            float dt = Time.deltaTime;
            _life += dt;
            if (Time.time >= _armedAt + _lifetime || _owner == null || !_owner.IsFighting)
            {
                Dismiss();
                return;
            }

            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null) return;
            float growth = Mathf.SmoothStep(0f, 1f, _life / SandstormChampionConfig.TornadoTelegraphSeconds);
            float fade = Mathf.Clamp01((_armedAt + _lifetime - Time.time) / 1.2f);
            if (_volume != null) _volume.SetEnvelope(growth * fade, growth, _cyclone ? 1f : 0f);
            if (_groundWarning != null) _groundWarning.SetStrength(Time.time < _armedAt ? 0.65f + growth * 0.35f : 0.72f);
            if (Time.time < _armedAt) return;
            Vector3 pos = transform.position;
            if (!_erupted)
            {
                _erupted = true;
                PlayEruption(pos);
            }
            // 形成后的沙柱固定；追踪仅发生在之前的地面沙圈阶段。
            if (_volleys < (_cyclone ? 12 : 6) && Time.time >= _nextVolley)
            {
                _volleys++;
                _nextVolley = Time.time + (_cyclone ? SandstormChampionConfig.CycloneVolleySeconds
                    : SandstormChampionConfig.TornadoVolleySeconds);
                _owner.SpawnTornadoVolley(pos, _cyclone);
            }
            Vector3 remaining = player.transform.position - pos;
            float heightDifference = Mathf.Abs(remaining.y);
            remaining.y = 0f;
            if (remaining.sqrMagnitude <= _radius * _radius && heightDifference <= 2f && Time.time >= _nextTick)
            {
                _nextTick = Time.time + SandstormChampionConfig.TornadoTickSeconds;
                if (!BossSkillDamageRules.IsDodging(player))
                    _owner.HurtPlayer(SandstormChampionConfig.TornadoTickDamage, player.transform.position + Vector3.up);
            }
        }

        /// <summary>
        /// 预警结束、沙柱真正成形的那一刻：脚下砂环炸开，碎砂块被掀上天再落下，一股砂尘顺柱心冲起，
        /// 近处玩家感到一震。大沙暴按半径放大。只是表现，伤害仍由上面的计时判定。
        /// </summary>
        private void PlayEruption(Vector3 pos)
        {
            try
            {
                Color ring = SandstormChampionConfig.WarningColor;
                ring.a = 0.7f;
                SandstormChampionAssetManager.PlaySandBurst(pos, _radius, _cyclone ? 34 : 22, ring);
                BossRushFxBurst chunks = BossRushFxKit.Dust(SandstormChampionConfig.SandDark, _cyclone ? 18 : 10);
                chunks.Shape = BossRushParticleShape.Shard;
                chunks.Radial = false;
                chunks.FlatOnGround = false;
                chunks.Upward = true;
                chunks.ShapeRadius = _radius * 0.6f;
                chunks.SpeedMin = 5f;
                chunks.SpeedMax = 10f;
                chunks.Gravity = 2.2f;
                chunks.Drag = 0.8f;
                chunks.SizeMin = 0.12f;
                chunks.SizeMax = 0.3f;
                chunks.LifeMin = 0.9f;
                chunks.LifeMax = 1.4f;
                chunks.GrowTo = 0.8f;
                chunks.Spin = 360f;
                chunks.FadeIn = 0f;
                chunks.Core = new Color(0.7f, 0.55f, 0.34f, 1f);
                chunks.Main = new Color(0.55f, 0.41f, 0.25f, 0.95f);
                BossRushFxKit.PlayBurst(pos + Vector3.up * 0.2f, chunks);
                BossRushFxBurst geyser = BossRushFxKit.Dust(SandstormChampionConfig.SandLight, _cyclone ? 20 : 12);
                geyser.Radial = false;
                geyser.FlatOnGround = false;
                geyser.Cone = 14f;
                geyser.Direction = Vector3.up;
                geyser.ShapeRadius = _radius * 0.35f;
                geyser.SpeedMin = 8f;
                geyser.SpeedMax = 16f;
                geyser.Drag = 2.5f;
                geyser.SizeMin = 0.9f;
                geyser.SizeMax = 1.8f;
                geyser.LifeMin = 0.8f;
                geyser.LifeMax = 1.3f;
                BossRushFxKit.PlayBurst(pos, geyser);
                SandstormChampionAssetManager.ShakeNear(pos, _cyclone ? 0.32f : 0.2f, _cyclone ? 24f : 16f);
            }
            catch { /* 表现失败不影响沙柱 */ }
        }

        internal void Dismiss()
        {
            if (_dead) return;
            _dead = true;
            if (_warningRing != null) Destroy(_warningRing);
            if (_volume != null) _volume.Release();
            BossRushFxKit.Release(gameObject, 0.1f, 2.5f, false);
        }
    }

    // ========================================================================

    internal sealed class SandstormAmbience : MonoBehaviour
    {
        private ParticleSystem _streaks;
        private ParticleSystem _clouds;
        private float _time;
        private bool _released;

        internal static SandstormAmbience Create()
        {
            GameObject go = null;
            try
            {
                go = new GameObject("SandstormChampion_Ambience");
                SandstormAmbience a = go.AddComponent<SandstormAmbience>();
                a.Init();
                return a;
            }
            catch (Exception e)
            {
                if (go != null) UnityEngine.Object.Destroy(go);
                ModBehaviour.DevLog(SandstormChampionConfig.LogPrefix + "[WARNING] 沙暴氛围失败: " + e.Message);
                return null;
            }
        }

        private void Init()
        {
            Material wisp = BossRushFxKit.GetShapeMaterial(BossRushParticleShape.Wisp, BossRushFxBlend.Alpha, BossRushFxKit.GainSoft);
            Material glow = BossRushFxKit.GetShapeMaterial(BossRushParticleShape.GlowDot, BossRushFxBlend.Alpha, BossRushFxKit.GainSoft);

            _clouds = BossRushFxKit.CreateEmitter("Clouds", transform, Vector3.up * 3.2f, wisp, 80, true);
            if (_clouds != null)
            {
                ParticleSystem.MainModule main = _clouds.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(2.2f, 3f);
                main.startSpeed = 0f;
                main.startSize = new ParticleSystem.MinMaxCurve(3f, 5.5f);
                main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                ParticleSystem.EmissionModule emission = _clouds.emission;
                emission.rateOverTime = 24f;
                ParticleSystem.ShapeModule shape = _clouds.shape;
                shape.shapeType = ParticleSystemShapeType.Box;
                shape.scale = new Vector3(30f, 2f, 30f);
                ParticleSystem.VelocityOverLifetimeModule vel = _clouds.velocityOverLifetime;
                vel.enabled = true;
                vel.space = ParticleSystemSimulationSpace.World;
                vel.x = new ParticleSystem.MinMaxCurve(5f);
                vel.y = new ParticleSystem.MinMaxCurve(0f);
                vel.z = new ParticleSystem.MinMaxCurve(1.5f);
                ParticleSystem.ColorOverLifetimeModule col = _clouds.colorOverLifetime;
                col.enabled = true;
                col.color = BossRushFxKit.FadeGradient(new Color(0.55f, 0.47f, 0.35f, 0f), new Color(0.5f, 0.43f, 0.32f, 0.15f),
                    new Color(0.45f, 0.32f, 0.2f, 0f), 0.25f);
                _clouds.Play();
            }

            _streaks = BossRushFxKit.CreateEmitter("Streaks", transform, Vector3.up * 1.2f, glow, 120, true);
            if (_streaks != null)
            {
                ParticleSystem.MainModule main = _streaks.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1f);
                main.startSpeed = 0f;
                main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.08f);
                main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.95f, 0.8f, 0.55f, 0.6f));
                ParticleSystem.EmissionModule emission = _streaks.emission;
                emission.rateOverTime = 110f;
                ParticleSystem.ShapeModule shape = _streaks.shape;
                shape.shapeType = ParticleSystemShapeType.Box;
                shape.scale = new Vector3(24f, 3f, 24f);
                ParticleSystem.VelocityOverLifetimeModule vel = _streaks.velocityOverLifetime;
                vel.enabled = true;
                vel.space = ParticleSystemSimulationSpace.World;
                vel.x = new ParticleSystem.MinMaxCurve(16f);
                vel.y = new ParticleSystem.MinMaxCurve(0f);
                vel.z = new ParticleSystem.MinMaxCurve(4f);
                ParticleSystemRenderer renderer = _streaks.GetComponent<ParticleSystemRenderer>();
                if (renderer != null)
                {
                    renderer.renderMode = ParticleSystemRenderMode.Stretch;
                    renderer.velocityScale = 0.06f;
                    renderer.lengthScale = 1f;
                }
                _streaks.Play();
            }
        }

        private void LateUpdate()
        {
            if (_released) return;
            CharacterMainControl player = CharacterMainControl.Main;
            if (player != null) transform.position = player.transform.position;
            // 风向慢慢摆动，沙不总是一个方向吹
            _time += Time.deltaTime;
            float angle = Mathf.Sin(_time * 0.15f) * 50f;
            Vector3 wind = Quaternion.Euler(0f, angle, 0f) * new Vector3(1f, 0f, 0.25f);
            SetWind(_clouds, wind * 5f);
            SetWind(_streaks, wind * 16f);
        }

        private static void SetWind(ParticleSystem ps, Vector3 v)
        {
            if (ps == null) return;
            ParticleSystem.VelocityOverLifetimeModule vel = ps.velocityOverLifetime;
            vel.x = new ParticleSystem.MinMaxCurve(v.x);
            vel.z = new ParticleSystem.MinMaxCurve(v.z);
        }

        internal void Dismiss()
        {
            if (_released) return;
            _released = true;
            BossRushFxKit.Release(gameObject, 0.1f, 3.5f, false);
        }
    }
}
