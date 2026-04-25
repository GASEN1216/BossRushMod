using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Duckov.Economy;
using Duckov.UI;
using Duckov.UI.DialogueBubbles;
using Duckov.Utilities;
using ItemStatsSystem;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using BossRush.UI;

namespace BossRush
{
    public static class CourierPaidLootSweepService
    {
        public const string PaidSweepInteractKey = "BossRush_CourierPaidLootSweep";
        public const string ResultTitleKey = "BossRush_CourierPaidLootSweep_ResultTitle";

        private const float BubbleYOffset = 1.5f;
        private const float BubbleDuration = 3f;
        private const float StartNextSweepButtonYOffset = -38f;
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

        public static bool TryRunPaidSweep(Transform npcTransform, CharacterMainControl player)
        {
            ModBehaviour mod = ModBehaviour.Instance;
            if (mod == null || player == null)
            {
                return false;
            }

            if (HasPendingSweepResult())
            {
                BindServiceNpc(npcTransform);
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
                ShowBubbleOrMessage(npcTransform, L10n.T("小子，钱呢？！", "Kid, where's the money?!"));
                ExitServiceState();
                return false;
            }

            if (!TryPay(cost))
            {
                ShowBubbleOrMessage(npcTransform, L10n.T("小子，钱呢？！", "Kid, where's the money?!"));
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
            ConfirmDialogUI.Show(
                L10n.T(
                    "阿稳这边还保留着上一批代收箱。\n是否现在免费取回？",
                    "Awen is still holding your previous sweep crate.\nReopen it for free now?"),
                () =>
                {
                    ConfirmDialogUI.Hide();
                    if (!OpenPendingSweepResult())
                    {
                        ExitServiceState();
                    }
                },
                () =>
                {
                    ConfirmDialogUI.Hide();
                    ExitServiceState();
                },
                L10n.T("免费取回", "Free Reopen"),
                L10n.T("取消", "Cancel"));

            return ConfirmDialogUI.IsVisible;
        }

        private static bool ShowFreshSweepPrompt(Transform npcTransform, List<PaidSweepBoxPlan> plans, int cost)
        {
            if (plans == null || plans.Count <= 0)
            {
                return false;
            }

            string message = L10n.T(
                "当前可扫箱子：<color=#FFD700>" + plans.Count + "</color> 个\n本次费用：<color=#FFD700>￥" + cost + "</color>\n阿稳会把场上的战利品统一整理进代收箱。\n确认开始扫箱？",
                "Sweepable lootboxes: <color=#FFD700>" + plans.Count + "</color>\nCost: <color=#FFD700>$" + cost + "</color>\nAwen will organize the battlefield loot into one pickup crate.\nStart sweep?");

            ConfirmDialogUI.Show(
                message,
                () =>
                {
                    ConfirmDialogUI.Hide();
                    if (!TryExecuteFreshPaidSweep(npcTransform))
                    {
                        ExitServiceState();
                    }
                },
                () =>
                {
                    ConfirmDialogUI.Hide();
                    ExitServiceState();
                },
                L10n.T("确认扫箱", "Start Sweep"),
                L10n.T("取消", "Cancel"));

            return ConfirmDialogUI.IsVisible;
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

            activeServiceMovement = npcTransform.GetComponent<CourierMovement>();
            activeServiceController = npcTransform.GetComponent<CourierNPCController>();
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

        private static int GetCurrentPayableSweepCount()
        {
            ModBehaviour mod = ModBehaviour.Instance;
            if (mod == null)
            {
                return 0;
            }

            List<AwenLootSweepTarget> targets = new List<AwenLootSweepTarget>();
            mod.CopyFreshAwenLootSweepTargets(targets);
            return BuildBoxPlans(targets).Count;
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

        private static void SortSweepResultInventory(Inventory resultInventory)
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

            if (items.Count <= 1)
            {
                return;
            }

            items.Sort(CompareSweepResultItems);

            for (int i = 0; i < items.Count; i++)
            {
                try
                {
                    items[i].Detach();
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[CourierPaidLootSweep] [WARNING] 排序前 Detach 失败: " + e.Message);
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
                    if (resultInventory.AddAndMerge(item, 0))
                    {
                        continue;
                    }
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[CourierPaidLootSweep] [WARNING] 排序后回填失败: " + e.Message);
                }

                try
                {
                    ItemUtilities.SendToPlayer(item, true, true);
                }
                catch (Exception sendEx)
                {
                    ModBehaviour.DevLog("[CourierPaidLootSweep] [WARNING] 排序失败物品回退给玩家失败: " + sendEx.Message);
                }
            }
        }

        private static int CompareSweepResultItems(Item left, Item right)
        {
            if (object.ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left == null)
            {
                return 1;
            }

            if (right == null)
            {
                return -1;
            }

            int leftQuality = GetSweepItemQuality(left);
            int rightQuality = GetSweepItemQuality(right);
            int qualityCompare = rightQuality.CompareTo(leftQuality);
            if (qualityCompare != 0)
            {
                return qualityCompare;
            }

            int leftValue = GetSweepItemSortValue(left);
            int rightValue = GetSweepItemSortValue(right);
            int valueCompare = rightValue.CompareTo(leftValue);
            if (valueCompare != 0)
            {
                return valueCompare;
            }

            string leftName = string.Empty;
            string rightName = string.Empty;
            try { leftName = left.DisplayName ?? string.Empty; } catch {}
            try { rightName = right.DisplayName ?? string.Empty; } catch {}
            return string.Compare(leftName, rightName, StringComparison.Ordinal);
        }

        private static int GetSweepItemQuality(Item item)
        {
            if (item == null)
            {
                return -1;
            }

            try
            {
                return item.Quality;
            }
            catch
            {
                return -1;
            }
        }

        private static int GetSweepItemSortValue(Item item)
        {
            if (item == null)
            {
                return 0;
            }

            try
            {
                int rawValue = item.GetTotalRawValue();
                if (rawValue > 0)
                {
                    return rawValue;
                }
            }
            catch {}

            try
            {
                return item.Value * Math.Max(1, item.StackCount);
            }
            catch
            {
                return 0;
            }
        }

        private static bool CanAfford(int cost)
        {
            if (cost <= 0)
            {
                return true;
            }

            try
            {
                return EconomyManager.IsEnough(new Cost((long)cost), true, true);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[CourierPaidLootSweep] [WARNING] 检查资金失败: " + e.Message);
                return false;
            }
        }

        private static bool TryPay(int cost)
        {
            if (cost <= 0)
            {
                return true;
            }

            try
            {
                Cost payment = new Cost((long)cost);
                return EconomyManager.IsEnough(payment, true, true) && EconomyManager.Pay(payment, true, true);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[CourierPaidLootSweep] [WARNING] 扣费失败: " + e.Message);
                return false;
            }
        }

        private static void TryRefund(int cost, bool shouldRefund)
        {
            if (!shouldRefund || cost <= 0)
            {
                return;
            }

            try
            {
                EconomyManager.Add(cost);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[CourierPaidLootSweep] [WARNING] 退款失败: " + e.Message);
            }
        }

        private static void ShowBubbleOrMessage(Transform npcTransform, string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            try
            {
                if (npcTransform != null)
                {
                    Cysharp.Threading.Tasks.UniTaskExtensions.Forget(
                        DialogueBubblesManager.Show(
                            message,
                            npcTransform,
                            BubbleYOffset,
                            false,
                            false,
                            -1f,
                            BubbleDuration)
                    );
                    return;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[CourierPaidLootSweep] [WARNING] 显示气泡失败: " + e.Message);
            }

            ModBehaviour mod = ModBehaviour.Instance;
            if (mod != null)
            {
                mod.ShowMessage(message);
            }
        }
    }
}
