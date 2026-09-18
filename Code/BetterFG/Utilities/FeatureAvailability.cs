namespace BetterFG.Utilities
{
    public static partial class FeatureAvailability
    {
        static partial void Check(string id, ref bool off);

        public static bool Off(string id)
        {
            bool off = false;
            Check(id, ref off);
            return off;
        }
    }
}
