using System;
using System.Collections;
using System.Collections.Generic;
using ItemStatsSystem;
using ItemStatsSystem.Stats;
using UnityEngine;
using UnityEngine.Events;

namespace BossRush
{
    /// <summary>
    /// COMPAT：G 残星工坊的岛主「残星匠首」（R1 纵切，owner 2026-09-14 批准的名册第一位）。
    ///
    /// 核心招式三件事，都绑在它身上那套星工装备上（装备即招式，参考 Arc Raiders「打掉护甲板就削弱」）：
    /// 1. **星炉供能桩**（血线 70% / 40% / 15%）：在身边立两根可以打的桩，桩在时匠首头甲与身甲各加一层护甲
    ///    （走官方护甲公式，不上无敌、不打补丁）；桩 25 秒自灭，避免落在够不着的地方把战斗卡死。
    ///    桩照云蚋的做法做成轻量伤害接收体（非触发球 + 运动学刚体 + DamageReceiver + HealthSimpleBase），死亡看 activeSelf。
    /// 2. **星焰落点**：在玩家脚下与左右两侧各落一圈，1.25 秒预警，从两圈之间的缝走；半径 ÷ 预警 ≈ 2.1 m/s，远低于 5.5 m/s 判据。
    /// 3. **过热**：每放两次星焰进入 4 秒过热——停下来散热（官方 AI 暂停）、这几秒物理伤害系数 +0.25，给明确的反打窗口。
    ///
    /// 破甲断招（每 0.25 秒节流读一次耐久，不进每帧热路径）：
    /// - 星铜护目盔耐久打空（被爆头打穿）→ 星焰只落中间一圈；
    /// - 星炉背甲耐久打空 → 供能桩给的护甲减半。
    ///
    /// 伤害走官方爆炸（口径同噬风）；字幕的机制提示走警示通道。
    /// 事件订阅：只订自己身上的 `Health.OnDeadEvent`，OnDestroy 成对退订；桩、圈与光都在 OnDead / OnDestroy 里收。
    /// </summary>
    internal sealed class SkyIslandForemanBoss : MonoBehaviour
    {
        private const float TickInterval = 0.25f;
        private const float PylonCoreHeight = 1.1f;
        private static readonly Color StarfireTint = new Color(0.98f, 0.66f, 0.30f, 1f);
        private static readonly Color PylonTint = new Color(0.46f, 0.86f, 0.90f, 1f);

        private sealed class Pylon
        {
            internal GameObject Root;
            internal LineRenderer Tether;
            internal float ExpiresAt;
        }

        private CharacterMainControl boss;
        private Health health;
        private SkyIslandBossProfile profile;
        private SkyIslandBossContext context;
        private BossAIController aiControl;
        private readonly List<Pylon> pylons = new List<Pylon>();
        private readonly List<GameObject> rings = new List<GameObject>();
        private Stat headArmorStat, bodyArmorStat, physicsStat;
        private Modifier headShield, bodyShield, overheatModifier;
        private GameObject overheatGlow;
        private Light overheatLight;
        private ParticleSystem overheatSteam;
        private int receiverLayer, phase, casts;
        private float nextTick, nextCastAt, overheatUntil;
        private bool subscribed, casting, overheated, finished;
        private bool helmEquipped, harnessEquipped, helmBrokenAnnounced, harnessBrokenAnnounced;

        /// <summary>只读，给 F3 与演练：当前相位、场上活着的供能桩、护盾与过热状态。</summary>
        internal int Phase { get { return phase; } }
        internal int LivePylons { get { return pylons.Count; } }
        internal bool ShieldActive { get { return headShield != null || bodyShield != null; } }
        internal bool Overheated { get { return overheated; } }

