using System;
using System.Collections.Generic;
using ItemStatsSystem;

namespace BossRush
{
    internal sealed partial class ModeHRuntimeModule
    {
        private string _selectedMatchCommandId;
        private bool _showLoadoutEditor;
        /// <summary>整备页当前分区：1 阵容 / 2 首发配装 / 3 接力配装 / 4 口令（页头下一排页签，审查 B-08）。</summary>
        private int _loadoutSection = 1;

        private bool CanEditLoadout(ModeHMatchRosterDto roster)
        {
            return !_commandsClosed && _season != null && _runState != null
                && ReferenceEquals(_season.matchRoster, roster)
                && roster.matchIndex == _runState.MatchIndex
                && (_runState.Lifecycle == ModeHLifecycle.LoadoutEditing
                    || _runState.Lifecycle == ModeHLifecycle.OddsPreview);
        }

        /// <summary>
        /// 整备页的一行选项。<paramref name="selected"/> 为真时这一行画成选中卡（主色描边 + 「√ 已选」角标），
        /// 标签里不再拼行首「√ 」（审查 UB-11：选中与未选中只差一个字符）。
        /// </summary>
        private void AddPreparationOption(ModeHPageContent page, string label, Action edit, bool selected = false,
            string selectedBadge = null)
        {
            page.PreparationOptions.Add(MakePreparationOption(label, edit, selected, selectedBadge));
        }

        private ModeHActionData MakePreparationOption(string label, Action edit, bool selected, string selectedBadge)
        {
            ModeHMatchRosterDto owner = _season.matchRoster;
            return new ModeHActionData
            {
                Label = label,
                IsSelected = selected,
                SelectedBadge = selectedBadge,
                OnClick = delegate
                {
                    if (!CanEditLoadout(owner)) return;
                    edit();
                    RouteUiForLifecycle(_runState.Lifecycle);
                },
            };
        }

        /// <summary>
        /// 整备页（审查 B-08）：四个分区做成页头下的一排页签（阵容 / 首发配装 / 接力配装 / 口令），
        /// 不再是「先进目录页、再点进分区、6 项一页翻页」；选项列表本身可以滚动，不分页。
        /// 底栏只有一颗「完成」，回到双方对照页再锁定开打。
        /// 2026-09-25 owner「整备页也是乱的」：页签下一行写清当前的首发 / 接力 / 口令；阵容页左列首发、右列接力；
        /// 配装页两列、每格带物品图标；口令页整行（说明长）。
        /// </summary>
        private ModeHPageContent BuildLoadoutEditorPage()
        {
            ModeHPageContent page = new ModeHPageContent();
            page.Title = L10n.T("本场阵容、配装与口令", "Match roster, kits and command");
            ModeHMatchRosterDto roster = _season.matchRoster;
            bool hasRelay = !string.IsNullOrEmpty(roster.matchRelayProfileId);
            if (_loadoutSection < 1 || _loadoutSection > 4 || (_loadoutSection == 3 && !hasRelay)) _loadoutSection = 1;

            ModeHOptionRow tabs = new ModeHOptionRow();
            tabs.AtTop = true;
            AddSectionTab(tabs, roster, 1, L10n.T("阵容", "Roster"));
            AddSectionTab(tabs, roster, 2, L10n.T("首发配装 ", "Starter kits ") + CountKits(roster.starterKitIds));
            // 接力休息（单人出战）时没有接力配装可调：不挂这个页签（§4.14 不挂灰掉的占位项）
            if (hasRelay) AddSectionTab(tabs, roster, 3, L10n.T("接力配装 ", "Relay kits ") + CountKits(roster.relayKitIds));
            AddSectionTab(tabs, roster, 4, L10n.T("口令", "Command"));
            page.OptionRows.Add(tabs);
            page.Body = DescribeLoadoutSummary(roster);

            if (_loadoutSection == 1) AddRosterOptions(page, roster);
            else if (_loadoutSection == 2) AddKitOptions(page,
                FindSeasonProfile(roster.matchStarterProfileId), roster.starterKitIds);
            else if (_loadoutSection == 3) AddKitOptions(page,
                FindSeasonProfile(roster.matchRelayProfileId), roster.relayKitIds);
            else AddCommandOptions(page, roster);

            page.Actions.Add(new ModeHActionData
            {
                Label = L10n.T("完成", "Done"),
                IsPrimary = true,
                IsCancel = true, // ESC = 完成，回赔率页
                OnClick = delegate
                {
                    if (!CanEditLoadout(roster)) return;
                    _showLoadoutEditor = false;
                    _loadoutSection = 1;
                    RouteUiForLifecycle(_runState.Lifecycle);
                },
            });
            return page;
        }

