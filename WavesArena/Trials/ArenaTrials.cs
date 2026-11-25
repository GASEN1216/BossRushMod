// ============================================================================
// ArenaTrials.cs - 出征令战规在局内的服务对象：天色 / 天象、厄运与常驻诅咒、天灾、首领词缀、焚城
// ============================================================================
// 归属（AGENTS §4.15）：唯一 owner 是 WavesArenaRuntimeModule——BeginExpeditionRun 时 Begin、EndExpeditionRun 时 End
// （切图、模块销毁都走那里）；开波 / 收波 / 首领登记由模块经 Notify* 转进来，本类不读写任何波次状态，
// 分裂 / 双生压着收波也一样：模块在 HandleBossDeath 推波之前问 Affixes.HoldWaveFor「还有没有身体要打」，
// 压住的那一波由本类在 Update 最后一句交还模块（ExpeditionReleaseHeldWave），推波仍是模块自己做（WavesArenaRuntimeModule_Expedition）。
//
// 生命周期（AGENTS §4.6 / §4.12）：
//   - 本对象只在出征令开的竞技场局里存在；不是出征令的局根本不建它，每帧成本为零；
//   - 竞技场一结束（通关、玩家阵亡、Mode D 借用）Update 当帧自毁，摘掉所有属性修饰、收掉天灾与表现；
//   - 全局事件（Health.OnHurt / OnDead）由私有布尔守着只订一次，Shutdown 里退订；
//   - 挂在角色 Item 上的修饰全部记在本对象的记录表里，Shutdown 逐条摘（角色 Item 跨场景存活，不摘会留到下一局）。
// 各部分（Sky / Curses / Hazards / Affixes / FireRing）是普通对象，由本类按固定顺序驱动。
// ============================================================================

using System;
using System.Collections.Generic;
using ItemStatsSystem.Stats;
using UnityEngine;

namespace BossRush
{
    /// <summary>玩家身上各条战规想要的属性倍数（乘法）与增量（加法）。</summary>
    internal struct ArenaTrialPlayerStats
    {
        public float ViewDistance, ViewAngle, SenseRange;
        public float Move;
        public float ReloadGain;
        public float Stamina;
        public float Scatter;
        public float MaxHealth;

        internal static ArenaTrialPlayerStats Identity
        {
            get
            {
                ArenaTrialPlayerStats s = new ArenaTrialPlayerStats();
                s.ViewDistance = s.ViewAngle = s.SenseRange = 1f;
                s.Move = s.Stamina = s.Scatter = s.MaxHealth = 1f;
                s.ReloadGain = 0f;
                return s;
            }
        }
    }

    /// <summary>敌人身上各条战规想要的属性倍数。Taken 是受到的伤害倍数（护驾减伤），落在官方各元素系数上。</summary>
    internal struct ArenaTrialFoeStats
    {
        public float Move;
        public float Damage;
        public float Taken;

        internal static ArenaTrialFoeStats Identity
        {
            get
            {
                ArenaTrialFoeStats s = new ArenaTrialFoeStats();
                s.Move = s.Damage = s.Taken = 1f;
                return s;
            }
        }
    }

    /// <summary>战规的一个部分。所有回调都在主线程、由 <see cref="ArenaTrials"/> 驱动。</summary>
    internal abstract class ArenaTrialPart
    {
        protected readonly ArenaTrials Trials;

        protected ArenaTrialPart(ArenaTrials trials) { Trials = trials; }

        internal virtual void OnBegin() { }
        internal virtual void OnWaveStart(int wave, bool finale) { }
        internal virtual void OnWaveEnd() { }
        internal virtual void OnFoeJoined(CharacterMainControl foe, bool waveBoss) { }
        internal virtual void OnAnyHurt(Health health, DamageInfo info) { }
        internal virtual void OnAnyDeath(Health health, DamageInfo info) { }
        internal virtual void Tick(float deltaTime) { }
        internal virtual void ContributePlayer(ref ArenaTrialPlayerStats stats) { }
        internal virtual void ContributeFoe(CharacterMainControl foe, ref ArenaTrialFoeStats stats) { }
        internal virtual void Shutdown() { }
    }

    internal sealed class ArenaTrials : MonoBehaviour
    {
        private const string RootName = "BossRush_ArenaTrials";
        private const string LogPrefix = "[BossRush] [出征令战规] ";

        internal static ArenaTrials Active { get; private set; }

