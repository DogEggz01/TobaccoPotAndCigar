// Ashtray — grouped mod source. Existing type identities are preserved.

// Storage/rack ownership is shared by all seven displays; occupants remain independently saved items.
namespace TobaccoPotAndCigar.Runtime
{
    using System;
    using System.Collections;
    using UnityEngine;

    public abstract class DisplayStorageState : MonoBehaviour
    {
        [SerializeField] protected Transform[] seats;
        protected SecuredDisplayItem[] contents;
        private ShipItem item;
        private SaveablePrefab identity;
        private StoragePointerTarget pointerTarget;
        private Collider[] interactionColliders;
        public ShipItem Item { get { return item != null ? item : (item = GetComponent<ShipItem>()); } }
        public SaveablePrefab Identity { get { return identity != null ? identity : (identity = GetComponent<SaveablePrefab>()); } }
        public abstract int Capacity { get; }
        public void ConfigureSeats(Transform[] value) { seats = value; }
        private void LateUpdate()
        {
            // Vanilla changes only the root layer on pickup/drop. The moving lid
            // must follow it, including after native inventory-slot withdrawal.
            if (interactionColliders == null) interactionColliders = GetComponentsInChildren<Collider>(true);
            foreach (var collider in interactionColliders)
                if (collider != null) collider.gameObject.layer = gameObject.layer;
        }
        public void SyncContents()
        {
            EnsureContents();
            foreach (var content in contents) if (content != null) content.Sync();
        }
        public SecuredDisplayItem GetContent(int slot)
        { EnsureContents(); return slot >= 0 && slot < contents.Length ? contents[slot] : null; }
        protected void EnsureContents() { if (contents == null) contents = new SecuredDisplayItem[Capacity]; }
        public virtual bool CanAccess(int slot) { return slot >= 0 && slot < Capacity; }
        public virtual Transform Seat(int slot, ShipItem held) { return seats[slot]; }
        public abstract bool Accepts(ShipItem held, int slot);
        protected virtual bool ExtinguishOnSeat { get { return false; } }
        public bool Available
        {
            get { return Item.sold && gameObject.activeInHierarchy && !Item.held && Identity.currentCrateId == 0 &&
                Item.itemRigidbodyC != null && Item.itemRigidbodyC.GetCurrentInventorySlot() == null; }
        }
        public virtual int NearestEmpty(ShipItem held, Vector3 point)
        {
            EnsureContents(); int best = -1; float distance = float.MaxValue;
            for (int slot = 0; slot < Capacity; slot++)
            {
                if (contents[slot] != null || !CanAccess(slot) || !Accepts(held, slot)) continue;
                Transform pose = Seat(slot, held);
                float d = (pose.position - point).sqrMagnitude;
                if (d < distance) { best = slot; distance = d; }
            }
            return best;
        }
        public bool TrySeat(ShipItem held, int slot, bool restoring = false)
        {
            EnsureContents();
            if (held == null || held == Item || slot < 0 || slot >= Capacity || contents[slot] != null ||
                !Accepts(held, slot) || held.itemRigidbodyC == null || !held.sold || held.nailed ||
                (!restoring && (!Available || !CanAccess(slot)))) return false;
            var existing = held.GetComponent<SecuredDisplayItem>();
            if (existing != null && existing.Owner != null) return false;
            if (held.held != null) held.held.DropItem();
            var cigar = held.GetComponent<CigarRuntimeState>(); if (cigar != null) cigar.LeaveRest();
            var pipe = held.GetComponent<RestingPipeState>(); if (pipe != null) pipe.LeaveRest();
            var state = existing != null ? existing : held.gameObject.AddComponent<SecuredDisplayItem>();
            contents[slot] = state; state.Attach(this, slot);
            if (!restoring && ExtinguishOnSeat) StoragePipeHeat.Extinguish(held as ShipItemPipe);
            return true;
        }
        public bool TryPlace(ShipItem held, Vector3 point)
        { int slot = NearestEmpty(held, point); return slot >= 0 && TrySeat(held, slot); }
        internal void Release(SecuredDisplayItem state)
        { EnsureContents(); for (int i = 0; i < contents.Length; i++) if (contents[i] == state) contents[i] = null; }
        public virtual void Pose(int slot, ShipItem held, out Vector3 position, out Quaternion rotation)
        { Transform socket = Seat(slot, held); position = socket.position; rotation = socket.rotation; }
        public StoragePointerTarget Target(int slot, Vector3 point, GoPointer pointer)
        {
            if (pointerTarget == null)
            {
                var host = new GameObject("Storage interaction"); host.transform.SetParent(transform, false);
                host.AddComponent<MeshRenderer>().enabled = false;
                pointerTarget = host.AddComponent<StoragePointerTarget>(); pointerTarget.Owner = this;
            }
            pointerTarget.Slot = slot; pointerTarget.Point = point; pointerTarget.Refresh(pointer);
            return pointerTarget;
        }
        protected virtual void OnDestroy()
        {
            if (contents == null) return;
            for (int i = 0; i < contents.Length; i++) if (contents[i] != null) contents[i].Detach(true);
        }
    }

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

