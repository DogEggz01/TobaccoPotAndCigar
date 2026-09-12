using UnityEngine;

namespace TobaccoPotAndCigar.Runtime
{
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
