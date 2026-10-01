// Pipe rack — grouped mod source. Existing type identities are preserved.

// PipeRackState
namespace TobaccoPotAndCigar.Runtime
{
    using System.Collections;
    using UnityEngine;

    public sealed class PipeRackState : DisplayStorageState
    {
        [SerializeField] private bool wallRack;
        [SerializeField] private Transform ashtraySeat;
        [SerializeField] private Transform jarTarget;
        [SerializeField] private float[] swingClearance;
        private Vector3 lastPosition, lastVelocity;
        private Quaternion lastRotation;
        private bool motionInitialized;
        private Coroutine openingJar;
        private readonly Vector2[] angle = new Vector2[3], velocity = new Vector2[3];
        public bool IsWallRack { get { return wallRack; } }
        public bool HasJar { get { return jarTarget != null; } }
        public override int Capacity { get { return ashtraySeat != null ? 4 : 3; } }
        protected override bool ExtinguishOnSeat { get { return wallRack; } }
        public void Configure(bool wall, Transform tray, Transform jar, float[] clearance)
        { wallRack = wall; ashtraySeat = tray; jarTarget = jar; swingClearance = clearance; }
        public override bool Accepts(ShipItem held, int slot)
        { return slot == 3 ? ashtraySeat != null && held.GetComponent<AshtrayState>() != null : RestingPipeState.NativePipeSlot(held as ShipItemPipe) >= 0; }
        public override Transform Seat(int slot, ShipItem held)
        { return slot == 3 ? ashtraySeat : seats[slot * 3 + RestingPipeState.NativePipeSlot(held as ShipItemPipe)]; }
        public bool AimingAtJar(Vector3 point)
        { return HasJar && (jarTarget.InverseTransformPoint(point)).sqrMagnitude < .17f * .17f; }
        public bool RayAtJar(Ray ray, out Vector3 point)
        {
            point = Vector3.zero; if (!HasJar) return false;
            Ray local = new Ray(jarTarget.InverseTransformPoint(ray.origin), jarTarget.InverseTransformDirection(ray.direction));
            float distance;
            if (!new Bounds(Vector3.zero, new Vector3(.27f, .34f, .27f)).IntersectRay(local, out distance) || distance > 1.8f) return false;
            point = ray.GetPoint(distance); return true;
        }
        public void OpenJar()
        {
            if (Available && HasJar && CrateInventoryUI.instance != null && openingJar == null)
                openingJar = StartCoroutine(OpenJarAfterInput());
        }
        private IEnumerator OpenJarAfterInput()
        {
            // Let the opening R press expire before native crate UI tests the same key.
            yield return null;
            yield return null;
            openingJar = null;
            if (Available && CrateInventoryUI.instance != null) GetComponent<CrateInventory>().OpenCrate();
        }
        public void PreparePickup()
        {
            if (openingJar != null) { StopCoroutine(openingJar); openingJar = null; }
            var ui = CrateInventoryUI.instance;
            if (HasJar && ui != null && ui.showingUI && ui.currentCrate == GetComponent<CrateInventory>()) ui.HideInventory(false);
        }
        protected override void OnDestroy() { PreparePickup(); base.OnDestroy(); }
        private void FixedUpdate()
        {
            if (!wallRack || !GameState.playing || GameState.currentlyLoading) { motionInitialized = false; return; }
            StepSwing(Time.fixedDeltaTime);
        }
        // A bounded damped pendulum avoids fighting the native boat-space rigidbody.
        // Offline surface-clearance samples keep each original-size bowl supported as it rocks.
        public void StepSwing(float dt)
        {
            if (dt <= 0) return;
            if (!motionInitialized)
            { lastPosition = transform.position; lastRotation = transform.rotation; lastVelocity = Vector3.zero; motionInitialized = true; return; }
            Vector3 displacement = transform.position - lastPosition;
            if (displacement.sqrMagnitude > 4) { lastPosition = transform.position; lastVelocity = Vector3.zero; lastRotation = transform.rotation; return; }
            Vector3 speed = displacement / dt;
            Vector3 acceleration = transform.InverseTransformDirection((speed - lastVelocity) / dt);
            Quaternion delta = Quaternion.Inverse(lastRotation) * transform.rotation;
            Vector3 turn = delta.eulerAngles;
            turn = new Vector3(Mathf.DeltaAngle(0, turn.x), 0, Mathf.DeltaAngle(0, turn.z));
            Vector3 gravity = transform.InverseTransformDirection(Vector3.down);
            Vector2 forcing = new Vector2(gravity.z * 55 - acceleration.z * 1.2f - turn.x * 2, -gravity.x * 55 + acceleration.x * 1.2f - turn.z * 2);
            forcing = Vector2.ClampMagnitude(forcing, 100);
            for (int i = 0; i < 3; i++)
            {
                float frequency = 27 + i * 2;
                velocity[i] += (forcing - angle[i] * frequency - velocity[i] * 4.5f) * dt;
                angle[i] += velocity[i] * dt;
                for (int axis = 0; axis < 2; axis++) if (Mathf.Abs(angle[i][axis]) > 2)
                { angle[i][axis] = Mathf.Clamp(angle[i][axis], -2, 2); velocity[i][axis] *= -.18f; }
            }
            lastPosition = transform.position; lastVelocity = speed; lastRotation = transform.rotation;
        }
        public Vector2 SwingAngle(int slot) { return angle[slot]; }
        public override void Pose(int slot, ShipItem held, out Vector3 position, out Quaternion rotation)
        {
            base.Pose(slot, held, out position, out rotation);
            if (!wallRack || slot >= 3) return;
            Vector2 a = angle[slot]; Quaternion rock = Quaternion.Euler(a.x, 0, a.y);
            Transform socket = Seat(slot, held);
            Vector3 localPivot = socket.localPosition + socket.localRotation * new Vector3(0, .02f, .015f);
            Vector3 localPosition = localPivot + rock * (socket.localPosition - localPivot);
            float lift = Clearance(RestingPipeState.NativePipeSlot(held as ShipItemPipe), a);
            position = transform.TransformPoint(localPosition + Vector3.up * lift);
            rotation = transform.rotation * rock * socket.localRotation;
        }
        private float Clearance(int type, Vector2 a)
        {
            if (swingClearance == null || swingClearance.Length != 243) return 0;
            float x = Mathf.Clamp(a.x * 2 + 4, 0, 8), y = Mathf.Clamp(a.y * 2 + 4, 0, 8);
            int ix = Mathf.Min((int)x, 7), iy = Mathf.Min((int)y, 7), start = type * 81 + iy * 9 + ix;
            return Mathf.Lerp(Mathf.Lerp(swingClearance[start], swingClearance[start+1], x-ix),
                Mathf.Lerp(swingClearance[start+9], swingClearance[start+10], x-ix), y-iy);
        }
    }
}


