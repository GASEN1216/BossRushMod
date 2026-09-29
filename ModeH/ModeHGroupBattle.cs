// ============================================================================
// ModeHGroupBattle.cs - 鸭王杯群战：一场的战斗 owner（2026-09-29 owner 改版）
// ============================================================================
// 两边各一群 Boss 同时上场互殴：左边 Teams.scav（玩家押的这边），右边 Teams.wolf。
//   - 胜负：一边全倒另一边赢；同时全倒按玩家输（庄家赢平局）；到时按「存活者战力 × 剩余血量比例」之和比大小；
//   - 索敌：原生行为树只会找视野里的人，双方隔着擂台会干站，这里按 0.5 秒节流给没目标的补最近的对手，
//     离得远就发一次 MoveToPos 走近（同单挑版 WakeArenaOpponent / NudgeArenaApproach 的口径）；
//   - 拍铃：每场一次的全局天灾，随机一种，对两边一视同仁（陨星雨、导弹轰炸、全员加速、全员狂暴、回春、寒潮）。
// 官方 Boss 的实例归 ModeHSpawnTransaction 回收；三只自定义 Boss 走托管生成，实例与随从由本类回收。
// 每帧成本：O(两边人数)，零分配；索敌 O(n×m) 按 0.5 秒节流。
// ============================================================================

using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Duckov.UI;
using TMPro;
using UnityEngine;

namespace BossRush
{
    /// <summary>场上的一名选手。</summary>
    internal sealed class ModeHGroupUnit
    {
        public ModeHGroupEntry Entry;
        /// <summary>右边（敌方）为 true。</summary>
        public bool IsEnemy;
        public CharacterMainControl Character;
        public Health Health;
        /// <summary>自定义 Boss 的托管 handle；官方 Boss 为 null（实例归生成事务）。</summary>
        public ManagedBossRuntimeHandle ManagedHandle;
        /// <summary>托管实例登记进死亡抑制表的 preset（回收时先解登记）。</summary>
        public CharacterRandomPreset SuppressedPreset;
        public ModeHParticipantRef Participant;
        public Vector3 SpawnPosition;
        public bool Down;
        public float InactiveSeconds;
        public float NextApproachTime;
    }

    /// <summary>拍铃天灾种类。</summary>
    internal enum ModeHGroupBellEffect
    {
        None = 0,
        MeteorShower = 1,
        MissileStrike = 2,
        Haste = 3,
        Berserk = 4,
        Rejuvenate = 5,
        FrostWave = 6,
    }

    internal sealed class ModeHGroupBattle : IModeHTelemetrySink
    {
        #region 常量

        private const float RetargetIntervalSeconds = 0.5f;
        private const float ApproachRepathSeconds = 1.5f;
        private const float DepartedInactiveGraceSeconds = 3f;

        private const float MeteorDuration = 8f;
        private const float MeteorInterval = 0.4f;
        private const float MeteorRadius = 3.5f;
        private const float MeteorDamageFraction = 0.07f;
        private const float MissileDuration = 4.5f;
        private const float MissileWaveInterval = 1.5f;
        private const int MissilesPerWave = 5;
        private const float MissileRadius = 2.5f;
        private const float MissileDamageFraction = 0.12f;
        private const float BuffDuration = 15f;
        private const float HastePercent = 0.5f;
        private const float BerserkPercent = 0.5f;
        private const float FrostDuration = 12f;
        private const float FrostPercent = -0.4f;
        private const float RejuvenateFraction = 0.3f;
        private const float RejuvenateDisplaySeconds = 2f;

        private static readonly ModeHGroupBellEffect[] BellEffects =
        {
            ModeHGroupBellEffect.MeteorShower, ModeHGroupBellEffect.MissileStrike, ModeHGroupBellEffect.Haste,
            ModeHGroupBellEffect.Berserk, ModeHGroupBellEffect.Rejuvenate, ModeHGroupBellEffect.FrostWave,
        };

        #endregion

        #region 状态

        private readonly List<ModeHGroupUnit> _units = new List<ModeHGroupUnit>();
        private readonly List<CharacterMainControl> _auxiliaries = new List<CharacterMainControl>();
        private readonly List<Teams> _auxiliaryTeams = new List<Teams>();
        private readonly List<BossRushStatModifierRecord> _bellModifiers = new List<BossRushStatModifierRecord>();
        private readonly System.Random _rng;

        private float _elapsed;
        private float _retargetAccumulator;
        private bool _hasResult;
        private int _winner;
        private bool _surrendered;
        private bool _timeout;
        private int _cameraIndex = -1;
        private bool _cameraPreferRed;
        private int _allyAlive;
        private int _enemyAlive;
        private int _allyTotal;
        private int _enemyTotal;
        private int _allyKills;
        private int _enemyKills;

