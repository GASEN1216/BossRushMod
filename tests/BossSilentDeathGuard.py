"""已登记 Boss 漏收 OnDead 的兜底必须保留正常结案与联机个人事实。"""
from pathlib import Path
import re
import sys
from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parents[1]


def read(path):
    return clean_source((ROOT / path).read_text(encoding='utf-8-sig'))


def body(source, signature):
    start = source.index(signature)
    opening = source.index('{', start)
    depth = 0
    for index in range(opening, len(source)):
        depth += (source[index] == '{') - (source[index] == '}')
        if depth == 0:
            return re.sub(r'\s+', '', source[opening:index + 1])
    raise ValueError(signature)


def main():
    errors = []
    def require(value, reason):
        if not value:
            errors.append(reason)
    g = read('ModeG/ModeGRuntimeModule_SilentDeathSweep.cs')
    sweep = body(g, 'private void SweepSilentBossDeaths()')
    require('if(!CoopRunHasWorldAuthority||!CanContinueRun()' in sweep, 'Mode G 兜底必须限制原世界 owner')
    require('_waveSpawnInFlight||_batchActivationInProgress||_spawnTransaction.ActiveBossCount==0)return;' in sweep
            and 'if(_state.combatPhase!=ModeGCombatPhase.Fighting&&_state.combatPhase!=ModeGCombatPhase.LastStand)return;' in sweep,
            'Mode G 兜底只处理战斗 / 最后处决的已提交 Boss')
    require('DeadSweepIntervalSeconds=0.25f;' in re.sub(r'\s+', '', g)
            and 'if(now<_nextDeadSweepTime)return;' in sweep, 'Mode G 死亡扫描须有 0.25 秒预算')
    publish = sweep.find('ModeGEntryRuntime.PublishCoopCombat(health,default(DamageInfo),true);')
    settle = sweep.find('HandleBossDead(health,default(DamageInfo));')
    require(0 <= publish < settle, 'Mode G 注销 Health 前必须先发送现有个人 Boss 死亡事实')
    require('dead=health==null||health.IsDead;' in sweep and 'if(settled&&_spawnTransaction.ActiveBossCount==0)_waveSettlementPending=true;' in sweep,
            'Mode G 已死 / 已销毁引用必须结案并在清零时排入正常结算')
    drive = body(read('ModeG/ModeGRuntimeModule.cs'), 'private void DriveCore(float deltaTime)')
    require(0 <= drive.find('SweepSilentBossDeaths();') < drive.find('SettleCurrentWave();'), 'Mode G 实际 tick 必须先补死亡再消费波结算')
    coop = read('ModeG/ModeGCoopRuntime.cs')
    capture = body(coop, 'internal ModeGCoopWorld CaptureCoopWorld()')
    require('RememberCoopBossIdentity(id,character.Health);' in capture,
            'Mode G 发布原生 AI 时必须冻结 exact Health 身份供销毁后结案')
    publish = body(coop, 'internal static void PublishCoopCombat(')
    require('ReferenceEquals(health,null)' in publish and 'run.ResolveCoopBossIdentity(health)' in publish,
            'Mode G 发布个人死亡必须容许已销毁的已登记原 Health')
    identity = body(coop, 'internal int ResolveCoopBossIdentity(')
    require('_state.IsRegisteredBossHealth(health)' in identity and 'coopBossGeneration!=BossRushCoop.SceneGeneration' in identity
            and 'ReferenceEquals(pair.Value,health)' in identity, 'Mode G 冻结 AI 身份必须同时核对登记、场景代次和 exact Health')

    waves = read('WavesArena/WavesArenaRuntimeModule_WaveDeaths.cs')
    sweep = body(waves, 'internal void SweepSilentWaveBossDeaths(float deltaTime)')
    require('if(!BossRushCoop.HasWorldAuthority||!owner.IsActive||owner.IsModeDActive)return;' in sweep,
            'Waves 兜底只能由世界 owner 在标准模式驱动')
    require('CurrentWaveBosses[i]asCharacterMainControl' in sweep and 'CurrentBossasCharacterMainControl' in sweep
            and 'HandleBossDeath(silentBossDeathScratch[i],default(DamageInfo));' in sweep,
            'Waves 只能扫描本波成员并回到正常 HandleBossDeath')
    require('ProceedAfterWaveFinished' not in sweep and 'BossesInCurrentWaveRemaining' not in sweep,
            'Waves 兜底不得自行改账推波')
    dead = body(waves, 'private bool IsSilentlyDeadWaveBoss(CharacterMainControl boss)')
    require('CountedDeadBosses.Contains(boss)' in dead and 'health.IsDead' in dead, 'Waves 只认已死未记账成员')
    tick = body(read('WavesArena/WavesArenaRuntimeModule_Tick.cs'), 'internal bool TickWavesArenaRuntime(float deltaTime)')
    require(0 <= tick.find('SweepSilentWaveBossDeaths(deltaTime);') < tick.find('owner.TryFixStuckWaveIfNoBossAlive();'),
            'Waves 实际 tick 必须在完整性自检前完成死亡补账')
    for error in errors:
        print('BossSilentDeathGuard: FAIL - ' + error)
    if not errors:
        print('BossSilentDeathGuard: PASS')
    return 1 if errors else 0


if __name__ == '__main__':
    sys.exit(main())
