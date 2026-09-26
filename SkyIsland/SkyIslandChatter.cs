// ============================================================================
// SkyIslandChatter.cs - 天空岛头顶气泡的调度（什么时候说、说不说得出口）
// ============================================================================
// 【分工】话语表在 `SkyIslandChatterLines`（纯规则、可离线执行）；本文件只管四道门与一个出口，
//   一行玩家可见文案都不放。
//
// 【复用官方气泡】出口是官方 `Duckov.UI.DialogueBubbles.DialogueBubblesManager.Show`，
//   与基地的快递员阿稳、叮当、羽织走的是同一套（`CourierNPCController.ShowRandomDialogue`）。
//   官方气泡自己每帧跟随 target（`DialogueBubble.UpdatePosition`，target 销毁后自动收），
//   所以会走动的居民、会被打死的敌人都不需要额外处理。
//
// 【四道门】顺序是「最便宜的先判」：
//   1. 静音：官方对话进行中、游戏暂停（剧情面板把 timeScale 压到 0）、会话已失效 —— 一律不说。
//      会话 `Update` 在面板可见时本来就提前 return，这道门是为了异步落地的那一帧。
//   2. 同屏上限：**每个 owner 同时至多一个气泡**。居民 owner 一个 + 敌人 owner 一个；倒下台词可强制插入，不保证像素层绝对上限。
//   3. 距离：玩家 `SpeakRange` 米内才说（平方比较，不开根）。看不见的地方说话等于白算。
//   4. 单人冷却：同一个说话者多久才轮得到再说一次。居民与小兵用不同的区间——
//      owner 定的口径是「居民中等密度、小兵话少一点」，所以两个 owner 各建一个实例、各带各的区间。
//
// 【每帧成本】关闭态是一次时间比较 + 一次距离平方比较的 O(1) 早返（AGENTS §4.12）；
//   驱动方每次派发只挑一个候选，不遍历全岛说话。
// ============================================================================

