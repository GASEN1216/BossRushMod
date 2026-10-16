using System;
using System.Linq;
using BossRush;
using UnityEngine;

partial class Program
{
    static void SceneGenerationBinding()
    {
        var client = Start(false);
        var native = BossRushCoopTransport.Current;
        object upstream = new object();
        BossRushCoopTransport.NativeHost = upstream;
        BossRushCoopTransport.NativePeers.Add(upstream);
        Scene(801, "Base");
        native.Packet(upstream, "$welcome", Welcome(new BossRushCoopWelcome { helloSequence = 1, session = "travel-room", playerId = "me", generation = 30 }));
        Check(client.Generation == 0 && !client.HasBoundScene, "welcome alone cannot publish a new world generation");
        var entries = new[] { new LifeEntry { id = "me", nativeId = "native-local", connected = true, alive = true },
            new LifeEntry { id = "host", nativeId = "host-body", assetId = Id(), connected = true, alive = true } };
        BossRushCoopTransport.RemoteCharacters["host-body"] = new CharacterMainControl();
        long revision = 0;
        Action<int, string> roster = (generation, token) => native.Packet(upstream, "$roster", new LifeRoster {
            generation = generation, revision = ++revision, sceneId = "Base", sceneToken = token, ready = true, players = entries }, "travel-room");
        roster(30, "");
        Check(client.Generation == 0 && !client.HasBoundScene, "missing token cannot bind even the first same-name scene");
        roster(30, BossRushCoopTransport.Token(30)); roster(30, BossRushCoopTransport.Token(30));
        var remote = client.Players.Single(p => p.Id == "host");
        Check(client.Generation == 30 && remote.IsAlive, "initial authenticated token binds a truly stable late-join Core");
        client.MarkSceneReady("old-core"); native.Sent.Clear();
        roster(31, BossRushCoopTransport.Token(31));
        Check(client.Generation == 30 && !client.HasBoundScene && !remote.IsAlive, "future roster retires life without exposing its world generation");
        roster(31, BossRushCoopTransport.Token(31));
        Check(client.Generation == 30 && !client.HasBoundScene, "repeated roster is not a native load event");
        Scene(802, "Base", 32); client.OnUpdate(0, 0);
        client.MarkSceneReady("actual-core-32");
        roster(31, BossRushCoopTransport.Token(31));
        Check(client.Generation == 30 && !client.HasBoundScene && ReadyPackets(native) == 0, "pending 31 cannot bind actual same-name Core 32 or consume its ready key");
        roster(32, BossRushCoopTransport.Token(32));
        Check(client.Generation == 32 && client.HasBoundScene && !remote.IsAlive, "later matching 32 binds the already loaded Core without requiring an extra reload");
        Check(ReadyPackets(native) == 1 && !native.Sent.Any(x => x.Contains("old-core")), "only the actual Core 32 preparation survives the skipped generation");
        roster(32, BossRushCoopTransport.Token(32));
        Check(remote.IsAlive, "subsequent complete roster restores remote participation after continuous same-map travel");
        roster(31, BossRushCoopTransport.Token(31));
        Check(client.Generation == 32 && remote.IsAlive, "late lower generation cannot roll back a bound world or clear current life");
        roster(32, BossRushCoopTransport.Token(31));
        Check(!remote.IsAlive, "same generation with wrong native token cannot certify life");
        roster(32, BossRushCoopTransport.Token(32));
        Check(remote.IsAlive, "correct next snapshot recovers from a wrong-token snapshot");
        native.BeginLoad(33, "Base");
        Check(!client.HasBoundScene && !remote.IsAlive, "real native Begin invalidates the old bound Core before it is physically replaced");
        CompleteNativeCore(803, "Base"); client.OnUpdate(0, 0);
        roster(33, BossRushCoopTransport.Token(33)); roster(33, BossRushCoopTransport.Token(33));
        roster(35, BossRushCoopTransport.Token(35));
        Scene(804, "Base", 34); client.OnUpdate(0, 0);
        roster(34, BossRushCoopTransport.Token(34));
        Check(client.Generation == 33 && !client.HasBoundScene, "lower pending roster cannot assign future 35 to the genuinely loaded Core 34");
        string replacementRoom = BossRushCoopTransport.Room("replacement-before-welcome");
        native.BeginLoad(1, "Base", replacementRoom); CompleteNativeCore(805, "Base");
        Time.unscaledTime = 3; client.OnUpdate(3, 3);
        Check(client.Generation == 33 && !client.HasBoundScene, "new-room native Core cannot publish while old-room welcome remains authoritative");
        native.Packet(upstream, "$welcome", Welcome(new BossRushCoopWelcome { helloSequence = HelloSequence(client), session = replacementRoom, playerId = "replacement-me", generation = 1 }));
        native.Packet(upstream, "$roster", new BossRushCoopRoster { generation = 1, sceneId = "Base", sceneToken = BossRushCoopTransport.Token(35), revision = 1, ready = true,
            players = new[] { new BossRushCoopRosterEntry { id = "replacement-me", connected = true } } }, replacementRoom);
        Check(client.Generation == 0 && !client.HasBoundScene, "new-room native history cannot fall back to adopting the first wrong roster token");
        native.Packet(upstream, "$roster", new BossRushCoopRoster { generation = 1, sceneId = "Base", revision = 2, ready = true,
            players = new[] { new BossRushCoopRosterEntry { id = "replacement-me", connected = true } } }, replacementRoom);
        Check(client.Generation == 1 && client.HasBoundScene && client.Players.Count == 1, "same native service room replacement preserves only exact new native travel and clears pending old generation");
        client.OnDestroy(); Duckov.Scenes.SceneLoader.IsSceneLoading = false;

        // Native scene events may arrive before the runtime observes NetworkStarted,
        // and before the queued welcome is delivered. Execute that ordering directly.
        client = Start(false); native = BossRushCoopTransport.Current;
        upstream = new object(); BossRushCoopTransport.NativeHost = upstream; BossRushCoopTransport.NativePeers.Add(upstream);
        Scene(811, "Base");
        BossRushCoopTransport.Started = false; client.OnUpdate(0, 0);
        BossRushCoopTransport.Started = true;
        string newRoom = BossRushCoopTransport.Room("native-before-runtime");
        native.BeginLoad(4, "Base", newRoom);
        CompleteNativeCore(812, "Base");
        client.OnUpdate(0, 0);
        Check(!client.HasBoundScene && client.Generation == 0, "early native load stays unpublished until the exact room welcome");
        native.Packet(upstream, "$welcome", Welcome(new BossRushCoopWelcome { helloSequence = HelloSequence(client), session = newRoom, playerId = "me", generation = 4 }));
        native.Packet(upstream, "$roster", new BossRushCoopRoster { generation = 4, sceneId = "Base", sceneToken = BossRushCoopTransport.Token(3), revision = 1, ready = true,
            players = new[] { new BossRushCoopRosterEntry { id = "me", connected = true } } }, newRoom);
        Check(client.Generation == 0 && !client.HasBoundScene, "early native history prevents first-update reset from adopting the wrong roster token");
        native.Packet(upstream, "$roster", new BossRushCoopRoster { generation = 4, sceneId = "Base", revision = 2, ready = true,
            players = new[] { new BossRushCoopRosterEntry { id = "me", connected = true } } }, newRoom);
        Check(client.Generation == 4 && client.HasBoundScene, "exact accepted welcome preserves the genuine native load preceding the runtime reset");
        client.OnDestroy();

        var host = Start(true); Scene(821, "Base");
        var core = Duckov.Scenes.MultiSceneCore.Instance; core.IsLoading = true; host.OnUpdate(0, 0);
        Check(host.Generation == 0 && !host.HasBoundScene, "host scene identity does not commit an initializing Core");
        core.IsLoading = false; host.OnUpdate(0, 0);
        Check(host.Generation == 1 && host.HasBoundScene, "same handle commits once when the actual host Core finishes initialization");
        BossRushCoopTransport.Current.BeginLoad(2, "Base");
        Check(!host.HasBoundScene, "host native travel closes world access before its new Core exists");
        host.OnDestroy(); Duckov.Scenes.SceneLoader.IsSceneLoading = false;
    }

    static void CompleteNativeCore(int handle, string id)
    {
        var previous = Duckov.Scenes.MultiSceneCore.Instance;
        if (previous != null)
        {
            UnityEngine.SceneManagement.SceneManager.Unload(previous.gameObject.scene);
            UnityEngine.Object.Destroy(previous); UnityEngine.Object.Destroy(previous.gameObject);
        }
        Duckov.Scenes.MultiSceneCore.Instance = new Duckov.Scenes.MultiSceneCore { SceneInfo = new Duckov.Scenes.SceneInfo { ID = id },
            gameObject = new GameObject { scene = new UnityEngine.SceneManagement.Scene { handle = handle, isLoaded = true } } };
        Duckov.Scenes.SceneLoader.IsSceneLoading = false;
    }
}
