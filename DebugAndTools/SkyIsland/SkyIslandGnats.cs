using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Duckov.Utilities;
using UnityEngine;
using UnityEngine.Events;

namespace BossRush
{
    /// <summary>
    /// COMPAT：天空岛内容批次四「云蚋」的局内 owner——夜里的蚊群：刷新、飞行、躲子弹、叮咬与痒、风晶灭蚊灯、药烟蒲扇、镜水寺的蛙卵，
    /// 以及全场共享的一个嗡嗡声发声体。规则全在 <see cref="SkyIslandMosquitoRules"/> 与 <see cref="SkyIslandGnatMotor"/>
    /// （离线模拟跑的是同一份代码），这里只把规则接到 Unity 与官方系统上。
    ///
    /// 生命周期挂在 <see cref="SkyIslandFieldcraft"/> 上（它持有夜风、灶火、耗材状态），随它创建、随它销毁；
    /// `SkyIslandSession.cs` 主文件一行不动。伤害代码只在这里：`SkyIslandFieldcraftGuard` 禁止批次三的局内 owner 出现伤害。
    ///
    /// 可被打中的轻量目标（不克隆角色）：每只蚊子是一个非触发 `SphereCollider`（伤害接收体层）+ `DamageReceiver`（useSimpleHealth）
    /// + `HealthSimpleBase`，阵营 `Teams.wolf`（`middle` 会被 canHurtSelf:false 的爆炸跳过）。先建成失活再激活：
    /// `HealthSimpleBase.Awake` 立刻取 `dmgReceiver.OnHurtEvent`，而血量是私有字段、只在 Awake 赋值，所以死掉的不复用。
    /// 官方死亡处理把接收体物体失活，这里逐帧看 `activeSelf` 发现死亡，不订阅任何事件。
    ///
    /// 纪律：玩法计时走游戏时间（`Time.time` / `Time.deltaTime`）；逐帧路径不查找场景、不分配、不用 LINQ 与闭包；
    /// 精灵表缺失时**整趟不刷云蚋**（看不见却会叮人的蚊子比没有更糟），只打一条 CriticalLog。
    /// </summary>
    internal sealed class SkyIslandGnats : IDisposable, ISkyIslandGnatSpace
    {
        /// <summary>本趟的蚊群 owner：开枪补丁只在按下扳机时读一次；离岛时为 null。</summary>
        internal static SkyIslandGnats Current { get; private set; }

        /// <summary>精灵表的帧序，与 `tools/gen_sky_island_gnat_sprites.py` 的 FRAME_NAMES 一一对应。</summary>
        internal const int FrameFlyUp = 0, FrameFlyMid = 1, FrameFlyDown = 2, FrameWindup = 3, FrameDash = 4, FrameBite = 5, FrameSplat = 6;
        internal const int FrameCount = 7;
        internal const int FramePixels = 16;
        internal const string SheetFile = "sky_island_gnat_sheet.png";
        internal const string BuzzFile = "gnat_buzz.wav";
        /// <summary>16 像素对应约 0.48 m：1080p 下约 32 px。</summary>
        internal const float PixelsPerUnit = FramePixels / 0.48f;
        /// <summary>15 m 内有蚊子才响嗡声。</summary>
        internal const float BuzzRange = 15f;
        private const int SortingOrder = 130;
        private const float WingFramesPerSecond = 20f;
        private const float SplatSeconds = 0.6f;
        private const string ModifierContext = "SkyIslandGnats";

        private sealed class Gnat
        {
            internal GameObject Root;
            internal Transform Visual;
            internal SpriteRenderer Sprite;
            internal TrailRenderer Trail;
            internal DamageReceiver Receiver;
            internal SkyIslandGnatMotor Motor;
            internal Vector3 Position, Wander, LeaveDirection, LastStep;
            internal float Phase, BiteReadyAt, BiteShownUntil, WanderUntil, LeaveUntil, OrbitAngle;
            internal bool Leaving;
        }

        private sealed class Zapper
        {
            internal GameObject Root;
            internal Light Glow;
            internal LineRenderer Arc;
            internal float Until, NextPulse, ArcUntil;
        }

        private sealed class Splat
        {
            internal GameObject Root;
            internal SpriteRenderer Sprite;
            internal float Until;
        }

        private static Texture2D sheet;
        private static Sprite[] frames;
        private static Material spriteMaterial, trailMaterial;
        private static bool artAttempted;

        private readonly SkyIslandSession session;
        private readonly SkyIslandFieldcraft owner;
        private readonly SkyIslandStoryService story;
        private readonly Transform root;
        private readonly int groundMask, wallMask, receiverLayer;
        private readonly System.Random random;
        private readonly Gnat[] gnats = new Gnat[SkyIslandMosquitoRules.MaxAlive];
        private readonly Zapper[] zappers = new Zapper[SkyIslandMosquitoRules.ZapperMaxActive];
        private readonly Splat[] splats = new Splat[SkyIslandMosquitoRules.MaxAlive];
        private readonly List<ZombieModeAttributeModifierRecord> itchRecords = new List<ZombieModeAttributeModifierRecord>();
        private readonly object modifierSource = new object();
        private readonly MethodInfo postSound, stopAll;
        private readonly object stopImmediately;
        private readonly bool usable;
        private GameObject buzzEmitter;
        private LineRenderer fanArc;
        private float nextSpawnCheck, spawnCooldownUntil, itch, sootheUntil = -1f, lastBiteAt = -100f, fanReadyAt, nextVeilCheck, fanArcUntil,
            nextBuzzAttempt;
        private int alive, bitesSinceSample;
        private bool night, lantern, veilCarried, itchy, buzzing, carryingSpawn, swarmExplained, lanternExplained, galeExplained,
            smokeExplained, failureReported, disposed;

