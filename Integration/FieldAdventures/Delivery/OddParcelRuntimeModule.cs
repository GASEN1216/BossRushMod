using System;
using System.Collections.Generic;
using System.Threading;
using BossRush.Utils;
using Cysharp.Threading.Tasks;
using Duckov.MiniMaps;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    /// <summary>
    /// COMPAT：普通出击的自愿怪件快递。生命周期以 LevelManager 为整趟 owner：
    /// 子场景变化清理包裹和对话，但不刷新两单额度；不建官方任务、不读写存档。
    /// </summary>
    internal sealed class OddParcelRuntimeModule : BossRushRuntimeModuleBase
    {
        private const int MaxOrders = 2;
        private ModBehaviour owner;
        private LevelManager raidOwner;
        private CharacterMainControl player;
        private int acceptedOrders, sceneHandle = -1;
        private GameObject worldRoot, dispatch, parcel, destination;
        private OddParcelVisual dispatchFx, parcelFx, destinationFx;
        private OddParcelInteractable dispatchInteract, parcelInteract, inspectInteract, deliveryInteract, courierOption;
        private List<InteractableBase> courierGroup;
        private CourierInteractable courierMaster;
        private OddParcelCondition condition;
        private CancellationTokenSource cancellation;
        private bool carried, dialogueOpen;
        private int generation;
        private float gateDelay, sceneAge, sampleElapsed, labelElapsed, noiseElapsed, destinationExitTime;
        private float courierScanElapsed;
        private int courierScans;
        private Vector3 lastPlayerPosition;
        public override string ModuleName { get { return "OddParcel"; } }

        public override void OnAwake(ModBehaviour host) { owner = host; }

        public override void OnSceneLoaded(SceneRuntimeContext context)
        {
            if (worldRoot != null && !worldRoot.scene.isLoaded) ClearWorld();
        }

        public override void OnUpdate(float deltaTime, float unscaledDeltaTime)
        {
            try
            {
                // 设置界面会暂停玩法更新；已经响起的 Unity 叫声仍须立即跟随官方音量。
                if (parcelFx != null) parcelFx.SyncVoiceVolume();
                Tick(deltaTime);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[OddParcel] [WARNING] 快递运行时已清理：" + e.Message);
                ClearWorld();
                gateDelay = 5f;
            }
        }

        private void Tick(float deltaTime)
        {
            LevelManager level = LevelManager.Instance;
            if (!ReferenceEquals(level, raidOwner))
            {
                ClearWorld();
                raidOwner = level;
                acceptedOrders = 0;
            }
            Scene scene;
            CharacterMainControl currentPlayer;
            if (!FieldAdventureSupport.TryGetRaid(owner, out currentPlayer, out scene))
            {
                if (worldRoot != null || cancellation != null) ClearWorld();
                return;
            }
            if (currentPlayer == null || currentPlayer.Health == null || currentPlayer.Health.IsDead)
            {
                ClearWorld();
                return;
            }
            player = currentPlayer;
            if (sceneHandle != scene.handle)
            {
                bool abandoned = condition != null;
                ClearWorld();
                player = currentPlayer;
                sceneHandle = scene.handle;
                if (abandoned) FieldAdventureSupport.Notify("怪件快递已退单：离开了派件区域，本趟额度不返还。",
                    "Odd parcel cancelled: you left the delivery area. This raid's order slot is still used.");
            }
            if (BossRushUI.IsGamePaused() || deltaTime <= 0f) return;
            gateDelay -= deltaTime;
            if (gateDelay > 0f) return;
            sceneAge += deltaTime;
            if (worldRoot == null)
            {
                if (acceptedOrders >= MaxOrders || sceneAge < 2f) return;
                OpenWorld(scene);
                return;
            }
            if (dispatchFx != null) dispatchFx.Tick(deltaTime, 0f, false);
            if (destinationFx != null) destinationFx.Tick(deltaTime, 0f, false);
            if (condition == null && destination != null)
            {
                destinationExitTime -= deltaTime;
                if (destinationExitTime <= 0f) ClearDestination();
            }
            if (courierOption == null && courierScans < 8 && acceptedOrders < MaxOrders)
            {
                courierScanElapsed += deltaTime;
                if (courierScanElapsed >= 3f) { courierScanElapsed = 0f; TryAttachCourier(scene); }
            }
            labelElapsed += deltaTime;
            if (labelElapsed >= .8f)
            {
                labelElapsed = 0f;
                RefreshLabels();
            }
            if (condition == null || parcel == null) return;
            if (dialogueOpen || BossRushUI.IsOfficialHudHidden())
            {
                lastPlayerPosition = player.transform.position;
                sampleElapsed = 0f;
                return;
            }
            if (carried) UpdateCarriedPose();
            parcelFx.Tick(deltaTime, condition.Pressure, carried);
            sampleElapsed += deltaTime;
            noiseElapsed += deltaTime;
            if (sampleElapsed >= .15f)
            {
                Vector3 movement = player.transform.position - lastPlayerPosition;
                movement.y = 0f;
                float speed = Mathf.Min(15f, movement.magnitude / sampleElapsed);
                bool burst = condition.Step(sampleElapsed, speed, carried);
                lastPlayerPosition = player.transform.position;
                sampleElapsed = 0f;
                if (burst) ParcelBurst();
                else if (condition.Kind == OddParcelKind.QuackingEgg && carried && speed > 1f && noiseElapsed >= 7f)
                {
                    noiseElapsed = 0f;
                    parcelFx.Squawk();
                    MakeNoise(parcel.transform.position, 30f);
                    FieldAdventureSupport.Notify("巨蛋：嘎——！附近敌人听见了，停下来能让它安静。",
                        "The giant egg QUACKS! Nearby enemies heard it. Stop moving to settle it.");
                }
            }
        }

        private void OpenWorld(Scene scene)
        {
            worldRoot = new GameObject("BossRush_OddParcel_Raid");
            SceneManager.MoveGameObjectToScene(worldRoot, scene);
            cancellation = new CancellationTokenSource();
            TryAttachCourier(scene);
            if (courierOption == null)
            {
                Vector3 position;
                if (!FieldAdventureSupport.TryFindGround(player.transform.position, 3f, 7f, out position))
                {
                    ClearWorld(); gateDelay = 4f; return;
                }
                dispatch = NewAnchor("AwenOddParcelDispatch", position);
                dispatchInteract = dispatch.AddComponent<OddParcelInteractable>();
                dispatchInteract.Configure(this, OddParcelAction.Offer);
                dispatchFx = new OddParcelVisual(dispatch, OddParcelKind.QuackingEgg, true, false);
                AddMapPoint(dispatch, "BossRush_OddParcel_DispatchMap", L10n.T("阿稳的怪件发件台", "Awen's odd-parcel dispatch"));
                dispatch.SetActive(true);
                SetupMapPoint(dispatch, "BossRush_OddParcel_DispatchMap");
                FieldAdventureSupport.Notify("阿稳留下了怪件发件台：巨蛋和汽水正在等人送。地图上已标记。",
                    "Awen left an odd-parcel dispatch nearby. A giant egg and soda can need a courier. Marked on your map.");
            }
        }

        private void TryAttachCourier(Scene scene)
        {
            courierScans++;
            CourierInteractable[] candidates = UnityEngine.Object.FindObjectsOfType<CourierInteractable>();
            for (int i = 0; i < candidates.Length; i++)
            {
                CourierInteractable candidate = candidates[i];
                if (candidate == null || candidate.gameObject.scene.handle != scene.handle) continue;
                List<InteractableBase> group = NPCInteractionGroupHelper.GetOrCreateGroupList(candidate, "[OddParcel]");
                if (group == null) continue;
                courierOption = NPCInteractionGroupHelper.AddSubInteractable<OddParcelInteractable>(candidate.transform,
                    "OddParcelOption", group, option => option.Configure(this, OddParcelAction.Offer, true));
                if (courierOption != null)
                {
                    courierGroup = group;
                    courierMaster = candidate;
                    // Helper adds new children immediately; let the shared availability gate own membership.
                    group.Remove(courierOption);
                    SyncCourierOption();
                    return;
                }
            }
        }

        private void SyncCourierOption(bool clearing = false)
        {
            if (courierOption == null || courierGroup == null) return;
            bool available = !clearing && CanInteract(OddParcelAction.Offer);
            if (courierGroup.Contains(courierOption) == available) return;
            CA_Interact action = player != null ? player.interactAction : null;
            bool current = courierMaster != null && action != null && action.MasterInteractableAround == courierMaster;
            // GetInteractableInGroup reads the live list: retain the object before changing its indices.
            InteractableBase selected = current ? action.InteractTarget : null;
            if (available) courierGroup.Add(courierOption);
            else courierGroup.Remove(courierOption);
            if (!current) return;
            int index = courierMaster.GetInteractableList().IndexOf(selected);
            if (index < 0) index = 0;
            action.SwitchInteractable(index - action.InteractIndexInGroup);
            courierOption.RefreshVisibleGroup(courierMaster, selected);
        }

        private GameObject NewAnchor(string name, Vector3 position)
        {
            var go = new GameObject(name);
            go.SetActive(false);
            go.transform.SetParent(worldRoot.transform, false);
            go.transform.position = position;
            int layer = LayerMask.NameToLayer("Interactable");
            if (layer >= 0) go.layer = layer;
            SphereCollider collider = go.AddComponent<SphereCollider>();
            // 官方只在脚边向前 0.2 米、抬高 0.5 米处检测半径 0.3 的球；携带时也要够得到。
            collider.radius = .95f; collider.center = new Vector3(0f, .35f, 0f); collider.isTrigger = true;
            return go;
        }

        private static void AddMapPoint(GameObject target, string key, string name)
        {
            LocalizationHelper.InjectLocalization(key, name);
            var marker = target.AddComponent<SimplePointOfInterest>();
            marker.Color = BossRushUIColors.WarningText;
            marker.ScaleFactor = 1.25f;
            marker.IsArea = false;
            // Setup 必须在激活后调用，避免 OnEnable 与 Setup 双重登记。
        }

        private static void SetupMapPoint(GameObject target, string key)
        {
            if (target == null) return;
            SimplePointOfInterest poi = target.GetComponent<SimplePointOfInterest>();
            if (poi != null) poi.Setup(null, key);
        }

        internal bool CanInteract(OddParcelAction action)
        {
            if (worldRoot == null || player == null || player.Health == null || player.Health.IsDead || dialogueOpen) return false;
            switch (action)
            {
                case OddParcelAction.Offer: return condition == null && acceptedOrders < MaxOrders;
                case OddParcelAction.Deliver:
                    return condition != null && !condition.Paid && !condition.Ruined && carried && destination != null
                        && Vector3.Distance(player.transform.position, destination.transform.position) < 3.3f;
                default: return condition != null && !condition.Paid;
            }
        }

        internal string InteractionLabel(OddParcelAction action)
        {
            switch (action)
            {
                case OddParcelAction.Offer: return L10n.T("怪件快递", "Odd-parcel delivery");
                case OddParcelAction.Deliver: return L10n.T("签收怪件", "Deliver odd parcel");
                case OddParcelAction.Inspect: return L10n.T("货况与退单", "Parcel details / cancel");
                default:
                    if (condition == null) return L10n.T("搬运怪件", "Carry odd parcel");
                    string title = carried ? L10n.T("放下", "Set down ") : L10n.T("抱起", "Pick up ");
                    return title + ParcelName(condition.Kind) + L10n.T(" · 压力 ", " · pressure ")
                        + Mathf.RoundToInt(condition.Pressure) + "%" + L10n.T(" · 完整 ", " · integrity ")
                        + Mathf.RoundToInt(condition.Integrity) + "%";
            }
        }

        private static string ParcelName(OddParcelKind kind)
        {
            return kind == OddParcelKind.QuackingEgg ? L10n.T("嘎嘎巨蛋", "Quacking Egg") : L10n.T("暴躁汽水", "Angry Soda");
        }

        internal void Interact(OddParcelAction action, OddParcelInteractable from)
        {
            if (!CanInteract(action)) return;
            if (action == OddParcelAction.Carry) ToggleCarry();
            else if (action == OddParcelAction.Deliver) Deliver();
            else ShowChoices(from, action == OddParcelAction.Inspect).Forget();
        }

        private async UniTaskVoid ShowChoices(OddParcelInteractable from, bool inspecting)
        {
            if (cancellation == null || from == null) return;
            int operation = generation;
            CancellationToken token = cancellation.Token;
            dialogueOpen = true;
            SyncCourierOption();
            try
            {
                var actor = DialogueActorFactory.CreateBilingual(from.gameObject, "OddParcelDispatch", "阿稳的怪件货单", "Awen's odd-parcel invoice");
                DialogueActorFactory.RefreshBilingualName("OddParcelDispatch", "阿稳的怪件货单", "Awen's odd-parcel invoice");
                if (actor == null) return;
                if (inspecting)
                {
                    int remaining = destination == null ? 0 : Mathf.RoundToInt(Vector3.Distance(player.transform.position, destination.transform.position));
                    await DialogueManager.ShowDialogueSequenceBilingual(actor, new[] { new[] {
                        "签收台距你 " + remaining + " 米，按 M 看地图。完整度 " + Mathf.RoundToInt(condition.Integrity) + "% ，预计报酬 " + condition.Reward + " 金。按交互键抱起或放下，放下会泄压。退单不扣钱，但本趟额度不返还。",
                        "The receipt desk is " + remaining + " m away; press M for the map. Integrity " + Mathf.RoundToInt(condition.Integrity) + "%, payout " + condition.Reward + ". Interact to pick up / set down. Resting lowers pressure. Cancellation costs no money but uses this raid's slot."
                    } }, "BossRush_OddParcel_Status", token);
                    int answer = await DialogueManager.ShowMultipleChoiceBilingual(actor,
                        new[] { new[] { "继续送货", "Keep delivering" }, new[] { "退掉这单", "Cancel this order" } }, 0f, "BossRush_OddParcel_Cancel", token);
                    if (operation == generation && answer == 1)
                    {
                        ClearOrder();
                        FieldAdventureSupport.Notify("已退单。阿稳收回了怪件，本趟接单额度不返还。", "Order cancelled. Awen reclaimed the parcel; this raid's slot stays used.");
                    }
                    return;
                }
                await DialogueManager.ShowDialogueSequenceBilingual(actor, new[] { new[] {
                    "两种怪件，自己选。巨蛋一路嘎嘎叫，会把敌人引过来；快跑还会把它吓得弹起来。汽水会越抱越涨，放地上喷完气再走。送到地图上的签收台可拿 1200-2400 金，完整度越高越多。每趟最多接两单，退单也算一单。",
                    "Pick your parcel. The egg quacks and attracts enemies; sprinting makes it bounce. Soda pressure builds while carried; set it down to vent. Deliver to the marked desk for 1200-2400, depending on integrity. Two accepted orders per raid; cancellations count."
                } }, "BossRush_OddParcel_Intro", token);
                int choice = await DialogueManager.ShowMultipleChoiceBilingual(actor, new[] {
                    new[] { "接单：嘎嘎巨蛋", "Accept: Quacking Egg" },
                    new[] { "接单：暴躁汽水", "Accept: Angry Soda" },
                    new[] { "先不接", "Maybe later" }
                }, 0f, "BossRush_OddParcel_Choices", token);
                if (operation == generation && (choice == 0 || choice == 1)) StartOrder((OddParcelKind)choice, from.transform.position);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception e) { ModBehaviour.DevLog("[OddParcel] [WARNING] 货单对话失败：" + e.Message); }
            finally
            {
                if (operation == generation) { dialogueOpen = false; SyncCourierOption(); }
            }
        }

        private void StartOrder(OddParcelKind kind, Vector3 origin)
        {
            if (worldRoot == null || condition != null || acceptedOrders >= MaxOrders || player == null) return;
            Vector3 end, start;
            if (!FieldAdventureSupport.TryFindGround(origin, 28f, 45f, out end)
                || !FieldAdventureSupport.TryFindGround(player.transform.position, 1.5f, 3f, out start))
            {
                FieldAdventureSupport.Notify("阿稳：附近没有合适的签收落点，这单先不算。", "Awen: no suitable receipt location nearby. This attempt uses no order slot.");
                return;
            }
            ClearDestination();
            try
            {
                parcel = NewAnchor("AwenOddParcel_" + kind, start);
                parcelInteract = parcel.AddComponent<OddParcelInteractable>();
                parcelInteract.Configure(this, OddParcelAction.Carry);
                parcelFx = new OddParcelVisual(parcel, kind, false, false);
                List<InteractableBase> group = NPCInteractionGroupHelper.PrepareGroupedInteractionOwner(parcelInteract, "[OddParcel]");
                inspectInteract = NPCInteractionGroupHelper.AddSubInteractable<OddParcelInteractable>(parcel.transform,
                    "InspectParcel", group, entry => entry.Configure(this, OddParcelAction.Inspect, true));
                destination = NewAnchor("AwenOddParcelReceipt", end);
                deliveryInteract = destination.AddComponent<OddParcelInteractable>();
                deliveryInteract.Configure(this, OddParcelAction.Deliver);
                destinationFx = new OddParcelVisual(destination, kind, true, true);
                AddMapPoint(destination, "BossRush_OddParcel_ReceiptMap", L10n.T("怪件签收台", "Odd-parcel receipt desk"));
                condition = new OddParcelCondition(kind);
                carried = false;
                parcel.SetActive(true); destination.SetActive(true);
                SetupMapPoint(destination, "BossRush_OddParcel_ReceiptMap");
                acceptedOrders++;
                lastPlayerPosition = player.transform.position;
                sampleElapsed = labelElapsed = noiseElapsed = 0f;
                RefreshLabels();
                FieldAdventureSupport.Notify("怪件放在你身边，交互抱起。签收台已标在地图；放下可泄压。",
                    "Your odd parcel is beside you. Interact to pick it up. The receipt desk is marked on the map; set down to vent.");
            }
            catch (Exception) { ClearOrder(); throw; }
        }

        private void UpdateCarriedPose()
        {
            Vector3 forward = player.CurrentAimDirection;
            forward.y = 0f;
            if (forward.sqrMagnitude < .01f) forward = Vector3.forward;
            // 与官方交互探测使用同一朝向，抱起当刻也同步实体位置。
            parcel.transform.position = player.transform.position + forward.normalized + Vector3.up * .35f;
            parcel.transform.rotation = Quaternion.LookRotation(forward);
        }

        private void UpdateParcelCollider()
        {
            SphereCollider collider = parcel.GetComponent<SphereCollider>();
            // 携带态把交互球退回玩家上方，兼容角色先移动/转身、官方先搜索、owner 后更新的帧序。
            // 放下后回到实体中心，玩家仍需靠近地上的包裹才能交互。
            if (collider != null) collider.center = new Vector3(0f, .35f, carried ? -1f : 0f);
        }

        private void ToggleCarry()
        {
            if (condition == null || parcel == null) return;
            if (carried)
            {
                Vector3 ground;
                if (!FieldAdventureSupport.TryFindGround(player.transform.position, 1.3f, 2.7f, out ground))
                {
                    FieldAdventureSupport.Notify("这里放不稳，往空地挪两步。", "No stable spot here; move a little into the open.");
                    return;
                }
                parcel.transform.position = ground;
            }
            carried = !carried;
            if (carried) UpdateCarriedPose();
            UpdateParcelCollider();
            lastPlayerPosition = player.transform.position;
            sampleElapsed = 0f;
            RefreshLabels();
        }

        private void ParcelBurst()
        {
            if (parcel == null || condition == null) return;
            // 安全落点先准备；找不到时仍抱着包裹，只做弹跳，不把实体丢进墙或地图外。
            Vector3 ground;
            if (FieldAdventureSupport.TryFindGround(player.transform.position, 1.5f, 3f, out ground))
            {
                carried = false;
                parcel.transform.position = ground;
                UpdateParcelCollider();
            }
            RefreshLabels();
            parcelFx.Burst(false);
            MakeNoise(parcel.transform.position, condition.Kind == OddParcelKind.QuackingEgg ? 40f : 24f);
            FieldAdventureSupport.Notify(condition.Kind == OddParcelKind.QuackingEgg ? "嘎嘎巨蛋跳起来了！包装磕坏了一角。" : "汽水喷成了小喷泉！包装受损，放稳后再抱。",
                condition.Kind == OddParcelKind.QuackingEgg ? "The giant egg bounces! Its packaging is dented." : "The soda erupts into a fountain! Packaging damaged; let it settle.");
            if (condition.Ruined)
            {
                ClearOrder();
                FieldAdventureSupport.Notify("怪件已经散架，阿稳撤回了货单。", "The parcel fell apart. Awen withdrew the order.");
            }
        }

        private static void MakeNoise(Vector3 position, float radius)
        {
            try
            {
                AISound sound = default(AISound);
                sound.pos = position; sound.radius = radius; sound.fromTeam = Teams.player;
                sound.soundType = SoundTypes.unknowNoise; sound.fromCharacter = null; sound.fromObject = null;
                AIMainBrain.MakeSound(sound);
            }
            catch (Exception e) { ModBehaviour.DevLog("[OddParcel] 引怪声源未投递: " + e.Message); }
        }

        private void Deliver()
        {
            if (!CanInteract(OddParcelAction.Deliver)) return;
            int reward = condition.Reward;
            if (!condition.TryPay(TryGrantCash))
            {
                FieldAdventureSupport.Notify("签收台暂时没能结账，再交互一次即可重试。", "The desk could not settle payment. Interact again to retry.");
                return;
            }
            if (destinationFx != null) destinationFx.Burst(true);
            if (parcelFx != null) parcelFx.Dispose();
            parcelFx = null;
            if (parcel != null) UnityEngine.Object.Destroy(parcel);
            parcel = null; condition = null; carried = false;
            parcelInteract = inspectInteract = null;
            destinationExitTime = 3f;
            FieldAdventureSupport.Notify("签收成功！阿稳支付了 " + reward + " 金。",
                "Delivery signed! Awen paid " + reward + ".");
            RefreshLabels();
        }

        private static bool TryGrantCash(long amount)
        {
            if (Duckov.Economy.EconomyManager.Instance == null) return false;
            long before = Duckov.Economy.EconomyManager.Money;
            try { return Duckov.Economy.EconomyManager.Add(amount); }
            catch (Exception e)
            {
                // 官方先写余额，再触发 OnMoneyChanged；其它监听者抛出不能让本单再次加钱。
                bool credited = Duckov.Economy.EconomyManager.Money >= before + amount;
                ModBehaviour.DevLog("[OddParcel] [WARNING] 签收通知异常，按实际余额确认到账：" + credited + " / " + e.Message);
                return credited;
            }
        }

        private void RefreshLabels()
        {
            SyncCourierOption();
            if (courierOption != null) courierOption.RefreshLabel();
            if (dispatchInteract != null) dispatchInteract.RefreshLabel();
            if (parcelInteract != null) parcelInteract.RefreshLabel();
            if (inspectInteract != null) inspectInteract.RefreshLabel();
            if (deliveryInteract != null) deliveryInteract.RefreshLabel();
            LocalizationHelper.InjectLocalization("BossRush_OddParcel_ReceiptMap", L10n.T("怪件签收台", "Odd-parcel receipt desk"));
            LocalizationHelper.InjectLocalization("BossRush_OddParcel_DispatchMap", L10n.T("阿稳的怪件发件台", "Awen's odd-parcel dispatch"));
        }

        private void ClearDestination()
        {
            if (destinationFx != null) destinationFx.Dispose(); destinationFx = null;
            if (destination != null) UnityEngine.Object.Destroy(destination);
            destination = null; deliveryInteract = null;
        }

        private void ClearOrder()
        {
            if (parcelFx != null) parcelFx.Dispose(); parcelFx = null;
            if (parcel != null) UnityEngine.Object.Destroy(parcel);
            parcel = null; condition = null; carried = false;
            parcelInteract = inspectInteract = null;
            ClearDestination();
            SyncCourierOption();
        }

        private void ClearWorld()
        {
            generation++;
            if (cancellation != null) { cancellation.Cancel(); cancellation.Dispose(); cancellation = null; }
            dialogueOpen = false;
            SyncCourierOption(true);
            if (courierOption != null)
            {
                UnityEngine.Object.Destroy(courierOption.gameObject);
            }
            courierOption = null; courierGroup = null; courierMaster = null;
            ClearOrder();
            if (dispatchFx != null) dispatchFx.Dispose(); dispatchFx = null;
            if (worldRoot != null) UnityEngine.Object.Destroy(worldRoot);
            worldRoot = dispatch = null; dispatchInteract = null;
            player = null; sceneHandle = -1;
            sceneAge = courierScanElapsed = 0f; courierScans = 0;
        }

        public override void OnDestroy() { ClearWorld(); owner = null; raidOwner = null; }
    }
}