        /// <summary>页签下那一行：「首发 A · 接力 B · 口令 C」（单人出战、还没选口令时照实写）。</summary>
        private string DescribeLoadoutSummary(ModeHMatchRosterDto roster)
        {
            string relay = string.IsNullOrEmpty(roster.matchRelayProfileId)
                ? L10n.T("休息（单人出战）", "resting (solo)")
                : ResolveProfileDisplayName(roster.matchRelayProfileId);
            string command = L10n.T("未选", "not chosen");
            if (!string.IsNullOrEmpty(_selectedMatchCommandId))
            {
                ModeHCommandSpec spec = ModeHContentCatalog.Commands.Find(c => c.CommandId == _selectedMatchCommandId);
                if (spec != null) command = L10n.T(spec.NameKey);
            }
            return L10n.T("首发 ", "Starter ") + ResolveProfileDisplayName(roster.matchStarterProfileId)
                + L10n.T("　·　接力 ", "  ·  Relay ") + relay
                + L10n.T("　·　口令 ", "  ·  Command ") + command;
        }

        /// <summary>「已带 n / 上限」：配装是多选，页签上直接写件数（审查 B-22）。</summary>
        private static string CountKits(List<string> kits)
        {
            return (kits != null ? kits.Count : 0) + "/" + ModeHConfig.MaxKitsPerFighter;
        }

        private void AddSectionTab(ModeHOptionRow tabs, ModeHMatchRosterDto roster, int section, string label)
        {
            tabs.Options.Add(new ModeHActionData
            {
                Label = label,
                IsSelected = _loadoutSection == section,
                OnClick = delegate
                {
                    if (!CanEditLoadout(roster)) return;
                    _loadoutSection = section;
                    RouteUiForLifecycle(_runState.Lifecycle);
                },
            });
        }

