// ============================================================================
// BossMapBeaconRuntimeModule.cs - 烽烟圈：竞技场 / Mode D / Mode E / Mode G 的 Boss 在 M 图上的标记
// ============================================================================
// 模块说明（2026-11-24 玩家许愿第 8 项）：
//   跨模式基础设施，按 AGENTS 4.9 放 Utilities/；状态与清理只归本模块（唯一 owner）。规则数字见 BossMapBeaconRules。
//
// 数据来源（只读现成的登记表，不扫场景）：
//   - 竞技场与 Mode D：竞技场模块的 Boss 掉落登记表，再补本波登记的内容 Boss（CopyMapBeaconBossesTo；
//     冠军之影、蚀日花冠只登记为本波 Boss、不进掉落表）；门控用宿主的「竞技场或 Mode D 进行中」。
//   - Mode E：ModeEAliveEnemies，只留与主角敌对的（玩家自己阵营的不标）。
//   - Mode G：当前 ModeGRunState 的托管 Boss（CollectTrackedBosses）。
//   Mode F、天空岛、潮闸镇、末班编组站、丧尸模式各有自己的指路方式，不接入。
//
// 开销：四个模式都不在进行时，每帧只读三个布尔就返回，不建任何东西、不订阅任何事件（O(1)）。
//   进行中每 0.5 秒对一次登记表（不分配：复用列表），每帧只更新至多 8 个标记的位置。
//   官方地图每帧自己读 POI 的位置；半径、颜色、图标只在 Setup 时读，所以换档 / 显形时重新 Setup 一次。
// 受击计时：有标记时才订阅 Health.OnHurt（命名方法 + 私有布尔防重复），标记清空、换场景、模块销毁时退订。
// 清理：Boss 死亡或离开登记表、模式结束、换场景、模块销毁。
// ============================================================================

using System;
using System.Collections.Generic;
using Duckov.MiniMaps;
using UnityEngine;

namespace BossRush
{
    internal sealed class BossMapBeaconRuntimeModule : BossRushRuntimeModuleBase
    {
        private const float ScanSeconds = 0.5f;
        private const string FallbackLabelKey = "BossRush_MapBeacon_Boss";

        private sealed class Beacon
        {
            internal CharacterMainControl Boss;
            internal Health Health;
            internal GameObject Root;
            internal SimplePointOfInterest Poi;
            internal string LabelKey;
            internal string SceneId;
            internal int Stage;
            internal bool Revealed;
            /// <summary>安静计时起点（Time.time）：上次收紧、或上次被玩家打到。</summary>
            internal float QuietSince;
            internal float NextRecenter;
            internal int Seen;
        }

        private readonly Func<bool> arenaOrModeDActive;
        private readonly Action<ICollection<CharacterMainControl>> copyArenaBosses;
        private readonly Func<bool> modeEActive;
        private readonly Func<List<CharacterMainControl>> modeEAliveEnemies;

        private readonly List<Beacon> beacons = new List<Beacon>();
        private readonly Dictionary<Health, Beacon> byHealth = new Dictionary<Health, Beacon>();
        private readonly List<CharacterMainControl> candidates = new List<CharacterMainControl>();
        private readonly System.Random random = new System.Random();
        private float scanTimer;
        private int scanStamp;
        private bool hurtSubscribed;
        private bool sourceWarned;
        private float pillarBusyUntil;

        internal BossMapBeaconRuntimeModule(Func<bool> arenaOrModeDActive, Action<ICollection<CharacterMainControl>> copyArenaBosses,
            Func<bool> modeEActive, Func<List<CharacterMainControl>> modeEAliveEnemies)
        {
            this.arenaOrModeDActive = arenaOrModeDActive;
            this.copyArenaBosses = copyArenaBosses;
            this.modeEActive = modeEActive;
            this.modeEAliveEnemies = modeEAliveEnemies;
        }

        public override string ModuleName { get { return "BossMapBeacon"; } }

        public override void OnSceneLoaded(SceneRuntimeContext context)
        {
            ClearAll();
        }

        public override void OnDestroy()
        {
            ClearAll();
            BossMapBeaconFx.ResetStaticCaches();
        }

        public override void OnUpdate(float deltaTime, float unscaledDeltaTime)
        {
            bool arena = Read(arenaOrModeDActive);
            bool modeE = Read(modeEActive);
            ModeGRunState modeG = ActiveModeG();
            if (!arena && !modeE && modeG == null)
            {
                if (beacons.Count > 0 || hurtSubscribed) ClearAll();
                return;
            }

            CharacterMainControl main = CharacterMainControl.Main;
            if (main == null) return;
            scanTimer -= deltaTime;
            if (scanTimer <= 0f)
            {
                scanTimer = ScanSeconds;
                Scan(main, arena, modeE, modeG);
            }
            Tick(main);
        }

