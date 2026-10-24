using System;
using System.Linq;
using System.Threading.Tasks;
using BossRush;
using BossRush.Utils;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

internal static partial class Program
{
    private static CourierInteractable courier;
    private static OddParcelInteractable offer;
    private static InteractableBase serviceBefore, serviceAfter;
    private static int SelectedEvents { get { return UnityEvent.TotalInvocations; } }

    private static void ResetCourier(bool lateAttachment = false, bool hiddenAttachment = false)
    {
        if (runtime != null) runtime.OnDestroy();
        UnityEngine.Object.All.Clear();
        BossRushUI.Paused=BossRushUI.Hidden=false;
        FieldAdventureSupport.Raid=FieldAdventureSupport.Ground=true;
        FieldAdventureSupport.FixedGround=false; FieldAdventureSupport.SceneHandle=1;
        DialogueManager.Choice=0; DialogueManager.PendingChoice=null;
        L10n.English=false; InputManager.InputActived=true;
        SetupHost();
        runtime=new OddParcelRuntimeModule(); runtime.OnAwake(new ModBehaviour());
        if (lateAttachment) runtime.OnUpdate(2.1f,2.1f);
        courier=new GameObject("Awen").AddComponent<CourierInteractable>();
        courier.transform.position=new Vector3(10,0,0);
        courier.interactableGroup=true;
        var collider=courier.gameObject.AddComponent<SphereCollider>();
        collider.radius=.8f; collider.center=new Vector3(0,.35f,0);
        var group=NPCInteractionGroupHelper.GetOrCreateGroupList(courier,"test");
        serviceBefore=new GameObject("ExistingServiceBefore").AddComponent<InteractableBase>();
        serviceBefore.gameObject.transform.SetParent(courier.transform,false);
        group.Add(serviceBefore);
        if (!lateAttachment) runtime.OnUpdate(2.1f,2.1f);
        serviceAfter=new GameObject("ExistingServiceAfter").AddComponent<InteractableBase>();
        serviceAfter.gameObject.transform.SetParent(courier.transform,false);
        group.Add(serviceAfter);
        ApproachCourier();
        if (lateAttachment)
        {
            Select(serviceAfter);
            int events=SelectedEvents;
            if (hiddenAttachment) Probe.Hud.transform.parent.gameObject.SetActive(false);
            Probe.Call(runtime,"TryAttachCourier",new Scene{handle=1,isLoaded=true});
            offer=Probe.Get<OddParcelInteractable>(runtime,"courierOption");
            Check(player.interactAction.MasterInteractableAround==courier,"late attachment keeps official master");
            Check(player.interactAction.InteractTarget==serviceAfter,"late attachment preserves selected service object");
            Check(SelectedEvents==events,"late attachment does not repeat retained selection event");
            if (hiddenAttachment) Probe.Hud.transform.parent.gameObject.SetActive(true);
            AssertCourierRows(true,hiddenAttachment ? "first hidden attachment refreshes cached same-master HUD" : "late attachment refreshes same-master HUD");
            HudUpdate();
            Check(SelectedEvents==events,"late attachment synchronizes official HUD index cache");
        }
        else offer=Probe.Get<OddParcelInteractable>(runtime,"courierOption");
        Check(offer!=null && offer.CheckInteractable(),"first courier offer is executable");
        AssertCourierRows(true,"first courier offer is shown");
    }

    private static void ApproachCourier()
    {
        player.transform.position=courier.transform.position;
        Search(); HudUpdate();
        Check(player.interactAction.MasterInteractableAround==courier,"official search finds Awen");
    }

    private static void Select(InteractableBase target)
    {
        var action=player.interactAction;
        int index=action.MasterInteractableAround.GetInteractableList().IndexOf(target);
        Check(index>=0,"requested selected object is a live group member");
        action.SwitchInteractable(index-action.InteractIndexInGroup); HudUpdate();
        Check(action.InteractTarget==target && Row(target).selectIndicator.activeSelf,"official selection highlights requested object");
    }

    private static void AssertCourierRows(bool available,string message)
    {
        Check(courier.GetInteractableList().Contains(offer)==available,message+" / group");
        var rows=Probe.Hud.GetComponentsInChildren<InteractSelectionHUD>();
        Check(rows.Any(row=>row.InteractTarget==offer)==available,message+" / visible rows");
        var members=courier.GetInteractableList().ToArray();
        Check(rows.Length==members.Length && members.All(member=>rows.Count(row=>row.InteractTarget==member)==1),message+" / exact official content");
        Check(player.interactAction.InteractTarget!=null && rows.Count(row=>row.selectIndicator.activeSelf)==1
            && Row(player.interactAction.InteractTarget).selectIndicator.activeSelf,message+" / one matching selection");
    }

