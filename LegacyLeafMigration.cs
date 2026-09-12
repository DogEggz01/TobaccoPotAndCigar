namespace TobaccoPotAndCigar.Runtime
{
    /// <summary>Transforms loaded records before native spawning/caching; never writes save files.</summary>
    public static class LegacyLeafMigration
    {
        public static bool TryMigrate(SavePrefabData data)
        {
            if (data == null || data.prefabIndex != RuntimeConstants.DriedTobaccoLeafPrefabIndex || !data.isSold)
                return false;
            TobaccoRecipe recipe;
            if (!LeafStateRules.TryReadWrapper(data.itemHealth, data.itemAmount, out recipe))
            {
                RuntimeDiagnostics.Error("Legacy leaf instance " + data.instanceId +
                    " has conflicting count/recipe data (health=" + data.itemHealth +
                    ", amount=" + data.itemAmount + "). Original ID 613 and values preserved; repair the save record before crafting.");
                return false;
            }
            if (recipe.Count == 0)
            {
                data.prefabIndex = RuntimeConstants.FreshTobaccoLeafPrefabIndex;
                data.itemHealth = LeafStateRules.DryingHours;
            }
            else
            {
                data.prefabIndex = RuntimeConstants.CigarWrapperPrefabIndex;
                // Legacy leaves use Z-up geometry; wrappers use Y-up. Preserve the
                // visible resting plane while native loading restores the same owner/position.
                data.rotation = (UnityEngine.Quaternion)data.rotation * new UnityEngine.Quaternion(.70710678f, 0, 0, .70710678f);
                // Preserve the precise old recipe encoding, count and all ownership fields.
            }
            return true;
        }

        public static SaveContainer MigrateLoadedContainer(SaveContainer save)
        {
            if (save == null || save.savedPrefabs == null) return save;
            int migrated = 0;
            foreach (SavePrefabData data in save.savedPrefabs)
                if (TryMigrate(data)) migrated++;
            if (migrated > 0)
                RuntimeDiagnostics.Info("Migrated " + migrated + " legacy leaf records for 1.0.2 before native object loading.");
            return save;
        }
    }
}
