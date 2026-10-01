using System;
using Duckov.Utilities;

namespace BossRush
{
    /// <summary>
    /// 离岛去向与「这一趟暂不入档的永久记录」（点灯、放生、纪念品手记）的去留。主文件有行数预算，判据放这里。
    ///
    /// CR-2026-09-30-002：旧判据只看岛场景是否随卸载离开（`raid_unloaded`），而从暂停菜单回主菜单同样会卸载岛场景，
    /// 官方此时不存背包（材料回到出击前），记录却被保留——灯白点、料没扣。现在只认两条官方会存背包的离岛路：
    /// 玩家自己撤离（<c>returnRequested</c>）与倒下（<c>deathPending</c>）；去向是主菜单的一律不留。
    /// </summary>
    internal sealed partial class SkyIslandSession
    {
        private bool KeepsRaidHeldRecords(string reason)
        {
            if (reason != "raid_unloaded" || !(returnRequested || deathPending)) return false;
            return !IsMainMenuScene(departureScene);
        }

        private static bool IsMainMenuScene(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName)) return false;
            try
            {
                string menu = GameplayDataSettings.SceneManagement.MainMenuScene.Name;
                return !string.IsNullOrEmpty(menu) && string.Equals(sceneName, menu, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception) { return false; }
        }
    }
}
