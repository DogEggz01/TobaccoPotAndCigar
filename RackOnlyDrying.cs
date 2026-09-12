using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TobaccoPotAndCigar.Runtime
{
    public sealed class RackOnlyDrying : MonoBehaviour
    {
        public const float LooseTobaccoRequiredGameHours = 72f;
        public const float FreshLeafRequiredGameHours = 144f;

        [SerializeField] private int resultPrefabIndex;

        private readonly HashSet<DryingRackCol> racks =
            new HashSet<DryingRackCol>();
        private ShipItem item;
        private Coroutine dryingRoutine;

        public int ResultPrefabIndex
        {
            get { return resultPrefabIndex; }
        }

        public float CurrentRequiredGameHours
        {
            get { return GetRequiredGameHours(); }
        }

        private void Awake()
        {
            item = GetComponent<ShipItem>();
        }

        private void Start()
        {
            SyncFromSavedState();
        }

        private void OnDisable()
        {
            StopDrying();
            racks.Clear();
        }

        private void OnTriggerEnter(Collider other)
        {
            RegisterRack(other);
        }

        private void OnTriggerStay(Collider other)
        {
            RegisterRack(other);
        }

        private void OnTriggerExit(Collider other)
        {
            DryingRackCol rack = FindRack(other);
            if (rack == null || !racks.Remove(rack))
                return;
            if (racks.Count == 0)
                StopDrying();
        }

        public void ConfigureResultPrefab(int newResultPrefabIndex)
        {
            resultPrefabIndex = newResultPrefabIndex;
        }

        public void SyncFromSavedState()
        {
            if (item == null)
                item = GetComponent<ShipItem>();
            if (item == null)
                return;
            item.health = Mathf.Clamp(
                item.health,
                0f,
                GetRequiredGameHours());
            FreshTobaccoLeafState freshState =
                item.GetComponent<FreshTobaccoLeafState>();
            if (freshState != null)
            {
                freshState.SyncDryingVisual();
                resultPrefabIndex = 0;
                if (freshState.IsFullyDry) enabled = false;
            }
        }

        private void RegisterRack(Collider other)
        {
            // Unity can deliver trigger callbacks to disabled behaviours.
            if (!enabled) return;
            DryingRackCol rack = FindRack(other);
            if (rack != null && racks.Add(rack) && dryingRoutine == null)
                dryingRoutine = StartCoroutine(DryWhileRacked());
        }

        private static DryingRackCol FindRack(Collider other)
        {
            if (other == null)
                return null;
            DryingRackCol rack = other.GetComponent<DryingRackCol>();
            return rack != null ? rack : other.GetComponentInParent<DryingRackCol>();
        }

        private IEnumerator DryWhileRacked()
        {
            while (racks.Count > 0)
            {
                Sun clock = Sun.sun;
                if (item != null && item.sold && clock != null &&
                    !Sun.SunPaused() && (resultPrefabIndex > 0 || item.GetComponent<FreshTobaccoLeafState>() != null))
                {
                    float requiredGameHours = GetRequiredGameHours();
                    item.health = Mathf.Min(
                        requiredGameHours,
                        item.health + Time.deltaTime * clock.timescale);
                    FreshTobaccoLeafState freshState =
                        item.GetComponent<FreshTobaccoLeafState>();
                    if (freshState != null)
                        freshState.SyncDryingVisual();
                    if (item.health >= requiredGameHours)
                    {
                        if (freshState != null)
                        {
                            resultPrefabIndex = 0;
                            dryingRoutine = null;
                            enabled = false;
                            yield break;
                        }
                        if (TryAdvanceLooseTobaccoInPlace())
                        {
                            if (resultPrefabIndex <= 0)
                            {
                                dryingRoutine = null;
                                enabled = false;
                                yield break;
                            }

                            yield return null;
                            continue;
                        }

                        RuntimeDiagnostics.Error(
                            "Could not advance rack drying from prefab " +
                            GetCurrentPrefabIndex() + " to " +
                            resultPrefabIndex +
                            " in place. The physical item was preserved; " +
                            "replacement spawning was not attempted.");
                        dryingRoutine = null;
                        enabled = false;
                        yield break;
                    }
                }
                yield return null;
            }
            dryingRoutine = null;
        }

        private bool TryAdvanceLooseTobaccoInPlace()
        {
            ShipItemTobacco sourceTobacco = item as ShipItemTobacco;
            SaveablePrefab saveable = item != null
                ? item.GetComponent<SaveablePrefab>()
                : null;
            if (sourceTobacco == null || saveable == null ||
                !TobaccoDryingChain.IsStageTransition(
                    saveable.prefabIndex,
                    resultPrefabIndex))
            {
                return false;
            }

            GameObject targetPrefab;
            ShipItem targetItem;
            Renderer targetRenderer;
            if (!TryResolveTarget(
                    resultPrefabIndex,
                    out targetPrefab,
                    out targetItem,
                    out targetRenderer))
                return false;
            ShipItemTobacco targetTobacco = targetPrefab != null
                ? targetPrefab.GetComponent<ShipItemTobacco>()
                : null;
            Renderer sourceRenderer = item.GetComponent<Renderer>();
            if (targetTobacco == null || sourceRenderer == null ||
                targetRenderer == null)
            {
                return false;
            }

            int completedPrefabIndex = resultPrefabIndex;
            CopyRenderer(sourceRenderer, targetRenderer);
            MeshFilter sourceMesh = item.GetComponent<MeshFilter>();
            MeshFilter targetMesh = targetPrefab.GetComponent<MeshFilter>();
            if (sourceMesh != null && targetMesh != null)
                sourceMesh.sharedMesh = targetMesh.sharedMesh;

            item.gameObject.name = targetPrefab.name + "(Clone)";
            CopyItemPresentation(item, targetItem);
            item.amount = targetTobacco.amount;
            item.health = 0f;
            sourceTobacco.tobaccoType = targetTobacco.tobaccoType;
            saveable.prefabIndex = completedPrefabIndex;
            if (item.itemRigidbodyC != null)
                item.itemRigidbodyC.UpdateMass();

            int nextPrefabIndex;
            resultPrefabIndex = TobaccoDryingChain.TryGetNextStage(
                completedPrefabIndex,
                out nextPrefabIndex)
                ? nextPrefabIndex
                : 0;

            RuntimeDiagnostics.Info(
                "Advanced loose tobacco to prefab " + completedPrefabIndex +
                " in place while preserving its rack contact and save identity.");
            return true;
        }

        private static bool TryResolveTarget(
            int prefabIndex,
            out GameObject targetPrefab,
            out ShipItem targetItem,
            out Renderer targetRenderer)
        {
            targetPrefab = null;
            targetItem = null;
            targetRenderer = null;
            PrefabsDirectory directory = PrefabsDirectory.instance;
            if (directory == null || directory.directory == null ||
                prefabIndex < 0 || prefabIndex >= directory.directory.Length)
            {
                return false;
            }

            targetPrefab = directory.directory[prefabIndex];
            if (targetPrefab == null)
                return false;
            targetItem = targetPrefab.GetComponent<ShipItem>();
            targetRenderer = targetPrefab.GetComponent<Renderer>();
            return targetItem != null && targetRenderer != null;
        }

        private int GetCurrentPrefabIndex()
        {
            SaveablePrefab saveable = item != null
                ? item.GetComponent<SaveablePrefab>()
                : null;
            return saveable != null ? saveable.prefabIndex : -1;
        }

        private float GetRequiredGameHours()
        {
            return item != null &&
                   item.GetComponent<FreshTobaccoLeafState>() != null
                ? FreshLeafRequiredGameHours
                : LooseTobaccoRequiredGameHours;
        }

        private static void CopyRenderer(Renderer target, Renderer source)
        {
            target.sharedMaterials = source.sharedMaterials;
            target.enabled = source.enabled;
            target.shadowCastingMode = source.shadowCastingMode;
            target.receiveShadows = source.receiveShadows;
            target.lightProbeUsage = source.lightProbeUsage;
            target.reflectionProbeUsage = source.reflectionProbeUsage;
        }

        private static void CopyItemPresentation(ShipItem target, ShipItem source)
        {
            target.name = source.name;
            target.description = source.description;
            target.mass = source.mass;
            target.value = source.value;
            target.category = source.category;
            target.inventoryScale = source.inventoryScale;
            target.inventoryRotation = source.inventoryRotation;
            target.inventoryRotationX = source.inventoryRotationX;
            target.floaterHeight = source.floaterHeight;
            target.wallAttachment = source.wallAttachment;
            target.delayLook = source.delayLook;
            target.big = source.big;
            target.holdDistance = source.holdDistance;
            target.furniturePlaceHeight = source.furniturePlaceHeight;
            target.holdHeight = source.holdHeight;
            target.itemClickDistance = source.itemClickDistance;
        }

        private void StopDrying()
        {
            if (dryingRoutine == null)
                return;
            StopCoroutine(dryingRoutine);
            dryingRoutine = null;
        }
    }
}