        private ModeHGroupBellEffect _bellEffect;
        private bool _bellConsumed;
        private float _bellRemaining;
        private float _bellTick;
        private int _missileWavesLeft;

        #endregion

        internal ModeHGroupBattle(long seed)
        {
            _rng = new System.Random(unchecked((int)(seed ^ (seed >> 32))));
        }

        #region 只读

        internal List<ModeHGroupUnit> Units { get { return _units; } }
        internal bool HasResult { get { return _hasResult; } }
        /// <summary>胜方：0 = 同归于尽 / 平局（庄家赢），1 = 蓝队，2 = 红队。</summary>
        internal int Winner { get { return _winner; } }
        internal const int WinnerNone = 0, WinnerBlue = 1, WinnerRed = 2;
        /// <summary>玩家在观战里投降（不论押哪边都按输结算）。</summary>
        internal bool Surrendered { get { return _surrendered; } }
        internal bool Timeout { get { return _timeout; } }
        internal float Elapsed { get { return _elapsed; } }
        internal float RemainingSeconds { get { return Mathf.Max(0f, ModeHGroupConfig.MatchDurationSeconds - _elapsed); } }
        internal int AllyAlive { get { return _allyAlive; } }
        internal int EnemyAlive { get { return _enemyAlive; } }
        internal int AllyTotal { get { return _allyTotal; } }
        internal int EnemyTotal { get { return _enemyTotal; } }
        internal int AllyKills { get { return _allyKills; } }
        internal int EnemyKills { get { return _enemyKills; } }
        internal bool BellConsumed { get { return _bellConsumed; } }
        internal ModeHGroupBellEffect BellEffect { get { return _bellEffect; } }
        internal float BellRemaining { get { return _bellRemaining; } }

        /// <summary>
        /// 镜头跟谁：玩家用 WASD / 方向键 / 鼠标切换选中的那一个；他倒了就顺到同一队下一个还站着的，
        /// 那一队全倒了跟另一队。开场先跟玩家押的那一队。
        /// </summary>
        internal CharacterMainControl CameraFocus
        {
            get
            {
                if (_cameraIndex >= 0 && _cameraIndex < _units.Count && IsCameraCandidate(_units[_cameraIndex]))
                    return _units[_cameraIndex].Character;
                bool side = _cameraIndex >= 0 && _cameraIndex < _units.Count ? _units[_cameraIndex].IsEnemy : _cameraPreferRed;
                int next = FindAlive(_cameraIndex < 0 ? 0 : _cameraIndex, 1, side, true);
                if (next < 0) next = FindAlive(0, 1, !side, true);
                _cameraIndex = next;
                return next >= 0 ? _units[next].Character : null;
            }
        }

        /// <summary>开场镜头先跟哪一队。</summary>
        internal void SetCameraPreference(bool red)
        {
            _cameraPreferRed = red;
            _cameraIndex = -1;
        }

        /// <summary>A / ← / 鼠标左键：上一个；D / → / 鼠标右键：下一个（两队连着轮）。</summary>
        internal void CycleCamera(int direction)
        {
            if (_units.Count == 0) return;
            int start = _cameraIndex < 0 ? 0 : _cameraIndex + (direction >= 0 ? 1 : -1);
            int next = FindAlive(start, direction >= 0 ? 1 : -1, false, false);
            if (next >= 0) _cameraIndex = next;
        }

        /// <summary>W / ↑：跳到蓝队；S / ↓：跳到红队（再按一次在这一队里往下轮）。</summary>
        internal void FocusSide(bool red)
        {
            bool sameSide = _cameraIndex >= 0 && _cameraIndex < _units.Count && _units[_cameraIndex].IsEnemy == red;
            int start = sameSide ? _cameraIndex + 1 : 0;
            int next = FindAlive(start, 1, red, true);
            if (next >= 0) _cameraIndex = next;
        }

        private int FindAlive(int start, int step, bool red, bool filterSide)
        {
            int count = _units.Count;
            if (count == 0) return -1;
            for (int n = 0; n < count; n++)
            {
                int i = ((start + n * step) % count + count) % count;
                ModeHGroupUnit unit = _units[i];
                if (!IsCameraCandidate(unit) || (filterSide && unit.IsEnemy != red)) continue;
                return i;
            }
            return -1;
        }

        private static bool IsCameraCandidate(ModeHGroupUnit unit)
        {
            return unit != null && !unit.Down && unit.Character != null;
        }

        #endregion

        #region 开场

        /// <summary>登记一名已激活的选手。由生成 owner 在提交成功后逐个调用。</summary>
        internal void AddUnit(ModeHGroupUnit unit)
        {
            if (unit == null || unit.Character == null) return;
            _units.Add(unit);
            if (unit.IsEnemy) _enemyTotal++;
            else _allyTotal++;
        }

