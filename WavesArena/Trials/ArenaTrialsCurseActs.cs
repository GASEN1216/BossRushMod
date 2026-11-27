// ============================================================================
// ArenaTrialsCurseActs.cs - 厄运里「挨打之后才发作」的诅咒：回响、沉疴的局内结算与表现，破绽的受击裂光
// ============================================================================
// 2026-11-27 改进方案：厄运池从五道加到八道（数值见 ArenaTrialsTuning），新增的三道各逼出一种应对：
//   回响：被敌人打中的地方隔 1.25 秒（重 1 秒）再震一下，伤 5%（重 8%）生命上限，翻滚可躲——挨打后要挪步；
//         地上先起一圈墨紫符环，外圈向内收拢到判定边界读剩余时间，到点一声磬、炸开；同一段时间只记一处，至多三处。
//   沉疴：被敌人打中后伤口发作，4 秒内每秒流失 1%（重 1.5%）生命上限，再挨打续上；翻滚一次止住——
//         发作时身上缓缓升起暗朱烟缕，止住时散成一团墨、一声笔扫。
//   破绽：受到的伤害 +15%（重 +25%，ArenaTrialsCurses.ContributePlayer 落在官方元素系数上）；挨打时身上迸一点朱砂裂光。
// 伤害只经 ArenaTrials.HurtNeutral（不吃难度倍率、不看护甲）；来源为空，不会再触发厄运与血债。
// 生命周期：Curses 换诅咒 / 收波 / 收尾时 Clear()，符环与烟缕自然淡出（不一帧消失）。只作用于主玩家。
// 质感（owner：高级、大气、史诗、无塑料感）：预警画在真实判定半径上，粒子少而亮，声音只用钟、磬、纸。
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    internal sealed class ArenaTrialsCurseActs
    {
        private const string LogPrefix = "[BossRush] [出征令战规] ";
        private const float CrackCooldown = 0.35f;
        private const float FesterRate = 7f;

        private static readonly Color EchoInk = new Color(0.70f, 0.62f, 0.86f, 1f);
        private static readonly Color EchoPaper = new Color(0.93f, 0.89f, 0.80f, 1f);
        private static readonly Color FesterInk = new Color(0.42f, 0.10f, 0.08f, 1f);
        private static readonly Color Cinnabar = new Color(0.90f, 0.34f, 0.24f, 1f);
        private static readonly Color Ash = new Color(0.20f, 0.18f, 0.17f, 0.5f);

        private sealed class EchoMark
        {
            public Vector3 At;
            public float Age;
            public float Delay;
            public float Damage;
            public GameObject Root;
            public SpriteRenderer Ring, Inner, Fill;
        }

        private readonly List<EchoMark> echoes = new List<EchoMark>(ArenaTrialsTuning.EchoMaxMarks);
        private float echoCooldown;
        private float festerLeft;
        private float festerTick;
        private int festerLevel;
        private GameObject festerRoot;
        private ParticleSystem festerWisps;
        private float crackCooldown;

        // ------------------------------------------------------------------
        // 入口（ArenaTrialsCurses.OnAnyHurt：主玩家被敌人打中时）
        // ------------------------------------------------------------------

        /// <summary>回响：在被打中的地方记一处，隔一会儿再震。</summary>
        internal void Echo(Vector3 at, int level)
        {
            if (echoCooldown > 0f || echoes.Count >= ArenaTrialsTuning.EchoMaxMarks) return;
            echoCooldown = ArenaTrialsTuning.EchoCooldown(level);
            EchoMark mark = new EchoMark();
            mark.At = at;
            mark.Delay = ArenaTrialsTuning.EchoDelay(level);
            mark.Damage = ArenaTrialsTuning.EchoDamage(level);
            echoes.Add(mark);
            try
            {
                float radius = ArenaTrialsTuning.EchoRadius;
                mark.Root = new GameObject("BossRush_TrialEcho");
                mark.Root.transform.position = at;
                Transform node = mark.Root.transform;
                mark.Fill = ArenaTrialsFx.FlatSoft(node, "Fill", BossRushFxBlend.Additive, BossRushFxKit.GainSoft);
                mark.Ring = ArenaTrialsFx.FlatShape(node, "Ring", BossRushParticleShape.RuneOuter, BossRushFxBlend.Additive, BossRushFxKit.GainBright);
                mark.Inner = ArenaTrialsFx.FlatShape(node, "Inner", BossRushParticleShape.RuneInner, BossRushFxBlend.Additive, BossRushFxKit.GainSoft);
                ArenaTrialsFx.SetRadius(mark.Ring, radius, ArenaTrialsFx.RuneRadius);
                ArenaTrialsFx.SetRadius(mark.Inner, radius * 0.62f, ArenaTrialsFx.RuneInnerRadius);
                ArenaTrialsFx.SetRadius(mark.Fill, 0.05f, ArenaTrialsFx.SoftRadius);
                ArenaTrialsFx.Spin(mark.Ring, UnityEngine.Random.Range(0f, 360f));
                ArenaTrialsFx.Tint(mark.Ring, EchoInk, 0f);
                ArenaTrialsFx.Tint(mark.Inner, EchoPaper, 0f);
                ArenaTrialsFx.Tint(mark.Fill, EchoInk, 0f);
                // 外圈从 2.5 倍半径向内收拢、正好在到点时落到判定边界（细环贴图的环在约 0.45 倍粒子尺寸处）
                BossRushFxKit.PlayBurst(at + Vector3.up * ArenaTrialsFx.GroundLift,
                    BossRushFxKit.Shockwave(new Color(EchoInk.r, EchoInk.g, EchoInk.b, 0.55f), radius * 2.5f / 0.45f, 0.4f, mark.Delay, true));
                ArenaTrialsFx.SoundAt(BossRushInkSkin.SfxBellHum, at);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + "[WARNING] 回响符环表现失败（照常结算）: " + e.Message);
            }
        }

        /// <summary>沉疴：伤口发作（已在发作就续上时长）。</summary>
        internal void Fester(int level)
        {
            bool was = festerLeft > 0f;
            festerLeft = ArenaTrialsTuning.FesterSeconds;
            festerLevel = level;
            if (was) return;
            festerTick = ArenaTrialsTuning.FesterTickSeconds;
            SetFesterWisps(true);
        }

        /// <summary>破绽：挨打时身上迸一点朱砂裂光（节流，霰弹一梭子只亮一下）。</summary>
        internal void Crack(Vector3 at)
        {
            if (crackCooldown > 0f) return;
            crackCooldown = CrackCooldown;
            try
            {
                Vector3 body = at + Vector3.up * 1f;
                BossRushFxBurst shards = BossRushFxKit.Sparks(Cinnabar, 7);
                shards.SpeedMin = 2.5f;
                shards.SpeedMax = 5f;
                shards.LifeMin = 0.15f;
                shards.LifeMax = 0.3f;
                BossRushFxKit.PlayBurst(body, shards);
                BossRushFxKit.PlayBurst(body, BossRushFxKit.Glint(Cinnabar, 0.8f, 0.14f));
            }
            catch (Exception) { }
        }

        // ------------------------------------------------------------------
        // 每帧
        // ------------------------------------------------------------------

        internal void Tick(float deltaTime)
        {
            if (echoCooldown > 0f) echoCooldown -= deltaTime;
            if (crackCooldown > 0f) crackCooldown -= deltaTime;
            if (echoes.Count > 0) TickEchoes(deltaTime);
            if (festerLeft > 0f) TickFester(deltaTime);
        }

        private void TickEchoes(float deltaTime)
        {
            float radius = ArenaTrialsTuning.EchoRadius;
            for (int i = echoes.Count - 1; i >= 0; i--)
            {
                EchoMark mark = echoes[i];
                mark.Age += deltaTime;
                float p = Mathf.Clamp01(mark.Age / Mathf.Max(0.05f, mark.Delay));
                float a = ArenaTrialsFx.Envelope(mark.Age, 0.18f, 1f, 0f);
                ArenaTrialsFx.Tint(mark.Ring, EchoInk, (0.5f + 0.4f * p) * a);
                ArenaTrialsFx.Tint(mark.Inner, EchoPaper, 0.32f * a);
                ArenaTrialsFx.Spin(mark.Ring, -26f * deltaTime);
                ArenaTrialsFx.Spin(mark.Inner, 54f * deltaTime);
                ArenaTrialsFx.SetRadius(mark.Fill, Mathf.Max(0.05f, radius * BossRushUI.SmoothStep(p)), ArenaTrialsFx.SoftRadius);
                ArenaTrialsFx.Tint(mark.Fill, EchoInk, (0.1f + 0.26f * p) * a);
                if (mark.Age < mark.Delay) continue;
                echoes.RemoveAt(i);
                Detonate(mark);
            }
        }

        private static void Detonate(EchoMark mark)
        {
            float radius = ArenaTrialsTuning.EchoRadius;
            ArenaTrialsFx.Impact(mark.At, EchoInk, radius, 8, Ash);
            ArenaTrialsFx.SoundAt(BossRushInkSkin.SfxChimeStone, mark.At);
            if (mark.Root != null) ArenaTrialsFx.Retire(mark.Root, 0.45f);
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null || player.Health == null || player.Health.IsDead) return;
            if (ArenaTrials.FlatSqr(player.transform.position, mark.At) > radius * radius) return;
            ArenaTrials.HurtNeutral(player.Health, ArenaTrials.ShareOfMaxHealth(player.Health, mark.Damage), mark.At, true);
        }

        private void TickFester(float deltaTime)
        {
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null || player.Health == null || player.Health.IsDead)
            {
                StopFester();
                return;
            }
            if (festerRoot != null) festerRoot.transform.position = player.transform.position;
            if (player.Dashing)
            {
                Cleanse(player.transform.position);
                return;
            }
            festerLeft -= deltaTime;
            festerTick -= deltaTime;
            if (festerTick <= 0f)
            {
                float tick = ArenaTrialsTuning.FesterTickSeconds;
                festerTick += tick;
                float fraction = ArenaTrialsTuning.FesterPerSecond(festerLevel) * tick;
                ArenaTrials.HurtNeutral(player.Health, ArenaTrials.ShareOfMaxHealth(player.Health, fraction), player.transform.position, false);
            }
            if (festerLeft <= 0f) StopFester();
        }

        /// <summary>翻滚止住沉疴：烟缕停发，身上散开一团墨、一声笔扫。</summary>
        private void Cleanse(Vector3 at)
        {
            StopFester();
            try
            {
                Vector3 ground = at + Vector3.up * ArenaTrialsFx.GroundLift;
                BossRushFxBurst ink = BossRushFxKit.Dust(new Color(ArenaTrialsFx.InkDark.r, ArenaTrialsFx.InkDark.g, ArenaTrialsFx.InkDark.b, 0.45f), 6);
                ink.SpeedMin = 1.4f;
                ink.SpeedMax = 2.4f;
                BossRushFxKit.PlayBurst(ground, ink);
                BossRushFxKit.PlayBurst(at + Vector3.up * 0.9f, BossRushFxKit.Glint(EchoPaper, 1f, 0.2f));
                ArenaTrialsFx.SoundAt(BossRushInkSkin.SfxBrushSweep, at);
            }
            catch (Exception) { }
        }

        private void StopFester()
        {
            festerLeft = 0f;
            SetFesterWisps(false);
        }

        private void SetFesterWisps(bool on)
        {
            try
            {
                if (on && festerRoot == null)
                {
                    festerRoot = new GameObject("BossRush_TrialFester");
                    CharacterMainControl player = CharacterMainControl.Main;
                    if (player != null) festerRoot.transform.position = player.transform.position;
                    // 按开着的发射率建（粒子上限按它算），关的时候再把发射率降到 0
                    festerWisps = ArenaTrialsFx.Wisps(festerRoot.transform, "Fester", FesterInk, 0.5f, 0.35f, FesterRate,
                        0.35f, 0.6f, 1.4f, false, 0.9f);
                }
                ArenaTrialsFx.SetRate(festerWisps, on ? FesterRate : 0f);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + "[WARNING] 沉疴烟缕表现失败（照常结算）: " + e.Message);
            }
        }

        // ------------------------------------------------------------------
        // 收掉（换诅咒 / 收波 / 收尾）：不再结算，表现自然淡出
        // ------------------------------------------------------------------

        internal void Clear()
        {
            for (int i = 0; i < echoes.Count; i++)
            {
                if (echoes[i].Root != null) ArenaTrialsFx.Retire(echoes[i].Root, 0.5f);
            }
            echoes.Clear();
            echoCooldown = 0f;
            crackCooldown = 0f;
            festerLeft = 0f;
            if (festerRoot != null)
            {
                BossRushFxKit.Release(festerRoot, 0.2f, 2.5f, true);
                festerRoot = null;
                festerWisps = null;
            }
        }
    }
}
