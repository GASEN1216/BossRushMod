using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace BossRush
{
    /// <summary>Native ModApi stores byte-array length in UInt16; preserve the complete envelope across bounded frames.</summary>
    internal sealed class BossRushCoopPacketFrames
    {
        internal const int MaxMessageBytes = 262144;
        internal const int MaxFrameBytes = 60000;
        private const int HeaderBytes = 60;
        private const int PayloadBytes = MaxFrameBytes - HeaderBytes;
        private const int MaxPendingPerPeer = 64;
        private readonly Dictionary<object, Dictionary<Guid, Assembly>> pending = new Dictionary<object, Dictionary<Guid, Assembly>>();
        private float nextPrune;

        private sealed class Assembly
        {
            internal int Length, Received;
            internal byte[] Hash;
            internal byte[][] Parts;
            internal float Expires;
        }

        internal static bool Send(byte[] message, Func<byte[], bool> send)
        {
            if (message == null || message.Length == 0 || message.Length > MaxMessageBytes || send == null) return false;
            if (message.Length <= MaxFrameBytes) return send(message);
            byte[] id = Guid.NewGuid().ToByteArray();
            byte[] hash;
            using (var sha = SHA256.Create()) hash = sha.ComputeHash(message);
            int count = (message.Length + PayloadBytes - 1) / PayloadBytes;
            for (int index = 0; index < count; index++)
            {
                int offset = index * PayloadBytes;
                int size = Math.Min(PayloadBytes, message.Length - offset);
                var frame = new byte[HeaderBytes + size];
                frame[0] = (byte)'B'; frame[1] = (byte)'R'; frame[2] = (byte)'F'; frame[3] = 1;
                Buffer.BlockCopy(id, 0, frame, 4, 16);
                WriteInt(frame, 20, message.Length); WriteInt(frame, 24, index);
                Buffer.BlockCopy(hash, 0, frame, 28, 32);
                Buffer.BlockCopy(message, offset, frame, HeaderBytes, size);
                if (!send(frame)) return false;
            }
            return true;
        }

        internal static bool IsFrame(byte[] bytes)
        {
            return bytes != null && bytes.Length >= 4 && bytes[0] == 'B' && bytes[1] == 'R' && bytes[2] == 'F' && bytes[3] == 1;
        }

        internal bool Receive(object peer, byte[] frame, float now, out byte[] message)
        {
            message = null;
            if (peer == null || frame == null || frame.Length == 0 || frame.Length > MaxFrameBytes) return false;
            if (!IsFrame(frame)) { message = frame; return true; }
            if (frame.Length <= HeaderBytes) return false;
            int length = ReadInt(frame, 20), index = ReadInt(frame, 24);
            if (length <= MaxFrameBytes || length > MaxMessageBytes) return false;
            int count = (length + PayloadBytes - 1) / PayloadBytes;
            if (index < 0 || index >= count || frame.Length != HeaderBytes + Math.Min(PayloadBytes, length - index * PayloadBytes)) return false;
            Prune(now);
            var idBytes = new byte[16]; Buffer.BlockCopy(frame, 4, idBytes, 0, 16);
            Guid id = new Guid(idBytes);
            Dictionary<Guid, Assembly> entries;
            if (!pending.TryGetValue(peer, out entries)) pending.Add(peer, entries = new Dictionary<Guid, Assembly>());
            Assembly value;
            if (!entries.TryGetValue(id, out value))
            {
                if (entries.Count >= MaxPendingPerPeer) return false;
                value = new Assembly { Length = length, Parts = new byte[count][], Hash = new byte[32], Expires = now + 30f };
                Buffer.BlockCopy(frame, 28, value.Hash, 0, 32);
                entries.Add(id, value);
            }
            if (value.Length != length || !Equal(value.Hash, 0, frame, 28, 32)) { entries.Remove(id); return false; }
            int size = frame.Length - HeaderBytes;
            if (value.Parts[index] != null)
            {
                if (!Equal(value.Parts[index], 0, frame, HeaderBytes, size)) entries.Remove(id);
                return false;
            }
            var part = new byte[size]; Buffer.BlockCopy(frame, HeaderBytes, part, 0, size);
            value.Parts[index] = part;
            if (++value.Received != count) return false;
            entries.Remove(id);
            if (entries.Count == 0) pending.Remove(peer);
            var complete = new byte[length];
            for (int i = 0; i < count; i++) Buffer.BlockCopy(value.Parts[i], 0, complete, i * PayloadBytes, value.Parts[i].Length);
            byte[] actual;
            using (var sha = SHA256.Create()) actual = sha.ComputeHash(complete);
            if (!Equal(actual, 0, value.Hash, 0, 32)) return false;
            message = complete;
            return true;
        }

        internal void Prune(float now)
        {
            if (now < nextPrune) return;
            nextPrune = now + 1f;
            var empty = new List<object>();
            foreach (var peer in pending)
            {
                var expired = new List<Guid>();
                foreach (var entry in peer.Value) if (entry.Value.Expires <= now) expired.Add(entry.Key);
                foreach (Guid id in expired) peer.Value.Remove(id);
                if (peer.Value.Count == 0) empty.Add(peer.Key);
            }
            foreach (object peer in empty) pending.Remove(peer);
        }
        internal void Remove(object peer) { if (peer != null) pending.Remove(peer); }
        internal void Clear() { pending.Clear(); nextPrune = 0f; }

        private static bool Equal(byte[] a, int offsetA, byte[] b, int offsetB, int count)
        {
            if (offsetA + count > a.Length || offsetB + count > b.Length) return false;
            for (int i = 0; i < count; i++) if (a[offsetA + i] != b[offsetB + i]) return false;
            return true;
        }
        private static void WriteInt(byte[] bytes, int offset, int value)
        {
            for (int i = 0; i < 4; i++) bytes[offset + i] = (byte)(value >> (8 * i));
        }
        private static int ReadInt(byte[] bytes, int offset)
        {
            return bytes[offset] | bytes[offset + 1] << 8 | bytes[offset + 2] << 16 | bytes[offset + 3] << 24;
        }
    }
}