        internal void Bind(CharacterMainControl character, SkyIslandBossProfile value, SkyIslandBossContext ctx)
        {
            if (character == null) throw new ArgumentNullException("character");
            if (value == null) throw new ArgumentNullException("value");
            if (ctx == null) throw new ArgumentNullException("ctx");
            boss = character;
            profile = value;
            context = ctx;
            health = character.Health;
            if (health == null) throw new InvalidOperationException("残星匠首缺少生命组件");
            receiverLayer = LayerMask.NameToLayer("DamageReceiver");
            Item item = character.CharacterItem;
            if (item != null)
            {
                headArmorStat = item.GetStat("HeadArmor");
                bodyArmorStat = item.GetStat("BodyArmor");
                physicsStat = item.GetStat("ElementFactor_Physics");
            }
            // 配装在控制器之前做完：只有真的穿上了，「被打穿」才有意义，也才会播那句字幕。
            helmEquipped = !SkyIslandBossForge.PieceBroken(character.GetHelmatItem(), BossRushItemIds.SkyIslandStarbrassVisorHelm);
            harnessEquipped = !SkyIslandBossForge.PieceBroken(character.GetArmorItem(), BossRushItemIds.SkyIslandStarfurnaceHarness);
            try { aiControl = new BossAIController(character, "SkyIslandForeman"); }
            catch (Exception e)
            {
                aiControl = null;
                Debug.LogWarning("[SkyIslandBoss] 匠首 AI 暂停控制不可用，过热时不停手：" + e.Message);
            }
            nextCastAt = Time.time + SkyIslandBossRules.StarfireInterval;
            health.OnDeadEvent.AddListener(OnDead);
            subscribed = true;
            Announce("残星匠首拍了拍背上的星炉，工坊里的火又旺了起来。",
                "The Starforge Foreman slaps the furnace on its back and the workshop fires roar up again.", false);
        }

        private void Update()
        {
            if (finished || boss == null || health == null) return;
            UpdateTethers();
            if (Time.time < nextTick) return;
            nextTick = Time.time + TickInterval;
            if (context.Valid != null && !context.Valid()) return;
            if (health.IsDead) return;
            TickPylons();
            TickOverheat();
            TickBrokenGear();
            float max = health.MaxHealth;
            if (max > 0f)
            {
                int target = SkyIslandBossRules.PhaseFor(Mathf.Clamp01(health.CurrentHealth / max), SkyIslandBossRules.ForemanPhaseThresholds);
                if (target > phase)
                {
                    phase = target;
                    DeployPylons();
                }
            }
            if (!casting && !overheated && Time.time >= nextCastAt) TryCastStarfire();
        }

        // ====================================================================
        // 星炉供能桩
        // ====================================================================

        private void DeployPylons()
        {
            ClearPylons();
            Vector3 center = boss.transform.position;
            float baseAngle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
            for (int i = 0; i < SkyIslandBossRules.PylonCount; i++)
            {
                Vector3 ground;
                if (!TryFindPylonPoint(center, baseAngle + i * (Mathf.PI * 2f / SkyIslandBossRules.PylonCount), out ground)) continue;
                Pylon pylon = SpawnPylon(ground);
                if (pylon != null) pylons.Add(pylon);
            }
            if (pylons.Count == 0) return;
            Physics.SyncTransforms();
            ApplyShield(true);
            Announce("残星匠首立起了星炉供能桩：先打掉桩，它身上那层护甲才会退。",
                "The Foreman plants furnace pylons. Break them and its plating falls away.", true);
        }

        private bool TryFindPylonPoint(Vector3 center, float angle, out Vector3 ground)
        {
            for (int attempt = 0; attempt < 9; attempt++)
            {
                float a = angle + attempt * 0.42f;
                float distance = Mathf.Lerp(SkyIslandBossRules.PylonMinDistance, SkyIslandBossRules.PylonMaxDistance, (attempt % 3) / 2f);
                Vector3 probe = center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * distance;
                if (SkyIslandBossForge.SnapToGround(probe, context, 0.45f, out ground)) return true;
            }
            ground = Vector3.zero;
            return false;
        }

