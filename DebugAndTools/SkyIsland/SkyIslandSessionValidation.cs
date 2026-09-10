// ============================================================================
// SkyIslandSessionValidation.cs - 天空岛会话给 F3 岛内验收的只读观测面
// ============================================================================
// 从 SkyIslandSession.cs 拆出来：会话主文件已逼近 1200 行的新文件硬预算（tests/LargeFileBudgetGuard.py），
// 而这一段本来就是独立的一块——只有 F3GameplayValidationSkyIsland 消费，玩家帧上从不调用。
// 拆分不改变任何成员的语义与可见性。
//
// 纪律：这一段**只读**。不得在这里推进剧情、移动玩家、生成敌人或写存档——
// 岛内验收跑在玩家的真实旅程上，任何副作用都会污染玩家正在做的这一趟。
// 需要「做点什么才能验」的用例一律进人工清单，不许在这里偷偷改状态。
// ============================================================================

using Pathfinding;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    internal sealed partial class SkyIslandSession
    {
        internal GameObject ValidationWorldRoot { get { return root; } }
        internal Scene ValidationScene { get { return entryScene; } }
        internal SkyIslandStoryService ValidationStory { get { return story; } }
        internal SkyIslandResidents ValidationResidents { get { return residents; } }
        internal Transform ValidationPlayerSpawn { get { return playerSpawn; } }
        internal Transform ValidationExitMarker { get { return exitMarker; } }
        internal Transform ValidationBellMarker { get { return bellExit; } }
        internal Transform ValidationWindMarker { get { return windExit; } }
        internal Transform ValidationStarMarker { get { return starExit; } }
        /// <summary>官方撤离读条桥是否可用（F3 只读）。不可用时撤离读秒退回 HUD 文字。</summary>
        internal bool ValidationOfficialCountdownAvailable
        { get { return extractionCountdown != null && extractionCountdown.Available; } }
        internal GraphMask ValidationNavigationMask
        { get { return navigation == null ? default(GraphMask) : navigation.Mask; } }
        internal static float ValidationExtractionRadius { get { return ExtractionRadius; } }
        internal static float ValidationExtractionHold { get { return ExtractionHold; } }
        internal static float ValidationStoryPanelQuietRadius { get { return StoryPanelQuietRadius; } }
        internal static float ValidationSaveQuietRadius { get { return SaveQuietRadius; } }
        /// <summary>
        /// 本局按地面碰撞体索引到的区域数（场景包健康时为 12）。缺切分时区域名与巡岛记账整体失效却不报错，
        /// 巡岛委托因可完成量为 0 永远不派——只有验收读得出来。
        /// </summary>
        internal int ValidationGroundRegionCount { get { return regionIds.Count; } }
        /// <summary>剧情面板此刻是否开着（F3 只读：收尾核对模态租约时扣掉玩家自己开着的那一个）。</summary>
        internal bool ValidationStoryPanelVisible { get { return worldStory != null && worldStory.Visible; } }
        /// <summary>HUD 卡片此刻登记的目标文本。F3 比对「显示给玩家的」，而不是再算一遍规则。</summary>
        internal string ValidationHudObjective { get { return hud == null ? null : hud.ObjectiveText; } }
        /// <summary>本局全部见闻点标记名（含 _02 点位）。F3 英文完整性用例遍历真实点位，不再手写一份清单。</summary>
        internal string[] ValidationSearchMarkerNames()
        {
            var names = new string[searchMarkers.Count];
            for (int i = 0; i < searchMarkers.Count; i++)
                names[i] = searchMarkers[i] == null ? string.Empty : searchMarkers[i].name;
            return names;
        }

        /// <summary>
        /// 只读几何探测：给定世界坐标**是否**落在某个撤离圈内。
        ///
        /// 与玩家判定共用同一个 `ExtractionMarkerAt`（不再是逐字复制的第二份），但不读玩家位置、不改任何状态，
        /// 因此可以在验收里对「圆心 / 半径内侧 / 半径外侧」三点取样，证明画出来的圈与判定一致
        /// （`M_SKY_ISLAND_08`），而不必真的把玩家搬过去。
        /// </summary>
        internal bool ValidationIsInsideExtractionAt(Vector3 position, out string markerName)
        {
            Transform marker = ExtractionMarkerAt(position);
            markerName = marker == null ? null : marker.name;
            return marker != null;
        }

        /// <summary>
        /// 会话与内容装配的一次性快照。字段全部来自已有 owner；唯一的重算是导航可走节点计数（遍历全图节点），
        /// 只在 F3 用例里调，不在玩家帧上跑。
        /// </summary>
        internal SkyIslandValidationSnapshot ValidationSnapshot()
        {
            var snapshot = new SkyIslandValidationSnapshot();
            snapshot.Ready = ready;
            snapshot.Closed = closed;
            snapshot.Returning = returning;
            snapshot.DeathPending = deathPending;
            snapshot.NavigationReady = navigationReady;
            snapshot.RaidSeed = raidSeed;
            snapshot.SearchPoints = searchCount;
            snapshot.Landmarks = landmarks.Count;
            snapshot.EnemyMarkers = enemyMarkers.Count;
            snapshot.WorldRootActive = root != null && root.activeInHierarchy;
            snapshot.NavigationNodes = navigation == null ? 0 : navigation.CountWalkableNodes();
            snapshot.ContentSource = content == null ? "None" : content.Source;
            snapshot.ContentEncounters = content == null || content.Encounters == null ? 0 : content.Encounters.Length;
            snapshot.ContentGates = content == null || content.Gates == null ? 0 : content.Gates.Length;
            snapshot.EncounterGroups = encounters == null ? 0 : encounters.GroupCount;
            snapshot.EncounterRemainingClearable = encounters == null ? 0 : encounters.RemainingClearable;
            snapshot.ScavengeAnchors = SkyIslandLootTables.Anchors.Length;
            snapshot.ScavengePlaced = scavenging == null ? 0 : scavenging.PlacedPoints;
            snapshot.ScavengeOpened = scavenging == null ? 0 : scavenging.OpenedPoints;
            snapshot.ScavengeAvailable = scavenging == null ? 0 : scavenging.AvailablePoints;
            snapshot.ScavengeFailed = scavenging == null ? 0 : scavenging.FailedPoints;
            snapshot.ScavengeBuilt = scavenging == null ? 0 : scavenging.BuiltPoints;
            snapshot.GroundRegions = regionIds.Count;
            snapshot.ResidentsSpawned = residents == null ? 0 : residents.SpawnedCount;
            snapshot.ExtractionRingsBuilt = extractionRings != null;
            snapshot.BellUnlocked = BellExitIfUnlocked() != null;
            snapshot.WindUnlocked = WindExitIfUnlocked() != null;
            snapshot.StarUnlocked = StarExitIfUnlocked() != null;
            snapshot.ServicesReady = services != null;
            snapshot.BountyRounds = bounty.CompletedRounds;
            snapshot.BountyActive = bounty.HasActive;
            if (story != null)
            {
                snapshot.StoryFlags = story.Current.flags;
                snapshot.VisitedRegions = story.Current.visitedRegions;
                snapshot.StoryCurrentSlot = story.IsCurrentSlot;
                snapshot.StoryCanWrite = story.CanWrite;
                snapshot.Objective = story.CurrentObjective;
                snapshot.SaveStatus = story.SaveStatus;
                snapshot.Summary = story.Summary;
            }
            return snapshot;
        }
    }

    /// <summary>
    /// 会话与内容装配的只读快照，由 <see cref="SkyIslandSession.ValidationSnapshot"/> 填充。
    ///
    /// 刻意是**纯数据**且不引用任何 Unity 对象：F3 验收把它整份写进报告的 metrics 行，
    /// 往返两次的差值就是 `M_SKY_ISLAND_01` 要的「重复进入无残留」基线。
    /// </summary>
    internal sealed class SkyIslandValidationSnapshot
    {
        internal bool Ready, Closed, Returning, DeathPending, NavigationReady, WorldRootActive;
        internal bool StoryCurrentSlot, StoryCanWrite, BellUnlocked, ExtractionRingsBuilt, ServicesReady, BountyActive;
        internal bool WindUnlocked, StarUnlocked;
        internal int RaidSeed, SearchPoints, Landmarks, EnemyMarkers, NavigationNodes;
        internal int ContentEncounters, ContentGates, EncounterGroups, EncounterRemainingClearable;
        internal int ScavengeAnchors, ScavengePlaced, ScavengeOpened, ScavengeAvailable, ScavengeFailed, ScavengeBuilt;
        internal int GroundRegions;
        internal int ResidentsSpawned, StoryFlags, VisitedRegions, BountyRounds;
        internal string ContentSource, Objective, SaveStatus, Summary;

        /// <summary>报告用的一行式描述。字段顺序冻结，方便两次出击的行做逐字对照。</summary>
        internal string Describe()
        {
            return "ready=" + Ready + ",world_active=" + WorldRootActive + ",nav_nodes=" + NavigationNodes
                + ",searches=" + SearchPoints + ",landmarks=" + Landmarks + ",enemy_markers=" + EnemyMarkers
                + ",content=" + ContentSource + "/" + ContentEncounters + "e" + ContentGates + "g"
                + ",encounters=" + EncounterGroups + ",clearable=" + EncounterRemainingClearable
                + ",scav=" + ScavengePlaced + "/" + ScavengeAnchors + "(open=" + ScavengeOpened
                + ",avail=" + ScavengeAvailable + ",failed=" + ScavengeFailed + ")"
                + ",residents=" + ResidentsSpawned + ",rings=" + ExtractionRingsBuilt + ",bell=" + BellUnlocked
                + ",flags=" + StoryFlags + ",regions=" + VisitedRegions + ",slot=" + StoryCurrentSlot
                + ",can_write=" + StoryCanWrite + ",bounty=" + BountyRounds + "/" + BountyActive
                + ",seed=" + RaidSeed
                // 新字段只追加在末尾：前面的顺序冻结，两次出击的行仍可逐字对照。
                + ",scav_built=" + ScavengeBuilt + ",ground_regions=" + GroundRegions
                + ",wind=" + WindUnlocked + ",star=" + StarUnlocked;
        }
    }
}
