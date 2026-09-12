using UnityEngine;

namespace TobaccoPotAndCigar.Runtime
{
    public sealed class CigarVisualController : MonoBehaviour
    {
        private const float BurnVisualLerpSpeed = 12f;
        private const float SpentFrontClearance = 0.008f;
        private const float MinimumPhysicalColliderRatio = 0.05f;
        private const float MinimumPickupLength = 0.04f;
        private const float MaximumEmberLightIntensity = 0.15f;
        private const int IgnoreRaycastLayer = 2;

        private static readonly int ColorProperty = Shader.PropertyToID("_Color");
        private static readonly int EmissionColorProperty =
            Shader.PropertyToID("_EmissionColor");
        private static readonly Color YellowColor =
            new Color(1f, 0.42f, 0.025f, 1f);
        private static readonly Color OrangeColor =
            new Color(0.92f, 0.12f, 0.008f, 1f);
        private static readonly Color YellowEmission =
            new Color(3.2f, 0.85f, 0.04f, 1f);
        private static readonly Color OrangeEmission =
            new Color(2.6f, 0.22f, 0.012f, 1f);
        private static readonly Color YellowLightColor =
            new Color(1f, 0.38f, 0.04f, 1f);
        private static readonly Color OrangeLightColor =
            new Color(1f, 0.12f, 0.015f, 1f);
        private static readonly Color RedLightColor =
            new Color(1f, 0.025f, 0.008f, 1f);

        [SerializeField] private Transform mouthAnchor;
        [SerializeField] private Transform burnableBody;
        [SerializeField] private Transform burnFront;
        [SerializeField] private Transform unlitTip;
        [SerializeField] private Transform ash;
        [SerializeField] private GameObject band;
        [SerializeField] private Renderer emberRenderer;
        [SerializeField] private Light emberLight;
        [SerializeField] private BoxCollider rootCollider;
        [SerializeField] private BoxCollider pickupCollider;
        [SerializeField] private Collider mouthSmokingTrigger;

        private ShipItemPipe pipe;
        private BoxCollider physicalCollider;
        private Vector3 fullBodyScale;
        private Vector3 fullBurnFrontPosition;
        private Vector3 spentBurnFrontPosition;
        private Vector3 fullRootColliderCenter;
        private Vector3 fullRootColliderSize;
        private Vector3 fullPickupColliderCenter;
        private Vector3 fullPickupColliderSize;
        private float rootColliderMouthEndY;
        private float pickupColliderMouthEndY;
        private float burnDirectionSign;
        private float bandThresholdY;
        private float targetRemaining01;
        private float visibleRemaining01;
        private float heat01;
        private float coolingStartHeat01;
        private bool inhaling;
        private bool wasInhaling;
        private bool hasCoolingStartHeat;
        private bool available;
        private bool initialized;
        private MaterialPropertyBlock emberProperties;
        private Color emberBaseColor = Color.white;
        private Color emberBaseEmission = Color.black;
        private float ashBaseline01 = 1f;
        private Vector3 ashOriginalScale;
        private Vector3 ashOriginalPosition;
        private float ashMeshLength;
        private float ashMeshBase;

        private void Awake()
        {
            if (ash != null)
            {
                ashOriginalScale = ash.localScale;
                ashOriginalPosition = ash.localPosition;
                Bounds bounds = ash.GetComponent<MeshFilter>().sharedMesh.bounds;
                ashMeshLength = bounds.size.y * ashOriginalScale.y;
                ashMeshBase = bounds.min.y * ashOriginalScale.y;
            }
            fullBodyScale = burnableBody != null
                ? burnableBody.localScale
                : Vector3.one;
            fullBurnFrontPosition = burnFront != null
                ? burnFront.localPosition
                : Vector3.zero;
            burnDirectionSign = mouthAnchor != null && burnFront != null
                ? Mathf.Sign(burnFront.localPosition.y - mouthAnchor.localPosition.y)
                : 1f;
            if (Mathf.Approximately(burnDirectionSign, 0f))
                burnDirectionSign = 1f;

            spentBurnFrontPosition = fullBurnFrontPosition;
            if (mouthAnchor != null)
            {
                spentBurnFrontPosition.y = mouthAnchor.localPosition.y +
                                           burnDirectionSign * SpentFrontClearance;
            }
            bandThresholdY = band != null
                ? band.transform.localPosition.y
                : spentBurnFrontPosition.y;

            CacheRootColliderGeometry();
            CachePickupColliderGeometry();

            if (emberRenderer != null && emberRenderer.sharedMaterial != null)
            {
                Material material = emberRenderer.sharedMaterial;
                if (material.HasProperty(ColorProperty))
                    emberBaseColor = material.GetColor(ColorProperty);
                if (material.HasProperty(EmissionColorProperty))
                    emberBaseEmission = material.GetColor(EmissionColorProperty);
                emberProperties = new MaterialPropertyBlock();
            }
        }

