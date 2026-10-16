using System;
using System.Collections.Generic;
namespace BossRush
{
    // Only the wire is simulated. Registration, receipt serialization, retry,
    // asset checks, application deduplication and ACK removal are production code.
    internal sealed class BossRushCoopMessage
    {
        internal string SenderId, Json;
        internal bool IsFromHost;
        internal T Read<T>() { return BossRushCoopJson.FromJson<T>(Json); }
    }
    internal static partial class BossRushCoop
    {
        internal static int SceneGeneration = 1;
#if !ZOMBIE_DELIVERY
        internal static bool HasWorldAuthority { get { return IsHost; } }
#endif
        internal sealed class Packet { internal string Sender, Target, Topic, Json; }
        private sealed class Subscription : IDisposable
        {
            internal string Owner, Topic; internal Action<BossRushCoopMessage> Receive;
            public void Dispose() { subscriptions.Remove(this); }
        }
        private static readonly List<Subscription> subscriptions = new List<Subscription>();
        internal static readonly List<Packet> Packets = new List<Packet>();
        internal static IDisposable Register(string topic, Action<BossRushCoopMessage> receive)
        {
            var value = new Subscription { Owner = LocalPlayerId, Topic = topic, Receive = receive };
            subscriptions.Add(value); return value;
        }
        internal static bool SendToPlayer<T>(string player, string topic, T value)
        { Packets.Add(new Packet { Sender = LocalPlayerId, Target = player, Topic = topic, Json = BossRushCoopJson.ToJson(value) }); return true; }
        internal static bool SendToHost<T>(string topic, T value) { return SendToPlayer("host", topic, value); }
        internal static void Select(string id)
        {
            LocalPlayerId = id; LocalAssetId = FindPlayer(id).AssetId; IsHost = id == "host";
#if ZOMBIE_DELIVERY
            HasWorldAuthority = IsHost;
#else
            IsClient = !IsHost;
#endif
            CharacterMainControl.Main = FindPlayer(id).Character;
        }
        internal static Packet Take(string target, bool ack)
        {
            int index = Packets.FindIndex(p => p.Target == target && p.Topic.EndsWith(".ack", StringComparison.Ordinal) == ack);
            if (index < 0) throw new Exception("expected real wire packet for " + target + " ACK=" + ack);
            Packet packet = Packets[index]; Packets.RemoveAt(index); return packet;
        }
        internal static void Dispatch(Packet packet)
        {
            Select(packet.Target);
            var message = new BossRushCoopMessage { SenderId = packet.Sender, IsFromHost = packet.Sender == "host", Json = packet.Json };
            foreach (Subscription subscription in new List<Subscription>(subscriptions))
                if (subscription.Owner == packet.Target && subscription.Topic == packet.Topic) subscription.Receive(message);
        }
    }
}
