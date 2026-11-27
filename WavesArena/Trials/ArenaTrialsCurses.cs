// ============================================================================
// ArenaTrialsCurses.cs - 出征令的厄运（每波一个诅咒，下一波换）与噩梦三条常驻诅咒（失明、断补、血债）
// ============================================================================
// 厄运池（困难轻、噩梦重，数值见 ArenaTrialsTuning）：
//   手滑（换弹慢）· 沉重（体力恢复慢）· 手抖（散射大）· 怕痛（被敌人打中后短暂减速）· 近视（视距短）·
//   回响（被打中的地方隔一会儿再震，翻滚可躲）· 沉疴（挨打后伤口流血几秒，翻滚止住）· 破绽（受到伤害增加）。
//   后三道 2026-11-27 加入，挨打后的结算与表现在 ArenaTrialsCurseActs.cs。
// 失明：迷雾收窄；首领血条量化成几格、不显示单次掉血条（HealthBar 补丁读 BlindStepsFor）；M 图不标首领
//   （沿用 ArenaExpeditionTuning.HidesMapBeacons，烽烟圈来源入口挡掉）。
// 断补：用药读条 ×2、开波后不定时禁药 8 秒（CA_UseItem 补丁读 UseItemSealed / UseTimeScale），
//   首领掉落里的弹药堆叠减半（竞技场掉落入口 ExpeditionAfterBossLoot 调 HalveAmmo）。
// 血债：只能靠击杀回血（Health.AddHealth 补丁读 HealBlocked；击杀回血自己开闸）；
//   每被首领打中一次生命上限压低 3%，封顶 30%，这一波打完恢复。
// 全部只作用于主玩家；都随 ArenaTrials 摘除。
// ============================================================================

