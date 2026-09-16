// TobaccoFermentAndLeaf — grouped mod source. Existing type identities are preserved.

// KnifePatches
namespace TobaccoPotAndCigar.Patches
{
    using HarmonyLib;
    using System.Collections.Generic;
    using TobaccoPotAndCigar.Runtime;
    using UnityEngine;
[HarmonyPatch(typeof(ShipItemKnife), "AllowOnItemClick")]
    internal static class TobaccoLeafKnifeTargetPatch
    {
        [HarmonyPostfix]
        private static void Postfix(
            ShipItemKnife __instance,
            GoPointerButton lookedAtButton,
            ref bool __result)
        {
            FreshTobaccoLeafState leaf = lookedAtButton != null ? lookedAtButton.GetComponent<FreshTobaccoLeafState>() : null;
            if (leaf != null && leaf.IsFullyDry) { __result = false; return; }
            if (!__result && __instance.sold && lookedAtButton != null)
            {
                ShipItem target = lookedAtButton.GetComponent<ShipItem>();
                __result = target != null && target.sold &&
                           target.GetComponent<FreshTobaccoLeafState>() != null;
            }
        }
    }

    [HarmonyPatch(typeof(ShipItemKnife), "OnAltActivate")]
    internal static class TobaccoLeafKnifePatch
    {
        private const int SliceCount = 3;

        [HarmonyPostfix]
        private static void Postfix(
            ShipItemKnife __instance,
            ref bool ___animating,
            ref float ___heldRotationOffset,
            ref float ___cutTimer)
        {
            if (!__instance.sold || __instance.held == null)
                return;

            ShipItem target = __instance.held.GetPointedAtItem();
            if (target == null || !target.sold ||
                target.GetComponent<FreshTobaccoLeafState>() == null ||
                target.GetComponent<FreshTobaccoLeafState>().IsFullyDry ||
                !PrefabReplacement.CanTransform(target) ||
                !PrefabReplacement.CanSpawn(RuntimeConstants.GreenTobaccoPrefabIndex))
            {
                return;
            }

            ___animating = true;
            ___heldRotationOffset = 0f;
            ___cutTimer = 0.25f;

            List<ShipItem> spawnedTobacco = new List<ShipItem>(SliceCount);
            float localOffset = -0.01f * SliceCount;
            for (int i = 0; i < SliceCount; i++)
            {
                ShipItem tobacco = PrefabReplacement.SpawnOwned(
                    RuntimeConstants.GreenTobaccoPrefabIndex,
                    target.transform.position + target.transform.right * localOffset,
                    target.transform.rotation * Quaternion.Euler(0f, 90f, 0f),
                    target,
                    0f,
                    0f);
                if (tobacco != null)
                    spawnedTobacco.Add(tobacco);
                localOffset += 0.02f;
            }

            if (spawnedTobacco.Count == SliceCount)
                PrefabReplacement.ConsumeOwned(target);
            else
            {
                for (int i = 0; i < spawnedTobacco.Count; i++)
                    spawnedTobacco[i].DestroyItem();
                RuntimeDiagnostics.Error("Knife cut could not create all three green " +
                                         "tobaccos; partial output was rolled back and " +
                                         "the leaf was preserved.");
            }
        }
    }
}


// LeafCurlMesh
namespace TobaccoPotAndCigar.Runtime
{
    using System;
    using UnityEngine;
/// <summary>Imported vertex correspondence is baked by the authoring builder.</summary>
    [Serializable]
    public sealed class LeafCurlMesh
    {
        [SerializeField] private MeshFilter filter;
        [SerializeField] private Vector3[] driedVertices;
        [NonSerialized] private Mesh source;
        [NonSerialized] private Mesh instance;
        [NonSerialized] private Vector3[] freshVertices;
        [NonSerialized] private Vector3[] workingVertices;

        public LeafCurlMesh(MeshFilter newFilter, Vector3[] target)
        {
            filter = newFilter;
            driedVertices = target;
        }