        private Pylon SpawnPylon(Vector3 ground)
        {
            GameObject go = null;
            try
            {
                go = new GameObject("SkyIslandForemanPylon");
                go.SetActive(false);
                if (context.Root != null) go.transform.SetParent(context.Root, true);
                go.transform.position = ground + Vector3.up * PylonCoreHeight;
                if (receiverLayer >= 0) go.layer = receiverLayer;
                SphereCollider collider = go.AddComponent<SphereCollider>();
                collider.radius = 0.5f;
                collider.isTrigger = false;
                Rigidbody body = go.AddComponent<Rigidbody>();
                body.isKinematic = true;
                body.useGravity = false;
                DamageReceiver receiver = go.AddComponent<DamageReceiver>();
                receiver.useSimpleHealth = true;
                if (receiver.OnHurtEvent == null) receiver.OnHurtEvent = new UnityEvent<DamageInfo>();
                if (receiver.OnDeadEvent == null) receiver.OnDeadEvent = new UnityEvent<DamageInfo>();
                HealthSimpleBase simple = go.AddComponent<HealthSimpleBase>();
                simple.team = Teams.wolf;
                simple.maxHealthValue = SkyIslandBossRules.PylonHealth;
                simple.dmgReceiver = receiver;
                receiver.simpleHealth = simple;
                // 表现全部挂在桩自己身上：官方 HealthSimpleBase 打空后停用根物体，圈、光柱与供能线跟着一起消失；
                // 打碎的那一下由 TickPylons 在原位补一次碎裂（碎裂不能挂在被停用的根上）。
                LineRenderer ring = SkyIslandGroundRing.Create(go.transform, new Vector3(0f, 0.08f - PylonCoreHeight, 0f));
                SkyIslandGroundRing.SetShape(ring, 0.9f, 0.22f, PylonTint);
                // 光柱底宽顶窄、顶端淡出（VB-24），不再是一根上下硬断的平色条；桩上再点一盏同色小灯。
                LineRenderer column = SkyIslandBossForge.ColumnLine(go.transform, "Column", 0.24f, PylonTint);
                column.SetPosition(0, ground + Vector3.up * 0.05f);
                column.SetPosition(1, ground + Vector3.up * 2.3f);
                SkyIslandBossForge.PylonLight(go.transform, PylonTint);
                LineRenderer tether = SkyIslandBossForge.StraightLine(go.transform, "Tether", 0.07f, PylonTint);
                go.SetActive(true);
                // 非触发球是官方弹道扫掠要的；它不该把人顶开或卡住（口径同云蚋，必须在 SetActive 之后调用）。
                IgnoreContact(collider, CharacterMainControl.Main);
                IgnoreContact(collider, boss);
                Pylon pylon = new Pylon { Root = go, Tether = tether, ExpiresAt = Time.time + SkyIslandBossRules.PylonLifetime };
                return pylon;
            }
            catch (Exception e)
            {
                if (go != null) Destroy(go);
                Debug.LogWarning("[SkyIslandBoss] 供能桩创建失败：" + e.Message);
                return null;
            }
        }

        private static void IgnoreContact(Collider collider, CharacterMainControl character)
        {
            if (collider == null || character == null) return;
            try
            {
                Collider other = character.GetComponent<Collider>();
                if (other != null) Physics.IgnoreCollision(collider, other, true);
            }
            catch (Exception e) { Debug.LogWarning("[SkyIslandBoss] 供能桩接触屏蔽失败：" + e.Message); }
        }

        private void UpdateTethers()
        {
            if (pylons.Count == 0) return;
            Vector3 chest = boss.transform.position + Vector3.up * 1.1f;
            for (int i = 0; i < pylons.Count; i++)
            {
                Pylon pylon = pylons[i];
                if (pylon.Root == null || !pylon.Root.activeSelf || pylon.Tether == null) continue;
                pylon.Tether.SetPosition(0, pylon.Root.transform.position);
                pylon.Tether.SetPosition(1, chest);
            }
        }

