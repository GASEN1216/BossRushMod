using System;
using System.Collections.Generic;

namespace BossRush
{
    internal sealed partial class ModeHRuntimeModule
    {
        private string _selectedMatchCommandId;
        private bool _showLoadoutEditor;

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
            List<string> live = ModeHTransferMarket.GetLiveContractProfileIds(_season);
            foreach (string id in live)
            {
                string profileId = id;
                AddPreparationOption(page, (roster.matchStarterProfileId == id ? "✓ " : "")
                    + L10n.T("首发：", "Starter: ") + ResolveProfileDisplayName(id), delegate
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
                AddPreparationOption(page, (roster.matchRelayProfileId == id ? "✓ " : "")
                    + L10n.T("接力：", "Relay: ") + ResolveProfileDisplayName(id), delegate
                {
                    if (roster.matchRelayProfileId == profileId) return;
                    roster.matchRelayProfileId = profileId;
                    roster.relayKitIds = BuildDefaultKitSelection(FindSeasonProfile(profileId));
                });
            }
            AddPreparationOption(page, (string.IsNullOrEmpty(roster.matchRelayProfileId) ? "✓ " : "")
                + L10n.T("接力休息，本场单人出战", "Rest relay; fight solo"), delegate
            {
                roster.matchRelayProfileId = string.Empty;
                roster.relayKitIds = new List<string>();
            });
            AddKitOptions(page, FindSeasonProfile(roster.matchStarterProfileId), roster.starterKitIds);
            ModeHProfileDto relay = FindSeasonProfile(roster.matchRelayProfileId);
            if (relay != null) AddKitOptions(page, relay, roster.relayKitIds);
            ModeHProfileDto starter = FindSeasonProfile(roster.matchStarterProfileId);
            List<string> commands = ModeHCommandController.GetSelectableCommands(starter.stableKey,
                relay != null ? relay.stableKey : null, starter.signatureCommandId,
                relay != null ? relay.signatureCommandId : null);
            foreach (string command in commands)
            {
                string selected = command;
                string name = command;
                foreach (ModeHCommandSpec spec in ModeHContentCatalog.Commands)
                    if (spec.CommandId == command) { name = L10n.T(spec.NameKey); break; }
                AddPreparationOption(page, (_selectedMatchCommandId == command ? "✓ " : "")
                    + L10n.T("口令：", "Command: ") + name,
                    delegate { _selectedMatchCommandId = selected; });
            }
            page.Actions.Add(new ModeHActionData
            {
                Label = L10n.T("返回赔率预览", "Review odds"),
                OnClick = delegate
                {
                    if (!CanEditLoadout(roster)) return;
                    _showLoadoutEditor = false;
                    RouteUiForLifecycle(_runState.Lifecycle);
                },
            });
            return page;
        }

        private void AddKitOptions(ModeHPageContent page, ModeHProfileDto profile, List<string> selected)
        {
            foreach (ModeHResolvedKit kit in ModeHLoadoutKitRegistry.GetSelectableKits(
                _season.unlockedKitIds, profile.archetypeId, profile.profileId))
            {
                if (kit == null || !kit.Available || kit.Spec == null) continue;
                ModeHResolvedKit choice = kit;
                AddPreparationOption(page, (selected.Contains(kit.Spec.KitId) ? "✓ " : "")
                    + ResolveProfileDisplayName(profile.profileId) + " · " + L10n.T(kit.Spec.NameKey), delegate
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
