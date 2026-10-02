// Shared plugin infrastructure — grouped mod source. Existing type identities are preserved.

// Plugin
namespace TobaccoPotAndCigar
{
    using System.IO;
    using BepInEx;
    using BepInEx.Logging;
    using HarmonyLib;
    using TobaccoPotAndCigar.Prefabs;
    using TobaccoPotAndCigar.Runtime;
    using TobaccoPotAndCigar.Shops;
    using TobaccoPotAndCigar.Smoking;
[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(
        Compatibility.RadRefinementsCompatibility.PluginGuid,
        BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "DogEggz.Cigar";
        public const string PluginName = "Tobacco pot and cigar";
        public const string PluginVersion = "1.2.3";

        internal static ManualLogSource LogSource { get; private set; }
        internal static string PluginDirectory { get; private set; }

        private Harmony harmony;

        private void Awake()
        {
            LogSource = Logger;
            PluginDirectory = Path.GetDirectoryName(Info.Location) ?? string.Empty;
            RuntimeDiagnostics.InfoSink = message => Logger.LogInfo(message);
            RuntimeDiagnostics.WarningSink = message => Logger.LogWarning(message);
            RuntimeDiagnostics.ErrorSink = message => Logger.LogError(message);

            try
            {
                CigarAssetBundle.Load(PluginDirectory);
                harmony = new Harmony(PluginGuid);
                harmony.PatchAll(typeof(Plugin).Assembly);
                ShopPlacement.ResetDiagnostics();

                Logger.LogInfo(PluginName + " " + PluginVersion + " loaded.");
            }
            catch (System.Exception exception)
            {
                // Unity exceptions may be excluded from BepInEx's disk log.
                Logger.LogError("Cigar initialization failed: " + exception);
                throw;
            }
        }

        private void OnDestroy()
        {
            harmony?.UnpatchSelf();
            CigarEffectService.Reset();
            Compatibility.RadRefinementsCompatibility.Reset();
            CigarPrefabRegistrar.Reset();
            CigarAssetBundle.Unload();
            ShopPlacement.ResetDiagnostics();
            RuntimeDiagnostics.Reset();
            PluginDirectory = null;
            LogSource = null;
        }
    }
}


// Apply vanilla distance culling to the complete authored item, excluding the
// invisible outline proxy. Native ShipItem otherwise registers only its root.
namespace TobaccoPotAndCigar.Patches
{
    using HarmonyLib;
    using UnityEngine;
    using System.Collections.Generic;
    [HarmonyPatch(typeof(ShipItem), "AddLODGroup")]
    internal static class CustomItemLodPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(ShipItem __instance)
        {
            var saveable=__instance.GetComponent<SaveablePrefab>();
            if(saveable==null || saveable.prefabIndex<610 || saveable.prefabIndex>631 || saveable.prefabIndex==614)return true;
            var renderers=new List<Renderer>();
            foreach(var renderer in __instance.GetComponentsInChildren<MeshRenderer>(true))
            {
                if(renderer.sharedMaterial!=null && renderer.sharedMaterial.name=="InteractionOutlineProxy")
                    renderer.enabled=false;
                else renderers.Add(renderer);
            }
            var group=__instance.GetComponent<LODGroup>();
            if(group==null)group=__instance.gameObject.AddComponent<LODGroup>();
            var levels=(LOD[])RefsDirectory.instance.LODtemplateItems.GetLODs().Clone();
            levels[0].renderers=renderers.ToArray();group.SetLODs(levels);group.RecalculateBounds();
            return false;
        }
    }
}

// RadRefinementsCompatibility
namespace TobaccoPotAndCigar.Compatibility
{
    using System;
    using System.Reflection;
    using BepInEx.Bootstrap;
    using BepInEx.Configuration;
internal struct BlueEffectProfile
    {
        internal float ImmediateSleepChange;
        internal float GreenVisualChargeRate;

        internal static BlueEffectProfile VanillaGreen
        {
            get
            {
                return new BlueEffectProfile
                {
                    ImmediateSleepChange = -0.11f,
                    GreenVisualChargeRate = 2f
                };
            }
        }

        internal static BlueEffectProfile RadEnhanced(int multiplier)
        {
            multiplier = Math.Max(0, multiplier);
            return new BlueEffectProfile
            {
                ImmediateSleepChange = -0.11f * multiplier,
                GreenVisualChargeRate = 2f * multiplier * 0.33f
            };
        }
    }

    internal static class RadRefinementsCompatibility
    {
        internal const string PluginGuid = "com.raddude.radrefinements";

        private static ConfigEntry<bool> enableBlueTobacco;
        private static ConfigEntry<int> bluePotencyMult;
        private static bool warnedAboutIncompatibleVersion;

        internal static BlueEffectProfile GetBlueEffectProfile()
        {
            BepInEx.PluginInfo plugin;
            if (!Chainloader.PluginInfos.TryGetValue(PluginGuid, out plugin))
                return BlueEffectProfile.VanillaGreen;

            if ((enableBlueTobacco == null || bluePotencyMult == null) &&
                !TryBindConfigEntries(plugin.Instance.GetType().Assembly))
            {
                WarnOnce();
                return BlueEffectProfile.VanillaGreen;
            }

            return enableBlueTobacco.Value
                ? BlueEffectProfile.RadEnhanced(bluePotencyMult.Value)
                : BlueEffectProfile.VanillaGreen;
        }

        internal static void Reset()
        {
            enableBlueTobacco = null;
            bluePotencyMult = null;
            warnedAboutIncompatibleVersion = false;
        }

        private static bool TryBindConfigEntries(Assembly assembly)
        {
            Type configs = assembly.GetType("RadRefinements.Configs", false);
            if (configs == null)
                return false;

            const BindingFlags Flags = BindingFlags.Static |
                                       BindingFlags.Public |
                                       BindingFlags.NonPublic;
            FieldInfo enabledField = configs.GetField("enableBlueTobacco", Flags);
            FieldInfo multiplierField = configs.GetField("bluePotencyMult", Flags);
            enableBlueTobacco = enabledField == null
                ? null
                : enabledField.GetValue(null) as ConfigEntry<bool>;
            bluePotencyMult = multiplierField == null
                ? null
                : multiplierField.GetValue(null) as ConfigEntry<int>;
            return enableBlueTobacco != null && bluePotencyMult != null;
        }

        private static void WarnOnce()
        {
            if (warnedAboutIncompatibleVersion)
                return;
            warnedAboutIncompatibleVersion = true;
            Plugin.LogSource?.LogWarning(
                "Rad Refinements was detected but its blue-tobacco settings " +
                "could not be read. Cigar blue tobacco will use vanilla green effects.");
        }
    }
}


// ItemInteractionPatches
namespace TobaccoPotAndCigar.Patches
{
    using HarmonyLib;
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using System.Reflection.Emit;
    using TobaccoPotAndCigar.Runtime;
    using UnityEngine;
[HarmonyPatch(typeof(ShipItem), "OnItemClick")]
    internal static class CustomItemClickPatch
    {
        private static readonly AccessTools.FieldRef<GoPointer, RaycastHit> ReadPointerHit =
            AccessTools.FieldRefAccess<GoPointer, RaycastHit>("hit");

        [HarmonyPrefix]
        private static bool Prefix(
            ShipItem __instance,
            PickupableItem heldItem,
            ref bool __result)
        {
            DisplayStorageState display = __instance.GetComponent<DisplayStorageState>();
            if (display != null)
            {
                __result = false;
                var held = heldItem != null ? heldItem.GetComponent<ShipItem>() : null;
                if (held != null && held.held != null) display.TryPlace(held, ReadPointerHit(held.held).point);
                return false;
            }
            TobaccoPlantPotState plant =
                __instance.GetComponent<TobaccoPlantPotState>();
            if (plant != null)
            {
                __result = false;
                if (heldItem != null)
                    plant.TryAcceptWater(heldItem.GetComponent<ShipItemBottle>());
                return false;
            }

            FreshTobaccoLeafState dryLeaf = __instance.GetComponent<FreshTobaccoLeafState>();
            if (dryLeaf != null)
            {
                __result = false;
                return false;
            }

            AshtrayState tray = __instance.GetComponent<AshtrayState>();
            if (tray != null)
            {
                __result = false;
                CigarRuntimeState cigar = heldItem != null ? heldItem.GetComponent<CigarRuntimeState>() : null;
                ShipItemPipe pipe = heldItem != null ? heldItem.GetComponent<ShipItemPipe>() : null;
                GoPointer pointer = pipe != null ? pipe.held : null;
                if (pointer != null && pointer.GetHeldItem() == heldItem)
                {
                    RaycastHit hit = ReadPointerHit(pointer);
                    if (hit.collider != null && hit.collider.GetComponentInParent<AshtrayState>() == tray)
                    {
                        if (cigar != null) tray.TrySeatAt(cigar, hit.point);
                        else tray.TrySeatPipe(pipe);
                    }
                }
                return false;
            }

            CigarWrapperState leaf =
                __instance.GetComponent<CigarWrapperState>();
            if (leaf != null)
            {
                __result = false;
                if (heldItem != null)
                    leaf.TryInsert(heldItem.GetComponent<ShipItemTobacco>());
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(ShipItem), "OnAltActivate")]
    internal static class CustomAltInteractionPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(ShipItem __instance)
        {
            CigarBoxState box = __instance.GetComponent<CigarBoxState>();
            if (box != null && __instance.sold) { box.ToggleLid(); return false; }
            TobaccoPlantPotState plant =
                __instance.GetComponent<TobaccoPlantPotState>();
            if (plant != null && __instance.sold)
            {
                plant.TryHarvest();
                return false;
            }

