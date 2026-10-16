using System;
using System.Reflection;
using HarmonyLib;
using ItemStatsSystem;
using UnityEngine;
using Duckov.UI;
using Duckov.Scenes;
using UnityEngine.SceneManagement;

namespace BossRush
{
    /// <summary>只挂在个人收据生成的独立库存上；普通地图箱不经过此类型。</summary>
    internal sealed class CoopArenaPersonalLoot : MonoBehaviour
    {
        private string room, asset;
        private InteractableLootbox box;
        private bool visible;
        private bool ready;
        private int sceneHandle;
        private Renderer[] renderers;
        private Collider[] colliders;
        private bool[] rendererEnabled, colliderEnabled;
        private static Harmony harmony;
        private static bool hooked;
        private static int markers;
        private static bool releaseRequested;

        internal static bool Attach(InteractableLootbox value)
        {
            if (value == null || value.Inventory == null || string.IsNullOrEmpty(BossRushCoop.RunId)
                || string.IsNullOrEmpty(BossRushCoop.LocalAssetId) || !EnsureNativePrivateHook()) return false;
            CoopArenaPersonalLoot marker = value.Inventory.GetComponent<CoopArenaPersonalLoot>();
            if (marker == null) { marker = value.Inventory.gameObject.AddComponent<CoopArenaPersonalLoot>(); markers++; }
            releaseRequested = false;
            marker.room = BossRushCoop.RunId; marker.asset = BossRushCoop.LocalAssetId;
            marker.box = value; marker.visible = value.gameObject.activeSelf;
            marker.ready = false;
            marker.sceneHandle = SceneManager.GetActiveScene().handle;
            // 未交付的原物由同一个收据箱保存，过图不能只留下已扣余额。
            UnityEngine.Object.DontDestroyOnLoad(value.gameObject);
            marker.renderers = value.GetComponentsInChildren<Renderer>(true);
            marker.colliders = value.GetComponentsInChildren<Collider>(true);
            marker.rendererEnabled = Array.ConvertAll(marker.renderers, renderer => renderer != null && renderer.enabled);
            marker.colliderEnabled = Array.ConvertAll(marker.colliders, collider => collider != null && collider.enabled);
            return true;
        }

        internal static bool TryPublish(InteractableLootbox value)
        {
            CoopArenaPersonalLoot marker = value != null && value.Inventory != null ? value.Inventory.GetComponent<CoopArenaPersonalLoot>() : null;
            if (marker == null || !BossRushCoop.IsConnected || !BossRushCoop.HasBoundScene
                || marker.room != BossRushCoop.RunId || marker.asset != BossRushCoop.LocalAssetId) return false;
            if (marker.ready) return true;
            Scene scene = SceneManager.GetActiveScene();
            if (marker.sceneHandle != scene.handle)
            {
                CharacterMainControl player = CharacterMainControl.Main;
                LevelManager manager = LevelManager.Instance;
                if (SceneLoader.IsSceneLoading || manager == null || !LevelManager.AfterInit || player == null
                    || manager.MainCharacter != player || !player.gameObject.activeInHierarchy
                    || player.Health == null || player.Health.IsDead) return false;
                Vector3 landing;
                if (!SpawnPositionHelper.TrySnapToGround(player.transform.position, out landing)) return false;
                value.transform.position = landing;
            }
            MultiSceneCore.MoveToActiveWithScene(value.gameObject, scene.buildIndex);
            marker.ready = true;
            return true;
        }

        internal static bool IsPersonal(Inventory inventory)
        {
            return inventory != null && inventory.GetComponent<CoopArenaPersonalLoot>() != null;
        }

        private static bool NativePrivateInventory(Inventory __0, ref bool __result)
        {
            if (!IsPersonal(__0)) return true;
            // 即使正在切槽也不能把原个人箱降为世界共享箱。
            __result = true;
            return false;
        }

        private static bool EnsureNativePrivateHook()
        {
            if (hooked) return true;
            try
            {
                Type type = AccessTools.TypeByName("LootboxDetectUtil");
                MethodInfo target = type != null ? AccessTools.Method(type, "IsPrivateInventory", new[] { typeof(Inventory) }) : null;
                if (target == null) return false;
                harmony = new Harmony("BossRush.Coop.ArenaPersonalLoot");
                harmony.Patch(target, new HarmonyMethod(typeof(CoopArenaPersonalLoot), nameof(NativePrivateInventory)));
                harmony.Patch(AccessTools.Method(typeof(InteractableLootbox), "IsInteractable"), new HarmonyMethod(typeof(CoopArenaPersonalLoot), nameof(BeforeInteract)));
                hooked = true;
                return true;
            }
            catch (Exception e)
            {
                if (harmony != null) harmony.UnpatchAll(harmony.Id);
                harmony = null;
                ModBehaviour.DevLog("[BossRush Coop] Personal loot compatibility binding failed: " + e.Message); return false;
            }
        }

        private void Update()
        {
            if (box == null) return;
            bool current = BossRushCoop.IsConnected && BossRushCoop.RunId == room && BossRushCoop.LocalAssetId == asset;
            if (visible == current) return;
            visible = current;
            if (!current && LootView.Instance != null && LootView.Instance.TargetInventory == GetComponent<Inventory>()) LootView.Instance.Close();
            // 不关闭承载 owner 的 GameObject，回原槽仍能恢复原箱和原物。
            for (int i = 0; i < renderers.Length; i++) if (renderers[i] != null) renderers[i].enabled = current && rendererEnabled[i];
            for (int i = 0; i < colliders.Length; i++) if (colliders[i] != null) colliders[i].enabled = current && colliderEnabled[i];
        }

        private static bool BeforeInteract(InteractableLootbox __instance, ref bool __result)
        {
            CoopArenaPersonalLoot marker = __instance != null && __instance.Inventory != null ? __instance.Inventory.GetComponent<CoopArenaPersonalLoot>() : null;
            if (marker == null || marker.ready && BossRushCoop.IsConnected && marker.room == BossRushCoop.RunId && marker.asset == BossRushCoop.LocalAssetId) return true;
            __result = false;
            return false;
        }

        private void OnDestroy()
        {
            // 没有全局库存注册表；随原 inventory 的实际生命周期释放 marker。
            box = null;
            markers = Math.Max(0, markers - 1);
            ReleaseIfUnused();
        }

        internal static void Shutdown() { releaseRequested = true; ReleaseIfUnused(); }
        private static void ReleaseIfUnused()
        {
            // 仍留在当前图的个人箱在销毁前不能变成共享箱。
            if (!releaseRequested || markers != 0) return;
            if (harmony != null) harmony.UnpatchAll(harmony.Id);
            harmony = null; hooked = false;
        }
    }
}
