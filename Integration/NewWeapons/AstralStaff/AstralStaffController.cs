// ============================================================================
// AstralStaffController.cs - 星阙运行时：棍势豆、蓄势、三档重击、伤害与打击感
// ============================================================================
// 生命周期：NewWeaponRuntime.Initialize 建一个常驻物体挂本组件，CleanupOnDestroy 销毁。
// 门控（AGENTS.md §4.12）：每帧第一件事是 NewWeaponEquipState.IsHolding（O(1) 缓存）；
//   没拿星阙就收掉 HUD 与状态后早返，蓄势、伤害、HUD、表现全部不跑。
// 输入：左键是官方轻击（CA_Attack），本组件只在命中时记段；右键（官方 ADS 动作）按住蓄势、
//   松开出重击。拿着星阙时每帧把官方 ADS 压回 false（焚皇断界戟同款），避免右键同时进瞄准。
// 打击感：顿帧走官方 TimeScaleManager.EnterBulletTime（暂停 / 拍照会正确覆盖），
//   震屏走官方 CameraShaker，击退走目标自己的 Movement.SetForceMoveVelocity（撞墙会停）。
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Duckov.Utilities;
using UnityEngine;

namespace BossRush
{
    internal sealed class AstralStaffController : MonoBehaviour
    {
        private static AstralStaffController instance;
        internal static AstralStaffController Instance { get { return instance; } }

        // ---- 棍势状态（只属于当前手持，换手 / 切图 / 死亡清零，不进存档） ----
        private int beans;
        private int lightHits;
        private int swingSerial;
        private int creditedSwing = -1;
        private int lightImpactCount;
        private bool charging;
        private float pressTime = -1f;
        private float chargeProgress;
        private bool heavyRunning;
        private Coroutine heavyRoutine;
        private bool wasHolding;
        private bool dealingHeavyDamage;

        private AstralStaffBeanHud hud;
        private Transform transientFxRoot;
        private DuckovItemAgent activeAgent;

        // ---- 输入（官方 ADS 动作，反射读取：编译清单没有 InputSystem 引用） ----
        private object adsAction;
        private MethodInfo readButtonMethod;
        private MethodInfo wasPressedMethod;
        private MethodInfo wasReleasedMethod;
        private bool adsResolved;
        private float nextAdsResolveTime;
        private bool lastAdsHeld;

        // ---- 击退 ----
        private struct Knock
        {
            public CharacterMainControl Target;
            public Vector3 Velocity;
            public float Until;
        }
        private readonly List<Knock> knocks = new List<Knock>(16);

        // ---- 伤害去重 ----
        private static readonly Collider[] OverlapBuffer = new Collider[128];
        private readonly HashSet<int> hitHealthIds = new HashSet<int>();
        private static bool hurtSubscribed;
        private static bool hitStopFailureReported;
        private static bool tutorialShown;
        private float nextHintTime;

        private static readonly object[] NoArgs = new object[0];

        // ====================================================================
        // 生命周期
        // ====================================================================

        internal static void EnsureCreated()
        {
            if (instance != null) return;
            GameObject go = new GameObject("AstralStaffController");
            DontDestroyOnLoad(go);
            go.AddComponent<AstralStaffController>();
        }

        internal static void DestroyInstance()
        {
            UnsubscribeHurt();
            if (instance != null)
            {
                Destroy(instance.gameObject);
                instance = null;
            }
        }

        private void Awake()
        {
            instance = this;
            hud = gameObject.AddComponent<AstralStaffBeanHud>();
            SubscribeHurt();
        }

        private void OnDestroy()
        {
            ResetState();
            UnsubscribeHurt();
            if (instance == this) instance = null;
        }

        private static void SubscribeHurt()
        {
            if (hurtSubscribed) return;
            Health.OnHurt += OnAnyHurt;
            CharacterMainControl.OnMainCharacterChangeHoldItemAgentEvent += OnHoldChanged;
            hurtSubscribed = true;
        }

        private static void UnsubscribeHurt()
        {
            if (!hurtSubscribed) return;
            Health.OnHurt -= OnAnyHurt;
            CharacterMainControl.OnMainCharacterChangeHoldItemAgentEvent -= OnHoldChanged;
            hurtSubscribed = false;
        }

        private static void OnHoldChanged(CharacterMainControl character, DuckovItemAgent agent)
        {
            if (instance != null && instance.activeAgent != agent) instance.ResetState();
        }

        // 临时特效统一属于当前手持会话；换手、死亡、切图可以立即撤销，最多 64 个对象。
        internal Transform GetTransientFxRoot()
        {
            if (!wasHolding || !NewWeaponEquipState.IsHolding(AstralStaffConfig.TypeId)) return null;
            if (transientFxRoot == null)
            {
                transientFxRoot = new GameObject("AstralStaff_TransientFx").transform;
                transientFxRoot.SetParent(transform, false);
            }
            return transientFxRoot.childCount < 64 ? transientFxRoot : null;
        }

        /// <summary>切图：官方会重建主角，蓄势与豆数清零，输入动作重新解析。</summary>
        internal void OnSceneChanged()
        {
            ResetState();
            adsResolved = false;
            adsAction = null;
            nextAdsResolveTime = 0f;
        }

        private void ResetState()
        {
            if (heavyRoutine != null)
            {
                StopCoroutine(heavyRoutine);
                heavyRoutine = null;
            }
            heavyRunning = false;
            dealingHeavyDamage = false;
            charging = false;
            pressTime = -1f;
            chargeProgress = 0f;
            lastAdsHeld = false;
            beans = 0;
            lightHits = 0;
            creditedSwing = -1;
            lightImpactCount = 0;
            knocks.Clear();
            hitHealthIds.Clear();
            if (hud != null) hud.HideAll();
            if (transientFxRoot != null)
            {
                transientFxRoot.gameObject.SetActive(false);
                Destroy(transientFxRoot.gameObject);
                transientFxRoot = null;
            }
            activeAgent = null;
            wasHolding = false;
        }

        internal static void ResetStaticCaches()
        {
            UnsubscribeHurt();
            hitStopFailureReported = false;
            tutorialShown = false;
            Array.Clear(OverlapBuffer, 0, OverlapBuffer.Length);
        }