            CigarWrapperState leaf =
                __instance.GetComponent<CigarWrapperState>();
            if (leaf != null && __instance.sold)
            {
                leaf.TryRoll();
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(ShipItemBottle), "AllowOnItemClick")]
    internal static class PotWaterTargetPatch
    {
        [HarmonyPostfix]
        private static void Postfix(
            ShipItemBottle __instance,
            GoPointerButton lookedAtButton,
            ref bool __result)
        {
            if (__result || lookedAtButton == null || !__instance.sold)
                return;
            TobaccoPlantPotState plant =
                lookedAtButton.GetComponent<TobaccoPlantPotState>();
            if (plant != null && plant.StoredWater < GrowthMath.Capacity &&
                lookedAtButton.GetComponent<ShipItem>() != null &&
                lookedAtButton.GetComponent<ShipItem>().sold &&
                __instance.amount == (float)LiquidType.water &&
                __instance.health > 0f)
            {
                __result = true;
            }
        }
    }

    [HarmonyPatch(typeof(ShipItemTobacco), "AllowOnItemClick")]
    internal static class TobaccoTargetPatch
    {
        [HarmonyPostfix]
        private static void Postfix(
            ShipItemTobacco __instance,
            GoPointerButton lookedAtButton,
            ref bool __result)
        {
            if (lookedAtButton == null)
                return;
            if (lookedAtButton.GetComponent<CigarRuntimeState>() != null)
            {
                __result = false;
                return;
            }
            if (lookedAtButton.GetComponent<FreshTobaccoLeafState>() != null ||
                lookedAtButton.GetComponent<DriedTobaccoLeafState>() != null)
            {
                __result = false;
                return;
            }
            CigarWrapperState leaf =
                lookedAtButton.GetComponent<CigarWrapperState>();
            if (leaf != null) __result = leaf.CanInsert(__instance);
        }
    }

    [HarmonyPatch(typeof(ShipItem), "AllowOnItemClick")]
    internal static class DryLeafCombineTargetPatch
    {
        [HarmonyPostfix]
        private static void Postfix(ShipItem __instance, GoPointerButton lookedAtButton, ref bool __result)
        {
            FreshTobaccoLeafState held = __instance.GetComponent<FreshTobaccoLeafState>();
            FreshTobaccoLeafState target = lookedAtButton != null ? lookedAtButton.GetComponent<FreshTobaccoLeafState>() : null;
            if (held != null && target != null) __result = target.CanCombineWith(held);
            CigarRuntimeState cigar = __instance.GetComponent<CigarRuntimeState>();
            AshtrayState tray = lookedAtButton != null ? lookedAtButton.GetComponent<AshtrayState>() : null;
            if (cigar != null && tray != null) __result = tray.CanUse(cigar);
            else if (tray != null && __instance is ShipItemPipe)
                __result = tray.CanSeatPipe((ShipItemPipe)__instance);
            var storageTarget = lookedAtButton != null ? lookedAtButton.GetComponent<StoragePointerTarget>() : null;
            var storage = storageTarget != null ? storageTarget.Owner : lookedAtButton != null ? lookedAtButton.GetComponent<DisplayStorageState>() : null;
            if (storage != null) __result = storage.Available && storage.NearestEmpty(__instance, storageTarget != null ? storageTarget.Point : storage.transform.position) >= 0;
        }
    }

    [HarmonyPatch(typeof(GoPointerButton), "OnAltActivate", new[] { typeof(GoPointer) })]
    internal static class HeldCigarAndLeafAltPatch
    {
        [HarmonyPostfix]
        private static void Postfix(GoPointerButton __instance, GoPointer activatingPointer)
        {
            if (activatingPointer == null || activatingPointer.GetHeldItem() != __instance ||
                GameState.inCursorMenu || GameState.sleeping || GameState.inBed || BoatCamera.on) return;
            AshtrayState heldTray = __instance.GetComponent<AshtrayState>();
            if (heldTray != null) { heldTray.BeginEmptying(); return; }
            // Resolve this press against the current ray; native pointedAtButton can be stale.
            RaycastHit hit;
            if (!Physics.Raycast(activatingPointer.transform.position, activatingPointer.transform.forward,
                out hit, 1.8f, -604165)) return;
            ShipItem target = hit.collider.GetComponent<ShipItem>();
            if (target == null || !target.sold || target.unclickable) return;
            FreshTobaccoLeafState held = __instance.GetComponent<FreshTobaccoLeafState>();
            FreshTobaccoLeafState leaf = target.GetComponent<FreshTobaccoLeafState>();
            if (held != null && leaf != null) leaf.TryCombineWith(held);
            CigarRuntimeState cigar = __instance.GetComponent<CigarRuntimeState>();
            AshtrayState tray = target.GetComponent<AshtrayState>();
            if (cigar != null && tray != null) tray.TryClearAsh(cigar);
        }
    }

    // Preserve the nailable ashtrays added in the distributed 1.1.3 build.
    [HarmonyPatch(typeof(ShipItemHammer), "CanNail")]
    internal static class AshtrayHammerPatch
    {
        [HarmonyPostfix]
        private static void Postfix(ShipItem item, ref bool __result)
        {
            if (__result || item == null || !item.sold) return;
            __result = item.GetComponent<AshtrayState>() != null || item.GetComponent<DisplayStorageState>() != null;
        }
    }

    [HarmonyPatch(typeof(ShipItem), "OnPickup")]
    internal static class CigarLeaveRestPatch
    {
        [HarmonyPrefix]
        private static void Prefix(ShipItem __instance)
        {
            var stored = __instance.GetComponent<SecuredDisplayItem>();
            if (stored != null) stored.Detach();
            CigarRuntimeState cigar = __instance.GetComponent<CigarRuntimeState>();
            if (cigar != null) cigar.LeaveRest();
            RestingPipeState pipe = __instance.GetComponent<RestingPipeState>();
            if (pipe != null) pipe.LeaveRest();
            AshtrayState tray = __instance.GetComponent<AshtrayState>();
            if (tray != null) tray.PreparePickup();
            var rack = __instance.GetComponent<PipeRackState>();
            if (rack != null) rack.PreparePickup();
        }
    }

    [HarmonyPatch(typeof(GoPointer), "DoRaycast")]
    internal static class SeatedCigarPointerPatch
    {
        private static readonly RaycastHit[] NearbyHits = new RaycastHit[16];
        private static readonly AccessTools.FieldRef<GoPointer, Ray> ReadPointerRay =
            AccessTools.FieldRefAccess<GoPointer, Ray>("raycastRay");

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var hitField = AccessTools.Field(typeof(GoPointer), "hit");
            bool writesPointerHit = false;
            int replaced = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Ldflda && Equals(instruction.operand, hitField))
                    writesPointerHit = true;
                yield return instruction;
                var method = instruction.operand as MethodInfo;
                if (!writesPointerHit || instruction.opcode != OpCodes.Call || method == null ||
                    method.ReturnType != typeof(bool)) continue;
                var parameters = method.GetParameters();
                if (parameters.Length == 4 && parameters[0].ParameterType == typeof(Ray) &&
                    parameters[1].ParameterType == typeof(RaycastHit).MakeByRefType() &&
                    parameters[2].ParameterType == typeof(float) && parameters[3].ParameterType == typeof(int))
                {
                    // Preserve the original call so other pointer patches (including Wind Totem)
                    // can still replace it. Also accept an already-replaced call of this signature.
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Ldflda, hitField);
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(SeatedCigarPointerPatch), nameof(PreferCigar)));
                    replaced++;
                    writesPointerHit = false;
                }
            }
            if (replaced != 1) throw new InvalidOperationException("Cannot locate native pointer raycast for seated cigar targeting.");
        }

        private static bool PreferCigar(bool didHit, GoPointer pointer, ref RaycastHit hit)
        {
            if (!didHit) return false;
            if (pointer.GetHeldItem() != null) return StoragePointerPatch.SkipHeldCollider(pointer, ref hit);
            AshtrayState tray = hit.collider.GetComponent<AshtrayState>();
            if (tray == null) return true;
            Ray ray = pointer.debugEditorPointer ? Camera.main.ScreenPointToRay(Input.mousePosition) :
                ReadPointerRay(pointer);
            Collider cigar = TargetCollider(tray.Occupant != null ? tray.Occupant.Pipe : null);
            Collider pipe = TargetCollider(tray.PipeOccupant != null ? tray.PipeOccupant.Pipe : null);
            if (cigar == null && pipe == null) return true;
            RaycastHit seatedHit;
            bool direct = false;
            float closest = 1.8f;
            if (cigar != null && cigar.Raycast(ray, out seatedHit, closest))
            { hit = seatedHit; closest = seatedHit.distance; direct = true; }
            if (pipe != null && pipe.Raycast(ray, out seatedHit, closest))
            { hit = seatedHit; direct = true; }
            if (direct) return true;
            // Only assist over this occupied tray; other objects retain native occlusion.
            int count = Physics.SphereCastNonAlloc(ray, .012f, NearbyHits, 1.8f, -604165);
            for (int i = 0; i < count; i++)
                if ((NearbyHits[i].collider == cigar || NearbyHits[i].collider == pipe) && NearbyHits[i].distance < closest)
                { hit = NearbyHits[i]; closest = hit.distance; }
            return true;
        }

        private static Collider TargetCollider(ShipItemPipe item)
        { return item != null && !item.unclickable && !item.nailed ? item.GetComponent<Collider>() : null; }
    }

    [HarmonyPatch(typeof(ShipItem), "ExtraFixedUpdate")]
    internal static class RestingCigarBoatPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(ShipItem __instance)
        {
            CigarRuntimeState cigar = __instance.GetComponent<CigarRuntimeState>();
            RestingPipeState pipe = __instance.GetComponent<RestingPipeState>();
            var stored = __instance.GetComponent<SecuredDisplayItem>();
            return (stored == null || stored.Owner == null) && (cigar == null || !cigar.IsResting) && (pipe == null || !pipe.IsResting);
        }
    }

    [HarmonyPatch(typeof(ShipItem), "OnEnterInventory")]
    internal static class AshtrayInventoryPatch
    {
        [HarmonyPrefix]
        private static void Prefix(ShipItem __instance)
        {
            var stored = __instance.GetComponent<SecuredDisplayItem>();
            if (stored != null) stored.Detach();
            var rack = __instance.GetComponent<PipeRackState>();
            if (rack != null) rack.PreparePickup();
            AshtrayState tray = __instance.GetComponent<AshtrayState>();
            if (tray != null) tray.ReleaseOccupant();
            RestingPipeState pipe = __instance.GetComponent<RestingPipeState>();
            if (pipe != null) pipe.LeaveRest();
        }
    }
}


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