        public void Apply(float progress)
        {
            if (filter == null || filter.sharedMesh == null)
                return;
            if (instance == null)
            {
                source = filter.sharedMesh;
                if (driedVertices == null || driedVertices.Length != source.vertexCount)
                    throw new InvalidOperationException("Leaf curl target does not match its source mesh.");
                freshVertices = source.vertices;
                workingVertices = new Vector3[freshVertices.Length];
                instance = UnityEngine.Object.Instantiate(source);
                instance.name = source.name + " (leaf instance)";
                instance.MarkDynamic();
                filter.sharedMesh = instance;
            }
            // A completed legacy conversion can replace the root mesh before cleanup.
            if (filter.sharedMesh != instance)
                return;
            for (int i = 0; i < workingVertices.Length; i++)
                workingVertices[i] = Vector3.LerpUnclamped(freshVertices[i], driedVertices[i], progress);
            instance.vertices = workingVertices;
            instance.RecalculateNormals();
            instance.RecalculateTangents();
            instance.RecalculateBounds();
        }

        public void Release()
        {
            if (instance == null)
                return;
            if (filter != null && filter.sharedMesh == instance)
                filter.sharedMesh = source;
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(instance);
            else
                UnityEngine.Object.DestroyImmediate(instance);
            instance = null;
            freshVertices = null;
            workingVertices = null;
        }
    }
}


// LeafStateRules
namespace TobaccoPotAndCigar.Runtime
{
    using System;
public static class LeafStateRules
    {
        public const float DryingHours = 144f;
        public const int HarvestCount = 3;

        public static bool IsDry(float hours)
        {
            return !float.IsNaN(hours) && !float.IsInfinity(hours) && hours >= DryingHours;
        }

        public static bool TryReadWrapper(float count, float code, out TobaccoRecipe recipe)
        {
            recipe = default(TobaccoRecipe);
            if (float.IsNaN(count) || float.IsInfinity(count) || count < 0 || count > 3 ||
                count != Math.Floor(count) || float.IsNaN(code) || float.IsInfinity(code) ||
                code < 0 || code > int.MaxValue || code != Math.Floor(code))
                return false;
            if (count == 0) return code == 0;
            bool legacy;
            return TobaccoRecipeCodec.TryDecode((int)code, out recipe, out legacy) && recipe.Count == (int)count;
        }

        public static bool CanCombine(float targetHours, float heldHours, bool distinct,
            bool targetOwned, bool heldOwned)
        {
            return distinct && targetOwned && heldOwned && IsDry(targetHours) && IsDry(heldHours);
        }
    }
}


// LegacyLeafMigration
namespace TobaccoPotAndCigar.Runtime
{

/// <summary>Transforms loaded records before native spawning/caching; never writes save files.</summary>
    public static class LegacyLeafMigration
    {
        public static bool TryMigrate(SavePrefabData data)
        {
            if (data == null || data.prefabIndex != RuntimeConstants.DriedTobaccoLeafPrefabIndex || !data.isSold)
                return false;
            TobaccoRecipe recipe;
            if (!LeafStateRules.TryReadWrapper(data.itemHealth, data.itemAmount, out recipe))
            {
                RuntimeDiagnostics.Error("Legacy leaf instance " + data.instanceId +
                    " has conflicting count/recipe data (health=" + data.itemHealth +
                    ", amount=" + data.itemAmount + "). Original ID 613 and values preserved; repair the save record before crafting.");
                return false;
            }
            if (recipe.Count == 0)
            {
                data.prefabIndex = RuntimeConstants.FreshTobaccoLeafPrefabIndex;
                data.itemHealth = LeafStateRules.DryingHours;
            }
            else
            {
                data.prefabIndex = RuntimeConstants.CigarWrapperPrefabIndex;
                // Legacy leaves use Z-up geometry; wrappers use Y-up. Preserve the
                // visible resting plane while native loading restores the same owner/position.
                data.rotation = (UnityEngine.Quaternion)data.rotation * new UnityEngine.Quaternion(.70710678f, 0, 0, .70710678f);
                // Preserve the precise old recipe encoding, count and all ownership fields.
            }
            return true;
        }

