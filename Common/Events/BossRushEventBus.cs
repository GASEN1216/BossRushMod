// ============================================================================
// BossRushEventBus.cs - low-risk runtime notification bus
// ============================================================================

using System;
using System.Collections.Generic;

namespace BossRush
{
    internal static class BossRushEventBus
    {
        private static readonly Dictionary<Type, List<Delegate>> runtimeSubscribers =
            new Dictionary<Type, List<Delegate>>();

        public static void Subscribe<TEvent>(Action<TEvent> handler)
        {
            if (handler == null)
            {
                return;
            }

            Type eventType = typeof(TEvent);
            List<Delegate> subscribers;
            if (!runtimeSubscribers.TryGetValue(eventType, out subscribers))
            {
                subscribers = new List<Delegate>();
                runtimeSubscribers[eventType] = subscribers;
            }

            if (!subscribers.Contains(handler))
            {
                subscribers.Add(handler);
            }
        }

        public static void Unsubscribe<TEvent>(Action<TEvent> handler)
        {
            if (handler == null)
            {
                return;
            }

            Type eventType = typeof(TEvent);
            List<Delegate> subscribers;
            if (!runtimeSubscribers.TryGetValue(eventType, out subscribers))
            {
                return;
            }

            subscribers.Remove(handler);
            if (subscribers.Count == 0)
            {
                runtimeSubscribers.Remove(eventType);
            }
        }

        public static void Publish<TEvent>(TEvent eventData)
        {
            Type eventType = typeof(TEvent);
            List<Delegate> subscribers;
            if (!runtimeSubscribers.TryGetValue(eventType, out subscribers) || subscribers.Count == 0)
            {
                return;
            }

            Delegate[] snapshot = subscribers.ToArray();
            for (int i = 0; i < snapshot.Length; i++)
            {
                Action<TEvent> handler = snapshot[i] as Action<TEvent>;
                if (handler == null)
                {
                    continue;
                }

                try
                {
                    handler(eventData);
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[BossRushEventBus] [WARNING] handler failed for "
                        + eventType.Name + ": " + e.Message);
                }
            }
        }

        public static void ClearRuntimeSubscribers()
        {
            runtimeSubscribers.Clear();
        }

        public static void ResetStaticCaches()
        {
            ClearRuntimeSubscribers();
        }
    }

    internal struct BossRushAchievementUnlockedEvent
    {
        public readonly BossRushAchievementDef Achievement;

        public BossRushAchievementUnlockedEvent(BossRushAchievementDef achievement)
        {
            Achievement = achievement;
        }
    }
}
