// TobaccoPot — grouped mod source. Existing type identities are preserved.

// PotRainPatches
namespace TobaccoPotAndCigar.Patches
{
    using HarmonyLib;
    using TobaccoPotAndCigar.Runtime;
// GoPointer clears held state after ShipItem.OnDrop. Hook here so the
    // exposure snapshot is taken once at the completed placement.
    [HarmonyPatch(typeof(GoPointer), "DropItem")]
    internal static class PotRainPlacementPatch
    {
        [HarmonyPrefix]
        private static void Prefix(GoPointer __instance, out TobaccoPlantPotState __state)
        {
            PickupableItem held = __instance.GetHeldItem();
            __state = held != null ? held.GetComponent<TobaccoPlantPotState>() : null;
        }

        [HarmonyPostfix]
        private static void Postfix(TobaccoPlantPotState __state)
        {
            if (__state != null) __state.CheckRainExposureOnPlacement();
        }
    }

    [HarmonyPatch(typeof(ShipItem), "OnPickup")]
    internal static class PotRainPickupPatch
    {
        [HarmonyPostfix]
        private static void Postfix(ShipItem __instance)
        {
            TobaccoPlantPotState pot = __instance.GetComponent<TobaccoPlantPotState>();
            if (pot != null) pot.ClearRainExposure();
        }
    }

    [HarmonyPatch(typeof(ShipItem), "OnEnterInventory")]
    internal static class PotRainInventoryPatch
    {
        [HarmonyPostfix]
        private static void Postfix(ShipItem __instance)
        {
            TobaccoPlantPotState pot = __instance.GetComponent<TobaccoPlantPotState>();
            if (pot != null) pot.ClearRainExposure();
        }
    }
}


// GrowthMath
namespace TobaccoPotAndCigar.Runtime
{
    using UnityEngine;
public static class GrowthMath
    {
        public const float Capacity = 15f;
        public const float GameHoursPerDay = 24f;
        public const int RequiredDays = 15;
        public const float RequiredGameHours = RequiredDays * GameHoursPerDay;

        public static int Advance(
            ref float storedWater,
            ref float growthGameHours,
            float availableGameHours)
        {
            storedWater = Mathf.Clamp(storedWater, 0f, Capacity);
            growthGameHours = Mathf.Clamp(growthGameHours, 0f, RequiredGameHours);
            availableGameHours = Mathf.Max(0f, availableGameHours);
            int waterUnitsConsumed = 0;

            while (availableGameHours > 0.00001f &&
                   storedWater >= 1f &&
                   growthGameHours < RequiredGameHours)
            {
                int completedDays = Mathf.FloorToInt(
                    growthGameHours / GameHoursPerDay);
                float nextBoundary = Mathf.Min(
                    (completedDays + 1) * GameHoursPerDay,
                    RequiredGameHours);
                float step = Mathf.Min(
                    availableGameHours,
                    nextBoundary - growthGameHours);

                if (step <= 0f)
                    break;

                growthGameHours += step;
                availableGameHours -= step;

                if (growthGameHours >= nextBoundary - 0.0001f)
                {
                    growthGameHours = nextBoundary;
                    storedWater = Mathf.Max(0f, storedWater - 1f);
                    waterUnitsConsumed++;
                }
            }

            return waterUnitsConsumed;
        }
    }
}


// RainWaterMath
namespace TobaccoPotAndCigar.Runtime
{
    using UnityEngine;
public static class RainWaterMath
    {
        public const float UnitsPerIntensityGameHour = .3f;

        public static float Collect(float water, float intensity, float gameHours)
        {
            if (float.IsNaN(intensity) || float.IsInfinity(intensity) ||
                float.IsNaN(gameHours) || float.IsInfinity(gameHours)) return water;
            return Mathf.Clamp(water + Mathf.Max(0, intensity) *
                Mathf.Max(0, gameHours) * UnitsPerIntensityGameHour, 0, GrowthMath.Capacity);
        }
    }
}


// TobaccoPlantPotState
namespace TobaccoPotAndCigar.Runtime
{
    using System.Collections;
    using System.Collections.Generic;
    using UnityEngine;
public sealed class TobaccoPlantPotState : MonoBehaviour
    {
        [SerializeField] private TobaccoPlantVisual plantVisual;

        private ShipItem item;
        private Coroutine growthRoutine;
        private bool harvesting;
        private SaveablePrefab saveable;
        // Saved in the pot's otherwise unused extraValue0: 0 unknown, 1 sheltered, 2 exposed.
        private int rainExposure;
        private int displayedWater = -1;

        public bool IsRainExposed { get { return rainExposure == 2; } }

        public void ClearRainExposure() { rainExposure = 0; }

        public void ReadRainSave(SavePrefabData data)
        {
            rainExposure = data.extraValue0 == 1f ? 1 : data.extraValue0 == 2f ? 2 : 0;
        }

