using System;
using System.Collections.Generic;
using BossRush;

// 只替代 Unity 布局/字体测量和官方建筑容器；待测方法由 run.py 从生产源码逐字抽取。
namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public static bool operator ==(Object a, Object b)
        {
            bool an = ReferenceEquals(a, null) || a.Destroyed;
            bool bn = ReferenceEquals(b, null) || b.Destroyed;
            return an || bn ? an == bn : ReferenceEquals(a, b);
        }
        public static bool operator !=(Object a, Object b) { return !(a == b); }
        public override bool Equals(object other) { return this == other as Object; }
        public override int GetHashCode() { return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this); }
    }
    public class Component : Object { public string name; }
    public class GameObject : Object
    {
        public Component Component;
        public Component GetComponent(Type type) { return type.IsInstanceOfType(Component) ? Component : null; }
    }
    public class Sprite : Object { }
    public class Transform : Component { }
    public struct Vector2 { public float x, y; public Vector2(float x, float y) { this.x = x; this.y = y; } }
    public struct Vector3
    {
        public float x, y, z;
        public static Vector3 one { get { return new Vector3 { x = 1, y = 1, z = 1 }; } }
        public static Vector3 operator *(Vector3 v, float f) { return new Vector3 { x = v.x * f, y = v.y * f, z = v.z * f }; }
        public static bool operator ==(Vector3 a, Vector3 b) { return a.x == b.x && a.y == b.y && a.z == b.z; }
        public static bool operator !=(Vector3 a, Vector3 b) { return !(a == b); }
        public override bool Equals(object value) { return value is Vector3 && this == (Vector3)value; }
        public override int GetHashCode() { return x.GetHashCode(); }
    }
    public struct Rect { public float width, height; }
    public class RectTransform : Transform
    {
        public enum Axis { Horizontal, Vertical }
        public Rect rect;
        public Vector2 anchorMin = new Vector2(.5f, .5f), anchorMax = new Vector2(.5f, .5f);
        public Vector2 anchoredPosition, pivot = new Vector2(.5f, .5f);
        public readonly List<RectTransform> Children = new List<RectTransform>();
        public int childCount { get { return Children.Count; } }
        public Transform GetChild(int index) { return Children[index]; }
        public Vector3 localScale = Vector3.one;
        public TMPro.TextMeshProUGUI Text;
        public T GetComponent<T>() where T : class { return Text as T; }
        public void SetSizeWithCurrentAnchors(Axis axis, float value)
        { if (axis == Axis.Vertical) rect.height = value; else rect.width = value; }
    }
    static class Mathf
    {
        public static float Max(float a, float b) { return Math.Max(a, b); }
        public static float Min(float a, float b) { return Math.Min(a, b); }
    }
}
namespace UnityEngine.UI { class ScrollRect { public float verticalNormalizedPosition; } }
namespace TMPro
{
    public class TextMeshProUGUI
    {
        public UnityEngine.RectTransform rectTransform;
        public float MeasuredHeight;
    }
}
namespace Duckov.Economy
{
    struct Cost { public long money; public Cost(long amount) { money = amount; } }
}
namespace Duckov.Buildings
{
    class Building : UnityEngine.Component { public string ID { get; set; } }
    struct BuildingInfo
    {
        public string id, prefabName;
        public int maxAmount;
        public string[] requireBuildings, alternativeFor;
        public int[] requireQuests;
        public UnityEngine.Sprite iconReference;
        public Duckov.Economy.Cost cost;
    }
    class BuildingDataCollection
    {
        public static BuildingDataCollection Instance { get; set; }
        private List<BuildingInfo> infos = new List<BuildingInfo>();
        private List<Building> prefabs = new List<Building>();
        public object readonlyInfos;
        public List<BuildingInfo> Infos { get { return infos; } }
        public List<Building> Prefabs { get { return prefabs; } }
    }
}
namespace BossRush
{
    static class BuildingInjectionHelper
    {
        public static bool MissingCost;
        public static Type FindGameType(string name)
        { return MissingCost && name == "Duckov.Economy.Cost" ? null : typeof(BuildingInjectionHelper).Assembly.GetType(name); }
        public static Type GetBuildingType() { return typeof(Duckov.Buildings.Building); }
        public static System.Reflection.PropertyInfo GetBuildingIdProperty() { return GetBuildingType().GetProperty("ID"); }
    }
    static class BossRushUI
    {
        public static float MeasureTextHeight(TMPro.TextMeshProUGUI text, float width, float minimum)
        {
            float height = Math.Max(text.MeasuredHeight, minimum);
            text.rectTransform.rect = new UnityEngine.Rect { width = width, height = height };
            return height;
        }
    }
    partial class DailyReportMailboxBuilder
    {
        private const string DAILYREPORT_BUILDING_ID = DailyReportTuning.MailboxBuildingId;
        private const string DAILYREPORT_PREFAB_NAME = "BossRushDailyMailbox";
        private const int DAILYREPORT_BUILDING_MAX_AMOUNT = DailyReportTuning.MailboxMaxAmount;
        private const long DAILYREPORT_BUILDING_COST = DailyReportTuning.MailboxCost;
        private UnityEngine.GameObject dailyReportBuildingPrefabGO;
        private UnityEngine.Sprite dailyReportBuildingIcon;
        internal DailyReportMailboxBuilder(UnityEngine.GameObject prefab) { dailyReportBuildingPrefabGO = prefab; }
        internal bool Register() { return InjectDailyReportBuildingData(); }
    }
    partial class DailyReportView
    {
        private const float Margin = 28, PanelWidth = 1000, PanelHeight = 760;
        private UnityEngine.RectTransform panelRect, paperFrame, signInArea;
        private UnityEngine.Transform transform;
        private TMPro.TextMeshProUGUI signInStatusText;
        private UnityEngine.UI.ScrollRect paperScroll;
        private readonly List<PaperRow> paperRows = new List<PaperRow>();
        private static UnityEngine.RectTransform TextRect(float height)
        {
            var rect = new UnityEngine.RectTransform { rect = new UnityEngine.Rect { width = 450, height = 20 } };
            rect.Text = new TMPro.TextMeshProUGUI { rectTransform = rect, MeasuredHeight = height };
            return rect;
        }
        internal static void Verify(Action<bool, string> check)
        {
            var view = new DailyReportView { panelRect = new UnityEngine.RectTransform(),
                paperScroll = new UnityEngine.UI.ScrollRect { verticalNormalizedPosition = .4f } };
            var left = TextRect(80); var right = TextRect(340); var below = TextRect(190);
            view.paperRows.Add(new PaperRow(left, 100, 6, right));
            view.paperRows.Add(new PaperRow(below, 118, 4));
            view.ReflowPaper();
            check(left.rect.height == 340 && right.rect.height == 340, "paper columns reserve the taller text height");
            check(below.anchoredPosition.y + below.rect.height / 2 <= left.anchoredPosition.y - left.rect.height / 2 - 6,
                "long column cannot overlap bounty row");
            check(view.panelRect.rect.height >= -below.anchoredPosition.y + below.rect.height / 2 + Margin,
                "scroll content includes the complete final row");
            check(view.paperScroll.verticalNormalizedPosition == .4f, "refresh preserves the reader's scroll position");
            right.Text.MeasuredHeight = 40; below.Text.MeasuredHeight = 20;
            view.ReflowPaper();
            check(left.rect.height == 100 && below.rect.height == 118, "shorter language shrinks back to row minimums");
            view.signInArea = new UnityEngine.RectTransform { rect = new UnityEngine.Rect { width = 944, height = 210 } };
            var status = TextRect(260);
            status.rect.height = 104;
            status.anchoredPosition = new UnityEngine.Vector2(300, -8);
            view.signInStatusText = status.Text;
            var button = new UnityEngine.RectTransform { rect = new UnityEngine.Rect { width = 200, height = 46 },
                anchoredPosition = new UnityEngine.Vector2(300, 74) };
            view.signInArea.Children.Add(button); view.signInArea.Children.Add(status);
            view.PinSignInContentToTop();
            view.paperRows.Add(new PaperRow(view.signInArea, 210, 4));
            view.ReflowPaper();
            check(TopOffset(view.signInArea, status) >= TopOffset(view.signInArea, button) + button.rect.height + 6,
                "long sign-in status stays below the sign-in button");
            check(TopOffset(view.signInArea, status) + status.rect.height <= view.signInArea.rect.height,
                "sign-in row contains the complete long status");
            float buttonTop = TopOffset(view.signInArea, button), statusTop = TopOffset(view.signInArea, status);
            status.Text.MeasuredHeight = 60;
            view.ReflowPaper();
            check(view.signInArea.rect.height == 210 && status.rect.height == 104
                && TopOffset(view.signInArea, button) == buttonTop && TopOffset(view.signInArea, status) == statusTop,
                "short sign-in status restores row height without moving controls");
            view.paperFrame = new UnityEngine.RectTransform();
            var root = new UnityEngine.RectTransform(); view.transform = root;
            foreach (var size in new[] { new UnityEngine.Vector2(640, 480), new UnityEngine.Vector2(1280, 720),
                new UnityEngine.Vector2(1920, 1080), new UnityEngine.Vector2(1080, 1920) })
            {
                root.rect = new UnityEngine.Rect { width = size.x, height = size.y }; view.FitPaper();
                check(PanelWidth * view.paperFrame.localScale.x <= size.x - 23.9f
                    && PanelHeight * view.paperFrame.localScale.y <= size.y - 23.9f,
                    "paper and fixed close button fit viewport " + size.x + "x" + size.y);
            }
            string body = BuildBountyBlock(new DailyReportIssue { TodayBountyTarget = 1, TodayBountyTitle = "Return",
                TodayBountyStatus = "Failed today", TodayBountyCash = 1500 });
            check(body.Contains("Failed today") && body.Contains("1500"), "bounty block retains explicit status and reward");
        }
        private static float TopOffset(UnityEngine.RectTransform parent, UnityEngine.RectTransform child)
        {
            return parent.rect.height * (1 - child.anchorMin.y) - child.anchoredPosition.y
                - child.rect.height * (1 - child.pivot.y);
        }
    }
}
partial class Program
{
    static void PresentationAndMailbox()
    {
        DailyReportView.Verify(Check);
        var building = new Duckov.Buildings.Building { name = "BossRushDailyMailbox" };
        var builder = new DailyReportMailboxBuilder(new UnityEngine.GameObject { Component = building });
        Duckov.Buildings.BuildingDataCollection.Instance = null;
        Check(!builder.Register(), "unready building collection cannot report registration success");
        var collection = new Duckov.Buildings.BuildingDataCollection();
        Duckov.Buildings.BuildingDataCollection.Instance = collection;
        Check(!builder.Register() && collection.Infos.Count == 0, "unbound prefab identity cannot expose a mailbox entry");
        building.ID = DailyReportTuning.MailboxBuildingId;
        BuildingInjectionHelper.MissingCost = true;
        Check(!builder.Register() && collection.Infos.Count == 0 && collection.Prefabs.Count == 0,
            "missing cost cannot expose a free or partial mailbox entry");
        BuildingInjectionHelper.MissingCost = false;
        Check(builder.Register() && collection.Infos.Count == 1 && collection.Prefabs.Count == 1,
            "registration recovers after official dependencies become available");
        var info = collection.Infos[0];
        Check(info.cost.money == 500 && info.maxAmount == 1 && info.prefabName == building.name
            && info.requireBuildings != null && info.requireQuests != null && info.alternativeFor != null,
            "mailbox cost, limit, prefab and required arrays are fully wired");
        collection.Prefabs.Clear();
        Check(builder.Register() && collection.Prefabs.Count == 1 && collection.Infos.Count == 1,
            "metadata-only registration repairs the missing prefab without duplicating metadata");
        building.Destroyed = true;
        var replacement = new Duckov.Buildings.Building { name = "BossRushDailyMailbox", ID = DailyReportTuning.MailboxBuildingId };
        builder = new DailyReportMailboxBuilder(new UnityEngine.GameObject { Component = replacement });
        Check(builder.Register() && collection.Prefabs.Contains(replacement), "destroyed Unity prefab does not block replacement");
        int count = collection.Prefabs.Count;
        Check(builder.Register() && collection.Prefabs.Count == count && collection.Infos.Count == 1,
            "complete registration is idempotent");
    }
}