        // ------------------------------------------------------------------
        // 对登记表：收掉离场的，空位补最近的
        // ------------------------------------------------------------------

        private void Scan(CharacterMainControl main, bool arena, bool modeE, ModeGRunState modeG)
        {
            candidates.Clear();
            try
            {
                if (arena && copyArenaBosses != null) copyArenaBosses(candidates);
                if (modeE && modeEAliveEnemies != null)
                {
                    List<CharacterMainControl> alive = modeEAliveEnemies();
                    if (alive != null) candidates.AddRange(alive);
                }
                if (modeG != null) modeG.CollectTrackedBosses(candidates);
            }
            catch (Exception e)
            {
                if (!sourceWarned)
                {
                    sourceWarned = true;
                    ModBehaviour.DevLog("[BossMapBeacon] [WARNING] 读 Boss 登记表失败: " + e.Message);
                }
            }

            scanStamp++;
            for (int i = 0; i < candidates.Count; i++)
            {
                CharacterMainControl boss = candidates[i];
                if (!IsLiveHostile(boss, main)) continue;
                Beacon beacon = Find(boss);
                if (beacon != null) beacon.Seen = scanStamp;
            }
            for (int i = beacons.Count - 1; i >= 0; i--)
            {
                if (beacons[i].Seen != scanStamp) RemoveAt(i);
            }

            Vector3 origin = main.transform.position;
            while (beacons.Count < BossMapBeaconRules.MaxBeacons)
            {
                CharacterMainControl nearest = null;
                float best = float.MaxValue;
                for (int i = 0; i < candidates.Count; i++)
                {
                    CharacterMainControl boss = candidates[i];
                    if (!IsLiveHostile(boss, main) || Find(boss) != null) continue;
                    float distance = (boss.transform.position - origin).sqrMagnitude;
                    if (distance < best)
                    {
                        best = distance;
                        nearest = boss;
                    }
                }
                if (nearest == null || !Create(nearest)) break;
            }
            candidates.Clear();
        }

        // ------------------------------------------------------------------
        // 每帧：显形的跟着 Boss，烽烟按安静时长收紧、按 8 秒重落
        // ------------------------------------------------------------------

        private void Tick(CharacterMainControl main)
        {
            float now = Time.time;
            Vector3 origin = main.transform.position;
            for (int i = beacons.Count - 1; i >= 0; i--)
            {
                Beacon beacon = beacons[i];
                if (beacon.Boss == null || beacon.Health == null || beacon.Health.IsDead || beacon.Root == null)
                {
                    RemoveAt(i);
                    continue;
                }
                Vector3 position = beacon.Boss.transform.position;
                if (beacon.Revealed)
                {
                    beacon.Root.transform.position = position;
                    continue;
                }
                float quiet = now - beacon.QuietSince;
                float dx = position.x - origin.x;
                float dz = position.z - origin.z;
                if (BossMapBeaconRules.ShouldReveal(beacon.Stage, quiet, Mathf.Sqrt(dx * dx + dz * dz)))
                {
                    Reveal(beacon, position, now);
                    continue;
                }
                int stage = BossMapBeaconRules.StageAfterQuiet(beacon.Stage, quiet);
                if (stage != beacon.Stage)
                {
                    beacon.Stage = stage;
                    beacon.QuietSince = now;
                    Recenter(beacon, position, now);
                    ApplyArea(beacon);
                }
                else if (now >= beacon.NextRecenter)
                {
                    Recenter(beacon, position, now);
                }
            }
        }

        private bool Create(CharacterMainControl boss)
        {
            GameObject root = null;
            try
            {
                root = new GameObject("BossRush_MapBeacon");
                Beacon beacon = new Beacon();
                beacon.Boss = boss;
                beacon.Health = boss.Health;
                beacon.Root = root;
                beacon.Poi = root.AddComponent<SimplePointOfInterest>();
                beacon.LabelKey = ResolveLabelKey(boss);
                beacon.SceneId = MapPointSceneResolver.Resolve();
                beacon.QuietSince = Time.time;
                beacon.Seen = scanStamp;
                Recenter(beacon, boss.transform.position, Time.time);
                ApplyArea(beacon);
                beacons.Add(beacon);
                byHealth[beacon.Health] = beacon;
                SubscribeHurt();
                return true;
            }
            catch (Exception e)
            {
                if (root != null) UnityEngine.Object.Destroy(root);
                ModBehaviour.DevLog("[BossMapBeacon] [WARNING] 烽烟圈创建失败: " + e.Message);
                return false;
            }
        }

        private void Recenter(Beacon beacon, Vector3 bossPosition, float now)
        {
            float x;
            float z;
            BossMapBeaconRules.Offset(beacon.Stage, random.NextDouble(), random.NextDouble(), out x, out z);
            beacon.Root.transform.position = bossPosition + new Vector3(x, 0f, z);
            beacon.NextRecenter = now + BossMapBeaconRules.RecenterSeconds;
        }