    [DefaultExecutionOrder(1000)]
    public sealed class SecuredDisplayItem : MonoBehaviour
    {
        public DisplayStorageState Owner { get; private set; }
        public int Slot { get; private set; }
        private ShipItem item;
        private SaveablePrefab identity;
        private Transform[] layers;
        private int lastLayer = -1;
        private int savedOwner;
        private bool started;
        private bool inventoryHidden;
        public ShipItem Item { get { return item != null ? item : (item = GetComponent<ShipItem>()); } }
        private void Start() { started = true; if (savedOwner != 0) StartCoroutine(Restore()); }
        public void Attach(DisplayStorageState owner, int slot)
        {
            Owner = owner; Slot = slot; savedOwner = 0;
            if (layers == null) layers = GetComponentsInChildren<Transform>(true);
            Item.itemRigidbodyC.attached = true; Item.itemRigidbodyC.inStove = true; Item.itemRigidbodyC.disableCol = true;
            Item.ToggleDisallowDisembarking(true); Sync();
        }
        public void Detach(bool drop = false)
        {
            savedOwner = 0;
            if (Owner == null) return;
            DisplayStorageState owner = Owner; Owner = null; owner.Release(this);
            Item.ToggleDisallowDisembarking(false);
            SetLayer(Item.held != null ? 2 : 0); transform.localScale = Vector3.one;
            if (Item.itemRigidbodyC == null) return;
            Item.itemRigidbodyC.ExitBox(); Item.itemRigidbodyC.attached = false; Item.itemRigidbodyC.inStove = false; Item.itemRigidbodyC.disableCol = false;
            if (inventoryHidden && Item.itemRigidbodyC.GetCurrentInventorySlot() == null) Item.GetComponent<Collider>().enabled = true;
            inventoryHidden = false;
            Item.ResetRigidbody();
            if (Item.currentActualBoat != null && Item.currentWalkCol != null) Item.itemRigidbodyC.ForceRigidbodyToWalkCol();
            var body = Item.itemRigidbodyC.GetBody();
            if (body != null && drop) { body.isKinematic = false; body.WakeUp(); }
        }
        private void SetLayer(int layer)
        { if (lastLayer == layer) return; if (layers == null) layers = GetComponentsInChildren<Transform>(true); foreach (Transform t in layers) if (t != null) t.gameObject.layer = layer; lastLayer = layer; }
        public void Sync()
        {
            if (Owner == null || Item.itemRigidbodyC == null) return;
            if (Item.held != null) { Detach(); return; }
            var ownerItem = Owner.Item;
            Item.currentActualBoat = ownerItem.currentActualBoat; Item.currentWalkCol = ownerItem.currentWalkCol;
            if (identity == null) identity = GetComponent<SaveablePrefab>();
            identity.SetParentObject(Owner.Identity.GetParentObject());
            Vector3 position; Quaternion rotation; Owner.Pose(Slot, Item, out position, out rotation);
            transform.SetPositionAndRotation(position, rotation); transform.localScale = Owner.transform.lossyScale;
            Item.itemRigidbodyC.EnterBox(Owner.transform, Owner.transform.InverseTransformPoint(position), Quaternion.Inverse(Owner.transform.rotation) * rotation);
            var body = Item.itemRigidbodyC.GetBody(); if (body != null) { body.isKinematic = true; body.position = position; body.rotation = rotation; }
            if (Item.currentActualBoat != null && Item.currentWalkCol != null) Item.itemRigidbodyC.ForceRigidbodyToWalkCol();
            bool inInventory = ownerItem.itemRigidbodyC.GetCurrentInventorySlot() != null;
            if (inventoryHidden != inInventory) { Item.GetComponent<Collider>().enabled = !inInventory; inventoryHidden = inInventory; }
            SetLayer(ownerItem.gameObject.layer == 26 ? 26 : inInventory ? 5 : ownerItem.held != null ? 2 : 0);
        }
        private void LateUpdate() { Sync(); }
        private void OnDestroy() { if (Owner != null) Owner.Release(this); }
        public void WriteSave(SavePrefabData data)
        {
            int id = Owner != null ? Owner.Identity.instanceId : savedOwner;
            if (id == 0) return;
            Sync(); data.extraValue1 = id & 65535; data.extraValue2 = (id >> 16) & 32767; data.extraValue3 = Slot; data.extraValue4 = 130;
        }
        public void ReadSave(SavePrefabData data)
        {
            if (data.extraValue4 != 130 || data.inventorySlot >= 0 || data.crateId != 0 ||
                data.extraValue1 < 0 || data.extraValue1 > 65535 || data.extraValue1 % 1 != 0 ||
                data.extraValue2 < 0 || data.extraValue2 > 32767 || data.extraValue2 % 1 != 0 ||
                data.extraValue3 < 0 || data.extraValue3 >= 10 || data.extraValue3 % 1 != 0) return;
            savedOwner = (int)data.extraValue1 | ((int)data.extraValue2 << 16); Slot = (int)data.extraValue3;
            if (started && savedOwner != 0) StartCoroutine(Restore());
        }
        private IEnumerator Restore()
        {
            while (GameState.currentlyLoading) yield return null;
            for (int frame = 0; frame < 180 && savedOwner != 0; frame++)
            {
                if (SaveLoadManager.instance != null && Item.itemRigidbodyC != null)
                    foreach (SaveablePrefab saved in SaveLoadManager.instance.GetCurrentPrefabs())
                        if (saved != null && saved.instanceId == savedOwner)
                        {
                            var owner = saved.GetComponent<DisplayStorageState>();
                            if (owner != null && owner.TrySeat(Item, Slot, true)) yield break;
                        }
                yield return null;
            }
            savedOwner = 0;
        }
    }

