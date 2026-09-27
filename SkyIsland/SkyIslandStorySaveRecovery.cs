using UnityEngine;

namespace BossRush
{
    /// <summary>离岛物理保存被官方 IsSaving 推迟时保留唯一 store owner，避免场景销毁吞掉待保存事实。</summary>
    internal sealed class SkyIslandStorySaveRecovery : MonoBehaviour
    {
        private SkyIslandStoryService story;
        private float retryAt;

        internal static bool IsPending()
        {
            // 保存 owner 一律是独立持久对象，与 Mod 宿主无关；仅在入岛检查时扫描，不能漏掉这类 pending。
            foreach (SkyIslandStorySaveRecovery recovery in UnityEngine.Object.FindObjectsOfType<SkyIslandStorySaveRecovery>(true))
                if (recovery.story != null && recovery.story.IsCurrentSlot) return true;
            return false;
        }

        internal static void CloseOrRetain(SkyIslandStoryService value)
        {
            if (value == null || value.TryClose()) return;
            foreach (SkyIslandStorySaveRecovery existing in UnityEngine.Object.FindObjectsOfType<SkyIslandStorySaveRecovery>(true))
                if (ReferenceEquals(existing.story, value)) return;
            // 从第一次移交起就独立于 Mod 宿主；后续禁用 Mod 或重建宿主不会销毁待保存 owner。
            GameObject recoveryRoot = new GameObject("SkyIslandStorySaveRecovery");
            UnityEngine.Object.DontDestroyOnLoad(recoveryRoot);
            var recovery = recoveryRoot.AddComponent<SkyIslandStorySaveRecovery>();
            recovery.story = value;
            recovery.retryAt = Time.unscaledTime + 1f;
            Debug.LogWarning("[SkyIsland] 离岛进度保存推迟，已保留记录并等待官方存档完成。");
        }

        private void Update()
        {
            if (story == null || Time.unscaledTime < retryAt) return;
            retryAt = Time.unscaledTime + 1f;
            if (!story.IsCurrentSlot || story.TryClose())
            {
                story.Close();
                story = null;
                Destroy(gameObject);
            }
        }

        private void OnDestroy()
        {
            if (story != null) story.Close();
            story = null;
        }
    }
}
