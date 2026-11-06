using System;
using Duckov.MiniMaps;
using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    public sealed class TidegateLeadUsage : UsageBehavior
    {
        internal const string PinKey = "BossRush_Tidegate_SoundingLead_Cache";
        public override DisplaySettingsData DisplaySettings { get { return new DisplaySettingsData { display = true,
            description = L10n.T("使用：探明最近一处未搜的埋货（潮闸镇内）。", "Use: sound for the nearest unsearched buried cache in Tidegate.") }; } }
        public override bool CanBeUsed(Item item, object user) { return item != null && CharacterMainControl.Main != null && (user as CharacterMainControl) == CharacterMainControl.Main; }
        protected override void OnUse(Item item, object user)
        {
            if (!CanBeUsed(item, user)) return;
            try
            {
                // 同风标罗盘：仅在主角按下使用时查当前会话，不缓存宿主，也不在每帧扫描。
                var session = UnityEngine.Object.FindObjectOfType<TidegateSession>();
                if (session != null && session.TrySoundLead()) return;
                Duckov.UI.NotificationText.Push(L10n.T("这把响铊只认得潮闸镇的泥滩。", "This lead only knows the mudflats of Tidegate."));
            }
            catch (Exception e) { Debug.LogWarning("[Tidegate] 响铊测深失败：" + e.Message); }
        }
    }

    internal sealed partial class TidegateSession
    {
        private GameObject soundingPin;
        internal bool TrySoundLead()
        {
            if (!IsSessionValid() || loot == null) return false;
            Vector3 mark;
            if (soundingPin != null) { soundingPin.SetActive(false); Destroy(soundingPin); soundingPin = null; }
            if (!loot.NearestBuried(player.transform.position, out mark))
                Status(L10n.T("响铊沉底：这趟的埋货都搜过了。", "The lead settles. No buried caches left to search."), false);
            else
            {
                soundingPin = new GameObject("TidegateSoundingLeadPin");
                soundingPin.transform.SetParent(root.transform, false); soundingPin.transform.position = mark;
                var pin = soundingPin.AddComponent<SimplePointOfInterest>();
                pin.Color = new Color(.38f, .91f, .85f); pin.IsArea = true; pin.AreaRadius = 2.5f; pin.Setup(null, TidegateLeadUsage.PinKey);
                Destroy(soundingPin, 25f); // 归当趟世界所有；提前离图也会随父节点清理。
                Status(tide.IsDeepAt(mark) ? L10n.T("响铊碰到了货。这处还在水下；按 M 看青圈，等退潮。", "Sounding found cargo underwater. Look for the blue-green circle on M and wait for the ebb.")
                    : L10n.T("响铊碰到了货，泥面已经露出。按 M 看青圈。", "The lead found cargo on exposed mud. Look for the blue-green circle on M."), false);
            }
            return true;
        }
    }

    internal sealed partial class TidegateLoot
    {
        internal bool NearestBuried(Vector3 from, out Vector3 mark)
        {
            float best = float.MaxValue; mark = Vector3.zero;
            foreach (Point p in points)
            {
                if (p.Kind != "buried" || p.Opened || p.Failed || p.Marker == null) continue;
                Vector3 here = p.Marker.position;
                float distance = (here - from).sqrMagnitude;
                if (distance >= best) continue;
                best = distance; mark = here;
            }
            return best < float.MaxValue;
        }
    }
}