        private WavesArenaRuntimeModule arena;
        private Func<bool> isLive;
        private ArenaTrialPart[] parts = new ArenaTrialPart[0];

        internal ArenaExpeditionRun Run { get; private set; }
        internal ArenaExpeditionTier Tier { get; private set; }
        internal ArenaTrialProfile Profile { get; private set; }
        internal ArenaTrialsSky Sky { get; private set; }
        internal ArenaTrialsCurses Curses { get; private set; }
        internal ArenaTrialsHazards Hazards { get; private set; }
        internal ArenaTrialsAffixes Affixes { get; private set; }
        internal ArenaTrialsFireRing FireRing { get; private set; }

        /// <summary>当前第几波（0 起）。</summary>
        internal int Wave { get; private set; }
        /// <summary>本波是否在打（开波到收波之间）。</summary>
        internal bool WaveLive { get; private set; }
        internal bool FinaleWave { get; private set; }
        /// <summary>本波开打了多少秒（只在 WaveLive 时走）。</summary>
        internal float WaveClock { get; private set; }

        /// <summary>本局的敌方身体：波次首领与战规叫出来的身体（分身、护卫、召唤）。</summary>
        private readonly List<CharacterMainControl> foes = new List<CharacterMainControl>(12);
        private readonly HashSet<CharacterMainControl> waveBosses = new HashSet<CharacterMainControl>();
        private readonly List<BossRushStatModifierRecord> playerRecords = new List<BossRushStatModifierRecord>(12);
        private readonly Dictionary<CharacterMainControl, List<BossRushStatModifierRecord>> foeRecords =
            new Dictionary<CharacterMainControl, List<BossRushStatModifierRecord>>();
        private bool playerStatsDirty = true;
        private bool foeStatsDirty;
        private CharacterMainControl statPlayer;
        private float pruneTimer;
        private bool hurtSubscribed;
        private bool deadSubscribed;
        private bool shutDown;

        // ------------------------------------------------------------------
        // 生命周期（owner：WavesArenaRuntimeModule）
        // ------------------------------------------------------------------

        internal static void Begin(WavesArenaRuntimeModule arena, ArenaExpeditionRun run, Func<bool> isLive)
        {
            End();
            if (arena == null || run == null || run.Order == null || isLive == null) return;
            try
            {
                GameObject root = new GameObject(RootName);
                ArenaTrials trials = root.AddComponent<ArenaTrials>();
                Active = trials;
                trials.Setup(arena, run, isLive);
            }
            catch (Exception e)
            {
                Debug.LogWarning(LogPrefix + "战规启动失败（这一局按无战规打）: " + e.Message);
                End();
            }
        }

        internal static void End()
        {
            ArenaTrials trials = Active;
            Active = null;
            if (trials == null) return;
            trials.Shutdown();
            if (trials.gameObject != null) Destroy(trials.gameObject);
        }

        private void Setup(WavesArenaRuntimeModule owner, ArenaExpeditionRun run, Func<bool> live)
        {
            arena = owner;
            isLive = live;
            Run = run;
            Tier = run.Order.Tier;
            Profile = ArenaTrialsTuning.ProfileFor(Tier);
            Sky = new ArenaTrialsSky(this);
            Curses = new ArenaTrialsCurses(this);
            Hazards = new ArenaTrialsHazards(this);
            Affixes = new ArenaTrialsAffixes(this);
            FireRing = new ArenaTrialsFireRing(this);
            parts = new ArenaTrialPart[] { Sky, Curses, Hazards, Affixes, FireRing };
            if (!hurtSubscribed)
            {
                Health.OnHurt += OnAnyHurt;
                hurtSubscribed = true;
            }
            if (!deadSubscribed)
            {
                Health.OnDead += OnAnyDeath;
                deadSubscribed = true;
            }
            for (int i = 0; i < parts.Length; i++)
            {
                try { parts[i].OnBegin(); }
                catch (Exception e) { Debug.LogWarning(LogPrefix + parts[i].GetType().Name + " 启动失败: " + e.Message); }
            }
            ModBehaviour.DevLog(LogPrefix + "开局：战帖=" + Tier + "，天象=" + Profile.HasOmens + "，天灾=" + Profile.HasHazards
                + "，词缀=" + Profile.Affixes + "/" + Profile.FinaleAffixes + "，厄运=" + Profile.CurseLevel);
        }

