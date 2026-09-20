using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ItemStatsSystem;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Duckov.UI;

namespace UnityEngine
{
    public class Object
    {
        public bool destroyed;
        public static bool operator ==(Object a, Object b)
        {
            bool an = ReferenceEquals(a, null) || a.destroyed;
            bool bn = ReferenceEquals(b, null) || b.destroyed;
            return an || bn ? an == bn : ReferenceEquals(a, b);
        }
        public static bool operator !=(Object a, Object b) { return !(a == b); }
        public override bool Equals(object other) { return this == other as Object; }
        public override int GetHashCode() { return base.GetHashCode(); }
        public static void Destroy(Object obj)
        {
            if (ReferenceEquals(obj, null)) return;
            obj.destroyed = true;
            var go = obj as GameObject;
            if (go != null) foreach (var child in go.components) Destroy(child);
        }
    }
    public class GameObject : UnityEngine.Object
    {
        public bool activeSelf;
        public readonly List<Object> components = new List<Object>();
        public void SetActive(bool value) { activeSelf = value; }
        public T[] GetComponentsInChildren<T>(bool all) { return components.OfType<T>().ToArray(); }
    }
    public sealed class Coroutine { public IEnumerator body; public bool stopped; }
    public struct Color { public static Color white = new Color(); }
}
namespace TMPro
{
    public class TextMeshProUGUI : UnityEngine.Object { public string text; public Color color; public GameObject gameObject = new GameObject(); }
}
namespace UnityEngine.UI
{
    public class Button : UnityEngine.Object
    {
        public GameObject gameObject = new GameObject();
        public bool interactable;
        public T[] GetComponentsInChildren<T>(bool all) { return gameObject.GetComponentsInChildren<T>(all); }
    }
}
namespace ItemStatsSystem
{
    public class Item : UnityEngine.Object
    {
        public string DisplayName;
        public bool eligible;
        public bool decomposable;
        public bool locked;
    }
}
namespace Duckov.UI
{
    public static class ItemUIUtilities { public static Item SelectedItem; }
    public class ItemDecomposeView : UnityEngine.Object
    {
        private GameObject cannotDecomposeIndicator;
        public bool open;
        public ItemDecomposeView(GameObject indicator) { cannotDecomposeIndicator = indicator; }
    }
}
namespace BossRush
{
    internal enum ForgeUIMode { Reforge, AffixForge }
    public sealed class ModBehaviour
    {
        public static ModBehaviour Instance;
        public bool IsZombieModeTemporaryRealNpc(object controller) { return false; }
        public readonly List<Coroutine> work = new List<Coroutine>();
        public Coroutine StartCoroutine(IEnumerator body)
        {
            var task = new Coroutine { body = body };
            if (body.MoveNext()) work.Add(task);
            return task;
        }
        public void StopCoroutine(Coroutine task) { task.stopped = true; work.Remove(task); }
        public void NextFrame()
        {
            foreach (var task in work.ToArray())
                if (task.stopped || !task.body.MoveNext()) work.Remove(task);
        }
        public static void DevLog(string message) { throw new Exception(message); }
    }
    public static class L10n
    {
        public static bool English;
        public static string T(string cn, string en) { return English ? en : cn; }
    }
    public struct AffixSlotView { public bool Locked; }
    public static class AffixItemData
    {
        public static bool TryReadSlot(Item item, int slot, out AffixSlotView view)
        { view = new AffixSlotView { Locked = item.locked }; return true; }
    }
    public static class AffixForgeSystem
    {
        public static int Stones;
        public static bool CanAffixForge(Item item) { return item != null && item.eligible; }
        public static int GetSlotCount(Item item) { return 1; }
        public static int GetOwnedStoneCount() { return Stones; }
        public static int GetStoneCost(Item item) { return 1; }
        public static long GetMoneyCost(Item item) { return 100; }
    }
    public static class ReforgeSystem
    {
        public static int QueryCount;
        public static int GetDiscountedCost(Item item) { QueryCount++; return 2000; }
        public static float GetCurrentDiscount() { return 0f; }
    }
    public static partial class ReforgeUIManager
    {
        private static ForgeUIMode currentForgeMode;
        private static bool isReforgeMode, affixForging, affixEntryPending;
        private static Item selectedItem;
        private static Coroutine affixSelectionRefreshCoroutine;
        private static ItemDecomposeView decomposeView;
        private static FieldInfo _cannotDecomposeField;
        private static Button reforgeButton;
        private static TextMeshProUGUI targetNameDisplay, probabilityText, affixStoneCountText;
        private static GameObject noItemSelectedIndicator, resultDisplayObj, affixPanelRoot, affixStoneContainer, affixHiddenMoneySliderRoot;
        private static readonly List<object> affixRows = new List<object>();
        private static GameObject cannotIndicator;
        private static long money;
        private static int currentMoney;
        private static object currentController;
        private static int GetTendencyCost() { return 0; }
        private static long GetPlayerMoney() { return money; }
        private static void HideReforgeOnlyWidgets() { }
        // 2026-09-20：延时刷新新增的整块面板重建入口（幂等替身，只记次数）
        internal static int AffixPanelBuilds;
        internal static int AffixPanelRefreshes;
        private static void BuildAffixPanel() { AffixPanelBuilds++; }
        private static void RefreshAffixPanel() { AffixPanelRefreshes++; }
        private static void UpdateAffixStoneCount() { }
        private static void UpdateAffixProbabilityText() { }
        internal static int PanelRefreshes { get { return AffixPanelRefreshes; } }
        internal static int PanelBuilds { get { return AffixPanelBuilds; } }