        internal SkyIslandGnats(SkyIslandSession session, SkyIslandFieldcraft owner, SkyIslandStoryService story, Transform root, int groundMask)
        {
            if (session == null) throw new ArgumentNullException("session");
            if (owner == null) throw new ArgumentNullException("owner");
            if (root == null) throw new ArgumentNullException("root");
            this.session = session;
            this.owner = owner;
            this.story = story;
            this.root = root;
            this.groundMask = groundMask;
            wallMask = GameplayDataSettings.Layers.wallLayerMask.value;
            receiverLayer = LayerMask.NameToLayer("DamageReceiver");
            random = new System.Random(unchecked(Environment.TickCount ^ 0x51A7));
            // 正式构建不引用 FMOD：与 SkyIslandAmbience 同一套反射绑定。
            postSound = typeof(Duckov.AudioManager).GetMethod("PostCustomSFX", BindingFlags.Public | BindingFlags.Static,
                null, new[] { typeof(string), typeof(GameObject), typeof(bool) }, null);
            stopAll = typeof(Duckov.AudioObject).GetMethod("StopAll", BindingFlags.NonPublic | BindingFlags.Instance);
            if (stopAll != null) stopImmediately = Enum.ToObject(stopAll.GetParameters()[0].ParameterType, 1);
            usable = EnsureArt();
            if (usable)
            {
                buzzEmitter = new GameObject("SkyIslandGnatBuzz");
                buzzEmitter.transform.SetParent(root, false);
            }
            Current = this;
        }

        internal int Alive { get { return alive; } }
        internal bool Usable { get { return usable && !disposed; } }
        internal bool CarryingSpawn { get { return carryingSpawn; } }
        internal bool Itching { get { return itch > 0f || itchy; } }
        internal bool VeilCarried { get { return veilCarried; } }
        internal bool NightNow { get { return SkyIslandNight.IsNight(SkyIslandLighting.ClockHours()); } }
        internal bool FanReady { get { return Usable && Time.time >= fanReadyAt; } }

        internal bool CanDeployZapper
        {
            get
            {
                if (!Usable) return false;
                for (int i = 0; i < zappers.Length; i++) if (zappers[i] == null) return true;
                return false;
            }
        }

        /// <summary>风晶灯的冲刺路径检测：途中没有墙（`ISkyIslandGnatSpace`）。</summary>
        public bool DashClear(SkyIslandGnatVec from, SkyIslandGnatVec to)
        {
            return !Physics.Linecast(ToVector(from), ToVector(to), wallMask, QueryTriggerInteraction.Ignore);
        }

        #region 每帧

        /// <summary>
        /// 由 <see cref="SkyIslandFieldcraft.Tick"/> 每帧调用（不走 0.5 秒节流：冲刺按帧推进）。没有蚊子、没有灭蚊灯时几乎是空转。
        /// 暂停菜单、拍照模式与剧情面板把 timeScale 压到 0 时 `Time.deltaTime` 为 0，蚊子原地不动、也不叮人。
        /// </summary>
        internal void Frame(float now, float dt, CharacterMainControl player)
        {
            if (!Usable || player == null) return;
            try
            {
                int frame = Time.frameCount;
                Quaternion view = ViewRotation();
                Vector3 playerPosition = player.transform.position;
                Vector3 muzzle, aim;
                ReadAim(player, out muzzle, out aim);
                SkyIslandGnatVec muzzleVec = ToVec(muzzle), aimVec = ToVec(aim);
                bool moved = false;
                Gnat nearest = null;
                float nearestSqr = BuzzRange * BuzzRange;
                for (int i = 0; i < gnats.Length; i++)
                {
                    Gnat gnat = gnats[i];
                    if (gnat == null) continue;
                    if (gnat.Root == null || !gnat.Root.activeSelf)
                    {
                        Remove(i, true, now, view);
                        continue;
                    }
                    if (dt > 0f)
                    {
                        Steer(gnat, player, playerPosition, muzzleVec, aimVec, now, dt, frame);
                        moved = true;
                    }
                    if (gnat.Leaving && now >= gnat.LeaveUntil)
                    {
                        Remove(i, false, now, view);
                        continue;
                    }
                    Animate(gnat, now, view);
                    float sqr = (gnat.Position - playerPosition).sqrMagnitude;
                    if (sqr < nearestSqr)
                    {
                        nearestSqr = sqr;
                        nearest = gnat;
                    }
                }
                // 物理不自动同步变换：挪完蚊群同步一次，官方弹的扫掠才看得到它们的新位置（先例 SkyIslandSession 装配时的 SyncTransforms）。
                if (moved) Physics.SyncTransforms();
                TickZappers(now, player);
                TickSplats(now, view);
                if (fanArc != null && fanArc.enabled && now >= fanArcUntil) fanArc.enabled = false;
                TickBuzz(nearest, now);
            }
            catch (Exception e)
            {
                Fail("frame", e);
            }
        }

        private void Steer(Gnat gnat, CharacterMainControl player, Vector3 playerPosition, SkyIslandGnatVec muzzle, SkyIslandGnatVec aim,
            float now, float dt, int frame)
        {
            SkyIslandGnatMotor motor = gnat.Motor;
            float dazzle = SkyIslandMosquitoRules.LanternDazzleRadius;
            motor.Dazzled = lantern && (gnat.Position - playerPosition).sqrMagnitude <= dazzle * dazzle;
            motor.Tick(dt);
            if (!gnat.Leaving) motor.OnAim(ToVec(gnat.Position), muzzle, aim, frame, this);
            Vector3 step = ToVector(motor.Step(dt));
            if (step.sqrMagnitude <= 0f && !motor.Busy) step = Cruise(gnat, playerPosition, now, dt);
            float cap = SkyIslandMosquitoRules.DashSpeed * dt;
            if (step.sqrMagnitude > cap * cap) step = step.normalized * cap;
            gnat.Position += step;
            gnat.LastStep = step;
            gnat.Root.transform.position = gnat.Position;
            if (!gnat.Leaving && motor.Phase == SkyIslandGnatPhase.Idle) TryBite(gnat, player, playerPosition, now);
        }

