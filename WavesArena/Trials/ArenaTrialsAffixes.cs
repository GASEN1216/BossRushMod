// ============================================================================
// ArenaTrialsAffixes.cs - 出征令的首领词缀（凶相 / 凶煞：分身、护卫、身法、死相四类各两个）
// ============================================================================
// 困难每只首领 1 条（压轴 2 条），噩梦 3 条（压轴 4 条），先每类取一个、取自不同类，开波后首领登记进战规时当场抽：
//   分身类  分裂：倒下时裂成两只小分身；双生：开场带一只孪生，两只没在 10 秒内一起倒下，先倒的那只被拉起（一对最多拉起两次）。
//   护卫类  护驾：身边三名亲卫，亲卫没清完时首领减伤八成；召唤：每 20 秒叫一批影从（同时最多 6 只）。
//   身法类  闪现：冒烟半秒后落到玩家身后；狂暴：半血以下体型变大、移速大涨、伤害 +25%（2026-11-27 起，原先只长个子）。
// 全场亲卫 + 影从（含正在叫的）最多 AffixMinionCap 只：三首领同台的噩梦不至于叫出二十多只，撞上官方 AI 重寻路饿死。
//   死相类  亡魂：倒下后一缕亡魂追人 15 秒；殉爆：倒下 3 秒后原地炸开。
// 内容 Boss（龙裔、龙皇、女巫、冠军之影、蚀日花冠）自带阶段与专属刷怪，只挂身法 / 死相两类，不另叫身体；
// 噩梦要 3 / 4 条时两类不够，就在这两类里补另一个（压轴把闪现、狂暴、亡魂、殉爆挂全），条数与额外掉落照写的算。
//
// 身体（分身、双生、亲卫、影从）由竞技场模块照首领的角色预设刷出来（WavesArenaRuntimeModule.SpawnExpeditionAffixBodyAsync）：
// 不进本波座位、不登记掉落、死了不掉箱；登记进战规后照样吃天象、天灾与诅咒。
//
// 收波：分身 / 双生是「这一波还没打完」——首领倒下时模块先问 HoldWaveFor，还有身体要打（或正在叫出来、双生还在倒计时）
// 就压着不收；本类在 Tick 里看它们打完了，置 releasePending，由 ArenaTrials.Update 最后一句交还模块推波。
// 压满 AffixHoldMaxSeconds 还没打完（身体卡进墙里），把剩下的散掉照常收波。亲卫 / 影从不压收波：主将一倒就散。
// 亡魂、殉爆不是身体，收波后照常演完，只在战规收尾时收掉。
//
// 额外掉落：本体（波次首领）死时按词缀条数多掉 1 / 2 件（从它自己的掉落池抽，见 BossLootEvent）；身体一律不掉。
// 血条名字：HealthBar.RefreshCharacterIcon 的补丁把名缀写在官方名字前（ArenaTrialsPatches），登记后主动刷一次。
// 表现在 ArenaTrialsAffixFx.cs；质感口径同天灾（ArenaTrialsFx 文件头）。
// ============================================================================

using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using ItemStatsSystem.Stats;
using UnityEngine;

namespace BossRush
{
    /// <summary>词缀叫出来的身体是哪一种。</summary>
    internal enum ArenaAffixRole
    {
        /// <summary>分裂出来的小分身（压收波）。</summary>
        Shard = 0,
        /// <summary>双生的另一只、或被拉起的那只（压收波）。</summary>
        Twin = 1,
        /// <summary>护驾的亲卫（不压收波，主将倒下就散）。</summary>
        Guard = 2,
        /// <summary>召唤出来的影从（同上）。</summary>
        Shade = 3,
    }

    /// <summary>一只挂了词缀的波次首领本体。死后记录留到下一波开打（额外掉落、亡魂殉爆要用）。</summary>
    internal sealed class ArenaAffixBearer
    {
        internal CharacterMainControl Body;
        /// <summary>null = 内容 Boss（不叫身体）。</summary>
        internal EnemyPresetInfo Preset;
        internal CharacterRandomPreset Template;
        internal Teams Team;
        internal readonly List<ArenaAffix> Affixes = new List<ArenaAffix>(4);
        internal string NamePrefix = string.Empty;
        /// <summary>
        /// 首领的生命上限（全局倍率、战帖与变异词条都乘过）：分身、亲卫按它折算。变异词条在入场通知之后才挂，
        /// 所以叫身体、首领倒下时都经 MasterMaxHealth 现读刷新，这里只是读不到时的后备。
        /// </summary>
        internal float MaxHealth;
        /// <summary>狂暴那一刻的体型（巨灵兵、群鼠的缩放已经挂上）：长大以它为底，收尾还原回它。</summary>
        internal Vector3 BaseScale = Vector3.one;
        internal Vector3 LastPosition;
        internal bool DeathHandled;
        internal bool Frenzied;
        internal float FrenzyGrow = -1f;
        internal float BlinkTimer;
        internal float BlinkWindup = -1f;
        internal float SummonTimer;
        internal bool GuardUp;
        internal readonly List<CharacterMainControl> Guards = new List<CharacterMainControl>(3);
        internal readonly List<CharacterMainControl> Shades = new List<CharacterMainControl>(6);
        internal ArenaAffixTwinPair Twin;
        internal ArenaAffixAura Aura;

        internal bool Has(ArenaAffix affix) { return Affixes.Contains(affix); }
        internal bool Alive { get { return Body != null && Body.Health != null && !Body.Health.IsDead; } }
    }

    /// <summary>词缀叫出来的一个身体。</summary>
    internal sealed class ArenaAffixBody
    {
        internal CharacterMainControl Body;
        internal ArenaAffixRole Role;
        internal ArenaAffixBearer Master;
        /// <summary>占首领生命上限的几成（双生被拉起时按它再折一次）。</summary>
        internal float Share;
        internal float MaxHealth;
        /// <summary>这只身体的生命上限修饰（在 bodyRecords 里）：身体移出列表时一并摘掉引用。</summary>
        internal BossRushStatModifierRecord HealthRecord;
        internal Vector3 LastPosition;
        internal bool Dead;
        internal ArenaAffixVeil Veil;

        internal bool HoldsWave { get { return Role == ArenaAffixRole.Shard || Role == ArenaAffixRole.Twin; } }
    }