    public sealed class StoragePointerTarget : GoPointerButton
    {
        public DisplayStorageState Owner;
        public int Slot;
        public Vector3 Point;
        private int withdrawalFrame = -1;
        private ShipItem lastHovered;
        public override void Start() { } // No invisible outline renderer or extra mesh.
        private void LateUpdate() { } // Native button outlines require a visible mesh.
        public void Refresh(GoPointer pointer)
        {
            var occupant = Owner.GetContent(Slot);
            var hovered = occupant != null ? occupant.Item : null;
            if (lastHovered != null && lastHovered != hovered) lastHovered.ForceUnlook();
            lastHovered = hovered;
            transform.position = Point;
            lookText = occupant != null ? occupant.Item.name : string.Empty;
            description = occupant != null ? occupant.Item.description : string.Empty;
            if (occupant != null) occupant.Item.Look(pointer);
        }
        public override void OnActivate(GoPointer pointer)
        {
            if (pointer.GetHeldItem() != null) return;
            if (Slot == -2 && Owner is PipeRackState)
            { if (!Owner.Item.nailed) { withdrawalFrame = Time.frameCount; pointer.PickUpItem(Owner.Item); } return; }
            if (!Owner.CanAccess(Slot)) return;
            var occupant = Owner.GetContent(Slot);
            if (occupant != null && !occupant.Item.nailed) { withdrawalFrame = Time.frameCount; pointer.PickUpItem(occupant.Item); }
        }
        public override bool OnItemClick(PickupableItem held)
        { if (withdrawalFrame == Time.frameCount) return false; var item = held != null ? held.GetComponent<ShipItem>() : null; if (item != null) Owner.TryPlace(item, Point); return false; }
        public override void OnAltActivate()
        {
            var box = Owner as CigarBoxState; if (box != null) box.ToggleLid();
            var rack = Owner as PipeRackState; if (rack != null && Slot == -2) rack.OpenJar();
        }
    }