        /// <summary>不冲刺时怎么飞：散开时往外飞；有灭蚊灯在嗡就被引过去；否则围着玩家的脖子打转，到点就扑上去。</summary>
        private Vector3 Cruise(Gnat gnat, Vector3 playerPosition, float now, float dt)
        {
            if (now >= gnat.WanderUntil)
            {
                gnat.Wander = new Vector3((float)(random.NextDouble() * 2.0 - 1.0), (float)(random.NextDouble() - 0.5) * 0.4f,
                    (float)(random.NextDouble() * 2.0 - 1.0)) * 1.2f;
                gnat.WanderUntil = now + 0.25f + (float)random.NextDouble() * 0.15f;
            }
            Vector3 velocity;
            if (gnat.Leaving) velocity = gnat.LeaveDirection * (SkyIslandMosquitoRules.CruiseSpeed * 1.4f);
            else
            {
                Vector3 target;
                Zapper lure = NearestLure(gnat.Position);
                if (lure != null) target = lure.Root.transform.position + Vector3.up * 1.1f;
                else
                {
                    Vector3 neck = playerPosition + Vector3.up * (SkyIslandMosquitoRules.HoverHeight + SkyIslandMosquitoRules.HoverBob *
                        Mathf.Sin(now * 6f + gnat.Phase));
                    // 到点了就扑向脖子；没到点就在外面打转（带着云苔纱笠时只能在一米外转）。
                    float radius = now >= gnat.BiteReadyAt ? 0.2f : SkyIslandMosquitoRules.OrbitRadiusFor(veilCarried);
                    gnat.OrbitAngle += dt * (2.6f + gnat.Phase * 0.3f);
                    target = neck + new Vector3(Mathf.Cos(gnat.OrbitAngle), 0f, Mathf.Sin(gnat.OrbitAngle)) * radius;
                }
                Vector3 toward = target - gnat.Position;
                float distance = toward.magnitude;
                float speed = Mathf.Min(SkyIslandMosquitoRules.CruiseSpeed, distance * 4f);
                velocity = distance > 1e-4f ? toward * (speed / distance) : Vector3.zero;
            }
            velocity += gnat.Wander;
            if (velocity.sqrMagnitude > SkyIslandMosquitoRules.CruiseSpeed * SkyIslandMosquitoRules.CruiseSpeed * 2.25f)
                velocity = velocity.normalized * (SkyIslandMosquitoRules.CruiseSpeed * 1.5f);
            return velocity * dt;
        }

        private void TryBite(Gnat gnat, CharacterMainControl player, Vector3 playerPosition, float now)
        {
            Vector3 neck = playerPosition + Vector3.up * SkyIslandMosquitoRules.HoverHeight;
            float reach = SkyIslandMosquitoRules.BiteReach;
            if ((gnat.Position - neck).sqrMagnitude > reach * reach) return;
            if (!SkyIslandMosquitoRules.BiteReady(now, gnat.BiteReadyAt, lastBiteAt, veilCarried)) return;
            Health health = player.Health;
            if (health == null) return;
            gnat.BiteReadyAt = now + SkyIslandMosquitoRules.BiteDelay(random.NextDouble(), veilCarried);
            // 生命低于下限时只绕不叮：到点照样重新计时，免得血一回上来六只一起下嘴。
            if (!SkyIslandMosquitoRules.HealthAllowsBite(health.CurrentHealth, health.MaxHealth)) return;
            lastBiteAt = now;
            gnat.BiteShownUntil = now + 0.25f;
            DamageInfo bite = new DamageInfo(null);
            bite.damageValue = SkyIslandMosquitoRules.BiteDamage;
            bite.damageType = DamageTypes.realDamage;
            bite.isFromBuffOrEffect = true;
            // 一口就是 1 点：不跟难度倍率走，否则高难度下云蚋会变成数值墙（官方小于 1 的伤害本来就会被抬到 1）。
            bite.ignoreDifficulty = true;
            bite.damagePoint = neck;
            bite.damageNormal = Vector3.up;
            health.Hurt(bite);
            bitesSinceSample++;
        }

        private void Animate(Gnat gnat, float now, Quaternion view)
        {
            SkyIslandGnatPhase phase = gnat.Motor.Phase;
            Quaternion rotation = view;
            int index;
            if (phase == SkyIslandGnatPhase.Windup) index = FrameWindup;
            else if (phase == SkyIslandGnatPhase.Dash)
            {
                index = FrameDash;
                Vector3 direction = ToVector(gnat.Motor.DashDirection);
                float screenX = Vector3.Dot(direction, view * Vector3.right), screenY = Vector3.Dot(direction, view * Vector3.up);
                rotation = view * Quaternion.Euler(0f, 0f, Mathf.Atan2(screenY, screenX) * Mathf.Rad2Deg);
                gnat.Sprite.flipX = false;
            }
            else
            {
                index = now < gnat.BiteShownUntil ? FrameBite : (int)(now * WingFramesPerSecond + gnat.Phase * 3f) % 3;
                float screenX = Vector3.Dot(gnat.LastStep, view * Vector3.right);
                if (Mathf.Abs(screenX) > 1e-4f) gnat.Sprite.flipX = screenX < 0f;
            }
            gnat.Sprite.sprite = frames[index];
            gnat.Visual.rotation = rotation;
            gnat.Trail.emitting = phase == SkyIslandGnatPhase.Dash;
        }

        #endregion

        #region 采样与刷新（0.5 游戏秒一次）

