using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using ItemStatsSystem;
using ItemStatsSystem.Items;

namespace BossRush
{
    /// <summary>Only the native synchronous snapshot builder may assemble fresh world ticket trees.</summary>
    internal static class CoopWaylayerNativeBuildScope
    {
        private sealed class Frame
        {
            internal Frame Previous;
            internal Item Root, CompletedChild;
            internal int Epoch;
            internal bool ContainsWorldTicket;
        }
        private sealed class CorpseFrame
        {
            internal Item Root;
            internal InteractableLootbox Box;
            internal Inventory Inventory;
            internal readonly HashSet<Item> Children = new HashSet<Item>();
            internal int Epoch;
        }
        private sealed class Admission : IDisposable
        {
            internal Frame Frame;
            internal Item Item;
            internal Inventory Inventory;
            internal Slot Slot;
            internal bool Consumed;
            private readonly Admission previous;
            internal Admission(Frame frame, Item item, Inventory inventory, Slot slot)
            { Frame = frame; Item = item; Inventory = inventory; Slot = slot; previous = admission; admission = this; }
            public void Dispose() { admission = previous; }
        }
        private sealed class Owner : IDisposable
        {
            private bool disposed;
            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                if (--owners != 0) return;
                epoch++; frame = null; admission = null; corpse = null; ticketRoots = new ConditionalWeakTable<Item, object>();
                if (patch != null) patch.UnpatchAll(patch.Id);
                patch = null; disabled = false;
            }
        }
        private const BindingFlags Flags = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        [ThreadStatic] private static Frame frame;
        [ThreadStatic] private static Admission admission;
        [ThreadStatic] private static CorpseFrame corpse;
        private static Harmony patch;
        private static int owners, epoch;
        private static bool disabled;
        private static ConditionalWeakTable<Item, object> ticketRoots = new ConditionalWeakTable<Item, object>();

        internal static void RegisterTicketRoot(Item item)
        { if (owners > 0 && item != null) ticketRoots.GetValue(item, key => new object()); }

