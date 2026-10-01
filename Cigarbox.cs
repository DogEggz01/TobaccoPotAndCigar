// Cigar box — grouped mod source. Existing type identities are preserved.

// Regional boxes use the authored hinge and slots; contents keep their native identities.
namespace TobaccoPotAndCigar.Runtime
{
    using UnityEngine;
    public sealed class CigarBoxState : DisplayStorageState
    {
        [SerializeField] private Transform lid;
        [SerializeField] private Bounds lidBounds;
        [SerializeField] private Bounds bodyBounds;
        [SerializeField] private int baseSeats;
        private float angle;
        private bool wantsOpen;
        private bool moving;
        private int previousAim = -1;
        private readonly Collider[] obstacles = new Collider[128];
        public override int Capacity { get { return seats != null ? seats.Length : 0; } }
        public float LidAngle { get { return angle; } }
        public bool WantsOpen { get { return wantsOpen; } }
        public bool IsMoving { get { return moving; } }
        public Transform Lid { get { return lid; } }
        protected override bool ExtinguishOnSeat { get { return true; } }
        public void Configure(Transform hinge, Bounds lidShape, Bounds bodyShape, int bottomCount)
        { lid = hinge; lidBounds = lidShape; bodyBounds = bodyShape; baseSeats = bottomCount; }
        public override bool Accepts(ShipItem held, int slot)
        { return held != null && held.GetComponent<CigarRuntimeState>() != null && held.health > 0; }
        public override bool CanAccess(int slot)
        { return base.CanAccess(slot) && angle > 1; }
        public void ToggleLid() { if (Item.sold) wantsOpen = !wantsOpen; }
        private void Update()
        { if (GameState.playing && !GameState.currentlyLoading) AdvanceLid(Time.deltaTime); }
        public void AdvanceLid(float dt)
        {
            moving = false; if (lid == null || dt <= 0) return;
            float target = wantsOpen ? 180 : 0;
            float end = Mathf.MoveTowards(angle, target, 225 * Mathf.Min(dt, .05f));
            // Half-degree swept intervals, with a clearance skin larger than the arc step.
            while (Mathf.Abs(end - angle) > .0001f)
            {
                float next = Mathf.MoveTowards(angle, end, .5f);
                if (!ClearAt(next)) break;
                angle = next; moving = true;
            }
            if (moving) lid.localRotation = Quaternion.Euler(-angle, 0, 0);
        }
        public bool ClearAt(float degrees)
        {
            Quaternion rotation = transform.rotation * Quaternion.Euler(-degrees, 0, 0);
            Vector3 scale = transform.lossyScale;
            Vector3 centre = lid.position + rotation * Vector3.Scale(lidBounds.center, scale);
            Vector3 half = Vector3.Scale(lidBounds.extents, scale) + Vector3.one * .0016f;
            if (!VolumeClear(centre, half, rotation, false)) return false;
            // Sailwind keeps boat collision geometry in a separate physics frame.
            Transform boat = Item.currentActualBoat, walk = Item.currentWalkCol;
            if (boat != null && walk != null)
            {
                centre = walk.TransformPoint(boat.InverseTransformPoint(centre));
                rotation = walk.rotation * Quaternion.Inverse(boat.rotation) * rotation;
                if (!VolumeClear(centre, half, rotation, true)) return false;
            }
            return true;
        }
        private bool VolumeClear(Vector3 centre, Vector3 half, Quaternion rotation, bool boatPhysics)
        {
            // BoatCapsule is the broad exterior boat collision envelope, not its
            // deck/walls. It surrounds open interior space and would block both
            // directions. Keep the detailed world and walk-frame geometry.
            const int obstacleLayers = ~((1 << 13) | (1 << 26));
            int count = Physics.OverlapBoxNonAlloc(centre, half, obstacles, rotation, obstacleLayers, QueryTriggerInteraction.Collide);
            if (count == obstacles.Length) return false;
            for (int i = 0; i < count; i++)
            {
                Collider c = obstacles[i];
                if (c == null || c.transform.IsChildOf(transform)) continue;
                // Some native boats (including the large dhow) keep their root
                // capsule on another layer. Match its role without excluding
                // whole layers that can also contain genuine surfaces.
                if (c is CapsuleCollider && c.GetComponent<BoatRefs>() != null) continue;
                var stored = c.GetComponentInParent<SecuredDisplayItem>();
                if (stored != null && stored.Owner == this) continue;
                var body = c.GetComponentInParent<ItemRigidbody>();
                if (body != null)
                {
                    if (!boatPhysics || body == Item.itemRigidbodyC) continue;
                    bool ownContent = false;
                    for (int slot = 0; slot < Capacity; slot++)
                    {
                        var content = GetContent(slot);
                        if (content != null && content.Item.itemRigidbodyC == body) { ownContent = true; break; }
                    }
                    if (ownContent) continue;
                }
                if (c.isTrigger && c.GetComponentInParent<ShipItem>() == null) continue;
                if (c.gameObject.layer == 26) continue;
                return false;
            }
            return true;
        }
        public bool ResolveInterior(Ray ray, out int slot, out Vector3 point)
        {
            slot = -1; point = transform.position;
            if (angle <= 1) { previousAim = -1; return false; }
            // Full-length, disjoint slot strips share the gaps. Names and highlights still
            // refer to the actual cigar; removing it never falls through to picking up the box.
            float closest = 1.8f; int best = -1;
            if (previousAim >= 0 && previousAim < seats.Length)
            {
                Transform previous = seats[previousAim]; float stableDistance;
                Ray stableRay = new Ray(previous.InverseTransformPoint(ray.origin), previous.InverseTransformDirection(ray.direction));
                if (new Bounds(new Vector3(0, -.12025f, 0), new Vector3(.030f, .258f, .030f)).IntersectRay(stableRay, out stableDistance) &&
                    stableDistance <= 1.8f && Exposed(ray, stableDistance, previousAim >= baseSeats))
                { slot = previousAim; point = ray.GetPoint(stableDistance); return true; }
            }
            for (int i = 0; i < seats.Length; i++)
            {
                Transform socket = seats[i];
                Ray local = new Ray(socket.InverseTransformPoint(ray.origin), socket.InverseTransformDirection(ray.direction));
                float distance;
                Bounds strip = new Bounds(new Vector3(0, -.12025f, 0), new Vector3(.028f, .258f, .030f));
                if (i == previousAim) strip.Expand(new Vector3(.002f, 0, 0));
                if (!strip.IntersectRay(local, out distance) || distance > closest) continue;
                Vector3 world = ray.GetPoint(distance);
                if (!Exposed(ray, distance, i >= baseSeats)) continue;
                best = i; closest = distance; point = world;
            }
            if (best >= 0) { slot = previousAim = best; return true; }
            previousAim = -1;
            Ray rootRay = new Ray(transform.InverseTransformPoint(ray.origin), transform.InverseTransformDirection(ray.direction));
            Bounds interior = new Bounds(new Vector3(0, bodyBounds.max.y, 0), new Vector3(bodyBounds.size.x - .014f, .014f, bodyBounds.size.z - .014f));
            float hit;
            if (interior.IntersectRay(rootRay, out hit) && hit <= 1.8f && Vector3.Dot(ray.direction, transform.up) < 0 && Exposed(ray, hit, false))
            { point = ray.GetPoint(hit); return true; }
            // An empty lid holder panel also blocks F picking up the entire open box.
            Ray lidRay = new Ray(lid.InverseTransformPoint(ray.origin), lid.InverseTransformDirection(ray.direction));
            if (baseSeats < Capacity && lidBounds.IntersectRay(lidRay, out hit) && hit <= 1.8f && Vector3.Dot(ray.direction, lid.up) > 0)
            { point = ray.GetPoint(hit); return true; }
            return false;
        }
        private bool Exposed(Ray ray, float distance, bool onLid)
        {
            if (onLid) return Vector3.Dot(ray.direction, lid.up) > .02f;
            if (Vector3.Dot(ray.direction, transform.up) >= -.02f) return false;
            Ray local = new Ray(lid.InverseTransformPoint(ray.origin), lid.InverseTransformDirection(ray.direction));
            float lidDistance;
            return !lidBounds.IntersectRay(local, out lidDistance) || lidDistance > distance - .006f;
        }
        public override int NearestEmpty(ShipItem held, Vector3 point)
        {
            // Distance to the centreline, not just the burning tip, gives the same nearest
            // seat along the whole cigar and on the raised lid.
            EnsureContents(); int best = -1; float distance = float.MaxValue;
            for (int i = 0; i < Capacity; i++)
            {
                if (!CanAccess(i) || contents[i] != null || !Accepts(held, i)) continue;
                Vector3 local = seats[i].InverseTransformPoint(point);
                local.y -= Mathf.Clamp(local.y, -.2405f, 0);
                float d = local.sqrMagnitude;
                if (d < distance) { best = i; distance = d; }
            }
            return best;
        }
        public void WriteSave(SavePrefabData data)
        { data.extraValue0 = angle; data.extraValue1 = wantsOpen ? 1 : 0; data.extraValue4 = 131; }
        public void ReadSave(SavePrefabData data)
        {
            if (data.extraValue4 != 131) return;
            angle = float.IsNaN(data.extraValue0) || float.IsInfinity(data.extraValue0) ? 0 : Mathf.Clamp(data.extraValue0, 0, 180);
            wantsOpen = data.extraValue1 == 1; moving = false;
            if (lid != null) lid.localRotation = Quaternion.Euler(-angle, 0, 0);
        }
    }
}
