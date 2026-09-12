using UnityEngine;

namespace TobaccoPotAndCigar.Runtime
{
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