        /// <summary>阵容页：左列选首发、右列选接力（含「接力休息」），两列同一行对齐；列头写「首发」「接力」。</summary>
        private void AddRosterOptions(ModeHPageContent page, ModeHMatchRosterDto roster)
        {
            List<string> live = ModeHTransferMarket.GetLiveContractProfileIds(_season);
            List<ModeHActionData> starters = new List<ModeHActionData>();
            List<ModeHActionData> relays = new List<ModeHActionData>();
            foreach (string id in live)
            {
                string profileId = id;
                starters.Add(MakePreparationOption(ResolveProfileDisplayName(id)
                    + "\n" + DescribeFighterState(FindSeasonProfile(id)), delegate
                {
                    if (roster.matchStarterProfileId == profileId) return;
                    string old = roster.matchStarterProfileId;
                    roster.matchStarterProfileId = profileId;
                    if (roster.matchRelayProfileId == profileId)
                    {
                        roster.matchRelayProfileId = old;
                        roster.relayKitIds = roster.starterKitIds;
                    }
                    roster.starterKitIds = BuildDefaultKitSelection(FindSeasonProfile(profileId));
                    roster.activeProfileId = profileId;
                }, roster.matchStarterProfileId == id, L10n.T("√ 首发", "√ Starter")));
                if (id == roster.matchStarterProfileId) continue;
                relays.Add(MakePreparationOption(ResolveProfileDisplayName(id)
                    + "\n" + DescribeFighterState(FindSeasonProfile(id)), delegate
                {
                    if (roster.matchRelayProfileId == profileId) return;
                    roster.matchRelayProfileId = profileId;
                    roster.relayKitIds = BuildDefaultKitSelection(FindSeasonProfile(profileId));
                }, roster.matchRelayProfileId == id, L10n.T("√ 接力", "√ Relay")));
            }
            relays.Add(MakePreparationOption(L10n.T("接力休息", "Rest the relay")
                + "\n" + L10n.T("这一场只让首发上，接力歇一场", "Only the starter fights; the relay sits this one out"), delegate
            {
                roster.matchRelayProfileId = string.Empty;
                roster.relayKitIds = new List<string>();
            }, string.IsNullOrEmpty(roster.matchRelayProfileId), L10n.T("√ 单人", "√ Solo")));

            page.PreparationColumns = 2;
            page.PreparationRowHeight = 100f;
            page.PreparationHeaders.Add(L10n.T("首发", "Starter"));
            page.PreparationHeaders.Add(L10n.T("接力", "Relay"));
            int rows = Math.Max(starters.Count, relays.Count);
            for (int i = 0; i < rows; i++)
            {
                // 空位写 null：渲染按下标排格，null 格跳过，两列各自从上往下对齐
                page.PreparationOptions.Add(i < starters.Count ? starters[i] : null);
                page.PreparationOptions.Add(i < relays.Count ? relays[i] : null);
            }
        }

        private void AddCommandOptions(ModeHPageContent page, ModeHMatchRosterDto roster)
        {
            ModeHProfileDto relay = FindSeasonProfile(roster.matchRelayProfileId);
            ModeHProfileDto starter = FindSeasonProfile(roster.matchStarterProfileId);
            List<string> commands = GetMatchCommands(starter, relay);
            foreach (string command in commands)
            {
                string selected = command;
                string name = command;
                foreach (ModeHCommandSpec spec in ModeHContentCatalog.Commands)
                    if (spec.CommandId == command) { name = L10n.T(spec.NameKey) + "\n"
                        + DescribeCommand(spec, starter, relay); break; }
                AddPreparationOption(page, name,
                    delegate { _selectedMatchCommandId = selected; }, _selectedMatchCommandId == command);
            }
            page.PreparationRowHeight = 100f;
        }

        private void AddKitOptions(ModeHPageContent page, ModeHProfileDto profile, List<string> selected)
        {
            if (profile == null || selected == null) return;
            foreach (ModeHResolvedKit kit in ModeHLoadoutKitRegistry.GetSelectableKits(
                _season.unlockedKitIds, profile.archetypeId, profile.profileId))
            {
                if (kit == null || !kit.Available || kit.Spec == null
                    || ModeHInjuryAndScarSystem.InjuryDisablesKitSlot(profile.injuryId, kit.Spec.ReplaceSlot)) continue;
                bool replacesSlot = selected.Exists(id =>
                {
                    ModeHResolvedKit old = ModeHLoadoutKitRegistry.GetKit(id);
                    return old != null && old.Spec.ReplaceSlot == kit.Spec.ReplaceSlot;
                });
                if (!replacesSlot && selected.Count >= ModeHConfig.MaxKitsPerFighter) continue;
                ModeHResolvedKit choice = kit;
                ModeHActionData option = MakePreparationOption(L10n.T(kit.Spec.NameKey) + "\n" + L10n.T(kit.Spec.DescKey), delegate
                {
                    if (!selected.Remove(choice.Spec.KitId))
                    {
                        selected.RemoveAll(id =>
                        {
                            ModeHResolvedKit old = ModeHLoadoutKitRegistry.GetKit(id);
                            return old != null && old.Spec.ReplaceSlot == choice.Spec.ReplaceSlot;
                        });
                        if (selected.Count < ModeHConfig.MaxKitsPerFighter) selected.Add(choice.Spec.KitId);
                    }
                    selected.Sort(StringComparer.Ordinal);
                }, selected.Contains(kit.Spec.KitId), L10n.T("√ 已带上", "√ Equipped"));
                // 每格带上这件套装对应的官方物品图标与品质边（读元数据，不实例化物品）
                ItemMetaData meta = ItemAssetsCollection.GetMetaData(kit.ResolvedTypeId);
                option.Icon = meta.icon;
                option.IconQuality = kit.ResolvedQuality;
                page.PreparationOptions.Add(option);
            }
            page.PreparationColumns = 2;
            page.PreparationRowHeight = 108f;
        }

