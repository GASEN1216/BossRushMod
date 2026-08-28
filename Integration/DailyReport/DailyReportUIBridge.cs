// ============================================================================
// DailyReportUIBridge.cs - 日报面板的宿主桥接
// ============================================================================
// 形态照 Integration/WishFountain/WishFountainUIBridge.cs：
// View 在首次需要时创建并常驻，之后只做开合，避免每次交互都重建整套 UI。
// ============================================================================

using Duckov.UI;
using UnityEngine;

namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        private DailyReportView dailyReportView;

        /// <summary>打开《鸭科夫日报》面板。报箱交互与调试菜单都走这里。</summary>
        public void OpenDailyReportUI()
        {
            if (!IsDailyReportConfiguredEnabled())
            {
                DevLog(DailyReportTuning.LogPrefix + "开关已关闭，忽略打开请求");
                return;
            }

            EnsureDailyReportView();
            if (dailyReportView == null)
            {
                DevLog(DailyReportTuning.LogPrefix + "无法创建日报面板");
                return;
            }

            dailyReportView.RefreshAndOpen();
        }

        /// <summary>幂等创建面板实例。</summary>
        private void EnsureDailyReportView()
        {
            if (dailyReportView != null) return;

            Transform parent = GameplayUIManager.Instance != null
                ? GameplayUIManager.Instance.transform
                : null;
            if (parent == null)
            {
                DevLog(DailyReportTuning.LogPrefix + "GameplayUIManager 不存在，无法创建日报面板");
                return;
            }

            dailyReportView = DailyReportView.CreateRuntime(parent);
        }
    }
}