        public static SaveContainer MigrateLoadedContainer(SaveContainer save)
        {
            if (save == null || save.savedPrefabs == null) return save;
            int migrated = 0;
            foreach (SavePrefabData data in save.savedPrefabs)
                if (TryMigrate(data)) migrated++;
            if (migrated > 0)
                RuntimeDiagnostics.Info("Migrated " + migrated + " legacy leaf records for 1.0.2 before native object loading.");
            return save;
        }
    }
}


// TobaccoDryingChain
namespace TobaccoPotAndCigar.Runtime
{

public static class TobaccoDryingChain
    {
        public static bool TryGetNextStage(int sourcePrefabIndex, out int resultPrefabIndex)
        {
            switch (sourcePrefabIndex)
            {
                case RuntimeConstants.GreenTobaccoPrefabIndex:
                    resultPrefabIndex = RuntimeConstants.WhiteTobaccoPrefabIndex;
                    return true;
                case RuntimeConstants.WhiteTobaccoPrefabIndex:
                    resultPrefabIndex = RuntimeConstants.BrownTobaccoPrefabIndex;
                    return true;
                case RuntimeConstants.BrownTobaccoPrefabIndex:
                    resultPrefabIndex = RuntimeConstants.BlackTobaccoPrefabIndex;
                    return true;
                default:
                    resultPrefabIndex = 0;
                    return false;
            }
        }

        public static bool IsStageTransition(int sourcePrefabIndex, int resultPrefabIndex)
        {
            int expectedResult;
            return TryGetNextStage(sourcePrefabIndex, out expectedResult) &&
                   expectedResult == resultPrefabIndex;
        }
    }
}


// DriedTobaccoLeafState
namespace TobaccoPotAndCigar.Runtime
{
    using UnityEngine;
/// <summary>ID 613 recovery asset. Valid saved records migrate before native loading.</summary>
    public sealed class DriedTobaccoLeafState : MonoBehaviour
    {
        [SerializeField] private DriedTobaccoLeafVisual leafVisual;
        private ShipItem item;
        private bool logged;
        public void BindVisual(DriedTobaccoLeafVisual visual) { leafVisual = visual; }
        public int FillCount
        {
            get
            {
                TobaccoRecipe recipe;
                return item != null && LeafStateRules.TryReadWrapper(item.health, item.amount, out recipe) ? recipe.Count : -1;
            }
        }
        private void Awake() { item = GetComponent<ShipItem>(); }
        private void Start() { SyncFromSavedState(); }
        public void SyncFromSavedState()
        {
            if (item == null) item = GetComponent<ShipItem>();
            if (item == null) return;
            TobaccoRecipe recipe;
            bool valid = LeafStateRules.TryReadWrapper(item.health, item.amount, out recipe);
            if (leafVisual != null) leafVisual.SetFillCount(valid ? recipe.Count : 0);
            item.description = string.Empty;
            if (!valid && !logged)
            {
                RuntimeDiagnostics.Error("Legacy leaf instance " + GetInstanceID() + " has invalid count/recipe data; preserved without normalization.");
                logged = true;
            }
        }
    }
}


// DriedTobaccoLeafVisual
namespace TobaccoPotAndCigar.Runtime
{
    using UnityEngine;
public sealed class DriedTobaccoLeafVisual : MonoBehaviour
    {
        [SerializeField] private GameObject fill0Empty;
        [SerializeField] private GameObject fill1OneThird;
        [SerializeField] private GameObject fill2TwoThirds;
        [SerializeField] private GameObject fill3Full;

        public void Configure(
            GameObject newFill0Empty,
            GameObject newFill1OneThird,
            GameObject newFill2TwoThirds,
            GameObject newFill3Full)
        {
            fill0Empty = newFill0Empty;
            fill1OneThird = newFill1OneThird;
            fill2TwoThirds = newFill2TwoThirds;
            fill3Full = newFill3Full;
        }

