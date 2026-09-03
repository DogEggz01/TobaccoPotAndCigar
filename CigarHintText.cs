namespace TobaccoPotAndCigar.Runtime
{
    public static class CigarHintText
    {
        public const string GreenHex = "3FAF45";
        public const string BrownHex = "8B5A2B";
        public const string BlueHex = "3F7FFF";
        public const string BlackHex = "202020";

        public static string Build(TobaccoRecipe recipe)
        {
            if (recipe.Count <= 0)
                return "cigar";

            if (recipe.IsFullSingleType)
                return "Full " + GetName(recipe.GetAt(0)) + " Cigar";

            string code = "";
            for (int i = 0; i < recipe.Count; i++)
                code += GetColoredLetter(recipe.GetAt(i));
            return code + " Cigar";
        }

        private static string GetName(CigarTobaccoType tobaccoType)
        {
            switch (tobaccoType)
            {
                case CigarTobaccoType.White:
                    return "White";
                case CigarTobaccoType.Green:
                    return "Green";
                case CigarTobaccoType.Black:
                    return "Black";
                case CigarTobaccoType.Brown:
                    return "Brown";
                case CigarTobaccoType.Blue:
                    return "Blue";
                default:
                    return "Unknown";
            }
        }

        private static string GetColoredLetter(CigarTobaccoType tobaccoType)
        {
            switch (tobaccoType)
            {
                case CigarTobaccoType.White:
                    return "W";
                case CigarTobaccoType.Green:
                    return Color("G", GreenHex);
                case CigarTobaccoType.Brown:
                    return Color("B", BrownHex);
                case CigarTobaccoType.Blue:
                    return Color("B", BlueHex);
                case CigarTobaccoType.Black:
                    return Color("B", BlackHex);
                default:
                    return "?";
            }
        }

        private static string Color(string letter, string hex)
        {
            return "<color=#" + hex + ">" + letter + "</color>";
        }
    }
}