using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 说一句气泡。头目 / 岛主的招式控制器经 <see cref="SkyIslandBossContext"/> 拿到这个委托，
    /// 不直接持有敌人侧的 <see cref="SkyIslandChatter"/>。
    /// </summary>
    /// <param name="force">倒下那一句用：跳过同屏上限与单人冷却，静音与距离两道门照走。</param>
    internal delegate bool SkyIslandBarkDelegate(Transform speaker, float height, string[] pool, bool force);

    /// <summary>COMPAT：一组说话者共用的气泡预算。实例化持有，不用静态状态，随 owner 一起释放。</summary>
    internal sealed class SkyIslandChatter
    {
        /// <summary>玩家多远之内才说得着。布局 v2 的聚落直径约 40 米，20 米差不多是「走到跟前」。</summary>
        internal const float SpeakRange = 20f;

        /// <summary>居民：中等密度（owner 2026-09-17 定）。</summary>
        internal const float ResidentCooldownMin = 15f;
        internal const float ResidentCooldownMax = 25f;

        /// <summary>小兵：话少一点。同一名拾荒者要隔这么久才再开口。</summary>
        internal const float EnemyCooldownMin = 35f;
        internal const float EnemyCooldownMax = 60f;

        /// <summary>头目 / 岛主：一场仗里只有出场、两档血线、倒下四句，冷却只用来挡同一档重入。</summary>
        internal const float BossCooldown = 6f;
        /// <summary>事件不能沿用闲话的长冷却，也不能隔了半分钟才喊旧事。</summary>
        internal const float EventCooldown = 6f;
        internal const float EventLifetime = 12f;

        /// <summary>跟踪的说话者上限。一趟出击刷过的敌人会越攒越多，超限只清理已经过期的记录。</summary>
        private const int TrackedSpeakerCap = 160;

        private readonly Dictionary<int, float> nextAt = new Dictionary<int, float>();
        private readonly Dictionary<int, float> lastSpokenAt = new Dictionary<int, float>();
        private readonly Dictionary<int, int> lastLine = new Dictionary<int, int>();
        /// <summary>Prune 的临时缓冲：复用同一个 List，不在推进里分配。</summary>
        private readonly List<int> expired = new List<int>();
        private readonly float cooldownMin;
        private readonly float cooldownMax;
        private readonly Func<bool> valid;
        private float busyUntil;

        /// <summary>驱动方每次推进时写进来的玩家位置。事件化的台词（头目血线、倒下）用的是上一次推进的值，最多差 0.25 秒。</summary>
        internal Vector3 PlayerPosition;

        internal SkyIslandChatter(float cooldownMin, float cooldownMax, Func<bool> isValid)
        {
            this.cooldownMin = cooldownMin;
            this.cooldownMax = Mathf.Max(cooldownMin, cooldownMax);
            valid = isValid;
        }

        /// <summary>F3 只读：本趟进入官方展示流程的请求数；不代表实际像素可见或播放完整。玩法不读它。</summary>
        internal int SpokenCount { get; private set; }

        /// <summary>此刻是否占用该 owner 的气泡展示预算。F3 只读，不代替像素可见性检查。</summary>
        internal bool Busy { get { return Time.time < busyUntil; } }

        /// <summary>
        /// 此刻整个 owner 都不该说话（官方对话中 / 暂停 / 会话失效）。
        ///
        /// 驱动方先问这一句再遍历；静音期间不挑候选，恢复后再观测当前目标。
        /// 已经观测的待播事件按游戏时间过期，不积压到很久以后。
        /// </summary>
        internal bool Muted { get { return Silenced(); } }

        /// <summary>静音门：这三种情况下全岛一个气泡都不冒。</summary>
        private bool Silenced()
        {
            if (valid != null && !valid()) return true;
            if (DialogueManager.IsDialogueActive) return true;
            return BossRushUI.IsGamePaused();
        }

        /// <summary>
        /// 这位此刻轮不轮得到说话。驱动方用它**先挑候选再取台词**——取台词要现建数组，
        /// 挑不中的那些不该为此付代价。
        /// </summary>
        internal bool Ready(Transform speaker, float cooldown = -1f)
        {
            if (speaker == null || Silenced() || Time.time < busyUntil) return false;
            if ((speaker.position - PlayerPosition).sqrMagnitude > SpeakRange * SpeakRange) return false;
            return !CoolingDown(speaker.GetInstanceID(), cooldown);
        }

        /// <summary>
        /// 说一句。四道门全过才真的冒气泡。
        /// </summary>
        /// <param name="height">气泡挂在头顶多高。居民与血条名同高（2.2），敌人略低，岛主按体型略高。</param>
        /// <param name="pool">这一刻的话语池（<see cref="SkyIslandChatterLines"/> 现取，随语言与剧情变化）。</param>
        /// <param name="force">倒下那一句：跳过同屏上限与单人冷却。静音与距离照走。</param>
        /// <param name="cooldown">
        /// 本次请求距上次说话的最短间隔（秒）；负数表示遵守闲话冷却。事件说完仍重置完整的闲话冷却。
        /// 头目要用它：一场仗里出场、两档血线、倒下共四句，按小兵那 35–60 秒的区间会把血线那两句全吞掉。
        /// </param>
        /// <returns>是否已进入官方展示流程。</returns>
        internal bool TrySay(Transform speaker, float height, string[] pool, bool force = false, float cooldown = -1f)
        {
            if (speaker == null || !SkyIslandChatterLines.HasLines(pool)) return false;
            if (Silenced()) return false;
            if (!force && Time.time < busyUntil) return false;
            if ((speaker.position - PlayerPosition).sqrMagnitude > SpeakRange * SpeakRange) return false;

            int key = speaker.GetInstanceID();
            if (!force && CoolingDown(key, cooldown)) return false;

            int last;
            if (!lastLine.TryGetValue(key, out last)) last = -1;
            int index = SkyIslandChatterLines.Pick(pool.Length, last, UnityEngine.Random.Range(0, int.MaxValue));
            if (index < 0) return false;
            string line = pool[index];
            if (string.IsNullOrEmpty(line)) return false;

            float duration = Duration(line);
            if (!Show(speaker, height, line, duration)) return false;

            lastLine[key] = index;
            lastSpokenAt[key] = Time.time;
            nextAt[key] = Time.time + UnityEngine.Random.Range(cooldownMin, cooldownMax);
            busyUntil = Time.time + duration;
            SpokenCount++;
            Prune();
            return true;
        }

        // 显式短冷却用于事件：闲话刚说过也只等六秒，不必等完整的 35–60 秒。
        private bool CoolingDown(int key, float cooldown)
        {
            float next;
            if (cooldown >= 0f)
                return lastSpokenAt.TryGetValue(key, out next) && Time.time < next + cooldown;
            return nextAt.TryGetValue(key, out next) && Time.time < next;
        }

        /// <summary>当前目标或最近动静；不把永不复位的 noticed 当作一直在战斗。</summary>
        internal static bool Engaged(AICharacterController ai)
        {
            return ai != null && (ai.searchedEnemy != null || ai.isNoticing(EventLifetime));
        }

        internal static bool TargetsPlayer(AICharacterController ai, CharacterMainControl player)
        {
            if (ai == null || player == null) return false;
            // 强制追踪/视觉搜索直接写 searchedEnemy，不一定先经过听声的 NoticeFromCharacter。
            if (ai.searchedEnemy != null) return ai.searchedEnemy == player.mainDamageReceiver;
            return ai.NoticeFromCharacter == player && ai.isNoticing(EventLifetime);
        }

        /// <summary>
        /// 停留时长按可见字数算：英文同一句占的字符多、读起来不慢，所以每字符的权重比中文小
        /// （口径同 <c>CourierNPCController.ShowRandomDialogue</c>）。气泡不挡操作，长一点只是好读。
        /// </summary>
        private static float Duration(string line)
        {
            return Mathf.Clamp(1.4f + line.Length * (L10n.IsChinese ? 0.18f : 0.07f), 2.5f, 4.5f);
        }

        /// <summary>官方气泡；speed = -1 沿用官方默认速度，不需要交互、不可跳过。</summary>
        private static bool Show(Transform speaker, float height, string line, float duration)
        {
            try
            {
                var manager = Duckov.UI.DialogueBubbles.DialogueBubblesManager.Instance;
                if (manager == null || !manager.isActiveAndEnabled) return false;
                var request = Duckov.UI.DialogueBubbles.DialogueBubblesManager.Show(
                    line, speaker, height, false, false, -1f, duration);
                var awaiter = request.GetAwaiter();
                // 官方缺 manager / prefab 会同步成功结束，异常也可能封在 UniTask 里。
                // 此处正时长的正常展示必定跨帧；已完成就消费结果并拒绝记账，不碰私有 prefab / 池。
                if (awaiter.IsCompleted)
                {
                    awaiter.GetResult();
                    return false;
                }
                // 只消费一次任务；后续展示异常由静态回调观察，不捕获 owner 或复活已清理的预算。
                UniTaskExtensions.Forget(request, OnShowFailed);
                return true;
            }
            catch (Exception e)
            {
                OnShowFailed(e);
                return false;
            }
        }

        private static void OnShowFailed(Exception error)
        {
            ModBehaviour.DevLog("[SkyIslandChatter] [WARNING] 气泡显示失败：" + error.Message);
        }

        /// <summary>某个说话者没了（敌人倒下、居民离岛）：忘掉它的记录。</summary>
        internal void Forget(Transform speaker)
        {
            if (speaker == null) return;
            int key = speaker.GetInstanceID();
            nextAt.Remove(key);
            lastSpokenAt.Remove(key);
            lastLine.Remove(key);
        }

        /// <summary>
        /// 跟踪表长到上限时**只丢已经过期的那些**（冷却已经走完、下次说话不受影响的）。
        ///
        /// 早先写的是整表清空，那会把全场还在冷却里的说话者一起放行：一趟长出击里攒够 160 个记录的那一刻，
        /// 十来个小兵同时变成「刚生成、没冷却」，接下来每个气泡间隔都有人抢着说话，正好是这套预算要避免的事。
        /// 过期记录一个不留还不够时（极端情况下全在冷却里）就随它超一点——多占几百字节，好过把节奏打乱。
        /// </summary>
        private void Prune()
        {
            if (nextAt.Count <= TrackedSpeakerCap) return;
            float now = Time.time;
            expired.Clear();
            foreach (KeyValuePair<int, float> pair in nextAt)
                if (now >= pair.Value) expired.Add(pair.Key);
            for (int i = 0; i < expired.Count; i++)
            {
                nextAt.Remove(expired[i]);
                lastSpokenAt.Remove(expired[i]);
                lastLine.Remove(expired[i]);
            }
            expired.Clear();
        }

        /// <summary>owner 释放时清空。本类不持有 Unity 对象，也没有静态状态，不需要别的清理。</summary>
        internal void Clear()
        {
            nextAt.Clear();
            lastSpokenAt.Clear();
            lastLine.Clear();
            busyUntil = 0f;
        }
    }

    /// <summary>
    /// 单个说话者/小队的待播进度。新版本覆盖旧版本，过期或成功才消费；不持有宿主对象。
    /// 小兵发现、同伴死亡、Boss 血线共用它，新增事件仍由各自 owner 观测与派发。
    /// </summary>
    internal struct SkyIslandChatterEvent
    {
        private int observed;
        private int consumed;
        private float expiresAt;

        internal void Observe(int version, float now)
        {
            if (version <= observed) return;
            observed = version;
            expiresAt = now + SkyIslandChatter.EventLifetime;
        }

        internal bool Pending(float now)
        {
            if (now >= expiresAt) Discard();
            return observed > consumed;
        }

        internal void Consume() { consumed = observed; }
        internal void Discard() { consumed = observed; }
    }

}