        /// <summary>重击进行中官方轻击不许起手（AstralStaffAttackPatch 前缀读它）。</summary>
        internal static bool BlocksLightAttack
        {
            get { return instance != null && (instance.heavyRunning || instance.pressTime >= 0f); }
        }

        /// <summary>官方轻击起手（AstralStaffAttackPatch 后缀调用）：开一个新的挥击编号。</summary>
        internal static void NotifyLightSwing()
        {
            if (instance == null) return;
            instance.swingSerial++;
            instance.lightImpactCount = 0;
        }

        // ====================================================================
        // 每帧
        // ====================================================================

        private void Update()
        {
            if (!ModBehaviour.CanRunGameplayRuntimeCached())
            {
                if (wasHolding) ResetState();
                return;
            }

            bool holding = NewWeaponEquipState.IsHolding(AstralStaffConfig.TypeId);
            if (!holding)
            {
                if (wasHolding)
                {
                    wasHolding = false;
                    ResetState();
                }
                if (knocks.Count > 0) TickKnockbacks();
                return;
            }
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null || player.Health == null || player.Health.IsDead)
            {
                ResetState();
                return;
            }

            if (activeAgent != player.CurrentHoldItemAgent) ResetState();
            activeAgent = player.CurrentHoldItemAgent;
            if (!wasHolding && !tutorialShown)
            {
                tutorialShown = true;
                Hint(player, L10n.T("星阙：左键打中攒星豆 · 按住右键蓄势 · 松开放重击",
                    "Astral Staff: land hits to build Focus · hold RMB to charge · release to strike"));
            }
            wasHolding = true;

            SuppressVanillaAds();
            TickInput(player);
            TickKnockbacks();

            float shown = beans >= AstralStaffConfig.MaxBeans
                ? AstralStaffConfig.MaxBeans
                : beans + (charging ? chargeProgress : lightHits / (float)AstralStaffConfig.LightHitsPerBean);
            AstralStaffHandVisual hand = AstralStaffHandVisual.Current;
            if (hand != null) hand.SetFocus(shown, charging);
            if (hud != null) hud.Tick(player, shown, chargeProgress, charging);
        }

        private void LateUpdate()
        {
            if (wasHolding) SuppressVanillaAds();
        }

        private void TickInput(CharacterMainControl player)
        {
            bool allowed = IsGameplayInputAllowed() && !player.Dashing;
            bool held, pressed, released;
            ReadAds(out held, out pressed, out released);

            if (!allowed)
            {
                // 打开背包 / 暂停 / 翻滚：蓄势作废，已攒的豆保留；松手不会在回来时自动出招
                charging = false;
                pressTime = -1f;
                chargeProgress = 0f;
                return;
            }

            if (heavyRunning) return;

            if (pressed && pressTime < 0f)
            {
                pressTime = Time.time;
            }

            if (held && pressTime >= 0f)
            {
                if (!charging && Time.time - pressTime >= AstralStaffConfig.ChargeStartDelay)
                {
                    charging = true;
                    chargeProgress = lightHits / (float)AstralStaffConfig.LightHitsPerBean;
                }
                if (charging && beans < AstralStaffConfig.MaxBeans)
                {
                    chargeProgress += Time.deltaTime / AstralStaffConfig.ChargeSecondsPerBean;
                    if (chargeProgress >= 1f)
                    {
                        chargeProgress = 0f;
                        AddBean(player, true);
                    }
                }
            }

            if (released && pressTime >= 0f)
            {
                bool wasCharging = charging;
                pressTime = -1f;
                charging = false;
                chargeProgress = 0f;
                if (beans >= 1)
                {
                    if (player.CurrentStamina < HeavyStamina(beans))
                    {
                        AstralStaffSound.PostFizzle(player);
                        HintThrottled(player, L10n.T("体力不足，星豆保留", "Not enough stamina - Focus kept"));
                    }
                    else StartHeavy(player, beans);
                }
                else if (!wasCharging)
                {
                    HintThrottled(player, L10n.T("没有星豆：先用左键打中敌人，或按住右键蓄势",
                        "No Focus yet: land light hits, or hold right-click to charge"));
                }
            }
        }

        private void AddBean(CharacterMainControl player, bool fromCharge)
        {
            if (beans >= AstralStaffConfig.MaxBeans) return;
            if (fromCharge) lightHits = 0;
            beans++;
            if (hud != null) hud.PopBean(beans - 1);
            try
            {
                Vector3 at = player.transform.position + Vector3.up * 1.8f - player.transform.forward * 0.38f;
                AstralStaffFx.PlayHitSpark(at, beans >= AstralStaffConfig.MaxBeans ? 0.9f : 0.55f);
                AstralStaffSigil.Play(player.transform.position + Vector3.up * 1.25f, AimDirection(player),
                    0.4f + beans * 0.14f, beans, 0.12f, 0.22f, player.transform, true);
                AstralStaffFx.PlayFlash(at, AstralStaffConfig.Gold, beans >= AstralStaffConfig.MaxBeans ? 0.9f : 0.5f, 2.2f, 0.16f);
                if (beans >= AstralStaffConfig.MaxBeans)
                {
                    // 满势：脚下一圈金环撑开，背后星豆处一记星芒，读得出「三豆到了」。
                    Color ring = AstralStaffConfig.Gold;
                    ring.a = 0.6f;
                    AstralStaffFx.PlayBurst(player.transform.position + Vector3.up * 0.06f,
                        BossRushFxKit.Shockwave(ring, 0.8f, 4.2f, 0.35f, true));
                    AstralStaffFx.PlayBurst(at, BossRushFxKit.Glint(AstralStaffConfig.CoreWhite, 0.9f, 0.14f));
                }
                // 攒豆音（参照《黑神话：悟空》棍势）：一豆、二豆是音高递进的清脆「叮」，三豆满是带余韵的「铮」。
                AstralStaffSound.PostFocus(player, beans);
                AstralStaffHandVisual hand = AstralStaffHandVisual.Current;
                if (hand != null) hand.SetSurge(0.18f + 0.12f * beans);
            }
            catch { /* 表现失败不影响豆数 */ }
        }

        // ====================================================================
        // 轻击记段
        // ====================================================================