        /// <summary>烽烟段：朱砂区域圈，不画中心图标（圆心本来就是偏开的），标签是 Boss 名。</summary>
        private static void ApplyArea(Beacon beacon)
        {
            SimplePointOfInterest poi = beacon.Poi;
            poi.Color = BossMapBeaconFx.AreaColor;
            poi.ShadowColor = BossMapBeaconFx.AreaColor;
            poi.ScaleFactor = 1f;
            poi.IsArea = true;
            poi.AreaRadius = BossMapBeaconRules.RadiusOf(beacon.Stage);
            poi.HideIcon = true;
            poi.Setup(null, beacon.LabelKey, false, beacon.SceneId);
        }

        /// <summary>显形：钉在 Boss 身上的实时点，换烽火图标、放大；同一时刻最多一道光柱。</summary>
        private void Reveal(Beacon beacon, Vector3 bossPosition, float now)
        {
            beacon.Revealed = true;
            beacon.Root.transform.position = bossPosition;
            SimplePointOfInterest poi = beacon.Poi;
            poi.Color = BossMapBeaconFx.RevealColor;
            poi.ShadowColor = BossMapBeaconFx.RevealColor;
            poi.ScaleFactor = BossMapBeaconRules.RevealedScale;
            poi.IsArea = false;
            poi.HideIcon = false;
            poi.Setup(BossMapBeaconFx.GetIcon(), beacon.LabelKey, false, beacon.SceneId);
            if (BossMapBeaconRules.CanPlayPillar(now, pillarBusyUntil))
            {
                pillarBusyUntil = now + BossMapBeaconRules.PillarSeconds;
                BossMapBeaconFx.PlayPillar(bossPosition);
            }
        }

        // ------------------------------------------------------------------
        // 受击计时
        // ------------------------------------------------------------------

        private void SubscribeHurt()
        {
            if (hurtSubscribed) return;
            Health.OnHurt += OnAnyHealthHurt;
            hurtSubscribed = true;
        }

        private void UnsubscribeHurt()
        {
            if (!hurtSubscribed) return;
            Health.OnHurt -= OnAnyHealthHurt;
            hurtSubscribed = false;
        }

        /// <summary>主角打到被标记的 Boss：重新计安静时长（不会把已经收紧的圈放大回去）。</summary>
        private void OnAnyHealthHurt(Health health, DamageInfo damage)
        {
            if (health == null || byHealth.Count == 0) return;
            Beacon beacon;
            if (!byHealth.TryGetValue(health, out beacon) || beacon.Revealed) return;
            CharacterMainControl from = damage.fromCharacter;
            if (from == null || !from.IsMainCharacter) return;
            beacon.QuietSince = Time.time;
        }

        // ------------------------------------------------------------------
        // 杂项
        // ------------------------------------------------------------------

        private Beacon Find(CharacterMainControl boss)
        {
            for (int i = 0; i < beacons.Count; i++)
            {
                if (object.ReferenceEquals(beacons[i].Boss, boss)) return beacons[i];
            }
            return null;
        }

        private static bool IsLiveHostile(CharacterMainControl boss, CharacterMainControl main)
        {
            if (boss == null || object.ReferenceEquals(boss, main)) return false;
            try
            {
                Health health = boss.Health;
                return health != null && !health.IsDead && Team.IsEnemy(main.Team, boss.Team);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>地图标签用 Boss preset 的名字键（官方按键查本地化）；没有键时用一条注入的「Boss」。</summary>
        private static string ResolveLabelKey(CharacterMainControl boss)
        {
            CharacterRandomPreset preset = boss.characterPreset;
            if (preset != null && !string.IsNullOrEmpty(preset.nameKey)) return preset.nameKey;
            LocalizationHelper.InjectLocalization(FallbackLabelKey, "Boss");
            return FallbackLabelKey;
        }

        private void RemoveAt(int index)
        {
            Beacon beacon = beacons[index];
            beacons.RemoveAt(index);
            if (beacon.Health != null) byHealth.Remove(beacon.Health);
            if (beacon.Root != null) UnityEngine.Object.Destroy(beacon.Root);
            if (beacons.Count == 0) UnsubscribeHurt();
        }

        private void ClearAll()
        {
            for (int i = 0; i < beacons.Count; i++)
            {
                if (beacons[i].Root != null) UnityEngine.Object.Destroy(beacons[i].Root);
            }
            beacons.Clear();
            byHealth.Clear();
            candidates.Clear();
            UnsubscribeHurt();
            scanTimer = 0f;
            pillarBusyUntil = 0f;
        }

        private static ModeGRunState ActiveModeG()
        {
            try
            {
                ModeGRunState state = ModeGRunContext.Current;
                return state != null && state.IsActive ? state : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static bool Read(Func<bool> query)
        {
            try
            {
                return query != null && query();
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
