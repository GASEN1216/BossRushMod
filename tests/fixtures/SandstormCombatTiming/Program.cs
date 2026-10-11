using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BossRush;
using UnityEngine;

internal static class Program
{
    private static int checks, failures;
    private static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    private static void Near(float actual, float expected, string message, float tolerance=0.002f)
    { Check(Math.Abs(actual-expected)<=tolerance, message+": actual="+actual+", expected="+expected); }
    private static void Case(string name, Action body)
    {
        Reset();
        try { body(); Console.WriteLine("PASS "+name); }
        catch (Exception error) { failures++; Console.WriteLine("FAIL "+name+": "+error.GetBaseException().Message); }
    }
    private static void Reset()
    {
        GameObject.ClearScene();
        Time.time=Time.deltaTime=Time.unscaledDeltaTime=0;
        BossRushUI.Paused=false;
        BossSkillDamageRules.Dodging=false;
        Physics.Wall=false;
        Physics.HasGround=p=>true;
        Physics.GroundHeight=p=>0f;
        Physics.Probes.Clear();
        CharacterMainControl.Main=new CharacterMainControl();
        CharacterMainControl.Main.transform.position=new Vector3(0,0,23);
        AstralStaffFx.Root=null;
    }
    private static SandstormChampionController Begin(int phase=3, int count=1)
    {
        var c=new SandstormChampionController { gameObject=new GameObject("controller") };
        c.Begin(phase,count);
        return c;
    }
    private static void Until(SandstormChampionController c, Func<bool> done, float dt)
    {
        for (int i=0;i<2000&&!done();i++) c.Frame(dt);
        Check(done(),"bounded fight progression");
    }
    private static void Pause(bool menu)
    {
        var c=Begin();
        Until(c,()=>c._boss.PositionWrites>=3,1f/60f);
        float at=c._boss.transform.position.z;
        int ticks=c.TrackingTicks, hits=c.Hits;
        BossRushUI.Paused=menu;
        for(int i=0;i<120;i++) c.Frame(0f,1f/60f);
        Check(c._body.Charging&&c.Settles==0,"zero-time frames must preserve in-flight dash");
        Near(c._boss.transform.position.z,at,"pause position");
        Check(c.TrackingTicks==ticks&&c.Hits==hits,"pause must not advance combat or velocity tracking");
        BossRushUI.Paused=false;
        Until(c,()=>c.Finished,1f/60f);
        Check(c._body.Starts==1&&c.Settles==1,"resume finishes the same dash exactly once");
        Check(c._boss.transform.position.z>=32f,"resume completes planned distance");
    }
    private static void FrameRate(float dt)
    {
        var c=Begin();
        Until(c,()=>c._body.Charging,dt);
        Near(c._boss.transform.position.z,SandstormChampionConfig.DashSpeedP3*dt,"first frame consumes full movement time");
        Until(c,()=>c.Finished,dt);
        Check(c._boss.transform.position.z>=32f&&c._boss.transform.position.z<32f+58f*dt+0.01f,"same distance within one frame");
        Check(c.Hits==1&&c.Damage==SandstormChampionConfig.DashDamageP3,"swept contact damages once with original tuning");
        for(int i=1;i<Physics.Probes.Count;i++)
            Check(Vector3.Distance(Physics.Probes[i-1],Physics.Probes[i])<=58f*0.05f+0.01f,"terrain is checked in bounded substeps");
    }
    private static void Geometry(bool cliff)
    {
        // A terrain strip must not be skipped by a single 11.6m step after a 200ms frame.
        if(cliff) Physics.HasGround=p=>p.z<4f||p.z>7f;
        else Physics.GroundHeight=p=>p.z>=4f&&p.z<=7f?3f:0f;
        var c=Begin();
        Until(c,()=>c.Finished,0.2f);
        Check(c._boss.transform.position.z<4f,"substep must stop before missing ground / excessive height");
        Check(c.Settles==1,"terrain failure settles once");
    }
    private static void MinionWarnings(string method)
    {
        var controller=Begin();
        var marker=new GameObject("guard").AddComponent<SandstormChampionMinionMarker>();
        var minion=new CharacterMainControl();
        var arts=new SandstormChampionMinionArts(marker,minion,controller);
        var routine=(IEnumerator)typeof(SandstormChampionMinionArts).GetMethod(method,BindingFlags.NonPublic|BindingFlags.Instance).Invoke(arts,null);
        Check(routine.MoveNext(),"production minion windup starts");
        var wait=routine.Current as IEnumerator;
        Check(wait!=null&&wait.MoveNext(),"production minion uses game-time wait");
        var ribbons=GameObject.Live<AstralStaffRibbon>();
        Check(ribbons.Count==1,"one ground warning per attack");
        var ribbon=ribbons[0];
        var mesh=ribbon.GetComponent<MeshFilter>().sharedMesh;
        Check(mesh.vertices.Length==82&&mesh.triangles.Length==240,"existing ribbon geometry preserved");
        // CameraMode.Active freezes Time.deltaTime independently of GameManager.Paused.
        for(int i=0;i<60;i++) TickRibbon(ribbon,0f,1f/30f);
        Check(ribbon!=null&&mesh!=null&&wait.MoveNext(),"photo mode must preserve warning while attack waits");
        float before=Elapsed(ribbon);
        TickRibbon(ribbon,0.005f,0.1f);
        Near(Elapsed(ribbon)-before,0.005f,"warning follows slow-motion attack clock");
        float frozen=Elapsed(ribbon);
        BossRushUI.Paused=true;
        TickRibbon(ribbon,0f,1f);
        Near(Elapsed(ribbon),frozen,"menu pause preserves warning");
        BossRushUI.Paused=false;
        for(int i=0;i<300&&ribbon!=null;i++) TickRibbon(ribbon,1f/60f,1f/60f);
        Check(ribbon==null&&mesh==null,"normal completion destroys warning and its mesh");
    }
    private static void TickRibbon(AstralStaffRibbon ribbon,float dt,float real)
    {
        if(ribbon==null)return;
        Time.deltaTime=dt;Time.unscaledDeltaTime=real;Time.time+=dt;
        typeof(AstralStaffRibbon).GetMethod("Update",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(ribbon,null);
    }
    private static float Elapsed(AstralStaffRibbon ribbon)
    { return (float)typeof(AstralStaffRibbon).GetField("_t",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(ribbon); }
    private static void PlayerVisual()
    {
        var ribbon=AstralStaffRibbon.Play(Vector3.zero,Vector3.forward,130f,1f,3f,AstralStaffConfig.Gold,0.1f,0.1f,0.3f,0.5f,0f,null);
        var mesh=ribbon.GetComponent<MeshFilter>().sharedMesh;
        TickRibbon(ribbon,0.005f,0.1f);
        Near(Elapsed(ribbon),0.1f,"existing player ribbon retains unscaled animation");
        BossRushUI.Paused=true;
        TickRibbon(ribbon,0f,1f);
        Near(Elapsed(ribbon),0.1f,"existing player menu pause behavior");
        UnityEngine.Object.Destroy(ribbon.gameObject);
        Check(ribbon==null&&mesh==null,"owner cancellation releases ribbon and mesh");
    }
    private static int Main()
    {
        Case("menu pause and resume",()=>Pause(true));
        Case("photo pause and resume",()=>Pause(false));
        foreach(float dt in new[]{1f/120f,1f/60f,1f/30f,0.05f,0.1f})
        { float step=dt;Case("dash frame "+step,()=>FrameRate(step)); }
        Case("variable and long frames",()=>
        {
            var c=Begin();float[] steps={1f/60f,0.1f,1f/30f,0.2f};
            for(int i=0;i<2000&&!c.Finished;i++)c.Frame(steps[i%steps.Length]);
            Check(c.Finished&&c._boss.transform.position.z>=32f,"variable delta must not shorten dash");
            Check(c.Hits==1,"long-frame swept hit remains once");
        });
        Case("missing terrain",()=>Geometry(true));
        Case("height discontinuity",()=>Geometry(false));
        Case("safe partial movement still hits",()=>
        {
            CharacterMainControl.Main.transform.position=new Vector3(0,0,2.9f);
            Physics.HasGround=p=>p.z<4f;
            var c=Begin();Until(c,()=>c.Finished,0.1f);
            Check(c.Hits==1&&c._boss.transform.position.z<4f,"safe segment before failed substep keeps its hit");
        });
        Case("owner cancelled by position callback",()=>
        {
            var c=Begin();c._boss.PositionChanged=()=>c.Alive=false;
            Until(c,()=>c.Finished,0.1f);
            Check(c._boss.PositionWrites==1&&c.Hits==0,"position callback cancels remaining substeps and damage");
        });
        Case("enraged speed preserved",()=>
        {
            var c=Begin();c._enraged=true;Until(c,()=>c._body.Charging,0.1f);
            Near(c._boss.transform.position.z,58f*1.3f*0.1f,"enraged dash consumes same full frame time");
        });
        Case("phase three passes walls",()=>{Physics.Wall=true;var c=Begin();Until(c,()=>c.Finished,0.1f);Check(c._boss.transform.position.z>=32f,"phase three keeps wall phasing");});
        Case("dodge immunity",()=>{BossSkillDamageRules.Dodging=true;var c=Begin();Until(c,()=>c.Finished,0.1f);Check(c.Hits==0,"dash does not damage dodging player");});
        Case("death during pause",()=>{var c=Begin();c.Frame(1f/60f);c.Alive=false;c.Frame(0f,1f);Check(c.Finished,"invalid owner still cleans up during pause");});
        foreach(int phase in new[]{1,2})
        { int p=phase;Case("unchanged phase "+p,()=>{var c=Begin(p);Until(c,()=>c.Finished,1f/60f);Check(c._boss.PositionWrites==0&&c.Hits==1,"early phases use official motion and damage");}); }
        foreach(string method in new[]{"Sweep","Spin","Starfall"})
        { string action=method;Case("minion "+action+" warning",()=>MinionWarnings(action)); }
        Case("player ribbon compatibility",PlayerVisual);
        GameObject.ClearScene();
        Console.WriteLine("SandstormCombatTiming: "+checks+" checks / "+failures+" failed cases");
        return failures==0?0:1;
    }
}