    /// <summary>双生的一对：当前两个成员、先倒下那只的位置与倒计时、已经拉起过几次。</summary>
    internal sealed class ArenaAffixTwinPair
    {
        internal ArenaAffixBearer Bearer;
        internal CharacterMainControl A, B;
        internal float Countdown = -1f;
        internal bool FallenIsA;
        internal Vector3 FallenAt;
        /// <summary>先倒下那只占首领生命上限的几成（本体 1，双生 TwinHealth）。</summary>
        internal float FallenShare;
        internal int Revives;
        /// <summary>拉起的那只正在刷（时限已到、还没站起来）。</summary>
        internal bool Reviving;
        internal bool Done;
        internal ArenaAffixOmen Omen;
        internal ArenaAffixTether Tether;
    }

    /// <summary>召唤的一个落点：符阵亮 SummonWindup 秒后影从从里面起来。</summary>
    internal sealed class ArenaAffixPortal
    {
        internal ArenaAffixBearer Bearer;
        internal Vector3 At;
        internal float Remaining;
        internal ArenaAffixOmen Omen;
    }

    internal sealed class ArenaTrialsAffixes : ArenaTrialPart
    {
        /// <summary>召唤符阵亮多久影从才起来。</summary>
        private const float SummonWindup = 1.2f;
        /// <summary>闪现条件：离玩家至少这么远才闪（贴脸了不必闪），太远（不在一个战场上）也不闪。</summary>
        private const float BlinkMinDistance = 4.5f, BlinkMaxDistance = 40f;

        private readonly List<ArenaAffixBearer> bearers = new List<ArenaAffixBearer>(4);
        private readonly List<ArenaAffixBody> bodies = new List<ArenaAffixBody>(12);
        private readonly List<ArenaAffixTwinPair> pairs = new List<ArenaAffixTwinPair>(2);
        private readonly List<ArenaAffixPortal> portals = new List<ArenaAffixPortal>(6);
        private readonly List<ArenaAffixAftermath> aftermaths = new List<ArenaAffixAftermath>(4);
        /// <summary>改了身体生命上限的修饰（身体死了随角色 Item 走，收尾逐条摘）。</summary>
        private readonly List<BossRushStatModifierRecord> bodyRecords = new List<BossRushStatModifierRecord>(16);
        private readonly List<ArenaAffixKind> kindScratch = new List<ArenaAffixKind>(4);

        private CharacterMainControl pendingSourceBody;
        private EnemyPresetInfo pendingSource;
        /// <summary>正在叫出来、会压收波的身体数（分身、双生、拉起）。</summary>
        private int pendingHoldSpawns;
        /// <summary>正在叫出来的亲卫 / 影从（全场上限要算上它们）。</summary>
        private int pendingMinionSpawns;
        /// <summary>压满上限后加一：之前发出的压波刷怪回来时直接散掉，不再计数。</summary>
        private int holdGeneration;
        private bool waveHeld;
        private float holdClock;
        private bool releasePending;
        private bool closed;

        internal ArenaTrialsAffixes(ArenaTrials trials) : base(trials) { }

        private bool Enabled { get { return Trials.Profile.HasAffixes; } }

        // ------------------------------------------------------------------
        // 别的部分要问的
        // ------------------------------------------------------------------

        /// <summary>护驾亲卫、召唤影从：不吃血月加成，打中玩家不记血债。</summary>
        internal bool IsMinion(CharacterMainControl character)
        {
            ArenaAffixBody body = FindBody(character);
            return body != null && (body.Role == ArenaAffixRole.Guard || body.Role == ArenaAffixRole.Shade);
        }

        /// <summary>分裂 / 双生叫出来的首领分身：击杀按首领算回血。</summary>
        internal bool IsCloneOfBoss(CharacterMainControl character)
        {
            ArenaAffixBody body = FindBody(character);
            return body != null && body.HoldsWave;
        }

        /// <summary>这只首领死后多掉几件（只算波次首领本体；身体、没挂词缀的都是 0）。</summary>
        internal int BonusLootFor(CharacterMainControl boss)
        {
            ArenaAffixBearer bearer = FindBearer(boss);
            return bearer != null ? ArenaTrialsTuning.AffixBonusLoot(bearer.Affixes.Count) : 0;
        }

        /// <summary>NotifyBossJoined 在 AddFoe 之前交来的普通 Boss 预设（内容 Boss 为 null）。</summary>
        internal void NoteSource(CharacterMainControl boss, EnemyPresetInfo preset)
        {
            pendingSourceBody = boss;
            pendingSource = preset;
        }

        /// <summary>血条名缀（补丁读）：本体是「分裂·狂暴 」，身体是「分身 」「亲卫 」等；不是本局的返回空。</summary>
        internal static string NamePrefixFor(Health health)
        {
            ArenaTrials trials = ArenaTrials.Active;
            if (trials == null || trials.Affixes == null || health == null) return null;
            CharacterMainControl character = health.TryGetCharacter();
            if (character == null) return null;
            ArenaAffixBearer bearer = trials.Affixes.FindBearer(character);
            if (bearer != null) return bearer.NamePrefix;
            ArenaAffixBody body = trials.Affixes.FindBody(character);
            return body != null ? RolePrefix(body.Role) : null;
        }

        // ------------------------------------------------------------------
        // 收波（模块问 / 交回）
        // ------------------------------------------------------------------

        /// <summary>
        /// 模块在推波之前问：这只首领倒下后还有没有身体要打。先幂等地结算它的死亡（叫分身、开双生计时、亡魂殉爆），
        /// 再看场上有没有压收波的身体、刷怪有没有在路上。返回 true 时模块压着不推，打完由 <see cref="ConsumeWaveRelease"/> 交回。
        /// </summary>
        internal bool HoldWaveFor(CharacterMainControl boss)
        {
            if (closed) return false;
            ArenaAffixBearer bearer = FindBearer(boss);
            if (bearer != null) HandleBearerDeath(bearer);
            bool hold = HoldActive();
            if (hold)
            {
                waveHeld = true;
                holdClock = 0f;
                releasePending = false;
            }
            return hold;
        }

        /// <summary>ArenaTrials.Update 最后一句问：压着的收波这一帧该不该放开（只返回 true 一次）。</summary>
        internal bool ConsumeWaveRelease()
        {
            if (!releasePending) return false;
            releasePending = false;
            return true;
        }