        public static void ResetHarness(bool english)
        {
            AffixPanelBuilds = 0; AffixPanelRefreshes = 0;
            ModBehaviour.Instance = new ModBehaviour();
            currentForgeMode = ForgeUIMode.AffixForge; isReforgeMode = true;
            affixSelectionRefreshCoroutine = null; selectedItem = null; _cannotDecomposeField = null;
            reforgeButton = new Button(); reforgeButton.gameObject.components.Add(new TextMeshProUGUI());
            cannotIndicator = new GameObject(); cannotIndicator.components.Add(new TextMeshProUGUI());
            noItemSelectedIndicator = new GameObject(); noItemSelectedIndicator.components.Add(new TextMeshProUGUI());
            resultDisplayObj = new GameObject(); probabilityText = new TextMeshProUGUI(); targetNameDisplay = new TextMeshProUGUI();
            decomposeView = new ItemDecomposeView(cannotIndicator) { open = true };
            L10n.English = english; money = 1000; AffixForgeSystem.Stones = 10;
            currentMoney = 0; currentController = null; ReforgeSystem.QueryCount = 0;
        }
        public static void Select(Item item, bool vanillaLast)
        {
            ItemUIUtilities.SelectedItem = item;
            if (!vanillaLast) VanillaSetup(item);
            selectedItem = item;
            AffixForge_HandleSelectionChanged();
            if (vanillaLast) VanillaSetup(item);
        }
        // Host boundary: official Setup/SetupEmpty toggles these objects using DecomposeDatabase.
        private static void VanillaSetup(Item item)
        {
            reforgeButton.gameObject.SetActive(item != null && item.decomposable);
            cannotIndicator.SetActive(item != null && !item.decomposable);
            noItemSelectedIndicator.SetActive(item == null);
        }
        public static void SetBudget(long cash, int stones) { money = cash; AffixForgeSystem.Stones = stones; }
        public static bool ButtonVisible { get { return reforgeButton.gameObject.activeSelf; } }
        public static bool Clickable { get { return reforgeButton.interactable; } }
        public static bool CannotVisible { get { return cannotIndicator.activeSelf; } }
        public static bool EmptyVisible { get { return noItemSelectedIndicator.activeSelf; } }
        public static string ButtonLabel { get { return reforgeButton.GetComponentsInChildren<TextMeshProUGUI>(true)[0].text; } }
        public static string CannotLabel { get { return cannotIndicator.GetComponentsInChildren<TextMeshProUGUI>(true)[0].text; } }
        public static int Pending { get { return ModBehaviour.Instance.work.Count; } }
        public static void Frame() { ModBehaviour.Instance.NextFrame(); }
        public static void RefreshSharedButton() { UpdateReforgeButtonInteractable(); }
        public static void CloseHarness() { decomposeView.open = false; }
        public static void SwitchToReforge() { currentForgeMode = ForgeUIMode.Reforge; }
        public static void DestroyView() { UnityEngine.Object.Destroy(decomposeView); }
    }
}