    private static void RefreshParcel()
    {
        parcel=Probe.Get<OddParcelInteractable>(runtime,"parcelInteract");
        inspect=Probe.Get<OddParcelInteractable>(runtime,"inspectInteract");
        Probe.Call(parcel,"Awake"); Probe.Call(parcel,"Start");
    }

    private static void AcceptCourier(int choice)
    {
        Select(offer); int events=SelectedEvents;
        var previousRows=Probe.Hud.GetComponentsInChildren<InteractSelectionHUD>();
        DialogueManager.Choice=choice; Press();
        Check(Probe.Get<OddParcelCondition>(runtime,"condition")!=null,"official courier F accepts order");
        Check(player.interactAction.MasterInteractableAround==courier && player.interactAction.InteractTarget==courier,
            "removed selected offer converges to Awen master");
        AssertCourierRows(false,"active order removes offer immediately");
        Check(Probe.Hud.GetComponentsInChildren<InteractSelectionHUD>().All(row=>previousRows.Contains(row)),
            "official pool rebuild reuses previous row identities");
        Check(previousRows.Any(row=>row!=null && !row.gameObject.activeSelf),
            "smaller group leaves an inactive surplus row in the real HUD subtree");
        Check(SelectedEvents==events+1,"removed offer emits one fallback selection event");
        HudUpdate();
        Check(SelectedEvents==events+1,"next official HUD update does not duplicate fallback event");
        RefreshParcel();
    }

    private static void CancelAtCourier()
    {
        player.transform.position=parcel.transform.position; Search(); Select(inspect);
        DialogueManager.PendingChoice=new TaskCompletionSource<int>();
        Press();
        Check(Probe.Get<bool>(runtime,"dialogueOpen"),"official parcel F opens pending cancellation");
        ApproachCourier(); Select(serviceAfter); int events=SelectedEvents;
        var pending=DialogueManager.PendingChoice; DialogueManager.PendingChoice=null;
        pending.SetResult(1);
        Check(Probe.Get<OddParcelCondition>(runtime,"condition")==null,"official cancellation clears active order");
        Check(!Probe.Get<bool>(runtime,"dialogueOpen"),"cancellation finally releases dialogue gate");
        Check(player.interactAction.InteractTarget==serviceAfter,"cancellation while menu open preserves other selected service");
        Check(SelectedEvents==events,"cancellation reattachment has no repeated selected event");
        HudUpdate();
        Check(SelectedEvents==events,"cancellation synchronizes official cached index");
    }

    private static void DeliverFirst()
    {
        player.transform.position=parcel.transform.position; Search(); Select(parcel); Press();
        Check(Probe.Get<bool>(runtime,"carried"),"official parcel F picks up courier order");
        var desk=Probe.Get<GameObject>(runtime,"destination");
        player.transform.position=desk.transform.position; runtime.OnUpdate(.016f,.016f); Search(); HudUpdate();
        Check(player.interactAction.InteractTarget==Probe.Get<OddParcelInteractable>(runtime,"deliveryInteract"),"official search finds receipt desk");
        long before=Duckov.Economy.EconomyManager.Money; Press();
        Check(Probe.Get<OddParcelCondition>(runtime,"condition")==null,"official receipt F completes courier order");
        Check(Duckov.Economy.EconomyManager.Money>before,"official receipt pays order once");
        long paid=Duckov.Economy.EconomyManager.Money;
        runtime.Interact(OddParcelAction.Deliver,null);
        Check(Duckov.Economy.EconomyManager.Money==paid,"repeat receipt callback cannot duplicate payout");
        ApproachCourier();
    }