        private bool HoldActive()
        {
            if (pendingHoldSpawns > 0) return true;
            for (int i = 0; i < bodies.Count; i++)
            {
                if (bodies[i].HoldsWave && !bodies[i].Dead) return true;
            }
            for (int i = 0; i < pairs.Count; i++)
            {
                if (!pairs[i].Done && (pairs[i].Countdown >= 0f || pairs[i].Reviving)) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------
        // 波次与登记
        // ------------------------------------------------------------------

        internal override void OnWaveStart(int wave, bool finale)
        {
            waveHeld = false;
            releasePending = false;
            // 上一波倒下的本体记录用完了（掉落在死的那一刻就算过）
            for (int i = bearers.Count - 1; i >= 0; i--)
            {
                if (bearers[i].DeathHandled) bearers.RemoveAt(i);
            }
        }

        internal override void OnWaveEnd()
        {
            // 收波时还在场上的亲卫 / 影从一起散掉；压收波的身体正常已经打完（超时或模块强推时也一并散掉）
            for (int i = bodies.Count - 1; i >= 0; i--) Dissolve(bodies[i]);
            bodies.Clear();
            for (int i = 0; i < portals.Count; i++)
            {
                if (portals[i].Omen != null) portals[i].Omen.Retire(0.5f);
            }
            portals.Clear();
            for (int i = 0; i < pairs.Count; i++) EndPair(pairs[i]);
            pairs.Clear();
            for (int i = 0; i < bearers.Count; i++)
            {
                if (bearers[i].Aura != null && !bearers[i].Alive)
                {
                    bearers[i].Aura.Retire(0.8f);
                    bearers[i].Aura = null;
                }
            }
            pendingHoldSpawns = 0;
            holdGeneration++;
            waveHeld = false;
            Trials.MarkFoeStatsDirty();
        }

        internal override void OnFoeJoined(CharacterMainControl foe, bool waveBoss)
        {
            if (!waveBoss || foe == null || closed) return;
            EnemyPresetInfo preset = pendingSourceBody == foe ? pendingSource : null;
            pendingSourceBody = null;
            pendingSource = null;
            if (!Enabled || FindBearer(foe) != null) return;

            ArenaAffixBearer bearer = new ArenaAffixBearer();
            bearer.Body = foe;
            bearer.Preset = preset;
            bearer.Template = preset != null ? foe.characterPreset : null;
            if (bearer.Template == null) bearer.Preset = null;
            bearer.Team = foe.Team;
            bearer.MaxHealth = foe.Health != null ? Mathf.Max(1f, foe.Health.MaxHealth) : 1f;
            bearer.LastPosition = foe.transform.position;
            Roll(bearer, Trials.FinaleWave ? Trials.Profile.FinaleAffixes : Trials.Profile.Affixes);
            if (bearer.Affixes.Count == 0) return;
            bearer.NamePrefix = BuildPrefix(bearer.Affixes);
            bearers.Add(bearer);

            bearer.Aura = ArenaAffixAura.Create(foe, bearer.Affixes);
            ArenaTrialsHealthBarName.Refresh(foe.Health);
            bearer.BlinkTimer = UnityEngine.Random.Range(ArenaTrialsTuning.BlinkIntervalMin, ArenaTrialsTuning.BlinkIntervalMax);
            bearer.SummonTimer = ArenaTrialsTuning.SummonInterval;

            if (bearer.Has(ArenaAffix.Twin) && bearer.Preset != null)
            {
                ArenaAffixTwinPair pair = new ArenaAffixTwinPair();
                pair.Bearer = bearer;
                pair.A = foe;
                bearer.Twin = pair;
                pairs.Add(pair);
                Vector3 at = PlaceNear(foe.transform.position, 2.2f, 3.4f);
                ArenaAffixFx.TwinEmerge(at);
                Spawn(bearer, ArenaAffixRole.Twin, at, ArenaTrialsTuning.TwinHealth, 1f, 1f);
            }
            if (bearer.Has(ArenaAffix.Escort) && bearer.Preset != null)
            {
                bearer.GuardUp = true;
                float offset = UnityEngine.Random.Range(0f, 360f);
                int escorts = Mathf.Min(ArenaTrialsTuning.EscortCount, MinionRoom());
                for (int i = 0; i < escorts; i++)
                {
                    float angle = (offset + i * 360f / ArenaTrialsTuning.EscortCount) * Mathf.Deg2Rad;
                    Vector3 raw = foe.transform.position + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 2.6f;
                    Vector3 at = Snap(raw, foe.transform.position);
                    ArenaAffixFx.GuardArrive(at);
                    Spawn(bearer, ArenaAffixRole.Guard, at, ArenaTrialsTuning.MinionHealthShare, ArenaTrialsTuning.MinionScale, 1f);
                }
                ArenaTrialsFx.SoundAt(BossRushInkSkin.SfxTempleBell, foe.transform.position);
                Trials.MarkFoeStatsDirty();
            }
            ModBehaviour.DevLog("[BossRush] [出征令战规] 首领词缀：" + foe.gameObject.name + " = " + bearer.NamePrefix);
        }

        private void Roll(ArenaAffixBearer bearer, int count)
        {
            RollInto(bearer.Affixes, kindScratch, bearer.Preset != null, count, Trials.Profile.AffixDistinctKinds);
        }

        /// <summary>
        /// 抽词缀（纯函数，F3 直接调来核对条数）：有预设的普通 Boss 四类都能抽，内容 Boss 只有身法 / 死相两类。
        /// 要求不同类时先每类取一个；内容 Boss 类别不够（噩梦 3 条、压轴 4 条）就在已抽的类里补上另一个，
        /// 条数始终等于悬赏令写的数，额外掉落也照条数算（CR-2026-11-27-003）。
        /// </summary>
        internal static void RollInto(List<ArenaAffix> output, List<ArenaAffixKind> scratch, bool hasPreset, int count, bool distinctKinds)
        {
            output.Clear();
            scratch.Clear();
            scratch.Add(ArenaAffixKind.Motion);
            scratch.Add(ArenaAffixKind.Death);
            if (hasPreset)
            {
                scratch.Add(ArenaAffixKind.Clone);
                scratch.Add(ArenaAffixKind.Guard);
            }
            count = Mathf.Min(count, scratch.Count * 2);
            for (int i = scratch.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                ArenaAffixKind swap = scratch[i];
                scratch[i] = scratch[j];
                scratch[j] = swap;
            }
            if (distinctKinds)
            {
                int take = Mathf.Min(count, scratch.Count);
                for (int i = 0; i < take; i++)
                {
                    output.Add((ArenaAffix)((int)scratch[i] * 2 + UnityEngine.Random.Range(0, 2)));
                }
                // 类别不够时按打乱后的顺序补同类的另一个（殉爆配亡魂、闪现配狂暴）
                for (int i = 0; i < scratch.Count && output.Count < count; i++)
                {
                    for (int k = 0; k < 2 && output.Count < count; k++)
                    {
                        ArenaAffix sibling = (ArenaAffix)((int)scratch[i] * 2 + k);
                        if (!output.Contains(sibling)) output.Add(sibling);
                    }
                }
            }
            else
            {
                for (int guard = 0; output.Count < count && guard < 32; guard++)
                {
                    ArenaAffixKind kind = scratch[UnityEngine.Random.Range(0, scratch.Count)];
                    ArenaAffix affix = (ArenaAffix)((int)kind * 2 + UnityEngine.Random.Range(0, 2));
                    if (!output.Contains(affix)) output.Add(affix);
                }
            }
            output.Sort();
        }

        private static string BuildPrefix(List<ArenaAffix> affixes)
        {
            string text = string.Empty;
            for (int i = 0; i < affixes.Count; i++)
            {
                if (i > 0) text += "<color=#8C8074>·</color>";
                text += "<color=#" + ColorUtility.ToHtmlStringRGB(ArenaTrialsTuning.AffixColor(affixes[i])) + ">"
                    + ArenaTrialsTuning.AffixName(affixes[i]) + "</color>";
            }
            return text + " ";
        }

        private static string RolePrefix(ArenaAffixRole role)
        {
            string name;
            ArenaAffix tone;
            switch (role)
            {
                case ArenaAffixRole.Shard: name = L10n.T("分身", "Shard"); tone = ArenaAffix.Split; break;
                case ArenaAffixRole.Twin: name = L10n.T("双生", "Twin"); tone = ArenaAffix.Twin; break;
                case ArenaAffixRole.Guard: name = L10n.T("亲卫", "Guard"); tone = ArenaAffix.Escort; break;
                default: name = L10n.T("影从", "Shade"); tone = ArenaAffix.Summoner; break;
            }
            return "<color=#" + ColorUtility.ToHtmlStringRGB(ArenaTrialsTuning.AffixColor(tone)) + ">" + name + "</color> ";
        }

        // ------------------------------------------------------------------
        // 叫身体
        // ------------------------------------------------------------------

        /// <summary>
        /// 发出一次刷怪；返回 false 表示根本没发（预设丢了、战规已收）。share 是占首领生命上限的几成，
        /// 到身体真正落地登记时才按首领当时的上限折算（入场那刻变异词条还没挂上）。
        /// </summary>
        private bool Spawn(ArenaAffixBearer bearer, ArenaAffixRole role, Vector3 at, float share, float scale, float healthFraction)
        {
            if (closed || bearer == null || bearer.Preset == null || bearer.Template == null || Trials.Arena == null) return false;
            SpawnAsync(bearer, role, at, share, scale, healthFraction).Forget();
            return true;
        }

        /// <summary>全场还能再叫几只亲卫 / 影从：活着的、符阵上等着起来的、正在刷的都算。</summary>
        private int MinionRoom()
        {
            int count = portals.Count + pendingMinionSpawns;
            for (int i = 0; i < bodies.Count; i++)
            {
                if (!bodies[i].Dead && !bodies[i].HoldsWave) count++;
            }
            return Mathf.Max(0, ArenaTrialsTuning.AffixMinionCap - count);
        }

        /// <summary>首领的生命上限现读（变异词条在入场通知之后才挂）；本体已被官方销毁、读不到时用上次记下的。</summary>
        private static float MasterMaxHealth(ArenaAffixBearer bearer)
        {
            try
            {
                if (bearer.Body != null && bearer.Body.Health != null)
                {
                    float live = bearer.Body.Health.MaxHealth;
                    if (live > 0f) bearer.MaxHealth = live;
                }
            }
            catch (Exception) { }
            return Mathf.Max(1f, bearer.MaxHealth);
        }

        private async UniTaskVoid SpawnAsync(ArenaAffixBearer bearer, ArenaAffixRole role, Vector3 at, float share, float scale,
            float healthFraction)
        {
            bool holds = role == ArenaAffixRole.Shard || role == ArenaAffixRole.Twin;
            int generation = holdGeneration;
            if (holds) pendingHoldSpawns++;
            else pendingMinionSpawns++;
            ArenaTrials trials = Trials;
            CharacterMainControl body = null;
            try
            {
                body = await trials.Arena.SpawnExpeditionAffixBodyAsync(bearer.Preset, bearer.Template, bearer.Team, at,
                    () => !closed && ArenaTrials.Active == trials);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] [出征令战规] [WARNING] 词缀身体刷出失败: " + e.Message);
            }
            if (!holds) pendingMinionSpawns = Mathf.Max(0, pendingMinionSpawns - 1);
            bool stale = generation != holdGeneration;
            if (holds && !stale) pendingHoldSpawns = Mathf.Max(0, pendingHoldSpawns - 1);
            if (body == null)
            {
                // 拉起刷不出来：这一对当成拆开了，剩下那只打掉就算完
                if (role == ArenaAffixRole.Twin && bearer.Twin != null && bearer.Twin.Reviving) CancelRevive(bearer.Twin);
                return;
            }
            bool masterGone = (role == ArenaAffixRole.Guard || role == ArenaAffixRole.Shade) && !bearer.Alive;
            // 拉起途中另一只也倒了：这一对已经封住，在路上的那只作废
            bool pairSealed = role == ArenaAffixRole.Twin && (bearer.Twin == null || bearer.Twin.Done);
            if (closed || ArenaTrials.Active != trials || stale || masterGone || pairSealed)
            {
                ArenaAffixFx.Disperse(body.transform.position);
                UnityEngine.Object.Destroy(body.gameObject);
                return;
            }
            Register(bearer, role, body, share, scale, healthFraction);
        }

        private void Register(ArenaAffixBearer bearer, ArenaAffixRole role, CharacterMainControl body, float share, float scale,
            float healthFraction)
        {
            ArenaAffixBody record = new ArenaAffixBody();
            record.Body = body;
            record.Role = role;
            record.Master = bearer;
            record.Share = share;
            record.MaxHealth = Mathf.Max(1f, share * MasterMaxHealth(bearer));
            record.LastPosition = body.transform.position;
            record.HealthRecord = SetBodyHealth(body, record.MaxHealth, healthFraction);
            if (Mathf.Abs(scale - 1f) > 0.001f) body.transform.localScale = body.transform.localScale * scale;
            record.Veil = ArenaAffixVeil.Create(body, role);
            bodies.Add(record);
            Trials.AddFoe(body, false);
            ArenaTrialsHealthBarName.Refresh(body.Health);

            switch (role)
            {
                case ArenaAffixRole.Twin:
                    JoinPair(bearer.Twin, body);
                    break;
                case ArenaAffixRole.Guard:
                    bearer.Guards.Add(body);
                    if (bearer.Aura != null) bearer.Aura.SetGuards(bearer.Guards);
                    Trials.MarkFoeStatsDirty();
                    break;
                case ArenaAffixRole.Shade:
                    bearer.Shades.Add(body);
                    break;
            }
        }

        /// <summary>按目标上限改身体生命并按比例给血；返回挂上的那条修饰（没挂返回 null）。</summary>
        private BossRushStatModifierRecord SetBodyHealth(CharacterMainControl body, float maxHealth, float fraction)
        {
            BossRushStatModifierRecord added = null;
            try
            {
                Health health = body.Health;
                if (health == null) return null;
                float current = health.MaxHealth;
                if (current > 0f && Mathf.Abs(maxHealth / current - 1f) > 0.001f)
                {
                    int before = bodyRecords.Count;
                    if (RuntimeStatModifierTracker.TryAdd(body, ZombieModeStatNames.MaxHealth, maxHealth / current - 1f, this, bodyRecords,
                            "ArenaTrials affix body", ModifierType.PercentageMultiply) && bodyRecords.Count > before)
                    {
                        added = bodyRecords[bodyRecords.Count - 1];
                    }
                }
                health.SetHealth(Mathf.Max(1f, health.MaxHealth * Mathf.Clamp01(fraction)));
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] [出征令战规] [WARNING] 词缀身体生命设置失败: " + e.Message);
            }
            return added;
        }

