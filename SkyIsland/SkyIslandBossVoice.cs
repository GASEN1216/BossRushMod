// ============================================================================
// SkyIslandBossVoice.cs - 头目 / 岛主与具名对手的事件化台词
// ============================================================================
// 【做什么】给一位刚装配好的头目 / 岛主（或折翎的战斗体、失控的守钟装置）接上三个时刻的头顶气泡：
//   出场（第一次注意到你）、跨过血线、倒下。话在 `SkyIslandChatterLines.Boss` / `.Champion`，
//   预算与四道门在 `SkyIslandChatter`，本文件只负责「什么时候该说」。
//
// 【和机制字幕不抢】招式控制器的预警（「离开它脚下的那一圈」）走 `SkyIslandHud.Caption` 的**警示通道**，
//   在屏幕下方，是必须读到的信息；本文件的气泡在头顶，是演出，漏读不影响打得过。
//   两条通道互不打断，正好一上一下互相补足。
//
// 【为什么不改九个招式控制器】每位头目自己最清楚「哪一招开始了」，但那要改九个文件、
//   每处再穿一条通道。血线 60% / 30% 两档已经压在多数档案的相位附近（悬根猎首 75/45、穗镰 80/50/25、
//   截信人 40），足够把演出落在同一批节拍上。将来某一位确实要「就在这一招上说话」，
//   再单独给那个控制器接 `SkyIslandBossContext.Bark`——通道已经在了。
//
// 【生命周期】自己订阅自己身上的 `Health.OnDeadEvent` / `OnHurtEvent`，`OnDestroy` 成对退订
//   （AGENTS §4.6）。装配失败（没有生命组件、没有话）只是这一位不说话，不影响这场仗。
// ============================================================================

using System;
using UnityEngine;

namespace BossRush
{
    /// <summary>COMPAT：一位头目 / 岛主 / 具名对手的台词时机。由 <see cref="SkyIslandBossForge"/> 装配时挂上。</summary>
    internal sealed class SkyIslandBossVoice : MonoBehaviour
    {
        /// <summary>两档血线。分开跨过各说一次，短时间连跨合并成一句；重复掉血不重播。</summary>
        private const float FirstThreshold = 0.6f;
        private const float SecondThreshold = 0.3f;

        /// <summary>出场判定与待播血线重试的推进间隔（秒）。</summary>
        private const float TickInterval = 0.5f;

        private SkyIslandBossContext context;
        private SkyIslandBossProfile profile;
        private string championId;
        private Health health;
        private AICharacterController ai;
        private float height = 2.1f;
        private float nextTick;
        private bool subscribed;
        private bool announced;
        private SkyIslandChatterEvent wounded;
        private bool finished;

        /// <summary>头目 / 岛主：话按档案走，气泡高度跟着体型。</summary>
        internal void Bind(CharacterMainControl character, SkyIslandBossProfile value, SkyIslandBossContext ctx)
        {
            profile = value;
            championId = null;
            height = 2.1f * (value != null && value.Scale > 0f ? value.Scale : 1f);
            Attach(character, ctx);
        }

        /// <summary>折翎的战斗体与失控的守钟装置：它们不在档案表里，走 <c>SkyIslandChatterLines.Champion</c>。</summary>
        internal void BindChampion(CharacterMainControl character, string id, SkyIslandBossContext ctx)
        {
            championId = id;
            profile = null;
            height = 2.2f;
            Attach(character, ctx);
        }

        private void Attach(CharacterMainControl character, SkyIslandBossContext ctx)
        {
            Detach();
            context = ctx;
            announced = finished = false;
            nextTick = 0f;
            wounded = default(SkyIslandChatterEvent);
            if (character == null || ctx == null || ctx.Bark == null)
            {
                enabled = false;
                return;
            }
            context = ctx;
            health = character.Health;
            if (health == null)
            {
                // 没有生命组件就没有血线与倒下：整条早退，这一位只是不说话。
                enabled = false;
                return;
            }
            // 官方 AI 组件挂在子物体上，根节点 GetComponent 恒为 null。
            ai = character.GetComponentInChildren<AICharacterController>();
            health.OnDeadEvent.AddListener(OnDead);
            health.OnHurtEvent.AddListener(OnHurt);
            subscribed = true;
            enabled = true;
        }

