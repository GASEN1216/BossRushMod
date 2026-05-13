using Duckov.UI;
using UnityEngine;

namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        private WishFountainView wishFountainView;

        public void OpenWishFountainUI()
        {
            EnsureWishFountainView();
            if (wishFountainView == null)
            {
                DevLog("[WishFountain] 无法创建原版风格许愿面板");
                return;
            }

            wishFountainView.ResetAndOpen();
            DevLog("[WishFountain] 许愿 View 已打开");
        }

        private void EnsureWishFountainView()
        {
            if (wishFountainView != null)
            {
                return;
            }

            Transform parent = GameplayUIManager.Instance != null ? GameplayUIManager.Instance.transform : null;
            if (parent == null)
            {
                DevLog("[WishFountain] GameplayUIManager 不存在，无法创建许愿面板");
                return;
            }

            wishFountainView = WishFountainView.CreateRuntime(parent);
        }
    }
}