        public void WriteRainSave(SavePrefabData data) { data.extraValue0 = rainExposure; }

        private bool IsPlacedInWorld()
        {
            return item != null && item.sold && item.held == null &&
                gameObject.activeInHierarchy && saveable != null && saveable.currentCrateId == 0 &&
                item.itemRigidbodyC != null && item.GetCurrentInventorySlot() < 0;
        }

        public void CheckRainExposureOnPlacement()
        {
            rainExposure = 0;
            if (!IsPlacedInWorld()) return;
            MeshFilter potMesh = GetComponent<MeshFilter>();
            if (potMesh == null || potMesh.sharedMesh == null) return;
            Bounds bounds = potMesh.sharedMesh.bounds;
            Vector3 opening = transform.TransformPoint(new Vector3(bounds.center.x,
                bounds.max.y + .01f, bounds.center.z));
            // One exposure check per placement. Cast down from the sky to detect the
            // upward-facing triangles of thin roofs without changing global backface settings.
            // Exclude water/UI, the remote walking copy, player/boat capsules,
            // depth/invisible helpers, sail checkers, wind shadows and crate items.
            const int excluded = (1 << 2) | (1 << 4) | (1 << 5) | (1 << 8) |
                (1 << 11) | (1 << 13) | (1 << 14) | (1 << 16) | (1 << 21) |
                (1 << 22) | (1 << 23) | (1 << 24) | (1 << 26) | (1 << 27);
            Physics.SyncTransforms();
            rainExposure = HasRainCover(opening, Vector3.up, ~excluded, null) ? 1 : 2;
            if (rainExposure == 1) return;
            // Most native cabin roofs only collide on the separate walking copy.
            // Map the SAME world vertical ray into that boat's collision space;
            // never treat the remote copy as a roof over unrelated world items.
            Transform boat = item.currentActualBoat;
            Transform walk = item.currentWalkCol;
            if (boat == null || walk == null) return;
            Vector3 physicalOpening = walk.TransformPoint(boat.InverseTransformPoint(opening));
            Vector3 physicalUp = walk.TransformDirection(boat.InverseTransformDirection(Vector3.up)).normalized;
            if (HasRainCover(physicalOpening, physicalUp, -1, walk)) rainExposure = 1;
        }

        private bool HasRainCover(Vector3 opening, Vector3 up, int mask, Transform requiredParent)
        {
            RaycastHit[] hits = Physics.RaycastAll(opening + up * 1000f,
                -up, 1000f, mask, QueryTriggerInteraction.Ignore);
            foreach (RaycastHit hit in hits)
            {
                if (requiredParent != null && !hit.collider.transform.IsChildOf(requiredParent)) continue;
                if (hit.collider.transform.IsChildOf(transform) ||
                    hit.collider.GetComponentInParent<ItemRigidbody>() == item.itemRigidbodyC) continue;
                return true;
            }
            return false;
        }

        public float StoredWater
        {
            get { return item == null ? 0f : Mathf.Clamp(item.health, 0f, GrowthMath.Capacity); }
        }

        public float GrowthGameHours
        {
            get { return item == null ? 0f : Mathf.Clamp(item.amount, 0f, GrowthMath.RequiredGameHours); }
        }

        public int CompletedGrowthDays
        {
            get
            {
                return Mathf.Clamp(
                    Mathf.FloorToInt(GrowthGameHours / GrowthMath.GameHoursPerDay),
                    0,
                    GrowthMath.RequiredDays);
            }
        }

        public bool FullyGrown
        {
            get { return GrowthGameHours >= GrowthMath.RequiredGameHours; }
        }

        private void Awake()
        {
            item = GetComponent<ShipItem>();
            saveable = GetComponent<SaveablePrefab>();
            if (plantVisual == null)
                plantVisual = GetComponentInChildren<TobaccoPlantVisual>(true);
        }

        private void Start()
        {
            SyncFromSavedState();
        }

        private void OnEnable()
        {
            if (growthRoutine == null)
                growthRoutine = StartCoroutine(GrowUsingGameTime());
        }

        private void OnDisable()
        {
            if (growthRoutine == null)
                return;
            StopCoroutine(growthRoutine);
            growthRoutine = null;
        }

        public void SyncFromSavedState()
        {
            if (item == null)
                item = GetComponent<ShipItem>();
            if (item == null)
                return;

            item.health = Mathf.Clamp(item.health, 0f, GrowthMath.Capacity);
            item.amount = Mathf.Clamp(item.amount, 0f, GrowthMath.RequiredGameHours);
            if (plantVisual != null)
                plantVisual.SetHarvestReady(FullyGrown);
            RefreshDescription();
        }