        /// <summary>开打：计时清零，数一次活人。</summary>
        internal void Begin()
        {
            ModeHGroupTeamTags.SetActive(true);
            _elapsed = 0f;
            _retargetAccumulator = RetargetIntervalSeconds;
            CountAlive(0f);
        }

        /// <summary>
        /// 自定义 Boss 召唤的辅助单位（女巫随从、龙王第三阶段的龙裔）：激活前登记，跟随召唤者阵营，
        /// 回收时一起销毁。返回 true 放行激活。
        /// </summary>
        internal bool RegisterAuxiliary(CharacterMainControl character, Teams team)
        {
            if (character == null || _hasResult) return false;
            if (_auxiliaries.Contains(character)) return true;
            _auxiliaries.Add(character);
            _auxiliaryTeams.Add(team);
            try
            {
                if (character.Health != null) ModeHDeathSuppressionRegistry.RegisterCharacter(character.Health, character);
                if (character.CharacterItem != null) character.CharacterItem.SetInt("Exp", 0, true);
            }
            catch (Exception) { /* 登记失败只少一层掉落抑制 */ }
            return true;
        }

        #endregion

        #region 每帧

        /// <summary>每帧推进。返回 true 表示本帧分出胜负。</summary>
        internal bool Tick(float deltaTime)
        {
            if (_hasResult) return false;
            _elapsed += deltaTime;
            CountAlive(deltaTime);
            SyncAuxiliaryTeams();

            if (_enemyAlive == 0 || _allyAlive == 0)
            {
                // 同帧两边全倒：没人站着就是庄家赢
                ClaimResult(_allyAlive > 0 ? WinnerBlue : (_enemyAlive > 0 ? WinnerRed : WinnerNone), false);
                return true;
            }
            if (_elapsed >= ModeHGroupConfig.MatchDurationSeconds)
            {
                float blue = ScoreSide(false);
                float red = ScoreSide(true);
                ClaimResult(blue > red ? WinnerBlue : (red > blue ? WinnerRed : WinnerNone), true);
                return true;
            }

            _retargetAccumulator += deltaTime;
            if (_retargetAccumulator >= RetargetIntervalSeconds)
            {
                _retargetAccumulator = 0f;
                Retarget();
            }
            TickBell(deltaTime);
            return false;
        }

        /// <summary>投降：直接判玩家输（不论押哪边）。</summary>
        internal bool TrySurrender()
        {
            if (_hasResult) return false;
            _surrendered = true;
            ClaimResult(WinnerNone, false);
            return true;
        }

        private void ClaimResult(int winner, bool timeout)
        {
            _hasResult = true;
            _winner = winner;
            _timeout = timeout;
            EndBellEffect();
            ModeHFriendlyFireBarrier.Disarm();
        }

        /// <summary>开打：同队伤害屏障上膛（两队 Boss 的爆炸、范围技能不再误伤队友）。</summary>
        internal void ArmFriendlyFireBarrier()
        {
            ModeHFriendlyFireBarrier.Arm();
        }

        private void CountAlive(float deltaTime)
        {
            int allies = 0;
            int enemies = 0;
            for (int i = 0; i < _units.Count; i++)
            {
                ModeHGroupUnit unit = _units[i];
                if (!unit.Down && IsDeparted(unit, deltaTime)) unit.Down = true;
                if (unit.Down) continue;
                if (unit.IsEnemy) enemies++;
                else allies++;
            }
            _allyAlive = allies;
            _enemyAlive = enemies;
        }

        private static bool IsDeparted(ModeHGroupUnit unit, float deltaTime)
        {
            CharacterMainControl character = unit.Character;
            if (character == null) return true;
            try
            {
                Health health = character.Health;
                if (health == null || health.IsDead) return true;
                if (character.gameObject.activeInHierarchy)
                {
                    unit.InactiveSeconds = 0f;
                    return false;
                }
            }
            catch (Exception) { return true; }
            unit.InactiveSeconds += deltaTime > 0f ? deltaTime : 0f;
            return unit.InactiveSeconds >= DepartedInactiveGraceSeconds;
        }

        /// <summary>到时的判分：存活者战力 × 剩余血量比例之和。</summary>
        private float ScoreSide(bool enemy)
        {
            float score = 0f;
            for (int i = 0; i < _units.Count; i++)
            {
                ModeHGroupUnit unit = _units[i];
                if (unit.Down || unit.IsEnemy != enemy) continue;
                score += unit.Entry.Power * ModeHCombatTelemetry.ReadHealthFraction(unit.Character);
            }
            return score;
        }

        /// <summary>召唤物被官方激活逻辑改成 wolf：每帧对齐回召唤者的阵营（只在不一致时写）。</summary>
        private void SyncAuxiliaryTeams()
        {
            for (int i = _auxiliaries.Count - 1; i >= 0; i--)
            {
                CharacterMainControl aux = _auxiliaries[i];
                if (aux == null) continue;
                try
                {
                    if (aux.Team != _auxiliaryTeams[i] && aux.gameObject.activeInHierarchy) aux.SetTeam(_auxiliaryTeams[i]);
                }
                catch (Exception) { /* 召唤物刚被回收 */ }
            }
        }

