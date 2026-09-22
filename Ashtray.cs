// Ashtray — grouped mod source. Existing type identities are preserved.

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
            const int segments = 64, rings = 5;
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

namespace TobaccoPotAndCigar.Patches
{
    using HarmonyLib;
    using TobaccoPotAndCigar.Runtime;

    [HarmonyPatch(typeof(ShipItemHammer), nameof(ShipItemHammer.CanNail))]
    internal static class AshtrayHammerPatch
    {
        [HarmonyPostfix]
        private static void Postfix(ShipItem item, ref bool __result)
        {
            if (__result || item == null || !item.sold) return;
            __result = item.GetComponent<AshtrayState>() != null;
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
