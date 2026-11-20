<div align="center">
<img width="256" height="256" alt="Expie box" src="https://github.com/user-attachments/assets/ceaed8ab-9aaa-48d8-902c-4b7450657f84" />
</div>

# CasPrepared

Addon for **CU** multiplayer mod (**Casualties: Together**). After spawning, each player gets a loadout menu once per campaign. They pick items within a budget, and game delivers them in a temporary supply crate.

## Features

- **Loadout menu.** Searchable item catalog with categories, a selection list, the budget and the loadout cost. The Spawn button is blocked when the cost exceeds the budget.
- **Prices.** Each item costs its trader price times a host-adjustable multiplier (default 1.0).
- **Host-authoritative.** Every purchase is a request to the host over Casualties: Together. The host validates it and spawns the items at the player's position.
- **Supply crate.** Items arrive in a temporary crate that cannot be carried or worn. It decays after 5 minutes by default, and its contents drop on the floor.
- **Once per campaign.** The host records players by SteamID, and the records are saved with the campaign.
- **Spawn window.** The menu closes after a time limit (can be turned off) or when the player moves too far from the spawn point.
- **Achievements disabled.** In multiplayer sessions with the mod active, achievements are disabled the same way Together does it for `sv_cheats`, without unlocking cheat commands.
- **Server settings toggle.** A checkbox in Together's Server settings turns the mod on or off.

## Requirements

- [BepInEx](https://github.com/bepinex/bepinex)
- [CUCoreLib](https://github.com/jimmyking9999999/CUCoreLib)
- [Casualties: Together](https://github.com/creaturefeaturelarry/casualties-together)

## Console commands

Settings can always be changed in the main menu. During a running campaign they are locked unless `sv_cheats` is on.

| Command | Description |
|---|---|
| `casprep_menu` | Open or close the loadout menu (default key F6) |
| `casprep_enable [on/off]` | Turn the mod on or off for sessions you host |
| `casprep_budget [n]` | Loadout budget per player (default 100) |
| `casprep_mult [x]` | Price multiplier (default 1.0) |
| `casprep_window [sec/off]` | Seconds the menu stays available after spawn; `0` or `off` removes the limit (default 300) |
| `casprep_radius [tiles]` | Maximum distance from spawn before the menu closes (default 100) |
| `casprep_cratetime [sec]` | Supply crate lifetime (default 300) |
| `casprep_status` | List players and their loadout state |
| `casprep_reset [player]` | Let a player pick a loadout again |
| `casprep_cheats` | Show whether cheat rules allow `casprep_giveloadout` |
| `casprep_giveloadout [player]` | Open the menu again; needs `sv_cheats` (and `AllowClientCheatCommands` for clients) |

The same settings are stored in `BepInEx/config/com.casprepared.loadout.cfg`.

## Standalone (single-player) version [HERE](https://github.com/Turianel/Casualties-Prepared/releases/tag/Standalone)

`CasPreparedStandalone` is a separate build for single-player runs. It needs only BepInEx and CUCoreLib, not Casualties: Together.

- Same menu, catalog, budget, prices and supply crate.
- One loadout per run, saved with the run.
- No time limit: the menu closes only when you move too far from the spawn point or die.
- On/off switch on the character select screen. Achievements are disabled for runs with the menu on (vanilla cheat flag).
- Settings can be changed only outside a run. `casprep_window`, `casprep_reset`, `casprep_cheats` and `casprep_giveloadout` don't exist in this build.
- Install either the multiplayer or the standalone build, not both. With both installed, the standalone build doesn't load.
- If Together is installed too, the standalone build stays idle during multiplayer sessions.

Settings are stored in `BepInEx/config/com.casprepared.standalone.cfg`.

## Building

1. Install the game with BepInEx 5, CUCoreLib and Casualties: Together.
2. Set `ManualBaseGamePath` in `vars.targets` to your game folder.
3. Run `dotnet build CasPrepared`. The DLL is copied to `BepInEx/plugins/CasPrepared`.
4. Standalone: run `dotnet build CasPrepared.Standalone`. Add `-p:DeployStandalone=true` to copy the DLL to `BepInEx/plugins/CasPreparedStandalone` (remove the multiplayer build first).

## License
MIT, see [LICENSE](LICENSE).

## Additional info from author

This mod was created using AI, and I will never hide that fact. I have the utmost respect for old-school modders and programmers who don’t use AI; I simply lack those specific skills and that particular mindset—but I do have ideas that I want to bring to life.
If anyone likes the mod and wants to rewrite it by hand, I have no objection—in fact, I’d be happy if they did.
