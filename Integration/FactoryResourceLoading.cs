using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    internal static class FactoryResourceLoading
    {
        internal static IEnumerator InitializeItems(ModBehaviour owner, Action finish)
        {
            yield return ProductionIconCache.Prepare(() => owner == null);
            yield return RunSpecial(owner, "Assets/bossrush_ticket", () => BossRushDynamicItemRegistry.EnsureRegistered(BossRushItemIds.BossRushTicket));
            if (owner == null) yield break;
            owner.EnsureItemContentConfiguratorsRegisteredForDynamicRegistry();
            yield return ItemFactory.LoadAllItemsAsync(owner);
            if (owner != null) finish();
        }

        internal static IEnumerator RunSpecial(ModBehaviour owner, string relativePath, Action consume)
        {
            string path = Path.Combine(ModBehaviour.GetModPath(), relativePath);
            Action guardedConsume = () =>
            {
                try { consume(); }
                catch (Exception e) { Debug.LogWarning("[BossRushResources] Register " + relativePath + ": " + e.Message); }
            };
            while (owner != null)
            {
                int scene = SceneManager.GetActiveScene().handle;
                Func<bool> cancelled = () => owner == null || SceneManager.GetActiveScene().handle != scene;
                yield return ResourceBundleLoader.Prepare(path, true, cancelled, guardedConsume);
                if (!cancelled()) yield break;
                // A scene transition invalidates this attempt, but not the published content catalog.
                while (owner != null && (SceneLoader.IsSceneLoading || LevelManager.LevelInitializing)) yield return null;
            }
        }

        internal static IEnumerator LoadDirectory(ModBehaviour owner, string relativeDirectory, Func<string, bool> loaded, Func<string, int> load)
        {
            string path = Path.Combine(ModBehaviour.GetModPath(), relativeDirectory);
            if (!Directory.Exists(path)) yield break;
            string[] files = Directory.GetFiles(path);
            // Preserve the same enumeration/registration order as the original factories.
            foreach (string file in files)
            {
                if (owner == null) yield break;
                string name = Path.GetFileName(file);
                if (name.Contains(".") || loaded(name)) continue;
                yield return RunSpecial(owner, relativeDirectory + "/" + name, () => load(name));
                yield return null;
            }
        }
    }

    public static partial class EquipmentFactory
    {
        internal static int LoadedBundleCount { get { return loadedBundles.Count; } }
        internal static IEnumerator LoadAllEquipmentAsync(ModBehaviour owner)
        { return FactoryResourceLoading.LoadDirectory(owner, EQUIPMENT_PATH, loadedBundles.Contains, LoadBundle); }
    }

    public static partial class ItemFactory
    {
        internal static int LoadedItemCount { get { return loadedItems.Count; } }
        internal static IEnumerator LoadAllItemsAsync(ModBehaviour owner)
        { return FactoryResourceLoading.LoadDirectory(owner, ITEMS_PATH, loadedBundles.Contains, LoadBundle); }
    }
}
