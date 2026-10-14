using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    /// <summary>个人联机比赛凭证。现金和押品仍由原押注账本提交；此处只保存选择、结果与去重事实。</summary>
    internal sealed class ModeHCoopLedger : IDisposable, IBossRushSaveBatchSource
    {
        internal const string StorageKey = "BossRush_ModeHCoop_v1";
        [Serializable] internal sealed class Round
        {
            public string Run, Token, Created;
            public long Seed, Net;
            public int Match, Odds, Winner;
            public bool Red, Resolved, Final, Forfeited;
        }
        [Serializable] internal sealed class Data
        {
            public int schemaVersion = 1;
            public List<Round> Rounds = new List<Round>();
        }
        private readonly BossRushSlotJsonStore<Data> store;
        private readonly BossRushSaveCoordinatorEngine engine;

        internal ModeHCoopLedger()
        {
            store = new BossRushSlotJsonStore<Data>(new BossRushSlotJsonStoreSpec<Data>
            {
                StorageKey = StorageKey, SchemaVersion = 1, LogPrefix = "[ModeH Coop] ", DisplayName = "鸭王杯个人联机记录",
                Encode = value => BossRushCoopJson.ToJson(value), Decode = Decode,
                ReadSchemaVersion = raw => { Data value = BossRushCoopJson.FromJson<Data>(raw); return value != null ? value.schemaVersion : -1; },
                CreateDefault = () => new Data(), NotifySlotChanged = ResetEngine,
                AfterPendingWriteFlushed = NotifyFlushed
            });
            engine = new BossRushSaveCoordinatorEngine(this, false);
            store.EnsureSubscribed();
        }
        private static Data Decode(string raw)
        {
            Data value = BossRushCoopJson.FromJson<Data>(raw);
            if (value == null || value.Rounds == null || value.Rounds.Count > 1024) return null;
            foreach (Round round in value.Rounds)
                if (round == null || string.IsNullOrEmpty(round.Run) || round.Run.Length > 128
                    || round.Match < 1 || round.Match > ModeHConfig.SeasonMatchCount || round.Winner < 0 || round.Winner > 2
                    || round.Odds < ModeHConfig.MinOdds || round.Odds > ModeHConfig.MaxOdds) return null;
            return value;
        }
        private void ResetEngine() { if (engine != null) engine.Reset(); }
        private void NotifyFlushed() { if (engine != null) engine.NotifyPendingWriteFlushed(); }
        private Data Copy()
        { Data current = store.Current; return current != null ? Decode(BossRushCoopJson.ToJson(current)) : null; }
        private static Round Find(Data data, string run, int match)
        {
            if (data != null) foreach (Round round in data.Rounds) if (round.Run == run && round.Match == match) return round;
            return null;
        }
        internal bool Owns(string run, int match) { return Find(store.Current, run, match) != null; }
        internal bool ResolveSide(string run, int match, bool fallback)
        { Round round = Find(store.Current, run, match); return round != null ? round.Red : fallback; }
        internal bool IsResolved(string run, int match)
        { Round round = Find(store.Current, run, match); return round != null && round.Resolved; }
        internal bool Prepare(string run, int match, long seed, bool red, int odds)
        {
            if (string.IsNullOrEmpty(run) || run.Length > 128 || match < 1 || match > ModeHConfig.SeasonMatchCount) return false;
            Data next = Copy();
            if (next == null) return false;
            Round found = Find(next, run, match);
            if (found == null)
            {
                if (odds < ModeHConfig.MinOdds || odds > ModeHConfig.MaxOdds) return false;
                // 只淘汰已闭环的最旧赛季；未结算凭证绝不因容量收缩消失。
                while (next.Rounds.Count >= 768)
                {
                    int remove = next.Rounds.FindIndex(x => x.Resolved && x.Run != run);
                    if (remove < 0) return false;
                    next.Rounds.RemoveAt(remove);
                }
                next.Rounds.Add(new Round { Run = run, Match = match, Seed = seed, Red = red, Odds = odds, Created = DateTime.UtcNow.ToString("O") });
                return Persist(next);
            }
            string error;
            return !found.Resolved && found.Seed == seed && engine.RequestFlush(out error);
        }
        internal bool AlignReservedBet(string run, int match, ModeHCashBetRecord bet)
        {
            if (bet == null) return true;
            if (bet.status != ModeHCashBetService.StatusReserved || bet.runId != run || bet.matchIndex != match
                || (bet.betSide != ModeHCashBetService.BetSideBlue && bet.betSide != ModeHCashBetService.BetSideRed)) return false;
            Data next = Copy(); Round round = Find(next, run, match);
            if (round == null || round.Resolved) return false;
            bool red = bet.betSide == ModeHCashBetService.BetSideRed;
            if (round.Red != red || round.Odds != bet.odds)
            {
                round.Red = red; round.Odds = bet.odds;
                return Persist(next);
            }
            string error; return engine.RequestFlush(out error);
        }
        internal bool Forfeit(string run, int match)
        {
            Data next = Copy(); Round round = Find(next, run, match);
            if (round == null) return true;
            if (round.Resolved) return true;
            round.Forfeited = true; return Persist(next);
        }
        internal bool Apply(ModeHCoopResult result, bool host)
        {
            if (result == null || string.IsNullOrEmpty(result.Token) || result.Token.Length > 256
                || result.Winner < 0 || result.Winner > 2 || result.Final != (result.Match == ModeHConfig.SeasonMatchCount)) return false;
            Data next = Copy(); Round round = Find(next, result.Run, result.Match);
            if (round == null || result.Seed != round.Seed) return false;
            if (round.Resolved && (round.Token != result.Token || round.Winner != result.Winner || round.Final != result.Final)) return false;
            bool won = !round.Forfeited && result.Winner == (round.Red ? ModeHGroupBattle.WinnerRed : ModeHGroupBattle.WinnerBlue);
            ModeHCashBetRecord bet = ModeHCashBetService.Current;
            if (bet != null && bet.runId == result.Run && bet.matchIndex == result.Match)
            {
                if (bet.status == ModeHCashBetService.StatusReserved)
                {
                    bool settled;
                    if (bet.kind == ModeHCashBetService.KindItems)
                        settled = ModeHCashBetService.TrySettleItems(result.Run, result.Match, won, result.Seed);
                    else
                    {
                        long payout;
                        settled = ModeHCashBetService.TrySettle(result.Run, result.Match, won, 0L, 0L, string.Empty, out payout);
                    }
                    if (!settled) return false;
                    bet = ModeHCashBetService.Current;
                }
                if (bet == null || bet.status == ModeHCashBetService.StatusReserved) return false;
                round.Net = bet.payout - bet.amount;
            }
            if (!round.Resolved)
            {
                round.Resolved = true; round.Token = result.Token; round.Winner = result.Winner; round.Final = result.Final;
                if (!Persist(next)) return false;
            }
            else { string error; if (!engine.RequestFlush(out error)) return false; }
            if (result.Final && !host) return InsertHall(next, result.Run);
            return true;
        }
        private bool InsertHall(Data data, string run)
        {
            int wins = 0, matches = 0, maxOdds = 0; long net = 0;
            string created = null;
            List<string> tokens = new List<string>();
            foreach (Round round in data.Rounds)
            {
                if (round.Run != run || !round.Resolved) continue;
                matches++; tokens.Add(round.Token ?? string.Empty);
                net = ModeHCashBetService.SaturatingAdd(net, round.Net);
                if (!round.Forfeited && round.Winner == (round.Red ? ModeHGroupBattle.WinnerRed : ModeHGroupBattle.WinnerBlue))
                { wins++; maxOdds = Math.Max(maxOdds, round.Odds); }
                if (created == null || string.CompareOrdinal(round.Created, created) < 0) created = round.Created;
            }
            string error;
            return ModeHSaveFlushCoordinator.RequestHallOfFameInsert(new ModeHHallOfFameRecordDto
            {
                hallOfFameId = "hof|" + run, schemaVersion = ModeHConfig.CurrentSchemaVersion,
                seasonVersion = ModeHConfig.CurrentSchemaVersion, archetypeId = ModeHGroupHallOfFame.ArchetypeTag,
                quirkId = ModeHGroupHallOfFame.EncodeScore(wins, matches, net), createdUtc = created ?? string.Empty,
                aliasKey = string.Empty, temperamentId = string.Empty, anomalyId = string.Empty, signatureCommandId = string.Empty,
                scarIds = new List<string>(), matchReportIds = tokens, substituteHistory = new List<string>(),
                maxOddsWin = maxOdds, finalVirtualStakeCredits = wins, gameBuildSignature = string.Empty, modBuildSignature = string.Empty
            }, out error, true);
        }
        internal string Describe(string run, int match)
        {
            Round round = Find(store.Current, run, match);
            if (round == null) return L10n.T("本场观战，下一场可参与押注。", "Spectating this match; you can bet next round.");
            if (!round.Resolved) return L10n.T("等待比赛结果和个人结算。", "Waiting for the match result and your settlement.");
            bool won = !round.Forfeited && round.Winner == (round.Red ? ModeHGroupBattle.WinnerRed : ModeHGroupBattle.WinnerBlue);
            return L10n.T(won ? "本场押中。净收益：" : "本场未押中。净收益：", won ? "Your pick won. Net: " : "Your pick lost. Net: ") + round.Net;
        }
        private bool Persist(Data data)
        { string error; return store.Store(data) && engine.RequestFlush(out error); }
        internal void Tick() { engine.Tick(); }
        public void Dispose()
        { string error; engine.RequestFlush(out error, true); store.ResetAll(); engine.Reset(); }
        string IBossRushSaveBatchSource.LogPrefix { get { return "[ModeH Coop] "; } }
        bool IBossRushSaveBatchSource.HasPendingWrite { get { return store.HasPendingWrite; } }
        bool IBossRushSaveBatchSource.IsStoreFaulted { get { return store.IsStoreFaulted; } }
        bool IBossRushSaveBatchSource.HasSnapshotObligation { get { return false; } }
        string IBossRushSaveBatchSource.LastError { get { return store.LastError; } }
        bool IBossRushSaveBatchSource.CollectSnapshot(out string error) { error = null; return true; }
        bool IBossRushSaveBatchSource.FlushPending() { return store.FlushPending(); }
        void IBossRushSaveBatchSource.OnPhysicalSaveSucceeded() { }
    }
}
