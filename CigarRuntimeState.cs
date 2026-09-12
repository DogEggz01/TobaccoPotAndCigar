using System.Collections;
using UnityEngine;

namespace TobaccoPotAndCigar.Runtime
{
    public sealed class CigarRuntimeState : MonoBehaviour
    {
        [SerializeField] private CigarVisualController cigarVisual;

        private ShipItemPipe pipe;
        private int fullRecipeValue;
        private bool valueInitialized;
        private bool destructionQueued;
        private float ashClearedAt01 = 1f;
        private AshtrayState restingTray;
        private int restSlot;
        private int savedTrayId;
        private Transform restSocket;
        private bool started;
        public bool IsResting { get { return restingTray != null; } }
        public float AccumulatedAsh01
        {
            get
            {
                float initial = GetInitialHealth();
                return initial > 0 ? Mathf.Max(0, ashClearedAt01 - Mathf.Clamp01(pipe.health / initial)) : 0;
            }
        }

        public ShipItemPipe Pipe
        {
            get { return pipe; }
        }

        private void Awake()
        {
            pipe = GetComponent<ShipItemPipe>();
            if (cigarVisual == null)
                cigarVisual = GetComponentInChildren<CigarVisualController>(true);
            if (cigarVisual != null)
                cigarVisual.BindRoot(pipe);
        }

        private void Start()
        {
            started = true;
            RefreshValue();
            SyncVisuals(true, 0f, false);
            if (savedTrayId != 0) StartCoroutine(RestoreRest());
        }

        public bool TryGetBlend(out TobaccoBlend blend)
        {
            blend = default(TobaccoBlend);
            TobaccoRecipe recipe;
            if (!TryGetRecipe(out recipe))
                return false;
            blend = recipe.ToBlend();
            return true;
        }

        public bool TryGetRecipe(out TobaccoRecipe recipe)
        {
            recipe = default(TobaccoRecipe);
            bool wasLegacy;
            return pipe != null && TobaccoRecipeCodec.TryDecodeCigar(
                pipe.amount,
                out recipe,
                out wasLegacy);
        }

        public float GetInitialHealth()
        {
            TobaccoRecipe recipe;
            return TryGetRecipe(out recipe) ? recipe.Count * 100f : 0f;
        }

        public void SyncVisuals(bool snap, float heat01, bool inhaling)
        {
            if (pipe == null)
                pipe = GetComponent<ShipItemPipe>();
            if (pipe == null)
                return;

            float initial = GetInitialHealth();
            SyncValueFromHealth(initial);
            if (cigarVisual == null)
                return;

            float remaining = initial > 0f
                ? Mathf.Clamp01(pipe.health / initial)
                : 0f;
            cigarVisual.SetAshBaseline(ashClearedAt01);
            cigarVisual.SetRemaining01(
                remaining,
                pipe.health > 0f && initial > 0f,
                Mathf.Clamp01(heat01),
                inhaling,
                snap);
        }

        public void RefreshValue()
        {
            if (pipe == null)
                pipe = GetComponent<ShipItemPipe>();
            if (pipe == null)
                return;

            TobaccoRecipe recipe;
            if (!TryGetRecipe(out recipe))
            {
                fullRecipeValue = CigarValueMath.WrapperValue;
                valueInitialized = true;
                pipe.description = string.Empty;
                SyncValueFromHealth(0f);
                return;
            }

            TobaccoBlend blend = recipe.ToBlend();

            fullRecipeValue = CigarValueMath.Calculate(
                blend,
                GetPrefabValue(RuntimeConstants.WhiteTobaccoPrefabIndex),
                GetPrefabValue(RuntimeConstants.GreenTobaccoPrefabIndex),
                GetPrefabValue(RuntimeConstants.BlackTobaccoPrefabIndex),
                GetPrefabValue(RuntimeConstants.BrownTobaccoPrefabIndex),
                GetPrefabValue(RuntimeConstants.BlueTobaccoPrefabIndex));
            valueInitialized = true;
            pipe.description = CigarHintText.Build(recipe);
            SyncValueFromHealth(blend.Count * 100f);
        }

        public bool TryDestroyIfConsumed()
        {
            if (destructionQueued)
                return true;
            if (pipe == null)
                pipe = GetComponent<ShipItemPipe>();
            if (pipe == null || pipe.health > 0f)
                return false;

            destructionQueued = true;
            pipe.DestroyItem();
            return true;
        }

        private void SyncValueFromHealth(float initialHealth)
        {
            if (!valueInitialized || pipe == null)
                return;

            int remainingValue = CigarValueMath.ScaleByRemainingHealth(
                fullRecipeValue,
                pipe.health,
                initialHealth);
            if (pipe.value != remainingValue)
                pipe.value = remainingValue;
        }

        private static int GetPrefabValue(int prefabIndex)
        {
            PrefabsDirectory directory = PrefabsDirectory.instance;
            if (directory == null || directory.directory == null ||
                prefabIndex < 0 || prefabIndex >= directory.directory.Length ||
                directory.directory[prefabIndex] == null)
            {
                return 0;
            }

            ShipItem item =
                directory.directory[prefabIndex].GetComponent<ShipItem>();
            return item != null ? item.value : 0;
        }

