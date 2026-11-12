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
        internal readonly GameObject Root;
        internal readonly Transform PlayerSpawn;
        internal readonly Dictionary<string, Transform> Exits = new Dictionary<string, Transform>(StringComparer.Ordinal);
        internal readonly Dictionary<string, Transform> Interacts = new Dictionary<string, Transform>(StringComparer.Ordinal);
        internal readonly List<Transform> LootMarkers = new List<Transform>();
        internal readonly List<Transform> SpawnMarkers = new List<Transform>();
        internal readonly List<Transform> Pois = new List<Transform>();
        internal readonly List<Vector3> NightLamps = new List<Vector3>();
        internal readonly RaidIndoorRoofs IndoorRoofs;
        private int groundMask;

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
            groundMask = GameplayDataSettings.Layers.groundLayerMask.value;
            int groundLayer = FirstLayer(groundMask);
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
            int ground = 0, walls = 0;
            foreach (Collider collider in collision.GetComponentsInChildren<Collider>(true))
            {
                // COL_Ground 是地坪网格，COL_Ground_Overhead 是跨线桥面与司机室楼面（2026-11-12 起架空层单独成一张地面碰撞）
                if (collider.name.StartsWith("COL_Ground", StringComparison.Ordinal)) { collider.gameObject.layer = groundLayer; ground++; }
                else { collider.gameObject.layer = wallLayer; walls++; }
            }
            if (ground == 0 || walls == 0) throw new InvalidOperationException("末班编组站地面或墙体碰撞体缺失");
            Physics.SyncTransforms();
            int halfCover = RaidHalfCover.Register(collision, wallLayer, TryGroundAt);
            Debug.Log(LastShiftRaidScene.LogTag + " LAYERS_READY ground=" + ground + " walls=" + walls + " halfCover=" + halfCover + " shaders=" + checkedShaders.Count);
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
            groundMask = mask;
            VerifyGround(PlayerSpawn);
            foreach (Transform exit in Exits.Values) VerifyGround(exit);
        }

        private void VerifyGround(Transform marker)
        {
            float y;
            if (!TryGroundAt(marker.position, out y)) throw new InvalidOperationException("点位脚下未命中自建地面：" + marker.name);
        }

        /// <summary>脚下地面高度：从上方 3 m 向下打地面层，命中必须属于本图。</summary>
        internal bool TryGroundAt(Vector3 position, out float y)
        {
            y = position.y;
            RaycastHit hit;
            if (Physics.Raycast(position + Vector3.up * 3f, Vector3.down, out hit, 12f, groundMask, QueryTriggerInteraction.Ignore)
                && hit.transform.IsChildOf(Root.transform))
            {
                y = hit.point.y;
                return true;
            }
            return false;
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
