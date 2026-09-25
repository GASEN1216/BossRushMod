// ============================================================================
// F3GameplayValidationMapTourJudges.cs - 地图选择器逐图进场（MAP_TOUR_*）的纯判据
// ============================================================================
// 模块说明：
//   F3 主套件 6/7 之后按地图选择器的清单逐张进场（F3GameplayValidationMapTour.cs 取数）。
//   这里只放判据：只吃基本类型与小结构，不引用 Unity，由执行回归
//   tests/fixtures/F3MapTourJudges 逐字抽出运行（AGENTS §4.17「判据与取数分开」）。
// ============================================================================

using System.Collections.Generic;
using System.Text;

namespace BossRush
{
    /// <summary>一张图进场、核对、回基地之后的观测值。全是基本类型，取数在 Unity 侧完成。</summary>
    internal struct F3MapTourObservation
    {
        internal string ExpectedScene;
        internal string ActualScene;
        internal bool RuntimeReady;
        internal bool ArenaActive;
        /// <summary>进场后意外开起来的模式（Mode D / E 之类）；没有为 null。</summary>
        internal string OtherMode;
        internal bool EntryPointPresent;
        internal int ConfiguredPoints;
        internal int LoadedPoints;
        internal int GroundedPoints;
        internal int NavPoints;
        /// <summary>落不了地或离导航网格太远的刷新点，形如 "3:ground,7:nav=4.2m"。</summary>
        internal string PointOffenders;
        internal bool HasCustomSpawn;
        internal float PlayerToSpawnMeters;
        internal bool PlayerGrounded;
        internal bool ReturnedToBase;
        /// <summary>回基地之后还挂着的竞技场 / 模式状态；干净为 null。</summary>
        internal string BaseLeftover;
        /// <summary>取数本身抛了异常（类型名）；正常为 null。</summary>
        internal string CollectError;
        internal long ElapsedMs;
    }

    internal static class F3MapTourJudges
    {
        /// <summary>玩家离配置的传送点多远以内算「落在出生点」（生产 TeleportPlayerToCustomPosition 只修 Y，水平应当几乎不动）。</summary>
        internal const float MaxPlayerToSpawnMeters = 4f;
        /// <summary>刷新点离最近可走导航多远以内算「Boss 走得动」（水平）。</summary>
        internal const float MaxNavDistanceMeters = 3f;

        internal const string CasePrefix = "MAP_TOUR_";

        #region 纯判据

        /// <summary>用例 id：MAP_TOUR_ + 场景名大写，非字母数字换成下划线（tools/gameplay_coverage.py 的 MAP_TOUR_* 展开同口径）。</summary>
        internal static string CaseId(string sceneName)
        {
            var sb = new StringBuilder(CasePrefix);
            foreach (char c in sceneName ?? string.Empty)
                sb.Append((c >= 'a' && c <= 'z') ? (char)(c - 32) : ((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') ? c : '_'));
            return sb.ToString();
        }

        /// <summary>
        /// 一张图算通过：进到了配置的场景、竞技场接管了（没有误进别的模式）、交互点在、刷新点全数加载且每个都落得了地、
        /// 接得上导航，玩家落在配置的传送点上且脚下有地，回基地之后竞技场与模式状态都清掉了。
        /// </summary>
        internal static bool JudgeArrival(F3MapTourObservation o, out string metrics, out string reason)
        {
            metrics = "scene=" + (o.ActualScene ?? "null") + ",expected=" + (o.ExpectedScene ?? "null")
                + ",ready=" + o.RuntimeReady + ",arena=" + o.ArenaActive + ",mode=" + (o.OtherMode ?? "normal")
                + ",entry_point=" + o.EntryPointPresent
                + ",points=" + o.LoadedPoints + "/" + o.ConfiguredPoints
                + ",grounded=" + o.GroundedPoints + ",nav=" + o.NavPoints
                + ",custom_spawn=" + o.HasCustomSpawn
                + (o.HasCustomSpawn ? ",player_to_spawn_m=" + o.PlayerToSpawnMeters.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) : string.Empty)
                + ",player_grounded=" + o.PlayerGrounded + ",returned_base=" + o.ReturnedToBase
                + ",base_leftover=" + (o.BaseLeftover ?? "none") + ",elapsed_ms=" + o.ElapsedMs;
            var errors = new List<string>();
            if (o.CollectError != null) errors.Add("collect_threw:" + o.CollectError);
            if (string.IsNullOrEmpty(o.ExpectedScene) || o.ActualScene != o.ExpectedScene) errors.Add("wrong_scene");
            if (!o.RuntimeReady) errors.Add("runtime_not_ready");
            if (!o.ArenaActive) errors.Add("arena_not_active");
            if (o.OtherMode != null) errors.Add("unexpected_mode:" + o.OtherMode);
            if (!o.EntryPointPresent) errors.Add("entry_point_missing");
            if (o.ConfiguredPoints <= 0) errors.Add("no_spawn_points_configured");
            else if (o.LoadedPoints != o.ConfiguredPoints) errors.Add("spawn_points_not_loaded");
            else
            {
                if (o.GroundedPoints < o.LoadedPoints) errors.Add("spawn_points_floating");
                if (o.NavPoints < o.LoadedPoints) errors.Add("spawn_points_off_navmesh");
            }
            if (o.HasCustomSpawn && !(o.PlayerToSpawnMeters <= MaxPlayerToSpawnMeters)) errors.Add("player_not_at_custom_spawn");
            if (!o.PlayerGrounded) errors.Add("player_not_grounded");
            if (!o.ReturnedToBase) errors.Add("return_base_failed");
            else if (o.BaseLeftover != null) errors.Add("arena_state_left_after_return:" + o.BaseLeftover);
            reason = errors.Count == 0 ? null
                : string.Join(",", errors.ToArray()) + (string.IsNullOrEmpty(o.PointOffenders) ? string.Empty : " | points=" + o.PointOffenders);
            return errors.Count == 0;
        }

        /// <summary>MAP_TOUR_ALL：清单非空，而且每一张都跑到了、都过了；少跑一张也算红（不能用跑过的几张冒充全表）。</summary>
        internal static bool JudgeTour(int configured, int passed, int ran, out string metrics, out string reason)
        {
            metrics = "maps=" + configured + ",ran=" + ran + ",passed=" + passed;
            if (configured <= 0) { reason = "map_selector_empty"; return false; }
            if (ran < configured) { reason = "not_all_maps_ran"; return false; }
            if (passed < configured) { reason = "some_maps_failed"; return false; }
            reason = null;
            return true;
        }

        #endregion
    }
}
