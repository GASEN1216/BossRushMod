"""执行生产龙皇碰撞、光束与岩浆入口；物理查询/接收体是可控宿主边界。"""
from pathlib import Path
import hashlib
import json
import sys
import tempfile
from xml.sax.saxutils import escape

ROOT=Path(__file__).resolve().parents[3]
HERE=Path(__file__).resolve().parent
sys.path.insert(0,str(ROOT/"tools"))
from run_runtime_regressions import run_project_fixture

def member(source,signature):
    assert source.count(signature)==1,signature
    start=source.index(signature); opening=source.index("{",start);depth=0
    for end in range(opening,len(source)):
        depth+=(source[end]=="{")-(source[end]=="}")
        if depth==0:return source[start:end+1]
    raise AssertionError(signature)

def main():
    out=ROOT/"Build/runtime-regressions/DragonKingCoopWorld";out.mkdir(parents=True,exist_ok=True)
    run=Path(tempfile.mkdtemp(prefix="source-",dir=out))
    paths=[ROOT/"Integration/DragonKing"/name for name in ("DragonKingAbilityHelpers.cs","DragonKingAbilityController_SpecialAttacks.cs","DragonKingAbilityController_ProjectileAndMovement.cs","DragonKingShockwaveEffect.cs","DragonKingCoopContactRuntimeModule.cs","DragonKingLavaZone.cs")]
    helper,special,movement,shockwave,contact,lava_source=[p.read_text(encoding="utf-8-sig") for p in paths]
    generated="using System; using System.Collections; using System.Collections.Generic; using UnityEngine; using Duckov.Buffs; namespace BossRush {\n"
    generated+=member(contact,"internal sealed class CoopBossCreationLease")+"\n"
    generated+=member(contact,"internal sealed class DragonKingCoopContactRuntimeModule")+"\n"
    generated+=member(helper,"public class DragonKingCollisionDetector")+"\n"+member(helper,"public class SunBeamDamageTrigger")+"\n"
    lava=member(lava_source,"public class DragonKingLavaZone")
    generated+="internal sealed partial class DragonKingLavaZone {\n"
    for signature in ("private sealed class PlayerZoneGeometry", "private void TickActiveZone(", "private void ApplyLavaDamage(",
                      "private void TickActiveZoneAgainstEnemies(", "private bool IsMainPlayerInsideZone(", "private bool IsCoopPlayerInsideZone(",
                      "private static void UpdateActiveZones(", "private static bool TryGetMainPlayer(", "private static void RefreshMainPlayerCache(",
                      "private static void UnregisterZone(", "private void ResetRuntimeState("):
        generated+=member(lava,signature)+"\n"
    generated+=member(lava,"internal static void ResetStaticCaches()")+"\n"
    generated+="}\npublic sealed partial class DragonKingAbilityController {\n"
    generated+=member(special,"public void OnCollisionWithPlayer(")+"\n"
    generated+=member(special,"internal bool HasCombatAuthority")+"\n"
    generated+=member(special,"private void ApplyKnockback(")+"\n"
    generated+=member(special,"internal void ApplySunBeamDamage(CharacterMainControl target)")+"\n"
    generated+=member(movement,"private void ApplyDamageToPlayer(float damage, CharacterMainControl target)")+"\n"
    generated+=member(movement,"private bool CheckProjectileHit(Vector3 position, float damage, bool hasTargetPosition,")+"\n"
    generated+=member(special,"private bool CheckLanceHit(GameObject lance, bool hasTargetSnapshot,")+"\n"
    generated+=member(special,"private bool CheckLanceTarget(")+"\n}\ninternal sealed partial class DragonKingShockwaveEffect {\n"
    for signature in ("private void FindPlayer()", "private void CaptureWaveScope()", "private bool IsWaveScopeValid()", "private void KnockbackPlayer(", "private IEnumerator ClearKnockbackAfterDelay()"):
        generated+=member(shockwave,signature)+"\n"
    generated+="}\n"
    phantom_paths=[ROOT/"Integration/PhantomWitch"/name for name in ("PhantomWitchAbilityController_MovementAndDamage.cs","PhantomWitchBossCurseRealmRuntime.cs")]
    phantom,realm=[p.read_text(encoding="utf-8-sig") for p in phantom_paths]
    generated+="internal sealed partial class PhantomWitchAbilityController {\n"+member(phantom,"internal static int QueryDamageReceivers(")+"}\n"
    generated+="internal sealed partial class PhantomWitchBossCurseRealmRuntime {\n"+member(realm,"private void DealRealmDamage()")+"}\n}\n"
    descendant_paths=[ROOT/"Integration/DragonDescendant"/name for name in ("DragonDescendantAbilities.cs","DragonDescendantAbilities_CollisionAndIce.cs")]
    descendant,collision=[p.read_text(encoding="utf-8-sig") for p in descendant_paths]
    generated=generated[:-2]+"public sealed partial class DragonDescendantAbilityController {\n"
    generated+=member(descendant,"internal bool HasCombatAuthority")+"\n"
    generated+=member(collision,"private void ApplyCollisionDamage(CharacterMainControl player)")+"\n}\n}\n"
    paths+=descendant_paths
    paths+=phantom_paths+[ROOT/"Utilities/CoopPersonalDelivery.cs",ROOT/"Common/Coop/BossRushCoopJson.cs",ROOT/"Common/Data/BossRushJsonValue.cs",ROOT/"Utilities/SimpleJsonHelper.cs"]
    (run/"Production.cs").write_text(generated,encoding="utf-8")
    (run/"production-sha256.json").write_text(json.dumps({str(p):hashlib.sha256(p.read_bytes()).hexdigest() for p in paths},indent=2),encoding="utf-8")
    files=[run/"Production.cs",HERE/"Stubs.cs",HERE/"Program.cs",HERE/"ContactRegression.cs",
           ROOT/"Utilities/CoopPersonalDelivery.cs",ROOT/"Common/Coop/BossRushCoopJson.cs",ROOT/"Common/Data/BossRushJsonValue.cs",ROOT/"Utilities/SimpleJsonHelper.cs"]
    project='<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup>'
    project+=''.join('<Compile Include="'+escape(str(p))+'" />' for p in files)+'</ItemGroup></Project>'
    (run/"Regression.csproj").write_text(project,encoding="utf-8")
    code,output=run_project_fixture(run/"Regression.csproj",run/"execution",ROOT);print(output);return code

if __name__=="__main__":raise SystemExit(main())
