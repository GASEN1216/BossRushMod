#if BOSSRUSH_DEV
// ============================================================================
// TidegateDrillHooks.cs - 潮闸镇 Dev 演练与 F3 调试用的观测面和驱动点（只在 Dev 构建里存在）
// ============================================================================
// owner 2026-11-06：「直接做演练一次性测完，你还可以做清空状态按钮，尽可能减少人工测试」。
// F3「潮闸镇演练」（DebugAndTools/F3GameplayValidationTidegateDrill.cs）与 F3「NPC/剧情」页的潮闸镇区
// （DebugAndTools/TidegateProgressDebugControls.cs）都经这里拨潮相、驱动机关、读各子系统的状态。
//
// 纪律（tests/TidegateDrillGuard.py 守着）：
// - 整份 #if BOSSRUSH_DEV；生产文件里的钩子点同样包在 #if BOSSRUSH_DEV 里，正式构建一行都没有。
// - 拨潮相只加在 TidegateTide.ClockHours 上：不写官方时钟，不碰存档里的游戏时间；会话收尾（TidegateTide.Dispose）清零。
// - 驱动机关走生产路径（TurnValve / LightLighthouse），只把「潮位站缺手轮」这一道换成不发物品的旁路；
//   复位只改这一趟出击的内存状态，不写存档。
// - Dev 前缀的只读属性不改任何东西；会改状态的方法名里写明了改什么。
// - 唯一写存档的是 TidegateStory.DevResetCurrentSlot（F3 手动按钮「清空潮闸镇进度」），演练不调用它。
// ============================================================================

using System;
using System.Collections.Generic;
using Duckov.MiniMaps;
using ItemStatsSystem;
using ItemStatsSystem.Data;
using Saves;
using UnityEngine;

namespace BossRush
{
    internal sealed partial class TidegateTide
    {
        /// <summary>拨潮相的偏移（游戏小时），只加在 <see cref="ClockHours"/> 上。会话收尾清零。</summary>
        internal static double DevOffsetHours;

        /// <summary>拨到 <paramref name="target"/> 相开始后 <paramref name="realSecondsIn"/> 现实秒处（只往前拨）。返回拨过的游戏小时，时钟不可用时 NaN。</summary>
        internal static double DevJumpInto(TidegateTidePhase target, double realSecondsIn)
        {
            double hours = Math.Max(0.0, realSecondsIn) * SkyIslandLighting.ClockScale() / 3600.0;
            return DevJumpToCycleOffset(F3GameplayValidationRunner.TidegateDrillPhaseStart(target) + hours);
        }

        /// <summary>拨到 <paramref name="target"/> 相结束前 <paramref name="realSecondsLeft"/> 现实秒处（只往前拨）。</summary>
        internal static double DevJumpBeforeEnd(TidegateTidePhase target, double realSecondsLeft)
        {
            double hours = Math.Max(0.0, realSecondsLeft) * SkyIslandLighting.ClockScale() / 3600.0;
            return DevJumpToCycleOffset(F3GameplayValidationRunner.TidegateDrillPhaseEnd(target) - hours);
        }

        private static double DevJumpToCycleOffset(double goal)
        {
            double now = ClockHours();
            if (double.IsNaN(now)) return double.NaN;
            double delta = F3GameplayValidationRunner.TidegateDrillForwardHours(TidegateTideRules.CycleOffset(now), goal);
            DevOffsetHours += delta;
            return delta;
        }

        internal static TidegateTidePhase DevNextPhase(TidegateTidePhase phase)
        {
            switch (phase)
            {
                case TidegateTidePhase.Ebb: return TidegateTidePhase.Low;
                case TidegateTidePhase.Low: return TidegateTidePhase.Flood;
                case TidegateTidePhase.Flood: return TidegateTidePhase.High;
                default: return TidegateTidePhase.Ebb;
            }
        }

        /// <summary>演练收尾：大闸放下。泻湖下一帧回到满湖（<see cref="TidegateTideRules.StepLagoon"/> 闸关着恒为满湖）。</summary>
        internal void DevCloseSluice() { sluiceOpen = false; }

        /// <summary>演练收尾：灯塔熄灭，灯头出灯数组（下一次排灯生效）。</summary>
        internal void DevClearBeacon() { beaconOn = false; nextLamps = 0f; }

