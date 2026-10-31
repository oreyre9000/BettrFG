using System;
using System.Collections.Generic;
using UnityEngine;

namespace BetterFG.Nametag
{
    internal static class FlagAssets
    {
        private const string prefix = "BetterFG.assets.flags.";
        private static string[] _codes;

        public static string[] GetAvailableCodes()
        {
            if (_codes != null) return _codes;

            var result = new List<string>();
            foreach (string name in BetterFG.Utilities.Atlas.Keys)
            {
                if (!name.StartsWith(prefix, StringComparison.Ordinal)) continue;
                // BetterFG.assets.flags.US.png -> "US"
                string code = name.Substring(prefix.Length, name.Length - prefix.Length - 4);
                if (code.Length == 2) result.Add(code);
            }

            result.Sort(StringComparer.Ordinal);
            _codes = result.ToArray();
            return _codes;
        }

        public static Sprite LoadFlag(string code)
            => BetterFG.Utilities.EmbeddedResourceandUnity.LoadSprite(prefix + code.ToUpper() + ".png");
    }
}
