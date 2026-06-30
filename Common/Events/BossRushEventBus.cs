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

        private static Delegate[] publishScratch = new Delegate[8];
        private static int publishDepth = 0;

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

            int count = subscribers.Count;
            Delegate[] snapshot = publishDepth == 0
                ? CopySubscribersToSharedScratch(subscribers, count)
                : subscribers.ToArray();

            publishDepth++;
            try
            {
                for (int i = 0; i < count; i++)
                {
                    Action<TEvent> handler = snapshot[i] as Action<TEvent>;
                    if (publishDepth == 1)
                    {
                        snapshot[i] = null;
                    }

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
            finally
            {
                publishDepth--;
                if (publishDepth == 0)
                {
                    int clearCount = Math.Min(count, snapshot.Length);
                    for (int i = 0; i < clearCount; i++)
                    {
                        snapshot[i] = null;
                    }
                }
            }
        }

        private static Delegate[] CopySubscribersToSharedScratch(List<Delegate> subscribers, int count)
        {
            if (publishScratch.Length < count)
            {
                publishScratch = new Delegate[count * 2];
            }

            subscribers.CopyTo(publishScratch, 0);
            return publishScratch;
        }

        public static void ClearRuntimeSubscribers()
        {
            runtimeSubscribers.Clear();
        }

        public static void ResetStaticCaches()
        {
            ClearRuntimeSubscribers();
            publishScratch = new Delegate[8];
            publishDepth = 0;
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
