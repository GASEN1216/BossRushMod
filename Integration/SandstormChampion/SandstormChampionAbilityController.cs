using System;
using System.Collections;
using System.Collections.Generic;
using ItemStatsSystem;
using ItemStatsSystem.Stats;
using Pathfinding;
using UnityEngine;

namespace BossRush
{
    /// <summary>单实例拥有战斗、危险物与死亡订阅；第六章 owner 销毁角色即取消整场战斗。</summary>
    internal sealed class SandstormChampionController : MonoBehaviour
    {
        private CharacterMainControl _boss;
        private ModBehaviour _owner;
        private CharacterRandomPreset _preset;
        private Item _reward;
        private SandstormChampionBody _body;
        private SandstormAmbience _ambience;
        private SandstormChampionMinions _minions;
        private SandstormDashWarning _warning;
        private GameObject _novaWarning;
        private readonly List<SandstormOrb> _orbs = new List<SandstormOrb>();
        private readonly List<SandstormTornado> _tornadoes = new List<SandstormTornado>();
        private readonly List<SandstormCycloneSeed> _seeds = new List<SandstormCycloneSeed>();
        private readonly SandstormChampionAttackPattern _attackPattern = new SandstormChampionAttackPattern();
        private bool _subscribed;
        private bool _stopped;
        private float _baseline;
        private int _phase = 1;
        private float _blockedSeconds;
        private Seeker _navigator;
        private readonly List<Vector3> _navigationPath = new List<Vector3>();
        private int _navigationIndex;
        private int _navigationRevision;
        private bool _pathPending;
        private float _nextPathRequest;
        private Vector3 _lastMotionPosition;
        private bool _motionCommanded;
        private int _groundMask;
        private int _wallMask;
        private float _nextDamageTime;
        private int _nextMinionCycle = 2;
        private bool _minionSummoning;
        private bool _enraged;
        private float _outsideSeconds;
        private Vector3 _arenaCenter;
        private Vector3 _lastDashDirection;
        // 冲锋预判用的玩家水平速度：按位移差分并指数平滑，翻滚 / 走位抖动不会让预判乱跳。
        private Vector3 _playerVelocity;
        private Vector3 _lastPlayerPosition;
        private bool _playerVelocityPrimed;
        private Modifier _bodyArmorModifier;
        private Modifier _headArmorModifier;
        private float _stormEndsAt;
        private float _moveAttemptSeconds;
        private float _moveDistance;
        private float _nextMoveReport;
        private Vector3 _reportPosition;
        private Func<bool> _isCurrent;
        private int _sceneHandle;
        private IEnumerator _fightRoutine;
        private bool _reportedBubbleBreak;
        private bool _reportedSharkBreak;
        private float _nextHurtFxTime;

        private bool HasPhaseTransition
        {
            get
            {
                if (!IsFighting) return false;
                float ratio = _boss.Health.CurrentHealth / Mathf.Max(1f, _boss.Health.MaxHealth);
                return (_phase < 3 && ratio < SandstormChampionConfig.Phase3HealthRatio)
                    || (_phase < 2 && ratio < SandstormChampionConfig.Phase2HealthRatio);
            }
        }

        internal bool IsFighting
        {
            get
            {
                CharacterMainControl player = CharacterMainControl.Main;
                return !_stopped && _owner != null && _boss != null && _boss.Health != null && !_boss.Health.IsDead
                    && player != null && player.Health != null && !player.Health.IsDead && !SceneLoader.IsSceneLoading
                    && _isCurrent != null && _isCurrent()
                    && UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle == _sceneHandle;
            }
        }

        internal float DamageScale { get { return BossSkillDamageRules.ResolveMeleeDamageScale(_boss, _baseline); } }
        internal Teams HazardTeam { get { return _boss != null ? _boss.Team : Teams.wolf; } }

        internal void ReportOrbShotDown(bool bubble)
        {
            if (bubble ? _reportedBubbleBreak : _reportedSharkBreak) return;
            if (bubble) _reportedBubbleBreak = true;
            else _reportedSharkBreak = true;
            Debug.Log(SandstormChampionConfig.LogPrefix + "弹幕击破已确认: " + (bubble ? "bubble" : "shark"));
        }

        internal void Initialize(CharacterMainControl boss, CharacterRandomPreset preset, Item reward,
            float baseline, BossAIController ai, Func<bool> isCurrent, int sceneHandle, ModBehaviour owner)
        {
            _boss = boss;
            _owner = owner;
            _preset = preset;
            _reward = reward;
            _baseline = baseline;
            _isCurrent = isCurrent;
            _sceneHandle = sceneHandle;
            ai.Pause();
            _groundMask = Duckov.Utilities.GameplayDataSettings.Layers.groundLayerMask;
            _wallMask = Duckov.Utilities.GameplayDataSettings.Layers.wallLayerMask;
            _navigator = boss.gameObject.AddComponent<Seeker>();
            _lastMotionPosition = boss.transform.position;
            _arenaCenter = boss.transform.position;
            Stat bodyArmor = boss.CharacterItem.GetStat("BodyArmor");
            Stat headArmor = boss.CharacterItem.GetStat("HeadArmor");
            if (bodyArmor != null)
            {
                _bodyArmorModifier = new Modifier(ModifierType.PercentageMultiply, 0f, true, int.MaxValue, this);
                bodyArmor.AddModifier(_bodyArmorModifier);
            }
            if (headArmor != null)
            {
                _headArmorModifier = new Modifier(ModifierType.PercentageMultiply, 0f, true, int.MaxValue, this);
                headArmor.AddModifier(_headArmorModifier);
            }
            _reportPosition = boss.transform.position;
            _nextMoveReport = Time.time + 8f;
            // 由官方 Movement/ECM2 处理重力、贴地与实体碰撞。连续移动不再每帧 ForceSetPosition。
            boss.movementControl.MovementEnabled = true;
            boss.movementControl.SetGravityFactor(1f);
            _minions = new SandstormChampionMinions();
            _minions.Initialize(boss, this, owner);
            _body = SandstormChampionAssetManager.CreateBody(boss);
            boss.Health.SetInvincible(true);
            if (!_subscribed)
            {
                boss.BeforeCharacterSpawnLootOnDead += OnBeforeSpawnLoot;
                boss.Health.OnDeadEvent.AddListener(OnBossDead);
                boss.Health.OnHurtEvent.AddListener(OnBossHurt);
                _subscribed = true;
            }
            BossRushAudioManager.Instance?.PlayBossBGM(BossBgmKeys.PhantomWitch, _boss);
            Debug.Log(SandstormChampionConfig.LogPrefix + "官方移动驱动就绪，groundMask=" + _groundMask
                + ", wallMask=" + _wallMask + ", position=" + boss.transform.position);
            _fightRoutine = RunFight();
        }