        internal bool DevBeaconOn { get { return beaconOn; } }
        internal Vector3 DevBeaconPosition { get { return new Vector3(beaconPosition.x, beaconPosition.y, beaconPosition.z); } }
        internal float DevBeaconRange { get { return beaconPosition.w; } }
        internal int DevStaticLamps { get { return staticLamps; } }
        internal Vector4 DevLampPosition(int index) { return index >= 0 && index < MaxLamps ? lampPositions[index] : Vector4.zero; }
    }

    internal sealed partial class TidegateDevices
    {
        private bool devBypassHandwheel;

        internal bool DevValveOpen(int index) { return index >= 0 && index < valves.Length && valves[index]; }
        internal TidegateDeviceInteractable DevValveInteraction(int index)
        {
            foreach (GameObject host in owned)
                if (host != null && host.name == "TidegateDevice_Valve" + index)
                    return host.GetComponentInChildren<TidegateDeviceInteractable>();
            return null;
        }
        internal static string DevValveName(int index) { return ValveName(index); }
        internal static int DevTideGaugeValve { get { return TideGaugeValve; } }
        internal bool DevLighthouseLit { get { return lighthouseLit; } }
        internal bool DevBoatArrived { get { return boatArrived; } }
        internal bool DevReinforcePending { get { return reinforceAt > 0f; } }
        internal Transform DevBeam { get { return beam; } }
        internal bool DevBeamLightOn { get { return beamLight != null && beamLight.enabled; } }
        /// <summary>这一趟有没有动过机关（拧过阀门、提过大闸、点过灯塔）。</summary>
        internal bool DevTouched { get { return OpenValves > 0 || lighthouseLit || boatAt > 0f || (tide != null && tide.SluiceOpen); } }

        /// <summary>
        /// 走生产 <see cref="TurnValve"/>（读完 3 秒条之后那一步）。<paramref name="bypassHandwheel"/> 为真时潮位站那一处
        /// 不发物品、直接当作装上了手轮；为假时与玩家空手去拧一样会被拒。
        /// </summary>
        internal void DevTurnValve(int index, bool bypassHandwheel)
        {
            if (index < 0 || index >= valves.Length) return;
            devBypassHandwheel = bypassHandwheel;
            try { TurnValve(index); }
            finally { devBypassHandwheel = false; }
        }

        /// <summary>走生产 <see cref="LightLighthouse"/>（读完 2 秒条之后那一步）。</summary>
        internal void DevLightLighthouse() { LightLighthouse(); }

        /// <summary>
        /// 演练收尾：阀门全关、大闸放下、海堤增援不再来、灯塔熄灭（光束停用、灯头出灯数组、船走、灯塔码头关）。
        /// 只改这一趟出击的内存状态；全图显示由 <see cref="TidegateEnemies.DevEndReveal"/> 收。
        /// </summary>
        internal void DevReset()
        {
            for (int i = 0; i < valves.Length; i++) valves[i] = false;
            reinforceAt = -1f;
            lighthouseLit = false;
            boatArrived = false;
            boatAt = -1f;
            if (tide != null) { tide.DevCloseSluice(); tide.DevClearBeacon(); }
            if (extraction != null) extraction.SetLighthouseBoat(false);
            if (beamLight != null)
            {
                GameObject light = beamLight.gameObject;
                owned.Remove(light);
                UnityEngine.Object.Destroy(light);
            }
            beamLight = null;
            if (beam != null) beam.gameObject.SetActive(false);
            beam = null;
        }
    }

    internal sealed partial class TidegateExtraction
    {
        internal List<string> DevExitIds()
        {
            var ids = new List<string>(exits.Count);
            foreach (Exit exit in exits) ids.Add(exit.Id);
            return ids;
        }

        internal List<string> DevOpenExits()
        {
            var ids = new List<string>(exits.Count);
            foreach (Exit exit in exits) if (SafeOpen(exit)) ids.Add(exit.Id);
            return ids;
        }

        internal List<string> DevVisibleRings()
        {
            var ids = new List<string>(exits.Count);
            foreach (Exit exit in exits) if (exit.Ring != null && exit.Ring.gameObject.activeSelf) ids.Add(exit.Id);
            return ids;
        }

        internal bool DevHasCountdown(string id)
        {
            foreach (Exit exit in exits) if (exit.Id == id) return exit.Countdown != null;
            return false;
        }

        internal string DevCurrentExit { get { return current == null ? null : current.Id; } }
        internal float DevHeld { get { return held; } }
        internal bool DevLighthouseBoat { get { return lighthouseBoat; } }
    }