        public void SetFillCount(int fillCount)
        {
            fillCount = Mathf.Clamp(fillCount, 0, 3);
            if (fill0Empty != null)
                fill0Empty.SetActive(fillCount == 0);
            if (fill1OneThird != null)
                fill1OneThird.SetActive(fillCount == 1);
            if (fill2TwoThirds != null)
                fill2TwoThirds.SetActive(fillCount == 2);
            if (fill3Full != null)
                fill3Full.SetActive(fillCount == 3);
        }
    }
}


// FreshTobaccoLeafState
namespace TobaccoPotAndCigar.Runtime
{
    using UnityEngine;
public sealed class FreshTobaccoLeafState : MonoBehaviour
    {
        [SerializeField] private FreshTobaccoLeafVisual leafVisual;

        private ShipItem item;
        private bool crafting;

        public bool IsFullyDry { get { return item != null && LeafStateRules.IsDry(item.health); } }

        public bool CanCombineWith(FreshTobaccoLeafState heldLeaf)
        {
            return !crafting && heldLeaf != null && !heldLeaf.crafting && item != null && heldLeaf.item != null &&
                LeafStateRules.CanCombine(item.health, heldLeaf.item.health, heldLeaf != this, item.sold, heldLeaf.item.sold) &&
                PrefabReplacement.CanTransform(item) && PrefabReplacement.CanTransform(heldLeaf.item);
        }

        public bool TryCombineWith(FreshTobaccoLeafState heldLeaf)
        {
            if (!CanCombineWith(heldLeaf)) return false;
            crafting = heldLeaf.crafting = true;
            try
            {
                return PrefabReplacement.CombineOwnedItems(item, heldLeaf.item,
                    RuntimeConstants.CigarWrapperPrefabIndex, Vector3.up * .035f,
                    Quaternion.FromToRotation((item.transform.rotation * Quaternion.Euler(90, 0, 0)) * Vector3.up,
                        Vector3.up) * item.transform.rotation * Quaternion.Euler(90, 0, 0));
            }
            finally
            {
                // Consumed sources are disabled immediately, before deferred Unity destruction.
                if (this != null && gameObject.activeSelf) crafting = false;
                if (heldLeaf != null && heldLeaf.gameObject.activeSelf) heldLeaf.crafting = false;
            }
        }

        private void Awake()
        {
            item = GetComponent<ShipItem>();
            if (leafVisual == null)
                leafVisual = GetComponentInChildren<FreshTobaccoLeafVisual>(true);
        }

        private void Start()
        {
            SyncDryingVisual();
        }

        public void BindVisual(FreshTobaccoLeafVisual newLeafVisual)
        {
            leafVisual = newLeafVisual;
        }

        public void SyncDryingVisual()
        {
            if (item == null)
                item = GetComponent<ShipItem>();
            if (item == null) return;
            if (float.IsNaN(item.health) || float.IsInfinity(item.health)) item.health = 0;
            item.health = Mathf.Clamp(item.health, 0, LeafStateRules.DryingHours);
            bool dry = IsFullyDry;
            item.name = dry ? "dried tobacco leaf" : "tobacco leaf";
            item.value = dry ? 200 : 500;
            float mass = dry ? .10f : .15f;
            bool massChanged = !Mathf.Approximately(item.mass, mass);
            item.mass = mass;
            item.description = string.Empty;
            if (leafVisual != null) leafVisual.SetDryingHours(item.health);
            if (massChanged && item.itemRigidbodyC != null && item.itemRigidbodyC.GetBody() != null)
                item.itemRigidbodyC.UpdateMass();
        }

    }
}


// FreshTobaccoLeafVisual
namespace TobaccoPotAndCigar.Runtime
{
    using UnityEngine;
public sealed class FreshTobaccoLeafVisual : MonoBehaviour
    {
        public const float GameHoursPerDay = 24f;
        public const float WhiteColorGameHours = 72f;
        public const float FullyDriedGameHours = 144f;
        public const int VeinsHiddenPerDay = 2;

        private static readonly int ColorProperty = Shader.PropertyToID("_LeafTint");

