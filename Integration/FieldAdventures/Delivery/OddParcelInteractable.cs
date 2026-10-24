using System;
using System.Reflection;
using UnityEngine;

namespace BossRush
{
    internal enum OddParcelAction { Offer, Carry, Deliver, Inspect }

    /// <summary>借官方交互组；子项的构造、退组均归当前 runtime，不改阿稳原有选项。</summary>
    public sealed class OddParcelInteractable : BossRushBuildingInteractableBase
    {
        internal OddParcelRuntimeModule Runtime;
        internal OddParcelAction Action;
        private bool groupMember;
        private string labelKey = "BossRush_OddParcel_Offer";
        private string resolvedLabel;
        private InteractHUD labelHud;
        private MethodInfo refreshHudContent, refreshHudSelection;
        private FieldInfo hudMaster, hudIndex;
        protected override string InteractNameKey { get { return labelKey; } }
        protected override string LogPrefix { get { return "[OddParcel] "; } }
        protected override string InteractionGroupLabel { get { return "[OddParcel]"; } }
        protected override float InteractMarkerHeight { get { return 1.25f; } }

        internal void Configure(OddParcelRuntimeModule runtime, OddParcelAction action, bool isGroupMember = false)
        {
            Runtime = runtime;
            Action = action;
            groupMember = isGroupMember;
            labelKey = "BossRush_OddParcel_" + action;
            RefreshLabel();
        }

        protected override void Awake()
        {
            base.Awake();
            HideOwnGroupMarker();
        }

        protected override void Start()
        {
            base.Start();
            HideOwnGroupMarker();
        }

        private void HideOwnGroupMarker()
        {
            if (!groupMember) return;
            MarkerActive = false;
            if (interactCollider != null) interactCollider.enabled = false;
        }

        internal void RefreshLabel()
        {
            if (Runtime == null) return;
            string next = Runtime.InteractionLabel(Action);
            bool changed = next != resolvedLabel;
            if (changed) LocalizationHelper.InjectLocalization(labelKey, next);
            ApplyInteractName("parcel label");
            if (!changed) return;
            resolvedLabel = next;
            RefreshVisibleLabel();
        }

        private void ResolveLabelHud()
        {
            if (labelHud != null) return;
            labelHud = UnityEngine.Object.FindObjectOfType<InteractHUD>();
            // The official HUD lives below this raid's LevelManager; its canvas may be inactive during dialogue.
            if (labelHud == null && LevelManager.Instance != null)
                labelHud = LevelManager.Instance.GetComponentInChildren<InteractHUD>(true);
        }

        private void RefreshVisibleLabel()
        {
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null || player.interactAction == null) return;
            InteractableBase master = player.interactAction.MasterInteractableAround;
            if (master == null || !master.GetInteractableList().Contains(this)) return;
            ResolveLabelHud();
            if (labelHud == null) return;
            // 官方只在主目标变化时复制文字。直接刷新已绑定行，保留组内选择、指示器与选中事件。
            InteractSelectionHUD[] rows = labelHud.GetComponentsInChildren<InteractSelectionHUD>(true);
            for (int i = 0; i < rows.Length; i++)
                if (rows[i].gameObject.activeSelf && rows[i].InteractTarget == this && rows[i].text != null) rows[i].text.text = GetInteractName();
        }

        internal void RefreshVisibleGroup(InteractableBase master, InteractableBase previousSelection)
        {
            try
            {
                CharacterMainControl player = CharacterMainControl.Main;
                if (player == null || player.interactAction == null || player.interactAction.MasterInteractableAround != master) return;
                ResolveLabelHud();
                if (labelHud == null || labelHud.master == null) return;
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                if (hudMaster == null) hudMaster = typeof(InteractHUD).GetField("interactableMaster", flags);
                if (hudIndex == null) hudIndex = typeof(InteractHUD).GetField("interactableIndexTemp", flags);
                if (refreshHudContent == null) refreshHudContent = typeof(InteractHUD).GetMethod("RefreshContent", flags, null, Type.EmptyTypes, null);
                if (refreshHudSelection == null) refreshHudSelection = typeof(InteractHUD).GetMethod("RefreshSelection", flags, null, Type.EmptyTypes, null);
                if (hudMaster == null || hudIndex == null || refreshHudContent == null || refreshHudSelection == null
                    || !ReferenceEquals(hudMaster.GetValue(labelHud), master)) return;
                InteractableBase selected = player.interactAction.InteractTarget;
                bool retained = false;
                InteractSelectionHUD[] oldRows = labelHud.GetComponentsInChildren<InteractSelectionHUD>(true);
                for (int i = 0; i < oldRows.Length; i++)
                    if (oldRows[i].gameObject.activeSelf && selected == previousSelection && oldRows[i].InteractTarget == selected
                        && oldRows[i].selectIndicator != null && oldRows[i].selectIndicator.activeSelf) retained = true;
                // Official content is only invalidated when the master changes. Rebind this group without clearing it.
                refreshHudContent.Invoke(labelHud, null);
                InteractSelectionHUD retainedRow = null;
                InteractSelectionHUD[] rows = labelHud.GetComponentsInChildren<InteractSelectionHUD>(true);
                if (retained)
                    for (int i = 0; i < rows.Length; i++)
                        if (rows[i].gameObject.activeSelf && rows[i].InteractTarget == selected) { retainedRow = rows[i]; break; }
                var selectedEvent = retainedRow != null ? retainedRow.OnSelectedEvent : null;
                try
                {
                    // SetSelection(true) always invokes its event, even when the selected object did not change.
                    if (retainedRow != null) retainedRow.OnSelectedEvent = null;
                    refreshHudSelection.Invoke(labelHud, null);
                    hudIndex.SetValue(labelHud, player.interactAction.InteractIndexInGroup);
                }
                finally { if (retainedRow != null) retainedRow.OnSelectedEvent = selectedEvent; }
            }
            catch (Exception e) { ModBehaviour.DevLog("[OddParcel] [WARNING] 交互组刷新失败：" + e.Message); }
        }

        protected override bool IsBuildingInteractable()
        {
            return Runtime != null && Runtime.CanInteract(Action);
        }

        protected override void OnInteractCompleted()
        {
            if (Runtime != null && Runtime.CanInteract(Action)) Runtime.Interact(Action, this);
        }
    }
}
