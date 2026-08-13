using System;
using System.Collections.Generic;

namespace BossRush
{
    /// <summary>
    /// Mode G 直伤分类结果。
    /// </summary>
    public enum ModeGDirectDamageClass
    {
        /// <summary>不计分（环境/buff/间接/非玩家来源）</summary>
        NotScoreable,
        /// <summary>枪械直伤</summary>
        Gun,
        /// <summary>近战直伤</summary>
        Melee
    }

    /// <summary>
    /// Mode G 五条件纯函数直伤分类器（规格 §4/§20 guard 20）。
    /// 无状态、无分配、不 GetComponent。
    /// </summary>
    public static class ModeGDirectDamageClassifier
    {
        /// <summary>
        /// 五条件判定：
        /// 1. 目标是已登记 Boss（exact Health 引用身份，调用方预查）；
        /// 2. 伤害来源是当前玩家角色（引用身份）；
        /// 3. 非 buff/效果来源（isFromBuffOrEffect == false）；
        /// 4. damageValue &gt; 0；
        /// 5. damageType == normal（realDamage 等间接通道不计分）。
        /// 通过后按开火武装状态区分 Gun/Melee。
        /// </summary>
        public static ModeGDirectDamageClass Classify(
            bool isRegisteredBoss,
            CharacterMainControl player,
            DamageInfo info,
            bool gunArmedRecently)
        {
            // 条件 1
            if (!isRegisteredBoss) return ModeGDirectDamageClass.NotScoreable;
            // 条件 2
            if (player == null || !ReferenceEquals(info.fromCharacter, player)) return ModeGDirectDamageClass.NotScoreable;
            // 条件 3
            if (info.isFromBuffOrEffect) return ModeGDirectDamageClass.NotScoreable;
            // 条件 4
            if (info.damageValue <= 0f) return ModeGDirectDamageClass.NotScoreable;
            // 条件 5
            if (info.damageType != DamageTypes.normal) return ModeGDirectDamageClass.NotScoreable;

            return gunArmedRecently ? ModeGDirectDamageClass.Gun : ModeGDirectDamageClass.Melee;
        }
    }

    /// <summary>
    /// Mode G 战斗遥测（规格 §4/§17/§20 guard 20 重写版）。
    ///
    /// 硬约束：
    /// - 三 combat 委托（Health.OnHurt / ItemAgent_Gun.OnMainCharacterShootEvent /
    ///   LevelManager.OnControllingCharacterChanged）+ 独立 Health.OnDead run owner，
    ///   各自私有 owner bool，幂等订阅精确退订；
    /// - OnHurt handler 先 exact Health 字典 O(1) 早返、零分配、不 GetComponent；
    /// - 威胁公式：directThreat = Damage×BulletDamageMultiplier×CharacterDamageMultiplier；
    ///   explosionThreat = BulletExplosionDamage×ExplosionDamageMultiplier×CharacterDamageMultiplier×clamp(ShotCount,1,cap)；
    ///   弹药 Constants 真实 key：damageMultiplier/ExplosionRange/ExplosionDamage（Item.Constants.GetFloat）；
    /// - 预分配 32/32/64/3/3 缓存，波间 Clear 复用；
    /// - 禁 SetTargetBulletType/TakeOutAllBullets/禁弹 Harmony（Armed ban 全程仅 exact TargetBulletID 比较）。
    /// </summary>
    public sealed class ModeGCombatTelemetry
    {
        #region Constants（缓存硬限，§17）

        /// <summary>弹药威胁计数容量</summary>
        public const int AmmoCacheCapacity = 32;
        /// <summary>projectileThreatCountCap（爆炸 ShotCount 钳制上限）</summary>
        public const int ProjectileThreatCountCap = 32;
        /// <summary>weapon-family 缓存容量</summary>
        public const int WeaponFamilyCacheCapacity = 64;
        /// <summary>主 Boss 缓存容量</summary>
        public const int BossCacheCapacity = 3;
        /// <summary>已点名弹种容量</summary>
        public const int NamedAmmoCapacity = 3;
        /// <summary>completedNemesisKeys 容量</summary>
        public const int CompletedNemesisKeysCapacity = 128;