        #endregion

        #region 索敌

        private void Retarget()
        {
            for (int i = 0; i < _units.Count; i++)
            {
                ModeHGroupUnit unit = _units[i];
                if (unit.Down || unit.Character == null) continue;
                ModeHGroupUnit nearest = FindNearestOpponent(unit);
                if (nearest == null) continue;
                DamageReceiver target;
                try { target = nearest.Character.mainDamageReceiver; }
                catch (Exception) { continue; }
                if (target == null) continue;
                WakeOpponent(unit.Character, target);
                NudgeApproach(unit, target);
            }
        }

        private ModeHGroupUnit FindNearestOpponent(ModeHGroupUnit from)
        {
            Vector3 origin;
            try { origin = from.Character.transform.position; }
            catch (Exception) { return null; }
            ModeHGroupUnit best = null;
            float bestSqr = float.MaxValue;
            for (int i = 0; i < _units.Count; i++)
            {
                ModeHGroupUnit other = _units[i];
                if (other.Down || other.IsEnemy == from.IsEnemy || other.Character == null) continue;
                float sqr;
                try { sqr = (other.Character.transform.position - origin).sqrMagnitude; }
                catch (Exception) { continue; }
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = other;
                }
            }
            return best;
        }

        /// <summary>没目标或目标已倒 / 不再敌对时补一次官方索敌输入；已有有效目标不覆盖。</summary>
        private static void WakeOpponent(CharacterMainControl character, DamageReceiver target)
        {
            try
            {
                if (target == null || target.health == null || target.health.IsDead
                    || character.Health == null || character.Health.IsDead
                    || !character.gameObject.activeInHierarchy || !Team.IsEnemy(character.Team, target.Team)) return;
                AICharacterController ai = ResolveAi(character);
                if (ai == null) return;
                // 托管 Boss 激活时会把强制追玩家打开：鸭王杯里玩家只在看台，一律关掉
                if (ai.forceTracePlayerDistance > 0.5f) ai.forceTracePlayerDistance = 0f;
                DamageReceiver current = ai.searchedEnemy;
                if (current != null && current.health != null && !current.health.IsDead
                    && Team.IsEnemy(character.Team, current.Team)
                    && !ReferenceEquals(current, CharacterMainControl.Main != null ? CharacterMainControl.Main.mainDamageReceiver : null)) return;
                ai.searchedEnemy = target;
                ai.noticed = true;
                ai.SetNoticedToTarget(target);
            }
            catch (Exception) { /* 角色刚被回收 */ }
        }

        /// <summary>目标在视距外时按 1.5 秒节流发一次走近指令，进视距后交还官方行为树。</summary>
        private static void NudgeApproach(ModeHGroupUnit unit, DamageReceiver target)
        {
            try
            {
                CharacterMainControl character = unit.Character;
                if (Time.time < unit.NextApproachTime || !character.gameObject.activeInHierarchy) return;
                AICharacterController ai = ResolveAi(character);
                if (ai == null || !ReferenceEquals(ai.searchedEnemy, target)) return;
                unit.NextApproachTime = Time.time + ApproachRepathSeconds;
                Vector3 from = character.transform.position;
                Vector3 to = target.transform.position;
                float sight = ai.sightDistance > 1f ? ai.sightDistance : 20f;
                if ((to - from).sqrMagnitude <= sight * sight * 0.64f) return;
                if (ai.WaitingForPathResult()) return;
                ai.MoveToPos(to);
            }
            catch (Exception) { /* 寻路组件缺失：本轮不靠近 */ }
        }

        private static AICharacterController ResolveAi(CharacterMainControl character)
        {
            if (character == null) return null;
            try
            {
                AICharacterController cached = character.aiCharacterController;
                if (cached != null) return cached;
                return character.GetComponentInChildren<AICharacterController>(true);
            }
            catch (Exception) { return null; }
        }

        #endregion

        #region 拍铃：全局天灾

        internal static string DescribeEffect(ModeHGroupBellEffect effect)
        {
            switch (effect)
            {
                case ModeHGroupBellEffect.MeteorShower: return L10n.T("陨星雨", "Meteor shower");
                case ModeHGroupBellEffect.MissileStrike: return L10n.T("导弹轰炸", "Missile strike");
                case ModeHGroupBellEffect.Haste: return L10n.T("全员加速 50%", "Everyone +50% speed");
                case ModeHGroupBellEffect.Berserk: return L10n.T("全员狂暴 伤害 +50%", "Everyone +50% damage");
                case ModeHGroupBellEffect.Rejuvenate: return L10n.T("回春 全员回血 30%", "Everyone heals 30%");
                case ModeHGroupBellEffect.FrostWave: return L10n.T("寒潮 全员减速 40%", "Frost: everyone -40% speed");
                default: return L10n.T("随机天灾", "Random disaster");
            }
        }