        /// <summary>
        /// 身体移出列表（死了、散了）：它那条生命修饰的记录一并丢掉。死去的身体随官方延时销毁，修饰跟着角色 Item 走，
        /// 不必再摘；只是不让无间长局里的记录表攥着一堆已销毁角色的 Stat。
        /// </summary>
        private void ForgetBodyAt(int index)
        {
            ArenaAffixBody body = bodies[index];
            bodies.RemoveAt(index);
            if (body.HealthRecord == null) return;
            bodyRecords.Remove(body.HealthRecord);
            body.HealthRecord = null;
        }

        private void Dissolve(ArenaAffixBody body)
        {
            if (body == null) return;
            if (body.Veil != null) body.Veil.Retire(0.6f);
            body.Veil = null;
            if (body.Dead || body.Body == null) return;
            body.Dead = true;
            try
            {
                ArenaAffixFx.Disperse(body.Body.transform.position);
                UnityEngine.Object.Destroy(body.Body.gameObject);
            }
            catch (Exception) { }
        }

        // ------------------------------------------------------------------
        // 死亡结算
        // ------------------------------------------------------------------

        internal override void OnAnyDeath(Health health, DamageInfo info)
        {
            if (closed || health == null) return;
            CharacterMainControl character = health.TryGetCharacter();
            if (character == null) return;
            ArenaAffixBearer bearer = FindBearer(character);
            if (bearer != null)
            {
                HandleBearerDeath(bearer);
                return;
            }
            ArenaAffixBody body = FindBody(character);
            if (body != null && !body.Dead) HandleBodyDeath(body);
        }