// Scoped storage interactions preserve native controls and all unrelated pointer/UI behavior.
namespace TobaccoPotAndCigar.Patches
{
    using HarmonyLib;
    using UnityEngine;
    using TobaccoPotAndCigar.Runtime;

    [HarmonyPatch(typeof(GoPointer), "DoRaycast")]
    internal static class StoragePointerPatch
    {
        private static readonly RaycastHit[] SelfHits = new RaycastHit[64];
        private static readonly AccessTools.FieldRef<GoPointer, RaycastHit> Hit = AccessTools.FieldRefAccess<GoPointer, RaycastHit>("hit");
        private static readonly AccessTools.FieldRef<GoPointer, Ray> Ray = AccessTools.FieldRefAccess<GoPointer, Ray>("raycastRay");
        private static readonly AccessTools.FieldRef<GoPointer, GoPointerButton> Pointed = AccessTools.FieldRefAccess<GoPointer, GoPointerButton>("pointedAtButton");
        private static readonly AccessTools.FieldRef<GoPointer, float> Distance = AccessTools.FieldRefAccess<GoPointer, float>("currentLookDistance");
        internal static bool SkipHeldCollider(GoPointer pointer, ref RaycastHit hit)
        {
            var held = pointer.GetHeldItem() as ShipItem;
            if (held == null || (!(held is ShipItemPipe) && held.GetComponent<DisplayStorageState>() == null)) return true;
            if (!IsHeld(hit.collider, held)) return true;
            Ray ray = pointer.debugEditorPointer && Camera.main != null ? Camera.main.ScreenPointToRay(Input.mousePosition) : Ray(pointer);
            int count = Physics.RaycastNonAlloc(ray, SelfHits, 1.8f, -604165);
            float distance = float.MaxValue; bool found = false;
            for (int i = 0; i < count; i++)
                if (!IsHeld(SelfHits[i].collider, held) && SelfHits[i].distance < distance)
                { hit = SelfHits[i]; distance = hit.distance; found = true; }
            if (!found) hit = default(RaycastHit);
            return found;
        }
        private static bool IsHeld(Collider collider, ShipItem held)
        {
            if (collider == null) return false;
            return collider.transform.IsChildOf(held.transform) ||
                (held.itemRigidbodyC != null && collider.transform.IsChildOf(held.itemRigidbodyC.transform));
        }
        [HarmonyPostfix]
        private static void Postfix(GoPointer __instance)
        {
            if (GameState.inCursorMenu || GameState.sleeping || GameState.inBed || BoatCamera.on) return;
            RaycastHit hit = Hit(__instance); if (hit.collider == null) return;
            var stored = hit.collider.GetComponentInParent<SecuredDisplayItem>();
            var owner = stored != null && stored.Owner != null ? stored.Owner : hit.collider.GetComponentInParent<DisplayStorageState>();
            if (owner == null || owner.Item.held != null || owner.Item.unclickable) return;
            Ray ray = __instance.debugEditorPointer && Camera.main != null ? Camera.main.ScreenPointToRay(Input.mousePosition) : Ray(__instance);
            var held = __instance.GetHeldItem() != null ? __instance.GetHeldItem().GetComponent<ShipItem>() : null;
            // A lid is a moving child collider, including on unsold stock. Never
            // replace its native sale/hammer identity with a content proxy.
            if (!owner.Item.sold || held is ShipItemHammer)
            {
                if (held == null || held.AllowOnItemClick(owner.Item)) Assign(__instance, owner.Item, hit.distance);
                return;
            }
            var box = owner as CigarBoxState;
            if (box != null)
            {
                int slot; Vector3 point;
                if (box.ResolveInterior(ray, out slot, out point))
                {
                    Assign(__instance, box.Target(slot, point, __instance), Vector3.Distance(point, ray.origin));
                    return;
                }
                // Closed glass/slats never allow the visible occupant to be removed.
                Assign(__instance, owner.Item.nailed ? (GoPointerButton)owner.Target(-3, hit.point, __instance) : owner.Item, hit.distance); return;
            }
            var rack = owner as PipeRackState;
            if (rack == null) return;
            // Retain the native pipe as the target while holding tobacco, so its native
            // LoadTobacco path still works in every rack.
            float nearest = 1.8f; SecuredDisplayItem nearestItem = null;
            for (int i = 0; i < rack.Capacity; i++)
            {
                var content = rack.GetContent(i); if (content == null) continue;
                var c = content.Item.GetComponent<Collider>(); RaycastHit contentHit;
                if (c != null && c.Raycast(ray, out contentHit, nearest)) { nearest = contentHit.distance; nearestItem = content; }
            }
            if (nearestItem != null && (held == null || held is ShipItemTobacco ||
                (nearestItem.Item.GetComponent<AshtrayState>() != null && held is ShipItemPipe)))
            { Assign(__instance, nearestItem.Item, nearest); return; }
            Vector3 jarPoint;
            if (held == null && rack.RayAtJar(ray, out jarPoint))
            { Assign(__instance, rack.Target(-2, jarPoint, __instance), Vector3.Distance(jarPoint, ray.origin)); return; }
            if (held != null && (held is ShipItemPipe || held.GetComponent<AshtrayState>() != null))
                Assign(__instance, rack.Target(-1, hit.point, __instance), hit.distance);
            else if (held == null && owner.Item.nailed)
                Assign(__instance, rack.Target(-3, hit.point, __instance), hit.distance);
        }
        private static void Assign(GoPointer pointer, GoPointerButton target, float distance)
        {
            var previous = Pointed(pointer);
            if (previous != null && previous != target) previous.ForceUnlook();
            var proxy = target as StoragePointerTarget;
            if (proxy != null) proxy.Owner.Item.ForceUnlook();
            Pointed(pointer) = target; Distance(pointer) = distance; target.Look(pointer);
        }
    }

    [HarmonyPatch(typeof(GoPointer), "PickUpItem")]
    internal static class NailedStoragePickupPatch
    {
        [HarmonyPrefix] private static bool Prefix(PickupableItem item)
        {
            var ship = item as ShipItem;
            return ship == null || !ship.nailed || (ship.GetComponent<DisplayStorageState>() == null && ship.GetComponent<AshtrayState>() == null);
        }
    }

    // Let vanilla render purchases, controls and descriptions. Only suppress
    // the redundant standalone furniture/slot name, not the cigar recipe hint.
    [HarmonyPatch(typeof(LookUI), "ShowLookText")]
    internal static class StorageLookTextPatch
    {
        private static readonly AccessTools.FieldRef<LookUI, TextMesh> Extra = AccessTools.FieldRefAccess<LookUI, TextMesh>("extraText");
        [HarmonyPostfix] private static void Postfix(LookUI __instance, GoPointerButton button) { HideName(__instance, button); }
        internal static void HideName(LookUI ui, GoPointerButton button)
        {
            if (button == null) return;
            var proxy = button as StoragePointerTarget;
            var owner = proxy != null ? proxy.Owner : button.GetComponent<DisplayStorageState>();
            if (owner != null) Extra(ui).text = string.Empty;
        }
    }
    [HarmonyPatch(typeof(LookUI), "ShowHoldText")]
    internal static class StorageHoldTextPatch
    {
        [HarmonyPostfix] private static void Postfix(LookUI __instance, PickupableItem item) { StorageLookTextPatch.HideName(__instance, item); }
    }

    [HarmonyPatch(typeof(GoPointer), "LateUpdate")]
    internal static class StoragePlacementPreviewPatch
    {
        private static readonly AccessTools.FieldRef<GoPointer, GoPointerButton> Pointed = AccessTools.FieldRefAccess<GoPointer, GoPointerButton>("pointedAtButton");
        [HarmonyPostfix]
        private static void Postfix(GoPointer __instance)
        {
            var target = Pointed(__instance) as StoragePointerTarget;
            var held = __instance.GetHeldItem() as ShipItem;
            var display = held != null ? held.GetComponent<DisplayStorageState>() : null;
            if (display != null && !held.big)
            {
                // Native small-item carry rotation is reconstructed every frame.
                // Compose the facing change once, after that native calculation.
                held.transform.rotation = __instance.transform.rotation * Quaternion.Euler(held.heldRotationOffset, 180, 0);
                display.SyncContents();
            }
            if (target == null || held == null || !target.Owner.Available) return;
            int slot = target.Owner.NearestEmpty(held, target.Point);
            if (slot < 0) return;
            var mesh = held.GetComponent<MeshFilter>(); if (mesh == null || mesh.sharedMesh == null) return;
            Vector3 position; Quaternion rotation; target.Owner.Pose(slot, held, out position, out rotation);
            __instance.GetTargeter().DisplayTargeter(position, rotation, mesh.sharedMesh);
        }
    }
}

// PrefabRegistrationPatches
namespace TobaccoPotAndCigar.Patches
{
    using HarmonyLib;
    using TobaccoPotAndCigar.Prefabs;
[HarmonyPatch(typeof(PrefabsDirectory), "Start")]
    internal static class PrefabRegistrationPatches
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.Last)]
        private static void Prefix(PrefabsDirectory __instance)
        {
            CigarPrefabRegistrar.EnsureRegistered(__instance);
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(PrefabsDirectory __instance)
        {
            CigarPrefabRegistrar.ValidateAndConfigure(__instance);
        }
    }
}