    internal static class StoragePipeHeat
    {
        private static readonly System.Reflection.FieldInfo Heat = typeof(ShipItemPipe).GetField("currentHeat", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        private static readonly System.Reflection.FieldInfo Inhaling = typeof(ShipItemPipe).GetField("inhaling", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        private static readonly System.Reflection.FieldInfo Drinking = typeof(ShipItemPipe).GetField("drinking", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        public static void Extinguish(ShipItemPipe pipe)
        {
            if (pipe == null) return;
            Heat.SetValue(pipe, 0f);
            // Wall-rack pipes only lose heat. Native UpdateParticles toggles
            // emission; it never restarts a stopped particle system.
            var cigar = pipe.GetComponent<CigarRuntimeState>();
            if (cigar == null) return;
            Inhaling.SetValue(pipe, false); Drinking.SetValue(pipe, false);
            foreach (var particles in pipe.GetComponentsInChildren<ParticleSystem>(true)) particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            cigar.SyncVisuals(true, 0, false);
        }
    }
}

// AshPileVisual
namespace TobaccoPotAndCigar.Runtime
{
    using System.Collections.Generic;
    using UnityEngine;
/// <summary>Deposited ash follows the shared D-shaped basin, including its sloping floor.</summary>
    public sealed class AshPileVisual : MonoBehaviour
    {
        private Mesh ownedMesh;
        private float lastFill = -1;
        // Optional sampled basin floor for shapes such as the thicker glass tray.
        [SerializeField] private float[] floorHeights;
        public const int FloorGridSize = 65;

        public void ConfigureFloor(float[] heights)
        {
            floorHeights = heights;
            lastFill = -1;
        }

        public void SetFill(float fill)
        {
            fill = Mathf.Clamp01(fill);
            gameObject.SetActive(fill > .000001f);
            if (fill <= .000001f || fill == lastFill) return;
            if (ownedMesh == null) ownedMesh = new Mesh { name = "Deposited Ash (instance)" };
            WriteMesh(ownedMesh, fill, floorHeights);
            GetComponent<MeshFilter>().sharedMesh = ownedMesh;
            lastFill = fill;
        }

        private void OnDestroy()
        {
            if (ownedMesh == null) return;
            if (Application.isPlaying) Destroy(ownedMesh); else DestroyImmediate(ownedMesh);
        }

        public static Mesh CreateMesh(float fill, float[] floorHeights = null)
        {
            var mesh = new Mesh { name = "Deposited Ash" };
            WriteMesh(mesh, Mathf.Clamp01(fill), floorHeights);
            return mesh;
        }

        private static void WriteMesh(Mesh mesh, float fill, float[] floorHeights)
        {
            const int segments = 32, rings = 3;
            const float centre = .05f, outerRadius = .108f, divider = .002f;
            int half = 1 + segments * rings;
            var vertices = new Vector3[half * 2];
            var uv = new Vector2[vertices.Length];
            var triangles = new List<int>();
            float spread = Mathf.Lerp(.16f, 1, Mathf.Sqrt(fill));
            float peak = .015f * Mathf.Pow(fill, .6f);
            SetVertex(vertices, uv, 0, half, centre, 0, peak, floorHeights);
            for (int ring = 0; ring < rings; ring++)
            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2 / segments;
                float dx = Mathf.Cos(angle), dz = Mathf.Sin(angle);
                float reach = -centre * dx + Mathf.Sqrt(outerRadius * outerRadius - centre * centre * dz * dz);
                if (dx < 0) reach = Mathf.Min(reach, (divider - centre) / dx);
                // Broad irregularity stays inside both the divider and curved wall.
                reach *= .982f + .012f * Mathf.Sin(i * 1.7f);
                float t = (ring + 1f) / rings;
                float radius = reach * spread * t;
                float mound = peak * Mathf.Pow(1 - t * t, 1.25f) * (1 + .08f * Mathf.Sin(i * 2.3f + ring));
                int current = 1 + ring * segments + i;
                int next = 1 + ring * segments + (i + 1) % segments;
                SetVertex(vertices, uv, current, half, centre + dx * radius, dz * radius, mound, floorHeights);
                if (ring == 0) AddFace(triangles, 0, current, next, half);
                else
                {
                    AddFace(triangles, current - segments, current, next, half);
                    AddFace(triangles, current - segments, next, next - segments, half);
                }
                if (ring == rings - 1)
                {
                    triangles.AddRange(new[] { current, current + half, next, next, current + half, next + half });
                }
            }
            mesh.Clear(); mesh.vertices = vertices; mesh.uv = uv; mesh.triangles = triangles.ToArray();
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); mesh.RecalculateTangents();
        }

        private static void AddFace(List<int> triangles, int a, int b, int c, int half)
        {
            triangles.AddRange(new[] { a, b, c, c + half, b + half, a + half });
        }

        private static void SetVertex(Vector3[] vertices, Vector2[] uv, int i, int half, float x, float z, float mound, float[] floorHeights)
        {
            float radius = Mathf.Sqrt(x * x + z * z);
            // Same lathed floor profile as the Blender basin cutter (all ten styles).
            float floor = radius <= .075f ? .013f : radius <= .098f
                ? Mathf.Lerp(.013f, .016f, (radius - .075f) / .023f)
                : Mathf.Lerp(.016f, .023f, (radius - .098f) / .012f);
            if (floorHeights != null && floorHeights.Length == FloorGridSize * FloorGridSize)
            {
                float gx = Mathf.Clamp01(x / .11f) * (FloorGridSize - 1);
                float gz = Mathf.Clamp01((z + .11f) / .22f) * (FloorGridSize - 1);
                int ix = Mathf.Min((int)gx, FloorGridSize - 2), iz = Mathf.Min((int)gz, FloorGridSize - 2);
                float a = Mathf.Lerp(floorHeights[iz * FloorGridSize + ix], floorHeights[iz * FloorGridSize + ix + 1], gx - ix);
                float b = Mathf.Lerp(floorHeights[(iz + 1) * FloorGridSize + ix], floorHeights[(iz + 1) * FloorGridSize + ix + 1], gx - ix);
                floor = Mathf.Lerp(a, b, gz - iz);
            }
            const float cos = .984807753f, sin = .173648178f;
            Vector3 position = new Vector3(cos * x - sin * z, floor, -sin * x - cos * z);
            vertices[i] = position + Vector3.up * (.00065f + mound);
            vertices[i + half] = position + Vector3.up * .00035f;
            // Keep every sample within the cigar-ash atlas island, away from black padding.
            uv[i] = uv[i + half] = new Vector2(.225f + (x - .05f) * 1.3f, .21f + z * 1.3f);
        }
    }
}


// AshtrayState
namespace TobaccoPotAndCigar.Runtime
{
    using UnityEngine;
/// <summary>One cigar and one native pipe, with independent authored rests.</summary>
    public sealed class AshtrayState : MonoBehaviour
    {
        [SerializeField] private Transform[] rests;
        [SerializeField] private Transform ashPile;
        [SerializeField] private Transform[] pipeRests;
        [SerializeField] private Transform[] grooveTargets;
        private ShipItem item;
        private CigarRuntimeState occupant;
        private RestingPipeState pipeOccupant;
        private float emptyingTime = -1;
        private float restingAngle;
        private GoPointer emptyingPointer;
        public ShipItem Item { get { return item != null ? item : (item = GetComponent<ShipItem>()); } }
        public CigarRuntimeState Occupant { get { return occupant; } }
        public RestingPipeState PipeOccupant { get { return pipeOccupant; } }
        public void Configure(Transform[] sockets, Transform pile) { rests = sockets; ashPile = pile; }
        public void ConfigurePipeRests(Transform[] sockets, Transform[] grooves)
        { pipeRests = sockets; grooveTargets = grooves; }
        private void Start() { SyncFromSavedState(); }