        /// <summary>本体倒下（幂等）：散掉亲卫影从，分裂叫两只分身，双生开倒计时，亡魂升起，殉爆点火。</summary>
        private void HandleBearerDeath(ArenaAffixBearer bearer)
        {
            if (bearer.DeathHandled) return;
            bearer.DeathHandled = true;
            Vector3 at = bearer.Body != null ? bearer.Body.transform.position : bearer.LastPosition;
            bearer.LastPosition = at;
            // 倒下那刻再读一次上限：分身要按挂完变异词条的值折，双生隔 10 秒拉起时本体可能已被官方销毁
            MasterMaxHealth(bearer);
            if (bearer.Aura != null)
            {
                bearer.Aura.Retire(0.6f);
                bearer.Aura = null;
            }
            DisperseRetinue(bearer);

            if (bearer.Has(ArenaAffix.Split) && bearer.Preset != null)
            {
                ArenaAffixFx.SplitBurst(at);
                Vector3 side = Vector3.Cross(Vector3.up, FlatDirection(at)).normalized;
                if (side.sqrMagnitude < 0.01f) side = Vector3.right;
                for (int i = 0; i < 2; i++)
                {
                    Vector3 spot = Snap(at + side * (i == 0 ? 1.3f : -1.3f), at);
                    Spawn(bearer, ArenaAffixRole.Shard, spot, ArenaTrialsTuning.SplitHealth, ArenaTrialsTuning.SplitScale, 1f);
                }
            }
            if (bearer.Twin != null) MemberFell(bearer.Twin, bearer.Body, at, 1f);
            if (bearer.Has(ArenaAffix.Wraith)) aftermaths.Add(new ArenaAffixWraith(Trials, at));
            if (bearer.Has(ArenaAffix.Volatile)) aftermaths.Add(new ArenaAffixVolatile(Trials, at));
        }

        private void HandleBodyDeath(ArenaAffixBody body)
        {
            body.Dead = true;
            Vector3 at = body.Body != null ? body.Body.transform.position : body.LastPosition;
            if (body.Veil != null)
            {
                body.Veil.Retire(0.5f);
                body.Veil = null;
            }
            switch (body.Role)
            {
                case ArenaAffixRole.Shard:
                    ArenaAffixFx.ShardFall(at);
                    break;
                case ArenaAffixRole.Twin:
                    if (body.Master != null && body.Master.Twin != null) MemberFell(body.Master.Twin, body.Body, at, body.Share);
                    break;
                case ArenaAffixRole.Guard:
                    if (body.Master != null)
                    {
                        body.Master.Guards.Remove(body.Body);
                        PruneDead(body.Master.Guards);
                        if (body.Master.Aura != null) body.Master.Aura.SetGuards(body.Master.Guards);
                        if (body.Master.GuardUp && body.Master.Guards.Count == 0)
                        {
                            body.Master.GuardUp = false;
                            if (body.Master.Alive) ArenaAffixFx.GuardShatter(body.Master.Body);
                        }
                    }
                    Trials.MarkFoeStatsDirty();
                    break;
                case ArenaAffixRole.Shade:
                    if (body.Master != null) body.Master.Shades.Remove(body.Body);
                    break;
            }
        }

