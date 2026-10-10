using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public static bool operator ==(Object left, Object right)
        {
            bool leftNull = ReferenceEquals(left, null) || left.Destroyed;
            bool rightNull = ReferenceEquals(right, null) || right.Destroyed;
            return leftNull || rightNull ? leftNull == rightNull : ReferenceEquals(left, right);
        }
        public static bool operator !=(Object left, Object right) { return !(left == right); }
        public override bool Equals(object value) { return ReferenceEquals(this, value); }
        public override int GetHashCode() { return base.GetHashCode(); }
        public static void Destroy(Object value)
        {
            if (ReferenceEquals(value, null)) return;
            value.Destroyed = true;
            GameObject gameObject = value as GameObject;
            if (ReferenceEquals(gameObject, null)) return;
            foreach (Component component in gameObject.Components) component.Destroyed = true;
        }
    }
    public sealed class GameObject : Object
    {
        internal readonly List<Component> Components = new List<Component>();
    }
    public class Component : Object
    {
        public GameObject gameObject;
        protected Component()
        {
            gameObject = new GameObject();
            gameObject.Components.Add(this);
        }
    }
    public sealed class Transform : Component
    {
        internal Transform parent;
        public bool IsChildOf(Transform ancestor)
        {
            for (Transform cursor = this; cursor != null; cursor = cursor.parent)
                if (cursor == ancestor) return true;
            return false;
        }
    }
}

namespace BossRush
{
    internal sealed class Item : UnityEngine.Component
    {
        internal int TypeID = 500001;
        internal object InInventory;
        internal object PluggedIntoSlot;
        internal bool Detached;
        internal void Detach() { Detached = true; }
        internal void DestroyTree() { UnityEngine.Object.Destroy(gameObject); }
    }

    internal sealed class StockShop : UnityEngine.Component
    {
        internal static event Action<StockShop, Item> OnItemPurchased;
        internal static event Action<StockShop, Item, int> OnItemSoldByPlayer;
        internal static int PurchaseListeners { get { return OnItemPurchased == null ? 0 : OnItemPurchased.GetInvocationList().Length; } }
        internal static int SellListeners { get { return OnItemSoldByPlayer == null ? 0 : OnItemSoldByPlayer.GetInvocationList().Length; } }
        internal readonly List<Entry> entries = new List<Entry>();
        internal sealed class Entry
        {
            internal int ItemTypeID = 500001;
            internal int CurrentStock;
            internal bool Show;
        }
    }

    internal class ManagedUIElement : UnityEngine.Component
    {
        internal static event Action<ManagedUIElement> onClose;
        internal static int CloseListeners { get { return onClose == null ? 0 : onClose.GetInvocationList().Length; } }
        internal bool open;
        protected void NotifyClose() { if (onClose != null) onClose(this); }
    }

    internal sealed class StockShopView : ManagedUIElement
    {
        internal static StockShopView Instance;
        internal StockShop Target;
        internal int Refreshes;
        internal int Closes;
        internal bool InputBlocked;
        internal Action Closing;
        internal void Close()
        {
            open = false;
            Closes++;
            if (Closing != null) Closing();
            NotifyClose();
            InputBlocked = false;
        }
        private void SetupAndShow(StockShop shop) { Refreshes++; }
    }

    internal sealed class ShopController
    {
        internal int Farewells;
        internal void EndDialogueWithStay(float seconds, bool farewell) { Farewells++; }
    }

    internal sealed class Cost
    {
        internal static int Cash;
        private readonly int price;
        internal Cost(int price) { this.price = price; }
        internal bool Enough { get { return Cash >= price; } }
        internal void Pay(bool a, bool b) { Cash -= price; }
    }

    internal static class ItemUtilities
    {
        internal static Item Returned;
        internal static void SendToPlayer(Item item, bool a, bool b) { Returned = item; }
    }

    internal static class NotificationText
    {
        internal static readonly List<string> Messages = new List<string>();
        internal static void Push(string text) { Messages.Add(text); }
    }

    internal static class L10n
    {
        internal static string T(string chinese, string english) { return chinese; }
    }

    internal static class ModBehaviour
    {
        internal static void DevLog(string message) { }
    }
}
