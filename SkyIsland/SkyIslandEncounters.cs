using System;
using System.Collections.Generic;
using Duckov.Utilities;
using Pathfinding;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// COMPAT：区域接近生成，沿用官方装备、伤害、经验；地图拥有角色、preset 与战利品。
    ///
    /// 刷新口径（2026-09-09 可玩性复审后定）：
    /// - **自动组按出击刷新**：每次进岛都会重新生成，出击图应当每趟都有风险。
    /// - **手动组一次性**：折翎 / 钟守 / 噬风是具名剧情对手，存档事实一旦记下就不再出现。
    /// - 两者共用同一份持久 `clearedEncounters`；它对自动组只作为**剧情装置的前置证据**
    ///   （修风标 / 修星灯 / 校准观星镜），不再用于抑制生成。
    /// </summary>
    internal sealed class SkyIslandEncounters : IDisposable
    {
        private sealed class Encounter
        {
            internal string Id;
            internal Transform Marker;
            internal int Count;
            internal SkyIslandEncounterDefinition Definition;
            internal bool Manual, Started, Cleared, Spawning;
            /// <summary>带队是只在夜里出来的头目（蚋笛翁、镜中客）；整组换阵营（断风游猎）。装配时从档案读一次。</summary>
            internal bool NightLead, RivalFaction;
            internal float RetryAt;
            internal SkyIslandEnemyRecord[] Actors;
            /// <summary>同伴死亡按数量合并；成功发送或过期后消费，不进存档。</summary>
            internal SkyIslandChatterEvent Mourning;
            internal bool AllDead
            {
                get
                {
                    if (!Started || Spawning) return false;
                    for (int i = 0; i < Actors.Length; i++)
                    {
                        SkyIslandEnemyRecord actor = Actors[i];
                        if (actor.Died) continue;
                        // 夜限定带队从没刷出来：白天这一组只算随从，它不挡清场（清场之后夜里照样单独补刷）。
                        if (i == 0 && NightLead && actor.Life == null) continue;
                        return false;
                    }
                    return true;
                }
            }
        }
        private readonly List<Encounter> encounters = new List<Encounter>();
        private readonly List<CharacterRandomPreset> sources = new List<CharacterRandomPreset>();
        private readonly GameObject root;
        private readonly CharacterMainControl player;
        private readonly GraphMask mask;
        private readonly int groundMask;
        private readonly Func<bool> valid;
        private readonly Func<string, bool> completed;
        private readonly Action<string> cleared;
        private readonly Action<string, bool> report;
        /// <summary>遭遇 id → 玩家看得懂的名字（地标名或对手名）。由会话注入：本类不认识地标，也不反向依赖会话。</summary>
        private readonly Func<string, string> describe;
        /// <summary>噬风（或它的回响）本体倒下：带上遭遇 id，由会话分流战利品。</summary>
        private readonly Action<string, Vector3> stormDefeated;
        private bool closed;
        private float nextTick;
        /// <summary>头目 / 岛主招式控制器要的场景上下文（根节点、地面层、有效性、字幕通道），首次用到时建一次。</summary>
        private SkyIslandBossContext bossContext;
        /// <summary>
        /// 敌人侧的头顶气泡预算（小兵共享话语库 + 头目专属台词共用同一个同屏上限）。
        /// 居民侧另有一个实例，两边加起来同屏最多两个气泡。
        /// </summary>
        private readonly SkyIslandChatter chatter;
        /// <summary>小兵气泡挂多高。官方拾荒者模型比居民矮一点。</summary>
        private const float MobBubbleHeight = 2f;
        internal string ContentSource { get; private set; }

        /// <summary>内容表由会话加载一次后传入；同一次进岛不重复解析 World.json。</summary>
        internal SkyIslandEncounters(GameObject root, CharacterMainControl player, GraphMask mask, int groundMask,
            SkyIslandContentData content, Func<bool> valid, Func<string, bool> completed, Action<string> cleared,
            Action<string, bool> report, Func<string, string> describe, Action<string, Vector3> onStormDefeated)
        {
            if (content == null) throw new ArgumentNullException("content");
            if (describe == null) throw new ArgumentNullException("describe");
            this.root = root; this.player = player; this.mask = mask; this.groundMask = groundMask;
            this.valid = valid; this.completed = completed; this.cleared = cleared; this.report = report;
            this.describe = describe;
            this.stormDefeated = onStormDefeated;
            chatter = new SkyIslandChatter(SkyIslandChatter.EnemyCooldownMin, SkyIslandChatter.EnemyCooldownMax, valid);
            // 一次加载时缓存，不在每帧/每次遭遇扫描全局资源。
            foreach (CharacterRandomPreset preset in Resources.FindObjectsOfTypeAll<CharacterRandomPreset>())
                if (preset != null && !preset.isBoss && !preset.isZombie && preset.team == Teams.scav &&
                    preset.name.IndexOf("Dummy", StringComparison.OrdinalIgnoreCase) < 0 &&
                    !preset.name.StartsWith("BossRush_", StringComparison.Ordinal)) sources.Add(preset);
            sources.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            if (sources.Count == 0) throw new InvalidOperationException("天空岛没有可用的官方战斗角色资源");
            ContentSource = content.Source;
            foreach (SkyIslandEncounterDefinition definition in content.Encounters) Add(definition);
        }

        private void Add(SkyIslandEncounterDefinition definition)
        {
            Transform marker = root.transform.Find(definition.Marker);
            if (marker == null) throw new InvalidOperationException("遭遇点位缺失：" + definition.Marker);
            var actors = new SkyIslandEnemyRecord[definition.Count];
            for (int i = 0; i < definition.Count; i++) actors[i] = new SkyIslandEnemyRecord();
            encounters.Add(new Encounter { Id = definition.Id, Marker = marker, Count = definition.Count,
                Manual = definition.Manual, Definition = definition, Actors = actors,
                NightLead = !definition.Manual && SkyIslandBossForge.IsNightLead(definition.Id),
                RivalFaction = SkyIslandBossForge.IsRivalFaction(definition.Id) });
        }

        /// <summary>
        /// 按 id 取遭遇。刻意不用 `List.Find(e => e.Id == id)`：闭包捕获了 <paramref name="id"/>，
        /// 每次调用都要新建闭包对象与委托，而 <see cref="IsBusy"/> / <see cref="HasStarted"/>
        /// 是每帧路径（会话 Update 对折翎与钟守各问一次），等于每帧产生垃圾（AGENTS 4.12）。
        /// </summary>
        private Encounter Find(string id)
        {
            for (int i = 0; i < encounters.Count; i++)
                if (encounters[i].Id == id) return encounters[i];
            return null;
        }

#if BOSSRUSH_DEV
        /// <summary>
        /// Dev 全自动验收：把一组自动遭遇复原成「这一趟还没刷过」，主角走近时照常整组刷出（带队头目重新配装、重新挂招式）。
        /// 自动组一趟只刷一次，验收前面的步骤常把这一组清掉；头目步骤开头调它，就不依赖步骤顺序。
        /// 只动本趟内存：死亡事实与清场标记清零，活着的角色不碰；持久的 clearedEncounters 不回退（剧情前置照旧）。
        /// 死掉的角色身上的 <see cref="SkyIslandEnemyLife"/> 只在死亡事件那一刻写记录，清零之后不会再被写回。手动组不许重置。
        /// </summary>
        internal bool DevResetEncounter(string id, out string note)
        {
            Encounter encounter = Find(id);
            if (encounter == null) { note = "encounter_unknown:" + id; return false; }
            if (encounter.Manual) { note = "encounter_manual:" + id; return false; }
            if (encounter.Spawning) { note = "encounter_spawning:" + id; return false; }
            int restored = 0;
            foreach (SkyIslandEnemyRecord actor in encounter.Actors)
            {
                if (!actor.Died) continue;
                actor.Died = false;
                actor.Life = null;
                restored++;
            }
            bool wasCleared = encounter.Cleared;
            encounter.Cleared = false;
            encounter.RetryAt = 0f;
            if (CountLiving(encounter) == 0) encounter.Started = false;
            note = "reset_encounter=" + id + ",restored=" + restored + ",was_cleared=" + wasCleared;
            return true;
        }
#endif

        private bool AnySpawning()
        {
            for (int i = 0; i < encounters.Count; i++)
                if (encounters[i].Spawning) return true;
            return false;
        }

        internal bool IsBusy(string id)
        {
            Encounter encounter = Find(id);
            return encounter != null && encounter.Started && !encounter.Cleared && !encounter.AllDead;
        }

        /// <summary>
        /// 本局是否已经打响过这一组（含正在打、已打完、以及存档事实带来的一次性关闭）。
        ///
        /// 具名剧情对手的「剧情体该不该露面」必须用它，不能用 <see cref="IsBusy"/> 加持久 flag：
        /// 那是两个**延迟不同**的派生条件——最后一名倒下的那一帧 `IsBusy` 就转 false，
        /// 而持久 flag 要等下一次 <see cref="Tick"/>（0.25 s 节流）跑完 `cleared()` 并被存档接受；
        /// 中间这段窗口刚打死的人会站回自己的尸体旁，写屏障期间更是永远回不去。
        /// </summary>
        internal bool HasStarted(string id)
        {
            Encounter encounter = Find(id);
            return encounter != null && (encounter.Started || encounter.Cleared);
        }

        /// <summary>
        /// 本局还能被「清场」记账的遭遇组数。
        ///
        /// 只数**自动**组：手动组（折翎 / 钟守 / 噬风）要额外的剧情前置，本局未必开得了，
        /// 算进去会高估。宁可低估——低估只是少派一单，高估会派出做不完的委托。
        ///
        /// 这里**不能**再排除「存档里已清过」的组：自动组按出击刷新（见 <see cref="Tick"/> 的
        /// 短路条件只留给手动组），老档上照样会重新生成、照样能再记一次账。若沿用旧的
        /// `!completed(id)` 过滤，第二次进岛起可完成量恒为 0，「清理航路威胁」永远派不出来。
        /// </summary>
        internal int RemainingClearable
        {
            get
            {
                int count = 0;
                foreach (Encounter encounter in encounters)
                    if (!encounter.Manual && !encounter.Cleared) count++;
                return count;
            }
        }

        /// <summary>
        /// 指定点周围是否还有活着的敌人。给「战斗中不许开剧情面板」的门用。
        ///
        /// 刻意**不**复用 <see cref="HasLivingEnemies"/>：那是全图口径，玩家把一组敌人丢在
        /// 岛的另一头就会让全岛剧情交互永久不可用。按半径判定既能挡住「开面板当暂停键」，
        /// 又不会因为远处的残敌把主线卡死。活体上限 12，每次遍历开销可忽略。
        /// </summary>
        internal bool HasLivingEnemiesWithin(Vector3 point, float radius)
        {
            if (closed) return false;
            float squared = radius * radius;
            // 已清场的组也要看：夜限定带队可以在清场之后才单独补刷（活体上限 12，多遍历几组可忽略）。
            foreach (Encounter encounter in encounters)
            {
                foreach (SkyIslandEnemyRecord actor in encounter.Actors)
                {
                    if (actor.Died || actor.Life == null) continue;
                    if ((actor.Life.transform.position - point).sqrMagnitude <= squared) return true;
                }
            }
            return false;
        }

        /// <summary>本局装配出的遭遇组数。只读，给 F3 验收核对「内容表全量落地」。</summary>
        internal int GroupCount { get { return encounters.Count; } }

        /// <summary>当前活着的敌人数。只读，给 F3 验收记录 12 活体上限的实际水位。</summary>
        internal int LivingEnemyCount { get { return CountActiveActors(); } }

        internal bool HasLivingEnemies
        {
            get
            {
                foreach (Encounter encounter in encounters)
                {
                    if (encounter.Started && !encounter.Cleared && !encounter.AllDead) return true;
                    if (encounter.Cleared && CountLiving(encounter) > 0) return true;
                }
                return false;
            }
        }

        /// <summary>D/G 的装置要求整个区域两组均提交；其余 ID 返回单组的已接受结果。</summary>
        internal bool IsCleared(string id)
        {
            if (id == "D" || id == "G") return completed(id) && completed(id + "_02");
            return Find(id) != null && completed(id);
        }

        /// <summary>
        /// 自动敌群的触发半径（米）。布局 v2 岛间桥只有 27~53 米，旧值 85 米会让玩家还站在风铃集这类无敌安全枢纽时
        /// 就把邻岛敌群刷出来；55 米下站在风铃集任何一处都够不到邻岛敌群（最近 68 米），走上桥中段才触发。
        /// </summary>
        internal const float AutoSpawnRange = 55f;
        /// <summary>手动挑战（折翎 / 钟守 / 噬风）的发起距离（米）：要求在同一座岛上，布局 v2 里最远的是折翎 63 米。</summary>
        internal const float ChallengeRange = 70f;

        internal bool BeginChallenge(string id)
        {
            string reason;
            if (!CanBeginChallenge(id, out reason)) return false;
            Spawn(Find(id));
            return true;
        }

        /// <summary>
        /// 手动挑战此刻能不能发起，以及不能时「差什么」。<see cref="BeginChallenge"/> 与剧情面板挂不挂挑战项共用这一份
        /// （AGENTS §4.14「能不能挂」与「点了会不会被拒」共用同一份判据；2026-09-14 审核 F-28 ②：距离、交战中、同时存活上限
        /// 以前只在点下去之后判，面板上挂着的挑战项点了回一句笼统的「当前无法开始」）。
        /// 这一组已经清掉、根本不是手动组时 <paramref name="reason"/> 为 null——那不是引导。
        /// </summary>
        internal bool CanBeginChallenge(string id, out string reason)
        {
            reason = null;
            if (closed || !valid()) return false;
            Encounter encounter = Find(id);
            if (encounter == null || !encounter.Manual || encounter.Cleared || completed(id)) return false;
            if (encounter.Started)
            {
                reason = L10n.T("这一场已经打起来了。", "This fight has already begun.");
                return false;
            }
            if (Vector3.Distance(player.transform.position, encounter.Marker.position) > ChallengeRange)
            {
                reason = L10n.T("挑战地点就在这座岛上：走近一些再来。", "The challenge site is on this isle. Get closer first.");
                return false;
            }
            if (Time.time < encounter.RetryAt || AnySpawning() || CountActiveActors() + encounter.Count > 12)
            {
                reason = L10n.T("附近还在交战：等这一阵打完再来挑战。", "There's still fighting nearby. Finish it before starting a challenge.");
                return false;
            }
            return true;
        }

        internal void Tick()
        {
            // 节流排在委托之前：这一路每帧都进，而 0.25 秒的门 60 帧里挡掉 14/15，
            // 先判两个字段就不用每帧再走一次 IsSessionValid 委托（R-9「节流判断提前」）。
            // 顺序可换是因为 valid() 是纯查询（SkyIslandSession.IsSessionValid 只读状态）。
            if (closed || Time.time < nextTick || !valid()) return;
            nextTick = Time.time + 0.25f;
            int active = 0;
            foreach (Encounter encounter in encounters)
            {
                if (encounter.Cleared)
                {
                    // 清过场的组平常不会再有活人；夜限定带队清场之后才补刷的那一位照样算进同时存活上限。
                    active += CountLiving(encounter);
                    continue;
                }
                // 存档事实只用来一次性关掉**手动**组（折翎 / 钟守 / 噬风）：它们是具名剧情对手，
                // 打过一次就不该再出现。自动组按出击刷新——不这么做，跑通一遍之后全岛零敌人，
                // 而 39 个搜刮点每趟重刷，这张图就退化成无风险刷宝台。
                // 剧情前置（D/D_02 修风标、G/G_02 修星灯、S4 校准观星镜）读的仍是同一份持久事实，
                // 一次达成永久有效，重刷的敌群不会把已完成的装置重新锁上。
                if (!encounter.Started && encounter.Manual && completed(encounter.Id)) { encounter.Cleared = true; continue; }
                if (!encounter.Started) continue;
                foreach (SkyIslandEnemyRecord actor in encounter.Actors)
                {
                    // 死亡事实留在普通对象中，官方销毁角色后仍有效；活体丢失只补该槽，不重刷已死角色。
                    if (!actor.Died && actor.Life != null) active++;
                }
                if (encounter.AllDead && Time.time >= encounter.RetryAt)
                {
                    try
                    {
                        cleared(encounter.Id);
                        // 委托可能因写屏障/暂时失败未接受；保持物理清场事实并重复投递，不能重生敌群。
                        if (completed(encounter.Id))
                        {
                            encounter.Cleared = true;
                            report(L10n.T("航路已清理 · ", "Lane cleared · ") + describe(encounter.Id), false);
                        }
                    }
                    catch (Exception e)
                    {
                        // 异常原文进日志；提示条经 WithDetail，英文界面不拼中文原文。
                        Debug.LogWarning("[SkyIsland] clear record failed: " + e);
                        report(SkyIslandStoryRules.WithDetail(L10n.T("清场记录提交失败，将重试：",
                            "Could not record the clear; retrying"), e.Message), true);
                    }
                    encounter.RetryAt = Time.time + 1f;
                }
            }
            // 气泡排在生成之前：AnySpawning 期间（异步生成一组要跨好几帧）不推进的话，
            // chatter.PlayerPosition 会陈旧，而头目的血线 / 倒下台词按它判 20 米距离门。
            TickChatter();
            // 同时最多 12 名活跃敌人，一次只启动一组异步生成。既有遭遇只补失去 owner 的未死槽。
            if (AnySpawning()) return;
            foreach (Encounter encounter in encounters)
            {
                // 已清场的组只剩一种情况还要刷：白天清过场、带队是夜限定头目、现在入夜了（NightLeadDue）。
                if ((encounter.Manual && !encounter.Started) || (encounter.Cleared && !NightLeadDue(encounter)) || Time.time < encounter.RetryAt) continue;
                int missing = CountMissing(encounter);
                if (missing == 0 || active + missing > 12) continue;
                if ((player.transform.position - encounter.Marker.position).sqrMagnitude > AutoSpawnRange * AutoSpawnRange) continue;
                Spawn(encounter);
                break;
            }
        }

        /// <summary>
        /// 小兵的头顶气泡：**每次推进最多说一句**。优先级「身边倒下一个 &gt; 第一次注意到你 &gt; 闲话」——
        /// 有人刚倒下的时候还在念叨残铜值几个钱，比不说话更假。
        ///
        /// 头目 / 岛主 / 具名对手（<see cref="SkyIslandBossVoice"/>）与噬风不走这条路：生成时已经按
        /// <c>SkyIslandEnemyRecord.Silent</c> 标出来。四道门（静音 / 同屏上限 / 距离 / 单人冷却）全在
        /// <see cref="SkyIslandChatter"/> 里，这里只挑候选；<c>Ready</c> 挡掉的候选不为它建话语数组。
        ///
        /// 静音时不挑候选；恢复后按当前目标观测。待播事件最多保留 12 秒，
        /// 同类进度合并，发送失败可重试；战斗和最近动静期间不选闲话。
        /// </summary>
        private void TickChatter()
        {
            if (chatter == null || player == null || chatter.Muted) return;
            chatter.PlayerPosition = player.transform.position;
            SkyIslandEnemyRecord speaker = null;
            Encounter speakerGroup = null;
            SkyIslandChatterMoment moment = SkyIslandChatterMoment.Idle;
            int priority = 0;
            for (int i = 0; i < encounters.Count; i++)
            {
                Encounter encounter = encounters[i];
                if (!encounter.Started || encounter.Cleared) continue;
                int dead = 0;
                SkyIslandEnemyRecord living = null, freshlyNoticed = null, idle = null;
                for (int j = 0; j < encounter.Actors.Length; j++)
                {
                    SkyIslandEnemyRecord actor = encounter.Actors[j];
                    if (actor.Died) { dead++; continue; }
                    if (actor.Silent || actor.Life == null) continue;
                    bool targetsPlayer = SkyIslandChatter.TargetsPlayer(actor.Ai, player);
                    if (targetsPlayer) actor.Notice.Observe(1, Time.time);
                    else actor.Notice.Discard(); // 已转向别的目标，不补喊旧的「发现你」。
                    bool noticed = actor.Notice.Pending(Time.time);
                    Transform body = actor.Life.transform;
                    if (chatter.Ready(body, SkyIslandChatter.EventCooldown))
                    {
                        if (living == null) living = actor;
                        if (noticed && freshlyNoticed == null) freshlyNoticed = actor;
                    }
                    if (idle == null && !SkyIslandChatter.Engaged(actor.Ai) && chatter.Ready(body)) idle = actor;
                }
                // 每组都观测，即使先前已有高优先级候选；否则排在后面的事件会永远不开始过期。
                encounter.Mourning.Observe(dead, Time.time);
                bool mourn = encounter.Mourning.Pending(Time.time);
                if (mourn && living != null && priority < 3)
                {
                    speaker = living; moment = SkyIslandChatterMoment.AllyDown;
                    speakerGroup = encounter; priority = 3;
                }
                else if (freshlyNoticed != null && priority < 2)
                {
                    speaker = freshlyNoticed; moment = SkyIslandChatterMoment.Noticed;
                    speakerGroup = encounter; priority = 2;
                }
                else if (idle != null && !mourn && priority < 1)
                {
                    speaker = idle; moment = SkyIslandChatterMoment.Idle;
                    speakerGroup = encounter; priority = 1;
                }
            }
            if (speaker == null) return;
            bool rival = speakerGroup.RivalFaction;
            float cooldown = moment == SkyIslandChatterMoment.Idle ? -1f : SkyIslandChatter.EventCooldown;
            if (!chatter.TrySay(speaker.Life.transform, MobBubbleHeight,
                SkyIslandChatterLines.Mob(rival, moment), false, cooldown)) return;
            // 候选、距离、预算或官方展示失败都不消费事件。一次推进只提交真正发出的那一句。
            if (moment == SkyIslandChatterMoment.AllyDown) speakerGroup.Mourning.Consume();
            else if (moment == SkyIslandChatterMoment.Noticed) speaker.Notice.Consume();
        }

        private async void Spawn(Encounter encounter)
        {
            encounter.Started = encounter.Spawning = true;
            try
            {
                for (int i = 0; i < encounter.Count; i++)
                {
                    if (closed || !valid()) return;
                    SkyIslandEnemyRecord actor = encounter.Actors[i];
                    if (actor.Died || actor.Life != null) continue;
                    // 夜限定带队白天不刷：位置留着，玩家夜里走近时再补（SkyIslandBossForge.LeadWaitsForNight）。
                    if (LeadWaiting(encounter, i)) continue;
                    Vector3 point = FindGround(encounter.Marker, i);
                    CharacterRandomPreset clone = UnityEngine.Object.Instantiate(sources[PresetIndex(encounter.Id, i)]);
                    clone.name = "BossRush_SkyIsland_" + encounter.Id;
                    // 正式独立出击沿用官方 CharacterMainControl.OnDead 箱子、经验与魂语义。
                    clone.dropBoxOnDead = true;
                    clone.setActiveByPlayerDistance = false;
                    CharacterMainControl created = null;
                    bool retained = false, presetOwnedByCharacter = false;
                    try
                    {
                        // 在途 preset 仅由当前 async 栈拥有，场景退出无权提前销毁。
                        created = await clone.CreateCharacterAsync(point, Vector3.forward, -1, null, false);
                        if (created == null) throw new InvalidOperationException("官方角色创建失败");
                        SkyIslandEnemyLife life = created.gameObject.AddComponent<SkyIslandEnemyLife>();
                        life.OwnPreset(clone);
                        presetOwnedByCharacter = true;
                        if (closed || !valid() || root == null) return;
                        created.transform.SetParent(root.transform, true);
                        Seeker[] seekers = created.GetComponentsInChildren<Seeker>(true);
                        AICharacterController ai = created.GetComponentInChildren<AICharacterController>();
                        if (seekers.Length == 0 || ai == null) throw new InvalidOperationException("角色缺少战斗 AI / 导航");
                        foreach (Seeker seeker in seekers) { seeker.CancelCurrentPathRequest(); seeker.graphMask = mask; }
                        SpawnedEnemyActivationHelper.ReleaseFromPlayerDistanceSleep(created);
                        created.SetTeam(Teams.wolf);
                        // 断风游猎（SkyIslandBossRules.IsRivalFaction）整组换成另一阵营：官方 Team.IsEnemy 下与玩家、与岛上其余敌人都敌对。
                        if (encounter.RivalFaction) created.SetTeam(Teams.bear);
                        SkyIslandEnemyTier tier = encounter.Definition.TierFor(i);
                        SkyIslandEnemyTiers.ApplyAi(ai, tier);
                        ApplyIdentity(created, encounter, i, tier);
                        // 头顶气泡：头目 / 岛主 / 具名对手在身份层里挂过自己的台词组件，噬风按人设不说话；
                        // 其余全是小兵，走共享话语库。判一次存进记录，推进时不再逐个 GetComponent。
                        // `Notice` 必须跟着复位：这个槽位可能是补刷（上一位丢了 owner），
                        // 沿用旧标记的话新刷出来的这一位永远不会喊那句「有人上来了」。
                        actor.Ai = ai;
                        actor.Notice = default(SkyIslandChatterEvent);
                        actor.Silent = tier == SkyIslandEnemyTier.Storm
                            || created.GetComponent<SkyIslandBossVoice>() != null;
                        life.Bind(created, actor);
                        actor.Life = life;
                        retained = true;
                    }
                    finally
                    {
                        if (!retained && created != null) { created.gameObject.SetActive(false); UnityEngine.Object.Destroy(created.gameObject); }
                        if (!presetOwnedByCharacter && clone != null) UnityEngine.Object.Destroy(clone, created != null ? 0.1f : 0f);
                    }
                }
            }
            catch (Exception e)
            {
                encounter.RetryAt = Time.time + 10;
                Debug.LogWarning("[SkyIsland] encounter setup failed: " + e);
                if (!closed) report(SkyIslandStoryRules.WithDetail(L10n.T("天空岛遭遇准备失败，10 秒后可重试：",
                    "Encounter setup failed; retrying in 10 seconds"), e.Message), true);
            }
            finally { encounter.Spawning = false; }
        }

        /// <summary>
        /// 身份层：具名剧情对手保留自己的脸与名字（只吃数值），其余按档次装饰。
        /// 噬风的相位编排挂在带队者身上，死亡回调由它自己派发，不与清场记账争 owner。
        /// </summary>
        private void ApplyIdentity(CharacterMainControl created, Encounter encounter, int index, SkyIslandEnemyTier tier)
        {
            if (encounter.Id == "Zheling" && index == 0)
            {
                SkyIslandResidents.ApplyBattleFace(created, "sky_zheling");
                SkyIslandEnemyTiers.ApplyStoryChampion(created, "zheling", "折翎", "Zheling");
                SkyIslandBossForge.BindVoice(created, null, "zheling", BossContext());
                return;
            }
            if (encounter.Id == "BellKeeper" && index == 0)
            {
                SkyIslandResidents.ApplyBattleFace(created, "sky_bellkeeper");
                SkyIslandEnemyTiers.ApplyStoryChampion(created, "bellkeeper", "失控的守钟装置", "Runaway Bell Engine");
                SkyIslandBossForge.BindVoice(created, null, "bellkeeper", BossContext());
                return;
            }
            // 头目 / 岛主（SkyIslandBossRules 档案按「遭遇 id + 位次」查）：名字、数值、配装、掉落与招式控制器由 Forge 一次做完；
            // 这一位没有档案时返回 false，照常走下面的档次装饰。
            if (SkyIslandBossForge.TryApply(created, encounter.Id, index, BossContext())) return;
            SkyIslandEnemyTiers.Apply(created, tier);
            if (tier != SkyIslandEnemyTier.Storm) return;
            Transform bossTransform = created.transform;
            string encounterId = encounter.Id;
            // 首战与噬风·回响共用同一套相位编排，回响只多一个模式位（风眼钉在原地、每档多响一声）；倒下时带上 id，由会话分流奖励。
            created.gameObject.AddComponent<SkyIslandStormBoss>().Bind(created, valid, report, delegate
            {
                if (closed || stormDefeated == null || bossTransform == null) return;
                stormDefeated(encounterId, bossTransform.position);
            }, SkyIslandStormEchoRules.IsEcho(encounterId));
        }

        /// <summary>
        /// 这一组第 index 名用哪个官方 preset。
        ///
        /// 旧写法 `sources[i % sources.Count]` 与遭遇身份无关：全岛 13 组的**带队者永远是同一个**
        /// preset（按名字排序的第一个），第二名永远是第二个。一整张图打下来只见得到两三种敌人，
        /// 而 `sources` 里通常有十几种官方拾荒者。
        ///
        /// 改成按「遭遇 id + 位次」取稳定散列：不同区域拿到不同 preset，同一个位置每次进岛
        /// 仍是同一个（自动组现在按出击刷新，位置稳定比每趟随机更容易建立预期）。
        /// 用 `StableHash` 而不是 `string.GetHashCode`：后者在 Mono 与 .NET Core 上口径不同，
        /// 会让不同机器的同一处刷出不同敌人。`sources` 已按名字排序，下标同样跨机稳定。
        /// </summary>
        private int PresetIndex(string encounterId, int index)
        {
            return SkyIslandLootTables.StableHash(encounterId + "#" + index) % sources.Count;
        }

        private static int CountMissing(Encounter encounter)
        {
            int count = 0;
            for (int i = 0; i < encounter.Actors.Length; i++)
            {
                SkyIslandEnemyRecord actor = encounter.Actors[i];
                if (actor.Died || actor.Life != null || LeadWaiting(encounter, i)) continue;
                count++;
            }
            return count;
        }

        private static int CountLiving(Encounter encounter)
        {
            int count = 0;
            foreach (SkyIslandEnemyRecord actor in encounter.Actors) if (!actor.Died && actor.Life != null) count++;
            return count;
        }

        /// <summary>这一位是夜限定带队、此刻还不是夜里（白天不刷、不算缺人）。</summary>
        private static bool LeadWaiting(Encounter encounter, int index)
        {
            return index == 0 && encounter.NightLead && SkyIslandBossForge.LeadWaitsForNight(encounter.Id);
        }

        /// <summary>夜限定带队这一趟还没刷过、没死过，而现在入夜了：清过场的组也要把它单独补出来。</summary>
        private static bool NightLeadDue(Encounter encounter)
        {
            if (!encounter.NightLead || encounter.Actors.Length == 0) return false;
            SkyIslandEnemyRecord lead = encounter.Actors[0];
            return !lead.Died && lead.Life == null && !LeadWaiting(encounter, 0);
        }

        /// <summary>
        /// 穗镰「谷仓叫人」（头目 R3，经 <see cref="SkyIslandBossContext.CallGroup"/>）：把 <paramref name="id"/> 这一组还活着的人
        /// 挪到 <paramref name="near"/> 附近并盯上主角，返回挪过来几个。手动组、已清场的组、还没刷出来的人都不叫——
        /// 先把谷仓那边清了，它就喊不来人；不另开生成路径。
        /// </summary>
        internal int CallGroup(string id, Vector3 near)
        {
            if (closed || !valid()) return 0;
            Encounter encounter = Find(id);
            if (encounter == null || encounter.Manual || encounter.Cleared) return 0;
            int moved = 0;
            for (int i = 0; i < encounter.Actors.Length; i++)
            {
                SkyIslandEnemyRecord actor = encounter.Actors[i];
                if (actor.Died || actor.Life == null) continue;
                CharacterMainControl character = actor.Life.GetComponent<CharacterMainControl>();
                if (character == null || character.Health == null || character.Health.IsDead) continue;
                float angle = i * 2.1f;
                Vector3 ground;
                if (!SkyIslandBossProps.SnapNear(near + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 1.8f, BossContext(), 0.45f, 1.5f, out ground))
                    continue;
                if (!SkyIslandBossProps.Teleport(character, ground + Vector3.up * 0.1f, null)) continue;
                SkyIslandBossProps.NoticePlayer(character);
                moved++;
            }
            return moved;
        }
        private int CountActiveActors()
        {
            int count = 0;
            foreach (Encounter encounter in encounters)
                foreach (SkyIslandEnemyRecord actor in encounter.Actors)
                    if (!actor.Died && actor.Life != null) count++;
            return count;
        }

        /// <summary>
        /// 观星镜盔「站定标敌」的只读查询：把 <paramref name="radius"/> 米内活着的敌人本体追加进 <paramref name="into"/>，返回个数。
        /// 调用方（会话）先清空列表；这里不改遭遇状态、不分配。
        /// </summary>
        internal int CopyLivingEnemies(Vector3 center, float radius, List<Transform> into)
        {
            if (into == null || closed) return 0;
            float sqr = radius * radius;
            int count = 0;
            foreach (Encounter encounter in encounters)
                foreach (SkyIslandEnemyRecord actor in encounter.Actors)
                {
                    if (actor.Died || actor.Life == null) continue;
                    Transform body = actor.Life.transform;
                    if ((body.position - center).sqrMagnitude > sqr) continue;
                    into.Add(body);
                    count++;
                }
            return count;
        }

        /// <summary>F3 只读（SKY_CHATTER）：敌人这一侧的气泡预算——本趟说了几句、此刻有没有气泡挂着。玩法不读它。</summary>
        internal SkyIslandChatter ValidationChatter { get { return chatter; } }

        /// <summary>头目 / 岛主招式控制器的场景上下文：本对象的根节点、地面层、会话有效性、字幕通道、叫帮手与头顶气泡，首次用到时建一次。</summary>
        private SkyIslandBossContext BossContext()
        {
            if (bossContext == null)
                bossContext = new SkyIslandBossContext
                {
                    Root = root.transform, GroundMask = groundMask, Valid = valid, Report = report, CallGroup = CallGroup,
                    // 头目一场仗里只有四句，按小兵那 35–60 秒的冷却会把血线那两句全吞掉，所以单独给一个短冷却。
                    Bark = (speaker, height, pool, force) =>
                        chatter.TrySay(speaker, height, pool, force, SkyIslandChatter.BossCooldown)
                };
            return bossContext;
        }

        private Vector3 FindGround(Transform marker, int index)
        {
            for (int attempt = 0; attempt < 12; attempt++)
            {
                float angle = (index * 120 + attempt * 30) * Mathf.Deg2Rad;
                Vector3 point = marker.position + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * (index == 0 ? 0 : 3);
                RaycastHit hit;
                if (Physics.Raycast(point + Vector3.up * 2, Vector3.down, out hit, 4, groundMask, QueryTriggerInteraction.Ignore) &&
                    hit.transform.IsChildOf(root.transform) && !Physics.CheckCapsule(hit.point + Vector3.up * 0.6f,
                    hit.point + Vector3.up * 1.5f, 0.5f, GameplayDataSettings.Layers.wallLayerMask, QueryTriggerInteraction.Ignore))
                    return hit.point + Vector3.up * 0.1f;
            }
            throw new InvalidOperationException("遭遇落点不可站立：" + marker.name);
        }

        public void Dispose()
        {
            if (closed) return;
            closed = true;
            if (chatter != null) chatter.Clear();
            if (bossContext != null) bossContext.Bark = null;
            foreach (Encounter encounter in encounters)
                foreach (SkyIslandEnemyRecord actor in encounter.Actors)
                    if (actor.Life != null) { actor.Life.gameObject.SetActive(false); UnityEngine.Object.Destroy(actor.Life.gameObject); }
        }
    }

    internal sealed class SkyIslandEnemyRecord
    {
        internal bool Died;
        internal SkyIslandEnemyLife Life;
        /// <summary>
        /// 这一位不走小兵共享话语库：头目 / 岛主 / 具名剧情对手各有自己的台词
        /// （<see cref="SkyIslandBossVoice"/>），噬风按人设一个字都不说。生成时判一次，不进存档。
        /// </summary>
        internal bool Silent;
        /// <summary>「注意到你」的待播事件。按角色实例刷新，不进存档。</summary>
        internal SkyIslandChatterEvent Notice;
        /// <summary>官方战斗 AI（挂在子物体上）。生成时取一次，用来判「第一次注意到你」，省得每次推进再找一遍。</summary>
        internal AICharacterController Ai;
    }

    internal sealed class SkyIslandEnemyLife : MonoBehaviour
    {
        private Health health;
        private CharacterRandomPreset preset;
        private SkyIslandEnemyRecord record;
        private bool subscribed;
        internal void OwnPreset(CharacterRandomPreset value) { preset = value; }
        internal void Bind(CharacterMainControl value, SkyIslandEnemyRecord state)
        {
            record = state; health = value.Health;
            if (health == null) throw new InvalidOperationException("遭遇角色生命组件缺失");
            record.Died = health.IsDead;
            health.OnDeadEvent.AddListener(OnDead);
            subscribed = true;
        }
        private void OnDead(DamageInfo damage) { if (record != null) record.Died = true; }
        private void OnDestroy()
        {
            if (subscribed && health != null) health.OnDeadEvent.RemoveListener(OnDead);
            subscribed = false;
            // CharacterMainControl / 模型的 OnDestroy 仍可能读 preset；角色组件清理完成后再释放。
            if (preset != null) Destroy(preset, 0.1f);
            preset = null;
            record = null;
        }
    }
}
