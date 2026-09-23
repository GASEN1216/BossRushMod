using System;
using System.Collections;
using Duckov.Utilities;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// COMPAT：S4 残星瞭台的头目「瞭台观星手」（R1 纵切）。
    ///
    /// 核心招式：站在群岛最高的观星台上，**有视线**时远程标记玩家——标记圈先跟着人瞄 0.8 秒，
    /// 再原地锁定 0.6 秒，然后在锁定处落一发星火；连着两发。解法三条：
    /// - 躲进掩体断开视线：瞄准阶段每 0.1 秒复查视线，断了就打断标记；
    /// - 冲上去：玩家进到 8 米内它不再标记，交给官方 AI 常规射击；
    /// - 看准锁定：锁定后半径 2.2 米、预警 0.6 秒，≈ 3.7 m/s 就能出圈（判据 ≤ 5.5）。
    ///
    /// 装备即招式：观星镜盔耐久打空（被爆头打穿）之后再也标记不了——和残星匠首「打穿护目盔少落圈」不是同一种动词。
    /// 伤害走官方爆炸，口径同噬风；只订自己身上的 `Health.OnDeadEvent`，OnDestroy 成对退订。
    /// </summary>
    internal sealed class SkyIslandStargazerChief : MonoBehaviour
    {
        private const float TickInterval = 0.2f;
        private const float EyeHeight = 1.4f;
        private const float ChestHeight = 1.0f;
        private const float SightRecheck = 0.1f;
        private static readonly Color MarkTint = new Color(0.62f, 0.86f, 1f, 1f);

        private CharacterMainControl boss;
        private Health health;
        private SkyIslandBossProfile profile;
        private SkyIslandBossContext context;
        private LineRenderer markRing;
        private int wallMask;
        private float nextTick, cooldownUntil;
        private bool subscribed, marking, finished, lensEquipped, lensBrokenAnnounced, firstMarkAnnounced;

        /// <summary>只读，给 F3 与演练：是否正在标记、观星镜盔是否还起作用。</summary>
        internal bool Marking { get { return marking; } }
        internal bool LensWorking { get { return lensEquipped && !LensBroken(); } }

        internal void Bind(CharacterMainControl character, SkyIslandBossProfile value, SkyIslandBossContext ctx)
        {
            if (character == null) throw new ArgumentNullException("character");
            if (value == null) throw new ArgumentNullException("value");
            if (ctx == null) throw new ArgumentNullException("ctx");
            boss = character;
            profile = value;
            context = ctx;
            health = character.Health;
            if (health == null) throw new InvalidOperationException("瞭台观星手缺少生命组件");
            wallMask = GameplayDataSettings.Layers.wallLayerMask.value;
            lensEquipped = !LensBroken();
            cooldownUntil = Time.time + 2.5f;
            health.OnDeadEvent.AddListener(OnDead);
            subscribed = true;
        }

        private void Update()
        {
            if (finished || boss == null || health == null || Time.time < nextTick) return;
            nextTick = Time.time + TickInterval;
            if (context.Valid != null && !context.Valid()) return;
            if (health.IsDead) return;
            if (lensEquipped && !lensBrokenAnnounced && LensBroken())
            {
                lensBrokenAnnounced = true;
                Announce("观星镜盔的镜片碎了，观星手再也标记不了你。",
                    "The stargazer's lens shatters. It can no longer mark you.", false);
            }
            if (marking || Time.time < cooldownUntil || !LensWorking) return;
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null || player.Health == null || player.Health.IsDead) return;
            float distance = Vector3.Distance(boss.transform.position, player.transform.position);
            if (distance > SkyIslandBossRules.MarkRange || distance < SkyIslandBossRules.MarkCloseRange) return;
            if (!HasLineOfSight(player)) return;
            StartCoroutine(MarkRoutine(player));
        }

        private IEnumerator MarkRoutine(CharacterMainControl player)
        {
            marking = true;
            if (!firstMarkAnnounced)
            {
                firstMarkAnnounced = true;
                Announce("瞭台上的观星手把镜筒对准了你：躲进掩体断开视线，或者冲上去。",
                    "The stargazer on the overlook swings its lens onto you. Break line of sight, or rush it.", true);
            }
            bool cancelled = false;
            for (int shot = 0; shot < SkyIslandBossRules.FlareShots && !cancelled && !Aborted(); shot++)
            {
                try
                {
                    markRing = SkyIslandBossForge.CreateGroundRing(context.Root, player.transform.position);
                    SkyIslandBossForge.SetRing(markRing, SkyIslandBossRules.FlareRadius, 0f, MarkTint);
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[SkyIslandBoss] 观星手标记圈失败：" + e.Message);
                    markRing = null;
                }
                if (markRing == null) break;

                // 瞄准：圈跟着人走；第一发才复查视线（第二发紧跟在第一发之后，给的是「别站回原地」的压力）。
                float aim = shot == 0 ? SkyIslandBossRules.MarkAimSeconds : SkyIslandBossRules.FlareShotGap;
                float started = Time.time, nextSight = 0f;
                while (Time.time - started < aim && !Aborted())
                {
                    if (player == null) { cancelled = true; break; }
                    SkyIslandBossForge.PlaceRing(markRing, context.Root, player.transform.position);
                    SkyIslandBossForge.SetRing(markRing, SkyIslandBossRules.FlareRadius, 0.5f * (Time.time - started) / aim, MarkTint);
                    if (shot == 0 && Time.time >= nextSight)
                    {
                        nextSight = Time.time + SightRecheck;
                        if (!HasLineOfSight(player)) { cancelled = true; break; }
                    }
                    yield return null;
                }
                if (cancelled || Aborted())
                {
                    DestroyRing();
                    if (cancelled && shot == 0)
                        Announce("视线断了，观星手的标记散了。", "Line of sight broken; the stargazer's mark fades.", false);
                    break;
                }

                // 锁定：圈钉在这一刻的落点上，之后人怎么走都不再跟。
                Vector3 locked;
                if (!SkyIslandBossForge.SnapToGround(player.transform.position, context, 0f, out locked)) locked = player.transform.position;
                SkyIslandBossForge.PlaceRing(markRing, context.Root, locked);
                // 戴着静听耳罩的玩家早一点听见（SkyIslandBossGearWorn，头目 R3）：锁定只会更长，逃圈判据仍按不戴的算。
                float lockSeconds = SkyIslandBossRules.TelegraphSeconds(SkyIslandBossRules.MarkLockSeconds, SkyIslandBossGearWorn.Earmuffs);
                started = Time.time;
                while (Time.time - started < lockSeconds && !Aborted())
                {
                    SkyIslandBossForge.SetRing(markRing, SkyIslandBossRules.FlareRadius,
                        0.5f + 0.5f * (Time.time - started) / lockSeconds, MarkTint);
                    yield return null;
                }
                if (!Aborted())
                {
                    // 星火是真的炸开：留官方火球，加余波圈、扬尘与震屏（VB-21）。
                    try
                    {
                        SkyIslandBossForge.Detonate(boss, locked, SkyIslandBossRules.FlareRadius, SkyIslandBossRules.FlareDamage,
                            true, SkyIslandImpactFx.BossShake, MarkTint);
                    }
                    catch (Exception e) { Debug.LogWarning("[SkyIslandBoss] 星火落点失败：" + e.Message); }
                }
                DestroyRing();
            }
            marking = false;
            cooldownUntil = Time.time + (cancelled ? SkyIslandBossRules.MarkCooldown * 0.5f : SkyIslandBossRules.MarkCooldown);
        }

        private bool HasLineOfSight(CharacterMainControl player)
        {
            if (player == null) return false;
            Vector3 eye = boss.transform.position + Vector3.up * EyeHeight;
            Vector3 chest = player.transform.position + Vector3.up * ChestHeight;
            return !Physics.Linecast(eye, chest, wallMask, QueryTriggerInteraction.Ignore);
        }

        private bool LensBroken()
        {
            return SkyIslandBossForge.PieceBroken(boss.GetHelmatItem(), BossRushItemIds.SkyIslandStargazerLensHelm);
        }

        private bool Aborted()
        {
            return finished || boss == null || health == null || health.IsDead || (context.Valid != null && !context.Valid());
        }

        private void DestroyRing()
        {
            // 淡出后自毁，不再一帧消失（视线断了、结算完都走这里）。
            SkyIslandBossForge.ReleaseRing(markRing);
            markRing = null;
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
            DestroyRing();
            Announce("瞭台观星手倒下了，镜筒从台边滚了下去。", "The overlook stargazer falls and its spyglass rolls off the platform.", false);
            SkyIslandBossForge.RaiseDefeated(profile, position);
        }

        private void OnDestroy()
        {
            if (subscribed && health != null) health.OnDeadEvent.RemoveListener(OnDead);
            subscribed = false;
            finished = true;
            DestroyRing();
            context = null;
            profile = null;
            boss = null;
            health = null;
        }
    }
}