        public void ClearAsh()
        {
            float initial = GetInitialHealth();
            ashClearedAt01 = initial > 0 ? Mathf.Clamp01(pipe.health / initial) : 0;
            if (cigarVisual != null) cigarVisual.SetAshBaseline(ashClearedAt01);
        }

        public void RestOn(AshtrayState tray, Transform socket, int slot)
        {
            LeaveRest();
            restingTray = tray;
            restSocket = socket;
            restSlot = slot;
            Vector3 offset = RestOffset();
            transform.SetPositionAndRotation(tray.transform.TransformPoint(offset), socket.rotation);
            pipe.ResetRigidbody();
            pipe.itemRigidbodyC.EnterBox(tray.transform, offset, socket.localRotation);
            SyncRestParent();
        }

        public void LeaveRest()
        {
            savedTrayId = 0;
            if (restingTray == null) return;
            if (pipe != null && pipe.itemRigidbodyC != null &&
                pipe.itemRigidbodyC.GetCurrentBox() == restingTray.transform)
                pipe.itemRigidbodyC.ExitBox();
            restingTray.Release(this);
            restingTray = null;
            restSocket = null;
        }

        private void LateUpdate() { if (IsResting) SyncRestParent(); }
        private void OnDestroy() { LeaveRest(); }

        public void SyncRestParent()
        {
            if (!IsResting) return;
            // Native EnterBox owns pose/physics. Carry its boat save context too.
            ShipItem tray = restingTray.Item;
            if (tray.GetComponent<SaveablePrefab>().currentCrateId != 0 ||
                (tray.itemRigidbodyC != null && tray.itemRigidbodyC.GetCurrentInventorySlot() != null))
            {
                LeaveRest();
                return;
            }
            pipe.currentActualBoat = tray.currentActualBoat;
            pipe.currentWalkCol = tray.currentWalkCol;
            GetComponent<SaveablePrefab>().SetParentObject(tray.GetComponent<SaveablePrefab>().GetParentObject());
            if (restSocket != null && pipe.itemRigidbodyC != null)
                pipe.itemRigidbodyC.EnterBox(tray.transform, RestOffset(), restSocket.localRotation);
        }

        private Vector3 RestOffset()
        {
            float initial = GetInitialHealth();
            float remaining = initial > 0 ? Mathf.Clamp01(pipe.health / initial) : 0;
            // Keep the middle of the remaining wrapper supported by the groove.
            return restSocket.localPosition + restSocket.localRotation * Vector3.up * (.116f * (1 - remaining));
        }

        public void WriteSave(SavePrefabData data)
        {
            if (IsResting) SyncRestParent();
            data.extraValue0 = ashClearedAt01;
            int id = IsResting ? restingTray.GetComponent<SaveablePrefab>().instanceId : savedTrayId;
            // Split the 31-bit native ID: a single float cannot preserve every ID.
            data.extraValue1 = id & 65535;
            data.extraValue2 = (id >> 16) & 32767;
            data.extraValue3 = restSlot;
            data.extraValue4 = 110;
        }

        public void ReadSave(SavePrefabData data)
        {
            ashClearedAt01 = data.extraValue4 == 110 && !float.IsNaN(data.extraValue0) &&
                !float.IsInfinity(data.extraValue0) ? Mathf.Clamp01(data.extraValue0) : 1f;
            savedTrayId = 0;
            if (data.extraValue4 == 110 && data.inventorySlot < 0 && data.crateId == 0 &&
                data.extraValue1 >= 0 && data.extraValue1 <= 65535 && data.extraValue1 % 1 == 0 &&
                data.extraValue2 >= 0 && data.extraValue2 <= 32767 && data.extraValue2 % 1 == 0 &&
                (data.extraValue3 == 0 || data.extraValue3 == 1))
            {
                savedTrayId = (int)data.extraValue1 | ((int)data.extraValue2 << 16);
                restSlot = (int)data.extraValue3;
            }
            SyncVisuals(true, 0, false);
            if (started && savedTrayId != 0) StartCoroutine(RestoreRest());
        }

        private IEnumerator RestoreRest()
        {
            while (GameState.currentlyLoading) yield return null;
            // Native item rigidbodies and all saved tray IDs may initialize later.
            for (int frame = 0; frame < 120 && savedTrayId != 0; frame++)
            {
                if (SaveLoadManager.instance != null)
                    foreach (SaveablePrefab saved in SaveLoadManager.instance.GetCurrentPrefabs())
                        if (saved != null && saved.instanceId == savedTrayId)
                        {
                            AshtrayState tray = saved.GetComponent<AshtrayState>();
                            if (tray != null && tray.TrySeat(this, restSlot)) { savedTrayId = 0; yield break; }
                        }
                yield return null;
            }
            // Missing/occupied tray leaves the independently saved cigar recoverable.
            savedTrayId = 0;
        }
    }
}