        private void NormalizeInjuredLoadout(ModeHMatchRosterDto roster, ModeHProfileDto starter, ModeHProfileDto relay)
        {
            IList<string> kits = FilterKitsForInjury(roster.starterKitIds, starter.injuryId);
            if (!ReferenceEquals(kits, roster.starterKitIds)) roster.starterKitIds = new List<string>(kits);
            kits = FilterKitsForInjury(roster.relayKitIds, relay != null ? relay.injuryId : null);
            if (!ReferenceEquals(kits, roster.relayKitIds)) roster.relayKitIds = new List<string>(kits);
        }

        private List<string> GetMatchCommands(ModeHProfileDto starter, ModeHProfileDto relay)
        {
            List<string> commands = ModeHCommandController.GetSelectableCommands(starter.stableKey,
                relay != null ? relay.stableKey : null, starter.signatureCommandId,
                relay != null ? relay.signatureCommandId : null);
            int enemyCount = _season.currentMatchPlan.enemyStableKeys != null ? _season.currentMatchPlan.enemyStableKeys.Count : 0;
            commands.RemoveAll(id =>
            {
                ModeHCommandSpec spec = ModeHContentCatalog.Commands.Find(c => c.CommandId == id);
                return spec == null || spec.RequiresEnemyCountAtLeast > enemyCount;
            });
            return commands;
        }

        private static string DescribeFighterState(ModeHProfileDto profile)
        {
            if (profile == null) return string.Empty;
            string prefix = ModeHConfig.LocalizationKeyPrefix;
            string injury = string.IsNullOrEmpty(profile.injuryId) ? L10n.T("健康", "Healthy")
                : L10n.T(prefix + "Injury_" + profile.injuryId) + ": " + L10n.T(prefix + "Injury_" + profile.injuryId + "_Desc");
            return injury + L10n.T(" · 战痕 ", " · Scars ") + (profile.scarIds != null ? profile.scarIds.Count : 0);
        }

        private static string DescribeCommand(ModeHCommandSpec spec, ModeHProfileDto starter, ModeHProfileDto relay)
        {
            List<string> parts = new List<string> { L10n.T("生效 6 秒", "Active for 6 seconds") };
            if (spec.RequiresRelayEntered) parts.Add(L10n.T("仅接力登场后", "After relay entry only"));
            if (spec.RequiresEnemyCountAtLeast > 0) parts.Add(L10n.T("敌军至少 ", "Enemies required: ") + spec.RequiresEnemyCountAtLeast);
            if (spec.IsSignature) parts.Add(L10n.T("仅招牌持有者响应", "Signature owner only"));
            if (spec.Effects != null) foreach (ModeHEffectSpec effect in spec.Effects)
            {
                if (effect == null) continue;
                bool starterVerified = starter != null && (!spec.IsSignature || starter.signatureCommandId == spec.CommandId)
                    && !spec.RequiresRelayEntered && ModeHCommandCompatibilityRegistry.HasVerifiedBehavior(starter.stableKey, effect.EffectId);
                bool relayVerified = relay != null && (!spec.IsSignature || relay.signatureCommandId == spec.CommandId)
                    && ModeHCommandCompatibilityRegistry.HasVerifiedBehavior(relay.stableKey, effect.EffectId);
                if (!starterVerified && !relayVerified) continue;
                string label = DescribeControlPoint(effect.ControlPointId);
                if (string.IsNullOrEmpty(label)) continue;
                if (effect.Op == "set_bool") label += effect.BoolValue ? L10n.T("开启", " on") : L10n.T("关闭", " off");
                else if (effect.MultiplierMilli > 0) label += " ×" + (effect.MultiplierMilli / 1000f).ToString("0.##");
                else if (effect.Op == "set_value") label += " = " + (effect.ValueMilli / 1000f).ToString("0.##");
                parts.Add(label);
            }
            return string.Join(L10n.T("；", "; "), parts.ToArray());
        }

