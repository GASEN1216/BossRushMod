using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Cysharp.Threading.Tasks;
using Duckov.Economy;
using Duckov.UI;
using Duckov.UI.DialogueBubbles;
using Duckov.Utilities;
using ItemStatsSystem;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    public static partial class CourierPaidLootSweepService
    {
        public const string PaidSweepInteractKey = "BossRush_CourierPaidLootSweep";
        public const string ResultTitleKey = "BossRush_CourierPaidLootSweep_ResultTitle";

        private const float BubbleYOffset = 1.5f;
        private const float BubbleDuration = 3f;
        private const string StartNextSweepButtonTextCn = "开启下次扫箱";
        private const string StartNextSweepButtonTextEn = "Start Next Sweep";

        private static GameObject pendingResultObject = null;
        private static Inventory pendingResultInventory = null;
        private static InteractableLootbox pendingResultLootbox = null;
        private static Transform pendingResultNpcTransform = null;

        private static GameObject startNextSweepButtonObject = null;
        private static Button startNextSweepButton = null;
        private static TextMeshProUGUI startNextSweepButtonText = null;
        private static CourierMovement activeServiceMovement = null;
        private static CourierNPCController activeServiceController = null;

        private static FieldInfo lootTargetInventoryDisplayField = null;
        private static FieldInfo sortButtonField = null;
        private static bool reflectionInitialized = false;
        private static bool lootStopHookRegistered = false;
        private static bool sweepPromptInProgress = false;
        private static int serviceGeneration = 0;

        private sealed class PaidSweepBoxPlan
        {
            public InteractableLootbox Lootbox;
            public Inventory Inventory;
            public readonly List<Item> RootItems = new List<Item>();
            public int ConsumedIndex = -1;
        }

        public static string GetInteractText()
        {
            return L10n.T("扫箱", "Sweep Loot");
        }

        public static bool HasPendingSweepResult()
        {
            if (pendingResultObject == null || pendingResultLootbox == null || pendingResultInventory == null)
            {
                ClearPendingSweepResultReferences();
                return false;
            }

            return true;
        }

        public static void DiscardPendingSweepResult(bool closeLootView)
        {
            DiscardPendingSweepResultInternal(closeLootView, true);
        }

        public static void ReleasePendingSweepResultToPlayer(bool closeLootView, bool showMessage)
        {
            bool hadPending = HasPendingSweepResult();
            if (!hadPending)
            {
                DestroyStartNextSweepButton();
                return;
            }

            TryReturnResultItemsToPlayer(pendingResultInventory);
            DiscardPendingSweepResultInternal(closeLootView, false);

            if (showMessage)
            {
                ModBehaviour mod = ModBehaviour.Instance;
                if (mod != null)
                {
                    mod.ShowMessage(L10n.T(
                        "旧扫箱结果已返还到玩家背包或仓库。",
                        "Old sweep crate contents were returned to the player."));
                }
            }
        }

        public static void CloseServiceIfOwnedBy(Transform npcTransform)
        {
            bool ownsPendingResult = IsTransformOwnedBy(pendingResultNpcTransform, npcTransform);
            bool ownsActiveService = IsActiveServiceOwnedBy(npcTransform);
            if (!ownsPendingResult && !ownsActiveService)
            {
                return;
            }

            serviceGeneration++;
            sweepPromptInProgress = false;
            if (ownsPendingResult)
            {
                ReleasePendingSweepResultToPlayer(true, false);
                return;
            }

            DestroyStartNextSweepButton();
            ExitServiceState();
        }

        public static bool TryRunPaidSweep(Transform npcTransform, CharacterMainControl player)
        {
            ModBehaviour mod = ModBehaviour.Instance;
            if (mod == null || player == null)
            {
                return false;
            }

            if (sweepPromptInProgress)
            {
                return IsCurrentServiceNpc(npcTransform);
            }

            BindServiceNpc(npcTransform);

            if (HasPendingSweepResult())
            {
                return ShowPendingSweepResultPrompt();
            }

            List<AwenLootSweepTarget> targets = new List<AwenLootSweepTarget>();
            int targetCount = mod.CopyFreshAwenLootSweepTargets(targets);
            if (targetCount <= 0)
            {
                ShowBubbleOrMessage(npcTransform, L10n.T("小子，我只接受bossrush箱子服务", "Kid, I only accept BossRush lootbox services."));
                return false;
            }

            List<PaidSweepBoxPlan> plans = BuildBoxPlans(targets);
            if (plans.Count <= 0)
            {
                ShowBubbleOrMessage(npcTransform, L10n.T("小子，我只接受bossrush箱子服务", "Kid, I only accept BossRush lootbox services."));
                return false;
            }

            int cost = AwenLootSweepMath.CalculateSweepCost(plans.Count);
            return ShowFreshSweepPrompt(npcTransform, plans, cost);
        }

        private static bool TryExecuteFreshPaidSweep(Transform npcTransform)
        {
            ModBehaviour mod = ModBehaviour.Instance;
            if (mod == null)
            {
                return false;
            }

            List<AwenLootSweepTarget> freshTargets = new List<AwenLootSweepTarget>();
            int targetCount = mod.CopyFreshAwenLootSweepTargets(freshTargets);
            if (targetCount <= 0)
            {
                ShowBubbleOrMessage(npcTransform, L10n.T("小子，我只接受bossrush箱子服务", "Kid, I only accept BossRush lootbox services."));
                return false;
            }

            List<PaidSweepBoxPlan> freshPlans = BuildBoxPlans(freshTargets);
            if (freshPlans.Count <= 0)
            {
                ShowBubbleOrMessage(npcTransform, L10n.T("小子，我只接受bossrush箱子服务", "Kid, I only accept BossRush lootbox services."));
                return false;
            }

            int freshCost = AwenLootSweepMath.CalculateSweepCost(freshPlans.Count);
            return TryExecutePaidSweep(npcTransform, freshPlans, freshCost);
        }

        private static bool TryExecutePaidSweep(Transform npcTransform, List<PaidSweepBoxPlan> plans, int cost)
        {
            ModBehaviour mod = ModBehaviour.Instance;
            if (mod == null || plans == null || plans.Count <= 0)
            {
                return false;
            }

            if (!CanAfford(cost))
            {
                ShowBubbleOrMessage(
                    npcTransform,
                    activeServiceController != null &&
                    ModBehaviour.Instance != null &&
                    ModBehaviour.Instance.IsZombieModeTemporaryRealNpc(activeServiceController)
                        ? L10n.T("小子，净化点呢？！", "Kid, where's the purification?!")
                        : L10n.T("小子，钱呢？！", "Kid, where's the money?!"));
                ExitServiceState();
                return false;
            }

            if (!TryPay(cost))
            {
                ShowBubbleOrMessage(
                    npcTransform,
                    activeServiceController != null &&
                    ModBehaviour.Instance != null &&
                    ModBehaviour.Instance.IsZombieModeTemporaryRealNpc(activeServiceController)
                        ? L10n.T("小子，净化点呢？！", "Kid, where's the purification?!")
                        : L10n.T("小子，钱呢？！", "Kid, where's the money?!"));
                ExitServiceState();
                return false;
            }

            bool paymentDeducted = true;
            int processedCount = 0;
            bool hadFailures = false;
            GameObject resultObject = null;
            Inventory resultInventory = null;
            InteractableLootbox resultLootbox = null;

            try
            {
                int transferableRootItemCount = CountTransferableRootItems(plans);
                int capacity = AwenLootSweepMath.CalculateContainerCapacity(transferableRootItemCount);
                if (!CourierService.TryCreateTransientLootbox(
                        "CourierPaidLootSweepContainer",
                        capacity,
                        ResultTitleKey,
                        out resultObject,
                        out resultInventory,
                        out resultLootbox))
                {
                    TryRefund(cost, paymentDeducted && processedCount <= 0);
                    ExitServiceState();
                    return false;
                }

                if (resultObject != null)
                {
                    resultObject.transform.position = npcTransform != null ? npcTransform.position : Vector3.zero;
                }

                ShowBubbleOrMessage(npcTransform, L10n.T("哈哈有眼光小子！绝不漏件！", "Smart pick, kid! I never miss a package!"));

                for (int i = 0; i < plans.Count; i++)
                {
                    if (TryProcessSingleLootbox(plans[i], resultInventory))
                    {
                        processedCount++;
                        continue;
                    }

                    hadFailures = true;
                }

                SortSweepResultInventory(resultInventory);
                SetPendingSweepResult(resultObject, resultInventory, resultLootbox, npcTransform);
                mod.InvalidateAwenLootSweepTargetCache();

                if (!OpenPendingSweepResult())
                {
                    if (processedCount <= 0)
                    {
                        TryRefund(cost, paymentDeducted);
                    }

                    TryReturnResultItemsToPlayer(resultInventory);
                    DiscardPendingSweepResultInternal(false, false);
                    ExitServiceState();
                    return false;
                }

                if (hadFailures)
                {
                    mod.ShowMessage(L10n.T(
                        "阿稳这趟只扫了一部分，先看结果箱。",
                        "Awen only cleared part of the field. Check the result crate first."));
                }

                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[CourierPaidLootSweep] [ERROR] TryRunPaidSweep failed: " + e.Message);

                if (processedCount <= 0)
                {
                    TryRefund(cost, paymentDeducted);
                }
                else
                {
                    hadFailures = true;
                }

                if (hadFailures && mod != null)
                {
                    mod.ShowMessage(L10n.T(
                        "阿稳这趟只扫了一部分，先看结果箱。",
                        "Awen only cleared part of the field. Check the result crate first."));
                }

                TryReturnResultItemsToPlayer(resultInventory);
                DiscardPendingSweepResultInternal(false, false);
                ExitServiceState();
                return false;
            }
        }

        private static bool ShowPendingSweepResultPrompt()
        {
            if (sweepPromptInProgress)
            {
                return true;
            }

            sweepPromptInProgress = true;
            Transform promptNpcTransform = pendingResultNpcTransform;
            int promptGeneration = serviceGeneration;
            RunPendingSweepResultPromptAsync(promptNpcTransform, promptGeneration).Forget();
            return true;
        }

        private static async UniTaskVoid RunPendingSweepResultPromptAsync(Transform npcTransform, int promptGeneration)
        {
            bool promptOwnsState = false;
            try
            {
                OriginalConfirmDialogueResult result = await OriginalConfirmDialogueAdapter.Execute(
                    L10n.T(
                        "阿稳这边还保留着上一批代收箱。\n是否现在打开箱子？",
                        "Awen is still holding your previous sweep crate.\nOpen it now?"),
                    L10n.T("打开箱子", "Open Crate"),
                    L10n.T("取消", "Cancel"));

                if (!TryClaimPromptState(npcTransform, promptGeneration, ref promptOwnsState))
                {
                    return;
                }

                if (!result.Completed)
                {
                    HandleOriginalConfirmFailure(npcTransform, result);
                    return;
                }

                if (!result.Confirmed)
                {
                    ExitServiceState();
                    return;
                }

                if (!OpenPendingSweepResult())
                {
                    ExitServiceState();
                }
            }
            catch (Exception e)
            {
                if (TryClaimPromptState(npcTransform, promptGeneration, ref promptOwnsState))
                {
                    ModBehaviour.DevLog("[CourierPaidLootSweep] [ERROR] Pending sweep confirm failed: " + e.Message);
                    ExitServiceState();
                }
            }
            finally
            {
                ClearPromptInProgressIfOwned(npcTransform, promptGeneration, promptOwnsState);
            }
        }

        private static bool ShowFreshSweepPrompt(Transform npcTransform, List<PaidSweepBoxPlan> plans, int cost)
        {
            if (plans == null || plans.Count <= 0)
            {
                return false;
            }

            if (sweepPromptInProgress)
            {
                return true;
            }

            bool usePurification = activeServiceController != null &&
                ModBehaviour.Instance != null &&
                ModBehaviour.Instance.IsZombieModeTemporaryRealNpc(activeServiceController);
            string message = usePurification
                ? L10n.T(
                    "当前可扫箱子：<color=#FFD700>" + plans.Count + "</color> 个\n本次费用：<color=#FFD700>净化点 " + cost + "</color>\n阿稳会把场上的战利品统一整理进代收箱。\n确认开始扫箱？",
                    "Sweepable lootboxes: <color=#FFD700>" + plans.Count + "</color>\nCost: <color=#FFD700>Purification " + cost + "</color>\nAwen will organize the battlefield loot into one pickup crate.\nStart sweep?")
                : L10n.T(
                    "当前可扫箱子：<color=#FFD700>" + plans.Count + "</color> 个\n本次费用：<color=#FFD700>￥" + cost + "</color>\n阿稳会把场上的战利品统一整理进代收箱。\n确认开始扫箱？",
                    "Sweepable lootboxes: <color=#FFD700>" + plans.Count + "</color>\nCost: <color=#FFD700>$" + cost + "</color>\nAwen will organize the battlefield loot into one pickup crate.\nStart sweep?");

            sweepPromptInProgress = true;
            int promptGeneration = serviceGeneration;
            RunFreshSweepPromptAsync(npcTransform, message, promptGeneration).Forget();
            return true;
        }

        private static async UniTaskVoid RunFreshSweepPromptAsync(Transform npcTransform, string message, int promptGeneration)
        {
            bool promptOwnsState = false;
            try
            {
                OriginalConfirmDialogueResult result = await OriginalConfirmDialogueAdapter.Execute(
                    message,
                    L10n.T("确认扫箱", "Start Sweep"),
                    L10n.T("取消", "Cancel"));

                if (!TryClaimPromptState(npcTransform, promptGeneration, ref promptOwnsState))
                {
                    return;
                }

                if (!result.Completed)
                {
                    HandleOriginalConfirmFailure(npcTransform, result);
                    return;
                }

                if (!result.Confirmed)
                {
                    ExitServiceState();
                    return;
                }

                if (!TryExecuteFreshPaidSweep(npcTransform))
                {
                    ExitServiceState();
                }
            }
            catch (Exception e)
            {
                if (TryClaimPromptState(npcTransform, promptGeneration, ref promptOwnsState))
                {
                    ModBehaviour.DevLog("[CourierPaidLootSweep] [ERROR] Fresh sweep confirm failed: " + e.Message);
                    ExitServiceState();
                }
            }
            finally
            {
                ClearPromptInProgressIfOwned(npcTransform, promptGeneration, promptOwnsState);
            }
        }

        private static void HandleOriginalConfirmFailure(Transform npcTransform, OriginalConfirmDialogueResult result)
        {
            string message = result.FailureMessage;
            if (string.IsNullOrEmpty(message))
            {
                message = L10n.T(
                    "扫箱确认框不可用，扫箱已取消。",
                    "Sweep confirmation UI was unavailable. Sweep was cancelled.");
            }

            ShowBubbleOrMessage(npcTransform, message);
            ExitServiceState();
        }

        private static void SetPendingSweepResult(
            GameObject resultObject,
            Inventory resultInventory,
            InteractableLootbox resultLootbox,
            Transform npcTransform)
        {
            DestroyStartNextSweepButton();
            BindServiceNpc(npcTransform);
            pendingResultObject = resultObject;
            pendingResultInventory = resultInventory;
            pendingResultLootbox = resultLootbox;
            pendingResultNpcTransform = npcTransform;
            RegisterLootStopHook();
        }

        private static void ClearPendingSweepResultReferences()
        {
            pendingResultObject = null;
            pendingResultInventory = null;
            pendingResultLootbox = null;
            pendingResultNpcTransform = null;
        }

        private static void BindServiceNpc(Transform npcTransform)
        {
            if (npcTransform == null)
            {
                activeServiceMovement = null;
                activeServiceController = null;
                return;
            }

            activeServiceMovement = npcTransform.GetComponentInParent<CourierMovement>();
            activeServiceController = npcTransform.GetComponentInParent<CourierNPCController>();
        }

        private static bool IsCurrentServiceNpc(Transform npcTransform)
        {
            if (npcTransform == null)
            {
                return activeServiceMovement == null && activeServiceController == null;
            }

            CourierMovement movement = npcTransform.GetComponentInParent<CourierMovement>();
            CourierNPCController controller = npcTransform.GetComponentInParent<CourierNPCController>();

            if (activeServiceMovement != null || movement != null)
            {
                return object.ReferenceEquals(activeServiceMovement, movement);
            }

            return object.ReferenceEquals(activeServiceController, controller);
        }

        private static bool IsPromptStillValid(Transform npcTransform, int promptGeneration)
        {
            return promptGeneration == serviceGeneration && IsCurrentServiceNpc(npcTransform);
        }

        private static bool TryClaimPromptState(Transform npcTransform, int promptGeneration, ref bool promptOwnsState)
        {
            if (!IsPromptStillValid(npcTransform, promptGeneration))
            {
                return false;
            }

            promptOwnsState = true;
            return true;
        }

        private static void ClearPromptInProgressIfOwned(Transform npcTransform, int promptGeneration, bool promptOwnsState)
        {
            if (promptOwnsState || IsPromptStillValid(npcTransform, promptGeneration))
            {
                sweepPromptInProgress = false;
            }
        }

        private static bool IsActiveServiceOwnedBy(Transform npcTransform)
        {
            if (npcTransform == null)
            {
                return false;
            }

            if (activeServiceController != null && IsTransformOwnedBy(activeServiceController.transform, npcTransform))
            {
                return true;
            }

            return activeServiceMovement != null && IsTransformOwnedBy(activeServiceMovement.transform, npcTransform);
        }

        private static bool IsTransformOwnedBy(Transform candidate, Transform owner)
        {
            if (candidate == null || owner == null)
            {
                return false;
            }

            return ReferenceEquals(candidate, owner) ||
                   candidate.IsChildOf(owner) ||
                   owner.IsChildOf(candidate);
        }

        private static void RegisterLootStopHook()
        {
            if (lootStopHookRegistered)
            {
                return;
            }

            InteractableLootbox.OnStopLoot += OnLootboxStopLoot;
            lootStopHookRegistered = true;
        }

        private static void OnLootboxStopLoot(InteractableLootbox lootbox)
        {
            if (!HasPendingSweepResult() || !object.ReferenceEquals(pendingResultLootbox, lootbox))
            {
                return;
            }

            DestroyStartNextSweepButton();
            ExitServiceState();
        }

        private static bool OpenPendingSweepResult()
        {
            if (!HasPendingSweepResult())
            {
                return false;
            }

            DestroyStartNextSweepButton();
            if (!CourierService.TryOpenTransientLootbox(pendingResultLootbox))
            {
                return false;
            }

            ModBehaviour mod = ModBehaviour.Instance;
            if (mod != null)
            {
                mod.StartCoroutine(CreateStartNextSweepButtonDelayed());
            }

            return true;
        }

        private static IEnumerator CreateStartNextSweepButtonDelayed()
        {
            yield return new WaitForSeconds(0.15f);

            if (LootView.Instance == null || !LootView.Instance.open || !HasPendingSweepResult())
            {
                yield break;
            }

            CreateStartNextSweepButton();
        }

        private static void InitializeReflection()
        {
            if (reflectionInitialized)
            {
                return;
            }

            try
            {
                BindingFlags privateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
                lootTargetInventoryDisplayField = typeof(LootView).GetField("lootTargetInventoryDisplay", privateInstance);
                sortButtonField = typeof(InventoryDisplay).GetField("sortButton", privateInstance);
                reflectionInitialized = true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[CourierPaidLootSweep] [WARNING] 初始化按钮反射失败: " + e.Message);
            }
        }

        private static void CreateStartNextSweepButton()
        {
            DestroyStartNextSweepButton();
            InitializeReflection();
            if (LootView.Instance == null || lootTargetInventoryDisplayField == null || sortButtonField == null)
            {
                return;
            }

            try
            {
                InventoryDisplay lootTargetDisplay = lootTargetInventoryDisplayField.GetValue(LootView.Instance) as InventoryDisplay;
                if (lootTargetDisplay == null)
                {
                    return;
                }

                Button sortButton = sortButtonField.GetValue(lootTargetDisplay) as Button;
                if (sortButton == null)
                {
                    return;
                }

                startNextSweepButtonObject = UnityEngine.Object.Instantiate(sortButton.gameObject, sortButton.transform.parent);
                startNextSweepButtonObject.name = "CourierPaidLootSweepNextButton";
                startNextSweepButtonObject.SetActive(true);

                RectTransform rt = startNextSweepButtonObject.GetComponent<RectTransform>();
                RectTransform sortRt = sortButton.GetComponent<RectTransform>();
                if (rt != null && sortRt != null)
                {
                    rt.anchorMin = sortRt.anchorMin;
                    rt.anchorMax = sortRt.anchorMax;
                    rt.pivot = new Vector2(1f, sortRt.pivot.y);

                    float sortRightX = sortRt.anchoredPosition.x + (1f - sortRt.pivot.x) * sortRt.sizeDelta.x;
                    rt.anchoredPosition = new Vector2(sortRightX - sortRt.sizeDelta.x - 10f, sortRt.anchoredPosition.y);
                    rt.sizeDelta = new Vector2(140f, sortRt.sizeDelta.y);
                }

                LayoutElement layoutElement = startNextSweepButtonObject.GetComponent<LayoutElement>();
                if (layoutElement != null)
                {
                    layoutElement.preferredWidth = 140f;
                    layoutElement.minWidth = 140f;
                }

                ContentSizeFitter contentSizeFitter = startNextSweepButtonObject.GetComponent<ContentSizeFitter>();
                if (contentSizeFitter != null)
                {
                    contentSizeFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
                }

                startNextSweepButton = startNextSweepButtonObject.GetComponent<Button>();
                if (startNextSweepButton != null)
                {
                    startNextSweepButton.onClick.RemoveAllListeners();
                    startNextSweepButton.onClick.AddListener(OnStartNextSweepButtonClicked);
                }

                startNextSweepButtonText = startNextSweepButtonObject.GetComponentInChildren<TextMeshProUGUI>();
                ApplyStartNextSweepButtonState();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[CourierPaidLootSweep] [WARNING] 创建开启下次扫箱按钮失败: " + e.Message);
                DestroyStartNextSweepButton();
            }
        }

        private static void ApplyStartNextSweepButtonState()
        {
            if (startNextSweepButton == null)
            {
                return;
            }

            if (startNextSweepButtonText != null)
            {
                startNextSweepButtonText.text = L10n.T(StartNextSweepButtonTextCn, StartNextSweepButtonTextEn);
                startNextSweepButtonText.richText = true;
                startNextSweepButtonText.overflowMode = TextOverflowModes.Overflow;
                startNextSweepButtonText.enableAutoSizing = true;
                startNextSweepButtonText.fontSizeMin = 12f;
                startNextSweepButtonText.fontSizeMax = 18f;
                startNextSweepButtonText.enableWordWrapping = false;
            }

            startNextSweepButton.interactable = true;
            ColorBlock colors = startNextSweepButton.colors;
            Color buttonColor = new Color(0.85f, 0.55f, 0.2f, 1f);
            colors.normalColor = buttonColor;
            colors.highlightedColor = buttonColor * 1.05f;
            colors.selectedColor = colors.highlightedColor;
            colors.pressedColor = buttonColor * 0.9f;
            colors.disabledColor = Color.gray;
            startNextSweepButton.colors = colors;
        }

        private static void DestroyStartNextSweepButton()
        {
            if (startNextSweepButtonObject != null)
            {
                UnityEngine.Object.Destroy(startNextSweepButtonObject);
            }

            startNextSweepButtonObject = null;
            startNextSweepButton = null;
            startNextSweepButtonText = null;
        }

        private static void OnStartNextSweepButtonClicked()
        {
            Transform npc = pendingResultNpcTransform;
            DiscardPendingSweepResultInternal(true, false);

            if (npc != null)
            {
                ModBehaviour mod = ModBehaviour.Instance;
                if (mod != null)
                {
                    mod.StartCoroutine(StartNextSweepDelayed(npc));
                }
            }
        }

        private static IEnumerator StartNextSweepDelayed(Transform npc)
        {
            yield return new WaitForSeconds(0.15f);
            CharacterMainControl player = null;
            try
            {
                player = CharacterMainControl.Main;
            }
            catch { }
            TryRunPaidSweep(npc, player);
        }

        private static void DiscardPendingSweepResultInternal(bool closeLootView, bool showMessage)
        {
            bool hadPending = HasPendingSweepResult();

            if (closeLootView && LootView.Instance != null && LootView.Instance.open)
            {
                try
                {
                    LootView.Instance.Close();
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[CourierPaidLootSweep] [WARNING] 关闭待领箱 LootView 失败: " + e.Message);
                }
            }

            DestroyStartNextSweepButton();

            if (hadPending && pendingResultObject != null)
            {
                UnityEngine.Object.Destroy(pendingResultObject);
            }

            ClearPendingSweepResultReferences();
            ExitServiceState();

            if (showMessage)
            {
                ModBehaviour mod = ModBehaviour.Instance;
                if (mod != null)
                {
                    mod.ShowMessage(L10n.T(
                        "旧扫箱结果已丢弃，下次扫箱重新收费。",
                        "Old sweep crate discarded. The next sweep will charge again."));
                }
            }
        }

        private static List<PaidSweepBoxPlan> BuildBoxPlans(List<AwenLootSweepTarget> targets)
        {
            List<PaidSweepBoxPlan> plans = new List<PaidSweepBoxPlan>();
            if (targets == null)
            {
                return plans;
            }

            for (int i = 0; i < targets.Count; i++)
            {
                AwenLootSweepTarget target = targets[i];
                InteractableLootbox lootbox = target != null ? target.Lootbox : null;
                if (lootbox == null || lootbox.gameObject == null)
                {
                    continue;
                }

                Inventory inventory = null;
                try
                {
                    inventory = lootbox.Inventory;
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[CourierPaidLootSweep] [WARNING] 获取箱子 Inventory 失败: " + e.Message);
                }

                if (inventory == null)
                {
                    continue;
                }

                PaidSweepBoxPlan plan = new PaidSweepBoxPlan();
                plan.Lootbox = lootbox;
                plan.Inventory = inventory;
                CollectRootItems(inventory, plan.RootItems);
                plan.ConsumedIndex = AwenLootSweepMath.PickConsumedRootIndex(plan.RootItems.Count, UnityEngine.Random.value);
                plans.Add(plan);
            }

            return plans;
        }


        /// <summary>
        /// 静态缓存兜底清理 — 由 IBossRushRuntimeModule.OnDestroy 统一调用。
        /// 作为 ExitServiceState / CloseServiceIfOwnedBy 的上位兜底，
        /// 确保模组/场景销毁时所有静态字段被完整释放。
        /// </summary>
        public static void ResetStaticCaches()
        {
            serviceGeneration++;
            InteractableLootbox.OnStopLoot -= OnLootboxStopLoot;

            try
            {
                if (HasPendingSweepResult())
                {
                    ReleasePendingSweepResultToPlayer(true, false);
                }
                else
                {
                    DestroyStartNextSweepButton();
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[CourierPaidLootSweep] [WARNING] ResetStaticCaches 释放扫箱结果失败: " + e.Message);
            }

            try
            {
                ExitServiceState();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[CourierPaidLootSweep] [WARNING] ResetStaticCaches 退出服务状态失败: " + e.Message);
            }

            ClearPendingSweepResultReferences();

            activeServiceMovement = null;
            activeServiceController = null;
            lootTargetInventoryDisplayField = null;
            sortButtonField = null;
            reflectionInitialized = false;
            lootStopHookRegistered = false;
            sweepPromptInProgress = false;
        }

        private static void ExitServiceState()
        {
            try
            {
                if (activeServiceController != null)
                {
                    activeServiceController.StopTalking();
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[CourierPaidLootSweep] [WARNING] 退出扫箱服务时停止对话失败: " + e.Message);
            }

            try
            {
                if (activeServiceMovement != null)
                {
                    activeServiceMovement.SetInService(false);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[CourierPaidLootSweep] [WARNING] 退出扫箱服务时恢复移动失败: " + e.Message);
            }

            BindServiceNpc(null);
        }

        private static void CollectRootItems(Inventory inventory, List<Item> output)
        {
            output.Clear();
            if (inventory == null || inventory.Content == null)
            {
                return;
            }

            for (int i = 0; i < inventory.Content.Count; i++)
            {
                Item item = inventory.Content[i];
                if (item != null)
                {
                    output.Add(item);
                }
            }
        }

        private static int CountTransferableRootItems(List<PaidSweepBoxPlan> plans)
        {
            int count = 0;
            if (plans == null)
            {
                return count;
            }

            for (int i = 0; i < plans.Count; i++)
            {
                PaidSweepBoxPlan plan = plans[i];
                if (plan == null)
                {
                    continue;
                }

                count += Math.Max(0, plan.RootItems.Count - (plan.ConsumedIndex >= 0 ? 1 : 0));
            }

            return count;
        }

        private static bool TryProcessSingleLootbox(PaidSweepBoxPlan plan, Inventory resultInventory)
        {
            if (plan == null || plan.Lootbox == null || plan.Lootbox.gameObject == null || plan.Inventory == null || resultInventory == null)
            {
                return false;
            }

            List<Item> movedItems = new List<Item>();

            try
            {
                for (int i = 0; i < plan.RootItems.Count; i++)
                {
                    if (i == plan.ConsumedIndex)
                    {
                        continue;
                    }

                    Item item = plan.RootItems[i];
                    if (item == null)
                    {
                        continue;
                    }

                    item.Detach();
                    if (!resultInventory.AddAndMerge(item, 0))
                    {
                        movedItems.Add(item);
                        RestoreItemsToInventory(plan.Inventory, movedItems);
                        return false;
                    }

                    movedItems.Add(item);
                }

                if (plan.ConsumedIndex >= 0 && plan.ConsumedIndex < plan.RootItems.Count)
                {
                    Item consumedItem = plan.RootItems[plan.ConsumedIndex];
                    if (consumedItem != null)
                    {
                        consumedItem.Detach();
                        UnityEngine.Object.Destroy(consumedItem.gameObject);
                    }
                }

                UnityEngine.Object.Destroy(plan.Lootbox.gameObject);
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[CourierPaidLootSweep] [WARNING] 处理单个掉落箱失败: " + e.Message);
                RestoreItemsToInventory(plan.Inventory, movedItems);
                return false;
            }
        }

        private static void RestoreItemsToInventory(Inventory inventory, List<Item> items)
        {
            if (items == null || items.Count <= 0)
            {
                return;
            }

            for (int i = 0; i < items.Count; i++)
            {
                Item item = items[i];
                if (item == null)
                {
                    continue;
                }

                try
                {
                    item.Detach();
                }
                catch {}

                try
                {
                    if (inventory != null && inventory.AddAndMerge(item, 0))
                    {
                        continue;
                    }
                }
                catch {}

                try
                {
                    ItemUtilities.SendToPlayer(item, true, true);
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[CourierPaidLootSweep] [WARNING] 恢复物品失败: " + e.Message);
                }
            }
        }

        private static void TryReturnResultItemsToPlayer(Inventory resultInventory)
        {
            if (resultInventory == null || resultInventory.Content == null)
            {
                return;
            }

            List<Item> items = new List<Item>();
            for (int i = 0; i < resultInventory.Content.Count; i++)
            {
                Item item = resultInventory.Content[i];
                if (item != null)
                {
                    items.Add(item);
                }
            }

            for (int i = 0; i < items.Count; i++)
            {
                Item item = items[i];
                if (item == null)
                {
                    continue;
                }

                try
                {
                    item.Detach();
                }
                catch {}

                try
                {
                    ItemUtilities.SendToPlayer(item, true, true);
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[CourierPaidLootSweep] [WARNING] 返还总箱物品失败: " + e.Message);
                }
            }
        }

    }
}