        private void Shutdown()
        {
            if (shutDown) return;
            shutDown = true;
            if (hurtSubscribed)
            {
                Health.OnHurt -= OnAnyHurt;
                hurtSubscribed = false;
            }
            if (deadSubscribed)
            {
                Health.OnDead -= OnAnyDeath;
                deadSubscribed = false;
            }
            for (int i = parts.Length - 1; i >= 0; i--)
            {
                try { parts[i].Shutdown(); }
                catch (Exception e) { Debug.LogWarning(LogPrefix + parts[i].GetType().Name + " 收尾失败: " + e.Message); }
            }
            RuntimeStatModifierTracker.RemoveAll(playerRecords, "ArenaTrials player");
            foreach (KeyValuePair<CharacterMainControl, List<BossRushStatModifierRecord>> pair in foeRecords)
            {
                RuntimeStatModifierTracker.RemoveAll(pair.Value, "ArenaTrials foe");
            }
            foeRecords.Clear();
            foes.Clear();
            waveBosses.Clear();
            ArenaExpeditionHud.SetTrialLines(string.Empty, string.Empty, false);
            ArenaExpeditionHud.SetAmbience(ArenaExpeditionAmbience.None);
            arena = null;
            isLive = null;
        }

        private void OnDestroy()
        {
            // 切图时对象可能先于 End 被场景销毁：这里同样摘干净（Shutdown 幂等）。
            Shutdown();
            if (Active == this) Active = null;
        }

        // ------------------------------------------------------------------
        // 模块转进来的波次事件
        // ------------------------------------------------------------------

        internal static void NotifyWaveStart(int wave, bool finale)
        {
            ArenaTrials trials = Active;
            if (trials == null || trials.shutDown) return;
            trials.Wave = wave;
            trials.FinaleWave = finale;
            trials.WaveLive = true;
            trials.WaveClock = 0f;
            for (int i = 0; i < trials.parts.Length; i++)
            {
                try { trials.parts[i].OnWaveStart(wave, finale); }
                catch (Exception e) { ModBehaviour.DevLog(LogPrefix + "[WARNING] " + trials.parts[i].GetType().Name + " 开波失败: " + e.Message); }
            }
            trials.MarkPlayerStatsDirty();
            trials.MarkFoeStatsDirty();
        }

        internal static void NotifyWaveEnd()
        {
            ArenaTrials trials = Active;
            if (trials == null || trials.shutDown || !trials.WaveLive) return;
            trials.WaveLive = false;
            for (int i = 0; i < trials.parts.Length; i++)
            {
                try { trials.parts[i].OnWaveEnd(); }
                catch (Exception e) { ModBehaviour.DevLog(LogPrefix + "[WARNING] " + trials.parts[i].GetType().Name + " 收波失败: " + e.Message); }
            }
            // 不清 waveBosses：官方先发实例 OnDeadEvent（掉落 → HandleBossDeath → 收波）、后发静态 Health.OnDead，
            // 这里清掉的话，各部分在 OnAnyDeath 里就认不出刚倒下的是首领；死掉的由 PruneFoes 摘。
            trials.MarkPlayerStatsDirty();
        }

        /// <summary>
        /// 波次首领登记（普通 Boss 在战帖倍率之后、内容 Boss 在 RegisterContentWaveBoss 时）。
        /// preset 只有普通 Boss 才给：分身 / 双生 / 护卫照它再叫一只；内容 Boss（自带阶段与专属刷怪）给 null，只挂身法 / 死相两类。
        /// </summary>
        internal static void NotifyBossJoined(CharacterMainControl boss, EnemyPresetInfo preset)
        {
            ArenaTrials trials = Active;
            if (trials == null || trials.shutDown || boss == null) return;
            trials.waveBosses.Add(boss);
            if (trials.Affixes != null) trials.Affixes.NoteSource(boss, preset);
            trials.AddFoe(boss, true);
        }

        /// <summary>战规自己叫出来的敌方身体（分身、护卫、召唤）登记进来，吃天象与天灾。</summary>
        internal void AddFoe(CharacterMainControl foe, bool waveBoss)
        {
            if (foe == null || foes.Contains(foe)) return;
            foes.Add(foe);
            ApplyFoeStats(foe);
            for (int i = 0; i < parts.Length; i++)
            {
                try { parts[i].OnFoeJoined(foe, waveBoss); }
                catch (Exception e) { ModBehaviour.DevLog(LogPrefix + "[WARNING] " + parts[i].GetType().Name + " 登记敌人失败: " + e.Message); }
            }
        }