    internal sealed partial class TidegateLoot
    {
        internal sealed class DevPointInfo
        {
            internal string Id, Kind;
            internal Vector3 Position;
            internal bool Built, Failed, Flooded, Opened, FloodHinted, BoxActive, Locked;
            /// <summary>埋货的场景模型是否显示；不是埋货或没有模型时为 null。</summary>
            internal bool? BuriedVisualActive;
            internal readonly List<int> Items = new List<int>();
            internal readonly List<string> ItemQualityDetails = new List<string>();
            internal int BestOrdinaryQuality = -1, BestOrdinaryId, BestOrdinaryUnitValue;
            internal bool Underwater;
            internal int HighRandomItems, ExpectedHighRandomItems, FixedWatches;
        }

        internal List<DevPointInfo> DevPoints(bool withItems)
        {
            var list = new List<DevPointInfo>(points.Count);
            foreach (Point point in points) list.Add(DevDescribe(point, withItems));
            return list;
        }

        internal DevPointInfo DevPoint(string id, bool withItems)
        {
            foreach (Point point in points) if (point.Id == id) return DevDescribe(point, withItems);
            return null;
        }

        private DevPointInfo DevDescribe(Point point, bool withItems)
        {
            var info = new DevPointInfo
            {
                Id = point.Id, Kind = point.Kind, Position = point.Marker != null ? point.Marker.position : Vector3.zero,
                Built = point.Built, Failed = point.Failed, Flooded = point.Flooded, Opened = point.Opened, FloodHinted = point.FloodHinted,
                BoxActive = point.Box != null && point.Box.gameObject.activeSelf, Locked = Locked(point),
                Underwater = IsUnderwaterContainer(point.Id, point.Kind),
                ExpectedHighRandomItems = point.Id == underwaterJackpotPoint && underwaterJackpotRolled ? 1 : 0,
                BuriedVisualActive = point.BuriedVisual != null ? point.BuriedVisual.gameObject.activeSelf : (bool?)null,
            };
            if (withItems && point.Box != null && point.Box.Inventory != null)
                foreach (Item item in point.Box.Inventory.Content)
                {
                    if (item == null) continue;
                    info.Items.Add(item.TypeID);
                    info.ItemQualityDetails.Add(item.TypeID + ":Q" + item.Quality + "/DQ" + (int)item.DisplayQuality + "x" + item.StackCount);
                    if (item.TypeID == BossRushItemIds.TidegateTideWatch) info.FixedWatches += Math.Max(1, item.StackCount);
                    if (CountsAsUnderwaterJackpot(item)) info.HighRandomItems += Math.Max(1, item.StackCount);
                    if (SkyIslandEnemyArmoryRules.IsOwnModItem(item.TypeID)) continue;
                    int quality = item.Quality, value = item.Value;
                    if (!PrizeCatalog.MeetsPrizeTerms(item)) continue;
                    if (quality < info.BestOrdinaryQuality || (quality == info.BestOrdinaryQuality && value <= info.BestOrdinaryUnitValue)) continue;
                    info.BestOrdinaryQuality = quality;
                    info.BestOrdinaryId = item.TypeID;
                    info.BestOrdinaryUnitValue = value;
                }
            return info;
        }

        internal bool DevUnderwaterStockMissing { get { return underwaterJackpotRolled && underwaterJackpotItem <= 0; } }

        /// <summary>这一趟钥匙挂在哪只红工具柜里（tool_3 / tool_4 之一）；没有锁柜时为 null。</summary>
        internal string DevShiftKeyBox { get { return shiftKeyBox; } }
        internal static string DevForemanCabinet { get { return ForemanCabinet; } }
        internal bool DevKeyPinActive { get { return keyPin != null && keyPin.activeSelf; } }
        internal bool DevLockExplained { get { return lockExplained; } }
        internal string DevShiftKeyObjective { get { return ShiftKeyObjective; } }

        /// <summary>演练：不翻箱，直接走生产的「取下钥匙」那一步（开锁、M 图金圈、HUD 目标行、提示）。</summary>
        internal bool DevTakeShiftKey()
        {
            if (shiftKeyBox == null || carryingShiftKey || openedCabinet) return false;
            TakeShiftKey();
            return true;
        }

        /// <summary>演练还原：钥匙放回原处，领班柜重新上锁（没建好或被淹的柜子维持原状），金圈撤掉。</summary>
        internal void DevResetShift()
        {
            if (shiftKeyBox == null) return;
            carryingShiftKey = false;
            openedCabinet = false;
            ClearKeyPin();
            Point cabinet = points.Find(p => p.Id == ForemanCabinet);
            if (cabinet != null && cabinet.Box != null) cabinet.Box.gameObject.SetActive(!cabinet.Flooded && !Locked(cabinet));
        }
    }