// SaveLoadSyncPatches
namespace TobaccoPotAndCigar.Patches
{
    using HarmonyLib;
    using System;
    using System.Collections.Generic;
    using System.Reflection.Emit;
    using TobaccoPotAndCigar.Runtime;
[HarmonyPatch(typeof(SaveLoadManager), "LoadGame")]
    internal static class LegacyLeafSaveMigrationPatch
    {
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int hooks = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                yield return instruction;
                if (instruction.opcode == OpCodes.Castclass && Equals(instruction.operand, typeof(SaveContainer)))
                {
                    hooks++;
                    yield return new CodeInstruction(OpCodes.Call,
                        AccessTools.Method(typeof(LegacyLeafMigration), nameof(LegacyLeafMigration.MigrateLoadedContainer)));
                }
            }
            if (hooks != 1) throw new InvalidOperationException("Cannot safely locate the loaded Sailwind save container for leaf migration.");
        }
    }

    [HarmonyPatch(typeof(SaveablePrefab), "Load")]
    internal static class SaveLoadSyncPatches
    {
        [HarmonyPostfix]
        private static void Postfix(SaveablePrefab __instance, SavePrefabData data)
        {
            TobaccoPlantPotState pot = __instance.GetComponent<TobaccoPlantPotState>();
            if (pot != null) pot.ReadRainSave(data);
            CigarRuntimeState cigar = __instance.GetComponent<CigarRuntimeState>();
            if (cigar != null) cigar.ReadSave(data);
            RestingPipeState pipe = RestingPipeState.Ensure(__instance.GetComponent<ShipItemPipe>());
            if (pipe != null) pipe.ReadSave(data);
            var box = __instance.GetComponent<CigarBoxState>(); if (box != null) box.ReadSave(data);
            if (data.extraValue4 == 130 && __instance.GetComponent<ShipItem>() != null)
            {
                var stored = __instance.GetComponent<SecuredDisplayItem>() ?? __instance.gameObject.AddComponent<SecuredDisplayItem>();
                stored.ReadSave(data);
            }
            RuntimeStateSynchronizer.Sync(
                __instance.GetComponent<ShipItem>(),
                true);
        }
    }

    [HarmonyPatch(typeof(SaveablePrefab), "PrepareSaveData")]
    internal static class CigarSavePatch
    {
        [HarmonyPrefix]
        private static void Prefix(SaveablePrefab __instance)
        {
            CigarRuntimeState cigar = __instance.GetComponent<CigarRuntimeState>();
            if (cigar != null) cigar.SyncRestParent();
            RestingPipeState pipe = __instance.GetComponent<RestingPipeState>();
            if (pipe != null) pipe.SyncRestParent();
            var stored = __instance.GetComponent<SecuredDisplayItem>(); if (stored != null) stored.Sync();
        }
        [HarmonyPostfix]
        private static void Postfix(SaveablePrefab __instance, SavePrefabData __result)
        {
            TobaccoPlantPotState pot = __instance.GetComponent<TobaccoPlantPotState>();
            if (pot != null) pot.WriteRainSave(__result);
            CigarRuntimeState cigar = __instance.GetComponent<CigarRuntimeState>();
            if (cigar != null) cigar.WriteSave(__result);
            RestingPipeState pipe = __instance.GetComponent<RestingPipeState>();
            if (pipe != null) pipe.WriteSave(__result);
            var box = __instance.GetComponent<CigarBoxState>(); if (box != null) box.WriteSave(__result);
            var stored = __instance.GetComponent<SecuredDisplayItem>(); if (stored != null) stored.WriteSave(__result);
        }
    }
}


// ShopPlacementPatches
namespace TobaccoPotAndCigar.Patches
{
    using HarmonyLib;
    using TobaccoPotAndCigar.Shops;
[HarmonyPatch(typeof(ShopItemSpawner), "SpawnItem")]
    internal static class WrapperRestockPatch
    {
        [HarmonyPrefix]
        private static void Prefix(ShopItemSpawner __instance) { ShopPlacement.PrepareWrapperRestock(__instance); }
    }

    [HarmonyPatch(typeof(ShopItemSpawner), "Start")]
    internal static class ShopItemSpawnerPlacementPatch
    {
        [HarmonyPrefix]
        private static void Prefix(ShopItemSpawner __instance)
        {
            StorageShopPlacement.Configure(__instance);
            ShopPlacement.ConfigureSpawnerBeforeStart(__instance);
        }

        [HarmonyPostfix]
        private static void Postfix(ShopItemSpawner __instance)
        {
            ShopPlacement.AddRelativeDisplaysAfterStart(__instance);
        }
    }

    [HarmonyPatch(typeof(Shopkeeper), "Start")]
    internal static class SageHillsPotPlacementPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Shopkeeper __instance)
        {
            ShopPlacement.AddSageHillsPotAfterStart(__instance);
        }
    }
}


// CigarAssetBundle
namespace TobaccoPotAndCigar.Prefabs
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using TobaccoPotAndCigar.Runtime;
    using UnityEngine;
internal static class CigarAssetBundle
    {
        internal const string BundleName = "dogeggz.cigar.assets";

        private static readonly Dictionary<int, GameObject> Prefabs =
            new Dictionary<int, GameObject>();
        private static AssetBundle bundle;

        internal static bool IsLoaded
        {
            get { return bundle != null && Prefabs.Count == RuntimeConstants.CustomPrefabCount; }
        }

        internal static void Load(string pluginDirectory)
        {
            if (IsLoaded)
                return;

            string path = Path.Combine(pluginDirectory, "assets", BundleName);
            if (!File.Exists(path))
                throw new FileNotFoundException("Cigar AssetBundle is missing.", path);

            bundle = AssetBundle.LoadFromFile(path);
            if (bundle == null)
                throw new InvalidOperationException("Unity could not load " + path);

            CigarWrapperVisual.BindBundleData(bundle.LoadAsset<CigarWrapperVisualData>(
                "assets/cigar/generated/modelpreview/wrappervisualdata.asset"));

            Prefabs.Clear();
            GameObject[] loaded = bundle.LoadAllAssets<GameObject>();
            for (int i = 0; i < loaded.Length; i++)
            {
                SaveablePrefab saveable = loaded[i].GetComponent<SaveablePrefab>();
                if (saveable == null)
                    continue;
                int index = saveable.prefabIndex;
                if (index < RuntimeConstants.TobaccoPotPrefabIndex ||
                    index > RuntimeConstants.LastCustomPrefabIndex)
                    continue;
                if (Prefabs.ContainsKey(index))
                    throw new InvalidOperationException(
                        "AssetBundle contains duplicate prefab index " + index + ".");
                Prefabs.Add(index, loaded[i]);
            }

            for (int index = RuntimeConstants.TobaccoPotPrefabIndex;
                 index <= RuntimeConstants.LastCustomPrefabIndex;
                 index++)
            {
                if (!Prefabs.ContainsKey(index))
                    throw new InvalidOperationException(
                        "AssetBundle is missing prefab index " + index + ".");
            }
        }

        internal static GameObject GetPrefab(int index)
        {
            GameObject prefab;
            return Prefabs.TryGetValue(index, out prefab) ? prefab : null;
        }

        internal static void Unload()
        {
            CigarWrapperVisual.ClearBundleData();
            Prefabs.Clear();
            if (bundle != null)
                bundle.Unload(false);
            bundle = null;
        }
    }
}


// CigarPrefabRegistrar
namespace TobaccoPotAndCigar.Prefabs
{
    using System;
    using TobaccoPotAndCigar.Runtime;
    using UnityEngine;
internal static class CigarPrefabRegistrar
    {
        private const int RequiredDirectoryLength =
            RuntimeConstants.LastCustomPrefabIndex + 1;

        private static bool registered;

        internal static bool IsReady
        {
            get { return registered; }
        }

        internal static void EnsureRegistered(PrefabsDirectory directory)
        {
            if (registered)
                return;
            if (directory == null || directory.directory == null)
                throw new InvalidOperationException("PrefabsDirectory is unavailable.");
            if (!CigarAssetBundle.IsLoaded)
                throw new InvalidOperationException("Cigar AssetBundle is not loaded.");

            GameObject[] resized = directory.directory;
            if (resized.Length < RequiredDirectoryLength)
                Array.Resize(ref resized, RequiredDirectoryLength);

            for (int index = RuntimeConstants.TobaccoPotPrefabIndex;
                 index <= RuntimeConstants.LastCustomPrefabIndex;
                 index++)
            {
                GameObject bundled = CigarAssetBundle.GetPrefab(index);
                GameObject occupied = resized[index];
                if (occupied != null && occupied != bundled)
                {
                    throw new InvalidOperationException(
                        "Prefab index " + index + " is already occupied by " +
                        occupied.name + "; registration aborted without overwriting it.");
                }
                ValidateBundledPrefab(index, bundled);
            }

            directory.directory = resized;
            for (int index = RuntimeConstants.TobaccoPotPrefabIndex;
                 index <= RuntimeConstants.LastCustomPrefabIndex;
                 index++)
            {
                GameObject bundled = CigarAssetBundle.GetPrefab(index);
                directory.directory[index] = bundled;
                PrefabReplacement.RegisterCustomPrefab(index, bundled);
            }
            registered = true;
        }