        internal bool IsWaveBoss(CharacterMainControl character)
        {
            return character != null && waveBosses.Contains(character);
        }

        internal bool IsFoe(CharacterMainControl character)
        {
            return character != null && foes.Contains(character);
        }

        /// <summary>本局活着的敌方身体（只读快照；调用方不要改）。</summary>
        internal List<CharacterMainControl> Foes { get { return foes; } }

        internal WavesArenaRuntimeModule Arena { get { return arena; } }

        // ------------------------------------------------------------------
        // 每帧
        // ------------------------------------------------------------------

        private void Update()
        {
            if (shutDown) return;
            bool live = false;
            try { live = isLive != null && isLive(); }
            catch (Exception) { live = false; }
            if (!live)
            {
                ModBehaviour.DevLog(LogPrefix + "竞技场已结束，收掉战规");
                if (Active == this) End();
                else { Shutdown(); Destroy(gameObject); }
                return;
            }

            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            if (WaveLive) WaveClock += dt;

            pruneTimer += dt;
            if (pruneTimer >= 0.5f)
            {
                pruneTimer = 0f;
                PruneFoes();
            }

            for (int i = 0; i < parts.Length; i++)
            {
                if (shutDown) return;
                try { parts[i].Tick(dt); }
                catch (Exception e)
                {
                    // 每帧热路径：只在 Dev 构建里留一行，不刷正式日志
                    ModBehaviour.DevLog(LogPrefix + "[WARNING] " + parts[i].GetType().Name + " Tick 失败: " + e.Message);
                }
            }
            if (shutDown) return;

            CharacterMainControl player = CharacterMainControl.Main;
            if (player != statPlayer)
            {
                statPlayer = player;
                playerStatsDirty = true;
            }
            if (playerStatsDirty) ApplyPlayerStats();
            if (foeStatsDirty) ApplyAllFoeStats();

            // 分身 / 双生压着的收波在这一帧放开：放在最后，推波（可能直接通关、收掉本对象）之后不再碰任何状态
            if (Affixes != null && Affixes.ConsumeWaveRelease() && arena != null) arena.ExpeditionReleaseHeldWave();
        }

        private void PruneFoes()
        {
            for (int i = foes.Count - 1; i >= 0; i--)
            {
                CharacterMainControl foe = foes[i];
                bool gone = foe == null || foe.Health == null || foe.Health.IsDead;
                if (!gone) continue;
                foes.RemoveAt(i);
                if (foe != null) waveBosses.Remove(foe);
                List<BossRushStatModifierRecord> records;
                if (foe != null && foeRecords.TryGetValue(foe, out records))
                {
                    RuntimeStatModifierTracker.RemoveAll(records, "ArenaTrials foe gone");
                    foeRecords.Remove(foe);
                }
            }
            // 已销毁的键（foe == null）字典里查不到，统一扫一遍
            if (foeRecords.Count > foes.Count)
            {
                List<CharacterMainControl> stale = null;
                foreach (KeyValuePair<CharacterMainControl, List<BossRushStatModifierRecord>> pair in foeRecords)
                {
                    if (pair.Key == null || !foes.Contains(pair.Key))
                    {
                        if (stale == null) stale = new List<CharacterMainControl>();
                        stale.Add(pair.Key);
                        RuntimeStatModifierTracker.RemoveAll(pair.Value, "ArenaTrials foe stale");
                    }
                }
                if (stale != null)
                {
                    for (int i = 0; i < stale.Count; i++) foeRecords.Remove(stale[i]);
                }
            }
        }

        // ------------------------------------------------------------------
        // 全局事件
        // ------------------------------------------------------------------

        private void OnAnyHurt(Health health, DamageInfo info)
        {
            if (shutDown || health == null) return;
            for (int i = 0; i < parts.Length; i++)
            {
                try { parts[i].OnAnyHurt(health, info); }
                catch (Exception e) { ModBehaviour.DevLog(LogPrefix + "[WARNING] 受伤回调失败: " + e.Message); }
            }
        }

        private void OnAnyDeath(Health health, DamageInfo info)
        {
            if (shutDown || health == null) return;
            for (int i = 0; i < parts.Length; i++)
            {
                try { parts[i].OnAnyDeath(health, info); }
                catch (Exception e) { ModBehaviour.DevLog(LogPrefix + "[WARNING] 死亡回调失败: " + e.Message); }
            }
        }