        public void SyncFromSavedState()
        {
            // Ashtrays use native health as a 0-100 ash capacity, not durability.
            Item.health = float.IsNaN(Item.health) || float.IsInfinity(Item.health) ? 0 : Mathf.Clamp(Item.health, 0, 100);
            if (ashPile == null) return;
            var shapedPile = ashPile.GetComponent<AshPileVisual>();
            if (shapedPile != null)
            {
                shapedPile.SetFill(Item.health / 100f);
                return;
            }
            ashPile.gameObject.SetActive(Item.health > .0001f);
            float fill = Item.health / 100f;
            float width = Mathf.Lerp(.25f, 1, Mathf.Sqrt(fill));
            ashPile.localScale = new Vector3(width, Mathf.Lerp(.15f, 1, fill), width);
        }

        public bool BeginEmptying()
        {
            if (emptyingTime >= 0 || !Item.sold || Item.held == null) return false;
            emptyingPointer = Item.held;
            restingAngle = Item.heldRotationOffset;
            emptyingTime = 0;
            return true;
        }

        private void AdvanceEmptying(float deltaTime)
        {
            if (emptyingTime < 0) return;
            if (Item.held != emptyingPointer) { CancelEmptying(); return; }
            emptyingTime += deltaTime;
            float tilt = emptyingTime < .3f ? Mathf.SmoothStep(0, 1, emptyingTime / .3f) :
                1 - Mathf.SmoothStep(0, 1, (emptyingTime - .4f) / .3f);
            Item.heldRotationOffset = restingAngle - 150 * tilt;
            if (emptyingTime >= .3f && Item.health > 0) { Item.health = 0; SyncFromSavedState(); }
            if (emptyingTime >= .7f) CancelEmptying();
        }