    internal sealed partial class TidegateWater
    {
        /// <summary>0 = 照负重；1 = 当轻装；2 = 当超重。只在 Dev 演练里写（<see cref="ApplySwim"/> 读），随这一趟的实例丢弃。</summary>
        internal int DevSwimOverride;

        internal float DevSeaTop { get { return seaBoxes.Count == 0 ? float.NaN : DevTop(seaBoxes[0]); } }
        internal float DevLagoonTop { get { return lagoonBoxes.Count == 0 ? float.NaN : DevTop(lagoonBoxes[0]); } }
        internal int DevSeaBoxCount { get { return seaBoxes.Count; } }
        internal bool DevHasLagoonBox { get { return lagoonBoxes.Count > 0; } }
        internal List<float> DevSeaTops { get { return DevTops(seaBoxes); } }
        internal List<float> DevLagoonTops { get { return DevTops(lagoonBoxes); } }

        private static List<float> DevTops(List<BoxCollider> boxes)
        {
            var tops = new List<float>(boxes.Count);
            foreach (BoxCollider box in boxes) tops.Add(DevTop(box));
            return tops;
        }

        /// <summary>下一帧就按负重 / 覆盖重判一次游泳能力（平时半秒一查）。</summary>
        internal void DevCheckNow() { nextCheck = 0f; }

        private static float DevTop(BoxCollider box)
        {
            return box.transform.TransformPoint(box.center + new Vector3(0f, box.size.y * 0.5f, 0f)).y;
        }
    }

    internal sealed partial class TidegateEnemies
    {
        private readonly Dictionary<string, float> devSpawnDistance = new Dictionary<string, float>(StringComparer.Ordinal);

        /// <summary>
        /// 演练：把一名淘沙帮按在深水点上，观测「泡满 8 秒卷走」这条生产规则（见 TickTide）。
        /// 官方寻路会把角色拉回可走面，单靠瞬移一次留不住人（2026-11-07 实机：left_deep=True、deep_s=0）。
        /// </summary>
        internal bool DevHoldInDeep(string id, Vector3 at)
        {
            foreach (Slot slot in slots)
            {
                if (slot.Id != id) continue;
                CharacterMainControl character = slot.Character;
                if (character == null || character.Health == null || character.Health.IsDead) return false;
                slot.DevHoldDeep = true;
                slot.DevHoldAt = at;
                character.SetPosition(at);
                return true;
            }
            return false;
        }

        internal int DevReleaseHolds()
        {
            int released = 0;
            foreach (Slot slot in slots)
            {
                if (!slot.DevHoldDeep) continue;
                slot.DevHoldDeep = false;
                released++;
            }
            return released;
        }

        private static void DevObservePatrol(Slot slot, Vector3 target, bool newLeg)
        {
            float remaining = Vector3.Distance(slot.Character.transform.position, target);
            if (newLeg) slot.DevLegDistance = remaining;
            else slot.DevLegAdvance = Mathf.Max(slot.DevLegAdvance, slot.DevLegDistance - remaining);
        }

        private void DevNoteSpawn(Slot slot, Vector3 position)
        {
            if (slot == null || slot.Marker == null || player == null) return;
            devSpawnDistance[slot.Id] = Vector3.Distance(player.transform.position, position);
        }

        internal sealed class DevEnemyInfo
        {
            internal string Id, Group, Kind;
            internal TidegateEnemyRole Role;
            internal CharacterMainControl Character;
            internal bool Started, Failed, Alive, Extra, MapPoint, PresetIsBoss, Partner, NoticeAnonymous;
            internal bool SandBossBound, SandBossFlats;
            internal Vector3 Home, Patrol, Position, NoticePosition;
            internal float SpawnDistance = -1f, ForceTrace = -1f, Hearing, JoinedAfter = -1f, PatrolAdvance;
        }

