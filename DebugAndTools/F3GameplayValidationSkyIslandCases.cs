// ============================================================================
// F3GameplayValidationSkyIslandCases.cs - 天空岛岛内验收的逐条判据
// ============================================================================
// 编排、启动门与导航探路在 F3GameplayValidationSkyIsland.cs；这里只放同步用例本体，
// 一是让主文件守住「一眼看清跑了哪些用例」，二是两边都留在 1200 行的新文件预算内
// （tests/LargeFileBudgetGuard.py）。拆分不改变任何断言。
//
// 与主文件同一条纪律：**这里的每一条都只读**。岛内验收跑在玩家真实的这趟出击上，
// 任何写剧情、移动玩家、生成敌人或开箱的动作都会污染他正在做的事。
// ============================================================================

using System;
using System.Collections.Generic;
using Duckov.Scenes;
using Duckov.UI;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    internal sealed partial class F3GameplayValidationRunner
    {
        // ====================================================================
        // 1/5 场景装配与官方合同
        // ====================================================================

        private bool ValidateSkyIslandSessionReady(out string metrics, out string reason)
        {
            reason = null;
            SkyIslandSession session = SkyIslandSessionOrNull();
            if (session == null) { metrics = string.Empty; reason = "session_missing"; return false; }
            SkyIslandValidationSnapshot snapshot = session.ValidationSnapshot();
            metrics = snapshot.Describe();
            bool ok = snapshot.Ready && !snapshot.Closed && !snapshot.Returning && !snapshot.DeathPending
                && snapshot.NavigationReady && snapshot.WorldRootActive && session.IsReady;
            if (!ok) reason = "会话未处于「已就绪、未返航、未死亡、地形根已激活」状态";
            return ok;
        }

        private bool ValidateSkyIslandOfficialContract(out string metrics, out string reason)
        {
            reason = null;
            SkyIslandSession session = SkyIslandSessionOrNull();
            if (session == null) { metrics = string.Empty; reason = "session_missing"; return false; }
            Scene scene = session.ValidationScene;
            string contractError;
            bool ok = SkyIslandOfficialContract.Verify(scene, SkyIslandSceneReferenceBridge.SceneId, out contractError);
            metrics = "scene=" + scene.path + ",raid_map=" + (LevelManager.Instance != null && LevelManager.Instance.IsRaidMap)
                + ",save_character=" + LevelConfig.SaveCharacter + ",spawn_tomb=" + LevelConfig.SpawnTomb;
            if (!ok) reason = contractError;
            return ok;
        }

        private bool ValidateSkyIslandSceneIdentity(out string metrics, out string reason)
        {
            reason = null;
            Scene active = SceneManager.GetActiveScene();
            bool raid = SkyIslandRaidLease.IsRaidScene(active);
            bool subScene = string.Equals(MultiSceneCore.ActiveSubSceneID, SkyIslandSceneReferenceBridge.SceneId,
                StringComparison.Ordinal);
            bool bundle = SkyIslandRaidLease.IsBundleDeployed();
            // 基地必须真的被卸载：独立出击关卡不是 additive 资源场景，基地留在内存里
            // 就说明这趟根本没走官方 Single 加载（也会让往返计数基线失去意义）。
            bool baseUnloaded = !SceneManager.GetSceneByName("Base_SceneV2").isLoaded;
            metrics = "active=" + active.name + ",sub_scene=" + MultiSceneCore.ActiveSubSceneID
                + ",bundle_deployed=" + bundle + ",base_unloaded=" + baseUnloaded
                + ",loaded_scenes=" + SceneManager.sceneCount;
            bool ok = raid && subScene && bundle && baseUnloaded;
            if (!ok) reason = "场景身份、子图 ID、场景包或基地卸载状态不符合独立出击口径";
            return ok;
        }

        private bool ValidateSkyIslandNavigationGraph(out string metrics, out string reason)
        {
            reason = null;
            metrics = string.Empty;
            SkyIslandSession session = SkyIslandSessionOrNull();
            GameObject root = session == null ? null : session.ValidationWorldRoot;
            if (root == null) { reason = "world_root_missing"; return false; }
            Transform nav = root.transform.Find("Navigation");
            MeshFilter filter = nav == null ? null : nav.GetComponent<MeshFilter>();
            Mesh mesh = filter == null ? null : filter.sharedMesh;
            int vertices = mesh == null ? 0 : mesh.vertexCount;
            int nodes = session.ValidationSnapshot().NavigationNodes;
            metrics = "nav_vertices=" + vertices + "/4095,walkable_nodes=" + nodes
                + ",astar_active=" + (global::AstarPath.active != null);
            // 4095 是实现硬上限（`SkyIslandOfficialContract` 同址断言）。留出的余量随内容增长而缩小，
            // 所以这里把实际值写进 metrics，加区域前可以直接翻报告。
            bool ok = mesh != null && mesh.isReadable && vertices > 0 && vertices <= 4095
                && nodes > 0 && global::AstarPath.active != null;
            if (!ok) reason = "导航网格缺失/不可读/超上限，或没有可走节点";
            return ok;
        }

        private bool ValidateSkyIslandExplosionPatch(out string metrics, out string reason)
        {
            reason = null;
            Scene active = SceneManager.GetActiveScene();
            bool armed = SkyIslandExplosionObstaclePatch.IsArmedFor(active);
            // 官方在 y=0 平地传入的是「爆点 +0.2」与「目标 +0.6」，压平后必须逐位等于原版的 0.5。
            float flat = SkyIslandExplosionObstaclePatch.FlattenHeight(0.2f, 0.6f);
            bool parity = Mathf.Abs(flat - 0.5f) < 0.0001f;
            metrics = "armed_for_active_scene=" + armed + ",flatten(0.2,0.6)=" + flat.ToString("F3");
            bool ok = armed && parity;
            if (!ok) reason = armed ? "压平高度与官方 0.5 不再逐位一致" : "爆炸遮挡补丁未对当前场景武装（岛上爆炸会穿墙）";
            return ok;
        }

        // ====================================================================
        // 2/5 内容装配与落位
        // ====================================================================

        private bool ValidateSkyIslandMarkers(out string metrics, out string reason)
        {
            reason = null;
            metrics = string.Empty;
            SkyIslandSession session = SkyIslandSessionOrNull();
            if (session == null) { reason = "session_missing"; return false; }
            GameObject root = session.ValidationWorldRoot;
            if (root == null) { reason = "world_root_missing"; return false; }
            SkyIslandValidationSnapshot snapshot = session.ValidationSnapshot();
            bool spawn = session.ValidationPlayerSpawn != null;
            bool exit = session.ValidationExitMarker != null;
            bool bellMarker = session.ValidationBellMarker != null;

            // 判据是**这 12 个区域标记都在**，不是「地标总数正好 12」。
            // 场景包里还有 `POI_B_Mural` 这种以 POI_ 开头的装饰节点，它同样会被
            // `SkyIslandSession.PrepareMarkers` 收进 landmarks，于是总数是 13 而不是 12。
            // 按总数断言会在完全正常的包上假红——这正是本用例第一次跑之前发现的。
            string[] required =
            {
                "POI_A", "POI_B", "POI_C", "POI_D", "POI_E", "POI_F", "POI_G", "POI_H",
                "POI_S1", "POI_S2", "POI_S3", "POI_S4"
            };
            List<string> missing = new List<string>();
            for (int i = 0; i < required.Length; i++)
                if (root.transform.Find(required[i]) == null) missing.Add(required[i]);
            // 多出来的 POI_ 节点只报数不判红：它们是装饰，但**会**让「巡视群岛区域」委托的
            // 可完成量永久多算（`RegionBit` 对未登记名字返回 0，那一格永远算作未访问）。
            int extras = snapshot.Landmarks - required.Length;

            metrics = "player_spawn=" + spawn + ",exit=" + exit + ",bell_marker=" + bellMarker
                + ",searches=" + snapshot.SearchPoints + ",region_landmarks=" + (required.Length - missing.Count)
                + "/" + required.Length + ",extra_poi_nodes=" + extras
                + ",landmarks_total=" + snapshot.Landmarks + ",enemy_markers=" + snapshot.EnemyMarkers;
            bool ok = spawn && exit && bellMarker && missing.Count == 0
                && snapshot.SearchPoints > 0 && snapshot.EnemyMarkers > 0;
            if (!ok)
                reason = missing.Count > 0
                    ? "缺少区域地标：" + string.Join(",", missing.ToArray())
                    : "出生 / 撤离 / 归航钟庭 / 搜索点 / 敌人点位不完整";
            return ok;
        }

        private bool ValidateSkyIslandContentTable(out string metrics, out string reason)
        {
            reason = null;
            metrics = string.Empty;
            SkyIslandSession session = SkyIslandSessionOrNull();
            if (session == null) { reason = "session_missing"; return false; }
            SkyIslandValidationSnapshot snapshot = session.ValidationSnapshot();
            SkyIslandContentData fallback = SkyIslandContent.CreateFallback();
            metrics = "source=" + snapshot.ContentSource + ",encounters=" + snapshot.ContentEncounters
                + "/" + fallback.Encounters.Length + ",gates=" + snapshot.ContentGates
                + "/" + fallback.Gates.Length + ",groups_built=" + snapshot.EncounterGroups;
            // Source == "Fallback" 说明已部署的 World.json 读不出来或校验没过：功能仍在，
            // 但玩家装的那份数据是坏的，属于部署缺陷，必须红。
            bool ok = string.Equals(snapshot.ContentSource, "Json", StringComparison.Ordinal)
                && snapshot.ContentEncounters == fallback.Encounters.Length
                && snapshot.ContentGates == fallback.Gates.Length
                && snapshot.EncounterGroups == snapshot.ContentEncounters;
            if (!ok) reason = "内容表未来自已部署 JSON，或遭遇/门数量与内置表不一致";
            return ok;
        }

        private bool ValidateSkyIslandScavengePlacement(out string metrics, out string reason)
        {
            reason = null;
            metrics = string.Empty;
            SkyIslandSession session = SkyIslandSessionOrNull();
            if (session == null) { reason = "session_missing"; return false; }
            SkyIslandValidationSnapshot snapshot = session.ValidationSnapshot();
            metrics = "placed=" + snapshot.ScavengePlaced + ",failed=" + snapshot.ScavengeFailed
                + ",anchors=" + snapshot.ScavengeAnchors + ",opened=" + snapshot.ScavengeOpened
                + ",available=" + snapshot.ScavengeAvailable + ",seed=" + snapshot.RaidSeed;
            // `PlacedPoints` 已经扣掉 Failed，所以「落位 + 建箱失败 == 锚点总数」才说明
            // 39 个锚点全部解析成功。少了就是有锚点连落点都没算出来——运行时静默跳过，玩家什么都看不到。
            bool resolved = snapshot.ScavengePlaced + snapshot.ScavengeFailed == snapshot.ScavengeAnchors;
            // 建箱失败是 fail-open，但离线几何回归（SkyIslandContentPlacementPropertyTest）
            // 已经证明 39 个锚点在真实几何上全部落得下来，所以运行时失败一定是真缺陷。
            bool built = snapshot.ScavengeFailed == 0;
            if (!resolved) reason = "有搜刮锚点没有解析出落点（运行时静默跳过）";
            else if (!built) reason = "有搜刮点建箱失败；离线几何已证明全部锚点可落位，这是真缺陷";
            return resolved && built;
        }

        /// <summary>
        /// U1 同点交互竞争的**运行时**判据。
        ///
        /// 官方 `CA_Interact.SearchInteractableAround` 按「玩家到 collider 距离**严格小于**」
        /// 取唯一目标，同距时由 `Physics.OverlapSphereNonAlloc` 的返回顺序决定，且不同交互组
        /// 之间滚轮切不过去。于是两个交互体的触发体积一旦相交，玩家站在重叠区里就有一个按不到。
        ///
        /// 生产侧的三条退避（纪念物 3.2 m、航路图 3.2 m、谢礼箱 3 m）只躲开了**各自的那一个锚点**，
        /// `SkyIslandRewardCrate.TryFindCratePosition` 本身不做任何「与其它交互体净空」的裁决。
        /// 这里把当前场景里所有 Interactable 层的触发体两两算一遍，直接把重叠对报出来。
        ///
        /// 同组不算冲突：子交互体是 owner 的子物体（`NPCInteractionGroupHelper.AddSubInteractable`），
        /// 玩家可以滚轮切换。判据用「一方是另一方的后代」或「共用同一个最近的交互体/角色祖先」。
        /// </summary>
        private bool ValidateSkyIslandInteractionSeparation(out string metrics, out string reason)
        {
            reason = null;
            metrics = string.Empty;
            SkyIslandSession session = SkyIslandSessionOrNull();
            GameObject root = session == null ? null : session.ValidationWorldRoot;
            if (root == null) { reason = "world_root_missing"; return false; }

            List<InteractableBase> all = new List<InteractableBase>();
            foreach (InteractableBase candidate in root.GetComponentsInChildren<InteractableBase>(true))
                if (candidate != null && candidate.isActiveAndEnabled) all.Add(candidate);

            List<string> overlaps = new List<string>();
            float closest = float.MaxValue;
            string closestPair = "none";
            for (int i = 0; i < all.Count; i++)
            {
                float extentA;
                Vector3 centerA;
                if (!TryDescribeInteractable(all[i], out centerA, out extentA)) continue;
                for (int j = i + 1; j < all.Count; j++)
                {
                    float extentB;
                    Vector3 centerB;
                    if (!TryDescribeInteractable(all[j], out centerB, out extentB)) continue;
                    if (SharesInteractionGroup(all[i].transform, all[j].transform)) continue;
                    Vector3 delta = centerA - centerB;
                    delta.y = 0f; // 俯视游戏的交互选择由水平距离决定
                    float distance = delta.magnitude;
                    float margin = distance - (extentA + extentB);
                    if (margin < closest)
                    {
                        closest = margin;
                        closestPair = all[i].name + "|" + all[j].name + "@" + distance.ToString("F2");
                    }
                    if (margin < 0f)
                        overlaps.Add(all[i].name + "|" + all[j].name + "=" + distance.ToString("F2")
                            + "<" + (extentA + extentB).ToString("F2"));
                }
            }

            metrics = "interactables=" + all.Count + ",overlapping_pairs=" + overlaps.Count
                + ",closest_margin_m=" + (closest == float.MaxValue ? "n/a" : closest.ToString("F2"))
                + ",closest_pair=" + closestPair;
            if (overlaps.Count > 0)
            {
                reason = "跨组交互体触发体积重叠（官方按严格小于取唯一目标，重叠区里必有一个按不到）："
                    + string.Join(";", overlaps.ToArray());
                return false;
            }
            return true;
        }

        /// <summary>取交互体的触发体中心与水平半径。没有 collider 的（例如纯表现层）返回 false。</summary>
        private static bool TryDescribeInteractable(InteractableBase interactable, out Vector3 center, out float extent)
        {
            center = Vector3.zero;
            extent = 0f;
            if (interactable == null) return false;
            Collider[] colliders = interactable.GetComponentsInChildren<Collider>(false);
            bool any = false;
            Bounds bounds = default(Bounds);
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider collider = colliders[i];
                if (collider == null || !collider.enabled) continue;
                if (!any) { bounds = collider.bounds; any = true; }
                else bounds.Encapsulate(collider.bounds);
            }
            if (!any) return false;
            center = bounds.center;
            extent = Mathf.Max(bounds.extents.x, bounds.extents.z);
            return true;
        }

        /// <summary>
        /// 两个交互体是否属于同一交互组（滚轮可切，不构成竞争）。
        ///
        /// 本仓库的子交互体一律建在 owner 的 transform 之下，居民的剧情选项与官方关系交互体
        /// 则同为 NPC transform 的子物体，所以「后代关系」或「共用最近的交互体/角色祖先」
        /// 就能完整覆盖，不需要反射官方的 `otherInterablesInGroup` 字段——那个读取路径会顺手
        /// 建列表，验收不该有副作用。
        /// </summary>
        private static bool SharesInteractionGroup(Transform a, Transform b)
        {
            if (a == null || b == null) return false;
            if (a.IsChildOf(b) || b.IsChildOf(a)) return true;
            Transform ownerA = NearestInteractionOwner(a);
            Transform ownerB = NearestInteractionOwner(b);
            return ownerA != null && ownerA == ownerB;
        }

        private static Transform NearestInteractionOwner(Transform node)
        {
            for (Transform current = node.parent; current != null; current = current.parent)
            {
                if (current.GetComponent<InteractableBase>() != null) return current;
                if (current.GetComponent<CharacterMainControl>() != null) return current;
            }
            return null;
        }

        /// <summary>
        /// 面板插图有没有真的部署到位。
        ///
        /// 判据是「**要么全有、要么全无**」，不是「必须有」：`SkyIslandUiArt` 是 fail-open 的，
        /// 完全没出美术时面板退成无插图布局，那是合法状态，报红等于逼着美术未就绪就不能上线。
        /// 真正说明部署坏了的是**部分命中**——`Assets/ui/SkyIsland` 只拷进去一半，
        /// 于是有的区域有图、有的没有，玩家看到的是「时有时无」，而这在日志里一声不吭。
        ///
        /// 副作用只有缓存预热：这里会把 18 张图读进 `SkyIslandUiArt` 的静态缓存（满载约 20 MB），
        /// 与玩家逛遍全岛后的常驻量一致，不改变任何玩法状态。
        /// </summary>
        private bool ValidateSkyIslandPanelArt(out string metrics, out string reason)
        {
            reason = null;
            string[] regions = { "A", "B", "C", "D", "E", "F", "G", "H", "S1", "S2", "S3", "S4" };
            string[] residents = SkyIslandResidents.AllIds;
            List<string> missing = new List<string>();
            int scenes = 0, portraits = 0;
            for (int i = 0; i < regions.Length; i++)
            {
                if (SkyIslandUiArt.GetScene("POI_" + regions[i]) != null) scenes++;
                else missing.Add("scene:" + regions[i]);
            }
            for (int i = 0; i < residents.Length; i++)
            {
                if (SkyIslandUiArt.GetPortrait(residents[i]) != null) portraits++;
                else missing.Add("portrait:" + residents[i]);
            }
            int found = scenes + portraits;
            int total = regions.Length + residents.Length;
            metrics = "scenes=" + scenes + "/" + regions.Length
                + ",portraits=" + portraits + "/" + residents.Length
                + ",missing=" + (missing.Count == 0 ? "none" : string.Join(",", missing.ToArray()));
            if (found != 0 && found != total)
            {
                reason = "面板插图只部署了一部分，玩家会看到有的区域有图有的没有：" +
                    string.Join(",", missing.ToArray());
                return false;
            }
            return true;
        }

        private bool ValidateSkyIslandLootBands(out string metrics, out string reason)
        {
            reason = null;
            SkyIslandLootTier[] tiers =
            {
                SkyIslandLootTier.Supply, SkyIslandLootTier.Voyage, SkyIslandLootTier.Starworks
            };
            List<string> parts = new List<string>();
            List<string> errors = new List<string>();
            int previousMin = 0, previousMax = 0, previousCount = 0;
            for (int i = 0; i < tiers.Length; i++)
            {
                SkyIslandLootTier tier = tiers[i];
                int min = SkyIslandLootTables.MinQuality(tier);
                int max = SkyIslandLootTables.MaxQuality(tier);
                int minCount = SkyIslandLootTables.MinCount(tier);
                int maxCount = SkyIslandLootTables.MaxCount(tier);
                int[] pool = SkyIslandLootPools.Get(tier);
                int[] band = SkyIslandLootPools.GetGuaranteeBand(tier);
                parts.Add(tier + "=q" + min + "-" + max + ",n" + minCount + "-" + maxCount
                    + ",pool=" + (pool == null ? 0 : pool.Length) + ",guar=" + (band == null ? 0 : band.Length));
                if (min > max || minCount > maxCount) errors.Add(tier + ":band_inverted");
                // 单调不减：深处必须更值钱、件数不许倒挂（旧表里 Voyage 2-4 比 Starworks 2-3 还多）。
                if (i > 0 && (min < previousMin || max < previousMax || maxCount < previousCount))
                    errors.Add(tier + ":not_monotonic");
                if (pool == null || pool.Length == 0) errors.Add(tier + ":pool_empty");
                previousMin = min; previousMax = max; previousCount = maxCount;
            }
            // 顶档必须够得到官方第 8 档，否则最深处的箱子永远刷不出顶级物品。
            if (SkyIslandLootTables.MaxQuality(SkyIslandLootTier.Starworks) != 8) errors.Add("starworks_max_quality!=8");
            metrics = string.Join(" | ", parts.ToArray());
            if (errors.Count > 0) reason = "品质带/件数/池子不合格：" + string.Join(",", errors.ToArray());
            return errors.Count == 0;
        }

        // ====================================================================
        // 3/5 门控与导航
        // ====================================================================

        private bool ValidateSkyIslandGateState(out string metrics, out string reason)
        {
            reason = null;
            metrics = string.Empty;
            SkyIslandSession session = SkyIslandSessionOrNull();
            GameObject root = session == null ? null : session.ValidationWorldRoot;
            if (root == null) { reason = "world_root_missing"; return false; }
            SkyIslandStoryService story = session.ValidationStory;
            if (story == null) { reason = "story_service_missing"; return false; }
            // 用内置表而不是会话手里那份：`SkyIslandContent.TryParse` 已经逐条比对过
            // `known.Required == required && known.Any == any`，两者的门条件**必然逐位相同**，
            // 而 `SKY_CONTENT_TABLE` 另有一条断言钉住「本局用的是 Json 且条目数一致」。
            SkyIslandContentData content = SkyIslandContent.CreateFallback();
            SkyIslandStoryData data = story.Current;
            List<string> parts = new List<string>();
            List<string> errors = new List<string>();
            for (int i = 0; i < content.Gates.Length; i++)
            {
                string id = content.Gates[i].Id;
                Transform gate = root.transform.Find("StoryGate_" + id);
                bool shouldOpen = content.IsGateOpen(id, data);
                bool blocking = gate != null && gate.gameObject.activeSelf;
                parts.Add(id + "=" + (shouldOpen ? "open" : "closed") + "/" + (blocking ? "blocking" : "clear"));
                if (gate == null) { errors.Add(id + ":missing"); continue; }
                // 门物体激活 == 挡路。开着的门还挡路、关着的门却放行，都是玩家立刻能撞上的缺陷。
                if (blocking == shouldOpen) errors.Add(id + ":state_mismatch");
            }
            metrics = "flags=" + data.flags + " | " + string.Join(" ", parts.ToArray());
            if (errors.Count > 0) reason = "剧情门实体状态与内容表判定不一致：" + string.Join(",", errors.ToArray());
            return errors.Count == 0;
        }

        private bool ValidateSkyIslandObjective(out string metrics, out string reason)
        {
            reason = null;
            metrics = string.Empty;
            SkyIslandSession session = SkyIslandSessionOrNull();
            SkyIslandStoryService story = session == null ? null : session.ValidationStory;
            if (story == null) { reason = "story_service_missing"; return false; }
            string objective = story.CurrentObjective;
            string expected = SkyIslandStoryRules.Objective(story.Current);
            metrics = "objective=" + objective;
            bool ok = !string.IsNullOrEmpty(objective)
                && string.Equals(objective, expected, StringComparison.Ordinal);
            if (!ok) reason = "HUD 目标行与剧情规则算出的目标不一致，或为空";
            return ok;
        }

        // ====================================================================
        // 4/5 存档、服务与居民
        // ====================================================================

        private bool ValidateSkyIslandStoryCodec(out string metrics, out string reason)
        {
            reason = null;
            metrics = string.Empty;
            SkyIslandSession session = SkyIslandSessionOrNull();
            SkyIslandStoryService story = session == null ? null : session.ValidationStory;
            if (story == null) { reason = "story_service_missing"; return false; }
            SkyIslandStoryData current = story.Current.Copy();
            string encoded = SkyIslandStoryCodec.Encode(current);
            SkyIslandStoryData decoded = SkyIslandStoryCodec.Decode(encoded);
            bool known = (current.flags & ~SkyIslandStoryRules.KnownFlags) == 0;
            bool roundTrip = decoded != null && decoded.flags == current.flags
                && decoded.visitedRegions == current.visitedRegions
                && decoded.clearedEncounters.Length == current.clearedEncounters.Length
                && decoded.discoveredNotes.Length == current.discoveredNotes.Length;
            metrics = "flags=" + current.flags + ",known_mask=" + SkyIslandStoryRules.KnownFlags
                + ",regions=" + current.visitedRegions + ",cleared=" + current.clearedEncounters.Length
                + ",notes=" + current.discoveredNotes.Length + ",json_bytes=" + (encoded == null ? 0 : encoded.Length);
            bool ok = known && roundTrip;
            if (!ok) reason = known
                ? "当前快照过一次 Encode/Decode 就变了形（Codec 会拒绝整份存档）"
                : "剧情位超出 KnownFlags 掩码：新增 flag 没有同步掩码，Codec 会拒绝整份存档";
            return ok;
        }

        private bool ValidateSkyIslandSaveState(out string metrics, out string reason)
        {
            reason = null;
            metrics = string.Empty;
            SkyIslandSession session = SkyIslandSessionOrNull();
            SkyIslandStoryService story = session == null ? null : session.ValidationStory;
            if (story == null) { reason = "story_service_missing"; return false; }
            bool currentSlot = story.IsCurrentSlot;
            bool canWrite = story.CanWrite;
            metrics = "current_slot=" + currentSlot + ",can_write=" + canWrite
                + ",recovery_pending=" + SkyIslandStorySaveRecovery.IsPending()
                + ",status=" + story.SaveStatus;
            bool ok = currentSlot && canWrite;
            if (!ok) reason = "剧情存档不可写：槽位已变、有写屏障或 store 处于单向故障（进度提交会被拒）";
            return ok;
        }

        private bool ValidateSkyIslandServicePricing(out string metrics, out string reason)
        {
            reason = null;
            List<string> errors = new List<string>();
            // 苔药：按缺失血量比例计价，擦破皮便宜、濒死昂贵，且不低于服务费下限。
            int scratch = SkyIslandServices.HealPriceFor(99f, 100f);
            int half = SkyIslandServices.HealPriceFor(50f, 100f);
            int dying = SkyIslandServices.HealPriceFor(1f, 100f);
            if (!(scratch <= half && half <= dying)) errors.Add("heal_not_monotonic");
            if (scratch < SkyIslandServices.HealPriceMinimum) errors.Add("heal_below_minimum");
            if (dying > SkyIslandServices.HealPriceFull) errors.Add("heal_above_full");
            // 血量上限量级变化不该改变报价形状：比例计价对 MaxHealth 无关。
            if (SkyIslandServices.HealPriceFor(500f, 1000f) != half) errors.Add("heal_scale_dependent");

            // 整备：按官方口径「价值 × 修复比例 × 0.5」，且必然产生永久磨损。
            float lostFull, lostNone, lostHalf;
            int fullRepair = SkyIslandServices.RepairPriceFor(1000, 100f, 0f, 0f, 0.1f, out lostFull);
            int noRepair = SkyIslandServices.RepairPriceFor(1000, 100f, 100f, 0f, 0.1f, out lostNone);
            int halfRepair = SkyIslandServices.RepairPriceFor(1000, 100f, 50f, 0f, 0.1f, out lostHalf);
            if (noRepair != 0 || lostNone != 0f) errors.Add("repair_charges_intact_item");
            if (!(halfRepair > 0 && halfRepair < fullRepair)) errors.Add("repair_not_monotonic");
            if (!(lostFull > 0f && lostHalf > 0f && lostHalf < lostFull)) errors.Add("repair_no_permanent_loss");
            // 价值为 0 的受损物品仍要能进队列（官方 RepairItems 会修列表里每一件，报价 0 也照修）。
            float lostZero;
            if (SkyIslandServices.RepairPriceFor(0, 100f, 50f, 0f, 0.1f, out lostZero) != 0 || lostZero <= 0f)
                errors.Add("repair_zero_value_item");

            metrics = "heal(99/100)=" + scratch + ",heal(50/100)=" + half + ",heal(1/100)=" + dying
                + ",heal_min=" + SkyIslandServices.HealPriceMinimum + ",heal_full=" + SkyIslandServices.HealPriceFull
                + ",cooldown_s=" + SkyIslandServices.HealCooldown
                + ",repair_full=" + fullRepair + ",repair_half=" + halfRepair
                + ",repair_loss_full=" + lostFull.ToString("F4")
                + ",meal_hp=" + SkyIslandServices.MealMaxHealthBonus + ",meal_speed=" + SkyIslandServices.MealSpeedBonus;
            if (errors.Count > 0) reason = "服务定价纯函数不合格：" + string.Join(",", errors.ToArray());
            return errors.Count == 0;
        }

        private bool ValidateSkyIslandBountyGating(out string metrics, out string reason)
        {
            reason = null;
            metrics = string.Empty;
            SkyIslandSession session = SkyIslandSessionOrNull();
            if (session == null) { reason = "session_missing"; return false; }
            SkyIslandBounty bounty = session.Bounty;
            if (bounty == null) { reason = "bounty_owner_missing"; return false; }
            SkyIslandBountyKind[] kinds = SkyIslandBounty.AllKinds;
            List<string> parts = new List<string>();
            List<string> errors = new List<string>();
            for (int i = 0; i < kinds.Length; i++)
            {
                int target = bounty.TargetFor(kinds[i]);
                int available = session.AvailableBountyProgress(kinds[i]);
                parts.Add(kinds[i] + "=avail " + available + "/target " + target);
                if (target <= 0) errors.Add(kinds[i] + ":target_not_positive");
            }
            // 派单门控的全部意义就是「只派做得完的单」：接了单却没有可完成量就是死单。
            if (bounty.HasActive && session.AvailableBountyProgress(bounty.Active) + bounty.Progress < bounty.Target)
                errors.Add("active_contract_unfinishable");
            if (bounty.CompletedRounds > SkyIslandBounty.MaxRounds) errors.Add("rounds_over_max");
            metrics = "rounds=" + bounty.CompletedRounds + "/" + SkyIslandBounty.MaxRounds
                + ",active=" + bounty.Active + ",progress=" + bounty.Progress + "/" + bounty.Target
                + " | " + string.Join(" ", parts.ToArray());
            if (errors.Count > 0) reason = "委托门控不合格：" + string.Join(",", errors.ToArray());
            return errors.Count == 0;
        }

        private bool ValidateSkyIslandResidents(out string metrics, out string reason)
        {
            reason = null;
            metrics = string.Empty;
            SkyIslandSession session = SkyIslandSessionOrNull();
            GameObject root = session == null ? null : session.ValidationWorldRoot;
            SkyIslandResidents residents = session == null ? null : session.ValidationResidents;
            if (root == null || residents == null) { reason = "world_root_or_residents_owner_missing"; return false; }

            // 岛上实际挂着的剧情交互体总数。少于在岛居民数就说明有人生成了却按不了「聊聊航路」。
            int talkers = 0;
            foreach (SkyIslandResidentInteractable talk in
                UnityEngine.Object.FindObjectsOfType<SkyIslandResidentInteractable>())
                if (talk != null && talk.transform.IsChildOf(root.transform)) talkers++;

            string[] ids = SkyIslandResidents.AllIds;
            List<string> parts = new List<string>();
            List<string> errors = new List<string>();
            int present = 0, married = 0;
            for (int i = 0; i < ids.Length; i++)
            {
                bool isMarried = false;
                try { isMarried = AffinityManager.IsMarriedToPlayer(ids[i]); }
                catch (Exception) { /* 关系系统不可用时按未婚处理，下面的 spawned 判据会兜住 */ }
                bool spawned = residents.IsSpawned(ids[i]);
                if (isMarried) married++;
                if (spawned) present++;
                parts.Add(ids[i] + "=" + (spawned ? "on_island" : (isMarried ? "married_off_island" : "absent")));
                // 婚后离岛是既定行为（婚姻系统接管，`SpawnOneAsync` 直接跳过生成）；
                // 既没结婚又不在岛上才是缺陷——那位居民的服务与委托入口这一趟就失联了。
                if (!spawned && !isMarried) errors.Add(ids[i] + ":missing");
            }
            if (talkers < present) errors.Add("talk_interactables=" + talkers + "<on_island=" + present);
            metrics = "ids=" + ids.Length + ",on_island=" + present + ",married=" + married
                + ",owned=" + residents.SpawnedCount + ",talk_interactables=" + talkers
                + " | " + string.Join(" ", parts.ToArray());
            if (errors.Count > 0)
                reason = "居民装配不合格（既没结婚离岛也没在岛上生成，或缺少剧情交互体）："
                    + string.Join(",", errors.ToArray());
            return errors.Count == 0;
        }

        // ====================================================================
        // 5/5 撤离、表现与本地化
        // ====================================================================

        private bool ValidateSkyIslandExtractionRings(out string metrics, out string reason)
        {
            reason = null;
            metrics = string.Empty;
            SkyIslandSession session = SkyIslandSessionOrNull();
            GameObject root = session == null ? null : session.ValidationWorldRoot;
            if (root == null) { reason = "world_root_missing"; return false; }
            bool bellUnlocked = session.ValidationBellMarker != null && session.ValidationSnapshot().BellUnlocked;

            LineRenderer dock = FindExtractionRing(root, "Exit");
            LineRenderer bell = FindExtractionRing(root, "BellExtraction");
            float dockRadius = MeasureRingRadius(dock);
            float bellRadius = MeasureRingRadius(bell);
            float expected = SkyIslandSession.ValidationExtractionRadius;
            bool dockOk = dock != null && dock.gameObject.activeInHierarchy
                && Mathf.Abs(dockRadius - expected) < 0.01f
                && dock.GetComponent<Collider>() == null;
            // 钟庭环与 `BellExitIfUnlocked()` 必须同一事实源：地上看得到的圈就是站进去能走的圈。
            bool bellVisible = bell != null && bell.gameObject.activeInHierarchy;
            bool bellOk = bell == null
                ? !bellUnlocked
                : (bellVisible == bellUnlocked && (!bellVisible || Mathf.Abs(bellRadius - expected) < 0.01f)
                    && bell.GetComponent<Collider>() == null);
            metrics = "expected_radius=" + expected + ",dock_radius=" + dockRadius.ToString("F2")
                + ",dock_active=" + (dock != null && dock.gameObject.activeInHierarchy)
                + ",bell_unlocked=" + bellUnlocked + ",bell_visible=" + bellVisible
                + ",bell_radius=" + bellRadius.ToString("F2");
            if (!dockOk) reason = "码头蓝环缺失、半径与判定不一致或带了碰撞体";
            else if (!bellOk) reason = "钟庭绿环的显隐与敲钟结局不同源，或半径与判定不一致";
            return dockOk && bellOk;
        }

        private static LineRenderer FindExtractionRing(GameObject root, string markerName)
        {
            foreach (LineRenderer line in root.GetComponentsInChildren<LineRenderer>(true))
                if (line != null && line.gameObject.name == "SkyIslandExtractionRing_" + markerName) return line;
            return null;
        }

        /// <summary>按环上顶点到中心的距离反算实际画出来的半径。画错了这里立刻不等。</summary>
        private static float MeasureRingRadius(LineRenderer line)
        {
            if (line == null || line.positionCount <= 0) return -1f;
            float total = 0f;
            for (int i = 0; i < line.positionCount; i++) total += line.GetPosition(i).magnitude;
            return total / line.positionCount;
        }

        /// <summary>
        /// 撤离判定的三点取样：圆心内、半径 90% 内、半径外 0.5 m 外。
        /// 走 <see cref="SkyIslandSession.ValidationIsInsideExtractionAt"/>，只读几何，不搬玩家。
        /// 手感（站进去到底触没触发、倒计时冻不冻结）仍然只能实机看，见人工清单。
        /// </summary>
        private bool ValidateSkyIslandExtractionRule(out string metrics, out string reason)
        {
            reason = null;
            metrics = string.Empty;
            SkyIslandSession session = SkyIslandSessionOrNull();
            if (session == null) { reason = "session_missing"; return false; }
            Transform exit = session.ValidationExitMarker;
            if (exit == null) { reason = "exit_marker_missing"; return false; }
            float radius = SkyIslandSession.ValidationExtractionRadius;
            string marker;
            bool center = session.ValidationIsInsideExtractionAt(exit.position, out marker);
            bool inside = session.ValidationIsInsideExtractionAt(
                exit.position + new Vector3(radius * 0.9f, 0f, 0f), out marker);
            bool outside = session.ValidationIsInsideExtractionAt(
                exit.position + new Vector3(radius + 0.5f, 0f, 0f), out marker);

            SkyIslandValidationSnapshot snapshot = session.ValidationSnapshot();
            Transform bell = session.ValidationBellMarker;
            bool bellInside = false;
            if (bell != null) bellInside = session.ValidationIsInsideExtractionAt(bell.position, out marker);

            metrics = "radius=" + radius + ",hold_s=" + SkyIslandSession.ValidationExtractionHold
                + ",center=" + center + ",inner_90=" + inside + ",outer_+0.5=" + outside
                + ",bell_unlocked=" + snapshot.BellUnlocked + ",bell_center=" + bellInside;
            bool dockOk = center && inside && !outside;
            // 钟庭必须与结局 flag 同步：未敲钟就能在钟庭撤离＝跳过终章。
            bool bellOk = bell == null || bellInside == snapshot.BellUnlocked;
            if (!dockOk) reason = "撤离判定与半径不符（圆心/内侧应判定为内，外侧应判定为外）";
            else if (!bellOk) reason = "归航钟庭撤离点的开放状态与敲钟结局不一致";
            return dockOk && bellOk;
        }

        /// <summary>
        /// 撤离读条复用官方 EvacuationCountdownUI（2026-09-10 指引重做）。
        ///
        /// 这一条只有岛上能证：官方控件是不是真的挂在这张独立关卡里、CountDownArea 的私有字段名
        /// 在当前游戏版本里是否还对得上，离线都证明不了。不可用时撤离本身照常（会话退回 HUD 卡片里的
        /// 文字读秒），但观感回到了自绘文字，所以按 FAIL 报出来，而不是悄悄降级。
        /// 只读：不 Request、不写任何字段。
        /// </summary>
        private bool ValidateSkyIslandOfficialCountdown(out string metrics, out string reason)
        {
            reason = null;
            SkyIslandSession session = SkyIslandSessionOrNull();
            bool instance = EvacuationCountdownUI.Instance != null;
            bool bridge = session != null && session.ValidationOfficialCountdownAvailable;
            metrics = "official_ui_instance=" + instance + ",bridge_available=" + bridge;
            if (session == null) { reason = "session_missing"; return false; }
            if (!instance) reason = "本关卡里没有官方 EvacuationCountdownUI 实例：撤离读秒已退回 HUD 文字";
            else if (!bridge) reason = "官方撤离读条桥不可用（CountDownArea 私有字段对不上或接入失败）：撤离读秒已退回 HUD 文字";
            return instance && bridge;
        }

        private bool ValidateSkyIslandStormTuning(out string metrics, out string reason)
        {
            reason = null;
            List<string> errors = new List<string>();
            float[] thresholds = SkyIslandStormBoss.PhaseThresholds;
            if (thresholds.Length != 4) errors.Add("phase_count!=4");
            for (int i = 1; i < thresholds.Length; i++)
                if (thresholds[i] >= thresholds[i - 1]) errors.Add("thresholds_not_descending");
            // 相位提速必须单调不减、且封顶——旧写法每档 /= 1.25 会复利到 2.44 倍，末段没有反应窗口。
            float previous = 1f;
            for (int phase = 0; phase <= thresholds.Length + 2; phase++)
            {
                float speedup = SkyIslandStormBoss.PhaseSpeedup(phase);
                if (speedup < previous) errors.Add("speedup_not_monotonic@" + phase);
                if (speedup > SkyIslandStormBoss.MaxPhaseSpeedup + 0.0001f) errors.Add("speedup_over_cap@" + phase);
                previous = speedup;
            }
            if (Mathf.Abs(SkyIslandStormBoss.PhaseSpeedup(thresholds.Length) - SkyIslandStormBoss.MaxPhaseSpeedup) > 0.0001f)
                errors.Add("末档提速不等于封顶值");
            for (int phase = 0; phase < thresholds.Length; phase++)
                if (SkyIslandStormBoss.PhaseForFraction(thresholds[phase]) != phase + 1)
                    errors.Add("phase_for_fraction@" + phase);
            // 「跑不跑得出第一圈」= 半径 ÷ 预警。官方爆炸没有距离衰减，圈内一律吃满伤，
            // 所以这个比值就是这场战斗可不可打的**数值**判据；真跑得掉与否必须实机（见人工清单）。
            float escapeSpeed = SkyIslandStormBoss.PulseRadius / SkyIslandStormBoss.PulseTelegraph;
            if (escapeSpeed > 5.5f) errors.Add("escape_speed_too_high");
            metrics = "phases=" + thresholds.Length + ",cap=" + SkyIslandStormBoss.MaxPhaseSpeedup
                + ",speedup_last=" + SkyIslandStormBoss.PhaseSpeedup(thresholds.Length).ToString("F3")
                + ",pulse_radius=" + SkyIslandStormBoss.PulseRadius + ",telegraph_s=" + SkyIslandStormBoss.PulseTelegraph
                + ",required_escape_speed=" + escapeSpeed.ToString("F2") + "m/s"
                + ",waves=" + SkyIslandStormBoss.PulseWaves + ",damage=" + SkyIslandStormBoss.PulseDamage;
            if (errors.Count > 0) reason = "噬风相位编排不合格：" + string.Join(",", errors.ToArray());
            return errors.Count == 0;
        }

        /// <summary>
        /// `M_SKY_ISLAND_09` 的自动化部分：英文语境下不许出现中文。
        ///
        /// `SkyIslandLocalizationGuard` 是**源码**守卫（钉 `L10n.T` 成对出现），它证明不了
        /// 运行时真的返回英文——英文实参本身写了中文、或某处绕过 `L10n.T` 直接取 `TierNameCn`，
        /// 源码守卫都是绿的。这里在运行时把玩家可见文案真取一遍，再扫 CJK 码位。
        ///
        /// 同时扫**场景里已经渲染出来的**世界空间文字（桥口木牌、物资牌、纪念物标签），
        /// 那是纯静态断言够不到的一层。
        ///
        /// 中文语境下记 SKIP 而不是 PASS：这条用例在中文下没有判据，假绿比不跑更糟。
        /// </summary>
        private bool ValidateSkyIslandEnglishText(out string metrics, out string reason)
        {
            reason = null;
            metrics = string.Empty;
            // 中文语境下这条用例没有判据：扫不出中文残留是恒真，记 PASS 就是假绿。
            if (L10n.IsChinese) throw new SkyIslandSkipCase("language_not_english", "language=zh");
            SkyIslandSession session = SkyIslandSessionOrNull();
            List<string> offenders = new List<string>();
            int checkedStrings = 0;

            string[] pointKeys =
            {
                "Search_A", "Search_B", "Search_C", "Search_D", "Search_E", "Search_F", "Search_G", "Search_H",
                "Search_S1", "Search_S2", "Search_S3", "Search_S4", "Search_A_02"
            };
            for (int i = 0; i < pointKeys.Length; i++)
                Inspect("PointName:" + pointKeys[i], SkyIslandWorldStory.PointName(pointKeys[i]), offenders, ref checkedStrings);

            string[] residentIds = SkyIslandResidents.AllIds;
            for (int i = 0; i < residentIds.Length; i++)
                Inspect("ResidentName:" + residentIds[i], SkyIslandWorldStory.ResidentName(residentIds[i]),
                    offenders, ref checkedStrings);

            string[] landmarkNames =
            {
                "POI_A", "POI_B", "POI_C", "POI_D", "POI_E", "POI_F", "POI_G", "POI_H",
                "POI_S1", "POI_S2", "POI_S3", "POI_S4"
            };
            for (int i = 0; i < landmarkNames.Length; i++)
                Inspect("Landmark:" + landmarkNames[i], SkyIslandSession.LandmarkLabel(landmarkNames[i]),
                    offenders, ref checkedStrings);

            SkyIslandLootTier[] tiers =
            {
                SkyIslandLootTier.Supply, SkyIslandLootTier.Voyage, SkyIslandLootTier.Starworks
            };
            for (int i = 0; i < tiers.Length; i++)
                Inspect("Tier:" + tiers[i], SkyIslandLootTables.TierNameEn(tiers[i]), offenders, ref checkedStrings);

            SkyIslandBountyKind[] kinds = SkyIslandBounty.AllKinds;
            for (int i = 0; i < kinds.Length; i++)
                Inspect("Bounty:" + kinds[i], SkyIslandBounty.Name(kinds[i]), offenders, ref checkedStrings);

            // 剧情目标行与全部动作回执：`TryApply` 只构造候选状态，不写存档，纯读。
            SkyIslandStoryData probe = SkyIslandStoryRules.CreateDefault();
            Inspect("Objective:default", SkyIslandStoryRules.Objective(probe), offenders, ref checkedStrings);
            foreach (SkyIslandStoryAction action in Enum.GetValues(typeof(SkyIslandStoryAction)))
            {
                SkyIslandStoryData candidate;
                string message;
                SkyIslandStoryRules.TryApply(probe, action, out candidate, out message);
                Inspect("Action:" + action, message, offenders, ref checkedStrings);
            }
            if (session != null && session.ValidationStory != null)
            {
                for (int i = 0; i < residentIds.Length; i++)
                    Inspect("Npc:" + residentIds[i], session.ValidationStory.DescribeNpc(residentIds[i]),
                        offenders, ref checkedStrings);
                Inspect("SaveStatus", session.ValidationStory.SaveStatus, offenders, ref checkedStrings);
                Inspect("Summary", session.ValidationStory.Summary, offenders, ref checkedStrings);
            }

            // 场景里已经渲染出来的世界空间文字。
            GameObject root = session == null ? null : session.ValidationWorldRoot;
            if (root != null)
            {
                foreach (TextMeshPro text in root.GetComponentsInChildren<TextMeshPro>(true))
                    if (text != null) Inspect("World:" + text.transform.parent, text.text, offenders, ref checkedStrings);
            }

            metrics = "language=en,checked=" + checkedStrings + ",offenders=" + offenders.Count;
            if (offenders.Count > 0)
            {
                reason = "英文语境下仍出现中文：" + string.Join(";", offenders.ToArray());
                return false;
            }
            return true;
        }

        /// <summary>
        /// CJK 统一表意文字（含扩展 A）与 CJK 标点、全角形式。
        ///
        /// 码位一律写 `\u` 转义**而不是**字面汉字：这个文件将来若被某个工具按非 UTF-8 读写，
        /// 字面量会静默变形成别的字符，而这条断言恰恰是靠码位区间成立的，
        /// 变形之后它会安静地永远为真——那就是最糟的假绿。
        /// 诊断类文本（部署损坏、契约变更时才出现）按既定口径保留中文，不在这里取样。
        /// </summary>
        private static void Inspect(string label, string value, List<string> offenders, ref int counter)
        {
            if (string.IsNullOrEmpty(value)) return;
            counter++;
            for (int i = 0; i < value.Length; i++)
            {
                int code = value[i];
                if ((code >= CjkIdeographStart && code <= CjkIdeographEnd)
                    || (code >= CjkExtensionAStart && code <= CjkExtensionAEnd)
                    || (code >= CjkPunctuationStart && code <= CjkPunctuationEnd)
                    || (code >= FullWidthFormsStart && code <= FullWidthFormsEnd))
                {
                    offenders.Add(label);
                    return;
                }
            }
        }

        // 码位区间写成整数常量，不写字面汉字、也不写 `\u` 转义：
        // 前者会在文件被按非 UTF-8 读写时静默变形，后者在跨工具复制时被折过反斜杠。
        // 这条断言完全靠区间成立，一旦变形就会安静地永远为真——最糟的假绿。
        private const int CjkIdeographStart = 0x4E00, CjkIdeographEnd = 0x9FFF;
        private const int CjkExtensionAStart = 0x3400, CjkExtensionAEnd = 0x4DBF;
        private const int CjkPunctuationStart = 0x3000, CjkPunctuationEnd = 0x303F;
        private const int FullWidthFormsStart = 0xFF00, FullWidthFormsEnd = 0xFFEF;

        /// <summary>
        /// 往返对象计数基线。`M_SKY_ISLAND_01` 要的「重复进入无残留」需要**两次**出击的同一行做差，
        /// 而此前没有任何地方打印这些数。这里只采不判：单次数值本身说明不了泄漏。
        /// </summary>
        private bool ValidateSkyIslandSceneBaseline(out string metrics, out string reason)
        {
            reason = null;
            metrics = string.Empty;
            SkyIslandSession session = SkyIslandSessionOrNull();
            GameObject root = session == null ? null : session.ValidationWorldRoot;
            if (root == null) { reason = "world_root_missing"; return false; }

            int renderers = root.GetComponentsInChildren<Renderer>(true).Length;
            int lights = root.GetComponentsInChildren<Light>(true).Length;
            int audio = root.GetComponentsInChildren<AudioSource>(true).Length;
            int interactables = root.GetComponentsInChildren<InteractableBase>(true).Length;
            int texts = root.GetComponentsInChildren<TextMeshPro>(true).Length;
            int lines = root.GetComponentsInChildren<LineRenderer>(true).Length;
            HashSet<int> materials = new HashSet<int>();
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null) continue;
                Material[] shared = renderer.sharedMaterials;
                for (int i = 0; i < shared.Length; i++)
                    if (shared[i] != null) materials.Add(shared[i].GetInstanceID());
            }
            string hostiles;
            int hostileCount = _host == null ? -1 : _host.ValidationCountHostileCharacters(out hostiles);
            SkyIslandValidationSnapshot snapshot = session.ValidationSnapshot();

            metrics = "renderers=" + renderers + ",materials=" + materials.Count + ",lights=" + lights
                + ",audio_sources=" + audio + ",interactables=" + interactables + ",world_texts=" + texts
                + ",line_renderers=" + lines + ",hostiles=" + hostileCount
                + ",modal_leases=" + ZombieModeUIHelper.ModalInputLeaseCount
                + ",managed_memory=" + GC.GetTotalMemory(false)
                + " | " + snapshot.Describe();
            // 只有「一个渲染器都没有」这种明显装配失败才算红；其余是采样，不做阈值判断。
            // 本轮没有任何性能采样依据，不许拿单次计数下「无泄漏」结论。
            bool ok = renderers > 0 && interactables > 0;
            if (!ok) reason = "地形根下没有渲染器或交互体，场景装配不完整";
            return ok;
        }

        private bool ValidateSkyIslandSessionIntact(out string metrics, out string reason)
        {
            reason = null;
            metrics = string.Empty;
            SkyIslandSession session = SkyIslandSessionOrNull();
            if (session == null) { reason = "验收结束时天空岛会话已经不在了（套件不应结束这趟出击）"; return false; }
            SkyIslandValidationSnapshot snapshot = session.ValidationSnapshot();
            metrics = snapshot.Describe() + ",modal_leases=" + ZombieModeUIHelper.ModalInputLeaseCount
                + ",active_view=" + (View.ActiveView != null);
            bool ok = session.IsReady && !snapshot.Closed && !snapshot.Returning
                && ZombieModeUIHelper.ModalInputLeaseCount == 0;
            if (!ok) reason = "验收自己改变了会话状态或漏了模态输入租约（套件必须只读）";
            return ok;
        }
    }
}