        private void TickPylons()
        {
            if (pylons.Count == 0) return;
            bool removed = false;
            for (int i = pylons.Count - 1; i >= 0; i--)
            {
                Pylon pylon = pylons[i];
                bool down = pylon.Root == null || !pylon.Root.activeSelf;
                if (!down && Time.time < pylon.ExpiresAt) continue;
                if (pylon.Root != null)
                {
                    // 打碎：原位碎片 + 地面余波 + 轻震；到时自灭：一小团扬尘。表现挂地图根，不挂在被停用的桩上（VB-24）。
                    Vector3 core = pylon.Root.transform.position;
                    Vector3 foot = core - Vector3.up * PylonCoreHeight;
                    if (down) SkyIslandImpactFx.Shatter(context.Root, core, foot, PylonTint);
                    else SkyIslandImpactFx.Puff(context.Root, foot, 0.6f, 8);
                    Destroy(pylon.Root);
                }
                pylons.RemoveAt(i);
                removed = true;
            }
            if (!removed || pylons.Count > 0) return;
            ApplyShield(false);
            Announce("供能桩全倒了，匠首身上的护甲退了下去。", "The pylons are down and the Foreman's plating falls away.", false);
        }

        private void ClearPylons()
        {
            for (int i = 0; i < pylons.Count; i++)
                if (pylons[i].Root != null) Destroy(pylons[i].Root);
            pylons.Clear();
            ApplyShield(false);
        }

        private void ApplyShield(bool on)
        {
            RemoveModifier(headArmorStat, ref headShield);
            RemoveModifier(bodyArmorStat, ref bodyShield);
            if (!on) return;
            float value = harnessEquipped && HarnessBroken() ? SkyIslandBossRules.ShieldArmorBroken : SkyIslandBossRules.ShieldArmor;
            headShield = AddModifier(headArmorStat, value);
            bodyShield = AddModifier(bodyArmorStat, value);
        }

        private Modifier AddModifier(Stat stat, float value)
        {
            if (stat == null) return null;
            try
            {
                Modifier modifier = new Modifier(ModifierType.Add, value, this);
                stat.AddModifier(modifier);
                return modifier;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SkyIslandBoss] 匠首属性修饰失败：" + e.Message);
                return null;
            }
        }

        private static void RemoveModifier(Stat stat, ref Modifier modifier)
        {
            if (modifier == null) return;
            try { if (stat != null) stat.RemoveModifier(modifier); }
            catch (Exception e) { Debug.LogWarning("[SkyIslandBoss] 匠首属性修饰撤销失败：" + e.Message); }
            modifier = null;
        }

        // ====================================================================
        // 星焰落点与过热
        // ====================================================================