        public bool TryAcceptWater(ShipItemBottle source)
        {
            if (source == null || !source.sold ||
                !Mathf.Approximately(source.amount, (float)LiquidType.water) ||
                source.health <= 0f || StoredWater >= GrowthMath.Capacity)
            {
                return false;
            }

            Good good = source.GetComponent<Good>();
            if (good != null && good.GetMissionIndex() > -1)
                return false;

            float transferred = Mathf.Min(
                source.health,
                GrowthMath.Capacity - item.health);
            source.health -= transferred;
            item.health += transferred;

            if (source.health <= 0f)
                source.EmptyBottle();
            else
                source.UpdateLookText();

            if (source.itemRigidbodyC != null)
                source.itemRigidbodyC.UpdateMass();
            if (UISoundPlayer.instance != null)
                UISoundPlayer.instance.PlayLiquidPourSound();
            RefreshDescription();
            return true;
        }

        public bool TryHarvest()
        {
            if (harvesting || item == null || !item.sold || !FullyGrown ||
                !PrefabReplacement.CanSpawn(RuntimeConstants.FreshTobaccoLeafPrefabIndex))
            {
                return false;
            }

            harvesting = true;
            List<ShipItem> spawnedLeaves = new List<ShipItem>(LeafStateRules.HarvestCount);
            GameObject leafPrefab = PrefabReplacement.ResolvePrefab(RuntimeConstants.FreshTobaccoLeafPrefabIndex);
            BoxCollider leafCollider = leafPrefab.GetComponent<BoxCollider>();
            float spacing = leafCollider != null ? Mathf.Max(leafCollider.size.x, leafCollider.size.z) + .04f : .45f;
            for (int i = 0; i < LeafStateRules.HarvestCount; i++)
            {
                Vector3 offset = transform.right * ((i - 1) * spacing) + transform.forward * .5f;
                ShipItem leaf = PrefabReplacement.SpawnOwned(
                    RuntimeConstants.FreshTobaccoLeafPrefabIndex,
                    transform.position + transform.up * 0.5f + offset,
                    transform.rotation * Quaternion.Euler(-90, 0, 0),
                    item,
                    0f,
                    0f);
                if (leaf != null)
                    spawnedLeaves.Add(leaf);
            }

            if (spawnedLeaves.Count != LeafStateRules.HarvestCount)
            {
                for (int i = 0; i < spawnedLeaves.Count; i++)
                    PrefabReplacement.ConsumeOwned(spawnedLeaves[i]);
                harvesting = false;
                RuntimeDiagnostics.Error("Tobacco harvest could not create all three leaves; " +
                                         "partial output was rolled back and growth was not reset.");
                return false;
            }

            item.amount = 0f;
            SyncFromSavedState();
            harvesting = false;
            return true;
        }

        private IEnumerator GrowUsingGameTime()
        {
            while (true)
            {
                Sun clock = Sun.sun;
                if (item != null && item.sold && clock != null &&
                    !Sun.SunPaused())
                {
                    AdvanceWaterAndGrowth(Time.deltaTime * clock.timescale);
                }

                yield return null;
            }
        }

        private void AdvanceWaterAndGrowth(float gameHours)
        {
            if (item == null || !item.sold || Sun.SunPaused()) return;
            if (IsPlacedInWorld() && !GameState.currentlyLoading && !GameState.justStarted &&
                !GameState.loadingBoatLocalItems && GameState.loadingScenes == 0)
            {
                // Newly spawned/old-save pots lack a snapshot. New saves retain it;
                // neither weather changes nor movement causes a repeated query.
                if (rainExposure == 0) CheckRainExposureOnPlacement();
                if (IsRainExposed)
                    item.health = RainWaterMath.Collect(item.health, GameState.rainIntensity, gameHours);
            }
            float water = item.health;
            float growth = item.amount;
            bool wasGrown = FullyGrown;
            int consumed = GrowthMath.Advance(ref water, ref growth, gameHours);
            item.health = water;
            item.amount = growth;
            if (consumed > 0 || (!wasGrown && FullyGrown)) SyncFromSavedState();
            else if (displayedWater != Mathf.RoundToInt(item.health)) RefreshDescription();
        }

        private void RefreshDescription()
        {
            if (item == null)
                return;
            displayedWater = Mathf.RoundToInt(item.health);
            item.description = "Water: " + item.health.ToString("0") + " / 15";
        }
    }
}


// TobaccoPlantVisual
namespace TobaccoPotAndCigar.Runtime
{
    using UnityEngine;
public sealed class TobaccoPlantVisual : MonoBehaviour
    {
        [SerializeField] private GameObject plantBase;
        [SerializeField] private GameObject harvestLeaves;

        public void SetHarvestReady(bool ready)
        {
            if (plantBase != null && !plantBase.activeSelf)
                plantBase.SetActive(true);
            if (harvestLeaves != null)
                harvestLeaves.SetActive(ready);
        }
    }
}