        internal static string DescribeEffectPlain(ModeHGroupBellEffect effect)
        {
            switch (effect)
            {
                case ModeHGroupBellEffect.MeteorShower: return L10n.T("8 秒里陨石乱砸，砸中谁算谁。", "Meteors pound the arena for 8 s, hitting anyone.");
                case ModeHGroupBellEffect.MissileStrike: return L10n.T("三轮导弹点名轰炸，敌我不分。", "Three missile waves on random fighters, either side.");
                case ModeHGroupBellEffect.Haste: return L10n.T("15 秒内所有人移速 +50%。", "Everyone moves 50% faster for 15 s.");
                case ModeHGroupBellEffect.Berserk: return L10n.T("15 秒内所有人伤害 +50%。", "Everyone deals 50% more damage for 15 s.");
                case ModeHGroupBellEffect.Rejuvenate: return L10n.T("场上所有人立刻回 30% 血。", "Everyone on the field heals 30% at once.");
                case ModeHGroupBellEffect.FrostWave: return L10n.T("12 秒内所有人移速 -40%。", "Everyone moves 40% slower for 12 s.");
                default: return L10n.T("陨星、导弹、加速、狂暴……随机一种，两边一视同仁。", "Meteors, missiles, haste, frenzy... one at random, both sides alike.");
            }
        }

        /// <summary>拍铃：每场一次，随机一种天灾立刻生效。</summary>
        internal bool TryRingBell(out ModeHGroupBellEffect effect)
        {
            effect = ModeHGroupBellEffect.None;
            if (_hasResult || _bellConsumed) return false;
            _bellConsumed = true;
            _bellEffect = BellEffects[_rng.Next(BellEffects.Length)];
            effect = _bellEffect;
            _bellTick = 0f;
            switch (_bellEffect)
            {
                case ModeHGroupBellEffect.MeteorShower:
                    _bellRemaining = MeteorDuration;
                    break;
                case ModeHGroupBellEffect.MissileStrike:
                    _bellRemaining = MissileDuration;
                    _missileWavesLeft = 3;
                    break;
                case ModeHGroupBellEffect.Haste:
                    _bellRemaining = BuffDuration;
                    ApplyToAll(new[] { "RunSpeed", "WalkSpeed" }, HastePercent);
                    break;
                case ModeHGroupBellEffect.Berserk:
                    _bellRemaining = BuffDuration;
                    ApplyToAll(new[] { "GunDamageMultiplier", "MeleeDamageMultiplier" }, BerserkPercent);
                    break;
                case ModeHGroupBellEffect.FrostWave:
                    _bellRemaining = FrostDuration;
                    ApplyToAll(new[] { "RunSpeed", "WalkSpeed" }, FrostPercent);
                    break;
                case ModeHGroupBellEffect.Rejuvenate:
                    _bellRemaining = RejuvenateDisplaySeconds;
                    HealAll(RejuvenateFraction);
                    break;
            }
            return true;
        }

        private void TickBell(float deltaTime)
        {
            if (_bellRemaining <= 0f) return;
            _bellRemaining -= deltaTime;
            _bellTick -= deltaTime;
            if (_bellEffect == ModeHGroupBellEffect.MeteorShower && _bellTick <= 0f)
            {
                _bellTick = MeteorInterval;
                ModeHGroupUnit victim = PickRandomAlive();
                if (victim != null)
                {
                    Vector2 offset = UnityEngine.Random.insideUnitCircle * 3f;
                    Vector3 point;
                    try { point = victim.Character.transform.position + new Vector3(offset.x, 0f, offset.y); }
                    catch (Exception) { point = victim.SpawnPosition; }
                    Impact(point, MeteorRadius, MeteorDamageFraction);
                }
            }
            else if (_bellEffect == ModeHGroupBellEffect.MissileStrike && _bellTick <= 0f && _missileWavesLeft > 0)
            {
                _bellTick = MissileWaveInterval;
                _missileWavesLeft--;
                for (int i = 0; i < MissilesPerWave; i++)
                {
                    ModeHGroupUnit victim = PickRandomAlive();
                    if (victim == null) break;
                    Vector3 point;
                    try { point = victim.Character.transform.position; }
                    catch (Exception) { continue; }
                    Impact(point, MissileRadius, MissileDamageFraction);
                }
            }
            if (_bellRemaining <= 0f) EndBellEffect();
        }

        private void EndBellEffect()
        {
            _bellRemaining = 0f;
            _missileWavesLeft = 0;
            RuntimeStatModifierTracker.RemoveAll(_bellModifiers, "ModeHGroupBell");
        }

