using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public sealed class GameObject { public bool Destroyed; }
    public static class Object
    {
        public static void Destroy(GameObject gameObject) { gameObject.Destroyed = true; }
    }
}

namespace BossRush
{
    internal sealed class Item
    {
        internal int TypeID = 500001;
        internal UnityEngine.GameObject gameObject = new UnityEngine.GameObject();
        internal bool Detached;
        internal void Detach() { Detached = true; }
    }

    internal sealed class StockShop
    {
        internal readonly List<Entry> entries = new List<Entry>();
        internal sealed class Entry
        {
            internal int ItemTypeID = 500001;
            internal int CurrentStock;
            internal bool Show;
        }
    }

    internal sealed class StockShopView
    {
        internal static StockShopView Instance = new StockShopView();
        internal int Refreshes;
        private void SetupAndShow(StockShop shop) { Refreshes++; }
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
