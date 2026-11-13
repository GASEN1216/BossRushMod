using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    // The runner inserts the production OnYardSound. These objects supply only its scene and audio inputs.
    internal sealed partial class LastShiftEnemies
    {
        private const float SquadGunHearing = 30f;
        private enum SquadState { Dormant, Alert }

        private sealed class SquadSite { internal Vector3 Focus; }

        private sealed class Squad
        {
            internal SquadSite Site;
            internal readonly List<Guard> Members = new List<Guard>();
            internal SquadState State;
            internal bool Heard, HeardPlayer;
            internal Teams PendingTeam;
            internal float PendingHearing;
        }

        private sealed class Guard
        {
            internal CharacterMainControl Character;
            internal AICharacterController Ai;
            internal Vector3 Position;
            internal bool Failed, Spawning;
        }

        private bool closed;
        private readonly List<Squad> squads = new List<Squad>();
        private CharacterMainControl player;
        private readonly List<Guard> queue = new List<Guard>();

        internal static void TestDormantHearing(Action<bool, string> check)
        {
            var owner = new LastShiftEnemies { player = new CharacterMainControl { Team = Teams.player } };
            var member = new Guard { Character = new CharacterMainControl { Team = Teams.scav }, Ai = new AICharacterController() };
            member.Character.transform.position = new Vector3(5f, 0f, 0f);
            member.Ai.hearingAbility = 1.5f;
            var camp = new Squad { State = SquadState.Dormant, Site = new SquadSite { Focus = new Vector3(0f, 0f, 0f) } };
            owner.squads.Add(camp);
            camp.Members.Add(member);

            var sound = new AISound { pos = new Vector3(19.9f, 25f, 0f), radius = 10f, fromTeam = Teams.player,
                fromCharacter = owner.player, soundType = SoundTypes.unknowNoise };
            owner.OnYardSound(sound);
            check(camp.Heard && camp.HeardPlayer, "hearing uses member position and hearing ability beyond the camp center cutoff");

            camp.Heard = false;
            sound.pos = new Vector3(20f, 25f, 0f);
            owner.OnYardSound(sound);
            check(!camp.Heard, "hearing excludes a sound exactly on the official radius");

            sound.pos = new Vector3(5.1f, 0f, 0f);
            sound.fromTeam = Teams.scav;
            sound.fromCharacter = null;
            owner.OnYardSound(sound);
            check(!camp.Heard, "friendly footsteps do not wake a squad");
            sound.soundType = SoundTypes.alert;
            owner.OnYardSound(sound);
            check(camp.Heard && !camp.HeardPlayer, "friendly official alert wakes without attributing it to the player");

            camp.Heard = false;
            sound.pos = new Vector3(36f, 0f, 0f);
            sound.radius = 50f;
            sound.fromTeam = Teams.player;
            sound.soundType = SoundTypes.combatSound;
            owner.OnYardSound(sound);
            check(!camp.Heard, "combat sound respects the 30 m squad cap");

            camp.Members.Clear();
            var queued = new Guard { Position = new Vector3(8f, 0f, 0f) };
            camp.Members.Add(queued);
            camp.PendingTeam = Teams.scav;
            camp.PendingHearing = 1.5f;
            owner.queue.Add(queued);
            sound.pos = new Vector3(22.9f, 0f, 0f);
            sound.radius = 10f;
            sound.soundType = SoundTypes.unknowNoise;
            owner.OnYardSound(sound);
            check(camp.Heard, "queued member hears from its planned position with preset hearing");
            camp.Heard = false;
            queued.Failed = true;
            owner.OnYardSound(sound);
            check(!camp.Heard, "failed member cannot hear from its planned position");

            // 2026-11-13 起守货岗可以同时开好几队：每队围坐的分别判，醒着的交给官方 AI。
            var far = new Squad { State = SquadState.Dormant, Site = new SquadSite { Focus = new Vector3(100f, 0f, 0f) } };
            var sentry = new Guard { Character = new CharacterMainControl { Team = Teams.scav }, Ai = new AICharacterController() };
            sentry.Character.transform.position = new Vector3(100f, 0f, 0f);
            sentry.Ai.hearingAbility = 1.5f;
            far.Members.Add(sentry);
            owner.squads.Add(far);
            sound = new AISound { pos = new Vector3(105f, 0f, 0f), radius = 10f, fromTeam = Teams.player,
                fromCharacter = owner.player, soundType = SoundTypes.unknowNoise };
            owner.OnYardSound(sound);
            check(far.Heard && !camp.Heard, "each dormant garrison hears on its own: the far camp wakes, the near one does not");
            far.Heard = false;
            far.State = SquadState.Alert;
            owner.OnYardSound(sound);
            check(!far.Heard, "an awake garrison leaves hearing to the official AI");
        }
    }
}

namespace UnityEngine
{
    internal struct Vector3
    {
        internal float x, y, z;
        internal Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        internal float sqrMagnitude { get { return x * x + y * y + z * z; } }
        public static Vector3 operator -(Vector3 a, Vector3 b) { return new Vector3(a.x - b.x, a.y - b.y, a.z - b.z); }
    }

    internal static class Mathf { internal static float Min(float a, float b) { return Math.Min(a, b); } }
}

internal enum Teams { player, scav }
internal enum SoundTypes { unknowNoise, combatSound, grenadeDropSound, alert }
internal sealed class Health { internal bool IsDead; }
internal sealed class GameObject { internal bool activeInHierarchy = true; }
internal sealed class Transform { internal Vector3 position; }
internal sealed class AICharacterController { internal float hearingAbility; }

internal sealed class CharacterMainControl
{
    internal Teams Team;
    internal readonly Health Health = new Health();
    internal readonly GameObject gameObject = new GameObject();
    internal readonly Transform transform = new Transform();
}

internal struct AISound
{
    internal Vector3 pos;
    internal float radius;
    internal Teams fromTeam;
    internal SoundTypes soundType;
    internal CharacterMainControl fromCharacter;
}
