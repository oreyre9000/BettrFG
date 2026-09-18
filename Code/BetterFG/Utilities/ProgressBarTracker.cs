using System.Collections.Generic;
using BettrFG.uGUI;
using UnityEngine;

namespace BetterFG.Utilities
{
    // parallel key/fill lists for a window's per-row download bars (Background3dWindow,
    // MenuMusicWindow). rebuild the window's rows -> Clear() + Add() each; ManagedUpdate -> Tick().
    public class ProgressBarTracker
    {
        private readonly List<string> _keys = new List<string>();
        private readonly List<LoadingBar> _fills = new List<LoadingBar>();

        public void Clear()
        {
            _keys.Clear();
            _fills.Clear();
        }

        public void Add(string key, LoadingBar fill)
        {
            _keys.Add(key);
            _fills.Add(fill);
        }

        public void Tick(Dictionary<string, float> progress)
        {
            for (int i = 0; i < _keys.Count; i++)
            {
                var fill = _fills[i];
                if (fill == null) continue;
                progress.TryGetValue(_keys[i], out float p);
                fill.Set(p);
            }
        }
    }
}