        internal List<DevEnemyInfo> DevEnemies()
        {
            var list = new List<DevEnemyInfo>(slots.Count);
            foreach (Slot slot in slots)
            {
                CharacterMainControl character = slot.Character;
                bool alive = character != null && character.Health != null && !character.Health.IsDead;
                float distance;
                TidegatePresetOwner owner = character == null ? null : character.GetComponent<TidegatePresetOwner>();
                var boss = slot.Kind == "boss" && character != null ? character.GetComponent<TidegateSandBoss>() : null;
                var info = new DevEnemyInfo
                {
                    Id = slot.Id, Group = slot.Group, Kind = slot.Kind, Role = slot.Role, Character = character, Started = slot.Started, Failed = slot.Failed,
                    Alive = alive, Extra = slot.Id.StartsWith("TidegateExtraSpawn_", StringComparison.Ordinal), MapPoint = slot.MapPoint != null,
                    PresetIsBoss = owner != null && owner.Preset != null && owner.Preset.isBoss,
                    SandBossBound = boss != null && boss.Reference == TidegateSandBoss.SourceName && boss.OwnsDrop &&
                        owner != null && owner.Preset != null && owner.Preset.nameKey == TidegateText.MapBossKey,
                    SandBossFlats = boss != null && boss.OnFlats,
                    Home = slot.Home, Patrol = slot.Ai != null ? slot.Ai.patrolPosition : slot.Home,
                    Position = character != null ? character.transform.position : slot.Home,
                    SpawnDistance = devSpawnDistance.TryGetValue(slot.Id, out distance) ? distance : -1f,
                    Partner = slot.Lead != null, ForceTrace = slot.Ai != null ? slot.Ai.forceTracePlayerDistance : -1f,
                    JoinedAfter = slot.Lead != null && slot.JoinedAt >= 0f ? slot.JoinedAt - slot.Lead.JoinedAt : -1f,
                    PatrolAdvance = slot.DevLegAdvance,
                    Hearing = slot.Ai != null && slot.Ai.isActiveAndEnabled ? slot.Ai.hearingAbility : 0f,
                    NoticePosition = slot.Ai != null ? slot.Ai.NoticeFromPos : Vector3.zero,
                    NoticeAnonymous = slot.Ai != null && slot.Ai.noticed && slot.Ai.NoticeFromCharacter == null,
                };
                list.Add(info);
            }
            return list;
        }

        internal int DevInFlight { get { return inFlight; } }
        internal int DevSuspendedCount { get { return activitySuspended.Count; } }
        /// <summary>性能采样的起止快照。按当前角色位置量水平距离，不把待生成、被卷走或已死亡的槽算作敌人。</summary>
        internal bool DevCountLivingNear(Vector3 position, float radius, out int all, out int near)
        {
            all = near = 0;
            if (closed) return false;
            float squaredRadius = radius * radius;
            foreach (Slot slot in slots)
            {
                CharacterMainControl character = slot.Character;
                if (character == null || character.Health == null || character.Health.IsDead) continue;
                all++;
                Vector3 delta = character.transform.position - position;
                if (delta.x * delta.x + delta.z * delta.z <= squaredRadius) near++;
            }
            return true;
        }
        internal bool DevRevealActive { get { return revealUntil > 0f; } }
        internal static Vector3 DevBossDigSpot { get { return BossDigSpot; } }

        /// <summary>演练收尾：提前摘全图地图点。官方只收到一次匿名声源，没有追踪状态要还原。</summary>
        internal void DevEndReveal()
        {
            if (revealUntil > 0f) { revealUntil = -1f; ClearReveal(); }
        }

        /// <summary>
        /// 演练收尾：移走演练开闸招来的海堤增援（不走死亡、不建尸体箱、不掉东西）。
        /// 仍在官方创建中的槽先标取消，Spawn 收到迟到结果后立即丢弃并销毁；此处即可移除槽和标记。
        /// <paramref name="pending"/> 只记录已交给 Spawn finally 收掉的异步结果，不表示还原失败。
        /// </summary>
        internal int DevRemoveExtras(out int pending)
        {
            pending = 0;
            int removed = 0;
            for (int i = slots.Count - 1; i >= 0; i--)
            {
                Slot slot = slots[i];
                if (!slot.Id.StartsWith("TidegateExtraSpawn_", StringComparison.Ordinal)) continue;
                slot.DevCancelled = true;
                if (slot.DevSpawning) pending++;
                if (slot.MapPoint != null) UnityEngine.Object.Destroy(slot.MapPoint);
                if (slot.Character != null)
                {
                    slot.Character.gameObject.SetActive(false);
                    UnityEngine.Object.Destroy(slot.Character.gameObject);
                }
                if (slot.Marker != null) UnityEngine.Object.Destroy(slot.Marker.gameObject);
                slots.RemoveAt(i);
                removed++;
            }
            return removed;
        }


