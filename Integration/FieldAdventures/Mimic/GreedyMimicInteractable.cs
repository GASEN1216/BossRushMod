using UnityEngine;

namespace BossRush
{
    internal sealed class GreedyMimicInteractable : BossRushBuildingInteractableBase
    {
        private GreedyMimicEncounter encounter;
        private int action = -1;
        private string lastName;
        protected override string InteractNameKey { get { return "BossRush_GreedyMimic_Action_" + action; } }
        protected override string LogPrefix { get { return "[GreedyMimic] "; } }
        protected override string InteractionGroupLabel { get { return "[GreedyMimic]"; } }
        protected override float InteractMarkerHeight { get { return 3.2f; } }
        internal void Bind(GreedyMimicEncounter value, int selectedAction)
        {
            encounter = value; action = selectedAction; RefreshName();
        }
        protected override void Awake() { base.Awake(); HideGroupCollider(); }
        protected override void Start() { base.Start(); HideGroupCollider(); }
        private void HideGroupCollider()
        {
            if (action < 0) return;
            MarkerActive = false;
            if (interactCollider != null) interactCollider.enabled = false;
        }
        internal void RefreshName()
        {
            string name = action < 0 ? L10n.T("贪吃宝箱怪：开席", "Gluttonous Mimic: Feast")
                : action == 0 ? L10n.T("投喂 12 发弹药：长出炮管", "Feed 12 rounds: Grow cannons")
                : action == 1 ? L10n.T("投喂 1 颗手雷：长出炸弹瘤", "Feed 1 grenade: Grow bomb sacs")
                : L10n.T("投喂 1 件药品：长出巨型药瓶", "Feed 1 medicine: Grow giant bottle");
            if (name == lastName) return; lastName = name;
            LocalizationHelper.InjectLocalization(InteractNameKey, name); ApplyInteractName("language");
        }
        protected override bool IsBuildingInteractable() { return encounter != null && encounter.CanInteract(action); }
        protected override void OnInteractCompleted() { if (encounter != null) encounter.Interact(action); }
    }
}
