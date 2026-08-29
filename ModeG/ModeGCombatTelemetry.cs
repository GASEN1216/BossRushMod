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
    /// 只屏蔽 Mode G 遥测的同步 exact-Health 范围。
    /// 不改写 DamageInfo，也不影响伤害、死亡归因、HitMarker 或其它模式。
    /// </summary>
    public static class ModeGTelemetrySuppressionScope
    {
        [ThreadStatic]
        private static Health _suppressedHealth;

        [ThreadStatic]
        private static int _depth;

        public struct Token : IDisposable
        {
            private readonly Health _previousHealth;
            private readonly int _previousDepth;

            internal Token(Health previousHealth, int previousDepth)
            {
                _previousHealth = previousHealth;
                _previousDepth = previousDepth;
            }

            public void Dispose()
            {
                _suppressedHealth = _previousHealth;
                _depth = _previousDepth;
            }
        }

        public static Token Enter(Health health)
        {
            Token token = new Token(_suppressedHealth, _depth);
            _suppressedHealth = health;
            _depth++;
            return token;
        }

        public static bool IsActiveFor(Health health)
        {
            return _depth > 0 && health != null && ReferenceEquals(_suppressedHealth, health);
        }
    }

    /// <summary>
    /// Mode G 直伤分类器（规格 §4/§20 guard 20）。
    /// 无状态、无分配、不 GetComponent。
    /// </summary>
    public static class ModeGDirectDamageClassifier
    {
        /// <summary>
        /// 判定条件：
        /// 1. 目标是已登记 Boss（exact Health 引用身份，调用方预查）；
        /// 2. 伤害来源是当前玩家角色（引用身份）；
        /// 3. 非 buff/效果来源（isFromBuffOrEffect == false）；
        /// 4. damageValue &gt; 0；
        /// 5. damageType == normal（realDamage 等间接通道不计分）；
        /// 6. 非 exact telemetry suppression；
        /// 7. fromWeaponItemID &gt; 0 且 metadata 明确为 Gun/Melee。
        /// </summary>
        public static ModeGDirectDamageClass Classify(
            bool isRegisteredBoss,
            CharacterMainControl player,
            DamageInfo info,
            bool sourceContaminated,
            bool exactSuppressionActive,
            ModeGDirectDamageClass metadataFamily)
        {
            // 条件 1
            if (!isRegisteredBoss) return ModeGDirectDamageClass.NotScoreable;
            // 条件 2
            if (player == null || !ReferenceEquals(info.fromCharacter, player)) return ModeGDirectDamageClass.NotScoreable;
            // 条件 3
            if (info.isFromBuffOrEffect) return ModeGDirectDamageClass.NotScoreable;
            // 条件 4
            if (info.damageValue <= 0f) return ModeGDirectDamageClass.NotScoreable;
            if (float.IsNaN(info.damageValue) || float.IsInfinity(info.damageValue))
                return ModeGDirectDamageClass.NotScoreable;
            // 条件 5
            if (info.damageType != DamageTypes.normal) return ModeGDirectDamageClass.NotScoreable;
            // 条件 6
            if (sourceContaminated || exactSuppressionActive)
                return ModeGDirectDamageClass.NotScoreable;
            // 条件 7
            if (info.fromWeaponItemID <= 0) return ModeGDirectDamageClass.NotScoreable;
            if (metadataFamily != ModeGDirectDamageClass.Gun
                && metadataFamily != ModeGDirectDamageClass.Melee)
                return ModeGDirectDamageClass.NotScoreable;

            return metadataFamily;
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

        #endregion

        #region Preallocated Caches（Starting 一次分配，波间 Clear）

        // 32：弹药威胁累计（ammo TypeID -> threat）
        private readonly Dictionary<int, double> _ammoThreat = new Dictionary<int, double>(AmmoCacheCapacity);
        // 32：弹药开火计数（ammo TypeID -> shots）
        private readonly Dictionary<int, int> _ammoShotCount = new Dictionary<int, int>(AmmoCacheCapacity);
        // 64：weapon TypeID -> metadata family（Unknown 也缓存为 NotScoreable）
        private readonly Dictionary<int, ModeGDirectDamageClass> _weaponFamilyCache
            = new Dictionary<int, ModeGDirectDamageClass>(WeaponFamilyCacheCapacity);
        // 32：ammo TypeID -> 纯数据 profile；不持有 Item/Prefab 引用
        private readonly Dictionary<int, BulletThreatProfile> _ammoThreatProfileCache
            = new Dictionary<int, BulletThreatProfile>(AmmoCacheCapacity);
        // 3：per-Boss 直伤累计（exact Health -> damage）
        private readonly Dictionary<Health, float> _bossDirectDamage = new Dictionary<Health, float>(BossCacheCapacity);
        // 3：已点名弹种（Armed ban 历史）
        private readonly HashSet<int> _namedAmmo = new HashSet<int>();

        #endregion

        #region Run Binding

        private readonly ModeGRunState _state;
        private readonly Action<Health, DamageInfo> _onBossDeadCallback;

        // 四事件 owner bool（幂等订阅/精确退订）
        private bool _combatSubscribed;
        private bool _deadSubscribed;

        // 战斗事实
        private CharacterMainControl _playerCharacter;
        private bool _contaminationByCharacterSwitch; // 控制角色变化：单向置位
        private int _armedBanAmmoTypeId;             // 当前波 Armed ban（exact TargetBulletID 比较）
        private int _shotSequence;                    // run-scoped 成功开火序号（Calm/Spawning guard）
        private bool _ammoSampleValid = true;         // 生成期预射后单向关闭本波学习样本
        private float _combatStartAggregatePrimaryMaxHealth; // 波开始聚合主 Boss 最大生命和

        // 聚合计数
        private float _totalDirectDamage;
        private float _gunDirectDamage;
        private float _meleeDirectDamage;
        private float _closeExtremeDirectDamage;
        private float _farExtremeDirectDamage;
        private int _totalShotCount;
        private int _armedBanViolationCount;

        // 有界缓存溢出降级标记（§15：telemetry overflow 必须显示「本波挑战无效」，不显示假进度）
        /// <summary>run 级降级：weapon-family / 弹药 profile 缓存溢出后本局不再恢复</summary>
        private bool _runTelemetryDegraded;
        /// <summary>波级降级：本波弹药计数缓存溢出，随 ClearWaveCaches 复位</summary>
        private bool _waveTelemetryDegraded;

        private struct BulletThreatProfile
        {
            public bool valid;
            public float damageMultiplier;
            public float explosionDamage;
        }

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
            int preCombatBanViolations = _armedBanAmmoTypeId > 0
                ? _armedBanViolationCount
                : 0;
            _playerCharacter = player;
            _combatStartAggregatePrimaryMaxHealth = aggregatePrimaryMaxHealth;
            _contaminationByCharacterSwitch = false;
            _ammoSampleValid = true;
            ClearWaveCaches();
            _armedBanViolationCount = preCombatBanViolations;
        }

        /// <summary>
        /// 波间 Clear（预分配缓存复用，不重新分配）。
        /// </summary>
        public void ClearWaveCaches()
        {
            _ammoThreat.Clear();
            _ammoShotCount.Clear();
            _bossDirectDamage.Clear();
            // _namedAmmo 跨波保留（已点名历史，上限 NamedAmmoCapacity）
            _totalDirectDamage = 0f;
            _gunDirectDamage = 0f;
            _meleeDirectDamage = 0f;
            _closeExtremeDirectDamage = 0f;
            _farExtremeDirectDamage = 0f;
            _totalShotCount = 0;
            _armedBanViolationCount = 0;
            _waveTelemetryDegraded = false;
        }

        /// <summary>
        /// 武装当前波弹药禁令（exact TargetBulletID 比较，禁 SetTargetBulletType）。
        /// </summary>
        public void ArmAmmoBan(int ammoTypeId)
        {
            if (_armedBanAmmoTypeId != ammoTypeId)
            {
                _armedBanViolationCount = 0;
            }
            _armedBanAmmoTypeId = ammoTypeId;
            if (ammoTypeId > 0 && _namedAmmo.Count < NamedAmmoCapacity)
            {
                _namedAmmo.Add(ammoTypeId);
            }
        }

        public int ArmedBanAmmoTypeId { get { return _armedBanAmmoTypeId; } }

        public int ShotSequence { get { return _shotSequence; } }

        public bool IsAmmoSampleValid { get { return _ammoSampleValid; } }

        public void InvalidateAmmoSample()
        {
            _ammoSampleValid = false;
        }

        public void DisarmAmmoBan()
        {
            _armedBanAmmoTypeId = 0;
        }

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

                ModeGDirectDamageClass family = ResolveWeaponFamily(info.fromWeaponItemID, false);
                ModeGDirectDamageClass cls = ModeGDirectDamageClassifier.Classify(
                    true,
                    _playerCharacter,
                    info,
                    _contaminationByCharacterSwitch,
                    ModeGTelemetrySuppressionScope.IsActiveFor(health),
                    family);
                if (cls == ModeGDirectDamageClass.NotScoreable) return;

                float amount = info.damageValue;
                _totalDirectDamage += amount;
                if (cls == ModeGDirectDamageClass.Gun) _gunDirectDamage += amount;
                else _meleeDirectDamage += amount;

                // per-Boss 累计（容量有界，overflow 关分不猜结果）
                if (_bossDirectDamage.Count < BossCacheCapacity || _bossDirectDamage.ContainsKey(health))
                {
                    float prev;
                    _bossDirectDamage.TryGetValue(health, out prev);
                    _bossDirectDamage[health] = prev + amount;
                }

                // 距离轴：XZ 平方距离不开方；只累计目标极端带伤害（中距离只进分母）
                if (_playerCharacter != null)
                {
                    UnityEngine.Vector3 a = info.damagePoint;
                    UnityEngine.Vector3 b = _playerCharacter.transform.position;
                    float dx = a.x - b.x;
                    float dz = a.z - b.z;
                    float sq = dx * dx + dz * dz;
                    if (sq <= ExtremeCloseSq) _closeExtremeDirectDamage += amount;
                    else if (sq >= ExtremeFarSq) _farExtremeDirectDamage += amount;
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
                if (_state == null || !_state.IsActive) return;
                if (gun == null) return;

                ItemSetting_Gun gunSetting = gun.GunItemSetting;
                int ammoTypeId = gunSetting != null ? gunSetting.TargetBulletID : 0;
                _shotSequence++;
                if (ammoTypeId > 0)
                {
                    // Armed ban：仅 exact TargetBulletID 比较（禁 SetTargetBulletType）
                    if (_armedBanAmmoTypeId != 0 && ammoTypeId == _armedBanAmmoTypeId)
                    {
                        _armedBanViolationCount++;
                    }

                    // 只在第 2/5/8 波（0-based 1/4/7）Fighting/LastStand
                    // 记录下一宿敌波要消费的弹药样本。
                    bool samplingWave = _state.waveEpoch == 1
                        || _state.waveEpoch == 4
                        || _state.waveEpoch == 7;
                    if (!samplingWave
                        || !_ammoSampleValid
                        || (_state.combatPhase != ModeGCombatPhase.Fighting
                            && _state.combatPhase != ModeGCombatPhase.LastStand)) return;

                    BulletThreatProfile profile;
                    if (!TryGetBulletThreatProfile(ammoTypeId, out profile)) return;

                    int clampedProjectiles = gun.ShotCount;
                    if (clampedProjectiles < 1 || clampedProjectiles > ProjectileThreatCountCap) return;
                    double characterMultiplier = gun.CharacterDamageMultiplier;
                    double directThreat = gun.Damage * profile.damageMultiplier * characterMultiplier;
                    double explosionThreat = profile.explosionDamage * gun.ExplosionDamageMultiplier
                        * characterMultiplier * clampedProjectiles;
                    double rawThreat = directThreat + explosionThreat;
                    // 单次开火 clamp 到冻结的 10% 聚合血量（§4.3；常量由 AdaptiveCombat 单点持有）
                    double perShotCap = (double)_combatStartAggregatePrimaryMaxHealth
                        * ModeGAdaptiveCombat.AmmoBanClampShare;
                    if (double.IsNaN(rawThreat) || double.IsInfinity(rawThreat)
                        || rawThreat < 0.0 || perShotCap <= 0.0) return;
                    if (rawThreat > perShotCap) rawThreat = perShotCap;

                    if (_ammoShotCount.Count < AmmoCacheCapacity || _ammoShotCount.ContainsKey(ammoTypeId))
                    {
                        _totalShotCount++;
                        int shots;
                        _ammoShotCount.TryGetValue(ammoTypeId, out shots);
                        _ammoShotCount[ammoTypeId] = shots + 1;

                        double prev;
                        _ammoThreat.TryGetValue(ammoTypeId, out prev);
                        _ammoThreat[ammoTypeId] = prev + rawThreat;
                    }
                    else
                    {
                        // 第 33 个弹种：本波样本不再完整，关分而不是猜结果
                        _waveTelemetryDegraded = true;
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

        private ModeGDirectDamageClass ResolveWeaponFamily(int weaponTypeId, bool terminalCredit)
        {
            if (weaponTypeId <= 0) return ModeGDirectDamageClass.NotScoreable;

            ModeGWeaponScoringEntry matrixEntry;
            if (ModeGWeaponScoringCompatibilityMatrix.TryGetEntryByTypeId(weaponTypeId, out matrixEntry))
            {
                if (terminalCredit ? !matrixEntry.TerminalCreditAllowed : !matrixEntry.NormalAttackScoreable)
                    return ModeGDirectDamageClass.NotScoreable;
                if (matrixEntry.NormalAttackFamily == WeaponFamily.Gun) return ModeGDirectDamageClass.Gun;
                if (matrixEntry.NormalAttackFamily == WeaponFamily.Melee) return ModeGDirectDamageClass.Melee;
                return ModeGDirectDamageClass.NotScoreable;
            }

            ModeGDirectDamageClass cached;
            if (_weaponFamilyCache.TryGetValue(weaponTypeId, out cached)) return cached;
            if (_weaponFamilyCache.Count >= WeaponFamilyCacheCapacity)
            {
                // 第 65 个武器 ID：后续新 ID 一律不计分，本局标记降级
                _runTelemetryDegraded = true;
                return ModeGDirectDamageClass.NotScoreable;
            }

            ModeGDirectDamageClass resolved = ModeGDirectDamageClass.NotScoreable;
            try
            {
                ItemStatsSystem.ItemMetaData metaData = ItemStatsSystem.ItemAssetsCollection.GetMetaData(weaponTypeId);
                bool gun = false;
                bool melee = false;
                if (metaData.id > 0 && metaData.tags != null)
                {
                    for (int i = 0; i < metaData.tags.Length; i++)
                    {
                        Duckov.Utilities.Tag tag = metaData.tags[i];
                        if (tag == null) continue;
                        if (string.Equals(tag.name, "Gun", StringComparison.Ordinal)) gun = true;
                        else if (string.Equals(tag.name, "MeleeWeapon", StringComparison.Ordinal)
                            || string.Equals(tag.name, "Melee", StringComparison.Ordinal)) melee = true;
                    }
                }
                if (gun != melee)
                    resolved = gun ? ModeGDirectDamageClass.Gun : ModeGDirectDamageClass.Melee;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeG] 武器 family 元数据解析失败 typeId="
                    + weaponTypeId + ": " + e.Message);
            }
            _weaponFamilyCache[weaponTypeId] = resolved;
            return resolved;
        }

        private bool TryGetBulletThreatProfile(int ammoTypeId, out BulletThreatProfile profile)
        {
            if (_ammoThreatProfileCache.TryGetValue(ammoTypeId, out profile)) return profile.valid;
            profile = default(BulletThreatProfile);
            if (ammoTypeId <= 0) return false;
            if (_ammoThreatProfileCache.Count >= AmmoCacheCapacity)
            {
                // 第 33 个弹种 profile：关闭后续弹药分，本局标记降级
                _runTelemetryDegraded = true;
                return false;
            }
            try
            {
                ItemStatsSystem.Item prefab = ItemStatsSystem.ItemAssetsCollection.GetPrefab(ammoTypeId);
                if (prefab != null && prefab.TypeID == ammoTypeId && prefab.Constants != null)
                {
                    float damageMultiplier = prefab.Constants.GetFloat(ConstKey_DamageMultiplier, float.NaN);
                    float explosionDamage = prefab.Constants.GetFloat(ConstKey_ExplosionDamage, float.NaN);
                    float explosionRange = prefab.Constants.GetFloat(ConstKey_ExplosionRange, float.NaN);
                    if (!float.IsNaN(damageMultiplier) && !float.IsInfinity(damageMultiplier)
                        && !float.IsNaN(explosionDamage) && !float.IsInfinity(explosionDamage)
                        && !float.IsNaN(explosionRange) && !float.IsInfinity(explosionRange)
                        && damageMultiplier >= 0f && explosionDamage >= 0f && explosionRange >= 0f)
                    {
                        profile.valid = true;
                        profile.damageMultiplier = damageMultiplier;
                        profile.explosionDamage = explosionDamage;
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeG] 弹药威胁元数据解析失败 typeId="
                    + ammoTypeId + ": " + e.Message);
            }
            _ammoThreatProfileCache[ammoTypeId] = profile;
            return profile.valid;
        }

        #endregion

        #region Aggregates（波末/终局读取）

        public float TotalDirectDamage { get { return _totalDirectDamage; } }
        public float GunDirectDamage { get { return _gunDirectDamage; } }
        public float MeleeDirectDamage { get { return _meleeDirectDamage; } }
        public float CloseExtremeDirectDamage { get { return _closeExtremeDirectDamage; } }
        public float FarExtremeDirectDamage { get { return _farExtremeDirectDamage; } }
        public int ArmedBanViolationCount { get { return _armedBanViolationCount; } }

        /// <summary>
        /// 有界缓存溢出降级（§15）：为 true 时本波挑战无效，HUD 不得显示可得分进度。
        /// </summary>
        public bool IsTelemetryDegraded
        {
            get { return _runTelemetryDegraded || _waveTelemetryDegraded; }
        }
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

        public float CloseExtremeDamageShare
        {
            get { return _totalDirectDamage > 0f ? _closeExtremeDirectDamage / _totalDirectDamage : 0f; }
        }

        public float FarExtremeDamageShare
        {
            get { return _totalDirectDamage > 0f ? _farExtremeDirectDamage / _totalDirectDamage : 0f; }
        }

        public ModeGDirectDamageClass ClassifyTerminalDamage(Health health, DamageInfo info)
        {
            ModeGDirectDamageClass family = ResolveWeaponFamily(info.fromWeaponItemID, true);
            return ModeGDirectDamageClassifier.Classify(
                true,
                _playerCharacter,
                info,
                _contaminationByCharacterSwitch,
                ModeGTelemetrySuppressionScope.IsActiveFor(health),
                family);
        }

        public ModeGDistanceVerdict ClassifyTerminalDistance(Health health, DamageInfo info)
        {
            if (ClassifyTerminalDamage(health, info) == ModeGDirectDamageClass.NotScoreable
                || _playerCharacter == null) return ModeGDistanceVerdict.None;
            UnityEngine.Vector3 a = info.damagePoint;
            UnityEngine.Vector3 b = _playerCharacter.transform.position;
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return dx * dx + dz * dz <= BoundarySq
                ? ModeGDistanceVerdict.Close
                : ModeGDistanceVerdict.Far;
        }

        /// <summary>
        /// 弹药威胁表只读视图（弹药轴推断用）。
        /// </summary>
        public IReadOnlyDictionary<int, double> AmmoThreatTable { get { return _ammoThreat; } }

        /// <summary>
        /// 弹药开火计数表只读视图（样本 >=5 判定用）。
        /// </summary>
        public IReadOnlyDictionary<int, int> AmmoShotCountTable { get { return _ammoShotCount; } }

        public bool WasAmmoNamed(int ammoTypeId)
        {
            return ammoTypeId > 0 && _namedAmmo.Contains(ammoTypeId);
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