        public void BindRoot(ShipItemPipe newPipe)
        {
            pipe = newPipe;
            SyncInteractionLayers();
            if (rootCollider == null && pipe != null)
            {
                rootCollider = pipe.GetComponent<BoxCollider>();
                CacheRootColliderGeometry();
            }
            CachePickupColliderGeometry();
        }

        public void SetAshBaseline(float remainingWhenCleared)
        {
            ashBaseline01 = Mathf.Clamp01(remainingWhenCleared);
            if (initialized) ApplyAsh();
        }

        private void ApplyAsh()
        {
            if (ash == null || ashMeshLength <= 0) return;
            float length = Mathf.Max(0, ashBaseline01 - visibleRemaining01) *
                Mathf.Abs(fullBurnFrontPosition.y - spentBurnFrontPosition.y);
            ash.gameObject.SetActive(available && length > .00001f);
            float scale = length / ashMeshLength;
            ash.localScale = new Vector3(ashOriginalScale.x, ashOriginalScale.y * scale, ashOriginalScale.z);
            // Keep the ash's rear edge touching the moving ember while its front grows.
            ash.localPosition = ashOriginalPosition + Vector3.up * ashMeshBase * (1 - scale);
        }

        public void SetRemaining01(
            float remaining,
            bool isAvailable,
            float newHeat01,
            bool isInhaling,
            bool snap)
        {
            targetRemaining01 = Mathf.Clamp01(remaining);
            available = isAvailable;
            heat01 = Mathf.Clamp01(newHeat01);
            UpdateInhaleState(isInhaling, snap);
            if (!available)
                ResetEmberTimeline();

            if (!initialized || snap)
            {
                visibleRemaining01 = targetRemaining01;
                initialized = true;
                ApplyVisualState();
            }
        }

        private void LateUpdate()
        {
            SyncInteractionLayers();
            if (!initialized)
                return;
            TryBindPhysicalCollider();

            float lerp = 1f - Mathf.Exp(-BurnVisualLerpSpeed * Time.deltaTime);
            visibleRemaining01 = Mathf.Lerp(
                visibleRemaining01,
                targetRemaining01,
                lerp);
            if (Mathf.Abs(visibleRemaining01 - targetRemaining01) < 0.0001f)
                visibleRemaining01 = targetRemaining01;
            ApplyVisualState();
        }

        private void UpdateInhaleState(bool newInhaling, bool snap)
        {
            inhaling = newInhaling;
            if (snap)
            {
                wasInhaling = newInhaling;
                hasCoolingStartHeat = false;
                coolingStartHeat01 = 0f;
                return;
            }

            if (newInhaling)
            {
                wasInhaling = true;
                hasCoolingStartHeat = false;
                coolingStartHeat01 = 0f;
                return;
            }

            if (wasInhaling)
            {
                wasInhaling = false;
                coolingStartHeat01 = heat01;
                hasCoolingStartHeat = coolingStartHeat01 > 0f;
            }
        }

        private void ResetEmberTimeline()
        {
            inhaling = false;
            wasInhaling = false;
            coolingStartHeat01 = 0f;
            hasCoolingStartHeat = false;
        }

        private void SyncInteractionLayers()
        {
            // The mouth trigger must participate in mouth collisions but never
            // intercept GoPointer's single ray.
            if (mouthSmokingTrigger != null &&
                mouthSmokingTrigger.gameObject.layer != IgnoreRaycastLayer)
            {
                mouthSmokingTrigger.gameObject.layer = IgnoreRaycastLayer;
            }

            // The pickup trigger now lives on the item root, so it always shares
            // the root's pointer and inventory layer without synchronization.
        }

        private void TryBindPhysicalCollider()
        {
            if (physicalCollider != null || pipe == null || pipe.itemRigidbodyC == null)
                return;
            physicalCollider = pipe.itemRigidbodyC.GetComponent<BoxCollider>();
        }

        private void CacheRootColliderGeometry()
        {
            if (rootCollider == null)
                return;
            fullRootColliderCenter = rootCollider.center;
            fullRootColliderSize = rootCollider.size;
            rootColliderMouthEndY = fullRootColliderCenter.y -
                                     burnDirectionSign *
                                     fullRootColliderSize.y * 0.5f;
        }

        private void CachePickupColliderGeometry()
        {
            if (pickupCollider == null)
                return;
            fullPickupColliderCenter = pickupCollider.center;
            fullPickupColliderSize = pickupCollider.size;
            pickupColliderMouthEndY = fullPickupColliderCenter.y -
                                       burnDirectionSign *
                                       fullPickupColliderSize.y * 0.5f;
        }

