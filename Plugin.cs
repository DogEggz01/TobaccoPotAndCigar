// Others — grouped mod source. Existing type identities are preserved.

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
        public const string PluginVersion = "1.1.3";

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

    [HarmonyPatch(typeof(ShipItem), "OnPickup")]
    internal static class CigarLeaveRestPatch
    {
        [HarmonyPrefix]
        private static void Prefix(ShipItem __instance)
        {
            CigarRuntimeState cigar = __instance.GetComponent<CigarRuntimeState>();
            if (cigar != null) cigar.LeaveRest();
            RestingPipeState pipe = __instance.GetComponent<RestingPipeState>();
            if (pipe != null) pipe.LeaveRest();
            AshtrayState tray = __instance.GetComponent<AshtrayState>();
            if (tray != null) tray.PreparePickup();
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
            if (pointer.GetHeldItem() != null) return true;
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
            return (cigar == null || !cigar.IsResting) && (pipe == null || !pipe.IsResting);
        }
    }

    [HarmonyPatch(typeof(ShipItem), "OnEnterInventory")]
    internal static class AshtrayInventoryPatch
    {
        [HarmonyPrefix]
        private static void Prefix(ShipItem __instance)
        {
            AshtrayState tray = __instance.GetComponent<AshtrayState>();
            if (tray != null) tray.ReleaseOccupant();
            RestingPipeState pipe = __instance.GetComponent<RestingPipeState>();
            if (pipe != null) pipe.LeaveRest();
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
                    "PrefabsDirectory ship-item cache was not populated for indices 610-624.");
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
                "Registered bundled prefabs 610-624 and rack-only tobacco drying.");
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
                                : prefab.GetComponent<AshtrayState>() != null;
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
        public const int LastCustomPrefabIndex = GlassAshtrayPrefabIndex;
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
