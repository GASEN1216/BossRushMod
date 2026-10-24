using UnityEngine;

namespace BossRush
{
    /// <summary>同一官方交互用于叮当组内选项及缺席时的试验台。</summary>
    internal sealed class DangerousPrototypeInteractable : BossRushBuildingInteractableBase
    {
        internal DangerousPrototypeRuntimeModule Runtime;
        internal bool GroupMember;
        private string lastLabel;
        protected override void Awake() { base.Awake(); HideGroupCollider(); }
        protected override void Start() { base.Start(); HideGroupCollider(); }
        private void HideGroupCollider()
        {
            if (!GroupMember) return;
            MarkerActive = false;
            if (interactCollider != null) interactCollider.enabled = false;
        }
        protected override string InteractNameKey
        {
            get
            {
                const string key = "BossRush_DangerousPrototype";
                RefreshLabel();
                return key;
            }
        }
        protected override string LogPrefix { get { return "[DangerousPrototype] "; } }
        protected override string InteractionGroupLabel { get { return "[DangerousPrototype]"; } }
        protected override float InteractMarkerHeight { get { return 1.4f; } }
        protected override bool IsBuildingInteractable()
        {
            // 同步取语言：官方在靠近时查询可用性，语言切换无需重建交互。
            RefreshLabel();
            return Runtime != null && Runtime.CanInteract;
        }
        private void RefreshLabel()
        {
            string label = L10n.T("危险试用品", "Dangerous prototypes");
            if (label == lastLabel) return;
            lastLabel = label;
            LocalizationHelper.InjectLocalization("BossRush_DangerousPrototype", label);
        }
        protected override void OnInteractCompleted()
        {
            if (Runtime != null) Runtime.Open(this);
        }
    }
}