        internal static void ValidateAndConfigure(PrefabsDirectory directory)
        {
            if (!registered || directory == null || directory.shipItems == null ||
                directory.shipItems.Length < RequiredDirectoryLength)
            {
                throw new InvalidOperationException(
                    "PrefabsDirectory ship-item cache was not populated for indices 610-631.");
            }

            for (int index = RuntimeConstants.TobaccoPotPrefabIndex;
                 index <= RuntimeConstants.LastCustomPrefabIndex;
                 index++)
            {
                if (directory.directory[index] != CigarAssetBundle.GetPrefab(index) ||
                    directory.shipItems[index] == null)
                {
                    throw new InvalidOperationException(
                        "Runtime prefab cache validation failed at index " + index + ".");
                }
            }

            for (int index = RuntimeConstants.FirstVanillaPipePrefabIndex;
                 index <= RuntimeConstants.LastVanillaPipePrefabIndex;
                 index++)
            {
                VanillaPipeSmokeProfile.ApplyRequired(
                    directory.directory[index],
                    index);
            }

            ConfigureTobaccoHint(
                directory.directory[RuntimeConstants.WhiteTobaccoPrefabIndex],
                RuntimeConstants.WhiteTobaccoPrefabIndex);
            ConfigureTobaccoHint(
                directory.directory[RuntimeConstants.GreenTobaccoPrefabIndex],
                RuntimeConstants.GreenTobaccoPrefabIndex);
            ConfigureTobaccoHint(
                directory.directory[RuntimeConstants.BlackTobaccoPrefabIndex],
                RuntimeConstants.BlackTobaccoPrefabIndex);
            ConfigureTobaccoHint(
                directory.directory[RuntimeConstants.BrownTobaccoPrefabIndex],
                RuntimeConstants.BrownTobaccoPrefabIndex);
            ConfigureTobaccoHint(
                directory.directory[RuntimeConstants.BlueTobaccoPrefabIndex],
                RuntimeConstants.BlueTobaccoPrefabIndex);

            ConfigureVanillaDrying(
                directory.directory[RuntimeConstants.GreenTobaccoPrefabIndex],
                RuntimeConstants.WhiteTobaccoPrefabIndex);
            ConfigureVanillaDrying(
                directory.directory[RuntimeConstants.WhiteTobaccoPrefabIndex],
                RuntimeConstants.BrownTobaccoPrefabIndex);
            ConfigureVanillaDrying(
                directory.directory[RuntimeConstants.BrownTobaccoPrefabIndex],
                RuntimeConstants.BlackTobaccoPrefabIndex);

            Plugin.LogSource?.LogInfo(
                "Registered bundled prefabs 610-631 and rack-only tobacco drying.");
        }

        internal static void Reset()
        {
            registered = false;
            PrefabReplacement.ClearCustomPrefabs();
        }

        private static void ConfigureVanillaDrying(
            GameObject prefab,
            int resultPrefabIndex)
        {
            if (prefab == null)
                throw new InvalidOperationException(
                    "Vanilla drying source for result " + resultPrefabIndex + " is missing.");
            RackOnlyDrying drying = prefab.GetComponent<RackOnlyDrying>();
            if (drying == null)
                drying = prefab.AddComponent<RackOnlyDrying>();
            drying.ConfigureResultPrefab(resultPrefabIndex);
        }

        private static void ConfigureTobaccoHint(
            GameObject prefab,
            int prefabIndex)
        {
            ShipItemTobacco tobacco = prefab != null
                ? prefab.GetComponent<ShipItemTobacco>()
                : null;
            if (tobacco == null || string.IsNullOrEmpty(tobacco.name))
            {
                throw new InvalidOperationException(
                    "Tobacco prefab " + prefabIndex +
                    " cannot provide its pointer hint name.");
            }

            tobacco.description = tobacco.name;
        }

        private static void ValidateBundledPrefab(int index, GameObject prefab)
        {
            if (prefab == null || prefab.GetComponent<ShipItem>() == null ||
                prefab.GetComponent<SaveablePrefab>() == null)
            {
                throw new InvalidOperationException(
                    "Bundled prefab " + index + " is missing its item/save contract.");
            }

            bool specialized = index == RuntimeConstants.TobaccoPotPrefabIndex
                ? prefab.GetComponent<TobaccoPlantPotState>() != null
                : index == RuntimeConstants.FreshTobaccoLeafPrefabIndex
                    ? prefab.GetComponent<FreshTobaccoLeafState>() != null &&
                      prefab.GetComponent<RackOnlyDrying>() != null
                    : index == RuntimeConstants.CigarPrefabIndex
                        ? prefab.GetComponent<CigarRuntimeState>() != null
                        : index == RuntimeConstants.DriedTobaccoLeafPrefabIndex
                            ? prefab.GetComponent<DriedTobaccoLeafState>() != null
                            : index == RuntimeConstants.CigarWrapperPrefabIndex
                                ? prefab.GetComponent<CigarWrapperState>() != null &&
                                  prefab.GetComponent<CigarWrapperVisual>() != null
                                : index <= RuntimeConstants.GlassAshtrayPrefabIndex ? prefab.GetComponent<AshtrayState>() != null
                                : index <= RuntimeConstants.DragonCliffsPipeRackPrefabIndex ? prefab.GetComponent<PipeRackState>() != null
                                : prefab.GetComponent<CigarBoxState>() != null;
            if (!specialized)
            {
                throw new InvalidOperationException(
                    "Bundled prefab " + index +
                    " does not contain its authored runtime component.");
            }
            if(index==RuntimeConstants.CigarWrapperPrefabIndex)
            {
                string error;
                if(!prefab.GetComponent<CigarWrapperVisual>().IsConfigured(out error))
                    throw new InvalidOperationException("Bundled wrapper filling is invalid: "+error);
            }
        }
    }
}


// ShopPlacement
namespace TobaccoPotAndCigar.Shops
{
    using System.Collections.Generic;
    using System.Reflection;
    using TobaccoPotAndCigar.Prefabs;
    using TobaccoPotAndCigar.Runtime;
    using UnityEngine;
internal static class ShopPlacement
    {
        private const string GoldRockScene = "island 1 A Gold Rock";
        private const string DragonCliffsScene = "island 9 E Dragon Cliffs";
        private const string SageHillsScene = "island 13 E (Sage Hills)";
        private const string FortAestrinScene = "island 15 M (Fort)";
        private const string NeverdinScene = "island 3 A Neverdin";
        private const string SirenSongScene = "island 21 M (siren song)";
        private const string CrabBeachScene = "island 11 E (Crab Beach)";
        private const string OasisScene = "island 20 A (Oasis)";
        private const string EastwindScene = "island 19 M (Eastwind)";
        private const string PonderingPeakScene = "island 39 (onsen)";
        private const string ChronosScene = "island 25 (chronos)";

