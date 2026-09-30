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
    /// COMPAT：普通巡逻专用内容表。坐标取 collision_navigation.json 岛区三角形中心，运行时不随机选点。
    /// 每个巡逻 owner 入图时 Load 一次并持有结果；本类型不持有缓存、场景、活体或存档，不需要静态清理。
    /// JSON 缺失或结构/身份/数值无效时整表回退；离线几何由 SkyIslandPatrolPlacementPropertyTest 独立复算。
    /// </summary>
    internal static class SkyIslandPatrolRules
    {
        internal const string RelativePath = "Assets/Data/SkyIsland/Patrols.json";
        internal const int Version = 1;
        // 固定坐标的采样来源；与 JSON 一起由定向几何验收核对当前 NAV。
        internal const string SourceNavigationSha256 = "a9ca01b55ef559aefedaec3a81eac896fe7047083eda9b575b05ee48dbc17a42";
        // 仅属于普通巡逻。既有 SkyIslandEncounters 的自动组上限和剧情预留保持独立。
        internal const int ActiveLimit = 24;
        internal const float ActivationRadius = 70f;
        internal const float SuspensionRadius = 110f;
        internal const float SpawnInterval = 0.12f;
        internal const float TickInterval = 0.25f;
        internal const float SpawnClearance = 14f, ResidentClearance = 8f, InteractionClearance = 4f, SlotSpacing = 5f;

        internal static int TargetCount(string regionId)
        {
            switch (regionId)
            {
                case "A": return 6;
                case "B": return 8;
                case "C": return 10;
                case "D": case "E": case "F": return 12;
                case "G": case "H": return 14;
                case "S1": case "S2": case "S3": case "S4": return 6;
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
                new SkyIslandPatrolSlot("Patrol_A_01", "A", -50.956847f, 0.0f, -184.486664f),
                new SkyIslandPatrolSlot("Patrol_A_02", "A", 51.214229f, 0.0f, -255.517519f),
                new SkyIslandPatrolSlot("Patrol_A_03", "A", 26.216173f, 0.0f, -182.44266f),
                new SkyIslandPatrolSlot("Patrol_A_04", "A", -24.910386f, 0.0f, -257.920574f),
                new SkyIslandPatrolSlot("Patrol_A_05", "A", -7.779276f, 0.0f, -213.082091f),
                new SkyIslandPatrolSlot("Patrol_A_06", "A", -52.579537f, 0.0f, -230.367604f),
                new SkyIslandPatrolSlot("Patrol_B_01", "B", -61.140798f, 5.0f, -138.934601f),
                new SkyIslandPatrolSlot("Patrol_B_02", "B", 60.651587f, 5.0f, -50.341232f),
                new SkyIslandPatrolSlot("Patrol_B_03", "B", -42.637721f, 5.0f, -44.187044f),
                new SkyIslandPatrolSlot("Patrol_B_04", "B", 40.13661f, 5.0f, -144.233852f),
                new SkyIslandPatrolSlot("Patrol_B_05", "B", -4.959894f, 5.0f, -96.709205f),
                new SkyIslandPatrolSlot("Patrol_B_06", "B", 9.964543f, 5.0f, -44.979129f),
                new SkyIslandPatrolSlot("Patrol_B_07", "B", -58.29176f, 5.0f, -90.168176f),
                new SkyIslandPatrolSlot("Patrol_B_08", "B", -14.010416f, 5.0f, -144.685734f),
                new SkyIslandPatrolSlot("Patrol_C_01", "C", -108.764735f, 10.0f, -134.004551f),
                new SkyIslandPatrolSlot("Patrol_C_02", "C", -225.132909f, 10.0f, -31.349286f),
                new SkyIslandPatrolSlot("Patrol_C_03", "C", -113.20951f, 10.0f, -25.207405f),
                new SkyIslandPatrolSlot("Patrol_C_04", "C", -216.476186f, 10.0f, -134.563283f),
                new SkyIslandPatrolSlot("Patrol_C_05", "C", -164.483577f, 10.0f, -78.547926f),
                new SkyIslandPatrolSlot("Patrol_C_06", "C", -103.395146f, 10.0f, -79.711363f),
                new SkyIslandPatrolSlot("Patrol_C_07", "C", -169.754276f, 10.0f, -24.442195f),
                new SkyIslandPatrolSlot("Patrol_C_08", "C", -226.650792f, 10.0f, -83.468844f),
                new SkyIslandPatrolSlot("Patrol_C_09", "C", -159.300706f, 10.0f, -133.146652f),
                new SkyIslandPatrolSlot("Patrol_C_10", "C", -196.466968f, 10.0f, -55.337037f),
                new SkyIslandPatrolSlot("Patrol_D_01", "D", -210.380632f, 16.0f, 130.77398f),
                new SkyIslandPatrolSlot("Patrol_D_02", "D", -103.82994f, 16.0f, 18.967725f),
                new SkyIslandPatrolSlot("Patrol_D_03", "D", -216.293053f, 16.0f, 19.978703f),
                new SkyIslandPatrolSlot("Patrol_D_04", "D", -101.217818f, 16.0f, 126.345672f),
                new SkyIslandPatrolSlot("Patrol_D_05", "D", -160.923592f, 16.0f, 78.432635f),
                new SkyIslandPatrolSlot("Patrol_D_06", "D", -158.67639f, 16.0f, 16.507871f),
                new SkyIslandPatrolSlot("Patrol_D_07", "D", -155.37996f, 16.0f, 129.039066f),
                new SkyIslandPatrolSlot("Patrol_D_08", "D", -211.482933f, 16.0f, 76.296444f),
                new SkyIslandPatrolSlot("Patrol_D_09", "D", -103.673415f, 16.0f, 75.571401f),
                new SkyIslandPatrolSlot("Patrol_D_10", "D", -184.823075f, 16.0f, 46.919909f),
                new SkyIslandPatrolSlot("Patrol_D_11", "D", -126.191478f, 16.0f, 98.642559f),
                new SkyIslandPatrolSlot("Patrol_D_12", "D", -193.508995f, 16.0f, 103.798238f),
                new SkyIslandPatrolSlot("Patrol_E_01", "E", -37.223656f, 18.0f, 127.33826f),
                new SkyIslandPatrolSlot("Patrol_E_02", "E", 70.944391f, 18.0f, 34.057358f),
                new SkyIslandPatrolSlot("Patrol_E_03", "E", -32.857268f, 18.0f, 30.16772f),
                new SkyIslandPatrolSlot("Patrol_E_04", "E", 61.220309f, 18.0f, 129.949517f),
                new SkyIslandPatrolSlot("Patrol_E_05", "E", 24.489034f, 18.0f, 65.392056f),
                new SkyIslandPatrolSlot("Patrol_E_06", "E", 11.837715f, 18.0f, 125.843296f),
                new SkyIslandPatrolSlot("Patrol_E_07", "E", -40.851084f, 18.0f, 80.662949f),
                new SkyIslandPatrolSlot("Patrol_E_08", "E", 72.563161f, 18.0f, 82.455667f),
                new SkyIslandPatrolSlot("Patrol_E_09", "E", 11.686237f, 18.0f, 29.433651f),
                new SkyIslandPatrolSlot("Patrol_E_10", "E", -14.683088f, 18.0f, 100.078195f),
                new SkyIslandPatrolSlot("Patrol_E_11", "E", 36.384534f, 18.0f, 96.455782f),
                new SkyIslandPatrolSlot("Patrol_E_12", "E", -6.703062f, 18.0f, 64.839894f),
                new SkyIslandPatrolSlot("Patrol_F_01", "F", 174.111666f, 15.0f, -10.30818f),
                new SkyIslandPatrolSlot("Patrol_F_02", "F", 230.738665f, 15.0f, -121.190572f),
                new SkyIslandPatrolSlot("Patrol_F_03", "F", 114.563564f, 15.0f, -111.655213f),
                new SkyIslandPatrolSlot("Patrol_F_04", "F", 237.323485f, 15.0f, -46.466888f),
                new SkyIslandPatrolSlot("Patrol_F_05", "F", 113.401579f, 15.0f, -39.827308f),
                new SkyIslandPatrolSlot("Patrol_F_06", "F", 181.30927f, 15.0f, -83.694254f),
                new SkyIslandPatrolSlot("Patrol_F_07", "F", 220.411178f, 15.0f, -9.071067f),
                new SkyIslandPatrolSlot("Patrol_F_08", "F", 157.323391f, 15.0f, -114.328487f),
                new SkyIslandPatrolSlot("Patrol_F_09", "F", 198.198055f, 15.0f, -44.35438f),
                new SkyIslandPatrolSlot("Patrol_F_10", "F", 130.99509f, 15.0f, -8.481466f),
                new SkyIslandPatrolSlot("Patrol_F_11", "F", 222.549333f, 15.0f, -78.230685f),
                new SkyIslandPatrolSlot("Patrol_F_12", "F", 151.994675f, 15.0f, -40.443996f),
                new SkyIslandPatrolSlot("Patrol_G_01", "G", 221.628152f, 21.0f, 30.445328f),
                new SkyIslandPatrolSlot("Patrol_G_02", "G", 121.318501f, 21.0f, 119.346749f),
                new SkyIslandPatrolSlot("Patrol_G_03", "G", 220.715243f, 21.0f, 125.014312f),
                new SkyIslandPatrolSlot("Patrol_G_04", "G", 130.272725f, 21.0f, 26.715444f),
                new SkyIslandPatrolSlot("Patrol_G_05", "G", 177.446385f, 21.0f, 66.348871f),
                new SkyIslandPatrolSlot("Patrol_G_06", "G", 227.369438f, 21.0f, 76.445064f),
                new SkyIslandPatrolSlot("Patrol_G_07", "G", 170.583261f, 21.0f, 117.494533f),
                new SkyIslandPatrolSlot("Patrol_G_08", "G", 119.621193f, 21.0f, 72.851561f),
                new SkyIslandPatrolSlot("Patrol_G_09", "G", 181.040209f, 21.0f, 23.994672f),
                new SkyIslandPatrolSlot("Patrol_G_10", "G", 147.977981f, 21.0f, 88.322938f),
                new SkyIslandPatrolSlot("Patrol_G_11", "G", 201.426648f, 21.0f, 49.34683f),
                new SkyIslandPatrolSlot("Patrol_G_12", "G", 195.449333f, 21.0f, 105.866667f),
                new SkyIslandPatrolSlot("Patrol_G_13", "G", 230.497517f, 21.0f, 101.56597f),
                new SkyIslandPatrolSlot("Patrol_G_14", "G", 148.940832f, 21.0f, 36.845094f),
                new SkyIslandPatrolSlot("Patrol_H_01", "H", -49.567994f, 26.0f, 272.510454f),
                new SkyIslandPatrolSlot("Patrol_H_02", "H", 63.438291f, 26.0f, 180.370142f),
                new SkyIslandPatrolSlot("Patrol_H_03", "H", -37.686405f, 26.0f, 174.737453f),
                new SkyIslandPatrolSlot("Patrol_H_04", "H", 55.08083f, 26.0f, 275.116005f),
                new SkyIslandPatrolSlot("Patrol_H_05", "H", 15.364176f, 26.0f, 228.177615f),
                new SkyIslandPatrolSlot("Patrol_H_06", "H", 13.57868f, 26.0f, 173.820712f),
                new SkyIslandPatrolSlot("Patrol_H_07", "H", -41.729555f, 26.0f, 222.42304f),
                new SkyIslandPatrolSlot("Patrol_H_08", "H", 53.102281f, 26.0f, 229.186947f),
                new SkyIslandPatrolSlot("Patrol_H_09", "H", 10.912434f, 26.0f, 271.224989f),
                new SkyIslandPatrolSlot("Patrol_H_10", "H", -20.209342f, 26.0f, 249.691369f),
                new SkyIslandPatrolSlot("Patrol_H_11", "H", -11.30191f, 26.0f, 197.313168f),
                new SkyIslandPatrolSlot("Patrol_H_12", "H", 34.950382f, 26.0f, 204.255437f),
                new SkyIslandPatrolSlot("Patrol_H_13", "H", -52.563448f, 26.0f, 247.122402f),
                new SkyIslandPatrolSlot("Patrol_H_14", "H", 25.796635f, 26.0f, 242.6548f),
                new SkyIslandPatrolSlot("Patrol_S1_01", "S1", -262.197249f, 7.0f, -85.149257f),
                new SkyIslandPatrolSlot("Patrol_S1_02", "S1", -316.099627f, 7.0f, -111.814148f),
                new SkyIslandPatrolSlot("Patrol_S1_03", "S1", -280.999515f, 7.0f, -119.804781f),
                new SkyIslandPatrolSlot("Patrol_S1_04", "S1", -316.233526f, 7.0f, -78.037238f),
                new SkyIslandPatrolSlot("Patrol_S1_05", "S1", -290.445073f, 7.0f, -93.669851f),
                new SkyIslandPatrolSlot("Patrol_S1_06", "S1", -262.509724f, 7.0f, -111.357475f),
                new SkyIslandPatrolSlot("Patrol_S2_01", "S2", -259.15281f, 13.0f, 114.135664f),
                new SkyIslandPatrolSlot("Patrol_S2_02", "S2", -305.167562f, 13.0f, 75.051779f),
                new SkyIslandPatrolSlot("Patrol_S2_03", "S2", -301.538198f, 13.0f, 119.697729f),
                new SkyIslandPatrolSlot("Patrol_S2_04", "S2", -259.440016f, 13.0f, 79.772227f),
                new SkyIslandPatrolSlot("Patrol_S2_05", "S2", -281.013693f, 13.0f, 97.335381f),
                new SkyIslandPatrolSlot("Patrol_S2_06", "S2", -301.077652f, 13.0f, 100.588409f),
                new SkyIslandPatrolSlot("Patrol_S3_01", "S3", 332.333454f, 10.0f, -37.329442f),
                new SkyIslandPatrolSlot("Patrol_S3_02", "S3", 275.584812f, 10.0f, -94.108643f),
                new SkyIslandPatrolSlot("Patrol_S3_03", "S3", 275.217502f, 10.0f, -37.289374f),
                new SkyIslandPatrolSlot("Patrol_S3_04", "S3", 329.447204f, 10.0f, -89.304945f),
                new SkyIslandPatrolSlot("Patrol_S3_05", "S3", 306.929298f, 10.0f, -62.004197f),
                new SkyIslandPatrolSlot("Patrol_S3_06", "S3", 271.317447f, 10.0f, -64.71298f),
                new SkyIslandPatrolSlot("Patrol_S4_01", "S4", 322.667415f, 26.0f, 104.496735f),
                new SkyIslandPatrolSlot("Patrol_S4_02", "S4", 265.934543f, 26.0f, 47.030382f),
                new SkyIslandPatrolSlot("Patrol_S4_03", "S4", 266.961675f, 26.0f, 104.181865f),
                new SkyIslandPatrolSlot("Patrol_S4_04", "S4", 320.998095f, 26.0f, 49.441363f),
                new SkyIslandPatrolSlot("Patrol_S4_05", "S4", 296.631274f, 26.0f, 69.535225f),
                new SkyIslandPatrolSlot("Patrol_S4_06", "S4", 264.308199f, 26.0f, 74.827086f),
            }, "Fallback");
        }
    }
}
