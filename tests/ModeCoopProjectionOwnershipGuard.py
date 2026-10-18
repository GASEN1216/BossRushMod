"""Retire a world projection when its original native client room disappears."""
from pathlib import Path
from cs_source_util import clean_source
from ModeHOneClickFlowGuard import method_body, squeeze

def read(path): return clean_source(Path(path).read_text(encoding="utf-8-sig"))
def main():
    errors=[]
    def require(ok,label):
        if not ok: errors.append(label)
    for mode,cleanup in (("D","CleanupModeDRuntimeOnDestroy();"),("E","EndModeE(false);"),("F","ExitModeF(false);")):
        source=read(f"Mode{mode}/Mode{mode}RuntimeModule_Coop.cs")
        retire=squeeze(method_body(source,"private void RetireLostCoopProjection("))
        tick=squeeze(method_body(source,f"private void TickCoopMode{mode}("))
        world=squeeze(method_body(source,f"private void ApplyCoopMode{mode}("))
        require("coopProjectionRoom == null || (BossRushCoop.IsClient && coopProjectionRoom == BossRushCoop.RunId)" in retire,mode+" projection must retain original client role and room")
        require(cleanup in retire and "finally { coopProjectionRetiring = false; coopProjectionRoom = null; }" in retire,mode+" old projection must use existing cleanup and retire its owner")
        require(tick.index("RetireLostCoopProjection();") < tick.index("coopChannel.Tick("),mode+" projection cleanup must precede channel and offline world work")
        require("if (BossRushCoop.IsClient && state.Active) coopProjectionRoom = BossRushCoop.RunId;" in world,mode+" receiving world must freeze its client room")
    arena=read("WavesArena/WavesArenaRuntimeModule_Coop.cs")
    retire=squeeze(method_body(arena,"private void RetireLostCoopArenaProjection("))
    require("BossRushCoop.IsClient && coopArenaProjectionRoom == BossRushCoop.RunId" in retire and "waveGeneration++;" in retire and "SetBossRushRuntimeActive(false);" in retire and "CurrentWaveBosses.Clear(); CurrentBoss = null;" in retire,"Arena abort must invalidate pending waves and retire native replicas")
    entry=squeeze(method_body(arena,"internal IEnumerator SetupCoopClientEntry("))
    require(entry.count("BossRushCoop.IsClient && BossRushCoop.RunId == room") == 2 and entry.count("GetActiveScene().handle == scene.handle") == 2,"deferred client entry must retain original room/role and actual scene handle")
    require("if (!BossRushCoop.IsClient || BossRushCoop.RunId != room || !scene.isLoaded || SceneManager.GetActiveScene().handle != scene.handle) yield break;" in entry, "retired client entry must not clear a newer same-scene prepaid flow")
    runtime=read("WavesArena/WavesArenaRuntimeModule.cs")
    require("RetireLostCoopArenaProjection();" in method_body(runtime,"public override void OnUpdate("),"Arena runtime must retire old projection before world rewards")
    z=read("ZombieMode/ZombieModeRuntimeModule_Coop.cs")
    retire=squeeze(method_body(z,"private void RetireLostCoopZombieProjection("))
    tick=squeeze(method_body(z,"public override void OnUpdate("))
    require("BossRushCoop.IsClient && coopZombieProjectionRoom == BossRushCoop.RunId" in retire and "owner.CleanupZombieModeForRuntimeModule(" in retire and "coopZombieAdmissionPending = false;" in retire and "coopZombieLocalRun = 0;" in retire,"Zombie old projection must abort original run and pending admission")
    require(tick.index("RetireLostCoopZombieProjection();") < tick.index("coopZombieWorld.Tick("),"Zombie projection must abort before channel and offline phase drive")
    world=squeeze(method_body(z,"private void ApplyCoopZombieWorld("))
    require("string admissionRoom = BossRushCoop.RunId, admissionToken = BossRushCoop.SceneToken;" in world
        and world.count("!BossRushCoop.IsClient || BossRushCoop.RunId != admissionRoom || BossRushCoop.SceneToken != admissionToken") == 2
        and world.count("coopZombieLocalRun != state.Run || runState.RunId != state.Run || !coopZombieAdmissionPending") == 2,
        "late cash prompt callbacks must freeze room, scene and pending business transaction")
    lease=squeeze(method_body(z,"private bool IsCoopZombieRunLeaseCurrent("))
    capture=squeeze(method_body(z,"private void CaptureCoopZombieRunLease("))
    require("coopZombieLeaseConnected = BossRushCoop.IsConnected;" in capture
        and "coopZombieLeaseHost = BossRushCoop.IsHost;" in capture and "coopZombieLeaseRoom = BossRushCoop.RunId;" in capture,
        "Zombie run lease must capture connection, role and original room")
    require("if (coopZombieLeaseRevoked) return false;" in lease and "coopZombieLeaseRevoked = true;" in lease
        and "coopZombieLeaseConnected != BossRushCoop.IsConnected" in lease
        and "coopZombieLeaseHost != BossRushCoop.IsHost || coopZombieLeaseRoom != BossRushCoop.RunId" in lease,
        "Zombie lost run lease must remain permanently revoked across return to an old connection")
    runtime=read("ZombieMode/ZombieModeRuntimeModule.cs")
    require("!IsCoopZombieRunLeaseCurrent()" in method_body(runtime,"internal bool IsZombieModeRunValid("),
        "Zombie async validity must reject retired run before next Update")
    phase_tick=squeeze(method_body(runtime,"internal void TickZombieMode("))
    require(phase_tick.index("RetireLostCoopZombieProjection();") < phase_tick.index("owner.TickZombieModeWaveControllerForRuntimeModule("),
        "Zombie run retirement must precede world phase work")
    h=read("ModeH/ModeHRuntimeModule_Coop.cs")
    tick=squeeze(method_body(h,"private void TickCoop("))
    release=squeeze(method_body(h,"private void ReleaseCoopProjection("))
    require(tick.index("_coopProjection && (!BossRushCoop.IsClient || _coopProjectionRoom != BossRushCoop.RunId)") < tick.index("_coopLedger.Tick();") and "_coopProjectionRoom = null;" in release,"H native room/role loss must release projection before personal and world tick")
    for path in ("ModeD/ModeDRuntimeModule_Lifecycle.cs","ModeE/ModeELifecycle.cs","ModeF/ModeFPhases.cs"):
        require("!BossRushCoop.HasWorldAuthority && !coopProjectionRetiring" in read(path),path+" must retire only its old replicas even while new native room is client")
    if errors: print("ModeCoopProjectionOwnershipGuard: FAIL\n"+"\n".join(errors));return 1
    print("ModeCoopProjectionOwnershipGuard: PASS");return 0
if __name__=="__main__":raise SystemExit(main())
