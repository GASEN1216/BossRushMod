using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BossRush
{
    /// <summary>普通巡逻的岛区数值；只应用到巡逻 owner 的克隆 preset，不写既有头目与剧情组。</summary>
    internal sealed class SkyIslandPatrolProfile
    {
        internal readonly string RegionId;
        internal readonly int Rank;
        internal readonly float HealthFactor, DamageFactor, SightDistance, ReactionTime;

        internal SkyIslandPatrolProfile(string regionId, int rank, float healthFactor, float damageFactor,
            float sightDistance, float reactionTime)
        {
            RegionId = regionId; Rank = rank; HealthFactor = healthFactor; DamageFactor = damageFactor;
            SightDistance = sightDistance; ReactionTime = reactionTime;
        }
    }

    /// <summary>作者地图局部 Unity XYZ 固定落点；Y 是导航面高度，生成时的抬脚由巡逻 owner 统一处理。</summary>
    internal sealed class SkyIslandPatrolSlot
    {
        internal readonly string Id, RegionId;
        internal readonly float X, Y, Z;
        internal SkyIslandPatrolSlot(string id, string regionId, float x, float y, float z)
        { Id = id; RegionId = regionId; X = x; Y = y; Z = z; }
    }

    internal sealed class SkyIslandPatrolData
    {
        internal readonly SkyIslandPatrolProfile[] Profiles;
        internal readonly SkyIslandPatrolSlot[] Slots;
        internal readonly string Source;
        internal SkyIslandPatrolData(SkyIslandPatrolProfile[] profiles, SkyIslandPatrolSlot[] slots, string source)
        { Profiles = profiles; Slots = slots; Source = source; }

        internal SkyIslandPatrolProfile FindProfile(string regionId)
        {
            foreach (SkyIslandPatrolProfile profile in Profiles)
                if (string.Equals(profile.RegionId, regionId, StringComparison.Ordinal)) return profile;
            return null;
        }
    }

    /// <summary>
    /// COMPAT：普通巡逻专用内容表。坐标取 collision_navigation.json 岛区三角形中心，运行时不随机选点；
    /// 选点（tools/sky_island_patrol_slots.py）只取离岛缘够远、靠路网 / 桥头 / 地标、不贴墙的中心，判据在属性测试里。
    /// 每个巡逻 owner 入图时 Load 一次并持有结果；本类型不持有缓存、场景、活体或存档，不需要静态清理。
    /// JSON 缺失或结构/身份/数值无效时整表回退；离线几何由 SkyIslandPatrolPlacementPropertyTest 独立复算。
    /// </summary>
    internal static class SkyIslandPatrolRules
    {
        internal const string RelativePath = "Assets/Data/SkyIsland/Patrols.json";
        internal const int Version = 1;
        // 固定坐标的采样来源；与 JSON 一起由定向几何验收核对当前 NAV。
        internal const string SourceNavigationSha256 = "24e7cd204471e41ca7edc4f7da1d4764f0c110e6a8a71a7c1c54fe3f60fb269c";
        // 仅属于普通巡逻。既有 SkyIslandEncounters 的自动组上限和剧情预留保持独立。
        // 2026-10-01 加密后：全图任一点 70 m 内最多 26 个槽位（残星工坊腹地），32 = 26 + 6 个近远滞回余量；
        // 挂起半径从 110 m 收到 95 m，走过桥后上一座岛的活体早点让出名额（离线路线复算：56 m 内从不因预算缺人）。
        internal const int ActiveLimit = 32;
        internal const float ActivationRadius = 70f;
        internal const float SuspensionRadius = 95f;
        internal const float SpawnInterval = 0.12f;
        internal const float TickInterval = 0.25f;
        internal const float SpawnClearance = 14f, ResidentClearance = 8f, InteractionClearance = 4f, SlotSpacing = 5f;

        // owner 2026-10-01 实机反馈「密度不够」：主岛约翻倍；钟庭与小岛按合格候选的面积加（钟庭只有一条路、听雨洞最窄）。
        internal static int TargetCount(string regionId)
        {
            switch (regionId)
            {
                case "A": return 10;
                case "B": return 14;
                case "C": return 18;
                case "D": case "E": case "F": return 22;
                case "G": return 26;
                case "H": return 20;
                case "S1": case "S2": return 8;
                case "S3": return 7;
                case "S4": return 10;
                default: return 0;
            }
        }

        /// <summary>
        /// 调用方明确传入 Mod 根目录。只做文件读取，继续复用共享 JSON token/parser；不依赖 Unity 或宿主。
        /// 缺失/无效时返回 Source=Fallback，由 Runtime 决定如何报告。岛区门控也由 Runtime 持有：
        /// 只有 H 需要 content.IsGateOpen("BellCourt", story)，不把场景/剧情状态引入纯规则。
        /// </summary>
        internal static SkyIslandPatrolData Load(string modPath)
        {
            if (string.IsNullOrEmpty(modPath)) return CreateFallback();
            try
            {
                string path = Path.Combine(modPath, "Assets", "Data", "SkyIsland", "Patrols.json");
                if (!File.Exists(path)) return CreateFallback();
                string raw = File.ReadAllText(path, Encoding.UTF8);
                SkyIslandPatrolData result;
                string error;
                if (TryParse(raw, out result, out error)) return result;
            }
            catch (Exception)
            {
                // 路径、权限与部署错误均回退；不在纯数据层引用游戏日志或吞掉整份巡逻内容。
            }
            return CreateFallback();
        }

        internal static bool TryParse(string raw, out SkyIslandPatrolData result, out string error)
        {
            result = null;
            error = "invalid_root";
            BossRushJsonValue root = BossRushJsonParser.ParseOrNull(raw);
            int version;
            string navigationSha256;
            List<BossRushJsonValue> profileItems, slotItems;
            if (!Fields(root, "version", "sourceNavigationSha256", "profiles", "slots") || !root.TryGetInt("version", out version) || version != Version ||
                !root.TryGetArray("profiles", out profileItems) || !root.TryGetArray("slots", out slotItems)) return false;
            if (!root.TryGetString("sourceNavigationSha256", out navigationSha256) || navigationSha256 != SourceNavigationSha256)
            { error = "source_navigation_sha256"; return false; }
            SkyIslandPatrolData expected = CreateFallback();
            if (profileItems.Count != expected.Profiles.Length || slotItems.Count != expected.Slots.Length)
            { error = "incomplete_content"; return false; }
            var profiles = new List<SkyIslandPatrolProfile>();
            var regions = new HashSet<string>(StringComparer.Ordinal);
            foreach (BossRushJsonValue item in profileItems)
            {
                string region; int rank; float hp, damage, sight, reaction;
                if (!Fields(item, "regionId", "rank", "healthFactor", "damageFactor", "sightDistance", "reactionTime") ||
                    !item.TryGetString("regionId", out region) || !item.TryGetInt("rank", out rank) || !regions.Add(region) ||
                    !item.TryGetFloat("healthFactor", out hp) || !item.TryGetFloat("damageFactor", out damage) ||
                    !item.TryGetFloat("sightDistance", out sight) || !item.TryGetFloat("reactionTime", out reaction) ||
                    !InRange(hp, 0.1f, 10f) || !InRange(damage, 0.1f, 5f) ||
                    !InRange(sight, 1f, 80f) || !InRange(reaction, 0.1f, 10f))
                { error = "invalid_profile"; return false; }
                SkyIslandPatrolProfile known = expected.FindProfile(region);
                if (known == null || rank != known.Rank) { error = "region_rank:" + region; return false; }
                profiles.Add(new SkyIslandPatrolProfile(region, rank, hp, damage, sight, reaction));
            }
            foreach (SkyIslandPatrolProfile a in profiles)
                foreach (SkyIslandPatrolProfile b in profiles)
                {
                    if ((a.Rank < b.Rank && (a.HealthFactor >= b.HealthFactor || a.DamageFactor >= b.DamageFactor ||
                            a.SightDistance > b.SightDistance || a.ReactionTime < b.ReactionTime)) ||
                        (a.Rank == b.Rank && (a.HealthFactor != b.HealthFactor || a.DamageFactor != b.DamageFactor ||
                            a.SightDistance != b.SightDistance || a.ReactionTime != b.ReactionTime)) ||
                        (a.Rank <= 3 && (a.SightDistance < 8f || a.SightDistance > 12f)))
                    { error = "difficulty_order"; return false; }
                }
            var slots = new List<SkyIslandPatrolSlot>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (BossRushJsonValue item in slotItems)
            {
                string id, region; float x, y, z;
                if (!Fields(item, "id", "regionId", "x", "y", "z") || !item.TryGetString("id", out id) ||
                    !item.TryGetString("regionId", out region) || !regions.Contains(region) || !ids.Add(id) ||
                    !item.TryGetFloat("x", out x) || !item.TryGetFloat("y", out y) || !item.TryGetFloat("z", out z) ||
                    !InRange(x, -475f, 475f) || !InRange(y, -8f, 100f) || !InRange(z, -425f, 425f))
                { error = "invalid_slot"; return false; }
                bool known = false;
                foreach (SkyIslandPatrolSlot stable in expected.Slots)
                    if (stable.Id == id && stable.RegionId == region) { known = true; break; }
                if (!known) { error = "slot_binding:" + id; return false; }
                foreach (SkyIslandPatrolSlot other in slots)
                {
                    if (other.RegionId != region) continue;
                    double dx = (double)x - other.X, dz = (double)z - other.Z;
                    if (dx * dx + dz * dz < SlotSpacing * SlotSpacing)
                    { error = "slot_spacing:" + id; return false; }
                }
                int count;
                counts.TryGetValue(region, out count); counts[region] = count + 1;
                slots.Add(new SkyIslandPatrolSlot(id, region, x, y, z));
            }
            foreach (string region in regions)
                if (!counts.ContainsKey(region) || counts[region] != TargetCount(region))
                { error = "slot_count:" + region; return false; }
            result = new SkyIslandPatrolData(profiles.ToArray(), slots.ToArray(), "JSON");
            error = null;
            return true;
        }

        private static bool InRange(float value, float min, float max)
        { return !float.IsNaN(value) && !float.IsInfinity(value) && value >= min && value <= max; }

        private static bool Fields(BossRushJsonValue value, params string[] names)
        {
            if (value == null || value.Kind != BossRushJsonKind.Object || value.Properties == null || value.Properties.Count != names.Length)
                return false;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (BossRushJsonProperty property in value.Properties)
                if (property == null || Array.IndexOf(names, property.Name) < 0 || !seen.Add(property.Name)) return false;
            return true;
        }

        internal static SkyIslandPatrolData CreateFallback()
        {
            return new SkyIslandPatrolData(new[]
            {
                new SkyIslandPatrolProfile("A", 1, 0.8f, 0.7f, 8.0f, 1.2f),
                new SkyIslandPatrolProfile("B", 2, 1.0f, 0.8f, 10.0f, 1.1f),
                new SkyIslandPatrolProfile("C", 3, 1.2f, 0.9f, 12.0f, 1.0f),
                new SkyIslandPatrolProfile("D", 4, 1.45f, 1.0f, 14.0f, 0.9f),
                new SkyIslandPatrolProfile("E", 5, 1.7f, 1.1f, 16.0f, 0.8f),
                new SkyIslandPatrolProfile("F", 6, 2.0f, 1.2f, 18.0f, 0.7f),
                new SkyIslandPatrolProfile("G", 7, 2.3f, 1.3f, 20.0f, 0.6f),
                new SkyIslandPatrolProfile("H", 8, 2.6f, 1.35f, 22.0f, 0.5f),
                new SkyIslandPatrolProfile("S1", 3, 1.2f, 0.9f, 12.0f, 1.0f),
                new SkyIslandPatrolProfile("S2", 4, 1.45f, 1.0f, 14.0f, 0.9f),
                new SkyIslandPatrolProfile("S3", 6, 2.0f, 1.2f, 18.0f, 0.7f),
                new SkyIslandPatrolProfile("S4", 7, 2.3f, 1.3f, 20.0f, 0.6f),
            }, new[]
            {
                new SkyIslandPatrolSlot("Patrol_A_01", "A", 1.687623f, 0.0f, -209.434333f),
                new SkyIslandPatrolSlot("Patrol_A_02", "A", -37.851635f, 0.0f, -241.366321f),
                new SkyIslandPatrolSlot("Patrol_A_03", "A", 32.662683f, 0.0f, -195.123251f),
                new SkyIslandPatrolSlot("Patrol_A_04", "A", -24.96504f, 0.0f, -195.543114f),
                new SkyIslandPatrolSlot("Patrol_A_05", "A", 15.815604f, 0.0f, -239.725472f),
                new SkyIslandPatrolSlot("Patrol_A_06", "A", 39.672749f, 0.0f, -220.660144f),
                new SkyIslandPatrolSlot("Patrol_A_07", "A", -13.863137f, 0.0f, -235.610756f),
                new SkyIslandPatrolSlot("Patrol_A_08", "A", -32.424461f, 0.0f, -216.388343f),
                new SkyIslandPatrolSlot("Patrol_A_09", "A", 8.321255f, 0.0f, -190.32365f),
                new SkyIslandPatrolSlot("Patrol_A_10", "A", 23.382241f, 0.0f, -212.315209f),
                new SkyIslandPatrolSlot("Patrol_B_01", "B", 48.018381f, 5.0f, -118.225925f),
                new SkyIslandPatrolSlot("Patrol_B_02", "B", 14.670597f, 5.0f, -88.23363f),
                new SkyIslandPatrolSlot("Patrol_B_03", "B", 43.826913f, 5.0f, -65.899653f),
                new SkyIslandPatrolSlot("Patrol_B_04", "B", -49.080063f, 5.0f, -99.338251f),
                new SkyIslandPatrolSlot("Patrol_B_05", "B", -49.773463f, 5.0f, -64.663448f),
                new SkyIslandPatrolSlot("Patrol_B_06", "B", -32.860155f, 5.0f, -131.306541f),
                new SkyIslandPatrolSlot("Patrol_B_07", "B", 8.907149f, 5.0f, -131.586112f),
                new SkyIslandPatrolSlot("Patrol_B_08", "B", -5.488152f, 5.0f, -55.069289f),
                new SkyIslandPatrolSlot("Patrol_B_09", "B", -14.588116f, 5.0f, -97.543605f),
                new SkyIslandPatrolSlot("Patrol_B_10", "B", 50.799372f, 5.0f, -91.974524f),
                new SkyIslandPatrolSlot("Patrol_B_11", "B", 20.882249f, 5.0f, -58.983896f),
                new SkyIslandPatrolSlot("Patrol_B_12", "B", 30.322212f, 5.0f, -105.096271f),
                new SkyIslandPatrolSlot("Patrol_B_13", "B", 3.190156f, 5.0f, -109.860245f),
                new SkyIslandPatrolSlot("Patrol_B_14", "B", 29.841472f, 5.0f, -129.45717f),
                new SkyIslandPatrolSlot("Patrol_C_01", "C", -141.189478f, 10.0f, -84.612547f),
                new SkyIslandPatrolSlot("Patrol_C_02", "C", -196.413486f, 10.0f, -89.453046f),
                new SkyIslandPatrolSlot("Patrol_C_03", "C", -174.887068f, 10.0f, -44.993504f),
                new SkyIslandPatrolSlot("Patrol_C_04", "C", -115.309237f, 10.0f, -101.845361f),
                new SkyIslandPatrolSlot("Patrol_C_05", "C", -128.1367f, 10.0f, -40.093182f),
                new SkyIslandPatrolSlot("Patrol_C_06", "C", -151.524988f, 10.0f, -114.859112f),
                new SkyIslandPatrolSlot("Patrol_C_07", "C", -205.996956f, 10.0f, -117.492931f),
                new SkyIslandPatrolSlot("Patrol_C_08", "C", -172.17716f, 10.0f, -73.472005f),
                new SkyIslandPatrolSlot("Patrol_C_09", "C", -116.799669f, 10.0f, -69.584963f),
                new SkyIslandPatrolSlot("Patrol_C_10", "C", -199.787847f, 10.0f, -56.149236f),
                new SkyIslandPatrolSlot("Patrol_C_11", "C", -146.999227f, 10.0f, -54.826385f),
                new SkyIslandPatrolSlot("Patrol_C_12", "C", -165.246553f, 10.0f, -98.930632f),
                new SkyIslandPatrolSlot("Patrol_C_13", "C", -215.224984f, 10.0f, -80.384127f),
                new SkyIslandPatrolSlot("Patrol_C_14", "C", -154.780228f, 10.0f, -36.645086f),
                new SkyIslandPatrolSlot("Patrol_C_15", "C", -192.325516f, 10.0f, -38.177307f),
                new SkyIslandPatrolSlot("Patrol_C_16", "C", -190.201083f, 10.0f, -111.017316f),
                new SkyIslandPatrolSlot("Patrol_C_17", "C", -121.341679f, 10.0f, -86.804592f),
                new SkyIslandPatrolSlot("Patrol_C_18", "C", -162.687339f, 10.0f, -59.587482f),
                new SkyIslandPatrolSlot("Patrol_D_01", "D", -205.616845f, 16.0f, 79.402688f),
                new SkyIslandPatrolSlot("Patrol_D_02", "D", -178.062815f, 16.0f, 49.604684f),
                new SkyIslandPatrolSlot("Patrol_D_03", "D", -131.800122f, 16.0f, 79.572164f),
                new SkyIslandPatrolSlot("Patrol_D_04", "D", -114.794042f, 16.0f, 54.288558f),
                new SkyIslandPatrolSlot("Patrol_D_05", "D", -150.412709f, 16.0f, 33.434849f),
                new SkyIslandPatrolSlot("Patrol_D_06", "D", -145.634238f, 16.0f, 113.753034f),
                new SkyIslandPatrolSlot("Patrol_D_07", "D", -167.832702f, 16.0f, 83.048432f),
                new SkyIslandPatrolSlot("Patrol_D_08", "D", -208.985073f, 16.0f, 112.697108f),
                new SkyIslandPatrolSlot("Patrol_D_09", "D", -208.216718f, 16.0f, 45.745406f),
                new SkyIslandPatrolSlot("Patrol_D_10", "D", -108.979301f, 16.0f, 104.491805f),
                new SkyIslandPatrolSlot("Patrol_D_11", "D", -144.929335f, 16.0f, 59.343433f),
                new SkyIslandPatrolSlot("Patrol_D_12", "D", -108.642925f, 16.0f, 79.614421f),
                new SkyIslandPatrolSlot("Patrol_D_13", "D", -186.149263f, 16.0f, 69.384809f),
                new SkyIslandPatrolSlot("Patrol_D_14", "D", -164.656091f, 16.0f, 104.714896f),
                new SkyIslandPatrolSlot("Patrol_D_15", "D", -171.738884f, 16.0f, 29.879765f),
                new SkyIslandPatrolSlot("Patrol_D_16", "D", -122.888183f, 16.0f, 38.161006f),
                new SkyIslandPatrolSlot("Patrol_D_17", "D", -149.203024f, 16.0f, 81.450974f),
                new SkyIslandPatrolSlot("Patrol_D_18", "D", -198.56591f, 16.0f, 98.14608f),
                new SkyIslandPatrolSlot("Patrol_D_19", "D", -188.971642f, 16.0f, 33.693945f),
                new SkyIslandPatrolSlot("Patrol_D_20", "D", -161.860856f, 16.0f, 54.101172f),
                new SkyIslandPatrolSlot("Patrol_D_21", "D", -207.529788f, 16.0f, 63.175902f),
                new SkyIslandPatrolSlot("Patrol_D_22", "D", -124.909018f, 16.0f, 101.368092f),
                new SkyIslandPatrolSlot("Patrol_E_01", "E", 9.89047f, 18.0f, 99.913464f),
                new SkyIslandPatrolSlot("Patrol_E_02", "E", -25.247746f, 18.0f, 86.466308f),
                new SkyIslandPatrolSlot("Patrol_E_03", "E", 3.627148f, 18.0f, 62.79702f),
                new SkyIslandPatrolSlot("Patrol_E_04", "E", 54.324212f, 18.0f, 88.667194f),
                new SkyIslandPatrolSlot("Patrol_E_05", "E", 43.299026f, 18.0f, 59.988835f),
                new SkyIslandPatrolSlot("Patrol_E_06", "E", -16.228392f, 18.0f, 40.208814f),
                new SkyIslandPatrolSlot("Patrol_E_07", "E", 42.850446f, 18.0f, 119.515822f),
                new SkyIslandPatrolSlot("Patrol_E_08", "E", -12.188967f, 18.0f, 115.062749f),
                new SkyIslandPatrolSlot("Patrol_E_09", "E", 29.260729f, 18.0f, 81.92446f),
                new SkyIslandPatrolSlot("Patrol_E_10", "E", -31.697092f, 18.0f, 63.69401f),
                new SkyIslandPatrolSlot("Patrol_E_11", "E", -0.677598f, 18.0f, 83.284389f),
                new SkyIslandPatrolSlot("Patrol_E_12", "E", 5.12463f, 18.0f, 42.876765f),
                new SkyIslandPatrolSlot("Patrol_E_13", "E", 21.815706f, 18.0f, 55.562446f),
                new SkyIslandPatrolSlot("Patrol_E_14", "E", 62.030128f, 18.0f, 117.612822f),
                new SkyIslandPatrolSlot("Patrol_E_15", "E", 18.042364f, 18.0f, 117.03824f),
                new SkyIslandPatrolSlot("Patrol_E_16", "E", 58.173311f, 18.0f, 70.911561f),
                new SkyIslandPatrolSlot("Patrol_E_17", "E", 36.852746f, 18.0f, 96.905396f),
                new SkyIslandPatrolSlot("Patrol_E_18", "E", -29.686532f, 18.0f, 102.260753f),
                new SkyIslandPatrolSlot("Patrol_E_19", "E", 3.540979f, 18.0f, 115.894992f),
                new SkyIslandPatrolSlot("Patrol_E_20", "E", -13.99008f, 18.0f, 95.661619f),
                new SkyIslandPatrolSlot("Patrol_E_21", "E", 25.106949f, 18.0f, 104.628595f),
                new SkyIslandPatrolSlot("Patrol_E_22", "E", 16.074975f, 18.0f, 77.758684f),
                new SkyIslandPatrolSlot("Patrol_F_01", "F", 125.848078f, 15.0f, -80.270174f),
                new SkyIslandPatrolSlot("Patrol_F_02", "F", 145.259073f, 15.0f, -53.185064f),
                new SkyIslandPatrolSlot("Patrol_F_03", "F", 210.477538f, 15.0f, -73.14381f),
                new SkyIslandPatrolSlot("Patrol_F_04", "F", 213.625442f, 15.0f, -39.963557f),
                new SkyIslandPatrolSlot("Patrol_F_05", "F", 186.011954f, 15.0f, -105.233158f),
                new SkyIslandPatrolSlot("Patrol_F_06", "F", 179.344743f, 15.0f, -42.895625f),
                new SkyIslandPatrolSlot("Patrol_F_07", "F", 148.619219f, 15.0f, -102.651673f),
                new SkyIslandPatrolSlot("Patrol_F_08", "F", 143.049873f, 15.0f, -20.049378f),
                new SkyIslandPatrolSlot("Patrol_F_09", "F", 226.51601f, 15.0f, -99.561276f),
                new SkyIslandPatrolSlot("Patrol_F_10", "F", 122.555508f, 15.0f, -109.850772f),
                new SkyIslandPatrolSlot("Patrol_F_11", "F", 168.477248f, 15.0f, -87.607601f),
                new SkyIslandPatrolSlot("Patrol_F_12", "F", 124.049564f, 15.0f, -40.394427f),
                new SkyIslandPatrolSlot("Patrol_F_13", "F", 225.049923f, 15.0f, -21.009152f),
                new SkyIslandPatrolSlot("Patrol_F_14", "F", 158.471912f, 15.0f, -34.633612f),
                new SkyIslandPatrolSlot("Patrol_F_15", "F", 203.513156f, 15.0f, -94.987096f),
                new SkyIslandPatrolSlot("Patrol_F_16", "F", 149.536585f, 15.0f, -83.560619f),
                new SkyIslandPatrolSlot("Patrol_F_17", "F", 126.96457f, 15.0f, -59.535227f),
                new SkyIslandPatrolSlot("Patrol_F_18", "F", 221.51924f, 15.0f, -56.623088f),
                new SkyIslandPatrolSlot("Patrol_F_19", "F", 168.410516f, 15.0f, -106.842921f),
                new SkyIslandPatrolSlot("Patrol_F_20", "F", 184.348239f, 15.0f, -81.749706f),
                new SkyIslandPatrolSlot("Patrol_F_21", "F", 225.260112f, 15.0f, -79.078495f),
                new SkyIslandPatrolSlot("Patrol_F_22", "F", 200.830485f, 15.0f, -110.260504f),
                new SkyIslandPatrolSlot("Patrol_G_01", "G", 175.34567f, 21.0f, 54.676087f),
                new SkyIslandPatrolSlot("Patrol_G_02", "G", 146.442166f, 21.0f, 67.607283f),
                new SkyIslandPatrolSlot("Patrol_G_03", "G", 218.034993f, 21.0f, 76.218558f),
                new SkyIslandPatrolSlot("Patrol_G_04", "G", 204.736571f, 21.0f, 111.950501f),
                new SkyIslandPatrolSlot("Patrol_G_05", "G", 201.572203f, 21.0f, 38.101598f),
                new SkyIslandPatrolSlot("Patrol_G_06", "G", 165.665224f, 21.0f, 103.71573f),
                new SkyIslandPatrolSlot("Patrol_G_07", "G", 133.405934f, 21.0f, 108.193465f),
                new SkyIslandPatrolSlot("Patrol_G_08", "G", 137.140792f, 21.0f, 35.772986f),
                new SkyIslandPatrolSlot("Patrol_G_09", "G", 131.678532f, 21.0f, 86.4513f),
                new SkyIslandPatrolSlot("Patrol_G_10", "G", 160.889249f, 21.0f, 83.196558f),
                new SkyIslandPatrolSlot("Patrol_G_11", "G", 219.270879f, 21.0f, 55.174759f),
                new SkyIslandPatrolSlot("Patrol_G_12", "G", 189.12f, 21.0f, 69.533333f),
                new SkyIslandPatrolSlot("Patrol_G_13", "G", 177.753966f, 21.0f, 36.255166f),
                new SkyIslandPatrolSlot("Patrol_G_14", "G", 217.48922f, 21.0f, 98.629524f),
                new SkyIslandPatrolSlot("Patrol_G_15", "G", 134.360726f, 21.0f, 54.920613f),
                new SkyIslandPatrolSlot("Patrol_G_16", "G", 153.482168f, 21.0f, 41.94515f),
                new SkyIslandPatrolSlot("Patrol_G_17", "G", 166.053703f, 21.0f, 66.617989f),
                new SkyIslandPatrolSlot("Patrol_G_18", "G", 193.36387f, 21.0f, 55.397094f),
                new SkyIslandPatrolSlot("Patrol_G_19", "G", 203.116f, 21.0f, 66.866667f),
                new SkyIslandPatrolSlot("Patrol_G_20", "G", 133.671669f, 21.0f, 73.206647f),
                new SkyIslandPatrolSlot("Patrol_G_21", "G", 143.883266f, 21.0f, 82.115869f),
                new SkyIslandPatrolSlot("Patrol_G_22", "G", 163.470168f, 21.0f, 50.044294f),
                new SkyIslandPatrolSlot("Patrol_G_23", "G", 210.274741f, 21.0f, 86.108312f),
                new SkyIslandPatrolSlot("Patrol_G_24", "G", 187.333751f, 21.0f, 43.985787f),
                new SkyIslandPatrolSlot("Patrol_G_25", "G", 177.854674f, 21.0f, 66.025076f),
                new SkyIslandPatrolSlot("Patrol_G_26", "G", 177.121783f, 21.0f, 105.195298f),
                new SkyIslandPatrolSlot("Patrol_H_01", "H", 6.181274f, 26.0f, 197.699996f),
                new SkyIslandPatrolSlot("Patrol_H_02", "H", 6.436669f, 26.0f, 235.656748f),
                new SkyIslandPatrolSlot("Patrol_H_03", "H", -24.785092f, 26.0f, 210.1731f),
                new SkyIslandPatrolSlot("Patrol_H_04", "H", 37.687797f, 26.0f, 260.579462f),
                new SkyIslandPatrolSlot("Patrol_H_05", "H", 27.669793f, 26.0f, 225.256898f),
                new SkyIslandPatrolSlot("Patrol_H_06", "H", 24.310451f, 26.0f, 185.571539f),
                new SkyIslandPatrolSlot("Patrol_H_07", "H", -42.610701f, 26.0f, 198.307347f),
                new SkyIslandPatrolSlot("Patrol_H_08", "H", -6.668977f, 26.0f, 215.976079f),
                new SkyIslandPatrolSlot("Patrol_H_09", "H", -8.859074f, 26.0f, 186.428781f),
                new SkyIslandPatrolSlot("Patrol_H_10", "H", 39.419165f, 26.0f, 242.2548f),
                new SkyIslandPatrolSlot("Patrol_H_11", "H", 20.469571f, 26.0f, 206.662141f),
                new SkyIslandPatrolSlot("Patrol_H_12", "H", -12.584747f, 26.0f, 201.056688f),
                new SkyIslandPatrolSlot("Patrol_H_13", "H", -31.448425f, 26.0f, 223.189706f),
                new SkyIslandPatrolSlot("Patrol_H_14", "H", 25.811918f, 26.0f, 238.247529f),
                new SkyIslandPatrolSlot("Patrol_H_15", "H", 50.457173f, 26.0f, 258.941767f),
                new SkyIslandPatrolSlot("Patrol_H_16", "H", 15.326958f, 26.0f, 228.190592f),
                new SkyIslandPatrolSlot("Patrol_H_17", "H", -13.197297f, 26.0f, 225.115195f),
                new SkyIslandPatrolSlot("Patrol_H_18", "H", 47.565126f, 26.0f, 248.646782f),
                new SkyIslandPatrolSlot("Patrol_H_19", "H", 14.715555f, 26.0f, 188.316444f),
                new SkyIslandPatrolSlot("Patrol_H_20", "H", -33.143214f, 26.0f, 197.113795f),
                new SkyIslandPatrolSlot("Patrol_S1_01", "S1", -277.547808f, 7.0f, -108.604236f),
                new SkyIslandPatrolSlot("Patrol_S1_02", "S1", -309.569094f, 7.0f, -110.921636f),
                new SkyIslandPatrolSlot("Patrol_S1_03", "S1", -298.123165f, 7.0f, -87.124243f),
                new SkyIslandPatrolSlot("Patrol_S1_04", "S1", -271.142795f, 7.0f, -89.799348f),
                new SkyIslandPatrolSlot("Patrol_S1_05", "S1", -308.003885f, 7.0f, -95.716131f),
                new SkyIslandPatrolSlot("Patrol_S1_06", "S1", -288.981263f, 7.0f, -95.857389f),
                new SkyIslandPatrolSlot("Patrol_S1_07", "S1", -270.312283f, 7.0f, -105.690127f),
                new SkyIslandPatrolSlot("Patrol_S1_08", "S1", -304.934119f, 7.0f, -105.132955f),
                new SkyIslandPatrolSlot("Patrol_S2_01", "S2", -280.979168f, 13.0f, 97.394487f),
                new SkyIslandPatrolSlot("Patrol_S2_02", "S2", -298.52853f, 13.0f, 116.821059f),
                new SkyIslandPatrolSlot("Patrol_S2_03", "S2", -266.336237f, 13.0f, 114.145054f),
                new SkyIslandPatrolSlot("Patrol_S2_04", "S2", -265.569331f, 13.0f, 83.262683f),
                new SkyIslandPatrolSlot("Patrol_S2_05", "S2", -298.121364f, 13.0f, 100.613057f),
                new SkyIslandPatrolSlot("Patrol_S2_06", "S2", -268.462951f, 13.0f, 101.751333f),
                new SkyIslandPatrolSlot("Patrol_S2_07", "S2", -277.88435f, 13.0f, 114.82154f),
                new SkyIslandPatrolSlot("Patrol_S2_08", "S2", -275.302387f, 13.0f, 86.096575f),
                new SkyIslandPatrolSlot("Patrol_S3_01", "S3", 283.087566f, 10.0f, -62.038996f),
                new SkyIslandPatrolSlot("Patrol_S3_02", "S3", 323.91661f, 10.0f, -86.564252f),
                new SkyIslandPatrolSlot("Patrol_S3_03", "S3", 306.349285f, 10.0f, -62.059287f),
                new SkyIslandPatrolSlot("Patrol_S3_04", "S3", 284.303689f, 10.0f, -83.888024f),
                new SkyIslandPatrolSlot("Patrol_S3_05", "S3", 291.967051f, 10.0f, -54.829218f),
                new SkyIslandPatrolSlot("Patrol_S3_06", "S3", 282.545612f, 10.0f, -72.845609f),
                new SkyIslandPatrolSlot("Patrol_S3_07", "S3", 283.14333f, 10.0f, -52.861109f),
                new SkyIslandPatrolSlot("Patrol_S4_01", "S4", 282.442113f, 26.0f, 80.249737f),
                new SkyIslandPatrolSlot("Patrol_S4_02", "S4", 309.452996f, 26.0f, 61.734513f),
                new SkyIslandPatrolSlot("Patrol_S4_03", "S4", 314.022014f, 26.0f, 96.265421f),
                new SkyIslandPatrolSlot("Patrol_S4_04", "S4", 275.334511f, 26.0f, 53.955286f),
                new SkyIslandPatrolSlot("Patrol_S4_05", "S4", 292.552077f, 26.0f, 60.041063f),
                new SkyIslandPatrolSlot("Patrol_S4_06", "S4", 275.710198f, 26.0f, 95.211358f),
                new SkyIslandPatrolSlot("Patrol_S4_07", "S4", 275.411301f, 26.0f, 66.5297f),
                new SkyIslandPatrolSlot("Patrol_S4_08", "S4", 314.297072f, 26.0f, 71.353154f),
                new SkyIslandPatrolSlot("Patrol_S4_09", "S4", 296.63791f, 26.0f, 69.500729f),
                new SkyIslandPatrolSlot("Patrol_S4_10", "S4", 275.083297f, 26.0f, 75.977346f),
            }, "Fallback");
        }
    }
}