        private void Update()
        {
            if (_stopped) return;
            if (IsFighting)
            {
                TrackPlayerVelocity();
                TickFight();
                if (_stopped) return;
                UpdateEnrage();
                TickContactDamage();
                Vector3 moved = _boss.transform.position - _reportPosition;
                moved.y = 0f;
                _moveDistance += moved.magnitude;
                _reportPosition = _boss.transform.position;
                if (Time.time >= _nextMoveReport)
                {
                    if (_moveAttemptSeconds >= 1f && _moveDistance < 0.35f)
                        Debug.LogWarning(SandstormChampionConfig.LogPrefix + "移动受阻: phase=" + _phase
                            + ", attemptedSeconds=" + _moveAttemptSeconds.ToString("F2")
                            + ", movedMeters=" + _moveDistance.ToString("F2") + ", position=" + _reportPosition);
                    _moveAttemptSeconds = 0f;
                    _moveDistance = 0f;
                    _nextMoveReport = Time.time + 8f;
                }
                if (_phase == 1 && _ambience != null && Time.time >= _stormEndsAt)
                {
                    Destroy(_ambience.gameObject);
                    _ambience = null;
                }
                return;
            }
            // additive 场景回调也会撤销征程 runId；不能仅依靠场景卸载来回收活着的旧实例。
            StopBattle(false);
            if (_boss != null) Destroy(_boss.gameObject);
        }

        private void TickFight()
        {
            // 战斗由本组件 Update 推进；换位或宿主停止协程不能丢掉剩余攻击序列。
            if (_fightRoutine != null && !_fightRoutine.MoveNext()) _fightRoutine = null;
        }

        private IEnumerator WaitForFightSeconds(float seconds)
        {
            float until = Time.time + seconds;
            while (Time.time < until && IsFighting) yield return null;
        }

        private IEnumerator RunFight()
        {
            // 显式逐帧驱动嵌套迭代器；等待也由 WaitForFightSeconds 推进，不交给 Unity 协程调度。
            // 出错时结束本次实例，让报名 owner 能回收后重试。
            Stack<IEnumerator> routines = new Stack<IEnumerator>();
            routines.Push(Fight());
            while (routines.Count > 0 && IsFighting)
            {
                object current = null;
                bool moved = false;
                Exception failure = null;
                try
                {
                    IEnumerator routine = routines.Peek();
                    moved = routine.MoveNext();
                    if (moved) current = routine.Current;
                }
                catch (Exception e) { failure = e; }
                if (failure != null)
                {
                    Debug.LogWarning(SandstormChampionConfig.LogPrefix + "战斗中止，phase=" + _phase + ": " + failure);
                    StopBattle(false);
                    if (_boss != null) Destroy(_boss.gameObject);
                    yield break;
                }
                if (!moved) { routines.Pop(); continue; }
                IEnumerator nested = current as IEnumerator;
                if (nested != null) routines.Push(nested);
                else yield return current;
            }
        }

        private IEnumerator Fight()
        {
            yield return WaitForFightSeconds(1.1f);
            if (!IsFighting) yield break;
            _boss.Health.SetInvincible(false);
            Debug.Log(SandstormChampionConfig.LogPrefix + "弹幕战斗开始: phase=1");
            while (IsFighting)
            {
                float ratio = _boss.Health.CurrentHealth / Mathf.Max(1f, _boss.Health.MaxHealth);
                if (_attackPattern.UpdateState(ratio, _enraged))
                {
                    _phase = _attackPattern.Phase;
                    yield return Transition();
                }
                if (!IsFighting) break;
                SandstormChampionAttack attack = _attackPattern.Next();
                ModBehaviour.DevLog(SandstormChampionConfig.LogPrefix + "招式: phase=" + _phase
                    + ", attack=" + attack.Kind + ", count=" + attack.Count + ", enraged=" + _enraged);
                switch (attack.Kind)
                {
                    case SandstormChampionAttackKind.Dashes:
                        yield return Hover(_enraged ? 0.08f : SandstormChampionConfig.HoverSeconds);
                        yield return Dashes(attack.Count);
                        break;
                    case SandstormChampionAttackKind.BubbleBelch:
                        yield return BubbleBelch(attack.Count);
                        break;
                    case SandstormChampionAttackKind.SpiralBubbles:
                        yield return OrbitAndOrbs(attack.Count);
                        break;
                    case SandstormChampionAttackKind.TwinTornadoSeeds:
                        yield return SummonTornadoes(false);
                        break;
                    case SandstormChampionAttackKind.HomingCycloneSeed:
                        yield return SummonTornadoes(true);
                        break;
                    case SandstormChampionAttackKind.TeleportDashes:
                        yield return Reposition();
                        yield return Dashes(attack.Count);
                        break;
                }
                // 用户追加的持棍沙卫独立准备；不清沙暴、不占用原版招式的协程。
                if (_phase < 3 && _attackPattern.CompletedCycles >= _nextMinionCycle
                    && !_minionSummoning && _minions.CanSummon)
                {
                    _nextMinionCycle = _attackPattern.CompletedCycles + 2;
                    StartCoroutine(SummonMinions());
                }
            }
            StopBattle(false);
        }

        private IEnumerator Hover(float seconds)
        {
            float time = 0f;
            while (time < seconds && IsFighting && !HasPhaseTransition)
            {
                Vector3 from = _boss.transform.position;
                Vector3 to = from - CharacterMainControl.Main.transform.position;
                to.y = 0f;
                if (to.sqrMagnitude < 0.01f) to = Vector3.forward;
                Vector3 desired = CharacterMainControl.Main.transform.position + to.normalized * SandstormChampionConfig.HoverDistance;
                MoveToward(desired, SandstormChampionConfig.HoverSpeed);
                FacePlayer();
                time += Time.deltaTime;
                yield return null;
            }
            StopMotion();
        }

