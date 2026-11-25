<div align="center">
<img width="256" height="256" alt="Expie box" src="https://github.com/user-attachments/assets/ceaed8ab-9aaa-48d8-902c-4b7450657f84" />
</div>

# CasPrepared

[Nexus Mods](https://www.nexusmods.com/scavprototype/mods/1020) · [Thunderstore](https://thunderstore.io/c/casualties-unknown/p/Turianel_mod_team/CasualtiesPrepared/) · [Thunderstore (Standalone)](https://thunderstore.io/c/casualties-unknown/p/Turianel_mod_team/CasualtiesPreparedStandalone/)

Addon for **CU** multiplayer mod (**Casualties: Together**). After spawning, each player gets a loadout menu once per campaign. They pick items within a budget, and game delivers them in a temporary supply crate.

## Features

- **Loadout menu.** Searchable item catalog with categories, a selection list, the budget and the loadout cost. The Spawn button is blocked when the cost exceeds the budget.
- **Prices.** Each item costs its trader price times a host-adjustable multiplier (default 1.0).
- **Host-authoritative.** Every purchase is a request to the host over Casualties: Together. The host validates it and spawns the items at the player's position.
- **Supply crate.** Items arrive in a temporary crate that cannot be carried or worn. It decays after 5 minutes by default, and its contents drop on the floor.
- **Once per campaign.** The host records players by SteamID, and the records are saved with the campaign.
- **Spawn window.** The menu closes after a time limit (can be turned off) or when the player moves too far from the spawn point.
- **Achievements stay enabled.** The mod does not touch achievements (since 1.0.2). Cheat commands stay locked by `sv_cheats` as usual.
- **Server settings toggle.** A checkbox in Together's Server settings turns the mod on or off.
- **Presets.** Save loadouts under your own names (up to 32 characters) and load them in one click. The presets panel opens from the loadout window and shows each preset's cost at the current host's prices.
- **Item rules.** The host can set custom prices, hide items and add items that traders do not sell. Clients get the rules from the host automatically.

- **Same version everywhere.** Since 1.1 the host and every player need the same CasPrepared version. A player with another version sees the host as having no mod.

## Requirements

- [BepInEx](https://github.com/bepinex/bepinex)
- [CUCoreLib](https://github.com/jimmyking9999999/CUCoreLib)
- [Casualties: Together](https://github.com/creaturefeaturelarry/casualties-together)

## Console commands

All commands go through one console command: `casprep <command> [value]`. The console shows hints for the typed command, and Tab completes command names and item ids. `casprep help` lists everything.

Settings can always be changed in the main menu. During a running campaign they are locked unless `sv_cheats` is on.

| Command | Description |
|---|---|
| `casprep menu` | Open or close the loadout menu (default key F6) |
| `casprep enable [on/off]` | Turn the mod on or off for sessions you host |
| `casprep budget [n]` | Loadout budget per player (default 100) |
| `casprep mult [x]` | Price multiplier (default 1.0) |
| `casprep window [sec/off]` | Seconds the menu stays available after spawn; `0` or `off` removes the limit (default 300) |
| `casprep radius [tiles]` | Maximum distance from spawn before the menu closes (default 100) |
| `casprep cratetime [sec]` | Supply crate lifetime (default 300) |
| `casprep status` | Show settings and list players with their loadout state |
| `casprep reset [player/all]` | Let a player pick a loadout again |
| `casprep cheats` | Show whether cheat rules allow `casprep giveloadout` |
| `casprep giveloadout [player]` | Open the menu again; needs `sv_cheats` (and `AllowClientCheatCommands` for clients) |

### Item rules

| Command | Description |
|---|---|
| `casprep setprice [item] [price]` | Custom base price per unit, before the multiplier |
| `casprep resetprice [item/all]` | Back to the trader price |
| `casprep ban [item]` | Hide an item from the list |
| `casprep unban [item/all]` | Show a hidden item again |
| `casprep additem [item] [price]` | Add an item that traders do not sell; the price is optional when the item has a trader price |
| `casprep removeitem [item]` | Remove an added item |
| `casprep items` | List custom prices, hidden and added items |

The same settings are stored in `BepInEx/config/com.casprepared.loadout.cfg`, item rules in `BepInEx/config/com.casprepared.loadout.items.json`. Presets are stored per player in `BepInEx/config/CasPrepared.presets.json` (shared by both builds).

## Standalone (single-player) version

`CasPreparedStandalone` is a separate build for single-player runs. It needs only BepInEx and CUCoreLib, not Casualties: Together.

- Same menu, catalog, budget, prices and supply crate.
- One loadout per run, saved with the run.
- No time limit: the menu closes only when you move too far from the spawn point or die.
- On/off switch on the character select screen. Achievements stay enabled.
- Presets and item rules work the same way. Settings and item rules can be changed only outside a run. `casprep window`, `casprep reset`, `casprep cheats` and `casprep giveloadout` don't exist in this build.
- Install either the multiplayer or the standalone build, not both. With both installed, the standalone build doesn't load.
- If Together is installed too, the standalone build stays idle during multiplayer sessions.

Settings are stored in `BepInEx/config/com.casprepared.standalone.cfg`, item rules in `BepInEx/config/com.casprepared.standalone.items.json`.

## Building

1. Install the game with BepInEx 5, CUCoreLib and Casualties: Together.
2. Set `ManualBaseGamePath` in `vars.targets` to your game folder.
3. Run `dotnet build CasPrepared`. The DLL is copied to `BepInEx/plugins/CasPrepared`.
4. Standalone: run `dotnet build CasPrepared.Standalone`. Add `-p:DeployStandalone=true` to copy the DLL to `BepInEx/plugins/CasPreparedStandalone` (remove the multiplayer build first).

## Releasing

1. Bump the version in `Plugin.cs` and the `.csproj` of both builds, add a `## <version>` section to `CHANGELOG.md`, commit and push.
2. Run `python release/release.py <version>` (add `--only multiplayer` or `--only standalone` for one build, `--dry-run` to only build and package).
3. The script builds both projects, packages the zips into `release/out/<version>/` and creates the GitHub releases `v<version>` and `S-<version>`.
4. `.github/workflows/publish.yml` then uploads each release to Nexus Mods and Thunderstore (secrets `NEXUS` and `THUNDERSTORE`).

## License
MIT, see [LICENSE](LICENSE).

## Additional info from author

This mod was created using AI, and I will never hide that fact. I have the utmost respect for old-school modders and programmers who don’t use AI; I simply lack those specific skills and that particular mindset—but I do have ideas that I want to bring to life.
If anyone likes the mod and wants to rewrite it by hand, I have no objection—in fact, I’d be happy if they did.
