using System;
using System.Reflection;
using BossRush;
using EscapeFromDuckovCoopMod;

internal static class TransportWriterCases
{
    internal static void Run(Action<bool, string> check, Func<BossRushCoopTransport> start)
    {
        ProbeConnection(check, start);
        using (var owner = start())
        {
            ModNetworkApi.Writers.Clear(); ModNetworkApi.Payloads.Clear();
            ModNetworkApi.SendAccepted = true;
            for (int i = 0; i < 256; i++)
            {
                byte[] bytes = BitConverter.GetBytes(i);
                check(owner.Send(null, bytes), "client sends its actual distinct payload");
                check(BitConverter.ToInt32(ModNetworkApi.Payloads[i], 0) == i, "cached native callback does not reuse the prior payload");
            }
            var callback = ModNetworkApi.Writers[0];
            check(ModNetworkApi.Writers.TrueForAll(value => ReferenceEquals(value, callback)), "256 actual native sends reuse one writer delegate instead of compiling per message");
            NetService.Instance.IsServer = true;
            CoopTool.Wire.Clear();
            check(owner.BroadcastCompatibilityNotice("玩家 A 缺少 BossRush / Player A: BossRush handshake missing"), "host uses native RPC for peers without BossRush");
            var chat = (ChatMessageRpc)CoopTool.Wire[0];
            check(chat.SenderId == "BossRush" && chat.SenderName == "BossRush" && chat.Content.Contains("Player A"),
                "native compatibility notice identifies automated sender and affected player");
            NetService.Instance.IsServer = false;
            check(!owner.BroadcastCompatibilityNotice("unauthorized") && CoopTool.Wire.Count == 1, "client cannot broadcast compatibility chat as host");
            NetService.Instance.IsServer = true;
            check(owner.Send(new object(), new byte[] { 7 }), "host direct send returns actual native acceptance");
            check(owner.Send(null, new byte[] { 8 }, true), "host broadcast uses actual callback");
            var marker = typeof(BossRushCoopTransport).GetMethod("SendSceneIdentityMarker", BindingFlags.NonPublic | BindingFlags.Instance);
            marker.Invoke(owner, new object[] { new byte[] { 9 } });
            check(ReferenceEquals(callback, ModNetworkApi.Writers[256]) && ReferenceEquals(callback, ModNetworkApi.Writers[257])
                && ReferenceEquals(callback, ModNetworkApi.Writers[258]), "host direct, broadcast and scene marker share the bound writer");
            check(ModNetworkApi.Payloads[256][0] == 7 && ModNetworkApi.Payloads[257][0] == 8 && ModNetworkApi.Payloads[258][0] == 9,
                "distinct host routes retain exact current bytes");
            ModNetworkApi.BeforeWrite = () => owner.Send(new object(), new byte[] { 22 });
            owner.Send(new object(), new byte[] { 11 });
            check(ModNetworkApi.Payloads[259][0] == 22 && ModNetworkApi.Payloads[260][0] == 11, "reentrant send restores outer payload before native write");
            ModNetworkApi.ThrowAfterWrite = true;
            bool failed = false;
            try { owner.Send(new object(), new byte[] { 33 }); } catch (TargetInvocationException) { failed = true; }
            ModNetworkApi.ThrowAfterWrite = false;
            check(failed, "native failure is not converted into successful send");
            bool outside = false;
            try { callback(new Writer()); } catch (InvalidOperationException) { outside = true; }
            check(outside, "failed send releases outgoing payload instead of retaining it for later callbacks");
            owner.Send(new object(), new byte[] { 44 });
            check(ModNetworkApi.Payloads[ModNetworkApi.Payloads.Count - 1][0] == 44, "send after native failure uses new payload");
            ModNetworkApi.SendAccepted = false;
            check(!owner.Send(new object(), new byte[] { 55 }), "native rejection remains false");
            owner.Dispose();
            check(!owner.Send(new object(), new byte[] { 66 }), "disposed transport does not send or resurrect cached writer");
        }
        ModNetworkApi.Writers.Clear(); ModNetworkApi.Payloads.Clear();
        ModNetworkApi.SendAccepted = false;
    }

    private sealed class UnknownRole
    {
        public static UnknownRole Instance = new UnknownRole();
        public bool NetworkStarted = true;
    }
    private sealed class UnknownConnection { public static UnknownConnection Instance = new UnknownConnection(); }
    private sealed class UnknownSingleton { }
    private sealed class UnreadableService { public static object Instance { get { throw new InvalidOperationException("native state failed"); } } }
    private sealed class StoppedService { public static object Instance { get { return null; } } }
    private static void ProbeConnection(Action<bool, string> check, Func<BossRushCoopTransport> start)
    {
        using (var owner = start())
        {
            bool started, host;
            check(owner.TryGetNetworkState(out started, out host) && started && !host, "real reflection reads native online client independently of binding");
            NetService.Instance.IsServer = true;
            check(owner.TryGetNetworkState(out started, out host) && started && host, "real reflection recognizes native host");
            owner.Dispose();
            check(owner.TryGetNetworkState(out started, out host) && started && host, "unbound adapter still detects actual native online host");
            var serviceType = typeof(BossRushCoopTransport).GetField("serviceType", BindingFlags.NonPublic | BindingFlags.Instance);
            serviceType.SetValue(owner, typeof(UnknownRole));
            check(!owner.TryGetNetworkState(out started, out host) && started && !host, "renamed role member keeps network active and denies host role");
            UnknownRole.Instance.NetworkStarted = false;
            check(owner.TryGetNetworkState(out started, out host) && !started && !host, "explicit native stopped state preserves offline play despite unknown role");
            UnknownRole.Instance.NetworkStarted = true;
            serviceType.SetValue(owner, typeof(UnknownConnection));
            check(!owner.TryGetNetworkState(out started, out host) && started && !host, "live native instance without connection contract cannot silently grant solo authority");
            serviceType.SetValue(owner, typeof(UnreadableService));
            check(!owner.TryGetNetworkState(out started, out host) && started && !host, "throwing native state is fail-closed");
            serviceType.SetValue(owner, typeof(UnknownSingleton));
            check(!owner.TryGetNetworkState(out started, out host) && started && !host, "renamed native singleton is not mistaken for explicitly absent instance");
            serviceType.SetValue(owner, typeof(StoppedService));
            check(owner.TryGetNetworkState(out started, out host) && !started, "absent native singleton preserves single-player");
        }
    }
}
