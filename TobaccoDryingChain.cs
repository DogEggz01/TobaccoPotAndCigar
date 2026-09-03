namespace TobaccoPotAndCigar.Runtime
{
    public static class TobaccoDryingChain
    {
        public static bool TryGetNextStage(int sourcePrefabIndex, out int resultPrefabIndex)
        {
            switch (sourcePrefabIndex)
            {
                case RuntimeConstants.FreshTobaccoLeafPrefabIndex:
                    resultPrefabIndex = RuntimeConstants.DriedTobaccoLeafPrefabIndex;
                    return true;
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