        private static void OnAnyHurt(Health target, DamageInfo info)
        {
            // 早返：非星阙伤害零开销
            if (info.fromWeaponItemID != AstralStaffConfig.TypeId || info.isFromBuffOrEffect || info.finalDamage <= 0f) return;
            AstralStaffController self = instance;
            if (self == null || self.dealingHeavyDamage || target == null) return;
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null || info.fromCharacter != player) return;
            if (!self.wasHolding || !NewWeaponEquipState.IsHolding(AstralStaffConfig.TypeId)
                || !Team.IsEnemy(player.Team, target.team)) return;

            Vector3 aim = AimDirection(player);
            // 三段连招的第三段是收尾重段：命中表现升一档、补一记相机冲量并把目标往前顶。
            bool finisher = AstralStaffAttackPatch.CurrentComboStep == 2;
            bool killed = target.IsDead;
            try
            {
                if (self.lightImpactCount < AstralStaffConfig.MaxHitFxPerAttack)
                {
                    AstralStaffFx.PlayContact(info.damagePoint, aim, finisher ? 1 : 0);
                    if (killed) AstralStaffFx.PlayKillBloom(info.damagePoint, aim);
                    self.lightImpactCount++;
                }
            }
            catch { /* 同上 */ }
            if (killed)
            {
                // 击杀多停一拍；官方 EnterBulletTime 取最大值，与本次挥击的顿帧不叠加。
                try { GameManager.TimeScaleManager.EnterBulletTime(AstralStaffConfig.HitStopKill); }
                catch (Exception e) { ReportHitStopFailure(e); }
            }
            else
            {
                self.AddKnockback(target.TryGetCharacter(), aim * (finisher
                    ? AstralStaffConfig.LightFinisherKnockback : AstralStaffConfig.LightKnockback));
            }
            if (killed) AstralStaffSound.PostCustom(AstralStaffSound.KillFile);

