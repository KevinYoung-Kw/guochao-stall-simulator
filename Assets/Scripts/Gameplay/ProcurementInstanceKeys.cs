using System.Collections.Generic;

namespace Guochao
{
    // FREEDOM19 physical-unit identity. Cart and Cargo are occurrence lists:
    // each entry is one real unit, so identical consumables need distinct,
    // deterministic visual / packing instance keys. The canonical occurrence
    // order is Cargo first (in cargo order), then Cart (in basket order),
    // then the single held unit — this keeps a unit's key stable across the
    // load transition (Cart entries append into Cargo without reordering) and
    // lets a return of one duplicate simply retire the highest-ordinal object.
    // The first ordinal keeps the raw SKU key for backwards compatibility of
    // the visual dictionaries; later units carry a stable "#n" suffix. Keys
    // are identity strings ONLY — never feed them to catalog lookups; use
    // parallel metadata to recover the SKU.
    public static class ProcurementInstanceKeys
    {
        // Visual dictionary key for one owned unit.
        public static string Key(string sku, int ordinal) => ordinal <= 0 ? sku : sku + "#" + ordinal;

        // Ordinal of the cart unit at cartIndex (cargo occurrences first).
        public static int OrdinalOf(IReadOnlyList<string> cargo, IReadOnlyList<string> cart, string sku, int cartIndex)
        {
            int ordinal = 0;
            if (cargo != null) foreach (string id in cargo) if (id == sku) ordinal++;
            if (cart != null) for (int i = 0; i < cartIndex && i < cart.Count; i++) if (cart[i] == sku) ordinal++;
            return ordinal;
        }

        // Ordinal of the cargo unit at cargoIndex.
        public static int CargoOrdinal(IReadOnlyList<string> cargo, int cargoIndex)
        {
            int ordinal = 0;
            if (cargo == null) return 0;
            string sku = cargo[cargoIndex];
            for (int i = 0; i < cargoIndex && i < cargo.Count; i++) if (cargo[i] == sku) ordinal++;
            return ordinal;
        }

        // Ordinal the held unit will occupy once stowed (append position).
        public static int HeldOrdinal(IReadOnlyList<string> cargo, IReadOnlyList<string> cart, string sku)
        {
            int ordinal = 0;
            if (cargo != null) foreach (string id in cargo) if (id == sku) ordinal++;
            if (cart != null) foreach (string id in cart) if (id == sku) ordinal++;
            return ordinal;
        }

        // Unique vehicle-packing instance id per cargo occurrence. The first
        // occurrence preserves the legacy "cargo_SKU" id; later units get a
        // stable per-SKU suffix, matching the V10 validation fixture pattern.
        public static string LoadoutKey(IReadOnlyList<string> cargo, int cargoIndex)
        {
            string sku = cargo[cargoIndex];
            int ordinal = 0;
            for (int i = 0; i < cargoIndex && i < cargo.Count; i++) if (cargo[i] == sku) ordinal++;
            return ordinal == 0 ? "cargo_" + sku : "cargo_" + sku + "_" + ordinal;
        }
    }
}
