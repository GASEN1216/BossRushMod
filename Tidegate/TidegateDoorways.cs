using System;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 潮闸镇背对镜头的门（2026-11-17 owner「门上面被建筑挡住了，要蹭门才知道哪里有门」）。
    /// 官方默认机位从东南往西北看（<see cref="CameraPitch"/> / <see cref="CameraYaw"/>，潮闸镇不覆盖），
    /// 开在北墙、西墙的门整面背对镜头，门外紧贴墙的一条地也被楼体挡住，人走到门口都看不见门洞。
    /// 这里给这些门各登记一块门前区：脚点进去就收起这一栋的上部隐藏组，和进屋同一套官方 ShadowOnly、只限本栋；
    /// 朝南、朝东的门本来就看得见，不登记。门前区深度按本栋实际顶高与墙长算出被挡住的那条地，再多留一点余量。
    /// </summary>
    internal static class TidegateDoorways
    {
        /// <summary>官方 <c>CameraArm.globalPitch</c> / <c>globalYaw</c> 的默认值（反编译源 2026-09-10 核对）。</summary>
        internal const float CameraPitch = 55f, CameraYaw = -30f;
        /// <summary>门外法线与镜头水平朝向的点积大于它才算背对镜头（北墙 0.87、西墙 0.5；南、东为负）。</summary>
        internal const float AwayThreshold = 0.25f;
        /// <summary>门洞两侧各多留的宽度、被挡带外再多留的深度、门前区最浅深度（米）。</summary>
        internal const float SideMargin = 1.5f, OutMargin = 1f, MinDepth = 2.5f;

        internal struct Door
        {
            internal readonly string Building;
            internal readonly char Side;
            internal readonly float At, Width;
            internal Door(string building, char side, float at, float width) { Building = building; Side = side; At = at; Width = width; }
        }

        /// <summary>
        /// 原型布局里的全部门，照 `ArtSource/Tidegate/export/tidegate_layout.json` 写死：楼号、边（n/s/e/w，原型坐标）、
        /// 沿墙的原型绝对坐标（南北墙是 x，东西墙是原型 z）、门宽（缺省 2 m）。`TidegateDoorwayPropertyTest` 逐项比对。
        /// </summary>
        internal static readonly Door[] Doors =
        {
            new Door("h1", 's', -84f, 2f),
            new Door("h2", 'e', -18f, 2f),
            new Door("hw", 's', -63f, 2f),
            new Door("hw", 'e', -28f, 2f),
            new Door("h4", 's', -46f, 2f),
            new Door("inn", 's', -60f, 2f),
            new Door("inn", 'w', -17.5f, 2f),
            new Door("hall", 's', -18f, 2f),
            new Door("fish", 's', 16f, 2f),
            new Door("shed", 'w', -18f, 2f),
            new Door("clinic", 'n', -63f, 2f),
            new Door("h10", 'n', -47f, 2f),
            new Door("store", 'n', -22f, 2f),
            new Door("h12", 'e', 1f, 2f),
            new Door("pump2", 'w', 1f, 2f),
            new Door("pump2", 'n', 19f, 2f),
            new Door("h14", 'e', 0f, 2f),
            new Door("wh1", 's', -81f, 2f),
            new Door("wh2", 's', -60f, 2f),
            new Door("wh3", 's', -39f, 2f),
            new Door("wh3", 'n', -39f, 2f),
            new Door("pump1", 's', -8f, 2f),
            new Door("ctrl", 's', 13f, 2f),
            new Door("boat", 's', 68f, 2f),
            new Door("tide", 'w', 34f, 2f),
            new Door("cabin", 'n', 38f, 2f),
            new Door("vault", 'w', -5f, 2.4f),
        };

        /// <summary>镜头的水平朝向（Unity 坐标）。</summary>
        internal static Vector3 CameraFlatForward()
        {
            float yaw = CameraYaw * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
        }

        /// <summary>原型的边换成 Unity 的门外法线：原型 (x, z) 进 Unity 是 (x, -z)，所以原型 n（z 小）是 Unity 北。</summary>
        internal static Vector3 Outward(char side)
        {
            switch (side)
            {
                case 'n': return new Vector3(0f, 0f, 1f);
                case 's': return new Vector3(0f, 0f, -1f);
                case 'e': return new Vector3(1f, 0f, 0f);
                case 'w': return new Vector3(-1f, 0f, 0f);
                default: return Vector3.zero;
            }
        }

        internal static float AwayScore(char side) { return Vector3.Dot(Outward(side), CameraFlatForward()); }

        internal static bool FacesAway(char side) { return AwayScore(side) > AwayThreshold; }

        /// <summary>沿墙的单位方向（Unity）：南北墙沿 x、东西墙沿 z。</summary>
        internal static Vector3 Tangent(char side)
        {
            return side == 'n' || side == 's' ? new Vector3(1f, 0f, 0f) : new Vector3(0f, 0f, 1f);
        }

        /// <summary>门洞中心沿墙的 Unity 坐标（南北墙是 x；东西墙是原型 z 取反）。</summary>
        internal static float AlongUnity(Door door)
        {
            return door.Side == 'n' || door.Side == 's' ? door.At : -door.At;
        }

        /// <summary>
        /// 门洞正前方被本栋挡住的那条地有多深（沿门外法线）：地面点往镜头的射线要在楼高以内回到这面墙，
        /// 且回到墙面时没有横移出墙的两端（窄楼上射线会从楼角旁边漏过去，不再被挡）。取门洞上最深的那一点。
        /// </summary>
        internal static float HiddenDepth(Door door, Bounds footprint, float height)
        {
            float away = AwayScore(door.Side);
            if (away <= 0f || height <= 0f) return 0f;
            float byHeight = height * away / Mathf.Tan(CameraPitch * Mathf.Deg2Rad);
            Vector3 tangent = Tangent(door.Side);
            float slide = Vector3.Dot(-CameraFlatForward(), tangent) / away;
            if (Mathf.Abs(slide) < 1e-4f) return byHeight;
            float lo = Vector3.Dot(footprint.min, tangent), hi = Vector3.Dot(footprint.max, tangent);
            float center = AlongUnity(door), half = door.Width * 0.5f;
            float room = slide > 0f ? hi - (center - half) : (center + half) - lo;
            return Mathf.Max(0f, Mathf.Min(byHeight, room / Mathf.Abs(slide)));
        }

        /// <summary>
        /// 门前区（Unity 坐标）：沿墙是门洞加两侧余量，向外从墙里 0.2 m 到被挡带外 <see cref="OutMargin"/>（至少 <see cref="MinDepth"/>）；
        /// <paramref name="footprint"/> 是本栋室内触发区（中心高 = 地板 + 1.5、高 4），<paramref name="top"/> 是上部隐藏组的实际顶高。
        /// </summary>
        internal static Bounds Zone(Door door, Bounds footprint, float top)
        {
            float floor = footprint.center.y - 1.5f;
            float depth = Mathf.Max(MinDepth, HiddenDepth(door, footprint, top - floor) + OutMargin);
            float at = AlongUnity(door), half = door.Width * 0.5f + SideMargin;
            float y = floor + 1.5f, height = 5f;
            switch (door.Side)
            {
                case 'n': return Box(at - half, at + half, footprint.max.z - 0.2f, footprint.max.z + depth, y, height);
                case 's': return Box(at - half, at + half, footprint.min.z - depth, footprint.min.z + 0.2f, y, height);
                case 'e': return Box(footprint.max.x - 0.2f, footprint.max.x + depth, at - half, at + half, y, height);
                case 'w': return Box(footprint.min.x - depth, footprint.min.x + 0.2f, at - half, at + half, y, height);
                default: return default(Bounds);
            }
        }

        private static Bounds Box(float x0, float x1, float z0, float z1, float y, float height)
        {
            return new Bounds(new Vector3((x0 + x1) * 0.5f, y, (z0 + z1) * 0.5f), new Vector3(x1 - x0, height, z1 - z0));
        }

        /// <summary>开局登记一次；返回登记了几处。缺楼（比如没有屋顶组的废墟储蓄所）跳过。</summary>
        internal static int Register(RaidIndoorRoofs roofs)
        {
            if (roofs == null) return 0;
            int added = 0;
            foreach (Door door in Doors)
            {
                if (!FacesAway(door.Side)) continue;
                Bounds footprint;
                Renderer[] roof;
                if (!roofs.TryGetRoom(door.Building, out footprint, out roof) || roof == null) continue;
                float top = footprint.min.y;
                foreach (Renderer r in roof)
                    if (r != null) top = Mathf.Max(top, r.bounds.max.y);
                if (roofs.AddDoorway(door.Building, Zone(door, footprint, top))) added++;
            }
            return added;
        }
    }
}
