using System;
using TobaccoPotAndCigar.Runtime;
using UnityEngine;

namespace TobaccoPotAndCigar.Prefabs
{
    internal static class CigarPrefabRegistrar
    {
        private const int RequiredDirectoryLength =
            RuntimeConstants.DriedTobaccoLeafPrefabIndex + 1;

        private static bool registered;

        internal static bool IsReady
        {
            get { return registered; }
        }

        internal static void EnsureRegistered(PrefabsDirectory directory)
        {
            if (registered)
                return;
            if (directory == null || directory.directory == null)
                throw new InvalidOperationException("PrefabsDirectory is unavailable.");
            if (!CigarAssetBundle.IsLoaded)
                throw new InvalidOperationException("Cigar AssetBundle is not loaded.");

            GameObject[] resized = directory.directory;
            if (resized.Length < RequiredDirectoryLength)
                Array.Resize(ref resized, RequiredDirectoryLength);

            for (int index = RuntimeConstants.TobaccoPotPrefabIndex;
                 index <= RuntimeConstants.DriedTobaccoLeafPrefabIndex;
                 index++)
            {
                GameObject bundled = CigarAssetBundle.GetPrefab(index);
                GameObject occupied = resized[index];
                if (occupied != null && occupied != bundled)
                {
                    throw new InvalidOperationException(
                        "Prefab index " + index + " is already occupied by " +
                        occupied.name + "; registration aborted without overwriting it.");
                }
                ValidateBundledPrefab(index, bundled);
            }

            directory.directory = resized;
            for (int index = RuntimeConstants.TobaccoPotPrefabIndex;
                 index <= RuntimeConstants.DriedTobaccoLeafPrefabIndex;
                 index++)
            {
                GameObject bundled = CigarAssetBundle.GetPrefab(index);
                directory.directory[index] = bundled;
                PrefabReplacement.RegisterCustomPrefab(index, bundled);
            }
            registered = true;
        }

        internal static void ValidateAndConfigure(PrefabsDirectory directory)
        {
            if (!registered || directory == null || directory.shipItems == null ||
                directory.shipItems.Length < RequiredDirectoryLength)
            {
                throw new InvalidOperationException(
                    "PrefabsDirectory ship-item cache was not populated for indices 610-613.");
            }

            for (int index = RuntimeConstants.TobaccoPotPrefabIndex;
                 index <= RuntimeConstants.DriedTobaccoLeafPrefabIndex;
                 index++)
            {
                if (directory.directory[index] != CigarAssetBundle.GetPrefab(index) ||
                    directory.shipItems[index] == null)
                {
                    throw new InvalidOperationException(
                        "Runtime prefab cache validation failed at index " + index + ".");
                }
            }

            for (int index = RuntimeConstants.FirstVanillaPipePrefabIndex;
                 index <= RuntimeConstants.LastVanillaPipePrefabIndex;
                 index++)
            {
                VanillaPipeSmokeProfile.ApplyRequired(
                    directory.directory[index],
                    index);
            }

            ConfigureTobaccoHint(
                directory.directory[RuntimeConstants.WhiteTobaccoPrefabIndex],
                RuntimeConstants.WhiteTobaccoPrefabIndex);
            ConfigureTobaccoHint(
                directory.directory[RuntimeConstants.GreenTobaccoPrefabIndex],
                RuntimeConstants.GreenTobaccoPrefabIndex);
            ConfigureTobaccoHint(
                directory.directory[RuntimeConstants.BlackTobaccoPrefabIndex],
                RuntimeConstants.BlackTobaccoPrefabIndex);
            ConfigureTobaccoHint(
                directory.directory[RuntimeConstants.BrownTobaccoPrefabIndex],
                RuntimeConstants.BrownTobaccoPrefabIndex);
            ConfigureTobaccoHint(
                directory.directory[RuntimeConstants.BlueTobaccoPrefabIndex],
                RuntimeConstants.BlueTobaccoPrefabIndex);

            ConfigureVanillaDrying(
                directory.directory[RuntimeConstants.GreenTobaccoPrefabIndex],
                RuntimeConstants.WhiteTobaccoPrefabIndex);
            ConfigureVanillaDrying(
                directory.directory[RuntimeConstants.WhiteTobaccoPrefabIndex],
                RuntimeConstants.BrownTobaccoPrefabIndex);
            ConfigureVanillaDrying(
                directory.directory[RuntimeConstants.BrownTobaccoPrefabIndex],
                RuntimeConstants.BlackTobaccoPrefabIndex);

            Plugin.LogSource?.LogInfo(
                "Registered bundled prefabs 610-613 and rack-only tobacco drying.");
        }

        internal static void Reset()
        {
            registered = false;
            PrefabReplacement.ClearCustomPrefabs();
        }

        private static void ConfigureVanillaDrying(
            GameObject prefab,
            int resultPrefabIndex)
        {
            if (prefab == null)
                throw new InvalidOperationException(
                    "Vanilla drying source for result " + resultPrefabIndex + " is missing.");
            RackOnlyDrying drying = prefab.GetComponent<RackOnlyDrying>();
            if (drying == null)
                drying = prefab.AddComponent<RackOnlyDrying>();
            drying.ConfigureResultPrefab(resultPrefabIndex);
        }

        private static void ConfigureTobaccoHint(
            GameObject prefab,
            int prefabIndex)
        {
            ShipItemTobacco tobacco = prefab != null
                ? prefab.GetComponent<ShipItemTobacco>()
                : null;
            if (tobacco == null || string.IsNullOrEmpty(tobacco.name))
            {
                throw new InvalidOperationException(
                    "Tobacco prefab " + prefabIndex +
                    " cannot provide its pointer hint name.");
            }

            tobacco.description = tobacco.name;
        }

        private static void ValidateBundledPrefab(int index, GameObject prefab)
        {
            if (prefab == null || prefab.GetComponent<ShipItem>() == null ||
                prefab.GetComponent<SaveablePrefab>() == null)
            {
                throw new InvalidOperationException(
                    "Bundled prefab " + index + " is missing its item/save contract.");
            }

            bool specialized = index == RuntimeConstants.TobaccoPotPrefabIndex
                ? prefab.GetComponent<TobaccoPlantPotState>() != null
                : index == RuntimeConstants.FreshTobaccoLeafPrefabIndex
                    ? prefab.GetComponent<FreshTobaccoLeafState>() != null &&
                      prefab.GetComponent<RackOnlyDrying>() != null
                    : index == RuntimeConstants.CigarPrefabIndex
                        ? prefab.GetComponent<CigarRuntimeState>() != null
                        : prefab.GetComponent<DriedTobaccoLeafState>() != null;
            if (!specialized)
            {
                throw new InvalidOperationException(
                    "Bundled prefab " + index +
                    " does not contain its authored runtime component.");
            }
        }
    }
}