        /// <summary>
        /// 由局内 owner 的夜风推进调用（与夜风同一次地面射线）：判散、判刷、推进痒。
        /// <paramref name="windLevel"/> 是环境风（噬风之核不改它），<paramref name="inSmoke"/> 是站在灶火的烟里，<paramref name="nearLamp"/> 是点起来的风晶灯附近。
        /// </summary>
        internal void Sample(bool isNight, int windLevel, string region, CharacterMainControl player, bool incense, bool lanternLit, bool inSmoke,
            bool nearLamp, float elapsed)
        {
            if (!Usable || player == null) return;
            try
            {
                float now = Time.time;
                night = isNight;
                lantern = lanternLit;
                Vector3 position = player.transform.position;
                string reason;
                bool enemiesNear = !session.CanOpenStoryPanel(out reason);
                RefreshVeil(now);
                TickItch(player, now, elapsed);
                Scatter(position, now, windLevel, enemiesNear, inSmoke, incense);
                if (now < nextSpawnCheck) return;
                nextSpawnCheck = now + SkyIslandMosquitoRules.SpawnCheckSeconds;
                if (now < spawnCooldownUntil) return;
                Vector3 local = root.InverseTransformPoint(position);
                var site = new SkyIslandGnatSite
                {
                    Night = night, WindLevel = windLevel, InSmoke = inSmoke, Incense = incense, EnemiesNear = enemiesNear,
                    WaterEdgeDistance = SkyIslandMosquitoRules.WaterEdgeDistance(local.x, local.z),
                    FrogsReleased = story != null ? SkyIslandMosquitoRules.FrogsReleased(story.Current) : 0,
                    IslandCore = SkyIslandMosquitoRules.IsIslandCore(region, local.x, local.z),
                    Lantern = lanternLit, NearLamp = nearLamp, NearZapper = NearestZapper(position, SkyIslandMosquitoRules.ZapperLureRadius) != null
                };
                float weight = SkyIslandMosquitoRules.SpawnWeight(site);
                if (weight <= 0f || random.NextDouble() >= SkyIslandMosquitoRules.SpawnChance(weight)) return;
                int room = SkyIslandMosquitoRules.RoomFor(alive, SkyIslandMosquitoRules.GroupSize(weight, random.NextDouble()));
                if (room <= 0) return;
                spawnCooldownUntil = now + SkyIslandMosquitoRules.SpawnCooldown(random.NextDouble());
                double bearing = random.NextDouble() * Math.PI * 2.0;
                float distance = SkyIslandMosquitoRules.SpawnDistance(random.NextDouble());
                Vector3 center = position + new Vector3((float)Math.Cos(bearing), 0f, (float)Math.Sin(bearing)) * distance +
                    Vector3.up * SkyIslandMosquitoRules.HoverHeight;
                int spawned = 0;
                for (int k = 0; k < room; k++)
                {
                    Vector3 at = center + new Vector3((float)(random.NextDouble() * 2.4 - 1.2), (float)(random.NextDouble() * 0.4 - 0.2),
                        (float)(random.NextDouble() * 2.4 - 1.2));
                    if (Spawn(at, now)) spawned++;
                }
                if (spawned == 0) return;
                Debug.Log("[SkyIslandGnats] SWARM count=" + spawned + " weight=" + weight.ToString("0.00") + " alive=" + alive);
                if (!swarmExplained)
                {
                    swarmExplained = true;
                    session.Announce(SkyIslandMosquitoRules.SwarmArrives, false);
                }
                else if (lanternLit && !lanternExplained)
                {
                    lanternExplained = true;
                    session.Announce(SkyIslandMosquitoRules.LanternDraws, false);
                }
            }
            catch (Exception e)
            {
                Fail("sample", e);
            }
        }

        /// <summary>天亮、大风、烟里或焚着驱风香、附近来了敌人：这一群散开飞走；跑得太远的直接散掉。</summary>
        private void Scatter(Vector3 position, float now, int windLevel, bool enemiesNear, bool inSmoke, bool incense)
        {
            bool leave = !night || windLevel >= 2 || enemiesNear || inSmoke || incense;
            float despawn = SkyIslandMosquitoRules.DespawnDistance;
            bool any = false;
            for (int i = 0; i < gnats.Length; i++)
            {
                Gnat gnat = gnats[i];
                if (gnat == null) continue;
                if ((gnat.Position - position).sqrMagnitude > despawn * despawn)
                {
                    Remove(i, false, now, Quaternion.identity);
                    continue;
                }
                if (!leave || gnat.Leaving) continue;
                any = true;
                gnat.Leaving = true;
                gnat.Motor.Cancel();
                Vector3 away = gnat.Position - position;
                away.y = 0f;
                gnat.LeaveDirection = (away.sqrMagnitude > 1e-4f ? away.normalized : Vector3.forward) + Vector3.up * 0.35f;
                gnat.LeaveUntil = now + 1.4f;
            }
            if (!any || !night) return;
            if (windLevel >= 2 && !galeExplained)
            {
                galeExplained = true;
                session.Announce(SkyIslandMosquitoRules.GaleScatters, false);
            }
            else if ((inSmoke || incense) && !smokeExplained)
            {
                smokeExplained = true;
                session.Announce(SkyIslandMosquitoRules.SmokeScatters, false);
            }
        }

        private bool Spawn(Vector3 at, float now)
        {
            int slot = -1;
            for (int i = 0; i < gnats.Length && slot < 0; i++) if (gnats[i] == null) slot = i;
            if (slot < 0) return false;
            GameObject go = null;
            try
            {
                go = new GameObject("SkyIslandGnat");
                go.SetActive(false);
                go.transform.SetParent(root, true);
                go.transform.position = at;
                if (receiverLayer >= 0) go.layer = receiverLayer;
                SphereCollider collider = go.AddComponent<SphereCollider>();
                collider.radius = SkyIslandMosquitoRules.GnatRadius;
                collider.isTrigger = false;
                Rigidbody body = go.AddComponent<Rigidbody>();
                body.isKinematic = true;
                body.useGravity = false;
                DamageReceiver receiver = go.AddComponent<DamageReceiver>();
                receiver.useSimpleHealth = true;
                if (receiver.OnHurtEvent == null) receiver.OnHurtEvent = new UnityEvent<DamageInfo>();
                if (receiver.OnDeadEvent == null) receiver.OnDeadEvent = new UnityEvent<DamageInfo>();
                HealthSimpleBase health = go.AddComponent<HealthSimpleBase>();
                health.team = Teams.wolf;
                health.maxHealthValue = SkyIslandMosquitoRules.GnatHealth;
                health.dmgReceiver = receiver;
                receiver.simpleHealth = health;
                GameObject visual = new GameObject("Sprite");
                visual.transform.SetParent(go.transform, false);
                SpriteRenderer sprite = visual.AddComponent<SpriteRenderer>();
                sprite.sharedMaterial = spriteMaterial;
                sprite.sprite = frames[FrameFlyMid];
                sprite.sortingOrder = SortingOrder;
                TrailRenderer trail = visual.AddComponent<TrailRenderer>();
                trail.sharedMaterial = trailMaterial;
                trail.time = 0.12f;
                trail.minVertexDistance = 0.05f;
                trail.widthMultiplier = 0.16f;
                trail.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
                trail.startColor = new Color(0.86f, 0.95f, 1f, 0.7f);
                trail.endColor = new Color(0.86f, 0.95f, 1f, 0f);
                trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                trail.receiveShadows = false;
                trail.sortingOrder = SortingOrder - 1;
                trail.emitting = false;
                go.SetActive(true);
                gnats[slot] = new Gnat
                {
                    Root = go, Visual = visual.transform, Sprite = sprite, Trail = trail, Receiver = receiver,
                    Motor = new SkyIslandGnatMotor((random.Next() & 1) == 1), Position = at, Phase = (float)random.NextDouble() * 3f,
                    BiteReadyAt = now + SkyIslandMosquitoRules.BiteDelay(random.NextDouble(), veilCarried),
                    OrbitAngle = (float)(random.NextDouble() * Math.PI * 2.0)
                };
                alive++;
                return true;
            }
            catch (Exception e)
            {
                if (go != null) UnityEngine.Object.Destroy(go);
                Fail("spawn", e);
                return false;
            }
        }