        /// <summary>这一刻该说的话。档案在就按档案，否则按具名对手。</summary>
        private string[] Lines(SkyIslandChatterMoment moment)
        {
            if (profile != null) return SkyIslandChatterLines.Boss(profile.Kind, profile.Variant, moment);
            return SkyIslandChatterLines.Champion(championId, moment);
        }

        /// <returns>这一句真的说出口了没有。出场那一句靠它决定记不记「已出场」，见 <see cref="Update"/>。</returns>
        private bool Say(SkyIslandChatterMoment moment, bool force)
        {
            if (context == null || context.Bark == null || (context.Valid != null && !context.Valid())) return false;
            string[] pool = Lines(moment);
            if (!SkyIslandChatterLines.HasLines(pool)) return false;
            return context.Bark(transform, height, pool, force);
        }

        /// <summary>
        /// 出场那一句。
        ///
        /// 认当前 searchedEnemy，尚未锁定时才认最近的玩家动静。官方 noticed 永久置位，
        /// NoticeFromCharacter 也会保留旧来源；只看它们会把队友开枪或旧动静误当成发现玩家。
        ///
        /// 【为什么说成了才 latch】上面那条一旦成立，玩家可能还在二十米外——调度器的距离门会把这一句吞掉。
        /// 若此时就记成"已出场"，玩家走到跟前时它再也不会开口。所以只有真的冒出气泡才算说过；
        /// 挨第一下打（<see cref="OnHurt"/>）时无论说没说都封口——那时候「我看见你了」已经不成立。
        /// </summary>
        private void Update()
        {
            if (finished || health == null || health.IsDead || Time.time < nextTick) return;
            nextTick = Time.time + TickInterval;
            if (context == null || (context.Valid != null && !context.Valid())) return;
            TryWounded();
            if (announced) return;
            if (ai != null && !SkyIslandChatter.TargetsPlayer(ai, CharacterMainControl.Main)) return;
            if (Say(SkyIslandChatterMoment.Noticed, false)) announced = true;
        }

        private void TryWounded()
        {
            if (wounded.Pending(Time.time) && Say(SkyIslandChatterMoment.Wounded, false)) wounded.Consume();
        }

        private void OnHurt(DamageInfo damage)
        {
            if (finished || health == null || health.IsDead) return;
            float max = health.MaxHealth;
            if (max <= 0f) return;
            float ratio = health.CurrentHealth / max;
            announced = true;
            // 同一击或短时间跨两档只留最新一档；后续无需再次受伤，也能在 Update 重试。
            int stage = ratio <= SecondThreshold ? 2 : ratio <= FirstThreshold ? 1 : 0;
            wounded.Observe(stage, Time.time);
            TryWounded();
        }

        /// <summary>倒下台词尽力插入一次：跳过同屏上限与冷却，静音、距离和官方展示失败仍可拒绝。</summary>
        private void OnDead(DamageInfo damage)
        {
            if (finished) return;
            finished = true;
            wounded.Discard();
            Say(SkyIslandChatterMoment.Down, true);
        }

        private void Detach()
        {
            if (subscribed && health != null)
            {
                health.OnDeadEvent.RemoveListener(OnDead);
                health.OnHurtEvent.RemoveListener(OnHurt);
            }
            subscribed = false;
            health = null;
            ai = null;
        }
        private void OnDestroy()
        {
            Detach();
            finished = true;
            wounded.Discard();
            context = null;
            profile = null;
            championId = null;
        }
    }
}
