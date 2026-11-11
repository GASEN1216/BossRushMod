using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    /// <summary>官方角色和准心各有一个 VisualOcclusion 检查器；这里按主角脚下的实际房间调用
    /// 官方 ShadowOnly 状态，瞄准邻屋不会替玩家进屋。按 16 米网格索引，站定时不用重复找屋。</summary>
    internal sealed class RaidIndoorRoofs : IDisposable
    {
        private sealed class Room
        {
            internal Bounds Footprint;
            internal OcclusionFadeObject Fade;
            internal Renderer[] Walls;
        }
        private readonly Dictionary<Vector2Int,List<Room>> cells = new Dictionary<Vector2Int,List<Room>>();
        private readonly MaterialPropertyBlock transition = new MaterialPropertyBlock();
        private static readonly int ExteriorId = Shader.PropertyToID("_TidegateExterior");
        private static readonly int RuinId = Shader.PropertyToID("_TidegateRuin");
        private static readonly int BackAt = Shader.PropertyToID("_TidegateRoofBackAt");
        private Room inside;
        private Vector3 lastFeet;
        private bool sampled;
        private static int Cell(float coordinate) { return Mathf.FloorToInt(coordinate / 16f); }

        internal RaidIndoorRoofs(GameObject world, bool pavilions)
        {
            Transform group = pavilions ? world.transform.Find("SkyPavilionRooms") : null;
            if (!pavilions)
                foreach (GameObject g in world.scene.GetRootGameObjects())
                    if (g.name == "TidegateIndoorRooms" || g.name == "LastShiftIndoorRooms") { group = g.transform; break; }
            if (group == null)
            {
                Debug.LogWarning("[BossRush] " + world.name + " 缺少室内触发区；请更新地图包。");
                return;
            }
            var roofs = new Dictionary<string, Transform>(StringComparer.Ordinal);
            Transform buildings = pavilions ? group : world.transform.Find("Visual/VIS_buildings");
            if (buildings == null) return;
            // 单次登记建筑，非建筑物（含可互动物资模型）不受室外视线裁切。
            if (!pavilions) foreach (Renderer wall in buildings.GetComponentsInChildren<Renderer>(true))
            {
                wall.GetPropertyBlock(transition);
                transition.SetFloat(ExteriorId, 1f);
                wall.SetPropertyBlock(transition);
            }
            foreach (Transform node in buildings.GetComponentsInChildren<Transform>(true))
                if (node.name.StartsWith("BLD_", StringComparison.Ordinal) && node.name.EndsWith("_Roof", StringComparison.Ordinal))
                    roofs[node.name] = node;
            foreach (Transform volume in group)
            {
                Transform roof;
                BoxCollider box = volume.GetComponent<BoxCollider>();
                if (box == null || !roofs.TryGetValue("BLD_" + volume.name + "_Roof", out roof)) continue;
                var fade = volume.gameObject.AddComponent<OcclusionFadeObject>();
                fade.triggers = new OcclusionFadeTrigger[0];
                fade.fadeType = OcclusionFadeTypes.ShadowOnly;
                fade.renderers = roof.GetComponentsInChildren<Renderer>(true);
                var room = new Room { Footprint = box.bounds, Fade = fade };
                // FBX 的 TEXCOORD1 逐三角标记一层墙体、内衬和窗。即使家具、门和墙共用一张材质也不误隐。
                if (!pavilions)
                {
                    var upper = new HashSet<Renderer>(fade.renderers);
                    var lower = new List<Renderer>();
                    foreach (var part in roof.parent.GetComponentsInChildren<Renderer>(true))
                        if (!upper.Contains(part)) lower.Add(part);
                    room.Walls = lower.ToArray();
                }
                for (int x = Cell(room.Footprint.min.x); x <= Cell(room.Footprint.max.x); x++)
                    for (int z = Cell(room.Footprint.min.z); z <= Cell(room.Footprint.max.z); z++)
                    {
                        var key = new Vector2Int(x,z);
                        List<Room> list;
                        if (!cells.TryGetValue(key, out list)) cells[key] = list = new List<Room>();
                        list.Add(room);
                    }
            }
        }

        internal bool UpdateFeet(Vector3 feet)
        {
            if (sampled && feet == lastFeet) return inside != null;
            sampled = true;
            lastFeet = feet;
            if (inside != null && inside.Footprint.Contains(feet)) return true;
            if (inside != null)
            {
                foreach (Renderer r in inside.Fade.renderers)
                {
                    if (r == null) continue;
                    r.GetPropertyBlock(transition);
                    transition.SetFloat(BackAt, Time.time);
                    r.SetPropertyBlock(transition);
                }
                SetWalls(inside, false);
                inside.Fade.OnLeave();
                inside = null;
            }
            List<Room> candidates;
            if (!cells.TryGetValue(new Vector2Int(Cell(feet.x), Cell(feet.z)), out candidates)) return false;
            foreach (Room room in candidates)
                if (room.Footprint.Contains(feet)) { inside = room; room.Fade.OnEnter(); SetWalls(room, true); return true; }
            return false;
        }

        private void SetWalls(Room room, bool active)
        {
            if (room.Walls == null) return;
            foreach (Renderer wall in room.Walls)
            {
                if (!wall) continue;
                wall.GetPropertyBlock(transition);
                transition.SetVector(RuinId, new Vector4(1f, room.Footprint.min.y + 1.4f, active ? 1f : 0f, 0f));
                transition.SetFloat(BackAt, active ? -10f : Time.time);
                wall.SetPropertyBlock(transition);
            }
        }

        public void Dispose()
        {
            if (inside != null && inside.Fade != null) { SetWalls(inside, false); inside.Fade.OnLeave(); }
            inside = null;
            cells.Clear();
        }
    }
}
