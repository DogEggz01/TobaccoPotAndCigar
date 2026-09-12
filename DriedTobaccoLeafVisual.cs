using UnityEngine;

namespace TobaccoPotAndCigar.Runtime
{
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