        private IEnumerator Dashes(int count)
        {
            for (int i = 0; i < count && IsFighting && !HasPhaseTransition; i++)
            {
                Vector3 from = _boss.transform.position;
                Vector3 direction = CharacterMainControl.Main.transform.position - from;
                direction.y = 0f;
                float length = Mathf.Min(SandstormChampionConfig.DashMaxLength,
                    direction.magnitude + SandstormChampionConfig.DashOvershoot);
                if (direction.sqrMagnitude < 0.001f) direction = Vector3.forward;
                direction.Normalize();
                float windup = _phase == 3 ? SandstormChampionConfig.DashTelegraphP3
                    : _phase == 2 ? SandstormChampionConfig.DashTelegraphP2 : SandstormChampionConfig.DashTelegraphP1;
                if (i == 0) windup += 0.1f;
                float speed = _phase == 3 ? SandstormChampionConfig.DashSpeedP3
                    : _phase == 2 ? SandstormChampionConfig.DashSpeedP2 : SandstormChampionConfig.DashSpeedP1;
                if (_enraged) speed *= 1.3f;
                Vector3 end = from + direction * length;
                StopMotion();
                CancelNavigation();
                _warning = SandstormDashWarning.Create(from, end, SandstormChampionConfig.ContactRadius * 2f, windup);
                _owner?.PlaySoundEffect(DragonKingConfig.Sound_DashCharge);
                Face(direction);
                float lead = _phase == 3 ? SandstormChampionConfig.DashLeadP3
                    : _phase == 2 ? SandstormChampionConfig.DashLeadP2 : SandstormChampionConfig.DashLeadP1;
                if (lead > 0f)
                {
                    // 前摇期间持续预判玩家走位，预警带跟着转；出手前短暂锁定，留出读招翻滚的窗口。
                    float lockAt = windup - (_phase == 3 ? SandstormChampionConfig.DashAimLockP3
                        : _phase == 2 ? SandstormChampionConfig.DashAimLockP2 : SandstormChampionConfig.DashAimLockP1);
                    float waited = 0f;
                    while (waited < windup && IsFighting)
                    {
                        if (waited < lockAt)
                        {
                            AimDash(from, speed, lead, windup - waited, out direction, out length);
                            if (_warning != null) _warning.Retarget(from, from + direction * length);
                            Face(direction);
                        }
                        waited += Time.deltaTime;
                        yield return null;
                    }
                }
                else yield return WaitForFightSeconds(windup);
                if (!IsFighting || HasPhaseTransition) yield break;
                if (_warning != null) { _warning.Flash(); _warning = null; }
                if (_body != null) { _body.SetCharge(true); _body.Flare(0.55f); }
                // 起步蹬地：脚下砂环炸开 + 近处轻震，冲锋有「发力」的那一下。
                SandstormChampionAssetManager.PlaySandBurst(from, 1.8f, 12, SandstormChampionConfig.WarningColor);
                SandstormChampionAssetManager.ShakeNear(from, 0.14f, 16f);
                _lastDashDirection = direction;
                _owner?.PlaySoundEffect(DragonKingConfig.Sound_DashBurst);
                float distance = 0f;
                float elapsed = 0f;
                float blocked = 0f;
                bool hit = false;
                bool strikeBlocked = false;
                // 末阶段「沙暴无形」：冲锋直接穿过墙体与掩体（逐帧贴地重设位置），玩家仍在前方时还会微调方向。
                bool phasing = _phase == 3;
                while (distance < length && elapsed < length / speed + 0.3f && IsFighting && !HasPhaseTransition)
                {
                    Vector3 previous = _boss.transform.position;
                    if (phasing) { if (!PhaseDashStep(ref direction, speed, !hit)) { strikeBlocked = true; break; } }
                    else if (!CommandMotion(direction * speed)) { strikeBlocked = true; break; }
                    yield return null;
                    if (!IsFighting) break;
                    Vector3 actual = _boss.transform.position - previous;
                    actual.y = 0f;
                    distance += actual.magnitude;
                    elapsed += Time.deltaTime;
                    blocked = actual.sqrMagnitude < 0.0004f ? blocked + Time.deltaTime : 0f;
                    // ECM2 会在墙前截停；不能在原地耗完五连冲，下一轮改走导航绕开掩体。
                    if (blocked >= 0.14f)
                    {
                        _blockedSeconds = Mathf.Max(_blockedSeconds, 0.2f);
                        strikeBlocked = true;
                        break;
                    }
                    // 用扫过的线段判命中，低帧率也不会穿过玩家却漏伤害。
                    if (!hit && !BossSkillDamageRules.IsDodging(CharacterMainControl.Main)
                        && DistanceToSegment(CharacterMainControl.Main.transform.position, previous, _boss.transform.position)
                            <= SandstormChampionConfig.ContactRadius)
                    {
                        hit = true;
                        HurtPlayer(_phase == 3 ? SandstormChampionConfig.DashDamageP3
                            : _phase == 2 ? SandstormChampionConfig.DashDamageP2 : SandstormChampionConfig.DashDamage, previous);
                        PlayDashImpact(CharacterMainControl.Main.transform.position, direction);
                    }
                }
                StopMotion();
                if (phasing) SettlePhaseDash();
                if (_body != null) _body.SetCharge(false);
                if (strikeBlocked && !phasing)
                {
                    // 一次撞墙就结束整组冲刺，立刻给低频寻路窗口，避免把余下冲锋全耗在同一堵墙上。
                    yield return Hover(0.45f);
                    yield break;
                }
                yield return WaitForFightSeconds(_phase == 3 ? 0.06f : SandstormChampionConfig.DashRecover);
            }
        }

        /// <summary>
        /// 冲锋预判：玩家当前位置 + 水平速度 ×（剩余前摇 + 冲到那里所需时间）× lead，速度有上限；
        /// 长度仍按原规则带过冲并封顶。
        /// </summary>
        private void AimDash(Vector3 from, float speed, float lead, float remainingWindup,
            out Vector3 direction, out float length)
        {
            CharacterMainControl player = CharacterMainControl.Main;
            Vector3 target = player.transform.position;
            Vector3 velocity = Vector3.ClampMagnitude(_playerVelocity, SandstormChampionConfig.DashLeadMaxSpeed);
            Vector3 flat = target - from;
            flat.y = 0f;
            float travel = flat.magnitude / Mathf.Max(1f, speed);
            Vector3 predicted = target + velocity * ((remainingWindup + travel) * lead);
            direction = predicted - from;
            direction.y = 0f;
            float distance = direction.magnitude;
            if (distance < 0.05f)
            {
                direction = flat.sqrMagnitude > 0.0025f ? flat.normalized : (_boss.transform.forward);
                direction.y = 0f;
                distance = flat.magnitude;
            }
            direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
            length = Mathf.Min(SandstormChampionConfig.DashMaxLength, distance + SandstormChampionConfig.DashOvershoot);
        }

