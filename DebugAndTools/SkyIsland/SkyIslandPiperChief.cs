using System;
using System.Collections;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// COMPAT：S1 蛙鸣池的头目「蚋笛翁」（头目 R3，只在夜里出来：遭遇 owner 白天把带队位留到夜里补刷）。
    ///
    /// 核心招式：**引蚋笛**。玩家走近时它站定吹笛（官方 AI 暂停），脚下亮圈；吹完把附近的云蚋招到玩家身边，
    /// 并让全场云蚋在一段时间里往玩家身上扑、压过灭蚊灯（<see cref="SkyIslandGnats.LureSwarm"/> / <see cref="SkyIslandGnats.SetHostileLure"/>）。
    /// 脚下那个圈只是吹笛的提示，不是伤害范围：这位头目自己不放爆炸。
    ///
    /// 克制：现成的七种驱蚋手段都管用；吹笛那一下打掉它 <see cref="SkyIslandBossRules.FluteInterruptDamage"/> 点血，笛声就断了，
    /// 下一口笛也来得快一半（打断是给玩家的奖励，不是惩罚）；白天去找不到它。
    ///
    /// 装备联动：苔纱面罩被暴击打穿，它吹不响笛子。面罩槽官方不磨耐久，由本控制器在自己的 `Health.OnHurtEvent`
    /// 里照官方磨头盔的口径扣（<see cref="SkyIslandBossProps.WearSoftPiece"/>），每 0.2 秒节流读一次耐久。
    ///
    /// 事件订阅：自己身上的 `Health.OnDeadEvent` / `OnHurtEvent`，OnDestroy 成对退订。暂停过的 AI 在每条不是倒下的出口恢复
    /// （Resume 自带重新认人），倒下与销毁时不恢复；笛圈在 OnDead / OnDestroy 里收。
    /// </summary>
    internal sealed class SkyIslandPiperChief : MonoBehaviour
    {
        private const float TickInterval = 0.2f;
        /// <summary>刷出来之后隔这么久才吹第一口：先让玩家看清它是谁。</summary>
        private const float FirstFluteDelay = 4f;
        private const string FluteRingName = "SkyIslandFluteRing";
        private static readonly Color FluteTint = new Color(0.55f, 0.85f, 0.55f, 1f);

        private CharacterMainControl boss;
        private Health health;
        private SkyIslandBossProfile profile;
        private SkyIslandBossContext context;
        private BossAIController aiControl;
        private LineRenderer fluteRing;
        private int fluteCount, lastLured;
        private float nextTick, nextFluteAt;
        private bool subscribed, hurtSubscribed, channeling, finished, maskEquipped, maskBroken;
        private bool fluteAnnounced, interruptAnnounced, noGnatsAnnounced;

        /// <summary>只读，给 F3 与演练：是否正在吹笛、吹成了几次、上一口招来几只、苔纱面罩是否还起作用。</summary>
        internal bool Channeling { get { return channeling; } }
        internal int FluteCount { get { return fluteCount; } }
        internal int LastLured { get { return lastLured; } }
        internal bool MaskWorking { get { return maskEquipped && boss != null && !MaskBroken(); } }

        internal void Bind(CharacterMainControl character, SkyIslandBossProfile value, SkyIslandBossContext ctx)
        {
            if (character == null) throw new ArgumentNullException("character");
            if (value == null) throw new ArgumentNullException("value");
            if (ctx == null) throw new ArgumentNullException("ctx");
            boss = character;
            profile = value;
            context = ctx;
            health = character.Health;
            if (health == null) throw new InvalidOperationException("蚋笛翁缺少生命组件");
            // 配装在控制器之前做完：只有真的戴上了，「被打穿」才有意义，也才吹得响笛子。
            maskEquipped = !SkyIslandBossForge.PieceBroken(SkyIslandBossProps.WornIn(character, "FaceMask"), BossRushItemIds.SkyIslandMossgauzeMask);
            maskBroken = !maskEquipped;
            try { aiControl = new BossAIController(character, "SkyIslandPiper"); }
            catch (Exception e)
            {
                aiControl = null;
                Debug.LogWarning("[SkyIslandBoss] 蚋笛翁 AI 暂停控制不可用，吹笛时不站定：" + e.Message);
            }
            nextFluteAt = Time.time + FirstFluteDelay;
            health.OnDeadEvent.AddListener(OnDead);
            subscribed = true;
            if (maskEquipped && !hurtSubscribed)
            {
                health.OnHurtEvent.AddListener(OnHurt);
                hurtSubscribed = true;
            }
        }

        private void Update()
        {
            if (finished || boss == null || health == null || Time.time < nextTick) return;
            nextTick = Time.time + TickInterval;
            if (Aborted()) return;
            TickBrokenGear();
            if (channeling || maskBroken || Time.time < nextFluteAt) return;
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null || player.Health == null || player.Health.IsDead) return;
            float range = SkyIslandBossRules.FluteRange;
            if ((player.transform.position - boss.transform.position).sqrMagnitude > range * range) return;
            StartCoroutine(FluteRoutine(player));
        }

        // ====================================================================
        // 引蚋笛
        // ====================================================================

        private IEnumerator FluteRoutine(CharacterMainControl player)
        {
            channeling = true;
            try { if (aiControl != null) aiControl.Pause(); }
            catch (Exception e) { Debug.LogWarning("[SkyIslandBoss] 蚋笛翁吹笛停手失败：" + e.Message); }
            try
            {
                fluteRing = SkyIslandBossForge.CreateGroundRing(context.Root, boss.transform.position);
                fluteRing.gameObject.name = FluteRingName;
                SkyIslandBossForge.SetRing(fluteRing, SkyIslandBossRules.FluteRingRadius, 0f, FluteTint);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SkyIslandBoss] 蚋笛翁笛圈失败：" + e.Message);
                DestroyRing();
            }
            if (fluteRing == null)
            {
                // 没有亮圈，玩家就读不出「现在打它能打断」：这一口不吹。
                EndFlute(SkyIslandBossRules.FluteInterval);
                yield break;
            }

            // 戴着静听耳罩的玩家早一点听见（SkyIslandBossGearWorn，头目 R3）：打断窗口只会更长。
            float channel = SkyIslandBossRules.TelegraphSeconds(SkyIslandBossRules.FluteChannel, SkyIslandBossGearWorn.Earmuffs);
            float startHealth = health.CurrentHealth;
            float started = Time.time;
            bool interrupted = false;
            while (Time.time - started < channel && !Aborted())
            {
                if (startHealth - health.CurrentHealth >= SkyIslandBossRules.FluteInterruptDamage) { interrupted = true; break; }
                SkyIslandBossForge.PlaceRing(fluteRing, context.Root, boss.transform.position);
                SkyIslandBossForge.SetRing(fluteRing, SkyIslandBossRules.FluteRingRadius, Mathf.Clamp01((Time.time - started) / channel), FluteTint);
                yield return null;
            }
            if (Aborted())
            {
                // 倒下（OnDead 已收圈）或会话结束：不招云蚋。会话结束不是倒下，照样恢复 AI（EndFlute 自己分辨）。
                EndFlute(SkyIslandBossRules.FluteInterval);
                yield break;
            }
            // 按时退出循环之前最后一帧挨的打也算。
            if (!interrupted && startHealth - health.CurrentHealth >= SkyIslandBossRules.FluteInterruptDamage) interrupted = true;
            if (interrupted)
            {
                // 字幕只教一次：之后每次打断都靠圈消失看出来，不刷屏。
                if (!interruptAnnounced)
                    Announce("笛声被打断了：趁它吹笛时打它。", "The flute tune breaks off. Hit it while it's playing.", true);
                interruptAnnounced = true;
                EndFlute(SkyIslandBossRules.FluteInterval * 0.5f);
                yield break;
            }
            if (MaskBroken())
            {
                // 面罩在这一口气里被打穿：吹不响，字幕交给节流推进里的那一句。
                EndFlute(SkyIslandBossRules.FluteInterval);
                yield break;
            }
            Blow(player);
            EndFlute(SkyIslandBossRules.FluteInterval);
        }

        /// <summary>笛声吹成：夜里有蚊群可招，就招到玩家身边并压过灭蚊灯；招不到只提示一次。</summary>
        private void Blow(CharacterMainControl player)
        {
            SkyIslandGnats gnats = SkyIslandGnats.Current;
            if (gnats != null && gnats.Usable && gnats.NightNow && player != null)
            {
                try
                {
                    int lured = gnats.LureSwarm(player.transform.position,
                        UnityEngine.Random.Range(SkyIslandBossRules.FluteGnatsMin, SkyIslandBossRules.FluteGnatsMax + 1));
                    gnats.SetHostileLure(SkyIslandBossRules.FluteLureSeconds);
                    lastLured = lured;
                }
                catch (Exception e)
                {
                    lastLured = 0;
                    Debug.LogWarning("[SkyIslandBoss] 蚋笛翁招云蚋失败：" + e.Message);
                }
                fluteCount++;
                // 每 9 秒一口：字幕只在第一口说清楚，之后看云蚋扑过来就知道。
                if (!fluteAnnounced)
                    Announce("蚋笛翁吹响了笛子：云蚋全往你身上扑，灭蚊灯也引不走它们。",
                        "The Gnat Piper plays its flute. Every gnat dives at you, and bug zappers can't draw them away.", true);
                fluteAnnounced = true;
                return;
            }
            if (noGnatsAnnounced) return;
            noGnatsAnnounced = true;
            Announce("笛声在水面上散开了，这会儿没有云蚋可招。", "The tune drifts across the water. There are no gnats to call right now.", false);
        }

        /// <summary>这一口笛收尾：收圈、定下一口的时间；不是倒下就把暂停的 AI 恢复。</summary>
        private void EndFlute(float cooldown)
        {
            DestroyRing();
            channeling = false;
            nextFluteAt = Time.time + cooldown;
            if (finished || health == null || health.IsDead) return;
            try { if (aiControl != null && aiControl.IsPaused) aiControl.Resume(CharacterMainControl.Main); }
            catch (Exception e) { Debug.LogWarning("[SkyIslandBoss] 蚋笛翁恢复失败：" + e.Message); }
        }

        // ====================================================================
        // 破甲断招
        // ====================================================================

        private void OnHurt(DamageInfo damage)
        {
            if (finished || maskBroken) return;
            SkyIslandBossProps.WearSoftPiece(boss, damage, "FaceMask", BossRushItemIds.SkyIslandMossgauzeMask);
        }

        private bool MaskBroken()
        {
            return SkyIslandBossForge.PieceBroken(SkyIslandBossProps.WornIn(boss, "FaceMask"), BossRushItemIds.SkyIslandMossgauzeMask);
        }

        private void TickBrokenGear()
        {
            if (maskBroken || !MaskBroken()) return;
            maskBroken = true;
            Announce("苔纱面罩被打穿了：蚋笛翁吹不响笛子了。",
                "The mossgauze mask is shot through. The Gnat Piper can no longer play its flute.", false);
        }

        // ====================================================================
        // 生命周期
        // ====================================================================

        private bool Aborted()
        {
            return finished || boss == null || health == null || health.IsDead || (context.Valid != null && !context.Valid());
        }

        private void Announce(string cn, string en, bool urgent)
        {
            if (context != null && context.Report != null) context.Report(L10n.T(cn, en), urgent);
        }

        private void DestroyRing()
        {
            if (fluteRing != null) Destroy(fluteRing.gameObject);
            fluteRing = null;
        }

        private void OnDead(DamageInfo damage)
        {
            if (finished) return;
            finished = true;
            Vector3 position = boss != null ? boss.transform.position : transform.position;
            DestroyRing();
            channeling = false;
            Announce("蚋笛翁倒下了，笛子滚进了池子里。", "The Gnat Piper falls, and its flute rolls into the pond.", false);
            SkyIslandBossForge.RaiseDefeated(profile, position);
        }

        private void OnDestroy()
        {
            if (subscribed && health != null) health.OnDeadEvent.RemoveListener(OnDead);
            subscribed = false;
            if (hurtSubscribed && health != null) health.OnHurtEvent.RemoveListener(OnHurt);
            hurtSubscribed = false;
            finished = true;
            DestroyRing();
            channeling = false;
            aiControl = null;
            context = null;
            profile = null;
            boss = null;
            health = null;
        }
    }
}