        private void ApplyVisualState()
        {
            if (burnableBody != null)
            {
                Vector3 scale = fullBodyScale;
                scale.y = fullBodyScale.y * visibleRemaining01;
                burnableBody.localScale = scale;
            }

            if (burnFront != null)
            {
                burnFront.localPosition = Vector3.Lerp(
                    spentBurnFrontPosition,
                    fullBurnFrontPosition,
                    visibleRemaining01);
                bool burned = targetRemaining01 < .99999f || heat01 > 0f || inhaling;
                burnFront.gameObject.SetActive(available && (unlitTip == null || burned));
                if (unlitTip != null)
                {
                    unlitTip.localPosition = burnFront.localPosition;
                    unlitTip.gameObject.SetActive(available && !burned);
                }
            }

            if (band != null && burnFront != null)
            {
                bool frontBeforeBand = burnDirectionSign > 0f
                    ? burnFront.localPosition.y > bandThresholdY
                    : burnFront.localPosition.y < bandThresholdY;
                band.SetActive(available && frontBeforeBand);
            }

            ApplyPhysicalCollider(rootCollider);
            ApplyPhysicalCollider(physicalCollider);
            ApplyPickupCollider();
            ApplyAsh();
            ApplyEmber();
        }

        private void ApplyPhysicalCollider(BoxCollider collider)
        {
            if (collider == null || fullRootColliderSize.y <= 0f)
                return;
            float colliderRatio = Mathf.Max(
                MinimumPhysicalColliderRatio,
                visibleRemaining01);
            Vector3 size = fullRootColliderSize;
            size.y = fullRootColliderSize.y * colliderRatio;
            Vector3 center = fullRootColliderCenter;
            center.y = rootColliderMouthEndY +
                       burnDirectionSign * size.y * 0.5f;
            collider.size = size;
            collider.center = center;
        }

        private void ApplyPickupCollider()
        {
            if (pickupCollider == null || fullPickupColliderSize.y <= 0f)
                return;
            Vector3 size = fullPickupColliderSize;
            size.y = Mathf.Max(
                MinimumPickupLength,
                fullPickupColliderSize.y * visibleRemaining01);
            Vector3 center = fullPickupColliderCenter;
            center.y = pickupColliderMouthEndY +
                       burnDirectionSign * size.y * 0.5f;
            pickupCollider.size = size;
            pickupCollider.center = center;
        }

        private void ApplyEmber()
        {
            float activeHeat = available ? heat01 : 0f;
            Color surfaceColor;
            Color emissionColor;
            Color lightColor;

            if (activeHeat > 0f && inhaling)
            {
                surfaceColor = YellowColor;
                emissionColor = YellowEmission;
                lightColor = YellowLightColor;
            }
            else
            {
                if (activeHeat > 0f && !hasCoolingStartHeat)
                {
                    coolingStartHeat01 = activeHeat;
                    hasCoolingStartHeat = true;
                }

                CigarEmberCoolingPhase phase = CigarEmberMath.GetCoolingPhase(
                    activeHeat,
                    coolingStartHeat01);
                float progress = CigarEmberMath.GetPhaseProgress(
                    phase,
                    activeHeat,
                    coolingStartHeat01);
                Color darkColor = Color.Lerp(Color.black, emberBaseColor, 0.08f);
                if (phase == CigarEmberCoolingPhase.YellowToOrange)
                {
                    surfaceColor = Color.Lerp(
                        YellowColor,
                        OrangeColor,
                        progress);
                    emissionColor = Color.Lerp(
                        YellowEmission,
                        OrangeEmission,
                        progress);
                    lightColor = Color.Lerp(
                        YellowLightColor,
                        OrangeLightColor,
                        progress);
                }
                else if (phase == CigarEmberCoolingPhase.OrangeToRed)
                {
                    surfaceColor = Color.Lerp(
                        OrangeColor,
                        emberBaseColor,
                        progress);
                    emissionColor = Color.Lerp(
                        OrangeEmission,
                        emberBaseEmission,
                        progress);
                    lightColor = Color.Lerp(
                        OrangeLightColor,
                        RedLightColor,
                        progress);
                }
                else if (phase == CigarEmberCoolingPhase.RedToDark)
                {
                    surfaceColor = Color.Lerp(
                        emberBaseColor,
                        darkColor,
                        progress);
                    emissionColor = Color.Lerp(
                        emberBaseEmission,
                        Color.black,
                        progress);
                    lightColor = Color.Lerp(
                        RedLightColor,
                        Color.black,
                        progress);
                }
                else
                {
                    surfaceColor = darkColor;
                    emissionColor = Color.black;
                    lightColor = RedLightColor;
                }
            }

            if (emberRenderer != null && emberProperties != null)
            {
                emberRenderer.GetPropertyBlock(emberProperties);
                emberProperties.SetColor(ColorProperty, surfaceColor);
                emberProperties.SetColor(
                    EmissionColorProperty,
                    emissionColor);
                emberRenderer.SetPropertyBlock(emberProperties);
            }

            if (emberLight != null)
            {
                emberLight.color = lightColor;
                emberLight.intensity =
                    MaximumEmberLightIntensity * activeHeat;
                emberLight.enabled = activeHeat > 0.001f;
            }
        }
    }
}
