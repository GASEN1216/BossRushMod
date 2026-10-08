#if BOSSRUSH_DEV
// ============================================================================
// CampaignProgressDevReset.cs - F3「清空鸭王征程进度」的写入口（只在 Dev 构建里存在）
// ============================================================================
// owner 2026-10-08 要在 F3 里一键把当前存档槽的鸭王征程任务 / 剧情进度清回新档。
//
// - 清的是 Mod 自己的战役存档（CampaignTuning.ProgressSaveKey）：六章状态、交付 token、
//   线索、三段一次性引导全部回到 CreateDefault；第 1 章的可接状态由 GetState 的推导规则
//   重新开出来（存档里不落 Available，见 CampaignProgressService 头注）。
// - 解锁契约（CampaignFacilityUnlocks）与官方任务投影同步撤销：先回到未装载、再装载空集；
//   杰夫任务页由投影核心按清空后的事实下一拍自动撤回，这里只额外调一次
//   CampaignRuntime.SyncOfficialQuests 让它当场刷新，不直接改官方 QuestManager。
// - 官方笔记图鉴里已解锁的线索经 CampaignNoteBridge.EnsureNotesRegistered 反锁。
// - 不动背包 / 仓库里的物品，不动好感 / 婚姻、天空岛与其它模式。
// - 只走共享 store 的写屏障与协调器的立即落盘（CampaignSaveCoordinator.RequestImmediateFlush，
//   与 SkyIsland DevReset 同一条路），并从官方存档键直读回核对；不过自动验收的
//   「专用测试档」门——这是 owner 在自己档上手动按的按钮，调用方先弹确认框。
// - 只许在基地按：局内清空会把进行中的契约、终章战场与 HUD 弄乱。
// - 整份文件包在 #if BOSSRUSH_DEV 里：正式构建里没有这个入口。
// ============================================================================

using System;
using Saves;
using UnityEngine.SceneManagement;

namespace BossRush
{
    /// <summary>F3「清空鸭王征程进度」的整条流程（Dev 专用）。</summary>
    internal static class CampaignProgressDevReset
    {
        /// <summary>
        /// F3 按钮的整套流程：基地才执行；把当前槽的战役进度整份清回新档并立即落盘，
        /// 成功后从存档键读回核对确实是新档。失败时 <paramref name="detail"/> 给出原因。
        /// </summary>
        internal static bool DevResetCurrentSlot(ModBehaviour host, int expectedSlot, out string detail)
        {
            detail = null;
            try
            {
                if (host == null) { detail = "no_host"; return false; }
                if (SavesSystem.CurrentSlot != expectedSlot) { detail = "slot_changed"; return false; }
                if (!string.Equals(SceneManager.GetActiveScene().name, "Base_SceneV2", StringComparison.Ordinal)
                    || host.IsActive || host.IsModeDActive || host.IsModeEActive || host.IsModeFActive)
                {
                    detail = "not_in_base";
                    return false;
                }
                if (!CampaignPersistence.IsCurrentSlotReady) { detail = "slot_not_ready"; return false; }
                if (!CampaignPersistence.DevResetToDefault(out detail)) return false;

                // 顺序与换槽复位一致：先把解锁契约退回未装载，再整体装载空集；
                // 再会话态复位（清待交付缓存、收终章、作废对话演出），官方笔记反锁，任务页当场刷新。
                bool refreshed = Refresh("unlock_reset", CampaignFacilityUnlocks.ResetForSlotReload);
                refreshed &= Refresh("unlock_publish", () => CampaignPersistence.PublishTokensToUnlockContract(CampaignPersistence.Current));
                refreshed &= Refresh("session", CampaignProgressService.NotifySlotChanged);
                refreshed &= Refresh("notes", CampaignNoteBridge.EnsureNotesRegistered);
                refreshed &= Refresh("quests", () => { if (host.CampaignRuntime != null) host.CampaignRuntime.SyncOfficialQuests(true); });

                detail = "slot=" + expectedSlot + (refreshed ? string.Empty : ";refresh_pending");
                return true;
            }
            catch (Exception e)
            {
                detail = "reset_threw:" + e.GetType().Name + ":" + e.Message;
                return false;
            }
        }

        private static bool Refresh(string stage, Action action)
        {
            try { action(); return true; }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CampaignTuning.LogPrefix + "[WARNING] 清空已落盘，刷新待重试 " + stage + ":" + e.Message);
                return false;
            }
        }
    }
}
#endif
