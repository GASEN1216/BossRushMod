using System;
using System.Collections.Generic;
using System.Reflection;
using Duckov.MiniMaps;
using Duckov.MiniMaps.UI;
using Duckov.Scenes;
using HarmonyLib;
using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BossRush
{
    /// <summary>
    /// COMPAT / WIRE+：会话持有的地图数据租约。复用官方地图窗口和 POI prefab，
    /// 临时接管地图数据并返还原引用；不修改官方场景身份、不写地堡地图标记存档。
    /// </summary>
    internal sealed class StoneOutpostMap : IDisposable
    {
        internal static StoneOutpostMap Active { get; private set; }
        private StoneOutpostMapDataLease dataLease;
        private Sprite sprite;
        private Texture2D texture;
        private GameObject pointsRoot;
        private SimplePointOfInterest playerPoint;
        private SimplePointOfInterest pin;
        private readonly Dictionary<string, SimplePointOfInterest> searches = new Dictionary<string, SimplePointOfInterest>();
        private Vector3 origin;
        private string sceneID;
        private bool disposed;
        private FieldInfo nameField;
        private FieldInfo infoField;
        private string previousName;
        private string previousInfo;

        internal void Apply(GameObject arena, Vector3 center)
        {
            if (Active != null) throw new InvalidOperationException("前哨地图已有 owner");
            MiniMapSettings settings = MiniMapSettings.Instance;
            sceneID = MultiSceneCore.ActiveSubSceneID;
            if (settings == null || string.IsNullOrEmpty(sceneID) || SceneInfoCollection.GetSceneInfo(sceneID) == null)
                throw new InvalidOperationException("官方地图服务未就绪");
            nameField = AccessTools.Field(typeof(MiniMapView), "mapNameText");
            infoField = AccessTools.Field(typeof(MiniMapView), "mapInfoText");
            if (nameField == null || infoField == null) throw new MissingFieldException("官方地图标题字段已变化");
            origin = center;
            var footprints = new List<Rect>();
            foreach (BoxCollider box in arena.GetComponentsInChildren<BoxCollider>(true))
            {
                if (box.isTrigger || box.name == "COL_Ground") continue;
                Bounds bounds = box.bounds;
                footprints.Add(Rect.MinMaxRect(bounds.min.x - center.x, bounds.min.z - center.z,
                    bounds.max.x - center.x, bounds.max.z - center.z));
            }
            texture = new Texture2D(StoneOutpostMapRaster.Resolution, StoneOutpostMapRaster.Resolution, TextureFormat.RGBA32, false);
            texture.name = "StoneOutpost_TacticalMap";
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.SetPixels32(StoneOutpostMapRaster.Draw(footprints, Opaque(BossRushUIColors.SurfaceRaised),
                Opaque(BossRushUIColors.Header), Opaque(BossRushUIColors.TextSecondary), Opaque(BossRushUIColors.TextPrimary)));
            texture.Apply(false, true);
            sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), 1f);
            var ownedMaps = new List<MiniMapSettings.MapEntry> { new MiniMapSettings.MapEntry
            {
                sceneID = sceneID, sprite = sprite, imageWorldSize = StoneOutpostMapRaster.WorldSize,
                mapWorldCenter = origin, hide = false, noSignal = false
            } };
            dataLease = new StoneOutpostMapDataLease(settings, ownedMaps, origin, StoneOutpostMapRaster.WorldSize);
            Active = this;
            pointsRoot = new GameObject("StoneOutpost_MapPoints");
            pointsRoot.transform.SetParent(arena.transform, false);
            foreach (Transform marker in arena.GetComponentsInChildren<Transform>(true))
            {
                if (marker.GetComponent<StoneOutpostSearchPoint>() != null)
                    searches.Add(marker.name, CreatePoint(marker.position, SearchLabel(marker.name), BossRushUIColors.WarningText));
                else if (marker.name == "Exit")
                    CreatePoint(marker.position, L10n.T("撤离 · 停留3秒", "Extract · hold 3s"), BossRushUIColors.RarityRare);
                else if (marker.name == "PlayerSpawn")
                    CreatePoint(marker.position, L10n.T("入口", "Entry"), BossRushUIColors.TextSecondary);
            }
            CreatePoint(origin + new Vector3(0, 0, 46), L10n.T("北 N", "North N"), BossRushUIColors.TextPrimary);
            playerPoint = CreatePoint(CharacterMainControl.Main.transform.position, L10n.T("你", "You"), BossRushUIColors.Accent);
            playerPoint.ScaleFactor = 1.25f;
            MiniMapView view = MiniMapView.Instance;
            if (view != null)
            {
                previousName = (nameField.GetValue(view) as TMP_Text)?.text;
                previousInfo = (infoField.GetValue(view) as TMP_Text)?.text;
                view.LoadCurrent();
                RefreshTitle(view);
            }
            Debug.Log("[StoneOutpost] MAP_READY size=100 footprints=" + footprints.Count + " searches=" + searches.Count);
        }

        private static Color32 Opaque(Color color) { color.a = 1; return color; }
        private static string SearchLabel(string key)
        {
            if (key == "Search0") return L10n.T("西仓记录", "West records");
            if (key == "Search1") return L10n.T("东仓记录", "East records");
            return L10n.T("调度记录", "Dispatch records");
        }
        private SimplePointOfInterest CreatePoint(Vector3 position, string label, Color color)
        {
            var obj = new GameObject("OutpostPOI_" + label);
            obj.SetActive(false);
            obj.transform.SetParent(pointsRoot.transform, false);
            obj.transform.position = position;
            var point = obj.AddComponent<SimplePointOfInterest>();
            point.Color = color;
            point.ShadowColor = BossRushUIColors.Surface;
            point.Setup(displayName: label, overrideSceneID: sceneID);
            obj.SetActive(true);
            return point;
        }

        internal bool OwnsDisplay(MiniMapDisplay display)
        {
            return !disposed && display != null && MiniMapView.Instance != null &&
                display.GetComponentInParent<MiniMapView>() == MiniMapView.Instance;
        }
        internal bool OwnsPoint(MonoBehaviour point)
        {
            return point != null && pointsRoot != null && point.transform.IsChildOf(pointsRoot.transform);
        }
        internal void Tick()
        {
            if (playerPoint != null && CharacterMainControl.Main != null)
                playerPoint.transform.position = CharacterMainControl.Main.transform.position;
        }
        internal void MarkSearched(string key)
        {
            SimplePointOfInterest point;
            if (!searches.TryGetValue(key, out point) || point == null) return;
            point.Color = BossRushUIColors.SuccessText;
            point.Setup(displayName: SearchLabel(key) + L10n.T(" · 已搜索", " · searched"), overrideSceneID: sceneID);
        }
        internal void PlacePin(Vector3 position)
        {
            Vector3 local = position - origin;
            if (Mathf.Abs(local.x) > 50 || Mathf.Abs(local.z) > 50) return;
            if (pin != null) { pin.gameObject.SetActive(false); Object.Destroy(pin.gameObject); }
            pin = CreatePoint(position, L10n.T("临时标记", "Session pin"), BossRushUIColors.RarityEpic);
        }
        internal void RefreshTitle(MiniMapView view)
        {
            if (disposed || view == null) return;
            TMP_Text title = nameField.GetValue(view) as TMP_Text;
            TMP_Text info = infoField.GetValue(view) as TMP_Text;
            if (title != null) title.text = L10n.T("石堡前哨", "Stone Outpost");
            if (info != null) info.text = L10n.T("100 × 100 米 · 网格 10 米\n金色：记录  绿色：已搜索  蓝色：撤离\n右键：临时标记（离场清除）",
                "100 × 100 m · 10 m grid\nGold: records  Green: searched  Blue: extract\nRight-click: session pin");
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (ReferenceEquals(Active, this)) Active = null;
            // 先停用 POI，官方显示池同步退还条目，再销毁目标与地图纹理。
            if (pointsRoot != null) { pointsRoot.SetActive(false); Object.Destroy(pointsRoot); }
            searches.Clear();
            if (dataLease != null) dataLease.Dispose();
            try
            {
                MiniMapView view = MiniMapView.Instance;
                if (view != null && dataLease != null)
                {
                    view.LoadCurrent();
                    TMP_Text title = nameField.GetValue(view) as TMP_Text;
                    TMP_Text info = infoField.GetValue(view) as TMP_Text;
                    SceneInfoEntry sceneInfo = MultiSceneCore.Instance != null ? MultiSceneCore.Instance.SceneInfo : null;
                    if (title != null) title.text = sceneInfo != null ? sceneInfo.DisplayName : previousName ?? "";
                    if (info != null) info.text = sceneInfo != null ? sceneInfo.Description : previousInfo ?? "";
                    view.CeneterPlayer();
                }
            }
            catch (Exception e) { Debug.LogWarning("[StoneOutpost] map restore: " + e.Message); }
            if (sprite != null) Object.Destroy(sprite);
            if (texture != null) Object.Destroy(texture);
            dataLease = null;
            Debug.Log("[StoneOutpost] MAP_RESTORED");
        }
    }

    [HarmonyPatch(typeof(MiniMapView), "OnOpen")]
    internal static class StoneOutpostMapTitlePatch
    {
        private static void Postfix(MiniMapView __instance)
        {
            if (StoneOutpostMap.Active != null) StoneOutpostMap.Active.RefreshTitle(__instance);
        }
    }

    [HarmonyPatch(typeof(MiniMapDisplay), "AutoSetup")]
    internal static class StoneOutpostMapSetupPatch
    {
        private static bool Prefix(MiniMapDisplay __instance)
        {
            StoneOutpostMap map = StoneOutpostMap.Active;
            if (map == null || !map.OwnsDisplay(__instance)) return true;
            __instance.Setup(MiniMapSettings.Instance);
            return false;
        }
    }

    [HarmonyPatch(typeof(MiniMapDisplay), "HandlePointOfInterest")]
    internal static class StoneOutpostMapPointsPatch
    {
        private static bool Prefix(MiniMapDisplay __instance, MonoBehaviour poi)
        {
            StoneOutpostMap map = StoneOutpostMap.Active;
            return map == null || !map.OwnsDisplay(__instance) || map.OwnsPoint(poi);
        }
    }

    [HarmonyPatch(typeof(MiniMapDisplayEntry), "OnPointerClick")]
    internal static class StoneOutpostMapPinPatch
    {
        private static bool Prefix(MiniMapDisplayEntry __instance, UnityEngine.EventSystems.PointerEventData eventData)
        {
            StoneOutpostMap map = StoneOutpostMap.Active;
            if (map == null || !map.OwnsDisplay(__instance.Master)) return true;
            if (eventData.button == UnityEngine.EventSystems.PointerEventData.InputButton.Right)
            {
                Vector3 world;
                if (__instance.Master.TryConvertToWorldPosition(eventData.position, out world)) map.PlacePin(world);
                eventData.Use();
            }
            return false;
        }
    }
}
