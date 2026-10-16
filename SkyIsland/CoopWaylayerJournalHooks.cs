using System;
using HarmonyLib;
using ItemStatsSystem;
using ItemStatsSystem.Data;
using Saves;

namespace BossRush
{
    /// <summary>只限定原生角色树的同步加载调用域；不包装 UniTask，不钩异步状态机。</summary>
    [HarmonyPatch(typeof(ItemSavesUtilities), "LoadItem", new Type[] { typeof(string) })]
    internal static class CoopWaylayerLoadScopeHook
    {
        [ThreadStatic] internal static CoopWaylayerJournal Loading;
        internal sealed class Scope
        {
            internal CoopWaylayerJournal Previous, Temporary;
        }
        [HarmonyPrefix]
        private static void Prefix(string key, out Scope __state)
        {
            __state = new Scope { Previous = Loading };
            Loading = null;
            if (key != "MainCharacterItemData") return;
            Loading = CoopWaylayerJournal.Current;
            // 原生首载可能先于 runtime 的 OnAwake；临时 owner 只覆盖本次同步加载调用域。
            if (Loading == null) Loading = __state.Temporary = new CoopWaylayerJournal();
        }
        [HarmonyFinalizer]
        private static void Finalizer(Scope __state)
        {
            if (__state == null) return;
            if (__state.Temporary != null) __state.Temporary.Dispose();
            Loading = __state.Previous;
        }
    }
    [HarmonyPatch(typeof(ItemTreeData), "InstantiateAsync", new Type[] { typeof(ItemTreeData) })]
    internal static class CoopWaylayerLoadTreeHook
    {
        [HarmonyPrefix]
        private static void Prefix(ref ItemTreeData __0)
        {
            CoopWaylayerJournal journal = CoopWaylayerLoadScopeHook.Loading;
            if (journal != null) __0 = journal.PrepareLoad(__0);
        }
    }
    [HarmonyPatch(typeof(ItemSavesUtilities), "Save", new Type[] { typeof(Item), typeof(string) })]
    internal static class CoopWaylayerSaveTreeHook
    {
        [HarmonyPrefix]
        private static void Prefix(Item item, string key)
        {
            CharacterMainControl player = CharacterMainControl.Main;
            if (key == "MainCharacterItemData" && player != null && ReferenceEquals(player.CharacterItem, item)
                && CoopWaylayerJournal.Current != null) CoopWaylayerJournal.Current.BeforeNativeSave(item);
        }
    }
}