        // 弹药 Constants 真实 key（规格 §4.3）
        private const string ConstKey_DamageMultiplier = "damageMultiplier";
        private const string ConstKey_ExplosionRange = "ExplosionRange";
        private const string ConstKey_ExplosionDamage = "ExplosionDamage";

        // 距离轴分界（XZ 平方距离，不开方；§4.2）
        private const float BoundaryDistance = 13f;
        private const float ExtremeFarDistance = 18f;
        private const float ExtremeCloseDistance = 8f;
        private const float BoundarySq = BoundaryDistance * BoundaryDistance;
        private const float ExtremeFarSq = ExtremeFarDistance * ExtremeFarDistance;
        private const float ExtremeCloseSq = ExtremeCloseDistance * ExtremeCloseDistance;

        /// <summary>开火武装记忆窗口（秒）：最近一次开火后此窗口内伤害记为 Gun（owner tunable 0.5s）</summary>
        private const float GunArmedWindowSeconds = 0.5f;

        #endregion

        #region Preallocated Caches（Starting 一次分配，波间 Clear）

        // 32：弹药威胁累计（ammo TypeID -> threat）
        private readonly Dictionary<int, double> _ammoThreat = new Dictionary<int, double>(AmmoCacheCapacity);
        // 32：弹药开火计数（ammo TypeID -> shots）
        private readonly Dictionary<int, int> _ammoShotCount = new Dictionary<int, int>(AmmoCacheCapacity);
        // 64：weapon-family 开火计数（weapon TypeID -> shots）
        private readonly Dictionary<int, int> _weaponFamilyShots = new Dictionary<int, int>(WeaponFamilyCacheCapacity);
        // 3：per-Boss 直伤累计（exact Health -> damage）
        private readonly Dictionary<Health, float> _bossDirectDamage = new Dictionary<Health, float>(BossCacheCapacity);
        // 3：已点名弹种（Armed ban 历史）
        private readonly HashSet<int> _namedAmmo = new HashSet<int>();

        // gun agent -> 武器 TypeID（一次解析，避免热路径重复查询）
        private readonly Dictionary<ItemAgent_Gun, int> _gunWeaponTypeIdCache = new Dictionary<ItemAgent_Gun, int>(WeaponFamilyCacheCapacity);

        #endregion

        #region Run Binding

        private readonly ModeGRunState _state;
        private readonly Action<Health, DamageInfo> _onBossDeadCallback;

        // 四事件 owner bool（幂等订阅/精确退订）
        private bool _combatSubscribed;
        private bool _deadSubscribed;

        // 战斗事实
        private CharacterMainControl _playerCharacter;
        private float _lastGunShotTime = -1000f;
        private bool _contaminationByCharacterSwitch; // 控制角色变化：单向置位
        private int _armedBanAmmoTypeId;             // 当前波 Armed ban（exact TargetBulletID 比较）
        private float _combatStartAggregatePrimaryMaxHealth; // 波开始聚合主 Boss 最大生命和

        // 聚合计数
        private float _totalDirectDamage;
        private float _gunDirectDamage;
        private float _meleeDirectDamage;
        private int _totalShotCount;
        private int _closeHitCount;    // <=8m 命中
        private int _farHitCount;      // >=18m 命中
        private int _totalScoreableHits;
        private int _armedBanViolationCount;

        /// <summary>距离轴样本：战斗开始时的玩家-主 Boss XZ 平方距离累计</summary>
        private double _distanceSqSum;
        private int _distanceSampleCount;

        public ModeGCombatTelemetry(ModeGRunState state, Action<Health, DamageInfo> onBossDeadCallback)
        {
            _state = state;
            _onBossDeadCallback = onBossDeadCallback;
        }

        #endregion

        #region Subscription（幂等订阅 + 精确退订）