        /// <summary>主将倒下：亲卫、影从与还没起来的召唤符阵一起散。</summary>
        private void DisperseRetinue(ArenaAffixBearer bearer)
        {
            for (int i = bodies.Count - 1; i >= 0; i--)
            {
                ArenaAffixBody body = bodies[i];
                if (body.Master != bearer || body.HoldsWave) continue;
                Dissolve(body);
                ForgetBodyAt(i);
            }
            bearer.Guards.Clear();
            bearer.Shades.Clear();
            bearer.GuardUp = false;
            for (int i = portals.Count - 1; i >= 0; i--)
            {
                if (portals[i].Bearer != bearer) continue;
                if (portals[i].Omen != null) portals[i].Omen.Retire(0.4f);
                portals.RemoveAt(i);
            }
            Trials.MarkFoeStatsDirty();
        }

        // ------------------------------------------------------------------
        // 双生
        // ------------------------------------------------------------------

        private void JoinPair(ArenaAffixTwinPair pair, CharacterMainControl body)
        {
            if (pair == null || pair.Done) return;
            if (pair.Reviving)
            {
                // 拉起来的那只顶回先倒下的位置
                if (pair.FallenIsA) pair.A = body;
                else pair.B = body;
                pair.Reviving = false;
                pair.Countdown = -1f;
                if (pair.Omen != null)
                {
                    pair.Omen.Retire(0.3f);
                    pair.Omen = null;
                }
                ArenaAffixFx.TwinRevive(body.transform.position);
            }
            else if (pair.B == null || IsDeadOrGone(pair.B))
            {
                pair.B = body;
                // 本体在孪生赶到前就倒了：从现在起算倒计时
                if (IsDeadOrGone(pair.A) && pair.Countdown < 0f && pair.Bearer != null)
                {
                    StartCountdown(pair, true, pair.Bearer.LastPosition, 1f);
                }
            }
            if (pair.Tether == null) pair.Tether = ArenaAffixTether.Create("BossRush_AffixTwinTether", ArenaTrialsTuning.AffixColor(ArenaAffix.Twin));
        }

        private void MemberFell(ArenaAffixTwinPair pair, CharacterMainControl who, Vector3 at, float share)
        {
            if (pair == null || pair.Done) return;
            bool isA = who == pair.A;
            CharacterMainControl other = isA ? pair.B : pair.A;
            if (IsDeadOrGone(other))
            {
                // 两只都倒了：封住这一对，正在拉起的那只落地时在 SpawnAsync 里作废。
                // 只有孪生还没赶到（other 为空、刷怪在路上）才等它，JoinPair 里接着算倒计时
                if (pair.Countdown >= 0f && pair.Omen != null && other != null)
                {
                    ArenaAffixFx.TwinSealed(pair.FallenAt);
                }
                if (pendingHoldSpawns > 0 && other == null) return;
                EndPair(pair);
                return;
            }
            if (pair.Countdown >= 0f || pair.Reviving) return;
            if (pair.Revives >= ArenaTrialsTuning.TwinMaxRevives) return;
            StartCountdown(pair, isA, at, share);
        }

        private void CancelRevive(ArenaAffixTwinPair pair)
        {
            pair.Reviving = false;
            pair.Countdown = -1f;
            if (pair.Omen != null)
            {
                pair.Omen.Retire(0.4f);
                pair.Omen = null;
            }
        }

        private void StartCountdown(ArenaAffixTwinPair pair, bool fallenIsA, Vector3 at, float share)
        {
            pair.Countdown = ArenaTrialsTuning.TwinReviveSeconds;
            pair.FallenIsA = fallenIsA;
            pair.FallenAt = at;
            pair.FallenShare = Mathf.Max(0.01f, share);
            if (pair.Omen != null) pair.Omen.Retire(0.2f);
            pair.Omen = ArenaAffixOmen.Create("BossRush_AffixTwinOmen", at, 1.25f, ArenaTrialsTuning.AffixColor(ArenaAffix.Twin), true);
            ArenaTrialsFx.SoundAt(BossRushInkSkin.SfxBellHum, at);
        }

        private void TickPairs(float dt)
        {
            for (int i = pairs.Count - 1; i >= 0; i--)
            {
                ArenaAffixTwinPair pair = pairs[i];
                if (pair.Done)
                {
                    pairs.RemoveAt(i);
                    continue;
                }
                bool aliveA = !IsDeadOrGone(pair.A);
                bool aliveB = !IsDeadOrGone(pair.B);
                if (pair.Tether != null)
                {
                    if (aliveA && aliveB) pair.Tether.Show(pair.A.transform.position, pair.B.transform.position, dt);
                    else pair.Tether.Hide(dt);
                }
                if (pair.Countdown < 0f || pair.Reviving) continue;
                CharacterMainControl survivor = pair.FallenIsA ? pair.B : pair.A;
                if (IsDeadOrGone(survivor))
                {
                    ArenaAffixFx.TwinSealed(pair.FallenAt);
                    EndPair(pair);
                    pairs.RemoveAt(i);
                    continue;
                }
                pair.Countdown -= dt;
                if (pair.Omen != null) pair.Omen.SetProgress(1f - pair.Countdown / ArenaTrialsTuning.TwinReviveSeconds);
                if (pair.Countdown > 0f) continue;
                // 时限到了另一只还站着：先倒下的那只被拉起（半血）
                pair.Countdown = 0f;
                pair.Revives++;
                pair.Reviving = true;
                if (pair.Omen != null) pair.Omen.SetProgress(1f);
                bool started = pair.Bearer != null && Spawn(pair.Bearer, ArenaAffixRole.Twin, pair.FallenAt, pair.FallenShare, 1f,
                    ArenaTrialsTuning.TwinReviveHealth);
                if (!started) CancelRevive(pair);
            }
        }

        private void EndPair(ArenaAffixTwinPair pair)
        {
            if (pair == null) return;
            pair.Done = true;
            pair.Countdown = -1f;
            pair.Reviving = false;
            if (pair.Omen != null)
            {
                pair.Omen.Retire(0.5f);
                pair.Omen = null;
            }
            if (pair.Tether != null)
            {
                pair.Tether.Destroy();
                pair.Tether = null;
            }
        }

        // ------------------------------------------------------------------
        // 每帧
        // ------------------------------------------------------------------

