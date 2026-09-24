namespace BossRush
{
    /// <summary>保留许愿台旧宿主入口，并转交唯一运行时模块。</summary>
    public partial class ModBehaviour
    {
        public void InitWishFountainBuilding()
        {
            if (WishFountainRuntime != null) WishFountainRuntime.InitWishFountainBuilding();
        }

        internal void TryInitializeWishFountainEarly()
        {
            if (WishFountainRuntime != null) WishFountainRuntime.TryInitializeWishFountainEarly();
        }

        public void CleanupWishFountainBuilding()
        {
            if (WishFountainRuntime != null) WishFountainRuntime.CleanupWishFountainBuilding();
        }

        public void RestoreWishFountainBuildings()
        {
            if (WishFountainRuntime != null) WishFountainRuntime.RestoreWishFountainBuildings();
        }

        public void OpenWishFountainUI()
        {
            if (WishFountainRuntime != null) WishFountainRuntime.OpenWishFountainUI();
        }
    }
}