        private void Remove(int index, bool killed, float now, Quaternion view)
        {
            Gnat gnat = gnats[index];
            gnats[index] = null;
            if (gnat == null) return;
            alive = Math.Max(0, alive - 1);
            if (gnat.Root != null) UnityEngine.Object.Destroy(gnat.Root);
            if (killed) ShowSplat(gnat.Position, now, view);
        }

        #endregion

        #region 痒与纱笠

        private void RefreshVeil(float now)
        {
            if (now < nextVeilCheck) return;
            nextVeilCheck = now + SkyIslandFieldcraftRules.CarryCheckInterval;
            try { veilCarried = owner.CountInPack(BossRushItemIds.SkyIslandCloudmossVeil) > 0; }
            catch (Exception) { veilCarried = false; }
        }

        private void TickItch(CharacterMainControl player, float now, float elapsed)
        {
            itch = SkyIslandMosquitoRules.StepItch(itch, bitesSinceSample, elapsed, now < sootheUntil);
            bitesSinceSample = 0;
            bool next = SkyIslandMosquitoRules.NextItchy(itchy, itch);
            if (next == itchy) return;
            itchy = next;
            if (itchy)
            {
                RuntimeStatModifierTracker.TryAdd(player, ZombieModeStatNames.StaminaRecoverRate, SkyIslandMosquitoRules.ItchStaminaRecover,
                    modifierSource, itchRecords, ModifierContext);
                session.Announce(SkyIslandMosquitoRules.ItchStarted, true);
            }
            else
            {
                RuntimeStatModifierTracker.RemoveAll(itchRecords, ModifierContext);
                session.Announce(SkyIslandMosquitoRules.ItchEnded, false);
            }
        }

        /// <summary>星苔药膏：止痒，而且这一阵再被叮也不会痒（药膏管痒，眠苔的苔药管伤）。返回要读给玩家的那一句。</summary>
        internal string Soothe()
        {
            if (disposed) return null;
            ClearItch();
            sootheUntil = Time.time + SkyIslandMosquitoRules.SalveSootheSeconds;
            return SkyIslandMosquitoRules.Soothed;
        }

        /// <summary>眠苔的苔药敷上了：顺手把痒也止了（不附带药膏那一阵的防叮）。没有蚊群 owner 时什么也不做。</summary>
        internal static void RemedyClearsItch()
        {
            SkyIslandGnats current = Current;
            if (current != null && !current.disposed) current.ClearItch();
        }

        private void ClearItch()
        {
            itch = 0f;
            bitesSinceSample = 0;
            if (!itchy) return;
            itchy = false;
            RuntimeStatModifierTracker.RemoveAll(itchRecords, ModifierContext);
        }

        #endregion

        #region 开枪、蒲扇与灭蚊灯

        /// <summary>
        /// 玩家的弹生成时（`SkyIslandGnatProjectilePatch`）：只看主角自己的直线弹，逐只登记给躲闪状态机。异常一律吞在这里，不进官方开枪流程。
        /// </summary>
        internal void OnProjectile(Projectile projectile, ProjectileContext context)
        {
            if (!Usable || alive == 0 || projectile == null) return;
            try
            {
                CharacterMainControl main = CharacterMainControl.Main;
                bool mine = main != null && context.fromCharacter == main;
                if (!SkyIslandMosquitoRules.ShouldTrackProjectile(mine, context.gravity, context.explosionRange)) return;
                Vector3 origin = context.firstFrameCheck ? context.firstFrameCheckStartPoint : projectile.transform.position;
                var shot = new SkyIslandGnatShot(ToVec(origin), ToVec(context.direction), context.speed, context.distance, projectile.radius);
                int frame = Time.frameCount;
                for (int i = 0; i < gnats.Length; i++)
                {
                    Gnat gnat = gnats[i];
                    if (gnat == null || gnat.Leaving || gnat.Root == null || !gnat.Root.activeSelf) continue;
                    gnat.Motor.OnShot(ToVec(gnat.Position), shot, frame, this);
                }
            }
            catch (Exception e)
            {
                Fail("projectile", e);
            }
        }

        /// <summary>
        /// 药烟蒲扇扇一下：扇面近处的扑落，远处的扇退并晕一小会儿。**近身、瞬时**——灭蚊灯管的是一片地方、一段时间。
        /// </summary>
        internal bool SwingFan(CharacterMainControl player, out string message)
        {
            message = SkyIslandMosquitoRules.FanResting;
            if (!FanReady || player == null) return false;
            float now = Time.time;
            fanReadyAt = now + SkyIslandMosquitoRules.FanCooldownSeconds;
            Vector3 origin = player.transform.position;
            Vector3 facing = player.GetCurrentAimPoint() - origin;
            facing.y = 0f;
            if (facing.sqrMagnitude < 0.01f) facing = player.transform.forward;
            int downed = 0, pushed = 0;
            for (int i = 0; i < gnats.Length; i++)
            {
                Gnat gnat = gnats[i];
                if (gnat == null || gnat.Root == null || !gnat.Root.activeSelf) continue;
                int effect = SkyIslandMosquitoRules.FanEffect(ToVec(origin), ToVec(facing), ToVec(gnat.Position));
                if (effect == 1)
                {
                    HurtGnat(gnat, SkyIslandMosquitoRules.FanDamage, player);
                    downed++;
                }
                else if (effect == 2)
                {
                    gnat.Motor.Knockback(ToVec(gnat.Position - origin), SkyIslandMosquitoRules.FanKnockDistance, SkyIslandMosquitoRules.FanStunSeconds);
                    pushed++;
                }
            }
            ShowFanArc(origin, facing.normalized, now);
            message = SkyIslandMosquitoRules.FanSwept(downed, pushed);
            if (story != null) story.LogTiming("fan", downed + "/" + pushed);
            return true;
        }

