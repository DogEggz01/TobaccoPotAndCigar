using System;
using UnityEngine;

namespace TobaccoPotAndCigar.Runtime
{
    public static class PrefabReplacement
    {
        private static readonly GameObject[] CustomPrefabs = new GameObject[RuntimeConstants.CustomPrefabCount];

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

            GameObject instance = null;
            try
            {
            instance = UnityEngine.Object.Instantiate(
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
            catch (Exception exception)
            {
                if (instance != null)
                {
                    SaveablePrefab orphan = instance.GetComponent<SaveablePrefab>();
                    if (orphan != null && SaveLoadManager.instance != null) orphan.Unregister();
                    instance.SetActive(false);
                    UnityEngine.Object.Destroy(instance);
                }
                RuntimeDiagnostics.Error("Could not create prefab " + prefabIndex + "; partial output removed: " + exception.Message);
                return null;
            }
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
            return ReplaceOwnedItem(source, resultPrefabIndex, replacementHealth, replacementAmount,
                worldPositionOffset, source != null ? source.transform.rotation : Quaternion.identity);
        }

        public static bool ReplaceOwnedItem(ShipItem source, int resultPrefabIndex,
            float replacementHealth, float replacementAmount, Vector3 worldPositionOffset, Quaternion rotation)
        {
            if (!CanTransform(source))
                return false;

            ShipItem replacement = SpawnOwned(
                resultPrefabIndex,
                source.transform.position + worldPositionOffset,
                rotation,
                source,
                replacementHealth,
                replacementAmount);
            if (replacement == null)
                return false;

            ConsumeOwned(source);
            return true;
        }

        public static bool CombineOwnedItems(ShipItem target, ShipItem held, int resultPrefabIndex, Vector3 offset, Quaternion rotation)
        {
            if (target == held || !CanTransform(target) || !CanTransform(held)) return false;
            ShipItem result = SpawnOwned(resultPrefabIndex, target.transform.position + offset,
                rotation, target, 0, 0);
            if (result == null) return false;
            ConsumeOwned(held);
            ConsumeOwned(target);
            return true;
        }

        public static void ConsumeOwned(ShipItem source)
        {
            if (source == null) return;
            if (source.held != null) source.held.DropItem();
            source.sold = false;
            if (source.itemRigidbodyC != null) source.FreezeItem();
            else
                foreach (Collider collider in source.GetComponents<Collider>()) collider.enabled = false;
            source.gameObject.SetActive(false);
            source.DestroyItem();
        }

        public static bool CanTransform(ShipItem source)
        {
            if (source == null || !source.sold || !source.gameObject.activeInHierarchy || source.nailed)
                return false;

            SaveablePrefab saveable = source.GetComponent<SaveablePrefab>();
            if (saveable == null)
                return false;
            if (saveable.currentCrateId > 0)
            {
                return false;
            }
            if (source.itemRigidbodyC != null && source.GetCurrentInventorySlot() >= 0)
            {
                return false;
            }
            return true;
        }

        public static GameObject ResolvePrefab(int prefabIndex)
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