    private static void QuotaLifecycle(bool deliverFirst)
    {
        ResetCourier(); AcceptCourier(0);
        if (deliverFirst) DeliverFirst(); else CancelAtCourier();
        Check(Probe.Get<int>(runtime,"acceptedOrders")==1,"first completion keeps one consumed slot");
        Check(offer.CheckInteractable(),"first completion restores executable offer");
        AssertCourierRows(true,"first completion restores offer before menu closes");
        player.transform.position=new Vector3(100,0,100); Search(); HudUpdate();
        ApproachCourier(); AssertCourierRows(true,"first completion offer survives reapproach");
        AcceptCourier(1);
        Check(Probe.Get<int>(runtime,"acceptedOrders")==2,"second courier order consumes final slot");
        CancelAtCourier();
        Check(!offer.CheckInteractable() && !runtime.CanInteract(OddParcelAction.Offer),"quota exhaustion shares action rejection gate");
        AssertCourierRows(false,"two consumed slots keep offer absent");
        runtime.OnUpdate(.81f,.81f); HudUpdate(); AssertCourierRows(false,"periodic refresh cannot restore exhausted offer");
        player.transform.position=new Vector3(100,0,100); Search(); HudUpdate();
        ApproachCourier(); AssertCourierRows(false,"exhausted offer stays absent on reapproach");
        int accepted=Probe.Get<int>(runtime,"acceptedOrders");
        runtime.Interact(OddParcelAction.Offer,offer);
        Check(Probe.Get<int>(runtime,"acceptedOrders")==accepted && Probe.Get<OddParcelCondition>(runtime,"condition")==null,
            "stale courier callback cannot start third order");
        FieldAdventureSupport.SceneHandle=2; runtime.OnUpdate(.016f,.016f);
        Check(Probe.Get<int>(runtime,"acceptedOrders")==2,"subscene transition keeps used raid slots");
        runtime.OnDestroy();
    }

    private static void OtherSelectionAndCleanup()
    {
        ResetCourier(); Select(serviceAfter);
        Check(player.interactAction.InteractIndexInGroup==3,"offer precedes selected existing service");
        int events=SelectedEvents;
        DialogueManager.PendingChoice=new TaskCompletionSource<int>();
        runtime.Interact(OddParcelAction.Offer,offer);
        Check(player.interactAction.InteractTarget==serviceAfter && player.interactAction.InteractIndexInGroup==2,
            "membership removal preserves service object across index shift");
        AssertCourierRows(false,"open dialogue shares unavailable membership gate");
        Check(SelectedEvents==events,"retained service emits no selected event during removal");
        HudUpdate(); Check(SelectedEvents==events,"retained service cache prevents duplicate event on next Update");
        var pending=DialogueManager.PendingChoice; DialogueManager.PendingChoice=null; pending.SetResult(2);
        AssertCourierRows(true,"dismissed offer restores membership after finally");
        Check(player.interactAction.InteractTarget==serviceAfter && SelectedEvents==events,"dialogue dismissal preserves other selection and events");
        HudUpdate(); Check(SelectedEvents==events,"dismissed offer cache stays synchronized");
        // Exhaustive cleanup at an empty order: ClearOrder must not reattach while destroying the owner.
        var group=Probe.Get<System.Collections.Generic.List<InteractableBase>>(runtime,"courierGroup");
        var oldOffer=offer;
        runtime.OnDestroy();
        Check(!group.Contains(oldOffer),"runtime cleanup removes raw courier group member before destruction");
        Check(oldOffer==null && courier!=null && serviceAfter!=null,"cleanup destroys only the owned offer");
        Check(player.interactAction.MasterInteractableAround==courier && player.interactAction.InteractTarget==serviceAfter,
            "cleanup retains official master and existing selected service");
        Check(SelectedEvents==events,"cleanup does not repeat retained selection event");
        HudUpdate();
        Check(SelectedEvents==events,"cleanup cache prevents duplicate event next frame");
        runtime.OnDestroy(); Check(!group.Contains(oldOffer),"repeat cleanup cannot reattach removed offer");

        ResetCourier(); group=Probe.Get<System.Collections.Generic.List<InteractableBase>>(runtime,"courierGroup"); oldOffer=offer;
        player.Health.IsDead=true; BossRushUI.Paused=true; runtime.OnUpdate(0,1);
        Check(!group.Contains(oldOffer) && oldOffer==null,"death during pause removes raw courier member");
        Check(Probe.Get<GameObject>(runtime,"worldRoot")==null,"death cleans up owner world");

        ResetCourier();
        DialogueManager.PendingChoice=new TaskCompletionSource<int>();
        Select(offer); Press();
        group=Probe.Get<System.Collections.Generic.List<InteractableBase>>(runtime,"courierGroup"); oldOffer=offer;
        pending=DialogueManager.PendingChoice; DialogueManager.PendingChoice=null;
        runtime.OnDestroy(); pending.SetResult(0);
        Check(!group.Contains(oldOffer) && oldOffer==null,"stale dialogue finally cannot reattach destroyed offer");
        Check(Probe.Get<OddParcelCondition>(runtime,"condition")==null && Probe.Get<GameObject>(runtime,"worldRoot")==null,
            "stale dialogue cannot recreate order after cleanup");
    }

