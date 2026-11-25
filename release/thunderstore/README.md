#### **PREPARE YOURSELF, LITTLE FRIEND!**

##### **What is it?**

**Addon for CU multiplayer mod (Casualties: Together). After spawning, each player gets a loadout menu once per campaign. They pick items within a budget, and the game delivers them in a temporary supply crate.**

##### **How to install?**

Install with a mod manager, or unpack the CasPrepared folder into /bepinex/plugins.

The host and every player need the same CasPrepared version. A player with another version sees the host as having no mod.

##### **Main features**

* Loadout menu. Searchable item catalog with categories, a selection list, the budget and the loadout cost. The Spawn button is blocked when the cost exceeds the budget.
* Presets. Save loadouts under your own names and load them in one click. Each preset shows its cost at the current host's prices.
* Prices. Each item costs its trader price times a host-adjustable multiplier (default 1.0).
* Item rules. The host can set custom prices, hide items and add items that traders do not sell. Players get the rules automatically.
* Host-authoritative. Every purchase is a request to the host over Casualties: Together. The host validates it and spawns the items at the player's position.
* Supply crate. Items arrive in a temporary crate that cannot be carried or worn. It decays after 5 minutes by default, and its contents drop on the floor.
* Once per campaign. The host records players by SteamID, and the records are saved with the campaign.
* Spawn window. The menu closes after a time limit (can be turned off) or when the player moves too far from the spawn point.
* Achievements stay enabled.
* Server settings toggle. A checkbox in Together's Server settings turns the mod on or off.

##### **Console commands**

All commands go through one console command: casprep \[command] \[value]. Tab completes command names and item ids. casprep help lists everything.

|Command|Description|
|-|-|
|casprep menu|Open or close the loadout menu (default key F6)|
|casprep enable \[on/off]|Turn the mod on or off for sessions you host|
|casprep budget \[n]|Loadout budget per player (default 100)|
|casprep mult \[x]|Price multiplier (default 1.0)|
|casprep window \[sec/off]|Seconds the menu stays available after spawn; 0 or off removes the limit (default 300)|
|casprep radius \[tiles]|Maximum distance from spawn before the menu closes (default 100)|
|casprep cratetime \[sec]|Supply crate lifetime (default 300)|
|casprep status|Show settings and list players with their loadout state|
|casprep reset \[player/all]|Let a player pick a loadout again|
|casprep cheats|Show whether cheat rules allow casprep giveloadout|
|casprep giveloadout \[player]|Open the menu again; needs sv\_cheats (and AllowClientCheatCommands for clients)|
|casprep setprice \[item] \[price]|Custom base price per unit, before the multiplier|
|casprep resetprice \[item/all]|Back to the trader price|
|casprep ban \[item]|Hide an item from the list|
|casprep unban \[item/all]|Show a hidden item again|
|casprep additem \[item] \[price]|Add an item that traders do not sell|
|casprep removeitem \[item]|Remove an added item|
|casprep items|List custom prices, hidden and added items|

Settings are locked during a running campaign unless sv\_cheats is on.

##### **Requirements**

BepInEx

CUCoreLib

Casualties: Together

##### **Special thanks to:**

Orsoniks For such awesome game;

CU:Together and CUCoreLib devs For letting us playing with friends.