        private void TryCastStarfire()
        {
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null || player.Health == null || player.Health.IsDead) return;
            Vector3 toPlayer = player.transform.position - boss.transform.position;
            float range = SkyIslandBossRules.StarfireRange;
            if (toPlayer.sqrMagnitude > range * range)
            {
                nextCastAt = Time.time + 1f;
                return;
            }
            nextCastAt = Time.time + SkyIslandBossRules.StarfireInterval;
            StartCoroutine(StarfireRoutine(player.transform.position, toPlayer));
        }

        private IEnumerator StarfireRoutine(Vector3 target, Vector3 toPlayer)
        {
            casting = true;
            List<Vector3> points = new List<Vector3>();
            List<LineRenderer> lines = new List<LineRenderer>();
            try
            {
                Vector3 flat = new Vector3(toPlayer.x, 0f, toPlayer.z);
                Vector3 side = flat.sqrMagnitude > 0.01f ? Vector3.Cross(Vector3.up, flat.normalized) : Vector3.right;
                AddStarfirePoint(points, target);
                // 护目盔被打穿之后没了准头：只落中间那一圈。
                if (!(helmEquipped && HelmBroken()))
                {
                    AddStarfirePoint(points, target + side * SkyIslandBossRules.StarfireSpread);
                    AddStarfirePoint(points, target - side * SkyIslandBossRules.StarfireSpread);
                }
                for (int i = 0; i < points.Count; i++)
                {
                    LineRenderer line = SkyIslandBossForge.CreateGroundRing(context.Root, points[i]);
                    SkyIslandBossForge.SetRing(line, SkyIslandBossRules.StarfireRadius, 0f, StarfireTint);
                    lines.Add(line);
                    rings.Add(line.gameObject);
                }
            }
            catch (Exception e) { Debug.LogWarning("[SkyIslandBoss] 星焰预警失败：" + e.Message); }

            // 戴着静听耳罩的玩家早一点听见（SkyIslandBossGearWorn，头目 R3）：预警只会更长，逃圈判据仍按不戴的算。
            float telegraph = SkyIslandBossRules.TelegraphSeconds(SkyIslandBossRules.StarfireTelegraph, SkyIslandBossGearWorn.Earmuffs);
            float started = Time.time;
            while (Time.time - started < telegraph && !Aborted())
            {
                float charge = Mathf.Clamp01((Time.time - started) / telegraph);
                for (int i = 0; i < lines.Count; i++)
                    SkyIslandBossForge.SetRing(lines[i], SkyIslandBossRules.StarfireRadius, charge, StarfireTint);
                yield return null;
            }
            if (!Aborted())
            {
                for (int i = 0; i < points.Count; i++)
                {
                    // catch 子句体内不能 yield return（CS1631）：这里只记账。
                    // 星焰是真的爆炸：留官方火球；三圈齐落只震第一发（VB-21）。
                    try
                    {
                        SkyIslandBossForge.Detonate(boss, points[i], SkyIslandBossRules.StarfireRadius, SkyIslandBossRules.StarfireDamage,
                            true, i == 0 ? SkyIslandImpactFx.BossShake : 0f, StarfireTint);
                    }
                    catch (Exception e) { Debug.LogWarning("[SkyIslandBoss] 星焰落点失败：" + e.Message); }
                }
            }
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i] == null) continue;
                rings.Remove(lines[i].gameObject);
                SkyIslandBossForge.ReleaseRing(lines[i]);
            }
            casting = false;
            if (Aborted()) yield break;
            casts++;
            if (casts < SkyIslandBossRules.CastsBeforeOverheat) yield break;
            casts = 0;
            EnterOverheat();
        }

        private void AddStarfirePoint(List<Vector3> points, Vector3 at)
        {
            Vector3 ground;
            points.Add(SkyIslandBossForge.SnapToGround(at, context, 0f, out ground) ? ground : at);
        }

        private void EnterOverheat()
        {
            overheated = true;
            overheatUntil = Time.time + SkyIslandBossRules.OverheatSeconds;
            overheatModifier = AddModifier(physicsStat, SkyIslandBossRules.OverheatDamageTaken);
            try { if (aiControl != null) aiControl.Pause(); }
            catch (Exception e) { Debug.LogWarning("[SkyIslandBoss] 匠首过热停手失败：" + e.Message); }
            ShowOverheatGlow(true);
            Announce("星炉过热了：匠首停下来散热，这几秒它挨打更疼。",
                "The furnace overheats. The Foreman stops to vent and takes harder hits for a few seconds.", true);
        }

        private void TickOverheat()
        {
            if (!overheated || Time.time < overheatUntil) return;
            ExitOverheat(true);
        }

        private void ExitOverheat(bool resume)
        {
            if (!overheated) return;
            overheated = false;
            RemoveModifier(physicsStat, ref overheatModifier);
            if (resume)
            {
                try { if (aiControl != null && aiControl.IsPaused) aiControl.Resume(CharacterMainControl.Main); }
                catch (Exception e) { Debug.LogWarning("[SkyIslandBoss] 匠首恢复失败：" + e.Message); }
            }
            ShowOverheatGlow(false);
            nextCastAt = Time.time + SkyIslandBossRules.StarfireInterval * 0.5f;
        }

        /// <summary>
        /// 过热的样子（VB-29.3）：背后一盏橙灯 0.2 s 淡入淡出（不再硬开硬关），加一股往上冒的灰白蒸汽；
        /// 散热结束蒸汽只停发射、已冒出来的自然散完。只在过热时存在，挂在 Boss 身上随它销毁。
        /// </summary>
        private void ShowOverheatGlow(bool on)
        {
            try
            {
                if (overheatGlow == null)
                {
                    if (!on) return;
                    overheatGlow = new GameObject("SkyIslandForemanOverheat");
                    overheatGlow.transform.SetParent(boss.transform, false);
                    overheatGlow.transform.localPosition = new Vector3(0f, 1.2f, -0.35f);
                    overheatLight = overheatGlow.AddComponent<Light>();
                    overheatLight.type = LightType.Point;
                    overheatLight.color = StarfireTint;
                    overheatLight.intensity = 0f;
                    overheatLight.range = 4f;
                    overheatLight.shadows = LightShadows.None;
                    overheatSteam = CreateOverheatSteam(overheatGlow.transform);
                }
                SkyIslandLightFade.FadeTo(overheatLight, on ? 3.5f : 0f, 0.2f, false);
                if (overheatSteam == null) return;
                if (on) overheatSteam.Play(true);
                else overheatSteam.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
            catch (Exception e) { Debug.LogWarning("[SkyIslandBoss] 过热光失败：" + e.Message); }
        }

        private static ParticleSystem CreateOverheatSteam(Transform parent)
        {
            Material material = BossRushFxMaterials.Get(BossRushFxBlend.Alpha);
            if (material == null) return null;
            GameObject go = new GameObject("OverheatSteam");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 0.25f, 0f);
            // 锥形默认朝 +Z：转到朝上。
            go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.maxParticles = 24;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1f, 1.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 1.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.4f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new Color(0.86f, 0.84f, 0.80f, 1f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 12f;
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 14f;
            shape.radius = 0.12f;
            ParticleSystem.ColorOverLifetimeModule fade = ps.colorOverLifetime;
            fade.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.3f, 0.15f), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;
            ParticleSystem.SizeOverLifetimeModule grow = ps.sizeOverLifetime;
            grow.enabled = true;
            grow.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 2.4f));
            ParticleSystemRenderer renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return ps;
        }

        // ====================================================================
        // 破甲断招
        // ====================================================================

        private bool HelmBroken()
        {
            return SkyIslandBossForge.PieceBroken(boss.GetHelmatItem(), BossRushItemIds.SkyIslandStarbrassVisorHelm);
        }

        private bool HarnessBroken()
        {
            return SkyIslandBossForge.PieceBroken(boss.GetArmorItem(), BossRushItemIds.SkyIslandStarfurnaceHarness);
        }

        private void TickBrokenGear()
        {
            if (helmEquipped && !helmBrokenAnnounced && HelmBroken())
            {
                helmBrokenAnnounced = true;
                Announce("星铜护目盔被打穿了，匠首的星焰没了准头，一次只落一处。",
                    "The starbrass visor is shot through. The Foreman's starfire loses its aim and lands one ring at a time.", false);
            }
            if (harnessEquipped && !harnessBrokenAnnounced && HarnessBroken())
            {
                harnessBrokenAnnounced = true;
                if (pylons.Count > 0) ApplyShield(true);
                Announce("星炉背甲的接头被打坏了，供能桩只能给它一半的护甲。",
                    "The furnace harness couplings are wrecked. The pylons can only give it half the plating now.", false);
            }
        }

        // ====================================================================
        // 生命周期
        // ====================================================================

        private bool Aborted()
        {
            return finished || boss == null || health == null || health.IsDead || (context.Valid != null && !context.Valid());
        }

        private void Announce(string cn, string en, bool urgent)
        {
            if (context != null && context.Report != null) context.Report(L10n.T(cn, en), urgent);
        }

        private void OnDead(DamageInfo damage)
        {
            if (finished) return;
            finished = true;
            Vector3 position = boss != null ? boss.transform.position : transform.position;
            Cleanup(false);
            Announce("残星匠首倒下了，背上的星炉慢慢熄了火。", "The Starforge Foreman falls and the furnace on its back gutters out.", false);
            SkyIslandBossForge.RaiseDefeated(profile, position);
        }

        private void Cleanup(bool resumeAi)
        {
            ExitOverheat(resumeAi);
            ClearPylons();
            for (int i = 0; i < rings.Count; i++) if (rings[i] != null) Destroy(rings[i]);
            rings.Clear();
            if (overheatGlow != null) Destroy(overheatGlow);
            overheatGlow = null;
            overheatLight = null;
            overheatSteam = null;
        }

        private void OnDestroy()
        {
            if (subscribed && health != null) health.OnDeadEvent.RemoveListener(OnDead);
            subscribed = false;
            finished = true;
            Cleanup(false);
            aiControl = null;
            context = null;
            profile = null;
            boss = null;
            health = null;
        }
    }
}