        [SerializeField] private Renderer bladeRenderer;
        [SerializeField] private GameObject[] lateralVeins;
        [SerializeField] private GameObject centralMidrib;
        [SerializeField] private Renderer[] lateralVeinRenderers;
        [SerializeField] private Renderer centralMidribRenderer;
        [SerializeField] private Color freshColor = Color.white;
        [SerializeField] private Color whiteTobaccoColor = Color.white;
        [SerializeField] private Color driedLeafColor = Color.white;
        [SerializeField] private LeafCurlMesh[] curlMeshes;

        private MaterialPropertyBlock colorProperties;
        [SerializeField] private float[] lateralVeinBrightness;
        [SerializeField] private float centralMidribBrightness = 1f;
        private float displayedHours = float.NaN;

        public int CurlMeshCount { get { return curlMeshes != null ? curlMeshes.Length : 0; } }

        public void ConfigureCurl(LeafCurlMesh[] meshes)
        {
            ReleaseCurlMeshes();
            curlMeshes = meshes;
            displayedHours = float.NaN;
        }

        public static float EvaluateCurlProgress(float hours)
        {
            if (float.IsNaN(hours)) return 0f;
            float t = Mathf.Clamp01(hours / FullyDriedGameHours);
            return t * t * (3f - 2f * t);
        }

        private void OnDestroy()
        {
            ReleaseCurlMeshes();
        }

        // Also used by editor preview tools, whose non-ExecuteInEditMode components
        // do not receive Unity's runtime destruction callbacks.
        public void ReleaseCurlMeshes()
        {
            displayedHours = float.NaN;
            if (curlMeshes == null) return;
            foreach (LeafCurlMesh mesh in curlMeshes)
                if (mesh != null) mesh.Release();
        }

        public int LateralVeinCount
        {
            get { return lateralVeins != null ? lateralVeins.Length : 0; }
        }

        private void Awake()
        {
            CacheDetailRenderersAndBrightness(false);
        }

        public void Configure(
            Renderer newBladeRenderer,
            GameObject[] newLateralVeins,
            GameObject newCentralMidrib,
            Color newFreshColor,
            Color newWhiteTobaccoColor,
            Color newDriedLeafColor)
        {
            bladeRenderer = newBladeRenderer;
            lateralVeins = newLateralVeins;
            centralMidrib = newCentralMidrib;
            freshColor = newFreshColor;
            whiteTobaccoColor = newWhiteTobaccoColor;
            driedLeafColor = newDriedLeafColor;
            CacheDetailRenderersAndBrightness(true);
            displayedHours = float.NaN;
        }

        public void SetDryingHours(float gameHours)
        {
            if (float.IsNaN(gameHours)) gameHours = 0f;
            float clampedHours = Mathf.Clamp(
                gameHours,
                0f,
                FullyDriedGameHours);
            if (!float.IsNaN(displayedHours) &&
                Mathf.Abs(displayedHours - clampedHours) < 0.0001f)
            {
                return;
            }

            displayedHours = clampedHours;
            float curlProgress = EvaluateCurlProgress(clampedHours);
            if (curlMeshes != null)
                foreach (LeafCurlMesh mesh in curlMeshes)
                    if (mesh != null) mesh.Apply(curlProgress);
            ApplyColor(EvaluateDryingColor(
                clampedHours,
                freshColor,
                whiteTobaccoColor,
                driedLeafColor));

            int hiddenVeins = GetHiddenVeinCount(
                clampedHours,
                LateralVeinCount);
            for (int i = 0; i < LateralVeinCount; i++)
            {
                if (lateralVeins[i] != null)
                    lateralVeins[i].SetActive(i >= hiddenVeins);
            }
            if (centralMidrib != null)
                centralMidrib.SetActive(true);
        }

        public static Color EvaluateDryingColor(
            float gameHours,
            Color fresh,
            Color white,
            Color dried)
        {
            float clampedHours = Mathf.Clamp(
                gameHours,
                0f,
                FullyDriedGameHours);
            if (clampedHours <= WhiteColorGameHours)
            {
                return Color.Lerp(
                    fresh,
                    white,
                    clampedHours / WhiteColorGameHours);
            }

            return Color.Lerp(
                white,
                dried,
                (clampedHours - WhiteColorGameHours) /
                (FullyDriedGameHours - WhiteColorGameHours));
        }