        /// <summary>每帧按玩家位移差分出水平速度并做约 0.12 秒的指数平滑；切图 / 瞬移时重新起算。</summary>
        private void TrackPlayerVelocity()
        {
            CharacterMainControl player = CharacterMainControl.Main;
            float dt = Time.deltaTime;
            if (player == null || dt <= 0f) return;
            Vector3 position = player.transform.position;
            if (_playerVelocityPrimed)
            {
                Vector3 measured = (position - _lastPlayerPosition) / dt;
                measured.y = 0f;
                if (measured.sqrMagnitude > 900f) measured = Vector3.zero; // 传送 / 换场的跳变不算速度
                _playerVelocity = Vector3.Lerp(_playerVelocity, measured, 1f - Mathf.Exp(-dt / 0.12f));
            }
            _lastPlayerPosition = position;
            _playerVelocityPrimed = true;
        }

        /// <summary>
        /// 末阶段穿墙冲锋的一步：玩家仍在前方时按角速度上限微调方向，按地面高度逐帧重设位置（不经 ECM2 墙碰撞）。
        /// 前方没有地面或高差过大（悬崖、楼层）时结束这一冲。
        /// </summary>
        private bool PhaseDashStep(ref Vector3 direction, float speed, bool steer)
        {
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            if (dt <= 0f || _boss == null) return false;
            Vector3 from = _boss.transform.position;
            if (steer)
            {
                Vector3 toPlayer = CharacterMainControl.Main.transform.position - from;
                toPlayer.y = 0f;
                if (toPlayer.sqrMagnitude > 1f && Vector3.Dot(toPlayer.normalized, direction) > 0.2f)
                    direction = Vector3.RotateTowards(direction, toPlayer.normalized,
                        SandstormChampionConfig.PhaseDashSteerDegrees * Mathf.Deg2Rad * dt, 0f);
            }
            Vector3 next = from + direction * (speed * dt);
            RaycastHit ground;
            if (_groundMask == 0 || !Physics.Raycast(next + Vector3.up * 3f, Vector3.down, out ground, 8f,
                _groundMask, QueryTriggerInteraction.Ignore) || Mathf.Abs(ground.point.y - from.y) > 2.5f) return false;
            next.y = ground.point.y;
            PhaseTo(next);
            return true;
        }

        /// <summary>穿墙冲锋收尾：停在墙体 / 掩体里时吸附回与玩家连通的可走面，下一冲不会从墙里起步。</summary>
        private void SettlePhaseDash()
        {
            if (!IsFighting) return;
            Vector3 safe;
            Vector3 at = _boss.transform.position;
            if (!SpawnPositionHelper.TryResolveReachableFrom(at, CharacterMainControl.Main.transform.position, 3f,
                SpawnPositionHelper.DefaultLiftOffset, out safe)) return;
            Vector3 delta = safe - at;
            delta.y = 0f;
            if (delta.sqrMagnitude > 0.09f) PhaseTo(safe);
        }

        /// <summary>穿墙冲锋专用的位置重设（与 Reposition 换侧一起，是本模块仅有的两处 SetPosition）。</summary>
        private void PhaseTo(Vector3 position)
        {
            _boss.SetPosition(position);
            _blockedSeconds = 0f;
            _lastMotionPosition = position;
        }

        private IEnumerator BubbleBelch(int count)
        {
            float duration = SandstormChampionConfig.BubbleBelchSeconds;
            float elapsed = 0f;
            int emitted = 0;
            while (elapsed < duration && IsFighting && !HasPhaseTransition)
            {
                Vector3 player = CharacterMainControl.Main.transform.position;
                Vector3 away = _boss.transform.position - player;
                away.y = 0f;
                if (away.sqrMagnitude < 0.01f) away = Vector3.forward;
                MoveToward(player + away.normalized * SandstormChampionConfig.HoverDistance,
                    SandstormChampionConfig.HoverSpeed * 0.65f);
                FacePlayer();
                // 用已发数量追赶时间表：低帧率也完整发出 21 泡，不每帧只放一泡漏掉发数。
                while (emitted < count && elapsed >= emitted * duration / count)
                {
                    float angle = ((emitted % 3) - 1) * 22f;
                    SpawnOrb(_boss.transform.position + Vector3.up,
                        Quaternion.Euler(0f, angle, 0f) * -away.normalized, 1f);
                    emitted++;
                }
                elapsed += Time.deltaTime;
                yield return null;
            }
            if (IsFighting && !HasPhaseTransition)
                while (emitted++ < count) SpawnOrb(_boss.transform.position + Vector3.up,
                    CharacterMainControl.Main.transform.position - _boss.transform.position, 1f);
            StopMotion();
        }

        private IEnumerator OrbitAndOrbs(int count)
        {
            float duration = SandstormChampionConfig.SpiralBubbleSeconds;
            float elapsed = 0f;
            int emitted = 0;
            Vector3 radial = _boss.transform.position - CharacterMainControl.Main.transform.position;
            radial.y = 0f;
            if (radial.sqrMagnitude < 0.1f) radial = Vector3.forward;
            radial.Normalize();
            while (elapsed < duration && IsFighting && !HasPhaseTransition)
            {
                Vector3 player = CharacterMainControl.Main.transform.position;
                Vector3 desired = player + Quaternion.Euler(0f, 360f * elapsed / duration, 0f)
                    * radial * SandstormChampionConfig.SpiralOrbitRadius;
                MoveToward(desired, SandstormChampionConfig.SpiralOrbitSpeed);
                FacePlayer();
                // 每次用实际当前位置瞄准内侧玩家；理想轨道径向不代表受建筑阻挡后的实际发射方向。
                while (emitted < count && elapsed >= emitted * duration / count)
                {
                    Vector3 inward = CharacterMainControl.Main.transform.position - _boss.transform.position;
                    SpawnOrb(_boss.transform.position + Vector3.up, inward, 1.1f);
                    emitted++;
                }
                elapsed += Time.deltaTime;
                yield return null;
            }
            if (IsFighting && !HasPhaseTransition)
                while (emitted++ < count) SpawnOrb(_boss.transform.position + Vector3.up,
                    CharacterMainControl.Main.transform.position - _boss.transform.position, 1.1f);
            StopMotion();
        }

        private IEnumerator SummonTornadoes(bool cyclone)
        {
            if (!IsFighting || HasPhaseTransition) yield break;
            StopMotion();
            _boss.PopText(cyclone ? L10n.T("沙暴核心", "Sandstorm core") : L10n.T("双生沙卷", "Twin sand funnels"));
            _owner?.PlaySoundEffect(DragonKingConfig.Sound_RainbowSpawn);
            Vector3 center = CharacterMainControl.Main.transform.position;
            Vector3 radial = _boss.transform.position - center;
            radial.y = 0f;
            if (radial.sqrMagnitude < 0.01f) radial = Vector3.forward;
            Vector3 lateral = Vector3.Cross(Vector3.up, radial.normalized);
            if (cyclone) SpawnSeed(_boss.transform.position + Vector3.up * 1.4f, center, true);
            else
                for (int side = -1; side <= 1; side += 2)
                    SpawnSeed(_boss.transform.position + lateral * (side * 1.5f) + Vector3.up * 1.4f, center, false);
            yield return WaitForFightSeconds(cyclone ? 0.55f : 0.7f);
        }

