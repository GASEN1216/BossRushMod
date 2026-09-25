// 只把锁定的装备预案投影成页面数据；不在绘制过程中实例化或穿戴物品。
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    internal sealed partial class ModeHRuntimeModule
    {
        private Coroutine _preparedPageRoutine;
        private long _preparedPageOwner;
        private ModeHLifecycle _preparedPagePhase;
        private int _preparedPageMatch, _preparedPageRefresh, _preparedPageGeneration;
        private bool _preparedPageComplete;
        /// <summary>选人页刷新候选后原地换内容时，让新一批卡错峰升起一次（面板本身不重播打开动画）。纯运行时。</summary>
        private bool _replayCardEntrance;

        /// <summary>首次装配预览每帧一名；同一页重绘只读已有预案，不重新创建物品树。</summary>
        private bool TryDeferPreparedFighterPage(ModeHLifecycle phase)
        {
            if (phase != ModeHLifecycle.Drafting && phase != ModeHLifecycle.MatchBrief)
            {
                if (_preparedPageRoutine != null) CancelPreparedFighterPage();
                return false;
            }
            if (_owner == null || _runState == null || _season == null) return false;
            bool same = _preparedPageOwner == _runState.OwnerToken && _preparedPagePhase == phase
                && _preparedPageMatch == _runState.MatchIndex && _preparedPageRefresh == _draftRefreshCount;
            if (same && _preparedPageComplete) return false;
            if (same && _preparedPageRoutine != null) return true;
            CancelPreparedFighterPage();
            if (phase == ModeHLifecycle.Drafting) EnsureDraftCandidates();
            else EnsureMatchPlan();
            if (_commandsClosed || _runState == null || _runState.Lifecycle != phase) return true;
            _preparedPageOwner = _runState.OwnerToken;
            _preparedPagePhase = phase;
            _preparedPageMatch = _runState.MatchIndex;
            _preparedPageRefresh = _draftRefreshCount;
            if (_ui != null && _ui.CurrentPage != ModeHPage.None)
            {
                // 已经有一页开着（选人页点了刷新、结算页点了下一场）：原页留着、只挡点击，预案备好后原地换内容。
                // 旧版先换成一张「准备参赛选手」再换回来，面板连播两次打开动画（2026-09-25 owner：刷新时整页像关掉又重开）
                _ui.SetPageBusy(true);
                _replayCardEntrance = phase == ModeHLifecycle.Drafting && _ui.CurrentPage == ModeHPage.Entry;
            }
            else
            {
                ModeHPageContent loading = new ModeHPageContent
                {
                    Title = L10n.T("准备参赛选手", "Preparing fighters"),
                    Body = L10n.T("正在给选手换装备、量属性，马上就好。", "Getting the fighters geared up. One moment."),
                    IsPlaceholder = true,
                };
                loading.Actions.Add(new ModeHActionData
                {
                    Label = L10n.T("返回基地", "Return to base"),
                    IsCancel = true,
                    OnClick = delegate { RequestExit(ModeHExitReason.UserMapReturn, "spectator_exit"); },
                });
                OpenPage(ModeHPage.Brief, loading);
                _replayCardEntrance = false;
            }
            _preparedPageRoutine = _owner.StartCoroutine(PrepareFighterPage(_preparedPageGeneration));
            return true;
        }

        private bool IsPreparedPageCurrent(int generation)
        {
            return generation == _preparedPageGeneration && !_commandsClosed && _runState != null
                && _season != null && _preparedPageOwner == _runState.OwnerToken
                && _preparedPagePhase == _runState.Lifecycle && _preparedPageMatch == _runState.MatchIndex
                && _preparedPageRefresh == _draftRefreshCount;
        }

        private IEnumerator PrepareFighterPage(int generation)
        {
            // StartCoroutine 首次执行到这里就返回，句柄先由 owner 持有。
            yield return null;
            if (!IsPreparedPageCurrent(generation)) yield break;
            List<string> profiles = _preparedPagePhase == ModeHLifecycle.Drafting
                ? (_season.draftCandidateProfileIds != null ? new List<string>(_season.draftCandidateProfileIds) : null)
                : ModeHTransferMarket.GetLiveContractProfileIds(_season);
            bool failed = profiles == null || profiles.Count == 0;
            for (int i = 0; profiles != null && i < profiles.Count; i++)
            {
                while (IsPreparedPageCurrent(generation) && BossRushUI.IsGamePaused()) yield return null;
                if (!IsPreparedPageCurrent(generation)) yield break;
                if (!PrepareOneFighterPreview(profiles[i], -1)) { failed = true; break; }
                yield return null;
            }
            if (!IsPreparedPageCurrent(generation)) yield break;
            ModeHMatchPlanDto plan = _preparedPagePhase == ModeHLifecycle.MatchBrief ? _season.currentMatchPlan : null;
            for (int i = 0; !failed && plan != null && plan.enemyStableKeys != null && i < plan.enemyStableKeys.Count; i++)
            {
                while (IsPreparedPageCurrent(generation) && BossRushUI.IsGamePaused()) yield return null;
                if (!IsPreparedPageCurrent(generation)) yield break;
                if (!PrepareOneFighterPreview(null, i)) { failed = true; break; }
                yield return null;
            }
            if (!IsPreparedPageCurrent(generation)) yield break;
            _preparedPageRoutine = null;
            if (_ui != null) _ui.SetPageBusy(false);
            if (failed)
            {
                _replayCardEntrance = false;
                RequestTechnicalRetry("prepared_stats_missing");
                yield break;
            }
            _preparedPageComplete = true;
            RouteUiForLifecycle(_runState.Lifecycle);
        }

        private bool PrepareOneFighterPreview(string profileId, int enemyIndex)
        {
            try
            {
                if (enemyIndex >= 0) return GetPreparedEnemyStats(_season.currentMatchPlan, enemyIndex) != null;
                ModeHProfileDto profile = FindSeasonProfile(profileId);
                IList<string> kits = null;
                ModeHMatchRosterDto roster = _season.matchRoster;
                if (_preparedPagePhase == ModeHLifecycle.MatchBrief && roster != null && roster.matchIndex == _runState.MatchIndex)
                {
                    if (roster.matchStarterProfileId == profileId) kits = roster.starterKitIds;
                    else if (roster.matchRelayProfileId == profileId) kits = roster.relayKitIds;
                }
                return GetPreparedFighterStats(profile, kits) != null;
            }
            catch (Exception e)
            {
                LogFailure("prepared_fighter_page", e);
                return false;
            }
        }

        private void CancelPreparedFighterPage()
        {
            _preparedPageGeneration++;
            Coroutine routine = _preparedPageRoutine;
            _preparedPageRoutine = null;
            _preparedPageComplete = false;
            _preparedPageOwner = 0;
            if (routine != null && _owner != null) _owner.StopCoroutine(routine);
            _replayCardEntrance = false;
            if (routine != null && _ui != null) _ui.SetPageBusy(false);
        }

        private void FillFighterDetails(ModeHCardData card, ModeHPreparedFighterStats stats)
        {
            if (card == null || stats == null) return;
            AddFighterStat(card, L10n.T("生命", "HP"), stats.Health, 1000f, "0");
            AddFighterStat(card, L10n.T("伤害", "Damage"), stats.Damage, 200f, "0.#");
            AddFighterStat(card, L10n.T("移速", "Speed"), stats.MoveSpeed, 10f, "0.##");
            AddFighterStat(card, L10n.T("射程", "Range"), stats.Range, 50f, "0.#");
            AddFighterStat(card, L10n.T("护甲", "Armor"), stats.Armor, 10f, "0.#");
            AddFighterStat(card, L10n.T("头盔", "Helmet"), stats.HeadArmor, 10f, "0.#");
            AddFighterStat(card, L10n.T("暴击", "Crit"), stats.CritChance * 100f, 100f, "0.#", "%");
            AddFighterStat(card, L10n.T("战力", "Power"), stats.Power, 500f, "0");
            for (int i = 0; stats.Gear != null && i < stats.Gear.Count; i++)
            {
                ModeHPreparedGear gear = stats.Gear[i];
                if (gear == null) continue;
                card.Equipment.Add(new ModeHItemIconData
                {
                    Icon = gear.Icon,
                    Label = ResolveEquipmentSlotLabel(gear.Slot),
                    Count = 0,
                    Quality = ItemAssetsCollection.GetMetaData(gear.TypeId).quality,
                });
            }
        }

        private static void AddFighterStat(ModeHCardData card, string label, float value, float maximum,
            string format, string suffix = "")
        {
            card.Stats.Add(new ModeHStatData
            {
                Label = label,
                Value = value,
                Maximum = Math.Max(maximum, value),
                Text = value.ToString(format, CultureInfo.InvariantCulture) + suffix,
            });
        }

        private static string ResolveEquipmentSlotLabel(string slot)
        {
            switch ((slot ?? string.Empty).ToLowerInvariant())
            {
                case "primaryweapon": case "primary": case "weapon": return L10n.T("武器", "Weapon");
                case "secondaryweapon": case "secondary": return L10n.T("副武器", "Sidearm");
                case "meleeweapon": case "melee": return L10n.T("近战", "Melee");
                case "helmat": case "helmet": case "head": return L10n.T("头盔", "Helmet");
                case "armor": case "body": return L10n.T("护甲", "Armor");
                case "backpack": return L10n.T("背包", "Pack");
                case "face": case "mask": return L10n.T("面罩", "Mask");
                case "headset": case "ear": return L10n.T("耳机", "Headset");
                case "totem": return L10n.T("图腾", "Totem");
                default: return L10n.T("装备", "Gear");
            }
        }

        private static void NormalizeFighterStatScales(List<ModeHCardData> first, List<ModeHCardData> second = null)
        {
            List<ModeHCardData> all = new List<ModeHCardData>(first);
            if (second != null) all.AddRange(second);
            for (int statIndex = 0; statIndex < 8; statIndex++)
            {
                float maximum = 1f;
                for (int i = 0; i < all.Count; i++)
                    if (all[i] != null && all[i].Stats.Count > statIndex)
                        maximum = Math.Max(maximum, all[i].Stats[statIndex].Value);
                for (int i = 0; i < all.Count; i++)
                    if (all[i] != null && all[i].Stats.Count > statIndex)
                        all[i].Stats[statIndex].Maximum = maximum;
            }
        }

        private void AppendPrizeIcons(ModeHPageContent page, ModeHCashBetRecord record)
        {
            List<ModeHItemBetEntry> prizes = ModeHItemBetEntry.Decode(record.prizeItems);
            for (int i = 0; i < prizes.Count; i++)
            {
                ModeHItemBetEntry prize = prizes[i];
                if (prize == null) continue;
                ItemMetaData meta = ItemAssetsCollection.GetMetaData(prize.TypeId);
                page.RewardItems.Add(new ModeHItemIconData
                {
                    Icon = meta.icon,
                    Count = prize.Count,
                    Quality = prize.Quality,
                });
            }
        }

        private static void AppendUnlockedKitIcon(ModeHPageContent page, ModeHSeasonRewardOperationDto reward)
        {
            if (page == null || reward == null || string.IsNullOrEmpty(reward.selectedRewardKitId)) return;
            ModeHResolvedKit kit = ModeHLoadoutKitRegistry.GetKit(reward.selectedRewardKitId);
            if (kit == null || !kit.Available) return;
            ItemMetaData meta = ItemAssetsCollection.GetMetaData(kit.ResolvedTypeId);
            page.RewardItems.Add(new ModeHItemIconData { Icon = meta.icon, Quality = kit.ResolvedQuality });
        }
    }
}
