using System;
using System.Collections.Generic;
using System.Threading;
using BossRush.Utils;
using Duckov.MiniMaps;
using Duckov.Scenes;
using Duckov.Utilities;
using ItemStatsSystem;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    /// <summary>
    /// 叮当局内借测。装备与存档不变；唯一 owner 持有事件、模型、对话和奖励事务。
    /// 2026-10-24：只有接下过叮当的委托（DangerousPrototypeCommission）才会出现；每趟出击按概率决定试验台来不来、
    /// 什么时候来、放哪一件（每台只放一种），试验台随机落在玩家附近较远处并标在地图上。
    /// </summary>
    internal sealed class DangerousPrototypeRuntimeModule : BossRushRuntimeModuleBase
    {
        private ModBehaviour owner;
        private readonly DangerousPrototypeRules rules = new DangerousPrototypeRules();
        private readonly List<DangerousPrototypeInteractable> entries = new List<DangerousPrototypeInteractable>();
        private GameObject station;
        private CharacterMainControl borrower;
        private ItemAgent_Gun borrowedGun;
        private Item borrowedItem;
        private DangerousPrototypeVisuals visuals;
        private CancellationTokenSource dialogueCancellation;
        private bool subscribed;
        private bool leaseSubscribed;
        private bool dialogueOpen;
        private bool delivering;
        private bool initializedRaid;
        private LevelManager raidLevel;
        private int generation;
        private int entryAttempts;
        private int entrySceneHandle;
        private float entryTimer;
        private float recoilTime;
        private float visualTailTime;
        private float lastChargeCue = 1f;
        private readonly Vector3[] guidePoints = new Vector3[DangerousPrototypeRules.RicochetBounces + 2];
        private float recoilSpeed = 6f;
        // 本趟出击的试验台计划：是否出现、什么时候出现、放哪一件。
        internal const float BenchChance = 0.45f;
        private bool benchRolled, benchPlanned;
        private float benchDelay;
        private DangerousPrototypeKind offeredKind;
        private readonly List<FinaleShell> finales = new List<FinaleShell>();
        private readonly List<FinaleBomblet> bomblets = new List<FinaleBomblet>();
        private sealed class FinaleShell
        {
            internal Vector3 From, To; internal float Age; internal GameObject Shell; internal CharacterMainControl Shooter;
        }
        private sealed class FinaleBomblet
        {
            internal Vector3 Position; internal float Fuse; internal Color Color; internal CharacterMainControl Shooter;
        }
        private Vector3 recoilDirection;
        private int WallMask { get { return GameplayDataSettings.Layers.wallLayerMask | GameplayDataSettings.Layers.groundLayerMask; } }
        private static readonly float[] BenchBands = { 14f, 30f, 8f, 18f };
        public override string ModuleName { get { return "DangerousPrototypes"; } }

        public override void OnAwake(ModBehaviour host)
        {
            owner = host;
#if BOSSRUSH_DEV
            DevCurrent = this;
#endif
            if (subscribed) return;
            subscribed = true;
            SceneLoader.onStartedLoadingScene += OnLoading;
            LevelManager.OnMainCharacterDead += OnDead;
        }
        public override void OnUpdate(float deltaTime, float unscaledDeltaTime)
        {
            if (owner == null || SceneLoader.IsSceneLoading || BossRushUI.IsGamePaused()) return;
            if (LevelManager.Instance != raidLevel)
            {
                CleanupRaid(); rules.ResetRaid(); raidLevel = LevelManager.Instance; benchRolled = false; benchPlanned = false;
            }
            CharacterMainControl player;
            Scene scene;
            // 场景未启用时只有一次轻量判定；装备专属资源仅借出且实际持枪才建立。
            if (!FieldAdventureSupport.TryGetRaid(owner, out player, out scene))
            {
                // 官方同一趟出击的子场景装卸会暂时关闭门；保留本趟额度与借用。
                if (MultiSceneCore.Instance != null && MultiSceneCore.Instance.IsLoading) return;
                if (initializedRaid) CleanupRaid();
                return;
            }
            TickFinales(deltaTime);
            if (!benchRolled)
            {
                // 每趟出击只掷一次：没接委托不来；接了也只有一部分出击会遇上，来的时间和那一件都随机。
                benchRolled = true;
                benchPlanned = DangerousPrototypeCommission.IsUnlocked && UnityEngine.Random.Range(0f, 1f) < BenchChance;
                benchDelay = UnityEngine.Random.Range(25f, 110f);
                offeredKind = DangerousPrototypeRules.RandomKind(UnityEngine.Random.Range(0f, 0.9999f));
            }
            if (!benchPlanned && !rules.Active && !rules.Completed) return;
            if (!initializedRaid)
            {
                initializedRaid = true; entryTimer = benchDelay; entrySceneHandle = scene.handle;
            }
            else if (entrySceneHandle != scene.handle)
            {
                // 换同一趟内的区域时，试验台重新随机落在新区域，验收/领奖状态不动。
                ClearEntries(); entrySceneHandle = scene.handle; entryAttempts = 0; entryTimer = 6f;
            }
            if (benchPlanned && entryAttempts < 15 && (entryTimer -= deltaTime) <= 0f)
            {
                entryTimer = 4f; entryAttempts++;
                TryCreateEntries(player, scene);
            }
            if (!rules.Active)
            {
                if (visualTailTime > 0f && visuals != null)
                {
                    visuals.Tick(deltaTime, 0f);
                    visualTailTime -= deltaTime;
                    if (visualTailTime <= 0f) { visuals.Dispose(); visuals = null; }
                }
                return;
            }
            if (borrower == null || borrower != player || borrower.Health == null || borrower.Health.IsDead)
            {
                EndLease(true); return;
            }
            bool held = IsHeld();
            if (rules.Tick(deltaTime, held))
            {
                EndLease(false);
                FieldAdventureSupport.Notify("试用品已自动回收。叮当：数据不够，再来一次！", "Prototype recalled. Dingdang: Not enough data. Try again!");
                return;
            }
            if (!held)
            {
                if (visuals != null) { visuals.Dispose(); visuals = null; }
                if (recoilTime > 0f) { borrower.SetForceMoveVelocity(Vector3.zero); recoilTime = 0f; }
                return;
            }
            if (visuals != null) visuals.Tick(deltaTime, rules.Charge);
            // 蓄满那一刻给一声提示：压力灯在枪身上不一定看得清，玩家要知道「下一枪会放出来」。
            if (lastChargeCue < 1f && rules.Charge >= 1f) NewWeaponFx.PlaySound("astral_bean_full.wav");
            lastChargeCue = rules.Charge;
            UpdateAimGuide();
            TickRecoil(deltaTime);
        }
        /// <summary>
        /// 反弹炮的价值在弹墙后的伤害，凭空预判反射角太难：手持时沿官方射向实时画出前两次反弹的路线。
        /// 每帧至多三次射线，只在借用中、实际持枪时运行。
        /// </summary>
        private void UpdateAimGuide()
        {
            if (visuals == null) return;
            if (rules.Kind != DangerousPrototypeKind.RicochetSun || borrowedGun == null || borrowedGun.muzzle == null || borrower == null)
            { visuals.HideGuide(); return; }
            try
            {
                Vector3 from = borrowedGun.muzzle.position;
                Vector3 direction = ResolveShotDirection(borrowedGun, borrower);
                float remaining = 30f;
                int count = 0;
                guidePoints[count++] = from;
                for (int bounce = 0; bounce <= 2 && remaining > 0.1f; bounce++)
                {
                    RaycastHit wall;
                    bool blocked = Physics.Raycast(from, direction, out wall, remaining, WallMask, QueryTriggerInteraction.Ignore);
                    Vector3 end = blocked ? wall.point : from + direction * remaining;
                    guidePoints[count++] = end;
                    if (!blocked) break;
                    remaining -= wall.distance + 0.06f;
                    direction = Vector3.Reflect(direction, wall.normal).normalized;
                    from = wall.point + wall.normal * 0.06f;
                }
                visuals.ShowGuide(guidePoints, count);
            }
            catch (Exception e) { visuals.HideGuide(); ModBehaviour.DevLog("[DangerousPrototype] 瞄准线失败: " + e.Message); }
        }
        internal bool CanInteract
        {
            get
            {
                CharacterMainControl player; Scene scene;
                return !dialogueOpen && owner != null && FieldAdventureSupport.TryGetRaid(owner, out player, out scene);
            }
        }
        private void TryCreateEntries(CharacterMainControl player, Scene scene)
        {
            try
            {
                GoblinInteractable[] goblins = UnityEngine.Object.FindObjectsOfType<GoblinInteractable>();
                for (int i = 0; i < goblins.Length; i++)
                {
                    GoblinInteractable goblin = goblins[i];
                    if (goblin == null || !goblin.gameObject.activeInHierarchy || goblin.gameObject.scene.handle != scene.handle) continue;
                    var list = NPCInteractionGroupHelper.GetOrCreateGroupList(goblin, "[DangerousPrototype]");
                    var option = NPCInteractionGroupHelper.AddSubInteractable<DangerousPrototypeInteractable>(
                        goblin.transform, "DangerousPrototypeOption", list, x => { x.Runtime = this; x.GroupMember = true; });
                    if (option != null && !entries.Contains(option)) entries.Add(option);
                }
                // 试验台随机落在玩家附近较远处并标在地图上；同趟内一直留着，方便回来交还。
                if (station != null) return;
                Vector3 position;
                // 远档整轮落空就退到近档，避免 15 次尝试全落空、掷中的试验台整趟不出现。
                float[] bands = BenchBands;
#if BOSSRUSH_DEV
                if (devNearBench) bands = DevBenchBands;
#endif
                if (!FieldAdventureSupport.TryFindGroundBands(player.transform.position, bands, out position)) return;
                station = DangerousPrototypeVisuals.CreateStation(position, offeredKind);
                SceneManager.MoveGameObjectToScene(station, scene);
                var entry = NPCInteractionGroupHelper.GetOrCreateStandaloneInteractable<DangerousPrototypeInteractable>(
                    station.transform, "PrototypeConsole", new Vector3(0f, 0.35f, 0f), x =>
                    {
                        x.Runtime = this;
                        // 官方从脚边检索交互，台面上的小球无法从桌边可靠触及。
                        SphereCollider sensor = x.GetComponent<SphereCollider>();
                        if (sensor != null) { sensor.radius = 1.3f; sensor.isTrigger = true; }
                    });
                if (entry != null) entries.Add(entry);
                else
                {
                    UnityEngine.Object.Destroy(station); station = null;
                    return;
                }
                AddBenchMapPoint(station);
                FieldAdventureSupport.Notify("叮当的危险试验台出现在附近（地图已标记），这次放的是「" + DangerousPrototypeCatalog.Name(offeredKind) + "」。",
                    "Dingdang's prototype bench has turned up nearby (marked on the map). This time it holds the " + DangerousPrototypeCatalog.Name(offeredKind) + ".");
            }
            catch (Exception e) { ModBehaviour.DevLog("[DangerousPrototype] 入口创建失败: " + e.Message); }
        }

        internal async void Open(DangerousPrototypeInteractable entry)
        {
            if (!CanInteract || entry == null) return;
            dialogueOpen = true;
            int epoch = generation;
            var cancellation = new CancellationTokenSource();
            dialogueCancellation = cancellation;
            try
            {
                var actor = DialogueActorFactory.CreateBilingual(entry.gameObject,
                    "dangerous_prototype", "叮当的试验录音", "Dingdang's test recording");
                DialogueActorFactory.RefreshBilingualName("dangerous_prototype", "叮当的试验录音", "Dingdang's test recording");
                if (rules.Completed)
                {
                    await DialogueManager.ShowDialogueSequenceBilingual(actor, new[] { new[] {
                        "数据漂亮！能把鸭子往后推的枪，绝对不是设计失误。交还装置可领取一瓶冷淬液。",
                        "Beautiful data! A gun that pushes ducks backwards is absolutely intentional. Return it for one Cold Quench Fluid." } },
                        "BossRush_Prototype_Complete", cancellation.Token);
                    int claim = await DialogueManager.ShowMultipleChoiceBilingual(actor, new[] {
                        new[] { "交还装置并领奖", "Return and claim reward" }, new[] { "稍后再来", "Come back later" } },
                        0f, "BossRush_Prototype_Claim", cancellation.Token);
                    if (epoch == generation && claim == 0) TryClaim(entry.transform.position);
                    return;
                }
                if (rules.Active)
                {
                    string progress = rules.Progress + "/" + rules.Target;
                    await DialogueManager.ShowDialogueSequenceBilingual(actor, new[] { new[] {
                        "验收进度 " + progress + "，还剩 " + Mathf.CeilToInt(rules.Remaining) + " 秒。换回借用时的那把枪即可继续，切枪也会继续计时。",
                        "Test progress " + progress + ", " + Mathf.CeilToInt(rules.Remaining) + " seconds left. Hold the original gun to continue. Switching guns does not stop the timer." } },
                        "BossRush_Prototype_Status", cancellation.Token);
                    int cancel = await DialogueManager.ShowMultipleChoiceBilingual(actor, new[] {
                        new[] { "继续试验", "Keep testing" }, new[] { "交还试用品", "Return prototype" } },
                        0f, "BossRush_Prototype_Return", cancellation.Token);
                    if (epoch == generation && cancel == 1) EndLease(true);
                    return;
                }
                DangerousPrototypeKind kind = offeredKind;
                if (!rules.CanBorrow(kind))
                {
                    await DialogueManager.ShowDialogueSequenceBilingual(actor, new[] { new[] {
                        "这趟数据已经够了！下次出击，叮当再准备新的试验装置。", "All tests complete for this raid! Dingdang will have a fresh rig next time." } },
                        "BossRush_Prototype_Done", cancellation.Token);
                    return;
                }
                string name = DangerousPrototypeCatalog.Name(kind);
                await DialogueManager.ShowDialogueSequenceBilingual(actor, new[] { new[] {
                    "这次只带了一件：「" + name + "」。" + DangerousPrototypeCatalog.Brief(kind) + "达标后回来领一瓶冷淬液。原枪照常消耗弹药。",
                    "Only one rig this time: the " + name + ". " + DangerousPrototypeCatalog.Brief(kind) + " Pass and return for one Cold Quench Fluid. Your gun still consumes ammo." } },
                    "BossRush_Prototype_Brief", cancellation.Token);
                int accept = await DialogueManager.ShowMultipleChoiceBilingual(actor, new[] {
                    new[] { "装上，开工！", "Install it. Let's go!" }, new[] { "再想想", "Maybe later" } },
                    0f, "BossRush_Prototype_Borrow", cancellation.Token);
                if (epoch == generation && accept == 0) BeginLease(kind);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception e) { ModBehaviour.DevLog("[DangerousPrototype] 对话失败: " + e.Message); }
            finally
            {
                if (ReferenceEquals(dialogueCancellation, cancellation))
                {
                    dialogueCancellation = null; dialogueOpen = false;
                }
                cancellation.Dispose();
            }
        }

        private void BeginLease(DangerousPrototypeKind kind)
        {
            CharacterMainControl player; Scene scene;
            if (!FieldAdventureSupport.TryGetRaid(owner, out player, out scene)) return;
            var gun = player.CurrentHoldItemAgent as ItemAgent_Gun;
            if (gun == null || gun.muzzle == null)
            {
                FieldAdventureSupport.Notify("先把要试验的枪拿在手里。", "Hold the gun you want to test first."); return;
            }
            if (!rules.Borrow(kind)) return;
            borrower = player; borrowedGun = gun; borrowedItem = gun.Item;
            if (!leaseSubscribed)
            {
                leaseSubscribed = true;
                CharacterMainControl.OnMainCharacterChangeHoldItemAgentEvent += OnHoldChanged;
                ItemAgent_Gun.OnMainCharacterShootEvent += OnShot;
            }
            RefreshVisuals();
            FieldAdventureSupport.Notify("试验开始！压力灯亮满后，正常开枪即可释放试用品。", "Test started! When the pressure lights fill, fire normally to release the prototype.");
        }
        private bool IsHeld()
        {
            return rules.Active && borrower != null && borrowedGun != null
                && borrower.CurrentHoldItemAgent == borrowedGun && borrowedGun.Holder == borrower;
        }
        private void OnHoldChanged(CharacterMainControl player, DuckovItemAgent agent)
        {
            if (player != borrower) return;
            if (recoilTime > 0f) player.SetForceMoveVelocity(Vector3.zero);
            recoilTime = 0f;
            ItemAgent_Gun heldGun = agent as ItemAgent_Gun;
            borrowedGun = heldGun != null && heldGun.Item == borrowedItem ? heldGun : null;
            RefreshVisuals();
        }
        private void RefreshVisuals()
        {
            if (visuals != null) { visuals.Dispose(); visuals = null; }
            visualTailTime = 0f;
            if (IsHeld()) visuals = new DangerousPrototypeVisuals(borrowedGun.muzzle, borrower, rules.Kind);
        }
        private void OnShot(ItemAgent_Gun gun)
        {
            try
            {
                CharacterMainControl player; Scene scene;
                if (gun != borrowedGun || !IsHeld() || BossRushUI.IsGamePaused()
                    || !FieldAdventureSupport.TryGetRaid(owner, out player, out scene) || player != borrower || !rules.TryFire()) return;
                Vector3 from = gun.muzzle.position;
                Vector3 direction = ResolveShotDirection(gun, borrower);
                // 枪口可能探过薄墙：先从角色体内检查到枪口，避免辅助攻击隔墙发射。
                RaycastHit obstruction;
                Vector3 chest = borrower.transform.position + Vector3.up * 0.7f;
                if (Physics.Linecast(chest, from, out obstruction, WallMask, QueryTriggerInteraction.Ignore))
                    // 沿已经检查的线段退回角色一侧；射向与墙面平行或反向时也不越墙。
                    from = Vector3.MoveTowards(obstruction.point, chest, 0.12f);
                int before = rules.Progress;
                DangerousPrototypeKind kind = rules.Kind;
                if (visuals != null) visuals.Fire();
                if (kind == DangerousPrototypeKind.JetCannon || kind == DangerousPrototypeKind.RicochetSun) TraceBlast(from, direction, kind);
                else FireSpecial(kind, from, direction);
                if ((kind == DangerousPrototypeKind.JetCannon || kind == DangerousPrototypeKind.QuackRail) && rules.Active)
                {
                    recoilDirection = -Vector3.ProjectOnPlane(direction, Vector3.up).normalized;
                    recoilTime = 0.22f;
                    recoilSpeed = kind == DangerousPrototypeKind.QuackRail ? 11f : 9f;
                }
                if (rules.Completed)
                {
                    EndLease(false);
                    FieldAdventureSupport.Notify("试验达标！找叮当或入场附近的试验台，交还后领取冷淬液。", "Test passed! Return to Dingdang or the bench near your entry point for Cold Quench Fluid.");
                }
                else if (rules.Progress != before)
                    FieldAdventureSupport.Notify("试验命中 " + rules.Progress + "/" + rules.Target, "Test hits " + rules.Progress + "/" + rules.Target);
            }
            catch (Exception e) { ModBehaviour.DevLog("[DangerousPrototype] 射击失败: " + e.Message); }
        }
        private static Vector3 ResolveShotDirection(ItemAgent_Gun gun, CharacterMainControl player)
        {
            // 与官方 ItemAgent_Gun.TransToFire 的近距与移动能力门一致，脚边瞄点不把附加攻击翻向身后。
            Vector3 direction = gun.muzzle.forward;
            if (player.CharacterMoveability > 0.5f)
            {
                Vector3 aim = player.GetCurrentAimPoint();
                Vector3 position = player.transform.position;
                Vector3 muzzle = gun.muzzle.position;
                aim.y = 0f; position.y = 0f; muzzle.y = 0f;
                if (Vector3.Distance(position, aim) > Vector3.Distance(position, muzzle) + 0.1f)
                    direction = (player.GetCurrentAimPoint() - gun.muzzle.position).normalized;
            }
            return direction;
        }
        private void TraceBlast(Vector3 from, Vector3 direction, DangerousPrototypeKind kind)
        {
            float remaining = kind == DangerousPrototypeKind.JetCannon ? DangerousPrototypeRules.JetRange : DangerousPrototypeRules.RicochetRange;
            int maxBounce = kind == DangerousPrototypeKind.JetCannon ? 0 : DangerousPrototypeRules.RicochetBounces;
            var hitThisShot = new HashSet<UnityEngine.Object>();
            int epoch = generation;
            for (int bounce = 0; bounce <= maxBounce && remaining > 0.1f; bounce++)
            {
                RaycastHit wall;
                bool blocked = Physics.Raycast(from, direction, out wall, remaining, WallMask, QueryTriggerInteraction.Ignore);
                float distance = blocked ? wall.distance : remaining;
                Vector3 end = from + direction * distance;
                if (visuals != null) visuals.Beam(from, end, kind == DangerousPrototypeKind.JetCannon, bounce);
                float radius = kind == DangerousPrototypeKind.JetCannon ? DangerousPrototypeRules.JetRadius : 0.3f;
                RaycastHit[] hits = Physics.SphereCastAll(from, radius, direction, distance,
                    GameplayDataSettings.Layers.damageReceiverLayerMask, QueryTriggerInteraction.Collide);
                Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
                for (int i = 0; i < hits.Length; i++)
                {
                    if (epoch != generation || borrower == null) return;
                    DamageReceiver receiver = hits[i].collider != null ? hits[i].collider.GetComponent<DamageReceiver>() : null;
                    if (receiver == null || !Team.IsEnemy(borrower.Team, receiver.Team)) continue;
                    Health health = receiver.useSimpleHealth ? null : receiver.health;
                    HealthSimpleBase simple = receiver.useSimpleHealth ? receiver.simpleHealth : null;
                    UnityEngine.Object target = health != null ? (UnityEngine.Object)health : simple;
                    if (target == null || hitThisShot.Contains(target)) continue;
                    if (health != null && (health.IsDead || health.IsMainCharacterHealth || PetNestCompanionAgent.IsCompanionHealth(health))) continue;
                    if (simple != null && simple.HealthValue <= 0f) continue;
                    Vector3 point = hits[i].collider.bounds.center;
                    if (Physics.Linecast(from, point, WallMask, QueryTriggerInteraction.Ignore)) continue;
                    hitThisShot.Add(target);
                    var damage = new DamageInfo(borrower);
                    damage.damageValue = DangerousPrototypeRules.Damage(kind, bounce);
                    damage.damageType = DamageTypes.normal;
                    damage.isFromBuffOrEffect = true;
                    damage.isExplosion = kind == DangerousPrototypeKind.JetCannon;
                    damage.damagePoint = point; damage.damageNormal = -direction;
                    float oldHealth = health != null ? health.CurrentHealth : simple.HealthValue;
                    try { receiver.Hurt(damage); }
                    catch (Exception e) { ModBehaviour.DevLog("[DangerousPrototype] 单目标伤害失败: " + e.Message); }
                    if (epoch != generation) return;
                    bool tookDamage = health != null ? (health.IsDead || health.CurrentHealth < oldHealth)
                        : (simple == null || simple.HealthValue < oldHealth);
                    if (target == null || tookDamage) rules.ObserveHit(bounce > 0);
                    if (tookDamage && visuals != null) visuals.Impact(point, -direction, bounce);
                }
                if (!blocked || bounce == maxBounce) break;
                if (visuals != null) visuals.Impact(wall.point, wall.normal, bounce);
                remaining -= distance + 0.06f;
                direction = Vector3.Reflect(direction, wall.normal).normalized;
                from = wall.point + wall.normal * 0.06f;
            }
        }
        private void TickRecoil(float deltaTime)
        {
            if (recoilTime <= 0f || borrower == null) return;
            float step = Mathf.Min(deltaTime, recoilTime);
            recoilTime -= step;
            Vector3 origin = borrower.transform.position;
            float speed = recoilSpeed * Mathf.Clamp01(recoilTime / 0.22f);
            // 留足下一帧以及停止惯性的距离；每一段都要求落脚地面连续，拒绝台阶/悬崖。
            float distance = speed * Mathf.Max(step, 0.06f) + 0.45f;
            RaycastHit hit;
            bool safe = !borrower.Swimming
                && !Physics.SphereCast(origin + Vector3.up * 0.7f, 0.4f, recoilDirection, out hit,
                    distance, WallMask, QueryTriggerInteraction.Ignore);
            for (int i = 1; safe && i <= 3; i++)
            {
                Vector3 sample = origin + recoilDirection * (distance * i / 3f);
                safe = Physics.Raycast(sample + Vector3.up * 0.4f, Vector3.down, out hit, 0.7f,
                    GameplayDataSettings.Layers.groundLayerMask, QueryTriggerInteraction.Ignore)
                    && hit.normal.y > 0.72f && Mathf.Abs(hit.point.y - origin.y) < 0.25f;
            }
            borrower.SetForceMoveVelocity(safe ? recoilDirection * speed : Vector3.zero);
            if (!safe || recoilTime <= 0f) { borrower.SetForceMoveVelocity(Vector3.zero); recoilTime = 0f; }
        }
        /// <summary>轨道炮 / 雷霆 / 烟花的出手；倒鸭一号与折射太阳仍走 TraceBlast。</summary>
        private void FireSpecial(DangerousPrototypeKind kind, Vector3 from, Vector3 direction)
        {
            if (kind == DangerousPrototypeKind.QuackRail) FireRail(from, direction);
            else if (kind == DangerousPrototypeKind.Thunderbill) FireThunder(from, direction);
            else if (kind == DangerousPrototypeKind.GrandFinale) LaunchFinale(from, direction);
        }

        /// <summary>贯穿一整条线上的所有敌人，遇墙才停。</summary>
        private void FireRail(Vector3 from, Vector3 direction)
        {
            RaycastHit wall;
            bool blocked = Physics.Raycast(from, direction, out wall, DangerousPrototypeRules.RailRange, WallMask, QueryTriggerInteraction.Ignore);
            float distance = blocked ? wall.distance : DangerousPrototypeRules.RailRange;
            Vector3 end = from + direction * distance;
            if (visuals != null) visuals.RailBeam(from, end);
            if (blocked && visuals != null) visuals.Impact(wall.point, wall.normal, 1);
            RaycastHit[] hits = Physics.SphereCastAll(from, DangerousPrototypeRules.RailRadius, direction, distance,
                GameplayDataSettings.Layers.damageReceiverLayerMask, QueryTriggerInteraction.Collide);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            var hit = new HashSet<UnityEngine.Object>();
            int epoch = generation;
            for (int i = 0; i < hits.Length; i++)
            {
                if (epoch != generation) return;
                TryHurt(hits[i].collider, from, DangerousPrototypeRules.Damage(DangerousPrototypeKind.QuackRail, 0), false, borrower, hit, epoch);
            }
        }

        /// <summary>沿射向找第一个敌人，再在 9 米内逐个跳向最近的下一个，最多 7 跳。</summary>
        private void FireThunder(Vector3 from, Vector3 direction)
        {
            RaycastHit wall;
            float reach = Physics.Raycast(from, direction, out wall, DangerousPrototypeRules.ThunderReach, WallMask, QueryTriggerInteraction.Ignore)
                ? wall.distance : DangerousPrototypeRules.ThunderReach;
            RaycastHit[] hits = Physics.SphereCastAll(from, 0.9f, direction, reach,
                GameplayDataSettings.Layers.damageReceiverLayerMask, QueryTriggerInteraction.Collide);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            var hit = new HashSet<UnityEngine.Object>();
            var points = new List<Vector3> { from };
            int epoch = generation;
            Collider current = null;
            for (int i = 0; i < hits.Length && current == null; i++)
                if (TryHurt(hits[i].collider, from, DangerousPrototypeRules.Damage(DangerousPrototypeKind.Thunderbill, 0), false, borrower, hit, epoch))
                    current = hits[i].collider;
            if (current == null)
            {
                points.Add(from + direction * Mathf.Min(reach, 8f));
                if (visuals != null) visuals.Lightning(points.ToArray(), false);
                return;
            }
            points.Add(current.bounds.center);
            for (int link = 1; link < DangerousPrototypeRules.ThunderLinks; link++)
            {
                if (epoch != generation) return;
                Vector3 center = current.bounds.center;
                Collider[] around = Physics.OverlapSphere(center, DangerousPrototypeRules.ThunderHopRange,
                    GameplayDataSettings.Layers.damageReceiverLayerMask, QueryTriggerInteraction.Collide);
                Array.Sort(around, (a, b) => (a.bounds.center - center).sqrMagnitude.CompareTo((b.bounds.center - center).sqrMagnitude));
                Collider next = null;
                for (int i = 0; i < around.Length && next == null; i++)
                    if (TryHurt(around[i], center, DangerousPrototypeRules.Damage(DangerousPrototypeKind.Thunderbill, link), false, borrower, hit, epoch))
                        next = around[i];
                if (next == null) break;
                current = next;
                points.Add(current.bounds.center);
            }
            if (visuals != null) visuals.Lightning(points.ToArray(), true);
        }

        /// <summary>朝准星抛一枚大烟花；落地、分裂与子弹爆炸在 TickFinales 里按时间推进，离手也照样炸完。</summary>
        private void LaunchFinale(Vector3 from, Vector3 direction)
        {
            Vector3 aim = borrower != null ? borrower.GetCurrentAimPoint() : from + direction * 12f;
            Vector3 flat = aim - from; flat.y = 0f;
            if (flat.sqrMagnitude > DangerousPrototypeRules.FinaleMaxRange * DangerousPrototypeRules.FinaleMaxRange)
                aim = from + flat.normalized * DangerousPrototypeRules.FinaleMaxRange;
            RaycastHit ground;
            if (Physics.Raycast(aim + Vector3.up * 4f, Vector3.down, out ground, 10f, GameplayDataSettings.Layers.groundLayerMask, QueryTriggerInteraction.Ignore))
                aim = ground.point;
            finales.Add(new FinaleShell { From = from, To = aim, Shooter = borrower, Shell = DangerousPrototypeVisuals.CreateFireworkShell(from) });
        }

        private void TickFinales(float deltaTime)
        {
            for (int i = finales.Count - 1; i >= 0; i--)
            {
                FinaleShell shell = finales[i];
                shell.Age += deltaTime;
                float t = Mathf.Clamp01(shell.Age / DangerousPrototypeRules.FinaleFlightSeconds);
                Vector3 position = Vector3.Lerp(shell.From, shell.To, t) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * 6f);
                if (shell.Shell != null) shell.Shell.transform.position = position;
                if (t < 1f) continue;
                finales.RemoveAt(i);
                if (shell.Shell != null) DangerousPrototypeVisuals.ReleaseFireworkShell(shell.Shell);
                DangerousPrototypeVisuals.FireworkBurst(shell.To, new Color(1f, 0.85f, 0.4f), DangerousPrototypeRules.FinaleMainRadius, true);
                Explode(shell.To, DangerousPrototypeRules.FinaleMainRadius, DangerousPrototypeRules.Damage(DangerousPrototypeKind.GrandFinale, 0), shell.Shooter);
                for (int b = 0; b < DangerousPrototypeRules.FinaleBomblets; b++)
                {
                    float angle = b * 2.39996323f;
                    float radius = 2.2f + 4.6f * ((b * 7 % DangerousPrototypeRules.FinaleBomblets) / (float)DangerousPrototypeRules.FinaleBomblets);
                    bomblets.Add(new FinaleBomblet
                    {
                        Position = shell.To + new Vector3(Mathf.Cos(angle) * radius, 0.2f, Mathf.Sin(angle) * radius),
                        Fuse = 0.2f + 0.05f * b, Color = FinalePalette[b % FinalePalette.Length], Shooter = shell.Shooter
                    });
                }
            }
            for (int i = bomblets.Count - 1; i >= 0; i--)
            {
                FinaleBomblet bomblet = bomblets[i];
                bomblet.Fuse -= deltaTime;
                if (bomblet.Fuse > 0f) continue;
                bomblets.RemoveAt(i);
                DangerousPrototypeVisuals.FireworkBurst(bomblet.Position, bomblet.Color, DangerousPrototypeRules.FinaleBombletRadius, false);
                Explode(bomblet.Position, DangerousPrototypeRules.FinaleBombletRadius, DangerousPrototypeRules.Damage(DangerousPrototypeKind.GrandFinale, 1), bomblet.Shooter);
            }
        }

        private static readonly Color[] FinalePalette =
        {
            new Color(1f, 0.82f, 0.3f), new Color(0.35f, 0.9f, 1f), new Color(1f, 0.45f, 0.75f), new Color(0.5f, 1f, 0.55f)
        };

        private void Explode(Vector3 center, float radius, float damage, CharacterMainControl shooter)
        {
            if (shooter == null || shooter.Health == null || shooter.Health.IsDead) return;
            Collider[] around = Physics.OverlapSphere(center, radius, GameplayDataSettings.Layers.damageReceiverLayerMask, QueryTriggerInteraction.Collide);
            var hit = new HashSet<UnityEngine.Object>();
            int epoch = generation;
            for (int i = 0; i < around.Length; i++)
            {
                if (epoch != generation) return;
                TryHurt(around[i], center + Vector3.up * 0.5f, damage, true, shooter, hit, epoch);
            }
        }

        /// <summary>
        /// 新三种试用品共用的单目标伤害：阵营、随从、去重与墙体遮挡同 TraceBlast 口径；实际扣血才推进验收。
        /// 返回是否结算了这个目标（用于雷霆选下一跳）。
        /// </summary>
        private bool TryHurt(Collider collider, Vector3 from, float amount, bool explosion, CharacterMainControl source,
            HashSet<UnityEngine.Object> hit, int epoch)
        {
            if (collider == null || source == null) return false;
            DamageReceiver receiver = collider.GetComponent<DamageReceiver>();
            if (receiver == null || !Team.IsEnemy(source.Team, receiver.Team)) return false;
            Health health = receiver.useSimpleHealth ? null : receiver.health;
            HealthSimpleBase simple = receiver.useSimpleHealth ? receiver.simpleHealth : null;
            UnityEngine.Object target = health != null ? (UnityEngine.Object)health : simple;
            if (target == null || hit.Contains(target)) return false;
            if (health != null && (health.IsDead || health.IsMainCharacterHealth || PetNestCompanionAgent.IsCompanionHealth(health))) return false;
            if (simple != null && simple.HealthValue <= 0f) return false;
            Vector3 point = collider.bounds.center;
            if (Physics.Linecast(from, point, WallMask, QueryTriggerInteraction.Ignore)) return false;
            hit.Add(target);
            var damage = new DamageInfo(source);
            damage.damageValue = amount;
            damage.damageType = DamageTypes.normal;
            damage.isFromBuffOrEffect = true;
            damage.isExplosion = explosion;
            damage.damagePoint = point; damage.damageNormal = (from - point).normalized;
            float oldHealth = health != null ? health.CurrentHealth : simple.HealthValue;
            try { receiver.Hurt(damage); }
            catch (Exception e) { ModBehaviour.DevLog("[DangerousPrototype] 单目标伤害失败: " + e.Message); }
            if (epoch != generation) return true;
            bool tookDamage = health != null ? (health.IsDead || health.CurrentHealth < oldHealth)
                : (simple == null || simple.HealthValue < oldHealth);
            if (target == null || tookDamage) rules.ObserveHit(true);
            if (tookDamage && visuals != null) visuals.Impact(point, (from - point).normalized, 1);
            return true;
        }

        private static void AddBenchMapPoint(GameObject bench)
        {
            try
            {
                const string key = "BossRush_PrototypeBench_Map";
                LocalizationHelper.InjectLocalization(key, L10n.T("叮当的试验台", "Dingdang's prototype bench"));
                bench.SetActive(false);
                var marker = bench.AddComponent<SimplePointOfInterest>();
                marker.Color = BossRushUIColors.WarningText;
                marker.ScaleFactor = 1.25f;
                marker.IsArea = false;
                bench.SetActive(true);
                marker.Setup(null, key);
            }
            catch (Exception e) { ModBehaviour.DevLog("[DangerousPrototype] 试验台地图标记失败: " + e.Message); }
        }

        private void TryClaim(Vector3 position)
        {
            if (!rules.Completed || delivering) return;
            CharacterMainControl player; Scene scene;
            if (!FieldAdventureSupport.TryGetRaid(owner, out player, out scene)) return;
            delivering = true;
            Item reward = null;
            bool delivered = false;
            try
            {
                // 官方未注册类型会造 FallbackItem；不能将它当冷淬液交付并消耗本趟额度。
                if (ItemAssetsCollection.GetPrefab(ColdQuenchFluidConfig.TYPE_ID) == null)
                    throw new InvalidOperationException("Cold Quench Fluid prefab is unavailable");
                reward = ItemAssetsCollection.InstantiateSync(ColdQuenchFluidConfig.TYPE_ID);
                if (reward == null || reward.TypeID != ColdQuenchFluidConfig.TYPE_ID)
                    throw new InvalidOperationException("Cold Quench Fluid creation failed");
                Inventory inventory = player.CharacterItem != null ? player.CharacterItem.Inventory : null;
                delivered = SkyIslandInventoryTransaction.TryDeliver(reward, inventory, remaining =>
                {
                    // 共用事务已完成合堆；剩余原件先占空槽，再按真实归属决定是否落地。
                    if (inventory != null)
                    {
                        try { inventory.AddItem(remaining); }
                        catch (Exception e) { ModBehaviour.DevLog("[DangerousPrototype] 奖励入包通知失败: " + e.Message); }
                        if (SkyIslandInventoryTransaction.HasOwner(remaining) || inventory.Content.Contains(remaining)) return true;
                    }
                    try { remaining.Drop(position + Vector3.up * 0.2f, true, Vector3.forward, 0f); }
                    catch (Exception e) { ModBehaviour.DevLog("[DangerousPrototype] 奖励落地通知失败: " + e.Message); }
                    return remaining != null && remaining.ActiveAgent != null;
                });
                if (delivered)
                {
                    rules.Claim();
                    FieldAdventureSupport.Notify("叮当验收通过：冷淬液已交付，背包放不下时会放在试验台旁。", "Dingdang approves: Cold Quench Fluid delivered, or placed beside the bench if your bag is full.");
                }
            }
            catch (Exception e) { ModBehaviour.DevLog("[DangerousPrototype] 奖励交付失败: " + e.Message); }
            finally
            {
                if (!delivered) SkyIslandInventoryTransaction.DestroyUnowned(reward);
                delivering = false;
            }
            if (!delivered) FieldAdventureSupport.Notify("奖励暂时没能交付，请再次交还领取。", "Reward delivery failed. Please interact again to claim it.");
        }
        private void EndLease(bool cancel)
        {
            if (recoilTime > 0f && borrower != null) borrower.SetForceMoveVelocity(Vector3.zero);
            recoilTime = 0f;
            if (leaseSubscribed)
            {
                leaseSubscribed = false;
                CharacterMainControl.OnMainCharacterChangeHoldItemAgentEvent -= OnHoldChanged;
                ItemAgent_Gun.OnMainCharacterShootEvent -= OnShot;
            }
            if (!cancel && rules.Completed && visuals != null)
            {
                visuals.HideRig(); visualTailTime = 0.65f;
            }
            else
            {
                if (visuals != null) { visuals.Dispose(); visuals = null; }
                visualTailTime = 0f;
            }
            borrower = null; borrowedGun = null; borrowedItem = null;
            if (cancel) rules.Cancel();
        }
        private void ClearFinales()
        {
            foreach (FinaleShell shell in finales) if (shell.Shell != null) UnityEngine.Object.Destroy(shell.Shell);
            finales.Clear();
            bomblets.Clear();
        }
        private void OnLoading(SceneLoadingContext context) { CleanupRaid(); }
        private void OnDead(DamageInfo info) { CleanupRaid(); }
        private void CleanupRaid()
        {
            generation++;
            if (dialogueCancellation != null) { dialogueCancellation.Cancel(); dialogueCancellation = null; }
            dialogueOpen = false;
            EndLease(true);
            ClearEntries();
            ClearFinales();
            initializedRaid = false; entryAttempts = 0; entryTimer = 0f;
#if BOSSRUSH_DEV
            devNearBench = false;
#endif
        }
        private void ClearEntries()
        {
            if (dialogueCancellation != null)
            {
                generation++;
                dialogueCancellation.Cancel(); dialogueCancellation = null; dialogueOpen = false;
            }
            foreach (var entry in entries)
            {
                if (entry == null) continue;
                entry.Runtime = null;
                // 清掉自己的组员引用，避免同一宿主被重建模块后残留失效选项。
                var goblin = entry.GetComponentInParent<GoblinInteractable>();
                if (goblin != null)
                {
                    var list = NPCInteractionGroupHelper.GetOrCreateGroupList(goblin, "[DangerousPrototype]");
                    if (list != null) list.Remove(entry);
                }
                UnityEngine.Object.Destroy(entry.gameObject);
            }
            entries.Clear();
            if (station != null) { station.SetActive(false); UnityEngine.Object.Destroy(station); station = null; }
        }
        public override void OnDestroy()
        {
            if (subscribed)
            {
                subscribed = false;
                SceneLoader.onStartedLoadingScene -= OnLoading;
                LevelManager.OnMainCharacterDead -= OnDead;
            }
            CleanupRaid(); owner = null;
#if BOSSRUSH_DEV
            if (DevCurrent == this) DevCurrent = null;
#endif
        }
#if BOSSRUSH_DEV
        internal static DangerousPrototypeRuntimeModule DevCurrent { get; private set; }
        private bool devNearBench;
        private static readonly float[] DevBenchBands = { 5f, 9f, 3f, 12f };

        /// <summary>F3 录制模式：本趟改放指定试用品，试验台下一帧落在玩家身边 5–9 米（不看委托与概率，额度重置）。</summary>
        internal bool DevForceBench(DangerousPrototypeKind kind, out string reason)
        {
            reason = string.Empty;
            CharacterMainControl player;
            Scene scene;
            if (!FieldAdventureSupport.TryGetRaid(owner, out player, out scene)) { reason = "not_in_raid"; return false; }
            EndLease(true);
            ClearEntries();
            ClearFinales();
            rules.ResetRaid();
            raidLevel = LevelManager.Instance;
            benchRolled = true;
            benchPlanned = true;
            offeredKind = kind;
            initializedRaid = true;
            entrySceneHandle = scene.handle;
            entryAttempts = 0;
            entryTimer = 0f;
            devNearBench = true;
            return true;
        }
#endif
    }
}