        private static string DescribeControlPoint(string id)
        {
            switch (id)
            {
                case "skillSuccessChance": return L10n.T("技能施放概率", "Skill chance");
                case "itemSkillChance": return L10n.T("物品技能概率", "Item-skill chance");
                case "skillCoolTimeRange": return L10n.T("技能间隔", "Skill interval");
                case "itemSkillCoolTime": return L10n.T("物品技能间隔", "Item-skill interval");
                case "shootCanMove": return L10n.T("移动射击", "Move while shooting");
                case "sightDistance": return L10n.T("索敌距离", "Detection range");
                case "sightAngle": return L10n.T("索敌视角", "Detection angle");
                case "combatTurnSpeed": return L10n.T("战斗转身速度", "Combat turn speed");
                case "patrolTurnSpeed": return L10n.T("巡逻转身速度", "Patrol turn speed");
                case "baseReactionTime": return L10n.T("反应耗时", "Reaction delay");
                case "coward_mitigation": return L10n.T("胆怯概率", "Cowardice chance");
                case "moveToPos": return L10n.T("回到擂台中央", "Move to ring center");
                case "searchedEnemy": return L10n.T("优先攻击残血目标", "Prioritize wounded target");
                case "setNoticedToTarget": return L10n.T("注意当前目标", "Notice target");
                case "nextReleaseSkillTimeMarker": return L10n.T("调整下次技能时机", "Adjust next skill timing");
                default: return string.Empty;
            }
        }

        /// <summary>看盘 / 赔率对照页场次行下面那行小字（替代旧的「赛况 / 侦察」页）。本场规则一行小字：「本场规则 · 中央掩体：蓝圈内……」；有高威胁核心时补半句。</summary>
        private string DescribeMatchNote()
        {
            ModeHPublicSummaryDto summary = _season != null && _season.currentMatchPlan != null
                ? _season.currentMatchPlan.publicSummary : null;
            if (summary == null || string.IsNullOrEmpty(summary.conditionId)) return null;
            string prefix = ModeHConfig.LocalizationKeyPrefix;
            string note = L10n.T("本场规则 · ", "Match rule · ") + L10n.T(prefix + "Condition_" + summary.conditionId)
                + L10n.T("：", ": ") + L10n.T(prefix + "Condition_" + summary.conditionId + "_Desc");
            if (summary.hasHighThreatCore) note += L10n.T("　对面有狠角色，留意它什么时候上场。", "  A heavy hitter is on their side; watch when it enters.");
            return note;
        }

        private bool RefreshSelectedLoadoutDigest(out string error)
        {
            // 摘要覆盖阵容、套装和实际口令，预览与锁盘共用这一份输入。
            ModeHMatchRosterDto roster = _season.matchRoster;
            ModeHLoadoutLockDto input = new ModeHLoadoutLockDto();
            input.matchIndex = roster.matchIndex;
            input.matchStarterProfileId = roster.matchStarterProfileId;
            input.matchRelayProfileId = roster.matchRelayProfileId;
            input.starterKitIds = roster.starterKitIds;
            input.relayKitIds = roster.relayKitIds;
            input.commandId = _selectedMatchCommandId;
            return ModeHCanonicalDigest.TryComputeObjectDigest(input, null, out roster.loadoutDigest, out error);
        }
    }
}