        private IEnumerator SummonMinions()
        {
            if (!IsFighting || HasPhaseTransition || _phase == 3 || !_minions.CanSummon) yield break;
            _minionSummoning = true;
            _boss.PopText(L10n.T("星阙沙卫", "Astral sandguard"));
            _owner?.PlaySoundEffect(DragonKingConfig.Sound_RainbowSpawn);
            BossRushFxKit.PlayBurst(_boss.transform.position + Vector3.up,
                BossRushFxKit.Dust(SandstormChampionConfig.SandLight, 32));
            yield return new WaitForSeconds(SandstormChampionConfig.MinionSummonWindup);
            if (IsFighting && !HasPhaseTransition && _phase < 3) _minions.SummonWave(_phase);
            _minionSummoning = false;
        }

        private IEnumerator Transition()
        {
            Debug.Log(SandstormChampionConfig.LogPrefix + "进入阶段: phase=" + _phase);
            StopMotion();
            CancelNavigation();
            if (_warning != null) Destroy(_warning.gameObject);
            _warning = null;
            _boss.Health.SetInvincible(true);
            ApplyPhaseDefense();
            if (_phase == 3) _minions.DismissWave();
            _owner?.PlaySoundEffect(DragonKingConfig.Sound_Phase2);
            if (_body != null) _body.SetIntensity(_phase == 3 ? 1f : 0.5f);
            _boss.PopText(_phase == 3 ? L10n.T("沙暴无形", "Eye of the storm") : L10n.T("狂沙怒潮", "Raging sands"));
            // 原版转阶段是约一秒的无敌咆哮；已有沙暴按自己的寿命继续，不附加新星伤害。
            BossRushFxKit.PlayBurst(_boss.transform.position,
                BossRushFxKit.Dust(SandstormChampionConfig.SandLight, 40));
            PlayRoarInhale(_boss.transform.position);
            _novaWarning = SandstormChampionAssetManager.CreateNovaWarning(_boss.transform.position, SandstormChampionConfig.NovaRadius);
            yield return WaitForFightSeconds(SandstormChampionConfig.TransitionSeconds);
            if (_novaWarning != null) Destroy(_novaWarning);
            _novaWarning = null;
            if (!IsFighting) yield break;
            _boss.Health.SetInvincible(false);
            BossRushFxBurst burst = BossRushFxKit.Sparks(SandstormChampionConfig.Ember, 45);
            burst.SpeedMin = 6f;
            burst.SpeedMax = 10f;
            BossRushFxKit.PlayBurst(_boss.transform.position + Vector3.up, burst);
            PlayRoarRelease(_boss.transform.position);
            if (_ambience == null) _ambience = SandstormAmbience.Create();
            if (_phase == 3 && _body != null) _body.SetEyesOnly(true);
        }

        private IEnumerator Reposition()
        {
            if (!IsFighting) yield break;
            StopMotion();
            CancelNavigation();
            if (_body != null) { _body.SetCharge(false); _body.SetVisibility(0.2f); }
            yield return WaitForFightSeconds(0.22f);
            if (!IsFighting) yield break;
            Vector3 player = CharacterMainControl.Main.transform.position;
            // 上次冲锋方向的反侧，而不是当前位置的反侧，避免玩家追跑使换侧方向翻转。
            Vector3 away = _lastDashDirection.sqrMagnitude > 0.01f
                ? -_lastDashDirection : _boss.transform.position - player;
            away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = Vector3.forward;
            for (int i = 0; i < 8; i++)
            {
                Vector3 raw = player + Quaternion.Euler(0f, i * 45f, 0f) * away.normalized * 11f;
                Vector3 safe;
                if (!SpawnPositionHelper.TryResolveReachableFrom(raw, player, 1.2f,
                    SpawnPositionHelper.DefaultLiftOffset, out safe)) continue;
                _boss.SetPosition(safe);
                _blockedSeconds = 0f;
                _lastMotionPosition = safe;
                break;
            }
            FacePlayer();
            if (_body != null) _body.SetVisibility(1f);
            yield return WaitForFightSeconds(0.08f);
        }

        /// <summary>
        /// 咆哮前的「吸气」：方圆数米的砂粒与火星向 Boss 收拢，眼光爆亮；与转阶段一秒无敌同起。
        /// 只是表现，不改判定与时序。
        /// </summary>
        private void PlayRoarInhale(Vector3 at)
        {
            try
            {
                if (_body != null) _body.Flare(1f);
                BossRushFxBurst grains = BossRushFxKit.Sparks(SandstormChampionConfig.EyeColor, 40);
                grains.ShapeRadius = SandstormChampionConfig.NovaRadius;
                grains.ShellOnly = true;
                grains.SpeedMin = -7.5f;
                grains.SpeedMax = -7f;
                grains.LifeMin = 0.82f;
                grains.LifeMax = 0.9f;
                grains.Drag = 0f;
                grains.Stretch = 0.05f;
                grains.GrowTo = 1f;
                grains.FadeIn = 0.25f;
                BossRushFxKit.PlayBurst(at + Vector3.up * 1.4f, grains);
                BossRushFxBurst veil = BossRushFxKit.Dust(SandstormChampionConfig.Sand, 18);
                veil.Radial = false;
                veil.FlatOnGround = false;
                veil.ShapeRadius = SandstormChampionConfig.NovaRadius * 0.8f;
                veil.ShellOnly = true;
                veil.SpeedMin = -5.6f;
                veil.SpeedMax = -5.2f;
                veil.Drag = 0f;
                veil.GrowTo = 0.4f;
                veil.LifeMin = 0.9f;
                veil.LifeMax = 1f;
                veil.FadeIn = 0.3f;
                BossRushFxKit.PlayBurst(at + Vector3.up * 1.2f, veil);
                SandstormChampionAssetManager.ShakeNear(at, 0.1f, 25f);
            }
            catch { /* 表现失败不影响转阶段 */ }
        }