        private static readonly HashSet<string> LoggedIdentityFailures =
            new HashSet<string>();
        private static readonly FieldInfo StockItem = typeof(ShopItemSpawner).GetField("item", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo ItemShopArea = typeof(ShipItem).GetField("shopArea", BindingFlags.Instance | BindingFlags.NonPublic);

        internal static void ResetDiagnostics()
        {
            LoggedIdentityFailures.Clear();
        }

        internal static void ConfigureSpawnerBeforeStart(ShopItemSpawner spawner)
        {
            StorageShopPlacement.Configure(spawner);
            if (!CigarPrefabRegistrar.IsReady || spawner == null ||
                !spawner.gameObject.scene.IsValid())
                return;

            string scene = spawner.gameObject.scene.name;
            string name = spawner.gameObject.name;
            bool replace =
                (scene == GoldRockScene &&
                 (name == "shop item (171)" || name == "shop item (173)")) ||
                (scene == DragonCliffsScene &&
                 name == "shop item spawner (184)");
            if (!replace)
                return;
            if (!IsUniqueSpawner(spawner))
            {
                LogIdentityFailure(scene + "/" + name);
                return;
            }

            spawner.itemPrefab = CigarAssetBundle.GetPrefab(
                RuntimeConstants.CigarWrapperPrefabIndex);
        }

        internal static void AddRelativeDisplaysAfterStart(ShopItemSpawner anchor)
        {
            if (!CigarPrefabRegistrar.IsReady || anchor == null ||
                !anchor.gameObject.scene.IsValid())
                return;

            string scene = anchor.gameObject.scene.name;
            string name = anchor.gameObject.name;
            // Most native stock is unrelated. Only scan for duplicate identities
            // after identifying one of our anchors or generated wrapper displays.
            bool candidate = IsWrapperDisplay(anchor) ||
                (scene == FortAestrinScene && name == "shop item (24)") ||
                (scene == NeverdinScene && name == "shop item (32)") ||
                (scene == SirenSongScene && name == "shop item (77)") ||
                (scene == CrabBeachScene && name == "shop spawner crab cakes (50)") ||
                (scene == OasisScene && name == "shop item (65)") ||
                (scene == EastwindScene && name == "shop item (42)") ||
                (scene == PonderingPeakScene && name == "shop item (80)") ||
                (scene == ChronosScene && name == "shop item (136)");
            if (!candidate || !IsUniqueSpawner(anchor))
                return;

            if (scene == DragonCliffsScene &&
                name == "shop item spawner (184)")
            {
                AlignWrapper(anchor);
                CreateRelativeSpawner(
                    anchor,
                    "DogEggz.Cigar.DriedLeaf.DragonCliffs.2",
                    new Vector3(0f, 0f, -.29f),
                    Vector3.zero,
                    RuntimeConstants.CigarWrapperPrefabIndex);
            }
            else if (scene == FortAestrinScene && name == "shop item (24)")
            {
                CreateRelativeSpawner(
                    anchor,
                    "DogEggz.Cigar.DriedLeaf.Fort.1",
                    new Vector3(-0.734f, 0f, 0f),
                    new Vector3(90f, 0f, 0f),
                    RuntimeConstants.CigarWrapperPrefabIndex);
                CreateRelativeSpawner(
                    anchor,
                    "DogEggz.Cigar.DriedLeaf.Fort.2",
                    new Vector3(-0.734f, 0f, -0.367f),
                    new Vector3(90f, 0f, 0f),
                    RuntimeConstants.CigarWrapperPrefabIndex);
                CreateRelativeSpawner(
                    anchor,
                    "DogEggz.Cigar.DriedLeaf.Fort.3",
                    new Vector3(-0.734f, 0f, -0.734f),
                    new Vector3(90f, 0f, 0f),
                    RuntimeConstants.CigarWrapperPrefabIndex);
            }
            if (IsWrapperDisplay(anchor))
            {
                AlignWrapper(anchor);
                ReconcileWrapperStock(anchor);
            }
            // Each regional tobacco counter also carries its matching ashtray.
            if (scene == GoldRockScene && name == "shop item (171)")
                CreateRelativeSpawner(anchor, "DogEggz.Cigar.Ashtray.AlAnkh", new Vector3(.024026f, -.062822f, -2.044131f), new Vector3(.159581f, .015844f, 11.339650f), RuntimeConstants.AlAnkhWoodAshtrayPrefabIndex);
            if (scene == DragonCliffsScene && name == "shop item spawner (184)")
                CreateRelativeSpawner(anchor, "DogEggz.Cigar.Ashtray.Emerald", new Vector3(-.032810f, -.082593f, .246239f), new Vector3(359.519700f, .031987f, 352.379300f), RuntimeConstants.EmeraldWoodAshtrayPrefabIndex);
            if (scene == FortAestrinScene && name == "shop item (24)")
                CreateRelativeSpawner(anchor, "DogEggz.Cigar.Ashtray.Aestrin", new Vector3(-.749037f, -.127f, -.995985f), Vector3.zero, RuntimeConstants.AestrinWoodAshtrayPrefabIndex);

            // Anchor to native food stock at each tavern; the measured offsets keep
            // the complete tray supported and clear of the existing goods.
            if (scene == NeverdinScene && name == "shop item (32)")
                CreateTavernAshtray(anchor, "Neverdin", new Vector3(.749442f, -.032372f, -.702033f), RuntimeConstants.ClayAshtrayPrefabIndex);
            if (scene == SirenSongScene && name == "shop item (77)")
                CreateTavernAshtray(anchor, "SirenSong", new Vector3(.089528f, -.031021f, -.304271f), RuntimeConstants.MetalAshtrayPrefabIndex);
            if (scene == CrabBeachScene && name == "shop spawner crab cakes (50)")
                CreateTavernAshtray(anchor, "CrabBeach", new Vector3(-.226775f, -.114298f, .099208f), RuntimeConstants.CeramicAshtrayPrefabIndex);
            if (scene == OasisScene && name == "shop item (65)")
                CreateRelativeSpawner(anchor, "DogEggz.Cigar.Ashtray.Oasis", new Vector3(-.224900f,-.223426f,-.000044f), new Vector3(359.792200f,358.005300f,.117850f), RuntimeConstants.IvoryHornAshtrayPrefabIndex);
            if (scene == EastwindScene && name == "shop item (42)")
                CreateRelativeSpawner(anchor, "DogEggz.Cigar.Ashtray.Eastwind", new Vector3(.069258f,-.080073f,.195737f), new Vector3(.171237f,291.801800f,359.907600f), RuntimeConstants.MarbleAshtrayPrefabIndex);
            if (scene == PonderingPeakScene && name == "shop item (80)")
                CreateRelativeSpawner(anchor, "DogEggz.Cigar.Ashtray.PonderingPeak", new Vector3(-.154666f,-.120439f,-.173347f), new Vector3(359.227300f,311.151900f,359.374400f), RuntimeConstants.JadeAshtrayPrefabIndex);
            if (scene == ChronosScene && name == "shop item (136)")
                CreateRelativeSpawner(anchor, "DogEggz.Cigar.Ashtray.Chronos", new Vector3(-.095731f,-.085555f,.224801f), new Vector3(359.046900f,68.828830f,.145291f), RuntimeConstants.GlassAshtrayPrefabIndex);
        }

        private static void CreateTavernAshtray(ShopItemSpawner anchor, string town, Vector3 offset, int prefabIndex)
        {
            // Native food spawners may be tilted. Use the upright orientation
            // checked against their counters, independently of the food's pose.
            CreateRelativeSpawner(anchor, "DogEggz.Cigar.Ashtray." + town, offset,
                Quaternion.Inverse(anchor.transform.rotation).eulerAngles, prefabIndex);
        }

        internal static bool IsWrapperDisplay(ShopItemSpawner spawner)
        {
            if (spawner == null || !spawner.gameObject.scene.IsValid()) return false;
            string scene = spawner.gameObject.scene.name, name = spawner.gameObject.name;
            return (scene == GoldRockScene && (name == "shop item (171)" || name == "shop item (173)")) ||
                (scene == DragonCliffsScene && (name == "shop item spawner (184)" || name == "DogEggz.Cigar.DriedLeaf.DragonCliffs.2")) ||
                (scene == FortAestrinScene && (name == "DogEggz.Cigar.DriedLeaf.Fort.1" || name == "DogEggz.Cigar.DriedLeaf.Fort.2" || name == "DogEggz.Cigar.DriedLeaf.Fort.3"));
        }

        internal static void PrepareWrapperRestock(ShopItemSpawner spawner)
        {
            if (!CigarPrefabRegistrar.IsReady || !IsWrapperDisplay(spawner)) return;
            spawner.itemPrefab = CigarAssetBundle.GetPrefab(RuntimeConstants.CigarWrapperPrefabIndex);
            AlignWrapper(spawner);
        }

        private static void AlignWrapper(ShopItemSpawner spawner)
        {
            if (spawner.gameObject.scene.name == DragonCliffsScene)
            {
                // Parallel wrappers, spaced across their narrow sides on the counter.
                Quaternion rotation = Quaternion.Euler(0, 317.992f, 0);
                spawner.transform.localPosition = new Vector3(-34.694f, 3.453f, -557.039f) +
                    rotation * new Vector3(0, 0, spawner.name == "shop item spawner (184)" ? -.08f : -.37f);
                spawner.transform.localRotation = rotation;
                return;
            }
            if (spawner.gameObject.scene.name == GoldRockScene)
            {
                // These two table slots were only 20 cm apart. Turn the 41 cm
                // wrappers across the row and space their narrow sides by 26 cm.
                spawner.transform.localPosition = spawner.name == "shop item (171)"
                    ? new Vector3(1568.48525f, 7.368f, -399.9322f)
                    : new Vector3(1568.17975f, 7.368f, -399.5058f);
                spawner.transform.localEulerAngles = new Vector3(0, 145.594f, 0);
                return;
            }
            // The new model's opening is +Y; old leaf displays used a Z-up mesh.
            spawner.transform.rotation = Quaternion.FromToRotation(spawner.transform.up, Vector3.up) * spawner.transform.rotation;
        }

        private static void ReconcileWrapperStock(ShopItemSpawner spawner)
        {
            ShipItem old = StockItem.GetValue(spawner) as ShipItem;
            if (old == null || old.sold) return;
            SaveablePrefab identity = old.GetComponent<SaveablePrefab>();
            if (identity == null || identity.prefabIndex != RuntimeConstants.DriedTobaccoLeafPrefabIndex) return;
            GameObject prefab = CigarAssetBundle.GetPrefab(RuntimeConstants.CigarWrapperPrefabIndex);
            if (prefab == null) return;
            ShipItem replacement = Object.Instantiate(prefab, spawner.transform.position, spawner.transform.rotation).GetComponent<ShipItem>();
            replacement.transform.SetParent(spawner.transform, true);
            ShopArea area = ItemShopArea.GetValue(old) as ShopArea;
            if (area != null)
            {
                area.itemsForSale.Remove(old);
                replacement.AddToShop(area);
            }
            replacement.gameObject.SetActive(old.gameObject.activeSelf);
            StockItem.SetValue(spawner, replacement);
            old.gameObject.SetActive(false);
            Object.Destroy(old.gameObject);
        }

        internal static void AddSageHillsPotAfterStart(Shopkeeper shopkeeper)
        {
            if (!CigarPrefabRegistrar.IsReady || shopkeeper == null ||
                !shopkeeper.gameObject.scene.IsValid() ||
                shopkeeper.gameObject.scene.name != SageHillsScene ||
                shopkeeper.gameObject.name != "shopkeeper (2)" ||
                !IsUniqueShopkeeper(shopkeeper))
            {
                return;
            }

            Transform parent = shopkeeper.transform.parent;
            Transform shopArea = FindDirectChild(parent, "shop area (3)");
            if (shopArea == null || shopArea.GetComponent<ShopArea>() == null)
            {
                LogIdentityFailure(SageHillsScene + "/shopkeeper (2)+shop area (3)");
                return;
            }

            Transform table = FindDirectChild(parent, "east_market_stall 1 (1)");
            if (table == null) { LogIdentityFailure(SageHillsScene + "/east_market_stall 1 (1)"); return; }
            // The imported table is Z-up: its local Y edge runs along the ground.
            Vector3 row = Vector3.ProjectOnPlane(-table.up, Vector3.up).normalized;
            if (Vector3.Dot(row, shopkeeper.transform.forward) < 0) row = -row;
            Vector3 step = shopkeeper.transform.InverseTransformVector(row * .72f);
            Vector3 rotation = (Quaternion.Inverse(shopkeeper.transform.rotation) * Quaternion.LookRotation(row, Vector3.up)).eulerAngles;
            for (int i = 0; i < 3; i++)
                CreateRelativeSpawner(shopkeeper.transform, false, 1f,
                    "DogEggz.Cigar.TobaccoPot.SageHills" + (i == 0 ? "" : "." + (i + 1)),
                    new Vector3(-2.175f, .60f, 3.931f) + step * i, rotation,
                    RuntimeConstants.TobaccoPotPrefabIndex);
        }

        private static void CreateRelativeSpawner(
            ShopItemSpawner anchor,
            string hostName,
            Vector3 localPosition,
            Vector3 localEuler,
            int prefabIndex)
        {
            CreateRelativeSpawner(
                anchor.transform,
                anchor.availableAtNight,
                anchor.priceMult,
                hostName,
                localPosition,
                localEuler,
                prefabIndex);
        }

        private static void CreateRelativeSpawner(
            Transform anchor,
            bool availableAtNight,
            float priceMultiplier,
            string hostName,
            Vector3 localPosition,
            Vector3 localEuler,
            int prefabIndex)
        {
            Transform parent = anchor.parent;
            if (parent == null) return;
            Transform existing = FindDirectChild(parent, hostName);
            if (existing != null)
            {
                ShopItemSpawner existingSpawner = existing.GetComponent<ShopItemSpawner>();
                if (prefabIndex == RuntimeConstants.CigarWrapperPrefabIndex && existingSpawner != null)
                {
                    existingSpawner.itemPrefab = CigarAssetBundle.GetPrefab(prefabIndex);
                    AlignWrapper(existingSpawner);
                    ReconcileWrapperStock(existingSpawner);
                }
                return;
            }

            GameObject host = new GameObject(hostName);
            host.layer = anchor.gameObject.layer;
            host.transform.SetParent(parent, true);
            host.transform.position = anchor.TransformPoint(localPosition);
            host.transform.rotation = anchor.rotation * Quaternion.Euler(localEuler);
            host.transform.localScale = Vector3.one;
            host.AddComponent<MeshFilter>();
            host.AddComponent<MeshRenderer>();
            ShopItemSpawner spawner = host.AddComponent<ShopItemSpawner>();
            spawner.itemPrefab = CigarAssetBundle.GetPrefab(prefabIndex);
            spawner.availableAtNight = availableAtNight;
            spawner.priceMult = priceMultiplier;
            if (prefabIndex == RuntimeConstants.CigarWrapperPrefabIndex) AlignWrapper(spawner);
            // Ashtray display poses follow the measured counter surface. Pickup
            // separately resets their facing and tilt in AshtrayState.
            Plugin.LogSource?.LogInfo(
                "Added shop display " + hostName + " in " +
                anchor.gameObject.scene.name + ".");
        }

        private static bool IsUniqueSpawner(ShopItemSpawner target)
        {
            int count = 0;
            ShopItemSpawner[] all =
                Resources.FindObjectsOfTypeAll<ShopItemSpawner>();
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].gameObject.scene.IsValid() &&
                    all[i].gameObject.scene.handle == target.gameObject.scene.handle &&
                    all[i].gameObject.name == target.gameObject.name)
                {
                    count++;
                }
            }
            return count == 1;
        }

        private static bool IsUniqueShopkeeper(Shopkeeper target)
        {
            int count = 0;
            Shopkeeper[] all = Resources.FindObjectsOfTypeAll<Shopkeeper>();
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].gameObject.scene.IsValid() &&
                    all[i].gameObject.scene.handle == target.gameObject.scene.handle &&
                    all[i].gameObject.name == target.gameObject.name)
                {
                    count++;
                }
            }
            return count == 1;
        }

        private static Transform FindDirectChild(Transform parent, string name)
        {
            if (parent == null)
                return null;
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (child.name == name)
                    return child;
            }
            return null;
        }

        private static void LogIdentityFailure(string identity)
        {
            if (LoggedIdentityFailures.Add(identity))
            {
                Plugin.LogSource?.LogError(
                    "Shop placement identity did not resolve exactly once: " +
                    identity + ". That display was skipped.");
            }
        }
    }
}


