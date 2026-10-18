"""Preserve exact-item publication barriers at all native consumption entries."""
from pathlib import Path
import re
from cs_source_util import clean_source
ROOT=Path(__file__).resolve().parents[1]

def main():
 source=clean_source((ROOT/'SkyIsland/CoopWaylayerClaimUseHooks.cs').read_text(encoding='utf-8-sig'))
 compact=lambda text:re.sub(r'\s+',' ',text).strip()
 rows=(
 ('StartUse','CharacterMainControl','UseItem','new Type[] { typeof(Item) }','Item item','item'),
 ('Usable','UsageUtilities','IsUsable','new Type[] { typeof(Item), typeof(object) }','Item item, ref bool __result','item'),
 ('Use','Item','Use','new Type[] { typeof(object), typeof(bool) }','Item __instance','__instance'),
 ('UtilitiesUse','UsageUtilities','Use','new Type[] { typeof(Item), typeof(object), typeof(bool) }','Item item','item'),
 ('BehaviorUse','UsageBehavior','Use','new Type[] { typeof(Item), typeof(object) }','Item item','item'),
 ('FinishUse','CA_UseItem','OnFinish',None,'Item ___item','___item'))
 for name,owner,method,args,parameters,item in rows:
  attribute='[HarmonyPatch(typeof('+owner+'), "'+method+'"'+(', '+args if args else '')+')]'
  name='CoopWaylayerClaim'+name+'Hook'
  pattern=re.escape(attribute)+r'\s+internal static class '+name+r'\s*\{(.*?)\n    \}'
  matches=re.findall(pattern,source,re.S)
  assert len(matches)==1,'exact native target: '+name
  block=compact(matches[0]);signature='[HarmonyPrefix] private static bool Prefix('+parameters+')'
  assert block.startswith(signature),'native argument/field contract: '+name
  expected=('{ if (!CoopWaylayerClaims.Locked(item)) return true; __result = false; return false; }' if method=='IsUsable' else '{ return !CoopWaylayerClaims.Locked('+item+'); }')
  assert block==signature+' '+expected,'exact instance publication gate: '+name
 assert source.count('[HarmonyPatch(')==6 and source.count('[HarmonyPrefix]')==6,'six narrow native gates only'
 claims=clean_source((ROOT/'SkyIsland/CoopWaylayerClaims.cs').read_text(encoding='utf-8-sig'))
 assert 'internal static bool Locked(Item item) { return item != null && !string.IsNullOrEmpty(item.GetString(CoopWaylayerJournal.ClaimLockKey)); }' in compact(claims),'shared lock must remain exact marker O(1) query'
 assert 'echo(SkyIsland\\CoopWaylayerClaimUseHooks.cs' in (ROOT/'compile_official.bat').read_text(encoding='utf-8-sig'),'official compile registration'
 assert '"WaylayerClaimUse"' in (ROOT/'tools/run_runtime_regressions.py').read_text(encoding='utf-8-sig'),'actual-native fixture registration'
 print('CoopWaylayerClaimUseGuard: PASS')
if __name__=='__main__':
 try:main()
 except AssertionError as error:print('CoopWaylayerClaimUseGuard: FAIL - '+str(error));raise SystemExit(1)
