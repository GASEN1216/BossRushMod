using System;
using System.Collections.Generic;
using UnityEngine;
namespace UnityEngine.SceneManagement {
 internal struct Scene {internal string name;}
 internal static class SceneManager {internal static string Current="Arena";internal static Scene GetActiveScene(){return new Scene{name=Current};}}
}
namespace BossRush {
 internal static class Team {internal static bool IsEnemy(Teams a,Teams b){return a!=b;}}
 internal static class PetNestCompanionAgent {internal static CharacterMainControl Pet;internal static bool IsCompanionCharacter(CharacterMainControl value){return value==Pet;}}
 internal partial class ValidationIdentityProbe {
  private const string BaseSceneName="Base";
  internal static CharacterMainControl[] Bodies;
  private static T[] FindObjectsOfType<T>(){return (T[])(object)Bodies;}
  internal static bool Hostile(CharacterMainControl target,CharacterMainControl player){return IsAutotestHostileWithin(target,player,100f);}
 }
 internal partial class ModeERuntimeModule {
  private static void RunTeamRegression(){
   BossRushCoop.IsConnected=true;BossRushCoop.IsHost=true;BossRushCoop.RunId="team-room";BossRushCoop.Players.Clear();
   CharacterMainControl.Main=new CharacterMainControl {Team=Teams.player};
   var host=new BossRushCoopPlayer {Id=BossRushCoop.LocalPlayerId,Character=CharacterMainControl.Main};
   var first=new BossRushCoopPlayer {Id="first",Character=new CharacterMainControl {Team=Teams.wolf}};
   var second=new BossRushCoopPlayer {Id="second",Character=new CharacterMainControl {Team=Teams.player}};
   BossRushCoop.Players.AddRange(new[]{host,first,second});
   var e=new ModeERuntimeModule();e.modeEPlayerFaction=Teams.scav;e.BeginCoopModeEParticipants();
   CharacterMainControl.Main.SetTeam(Teams.scav);e.SetCoopModeEPlayerTeam(first.Character,Teams.scav);e.SetCoopModeEPlayerTeam(second.Character,Teams.scav);
   e.SetCoopModeEPlayerTeam(first.Character,Teams.scav);
   var npc=new CharacterMainControl {Team=Teams.scav};e.SetCoopModeEPlayerTeam(npc,Teams.player);
   Check(npc.Team==Teams.scav&&e.coopModeEPlayerTeams.Count==3,"only actual three player identities acquire original team leases; NPC is untouched");
   Check(e.RestoreCoopModeEPlayerTeams()&&CharacterMainControl.Main.Team==Teams.player&&first.Character.Team==Teams.wolf&&second.Character.Team==Teams.player,"host End helper restores actual remote bodies and their original distinct teams");
   Check(!e.RestoreCoopModeEPlayerTeams(),"team restoration consumes ownership exactly once");
   e.SetCoopModeEPlayerTeam(first.Character,Teams.scav);var oldBody=first.Character;first.Character=new CharacterMainControl {Team=Teams.wolf};e.RestoreCoopModeEPlayerTeams();
   Check(first.Character.Team==Teams.wolf&&oldBody.Team==Teams.wolf,"replacement player body is not mutated while the unbound original body regains its original team");
   e.SetCoopModeEPlayerTeam(first.Character,Teams.scav);var unbound=first.Character;first.Character=null;e.RestoreCoopModeEPlayerTeams();
   Check(unbound.Team==Teams.wolf&&e.coopModeEPlayerTeams.Count==0,"temporary connected roster unbind still restores the original live body");first.Character=unbound;
   e.SetCoopModeEPlayerTeam(first.Character,Teams.scav);first.Character=null;var intruder=new BossRushCoopPlayer {Id="new-owner",Character=unbound};BossRushCoop.Players.Add(intruder);e.RestoreCoopModeEPlayerTeams();
   Check(unbound.Team==Teams.scav&&e.coopModeEPlayerTeams.Count==0,"original body reassigned to a different nonempty player identity is not modified");BossRushCoop.Players.Remove(intruder);first.Character=unbound;
   e.SetCoopModeEPlayerTeam(first.Character,Teams.scav);BossRushCoop.RunId="new-room";e.RestoreCoopModeEPlayerTeams();
   Check(first.Character.Team==Teams.scav&&e.coopModeEPlayerTeams.Count==0,"old room cannot change current room player team");
   BossRushCoop.RunId="team-room";first.Character.Team=Teams.wolf;e.SetCoopModeEPlayerTeam(first.Character,Teams.scav);CharacterMainControl.Main.Team=Teams.wolf;e.BeginCoopModeEParticipants();CharacterMainControl.Main.SetTeam(Teams.scav);
   var oldLocalId=BossRushCoop.LocalPlayerId;BossRushCoop.IsConnected=false;BossRushCoop.RunId=string.Empty;BossRushCoop.LocalPlayerId="local";BossRushCoop.Players.Clear();e.RestoreCoopModeEPlayerTeams();
   Check(first.Character.Team==Teams.wolf&&CharacterMainControl.Main.Team==Teams.wolf,"native Stop reset clears roster and resets LocalId but restores the exact original remote and local bodies");
   BossRushCoop.IsConnected=true;BossRushCoop.RunId="team-room";BossRushCoop.LocalPlayerId=oldLocalId;BossRushCoop.Players.AddRange(new[]{host,first,second});e.SetCoopModeEPlayerTeam(first.Character,Teams.scav);first.Character.Team=Teams.player;e.RestoreCoopModeEPlayerTeams();
   Check(first.Character.Team==Teams.player,"external team change is not overwritten by retired mode");
   first.Character.Team=Teams.wolf;e.SetCoopModeEPlayerTeam(first.Character,Teams.scav);first.Character.ThrowTeam=true;e.RestoreCoopModeEPlayerTeams();
   Check(e.coopModeEPlayerTeams.Count==1&&first.Character.Team==Teams.scav,"failed original team restore retains exact body debt");
   e.modeEPlayerFaction=Teams.player;first.Character.ThrowTeam=false;e.RestoreCoopModeEPlayerTeams();Check(e.coopModeEPlayerTeams.Count==0&&first.Character.Team==Teams.wolf,"subsequent production helper retry restores original body after shared mode faction reset");
   CharacterMainControl.Main.Team=Teams.wolf;e.modeEPlayerFaction=Teams.scav;e.BeginCoopModeEParticipants();CharacterMainControl.Main.SetTeam(Teams.scav);CharacterMainControl.Main.ThrowTeam=true;e.RestoreCoopModeEPlayerTeams();
   e.modeEPlayerFaction=Teams.player;CharacterMainControl.Main.ThrowTeam=false;e.RestoreCoopModeEPlayerTeams();Check(CharacterMainControl.Main.Team==Teams.wolf&&e.coopModeEPlayerTeams.Count==0,"local original startup team freezes across failed End and later shared mode reset");
   e.SetCoopModeEPlayerTeam(second.Character,Teams.scav);UnityEngine.Object.Destroy(second.Character.gameObject);e.RestoreCoopModeEPlayerTeams();
   Check(second.Character==null&&e.coopModeEPlayerTeams.Count==0,"destroyed Unity GameObject destroys its body and releases team lease without writing another body");
   first.Character.Team=Teams.scav;second.Character=new CharacterMainControl {Team=Teams.scav};CharacterMainControl.Main.Team=Teams.scav;
   var pet=new CharacterMainControl {Team=Teams.scav};PetNestCompanionAgent.Pet=pet;
   ValidationIdentityProbe.Bodies=new[]{CharacterMainControl.Main,first.Character,second.Character,npc,pet};
   var probe=new ValidationIdentityProbe();string details;
   Check(probe.ValidationCountHostileCharacters(out details)==1,"actual F3 hostile count excludes both identified remote scav players and preserves hostile NPC");
   Check(!ValidationIdentityProbe.Hostile(first.Character,CharacterMainControl.Main)&&!ValidationIdentityProbe.Hostile(second.Character,CharacterMainControl.Main),"actual autotest clear target predicate never treats identified remote players as enemies");
   Check(ValidationIdentityProbe.Hostile(npc,CharacterMainControl.Main),"actual autotest predicate retains real hostile NPC");
   BossRushCoop.Players.Remove(first);
   Check(probe.ValidationCountHostileCharacters(out details)==2&&ValidationIdentityProbe.Hostile(first.Character,CharacterMainControl.Main),"unknown former body is not given a name or team whitelist exemption");
   UnityEngine.SceneManagement.SceneManager.Current="Base";Check(probe.ValidationCountHostileCharacters(out details)==0,"actual F3 Base boundary remains unchanged");
   UnityEngine.SceneManagement.SceneManager.Current="Arena";PetNestCompanionAgent.Pet=null;BossRushCoop.Players.Clear();
  }
 }
}
