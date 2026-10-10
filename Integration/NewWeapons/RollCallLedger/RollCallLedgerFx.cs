using System;
using UnityEngine;

namespace BossRush
{
    /// <summary>短暂点名数字使用官方气泡；盖章使用共享爆点，无常驻 HUD 或预热。</summary>
    internal static class RollCallLedgerFx
    {
        private static readonly Color StampColor = new Color(0.94f, 0.52f, 0.34f, 0.9f);

        internal static void ShowMark(Health target, int ordinal)
        {
            if (target == null) return;
            try
            {
                // Show 的第三个参数是高度，时长使用具名参数，避免把时长误传成头顶偏移。
                Duckov.UI.DialogueBubbles.DialogueBubblesManager.Show(
                    ordinal.ToString(), target.transform, yOffset: target.healthBarHeight,
                    needInteraction: false, skippable: false, speed: 100f, duration: 0.85f);
            }
            catch (Exception e) { ModBehaviour.DevLog(RollCallLedgerConfig.LogPrefix + " 点名提示失败: " + e.Message); }
        }

        internal static void ShowComplete(CharacterMainControl player)
        {
            if (player == null) return;
            try
            {
                Duckov.UI.DialogueBubbles.DialogueBubblesManager.Show(
                    L10n.T("全员到齐！", "Everyone present!"), player.transform,
                    yOffset: 1.8f, needInteraction: false, skippable: false, speed: 100f, duration: 1.2f);
            }
            catch (Exception e) { ModBehaviour.DevLog(RollCallLedgerConfig.LogPrefix + " 到齐提示失败: " + e.Message); }
        }

        internal static void PlayStamp(Health target)
        {
            if (target == null) return;
            NewWeaponFx.PlayBurst(target.transform.position + Vector3.up * 0.7f,
                StampColor, 0.95f, 0.3f, 5, false);
        }
    }
}