        private void ApplyToAll(string[] stats, float percent)
        {
            for (int i = 0; i < _units.Count; i++)
            {
                ModeHGroupUnit unit = _units[i];
                if (unit.Down || unit.Character == null) continue;
                for (int s = 0; s < stats.Length; s++)
                    RuntimeStatModifierTracker.TryAdd(unit.Character, stats[s], percent, this, _bellModifiers, "ModeHGroupBell");
            }
        }

        private void HealAll(float fraction)
        {
            for (int i = 0; i < _units.Count; i++)
            {
                ModeHGroupUnit unit = _units[i];
                if (unit.Down || unit.Health == null) continue;
                try
                {
                    if (!unit.Health.IsDead) unit.Health.AddHealth(unit.Health.MaxHealth * fraction);
                }
                catch (Exception) { /* 角色刚被回收 */ }
            }
        }

        private ModeHGroupUnit PickRandomAlive()
        {
            int alive = _allyAlive + _enemyAlive;
            if (alive <= 0) return null;
            int pick = _rng.Next(alive);
            for (int i = 0; i < _units.Count; i++)
            {
                if (_units[i].Down) continue;
                if (pick-- == 0) return _units[i];
            }
            return null;
        }

        /// <summary>
        /// 一次落点爆炸：官方爆炸只出特效与震屏（半径极小、伤害 0），伤害按每人最大生命的百分比自己结，
        /// 这样血厚血薄的 Boss 挨一下的分量一样。击杀归到离被砸者最近的对手，避免空攻击者让第三方击杀提示报错。
        /// </summary>
        private void Impact(Vector3 point, float radius, float damageFraction)
        {
            try
            {
                LevelManager level = LevelManager.Instance;
                if (level != null && level.ExplosionManager != null && CharacterMainControl.Main != null)
                {
                    DamageInfo fx = new DamageInfo(null);
                    fx.damageValue = 0f;
                    fx.damagePoint = point;
                    fx.isFromBuffOrEffect = true;
                    level.ExplosionManager.CreateExplosion(point, 0.05f, fx, ExplosionFxTypes.normal, 0.25f, true);
                }
            }
            catch (Exception) { /* 特效失败不影响伤害 */ }

            float radiusSqr = radius * radius;
            for (int i = 0; i < _units.Count; i++)
            {
                ModeHGroupUnit unit = _units[i];
                if (unit.Down || unit.Character == null || unit.Health == null) continue;
                try
                {
                    Vector3 pos = unit.Character.transform.position;
                    float dx = pos.x - point.x;
                    float dz = pos.z - point.z;
                    if (dx * dx + dz * dz > radiusSqr || unit.Health.IsDead) continue;
                    ModeHGroupUnit credit = FindNearestOpponent(unit);
                    DamageInfo hit = new DamageInfo(credit != null ? credit.Character : null);
                    hit.damageValue = Mathf.Max(1f, unit.Health.MaxHealth * damageFraction);
                    hit.ignoreArmor = true;
                    hit.isExplosion = true;
                    hit.isFromBuffOrEffect = true;
                    hit.damagePoint = pos;
                    hit.damageNormal = Vector3.up;
                    unit.Health.Hurt(hit);
                }
                catch (Exception) { /* 单人失败不影响其他人 */ }
            }
        }

        #endregion

        #region 遥测接收（只记双方击倒数）

        public void OnParticipantHurt(ModeHParticipantRef target, ModeHParticipantRef attacker, float damageValue,
            int fromWeaponItemID)
        {
        }

        public void OnParticipantDead(ModeHParticipantRef target, ModeHParticipantRef killer)
        {
            if (target == null || _hasResult) return;
            if (target.IsEnemy) _allyKills++;
            else _enemyKills++;
        }

        #endregion

        #region 回收

        /// <summary>
        /// 幂等回收：撤掉天灾加成，自定义 Boss 与召唤物按托管契约清理；官方 Boss 由生成事务回滚销毁。
        /// </summary>
        internal void Release()
        {
            ModeHFriendlyFireBarrier.Disarm();
            ModeHGroupTeamTags.SetActive(false);
            try { RuntimeStatModifierTracker.RemoveAll(_bellModifiers, "ModeHGroupBell"); }
            catch (Exception) { /* 角色已销毁 */ }
            for (int i = 0; i < _units.Count; i++)
            {
                ModeHGroupUnit unit = _units[i];
                if (unit.ManagedHandle == null) continue;
                ReleaseManaged(unit);
            }
            for (int i = 0; i < _auxiliaries.Count; i++)
            {
                CharacterMainControl aux = _auxiliaries[i];
                try
                {
                    if (aux == null) continue;
                    ModeHDeathSuppressionRegistry.UnregisterCharacter(aux.Health, aux);
                    if (aux.gameObject != null) UnityEngine.Object.Destroy(aux.gameObject);
                }
                catch (Exception) { /* 已被召唤者回收 */ }
            }
            _auxiliaries.Clear();
            _auxiliaryTeams.Clear();
            _units.Clear();
        }

