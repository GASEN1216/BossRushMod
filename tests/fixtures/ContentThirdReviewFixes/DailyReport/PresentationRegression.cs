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
    public struct Rect
    {
        public float x, y, width, height;
        public Rect(float x, float y, float width, float height)
        { this.x = x; this.y = y; this.width = width; this.height = height; }
    }
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
    // 版面表要读 Assets/Data/DailyReportLayout.json；夹具直接从仓库读真实文件，
    // 读不到就让版面表走它自己的硬编码兜底——两条路都要能跑通。
    static class JsonDataRegistry
    {
        public static bool TryReadDataFile(string fileName, out string json)
        {
            json = null;
            try
            {
                string path = System.IO.Path.Combine(
                    System.IO.Path.Combine(System.AppContext.BaseDirectory, RepositoryRelativeDataPath), fileName);
                if (!System.IO.File.Exists(path)) return false;
                json = System.IO.File.ReadAllText(path, System.Text.Encoding.UTF8);
                return true;
            }
            catch (Exception) { return false; }
        }

        // Build/content-third-review-fixes/... -> 仓库根 -> Assets/Data
        private const string RepositoryRelativeDataPath =
            "../../../../../../../Assets/Data";
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
        private UnityEngine.RectTransform paperFrame;
        private UnityEngine.Transform transform;

        internal static void Verify(Action<bool, string> check)
        {
            // 1) 版面表本身：每块都要落在面板里，宽高为正
            string[] names = {
                "header", "mascot", "title", "info", "infoMeta", "infoWeather",
                "income", "incomePill", "incomeLeft", "incomeRight", "incomeTip", "incomeNote",
                "status", "statusPill", "statusLeft", "statusRight", "statusLuck", "statusTaboo",
                "signin", "signinPill", "button", "sideText", "legend",
            };
            foreach (string name in names)
            {
                UnityEngine.Rect rect = DailyReportLayoutTable.Get(name);
                check(rect.width > 0 && rect.height > 0, name + " has a positive size");
                check(rect.x >= 0 && rect.y >= 0
                    && rect.x + rect.width <= DailyReportLayoutTable.PanelWidth
                    && rect.y + rect.height <= DailyReportLayoutTable.PanelHeight,
                    name + " stays inside the panel");
            }

            // 2) 三张主卡片不得相互重叠：文字块可以嵌在卡里，卡与卡之间不行
            string[][] pairs = {
                new[] { "header", "income" }, new[] { "header", "status" },
                new[] { "income", "status" }, new[] { "income", "signin" },
                new[] { "status", "signin" }, new[] { "header", "signin" },
            };
            foreach (string[] pair in pairs)
            {
                check(!Overlaps(DailyReportLayoutTable.Get(pair[0]), DailyReportLayoutTable.Get(pair[1])),
                    pair[0] + " and " + pair[1] + " cards do not overlap");
            }

            // 3) 每块文字都必须在它所属的卡片里
            check(Contains(DailyReportLayoutTable.Get("income"), DailyReportLayoutTable.Get("incomeTip")),
                "income tip stays inside the income card");
            check(Contains(DailyReportLayoutTable.Get("income"), DailyReportLayoutTable.Get("incomeNote")),
                "income note stays inside the income card");
            check(Contains(DailyReportLayoutTable.Get("status"), DailyReportLayoutTable.Get("statusTaboo")),
                "status bottom strip stays inside the status card");
            check(Contains(DailyReportLayoutTable.Get("signin"), DailyReportLayoutTable.Get("legend")),
                "legend row stays inside the sign-in card");
            check(Contains(DailyReportLayoutTable.Get("signin"), DailyReportLayoutTable.Get("button")),
                "check-in button stays inside the sign-in card");

            // 4) 签到格：数量、不重叠、都在签到卡里
            int cells = DailyReportLayoutTable.Columns * DailyReportLayoutTable.Rows;
            check(cells >= DailyReportTuning.DaysPerPeriod, "grid can hold a whole period");
            for (int i = 0; i < DailyReportTuning.DaysPerPeriod; i++)
            {
                UnityEngine.Rect cell = DailyReportLayoutTable.GetCell(i);
                check(Contains(DailyReportLayoutTable.Get("signin"), cell), "cell " + i + " stays inside the card");
                if (i > 0)
                {
                    check(!Overlaps(DailyReportLayoutTable.GetCell(i - 1), cell), "cell " + i + " does not overlap its neighbour");
                }
            }
            check(!Overlaps(DailyReportLayoutTable.GetCell(DailyReportTuning.DaysPerPeriod - 1),
                DailyReportLayoutTable.Get("button")), "grid never runs under the check-in button");

            // 5) 坐标换算可逆：中心原点 + Y 向上
            UnityEngine.Rect probe = DailyReportLayoutTable.Get("income");
            UnityEngine.Vector2 anchored = DailyReportLayoutTable.ToAnchored(probe);
            float backX = anchored.x + DailyReportLayoutTable.PanelWidth / 2 - probe.width / 2;
            float backY = DailyReportLayoutTable.PanelHeight / 2 - anchored.y - probe.height / 2;
            check(Math.Abs(backX - probe.x) < 0.01f && Math.Abs(backY - probe.y) < 0.01f,
                "ToAnchored round-trips back to the top-left rect");

            // 6) 面板整体缩放仍然按视口收敛
            var view = new DailyReportView();
            view.paperFrame = new UnityEngine.RectTransform();
            var root = new UnityEngine.RectTransform(); view.transform = root;
            foreach (var size in new[] { new UnityEngine.Vector2(640, 480), new UnityEngine.Vector2(1280, 720),
                new UnityEngine.Vector2(1920, 1080), new UnityEngine.Vector2(1080, 1920) })
            {
                root.rect = new UnityEngine.Rect { width = size.x, height = size.y }; view.FitPaper();
                check(PanelWidth * view.paperFrame.localScale.x <= size.x - 23.9f
                    && PanelHeight * view.paperFrame.localScale.y <= size.y - 23.9f,
                    "paper fits viewport " + size.x + "x" + size.y);
            }

            // 7) 运行时图标（2026-09-23 起不再烤进底图）：都在面板里、正尺寸，并落在各自的宿主块里
            foreach (string icon in DailyReportLayoutTable.IconNames)
            {
                UnityEngine.Rect rect = DailyReportLayoutTable.GetIcon(icon);
                check(rect.width > 0 && rect.height > 0 && rect.x >= 0 && rect.y >= 0
                    && rect.x + rect.width <= DailyReportLayoutTable.PanelWidth
                    && rect.y + rect.height <= DailyReportLayoutTable.PanelHeight,
                    "icon " + icon + " stays inside the panel");
            }
            string[][] hosts = {
                new[] { "issue", "infoMeta" }, new[] { "deadline", "infoMeta" }, new[] { "weather", "infoWeather" },
                new[] { "income", "incomeLeft" }, new[] { "bounty", "incomeRight" }, new[] { "tip", "incomeTip" },
                new[] { "headline", "statusLeft" }, new[] { "broadcast", "statusRight" },
                new[] { "fortune", "statusLuck" }, new[] { "gossip", "statusTaboo" }, new[] { "gift", "button" },
            };
            foreach (string[] host in hosts)
            {
                check(Contains(DailyReportLayoutTable.Get(host[1]), DailyReportLayoutTable.GetIcon(host[0])),
                    host[0] + " icon sits inside " + host[1]);
            }
            check(Contains(DailyReportLayoutTable.Get("info"), DailyReportLayoutTable.Get("infoMeta"))
                && Contains(DailyReportLayoutTable.Get("info"), DailyReportLayoutTable.Get("infoWeather")),
                "issue and weather blocks share one info card");
            check(DailyReportLayoutTable.PillTextIndent > 0 && DailyReportLayoutTable.IconTextGap > 0,
                "ribbon text indent and icon gap are configured");

            string body = BuildBountyBlock(new DailyReportIssue { TodayBountyTarget = 1, TodayBountyTitle = "Return",
                TodayBountyStatus = "Failed today", TodayBountyCash = 1500 });
            check(body.Contains("Failed today") && body.Contains("1500"), "bounty block retains explicit status and reward");
        }

        private static bool Overlaps(UnityEngine.Rect a, UnityEngine.Rect b)
        {
            return a.x < b.x + b.width && b.x < a.x + a.width
                && a.y < b.y + b.height && b.y < a.y + a.height;
        }

        private static bool Contains(UnityEngine.Rect outer, UnityEngine.Rect inner)
        {
            return inner.x >= outer.x && inner.y >= outer.y
                && inner.x + inner.width <= outer.x + outer.width
                && inner.y + inner.height <= outer.y + outer.height;
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