        /// <summary>咆哮释放：贴地砂环推到冲击圈外、眼位一记星芒、远近都能感到的一震。三阶段多一道砂身坍落。</summary>
        private void PlayRoarRelease(Vector3 at)
        {
            try
            {
                if (_body != null) _body.Flare(1f);
                Color ring = SandstormChampionConfig.Ember;
                ring.a = 0.75f;
                SandstormChampionAssetManager.PlaySandBurst(at, SandstormChampionConfig.NovaRadius, 36, ring);
                BossRushFxKit.PlayBurst(at + Vector3.up * (2.25f * CampaignTuning.FinalBossScale),
                    BossRushFxKit.Glint(SandstormChampionConfig.EyeColor, 3.2f, 0.22f));
                if (_phase == 3)
                {
                    BossRushFxBurst fall = BossRushFxKit.Dust(SandstormChampionConfig.SandDark, 24);
                    fall.Radial = false;
                    fall.FlatOnGround = false;
                    fall.ShapeRadius = 1.2f;
                    fall.SpeedMin = 0.5f;
                    fall.SpeedMax = 2f;
                    fall.Gravity = 1.1f;
                    fall.SizeMin = 0.9f;
                    fall.SizeMax = 1.6f;
                    fall.LifeMin = 0.9f;
                    fall.LifeMax = 1.4f;
                    BossRushFxKit.PlayBurst(at + Vector3.up * 2.4f, fall);
                }
                SandstormChampionAssetManager.ShakeNear(at, 0.38f, 32f);
            }
            catch { /* 同上 */ }
        }

        /// <summary>冲锋撞上玩家：一记顺冲锋方向喷出的砂浪 + 火星亮芯 + 重震。只在真实触发伤害的那一下播放。</summary>
        private static void PlayDashImpact(Vector3 at, Vector3 direction)
        {
            try
            {
                Vector3 point = at + Vector3.up;
                BossRushFxKit.PlayBurst(point, BossRushFxKit.Glint(SandstormChampionConfig.Ember, 1.6f, 0.1f));
                BossRushFxBurst spray = BossRushFxKit.Dust(SandstormChampionConfig.Sand, 12);
                spray.Radial = false;
                spray.FlatOnGround = false;
                spray.Cone = 30f;
                spray.Direction = direction + Vector3.up * 0.35f;
                spray.SpeedMin = 5f;
                spray.SpeedMax = 10f;
                spray.Drag = 4f;
                spray.SizeMin = 0.5f;
                spray.SizeMax = 0.9f;
                BossRushFxKit.PlayBurst(point, spray);
                BossRushFxBurst sparks = BossRushFxKit.Sparks(SandstormChampionConfig.Ember, 14);
                sparks.Cone = 35f;
                sparks.Direction = direction + Vector3.up * 0.3f;
                sparks.SpeedMin = 7f;
                sparks.SpeedMax = 13f;
                sparks.Stretch = 0.06f;
                BossRushFxKit.PlayBurst(point, sparks);
                SandstormChampionAssetManager.ShakeNear(at, 0.3f, 4f);
            }
            catch { /* 同上 */ }
        }

        /// <summary>
        /// Boss 受击：命中点炸开一小团砂与几粒火星，读得出「沙身被打散了一块」。
        /// 每 0.07 秒最多一次，霰弹 / 群伤不会刷出一片烟。
        /// </summary>
        private void OnBossHurt(DamageInfo info)
        {
            if (_stopped || _boss == null || Time.time < _nextHurtFxTime) return;
            _nextHurtFxTime = Time.time + 0.07f;
            try
            {
                Vector3 point = info.damagePoint;
                Vector3 center = _boss.transform.position + Vector3.up * 1.2f;
                if (point.sqrMagnitude < 0.0001f || (point - center).sqrMagnitude > 16f) point = center;
                Vector3 outward = point - center;
                outward.y = 0f;
                if (outward.sqrMagnitude < 0.01f) outward = -info.damageNormal;
                BossRushFxBurst puff = BossRushFxKit.Dust(SandstormChampionConfig.SandLight, 5);
                puff.Radial = false;
                puff.FlatOnGround = false;
                puff.SpeedMin = 1.5f;
                puff.SpeedMax = 3.2f;
                puff.SizeMin = 0.35f;
                puff.SizeMax = 0.65f;
                puff.LifeMin = 0.35f;
                puff.LifeMax = 0.6f;
                puff.Drag = 4f;
                if (outward.sqrMagnitude > 0.01f)
                {
                    puff.Cone = 50f;
                    puff.Direction = outward;
                }
                BossRushFxKit.PlayBurst(point, puff);
                BossRushFxBurst grains = BossRushFxKit.Sparks(SandstormChampionConfig.EyeColor, 5);
                grains.SpeedMin = 3f;
                grains.SpeedMax = 6f;
                grains.Gravity = 1f;
                BossRushFxKit.PlayBurst(point, grains);
            }
            catch { /* 表现失败不影响伤害 */ }
        }

        private void SpawnOrb(Vector3 at, Vector3 direction, float speed, bool homing = true)
        {
            _orbs.RemoveAll(IsExpiredOrb);
            if (_orbs.Count >= SandstormChampionConfig.MaxLiveOrbs) return;
            SandstormOrb orb = SandstormOrb.Spawn(this, at, direction, speed, homing);
            if (orb != null) _orbs.Add(orb);
        }

        internal void SpawnTornadoVolley(Vector3 at, bool cyclone)
        {
            if (!IsFighting) return;
            Vector3 direction = CharacterMainControl.Main.transform.position - at;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.01f) direction = Vector3.forward;
            // 一柱每次一只可击破沙鲨，柱自身严格控制总数为 6 / 12；不受沙卫在场影响。
            SpawnOrb(at + Vector3.up, direction.normalized,
                SandstormChampionConfig.SharkSpeed / SandstormChampionConfig.OrbSpeed * (cyclone ? 1.18f : 1f), false);
        }

        private void SpawnSeed(Vector3 at, Vector3 target, bool cyclone)
        {
            _seeds.RemoveAll(IsExpiredSeed);
            if (_seeds.Count >= SandstormChampionConfig.MaxLiveSeeds) return;
            SandstormCycloneSeed seed = SandstormCycloneSeed.Spawn(this, at, target, cyclone);
            if (seed != null) _seeds.Add(seed);
        }

        internal void ResolveCycloneSeed(Vector3 target, bool cyclone)
        {
            if (!IsFighting) return;
            Vector3 safe;
            if (!SpawnPositionHelper.TryResolveReachableFrom(target, CharacterMainControl.Main.transform.position,
                2f, 0.1f, out safe)) return;
            SpawnTornado(safe, cyclone);
            if (_ambience == null) _ambience = SandstormAmbience.Create();
            _stormEndsAt = Mathf.Max(_stormEndsAt, Time.time + SandstormChampionConfig.TornadoTelegraphSeconds
                + (cyclone ? SandstormChampionConfig.CycloneLifetime : SandstormChampionConfig.TornadoLifetime));
            ModBehaviour.DevLog(SandstormChampionConfig.LogPrefix + "沙暴种子落地: cyclone=" + cyclone
                + ", liveTornadoes=" + _tornadoes.Count);
        }

