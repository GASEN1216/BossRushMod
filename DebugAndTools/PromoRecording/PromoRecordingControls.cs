#if BOSSRUSH_DEV
// ============================================================================
// PromoRecordingControls.cs - F3「录制视频」页：开始 / 跳到某一项 / 发道具 / 开关
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    internal static class PromoRecordingControls
    {
        internal static void Build(Transform parent, ModBehaviour host, Action closeMenu, Action<string, bool> report)
        {
            Font font = BossRushUI.GetLegacyChineseFont();
            List<PromoRecordingStep> script = PromoRecordingKit.BuildFieldAdventureScript(host);
            PromoRecordingDirector running = PromoRecordingDirector.Find(host);

            GameObject intro = F3DebugUiKit.CreateSection(parent,
                L10n.T("录制视频 · v2.3.8 野外趣事", "Recording · v2.3.8 Field Adventures"),
                L10n.T("在普通出击地图里按剧本依次触发要拍的内容：每项先倒数 5 秒给你站位，再刷出对应内容并按建议时长计时，到点自动进入下一项。"
                    + "屏幕最右侧 180 像素的窄条实时提示要做什么（剪辑时会裁掉）。血量低于 45% 自动回满。"
                    + "热键（小键盘同样可用）：小键盘 3 / PgDn 下一项、9 / PgUp 重来本项、7 / Home 暂停计时、0 / Insert 刷 3 个靶子、1 / End 结束。",
                    "Triggers each shot in order on a normal raid map: 5 s to get in position, then the content spawns and a timer runs; "
                    + "it moves on automatically. A 180 px strip on the far right tells you what to do (cropped out in editing). Health refills below 45%. "
                    + "Keys (numpad works too): Num 3 / PgDn next, 9 / PgUp redo, 7 / Home pause, 0 / Insert 3 targets, 1 / End stop."),
                font);
            Text status = F3DebugUiKit.CreateLabel(intro.transform, font,
                running != null ? L10n.T("进行中：", "Running: ") + running.StatusLine : L10n.T("未开始", "Not started"),
                17, running != null ? BossRushUIColors.SuccessText : BossRushUIColors.TextSecondary, FontStyle.Normal);

            GameObject row = F3DebugUiKit.CreateRow(intro.transform);
            F3DebugUiKit.CreateActionButton(row.transform, font, L10n.T("发道具并开始（关闭菜单）", "Grant kit and start (closes menu)"), BossRushUIColors.AccentFill, delegate
            {
                string kit = PromoRecordingKit.GrantKit();
                PromoRecordingDirector.Begin(host, script, -1);
                report(kit ?? L10n.T("已发放食材与残日盾片；站到开阔处按 PgDn（小键盘 3）开始第一项", "Kit granted; stand in the open and press PgDn (Num 3)"), kit != null);
                closeMenu();
            });
            F3DebugUiKit.CreateActionButton(row.transform, font, L10n.T("只发道具", "Grant kit only"), BossRushUIColors.Header, delegate
            {
                string kit = PromoRecordingKit.GrantKit();
                report(kit ?? L10n.T("已发放：弹药 60、手雷 3、药品 3、残日盾片 1", "Granted: 60 ammo, 3 grenades, 3 medicine, 1 shield"), kit != null);
            });
            F3DebugUiKit.CreateActionButton(row.transform, font, L10n.T("刷 3 个靶子", "Spawn 3 targets"), BossRushUIColors.Header, delegate
            {
                string error = PromoRecordingKit.SpawnTargets(host, 3);
                report(error ?? L10n.T("已在身边刷出 3 个敌人", "Spawned 3 enemies nearby"), error != null);
            });
            F3DebugUiKit.CreateActionButton(row.transform, font, L10n.T("结束录制", "Stop recording"), BossRushUIColors.Danger, delegate
            {
                PromoRecordingDirector.End(host);
                report(L10n.T("已结束录制，提示条已关闭", "Recording stopped"), false);
                closeMenu();
            });

            GameObject options = F3DebugUiKit.CreateRow(intro.transform);
            bool autoAdvance = running == null || running.AutoAdvance;
            bool autoHeal = running == null || running.AutoHeal;
            F3DebugUiKit.CreateActionButton(options.transform, font,
                (autoAdvance ? "√ " : "  ") + L10n.T("到点自动下一项", "Auto advance"), BossRushUIColors.Header, delegate
                {
                    PromoRecordingDirector d = PromoRecordingDirector.Find(host);
                    if (d == null) { report(L10n.T("先开始录制", "Start recording first"), true); return; }
                    d.AutoAdvance = !d.AutoAdvance;
                    report(d.AutoAdvance ? L10n.T("到点自动下一项：开", "Auto advance: on") : L10n.T("到点自动下一项：关（用 PgDn / 小键盘 3 手动切）", "Auto advance: off (use PgDn / Num 3)"), false);
                });
            F3DebugUiKit.CreateActionButton(options.transform, font,
                (autoHeal ? "√ " : "  ") + L10n.T("低血自动回满", "Auto heal"), BossRushUIColors.Header, delegate
                {
                    PromoRecordingDirector d = PromoRecordingDirector.Find(host);
                    if (d == null) { report(L10n.T("先开始录制", "Start recording first"), true); return; }
                    d.AutoHeal = !d.AutoHeal;
                    report(d.AutoHeal ? L10n.T("低血自动回满：开", "Auto heal: on") : L10n.T("低血自动回满：关", "Auto heal: off"), false);
                });

            GameObject jump = F3DebugUiKit.CreateSection(parent,
                L10n.T("单独触发某一项", "Trigger one shot"),
                L10n.T("重拍用：从这一项开始往下走（关闭菜单后倒数 5 秒）。", "For retakes: start from this shot (5 s countdown after the menu closes)."),
                font);
            GameObject line = null;
            for (int i = 0; i < script.Count; i++)
            {
                if (i % 4 == 0) line = F3DebugUiKit.CreateRow(jump.transform);
                int index = i;
                F3DebugUiKit.CreateActionButton(line.transform, font, (i + 1) + ". " + script[i].Title, BossRushUIColors.Header, delegate
                {
                    PromoRecordingDirector.Begin(host, script, index);
                    report(L10n.T("5 秒后触发：", "Triggering in 5 s: ") + script[index].Title, false);
                    closeMenu();
                });
            }
        }
    }
}
#endif