using System;
using System.Collections.Generic;
using Duckov.Utilities;
using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    internal sealed class ArenaTrialsCurses : ArenaTrialPart
    {
        private ArenaTrialCurse curse = ArenaTrialCurse.None;
        private float flinchLeft;
        private float banLeft;
        private float nextBanIn;
        private int bannedSecondsShown = -1;
        private int bloodDebtHits;
        private bool allowHeal;
        /// <summary>血债：每只敌人上次记账的时刻（实例 ID → Time.time），收波清空。</summary>
        private readonly Dictionary<int, float> debtLastHit = new Dictionary<int, float>(8);
        /// <summary>回响 / 沉疴 / 破绽挨打之后的结算与表现。</summary>
        private readonly ArenaTrialsCurseActs acts = new ArenaTrialsCurseActs();

        internal ArenaTrialsCurses(ArenaTrials trials) : base(trials) { }

        internal ArenaTrialCurse Current { get { return curse; } }
        private int Level { get { return Trials.Profile.CurseLevel; } }

        internal override void OnBegin()
        {
            nextBanIn = NextBanDelay();
            RefreshHud(false);
        }

        internal override void OnWaveStart(int wave, bool finale)
        {
            if (Level > 0)
            {
                curse = PickCurse(curse);
                flinchLeft = 0f;
                acts.Clear();
                ModBehaviour.DevLog("[BossRush] [出征令战规] 第 " + (wave + 1) + " 波厄运：" + curse);
            }
            RefreshHud(false);
        }

        internal override void OnWaveEnd()
        {
            debtLastHit.Clear();
            acts.Clear();
            if (bloodDebtHits > 0)
            {
                bloodDebtHits = 0;
                Trials.MarkPlayerStatsDirty();
            }
        }

        /// <summary>角标：天象一行（Sky 读）+ 诅咒一行。</summary>
        internal void RefreshHud(bool animateOmen)
        {
            string omen = ArenaTrialsTuning.OmenName(Trials.Sky != null ? Trials.Sky.Omen : ArenaTrialOmen.None);
            string line = string.Empty;
            if (curse != ArenaTrialCurse.None)
            {
                line = (Level >= 2 ? L10n.T("重厄 · ", "Dire Hex · ") : L10n.T("厄运 · ", "Hex · ")) + ArenaTrialsTuning.CurseName(curse);
            }
            ArenaExpeditionHud.SetTrialLines(omen, line, animateOmen);
        }

        internal override void Tick(float deltaTime)
        {
            acts.Tick(deltaTime);
            if (flinchLeft > 0f)
            {
                flinchLeft -= deltaTime;
                if (flinchLeft <= 0f) Trials.MarkPlayerStatsDirty();
            }

            if (!Trials.Profile.CutSupply) return;
            if (banLeft > 0f)
            {
                banLeft -= deltaTime;
                int seconds = Mathf.CeilToInt(Mathf.Max(0f, banLeft));
                if (seconds != bannedSecondsShown)
                {
                    bannedSecondsShown = seconds;
                    ArenaExpeditionHud.SetSealedLine(seconds > 0 ? L10n.T("禁用道具 ", "Sealed ") + seconds + "s" : string.Empty);
                }
                return;
            }
            if (!Trials.WaveLive) return;
            nextBanIn -= deltaTime;
            if (nextBanIn > 0f) return;
            banLeft = ArenaTrialsTuning.CutSupplyBanSeconds;
            nextBanIn = NextBanDelay();
            bannedSecondsShown = -1;
            BossRushInkSkin.PlaySfx(BossRushInkSkin.SfxSealStamp);
        }

        internal override void OnAnyHurt(Health health, DamageInfo info)
        {
            if (health == null || !health.IsMainCharacterHealth || info.damageValue <= 0f) return;
            CharacterMainControl from = info.fromCharacter;
            if (from == null || from.IsMainCharacter) return;
            if (!Team.IsEnemy(Teams.player, from.Team)) return;

            if (curse == ArenaTrialCurse.Flinch)
            {
                bool wasFlinching = flinchLeft > 0f;
                flinchLeft = ArenaTrialsTuning.FlinchSeconds(Level);
                if (!wasFlinching) Trials.MarkPlayerStatsDirty();
            }
            if (curse == ArenaTrialCurse.Echo || curse == ArenaTrialCurse.Festering || curse == ArenaTrialCurse.Exposed)
            {
                CharacterMainControl self = health.TryGetCharacter();
                Vector3 at = self != null ? self.transform.position : health.transform.position;
                if (curse == ArenaTrialCurse.Echo) acts.Echo(at, Level);
                else if (curse == ArenaTrialCurse.Festering) acts.Fester(Level);
                else acts.Crack(at);
            }
            if (Trials.Profile.BloodDebt && Trials.IsFoe(from) && !Trials.Affixes.IsMinion(from) && CountsAsDebtHit(from))
            {
                int cap = Mathf.RoundToInt(ArenaTrialsTuning.BloodDebtMaxHealthCap / ArenaTrialsTuning.BloodDebtMaxHealthPerHit);
                if (bloodDebtHits < cap)
                {
                    bloodDebtHits++;
                    Trials.MarkPlayerStatsDirty();
                }
            }
        }

        /// <summary>同一只敌人 BloodDebtHitWindow 秒内只记一次（2026-11-27 改进方案：原先霰弹每颗弹丸各算一次）。</summary>
        private bool CountsAsDebtHit(CharacterMainControl from)
        {
            int id = from.GetInstanceID();
            float now = Time.time;
            float last;
            if (debtLastHit.TryGetValue(id, out last) && now - last < ArenaTrialsTuning.BloodDebtHitWindow) return false;
            debtLastHit[id] = now;
            return true;
        }

        internal override void OnAnyDeath(Health health, DamageInfo info)
        {
            if (!Trials.Profile.BloodDebt || health == null || health.IsMainCharacterHealth) return;
            CharacterMainControl killer = info.fromCharacter;
            if (killer == null || !killer.IsMainCharacter) return;
            CharacterMainControl dead = health.TryGetCharacter();
            if (dead == null || !Team.IsEnemy(Teams.player, dead.Team)) return;
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null || player.Health == null || player.Health.IsDead) return;
            bool boss = Trials.IsWaveBoss(dead) || Trials.Affixes.IsCloneOfBoss(dead);
            float fraction = boss ? ArenaTrialsTuning.BloodDebtBossKillHeal : ArenaTrialsTuning.BloodDebtKillHeal;
            allowHeal = true;
            try
            {
                // 直接走 Health.AddHealth：不乘玩家的 HealGain，击杀回多少就是多少
                player.Health.AddHealth(player.Health.MaxHealth * fraction);
            }
            catch (Exception) { }
            finally
            {
                allowHeal = false;
            }
        }

        internal override void ContributePlayer(ref ArenaTrialPlayerStats stats)
        {
            int level = Level;
            switch (curse)
            {
                case ArenaTrialCurse.Butterfingers:
                    stats.ReloadGain += ArenaTrialsTuning.ButterfingersReload(level);
                    break;
                case ArenaTrialCurse.Leaden:
                    stats.Stamina *= 1f + ArenaTrialsTuning.LeadenStamina(level);
                    break;
                case ArenaTrialCurse.Jitters:
                    stats.Scatter *= 1f + ArenaTrialsTuning.JittersScatter(level);
                    break;
                case ArenaTrialCurse.Flinch:
                    if (flinchLeft > 0f) stats.Move *= 1f + ArenaTrialsTuning.FlinchSlow(level);
                    break;
                case ArenaTrialCurse.Nearsighted:
                    stats.ViewDistance *= ArenaTrialsTuning.NearsightedView(level);
                    break;
                case ArenaTrialCurse.Exposed:
                    stats.Taken *= 1f + ArenaTrialsTuning.ExposedTaken(level);
                    break;
            }
            if (Trials.Profile.Blind)
            {
                stats.ViewDistance *= ArenaTrialsTuning.BlindViewDistance;
                stats.ViewAngle *= ArenaTrialsTuning.BlindViewAngle;
            }
            if (Trials.Profile.BloodDebt && bloodDebtHits > 0)
            {
                stats.MaxHealth *= 1f - Mathf.Min(ArenaTrialsTuning.BloodDebtMaxHealthCap,
                    bloodDebtHits * ArenaTrialsTuning.BloodDebtMaxHealthPerHit);
            }
        }

        internal override void Shutdown()
        {
            curse = ArenaTrialCurse.None;
            banLeft = 0f;
            flinchLeft = 0f;
            bloodDebtHits = 0;
            debtLastHit.Clear();
            acts.Clear();
            allowHeal = false;
            ArenaExpeditionHud.SetSealedLine(string.Empty);
        }

        // ------------------------------------------------------------------
        // 补丁与掉落入口读的东西
        // ------------------------------------------------------------------

        internal bool Sealed { get { return Trials.Profile.CutSupply && banLeft > 0f; } }

        internal bool HealBlockedFor(Health health)
        {
            return Trials.Profile.BloodDebt && !allowHeal && health != null && health.IsMainCharacterHealth;
        }

        /// <summary>首领掉落里的弹药堆叠减半（至少留 1 发，不把整堆删掉）。</summary>
        internal void HalveAmmo(CharacterMainControl boss)
        {
            if (!Trials.Profile.CutSupply || boss == null || boss.CharacterItem == null) return;
            Inventory inventory = boss.CharacterItem.Inventory;
            if (inventory == null) return;
            int halved = 0;
            foreach (Item item in inventory)
            {
                if (item == null || !item.Stackable || item.StackCount <= 1) continue;
                if (item.Tags == null || !item.Tags.Contains(GameplayDataSettings.Tags.Bullet)) continue;
                item.StackCount = Mathf.Max(1, Mathf.FloorToInt(item.StackCount * ArenaTrialsTuning.CutSupplyAmmoDropFactor));
                halved++;
            }
            if (halved > 0) ModBehaviour.DevLog("[BossRush] [出征令战规] 断补：" + boss.name + " 掉落弹药减半 " + halved + " 堆");
        }

        private ArenaTrialCurse PickCurse(ArenaTrialCurse previous)
        {
            int kinds = ArenaTrialsTuning.CurseKinds;
            int pick = UnityEngine.Random.Range(1, kinds + 1);
            if ((ArenaTrialCurse)pick == previous) pick = pick % kinds + 1;
            return (ArenaTrialCurse)pick;
        }

        private static float NextBanDelay()
        {
            return UnityEngine.Random.Range(ArenaTrialsTuning.CutSupplyBanIntervalMin, ArenaTrialsTuning.CutSupplyBanIntervalMax);
        }

        // ------------------------------------------------------------------
        // 静态读点（补丁每次调用 O(1)：先看 Active 是否为空）
        // ------------------------------------------------------------------

        internal static bool UseItemSealed(CharacterMainControl character)
        {
            ArenaTrials trials = ArenaTrials.Active;
            return trials != null && trials.Curses != null && character != null && character.IsMainCharacter && trials.Curses.Sealed;
        }

        internal static float UseTimeScale(CharacterMainControl character)
        {
            ArenaTrials trials = ArenaTrials.Active;
            if (trials == null || character == null || !character.IsMainCharacter || !trials.Profile.CutSupply) return 1f;
            return ArenaTrialsTuning.CutSupplyUseTimeMultiplier;
        }

        internal static bool HealBlocked(Health health)
        {
            ArenaTrials trials = ArenaTrials.Active;
            return trials != null && trials.Curses != null && trials.Curses.HealBlockedFor(health);
        }

        /// <summary>失明下这条血条量化成几格；0 表示照常。</summary>
        internal static int BlindStepsFor(Health health)
        {
            ArenaTrials trials = ArenaTrials.Active;
            if (trials == null || !trials.Profile.Blind || health == null) return 0;
            CharacterMainControl character = health.TryGetCharacter();
            return character != null && trials.IsFoe(character) ? ArenaTrialsTuning.BlindHealthBarSteps : 0;
        }
    }
}
