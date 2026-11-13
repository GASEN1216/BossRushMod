using System;
using System.Collections.Generic;
using Duckov.Utilities;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 末班编组站世界根的索引与装配：标记点、碰撞分层、着色器校验、地面查询。
    /// 层级由作者工程 `LastShiftBuilder.BuildWorldPrefab` 生成：`Visual/VIS_*`、`Collision/COL_*`、`Markers/*`、`Navigation`、`Templates`（车辆，未激活）。
    /// </summary>
    internal sealed class LastShiftWorld : IDisposable
    {
        /// <summary>全图只有一个自研着色器（含站场地坪与站外地面），`tools/verify_lastshift_bundle_shaders.py` 的 REQUIRED 与这里一致。</summary>
        internal static readonly string[] WorldShaders = { "BossRush/LastShift/Environment" };
        /// <summary>可玩区：逻辑网格 168 × 132 m、以原点为中心。站界隐形墙在场景包里（`Collision/COL_Boundary`），这里只做越界兜底。</summary>
        internal static readonly Rect PlayArea = Rect.MinMaxRect(-84f, -66f, 84f, 66f);
        private const float OutOfBoundsMargin = 8f;
        private const float GroundProbeAbove = .25f, GroundProbeBelow = 9f;
        private const string BasinVisualPath = "Visual/VIS_props/Sediment and wash basins";
        internal readonly GameObject Root;
        internal readonly Transform PlayerSpawn;
        internal readonly Dictionary<string, Transform> Exits = new Dictionary<string, Transform>(StringComparer.Ordinal);
        internal readonly Dictionary<string, Transform> Interacts = new Dictionary<string, Transform>(StringComparer.Ordinal);
        internal readonly List<Transform> LootMarkers = new List<Transform>();
        internal readonly List<Transform> SpawnMarkers = new List<Transform>();
        internal readonly List<Transform> Pois = new List<Transform>();
        internal readonly List<Vector3> NightLamps = new List<Vector3>();
        internal readonly RaidIndoorRoofs IndoorRoofs;
        private readonly List<Collider> groundColliders = new List<Collider>();

        internal LastShiftWorld(GameObject root)
        {
            Root = root;
            Transform markers = root.transform.Find("Markers");
            if (markers == null) throw new InvalidOperationException("末班编组站缺少 Markers 节点");
            PlayerSpawn = markers.Find("PlayerSpawn");
            if (PlayerSpawn == null) throw new InvalidOperationException("末班编组站缺少出生点");
            Collect(markers.Find("Exits"), delegate (Transform t)
            {
                Exits[t.name.StartsWith("Exit_", StringComparison.Ordinal) ? t.name.Substring(5) : t.name] = t;
            });
            Collect(markers.Find("Interact"), delegate (Transform t) { Interacts[t.name] = t; });
            Collect(markers.Find("Loot"), LootMarkers.Add);
            // 刷怪点按组分了一层：Spawns/<组>/<id>
            Collect(markers.Find("Spawns"), delegate (Transform group) { Collect(group, SpawnMarkers.Add); });
            Collect(markers.Find("POI"), Pois.Add);
            Collect(markers.Find("NightLamps"), delegate (Transform t) { NightLamps.Add(t.position); });
            if (!Exits.ContainsKey("north")) throw new InvalidOperationException("末班编组站缺少常开撤离点（北公路）");
            if (LootMarkers.Count == 0 || SpawnMarkers.Count == 0) throw new InvalidOperationException("末班编组站搜刮点或刷怪点缺失");
            // 首班停在岔心等扳道岔，没有超时兜底；三条线索缺一条剧情就合不上。缺了直接装配失败，别让玩家进一张走不完的图。
            if (!Interacts.ContainsKey("switch")) throw new InvalidOperationException("末班编组站缺少道岔交互标记 switch");
            foreach (string note in LastShiftStory.NoteMarkers)
                if (!Interacts.ContainsKey(note)) throw new InvalidOperationException("末班编组站缺少线索标记 " + note);
            SpawnMarkers.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            LootMarkers.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            IndoorRoofs = new RaidIndoorRoofs(root, false);
        }

        private static void Collect(Transform group, Action<Transform> add)
        {
            if (group == null) return;
            foreach (Transform child in group) add(child);
        }

        /// <summary>
        /// 渲染器与碰撞体分层（口径同潮闸镇）：可见网格进地面层；`COL_Ground` 地面层，`Collision` 下其余碰撞体（墙、家具、掩体、货箱、栏杆、站界）墙层。
        /// 自研着色器必须被当前显卡支持，否则直接失败——owner 定过不做着色器降级（资源不对就硬失败，部署前判包）。
        /// </summary>
        internal void ApplyLayers()
        {
            int groundLayer = FirstLayer(GameplayDataSettings.Layers.groundLayerMask.value);
            int wallLayer = FirstLayer(GameplayDataSettings.Layers.wallLayerMask.value);
            if (groundLayer < 0 || wallLayer < 0) throw new InvalidOperationException("游戏碰撞层未就绪");
            var checkedShaders = new HashSet<Shader>();
            foreach (Renderer renderer in Root.GetComponentsInChildren<Renderer>(true))
            {
                renderer.gameObject.layer = groundLayer;
                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material == null) throw new InvalidOperationException("模型缺少材质：" + renderer.name);
                    Shader shader = material.shader;
                    if (shader == null || !checkedShaders.Add(shader)) continue;
                    if (Array.IndexOf(WorldShaders, shader.name) < 0)
                        throw new InvalidOperationException("末班编组站材质使用了未登记的着色器 " + shader.name + "：" + material.name);
                    if (!shader.isSupported)
                        throw new InvalidOperationException("当前显卡不支持末班编组站着色器 " + shader.name + "，请更新 Mod 资源包");
                }
            }
            Transform collision = Root.transform.Find("Collision");
            if (collision == null) throw new InvalidOperationException("末班编组站缺少 Collision 节点");
            AttachBuildingColliders(collision);
            int walls = 0;
            groundColliders.Clear();
            foreach (Collider collider in collision.GetComponentsInChildren<Collider>(true))
            {
                // COL_Ground 是地坪网格，COL_Ground_Overhead 是跨线桥面与司机室楼面（2026-11-12 起架空层单独成一张地面碰撞）
                if (collider.name.StartsWith("COL_Ground", StringComparison.Ordinal))
                {
                    collider.gameObject.layer = groundLayer;
                    if (!collider.isTrigger) groundColliders.Add(collider);
                }
                else { collider.gameObject.layer = wallLayer; walls++; }
            }
            if (groundColliders.Count == 0 || walls == 0) throw new InvalidOperationException("末班编组站地面或墙体碰撞体缺失");
            Physics.SyncTransforms();
            int water = ApplyBasinWater(collision);
            int halfCover = RaidHalfCover.Register(collision, wallLayer, TryGroundAt);
            Debug.Log(LastShiftRaidScene.LogTag + " LAYERS_READY ground=" + groundColliders.Count + " walls=" + walls + " water=" + water + " halfCover=" + halfCover + " shaders=" + checkedShaders.Count);
        }

        private static void AttachBuildingColliders(Transform collision)
        {
            if (collision.Find("COL_BuildingDetails") != null) return;
            var details = new GameObject("COL_BuildingDetails").transform;
            details.SetParent(collision, false);

            // kit-yard.mjs frameWalls 在布局墙段外加了门柱和横梁。门口中央一直空到横梁下沿。
            AddDoorFrame(details, "SignalSouth", -10f, 5.2f, -14f, 4.4f, 2.55f, false);
            AddDoorFrame(details, "RepairSouth", -47f, 0f, -2f, 4.8f, 3.45f, false);
            AddDoorFrame(details, "RepairEast", -39.5f, 0f, 8f, 4f, 3.45f, true);
            AddDoorFrame(details, "BondSouth", 48f, 0f, 8.5f, 4.6f, 3.45f, false);
            AddDoorFrame(details, "BondEast", 61.5f, 0f, 15f, 3.8f, 3.45f, true);

            // 旧包将柱脚并进了细钢柱、剔除了四只药箱。重建后的场景包带回布局原有实体。
            Vector3 footSize = new Vector3(1.22f, .5f, 1.22f);
            AddMissingBox(collision, details, "block", "gantryNE", new Vector3(7.2f, .25f, -5.5f), footSize);
            AddMissingBox(collision, details, "block", "gantrySE", new Vector3(7.2f, .25f, -10.5f), footSize);
            Vector3 caseSize = new Vector3(.86f, .50f, .60f);
            AddMissingBox(collision, details, "crate", "crate:signalMed", new Vector3(-16f, .305f, -42f), caseSize);
            AddMissingBox(collision, details, "crate", "crate:repairMed", new Vector3(-45f, .305f, 11f), caseSize);
            AddMissingBox(collision, details, "crate", "crate:eastCab", new Vector3(34f, .305f, 41f), caseSize);
            AddMissingBox(collision, details, "crate", "crate:bellMed", new Vector3(37f, .305f, -40f), caseSize);
        }

        private static void AddMissingBox(Transform collision, Transform details, string group, string name, Vector3 center, Vector3 size)
        {
            Transform node = collision.Find("COL_" + group + "/COL_" + name);
            BoxCollider authored = node == null ? null : node.GetComponent<BoxCollider>();
            // 世界根可能尚未激活，只检查 Collision 下的烘焙分组和实体自身。
            if (authored != null && authored.enabled && !authored.isTrigger &&
                node.gameObject.activeSelf && node.parent.gameObject.activeSelf) return;
            AddBuildingBox(details, name, center, size);
        }

        private static void AddDoorFrame(Transform parent, string name, float x, float floor, float z,
            float width, float lintelHeight, bool east)
        {
            float offset = width * .5f + .12f;
            for (int side = -1; side <= 1; side += 2)
            {
                float postX = east ? x : x + side * offset;
                float postZ = east ? z + side * offset : z;
                string post = name + (side < 0 ? "Left" : "Right");
                AddBuildingBox(parent, post + "Base", new Vector3(postX, floor + .46f, postZ), new Vector3(.33f, .92f, .33f));
                AddBuildingBox(parent, post, new Vector3(postX, floor + (lintelHeight + .89f) * .5f, postZ),
                    new Vector3(.29f, lintelHeight - .89f, .29f));
            }
            AddBuildingBox(parent, name + "Lintel", new Vector3(x, floor + lintelHeight, z),
                east ? new Vector3(.49f, .29f, width + .75f) : new Vector3(width + .75f, .29f, .49f));
            if (floor > 0f) return;
            // 两栋平地建筑的门楣上还有四条钢板，窄缝按可见间隔保留。
            Vector3 panelSize = east ? new Vector3(.42f, .10f, width - .1f) : new Vector3(width - .1f, .10f, .42f);
            for (int panel = 0; panel < 4; panel++)
                AddBuildingBox(parent, name + "Panel" + panel, new Vector3(x, lintelHeight + .12f + panel * .16f, z), panelSize);
        }

        private static void AddBuildingBox(Transform parent, string name, Vector3 center, Vector3 size)
        {
            var piece = new GameObject("COL_" + name);
            piece.transform.SetParent(parent, false);
            piece.transform.localPosition = center;
            piece.AddComponent<BoxCollider>().size = size;
        }

        /// <summary>浅水池沿用场景碰撞底与可见水面，交给官方 Water 层的角色检测。</summary>
        private int ApplyBasinWater(Transform collision)
        {
            int waterLayer = LayerMask.NameToLayer("Water");
            Transform basins = Root.transform.Find(BasinVisualPath);
            if (waterLayer < 0 || basins == null)
            {
                Debug.LogWarning(LastShiftRaidScene.LogTag + " 缺少官方 Water 层或水池模型，浅水池未接入入水检测");
                return 0;
            }
            int count = 0;
            foreach (Transform surface in basins)
            {
                // 水面是池组的平面子模型；池沿与进水格栅在独立的子组内。
                MeshRenderer renderer = surface.GetComponent<MeshRenderer>();
                if (renderer == null) continue;
                Bounds bounds = renderer.bounds;
                if (bounds.size.y > .01f || bounds.size.x < 1f || bounds.size.z < 1f) continue;
                float floor;
                float top = bounds.center.y;
                if (!TryGroundAt(bounds.center, out floor) || top - floor < .02f) continue;
                // 放在 Collision 之外，重复 ApplyLayers 时不会被墙层扫描重新归类。
                string name = "BR_BasinWater_" + surface.name;
                Transform existing = collision.parent.Find(name);
                if (existing != null) { count++; continue; }
                var water = new GameObject(name);
                water.transform.SetParent(collision.parent, false);
                water.transform.position = new Vector3(bounds.center.x, (floor + top) * .5f, bounds.center.z);
                water.layer = waterLayer;
                var trigger = water.AddComponent<BoxCollider>();
                trigger.isTrigger = true;
                trigger.size = new Vector3(bounds.size.x, top - floor, bounds.size.z);
                count++;
            }
            if (count != 2) Debug.LogWarning(LastShiftRaidScene.LogTag + " 水池 Water 触发体数量异常：" + count);
            return count;
        }

        /// <summary>
        /// 交互提示离标记点（物体底面中心）的高度（2026-11-12 实机「交互点不在物体中心」）：以前一律 +1 m。
        /// 数值是 2026-11-12 在 LastShiftYard.blend 里按标记点 0.5 m 内三角形量的物体顶高，取把手 / 面板 / 盒面中部。
        /// </summary>
        internal static Vector3 InteractOffset(string id)
        {
            switch (id)
            {
                case "switch": return new Vector3(0f, 1.15f, 0f);       // 道岔手柄，工作台面 1.17 m
                case "manifest": return new Vector3(0f, 1.4f, 0f);      // 班次运单牌，牌面到 2.85 m
                case "bell": return new Vector3(0f, 1.3f, 0f);          // 轨道车铃的拉绳
                case "note1": return new Vector3(0f, 1.05f, 0f);        // 工作台上的打孔钳
                case "note2": return new Vector3(0f, 1.0f, 0f);         // 架子上的旧录音机，顶 1.41 m
                case "note3": return new Vector3(0f, 0.95f, 0f);        // 退件簿，台面 1.31 m
                case "repairBuzzer": return new Vector3(0f, 1.15f, 0f); // 蜂鸣器控制柜，顶 1.78 m
                case "steamValve": return new Vector3(0f, 0.95f, 0f);   // 检修蒸汽阀手轮，顶 1.32 m
                default: return new Vector3(0f, 1f, 0f);
            }
        }

        /// <summary>箱柜按种类的提示高度（布局 loot.size 的箱高：封签货 / 风险箱 1.42、柜 1.05、工具箱 0.8、药箱 0.58、铅封侧柜 1.63）。</summary>
        internal static Vector3 CrateMarkerOffset(string kind)
        {
            switch (kind)
            {
                case "sealed": case "risk": return new Vector3(0f, 0.85f, 0f);
                case "supply": case "drop": return new Vector3(0f, 0.65f, 0f);
                case "tool": return new Vector3(0f, 0.55f, 0f);
                case "medical": return new Vector3(0f, 0.45f, 0f);
                case "side": return new Vector3(0f, 0.9f, 0f);
                default: return new Vector3(0f, 0.6f, 0f);
            }
        }

        internal static bool OutsidePlayArea(Vector3 position)
        {
            return position.x < PlayArea.xMin - OutOfBoundsMargin || position.x > PlayArea.xMax + OutOfBoundsMargin
                || position.z < PlayArea.yMin - OutOfBoundsMargin || position.z > PlayArea.yMax + OutOfBoundsMargin;
        }

        internal void VerifyGround(int mask)
        {
            foreach (Collider collider in groundColliders)
                if ((mask & (1 << collider.gameObject.layer)) == 0)
                    throw new InvalidOperationException("末班编组站地面碰撞体未处于官方地面层：" + collider.name);
            VerifyGround(PlayerSpawn);
            foreach (Transform exit in Exits.Values) VerifyGround(exit);
        }

        private void VerifyGround(Transform marker)
        {
            float y;
            if (!TryGroundAt(marker.position, out y)) throw new InvalidOperationException("点位脚下未命中自建地面：" + marker.name);
        }

        /// <summary>探测本图的地面与架空层，选择高度最接近请求脚点的一层。</summary>
        internal bool TryGroundAt(Vector3 position, out float y)
        {
            y = position.y;
            float distance = float.PositiveInfinity;
            Ray ray = new Ray(position + Vector3.up * GroundProbeAbove, Vector3.down);
            foreach (Collider collider in groundColliders)
            {
                RaycastHit hit;
                if (collider == null || !collider.Raycast(ray, out hit, GroundProbeAbove + GroundProbeBelow) || hit.normal.y <= 0f) continue;
                float next = Mathf.Abs(hit.point.y - position.y);
                if (next > distance || (next == distance && hit.point.y >= y)) continue;
                distance = next;
                y = hit.point.y;
            }
            return distance < float.PositiveInfinity;
        }

        internal Transform NearestPoi(Vector3 position, out float distance)
        {
            Transform best = null;
            float bestSqr = float.MaxValue;
            foreach (Transform poi in Pois)
            {
                Vector3 offset = poi.position - position;
                offset.y = 0f;
                float d = offset.sqrMagnitude;
                if (d < bestSqr) { bestSqr = d; best = poi; }
            }
            distance = best == null ? float.MaxValue : Mathf.Sqrt(bestSqr);
            return best;
        }

        private static int FirstLayer(int mask)
        {
            for (int i = 0; i < 32; i++) if ((mask & (1 << i)) != 0) return i;
            return -1;
        }

        public void Dispose() { IndoorRoofs.Dispose(); }
    }
}
