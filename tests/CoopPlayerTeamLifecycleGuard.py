"""Mode E owns only actual player team changes; Dev threat scans retain real NPCs."""
from pathlib import Path
import re
from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parents[1]

def source(path):
    return clean_source((ROOT / path).read_text(encoding='utf-8-sig'))

def method(text, signature):
    start = text.index(signature)
    end = text.index('{', start) + 1
    depth = 1
    while depth:
        depth += (text[end] == '{') - (text[end] == '}')
        end += 1
    return re.sub(r'\s+', '', text[start:end])

def require(body, text, why):
    assert re.sub(r'\s+', '', text) in body, why

coop = source('ModeE/ModeERuntimeModule_Coop.cs')
remember = method(coop, 'private void RememberCoopModeEPlayerTeam(')
require(remember, '!BossRushCoop.IsPlayer(character)', 'team lease must require actual player identity')
require(remember, 'Original = character.Team', 'capture original team before changing it')
require(remember, 'Room = BossRushCoop.RunId', 'capture original room')
require(remember, 'coopModeEPlayerTeams.ContainsKey(character)', 'repeat snapshots must preserve first original team')
restore = method(coop, 'private bool RestoreCoopModeEPlayerTeams(')
require(restore, 'pair.Value.Room == BossRushCoop.RunId', 'old room cannot alter a new room team')
require(restore, 'if (coopModeEPlayerTeams.Count == 0) return false;', 'inactive empty team owner must return before allocating retry work')
require(restore, 'string currentPlayer = BossRushCoop.IsConnected ? BossRushCoop.GetPlayerId(character) : null;', 'resolve actual connected body owner and avoid native reset local-id fallback')
require(restore, 'string.IsNullOrEmpty(currentPlayer) || currentPlayer == pair.Value.Player', 'unbound original body may restore but a new nonempty player owner must be protected')
require(restore, 'if (!pair.Value.Applied.HasValue) pair.Value.Applied = modeEPlayerFaction;', 'local startup team must freeze before mode state resets')
require(restore, 'character.Team == pair.Value.Applied.Value', 'external team change and failed restore retry must use the frozen applied team')
require(restore, 'character.SetTeam(pair.Value.Original);', 'restore each original team rather than forcing a shared team')
begin = method(coop, 'private void BeginCoopModeEParticipants(')
require(begin, 'RememberCoopModeEPlayerTeam(CharacterMainControl.Main);', 'capture host original team before startup SetTeam')
tick = method(coop, 'private void TickCoopModeE(')
require(tick, 'if (!modeEActive) RestoreCoopModeEPlayerTeams();', 'failed team restoration must retry after mode end')
require(tick, 'if (participant.Admitted) SetCoopModeEPlayerTeam(player.Character, modeEPlayerFaction);', 'host remote faction changes must acquire original body lease')
for signature in ('private void ApplyCoopModeE(', 'private bool ApplyCoopModeEPersonal('):
    require(method(coop, signature), 'SetCoopModeEPlayerTeam(', 'client actual faction changes must acquire original body lease')
end = method(source('ModeE/ModeELifecycle.cs'), 'public void EndModeE(')
require(end, 'bool coopTeamsOwned = RestoreCoopModeEPlayerTeams();', 'actual EndModeE must restore all leased player teams')
require(end, 'if (!coopTeamsOwned && player != null)', 'legacy local reset must not overwrite a restored original co-op team')
count = method(source('DebugAndTools/F3GameplayValidationRunner.cs'), 'internal int ValidationCountHostileCharacters(')
require(count, 'BossRushCoop.IsPlayer(c)', 'F3 hostile count must exclude other actual players even in an enemy faction')
require(count, 'if (!Team.IsEnemy(Teams.player, c.Team)) continue;', 'F3 must retain hostile NPC classification')
target = method(source('DebugAndTools/F3GameplayValidationAutotestActions.cs'), 'private static bool IsAutotestHostileWithin(')
require(target, 'BossRushCoop.IsPlayer(character)', 'autotest combat must never choose another actual player')
require(target, '!Team.IsEnemy(Teams.player, character.Team)', 'autotest must retain actual hostile NPC targets')
print('CoopPlayerTeamLifecycleGuard: PASS (production team owner, End/retry wiring, actual player exclusions and hostile NPC predicates)')
