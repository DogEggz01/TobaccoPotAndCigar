using UnityEngine;

namespace TobaccoPotAndCigar.Runtime
{
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