        /// <summary>
        /// 订阅三 combat 委托（幂等；owner bool 防重复）。
        /// </summary>
        public void SubscribeCombat()
        {
            if (_combatSubscribed) return;
            try
            {
                Health.OnHurt += HandleOnHurt;
                ItemAgent_Gun.OnMainCharacterShootEvent += HandleOnShoot;
                LevelManager.OnControllingCharacterChanged += HandleControllingCharacterChanged;
                _combatSubscribed = true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeG] [ERROR] combat 遥测订阅失败: " + e.Message);
            }
        }

        /// <summary>
        /// 订阅 OnDead（独立 run owner，Starting 首 await 前订阅；幂等）。
        /// </summary>
        public void SubscribeDead()
        {
            if (_deadSubscribed) return;
            try
            {
                Health.OnDead += HandleOnDead;
                _deadSubscribed = true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeG] [ERROR] OnDead 遥测订阅失败: " + e.Message);
            }
        }

        /// <summary>
        /// 精确退订三 combat 委托（按 owner）。
        /// </summary>
        public void UnsubscribeCombat()
        {
            if (!_combatSubscribed) return;
            try
            {
                Health.OnHurt -= HandleOnHurt;
                ItemAgent_Gun.OnMainCharacterShootEvent -= HandleOnShoot;
                LevelManager.OnControllingCharacterChanged -= HandleControllingCharacterChanged;
            }
            catch { /* no-throw */ }
            _combatSubscribed = false;
        }

        /// <summary>
        /// 精确退订 OnDead（独立 owner）。
        /// </summary>
        public void UnsubscribeDead()
        {
            if (!_deadSubscribed) return;
            try
            {
                Health.OnDead -= HandleOnDead;
            }
            catch { /* no-throw */ }
            _deadSubscribed = false;
        }

        #endregion

        #region Wave Lifecycle

        /// <summary>
        /// 波开始：绑定玩家角色、冻结聚合血量、清空波级缓存（复用容量）。
        /// </summary>
        public void BeginWave(CharacterMainControl player, float aggregatePrimaryMaxHealth)
        {
            _playerCharacter = player;
            _combatStartAggregatePrimaryMaxHealth = aggregatePrimaryMaxHealth;
            _contaminationByCharacterSwitch = false;
            _armedBanAmmoTypeId = 0;
            ClearWaveCaches();
        }

        /// <summary>
        /// 波间 Clear（预分配缓存复用，不重新分配）。
        /// </summary>
        public void ClearWaveCaches()
        {
            _ammoThreat.Clear();
            _ammoShotCount.Clear();
            _weaponFamilyShots.Clear();
            _bossDirectDamage.Clear();
            // _namedAmmo 跨波保留（已点名历史，上限 NamedAmmoCapacity）
            _totalDirectDamage = 0f;
            _gunDirectDamage = 0f;
            _meleeDirectDamage = 0f;
            _totalShotCount = 0;
            _closeHitCount = 0;
            _farHitCount = 0;
            _totalScoreableHits = 0;
            _armedBanViolationCount = 0;
            _distanceSqSum = 0.0;
            _distanceSampleCount = 0;
        }

        /// <summary>
        /// 武装当前波弹药禁令（exact TargetBulletID 比较，禁 SetTargetBulletType）。
        /// </summary>
        public void ArmAmmoBan(int ammoTypeId)
        {
            _armedBanAmmoTypeId = ammoTypeId;
            if (ammoTypeId > 0 && _namedAmmo.Count < NamedAmmoCapacity)
            {
                _namedAmmo.Add(ammoTypeId);
            }
        }

        public int ArmedBanAmmoTypeId { get { return _armedBanAmmoTypeId; } }

        #endregion

        #region Event Handlers（热路径：零分配/O(1) 早返）

        private void HandleOnHurt(Health health, DamageInfo info)
        {
            try
            {
                // 快路径：未激活/非登记 Boss 立即早返（exact Health 字典 O(1)）
                if (_state == null || !_state.IsCombatActive) return;
                if (health == null) return;
                if (!_state.IsRegisteredBossHealth(health)) return;

                bool gunArmed = (UnityEngine.Time.time - _lastGunShotTime) <= GunArmedWindowSeconds;
                ModeGDirectDamageClass cls = ModeGDirectDamageClassifier.Classify(
                    true, _playerCharacter, info, gunArmed);
                if (cls == ModeGDirectDamageClass.NotScoreable) return;

                float amount = info.damageValue;
                _totalDirectDamage += amount;
                _totalScoreableHits++;
                if (cls == ModeGDirectDamageClass.Gun) _gunDirectDamage += amount;
                else _meleeDirectDamage += amount;

                // per-Boss 累计（容量有界，overflow 关分不猜结果）
                if (_bossDirectDamage.Count < BossCacheCapacity || _bossDirectDamage.ContainsKey(health))
                {
                    float prev;
                    _bossDirectDamage.TryGetValue(health, out prev);
                    _bossDirectDamage[health] = prev + amount;
                }

                // 距离轴：XZ 平方距离不开方
                if (_playerCharacter != null)
                {
                    UnityEngine.Vector3 a = info.damagePoint;
                    UnityEngine.Vector3 b = _playerCharacter.transform.position;
                    float dx = a.x - b.x;
                    float dz = a.z - b.z;
                    float sq = dx * dx + dz * dz;
                    _distanceSqSum += sq;
                    _distanceSampleCount++;
                    if (sq <= ExtremeCloseSq) _closeHitCount++;
                    else if (sq >= ExtremeFarSq) _farHitCount++;
                }
            }
            catch
            {
                // 热路径无噪声日志
            }
        }

        private void HandleOnShoot(ItemAgent_Gun gun)
        {
            try
            {
                if (_state == null || !_state.IsCombatActive) return;
                if (gun == null) return;

                _lastGunShotTime = UnityEngine.Time.time;
                _totalShotCount++;

                // 武器 family 计数（一次解析缓存，overflow 关分）
                int weaponTypeId;
                if (!_gunWeaponTypeIdCache.TryGetValue(gun, out weaponTypeId))
                {
                    weaponTypeId = ResolveWeaponTypeId(gun);
                    if (_gunWeaponTypeIdCache.Count < WeaponFamilyCacheCapacity)
                    {
                        _gunWeaponTypeIdCache[gun] = weaponTypeId;
                    }
                }
                if (weaponTypeId > 0 &&
                    (_weaponFamilyShots.Count < WeaponFamilyCacheCapacity || _weaponFamilyShots.ContainsKey(weaponTypeId)))
                {
                    int shots;
                    _weaponFamilyShots.TryGetValue(weaponTypeId, out shots);
                    _weaponFamilyShots[weaponTypeId] = shots + 1;
                }

                // 弹药采样与威胁公式
                ItemStatsSystem.Item bullet = gun.BulletItem;
                int ammoTypeId = bullet != null ? bullet.TypeID : 0;
                if (ammoTypeId > 0)
                {
                    // Armed ban：仅 exact TargetBulletID 比较（禁 SetTargetBulletType）
                    if (_armedBanAmmoTypeId != 0 && ammoTypeId == _armedBanAmmoTypeId)
                    {
                        _armedBanViolationCount++;
                    }

                    if (_ammoShotCount.Count < AmmoCacheCapacity || _ammoShotCount.ContainsKey(ammoTypeId))
                    {
                        int shots;
                        _ammoShotCount.TryGetValue(ammoTypeId, out shots);
                        _ammoShotCount[ammoTypeId] = shots + 1;

                        // directThreat = Damage×BulletDamageMultiplier×CharacterDamageMultiplier
                        float bulletDamage = 0f;
                        float explosionDamageConst = 0f;
                        try
                        {
                            bulletDamage = bullet.Constants.GetFloat(ConstKey_DamageMultiplier, 0f);
                            explosionDamageConst = bullet.Constants.GetFloat(ConstKey_ExplosionDamage, 0f);
                            // ExplosionRange 供距离轴/呈现参考（读取但不参与威胁分）
                            bullet.Constants.GetFloat(ConstKey_ExplosionRange, 0f);
                        }
                        catch { /* 常量读取失败不影响计分 */ }

                        double directThreat = bulletDamage * gun.BulletDamageMultiplier * gun.CharacterDamageMultiplier;
                        // explosionThreat = BulletExplosionDamage×ExplosionDamageMultiplier×CharacterDamageMultiplier×clamp(ShotCount,1,cap)
                        int clampedShots = gun.ShotCount;
                        if (clampedShots < 1) clampedShots = 1;
                        if (clampedShots > ProjectileThreatCountCap) clampedShots = ProjectileThreatCountCap;
                        double explosionThreat = explosionDamageConst * gun.ExplosionDamageMultiplier
                            * gun.CharacterDamageMultiplier * clampedShots;

                        double prev;
                        _ammoThreat.TryGetValue(ammoTypeId, out prev);
                        _ammoThreat[ammoTypeId] = prev + directThreat + explosionThreat;
                    }
                }
            }
            catch
            {
                // 热路径无噪声日志
            }
        }

        private void HandleControllingCharacterChanged(CharacterMainControl newMain)
        {
            try
            {
                // 单向置 contamination（切换控制角色期间采样不计新角色数据）
                _contaminationByCharacterSwitch = true;
                if (newMain != null) _playerCharacter = newMain;
            }
            catch { /* no-throw */ }
        }

        private void HandleOnDead(Health health, DamageInfo info)
        {
            try
            {
                // run owner：只处理已登记 Boss；玩家死亡路由在 ModeGDeathRouting（独立订阅）
                if (_state == null || health == null) return;
                if (!_state.IsRegisteredBossHealth(health)) return;
                if (_onBossDeadCallback != null) _onBossDeadCallback(health, info);
            }
            catch { /* no-throw */ }
        }

        private static int ResolveWeaponTypeId(ItemAgent_Gun gun)
        {
            try
            {
                // ItemAgent.item 为武器 Item 实例（官方标准引用）
                ItemStatsSystem.Item weapon = gun.Item;
                if (weapon != null) return weapon.TypeID;
            }
            catch { }
            return 0;
        }

        #endregion

        #region Aggregates（波末/终局读取）

        public float TotalDirectDamage { get { return _totalDirectDamage; } }
        public float GunDirectDamage { get { return _gunDirectDamage; } }
        public float MeleeDirectDamage { get { return _meleeDirectDamage; } }
        public int TotalShotCount { get { return _totalShotCount; } }
        public int CloseHitCount { get { return _closeHitCount; } }
        public int FarHitCount { get { return _farHitCount; } }
        public int TotalScoreableHits { get { return _totalScoreableHits; } }
        public int ArmedBanViolationCount { get { return _armedBanViolationCount; } }
        public bool ContaminatedByCharacterSwitch { get { return _contaminationByCharacterSwitch; } }
        public float CombatStartAggregatePrimaryMaxHealth { get { return _combatStartAggregatePrimaryMaxHealth; } }

        /// <summary>
        /// 枪械直伤占比（0..1；无样本返回 0）。
        /// </summary>
        public float GunDamageShare
        {
            get { return _totalDirectDamage > 0f ? _gunDirectDamage / _totalDirectDamage : 0f; }
        }

        /// <summary>
        /// 近战直伤占比（0..1；无样本返回 0）。
        /// </summary>
        public float MeleeDamageShare
        {
            get { return _totalDirectDamage > 0f ? _meleeDirectDamage / _totalDirectDamage : 0f; }
        }

        /// <summary>
        /// 极端远距命中占比（>=18m；样本为计分命中）。
        /// </summary>
        public float FarExtremeShare
        {
            get { return _totalScoreableHits > 0 ? _farHitCount / (float)_totalScoreableHits : 0f; }
        }

        /// <summary>
        /// 极端近距命中占比（<=8m；样本为计分命中）。
        /// </summary>
        public float CloseExtremeShare
        {
            get { return _totalScoreableHits > 0 ? _closeHitCount / (float)_totalScoreableHits : 0f; }
        }

        /// <summary>
        /// 平均交战距离（米，开方一次仅在读取时）。
        /// </summary>
        public float AverageEngagementDistance
        {
            get
            {
                if (_distanceSampleCount <= 0) return 0f;
                return (float)Math.Sqrt(_distanceSqSum / _distanceSampleCount);
            }
        }

        /// <summary>
        /// 弹药威胁表只读视图（弹药轴推断用）。
        /// </summary>
        public IReadOnlyDictionary<int, double> AmmoThreatTable { get { return _ammoThreat; } }

        /// <summary>
        /// 弹药开火计数表只读视图（样本 >=5 判定用）。
        /// </summary>
        public IReadOnlyDictionary<int, int> AmmoShotCountTable { get { return _ammoShotCount; } }

        /// <summary>
        /// 已点名弹种只读视图。
        /// </summary>
        public IEnumerable<int> NamedAmmoTypeIds { get { return _namedAmmo; } }

        /// <summary>
        /// 某 Boss 的直伤贡献占聚合主 Boss 最大血量的比例（0..1+）。
        /// </summary>
        public float GetBossDamageContribution(Health health)
        {
            if (health == null || _combatStartAggregatePrimaryMaxHealth <= 0f) return 0f;
            float dmg;
            if (!_bossDirectDamage.TryGetValue(health, out dmg)) return 0f;
            return dmg / _combatStartAggregatePrimaryMaxHealth;
        }

        /// <summary>
        /// 总样本数（弹药轴样本 >=5 判定）。
        /// </summary>
        public int TotalAmmoSamples { get { return _totalShotCount; } }

        #endregion

        #region Pending Achievement Reports（host destroy 时 token CAS 同步消费）

        private struct PendingAchievementReport
        {
            public int token;
            public string bossType;
            public bool wasFlawlessAtDeath;
        }

        private static readonly object _reportLock = new object();
        private static readonly List<PendingAchievementReport> _pendingReports = new List<PendingAchievementReport>();

        /// <summary>
        /// 入队击杀成就上报（纯数据窄去重由 Achievement 侧按 token/bossType/wasFlawlessAtDeath 完成）。
        /// </summary>
        public static void EnqueueAchievementReport(int token, string bossType, bool wasFlawlessAtDeath)
        {
            lock (_reportLock)
            {
                _pendingReports.Add(new PendingAchievementReport
                {
                    token = token,
                    bossType = bossType ?? string.Empty,
                    wasFlawlessAtDeath = wasFlawlessAtDeath
                });
            }
        }

        /// <summary>
        /// PrepareHostDestroy token CAS 消费点：同步上报全部未 settled report（幂等）。
        /// </summary>
        public static void ConsumePendingAchievementReports(ModeGRunState state)
        {
            try
            {
                List<PendingAchievementReport> drained;
                lock (_reportLock)
                {
                    if (_pendingReports.Count == 0) return;
                    drained = new List<PendingAchievementReport>(_pendingReports);
                    _pendingReports.Clear();
                }

                ModBehaviour host = ModBehaviour.Instance;
                if (host == null) return;
                for (int i = 0; i < drained.Count; i++)
                {
                    PendingAchievementReport r = drained[i];
                    try
                    {
                        host.ReportModeGBossKillAchievement(r.token, r.bossType, r.wasFlawlessAtDeath);
                    }
                    catch { /* no-throw */ }
                }
            }
            catch { /* no-throw 契约 */ }
        }

        /// <summary>
        /// 丢弃全部未 settled report（仅测试/故障路径）。
        /// </summary>
        public static void DropPendingAchievementReports()
        {
            lock (_reportLock)
            {
                _pendingReports.Clear();
            }
        }

        #endregion
    }
}