        internal static void ReleaseManaged(ModeHGroupUnit unit)
        {
            if (unit == null || unit.ManagedHandle == null) return;
            try { ModeHDeathSuppressionRegistry.UnregisterCharacter(unit.Health, unit.Character); }
            catch (Exception) { /* 继续清理 */ }
            try { if (unit.SuppressedPreset != null) ModeHDeathSuppressionRegistry.UnregisterPreset(unit.SuppressedPreset); }
            catch (Exception) { /* 继续清理 */ }
            unit.SuppressedPreset = null;
            ManagedBossRuntimeHandle handle = unit.ManagedHandle;
            unit.ManagedHandle = null;
            handle.CleanupOnce(ManagedBossCleanupReason.RunEnded);
        }

        #endregion

        #region 自定义 Boss 托管生成

        /// <summary>按 key 调三只自定义 Boss 的托管生成器（只准备、不激活）。</summary>
        internal static async UniTask<ManagedBossPrepareResult> PrepareCustomAsync(ModBehaviour owner, string key,
            Vector3 position, ManagedBossSpawnContext ctx)
        {
            if (owner == null || ctx == null) return null;
            if (string.Equals(key, DragonDescendantConfig.BOSS_NAME_KEY, StringComparison.Ordinal))
                return await owner.PrepareManagedDragonDescendantAsync(position, ctx);
            if (string.Equals(key, DragonKingConfig.BossNameKey, StringComparison.Ordinal))
                return await owner.PrepareManagedDragonKingAsync(position, ctx);
            if (string.Equals(key, PhantomWitchConfig.BossNameKey, StringComparison.Ordinal))
                return await owner.PrepareManagedPhantomWitchAsync(position, ctx);
            return null;
        }

