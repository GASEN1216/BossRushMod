using System;
using System.Collections.Generic;

namespace BossRush
{
    internal sealed partial class ModeHRuntimeModule
    {
        private string _selectedMatchCommandId;
        private bool _showLoadoutEditor;
        private int _loadoutSection;
        private int _loadoutOptionPage;

        private bool CanEditLoadout(ModeHMatchRosterDto roster)
        {
            return !_commandsClosed && _season != null && _runState != null
                && ReferenceEquals(_season.matchRoster, roster)
                && roster.matchIndex == _runState.MatchIndex
                && (_runState.Lifecycle == ModeHLifecycle.LoadoutEditing
                    || _runState.Lifecycle == ModeHLifecycle.OddsPreview);
        }

        private void AddPreparationOption(ModeHPageContent page, string label, Action edit)
        {
            ModeHMatchRosterDto owner = _season.matchRoster;
            page.PreparationOptions.Add(new ModeHActionData
            {
                Label = label,
                OnClick = delegate
                {
                    if (!CanEditLoadout(owner)) return;
                    edit();
                    RouteUiForLifecycle(_runState.Lifecycle);
                },
            });
        }

        private ModeHPageContent BuildLoadoutEditorPage()
        {
            ModeHPageContent page = new ModeHPageContent();
            page.Title = L10n.T("本场阵容、配装与口令", "Match roster, kits and command");
            ModeHMatchRosterDto roster = _season.matchRoster;
            if (_loadoutSection == 0)
            {
                AddPreparationSection(page, 1, L10n.T("阵容与休息", "Roster and rest"));
                AddPreparationSection(page, 2, L10n.T("首发配装：", "Starter kits: ")
                    + ResolveProfileDisplayName(roster.matchStarterProfileId));
                if (!string.IsNullOrEmpty(roster.matchRelayProfileId))
                    AddPreparationSection(page, 3, L10n.T("接力配装：", "Relay kits: ")
                        + ResolveProfileDisplayName(roster.matchRelayProfileId));
                AddPreparationSection(page, 4, L10n.T("本场口令：", "Match command: ")
                    + ResolveCommandDisplayName(_selectedMatchCommandId));
            }
            else if (_loadoutSection == 1) AddRosterOptions(page, roster);
            else if (_loadoutSection == 2) AddKitOptions(page,
                FindSeasonProfile(roster.matchStarterProfileId), roster.starterKitIds);
            else if (_loadoutSection == 3) AddKitOptions(page,
                FindSeasonProfile(roster.matchRelayProfileId), roster.relayKitIds);
            else AddCommandOptions(page, roster);

            const int pageSize = 6;
            int lastPage = Math.Max(0, (page.PreparationOptions.Count - 1) / pageSize);
            _loadoutOptionPage = Math.Max(0, Math.Min(_loadoutOptionPage, lastPage));
            int offset = _loadoutOptionPage * pageSize;
            page.PreparationOptions = page.PreparationOptions.GetRange(offset,
                Math.Min(pageSize, page.PreparationOptions.Count - offset));
            if (_loadoutOptionPage > 0) AddPreparationPageAction(page, roster, -1, L10n.T("上一页", "Previous"));
            if (_loadoutOptionPage < lastPage) AddPreparationPageAction(page, roster, 1, L10n.T("下一页", "Next"));
            page.Actions.Add(new ModeHActionData
            {
                Label = _loadoutSection == 0 ? L10n.T("返回赔率预览", "Review odds") : L10n.T("返回整备", "Back to preparation"),
                OnClick = delegate
                {
                    if (!CanEditLoadout(roster)) return;
                    if (_loadoutSection == 0) _showLoadoutEditor = false;
                    _loadoutSection = 0;
                    _loadoutOptionPage = 0;
                    RouteUiForLifecycle(_runState.Lifecycle);
                },
            });
            return page;
        }

        private void AddPreparationSection(ModeHPageContent page, int section, string label)
        {
            AddPreparationOption(page, label, delegate { _loadoutSection = section; _loadoutOptionPage = 0; });
        }

        private void AddPreparationPageAction(ModeHPageContent page, ModeHMatchRosterDto roster, int delta, string label)
        {
            page.Actions.Add(new ModeHActionData { Label = label, OnClick = delegate
            {
                if (!CanEditLoadout(roster)) return;
                _loadoutOptionPage += delta;
                RouteUiForLifecycle(_runState.Lifecycle);
            }});
        }

