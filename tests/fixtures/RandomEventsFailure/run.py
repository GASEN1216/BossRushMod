#!/usr/bin/env python3
"""Execute exact production RandomEventDirector.TickEventActive and async callbacks in a host stub."""
from pathlib import Path
import subprocess, sys, re
ROOT=Path(__file__).resolve().parents[3]; HERE=Path(__file__).resolve().parent; OUT=ROOT/'Build/random-events-failure'

def method(src, marker):
    start=src.index(marker); opening=src.index('{',start); depth=0
    for i in range(opening,len(src)):
        if src[i]=='{': depth+=1
        elif src[i]=='}':
            depth-=1
            if depth==0:return src[start:i+1]
    raise ValueError(marker)

def main():
    OUT.mkdir(parents=True,exist_ok=True)
    director=(ROOT/'RandomEvents/RandomEventDirector.cs').read_text(encoding='utf-8-sig')
    catalog=(ROOT/'RandomEvents/RandomEventCatalog.cs').read_text(encoding='utf-8-sig')
    tick=method(director,'private void TickEventActive(float dt)')
    intruder_valid=method(catalog,'private bool IsSpawnStillValid(RandomEventContext ctx)')
    intruder_fail=method(catalog,'private void HandleIntruderSpawnFailed(RandomEventContext ctx)')
    merchant_valid=method(catalog,'private bool IsSpawnStillValid(RandomEventContext ctx)')
    merchant_fail=method(catalog,'private void HandleMerchantSpawnFailed(RandomEventContext ctx)')
    # The two IsSpawnStillValid methods have identical production text; extract once.
    generated='''using System;\nnamespace UnityEngine.SceneManagement { public static class SceneManager { public static Scene GetActiveScene(){ return new Scene(); } public static int CurrentBuild; } public struct Scene { public int buildIndex { get { return SceneManager.CurrentBuild; } } } }\nnamespace BossRush {\nusing UnityEngine.SceneManagement;\npublic static class RandomEventsTuning { public const float MaxDeltaPerFrame=1f; public const string LogPrefix=""; }\npublic enum RandomEventEndReason { TriggerFailed, Expired }\npublic sealed class RandomEventContext { public float ElapsedSeconds, DurationSeconds; }\npublic abstract class RandomEventBase { public virtual bool HasFailedToStart { get { return false; } } public virtual void OnTick(RandomEventContext c,float d){} }\npublic static class ModBehaviour { public static void DevLog(string s){} }\npublic sealed class DirectorProbe {\nprivate RandomEventContext _activeContext; private RandomEventBase _activeEvent; private bool _activeTickFaulted; private bool _activeEventCounted; private int _eventsFiredThisRun;\npublic int Events { get { return _eventsFiredThisRun; } } public bool Cleaned { get; private set; } public bool Cooldown { get; private set; }\npublic void Start(RandomEventBase e,bool natural){ _activeEvent=e; _activeContext=new RandomEventContext{DurationSeconds=120f}; _activeEventCounted=natural; _eventsFiredThisRun=natural?1:0; Cleaned=false; Cooldown=false; }\n''' + tick.replace('private void TickEventActive(float dt)','public void TickEventActive(float dt)') + '''\nprivate void EndActiveEvent(RandomEventEndReason r){ Cleaned=true; _activeEvent=null; _activeContext=null; _activeEventCounted=false; }\nprivate void EnterCooldown(){ Cooldown=true; } private void LogFailure(string s,Exception e){}\n}\npublic sealed class IntruderProbe { private bool _cleanedUp; private int _sceneBuildIndex; private RandomEventContext _spawnContext; private int _pendingSpawns; private int _spawnFailures; public int Failures { get{return _spawnFailures;} } public int Pending {get{return _pendingSpawns;}} public void Start(RandomEventContext c){_cleanedUp=false;_spawnContext=c;_sceneBuildIndex=0;_pendingSpawns=1;} public void Cleanup(){_cleanedUp=true;_spawnContext=null;} ''' + intruder_fail.replace('private void HandleIntruderSpawnFailed(RandomEventContext ctx)','public void InvokeLate(RandomEventContext ctx)') + ''' '''+intruder_valid.replace('private bool IsSpawnStillValid(RandomEventContext ctx)','private bool IsSpawnStillValid(RandomEventContext ctx)')+''' }\npublic sealed class MerchantProbe { private bool _cleanedUp; private int _sceneBuildIndex; private RandomEventContext _spawnContext; private bool _spawnCompleted; private bool _spawnFailed; public bool Failed {get{return _spawnFailed;}} public void Start(RandomEventContext c){_cleanedUp=false;_spawnContext=c;_sceneBuildIndex=0;_spawnCompleted=false;_spawnFailed=false;} public void Cleanup(){_cleanedUp=true;_spawnContext=null;} ''' + merchant_fail.replace('private void HandleMerchantSpawnFailed(RandomEventContext ctx)','public void InvokeLate(RandomEventContext ctx)') + ''' '''+merchant_valid.replace('private bool IsSpawnStillValid(RandomEventContext ctx)','private bool IsSpawnStillValid(RandomEventContext ctx)')+''' }\n}'''
    (OUT/'Generated.cs').write_text(generated,encoding='utf-8')
    (OUT/'source-hashes.txt').write_text('director '+str(hash(director))+'\n'+'catalog '+str(hash(catalog)))
    project=OUT/'RandomEventsFailure.csproj'; project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup><Compile Include="'+str(OUT/'Generated.cs')+'"/><Compile Include="'+str(HERE/'Program.cs')+'"/></ItemGroup></Project>')
    ok=subprocess.call(['dotnet','run','--project',str(project),'--configuration','Release'],cwd=ROOT)
    if ok:return ok
    # Reverse checks: removing each production invariant must make the same executable fail.
    for old,new,label in [('if (_activeEventCounted && _eventsFiredThisRun > 0) _eventsFiredThisRun--;','if (false) { _eventsFiredThisRun--; }','refund'),('ReferenceEquals(_spawnContext, ctx) &&','true &&','context')]:
        mutated = generated.replace('ReferenceEquals(_spawnContext, ctx)', 'true') if label == 'context' else generated.replace(old,new)
        (OUT/'Generated.mutated.cs').write_text(mutated,encoding='utf-8')
        project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup><Compile Include="'+str(OUT/'Generated.mutated.cs')+'"/><Compile Include="'+str(HERE/'Program.cs')+'"/></ItemGroup></Project>')
        if subprocess.call(['dotnet','run','--project',str(project),'--configuration','Release'],cwd=ROOT)==0:
            print('FAIL reverse probe did not fail: '+label); return 1
    print('RandomEventsFailure: exact production extracts PASS; reverse probes rejected')
    return 0
if __name__=='__main__':sys.exit(main())