// The approved regional counter layouts use native spawners and restocking.
namespace TobaccoPotAndCigar.Shops
{
    using System;
    using System.Linq;
    using System.Reflection;
    using UnityEngine;
    using TobaccoPotAndCigar.Prefabs;
    using TobaccoPotAndCigar.Runtime;

    internal static class StorageShopPlacement
    {
        internal static void Configure(ShopItemSpawner first)
        {
            if (!CigarPrefabRegistrar.IsReady || first == null || !first.gameObject.scene.IsValid()) return;
            string scene = first.gameObject.scene.name;
            int region = scene == "island 1 A Gold Rock" ? 0 : scene == "island 15 M (Fort)" ? 1 : scene == "island 9 E Dragon Cliffs" ? 2 : -1;
            if (region < 0) return;
            string anchorName = region == 0 ? "shop item (171)" : region == 1 ? "shop item (24)" : "shop item spawner (184)";
            var all = Resources.FindObjectsOfTypeAll<ShopItemSpawner>().Where(s => s.gameObject.scene == first.gameObject.scene).ToArray();
            var anchor = all.SingleOrDefault(s => s.name == anchorName);
            if (anchor == null || anchor.GetComponent<StorageShopMarker>() != null) return;
            anchor.gameObject.AddComponent<StorageShopMarker>();
            Vector3 origin = anchor.transform.position; origin.y = 0;
            if (region == 0)
            {
                Replace(all, "shop item (176)", 627, origin + new Vector3(-.68553f, 3.63290f, .85053f), Quaternion.Euler(348.659f, 236.368f, 0) * Quaternion.Euler(0,180,0));
                Create(anchor, 630, origin + new Vector3(-66.07019f, 3.31741f, -4.52365f), Quaternion.Euler(0, 161.34f, 0), true);
            }
            else if (region == 1)
            {
                Vector3 deskPosition = origin + new Vector3(.04406f, 3.06400f, .35797f);
                Quaternion deskRotation = Quaternion.Euler(0,179.065f,0);
                Create(anchor, 625, deskPosition, deskRotation, false);
                // R08 has a horizontal rail, not the older vertical backplate. Display it
                // flat, 48 cm along the counter from the desk rack, with aligned centres.
                Quaternion wallRotation = Quaternion.Euler(0,359.065f,0);
                var deskCollider = CigarAssetBundle.GetPrefab(625).GetComponent<BoxCollider>();
                var wallCollider = CigarAssetBundle.GetPrefab(626).GetComponent<BoxCollider>();
                Vector3 wallPosition = deskPosition + deskRotation * (deskCollider.center + Vector3.right * .48f) - wallRotation * wallCollider.center;
                wallPosition.y = deskPosition.y + deskCollider.center.y - deskCollider.size.y * .5f - wallCollider.center.y + wallCollider.size.y * .5f;
                Create(anchor, 626, wallPosition, wallRotation, false);
                Create(anchor, 629, origin + new Vector3(-38.75871f,3.03934f,-12.07408f), Quaternion.Euler(0,179.50f,0), true);
            }
            else
            {
                Replace(all, "shop item spawner (188)", 628, origin + new Vector3(.77688f,3.32881f,-.54858f), Quaternion.Euler(0,321.609f,352.364f) * Quaternion.Euler(0,180,0));
                Create(anchor,631,origin + new Vector3(-35.08951f,3.39949f,22.71332f),Quaternion.Euler(0,135.68f,0),true);
                var crates = new[] {185,186,187}.Select(n=>all.Single(s=>s.name=="shop item spawner ("+n+")")).ToArray();
                var basePoint = origin + new Vector3(.95999f,3.48246f,-1.14672f);
                var step = new Vector3(.027805f,.26464f,.02203f);
                for(int i=0;i<3;i++) crates[i].transform.position=basePoint+step*i;
                var stackHost=new GameObject("DogEggz.Cigar.TobaccoStockStack");
                stackHost.transform.SetParent(anchor.transform.parent,false); stackHost.transform.position=basePoint;
                stackHost.AddComponent<StorageStockStack>().Configure(crates,basePoint,step);
            }
        }
        private static void Replace(ShopItemSpawner[] all,string name,int id,Vector3 position,Quaternion rotation)
        {
            var target=all.Single(s=>s.name==name); target.itemPrefab=CigarAssetBundle.GetPrefab(id);
            target.transform.SetPositionAndRotation(position,rotation);
        }
        private static void Create(ShopItemSpawner anchor,int id,Vector3 position,Quaternion rotation,bool tavern)
        {
            var host=new GameObject("DogEggz.Cigar.Storage."+id);
            host.transform.SetParent(anchor.transform.parent,false); host.transform.SetPositionAndRotation(position,rotation);
            host.AddComponent<MeshFilter>(); host.AddComponent<MeshRenderer>();
            var spawner=host.AddComponent<ShopItemSpawner>(); spawner.itemPrefab=CigarAssetBundle.GetPrefab(id); spawner.priceMult=1; spawner.availableAtNight=tavern || anchor.availableAtNight;
        }
    }
    public sealed class StorageShopMarker : MonoBehaviour { }
    public sealed class StorageStockStack : MonoBehaviour
    {
        private ShopItemSpawner[] spawners;
        private Vector3 baseLocal, stepLocal;
        private float nextCheck;
        private static readonly FieldInfo Stock = typeof(ShopItemSpawner).GetField("item",BindingFlags.Instance|BindingFlags.NonPublic);
        private static readonly FieldInfo ReturnPosition = typeof(ShipItem).GetField("shopPos",BindingFlags.Instance|BindingFlags.NonPublic);
        private static readonly FieldInfo Shop = typeof(ShipItem).GetField("shopArea",BindingFlags.Instance|BindingFlags.NonPublic);
        public void Configure(ShopItemSpawner[] values,Vector3 bottom,Vector3 step)
        { spawners=values; baseLocal=transform.InverseTransformPoint(bottom); stepLocal=transform.InverseTransformVector(step); }
        private void LateUpdate()
        { if (Time.unscaledTime<nextCheck || spawners==null) return; nextCheck=Time.unscaledTime+.2f; Reconcile(); }
        public void Reconcile()
        {
            int level=0;
            for(int i=0;i<spawners.Length;i++)
            {
                var stock=Stock.GetValue(spawners[i]) as ShipItem;
                bool present=stock!=null && !stock.sold;
                Vector3 heldPosition=stock!=null?stock.transform.position:Vector3.zero;
                // Sold/missing crates do not leave the remaining shop goods floating.
                Vector3 position=transform.TransformPoint(baseLocal+stepLocal*level);
                spawners[i].transform.position=position;
                if(!present) continue;
                stock.transform.position=stock.held==null?position:heldPosition;
                // Native return-to-shop restores this local pose relative to the shop area.
                var area=Shop.GetValue(stock) as ShopArea;
                if(area!=null) ReturnPosition.SetValue(stock,area.transform.InverseTransformPoint(position));
                level++;
            }
        }
    }
}

// PrefabReplacement
namespace TobaccoPotAndCigar.Runtime
{
    using System;
    using UnityEngine;
public static class PrefabReplacement
    {
        private static readonly GameObject[] CustomPrefabs = new GameObject[RuntimeConstants.CustomPrefabCount];

        public static void RegisterCustomPrefab(int index, GameObject prefab)
        {
            int slot = index - RuntimeConstants.TobaccoPotPrefabIndex;
            if (slot < 0 || slot >= CustomPrefabs.Length)
                throw new ArgumentOutOfRangeException("index");
            CustomPrefabs[slot] = prefab;
        }

        public static void ClearCustomPrefabs()
        {
            for (int i = 0; i < CustomPrefabs.Length; i++)
                CustomPrefabs[i] = null;
        }