// The Al'Ankh rack's tobacco jar reuses the native crate interface at a 4x4 size.
namespace TobaccoPotAndCigar.Patches
{
    using HarmonyLib;
    using UnityEngine;
    using TobaccoPotAndCigar.Runtime;

    [HarmonyPatch(typeof(CrateInventoryUI), "GetCrateDimensions")]
    internal static class RackJarDimensionsPatch
    {
        internal static bool IsJar(CrateInventory crate) { var rack = crate != null ? crate.GetComponent<PipeRackState>() : null; return rack != null && rack.HasJar; }
        [HarmonyPrefix]
        private static bool Prefix(CrateInventoryUI __instance, ref Vector2 __result)
        { if (!IsJar(__instance.currentCrate)) return true; __result = new Vector2(4, 4); return false; }
    }

    [HarmonyPatch(typeof(CrateInventory), "LateUpdate")]
    internal static class RackJarContentsPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(CrateInventory __instance)
        {
            if (!RackJarDimensionsPatch.IsJar(__instance)) return true;
            var owner = __instance.GetComponent<ShipItem>(); var save = owner.GetComponent<SaveablePrefab>();
            bool showing = CrateInventoryUI.instance != null && CrateInventoryUI.instance.showingUI && CrateInventoryUI.instance.currentCrate == __instance;
            foreach (var item in __instance.containedItems)
            {
                if (item == null || item.itemRigidbodyC == null) continue;
                item.currentActualBoat = owner.currentActualBoat; item.currentWalkCol = owner.currentWalkCol;
                item.GetComponent<SaveablePrefab>().SetParentObject(save.GetParentObject());
                if (!showing) item.transform.SetPositionAndRotation(owner.transform.position, owner.transform.rotation);
                if (item.currentActualBoat != null && item.currentWalkCol != null) item.itemRigidbodyC.ForceRigidbodyToWalkCol();
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(CrateInventoryUI), "RefreshButtons")]
    internal static class RackJarLayoutPatch
    {
        private static readonly AccessTools.FieldRef<CrateInventoryUI, Transform> Tracker = AccessTools.FieldRefAccess<CrateInventoryUI, Transform>("localPosTracker");
        private static CrateInventoryUI scaledUI;
        private static Vector3 originalScale;
        [HarmonyPostfix]
        private static void Postfix(CrateInventoryUI __instance)
        {
            if (!RackJarDimensionsPatch.IsJar(__instance.currentCrate)) { Restore(__instance); return; }
            if (scaledUI != __instance) { scaledUI = __instance; originalScale = __instance.transform.localScale; }
            __instance.transform.localScale = originalScale * .5f;
            for (int i = 0; i < __instance.buttons.Length; i++)
            {
                __instance.buttons[i].gameObject.SetActive(i < 16);
                if (i < 16) __instance.buttons[i].transform.localPosition = new Vector3(.75f - (i % 4) * .5f, -.75f + (i / 4) * .5f, 0);
            }
            // Items are positioned in world space by CrateInventoryButton. Their normal
            // inventoryScale * .33 display scale is deliberately left untouched.
            if (__instance.showingUI) Position(__instance);
        }
        internal static void Restore(CrateInventoryUI ui)
        { if (scaledUI == ui) { ui.transform.localScale = originalScale; scaledUI = null; } }
        internal static void Position(CrateInventoryUI ui)
        {
            if (!RackJarDimensionsPatch.IsJar(ui.currentCrate) || Camera.main == null) return;
            Transform camera = Camera.main.transform;
            Bounds rack = ui.currentCrate.GetComponent<Collider>().bounds;
            Vector3 direction = camera.forward;
            float extent = Vector3.Dot(rack.extents, new Vector3(Mathf.Abs(direction.x), Mathf.Abs(direction.y), Mathf.Abs(direction.z)));
            float nearFace = Vector3.Dot(rack.center - camera.position, direction) - extent;
            float distance = Mathf.Clamp(nearFace - .12f, .30f, .65f);
            // Vanilla opens at one metre. Compensate the UI geometry for its new
            // distance to preserve half the apparent dimensions, not a magnified
            // half-size board. Native tobacco world/display scale stays untouched.
            ui.transform.localScale = originalScale * (.5f * distance);
            ui.transform.position = camera.position + direction * distance + camera.up * (.07f * distance);
            ui.transform.LookAt(camera.position);
            var tracker = Tracker(ui);
            tracker.SetPositionAndRotation(ui.transform.position, ui.transform.rotation);
        }
    }
    [HarmonyPatch(typeof(CrateInventoryUI), "ShowInventory")]
    internal static class RackJarPositionPatch
    {
        [HarmonyPostfix] private static void Postfix(CrateInventoryUI __instance) { if (__instance.showingUI) RackJarLayoutPatch.Position(__instance); }
    }
    [HarmonyPatch(typeof(CrateInventoryUI), "HideInventory")]
    internal static class RackJarClosePatch
    {
        [HarmonyPostfix] private static void Postfix(CrateInventoryUI __instance) { RackJarLayoutPatch.Restore(__instance); }
    }
    [HarmonyPatch(typeof(CrateInventoryButton), "OnActivate")]
    internal static class RackJarInsertPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(GoPointer activatingPointer)
        {
            if (CrateInventoryUI.instance == null || !RackJarDimensionsPatch.IsJar(CrateInventoryUI.instance.currentCrate)) return true;
            var held = activatingPointer.GetHeldItem() as ShipItem;
            return held == null || (CrateInventoryUI.instance.currentCrate.containedItems.Count < 16 &&
                (held is ShipItemTobacco || held.GetComponent<DriedTobaccoLeafState>() != null));
        }
    }
}