    private static void HiddenCourierMenu()
    {
        ResetCourier(true,true); runtime.OnDestroy();
        ResetCourier(); Select(serviceAfter);
        var canvas=Probe.Hud.transform.parent.gameObject;
        int events=SelectedEvents;
        canvas.SetActive(false);
        DialogueManager.PendingChoice=new TaskCompletionSource<int>();
        runtime.Interact(OddParcelAction.Offer,offer);
        Check(!courier.GetInteractableList().Contains(offer),"hidden menu removes unavailable group member");
        Check(player.interactAction.InteractTarget==serviceAfter,"hidden menu preserves selected service identity");
        Check(SelectedEvents==events,"hidden menu removal does not repeat retained selected event");
        canvas.SetActive(true);
        AssertCourierRows(false,"hidden removal refreshes content before unchanged-master Update");
        HudUpdate(); Check(SelectedEvents==events,"hidden removal synchronizes selection cache");
        canvas.SetActive(false);
        var pending=DialogueManager.PendingChoice; DialogueManager.PendingChoice=null; pending.SetResult(2);
        canvas.SetActive(true);
        AssertCourierRows(true,"hidden finally refreshes restored offer before Update");
        Check(SelectedEvents==events,"hidden restore preserves retained selected event count");
        HudUpdate(); Check(SelectedEvents==events,"hidden restore synchronizes selection cache");
        runtime.OnDestroy();
    }

    private static void OfficialHudHideAndReopen()
    {
        ResetCourier(); Select(offer);
        DialogueManager.PendingChoice=new TaskCompletionSource<int>();
        player.CurrentAction=player.interactAction;
        Check(player.interactAction.StartActionByCharacter(player),"official pending action starts");
        HudUpdate();
        Check(!Probe.Hud.master.gameObject.activeSelf,"official Running gate hides HUD during action");
        player.interactAction.UpdateAction(.5f); player.interactAction.StopAction(); player.CurrentAction=null;
        UnityEngine.Time.time+=.5f;
        Check(Probe.Get<bool>(runtime,"dialogueOpen") && !courier.GetInteractableList().Contains(offer),
            "pending official dialogue removes unavailable courier offer");
        InputManager.InputActived=false; HudUpdate();
        Check(!Probe.Hud.master.gameObject.activeSelf,"official input gate keeps pending dialogue HUD hidden");
        var pending=DialogueManager.PendingChoice; DialogueManager.PendingChoice=null; pending.SetResult(2);
        Check(courier.GetInteractableList().Contains(offer),"dismissed pending official dialogue restores offer");
        int events=SelectedEvents; InputManager.InputActived=true; HudUpdate();
        AssertCourierRows(true,"official reopen rebuilds current group");
        Check(SelectedEvents==events+1,"official reopen emits its own one selected event");
        HudUpdate(); Check(SelectedEvents==events+1,"unchanged official reopened menu does not repeat selected event");
        runtime.OnDestroy();
    }

    private static void PendingGenerationKeepsNewOwner()
    {
        ResetCourier();
        DialogueManager.PendingChoice=new TaskCompletionSource<int>(); Select(offer); Press();
        var oldPending=DialogueManager.PendingChoice;
        Probe.Call(runtime,"ClearWorld");
        runtime.OnUpdate(2.1f,2.1f);
        offer=Probe.Get<OddParcelInteractable>(runtime,"courierOption");
        ApproachCourier();
        DialogueManager.PendingChoice=new TaskCompletionSource<int>(); Select(offer); Press();
        var newPending=DialogueManager.PendingChoice; DialogueManager.PendingChoice=null;
        Check(Probe.Get<bool>(runtime,"dialogueOpen"),"new generation has its own pending dialogue");
        oldPending.SetResult(0);
        Check(Probe.Get<OddParcelCondition>(runtime,"condition")==null,"old pending choice cannot populate the new generation order");
        Check(Probe.Get<bool>(runtime,"dialogueOpen") && !courier.GetInteractableList().Contains(offer),
            "old finally cannot release or reattach the newer pending dialogue");
        newPending.SetResult(2);
        Check(!Probe.Get<bool>(runtime,"dialogueOpen"),"current generation finally releases its own dialogue");
        AssertCourierRows(true,"new generation dismissal restores only its own offer");
        runtime.OnDestroy();
    }

    private static void CourierAvailability()
    {
        QuotaLifecycle(false); QuotaLifecycle(true);
        ResetCourier(true); runtime.OnDestroy(); HiddenCourierMenu(); OfficialHudHideAndReopen(); PendingGenerationKeepsNewOwner(); OtherSelectionAndCleanup();
    }
}
