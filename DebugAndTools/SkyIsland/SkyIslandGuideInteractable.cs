using BossRush.Utils;
using UnityEngine;

namespace BossRush
{
    /// <summary>码头和风铃集的航路图服务，使用作者已验证落点，手柄也能查看地图与切换光色。</summary>
    public sealed class SkyIslandGuideInteractable : BossRushBuildingInteractableBase
    {
        private SkyIslandSession session;
        private bool lightingAction;

        internal static void Attach(GameObject root, SkyIslandSession owner)
        {
            foreach (Transform marker in root.GetComponentsInChildren<Transform>(true))
            {
                if (marker.name != "Search_A_02" && marker.name != "Search_B_02") continue;
                SkyIslandGuideInteractable guide = NPCInteractionGroupHelper.GetOrCreateStandaloneInteractable<SkyIslandGuideInteractable>(
                    marker, "SkyIsland_TravelGuide", Vector3.zero, value => value.session = owner);
                if (guide == null) continue;
                SphereCollider trigger = guide.GetComponent<SphereCollider>();
                trigger.isTrigger = true;
                trigger.center = Vector3.up * 0.8f;
                trigger.radius = 0.65f;
                var group = NPCInteractionGroupHelper.PrepareGroupedInteractionOwner(guide, "[SkyIslandGuide]");
                NPCInteractionGroupHelper.AddSubInteractable<SkyIslandGuideInteractable>(guide.transform,
                    "SkyIsland_Lighting", group, value => { value.session = owner; value.lightingAction = true; });
            }
        }

        protected override string InteractNameKey
        {
            get
            {
                string key = lightingAction ? "BossRush_SkyIsland_Lighting" : "BossRush_SkyIsland_Guide";
                LocalizationHelper.InjectLocalization(key, lightingAction
                    ? L10n.T("欣赏下一时段的群岛光色", "View the next island lighting preset")
                    : L10n.T("展开晴岚群岛航路图", "Open the Qinglan chart"));
                return key;
            }
        }
        protected override string LogPrefix { get { return "[SkyIslandGuide] "; } }
        protected override string InteractionGroupLabel { get { return "[SkyIslandGuide]"; } }
        protected override float InteractMarkerHeight { get { return 2.1f; } }
        protected override bool IsBuildingInteractable() { return session != null && session.IsReady; }
        protected override void OnInteractCompleted()
        {
            if (session == null || !session.IsReady) return;
            if (lightingAction) session.CycleLighting();
            else session.OpenMap();
        }
    }
}