        /// <summary>放下一盏风晶灭蚊灯：嗡声把附近的云蚋引过去，定时电落半径内的；**一片地方、一段时间**。同时至多两盏。</summary>
        internal bool DeployZapper(CharacterMainControl player, out string message)
        {
            message = SkyIslandMosquitoRules.ZapperLimit;
            if (!CanDeployZapper || player == null) return false;
            int slot = -1;
            for (int i = 0; i < zappers.Length && slot < 0; i++) if (zappers[i] == null) slot = i;
            float now = Time.time;
            Vector3 at = player.transform.position;
            RaycastHit hit;
            Vector3 probe = at + player.transform.forward * 0.8f + Vector3.up * 1.5f;
            if (Physics.Raycast(probe, Vector3.down, out hit, 4f, groundMask, QueryTriggerInteraction.Ignore)) at = hit.point;
            GameObject go = new GameObject("SkyIslandGnatZapper");
            go.transform.SetParent(root, true);
            go.transform.position = at;
            Light glow = go.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.color = new Color(0.62f, 0.86f, 1f);
            glow.range = 6f;
            glow.intensity = 1.5f;
            glow.shadows = LightShadows.None;
            LineRenderer cage = Line(go.transform, "Cage", 13, 0.04f, new Color(0.7f, 0.9f, 1f, 0.9f));
            cage.loop = true;
            for (int i = 0; i < 13; i++)
            {
                float angle = i * Mathf.PI * 2f / 13f;
                cage.SetPosition(i, new Vector3(Mathf.Cos(angle) * 0.22f, 0.55f + (i % 2) * 0.08f, Mathf.Sin(angle) * 0.22f));
            }
            LineRenderer arc = Line(go.transform, "Arc", 6, 0.05f, new Color(0.8f, 0.95f, 1f, 1f));
            arc.useWorldSpace = true;
            arc.enabled = false;
            zappers[slot] = new Zapper { Root = go, Glow = glow, Arc = arc, Until = now + SkyIslandMosquitoRules.ZapperBurnSeconds, NextPulse = now + 0.3f };
            message = SkyIslandMosquitoRules.ZapperLit;
            if (story != null) story.LogTiming("zapper", slot.ToString());
            return true;
        }

        private void TickZappers(float now, CharacterMainControl player)
        {
            for (int z = 0; z < zappers.Length; z++)
            {
                Zapper zapper = zappers[z];
                if (zapper == null) continue;
                if (zapper.Root == null || now >= zapper.Until)
                {
                    if (zapper.Root != null) UnityEngine.Object.Destroy(zapper.Root);
                    zappers[z] = null;
                    session.Announce(SkyIslandMosquitoRules.ZapperOut, false);
                    continue;
                }
                zapper.Glow.intensity = 1.3f + 0.25f * Mathf.Sin(now * 9f + z);
                if (zapper.Arc.enabled && now >= zapper.ArcUntil) zapper.Arc.enabled = false;
                if (now < zapper.NextPulse) continue;
                zapper.NextPulse = now + SkyIslandMosquitoRules.ZapperPulseSeconds;
                SkyIslandGnatVec center = ToVec(zapper.Root.transform.position);
                Gnat target = null;
                float best = float.MaxValue;
                for (int i = 0; i < gnats.Length; i++)
                {
                    Gnat gnat = gnats[i];
                    if (gnat == null || gnat.Root == null || !gnat.Root.activeSelf || !SkyIslandMosquitoRules.ZapperReaches(center, ToVec(gnat.Position))) continue;
                    float sqr = (gnat.Position - zapper.Root.transform.position).sqrMagnitude;
                    if (sqr < best) { best = sqr; target = gnat; }
                }
                if (target == null) continue;
                Vector3 from = zapper.Root.transform.position + Vector3.up * 0.6f;
                for (int p = 0; p < 6; p++)
                {
                    float t = p / 5f;
                    Vector3 point = Vector3.Lerp(from, target.Position, t);
                    if (p > 0 && p < 5)
                        point += new Vector3((float)(random.NextDouble() - 0.5), (float)(random.NextDouble() - 0.5), (float)(random.NextDouble() - 0.5)) * 0.25f;
                    zapper.Arc.SetPosition(p, point);
                }
                zapper.Arc.enabled = true;
                zapper.ArcUntil = now + 0.12f;
                HurtGnat(target, SkyIslandMosquitoRules.ZapperDamage, player);
            }
        }

        private Zapper NearestLure(Vector3 position)
        {
            return NearestZapper(position, SkyIslandMosquitoRules.ZapperLureRadius);
        }

        private Zapper NearestZapper(Vector3 position, float radius)
        {
            Zapper nearest = null;
            float best = radius * radius;
            for (int i = 0; i < zappers.Length; i++)
            {
                Zapper zapper = zappers[i];
                if (zapper == null || zapper.Root == null) continue;
                Vector3 delta = zapper.Root.transform.position - position;
                delta.y = 0f;
                if (delta.sqrMagnitude <= best)
                {
                    best = delta.sqrMagnitude;
                    nearest = zapper;
                }
            }
            return nearest;
        }

        private static void HurtGnat(Gnat gnat, float damage, CharacterMainControl player)
        {
            if (gnat.Receiver == null) return;
            DamageInfo hit = new DamageInfo(player);
            hit.damageValue = damage;
            hit.isFromBuffOrEffect = true;
            hit.damagePoint = gnat.Position;
            hit.damageNormal = Vector3.up;
            gnat.Receiver.Hurt(hit);
        }

        #endregion

        #region 蛙卵

