using System;
using System.Linq;
using UnityEngine;
using BossRush;

internal static partial class Program
{
    private static int assertions;
    private static OddParcelRuntimeModule runtime;
    private static CharacterMainControl player;
    private static OddParcelInteractable parcel;
    private static OddParcelInteractable inspect;
    private static void Check(bool value,string name) { assertions++; if(!value) throw new Exception(name); }
    private static void Near(float value,float expected,string name) { Check(Math.Abs(value-expected)<.0001f,name+" actual="+value+" expected="+expected); }
    private static Vector3 Direction(int degrees) { double a=degrees*Math.PI/180; return new Vector3((float)Math.Sin(a),0,(float)Math.Cos(a)); }
    private static void Search() { player.interactAction.SearchInteractableAround(); }
    private static void Press()
    {
        player.CurrentAction=player.interactAction;
        Check(player.interactAction.StartActionByCharacter(player),"full official action starts selected entry");
        player.interactAction.UpdateAction(.5f);
        player.interactAction.StopAction();
        player.CurrentAction=null;
        UnityEngine.Time.time+=.5f;
    }
    private static void HudUpdate() { Probe.Call(Probe.Hud,"Update"); }
    private static InteractSelectionHUD Row(InteractableBase target)
    { return Probe.Hud.GetComponentsInChildren<InteractSelectionHUD>().Single(r=>r.InteractTarget==target); }

    private static void SetupHost()
    {
        player=new GameObject("PlayerRoot").AddComponent<CharacterMainControl>(); CharacterMainControl.Main=player;
        player.modelRoot=new GameObject("ModelRoot").transform;
        player.interactAction=player.gameObject.AddComponent<CA_Interact>(); player.interactAction.characterController=player;
        Probe.Call(player.interactAction,"Awake");
        LevelManager.Instance=new GameObject("LevelManager").AddComponent<LevelManager>();
        LevelManager.Instance.MainCharacter=player;
        var canvas=new GameObject("HUDCanvas"); canvas.transform.SetParent(LevelManager.Instance.transform,false);
        Probe.Hud=new GameObject("OfficialHUD").AddComponent<InteractHUD>();
        Probe.Hud.transform.SetParent(canvas.transform,false);
        Probe.Hud.master=new GameObject("Master").transform; Probe.Hud.master.SetParent(Probe.Hud.transform,false);
        Probe.Hud.selectionPrefab=Probe.NewRow();
        Probe.Call(Probe.Hud,"Awake");
    }

    private static void Setup()
    {
        SetupHost();
        runtime=new OddParcelRuntimeModule(); runtime.OnAwake(new ModBehaviour()); runtime.OnUpdate(2.1f,2.1f);
        OddParcelInteractable dispatch=Probe.Get<OddParcelInteractable>(runtime,"dispatchInteract");
        Probe.Call(dispatch,"Awake"); Probe.Call(dispatch,"Start");
        player.transform.position=dispatch.transform.position; Search();
        Check(player.interactAction.InteractTarget==dispatch,"official search discovers delivery offer");
        Press();
        parcel=Probe.Get<OddParcelInteractable>(runtime,"parcelInteract"); inspect=Probe.Get<OddParcelInteractable>(runtime,"inspectInteract");
        Probe.Call(parcel,"Awake"); Probe.Call(parcel,"Start");
        player.transform.position=parcel.transform.position; Search(); HudUpdate(); Press();
        Check(Probe.Get<bool>(runtime,"carried"),"official carry action picks parcel up");
        Check(Vector3.Distance(parcel.transform.position,player.transform.position)>0,"pick-up places parcel immediately");
    }