        private static bool IsExpiredSeed(SandstormCycloneSeed seed) { return seed == null || seed.IsDead; }

        private void SpawnTornado(Vector3 at, bool cyclone)
        {
            _tornadoes.RemoveAll(IsExpiredTornado);
            if (_tornadoes.Count >= SandstormChampionConfig.MaxLiveTornadoes) return;
            SandstormTornado tornado = SandstormTornado.Spawn(this, at, cyclone);
            if (tornado != null) _tornadoes.Add(tornado);
        }

        private static bool IsExpiredOrb(SandstormOrb orb) { return orb == null || orb.IsDead; }
        private static bool IsExpiredTornado(SandstormTornado tornado) { return tornado == null || tornado.IsDead; }

        private void UpdateEnrage()
        {
            Vector3 delta = CharacterMainControl.Main.transform.position - _arenaCenter;
            delta.y = 0f;
            bool previous = _enraged;
            if (delta.sqrMagnitude <= SandstormChampionConfig.ArenaReturnRadius * SandstormChampionConfig.ArenaReturnRadius)
            {
                _outsideSeconds = 0f;
                _enraged = false;
            }
            else if (!_enraged)
            {
                _outsideSeconds = delta.sqrMagnitude > SandstormChampionConfig.ArenaRadius * SandstormChampionConfig.ArenaRadius
                    ? _outsideSeconds + Time.deltaTime : 0f;
                if (_outsideSeconds >= SandstormChampionConfig.EnrageDelay) _enraged = true;
            }
            if (previous == _enraged) return;
            ApplyPhaseDefense();
            _boss.PopText(_enraged ? L10n.T("越界狂沙", "Storm enraged: return to the arena")
                : L10n.T("狂沙平息", "Storm calmed"));
            Debug.Log(SandstormChampionConfig.LogPrefix + "场地激怒=" + _enraged + ", distance=" + delta.magnitude.ToString("F1"));
        }

        private void ApplyPhaseDefense()
        {
            // 原版二阶段降低防御，第三阶段为零；鸭科夫用原生护甲属性承担相同角色。
            float factor = (_phase == 3 ? 0f : _phase == 2 ? 0.8f : 1f) * (_enraged ? 2f : 1f);
            // Stat.Recalculate 按 Order 排序，最终的 PercentageMultiply 把完整装备护甲乘以系数。
            // 不把已经含 modifier 的 Value 回填 BaseValue；-1 保证末阶段整份护甲为零。
            if (_bodyArmorModifier != null) _bodyArmorModifier.Value = factor - 1f;
            if (_headArmorModifier != null) _headArmorModifier.Value = factor - 1f;
        }

        private void TickContactDamage()
        {
            if (_boss.Health.Invincible) return;
            CharacterMainControl player = CharacterMainControl.Main;
            Vector3 delta = player.transform.position - _boss.transform.position;
            float height = Mathf.Abs(delta.y);
            delta.y = 0f;
            if (height <= 2f && delta.sqrMagnitude <= SandstormChampionConfig.ContactRadius * SandstormChampionConfig.ContactRadius
                && !BossSkillDamageRules.IsDodging(player))
                HurtPlayer(_phase == 3 ? SandstormChampionConfig.DashDamageP3
                    : _phase == 2 ? SandstormChampionConfig.DashDamageP2 : SandstormChampionConfig.DashDamage,
                    _boss.transform.position);
        }