        internal override void Tick(float deltaTime)
        {
            if (bearers.Count == 0 && bodies.Count == 0 && aftermaths.Count == 0 && !waveHeld) return;
            bool live = Trials.WaveLive;

            for (int i = bearers.Count - 1; i >= 0; i--)
            {
                ArenaAffixBearer bearer = bearers[i];
                if (bearer.DeathHandled) continue;
                if (!bearer.Alive)
                {
                    // OnDead 可能被别的订阅者异常截断：这里兜一次
                    HandleBearerDeath(bearer);
                    continue;
                }
                bearer.LastPosition = bearer.Body.transform.position;
                if (bearer.Aura != null) bearer.Aura.Follow(bearer.Body, bearer.GuardUp, bearer.Frenzied, deltaTime);
                if (live) TickBearer(bearer, deltaTime);
            }

            for (int i = bodies.Count - 1; i >= 0; i--)
            {
                ArenaAffixBody body = bodies[i];
                if (body.Dead)
                {
                    ForgetBodyAt(i);
                    continue;
                }
                if (body.Body == null || body.Body.Health == null || body.Body.Health.IsDead)
                {
                    HandleBodyDeath(body);
                    ForgetBodyAt(i);
                    continue;
                }
                body.LastPosition = body.Body.transform.position;
                if (body.Veil != null) body.Veil.Follow(body.Body, deltaTime);
            }

            if (pairs.Count > 0) TickPairs(deltaTime);
            if (portals.Count > 0) TickPortals(deltaTime);

            for (int i = aftermaths.Count - 1; i >= 0; i--)
            {
                if (!aftermaths[i].Step(deltaTime)) aftermaths.RemoveAt(i);
            }

            if (waveHeld)
            {
                holdClock += deltaTime;
                if (!HoldActive())
                {
                    waveHeld = false;
                    releasePending = true;
                }
                else if (holdClock >= ArenaTrialsTuning.AffixHoldMaxSeconds)
                {
                    ModBehaviour.DevLog("[BossRush] [出征令战规] [WARNING] 分身 / 双生压收波超时，散掉剩下的身体照常收波");
                    for (int i = bodies.Count - 1; i >= 0; i--)
                    {
                        if (bodies[i].HoldsWave) Dissolve(bodies[i]);
                    }
                    for (int i = 0; i < pairs.Count; i++) EndPair(pairs[i]);
                    pendingHoldSpawns = 0;
                    holdGeneration++;
                    waveHeld = false;
                    releasePending = true;
                }
            }
        }

        private void TickBearer(ArenaAffixBearer bearer, float dt)
        {
            CharacterMainControl body = bearer.Body;
            if (bearer.Has(ArenaAffix.Frenzy)) TickFrenzy(bearer, dt);
            if (bearer.Has(ArenaAffix.Blink)) TickBlink(bearer, dt);
            if (bearer.Has(ArenaAffix.Summoner) && bearer.Preset != null)
            {
                bearer.SummonTimer -= dt;
                if (bearer.SummonTimer <= 0f)
                {
                    bearer.SummonTimer = ArenaTrialsTuning.SummonInterval;
                    PruneDead(bearer.Shades);
                    int pendingForBearer = 0;
                    for (int i = 0; i < portals.Count; i++) if (portals[i].Bearer == bearer) pendingForBearer++;
                    int room = Mathf.Min(ArenaTrialsTuning.SummonCap - bearer.Shades.Count - pendingForBearer, MinionRoom());
                    int batch = Mathf.Min(ArenaTrialsTuning.SummonBatch, room);
                    for (int i = 0; i < batch; i++)
                    {
                        ArenaAffixPortal portal = new ArenaAffixPortal();
                        portal.Bearer = bearer;
                        portal.At = PlaceNear(body.transform.position, 2.5f, 4.5f);
                        portal.Remaining = SummonWindup;
                        portal.Omen = ArenaAffixOmen.Create("BossRush_AffixSummonGate", portal.At, 0.95f,
                            ArenaTrialsTuning.AffixColor(ArenaAffix.Summoner), false);
                        portals.Add(portal);
                    }
                    if (batch > 0) ArenaTrialsFx.SoundAt(BossRushInkSkin.SfxSoulRise, body.transform.position);
                }
            }
        }

        private void TickPortals(float dt)
        {
            for (int i = portals.Count - 1; i >= 0; i--)
            {
                ArenaAffixPortal portal = portals[i];
                portal.Remaining -= dt;
                if (portal.Omen != null) portal.Omen.SetProgress(1f - portal.Remaining / SummonWindup);
                if (portal.Remaining > 0f) continue;
                portals.RemoveAt(i);
                if (portal.Omen != null) portal.Omen.Retire(0.5f);
                if (portal.Bearer == null || !portal.Bearer.Alive || !Trials.WaveLive) continue;
                ArenaAffixFx.ShadeRise(portal.At);
                Spawn(portal.Bearer, ArenaAffixRole.Shade, portal.At, ArenaTrialsTuning.MinionHealthShare, ArenaTrialsTuning.MinionScale, 1f);
            }
        }

        private void TickFrenzy(ArenaAffixBearer bearer, float dt)
        {
            Health health = bearer.Body.Health;
            if (!bearer.Frenzied)
            {
                float max = health.MaxHealth;
                if (max <= 0f || health.CurrentHealth > max * ArenaTrialsTuning.FrenzyThreshold) return;
                bearer.Frenzied = true;
                bearer.FrenzyGrow = 0f;
                // 以此刻的体型为底长大（巨灵兵、群鼠的缩放在入场通知之后才挂，登记时记下的不算数）
                bearer.BaseScale = bearer.Body.transform.localScale;
                ArenaAffixFx.FrenzyFlare(bearer.Body);
                Trials.MarkFoeStatsDirty();
            }
            if (bearer.FrenzyGrow < 0f) return;
            // 0.6 秒长到 1.3 倍（两头缓），不是一帧蹦大
            bearer.FrenzyGrow = Mathf.Min(1f, bearer.FrenzyGrow + dt / 0.6f);
            float k = Mathf.Lerp(1f, ArenaTrialsTuning.FrenzyScale, BossRushUI.SmoothStep(bearer.FrenzyGrow));
            bearer.Body.transform.localScale = bearer.BaseScale * k;
            if (bearer.FrenzyGrow >= 1f) bearer.FrenzyGrow = -1f;
        }

        private void TickBlink(ArenaAffixBearer bearer, float dt)
        {
            CharacterMainControl body = bearer.Body;
            if (bearer.BlinkWindup >= 0f)
            {
                bearer.BlinkWindup -= dt;
                if (bearer.BlinkWindup > 0f) return;
                bearer.BlinkWindup = -1f;
                Vector3 target;
                if (TryBlinkTarget(body, out target))
                {
                    Vector3 from = body.transform.position;
                    try { body.SetPosition(target); }
                    catch (Exception) { body.transform.position = target; }
                    ArenaAffixFx.BlinkArrive(from, target);
                }
                bearer.BlinkTimer = UnityEngine.Random.Range(ArenaTrialsTuning.BlinkIntervalMin, ArenaTrialsTuning.BlinkIntervalMax);
                return;
            }
            bearer.BlinkTimer -= dt;
            if (bearer.BlinkTimer > 0f) return;
            Vector3 probe;
            if (!TryBlinkTarget(body, out probe))
            {
                bearer.BlinkTimer = 1.5f;
                return;
            }
            bearer.BlinkWindup = ArenaTrialsTuning.BlinkWindup;
            ArenaAffixFx.BlinkGather(body.transform.position);
        }