    private static void GeometryAndActions()
    {
        // Check reachability before pose assertions so restoring the complete prior geometry fails at the real entry.
        for(int angle=0;angle<360;angle++)
        {
            player.modelRoot.forward=Direction(angle); runtime.OnUpdate(.016f,.016f); Search();
            Check(player.interactAction.MasterInteractableAround==parcel,"all-heading official carry entry remains reachable");
        }
        for(int moving=0;moving<2;moving++)
        for(int angle=0;angle<360;angle++)
        {
            player.modelRoot.forward=Direction(angle);
            if(moving!=0) player.transform.position+=Direction(angle)*.03f;
            runtime.OnUpdate(.016f,.016f);
            Vector3 delta=parcel.transform.position-player.transform.position; delta.y=0;
            Check(Vector3.Distance(delta.normalized,Direction(angle))<.0001f,"carry pose follows official aim");
            Search(); Check(player.interactAction.MasterInteractableAround==parcel,"all headings and movement keep carry entry reachable");
        }
        // Official character and mod Update order may differ. Exercise every new aim against a previous pose.
        for(int old=0;old<360;old+=15)
        {
            player.modelRoot.forward=Direction(old); runtime.OnUpdate(.016f,.016f);
            for(int next=0;next<360;next+=15)
            {
                player.modelRoot.forward=Direction(next); Search();
                Check(player.interactAction.MasterInteractableAround==parcel,"instant turn before owner update keeps menu reachable");
                Vector3 before=player.transform.position;
                player.transform.position+=Direction(next)*.5f; Search();
                Check(player.interactAction.MasterInteractableAround==parcel,"movement and turn before owner update keep menu reachable");
                player.transform.position=before;
            }
        }
        Search(); FieldAdventureSupport.Ground=false; Press();
        Check(Probe.Get<bool>(runtime,"carried"),"failed drop keeps parcel carried");
        Near(parcel.GetComponent<SphereCollider>().center.z,-1,"failed drop keeps carried interaction geometry");
        FieldAdventureSupport.Ground=true;
        Press(); Check(!Probe.Get<bool>(runtime,"carried"),"official carry action sets parcel down");
        Near(parcel.GetComponent<SphereCollider>().center.z,0,"grounded parcel restores entity-centered collider");
        player.transform.position=parcel.transform.position; Search(); Press();
        Check(Probe.Get<bool>(runtime,"carried"),"official carry action picks dropped parcel back up");
        Near(parcel.GetComponent<SphereCollider>().center.z,-1,"pick-up restores carried interaction geometry");
    }

    private static void DynamicHud()
    {
        runtime.OnUpdate(.016f,.016f); Search(); HudUpdate();
        InteractSelectionHUD carry=Row(parcel), details=Row(inspect);
        string initial=carry.text.text;
        player.interactAction.SwitchInteractable(1); HudUpdate();
        Check(player.interactAction.InteractTarget==inspect && details.selectIndicator.activeSelf,"official grouped details selection works");
        int events=details.OnSelectedEvent.Invocations;
        OddParcelCondition state=Probe.Get<OddParcelCondition>(runtime,"condition");
        while(state.Pressure<20) state.Step(.5f,5,true);
        runtime.OnUpdate(.81f,.81f);
        Check(carry.text.text==parcel.GetInteractName() && carry.text.text!=initial,"same-target HUD tracks pressure");
        Check(Row(parcel)==carry && Row(inspect)==details,"dynamic label does not rebuild official rows");
        Check(player.interactAction.InteractTarget==inspect && player.interactAction.InteractIndexInGroup==1,"dynamic label preserves group index");
        Check(details.OnSelectedEvent.Invocations==events && details.selectIndicator.activeSelf && !carry.selectIndicator.activeSelf,"dynamic label preserves highlight without selected events");
        L10n.English=true; runtime.OnUpdate(.81f,.81f);
        Check(carry.text.text.Contains("pressure") && details.text.text=="Parcel details / cancel","same-target grouped HUD changes language");
        Check(details.OnSelectedEvent.Invocations==events,"language refresh does not retrigger selection");
        FieldAdventureSupport.Ground=false;
        while(!state.Step(.5f,8,true)) { }
        Probe.Call(runtime,"ParcelBurst");
        Check(carry.text.text==parcel.GetInteractName() && carry.text.text.Contains("84%"),"burst refreshes integrity immediately");
        Check(player.interactAction.InteractTarget==inspect && details.OnSelectedEvent.Invocations==events,"burst preserves grouped selection");
        Near(parcel.GetComponent<SphereCollider>().center.z,-1,"failed burst landing retains carried interaction geometry");
        FieldAdventureSupport.Ground=true;
        player.interactAction.SwitchInteractable(-1); HudUpdate();
        Check(player.interactAction.InteractTarget==parcel && carry.selectIndicator.activeSelf,"official switching after dynamic refresh remains functional");
    }

