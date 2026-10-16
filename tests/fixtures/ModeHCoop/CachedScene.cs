using System;
namespace BossRush
{
    static class ModeHItemBetStake { internal static bool HasSelection; internal static int Releases; internal static void ReleaseLocked(){Releases++;} }
    partial class ModeHRuntimeModule
    {
        private bool _groupLockedBetOnRed,_groupBetOnRed,_showItemBetPicker;
        private float _coopPrepareTimer;
        private int ResolveGroupOdds(bool red) { return 2; }
        private int LockedGroupBetSide() { return _groupLockedBetOnRed ? 2 : 1; }
        private ModeHCashBetRecord CarriedGroupBet() { return ModeHCashBetService.ReservedFor(_runState.RunId,GroupMatchIndex); }
        private void ReserveItemBet(int odds) { throw new Exception("No item selection in this cash boundary case"); }
        private void ForfeitStartedCashBet(ModeHCashBetRecord value,string reason) { value.status=2; }
        private void RefundCashBet(string reason) { ModeHCashBetService.Current=null; }
        internal void CachedProjection(string run)
        {
            BeginProjection();_runState=new Run {RunId=run};
            _coopView=new ModeHCoopWorld {Run=run,Round=run+":1",Requested=new[]{BossRushCoop.LocalPlayerId}};
        }
        internal void PrepareCached() { TryPrepareCoopBet(1f,_coopView.Round); }
        internal void ConfirmCachedFunding() { _coopView.Funded=new[]{BossRushCoop.LocalPlayerId}; }
        internal void ReadyCached() { RequestCoopStart(); }
        internal int WorldRequests {get {return _coopChannel.Requests;} }
        internal bool HasPreparedCached { get {return _coopLedger.Owns(_runState.RunId,GroupMatchIndex);} }
    }
    static class CachedSceneRegression
    {
        internal static void Run(Action<bool,string> check)
        {
            BossRushCoop.IsConnected=true;BossRushCoop.IsClient=true;BossRushCoop.IsHost=false;
            BossRushCoop.RunId="cached-h-room";BossRushCoop.HasBoundScene=true;BossRushCoop.SceneToken="core-31";
            ModeHCashBetService.Current=null;ModeHCashBetService.StandingAmount=25;ModeHCashBetService.Reserves=0;
            var cached=new ModeHRuntimeModule();cached.CachedProjection("cached-h-season");
            BossRushCoop.HasBoundScene=false;cached.Tick();
            check(!cached.HasPreparedCached&&ModeHCashBetService.Reserves==0,"H actual Tick cannot prepare or reserve from cached requested roster while Core is unbound");
            check(cached.SpectatorTicks==0,"H actual Tick cannot drive spectator from cached old-world IDs while Core is unbound");
            cached.PrepareCached();check(!cached.HasPreparedCached&&ModeHCashBetService.Reserves==0,"H direct delayed prepare callback also refuses unbound cached world");
            cached.ReadyCached();check(cached.WorldRequests==0,"H queued ready cannot enqueue cached old round while Core is unbound");
            BossRushCoop.HasBoundScene=true;BossRushCoop.SceneToken="core-32";cached.Tick();cached.PrepareCached();
            check(!cached.HasPreparedCached&&ModeHCashBetService.Reserves==0,"H stale projection cannot prepare after token changed even if generation has not advanced");
            cached.CachedProjection("cached-h-season");cached.Tick();
            check(cached.HasPreparedCached&&ModeHCashBetService.Reserves==1&&ModeHCashBetService.Current.amount==25,"H current snapshot retains real production ledger preparation and full requested cash reservation");
            cached.ConfirmCachedFunding();int confirmedRequests=cached.WorldRequests;for(int i=0;i<100;i++)cached.PrepareCached();
            check(cached.WorldRequests==confirmedRequests&&ModeHCashBetService.Reserves==1,"H host-funded snapshot stops stable periodic reports without another charge");
            BossRushCoop.HasBoundScene=false;cached.Tick();cached.PrepareCached();
            check(cached.HasPreparedCached&&ModeHCashBetService.Reserves==1&&ModeHCashBetService.Current.status==ModeHCashBetService.StatusReserved,"H scene gap retains paid ledger and original reservation without recharging or resolving it");
            ModeHCashBetService.StandingAmount=0;
        }
    }
}
