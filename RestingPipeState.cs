using System.Collections;
using UnityEngine;

namespace TobaccoPotAndCigar.Runtime
{
    /// <summary>Native pipe seating, without replacing tobacco, smoking or save fields.</summary>
    public sealed class RestingPipeState : MonoBehaviour
    {
        private ShipItemPipe pipe;
        private AshtrayState tray;
        private Transform socket;
        private int savedTrayId;
        private bool started;
        public ShipItemPipe Pipe { get { return pipe != null ? pipe : (pipe = GetComponent<ShipItemPipe>()); } }
        public bool IsResting { get { return tray != null && socket != null; } }

        public static int NativePipeSlot(ShipItemPipe item)
        {
            SaveablePrefab save = item != null ? item.GetComponent<SaveablePrefab>() : null;
            int index = save != null ? save.prefabIndex : -1;
            return index >= RuntimeConstants.FirstVanillaPipePrefabIndex && index <= RuntimeConstants.LastVanillaPipePrefabIndex
                ? index - RuntimeConstants.FirstVanillaPipePrefabIndex : -1;
        }

        public static RestingPipeState Ensure(ShipItemPipe item)
        {
            if (NativePipeSlot(item) < 0) return null;
            RestingPipeState state = item.GetComponent<RestingPipeState>();
            return state != null ? state : item.gameObject.AddComponent<RestingPipeState>();
        }

        private void Start()
        {
            started = true;
            if (savedTrayId != 0) StartCoroutine(RestoreRest());
        }

        public void RestOn(AshtrayState target, Transform rest)
        {
            LeaveRest(); tray = target; socket = rest;
            transform.SetPositionAndRotation(rest.position, rest.rotation);
            Pipe.ResetRigidbody();
            Pipe.itemRigidbodyC.EnterBox(tray.transform, socket.localPosition, socket.localRotation);
            SyncRestParent();
        }

        public void LeaveRest(bool drop = false)
        {
            savedTrayId = 0;
            if (tray == null) return;
            if (Pipe.itemRigidbodyC != null && Pipe.itemRigidbodyC.GetCurrentBox() == tray.transform)
                Pipe.itemRigidbodyC.ExitBox();
            tray.ReleasePipe(this); tray = null; socket = null;
            if (drop && Pipe.itemRigidbodyC != null)
            {
                Rigidbody body = Pipe.itemRigidbodyC.GetBody();
                if (body != null) { Pipe.ResetRigidbody(); body.isKinematic = false; body.WakeUp(); }
            }
        }

        public void SyncRestParent()
        {
            if (!IsResting) return;
            ShipItem target = tray.Item;
            if (Pipe.held != null || target.held != null || target.GetComponent<SaveablePrefab>().currentCrateId != 0 ||
                (target.itemRigidbodyC != null && target.itemRigidbodyC.GetCurrentInventorySlot() != null))
            { LeaveRest(target.held != null); return; }
            Pipe.currentActualBoat = target.currentActualBoat; Pipe.currentWalkCol = target.currentWalkCol;
            GetComponent<SaveablePrefab>().SetParentObject(target.GetComponent<SaveablePrefab>().GetParentObject());
            if (Pipe.itemRigidbodyC != null)
                Pipe.itemRigidbodyC.EnterBox(tray.transform, socket.localPosition, socket.localRotation);
        }

        private void LateUpdate() { if (IsResting) SyncRestParent(); }
        private void OnDisable() { LeaveRest(); }

        public void WriteSave(SavePrefabData data)
        {
            int id = IsResting ? tray.GetComponent<SaveablePrefab>().instanceId : savedTrayId;
            // Native pipes leave these extra fields unused. Health and amount
            // retain the native tobacco quantity/type. Split IDs for float precision.
            data.extraValue1 = id & 65535; data.extraValue2 = (id >> 16) & 32767;
            data.extraValue4 = 120;
        }

        public void ReadSave(SavePrefabData data)
        {
            savedTrayId = 0;
            if (data.extraValue4 == 120 && data.inventorySlot < 0 && data.crateId == 0 &&
                data.extraValue1 >= 0 && data.extraValue1 <= 65535 && data.extraValue1 % 1 == 0 &&
                data.extraValue2 >= 0 && data.extraValue2 <= 32767 && data.extraValue2 % 1 == 0)
                savedTrayId = (int)data.extraValue1 | ((int)data.extraValue2 << 16);
            if (started && savedTrayId != 0) StartCoroutine(RestoreRest());
        }

        private IEnumerator RestoreRest()
        {
            while (GameState.currentlyLoading) yield return null;
            for (int frame = 0; frame < 120 && savedTrayId != 0; frame++)
            {
                if (SaveLoadManager.instance != null)
                    foreach (SaveablePrefab saved in SaveLoadManager.instance.GetCurrentPrefabs())
                        if (saved != null && saved.instanceId == savedTrayId)
                        {
                            AshtrayState target = saved.GetComponent<AshtrayState>();
                            if (target != null && target.TrySeatPipe(Pipe)) { savedTrayId = 0; yield break; }
                        }
                yield return null;
            }
            savedTrayId = 0;
        }
    }
}