        // ------------------------------------------------------------------
        // 属性修饰：各部分只说「想要多少」，这里合成一条挂上去；值变了才重挂
        // ------------------------------------------------------------------

        internal void MarkPlayerStatsDirty() { playerStatsDirty = true; }
        internal void MarkFoeStatsDirty() { foeStatsDirty = true; }

        private void ApplyPlayerStats()
        {
            playerStatsDirty = false;
            RuntimeStatModifierTracker.RemoveAll(playerRecords, "ArenaTrials player refresh");
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null || player.CharacterItem == null) return;

            ArenaTrialPlayerStats stats = ArenaTrialPlayerStats.Identity;
            for (int i = 0; i < parts.Length; i++)
            {
                try { parts[i].ContributePlayer(ref stats); }
                catch (Exception e) { ModBehaviour.DevLog(LogPrefix + "[WARNING] 玩家属性合成失败: " + e.Message); }
            }

            float floor = ArenaTrialsTuning.VisionFloor;
            Multiply(player, "ViewDistance", Mathf.Max(floor, stats.ViewDistance), playerRecords);
            Multiply(player, "ViewAngle", Mathf.Max(floor, stats.ViewAngle), playerRecords);
            Multiply(player, "SenseRange", Mathf.Max(floor, stats.SenseRange), playerRecords);
            Multiply(player, ZombieModeStatNames.WalkSpeed, Mathf.Max(0.2f, stats.Move), playerRecords);
            Multiply(player, ZombieModeStatNames.RunSpeed, Mathf.Max(0.2f, stats.Move), playerRecords);
            Multiply(player, ZombieModeStatNames.StaminaRecoverRate, Mathf.Max(0.05f, stats.Stamina), playerRecords);
            Multiply(player, "GunScatterMultiplier", Mathf.Max(0.1f, stats.Scatter), playerRecords);
            Multiply(player, ZombieModeStatNames.MaxHealth, Mathf.Clamp(stats.MaxHealth, 0.3f, 1f), playerRecords);
            if (Mathf.Abs(stats.ReloadGain) > 0.0001f)
            {
                RuntimeStatModifierTracker.TryAdd(player, ZombieModeStatNames.ReloadSpeedGain, stats.ReloadGain, this,
                    playerRecords, "ArenaTrials reload", ModifierType.Add);
            }
            ClampPlayerHealth(player);
        }

        private void ApplyAllFoeStats()
        {
            foeStatsDirty = false;
            for (int i = 0; i < foes.Count; i++) ApplyFoeStats(foes[i]);
        }

        private void ApplyFoeStats(CharacterMainControl foe)
        {
            if (foe == null || foe.CharacterItem == null) return;
            List<BossRushStatModifierRecord> records;
            if (!foeRecords.TryGetValue(foe, out records))
            {
                records = new List<BossRushStatModifierRecord>(4);
                foeRecords[foe] = records;
            }
            RuntimeStatModifierTracker.RemoveAll(records, "ArenaTrials foe refresh");
            ArenaTrialFoeStats stats = ArenaTrialFoeStats.Identity;
            for (int i = 0; i < parts.Length; i++)
            {
                try { parts[i].ContributeFoe(foe, ref stats); }
                catch (Exception e) { ModBehaviour.DevLog(LogPrefix + "[WARNING] 敌人属性合成失败: " + e.Message); }
            }
            Multiply(foe, ZombieModeStatNames.WalkSpeed, Mathf.Max(0.2f, stats.Move), records);
            Multiply(foe, ZombieModeStatNames.RunSpeed, Mathf.Max(0.2f, stats.Move), records);
            Multiply(foe, ZombieModeStatNames.GunDamageMultiplier, Mathf.Max(0.1f, stats.Damage), records);
            Multiply(foe, ZombieModeStatNames.MeleeDamageMultiplier, Mathf.Max(0.1f, stats.Damage), records);
            float taken = Mathf.Clamp(stats.Taken, 0.05f, 1f);
            for (int i = 0; i < ElementFactorStats.Length; i++) Multiply(foe, ElementFactorStats[i], taken, records);
        }

        /// <summary>官方 Health.ElementFactor 读的七个元素系数（物理 / 火 / 毒 / 电 / 空间 / 幽灵 / 冰）：受伤倍数要七个一起乘。</summary>
        private static readonly string[] ElementFactorStats =
        {
            "ElementFactor_Physics", "ElementFactor_Fire", "ElementFactor_Poison", "ElementFactor_Electricity",
            "ElementFactor_Space", "ElementFactor_Ghost", "ElementFactor_Ice",
        };

