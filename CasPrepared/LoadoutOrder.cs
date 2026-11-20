using System.Collections.Generic;
using UnityEngine;

namespace CasPrepared
{
    /// <summary>
    /// Purchase validation and supply crate spawn, shared by the multiplayer host and the standalone build.
    /// </summary>
    internal static class LoadoutOrder
    {
        public sealed class Line
        {
            public CatalogEntry Entry;
            public int Count;
        }

        /// <summary>
        /// Checks the selection against the catalog, per-item / total limits and the budget.
        /// Returns null on success (order, cost and total filled) or an error message.
        /// </summary>
        public static string Build(IEnumerable<KeyValuePair<string, int>> items, out List<Line> order, out long cost, out int total)
        {
            order = new List<Line>();
            cost = 0;
            total = 0;
            if (items == null) return "Nothing selected.";

            var mult = ModConfig.PriceMultiplier.Value;
            foreach (var pair in items)
            {
                if (!LoadoutCatalog.TryGet(pair.Key, out var entry) || !LoadoutCatalog.IsSpawnable(entry.Id))
                    return "Item is not available: " + pair.Key;
                int count = pair.Value;
                if (count <= 0) continue;
                if (count > LoadoutCatalog.MaxPerItem) return $"Too many of {entry.Name} (max {LoadoutCatalog.MaxPerItem}).";
                total += count;
                cost += LoadoutCatalog.PriceOf(entry, mult, count);
                order.Add(new Line { Entry = entry, Count = count });
            }
            if (order.Count == 0) return "Nothing selected.";
            if (total > LoadoutCatalog.MaxTotalItems) return $"Too many items (max {LoadoutCatalog.MaxTotalItems}).";
            if (cost > ModConfig.Budget.Value) return $"Loadout costs {cost}, budget is {ModConfig.Budget.Value}.";
            return null;
        }

        /// <summary>Spawns the supply crate at the body's feet and loads the order into it.</summary>
        public static void Spawn(Body body, List<Line> order)
        {
            Vector2 pos = body.transform.position;
            var crateGo = Utils.Create(SupplyCrate.ItemId, pos, 0f);
            var crate = crateGo != null ? crateGo.GetComponent<Container>() : null;
            if (crate == null) Plugin.Logger.LogWarning("Supply crate failed to spawn, items will be dropped on the floor.");

            foreach (var line in order)
            {
                for (int i = 0; i < line.Count; i++)
                {
                    // Plain prefab spawn, same as traders do (in multiplayer Together registers new objects for sync).
                    var go = Utils.Create(line.Entry.Id, pos, 0f);
                    LoadoutCatalog.PrepareSpawned(go);
                    if (go == null || crate == null) continue;
                    var item = go.GetComponent<Item>();
                    if (item != null && crate.CanHoldItem(item)) crate.LoadItem(item);
                }
            }
        }
    }
}
