using UnityEngine;

namespace TobaccoPotAndCigar.Runtime
{
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
            return Item != null && Item.sold && !Item.held && gameObject.activeInHierarchy &&
                Item.GetComponent<SaveablePrefab>().currentCrateId == 0 &&
                Item.itemRigidbodyC != null && Item.itemRigidbodyC.GetCurrentInventorySlot() == null &&
                Item.itemRigidbodyC.GetCurrentBox() == null;
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