        private void CancelEmptying()
        {
            if (emptyingTime >= 0 && Item != null) Item.heldRotationOffset = restingAngle;
            emptyingTime = -1; emptyingPointer = null;
        }

        public bool CanUse(CigarRuntimeState cigar)
        {
            return cigar != null && cigar.Pipe != null && cigar.Pipe.sold && cigar.Pipe.health > 0 &&
                IsAvailable();
        }

        private bool IsAvailable()
        {
            var secured = GetComponent<SecuredDisplayItem>();
            bool onRack = secured != null && secured.Owner != null && secured.Owner.Available;
            return Item != null && Item.sold && !Item.held && gameObject.activeInHierarchy &&
                Item.GetComponent<SaveablePrefab>().currentCrateId == 0 &&
                Item.itemRigidbodyC != null && Item.itemRigidbodyC.GetCurrentInventorySlot() == null &&
                (Item.itemRigidbodyC.GetCurrentBox() == null || onRack);
        }

        public bool CanSeatPipe(ShipItemPipe pipe)
        {
            int slot = RestingPipeState.NativePipeSlot(pipe);
            return slot >= 0 && pipe.sold && !pipe.nailed && IsAvailable() &&
                pipeRests != null && pipeRests.Length == 3 && pipeRests[slot] != null &&
                (pipeOccupant == null || pipeOccupant.Pipe == pipe);
        }

        public bool TrySeatPipe(ShipItemPipe pipe)
        {
            if (!CanSeatPipe(pipe) || pipe.itemRigidbodyC == null) return false;
            RestingPipeState state = RestingPipeState.Ensure(pipe);
            if (pipe.held != null) pipe.held.DropItem();
            state.RestOn(this, pipeRests[RestingPipeState.NativePipeSlot(pipe)]);
            pipeOccupant = state;
            return true;
        }

        public void ReleasePipe(RestingPipeState pipe) { if (pipeOccupant == pipe) pipeOccupant = null; }

