#### **PREPARE YOURSELF, LITTLE FRIEND!**

##### **What is it?**

**Single-player version of Casualties: Prepared. After spawning in a new run, you get a loadout menu once per run. Pick items within a budget, and the game delivers them in a temporary supply crate.**

##### **How to install?**

Install with a mod manager, or unpack the CasPreparedStandalone folder into /bepinex/plugins.

Do not install it together with the multiplayer version of CasPrepared: remove /bepinex/plugins/CasPrepared first.

##### **Main features**

* Loadout menu. Searchable item catalog with categories, a selection list, the budget and the loadout cost. The Spawn button is blocked when the cost exceeds the budget.
* Presets. Save loadouts under your own names and load them in one click.
* Prices. Each item costs its trader price times an adjustable multiplier (default 1.0).
* Item rules. Set custom prices, hide items and add items that traders do not sell.
* Supply crate. Items arrive in a temporary crate that cannot be carried or worn. It decays after 5 minutes by default, and its contents drop on the floor.
* Once per run. The record is saved with the run, so reloading does not give a second loadout.
* Spawn window. No time limit: the menu closes when you move too far from the spawn point or die.
* On/off switch on the character select screen.
* Achievements stay enabled.
* No Casualties: Together needed.

##### **Console commands**

All commands go through one console command: casprep \[command] \[value]. Tab completes command names and item ids. casprep help lists everything.

|Command|Description|
|-|-|
|casprep menu|Open or close the loadout menu (default key F6)|
|casprep enable \[on/off]|Turn the mod on or off (main menu only)|
|casprep budget \[n]|Loadout budget (default 100)|
|casprep mult \[x]|Price multiplier (default 1.0)|
|casprep radius \[tiles]|Maximum distance from spawn before the menu closes (default 100)|
|casprep cratetime \[sec]|Supply crate lifetime (default 300)|
|casprep status|Show settings and the loadout state of this run|
|casprep setprice \[item] \[price]|Custom base price per unit, before the multiplier|
|casprep resetprice \[item/all]|Back to the trader price|
|casprep ban \[item]|Hide an item from the list|
|casprep unban \[item/all]|Show a hidden item again|
|casprep additem \[item] \[price]|Add an item that traders do not sell|
|casprep removeitem \[item]|Remove an added item|
|casprep items|List custom prices, hidden and added items|

Settings and item rules can be changed only outside a run.

##### **Requirements**

BepInEx

CUCoreLib

##### **Special thanks to:**

Orsoniks For such awesome game;

CUCoreLib devs For the modding library.
