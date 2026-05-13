using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    public partial class ModBehaviour
    {
        private static void ResetDebugAndToolsStaticCaches()
        {
            if (placementPreviewObject != null)
            {
                try
                {
                    UnityEngine.Object.Destroy(placementPreviewObject);
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[BossRush] [WARNING] 销毁放置预览对象失败: " + e.Message);
                }

                placementPreviewObject = null;
            }

            if (outlineMaterial != null)
            {
                try
                {
                    UnityEngine.Object.Destroy(outlineMaterial);
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[BossRush] [WARNING] 销毁调试描边材质失败: " + e.Message);
                }

                outlineMaterial = null;
            }

            RestorePendingDeleteMaterialsForDebugReset();

            interactDebugListenerRegistered = false;
            shootDebugListenerRegistered = false;
            lastNearestGameObject = null;
            placementModeActive = false;
            placementRotationY = 0f;
            pendingDeleteObject = null;
            currentPrefabIndex = 0;
            prefabListInitialized = false;
            cachedDisplayField = null;

            originalMaterials.Clear();
            pendingDeleteOriginalMaterials.Clear();
            nearbyPrefabList.Clear();
        }

        private static void RestorePendingDeleteMaterialsForDebugReset()
        {
            if (pendingDeleteObject == null)
            {
                return;
            }

            foreach (KeyValuePair<Renderer, Material[]> kvp in pendingDeleteOriginalMaterials)
            {
                Renderer renderer = kvp.Key;
                Material[] originalMats = kvp.Value;
                if (renderer != null && originalMats != null)
                {
                    renderer.sharedMaterials = originalMats;
                }
            }
        }
    }
}