        private void Multiply(CharacterMainControl character, string stat, float factor, List<BossRushStatModifierRecord> records)
        {
            if (Mathf.Abs(factor - 1f) < 0.0001f) return;
            RuntimeStatModifierTracker.TryAdd(character, stat, factor - 1f, this, records, "ArenaTrials " + stat,
                ModifierType.PercentageMultiply);
        }

        private static void ClampPlayerHealth(CharacterMainControl player)
        {
            try
            {
                Health health = player.Health;
                if (health != null && !health.IsDead && health.CurrentHealth > health.MaxHealth)
                {
                    health.SetHealth(health.MaxHealth);
                }
            }
            catch (Exception) { }
        }

        // ------------------------------------------------------------------
        // 供各部分用的小工具
        // ------------------------------------------------------------------

        /// <summary>当前地图的首领刷新点（焚城、天灾落点用）。</summary>
        internal Vector3[] SpawnPoints()
        {
            try { return arena != null ? arena.ExpeditionSpawnPoints() : null; }
            catch (Exception) { return null; }
        }

        /// <summary>按生命上限的比例算一下伤害：玩家吃满额，其余吃 <see cref="ArenaTrialsTuning.HazardFoeDamageShare"/>。</summary>
        internal static float ShareOfMaxHealth(Health health, float fraction)
        {
            if (health == null) return 0f;
            float share = health.IsMainCharacterHealth ? 1f : ArenaTrialsTuning.HazardFoeDamageShare;
            return Mathf.Max(1f, health.MaxHealth * fraction * share);
        }

        /// <summary>
        /// 对一个 Health 打一下「无来源」的战规伤害（天灾、焚城）：不看护甲、不吃难度倍率，dodgeable 时翻滚中的不受伤。
        /// element 走官方元素系数（火 / 毒 / 电各有抗性装备，跳字也按元素着色）；只用全角色都带系数的物理 / 火 / 毒 / 电。
        /// </summary>
        internal static bool HurtNeutral(Health health, float damage, Vector3 from, bool dodgeable,
            ElementTypes element = ElementTypes.physics)
        {
            if (health == null || health.IsDead || damage <= 0f) return false;
            try
            {
                CharacterMainControl character = health.TryGetCharacter();
                if (dodgeable && character != null && character.Dashing) return false;
                DamageInfo info = new DamageInfo((CharacterMainControl)null);
                info.damageValue = damage;
                info.ignoreArmor = true;
                info.ignoreDifficulty = true;
                info.isFromBuffOrEffect = true;
                info.fromWeaponItemID = 0;
                // 构造函数已建好空表；留空时官方按物理 ×1 结算
                if (element != ElementTypes.physics && info.elementFactors != null)
                {
                    info.elementFactors.Add(new ElementFactor(element, 1f));
                }
                Vector3 at = (character != null ? character.transform.position : health.transform.position) + Vector3.up * 0.6f;
                info.damagePoint = at;
                Vector3 normal = at - from;
                normal.y = 0f;
                info.damageNormal = normal.sqrMagnitude > 0.0001f ? normal.normalized : Vector3.forward;
                return health.Hurt(info);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + "[WARNING] 战规伤害失败: " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// 枚举离 center 在 radius 内、会被战规波及的 Health：玩家 + 本局敌方身体。
        /// 只看这两类，不扫物理层：天灾不该把场景里的中立物件、鸭生无常的乱入者一起卷进来。
        /// </summary>
        internal void CollectVictims(Vector3 center, float radius, List<Health> output)
        {
            output.Clear();
            float sq = radius * radius;
            CharacterMainControl player = CharacterMainControl.Main;
            if (player != null && player.Health != null && !player.Health.IsDead && FlatSqr(player.transform.position, center) <= sq)
            {
                output.Add(player.Health);
            }
            for (int i = 0; i < foes.Count; i++)
            {
                CharacterMainControl foe = foes[i];
                if (foe == null || foe.Health == null || foe.Health.IsDead) continue;
                if (FlatSqr(foe.transform.position, center) <= sq) output.Add(foe.Health);
            }
        }

        internal static float FlatSqr(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return dx * dx + dz * dz;
        }
    }
}