        // 只认生产 SweepAway 实际移除的普通淘沙帮成员，不把死亡、卸载或海堤增援计为成功。
        internal int DevDiggerSweeps { get; private set; }
        internal string DevLastSweptDigger { get; private set; }
        private void DevNoteSweep(Slot slot)
        {
            if (slot.Group != "dig" || slot.Kind == "boss") return;
            DevDiggerSweeps++;
            DevLastSweptDigger = slot.Id;
        }
    }

    internal sealed partial class TidegateSounds
    {
        private readonly Dictionary<string, int> devPosted = new Dictionary<string, int>(StringComparer.Ordinal);

        private void DevNotePost(string file, bool posted)
        {
            if (!posted || file == null) return;
            int count;
            devPosted.TryGetValue(file, out count);
            devPosted[file] = count + 1;
        }

        internal int DevPosted(TidegateCue cue)
        {
            int index = (int)cue;
            return index >= 0 && index < CueFiles.Length ? DevPostedFile(CueFiles[index]) : 0;
        }

        internal int DevPostedFile(string file)
        {
            int count;
            return file != null && devPosted.TryGetValue(file, out count) ? count : 0;
        }

        internal int DevAmbientCount { get { return ambient.Count; } }

        internal int DevAmbientLoopsPlaying
        {
            get { int count = 0; foreach (Emitter emitter in ambient) if (emitter.Loop && emitter.Playing) count++; return count; }
        }

        internal bool DevUnavailable { get { return warned; } }
        internal string DevDirectory { get { return directory; } }
        internal static string DevBellFile { get { return BellFile; } }

        internal static List<string> DevAllFiles()
        {
            var files = new List<string>(CueFiles);
            files.Add(WavesFile);
            files.Add(BellFile);
            return files;
        }
    }

    internal sealed partial class TidegateMap
    {
        internal int DevExitPointCount
        {
            get { int count = 0; foreach (GameObject point in points) if (point != null) count++; return count; }
        }

        internal bool DevOfficialInstance { get { return settings != null && MiniMapSettings.Instance == settings; } }
    }

    internal sealed partial class TidegateSession
    {
        private readonly List<string> devStatus = new List<string>();
        private int devStatusTotal;

        private void DevNoteStatus(string message, bool error)
        {
            devStatus.Add((error ? "!" : string.Empty) + message);
            devStatusTotal++;
            if (devStatus.Count > 256) devStatus.RemoveAt(0);
        }

        /// <summary>到目前为止出过几条字幕（单调递增，给 <see cref="DevStatusSince"/> 当书签）。</summary>
        internal int DevStatusMark { get { return devStatusTotal; } }

        /// <summary>书签之后出的字幕（警示色的前面带 !）。</summary>
        internal List<string> DevStatusSince(int mark)
        {
            int take = Math.Max(0, Math.Min(devStatus.Count, devStatusTotal - mark));
            return devStatus.GetRange(devStatus.Count - take, take);
        }

        internal TidegateWorld DevWorld { get { return world; } }
        internal string DevIndoorLighting { get { return tide == null ? "indoor_fill=missing" : tide.DescribeIndoorFill(); } }
        internal TidegateWater DevWater { get { return water; } }
        internal TidegateExtraction DevExtraction { get { return extraction; } }
        internal TidegateLoot DevLoot { get { return loot; } }
        internal TidegateEnemies DevEnemies { get { return enemies; } }
        internal TidegateDevices DevDevices { get { return devices; } }
        internal TidegateSounds DevSounds { get { return sounds; } }
        internal TidegateMap DevMap { get { return map; } }
        internal ArenaPrototypeNavigation DevNavigation { get { return navigation; } }
        internal int DevUnreachableSpawns { get { return unreachable.Count; } }
        internal string DevObjective() { return Objective(); }
        internal string DevFieldStatus() { return FieldStatus(); }
        /// <summary>响铊这一趟的 M 图青圈（25 秒后自毁）。</summary>
        internal bool DevSoundingPinActive { get { return soundingPin != null && soundingPin.activeSelf; } }
        internal Vector3 DevSoundingPinPosition { get { return soundingPin != null ? soundingPin.transform.position : Vector3.zero; } }
        internal void DevClearSoundingPin()
        {
            if (soundingPin == null) return;
            soundingPin.SetActive(false);
            Destroy(soundingPin);
            soundingPin = null;
        }
        internal string DevHudObjective { get { return hud == null ? null : hud.ObjectiveText; } }
        internal string DevRegionPoi { get { return regionPoi == null ? null : regionPoi.name; } }
        internal int DevGroundMask { get { return groundMask; } }

