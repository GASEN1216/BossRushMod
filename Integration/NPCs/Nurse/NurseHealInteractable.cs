using System;
using BossRush.Utils;
using Duckov;
using Duckov.Buffs;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 护士治疗交互组件。
    /// 作为护士的子交互选项，玩家选择后执行治疗服务。
    /// </summary>
    public class NurseHealInteractable : InteractableBase
    {
        private const string HealInteractKey = "BossRush_NurseHeal";
        private const float OptionRefreshInterval = 2f;
        private const float InteractStopFallbackStayDuration = 0.05f;

        private NurseNPCController controller;
        private CharacterBuffManager observedBuffManager;
        private bool isInitialized;
        private bool stateListenersRegistered;
        private bool handledDialogueEndThisInteraction;
        private float nextOptionRefreshTime;
        private string lastInjectedHealText = string.Empty;

        protected override void Awake()
        {
            NPCExceptionHandler.TryExecute(() =>
            {
                overrideInteractName = true;
                _overrideInteractNameKey = HealInteractKey;
                InteractName = HealInteractKey;
                interactMarkerOffset = new Vector3(0f, 1f, 0f);
                UpdateHealOptionName();
            }, "NurseHealInteractable.Awake.Setup", false);

            NPCExceptionHandler.TryExecute(
                () => base.Awake(),
                "NurseHealInteractable.Awake.BaseAwake",
                false);

            NPCExceptionHandler.TryExecute(
                () => controller = GetComponentInParent<NurseNPCController>(),
                "NurseHealInteractable.Awake.GetController",
                false);

            NPCExceptionHandler.TryExecute(
                () => MarkerActive = false,
                "NurseHealInteractable.Awake.SetMarkerInactive",
                false);

            isInitialized = true;
        }

        protected override void Start()
        {
            NPCExceptionHandler.TryExecute(
                () => base.Start(),
                "NurseHealInteractable.Start.BaseStart",
                false);

            NPCExceptionHandler.TryExecute(
                RegisterStateListeners,
                "NurseHealInteractable.Start.RegisterStateListeners",
                false);

            NPCExceptionHandler.TryExecute(
                UpdateHealOptionName,
                "NurseHealInteractable.Start.UpdateHealOptionName",
                false);
        }

        private void OnEnable()
        {
            RegisterStateListeners();
            UpdateHealOptionName();
        }

        private void OnDisable()
        {
            UnregisterStateListeners();
        }

        protected override void OnDestroy()
        {
            UnregisterStateListeners();
            base.OnDestroy();
        }

        private void RegisterStateListeners()
        {
            if (!stateListenersRegistered)
            {
                EXPManager.onExpChanged += OnExpChanged;
                stateListenersRegistered = true;
            }

            BindBuffManagerEvents();
        }

        private void UnregisterStateListeners()
        {
            if (stateListenersRegistered)
            {
                EXPManager.onExpChanged -= OnExpChanged;
                stateListenersRegistered = false;
            }

            if (observedBuffManager != null)
            {
                observedBuffManager.onAddBuff -= OnBuffChanged;
                observedBuffManager.onRemoveBuff -= OnBuffChanged;
                observedBuffManager = null;
            }
        }

        private void BindBuffManagerEvents()
        {
            var player = CharacterMainControl.Main;
            var buffManager = player != null ? player.GetBuffManager() : null;
            if (buffManager == observedBuffManager)
            {
                return;
            }

            if (observedBuffManager != null)
            {
                observedBuffManager.onAddBuff -= OnBuffChanged;
                observedBuffManager.onRemoveBuff -= OnBuffChanged;
            }

            observedBuffManager = buffManager;
            if (observedBuffManager != null)
            {
                observedBuffManager.onAddBuff += OnBuffChanged;
                observedBuffManager.onRemoveBuff += OnBuffChanged;
            }
        }

        private void OnExpChanged(long newExp)
        {
            UpdateHealOptionName();
        }

        private void OnBuffChanged(CharacterBuffManager manager, Buff buff)
        {
            NurseHealingService.NotifyDebuffStateChanged();
            UpdateHealOptionName();
        }

        public void UpdateHealOptionName()
        {
            try
            {
                int cost = NurseHealingService.CalculateHealCost();
                string healText = cost <= 0
                    ? L10n.T("治疗（不需要）", "Heal (Not needed)")
                    : L10n.T("治疗（￥" + cost + "）", "Heal ($" + cost + ")");

                if (!string.Equals(lastInjectedHealText, healText, StringComparison.Ordinal))
                {
                    LocalizationHelper.InjectLocalization(HealInteractKey, healText);
                    lastInjectedHealText = healText;
                }

                _overrideInteractNameKey = HealInteractKey;
                InteractName = HealInteractKey;
            }
            catch
            {
                string fallbackText = L10n.T("治疗", "Heal");
                LocalizationHelper.InjectLocalization(HealInteractKey, fallbackText);
                lastInjectedHealText = fallbackText;
                _overrideInteractNameKey = HealInteractKey;
                InteractName = HealInteractKey;
            }
        }

        protected override bool IsInteractable()
        {
            float realtimeSinceStartup = Time.realtimeSinceStartup;
            if (realtimeSinceStartup >= nextOptionRefreshTime)
            {
                nextOptionRefreshTime = realtimeSinceStartup + OptionRefreshInterval;
                UpdateHealOptionName();
            }

            return isInitialized;
        }

        protected override void OnInteractStart(CharacterMainControl interactCharacter)
        {
            try
            {
                base.OnInteractStart(interactCharacter);
                handledDialogueEndThisInteraction = false;

                BossRushAudioManager.Instance?.PlayNPCInteractSFX("nurse_yuzhi");

                if (controller == null)
                {
                    controller = GetComponentInParent<NurseNPCController>();
                }

                if (controller != null)
                {
                    controller.StartDialogue();
                }

                DoHeal();
            }
            catch (Exception ex)
            {
                ModBehaviour.DevLog("[NurseNPC] [ERROR] 治疗交互启动失败: " + ex.Message);
            }
        }

        private void DoHeal()
        {
            try
            {
                int cost;
                NurseHealingService.HealingStatus healingStatus = NurseHealingService.GetHealingStatus(out cost);

                switch (healingStatus)
                {
                    case NurseHealingService.HealingStatus.FullHealthNoDebuff:
                    case NurseHealingService.HealingStatus.InsufficientFunds:
                        if (controller != null)
                        {
                            controller.ShowDialogueBubble(NurseHealingService.GetHealingDialogue(healingStatus));
                            EndDialogueWithMark(10f);
                        }
                        break;

                    case NurseHealingService.HealingStatus.DebuffOnly:
                        if (controller != null)
                        {
                            controller.ShowDialogueBubble(NurseHealingService.GetHealingDialogue(healingStatus));
                        }
                        ExecuteHealing(cost);
                        break;

                    default:
                        ExecuteHealing(cost);
                        break;
                }
            }
            catch (Exception ex)
            {
                ModBehaviour.DevLog("[NurseNPC] [ERROR] 治疗流程失败: " + ex.Message);
                EndDialogueWithMark(5f);
            }
        }

        private void ExecuteHealing(int cost)
        {
            if (cost <= 0)
            {
                if (controller != null)
                {
                    controller.ShowDialogueBubble(L10n.T("你现在不需要治疗。", "No treatment needed right now."));
                    EndDialogueWithMark(5f);
                }
                return;
            }

            if (NurseHealingService.PerformHealing(cost))
            {
                int level = NPCExceptionHandler.TryExecute(
                    () => AffinityManager.GetLevel("nurse_yuzhi"),
                    "NurseHealInteractable.ExecuteHealing.GetAffinityLevel",
                    1,
                    logException: false);

                string specialDialogue = NPCDialogueSystem.GetSpecialDialogue("nurse_yuzhi", "heal_success", level);
                if (controller != null)
                {
                    controller.ShowDialogueBubble(specialDialogue);
                    EndDialogueWithMark(10f);
                }

                ModBehaviour.DevLog("[NurseNPC] 治疗成功，费用: " + cost);
            }
            else
            {
                string failedText = L10n.T(
                    "治疗过程中出现问题，请稍后再试。",
                    "Something went wrong during treatment. Please try again.");

                if (controller != null)
                {
                    controller.ShowDialogueBubble(failedText);
                    EndDialogueWithMark(5f);
                }

                ModBehaviour.DevLog("[NurseNPC] 治疗失败。");
            }

            UpdateHealOptionName();
        }

        private void EndDialogueWithMark(float stayDuration)
        {
            if (controller == null)
            {
                controller = GetComponentInParent<NurseNPCController>();
            }

            if (controller != null)
            {
                handledDialogueEndThisInteraction = true;
                controller.EndDialogueWithStay(stayDuration);
            }
        }

        private void EnsureDialogueEndedOnStop()
        {
            if (controller == null)
            {
                controller = GetComponentInParent<NurseNPCController>();
            }

            if (controller != null && !controller.IsInStoryDialogue)
            {
                controller.EndDialogueWithStay(InteractStopFallbackStayDuration);
            }
        }

        protected override void OnInteractStop()
        {
            NPCExceptionHandler.TryExecute(
                () => base.OnInteractStop(),
                "NurseHealInteractable.OnInteractStop.BaseStop",
                false);

            if (!handledDialogueEndThisInteraction)
            {
                NPCExceptionHandler.TryExecute(
                    EnsureDialogueEndedOnStop,
                    "NurseHealInteractable.OnInteractStop.EnsureDialogueEnded",
                    false);
            }

            handledDialogueEndThisInteraction = false;
        }
    }
}
