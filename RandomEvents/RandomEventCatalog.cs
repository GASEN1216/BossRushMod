// ============================================================================
// RandomEventCatalog.cs — 局内随机事件「鸭生无常」注册表 + E1~E4 实现
// ============================================================================
// 模块职责：
//   1. RandomEventCatalog：8 个事件实例的懒构建缓存与查找入口（只读，构造后不可变）。
//   2. E1 空投补给 / E2 血月凶兆 / E3 Boss 乱入 / E4 神秘商人路过 的完整实现。
//      E5~E8 见同名 partial 追加文件 RandomEventCatalog_Fun.cs。
//
// 全局硬约束（违反即为缺陷）：
//   - 每个 OnTrigger 必须有配对的 OnCleanup，且 OnCleanup 必须幂等，
//     同时覆盖五条路径：自然到时 / 玩家死亡（局结束）/ 撤离胜利（局结束）/ 切图 / 运行中关开关。
//     实现手段：所有生成物一律登记进 ctx.Scope，非 Scope 管辖的实体（乱入 Boss、商人）
//     由事件自有列表 + _cleanedUp 幂等闸负责。
//   - OnTrigger / OnTick / OnCleanup 整体包 try/catch，异常只 DevLog 不外抛（AGENTS 4.7）。
//   - OnTick 是每帧热路径：禁止日志、禁止每帧分配、禁止每帧扫场景（AGENTS 4.12）。
//   - 零新增 Harmony patch；不订阅任何静态事件；唯一的事件订阅是 E3 的逐实例
//     Health.OnDeadEvent（UnityEvent，命名方法 + 缓存委托实例，清理时成对 RemoveListener）。
//   - 不触碰任何波次状态机符号（隔离面见 tests/RandomEventsWaveIsolationGuard.py）。
//   - UI 一律走共享库：sortingOrder 用 BossRushUILayers 常量，颜色用 BossRushUIColors，
//     Canvas 用 BossRushUI.CreateCanvasRoot（内部已配 CanvasScaler）。
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Duckov.Economy;

namespace BossRush
{
    /// <summary>8 个事件的注册表。懒构建 + 缓存，构造后不可变。</summary>
    internal static class RandomEventCatalog
    {
        private static RandomEventBase[] _all;

        /// <summary>按 RandomEventId 顺序返回全部事件实例。失败时返回空数组，绝不返回 null。</summary>
        internal static RandomEventBase[] GetAll()
        {
            if (_all != null)
            {
                return _all;
            }

            try
            {
                _all = new RandomEventBase[]
                {
                    new RandomEventAirdropSupply(),
                    new RandomEventBloodMoon(),
                    new RandomEventBossIntrusion(),
                    new RandomEventWanderingMerchant(),
                    new RandomEventFeint(),
                    new RandomEventFireworks(),
                    new RandomEventGoldenDuckRain(),
                    new RandomEventDuckParade()
                };
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(RandomEventsTuning.LogPrefix + "[ERROR] 事件注册表构建失败: " + e.Message);
                _all = new RandomEventBase[0];
            }

            return _all;
        }