        /// <summary>Markers 下任一组里的同名节点（地标 POI_*、撤离 Exit_*、搜刮 LOOT_* 等）。</summary>
        internal Transform DevFindMarker(string name)
        {
            if (world == null || world.Root == null || string.IsNullOrEmpty(name)) return null;
            Transform markers = world.Root.transform.Find("Markers");
            return markers == null ? null : DevFindChild(markers, name);
        }

        private static Transform DevFindChild(Transform parent, string name)
        {
            foreach (Transform child in parent)
            {
                if (child.name == name) return child;
                Transform deeper = DevFindChild(child, name);
                if (deeper != null) return deeper;
            }
            return null;
        }

        /// <summary>离 <paramref name="near"/> 最近的本图可走面（按当前潮位：被淹的三角形已经封掉）。</summary>
        internal bool DevSnapToWalkable(Vector3 near, out Vector3 point)
        {
            point = near;
            return navigation != null && navigation.TryNearestWalkable(near, out point);
        }

        /// <summary>
        /// 演练搬主角：落在陆地上时贴地；<paramref name="intoWater"/> 时放到水面下 0.2 m（脚下要有 1.2 m 以上的水）。
        /// 只走官方 SetPosition，不改落脚点兜底的规则。
        /// </summary>
        internal bool DevTeleport(Vector3 target, bool intoWater, out string reason)
        {
            reason = null;
            if (!IsSessionValid()) { reason = "session_invalid"; return false; }
            if (!TidegateWorld.PlayArea.Contains(new Vector2(target.x, target.z))) { reason = "outside_play_area"; return false; }
            float ground;
            bool hasGround = world.TryGroundAt(target, out ground);
            Vector3 point = target;
            if (intoWater)
            {
                float level = tide.WaterLevelAt(target);
                if (!hasGround || level - ground < 1.2f) { reason = "not_deep_enough"; return false; }
                point.y = level - 0.2f;
            }
            else
            {
                if (!hasGround) { reason = "no_ground"; return false; }
                point.y = ground + 0.12f;
            }
            player.SetPosition(point);
            return true;
        }
    }

    internal static partial class TidegateStory
    {
        /// <summary>「清空潮闸镇进度」一并收走的剧情链物品：信、潮汐怀表、阀门手轮、防水袋（鸭蹼脚掌不是剧情链，不动）。</summary>
        internal static readonly int[] DevStoryItemIds =
        {
            BossRushItemIds.TidegateLetter, BossRushItemIds.TidegateTideWatch, BossRushItemIds.TidegateHandwheel, BossRushItemIds.TidegateDryBag
        };

        /// <summary>
        /// F3「清空潮闸镇进度」（owner 2026-11-06 要的「清空状态按钮」）：剧情存档回到新档（信没送、没读、日志 0 页、怀表没交），
        /// 官方笔记图鉴下一拍按存档收回，主角身上与基地仓库里的剧情链物品收走。之后在基地等阿稳重新送信，整条线从头走一遍。
        /// 只在基地、专用测试档里由 F3 手动按钮调用；演练不调用。
        /// </summary>
        internal static bool DevResetCurrentSlot(out string detail)
        {
            if (persistence == null || !persistence.BeginAssetMutation()) { detail = "story_not_writable"; return false; }
            var reserved = new List<TidegateInventory.Reservation>();
            List<ItemTreeData> buffer = PlayerStorage.IncomingItemBuffer;
            var originalBuffer = new List<ItemTreeData>(buffer);
            int resetSlot = SavesSystem.CurrentSlot;
            bool accepted = false;
            int buffered = 0;
            try
            {
                DevRemoveStoryItems(reserved, out buffered);
                if (!persistence.StoreAssetState(new TidegateStoryData()) || !persistence.PublishAssetState()) { detail = "store_failed"; return false; }
                accepted = true;
                foreach (TidegateInventory.Reservation item in reserved) item.Commit();
            }
            catch (Exception e) { detail = "reset_failed:" + e.GetType().Name; return false; }
            finally
            {
                if (!accepted && resetSlot == SavesSystem.CurrentSlot) { buffer.Clear(); buffer.AddRange(originalBuffer); }
                foreach (TidegateInventory.Reservation item in reserved) item.Dispose();
                persistence.EndAssetMutation(accepted);
            }
            resendDay = long.MinValue;
            TidegateNoteBridge.RequestSync();
            TidegateStoryData back = Current;
            if (back == null || back.LetterDelivered || back.LetterRead || back.Pages != 0 || back.WatchDelivered)
            {
                detail = "readback_not_default";
                return false;
            }
            detail = "items_removed=" + reserved.Count + ",courier_buffer_removed=" + buffered + ",slot=" + SavesSystem.CurrentSlot;
            return true;
        }