        /// <summary>夜里在镜水寺池边捧一团蛙卵（用掉一把云苔纤维）。只活在这一趟：人倒下或离岛就没了。</summary>
        internal bool TakeSpawn(out string message)
        {
            if (!Usable || story == null)
            {
                message = SkyIslandMosquitoRules.SpawnNeedsNight;
                return false;
            }
            if (SkyIslandMosquitoRules.FrogsComplete(story.Current)) { message = SkyIslandMosquitoRules.FrogsAlreadyHome; return false; }
            if (carryingSpawn) { message = SkyIslandMosquitoRules.SpawnAlreadyCarried; return false; }
            if (!NightNow) { message = SkyIslandMosquitoRules.SpawnNeedsNight; return false; }
            if (owner.CountInPack(BossRushItemIds.SkyIslandCloudmossFiber) < 1 || !owner.ConsumeOne(BossRushItemIds.SkyIslandCloudmossFiber))
            {
                message = SkyIslandMosquitoRules.SpawnNeedsFiber;
                return false;
            }
            carryingSpawn = true;
            message = SkyIslandMosquitoRules.SpawnTaken;
            story.LogTiming("frogspawn", "take");
            return true;
        }

        /// <summary>把蛙卵放回蛙鸣池：**先记手记**（`Frog_n`），记上了才算放下。写屏障下这趟放不下，但蛙卵还捧在手里。</summary>
        internal bool ReleaseSpawn(out string message)
        {
            if (!Usable || story == null || !carryingSpawn)
            {
                message = SkyIslandMosquitoRules.SpawnNeedsNight;
                return false;
            }
            string note = SkyIslandMosquitoRules.NextFrogNote(story.Current);
            if (note == null)
            {
                carryingSpawn = false;
                message = SkyIslandMosquitoRules.FrogsAlreadyHome;
                return false;
            }
            if (!story.CanWrite)
            {
                message = story.SaveStatus;
                return false;
            }
            string recorded;
            if (!story.RecordNote(note, out recorded))
            {
                message = recorded;
                return false;
            }
            carryingSpawn = false;
            message = SkyIslandMosquitoRules.Released(SkyIslandMosquitoRules.FrogsReleased(story.Current));
            story.LogTiming("frogspawn", note);
            Debug.Log("[SkyIslandGnats] FROGS released=" + SkyIslandMosquitoRules.FrogsReleased(story.Current));
            return true;
        }

        #endregion

        #region 表现

        private void ShowSplat(Vector3 at, float now, Quaternion view)
        {
            int slot = 0;
            float oldest = float.MaxValue;
            for (int i = 0; i < splats.Length; i++)
            {
                if (splats[i] == null) { slot = i; oldest = float.MinValue; break; }
                if (splats[i].Until < oldest) { oldest = splats[i].Until; slot = i; }
            }
            Splat splat = splats[slot];
            if (splat == null || splat.Root == null)
            {
                GameObject go = new GameObject("SkyIslandGnatSplat");
                go.transform.SetParent(root, true);
                SpriteRenderer sprite = go.AddComponent<SpriteRenderer>();
                sprite.sharedMaterial = spriteMaterial;
                sprite.sprite = frames[FrameSplat];
                sprite.sortingOrder = SortingOrder;
                splat = new Splat { Root = go, Sprite = sprite };
                splats[slot] = splat;
            }
            splat.Root.SetActive(true);
            splat.Root.transform.position = at;
            splat.Root.transform.rotation = view;
            splat.Sprite.color = Color.white;
            splat.Until = now + SplatSeconds;
        }

        private void TickSplats(float now, Quaternion view)
        {
            for (int i = 0; i < splats.Length; i++)
            {
                Splat splat = splats[i];
                if (splat == null || splat.Root == null || !splat.Root.activeSelf) continue;
                float left = splat.Until - now;
                if (left <= 0f) { splat.Root.SetActive(false); continue; }
                splat.Root.transform.rotation = view;
                splat.Sprite.color = new Color(1f, 1f, 1f, Mathf.Clamp01(left / SplatSeconds));
            }
        }

        private void ShowFanArc(Vector3 origin, Vector3 facing, float now)
        {
            if (fanArc == null)
            {
                GameObject go = new GameObject("SkyIslandGnatFanArc");
                go.transform.SetParent(root, true);
                fanArc = Line(go.transform, "Arc", 9, 0.06f, new Color(0.85f, 0.95f, 0.8f, 0.55f));
                fanArc.useWorldSpace = true;
            }
            float half = SkyIslandMosquitoRules.FanHalfAngleDegrees;
            for (int i = 0; i < 9; i++)
            {
                Vector3 direction = Quaternion.Euler(0f, -half + i * (half * 2f / 8f), 0f) * facing;
                fanArc.SetPosition(i, origin + Vector3.up * 1.1f + direction * SkyIslandMosquitoRules.FanRange);
            }
            fanArc.enabled = true;
            fanArcUntil = now + 0.15f;
        }

        private static LineRenderer Line(Transform parent, string name, int points, float width, Color color)
        {
            GameObject child = new GameObject(name);
            child.transform.SetParent(parent, false);
            LineRenderer line = child.AddComponent<LineRenderer>();
            line.sharedMaterial = trailMaterial;
            line.useWorldSpace = false;
            line.positionCount = points;
            line.startWidth = line.endWidth = width;
            line.startColor = line.endColor = color;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.sortingOrder = SortingOrder;
            return line;
        }

        private void TickBuzz(Gnat nearest, float now)
        {
            if (buzzEmitter == null) return;
            if (nearest == null)
            {
                if (buzzing) StopBuzz();
                return;
            }
            buzzEmitter.transform.position = nearest.Position;
            if (buzzing || now < nextBuzzAttempt) return;
            nextBuzzAttempt = now + 5f;
            try
            {
                if (postSound == null || stopAll == null) throw new InvalidOperationException("官方空间音效生命周期入口缺失");
                string path = Path.Combine(Path.Combine(ModBehaviour.GetModPath(), "Assets/Sounds/SkyIsland"), BuzzFile);
                if (!File.Exists(path)) throw new FileNotFoundException("云蚋嗡声未部署", BuzzFile);
                buzzing = postSound.Invoke(null, new object[] { path, buzzEmitter, true }) != null;
            }
            catch (Exception e)
            {
                buzzing = false;
                nextBuzzAttempt = float.MaxValue;
                ModBehaviour.DevLog("[SkyIslandGnats] [WARNING] 嗡声不可用：" + e.Message);
            }
        }