        #endregion
    }

    /// <summary>
    /// 群战站位：以锚点为中心按六边形逐圈找点，每个点吸附到官方 A* 可走点，再过两道检查：
    /// 身体胶囊不碰墙 / 半高障碍，锚点到这个点之间没有墙（同一块开阔地，不会隔墙落到另一条走廊或墙体里）。
    /// 2026-09-29 owner 实测迷宫里有 Boss 刷进墙：旧版只吸附 A* 节点，节点贴墙时角色半个身子在墙里。
    /// 合格点不够时按合格点循环复用（角色之间互相推开，不会被推进墙），一个合格点都没有才退回锚点。
    /// </summary>
    internal static class ModeHGroupFormation
    {
        private const float Spacing = 1.6f;
        private const int MaxRings = 14;
        private const float BodyRadius = 0.45f;

        internal static List<Vector3> Build(Vector3 anchor, int count)
        {
            List<Vector3> result = new List<Vector3>(count);
            if (count <= 0) return result;
            List<Vector3> clear = new List<Vector3>(count);
            Vector3 point;
            if (TrySnapClear(anchor, anchor, out point)) clear.Add(point);
            for (int ring = 1; ring <= MaxRings && clear.Count < count; ring++)
            {
                int slots = ring * 6;
                float radius = ring * Spacing;
                for (int i = 0; i < slots && clear.Count < count; i++)
                {
                    float angle = (i / (float)slots) * Mathf.PI * 2f;
                    Vector3 raw = anchor + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
                    if (TrySnapClear(raw, anchor, out point) && !TooClose(clear, point)) clear.Add(point);
                }
            }
            if (clear.Count == 0) clear.Add(anchor);
            for (int i = 0; i < count; i++) result.Add(clear[i % clear.Count]);
            return result;
        }

        private static bool TooClose(List<Vector3> points, Vector3 point)
        {
            for (int i = 0; i < points.Count; i++)
                if ((points[i] - point).sqrMagnitude < 1f) return true;
            return false;
        }

        private static bool TrySnapClear(Vector3 raw, Vector3 anchor, out Vector3 position)
        {
            position = anchor;
            try
            {
                AstarPath astar = AstarPath.active;
                if (astar == null || astar.isScanning) return false;
                Pathfinding.NNInfo nearest = astar.GetNearest(raw, Pathfinding.NNConstraint.Walkable);
                if (nearest.node == null || !nearest.node.Walkable) return false;
                if (Vector3.Distance(nearest.position, raw) > 2f || Mathf.Abs(nearest.position.y - anchor.y) > 1.5f)
                    return false;
                Vector3 p = nearest.position;
                int walls = Duckov.Utilities.GameplayDataSettings.Layers.wallLayerMask
                    | Duckov.Utilities.GameplayDataSettings.Layers.halfObsticleLayer;
                if (Physics.CheckCapsule(p + Vector3.up * 0.5f, p + Vector3.up * 1.4f, BodyRadius, walls,
                        QueryTriggerInteraction.Ignore)) return false;
                if ((p - anchor).sqrMagnitude > 0.01f && Physics.Linecast(anchor + Vector3.up * 1f, p + Vector3.up * 1f,
                        Duckov.Utilities.GameplayDataSettings.Layers.wallLayerMask, QueryTriggerInteraction.Ignore))
                    return false;
                position = p;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// 鸭王杯群战的同队伤害屏障（2026-09-29 owner：一个队的会伤害自己的队友）。
    /// 官方子弹本来就不打同队（Projectile 对 context.team 与受击者同队直接跳过），漏网的是爆炸（榴弹、火箭、
    /// 部分 Boss 技能用 Teams.all 的「谁都炸」口径）与自定义 Boss 的范围技能。只在开打到分出胜负之间上膛，
    /// 由 Patches/Combat/ModeHFriendlyFireReceiverPatch（DamageReceiver.Hurt 前缀）查询：攻击者与受击者同队、都不是玩家、不是自伤时拦下。
    /// 天灾的陨石 / 导弹把伤害归到对面最近的人，不受影响；龙王「孩儿护我」的联动死亡来源为空，不受影响。
    /// </summary>
    internal static class ModeHFriendlyFireBarrier
    {
        private static bool _armed;

        internal static bool IsArmed { get { return _armed; } }

        internal static void Arm() { _armed = true; }

        internal static void Disarm() { _armed = false; }

        internal static void ResetStaticCaches() { _armed = false; }

        /// <summary>no-throw；异常按不拦截处理，不打断官方受伤流程。</summary>
        internal static bool ShouldBlock(Health target, CharacterMainControl from)
        {
            if (!_armed || target == null || from == null) return false;
            try
            {
                CharacterMainControl victim = target.TryGetCharacter();
                if (victim == null || ReferenceEquals(victim, from) || victim.IsMainCharacter || from.IsMainCharacter) return false;
                Teams team = victim.Team;
                return team == from.Team && team != Teams.player && team != Teams.all && team != Teams.middle;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// 鸭王杯群战的血条队伍标识（2026-09-29 owner：学划地为营给两边加「- 蓝队」「- 红队」，字色和意思一致）。
    /// 由统一的血条名字补丁（BossRushHealthBarNamePatch，与划地为营 / 血猎追击同一个 LateUpdate 后缀）在群战期间调用；
    /// 蓝队 = Teams.scav（左），红队 = Teams.wolf（右），颜色与对照页列头同一对 token。
    /// </summary>
    internal static class ModeHGroupTeamTags
    {
        private static bool _active;
        private static readonly string BlueOpen = "<color=#" + ColorUtility.ToHtmlStringRGB(BossRushUIColors.RarityRare) + ">";
        private static readonly string RedOpen = "<color=#" + ColorUtility.ToHtmlStringRGB(BossRushUIColors.DangerText) + ">";

        internal static bool IsActive { get { return _active; } }

        internal static void SetActive(bool active) { _active = active; }

        internal static void ResetStaticCaches() { _active = false; }

        internal static string TeamName(bool red) { return red ? L10n.T("红队", "Red Team") : L10n.T("蓝队", "Blue Team"); }

        private static string Suffix(bool red, string name) { return " " + (red ? RedOpen : BlueOpen) + "- " + name + "</color>"; }

        /// <summary>给一条 Boss 血条的名字补上队伍后缀（切语言后先剥掉另一种语言的旧后缀）。no-throw。</summary>
        internal static void Apply(HealthBar bar, TextMeshProUGUI nameText)
        {
            if (!_active || bar == null || nameText == null || !nameText.gameObject.activeSelf) return;
            try
            {
                Health target = bar.target;
                CharacterMainControl character = target != null ? target.TryGetCharacter() : null;
                if (character == null || character.IsMainCharacter) return;
                Teams team = character.Team;
                string text = nameText.text ?? string.Empty;
                string baseText = Strip(text);
                string desired = team == Teams.scav ? baseText + Suffix(false, TeamName(false))
                    : team == Teams.wolf ? baseText + Suffix(true, TeamName(true)) : baseText;
                if (!string.Equals(text, desired, StringComparison.Ordinal)) nameText.text = desired;
            }
            catch (Exception) { /* 血条刚被回收 */ }
        }

        private static string Strip(string text)
        {
            string[] suffixes =
            {
                Suffix(false, "蓝队"), Suffix(false, "Blue Team"), Suffix(true, "红队"), Suffix(true, "Red Team"),
            };
            for (int i = 0; i < suffixes.Length; i++)
                if (text.EndsWith(suffixes[i], StringComparison.Ordinal)) return text.Substring(0, text.Length - suffixes[i].Length);
            return text;
        }
    }
}
