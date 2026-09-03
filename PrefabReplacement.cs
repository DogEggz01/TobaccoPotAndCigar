using System;
using UnityEngine;

namespace TobaccoPotAndCigar.Runtime
{
    public static class PrefabReplacement
    {
        private static readonly GameObject[] CustomPrefabs = new GameObject[4];

        public static void RegisterCustomPrefab(int index, GameObject prefab)
        {
            int slot = index - RuntimeConstants.TobaccoPotPrefabIndex;
            if (slot < 0 || slot >= CustomPrefabs.Length)
                throw new ArgumentOutOfRangeException("index");
            CustomPrefabs[slot] = prefab;
        }

        public static void ClearCustomPrefabs()
        {
            for (int i = 0; i < CustomPrefabs.Length; i++)
                CustomPrefabs[i] = null;
        }

        public static bool CanSpawn(int prefabIndex)
        {
            return ResolvePrefab(prefabIndex) != null &&
                   SaveLoadManager.instance != null;
        }

        public static ShipItem SpawnOwned(
            int prefabIndex,
            Vector3 position,
            Quaternion rotation,
            ShipItem context,
            float health,
            float amount)
        {
            GameObject prefab = ResolvePrefab(prefabIndex);
            if (prefab == null)
                return null;
            if (SaveLoadManager.instance == null)
            {
                RuntimeDiagnostics.Error("Cannot spawn prefab " + prefabIndex +
                                         ": SaveLoadManager is unavailable.");
                return null;
            }

            GameObject instance = UnityEngine.Object.Instantiate(
                prefab,
                position,
                rotation);
            ShipItem item = instance.GetComponent<ShipItem>();
            SaveablePrefab saveable = instance.GetComponent<SaveablePrefab>();
            if (item == null || saveable == null)
            {
                RuntimeDiagnostics.Error("Registered prefab " + prefabIndex +
                                         " lost its ShipItem/SaveablePrefab contract.");
                UnityEngine.Object.Destroy(instance);
                return null;
            }

            if (context != null)
            {
                SaveablePrefab contextSaveable = context.GetComponent<SaveablePrefab>();
                if (contextSaveable != null)
                {
                    saveable.SetParentObject(contextSaveable.GetParentObject());
                    saveable.currentCrateId = contextSaveable.currentCrateId;
                }
                if (context.transform.parent != null)
                    instance.transform.SetParent(context.transform.parent, true);
            }

            item.sold = true;
            item.health = health;
            item.amount = amount;
            RuntimeStateSynchronizer.Sync(item, true);
            saveable.RegisterToSave();
            return item;
        }

        public static bool ReplaceOwnedItem(
            ShipItem source,
            int resultPrefabIndex,
            float replacementHealth,
            float replacementAmount)
        {
            return ReplaceOwnedItem(
                source,
                resultPrefabIndex,
                replacementHealth,
                replacementAmount,
                Vector3.zero);
        }

        public static bool ReplaceOwnedItem(
            ShipItem source,
            int resultPrefabIndex,
            float replacementHealth,
            float replacementAmount,
            Vector3 worldPositionOffset)
        {
            if (!CanTransform(source))
                return false;

            ShipItem replacement = SpawnOwned(
                resultPrefabIndex,
                source.transform.position + worldPositionOffset,
                source.transform.rotation,
                source,
                replacementHealth,
                replacementAmount);
            if (replacement == null)
                return false;

            if (source.itemRigidbodyC != null)
                source.FreezeItem();
            else
            {
                Collider sourceCollider = source.GetComponent<Collider>();
                if (sourceCollider != null)
                    sourceCollider.enabled = false;
            }
            source.DestroyItem();
            return true;
        }

        private static bool CanTransform(ShipItem source)
        {
            if (source == null || !source.sold)
                return false;

            SaveablePrefab saveable = source.GetComponent<SaveablePrefab>();
            if (saveable == null)
                return false;
            if (saveable.currentCrateId > 0)
            {
                RuntimeDiagnostics.Error("Refusing to transform crate-contained item " +
                                         source.name + ".");
                return false;
            }
            if (source.itemRigidbodyC != null && source.GetCurrentInventorySlot() >= 0)
            {
                RuntimeDiagnostics.Error("Refusing to transform inventory-held item " +
                                         source.name + ".");
                return false;
            }
            return true;
        }

        private static GameObject ResolvePrefab(int prefabIndex)
        {
            PrefabsDirectory directory = PrefabsDirectory.instance;
            if (directory == null || directory.directory == null ||
                prefabIndex < 0 || prefabIndex >= directory.directory.Length)
            {
                RuntimeDiagnostics.Error("Prefab directory cannot resolve index " +
                                         prefabIndex + ".");
                return null;
            }

            GameObject prefab = directory.directory[prefabIndex];
            int customSlot = prefabIndex - RuntimeConstants.TobaccoPotPrefabIndex;
            if (customSlot >= 0 && customSlot < CustomPrefabs.Length &&
                prefab != CustomPrefabs[customSlot])
            {
                RuntimeDiagnostics.Error("Custom prefab index " + prefabIndex +
                                         " is not the registered bundled asset.");
                return null;
            }
            return prefab;
        }
    }
}
