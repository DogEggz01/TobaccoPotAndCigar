using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TobaccoPotAndCigar.Runtime
{
    public sealed class TobaccoPlantPotState : MonoBehaviour
    {
        [SerializeField] private TobaccoPlantVisual plantVisual;

        private ShipItem item;
        private Coroutine growthRoutine;
        private bool harvesting;

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
                    !Sun.SunPaused() && !FullyGrown && item.health >= 1f)
                {
                    float water = item.health;
                    float growth = item.amount;
                    int consumed = GrowthMath.Advance(
                        ref water,
                        ref growth,
                        Time.deltaTime * clock.timescale);
                    item.health = water;
                    item.amount = growth;
                    if (consumed > 0 || FullyGrown)
                        SyncFromSavedState();
                }

                yield return null;
            }
        }

        private void RefreshDescription()
        {
            if (item == null)
                return;
            item.description = "Water: " + item.health.ToString("0") + " / 15";
        }
    }
}