        internal static IDisposable Acquire()
        {
            if (owners++ == 0) epoch++;
            EnsureInstalled();
            return new Owner();
        }
        internal static void EnsureInstalled()
        {
            if (owners == 0 || patch != null || disabled) return;
            Harmony attempt = null;
            try
            {
                // No hard reference or GetType(true): ordinary single-player remains usable without COOP.
                Type tool = Type.GetType("EscapeFromDuckovCoopMod.ItemTool, EscapeFromDuckovCoopMod", false);
                if (tool == null) return;
                MethodInfo target = null;
                foreach (MethodInfo candidate in tool.GetMethods(Flags))
                    if (candidate.Name == "BuildItemFromSnapshot" && candidate.GetParameters().Length == 1
                        && candidate.GetParameters()[0].ParameterType.FullName == "EscapeFromDuckovCoopMod.ItemSnapshot")
                    { if (target != null) throw new InvalidOperationException("Ambiguous native snapshot builder"); target = candidate; }
                if (target == null || !target.IsStatic || target.ReturnType != typeof(Item))
                    throw new InvalidOperationException("Expected the synchronous native Item-returning snapshot builder");
                attempt = new Harmony("BossRush.Coop.WaylayerNativeBuild");
                attempt.Patch(target,
                    prefix: new HarmonyMethod(typeof(CoopWaylayerNativeBuildScope), "BeforeBuild"),
                    postfix: new HarmonyMethod(typeof(CoopWaylayerNativeBuildScope), "AfterBuild"),
                    transpiler: new HarmonyMethod(typeof(CoopWaylayerNativeBuildScope), "BuildCalls"),
                    finalizer: new HarmonyMethod(typeof(CoopWaylayerNativeBuildScope), "FinishBuild"));
                MethodInfo create = typeof(InteractableLootbox).GetMethod("CreateFromItem", Flags, null,
                    new[] { typeof(Item), typeof(UnityEngine.Vector3), typeof(UnityEngine.Quaternion), typeof(bool), typeof(InteractableLootbox), typeof(bool) }, null);
                if (create == null) throw new InvalidOperationException("Native corpse builder signature changed");
                attempt.Patch(create,
                    prefix: new HarmonyMethod(typeof(CoopWaylayerNativeBuildScope), "BeforeCorpse"),
                    transpiler: new HarmonyMethod(typeof(CoopWaylayerNativeBuildScope), "CorpseCalls"),
                    finalizer: new HarmonyMethod(typeof(CoopWaylayerNativeBuildScope), "FinishCorpse"));
                patch = attempt;
            }
            catch (Exception error)
            {
                // A changed native method closes this small permission seam, not PatchAll.
                try { if (attempt != null) attempt.UnpatchAll(attempt.Id); }
                finally
                {
                    disabled = true; frame = null; admission = null; corpse = null;
                    ModBehaviour.CriticalLog("coop-waylayer-native-build", "[BossRush Coop] Waylayer world-ticket assembly is unavailable: " + error.Message);
                }
            }
        }
        private static void BeforeBuild(out Frame __state)
        { __state = new Frame { Previous = frame, Epoch = epoch }; frame = __state; }
        private static void BindRoot(Item item)
        {
            Frame current = frame;
            if (current != null && current.Epoch == epoch && IsFreshWorldRoot(item)) current.Root = item;
        }
        private static void AfterBuild(Item __result, Frame __state)
        {
            if (__state == null || __state.Epoch != epoch || owners == 0 || !ReferenceEquals(__state.Root, __result)) return;
            bool ticket = __state.ContainsWorldTicket || CoopWaylayerClaims.IsWorldTicket(__result);
            if (ticket) RegisterTicketRoot(__result);
            if (__state.Previous != null)
            { __state.Previous.CompletedChild = __result; __state.Previous.ContainsWorldTicket |= ticket; }
        }
        private static void FinishBuild(Frame __state)
        { frame = owners > 0 && __state != null && __state.Epoch == epoch ? __state.Previous : null; }
        private static bool IsFreshWorldRoot(Item item)
        {
            if (item == null || item.IsBeingDestroyed || item.ParentObject != null || item.ParentItem != null || item.ActiveAgent != null) return false;
            return !IsPlayerTree(item);
        }
        private static bool IsPlayerTree(Item item)
        {
            CharacterMainControl main = CharacterMainControl.Main;
            for (Item current = item; current != null; current = current.ParentItem)
            {
                if (main != null && ReferenceEquals(main.CharacterItem, current)) return true;
                foreach (BossRushCoopPlayer player in BossRushCoop.Players)
                    if (player.Character != null && ReferenceEquals(player.Character.CharacterItem, current)) return true;
            }
            return false;
        }
        private static bool ChildForCurrentRoot(Item item)
        {
            Frame current = frame;
            if (current == null || current.Epoch != epoch || owners == 0 || !ReferenceEquals(current.CompletedChild, item)
                || !IsFreshWorldRoot(current.Root) || !IsFreshWorldRoot(item)) return false;
            current.CompletedChild = null;
            return true;
        }
        private static bool AddBuiltChild(Inventory inventory, Item item, int position)
        {
            Frame current = frame;
            if (current != null && current.Root != null && inventory != null && ReferenceEquals(inventory, current.Root.Inventory)
                && ReferenceEquals(inventory.AttachedToItem, current.Root) && ChildForCurrentRoot(item))
                using (new Admission(current, item, inventory, null)) return inventory.AddAt(item, position);
            return inventory.AddAt(item, position);
        }
        private static bool PlugBuiltChild(Slot slot, Item item, out Item unplugged)
        {
            Frame current = frame;
            if (current != null && slot != null && ReferenceEquals(slot.Master, current.Root) && ChildForCurrentRoot(item))
                using (new Admission(current, item, null, slot)) return slot.Plug(item, out unplugged);
            return slot.Plug(item, out unplugged);
        }
        internal static bool AllowInventory(Item item, Inventory inventory, bool mutation)
        {
            Admission scope = admission;
            if (!mutation || scope == null || scope.Consumed || owners == 0 || !ReferenceEquals(scope.Frame, frame)
                || scope.Frame.Epoch != epoch || !ReferenceEquals(scope.Item, item) || !ReferenceEquals(scope.Inventory, inventory)
                || !IsFreshWorldRoot(scope.Frame.Root)) return false;
            scope.Consumed = true;
            return true;
        }
        internal static bool AllowSlot(Item item, Slot slot)
        {
            Admission scope = admission;
            if (scope == null || scope.Consumed || owners == 0 || !ReferenceEquals(scope.Frame, frame) || scope.Frame.Epoch != epoch
                || !ReferenceEquals(scope.Item, item) || !ReferenceEquals(scope.Slot, slot) || !IsFreshWorldRoot(scope.Frame.Root)) return false;
            scope.Consumed = true;
            return true;
        }
        private static void BeforeCorpse(Item item, out CorpseFrame __state)
        {
            // Hide any outer permission even for ordinary nested corpse creation, without allocation or scans.
            __state = corpse; corpse = null;
            object tracked;
            if (owners == 0 || item == null || !ticketRoots.TryGetValue(item, out tracked) || IsPlayerTree(item)) return;
            corpse = new CorpseFrame { Root = item, Epoch = epoch };
            if (item.Inventory != null) foreach (Item child in item.Inventory)
                if (child != null && ReferenceEquals(child.ParentItem, item) && CoopWaylayerClaims.IsWorldTicket(child)) corpse.Children.Add(child);
            if (item.Slots != null) foreach (Slot slot in item.Slots)
                if (slot.Content != null && ReferenceEquals(slot.Content.ParentItem, item) && CoopWaylayerClaims.IsWorldTicket(slot.Content)) corpse.Children.Add(slot.Content);
        }
        private static void BindCorpseBox(InteractableLootbox box)
        {
            CorpseFrame current = corpse;
            Inventory inventory = box != null ? box.Inventory : null;
            if (current == null || current.Epoch != epoch || owners == 0 || current.Children.Count == 0 || inventory == null
                || inventory.AttachedToItem != null || !ReferenceEquals(inventory.gameObject, box.gameObject)) return;
            foreach (Item existing in inventory) if (existing != null) return;
            current.Box = box; current.Inventory = inventory;
        }
        private static bool AddCorpseChild(Inventory inventory, Item item, int position)
        {
            // The unchanged native merge path handles all ordinary loot.
            if (!CoopWaylayerClaims.IsWorldTicket(item)) return ItemUtilities.AddAndMerge(inventory, item, position);
            CorpseFrame current = corpse;
            if (current == null || current.Epoch != epoch || owners == 0 || current.Root == null || IsPlayerTree(current.Root)
                || current.Box == null || !ReferenceEquals(current.Box.Inventory, inventory) || !ReferenceEquals(current.Inventory, inventory)
                || inventory.AttachedToItem != null || !ReferenceEquals(inventory.gameObject, current.Box.gameObject)
                || item == null || item.ParentObject != null || item.ActiveAgent != null || !current.Children.Remove(item)) return false;
            // AddItem delegates once to AddAt. Its exact one-shot permission is consumed before notifications.
            using (CoopWaylayerClaims.Admit(item, inventory))
            {
                try { return inventory.AddItem(item); }
                finally { CoopWaylayerClaims.TrackCorpse(item, inventory); }
            }
        }
        private static void FinishCorpse(CorpseFrame __state)
        { corpse = owners > 0 && __state != null && __state.Epoch == epoch ? __state : null; }
        private static IEnumerable<CodeInstruction> CorpseCalls(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo create = typeof(InteractableLootbox).GetMethod("CreateLocalInventory", Flags);
            MethodInfo merge = typeof(ItemUtilities).GetMethod("AddAndMerge", new[] { typeof(Inventory), typeof(Item), typeof(int) });
            var result = new List<CodeInstruction>();
            int boxes = 0, additions = 0;
            foreach (CodeInstruction original in instructions)
            {
                CodeInstruction copy = new CodeInstruction(original);
                if ((copy.opcode == OpCodes.Call || copy.opcode == OpCodes.Callvirt) && Equals(copy.operand, create))
                {
                    var duplicate = new CodeInstruction(OpCodes.Dup);
                    duplicate.labels.AddRange(copy.labels); copy.labels.Clear();
                    duplicate.blocks.AddRange(copy.blocks); copy.blocks.Clear();
                    result.Add(duplicate); result.Add(copy);
                    result.Add(new CodeInstruction(OpCodes.Call, typeof(CoopWaylayerNativeBuildScope).GetMethod("BindCorpseBox", Flags)));
                    boxes++; continue;
                }
                if ((copy.opcode == OpCodes.Call || copy.opcode == OpCodes.Callvirt) && Equals(copy.operand, merge))
                { copy.opcode = OpCodes.Call; copy.operand = typeof(CoopWaylayerNativeBuildScope).GetMethod("AddCorpseChild", Flags); additions++; }
                result.Add(copy);
            }
            if (boxes != 1 || additions != 1)
                throw new InvalidOperationException("Native corpse transfer sites changed (box=" + boxes + ", inventory=" + additions + ")");
            return result;
        }
        private static IEnumerable<CodeInstruction> BuildCalls(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions);
            MethodInfo add = typeof(Inventory).GetMethod("AddAt", new[] { typeof(Item), typeof(int) });
            MethodInfo plug = typeof(Slot).GetMethod("Plug", new[] { typeof(Item), typeof(Item).MakeByRefType() });
            int roots = 0, additions = 0, plugs = 0;
            var result = new List<CodeInstruction>(code.Count + 2);
            for (int i = 0; i < code.Count; i++)
            {
                CodeInstruction original = code[i], copy = new CodeInstruction(original);
                MethodInfo call = original.operand as MethodInfo;
                if ((original.opcode == OpCodes.Call || original.opcode == OpCodes.Callvirt) && call == add)
                { copy.opcode = OpCodes.Call; copy.operand = typeof(CoopWaylayerNativeBuildScope).GetMethod("AddBuiltChild", Flags); additions++; }
                if ((original.opcode == OpCodes.Call || original.opcode == OpCodes.Callvirt) && call == plug)
                { copy.opcode = OpCodes.Call; copy.operand = typeof(CoopWaylayerNativeBuildScope).GetMethod("PlugBuiltChild", Flags); plugs++; }
                result.Add(copy);
                MethodInfo previous = i > 0 ? code[i - 1].operand as MethodInfo : null;
                if (original.opcode == OpCodes.Stloc_0 && previous != null && previous.Name == "GetResult" && previous.ReturnType == typeof(Item)
                    && previous.DeclaringType.FullName.StartsWith("Cysharp.Threading.Tasks.UniTask", StringComparison.Ordinal))
                { result.Add(new CodeInstruction(OpCodes.Ldloc_0)); result.Add(new CodeInstruction(OpCodes.Call, typeof(CoopWaylayerNativeBuildScope).GetMethod("BindRoot", Flags))); roots++; }
            }
            if (roots != 1 || additions != 1 || plugs != 1)
                throw new InvalidOperationException("Native snapshot assembly sites changed (root=" + roots + ", inventory=" + additions + ", slot=" + plugs + ")");
            return result;
        }
    }
}
