## v2.3.3

### Release Date
- 2026-10-15
- This update adds three new totems (the Headmaster's Roll-Call Ledger, the Bell Keeper's Metronome and the License to Pass the Buck) plus the Tempering Solvent for reforging. Cubs gain behaviors, feeding and petting, NPC affinity is visible as soon as you walk up, and a batch of player-reported issues is fixed, including From Scratch entry, the Duck Cup and the trophy display bonus in the sewer.

### New

- **Headmaster's Roll-Call Ledger (500106)**: totem. Hit 3 different enemies with weapons within 5 seconds for "Everyone present!": each marked enemy still alive takes bonus damage (20 + 25% of the hit that marked it). 6-second cooldown. Sold by Fuzhou at Cloudrise Dock on the Sky Islands at affinity level 3 for 20000.
- **Bell Keeper's Metronome (500107)**: totem. During a full-magazine reload a beat bar appears under the crosshair; press reload again while the needle is in the gold window to finish the reload early. Missing the window just reloads at normal speed. 20% bonus drop from the vanilla boss Speedy, or exchange 20 seashells with the Silent Bell Keeper at affinity level 5.
- **License to Pass the Buck (500109)**: totem. Enemies you hit get a 5-second bomb overhead; whoever the carrier hits next takes the bomb (that can be you). Passing it does not reset the fuse. 12-second cooldown. Sold by Fuzhou at affinity level 3 for 18000.
- **Tempering Solvent (500108)**: double-click a locked stat in the Reforge screen to spend one bottle and unlock it; the value stays. Sold in Dingdang's shop from affinity level 4; Dingdang gives 2 bottles once at level 6, and players already past level 6 receive them the next time they chat.
- **Cub behaviors, feeding and petting**:
  - Switch between Follow, Aggressive and Evasive. Follow (the default) never starts a fight, joins once you or it gets hit, and breaks off 10 seconds after combat. Evasive never fights, takes no damage and regularly clears its negative effects.
  - Feed picks the cheapest food in your pack for +20 experience and 10% health, once every 10 minutes per cub. Pet gets a line based on its personality.
  - Kills you make within 30 m of your cub also give it experience. The HUD shows its level, experience progress and behavior.

### Improved

- **NPC affinity is always visible**: walk up to a custom duck NPC, Yu Zhi or Dingdang and the Chat option shows the current affinity, for example "Chat (Affinity Lv.3 · 120/300)", or MAX at the top level.
- **From Scratch explains why you did not get in**: if something is still in your pack, your dog's pack or your deployed cub's pack, or you are still wearing a mask, headset or totem, a message names the item. A ticket kept in the dog's pack is now allowed.
- **PetNest expeditions**: the Rough tier can no longer kill your cub; only Desperate can. Cards list the cash, souls and loot of a successful trip. Cub damage now scales smoothly with the source Boss instead of capping strong bloodlines at one value.
- **Viper Dagger**: the five-stack poison burst adds 5% of the target's max health (up to 400 per burst).

### Fixed

- Fixed From Scratch falling back to a normal challenge after your first run: items kept in the dog's pack between runs were silently blocking entry, and a failed starter weapon left the mode half-started. The starter weapon now retries with another gun if delivery fails.
- Fixed Boss Intrusion appearing early in From Scratch; it now waits until wave 6.
- Fixed the trophy display max-health bonus being wiped when entering the sewer, which also left runs started from the sewer without it.
- Fixed the Boss order not actually being shuffled (Dragon Descendant, Skyburner Dragon Lord and Phantom Witch always came last); strong Bosses still stay out of the first 20 waves.
- Fixed Infinite Hell losing the Dragon Lord / Dragon Descendant signature gear, and per-wave cash being truncated or losing remainders.
- Fixed the Dragon Lord's "My child, protect me!" and its second phase starting together, and Dragon Descendants spamming errors after death.
- Fixed the Duck Cup sometimes stalling 30 seconds and aborting at start, refund messages not matching the actual result, and arena Bosses idling after their target died.
- Fixed the Sky Islands refusing entry alongside "no drop on death" mods, and the navigation instrument occasionally being deleted after going into a box or your pack.
- Fixed the Shadow of the Champion's dashes misbehaving during pause, photo mode or low frame rates, and the sand guards' heavy-attack ground warnings timing incorrectly.
- Fixed reforging, gifts, refunds and feeding possibly eating items when something went wrong; the items are now returned.
- After extracting from the Sky Islands, the base ambient lighting is recalculated once, in response to reports of the base turning dark. If it still happens, please send the author the Player.log from the game folder.