        private void MoveToward(Vector3 target, float speed)
        {
            if (!IsFighting) return;
            Vector3 from = _boss.transform.position;
            if (_motionCommanded)
            {
                Vector3 moved = from - _lastMotionPosition;
                moved.y = 0f;
                _blockedSeconds = moved.sqrMagnitude < 0.0004f ? _blockedSeconds + Time.deltaTime : 0f;
            }
            Vector3 direction = target - from;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.04f) { StopMotion(); return; }
            bool needsPath = _blockedSeconds >= 0.14f || (_wallMask != 0 && Physics.SphereCast(from + Vector3.up,
                0.35f, direction.normalized, out RaycastHit obstacle, direction.magnitude, _wallMask,
                QueryTriggerInteraction.Ignore));
            if (needsPath)
            {
                RequestNavigation(target);
                while (_navigationIndex < _navigationPath.Count)
                {
                    Vector3 waypoint = _navigationPath[_navigationIndex] - from;
                    waypoint.y = 0f;
                    if (waypoint.sqrMagnitude > 0.36f) { direction = waypoint; break; }
                    _navigationIndex++;
                }
                // 等待首条路径时尝试切线滑行；不停止整轮战斗，更不把卡住当作瞬移理由。
                if (_navigationIndex >= _navigationPath.Count)
                {
                    Vector3 tangent = Vector3.Cross(Vector3.up, direction.normalized);
                    if (_wallMask == 0 || !Physics.SphereCast(from + Vector3.up, 0.35f, tangent,
                        out obstacle, 0.8f, _wallMask, QueryTriggerInteraction.Ignore)) direction = tangent;
                    else direction = -tangent;
                }
            }
            CommandMotion(direction.normalized * Mathf.Min(speed, direction.magnitude / Mathf.Max(0.01f, Time.deltaTime)));
        }

        private bool CommandMotion(Vector3 velocity)
        {
            if (_boss == null || Time.deltaTime <= 0f) return false;
            _moveAttemptSeconds += Time.deltaTime;
            Vector3 from = _boss.transform.position;
            velocity *= Mathf.Min(1f, 0.05f / Time.deltaTime);
            Vector3 next = from + velocity * Time.deltaTime;
            RaycastHit ground;
            // 连续移动只判断前方有没有真实地板，贴地与墙碰撞交给官方 ECM2。
            // 旧实现每米要求最近 A* 节点在 0.8 m 内，会把粗网格上的正常地板误判成不可走。
            if (_groundMask == 0 || !Physics.Raycast(next + Vector3.up * 2f, Vector3.down, out ground, 4f,
                _groundMask, QueryTriggerInteraction.Ignore) || Mathf.Abs(ground.point.y - from.y) > 1.7f)
            {
                _blockedSeconds += Time.deltaTime;
                StopMotion();
                return false;
            }
            _lastMotionPosition = from;
            _motionCommanded = true;
            _boss.SetForceMoveVelocity(velocity);
            return true;
        }

        private void StopMotion()
        {
            _motionCommanded = false;
            if (_boss == null) return;
            _boss.SetMoveInput(Vector3.zero);
            _boss.SetForceMoveVelocity(Vector3.zero);
        }

        private void RequestNavigation(Vector3 target)
        {
            if (_navigator == null || _pathPending || Time.time < _nextPathRequest || AstarPath.active == null
                || AstarPath.active.isScanning) return;
            _nextPathRequest = Time.time + SandstormChampionConfig.RepathSeconds;
            _pathPending = true;
            int revision = _navigationRevision;
            _navigator.StartPath(_boss.transform.position, target, path =>
            {
                if (this == null || revision != _navigationRevision || !IsFighting) return;
                _pathPending = false;
                _navigationPath.Clear();
                _navigationIndex = 0;
                if (path == null || path.error || path.vectorPath == null) return;
                // 回调在 Unity 主线程交付；复制路点，不在 Seeker 回收 Path 后继续引用其池内 List。
                for (int i = 0; i < path.vectorPath.Count; i++) _navigationPath.Add(path.vectorPath[i]);
            });
        }

        private void CancelNavigation()
        {
            unchecked { _navigationRevision++; }
            _pathPending = false;
            _navigationPath.Clear();
            _navigationIndex = 0;
            if (_navigator != null) _navigator.CancelCurrentPathRequest(true);
        }

        private void FacePlayer()
        {
            if (IsFighting) Face(CharacterMainControl.Main.transform.position - _boss.transform.position);
        }

        private void Face(Vector3 direction)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.001f || _boss == null) return;
            if (_boss.movementControl != null) _boss.movementControl.ForceTurnTo(direction.normalized);
        }

        private static float DistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            p.y = a.y = b.y = 0f;
            Vector3 d = b - a;
            float t = d.sqrMagnitude > 0.00001f ? Mathf.Clamp01(Vector3.Dot(p - a, d) / d.sqrMagnitude) : 0f;
            return Vector3.Distance(p, a + d * t);
        }

        internal void HurtPlayer(float damage, Vector3 point)
        {
            if (!IsFighting || damage <= 0f || Time.time < _nextDamageTime) return;
            CharacterMainControl player = CharacterMainControl.Main;
            if (!Team.IsEnemy(_boss.Team, player.Team) || player.mainDamageReceiver == null) return;
            _nextDamageTime = Time.time + SandstormChampionConfig.DamageGraceSeconds;
            DamageInfo info = new DamageInfo(_boss);
            info.damageType = DamageTypes.normal;
            info.damageValue = damage * BossSkillDamageRules.ResolveMeleeDamageScale(_boss, _baseline) * (_enraged ? 2f : 1f);
            info.damagePoint = point;
            info.damageNormal = (player.transform.position - point).normalized;
            info.crit = -1;
            player.mainDamageReceiver.Hurt(info);
        }

        private void OnBeforeSpawnLoot(DamageInfo damageInfo)
        {
            if (_reward == null || _boss == null) return;
            try
            {
                Inventory inventory = _boss.CharacterItem != null ? _boss.CharacterItem.Inventory : null;
                if (inventory != null)
                {
                    int contentCount = inventory.Content != null ? inventory.Content.Count : 0;
                    inventory.SetCapacity(Mathf.Max(inventory.Capacity, contentCount + 1));
                    if (inventory.AddAndMerge(_reward, 0)) { _reward = null; return; }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(SandstormChampionConfig.LogPrefix + "奖励箱插入失败，改为地面掉落: " + e.Message);
            }
            try
            {
                _reward.Drop(_boss.transform.position + Vector3.up * 0.2f, true, Vector3.forward, 30f);
                _reward = null;
            }
            catch (Exception e)
            {
                Debug.LogWarning(SandstormChampionConfig.LogPrefix + "星阙地面掉落失败: " + e.Message);
            }
        }

        private void OnBossDead(DamageInfo damageInfo)
        {
            if (_stopped) return;
            CodexKillCollector.OnKnownBossDead(_boss != null ? _boss.Health : null, damageInfo, SandstormChampionBoss.NameKey);
            StopBattle(true);
        }

        private void ClearHazards()
        {
            if (_warning != null) Destroy(_warning.gameObject);
            _warning = null;
            if (_novaWarning != null) Destroy(_novaWarning);
            _novaWarning = null;
            foreach (SandstormOrb orb in _orbs) if (orb != null) Destroy(orb.gameObject);
            foreach (SandstormTornado tornado in _tornadoes) if (tornado != null) Destroy(tornado.gameObject);
            foreach (SandstormCycloneSeed seed in _seeds) if (seed != null) Destroy(seed.gameObject);
            _orbs.Clear();
            _tornadoes.Clear();
            _seeds.Clear();
        }

        internal void StopBattle(bool death)
        {
            if (_stopped) return;
            _stopped = true;
            _fightRoutine = null;
            if (_boss != null && _boss.Health != null) _boss.Health.SetInvincible(false);
            if (_bodyArmorModifier != null) _bodyArmorModifier.RemoveFromTarget();
            if (_headArmorModifier != null) _headArmorModifier.RemoveFromTarget();
            _bodyArmorModifier = null;
            _headArmorModifier = null;
            StopMotion();
            CancelNavigation();
            if (_minions != null) _minions.Shutdown();
            try { BossRushAudioManager.Instance?.StopBossBGM(BossBgmKeys.PhantomWitch, _boss); }
            catch (Exception e) { ModBehaviour.DevLog(SandstormChampionConfig.LogPrefix + "BGM 收尾失败: " + e.Message); }
            StopAllCoroutines();
            ClearHazards();
            if (_ambience != null) Destroy(_ambience.gameObject);
            _ambience = null;
            if (_body != null)
            {
                if (death) _body.Dissipate();
                else Destroy(_body.gameObject);
            }
            _body = null;
            if (_subscribed && _boss != null)
            {
                _boss.BeforeCharacterSpawnLootOnDead -= OnBeforeSpawnLoot;
                if (_boss.Health != null)
                {
                    _boss.Health.OnDeadEvent.RemoveListener(OnBossDead);
                    _boss.Health.OnHurtEvent.RemoveListener(OnBossHurt);
                }
            }
            _subscribed = false;
        }

        private void OnDestroy()
        {
            StopBattle(false);
            if (_reward != null) Destroy(_reward.gameObject);
            _reward = null;
            if (_preset != null) Destroy(_preset);
            _preset = null;
            _isCurrent = null;
            _boss = null;
            _owner = null;
        }
    }
}