        public void PreparePickup()
        {
            CancelEmptying();
            ReleaseOccupant(true);
            // Models and sockets are authored bowl-forward (+Z). Reset scrolling
            // and prior emptying tilt each pickup; native GoPointer owns the pose.
            Item.heldRotationOffset = 0;
            if (Item.held != null) transform.rotation = Item.held.transform.rotation;
        }

        public bool TryClearAsh(CigarRuntimeState cigar)
        {
            if (!CanUse(cigar) || cigar.Pipe.held == null) return false;
            float deposited = cigar.AccumulatedAsh01 * 100f;
            cigar.ClearAsh();
            Item.health += deposited;
            SyncFromSavedState();
            return true;
        }

        public bool TrySeatAt(CigarRuntimeState cigar, Vector3 hitPoint)
        {
            if (rests == null || rests.Length != 2 || rests[0] == null || rests[1] == null) return false;
            // Sockets mark the burning tip, across the basin from the physical groove.
            Vector3 grooveOffset = new Vector3(0, -.120f, .00058f);
            Vector3 groove0 = rests[0].TransformPoint(grooveOffset);
            Vector3 groove1 = rests[1].TransformPoint(grooveOffset);
            if (grooveTargets != null && grooveTargets.Length == 2 && grooveTargets[0] != null && grooveTargets[1] != null)
            { groove0 = grooveTargets[0].position; groove1 = grooveTargets[1].position; }
            int slot = (hitPoint - groove0).sqrMagnitude <= (hitPoint - groove1).sqrMagnitude ? 0 : 1;
            return TrySeat(cigar, slot);
        }

        public bool TrySeat(CigarRuntimeState cigar, int slot)
        {
            if (!CanUse(cigar) || (occupant != null && occupant != cigar) ||
                rests == null || rests.Length != 2 || slot < 0 || slot > 1 ||
                rests[slot] == null || cigar.Pipe.itemRigidbodyC == null) return false;
            if (cigar.Pipe.held != null) cigar.Pipe.held.DropItem();
            cigar.RestOn(this, rests[slot], slot);
            occupant = cigar;
            return true;
        }

        public void Release(CigarRuntimeState cigar) { if (occupant == cigar) occupant = null; }
        public void ReleaseOccupant(bool drop = false)
        {
            if (pipeOccupant != null) pipeOccupant.LeaveRest(drop);
            if (occupant == null) return;
            CigarRuntimeState cigar = occupant;
            cigar.LeaveRest();
            if (drop && cigar.Pipe.itemRigidbodyC != null)
            {
                Rigidbody body = cigar.Pipe.itemRigidbodyC.GetBody();
                if (body != null)
                {
                    cigar.Pipe.ResetRigidbody();
                    // Boat physics runs on a separate collision copy, not at the visible pose.
                    if (cigar.Pipe.currentActualBoat != null && cigar.Pipe.currentWalkCol != null)
                        cigar.Pipe.itemRigidbodyC.ForceRigidbodyToWalkCol();
                    body.isKinematic = false;
                    body.WakeUp();
                }
            }
        }
        private void LateUpdate()
        {
            AdvanceEmptying(Time.deltaTime);
            if ((occupant != null || pipeOccupant != null) && Item.held != null) ReleaseOccupant(true);
            if ((occupant != null || pipeOccupant != null) && (Item.GetComponent<SaveablePrefab>().currentCrateId != 0 ||
                (Item.itemRigidbodyC != null && Item.itemRigidbodyC.GetCurrentInventorySlot() != null)))
                ReleaseOccupant();
        }
        private void OnDisable() { CancelEmptying(); ReleaseOccupant(); }
    }
}


// RestingPipeState
namespace TobaccoPotAndCigar.Runtime
{
    using System.Collections;
    using UnityEngine;
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
                if (body != null)
                {
                    Pipe.ResetRigidbody();
                    // ResetRigidbody writes the visible world pose; restore boat physics space.
                    if (Pipe.currentActualBoat != null && Pipe.currentWalkCol != null)
                        Pipe.itemRigidbodyC.ForceRigidbodyToWalkCol();
                    body.isKinematic = false;
                    body.WakeUp();
                }
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
