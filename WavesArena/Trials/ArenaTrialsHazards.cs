// ============================================================================
// ArenaTrialsHazards.cs - 出征令的天灾与临时地形（困难 30–45 秒一阵，噩梦 15–25 秒、25% 叠第二种）
// ============================================================================
// 九种（数值见 ArenaTrialsTuning）：
//   陨火雨   6–10 个落点符环依次亮起，1.5 秒后火流星砸下（火）；
//   落雷     三道依次锁定玩家（预判一点走位），每道提前 1 秒在脚下起银环（电）；
//   毒雾     2–3 团贴地翻涌的瘴气，留 20 秒（毒）；暴雨减半；
//   地裂熔缝 一道折线裂口由暗红烧到亮金后喷发，碰到就灼伤，15 秒后冷却闭合（火）；暴雨减半；
//   墨潮横扫 先在起点聚起墨浪、用两道金线画出缺口那条路，再整排推过场地：击退 + 伤害，翻滚可穿，敌人也被卷；
//   狂风     10 秒侧向阵风（先见风后推），敌我都被吹；
//   旋涡     往中心拉、越近越狠，中心伤害；
//   冰封     1–2 片冰面，站在上面移速 −40%（敌我都吃）；
//   雾泽     一团浓雾：玩家在雾里看不远，雾里的敌人、或玩家在雾里时所有敌人都看不见 3 米外的玩家。
// 落点：以玩家（70%）或活着的波次首领（30%）为基准随机散开，贴 NavMesh；不放任何碰撞体，不做逐图审核。
// 伤害：玩家按生命上限吃满额，敌方身体吃 30%（把首领引进去有用，但磨不死 Boss）；只波及玩家与本局敌方身体。
// 推拉：狂风 / 旋涡 / 墨潮击退都先累进同一张表，每帧对每个角色只下一次「输入速度 + 推力」的强制位移；翻滚中的不推。
// 质感（owner 2026-11-25：高级、大气、史诗、无塑料感）：预警一律是画在真实判定半径上的符环 / 墨线 / 金线，
//   填充从圆心长到边界读剩余时间；冲击 = 冲击波 + 火星 + 尘 + 亮芯 + 瞬时灯；粒子少而亮；声音只用钟、磬、纸、火、战鼓。
// 生命周期：只在出征令困难 / 噩梦局里、本波开打期间排程；收波时所有天灾淡出，冰封减速、雾里致盲、推力当帧还原；
//   Shutdown（通关、阵亡、切图）同样收干净。轻松 / 标准局 Tick 第一行就返回。
// 本文件是排程与公共结算；各天灾本体在 ArenaTrialsHazardRuns.cs（基类 + 陨火雨 / 落雷 / 毒雾 / 熔缝）
//   与 ArenaTrialsHazardFields.cs（墨潮 / 狂风 / 旋涡 / 冰封 / 雾泽），表现小工具在 ArenaTrialsFx.cs。
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    internal sealed class ArenaTrialsHazards : ArenaTrialPart
    {
        private const string LogPrefix = "[BossRush] [出征令战规] ";
        private const int MaxRunning = 4;

        private readonly List<ArenaTrialHazardRun> running = new List<ArenaTrialHazardRun>(4);
        private readonly List<Health> victims = new List<Health>(12);
        private readonly List<CharacterMainControl> bodies = new List<CharacterMainControl>(12);
        private readonly Dictionary<CharacterMainControl, Vector3> pushes = new Dictionary<CharacterMainControl, Vector3>();
        private readonly List<ArenaTrialKnock> knocks = new List<ArenaTrialKnock>(4);
        private readonly HashSet<CharacterMainControl> frostedFoes = new HashSet<CharacterMainControl>();
        private readonly List<CharacterMainControl> frostScratch = new List<CharacterMainControl>(8);
        private readonly Dictionary<CharacterMainControl, ArenaTrialFogBlind> blinded = new Dictionary<CharacterMainControl, ArenaTrialFogBlind>();
        private readonly List<CharacterMainControl> blindScratch = new List<CharacterMainControl>(8);
        private bool playerFrosted;
        private bool playerFogged;
        private float nextIn = -1f;
        private ArenaTrialHazard lastKind = ArenaTrialHazard.Count;
        private ArenaTrialHazard pendingSecond = ArenaTrialHazard.Count;
        private float pendingSecondIn;

        internal ArenaTrialsHazards(ArenaTrials trials) : base(trials) { }

        private bool Enabled { get { return Trials.Profile.HasHazards; } }

        /// <summary>暴雨缩短毒雾 / 熔缝。</summary>
        internal float DurationFactor { get { return Trials.Sky != null ? Trials.Sky.HazardDurationFactor() : 1f; } }

        // ------------------------------------------------------------------
        // 波次
        // ------------------------------------------------------------------

        internal override void OnWaveStart(int wave, bool finale)
        {
            if (!Enabled) return;
            nextIn = ArenaTrialsTuning.HazardFirstDelay;
            pendingSecond = ArenaTrialHazard.Count;
        }

        internal override void OnWaveEnd()
        {
            if (!Enabled) return;
            nextIn = -1f;
            pendingSecond = ArenaTrialHazard.Count;
            RetireAll(0.9f);
            ClearTerrainEffects();
        }

        internal override void Shutdown()
        {
            nextIn = -1f;
            pendingSecond = ArenaTrialHazard.Count;
            RetireAll(0.6f);
            ClearTerrainEffects();
        }

        internal override void Tick(float deltaTime)
        {
            if (!Enabled) return;
            if (Trials.WaveLive) Schedule(deltaTime);
            if (running.Count == 0 && knocks.Count == 0 && blinded.Count == 0
                && !playerFrosted && !playerFogged && frostedFoes.Count == 0) return;

            for (int i = running.Count - 1; i >= 0; i--)
            {
                ArenaTrialHazardRun run = running[i];
                bool alive;
                try { alive = run.Step(deltaTime); }
                catch (Exception e)
                {
                    alive = false;
                    ModBehaviour.DevLog(LogPrefix + "[WARNING] 天灾 " + run.Kind + " 中途失败，提前收掉: " + e.Message);
                }
                if (!alive)
                {
                    running.RemoveAt(i);
                    run.Retire(0.8f);
                }
            }
            ApplyKnocks(deltaTime);
            ApplyPushes();
            RefreshFrost();
            RefreshFog();
        }

        internal override void ContributePlayer(ref ArenaTrialPlayerStats stats)
        {
            if (playerFrosted) stats.Move *= 1f + ArenaTrialsTuning.FrostSlow;
            if (playerFogged) stats.ViewDistance *= ArenaTrialsTuning.FogmirePlayerView;
        }

        internal override void ContributeFoe(CharacterMainControl foe, ref ArenaTrialFoeStats stats)
        {
            if (foe != null && frostedFoes.Contains(foe)) stats.Move *= 1f + ArenaTrialsTuning.FrostSlow;
        }

        // ------------------------------------------------------------------
        // 排程
        // ------------------------------------------------------------------

        private void Schedule(float deltaTime)
        {
            if (pendingSecond != ArenaTrialHazard.Count)
            {
                pendingSecondIn -= deltaTime;
                if (pendingSecondIn <= 0f)
                {
                    ArenaTrialHazard second = pendingSecond;
                    pendingSecond = ArenaTrialHazard.Count;
                    Unleash(second);
                }
            }
            if (nextIn < 0f) return;
            nextIn -= deltaTime;
            if (nextIn > 0f) return;

            ArenaTrialProfile profile = Trials.Profile;
            nextIn = UnityEngine.Random.Range(profile.HazardSecondsMin, profile.HazardSecondsMax);
            ArenaTrialHazard first = Pick(ArenaTrialHazard.Count);
            if (first == ArenaTrialHazard.Count) return;
            Unleash(first);
            if (profile.HazardDoubleChance > 0f && UnityEngine.Random.value < profile.HazardDoubleChance)
            {
                ArenaTrialHazard second = Pick(first);
                if (second != ArenaTrialHazard.Count)
                {
                    pendingSecond = second;
                    pendingSecondIn = ArenaTrialsTuning.HazardDoubleDelay;
                }
            }
        }

        private ArenaTrialHazard Pick(ArenaTrialHazard exclude)
        {
            ArenaTrialOmen omen = Trials.Sky != null ? Trials.Sky.Omen : ArenaTrialOmen.None;
            float total = 0f;
            for (int i = 0; i < (int)ArenaTrialHazard.Count; i++) total += WeightOf((ArenaTrialHazard)i, exclude, omen);
            if (total <= 0f) return ArenaTrialHazard.Count;
            float roll = UnityEngine.Random.value * total;
            ArenaTrialHazard fallback = ArenaTrialHazard.Count;
            for (int i = 0; i < (int)ArenaTrialHazard.Count; i++)
            {
                ArenaTrialHazard kind = (ArenaTrialHazard)i;
                float weight = WeightOf(kind, exclude, omen);
                if (weight <= 0f) continue;
                fallback = kind;
                if (roll < weight) return kind;
                roll -= weight;
            }
            return fallback;
        }

        private float WeightOf(ArenaTrialHazard kind, ArenaTrialHazard exclude, ArenaTrialOmen omen)
        {
            if (kind == exclude || kind == lastKind || IsRunning(kind)) return 0f;
            return ArenaTrialsTuning.HazardWeight(kind, omen);
        }

        private bool IsRunning(ArenaTrialHazard kind)
        {
            for (int i = 0; i < running.Count; i++)
            {
                if (running[i].Kind == kind) return true;
            }
            return false;
        }

        private void Unleash(ArenaTrialHazard kind)
        {
            if (running.Count >= MaxRunning) return;
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null || player.Health == null || player.Health.IsDead) return;
            ArenaTrialHazardRun run = null;
            try
            {
                switch (kind)
                {
                    case ArenaTrialHazard.Meteor: run = new ArenaTrialMeteorRain(this); break;
                    case ArenaTrialHazard.Lightning: run = new ArenaTrialLightning(this); break;
                    case ArenaTrialHazard.Miasma: run = new ArenaTrialMiasma(this); break;
                    case ArenaTrialHazard.Rift: run = new ArenaTrialRift(this); break;
                    case ArenaTrialHazard.InkTide: run = new ArenaTrialInkTide(this); break;
                    case ArenaTrialHazard.Gale: run = new ArenaTrialGale(this); break;
                    case ArenaTrialHazard.Vortex: run = new ArenaTrialVortex(this); break;
                    case ArenaTrialHazard.Frost: run = new ArenaTrialFrost(this); break;
                    case ArenaTrialHazard.Fogmire: run = new ArenaTrialFogmire(this); break;
                }
                if (run == null) return;
                run.Begin();
                running.Add(run);
                lastKind = kind;
                ModBehaviour.DevLog(LogPrefix + "天灾：" + kind + "（第 " + (Trials.Wave + 1) + " 波，开打 " + Trials.WaveClock.ToString("F0") + " 秒）");
            }
            catch (Exception e)
            {
                Debug.LogWarning(LogPrefix + "天灾 " + kind + " 起不来（跳过这一阵）: " + e.Message);
                if (run != null) run.Retire(0f);
            }
        }

        private void RetireAll(float seconds)
        {
            for (int i = 0; i < running.Count; i++)
            {
                try { running[i].Retire(seconds); }
                catch (Exception) { }
            }
            running.Clear();
        }

        /// <summary>冰封减速、雾里致盲、推力：收波 / 收尾时当帧还原。</summary>
        private void ClearTerrainEffects()
        {
            pushes.Clear();
            knocks.Clear();
            if (playerFrosted || playerFogged)
            {
                playerFrosted = false;
                playerFogged = false;
                Trials.MarkPlayerStatsDirty();
            }
            if (frostedFoes.Count > 0)
            {
                frostedFoes.Clear();
                Trials.MarkFoeStatsDirty();
            }
            RestoreAllSight();
        }

        // ------------------------------------------------------------------
        // 给各天灾用的工具
        // ------------------------------------------------------------------

        /// <summary>落点基准：七成玩家、三成一只活着的波次首领（没有首领就玩家）。</summary>
        internal Vector3 PickAnchor()
        {
            CharacterMainControl player = CharacterMainControl.Main;
            Vector3 at = player != null ? player.transform.position : Vector3.zero;
            if (UnityEngine.Random.value < ArenaTrialsTuning.HazardBossAnchorChance)
            {
                CharacterMainControl boss = RandomLiveBoss();
                if (boss != null) at = boss.transform.position;
            }
            return at;
        }

        internal CharacterMainControl RandomLiveBoss()
        {
            List<CharacterMainControl> foes = Trials.Foes;
            int seen = 0;
            CharacterMainControl pick = null;
            for (int i = 0; i < foes.Count; i++)
            {
                CharacterMainControl foe = foes[i];
                if (foe == null || foe.Health == null || foe.Health.IsDead || !Trials.IsWaveBoss(foe)) continue;
                seen++;
                if (UnityEngine.Random.Range(0, seen) == 0) pick = foe;
            }
            return pick;
        }

        /// <summary>在 around 周围 [minRadius, maxRadius] 的环里找一个贴 NavMesh 的点；找不到就退回 around 本身贴地。</summary>
        internal Vector3 Place(Vector3 around, float minRadius, float maxRadius)
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                float radius = maxRadius > minRadius ? UnityEngine.Random.Range(minRadius, maxRadius) : minRadius;
                Vector3 raw = around + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                Vector3 resolved;
                if (SpawnPositionHelper.TrySampleNavMesh(raw, out resolved, 0f, 2.5f) && Mathf.Abs(resolved.y - around.y) < 3.5f)
                {
                    return resolved;
                }
            }
            Vector3 fallback;
            if (SpawnPositionHelper.TrySampleNavMesh(around, out fallback, 0f, 2.5f)) return fallback;
            return around;
        }

        /// <summary>玩家 + 本局活着的敌方身体（每次调用重填同一张表；调用方不要留着它）。</summary>
        internal List<CharacterMainControl> Bodies()
        {
            bodies.Clear();
            CharacterMainControl player = CharacterMainControl.Main;
            if (player != null && player.Health != null && !player.Health.IsDead) bodies.Add(player);
            List<CharacterMainControl> foes = Trials.Foes;
            for (int i = 0; i < foes.Count; i++)
            {
                CharacterMainControl foe = foes[i];
                if (foe != null && foe.Health != null && !foe.Health.IsDead) bodies.Add(foe);
            }
            return bodies;
        }

        /// <summary>center 周围 radius 内的伤害对象（同一张临时表）。</summary>
        internal List<Health> Victims(Vector3 center, float radius)
        {
            Trials.CollectVictims(center, radius, victims);
            return victims;
        }

        /// <summary>按生命上限比例打一下（玩家满额、敌方 30%）。</summary>
        internal static bool HurtShare(Health health, float fraction, Vector3 from, ElementTypes element, bool dodgeable)
        {
            return ArenaTrials.HurtNeutral(health, ArenaTrials.ShareOfMaxHealth(health, fraction), from, dodgeable, element);
        }

        /// <summary>圆形范围内所有人挨一下。</summary>
        internal void Hit(Vector3 center, float radius, float fraction, ElementTypes element, bool dodgeable)
        {
            List<Health> list = Victims(center, radius);
            for (int i = 0; i < list.Count; i++) HurtShare(list[i], fraction, center, element, dodgeable);
        }

        internal static Vector3 PositionOf(Health health)
        {
            if (health == null) return Vector3.zero;
            CharacterMainControl character = health.TryGetCharacter();
            return character != null ? character.transform.position : health.transform.position;
        }

        /// <summary>这一帧给某个角色加一份推力（米 / 秒，水平）。</summary>
        internal void Push(CharacterMainControl character, Vector3 velocity)
        {
            if (character == null) return;
            velocity.y = 0f;
            Vector3 sum;
            pushes[character] = pushes.TryGetValue(character, out sum) ? sum + velocity : velocity;
        }

        /// <summary>一下击退：velocity 在 seconds 内按平方衰减。</summary>
        internal void Knock(CharacterMainControl character, Vector3 velocity, float seconds)
        {
            if (character == null || seconds <= 0f) return;
            ArenaTrialKnock knock = new ArenaTrialKnock();
            knock.Character = character;
            knock.Velocity = velocity;
            knock.Total = seconds;
            knock.Left = seconds;
            knocks.Add(knock);
        }

        private void ApplyKnocks(float deltaTime)
        {
            for (int i = knocks.Count - 1; i >= 0; i--)
            {
                ArenaTrialKnock knock = knocks[i];
                knock.Left -= deltaTime;
                if (knock.Character == null || knock.Left <= 0f)
                {
                    knocks.RemoveAt(i);
                    continue;
                }
                float t = Mathf.Clamp01(knock.Left / knock.Total);
                Push(knock.Character, knock.Velocity * (t * t));
            }
        }

        /// <summary>
        /// 每个角色这一帧只下一次强制位移：保留它自己的移动输入（AI 照常寻路、玩家照常走），再叠上推力。
        /// 官方 SetForceMoveVelocity 只管一帧、走角色控制器（撞墙照常停），翻滚中的不推（不打断翻滚手感）。
        /// </summary>
        private void ApplyPushes()
        {
            if (pushes.Count == 0) return;
            foreach (KeyValuePair<CharacterMainControl, Vector3> pair in pushes)
            {
                CharacterMainControl character = pair.Key;
                try
                {
                    if (character == null || character.Health == null || character.Health.IsDead || character.Dashing) continue;
                    Vector3 push = pair.Value;
                    push.y = 0f;
                    if (push.sqrMagnitude < 0.0004f) continue;
                    Vector3 input = character.MoveInput;
                    input.y = 0f;
                    float speed = character.Running ? character.CharacterRunSpeed : character.CharacterWalkSpeed;
                    character.SetForceMoveVelocity(input * speed + push);
                }
                catch (Exception) { }
            }
            pushes.Clear();
        }

        private bool Covered(ArenaTrialHazard kind, Vector3 position)
        {
            for (int i = 0; i < running.Count; i++)
            {
                if (running[i].Kind == kind && running[i].Covers(position)) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------
        // 冰封：站在冰面上的玩家 / 敌人移速 −40%（进出才重挂修饰）
        // ------------------------------------------------------------------

        private void RefreshFrost()
        {
            bool any = IsRunning(ArenaTrialHazard.Frost);
            if (!any && !playerFrosted && frostedFoes.Count == 0) return;
            CharacterMainControl player = CharacterMainControl.Main;
            bool playerIn = any && player != null && Covered(ArenaTrialHazard.Frost, player.transform.position);
            if (playerIn != playerFrosted)
            {
                playerFrosted = playerIn;
                Trials.MarkPlayerStatsDirty();
            }
            frostScratch.Clear();
            if (any)
            {
                List<CharacterMainControl> foes = Trials.Foes;
                for (int i = 0; i < foes.Count; i++)
                {
                    CharacterMainControl foe = foes[i];
                    if (foe == null || foe.Health == null || foe.Health.IsDead) continue;
                    if (Covered(ArenaTrialHazard.Frost, foe.transform.position)) frostScratch.Add(foe);
                }
            }
            bool changed = frostScratch.Count != frostedFoes.Count;
            for (int i = 0; i < frostScratch.Count && !changed; i++)
            {
                if (!frostedFoes.Contains(frostScratch[i])) changed = true;
            }
            if (!changed) return;
            frostedFoes.Clear();
            for (int i = 0; i < frostScratch.Count; i++) frostedFoes.Add(frostScratch[i]);
            Trials.MarkFoeStatsDirty();
        }

        // ------------------------------------------------------------------
        // 雾泽：玩家在雾里时视距收短；雾里的敌人、或玩家在雾里时所有敌人，暂时看不见 3 米外的玩家
        // 改的是官方 AI 的三个公开字段（强制追踪距离、视距、当前目标），记下原值，出雾 / 收波 / 收尾时原样写回。
        // ------------------------------------------------------------------

        private void RefreshFog()
        {
            bool any = IsRunning(ArenaTrialHazard.Fogmire);
            if (!any && !playerFogged && blinded.Count == 0) return;
            CharacterMainControl player = CharacterMainControl.Main;
            bool playerIn = any && player != null && Covered(ArenaTrialHazard.Fogmire, player.transform.position);
            if (playerIn != playerFogged)
            {
                playerFogged = playerIn;
                Trials.MarkPlayerStatsDirty();
            }

            float sight = ArenaTrialsTuning.FogmireSightDistance;
            DamageReceiver playerReceiver = player != null ? player.mainDamageReceiver : null;
            List<CharacterMainControl> foes = Trials.Foes;
            for (int i = 0; i < foes.Count; i++)
            {
                CharacterMainControl foe = foes[i];
                if (foe == null || foe.Health == null || foe.Health.IsDead) continue;
                bool blind = any && (playerIn || Covered(ArenaTrialHazard.Fogmire, foe.transform.position));
                ArenaTrialFogBlind state;
                bool has = blinded.TryGetValue(foe, out state);
                if (blind)
                {
                    if (!has)
                    {
                        AICharacterController ai = foe.GetComponentInChildren<AICharacterController>();
                        if (ai == null) continue;
                        state = new ArenaTrialFogBlind(ai);
                        blinded[foe] = state;
                    }
                    AICharacterController brain = state.Ai;
                    if (brain == null) continue;
                    // 每帧压一次：别的系统（脱困监视、召唤）可能把强制追踪又抬回 500
                    brain.forceTracePlayerDistance = 0f;
                    if (brain.sightDistance > sight) brain.sightDistance = sight;
                    if (playerReceiver != null && brain.searchedEnemy == playerReceiver && player != null
                        && ArenaTrials.FlatSqr(foe.transform.position, player.transform.position) > sight * sight)
                    {
                        brain.searchedEnemy = null;
                    }
                }
                else if (has)
                {
                    state.Restore();
                    blinded.Remove(foe);
                }
            }

            if (blinded.Count == 0) return;
            blindScratch.Clear();
            foreach (KeyValuePair<CharacterMainControl, ArenaTrialFogBlind> pair in blinded)
            {
                CharacterMainControl foe = pair.Key;
                if (foe == null || foe.Health == null || foe.Health.IsDead || !Trials.IsFoe(foe)) blindScratch.Add(foe);
            }
            for (int i = 0; i < blindScratch.Count; i++)
            {
                ArenaTrialFogBlind state;
                if (blinded.TryGetValue(blindScratch[i], out state)) state.Restore();
                blinded.Remove(blindScratch[i]);
            }
        }

        private void RestoreAllSight()
        {
            if (blinded.Count == 0) return;
            foreach (KeyValuePair<CharacterMainControl, ArenaTrialFogBlind> pair in blinded)
            {
                try { pair.Value.Restore(); }
                catch (Exception) { }
            }
            blinded.Clear();
        }
    }

    internal sealed class ArenaTrialKnock
    {
        public CharacterMainControl Character;
        public Vector3 Velocity;
        public float Total;
        public float Left;
    }

    /// <summary>被雾遮住的那只 AI 原来的两项视觉参数。</summary>
    internal sealed class ArenaTrialFogBlind
    {
        internal readonly AICharacterController Ai;
        private readonly float forceTrace;
        private readonly float sight;

        internal ArenaTrialFogBlind(AICharacterController ai)
        {
            Ai = ai;
            forceTrace = ai.forceTracePlayerDistance;
            sight = ai.sightDistance;
        }

        internal void Restore()
        {
            if (Ai == null) return;
            Ai.forceTracePlayerDistance = forceTrace;
            Ai.sightDistance = sight;
        }
    }
}