        /// <summary>玩家身后（相对首领来的方向）BlinkBehind 米、贴 NavMesh、与玩家同层；落不下就不闪。</summary>
        private static bool TryBlinkTarget(CharacterMainControl body, out Vector3 target)
        {
            target = Vector3.zero;
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null || player.Health == null || player.Health.IsDead || body == null) return false;
            Vector3 p = player.transform.position;
            Vector3 away = p - body.transform.position;
            away.y = 0f;
            float distance = away.magnitude;
            if (distance < BlinkMinDistance || distance > BlinkMaxDistance) return false;
            Vector3 dir = away / Mathf.Max(0.01f, distance);
            for (int attempt = 0; attempt < 3; attempt++)
            {
                Vector3 side = attempt == 0 ? Vector3.zero : Vector3.Cross(Vector3.up, dir) * (attempt == 1 ? 1.4f : -1.4f);
                Vector3 raw = p + dir * ArenaTrialsTuning.BlinkBehind + side;
                Vector3 resolved;
                if (SpawnPositionHelper.TrySampleNavMesh(raw, out resolved, 0f, 1.5f) && Mathf.Abs(resolved.y - p.y) < 2.5f
                    && ArenaTrials.FlatSqr(resolved, p) > 1.2f)
                {
                    target = resolved;
                    return true;
                }
            }
            return false;
        }

        // ------------------------------------------------------------------
        // 属性
        // ------------------------------------------------------------------

        internal override void ContributeFoe(CharacterMainControl foe, ref ArenaTrialFoeStats stats)
        {
            ArenaAffixBearer bearer = FindBearer(foe);
            if (bearer != null && !bearer.DeathHandled)
            {
                if (bearer.GuardUp && CountAlive(bearer.Guards) > 0) stats.Taken *= 1f - ArenaTrialsTuning.EscortDamageReduction;
                if (bearer.Frenzied)
                {
                    stats.Move *= 1f + ArenaTrialsTuning.FrenzyBonus;
                    stats.Damage *= 1f + ArenaTrialsTuning.FrenzyDamageBonus;
                }
                return;
            }
            ArenaAffixBody body = FindBody(foe);
            if (body != null && (body.Role == ArenaAffixRole.Guard || body.Role == ArenaAffixRole.Shade))
            {
                stats.Damage *= ArenaTrialsTuning.MinionDamage;
            }
        }

        // ------------------------------------------------------------------
        // 收尾
        // ------------------------------------------------------------------

        internal override void Shutdown()
        {
            closed = true;
            for (int i = bodies.Count - 1; i >= 0; i--) Dissolve(bodies[i]);
            bodies.Clear();
            for (int i = 0; i < pairs.Count; i++) EndPair(pairs[i]);
            pairs.Clear();
            for (int i = 0; i < portals.Count; i++)
            {
                if (portals[i].Omen != null) portals[i].Omen.Retire(0.4f);
            }
            portals.Clear();
            for (int i = 0; i < bearers.Count; i++)
            {
                ArenaAffixBearer bearer = bearers[i];
                if (bearer.Aura != null) bearer.Aura.Retire(0.5f);
                bearer.Aura = null;
                // 狂暴长大的体型还原（本体若还活着，例如竞技场被中途结束）
                if (bearer.Frenzied && bearer.Body != null) bearer.Body.transform.localScale = bearer.BaseScale;
            }
            bearers.Clear();
            for (int i = 0; i < aftermaths.Count; i++) aftermaths[i].Retire();
            aftermaths.Clear();
            RuntimeStatModifierTracker.RemoveAll(bodyRecords, "ArenaTrials affix body");
            pendingHoldSpawns = 0;
            waveHeld = false;
            releasePending = false;
            pendingSourceBody = null;
            pendingSource = null;
        }

        // ------------------------------------------------------------------
        // 小工具
        // ------------------------------------------------------------------

        private ArenaAffixBearer FindBearer(CharacterMainControl character)
        {
            if (character == null) return null;
            for (int i = 0; i < bearers.Count; i++)
            {
                if (bearers[i].Body == character) return bearers[i];
            }
            return null;
        }

        private ArenaAffixBody FindBody(CharacterMainControl character)
        {
            if (character == null) return null;
            for (int i = 0; i < bodies.Count; i++)
            {
                if (bodies[i].Body == character) return bodies[i];
            }
            return null;
        }

        private static bool IsDeadOrGone(CharacterMainControl character)
        {
            return character == null || character.Health == null || character.Health.IsDead;
        }

        private static int CountAlive(List<CharacterMainControl> list)
        {
            int alive = 0;
            for (int i = 0; i < list.Count; i++)
            {
                if (!IsDeadOrGone(list[i])) alive++;
            }
            return alive;
        }

        private static void PruneDead(List<CharacterMainControl> list)
        {
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (IsDeadOrGone(list[i])) list.RemoveAt(i);
            }
        }

        /// <summary>离 center 一段距离的随机落点，贴 NavMesh、与 center 同层；落不下退回 center。</summary>
        private static Vector3 PlaceNear(Vector3 center, float min, float max)
        {
            for (int attempt = 0; attempt < 6; attempt++)
            {
                float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                float radius = UnityEngine.Random.Range(min, max);
                Vector3 raw = center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                Vector3 resolved;
                if (SpawnPositionHelper.TrySampleNavMesh(raw, out resolved, 0f, 1.5f) && Mathf.Abs(resolved.y - center.y) < 2.5f)
                {
                    return resolved;
                }
            }
            return center;
        }

        private static Vector3 Snap(Vector3 raw, Vector3 fallback)
        {
            Vector3 resolved;
            if (SpawnPositionHelper.TrySampleNavMesh(raw, out resolved, 0f, 1.5f) && Mathf.Abs(resolved.y - fallback.y) < 2.5f)
            {
                return resolved;
            }
            return fallback;
        }

        private static Vector3 FlatDirection(Vector3 at)
        {
            CharacterMainControl player = CharacterMainControl.Main;
            Vector3 dir = player != null ? at - player.transform.position : Vector3.forward;
            dir.y = 0f;
            return dir.sqrMagnitude > 0.01f ? dir.normalized : Vector3.forward;
        }
    }
}
