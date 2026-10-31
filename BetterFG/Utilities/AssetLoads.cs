using System;
using System.Collections.Generic;
using UnityEngine.AddressableAssets;

namespace BetterFG.Utilities
{
    public static class AssetLoads
    {
        private static readonly Dictionary<string, UnityEngine.Object> _ready = new Dictionary<string, UnityEngine.Object>();

        public static T Get<T>(string key) where T : UnityEngine.Object
        {
            string id = typeof(T).Name + ":" + key;
            if (_ready.TryGetValue(id, out var hit)) return hit as T;

            T asset = null;
            try { asset = Addressables.LoadAssetAsync<T>(key).WaitForCompletion(); }
            catch (Exception ex) { Plugin.Log.LogWarning($"{typeof(T).Name} {key} wouldn't load: {ex.Message}"); }
            if (asset == null) Plugin.Log.LogWarning($"nothing came back for {typeof(T).Name} {key}");
            _ready[id] = asset;
            return asset;
        }
    }
}