        private void AddRosterOptions(ModeHPageContent page, ModeHMatchRosterDto roster)
        {
            List<string> live = ModeHTransferMarket.GetLiveContractProfileIds(_season);
            foreach (string id in live)
            {
                string profileId = id;
                AddPreparationOption(page, (roster.matchStarterProfileId == id ? "√ " : "")
                    + L10n.T("首发：", "Starter: ") + ResolveProfileDisplayName(id) + "\n" + DescribeFighterState(FindSeasonProfile(id)), delegate
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
                });
                if (id == roster.matchStarterProfileId) continue;
                AddPreparationOption(page, (roster.matchRelayProfileId == id ? "√ " : "")
                    + L10n.T("接力：", "Relay: ") + ResolveProfileDisplayName(id) + "\n" + DescribeFighterState(FindSeasonProfile(id)), delegate
                {
                    if (roster.matchRelayProfileId == profileId) return;
                    roster.matchRelayProfileId = profileId;
                    roster.relayKitIds = BuildDefaultKitSelection(FindSeasonProfile(profileId));
                });
            }
            AddPreparationOption(page, (string.IsNullOrEmpty(roster.matchRelayProfileId) ? "√ " : "")
                + L10n.T("接力休息，本场单人出战", "Rest relay; fight solo"), delegate
            {
                roster.matchRelayProfileId = string.Empty;
                roster.relayKitIds = new List<string>();
            });
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
                AddPreparationOption(page, (_selectedMatchCommandId == command ? "√ " : "")
                    + L10n.T("口令：", "Command: ") + name,
                    delegate { _selectedMatchCommandId = selected; });
            }
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
                AddPreparationOption(page, (selected.Contains(kit.Spec.KitId) ? "√ " : "")
                    + L10n.T(kit.Spec.NameKey) + "\n" + L10n.T(kit.Spec.DescKey), delegate
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
                });
            }
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
                    && !spec.RequiresRelayEntered && ModeHCommandCompatibilityRegistry.GetEffectStatus(starter.stableKey, effect.EffectId)
                        == ModeHCommandCompatibilityStatus.VerifiedBehavior;
                bool relayVerified = relay != null && (!spec.IsSignature || relay.signatureCommandId == spec.CommandId)
                    && ModeHCommandCompatibilityRegistry.GetEffectStatus(relay.stableKey, effect.EffectId)
                        == ModeHCommandCompatibilityStatus.VerifiedBehavior;
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

        private static string DescribeReconResult(ModeHMatchPlanDto plan)
        {
            if (plan.reconChoiceId == "current_injury") return L10n.T("带伤敌军：", "Wounded enemies: ")
                + ModeHEncounterPlanner.GetWoundedEnemyCount(plan)
                + L10n.T("；高威胁者优先带伤，以 75% 生命入场，可正常治疗。", "; highest-threat enemies are wounded first and enter at 75% health; healing remains possible.");
            if (plan.reconChoiceId != "second_equipment") return plan.reconResult;
            string[] tags = (plan.reconResult ?? string.Empty).Split(',');
            List<string> labels = new List<string>();
            foreach (string tag in tags)
            {
                switch (tag)
                {
                    case "melee_rush": labels.Add(L10n.T("近身突击", "Close assault")); break;
                    case "burst": labels.Add(L10n.T("爆发", "Burst")); break;
                    case "kiting": labels.Add(L10n.T("移动牵制", "Kiting")); break;
                    case "ranged_pressure": labels.Add(L10n.T("远程压制", "Ranged pressure")); break;
                    case "armor_heavy": labels.Add(L10n.T("重甲", "Heavy armor")); break;
                    case "hold_ground": labels.Add(L10n.T("阵地防守", "Hold ground")); break;
                    case "area_denial": labels.Add(L10n.T("范围压制", "Area denial")); break;
                    case "attrition": labels.Add(L10n.T("消耗", "Attrition")); break;
                    case "control_core": labels.Add(L10n.T("控制", "Control")); break;
                    case "execute": labels.Add(L10n.T("残局追击", "Finishing pressure")); break;
                }
            }
            return string.Join(L10n.T("、", ", "), labels.ToArray());
        }

        private void AppendMatchPreview(ModeHPageContent page)
        {
            ModeHPublicSummaryDto summary = _season != null && _season.currentMatchPlan != null
                ? _season.currentMatchPlan.publicSummary : null;
            if (summary == null) return;
            string prefix = ModeHConfig.LocalizationKeyPrefix;
            page.Lines.Add(L10n.T("敌军人数：", "Enemy count: ") + summary.enemyCountMin + "–" + summary.enemyCountMax
                + L10n.T(" · 主要身份：", " · Main role: ") + L10n.T(prefix + "Archetype_" + summary.primaryArchetypeId));
            page.Lines.Add(L10n.T(prefix + "Entry_" + summary.entryScriptId) + ": " + L10n.T(prefix + "EntryHint_" + summary.entryScriptId));
            page.Lines.Add(L10n.T(prefix + "Condition_" + summary.conditionId) + ": "
                + L10n.T(prefix + "Condition_" + summary.conditionId + "_Desc"));
            if (summary.hasHighThreatCore) page.Lines.Add(L10n.T("含高威胁核心，留意它的入场时机。", "High-threat core present; watch its entry timing."));
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