    private static void AudioAndAi()
    {
        OddParcelVisual visual=Probe.Get<OddParcelVisual>(runtime,"parcelFx"); AudioSource audio=Probe.Get<AudioSource>(visual,"voice");
        Duckov.AudioManager.Bus master=Duckov.AudioManager.Master,sfx=Duckov.AudioManager.Sfx;
        master.Volume=0; sfx.Volume=1; int plays=audio.Plays; visual.Squawk();
        Near(audio.volume,0,"master volume zero suppresses voice"); Check(audio.Plays==plays,"master zero does not start voice");
        master.Volume=1; sfx.Volume=0; visual.Squawk(); Near(audio.volume,0,"SFX zero suppresses voice");
        sfx.Volume=1; master.Mute=true; visual.Squawk(); Near(audio.volume,0,"master mute suppresses voice");
        master.Mute=false; sfx.Mute=true; visual.Squawk(); Near(audio.volume,0,"SFX mute suppresses voice");
        sfx.Mute=false; master.Volume=.5f; sfx.Volume=.4f; visual.Squawk(); Near(audio.volume,.14f,"nonzero buses multiply voice volume"); Check(audio.isPlaying,"audible buses play voice");
        BossRushUI.Paused=true; master.Volume=.2f; runtime.OnUpdate(0,.016f); Near(audio.volume,.056f,"paused settings update active voice");
        master.Mute=true; runtime.OnUpdate(0,.016f); Near(audio.volume,0,"paused mute silences active voice");
        master.Mute=false; master.Volume=1; sfx.Volume=.5f; BossRushUI.Paused=false; BossRushUI.Hidden=true;
        runtime.OnUpdate(.016f,.016f); Near(audio.volume,.35f,"hidden HUD settings update active voice"); BossRushUI.Hidden=false;
        Duckov.AudioManager.Master=null; visual.Squawk(); Near(audio.volume,0,"missing audio manager fails closed"); Duckov.AudioManager.Master=master;
        master.Volume=0; sfx.Mute=true;
        FieldAdventureSupport.Ground=false;
        int sounds=AIMainBrain.Sounds.Count; Probe.Call(runtime,"ParcelBurst");
        Check(AIMainBrain.Sounds.Count==sounds+1 && AIMainBrain.Sounds.Last().radius==40,"muted parcel keeps independent AI noise");
        FieldAdventureSupport.Ground=true;
        Probe.Call(runtime,"ParcelBurst");
        Check(!Probe.Get<bool>(runtime,"carried"),"successful burst landing sets parcel down");
        Near(parcel.GetComponent<SphereCollider>().center.z,0,"burst landing restores entity-centered collider");
        // Mandatory scene invalidation: Unity destroys a parent and all components before the owner sees it.
        GameObject root=Probe.Get<GameObject>(runtime,"worldRoot"); UnityEngine.Object.Destroy(root);
        Check(parcel==null && audio==null,"scene destruction invalidates parcel and audio components");
        FieldAdventureSupport.SceneHandle=2;
        runtime.OnUpdate(.016f,.016f);
        Check(ReferenceEquals(Probe.Get<GameObject>(runtime,"parcel"),null) && ReferenceEquals(Probe.Get<OddParcelCondition>(runtime,"condition"),null),"scene transition releases destroyed parcel and order state");
        runtime.OnDestroy();
    }

    private static int Main()
    {
        try { Setup(); GeometryAndActions(); DynamicHud(); AudioAndAi(); CourierAvailability(); Console.WriteLine("PASS " + assertions + " delivery contract assertions"); return 0; }
        catch(Exception error) { Console.WriteLine(error); return 1; }
    }
}
