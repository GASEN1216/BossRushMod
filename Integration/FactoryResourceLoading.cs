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

        /// <summary>
        /// alreadyLoaded：消费方自己还持有这个 bundle（基地建筑每次进基地都会重跑装配）时直接交给消费方，
        /// 不再 LoadFromFileAsync——同一文件二次加载会被 Unity 拒绝并报 "another AssetBundle with the same files
        /// is already loaded"（2026-09-22 实机每次进基地四条）。判据与消费方「已注入，跳过」的持有字段同源。
        /// </summary>
        internal static IEnumerator RunSpecial(ModBehaviour owner, string relativePath, Action consume, Func<bool> alreadyLoaded = null)
        {
            string path = Path.Combine(ModBehaviour.GetModPath(), relativePath);
            Action guardedConsume = () =>
            {
                try { consume(); }
                catch (Exception e) { Debug.LogWarning("[BossRushResources] Register " + relativePath + ": " + e.Message); }
            };
            if (alreadyLoaded != null && alreadyLoaded())
            {
                guardedConsume();
                yield break;
            }
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