        public static bool CanSpawn(int prefabIndex)
        {
            return ResolvePrefab(prefabIndex) != null &&
                   SaveLoadManager.instance != null;
        }

        public static ShipItem SpawnOwned(
            int prefabIndex,
            Vector3 position,
            Quaternion rotation,
            ShipItem context,
            float health,
            float amount)
        {
            GameObject prefab = ResolvePrefab(prefabIndex);
            if (prefab == null)
                return null;
            if (SaveLoadManager.instance == null)
            {
                RuntimeDiagnostics.Error("Cannot spawn prefab " + prefabIndex +
                                         ": SaveLoadManager is unavailable.");
                return null;
            }

            GameObject instance = null;
            try
            {
            instance = UnityEngine.Object.Instantiate(
                prefab,
                position,
                rotation);
            ShipItem item = instance.GetComponent<ShipItem>();
            SaveablePrefab saveable = instance.GetComponent<SaveablePrefab>();
            if (item == null || saveable == null)
            {
                RuntimeDiagnostics.Error("Registered prefab " + prefabIndex +
                                         " lost its ShipItem/SaveablePrefab contract.");
                UnityEngine.Object.Destroy(instance);
                return null;
            }

            if (context != null)
            {
                SaveablePrefab contextSaveable = context.GetComponent<SaveablePrefab>();
                if (contextSaveable != null)
                {
                    saveable.SetParentObject(contextSaveable.GetParentObject());
                    saveable.currentCrateId = contextSaveable.currentCrateId;
                }
                if (context.transform.parent != null)
                    instance.transform.SetParent(context.transform.parent, true);
            }

            item.sold = true;
            item.health = health;
            item.amount = amount;
            RuntimeStateSynchronizer.Sync(item, true);
            saveable.RegisterToSave();
            return item;
            }
            catch (Exception exception)
            {
                if (instance != null)
                {
                    SaveablePrefab orphan = instance.GetComponent<SaveablePrefab>();
                    if (orphan != null && SaveLoadManager.instance != null) orphan.Unregister();
                    instance.SetActive(false);
                    UnityEngine.Object.Destroy(instance);
                }
                RuntimeDiagnostics.Error("Could not create prefab " + prefabIndex + "; partial output removed: " + exception.Message);
                return null;
            }
        }

        public static bool ReplaceOwnedItem(
            ShipItem source,
            int resultPrefabIndex,
            float replacementHealth,
            float replacementAmount)
        {
            return ReplaceOwnedItem(
                source,
                resultPrefabIndex,
                replacementHealth,
                replacementAmount,
                Vector3.zero);
        }

        public static bool ReplaceOwnedItem(
            ShipItem source,
            int resultPrefabIndex,
            float replacementHealth,
            float replacementAmount,
            Vector3 worldPositionOffset)
        {
            return ReplaceOwnedItem(source, resultPrefabIndex, replacementHealth, replacementAmount,
                worldPositionOffset, source != null ? source.transform.rotation : Quaternion.identity);
        }

        public static bool ReplaceOwnedItem(ShipItem source, int resultPrefabIndex,
            float replacementHealth, float replacementAmount, Vector3 worldPositionOffset, Quaternion rotation)
        {
            if (!CanTransform(source))
                return false;

            ShipItem replacement = SpawnOwned(
                resultPrefabIndex,
                source.transform.position + worldPositionOffset,
                rotation,
                source,
                replacementHealth,
                replacementAmount);
            if (replacement == null)
                return false;

            ConsumeOwned(source);
            return true;
        }

        public static bool CombineOwnedItems(ShipItem target, ShipItem held, int resultPrefabIndex, Vector3 offset, Quaternion rotation)
        {
            if (target == held || !CanTransform(target) || !CanTransform(held)) return false;
            ShipItem result = SpawnOwned(resultPrefabIndex, target.transform.position + offset,
                rotation, target, 0, 0);
            if (result == null) return false;
            ConsumeOwned(held);
            ConsumeOwned(target);
            return true;
        }

        public static void ConsumeOwned(ShipItem source)
        {
            if (source == null) return;
            if (source.held != null) source.held.DropItem();
            source.sold = false;
            if (source.itemRigidbodyC != null) source.FreezeItem();
            else
                foreach (Collider collider in source.GetComponents<Collider>()) collider.enabled = false;
            source.gameObject.SetActive(false);
            source.DestroyItem();
        }

        public static bool CanTransform(ShipItem source)
        {
            if (source == null || !source.sold || !source.gameObject.activeInHierarchy || source.nailed)
                return false;

            SaveablePrefab saveable = source.GetComponent<SaveablePrefab>();
            if (saveable == null)
                return false;
            if (saveable.currentCrateId > 0)
            {
                return false;
            }
            if (source.itemRigidbodyC != null && source.GetCurrentInventorySlot() >= 0)
            {
                return false;
            }
            return true;
        }

        public static GameObject ResolvePrefab(int prefabIndex)
        {
            PrefabsDirectory directory = PrefabsDirectory.instance;
            if (directory == null || directory.directory == null ||
                prefabIndex < 0 || prefabIndex >= directory.directory.Length)
            {
                RuntimeDiagnostics.Error("Prefab directory cannot resolve index " +
                                         prefabIndex + ".");
                return null;
            }

            GameObject prefab = directory.directory[prefabIndex];
            int customSlot = prefabIndex - RuntimeConstants.TobaccoPotPrefabIndex;
            if (customSlot >= 0 && customSlot < CustomPrefabs.Length &&
                prefab != CustomPrefabs[customSlot])
            {
                RuntimeDiagnostics.Error("Custom prefab index " + prefabIndex +
                                         " is not the registered bundled asset.");
                return null;
            }
            return prefab;
        }
    }
}


// RuntimeConstants
namespace TobaccoPotAndCigar.Runtime
{

public static class RuntimeConstants
    {
        public const int FirstVanillaPipePrefabIndex = 300;
        public const int LastVanillaPipePrefabIndex = 302;

        public const int TobaccoPotPrefabIndex = 610;
        public const int FreshTobaccoLeafPrefabIndex = 611;
        public const int CigarPrefabIndex = 612;
        public const int DriedTobaccoLeafPrefabIndex = 613;
        public const int CigarWrapperPrefabIndex = 614;
        public const int ClayAshtrayPrefabIndex = 615;
        public const int MetalAshtrayPrefabIndex = 616;
        public const int CeramicAshtrayPrefabIndex = 617;
        public const int AlAnkhWoodAshtrayPrefabIndex = 618;
        public const int AestrinWoodAshtrayPrefabIndex = 619;
        public const int EmeraldWoodAshtrayPrefabIndex = 620;
        public const int MarbleAshtrayPrefabIndex = 621;
        public const int JadeAshtrayPrefabIndex = 622;
        public const int IvoryHornAshtrayPrefabIndex = 623;
        public const int GlassAshtrayPrefabIndex = 624;
        public const int AestrinDeskPipeRackPrefabIndex = 625;
        public const int AestrinWallPipeRackPrefabIndex = 626;
        public const int AlAnkhPipeRackPrefabIndex = 627;
        public const int DragonCliffsPipeRackPrefabIndex = 628;
        public const int AestrinCigarBoxPrefabIndex = 629;
        public const int AlAnkhCigarBoxPrefabIndex = 630;
        public const int DragonCliffsCigarBoxPrefabIndex = 631;
        public const int LastCustomPrefabIndex = DragonCliffsCigarBoxPrefabIndex;
        public const int CustomPrefabCount = LastCustomPrefabIndex - TobaccoPotPrefabIndex + 1;

        public const int WhiteTobaccoPrefabIndex = 310;
        public const int GreenTobaccoPrefabIndex = 312;
        public const int BlackTobaccoPrefabIndex = 314;
        public const int BrownTobaccoPrefabIndex = 316;
        public const int BlueTobaccoPrefabIndex = 318;
    }
}


// RuntimeDiagnostics
namespace TobaccoPotAndCigar.Runtime
{
    using System;
    using UnityEngine;
public static class RuntimeDiagnostics
    {
        public static Action<string> InfoSink;
        public static Action<string> WarningSink;
        public static Action<string> ErrorSink;

        public static void Info(string message)
        {
            if (InfoSink != null)
                InfoSink(message);
            else
                Debug.Log("Tobacco pot and cigar: " + message);
        }

        public static void Warning(string message)
        {
            if (WarningSink != null)
                WarningSink(message);
            else
                Debug.LogWarning("Tobacco pot and cigar: " + message);
        }

        public static void Error(string message)
        {
            if (ErrorSink != null)
                ErrorSink(message);
            else
                Debug.LogError("Tobacco pot and cigar: " + message);
        }

        public static void Reset()
        {
            InfoSink = null;
            WarningSink = null;
            ErrorSink = null;
        }
    }
}


// RuntimeStateSynchronizer
namespace TobaccoPotAndCigar.Runtime
{

public static class RuntimeStateSynchronizer
    {
        public static void Sync(ShipItem item, bool snapCigar)
        {
            if (item == null)
                return;

            TobaccoPlantPotState plant = item.GetComponent<TobaccoPlantPotState>();
            if (plant != null)
                plant.SyncFromSavedState();

            RackOnlyDrying drying = item.GetComponent<RackOnlyDrying>();
            if (drying != null)
                drying.SyncFromSavedState();

            DriedTobaccoLeafState driedLeaf =
                item.GetComponent<DriedTobaccoLeafState>();
            if (driedLeaf != null)
                driedLeaf.SyncFromSavedState();

            CigarWrapperState wrapper = item.GetComponent<CigarWrapperState>();
            if (wrapper != null) wrapper.SyncFromSavedState();

            AshtrayState tray = item.GetComponent<AshtrayState>();
            if (tray != null) tray.SyncFromSavedState();

            CigarRuntimeState cigar = item.GetComponent<CigarRuntimeState>();
            if (cigar != null)
            {
                if (cigar.TryDestroyIfConsumed())
                    return;
                cigar.RefreshValue();
                cigar.SyncVisuals(snapCigar, 0f, false);
            }
        }
    }
}

