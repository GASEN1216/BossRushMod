using BossRush.Utils;
using Duckov.Utilities;
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
                // 这两个标记本身已经挂着见闻交互体（`SkyIslandSession.PrepareMarkers` 给所有
                // Search* 都挂了），航路图再用 localPosition = zero 就与它**完全同点**。
                // 官方 `CA_Interact` 按距离严格小于取唯一目标，同点时两者距离相等、又不在同一
                // 交互组，结果是航路图和见闻点必有一个永远按不到——手柄玩家连地图都开不了。
                // 与完成纪念物走同一条放置口径，间距常量也共用。
                SkyIslandGuideInteractable guide = NPCInteractionGroupHelper.GetOrCreateStandaloneInteractable<SkyIslandGuideInteractable>(
                    marker, "SkyIsland_TravelGuide", GuideOffset(root.transform, marker), value => value.session = owner);
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

        /// <summary>
        /// 航路图相对锚点标记的**局部**偏移。标记是 root 的直接子物体、且已由
        /// `SkyIslandSession.PrepareMarkers` 断言过单位缩放与零旋转，root 自身同样归一化，
        /// 因此局部偏移与世界偏移数值相同。
        ///
        /// 地面校验失败时退回纯方位角偏移而不是放弃：码头与风铃集都是平坦安全区，
        /// 正常走不到这条分支；真走到了，「落点没过地面校验」也远好过「航路图和见闻点
        /// 叠在一起、必有一个永远按不到」。
        /// </summary>
        private static Vector3 GuideOffset(Transform root, Transform marker)
        {
            float bearing = SkyIslandLootTables.StableHash(marker.name) % 360;
            Vector3 resolved;
            if (SkyIslandRewardCrate.TryFindCratePosition(root, marker.position, bearing,
                SkyIslandRewardCrate.InteractableSeparation,
                GameplayDataSettings.Layers.groundLayerMask.value, out resolved))
                return resolved - marker.position;
            float radians = bearing * Mathf.Deg2Rad;
            Debug.LogWarning("[SkyIslandGuide] " + marker.name + " 附近没有净空落点，航路图按方位角偏移放置。");
            return new Vector3(Mathf.Cos(radians), 0f, Mathf.Sin(radians)) * SkyIslandRewardCrate.InteractableSeparation;
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