            // 一次挥击打中几个敌人都只记一段
            if (self.creditedSwing == self.swingSerial) return;
            self.creditedSwing = self.swingSerial;
            // 多目标的一次挥击只触发一次顿帧；命中音由官方 HitMarker 的 Health 回调播放。
            try
            {
                GameManager.TimeScaleManager.EnterBulletTime(finisher
                    ? AstralStaffConfig.HitStopLightFinisher : AstralStaffConfig.HitStopLight);
            }
            catch (Exception e) { ReportHitStopFailure(e); }
            Shake(aim, finisher ? AstralStaffConfig.ShakeLightFinisher : AstralStaffConfig.ShakeLight, false);
            AstralStaffSound.PostCustom(finisher ? AstralStaffSound.HitFinisherFile : AstralStaffSound.HitFile);
            AstralStaffHandVisual hand = AstralStaffHandVisual.Current;
            if (hand != null) hand.SetSurge(finisher ? 0.55f : 0.35f);
            if (self.beans >= AstralStaffConfig.MaxBeans) return;
            self.lightHits += finisher ? 2 : 1;
            if (self.lightHits >= AstralStaffConfig.LightHitsPerBean)
            {
                // 收尾段记两段时可能溢出一段，余数留给下一豆。
                self.lightHits -= AstralStaffConfig.LightHitsPerBean;
                self.AddBean(player, false);
            }
        }

        // ====================================================================
        // 重击
        // ====================================================================

        private void StartHeavy(CharacterMainControl player, int level)
        {
            level = Mathf.Clamp(level, 1, AstralStaffConfig.MaxBeans);
            if (player.CurrentAction != null && player.CurrentAction.Running && !(player.CurrentAction is CA_Attack)) return;
            if (player.CurrentStamina < HeavyStamina(level)) return;
            heavyRunning = true;
            heavyRoutine = StartCoroutine(Guard(ExecuteHeavy(player, level)));
        }

        private static float HeavyStamina(int level)
        {
            return level == 1 ? AstralStaffConfig.SweepStamina
                : level == 2 ? AstralStaffConfig.SpinStamina : AstralStaffConfig.StarfallStamina;
        }

        private IEnumerator ExecuteHeavy(CharacterMainControl player, int level)
        {
            // 官方轻击已经起手时，让它先完整结算，避免同帧轻重叠伤。
            while (player != null && player.CurrentAction != null && player.CurrentAction.Running)
            {
                if (PlayerGone(player)) yield break;
                yield return null;
            }
            if (PlayerGone(player) || player.CurrentStamina < HeavyStamina(level)) yield break;
            beans = 0;
            lightHits = 0;
            // 出招那一下就要有「蓄满放出」的爆发声；三豆星陨更厚、更长。命中的重击声另由真实伤害触发。
            AstralStaffSound.PostCustom(level >= AstralStaffConfig.MaxBeans
                ? AstralStaffSound.ReleaseMaxFile : AstralStaffSound.ReleaseFile);
            if (level == 1) yield return RunSweep(player);
            else if (level == 2) yield return RunSpin(player);
            else yield return RunStarfall(player);
        }

        /// <summary>重击协程外壳：无论正常结束还是中途异常都复位 heavyRunning。</summary>
        private IEnumerator Guard(IEnumerator body)
        {
            Stack<IEnumerator> stack = new Stack<IEnumerator>();
            stack.Push(body);
            try
            {
                while (stack.Count > 0)
                {
                    object current = null;
                    bool moved = false;
                    bool failed = false;
                    IEnumerator top = stack.Peek();
                    try { moved = top.MoveNext(); if (moved) current = top.Current; }
                    catch (Exception e)
                    {
                        ModBehaviour.DevLog(AstralStaffConfig.LogPrefix + " [WARNING] 重击异常: " + e.Message);
                        failed = true;
                    }
                    if (failed) break;
                    if (!moved)
                    {
                        stack.Pop();
                        IDisposable completed = top as IDisposable;
                        if (completed != null) completed.Dispose();
                        continue;
                    }
                    IEnumerator nested = current as IEnumerator;
                    if (nested != null) { stack.Push(nested); continue; }
                    yield return current;
                }
            }
            finally
            {
                while (stack.Count > 0)
                {
                    IDisposable unfinished = stack.Pop() as IDisposable;
                    if (unfinished != null) unfinished.Dispose();
                }
                heavyRunning = false;
                dealingHeavyDamage = false;
                heavyRoutine = null;
            }
        }

        private static Vector3 AimDirection(CharacterMainControl player)
        {
            Vector3 dir = player.CurrentAimDirection;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f)
            {
                dir = player.transform.forward;
                dir.y = 0f;
            }
            return dir.sqrMagnitude < 0.0001f ? Vector3.forward : dir.normalized;
        }

        private static void FaceAndPush(CharacterMainControl player, Vector3 dir, float speed)
        {
            try
            {
                if (player.movementControl != null) player.movementControl.ForceTurnTo(dir);
                if (speed > 0f) player.SetForceMoveVelocity(dir * speed);
            }
            catch { /* 角色未就绪 */ }
        }

        private static bool PlayerGone(CharacterMainControl player)
        {
            return player == null || player.Health == null || player.Health.IsDead
                || !ModBehaviour.CanRunGameplayRuntimeCached() || player.Dashing
                || !IsGameplayInputAllowed() || !NewWeaponEquipState.IsHolding(AstralStaffConfig.TypeId);
        }

        // ---------------- 一豆 · 流星横扫 ----------------

        private IEnumerator RunSweep(CharacterMainControl player)
        {
            Vector3 dir = AimDirection(player);
            UseStamina(player, AstralStaffConfig.SweepStamina);
            AstralStaffSigil.Play(player.transform.position + Vector3.up * 0.06f, dir, 1.15f, 1,
                AstralStaffConfig.SweepWindup, 0.18f, player.transform);
            AstralStaffHandVisual hand = AstralStaffHandVisual.Current;
            if (hand != null) hand.SetSurge(0.7f);

            float t = 0f;
            while (t < AstralStaffConfig.SweepWindup)
            {
                if (PlayerGone(player)) yield break;
                FaceAndPush(player, dir, 5f);
                t += Time.deltaTime;
                yield return null;
            }

            if (PlayerGone(player)) yield break;
            Vector3 center = player.transform.position + Vector3.up * 0.9f;
            AstralStaffSound.PostSwing(player, 1);
            AstralStaffSound.PostCustom(AstralStaffSound.SwingFile);
            AstralStaffFx.PlaySweepArt(player.transform.position, dir, AstralStaffConfig.SweepRadius,
                AstralStaffConfig.SweepHalfAngle, AstralStaffConfig.Gold);

            int hits = DealDamage(player, center, dir, AstralStaffConfig.SweepRadius, AstralStaffConfig.SweepHalfAngle,
                0f, 0f, AstralStaffConfig.SweepDamageMultiplier, AstralStaffConfig.SweepKnockback, false, 1);
            PlayImpactFeedback(dir, hits, 1);

            yield return Recovery(player, AstralStaffConfig.SweepRecovery);
        }

        // ---------------- 二豆 · 双龙回旋 ----------------

        private IEnumerator RunSpin(CharacterMainControl player)
        {
            Vector3 dir = AimDirection(player);
            UseStamina(player, AstralStaffConfig.SpinStamina);
            AstralStaffHandVisual hand = AstralStaffHandVisual.Current;
            if (hand != null) hand.SetSurge(0.85f);

            float t = 0f;
            bool firstDone = false;
            bool secondDone = false;
            float spinAngle = 0f;
            AstralStaffSigil.Play(player.transform.position + Vector3.up * 0.065f, dir, 1.55f, 2,
                AstralStaffConfig.SpinFirstHit, 0.2f, player.transform);
            while (t < AstralStaffConfig.SpinSecondHit + 0.05f)
            {
                if (PlayerGone(player)) yield break;
                // 两圈原地回旋：朝向跟着转，看得出人在转
                spinAngle += Time.deltaTime * 1500f;
                FaceAndPush(player, Quaternion.Euler(0f, spinAngle, 0f) * dir, 0f);

                Vector3 center = player.transform.position + Vector3.up * 0.95f;
                if (!firstDone && t >= AstralStaffConfig.SpinFirstHit)
                {
                    firstDone = true;
                    AstralStaffSound.PostSwing(player, 2);
                    AstralStaffSound.PostCustom(AstralStaffSound.SwingFile);
                    AstralStaffFx.PlaySpinArt(player.transform.position, dir, AstralStaffConfig.SpinRadius, false, player.transform);
                    int hits = DealDamage(player, center, dir, AstralStaffConfig.SpinRadius, 180f, 0f, 0f,
                        AstralStaffConfig.SpinDamageMultiplier, 4f, false, 2);
                    PlayImpactFeedback(dir, hits, 2);
                    // 两次旋转之间再收紧一道较小的符轮，读得出第二次发力。
                    AstralStaffSigil.Play(player.transform.position + Vector3.up * 0.08f, -dir, 1.3f, 2,
                        AstralStaffConfig.SpinSecondHit - AstralStaffConfig.SpinFirstHit, 0.2f, player.transform);
                }
                if (!secondDone && t >= AstralStaffConfig.SpinSecondHit)
                {
                    secondDone = true;
                    AstralStaffSound.PostSwing(player, 2, true);
                    AstralStaffSound.PostCustom(AstralStaffSound.SwingFile);
                    // 第二段：青色反向回旋 + 龙身 + 向内收拢的青环（卷近）。
                    AstralStaffFx.PlaySpinArt(player.transform.position, dir, AstralStaffConfig.SpinRadius, true, player.transform);
                    // 第二段把周围的敌人往里卷（负击退 = 拉近）
                    int hits = DealDamage(player, center, dir, AstralStaffConfig.SpinRadius + 0.2f, 180f, 0f, 0f,
                        AstralStaffConfig.SpinDamageMultiplier, -AstralStaffConfig.SpinPullSpeed, true, 2);
                    PlayImpactFeedback(-dir, hits, 2);
                }
                t += Time.deltaTime;
                yield return null;
            }
            FaceAndPush(player, dir, 0f);
            yield return Recovery(player, AstralStaffConfig.SpinRecovery);
        }

        // ---------------- 三豆 · 星陨天崩 ----------------

        private IEnumerator RunStarfall(CharacterMainControl player)
        {
            Vector3 dir = AimDirection(player);
            UseStamina(player, AstralStaffConfig.StarfallStamina);
            AstralStaffHandVisual hand = AstralStaffHandVisual.Current;
            if (hand != null) hand.SetSurge(1f);
            // 起跳前收束：背后星纹与地面细环，保留中心视野。
            Vector3 start = player.transform.position;
            AstralStaffSigil.Play(start + Vector3.up * 1.35f - dir * 0.35f, dir, 1.55f, 3,
                AstralStaffConfig.StarfallImpactTime, 0.24f, player.transform, true);
            AstralStaffRibbon.Play(start + Vector3.up * 0.06f, dir, 360f, 1.25f, 1.31f,
                new Color(0.95f, 0.73f, 0.36f, 0.38f), 0.04f, 0f, 0.3f, 0.2f, 0.55f, null);
            AstralStaffFx.PlayDust(start, 7, 1.3f);

            // 巨型光棍：从身后高处抡下，落在正前方
            Vector3[] staffPoints = new Vector3[2];
            AstralStaffBeam giant = AstralStaffBeam.Create(staffPoints, 0.065f, 0.32f, new Color(0.95f, 0.73f, 0.36f, 0.78f),
                AstralStaffConfig.StarfallImpactTime + 0.18f, 0.35f, false, true);

            float leapSpeed = AstralStaffConfig.StarfallLeapDistance / AstralStaffConfig.StarfallLeapTime;
            // 落点预告：预计落点一圈细环向内收拢，正好在砸下那一刻收进中心。
            Vector3 predicted = FenHuangHalberdRuntime.SnapToGround(
                start + dir * (AstralStaffConfig.StarfallLeapDistance + AstralStaffConfig.StarfallBlastOffset), start.y);
            BossRushFxBurst landing = BossRushFxKit.Shockwave(new Color(0.95f, 0.73f, 0.36f, 0.5f),
                AstralStaffConfig.StarfallBlastRadius * 2.4f, 0.18f, AstralStaffConfig.StarfallImpactTime, true);
            AstralStaffFx.PlayBurst(predicted + Vector3.up * 0.06f, landing);
            // 星陨：从落点斜上方坠下的一道流星，和光棍同一帧落地。
            Vector3[] meteorPoints = new Vector3[2];
            AstralStaffBeam meteor = null;
            Vector3 sky = (Vector3.up * 3.2f - dir).normalized;
            float t = 0f;
            bool swingSound = false;
            while (t < AstralStaffConfig.StarfallImpactTime)
            {
                if (PlayerGone(player))
                {
                    if (giant != null) Destroy(giant.gameObject);
                    if (meteor != null) Destroy(meteor.gameObject);
                    yield break;
                }
                FaceAndPush(player, dir, t < AstralStaffConfig.StarfallLeapTime ? leapSpeed : 0f);

                float k = Mathf.Clamp01(t / AstralStaffConfig.StarfallImpactTime);
                if (!swingSound && k >= 0.7f)
                {
                    swingSound = true;
                    AstralStaffSound.PostSwing(player, 3);
                    AstralStaffSound.PostCustom(AstralStaffSound.SwingFile);
                }
                // 先慢后极快：最后 30% 时间里转完大半个角度，砸下去才有分量
                float swing = k < 0.7f ? Mathf.Lerp(0f, 35f, k / 0.7f) : Mathf.Lerp(35f, 180f, Mathf.Pow((k - 0.7f) / 0.3f, 2f));
                Vector3 pivot = player.transform.position + Vector3.up * 1.2f;
                // swing=0：棍子竖在身后上方；swing=180：平拍在正前方（略向下压进地面）
                Vector3 staffDir = Vector3.Slerp((Vector3.up - dir * 0.35f).normalized, (dir - Vector3.up * 0.18f).normalized,
                    Mathf.Clamp01(swing / 180f));
                staffPoints[0] = pivot - staffDir * 0.8f;
                staffPoints[1] = pivot + staffDir * (AstralStaffConfig.StarfallLineLength - 0.6f);
                if (giant != null)
                {
                    giant.SetPoints(staffPoints);
                    giant.SetWidthScale(0.6f + 0.6f * k);
                }
                if (k >= 0.4f)
                {
                    float remainLeap = leapSpeed * Mathf.Max(0f, AstralStaffConfig.StarfallLeapTime - t);
                    Vector3 target = player.transform.position + dir * (AstralStaffConfig.StarfallBlastOffset + remainLeap);
                    float m = Mathf.Clamp01((k - 0.4f) / 0.6f);
                    float fall = m * m * m;
                    Vector3 head = target + Vector3.up * 0.3f + sky * Mathf.Lerp(18f, 0f, fall);
                    meteorPoints[0] = head + sky * Mathf.Lerp(2.2f, 6.5f, m);
                    meteorPoints[1] = head;
                    if (meteor == null)
                    {
                        meteor = AstralStaffBeam.Create(meteorPoints, 0.05f, 0.34f, new Color(1f, 0.86f, 0.55f, 0.85f),
                            AstralStaffConfig.StarfallImpactTime, 0.06f, false, true);
                    }
                    if (meteor != null)
                    {
                        meteor.SetPoints(meteorPoints);
                        meteor.SetWidthScale(0.7f + 0.6f * m);
                    }
                }
                t += Time.deltaTime;
                yield return null;
            }
            if (meteor != null) Destroy(meteor.gameObject);

            if (PlayerGone(player)) yield break;
            // 落地：一条全中 + 落点震开一圈
            Vector3 origin = player.transform.position;
            Vector3 blast = origin + dir * AstralStaffConfig.StarfallBlastOffset;
            blast = FenHuangHalberdRuntime.SnapToGround(blast, origin.y);
            if (giant != null)
            {
                staffPoints[0] = origin + Vector3.up * 0.48f;
                staffPoints[1] = origin + dir * AstralStaffConfig.StarfallLineLength + Vector3.up * 0.08f;
                giant.SetPoints(staffPoints);
                giant.SetWidthScale(1.15f);
            }
            int hits = DealDamage(player, origin + Vector3.up * 0.9f, dir, AstralStaffConfig.StarfallLineLength, 0f,
                AstralStaffConfig.StarfallLineHalfWidth, AstralStaffConfig.StarfallBlastRadius,
                AstralStaffConfig.StarfallDamageMultiplier, AstralStaffConfig.StarfallKnockback, false, 3, blast);

            PlayImpactFeedback(dir, hits, 3);
            // 砸地本身就该有声：落地轰鸣不论是否打中都响（震屏与顿帧仍只给真实命中）。
            AstralStaffSound.PostCustom(AstralStaffSound.SlamFile);
            AstralStaffFx.PlayStarfallImpact(origin, blast, dir, hits > 0, AstralStaffConfig.StarfallBlastRadius);

            yield return Recovery(player, AstralStaffConfig.StarfallRecovery);
        }

        private IEnumerator Recovery(CharacterMainControl player, float seconds)
        {
            float t = 0f;
            while (t < seconds)
            {
                if (PlayerGone(player)) yield break;
                t += Time.deltaTime;
                yield return null;
            }
        }

        private static void UseStamina(CharacterMainControl player, float amount)
        {
            try { player.UseStamina(amount); } catch { /* 旧版本没有体力接口 */ }
        }

        /// <summary>空挥保留出手光轨；停顿和相机冲量只由真实伤害触发。</summary>
        private static void PlayImpactFeedback(Vector3 dir, int hits, int tier)
        {
            if (hits <= 0) return;
            if (tier < 3) AstralStaffSound.PostCustom(AstralStaffSound.HeavyHitFile);
            float stop = tier == 1 ? AstralStaffConfig.HitStopSweep
                : tier == 2 ? AstralStaffConfig.HitStopSpin : AstralStaffConfig.HitStopStarfall;
            try { GameManager.TimeScaleManager.EnterBulletTime(stop); }
            catch (Exception e) { ReportHitStopFailure(e); }
            Shake(dir, tier == 3 ? AstralStaffConfig.ShakeStarfall
                : tier == 2 ? AstralStaffConfig.ShakeSpin : AstralStaffConfig.ShakeSweep, tier == 3);
            AstralStaffHandVisual hand = AstralStaffHandVisual.Current;
            if (hand != null) hand.SetSurge(tier == 3 ? 1f : 0.65f);
        }

        private static void Hint(CharacterMainControl player, string text)
        {
            try { if (player != null) player.PopText(text); } catch { /* 气泡不可用时静默 */ }
        }

        private void HintThrottled(CharacterMainControl player, string text)
        {
            if (Time.unscaledTime < nextHintTime) return;
            nextHintTime = Time.unscaledTime + 1.6f;
            Hint(player, text);
        }

        private static void ReportHitStopFailure(Exception error)
        {
            if (hitStopFailureReported) return;
            hitStopFailureReported = true;
            ModBehaviour.DevLog(AstralStaffConfig.LogPrefix + " 命中停顿不可用: " + error.Message);
        }

        private static void Shake(Vector3 dir, float strength, bool explosion)
        {
            try
            {
                // 冲量沿发力方向，少量竖向反冲；不叠随机大幅抖动。
                CameraShaker.Shake(dir * strength + Vector3.up * strength * 0.18f,
                    explosion ? CameraShaker.CameraShakeTypes.explosion : CameraShaker.CameraShakeTypes.meleeAttackHit);
            }
            catch { /* 相机未就绪 */ }
        }

        // ====================================================================
        // 伤害
        // ====================================================================

        /// <summary>
        /// halfAngle&gt;0：扇形（halfAngle≥180 即整圈）；halfWidth&gt;0：前方长条，另加 blastCenter 处半径 blastRadius 的圆。
        /// 同一次判定里每个 Health 只吃一次。返回命中数。knockback 为负时把目标往里拉。
        /// </summary>
        private int DealDamage(CharacterMainControl player, Vector3 center, Vector3 dir, float range, float halfAngle,
            float halfWidth, float blastRadius, float multiplier, float knockback, bool pull, int tier,
            Vector3 blastCenter = default(Vector3))
        {
            ItemAgent_MeleeWeapon melee = player.GetMeleeWeapon();
            if (melee == null || melee.Item == null || melee.Item.TypeID != AstralStaffConfig.TypeId) return 0;

            hitHealthIds.Clear();
            int hits = 0;
            dealingHeavyDamage = true;
            try
            {
                float searchRadius = halfWidth > 0f
                    ? Mathf.Max(range, Vector3.Distance(center, blastCenter) + blastRadius) + 0.5f
                    : range + 0.5f;
                int count = Physics.OverlapSphereNonAlloc(center, searchRadius, OverlapBuffer,
                    FenHuangHalberdRuntime.DamageReceiverLayerMask, QueryTriggerInteraction.Collide);
                int wallMask = FenHuangHalberdRuntime.WallLayerMask;
                for (int i = 0; i < count; i++)
                {
                    Collider col = OverlapBuffer[i];
                    OverlapBuffer[i] = null;
                    if (col == null) continue;
                    DamageReceiver receiver = FenHuangHalberdRuntime.TryGetDamageReceiver(col);
                    if (receiver == null) continue;
                    if (receiver.useSimpleHealth)
                    {
                        if (receiver.simpleHealth == null || receiver.simpleHealth.HealthValue <= 0f) continue;
                    }
                    else if (receiver.health == null || receiver.health.IsDead) continue;
                    if (!Team.IsEnemy(player.Team, receiver.Team)) continue;
                    int id = receiver.useSimpleHealth ? receiver.simpleHealth.GetInstanceID() : receiver.health.GetInstanceID();
                    if (hitHealthIds.Contains(id)) continue;

                    CharacterMainControl victim = receiver.useSimpleHealth ? null : receiver.health.TryGetCharacter();
                    if (victim == player) continue;
                    if (victim != null && victim.Dashing) continue;

                    Vector3 targetPos = col.bounds.center;
                    Vector3 flat = targetPos - center;
                    flat.y = 0f;
                    float dist = flat.magnitude;
                    bool inside;
                    if (halfWidth > 0f)
                    {
                        float along = Vector3.Dot(flat, dir);
                        float across = Mathf.Abs(Vector3.Dot(flat, Vector3.Cross(Vector3.up, dir)));
                        Vector3 toBlast = targetPos - blastCenter;
                        toBlast.y = 0f;
                        inside = (along >= -0.4f && along <= range && across <= halfWidth)
                            || toBlast.magnitude <= blastRadius;
                    }
                    else
                    {
                        inside = dist <= range && (halfAngle >= 180f || dist < 0.8f
                            || Vector3.Angle(flat, dir) <= halfAngle);
                    }
                    if (!inside) continue;

                    // 隔墙不打
                    if (wallMask != 0 && Physics.Linecast(center, targetPos, wallMask, QueryTriggerInteraction.Ignore)) continue;

                    hitHealthIds.Add(id);
                    if (!HurtTarget(player, melee, receiver, targetPos, flat, multiplier, tier,
                        hits < AstralStaffConfig.MaxHitFxPerAttack)) continue;
                    hits++;

                    if (victim != null && Mathf.Abs(knockback) > 0.01f)
                    {
                        Vector3 push = dist > 0.05f ? flat / dist : dir;
                        AddKnockback(victim, push * knockback);
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(AstralStaffConfig.LogPrefix + " [WARNING] 重击判定异常: " + e.Message);
            }
            finally
            {
                dealingHeavyDamage = false;
                Array.Clear(OverlapBuffer, 0, OverlapBuffer.Length);
            }
            return hits;
        }

        private static bool HurtTarget(CharacterMainControl player, ItemAgent_MeleeWeapon melee, DamageReceiver receiver,
            Vector3 point, Vector3 flat, float multiplier, int tier, bool playContact)
        {
            Health health = receiver.health;
            HealthSimpleBase simple = receiver.useSimpleHealth ? receiver.simpleHealth : null;
            if (receiver.useSimpleHealth ? simple == null : health == null) return false;
            float healthBefore = receiver.useSimpleHealth ? simple.HealthValue : health.CurrentHealth;
            DamageInfo info = new DamageInfo(player);
            info.damageValue = melee.Damage * melee.CharacterDamageMultiplier * multiplier;
            info.damageFactorToZombie = melee.DamageFactorToZombie;
            info.armorPiercing = melee.ArmorPiercing;
            info.critRate = melee.CritRate * (1f + melee.CharacterCritRateGain);
            info.critDamageFactor = melee.CritDamageFactor * (1f + melee.CharacterCritDamageGain);
            info.crit = -1;
            info.damagePoint = point;
            info.damageNormal = flat.sqrMagnitude > 0.0001f ? -flat.normalized : -player.transform.forward;
            info.fromWeaponItemID = AstralStaffConfig.TypeId;
            info.fromCharacter = player;
            info.elementFactors.Add(new ElementFactor(ElementTypes.physics, 1f));
            // DamageReceiver.Hurt 只代表请求已转发；Health.Invincible / 元素免疫仍可能拒绝。
            // 必须实际掉血才算接触，受保护目标不凭空触发停顿、击退或命中火花。
            bool accepted = receiver.Hurt(info);
            if (!accepted) return false;
            if (receiver.useSimpleHealth)
            {
                if (simple == null || simple.HealthValue >= healthBefore) return false;
            }
            else if (health == null || health.CurrentHealth >= healthBefore) return false;
            if (!receiver.useSimpleHealth)
            {
                try { receiver.AddBuff(GameplayDataSettings.Buffs.Pain, player); } catch { /* 无 Pain buff 的目标 */ }
            }
            if (playContact) AstralStaffFx.PlayContact(point, flat, tier);
            bool killed = receiver.useSimpleHealth ? simple.HealthValue <= 0f : health.IsDead;
            if (killed)
            {
                if (playContact) AstralStaffFx.PlayKillBloom(point, flat);
                AstralStaffSound.PostCustom(AstralStaffSound.KillFile);
                try { GameManager.TimeScaleManager.EnterBulletTime(AstralStaffConfig.HitStopKill); }
                catch (Exception e) { ReportHitStopFailure(e); }
            }
            return true;
        }

        // ====================================================================
        // 击退
        // ====================================================================

        private void AddKnockback(CharacterMainControl victim, Vector3 velocity)
        {
            if (victim == null) return;
            // Boss（血量很厚的目标）只吃三成击退，避免把 Boss 推着走
            try
            {
                if (victim.Health != null && victim.Health.MaxHealth > 900f) velocity *= 0.3f;
            }
            catch { return; } // 无法判断目标重量时放弃击退，避免把 Boss 当普通敌人推走。
            velocity.y = 0f;
            if (knocks.Count >= 24) return;
            Knock k;
            k.Target = victim;
            k.Velocity = velocity;
            k.Until = Time.time + AstralStaffConfig.KnockbackSeconds;
            knocks.Add(k);
        }

        private void TickKnockbacks()
        {
            float now = Time.time;
            for (int i = knocks.Count - 1; i >= 0; i--)
            {
                Knock k = knocks[i];
                if (k.Target == null || now >= k.Until || k.Target.Health == null || k.Target.Health.IsDead)
                {
                    knocks.RemoveAt(i);
                    continue;
                }
                float remain = (k.Until - now) / AstralStaffConfig.KnockbackSeconds;
                try { k.Target.SetForceMoveVelocity(k.Velocity * Mathf.Clamp01(remain)); } catch { knocks.RemoveAt(i); }
            }
        }

        // ====================================================================
        // 输入
        // ====================================================================

        private void ResolveAds()
        {
            if (adsResolved || Time.unscaledTime < nextAdsResolveTime) return;
            nextAdsResolveTime = Time.unscaledTime + 1f;
            try
            {
                Type inputControlType = typeof(CharacterInputControl);
                PropertyInfo instanceProp = inputControlType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                object control = instanceProp != null ? instanceProp.GetValue(null, null) : null;
                if (control == null) return;
                FieldInfo actionsField = inputControlType.GetField("inputActions", BindingFlags.NonPublic | BindingFlags.Instance);
                object actions = actionsField != null ? actionsField.GetValue(control) : null;
                if (actions == null) return;
                FieldInfo adsField = actions.GetType().GetField("ADS", BindingFlags.Public | BindingFlags.Instance);
                adsAction = adsField != null ? adsField.GetValue(actions) : null;
                if (adsAction == null) return;
                Type actionType = adsAction.GetType();
                readButtonMethod = actionType.GetMethod("ReadValueAsButton", BindingFlags.Public | BindingFlags.Instance);
                wasPressedMethod = actionType.GetMethod("WasPressedThisFrame", BindingFlags.Public | BindingFlags.Instance);
                wasReleasedMethod = actionType.GetMethod("WasReleasedThisFrame", BindingFlags.Public | BindingFlags.Instance);
                adsResolved = readButtonMethod != null;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(AstralStaffConfig.LogPrefix + " [WARNING] 解析右键动作失败: " + e.Message);
            }
        }

        private void ReadAds(out bool held, out bool pressed, out bool released)
        {
            ResolveAds();
            held = false;
            pressed = false;
            released = false;
            if (adsResolved && adsAction != null)
            {
                try
                {
                    held = (bool)readButtonMethod.Invoke(adsAction, NoArgs);
                    pressed = wasPressedMethod != null ? (bool)wasPressedMethod.Invoke(adsAction, NoArgs) : (held && !lastAdsHeld);
                    released = wasReleasedMethod != null ? (bool)wasReleasedMethod.Invoke(adsAction, NoArgs) : (!held && lastAdsHeld);
                }
                catch
                {
                    adsResolved = false;
                }
            }
            else
            {
                try
                {
                    held = Input.GetMouseButton(1);
                    pressed = Input.GetMouseButtonDown(1);
                    released = Input.GetMouseButtonUp(1);
                }
                catch { /* 旧输入模块被禁用 */ }
            }
            lastAdsHeld = held;
        }

        private static void SuppressVanillaAds()
        {
            try
            {
                if (LevelManager.Instance != null && LevelManager.Instance.InputManager != null)
                {
                    LevelManager.Instance.InputManager.SetAdsInput(false);
                }
            }
            catch
            {
                // 官方输入宿主失效时撤销待释放的蓄势，避免重建后左右键动作一起补发。
                if (instance != null)
                {
                    instance.charging = false;
                    instance.pressTime = -1f;
                    instance.chargeProgress = 0f;
                }
            }
        }

        private static bool IsGameplayInputAllowed()
        {
            try { if (!InputManager.InputActived) return false; } catch { return false; }
            if (Time.timeScale <= 0f) return false;
            // 打开背包 / 商店等官方界面时 ActiveView 非空，此时不蓄势、不出招。
            // 不再看 EventSystem 的指针悬停：官方战斗输入本身不做这项判断，局内 HUD 元素挡在光标下时
            // 会让右键蓄势与松手整段失效、重击中途被打断（2026-10-08 实测「攒不到豆」的嫌疑根因之一）。
            try { if (Duckov.UI.View.ActiveView != null) return false; } catch { return false; }
            return true;
        }
    }

    /// <summary>官方音效（反射调用 AudioManager.Post：返回值是 FMOD 类型，编译清单没有 FMOD 引用）。</summary>
    internal static class AstralStaffSound
    {
        internal const string Swing = "SFX/Combat/Melee/attack_default";

        // 星阙专属程序化音效（tools/gen_astral_staff_sfx.py 生成，部署在 Assets/Sounds/NewWeapons）。
        // 官方近战没有挂 hitFx 时命中只有 HitMarker 的「嗒」，没有打在身上的那一下；这里补上。
        internal const string HitFile = "astral_hit.wav";
        internal const string HitFinisherFile = "astral_hit_finisher.wav";
        internal const string HeavyHitFile = "astral_heavy_hit.wav";
        internal const string SlamFile = "astral_slam.wav";
        internal const string KillFile = "astral_kill.wav";
        internal const string SwingFile = "astral_swing.wav";
        internal const string BeanFile = "astral_bean_1.wav";
        internal const string Bean2File = "astral_bean_2.wav";
        internal const string BeanFullFile = "astral_bean_full.wav";
        internal const string ReleaseFile = "astral_release.wav";
        internal const string ReleaseMaxFile = "astral_release_max.wav";
        internal const string FizzleFile = "astral_fizzle.wav";

        private static float lastCustomTime = -1f;
        private static string lastCustomFile;

        private static MethodInfo postMethod;
        private static MethodInfo setPitchMethod;
        private static MethodInfo setVolumeMethod;
        private static bool resolved;

        internal static void PostSwing(CharacterMainControl at, int tier, bool second = false)
        {
            float pitch = tier == 1 ? 1.02f : tier == 2 ? (second ? 0.81f : 0.91f) : 0.66f;
            Post(Swing, at, pitch, tier == 3 ? 0.95f : second ? 0.82f : 0.72f);
        }

        /// <summary>
        /// 播放一条星阙专属音效。同一条音效同一帧（20 ms 内）只放一次：群怪多杀、多段同帧命中不叠成爆音。
        /// 文件缺失时 NewWeaponFx.PlaySound 静默返回。
        /// </summary>
        internal static void PostCustom(string fileName)
        {
            float now = Time.unscaledTime;
            if (fileName == lastCustomFile && now - lastCustomTime < 0.02f) return;
            lastCustomFile = fileName;
            lastCustomTime = now;
            NewWeaponFx.PlaySound(fileName);
        }

        internal static void PostFizzle(CharacterMainControl at)
        {
            PostCustom(FizzleFile);
        }

        internal static void PostFocus(CharacterMainControl at, int beans)
        {
            // 不再叠官方 UI 悬停 / 确认音：界面音和棍势「叮」混在一起会显得廉价。
            PostCustom(beans >= AstralStaffConfig.MaxBeans ? BeanFullFile : beans >= 2 ? Bean2File : BeanFile);
        }

        private static void Post(string eventName, CharacterMainControl at, float pitch, float volume)
        {
            try
            {
                if (!resolved)
                {
                    resolved = true;
                    postMethod = typeof(global::Duckov.AudioManager).GetMethod("Post",
                        BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string), typeof(GameObject) }, null);
                }
                if (postMethod != null && at != null)
                {
                    object handle = postMethod.Invoke(null, new object[] { eventName, at.gameObject });
                    if (handle == null) return;
                    // 已对照本机 FMODUnity：EventInstance.setPitch/Volume(float)。
                    // 只调整这次官方事件，音量仍经过游戏的 Master / SFX bus。
                    if (setPitchMethod == null)
                    {
                        Type eventType = handle.GetType();
                        setPitchMethod = eventType.GetMethod("setPitch", new[] { typeof(float) });
                        setVolumeMethod = eventType.GetMethod("setVolume", new[] { typeof(float) });
                    }
                    if (setPitchMethod != null) setPitchMethod.Invoke(handle, new object[] { pitch });
                    if (setVolumeMethod != null) setVolumeMethod.Invoke(handle, new object[] { volume });
                }
            }
            catch { /* 音效不是关键路径 */ }
        }

        internal static void ResetStaticCaches()
        {
            postMethod = null;
            setPitchMethod = null;
            setVolumeMethod = null;
            resolved = false;
            lastCustomTime = -1f;
            lastCustomFile = null;
        }
    }
}
