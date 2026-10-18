"""Corrupt private source copies; never mutate a shared production file."""
from pathlib import Path
import hashlib,json,os,shutil,subprocess,sys,tempfile
ROOT=Path(__file__).resolve().parents[3];HERE=Path(__file__).resolve().parent

def main():
 source=ROOT/'SkyIsland/CoopWaylayerClaimUseHooks.cs';raw=source.read_bytes();text=raw.decode('utf-8-sig')
 work=Path(tempfile.mkdtemp(prefix='negative-',dir=ROOT/'Build/runtime-regressions/WaylayerClaimUse'));rows=[]
 expected={'StartUse':'non-main native start also rejects','Usable':'locked original not usable at UI query','Use':'direct Item.Use rejects before user events','UtilitiesUse':'direct Utilities.Use rejects effects and completion events','BehaviorUse':'direct Behavior.Use rejects effect','FinishUse':'actual finish cannot consume locked original'}
 for name,failure in expected.items():
  start=text.index('internal static class CoopWaylayerClaim'+name+'Hook');end=text.index('\n    }',start);segment=text[start:end]
  if name=='Usable':old='{ if (!CoopWaylayerClaims.Locked(item)) return true; __result = false; return false; }';new='{ return true; }'
  else:old='return !CoopWaylayerClaims.Locked('+('__instance' if name=='Use' else '___item' if name=='FinishUse' else 'item')+');';new='return true;'
  assert segment.count(old)==1
  altered=work/(name+'.cs');altered.write_text(text[:start]+segment.replace(old,new)+text[end:],encoding='utf-8')
  ran=subprocess.run([sys.executable,str(HERE/'run.py')],cwd=ROOT,env=dict(os.environ,WAYLAYER_USE_SOURCE=str(altered)),stdout=subprocess.PIPE,stderr=subprocess.STDOUT)
  output=ran.stdout.decode('utf-8','replace');(work/(name+'.log')).write_text(output,encoding='utf-8');rows.append(dict(name=name,kind='full-official-behavior',exit=ran.returncode,expected=failure,matched=ran.returncode!=0 and failure in output))
 guard=ROOT/'tests/CoopWaylayerClaimUseGuard.py'
 for name,old,new,failure in [('target','typeof(CA_UseItem), "OnFinish"','typeof(CA_UseItem), "OnStart"','exact native target'),('field','Prefix(Item ___item)','Prefix(Item item)','native argument/field contract'),('compile','echo(SkyIsland\\CoopWaylayerClaimUseHooks.cs','echo(SkyIsland\\CoopWaylayerClaims.cs','official compile registration')]:
  isolated=work/name
  for folder in ('SkyIsland','tests','tools'):(isolated/folder).mkdir(parents=True,exist_ok=True)
  for item in (source,ROOT/'SkyIsland/CoopWaylayerClaims.cs',ROOT/'compile_official.bat',ROOT/'tools/run_runtime_regressions.py',guard,ROOT/'tests/cs_source_util.py'):
   destination=isolated/item.relative_to(ROOT);payload=item.read_text(encoding='utf-8-sig')
   if (name=='compile' and item.name=='compile_official.bat') or (name!='compile' and item==source):assert payload.count(old)==1;payload=payload.replace(old,new)
   destination.write_text(payload,encoding='utf-8')
  ran=subprocess.run([sys.executable,str(isolated/'tests'/guard.name)],stdout=subprocess.PIPE,stderr=subprocess.STDOUT);output=ran.stdout.decode('utf-8','replace');(work/(name+'.log')).write_text(output,encoding='utf-8');rows.append(dict(name=name,kind='structure-guard',exit=ran.returncode,expected=failure,matched=ran.returncode!=0 and failure in output))
 restored=subprocess.run([sys.executable,str(HERE/'run.py')],cwd=ROOT,stdout=subprocess.PIPE,stderr=subprocess.STDOUT);(work/'restored.log').write_bytes(restored.stdout)
 result=dict(source_sha256=hashlib.sha256(raw).hexdigest(),source_unchanged=source.read_bytes()==raw,cases=rows,restored_exit=restored.returncode,all_expected=all(r['matched'] for r in rows) and restored.returncode==0 and source.read_bytes()==raw)
 (work/'summary.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
 for row in rows:print(('PASS' if row['matched'] else 'FAIL')+' '+row['name'])
 print('Negative evidence: '+str(work/'summary.json'));return 0 if result['all_expected'] else 1
if __name__=='__main__':raise SystemExit(main())
