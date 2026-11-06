#if BOSSRUSH_DEV
// ============================================================================
// F3GameplayValidationTidegateDrillKeyLead.cs - 潮闸镇 Dev 演练：轮班钥匙、领班柜与响铊（**非只读**，2026-11-07）
// ============================================================================
// 隔离纪律同 F3GameplayValidationTidegateDrill.cs 头注释：不翻箱、不发物品、不写存档；钥匙走生产的取钥匙那一步，
// 用例里放回，RestoreTidegateDrillState 再兜一次。因 LargeFileBudgetGuard（新文件不超过 1200 行）从用例文件拆出。
// ============================================================================

using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace BossRush
{
    internal sealed partial class F3GameplayValidationRunner
    {

        // ====================================================================
        // 3b. 轮班钥匙、领班柜与响铊（不翻箱：钥匙走生产的取钥匙那一步，最后放回去）
        // ====================================================================

        private IEnumerator RunTidegateDrillShiftKeyLead()
        {
            const string id = "TIDE_DRILL_SHIFT_KEY_LEAD";
            Stopwatch sw = Stopwatch.StartNew();
            TidegateSession session;
            string gone;
            if (!TidegateDrillLive(out session, out gone)) { Record(id, "SKIP", 0L, string.Empty, gone); yield break; }
            TidegateLoot loot = session.DevLoot;
            if (loot == null) { Record(id, "FAIL", 0L, string.Empty, "搜刮 owner 不可用"); yield break; }
            var errors = new List<string>();
            var notes = new List<string>();
            string keyBox = loot.DevShiftKeyBox, cabinetId = TidegateLoot.DevForemanCabinet;
            bool keyObserved = false;
            if (keyBox == null) notes.Add("no_locked_cabinet_this_raid");
            else
            {
                if (keyBox != "LOOT_tool_3" && keyBox != "LOOT_tool_4") errors.Add("key_box_" + keyBox);
                TidegateLoot.DevPointInfo cabinet = loot.DevPoint(cabinetId, false);
                if (cabinet == null) errors.Add("cabinet_marker_missing");
                else
                {
                    int lockMark = session.DevStatusMark;
                    // 锁牌提示一趟只说一次：开跑前已经路过柜子就不再要求这条字幕。
                    bool tagAlreadyShown = loot.DevLockExplained;
                    yield return TidegateDrillStand(session, cabinet.Position, errors, "foreman_cabinet");
                    // 60 m 内最近的先建、0.5 秒一只：站到柜前最多等 8 秒。
                    float until = Time.realtimeSinceStartup + 8f;
                    while (Time.realtimeSinceStartup < until && !ShouldAbort())
                    {
                        cabinet = loot.DevPoint(cabinetId, false);
                        if (cabinet == null || cabinet.Built || cabinet.Failed) break;
                        yield return null;
                    }
                    yield return WaitSeconds(1.2f);
                    cabinet = loot.DevPoint(cabinetId, false);
                    if (cabinet == null || !cabinet.Built || cabinet.Failed) errors.Add("cabinet_not_built");
                    else if (cabinet.Flooded) notes.Add("cabinet_flooded_skip");
                    else
                    {
                        keyObserved = true;
                        if (!cabinet.Locked || cabinet.BoxActive) errors.Add("cabinet_not_locked_on_arrival");
                        if (loot.DevKeyPinActive || loot.DevShiftKeyObjective != null) errors.Add("key_hint_before_key");
                        if (!tagAlreadyShown && TidegateDrillCount(session.DevStatusSince(lockMark), new[] { "领班柜锁着", "cabinet locked" }) != 1)
                            errors.Add("lock_tag_caption_missing");
                        int keyMark = session.DevStatusMark;
                        if (!loot.DevTakeShiftKey()) errors.Add("take_key_refused");
                        yield return WaitSeconds(0.6f);
                        cabinet = loot.DevPoint(cabinetId, false);
                        if (cabinet == null || cabinet.Locked || !cabinet.BoxActive) errors.Add("cabinet_still_locked_with_key");
                        if (!loot.DevKeyPinActive) errors.Add("key_map_pin_missing");
                        string objective = session.DevObjective();
                        if (!TidegateDrillContainsAny(objective, "轮班钥匙", "shift key"))
                        {
                            // 带着潮汐怀表时怀表那一行优先，属于设计。
                            if (TidegateDrillContainsAny(objective, "潮汐怀表", "pocket watch")) notes.Add("objective_preempted_by_watch");
                            else errors.Add("key_objective_missing");
                        }
                        if (TidegateDrillCount(session.DevStatusSince(keyMark), new[] { "轮班钥匙", "shift key" }) != 1) errors.Add("key_caption_missing");
                        loot.DevResetShift();
                        yield return null;
                        cabinet = loot.DevPoint(cabinetId, false);
                        if (cabinet == null || !cabinet.Locked || cabinet.BoxActive || loot.DevKeyPinActive || loot.DevShiftKeyObjective != null)
                            errors.Add("shift_key_not_reset");
                    }
                }
            }
            // 响铊：测深只指没搜过的埋货，青圈在 M 图上，用完由演练撤掉（生产 25 秒自毁）。
            int leadMark = session.DevStatusMark;
            bool sounded = session.TrySoundLead();
            if (!sounded) errors.Add("sounding_refused_in_tidegate");
            else if (session.DevSoundingPinActive)
            {
                Vector3 pin = session.DevSoundingPinPosition;
                bool target = false;
                foreach (TidegateLoot.DevPointInfo point in loot.DevPoints(false))
                    if (point.Kind == "buried" && !point.Opened && !point.Failed && TidegateDrillFlat(point.Position, pin) < 0.1f) target = true;
                if (!target) errors.Add("sounding_pin_not_on_unsearched_cache");
            }
            else if (TidegateDrillCount(session.DevStatusSince(leadMark), new[] { "埋货都搜过了", "No buried caches" }) == 0) errors.Add("sounding_pin_missing");
            if (sounded && TidegateDrillCount(session.DevStatusSince(leadMark), new[] { "响铊", "lead", "Sounding" }) == 0) errors.Add("sounding_caption_missing");
            session.DevClearSoundingPin();
            if (session.DevSoundingPinActive) errors.Add("sounding_pin_not_cleared");
            notes.Add("key_box=" + (keyBox ?? "none") + ",key_observed=" + keyObserved + ",sounded=" + sounded);
            if (errors.Count == 0 && !keyObserved)
            {
                Record(id, "WARN", sw.ElapsedMilliseconds, string.Join(" | ", notes.ToArray()), "这一趟没观测到锁柜与钥匙（没有锁柜或柜子被淹），须人工补测");
                yield break;
            }
            RecordTidegateDrill(id, sw, errors, notes);
        }
    }
}
#endif
