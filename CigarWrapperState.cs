using UnityEngine;

namespace TobaccoPotAndCigar.Runtime
{
    public sealed class CigarWrapperState : MonoBehaviour
    {
        [SerializeField] private CigarWrapperVisual wrapperVisual;
        private ShipItem item;
        private bool rolling;
        private bool invalidLogged;
        public int FillCount { get { TobaccoRecipe recipe; return TryGetRecipe(out recipe) ? recipe.Count : -1; } }

        private void Awake()
        {
            item = GetComponent<ShipItem>();
            if (wrapperVisual == null) wrapperVisual = GetComponent<CigarWrapperVisual>();
        }
        private void Start() { SyncFromSavedState(); }
        public void BindVisual(CigarWrapperVisual visual) { wrapperVisual = visual; }

        public bool TryGetRecipe(out TobaccoRecipe recipe)
        {
            if (item == null) item = GetComponent<ShipItem>();
            recipe = default(TobaccoRecipe);
            return item != null && LeafStateRules.TryReadWrapper(item.health, item.amount, out recipe);
        }

        public bool CanInsert(ShipItemTobacco tobacco)
        {
            TobaccoRecipe recipe;
            if(item==null) item=GetComponent<ShipItem>();
            return !rolling && gameObject.activeInHierarchy && item != null && item.sold &&
                tobacco != null && tobacco.sold && tobacco.gameObject.activeInHierarchy &&
                PrefabReplacement.CanTransform(item) && PrefabReplacement.CanTransform(tobacco) &&
                TryGetRecipe(out recipe) && recipe.Count < 3 && recipe.TryAdd(tobacco);
        }

        public bool TryInsert(ShipItemTobacco tobacco)
        {
            if (!CanInsert(tobacco)) return false;
            TobaccoRecipe recipe;
            if (!TryGetRecipe(out recipe) || !recipe.TryAdd(tobacco)) return false;
            if(wrapperVisual==null || wrapperVisual.gameObject!=gameObject) wrapperVisual=GetComponent<CigarWrapperVisual>();
            // Presentation must be usable before committing the recipe or consuming tobacco.
            if(wrapperVisual==null)
            {
                if(!invalidLogged) RuntimeDiagnostics.Error("Cigar Wrapper "+GetInstanceID()+" has no visual component; tobacco was not consumed.");
                invalidLogged=true;return false;
            }
            if(!wrapperVisual.TrySetRecipe(recipe)) return false;
            item.health = recipe.Count;
            item.amount = TobaccoRecipeCodec.Encode(recipe);
            PrefabReplacement.ConsumeOwned(tobacco);
            SyncFromSavedState();
            if (UISoundPlayer.instance != null) UISoundPlayer.instance.PlayUISound(UISounds.itemInventoryIn, .5f, .5f);
            return true;
        }

        public bool TryRoll()
        {
            TobaccoRecipe recipe;
            if (rolling || !gameObject.activeInHierarchy || !TryGetRecipe(out recipe) || recipe.Count == 0) return false;
            rolling = true;
            bool replaced = PrefabReplacement.ReplaceOwnedItem(item, RuntimeConstants.CigarPrefabIndex,
                recipe.Count * 100f, -TobaccoRecipeCodec.Encode(recipe), Vector3.up * .10f,
                item.transform.rotation * Quaternion.Euler(0, 0, -90));
            if (!replaced) rolling = false;
            return replaced;
        }

        public void SyncFromSavedState()
        {
            if (item == null) item = GetComponent<ShipItem>();
            if (item == null) return;
            item.name = "Cigar Wrapper";
            TobaccoRecipe recipe;
            if (!TryGetRecipe(out recipe))
            {
                item.description = string.Empty;
                if (!invalidLogged) RuntimeDiagnostics.Error("Cigar Wrapper instance " + GetInstanceID() +
                    " has invalid count/recipe data; original values preserved and crafting disabled.");
                invalidLogged = true;
                return;
            }
            invalidLogged = false;
            if (wrapperVisual == null || wrapperVisual.gameObject != gameObject) wrapperVisual = GetComponent<CigarWrapperVisual>();
            if (wrapperVisual != null) wrapperVisual.SetRecipe(recipe);
            item.description = "Tobacco: " + recipe.Count + " / 3";
        }
    }
}
