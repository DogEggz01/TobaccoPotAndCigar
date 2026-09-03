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