        private static void DevRemoveStoryItems(List<TidegateInventory.Reservation> reserved, out int buffered)
        {
            buffered = 0;
            List<ItemTreeData> buffer = PlayerStorage.IncomingItemBuffer;
            var kept = new List<ItemTreeData>();
            foreach (ItemTreeData tree in buffer) buffered += DevKeepOtherItems(tree, kept);
            var found = new List<Item>();
            CharacterMainControl player = CharacterMainControl.Main;
            if (player != null && player.CharacterItem != null) DevCollect(player.CharacterItem, found);
            Inventory storage = PlayerStorage.Inventory;
            if (storage != null)
                foreach (Item stored in new List<Item>(storage.Content))
                    if (stored != null) DevCollect(stored, found);
            // 先准备保留子项的快递树，再预留原件；剧情接受前都可以按原位置撤回。
            foreach (Item item in found)
            {
                if (item == null || item.IsBeingDestroyed) continue;
                DevKeepOtherItems(ItemTreeData.FromItem(item), kept);
                TidegateInventory.Reservation reservation;
                if (!TidegateInventory.Reservation.TryCreate(item, true, out reservation))
                    throw new InvalidOperationException("story_item_reservation_failed");
                reserved.Add(reservation);
            }
            buffer.Clear();
            buffer.AddRange(kept);
        }

        private static void DevCollect(Item root, List<Item> found)
        {
            if (Array.IndexOf(DevStoryItemIds, root.TypeID) >= 0) { found.Add(root); return; }
            foreach (Item child in root.GetAllChildren(false, true))
                if (child != null) DevCollect(child, found);
        }

        /// <summary>去掉目标节点及指向它的边；非目标子树成为独立待取件，变量、附件、格位和格位锁原样保留。</summary>
        private static int DevKeepOtherItems(ItemTreeData tree, List<ItemTreeData> output)
        {
            if (tree == null || tree.entries == null) { output.Add(tree); return 0; }
            var entries = new Dictionary<int, ItemTreeData.DataEntry>();
            var parents = new Dictionary<int, int>();
            int removed = 0;
            foreach (ItemTreeData.DataEntry entry in tree.entries)
            {
                if (entry == null) continue;
                if (Array.IndexOf(DevStoryItemIds, entry.typeID) >= 0) removed++;
                entries.Add(entry.instanceID, entry);
                if (entry.inventory != null)
                    foreach (ItemTreeData.InventoryDataEntry child in entry.inventory) parents[child.instanceID] = entry.instanceID;
                if (entry.slotContents != null)
                    foreach (ItemTreeData.SlotInstanceIDPair child in entry.slotContents) parents[child.instanceID] = entry.instanceID;
            }
            if (removed == 0) { output.Add(tree); return 0; }
            var surviving = new HashSet<int>();
            foreach (ItemTreeData.DataEntry entry in entries.Values)
                if (Array.IndexOf(DevStoryItemIds, entry.typeID) < 0) surviving.Add(entry.instanceID);
            var components = new Dictionary<int, ItemTreeData>();
            foreach (ItemTreeData.DataEntry entry in entries.Values)
            {
                if (!surviving.Contains(entry.instanceID)) continue;
                int root = entry.instanceID, parent, depth = 0;
                while (parents.TryGetValue(root, out parent) && surviving.Contains(parent))
                {
                    if (++depth > entries.Count) throw new InvalidOperationException("cyclic_item_tree");
                    root = parent;
                }
                ItemTreeData component;
                if (!components.TryGetValue(root, out component))
                {
                    component = new ItemTreeData { rootInstanceID = root };
                    components.Add(root, component);
                    output.Add(component);
                }
                component.entries.Add(new ItemTreeData.DataEntry
                {
                    instanceID = entry.instanceID, typeID = entry.typeID, variables = entry.variables,
                    inventorySortLocks = entry.inventorySortLocks,
                    inventory = entry.inventory == null ? new List<ItemTreeData.InventoryDataEntry>()
                        : entry.inventory.FindAll(child => surviving.Contains(child.instanceID)),
                    slotContents = entry.slotContents == null ? new List<ItemTreeData.SlotInstanceIDPair>()
                        : entry.slotContents.FindAll(child => surviving.Contains(child.instanceID))
                });
            }
            return removed;
        }
    }
}
#endif