        private void StopBuzz()
        {
            buzzing = false;
            try
            {
                // AudioObject 不承诺销毁时停掉循环：先 StopAll，再销毁发声体（与 SkyIslandAmbience 同一口径）。
                Duckov.AudioObject audio = buzzEmitter == null ? null : buzzEmitter.GetComponent<Duckov.AudioObject>();
                if (audio != null && stopAll != null) stopAll.Invoke(audio, new[] { stopImmediately });
            }
            catch (Exception e) { ModBehaviour.DevLog("[SkyIslandGnats] [WARNING] 嗡声停止失败：" + e.Message); }
        }

        private static Quaternion ViewRotation()
        {
            GameCamera camera = GameCamera.Instance;
            return camera != null && camera.renderCamera != null ? camera.renderCamera.transform.rotation : Quaternion.identity;
        }

        private static void ReadAim(CharacterMainControl player, out Vector3 muzzle, out Vector3 aim)
        {
            aim = player.GetCurrentAimPoint();
            muzzle = player.transform.position + Vector3.up * 1.2f;
            ItemAgent_Gun gun = player.GetGun();
            if (gun != null && gun.muzzle != null) muzzle = gun.muzzle.position;
        }

        #endregion

        private static SkyIslandGnatVec ToVec(Vector3 value) { return new SkyIslandGnatVec(value.x, value.y, value.z); }

        private static Vector3 ToVector(SkyIslandGnatVec value) { return new Vector3(value.X, value.Y, value.Z); }

        /// <summary>只报第一次（逐帧路径上不刷屏）。<paramref name="step"/> 是诊断用的 ASCII 步骤名（frame / sample / spawn / projectile），不进 UI。</summary>
        private void Fail(string step, Exception e)
        {
            if (failureReported) return;
            failureReported = true;
            ModBehaviour.CriticalLog("sky-island-gnats", "[SkyIsland] 云蚋失败 step=" + step + "：" + e.Message);
        }

        /// <summary>精灵表只读一次、跨出击复用；缺图或尺寸不对就整进程不刷云蚋（硬失败，不做看不见的蚊子）。</summary>
        private static bool EnsureArt()
        {
            if (frames != null) return true;
            if (artAttempted) return false;
            artAttempted = true;
            Texture2D texture = null;
            try
            {
                string path = Path.Combine(Path.Combine(ModBehaviour.GetModPath(), "Assets/ui/SkyIsland"), SheetFile);
                if (!File.Exists(path)) throw new FileNotFoundException("云蚋精灵表未部署", path);
                texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
                if (!texture.LoadImage(File.ReadAllBytes(path), true)) throw new InvalidOperationException("精灵表解码失败");
                if (texture.width != FramePixels * FrameCount || texture.height != FramePixels)
                    throw new InvalidOperationException("精灵表尺寸不对：" + texture.width + "x" + texture.height);
                texture.name = "SkyIslandGnatSheet";
                // 像素画必须点采样：双线性会把 1 px 描边糊成灰边，夜里就看不清轮廓。
                texture.filterMode = FilterMode.Point;
                texture.wrapMode = TextureWrapMode.Clamp;
                Shader shader = Shader.Find("Sprites/Default");
                if (shader == null) throw new InvalidOperationException("找不到 Sprites/Default 着色器");
                var sliced = new Sprite[FrameCount];
                for (int i = 0; i < FrameCount; i++)
                {
                    sliced[i] = Sprite.Create(texture, new Rect(i * FramePixels, 0f, FramePixels, FramePixels), new Vector2(0.5f, 0.5f),
                        PixelsPerUnit, 0u, SpriteMeshType.FullRect);
                    sliced[i].name = "SkyIslandGnat_" + i;
                }
                spriteMaterial = new Material(shader) { name = "SkyIslandGnatSprite" };
                trailMaterial = new Material(shader) { name = "SkyIslandGnatTrail" };
                sheet = texture;
                frames = sliced;
                return true;
            }
            catch (Exception e)
            {
                if (texture != null) UnityEngine.Object.Destroy(texture);
                ModBehaviour.CriticalLog("sky-island-gnat-art", "[SkyIsland] 云蚋精灵表不可用，本进程不刷云蚋：" + e.Message);
                return false;
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (Current == this) Current = null;
            try { RuntimeStatModifierTracker.RemoveAll(itchRecords, ModifierContext); }
            catch (Exception e) { Debug.LogWarning("[SkyIslandGnats] 摘除痒失败：" + e.Message); }
            if (buzzing) StopBuzz();
            for (int i = 0; i < gnats.Length; i++)
            {
                if (gnats[i] != null && gnats[i].Root != null) UnityEngine.Object.Destroy(gnats[i].Root);
                gnats[i] = null;
            }
            for (int i = 0; i < zappers.Length; i++)
            {
                if (zappers[i] != null && zappers[i].Root != null) UnityEngine.Object.Destroy(zappers[i].Root);
                zappers[i] = null;
            }
            for (int i = 0; i < splats.Length; i++)
            {
                if (splats[i] != null && splats[i].Root != null) UnityEngine.Object.Destroy(splats[i].Root);
                splats[i] = null;
            }
            if (fanArc != null) UnityEngine.Object.Destroy(fanArc.transform.parent.gameObject);
            fanArc = null;
            if (buzzEmitter != null) UnityEngine.Object.Destroy(buzzEmitter);
            buzzEmitter = null;
            alive = 0;
            carryingSpawn = false;
        }

        /// <summary>由 <c>SkyIslandRuntimeModule.OnDestroy</c> 调用：运行时 new 出来的贴图、精灵与材质必须显式销毁。</summary>
        internal static void ResetStaticCaches()
        {
            Current = null;
            if (frames != null)
                for (int i = 0; i < frames.Length; i++)
                    if (frames[i] != null) UnityEngine.Object.Destroy(frames[i]);
            frames = null;
            if (sheet != null) UnityEngine.Object.Destroy(sheet);
            sheet = null;
            if (spriteMaterial != null) UnityEngine.Object.Destroy(spriteMaterial);
            if (trailMaterial != null) UnityEngine.Object.Destroy(trailMaterial);
            spriteMaterial = trailMaterial = null;
            artAttempted = false;
        }
    }
}