        public static int GetHiddenVeinCount(float gameHours, int veinCount)
        {
            int completedDays = Mathf.FloorToInt(
                Mathf.Max(0f, gameHours) / GameHoursPerDay);
            return Mathf.Clamp(
                completedDays * VeinsHiddenPerDay,
                0,
                Mathf.Max(0, veinCount));
        }

        private void ApplyColor(Color color)
        {
            if (colorProperties == null)
                colorProperties = new MaterialPropertyBlock();
            ApplyRendererColor(bladeRenderer, color);

            if (lateralVeinRenderers != null)
            {
                for (int i = 0; i < lateralVeinRenderers.Length; i++)
                {
                    float brightness = lateralVeinBrightness != null &&
                                       i < lateralVeinBrightness.Length
                        ? lateralVeinBrightness[i]
                        : 1f;
                    ApplyRendererColor(
                        lateralVeinRenderers[i],
                        ApplyBrightness(color, brightness));
                }
            }

            ApplyRendererColor(
                centralMidribRenderer,
                ApplyBrightness(color, centralMidribBrightness));
        }

        public static Color ApplyBrightness(Color color, float brightness)
        {
            return new Color(
                Mathf.Clamp01(color.r * brightness),
                Mathf.Clamp01(color.g * brightness),
                Mathf.Clamp01(color.b * brightness),
                color.a);
        }

        private void ApplyRendererColor(Renderer target, Color color)
        {
            if (target == null)
                return;
            colorProperties.Clear();
            target.GetPropertyBlock(colorProperties);
            colorProperties.SetColor(ColorProperty, color);
            colorProperties.SetFloat("_Drying", Mathf.Clamp01(displayedHours / FullyDriedGameHours));
            target.SetPropertyBlock(colorProperties);
        }

        private void CacheDetailRenderersAndBrightness(bool refreshBrightness)
        {
            int count = LateralVeinCount;
            if (lateralVeinRenderers == null ||
                lateralVeinRenderers.Length != count)
            {
                lateralVeinRenderers = new Renderer[count];
            }
            bool createBrightness = lateralVeinBrightness == null ||
                                    lateralVeinBrightness.Length != count;
            if (createBrightness)
                lateralVeinBrightness = new float[count];
            for (int i = 0; i < count; i++)
            {
                if (lateralVeinRenderers[i] == null && lateralVeins[i] != null)
                {
                    lateralVeinRenderers[i] =
                        lateralVeins[i].GetComponentInChildren<Renderer>(true);
                }
                if (refreshBrightness || createBrightness)
                {
                    lateralVeinBrightness[i] = GetBrightnessRatio(
                        lateralVeinRenderers[i]);
                }
            }

            if (centralMidribRenderer == null && centralMidrib != null)
            {
                centralMidribRenderer =
                    centralMidrib.GetComponentInChildren<Renderer>(true);
            }
            if (refreshBrightness || createBrightness)
            {
                centralMidribBrightness = GetBrightnessRatio(
                    centralMidribRenderer);
            }
        }

        private float GetBrightnessRatio(Renderer target)
        {
            if (target == null || target.sharedMaterial == null ||
                !target.sharedMaterial.HasProperty(ColorProperty))
            {
                return 1f;
            }

            Color detailColor = target.sharedMaterial.GetColor(ColorProperty);
            float leafLuminance = GetLuminance(freshColor);
            return leafLuminance > 0.001f
                ? Mathf.Clamp(GetLuminance(detailColor) / leafLuminance, 0f, 4f)
                : 1f;
        }

        private static float GetLuminance(Color color)
        {
            return color.r * 0.2126f + color.g * 0.7152f + color.b * 0.0722f;
        }
    }
}


// RackOnlyDrying
namespace TobaccoPotAndCigar.Runtime
{
    using System.Collections;
    using System.Collections.Generic;
    using UnityEngine;
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
