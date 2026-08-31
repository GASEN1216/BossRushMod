// Mode H 实战使用的赛季选手查询与深拷贝辅助。
using System;
using System.Collections.Generic;

namespace BossRush
{
    internal sealed partial class ModeHRuntimeModule
    {
        private void ReplaceSeasonProfile(ModeHProfileDto snapshot)
        {
            if (snapshot == null || _season == null || _season.profiles == null) return;
            for (int i = 0; i < _season.profiles.Count; i++)
            {
                if (_season.profiles[i] != null
                    && string.Equals(_season.profiles[i].profileId, snapshot.profileId,
                        StringComparison.Ordinal))
                {
                    _season.profiles[i] = CloneProfile(snapshot);
                    return;
                }
            }
        }

        private ModeHProfileDto FindSeasonProfile(string profileId)
        {
            if (_season == null || _season.profiles == null || string.IsNullOrEmpty(profileId)) return null;
            for (int i = 0; i < _season.profiles.Count; i++)
            {
                ModeHProfileDto profile = _season.profiles[i];
                if (profile != null
                    && string.Equals(profile.profileId, profileId, StringComparison.Ordinal))
                {
                    return profile;
                }
            }
            return null;
        }

        private static ModeHProfileDto CloneProfile(ModeHProfileDto source)
        {
            if (source == null) return null;
            ModeHProfileDto copy = new ModeHProfileDto();
            copy.profileId = source.profileId;
            copy.stableKey = source.stableKey;
            copy.displayNameKey = source.displayNameKey;
            copy.archetypeId = source.archetypeId;
            copy.temperamentId = source.temperamentId;
            copy.quirkId = source.quirkId;
            copy.anomalyId = source.anomalyId;
            copy.signatureCommandId = source.signatureCommandId;
            copy.rumorKey = source.rumorKey;
            copy.standInPatternId = source.standInPatternId;
            copy.status = source.status;
            copy.injuryId = source.injuryId;
            copy.scarIds = source.scarIds != null
                ? new List<string>(source.scarIds) : new List<string>();
            copy.fameDisplayCount = source.fameDisplayCount;
            copy.enteredMatchCount = source.enteredMatchCount;
            copy.behaviorStatuses = new List<ModeHBehaviorStatusDto>();
            if (source.behaviorStatuses != null)
            {
                for (int i = 0; i < source.behaviorStatuses.Count; i++)
                {
                    ModeHBehaviorStatusDto item = source.behaviorStatuses[i];
                    if (item == null) continue;
                    copy.behaviorStatuses.Add(new ModeHBehaviorStatusDto
                    {
                        entryId = item.entryId,
                        entryKind = item.entryKind,
                        status = item.status,
                    });
                }
            }
            return copy;
        }

        private string ResolveProfileDisplayName(string profileId)
        {
            ModeHProfileDto profile = FindSeasonProfile(profileId);
            if (profile == null) return "-";
            string key = !string.IsNullOrEmpty(profile.displayNameKey)
                ? profile.displayNameKey
                : ModeHConfig.LocalizationKeyPrefix + "Fighter_" + profile.profileId;
            return L10n.T(key);
        }

        private ModeHMatchReportDto FindLatestPendingReport()
        {
            if (_season == null || _season.matchReports == null) return null;
            for (int i = _season.matchReports.Count - 1; i >= 0; i--)
            {
                ModeHMatchReportDto report = _season.matchReports[i];
                if (report != null && report.matchIndex == _runState.MatchIndex) return report;
            }
            return null;
        }

        private ModeHSeasonRewardOperationDto FindRewardOperation(string operationId)
        {
            if (_season == null || _season.seasonRewardOperations == null
                || string.IsNullOrEmpty(operationId)) return null;
            for (int i = 0; i < _season.seasonRewardOperations.Count; i++)
            {
                ModeHSeasonRewardOperationDto operation = _season.seasonRewardOperations[i];
                if (operation != null
                    && string.Equals(operation.operationId, operationId, StringComparison.Ordinal))
                {
                    return operation;
                }
            }
            return null;
        }
    }
}