        internal static RandomEventBase Find(RandomEventId id)
        {
            RandomEventBase[] all = GetAll();
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].Id == id)
                {
                    return all[i];
                }
            }
            return null;
        }

        /// <summary>丢弃缓存实例。宿主销毁时由运行时模块调用。</summary>
        internal static void ResetStaticCaches()
        {
            _all = null;
        }
    }

    /// <summary>事件实现共用的小工具。无状态。</summary>
    internal static class RandomEventEffectHelpers
    {
        /// <summary>解析事件宿主。ctx.Owner 优先，缺失时回落到单例；都拿不到返回 null。</summary>
        internal static ModBehaviour ResolveOwner(RandomEventContext ctx)
        {
            try
            {
                if (ctx != null && ctx.Owner != null)
                {
                    return ctx.Owner;
                }
                return ModBehaviour.Instance;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>解析主角。不存在或已死返回 null。</summary>
        internal static CharacterMainControl ResolveAlivePlayer()
        {
            try
            {
                CharacterMainControl player = CharacterMainControl.Main;
                if (player == null || player.Health == null || player.Health.IsDead)
                {
                    return null;
                }
                return player;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>ctx.Scope 的 no-throw 清空。</summary>
        internal static void ClearScope(RandomEventContext ctx, RandomEventEndReason reason)
        {
            try
            {
                if (ctx != null && ctx.Scope != null)
                {
                    ctx.Scope.Clear(reason.ToString());
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(RandomEventsTuning.LogPrefix + "[WARNING] 事件作用域清理失败: " + e.Message);
            }
        }
    }

    // ========================================================================
    // E1 空投补给
    // ========================================================================

    /// <summary>
    /// 空投补给：远处落下一个高品质奖励箱，落地伴随零伤爆炸与引怪声源。
    /// 清理 = 销毁箱子（Scope 托管），到时 / 局末 / 切图 / 关开关五条路径同一入口。
    /// </summary>
    internal sealed class RandomEventAirdropSupply : RandomEventBase
    {
        internal override RandomEventId Id { get { return RandomEventId.AirdropSupply; } }

        internal override string DisplayName
        {
            get { return L10n.T("空投补给", "Airdrop Supply"); }
        }

        internal override float DurationSeconds { get { return RandomEventsTuning.AirdropDurationSeconds; } }

        internal override float Weight { get { return RandomEventsTuning.WeightAirdropSupply; } }

        internal override bool OnTrigger(RandomEventContext ctx)
        {
            try
            {
                ModBehaviour owner = RandomEventEffectHelpers.ResolveOwner(ctx);
                CharacterMainControl player = RandomEventEffectHelpers.ResolveAlivePlayer();
                if (owner == null || player == null || ctx == null || ctx.Scope == null)
                {
                    return false;
                }

                Vector3[] points = owner.GetRandomEventSpawnPointsSafe();
                Vector3 ground;
                if (points != null && points.Length > 0)
                {
                    ground = SpawnPositionHelper.FindNearestSafeSpawnPoint(
                        points,
                        player.transform.position,
                        RandomEventsTuning.AirdropMinPlayerDistance);
                }
                else
                {
                    ground = SpawnPositionHelper.SnapToGround(
                        player.transform.position + player.transform.forward * 20f);
                }

                int qualityMax = RandomEventsTuning.AirdropQualityMaxNormal;
                try
                {
                    if (owner.IsModeDActive)
                    {
                        // 白手起家单独压上限，避免一箱空投直接跳过前期养成
                        qualityMax = RandomEventsTuning.AirdropQualityMaxModeD;
                    }
                    else if (owner.IsRandomEventInfiniteHellRun())
                    {
                        qualityMax = RandomEventsTuning.AirdropQualityMaxInfiniteHell;
                    }
                }
                catch (Exception) { }

                InteractableLootbox crate = owner.CreateRandomEventAirdropLootbox(
                    ground + Vector3.up * RandomEventsTuning.AirdropFallHeight,
                    RandomEventsTuning.AirdropItemCount,
                    RandomEventsTuning.AirdropQualityMinNormal,
                    qualityMax);
                if (crate == null || crate.gameObject == null)
                {
                    return false;
                }

                ctx.AnchorPosition = ground;
                ctx.Scope.RegisterObject(crate.gameObject);

                try
                {
                    Coroutine co = owner.StartCoroutine(owner.RandomEventAirdropDropRoutine(
                        crate.gameObject,
                        ground,
                        RandomEventsTuning.AirdropFallHeight,
                        RandomEventsTuning.AirdropFallSeconds,
                        null));
                    ctx.Scope.RegisterCoroutine(co);
                }
                catch (Exception e)
                {
                    // 下落动画失败不致命：箱子已在半空登记，直接落到地面即可
                    ModBehaviour.DevLog(RandomEventsTuning.LogPrefix + "[WARNING] 空投下落协程启动失败: " + e.Message);
                    try { crate.transform.position = ground; } catch (Exception) { }
                }

                owner.ShowRandomEventDirectionalBanner(DisplayName, ground);
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(RandomEventsTuning.LogPrefix + "[ERROR] 空投补给触发失败: " + e.Message);
                return false;
            }
        }

        internal override void OnCleanup(RandomEventContext ctx, RandomEventEndReason reason)
        {
            // 首版按「事件结束即回收箱子」实现：不回收就会跨图泄漏，
            // 若将来要让箱子留到局末，必须改成自有列表并在切图 / 宿主销毁时销毁。
            RandomEventEffectHelpers.ClearScope(ctx, reason);
        }
    }

    // ========================================================================
    // E2 血月凶兆
    // ========================================================================

    /// <summary>
    /// 血月凶兆：全屏红色 vignette 呼吸 + 场内存活敌人临时增益 + 强制暴风雨天气。
    /// 献祭支线：每有一只被增益的敌人死亡，给玩家结算一笔现金。
    ///
    /// 性能纪律：目标列表经桥只读收集（量级十几个），按 2 秒节流补挂新怪，
    /// **绝不每帧扫场景**；Tick 里只有 float 运算与一次 Image.color 赋值。
    /// </summary>
    internal sealed class RandomEventBloodMoon : RandomEventBase
    {
        private Image _vignette;
        private readonly List<ZombieModeAttributeModifierRecord> _records =
            new List<ZombieModeAttributeModifierRecord>(64);
        private readonly List<CharacterMainControl> _tracked = new List<CharacterMainControl>(32);
        private readonly List<CharacterMainControl> _collectBuffer = new List<CharacterMainControl>(64);
        private float _refreshTimer;
        private bool _weatherApplied;
        private bool _prevForceWeather;
        private Duckov.Weathers.Weather _prevWeatherValue;

        internal override RandomEventId Id { get { return RandomEventId.BloodMoon; } }

        internal override string DisplayName
        {
            get { return L10n.T("血月凶兆", "Blood Moon"); }
        }

        internal override float DurationSeconds { get { return RandomEventsTuning.BloodMoonDurationSeconds; } }

        internal override float Weight { get { return RandomEventsTuning.WeightBloodMoon; } }

        internal override bool OnTrigger(RandomEventContext ctx)
        {
            try
            {
                ModBehaviour owner = RandomEventEffectHelpers.ResolveOwner(ctx);
                if (owner == null || ctx == null || ctx.Scope == null)
                {
                    return false;
                }

                // 每次触发都是全新一轮，先把上一轮的残留状态归零
                _records.Clear();
                _tracked.Clear();
                _collectBuffer.Clear();
                _refreshTimer = 0f;
                _weatherApplied = false;
                _vignette = null;

                // ── 全屏红 vignette：uGUI 纯色 Image + alpha 呼吸，零贴图、非交互 ──
                try
                {
                    Canvas canvas = BossRushUI.CreateCanvasRoot(
                        "BossRushBloodMoon",
                        BossRushUILayers.HudOverlay,
                        false);
                    if (canvas != null)
                    {
                        ctx.Scope.RegisterObject(canvas.gameObject);
                        Image img = BossRushUI.CreateBackdrop(canvas.transform);
                        if (img != null)
                        {
                            img.color = new Color(0.62f, 0.05f, 0.06f, RandomEventsTuning.BloodMoonVignetteAlphaMin);
                            img.raycastTarget = false;
                            _vignette = img;
                        }
                    }
                }
                catch (Exception e)
                {
                    // 遮罩失败不算触发失败：增益与天气仍然生效
                    ModBehaviour.DevLog(RandomEventsTuning.LogPrefix + "[WARNING] 血月遮罩构建失败: " + e.Message);
                }

                // ── 强制天气支线：Instance 为 null 时静默跳过，天气失败不算触发失败 ──
                try
                {
                    bool prevForce;
                    Duckov.Weathers.Weather prevValue;
                    if (owner.TryApplyRandomEventForcedWeather(
                            Duckov.Weathers.Weather.Stormy_I,
                            out prevForce,
                            out prevValue))
                    {
                        _weatherApplied = true;
                        _prevForceWeather = prevForce;
                        _prevWeatherValue = prevValue;
                        // 兜底 1/3：登记进 Scope，随对象销毁前执行
                        ctx.Scope.RegisterCleanup(RestoreWeather);
                    }
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog(RandomEventsTuning.LogPrefix + "[WARNING] 血月天气支线失败: " + e.Message);
                }

                owner.PlayRandomEventStinger(RandomEventsTuning.BloodMoonStingerKey);
                owner.ShowRandomEventBanner(L10n.T("血月当空：敌人变得狂暴", "Blood Moon: enemies turn frenzied"));

                RefreshEnemyModifiers(owner);
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(RandomEventsTuning.LogPrefix + "[ERROR] 血月触发失败: " + e.Message);
                return false;
            }
        }

        internal override void OnTick(RandomEventContext ctx, float deltaTime)
        {
            try
            {
                if (ctx == null)
                {
                    return;
                }

                // 呼吸动画：纯 float 运算 + 一次 color 赋值，无分配、无日志
                if (_vignette != null)
                {
                    float phase = (Mathf.Sin(
                        ctx.ElapsedSeconds * (Mathf.PI * 2f) / RandomEventsTuning.BloodMoonVignetteBreathSeconds) + 1f) * 0.5f;
                    float alpha = Mathf.Lerp(
                        RandomEventsTuning.BloodMoonVignetteAlphaMin,
                        RandomEventsTuning.BloodMoonVignetteAlphaMax,
                        phase);
                    Color c = _vignette.color;
                    c.a = alpha;
                    _vignette.color = c;
                }

                _refreshTimer += deltaTime;
                if (_refreshTimer < RandomEventsTuning.BloodMoonRefreshIntervalSeconds)
                {
                    return;
                }
                _refreshTimer = 0f;

                ModBehaviour owner = RandomEventEffectHelpers.ResolveOwner(ctx);
                if (owner == null)
                {
                    return;
                }

                SettleSacrifice(owner);
                RefreshEnemyModifiers(owner);
            }
            catch (Exception)
            {
                // Tick 是热路径，异常不刷屏
            }
        }

        /// <summary>
        /// 给尚未挂过 buff 的敌人补挂临时 Stat Modifier。
        /// 移速必须三件套同挂（MoveSpeed / WalkSpeed / RunSpeed），只挂 MoveSpeed 官方角色不吃；
        /// 伤害同理需要枪械 + 近战两条。
        /// </summary>
        private void RefreshEnemyModifiers(ModBehaviour owner)
        {
            try
            {
                owner.CollectEventBuffTargets(_collectBuffer);

                for (int i = 0; i < _collectBuffer.Count; i++)
                {
                    CharacterMainControl c = _collectBuffer[i];
                    if (c == null || _tracked.Contains(c))
                    {
                        continue;
                    }

                    RuntimeStatModifierTracker.TryAdd(c, ZombieModeStatNames.MoveSpeed,
                        RandomEventsTuning.BloodMoonEnemyMoveSpeedBonus, this, _records, "BloodMoon MoveSpeed");
                    RuntimeStatModifierTracker.TryAdd(c, ZombieModeStatNames.WalkSpeed,
                        RandomEventsTuning.BloodMoonEnemyMoveSpeedBonus, this, _records, "BloodMoon WalkSpeed");
                    RuntimeStatModifierTracker.TryAdd(c, ZombieModeStatNames.RunSpeed,
                        RandomEventsTuning.BloodMoonEnemyMoveSpeedBonus, this, _records, "BloodMoon RunSpeed");
                    RuntimeStatModifierTracker.TryAdd(c, ZombieModeStatNames.GunDamageMultiplier,
                        RandomEventsTuning.BloodMoonEnemyDamageBonus, this, _records, "BloodMoon GunDamage");
                    RuntimeStatModifierTracker.TryAdd(c, ZombieModeStatNames.MeleeDamageMultiplier,
                        RandomEventsTuning.BloodMoonEnemyDamageBonus, this, _records, "BloodMoon MeleeDamage");

                    _tracked.Add(c);
                }

                _collectBuffer.Clear();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(RandomEventsTuning.LogPrefix + "[WARNING] 血月增益挂载失败: " + e.Message);
            }
        }

        #region 献祭（可选玩法，摘除时整块删掉即可）

        /// <summary>
        /// 结算献祭收益。刻意**不新订死亡事件**：在 2 秒节流内比对已挂 buff 名单的存活状态，
        /// 成本 O(已挂怪数)，与死亡回调链完全解耦。
        /// </summary>
        private void SettleSacrifice(ModBehaviour owner)
        {
            try
            {
                for (int i = _tracked.Count - 1; i >= 0; i--)
                {
                    CharacterMainControl c = _tracked[i];
                    if (c == null)
                    {
                        // 已销毁：无法判断是被杀还是被清场，不结算，仅退出名单
                        _tracked.RemoveAt(i);
                        continue;
                    }

                    bool dead = false;
                    try { dead = c.Health == null || c.Health.IsDead; }
                    catch (Exception) { dead = true; }

                    if (!dead)
                    {
                        continue;
                    }

                    _tracked.RemoveAt(i);
                    try
                    {
                        Duckov.Economy.EconomyManager.Add(RandomEventsTuning.BloodMoonCashPerKill);
                    }
                    catch (Exception) { }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(RandomEventsTuning.LogPrefix + "[WARNING] 血月献祭结算失败: " + e.Message);
            }
        }

        #endregion

        /// <summary>还原被本事件改过的强制天气。幂等：只在 _weatherApplied 为真时执行一次。</summary>
        private void RestoreWeather()
        {
            if (!_weatherApplied)
            {
                return;
            }
            _weatherApplied = false;

            try
            {
                ModBehaviour owner = ModBehaviour.Instance;
                if (owner != null)
                {
                    owner.RestoreRandomEventForcedWeather(_prevForceWeather, _prevWeatherValue);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(RandomEventsTuning.LogPrefix + "[WARNING] 血月天气还原失败: " + e.Message);
            }
        }

        internal override void OnCleanup(RandomEventContext ctx, RandomEventEndReason reason)
        {
            // 兜底 2/3：事件到时 / 局末 / 切图 / 关开关 / 宿主销毁全部走这里，
            // 且必须先于 Scope 清理执行——即使遮罩销毁抛异常，属性也要摘干净。
            RestoreWeather();

            try
            {
                RuntimeStatModifierTracker.RemoveAll(_records, "BloodMoon cleanup");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(RandomEventsTuning.LogPrefix + "[WARNING] 血月属性移除失败: " + e.Message);
            }

            _tracked.Clear();
            _collectBuffer.Clear();
            _vignette = null;
            _refreshTimer = 0f;

            // 兜底 3/3：Scope 里注册的天气还原动作（已被 RestoreWeather 幂等闸吞掉）+ 遮罩销毁
            RandomEventEffectHelpers.ClearScope(ctx, reason);
        }
    }

    // ========================================================================
    // E3 Boss 乱入
    // ========================================================================

    /// <summary>
    /// Boss 乱入：一只额外的 Boss 闯进战场，限时未被击杀则撤退并销毁。
    ///
    /// ⚠️ 本事件是整个子系统里最高危的一项，改动前必须确认：
    ///   - 生成物**绝不写入任何波次容器、绝不参与推波计数**（隔离面见波次隔离 guard）；
    ///   - 敌对性安全网在桥的 onCommit 内执行（AGENTS 4.5），不得移除；
    ///   - 逐实例 Health.OnDeadEvent 监听必须成对 RemoveListener，委托实例必须缓存；
    ///   - 清理必须自己 Destroy：通关清场按 characterPreset.team 判定，
    ///     被运行时 SetTeam 修正过的中立预设不会被它清掉。
    /// </summary>
    internal sealed class RandomEventBossIntrusion : RandomEventBase
    {
        private readonly List<CharacterMainControl> _intruders = new List<CharacterMainControl>(4);
        private readonly List<RandomEventIntruderDeathWatcher> _watchers =
            new List<RandomEventIntruderDeathWatcher>(4);
        private bool _cleanedUp = true;
        private int _sceneBuildIndex;
        private float _pruneTimer;

        internal override RandomEventId Id { get { return RandomEventId.BossIntrusion; } }

        internal override string DisplayName
        {
            get { return L10n.T("Boss 乱入", "Boss Intrusion"); }
        }

        internal override float DurationSeconds { get { return RandomEventsTuning.BossIntrusionDurationSeconds; } }

        internal override float Weight { get { return RandomEventsTuning.WeightBossIntrusion; } }

        internal override bool OnTrigger(RandomEventContext ctx)
        {
            try
            {
                ModBehaviour owner = RandomEventEffectHelpers.ResolveOwner(ctx);
                CharacterMainControl player = RandomEventEffectHelpers.ResolveAlivePlayer();
                if (owner == null || player == null || ctx == null)
                {
                    return false;
                }

                List<EnemyPresetInfo> pool = null;
                try { pool = owner.GetFilteredEnemyPresets(); }
                catch (Exception e)
                {
                    ModBehaviour.DevLog(RandomEventsTuning.LogPrefix + "[WARNING] 读取 Boss 预设池失败: " + e.Message);
                }

                if (pool == null || pool.Count == 0)
                {
                    return false;
                }

                EnemyPresetInfo preset = pool[UnityEngine.Random.Range(0, pool.Count)];
                if (preset == null)
                {
                    return false;
                }

                Vector3 pos = owner.GetRandomEventSafePointAwayFromPlayer(
                    RandomEventsTuning.BossIntrusionMinPlayerDistance);

                _intruders.Clear();
                _watchers.Clear();
                _pruneTimer = 0f;
                _cleanedUp = false;
                _sceneBuildIndex = SceneManager.GetActiveScene().buildIndex;

                int count = Mathf.Max(1, RandomEventsTuning.BossIntrusionCount);
                for (int i = 0; i < count; i++)
                {
                    owner.SpawnRandomEventIntruderBoss(
                        preset,
                        pos,
                        IsSpawnStillValid,
                        HandleIntruderSpawned,
                        HandleIntruderSpawnFailed);
                }

                string label = string.IsNullOrEmpty(preset.displayName) ? DisplayName : preset.displayName;
                owner.ShowRandomEventDirectionalBanner(
                    label + L10n.T(" 乱入战场！", " has crashed the fight!"),
                    pos);

                ctx.AnchorPosition = pos;
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(RandomEventsTuning.LogPrefix + "[ERROR] Boss 乱入触发失败: " + e.Message);
                _cleanedUp = true;
                return false;
            }
        }

        /// <summary>异步生成续作的有效性闸。命名方法，供桥以方法组形式引用。</summary>
        private bool IsSpawnStillValid()
        {
            try
            {
                return !_cleanedUp && SceneManager.GetActiveScene().buildIndex == _sceneBuildIndex;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private void HandleIntruderSpawned(CharacterMainControl boss)
        {
            if (boss == null)
            {
                return;
            }

            ModBehaviour owner = ModBehaviour.Instance;

            // 生成完成时事件可能已经收尾，直接回收，绝不留活口
            if (_cleanedUp)
            {
                if (owner != null)
                {
                    owner.DespawnRandomEventIntruderBoss(boss);
                }
                return;
            }

            _intruders.Add(boss);

            try
            {
                RandomEventIntruderDeathWatcher watcher = new RandomEventIntruderDeathWatcher(boss);
                if (watcher.Subscribe())
                {
                    _watchers.Add(watcher);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(RandomEventsTuning.LogPrefix + "[WARNING] 乱入 Boss 死亡监听注册失败: " + e.Message);
            }
        }

        private void HandleIntruderSpawnFailed()
        {
            // 已播报的横幅不撤：玩家看到「乱入」但没找到人，比横幅闪烁体验更好
            ModBehaviour.DevLog(RandomEventsTuning.LogPrefix + "乱入 Boss 生成失败，本次事件空转");
        }

        internal override void OnTick(RandomEventContext ctx, float deltaTime)
        {
            try
            {
                if (_watchers.Count == 0 && _intruders.Count == 0)
                {
                    return;
                }

                // 死亡剪枝每秒一次即可，不必每帧
                _pruneTimer += deltaTime;
                if (_pruneTimer < 1f)
                {
                    return;
                }
                _pruneTimer = 0f;

                for (int i = _watchers.Count - 1; i >= 0; i--)
                {
                    RandomEventIntruderDeathWatcher watcher = _watchers[i];
                    if (watcher == null || watcher.IsFinished)
                    {
                        if (watcher != null)
                        {
                            watcher.Unsubscribe();
                        }
                        _watchers.RemoveAt(i);
                    }
                }

                for (int i = _intruders.Count - 1; i >= 0; i--)
                {
                    CharacterMainControl boss = _intruders[i];
                    bool gone = boss == null;
                    if (!gone)
                    {
                        try { gone = boss.Health == null || boss.Health.IsDead; }
                        catch (Exception) { gone = true; }
                    }
                    if (gone)
                    {
                        _intruders.RemoveAt(i);
                    }
                }
            }
            catch (Exception)
            {
                // 热路径不刷屏
            }
        }

        internal override void OnCleanup(RandomEventContext ctx, RandomEventEndReason reason)
        {
            // 幂等闸：置位后所有异步续作自行作废
            _cleanedUp = true;

            ModBehaviour owner = RandomEventEffectHelpers.ResolveOwner(ctx);

            // 逐实例死亡监听成对退订
            RunScopedRegistry.ForEachReverse(
                _watchers,
                delegate (RandomEventIntruderDeathWatcher w) { w.Unsubscribe(); },
                delegate (Exception e, RandomEventIntruderDeathWatcher w)
                {
                    ModBehaviour.DevLog(RandomEventsTuning.LogPrefix + "[WARNING] 退订乱入 Boss 死亡监听失败: " + e.Message);
                });
            _watchers.Clear();

            // 超时未被击杀 → 撤退横幅（只播一次）
            bool anyAlive = false;
            for (int i = 0; i < _intruders.Count; i++)
            {
                CharacterMainControl boss = _intruders[i];
                if (boss == null)
                {
                    continue;
                }
                try
                {
                    if (boss.Health != null && !boss.Health.IsDead)
                    {
                        anyAlive = true;
                        break;
                    }
                }
                catch (Exception) { }
            }

            if (anyAlive && owner != null && reason == RandomEventEndReason.Expired)
            {
                owner.ShowRandomEventBanner(L10n.T("乱入者撤退了", "The intruder has withdrawn"));
            }

            RunScopedRegistry.ForEachReverse(
                _intruders,
                delegate (CharacterMainControl boss)
                {
                    ModBehaviour host = ModBehaviour.Instance;
                    if (host != null)
                    {
                        host.DespawnRandomEventIntruderBoss(boss);
                    }
                },
                delegate (Exception e, CharacterMainControl boss)
                {
                    ModBehaviour.DevLog(RandomEventsTuning.LogPrefix + "[WARNING] 销毁乱入 Boss 失败: " + e.Message);
                });
            _intruders.Clear();
            _pruneTimer = 0f;

            RandomEventEffectHelpers.ClearScope(ctx, reason);
        }
    }

    /// <summary>
    /// 乱入 Boss 的逐实例死亡监听。
    /// 用**命名方法 + 缓存委托实例**订阅官方 UnityEvent，保证 RemoveListener 能精确摘掉；
    /// 回调只置一个标志位，真正的剪枝在事件 Tick 里做，避免在事件派发过程中改动集合。
    /// </summary>
    internal sealed class RandomEventIntruderDeathWatcher
    {
        private readonly CharacterMainControl _boss;
        private Health _health;
        private UnityAction<DamageInfo> _handler;
        private bool _subscribed;
        private bool _dead;

        internal RandomEventIntruderDeathWatcher(CharacterMainControl boss)
        {
            _boss = boss;
        }

        /// <summary>监听目标已死亡或已失效。</summary>
        internal bool IsFinished
        {
            get
            {
                if (_dead || _boss == null)
                {
                    return true;
                }
                try { return _boss.Health == null || _boss.Health.IsDead; }
                catch (Exception) { return true; }
            }
        }

        internal bool Subscribe()
        {
            if (_subscribed || _boss == null)
            {
                return false;
            }

            try
            {
                _health = _boss.Health;
                if (_health == null || _health.OnDeadEvent == null)
                {
                    return false;
                }

                _handler = new UnityAction<DamageInfo>(HandleDead);
                _health.OnDeadEvent.AddListener(_handler);
                _subscribed = true;
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(RandomEventsTuning.LogPrefix + "[WARNING] 订阅乱入 Boss 死亡失败: " + e.Message);
                _subscribed = false;
                return false;
            }
        }

        /// <summary>成对退订。幂等。</summary>
        internal void Unsubscribe()
        {
            if (!_subscribed)
            {
                return;
            }
            _subscribed = false;

            try
            {
                if (_health != null && _health.OnDeadEvent != null && _handler != null)
                {
                    _health.OnDeadEvent.RemoveListener(_handler);
                }
            }
            catch (Exception) { }

            _handler = null;
            _health = null;
        }

        private void HandleDead(DamageInfo damageInfo)
        {
            _dead = true;
        }
    }

    // ========================================================================
    // E4 神秘商人路过
    // ========================================================================

    /// <summary>
    /// 神秘商人路过：玩家身前出现一名限时商人，卖弹药 / 医疗 + 1 件随机高品质，统一溢价。
    /// 清理 = 关 UI → 销毁商店 → 销毁角色（顺序不可反）。
    ///
    /// 存档语义提示：StockShop 是官方 ISaveDataProvider，商人在场期间若发生一次存档收集，
    /// 会落一条 "StockShop_<MerchantIdConstant>" 官方托管键。merchantID 用固定常量正是为此，
    /// 拼时间戳会让存档无界膨胀。
    /// </summary>
    internal sealed class RandomEventWanderingMerchant : RandomEventBase
    {
        private CharacterMainControl _merchant;
        private StockShop _shop;
        private bool _cleanedUp = true;
        private int _sceneBuildIndex;

        internal override RandomEventId Id { get { return RandomEventId.WanderingMerchant; } }

        internal override string DisplayName
        {
            get { return L10n.T("神秘商人路过", "Wandering Merchant"); }
        }

        internal override float DurationSeconds { get { return RandomEventsTuning.MerchantDurationSeconds; } }

        internal override float Weight { get { return RandomEventsTuning.WeightWanderingMerchant; } }

        internal override bool CanTrigger(RandomEventContext ctx)
        {
            try
            {
                ModBehaviour owner = RandomEventEffectHelpers.ResolveOwner(ctx);
                return owner != null && owner.HasRandomEventMerchantPreset();
            }
            catch (Exception)
            {
                return false;
            }
        }

        internal override bool OnTrigger(RandomEventContext ctx)
        {
            try
            {
                ModBehaviour owner = RandomEventEffectHelpers.ResolveOwner(ctx);
                CharacterMainControl player = RandomEventEffectHelpers.ResolveAlivePlayer();
                if (owner == null || player == null || ctx == null)
                {
                    return false;
                }

                Vector3 pos = SpawnPositionHelper.SnapToGround(
                    player.transform.position
                    + player.transform.forward * RandomEventsTuning.MerchantSpawnDistance);

                _merchant = null;
                _shop = null;
                _cleanedUp = false;
                _sceneBuildIndex = SceneManager.GetActiveScene().buildIndex;

                owner.SpawnRandomEventMerchant(
                    pos,
                    IsSpawnStillValid,
                    HandleMerchantSpawned,
                    HandleMerchantSpawnFailed);

                ctx.AnchorPosition = pos;
                owner.ShowRandomEventDirectionalBanner(DisplayName, pos);
                owner.PlayRandomEventModSound("lottery/tick.wav");
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(RandomEventsTuning.LogPrefix + "[ERROR] 神秘商人触发失败: " + e.Message);
                _cleanedUp = true;
                return false;
            }
        }

        private bool IsSpawnStillValid()
        {
            try
            {
                return !_cleanedUp && SceneManager.GetActiveScene().buildIndex == _sceneBuildIndex;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private void HandleMerchantSpawned(CharacterMainControl merchant, StockShop shop)
        {
            ModBehaviour owner = ModBehaviour.Instance;

            if (_cleanedUp)
            {
                if (owner != null)
                {
                    owner.DespawnRandomEventMerchant(merchant, shop);
                }
                return;
            }

            _merchant = merchant;
            _shop = shop;
        }

        private void HandleMerchantSpawnFailed()
        {
            ModBehaviour.DevLog(RandomEventsTuning.LogPrefix + "神秘商人生成失败，本次事件空转");
        }

        internal override void OnCleanup(RandomEventContext ctx, RandomEventEndReason reason)
        {
            _cleanedUp = true;

            try
            {
                ModBehaviour owner = RandomEventEffectHelpers.ResolveOwner(ctx);
                if (owner != null && (_merchant != null || _shop != null))
                {
                    owner.DespawnRandomEventMerchant(_merchant, _shop);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(RandomEventsTuning.LogPrefix + "[WARNING] 神秘商人清理失败: " + e.Message);
            }

            _merchant = null;
            _shop = null;

            RandomEventEffectHelpers.ClearScope(ctx, reason);
        }
    }
}
