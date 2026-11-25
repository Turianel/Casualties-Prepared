# Changelog

Release notes for both builds. `release/release.py <version>` uses the `## <version>` section
as the GitHub release notes and the Nexus Mods changelog.

## 1.1.0

- Presets: save loadouts under your own names and load them in one click. The presets panel opens from the loadout window and shows each preset's cost at the current prices.
- Item rules for the host: custom prices, hidden items and extra items (`casprep setprice / resetprice / ban / unban / additem / removeitem / items`). Players get the rules from the host automatically.
- One console command: `casprep <command> [value]`, with hints and Tab completion for commands, items and players. The old `casprep_*` commands are gone.
- Long names scroll while hovered. Selected amounts and prices line up in columns.
- Price multiplier first in the status strip. "Stay near your spawn" appears past half of the allowed range, with an alert when the menu is closed.
- Multiplayer 1.1 is not compatible with 1.0: everyone needs the same version.

## 1.0.2

- Achievements are no longer disabled.
