using System.Collections.Generic;
using FG.Common.CMS;

namespace BetterFG.Features.Assets
{
    public static class SceneExport
    {
        public static string Prefix => "scene:";

        public static bool IsScene(string key) => key.StartsWith(Prefix);

        public static string NameOf(string key) => key.Substring(Prefix.Length);

        private static readonly List<string> _scenes = new List<string>();
        private static readonly Dictionary<string, string> _roundLabels = new Dictionary<string, string>();
        private static readonly Dictionary<string, string> _roundSplashes = new Dictionary<string, string>();
        private static bool _roundsRead;

        private static void ReadRounds()
        {
            _roundsRead = true;
            var so = CMSLoader.Instance._roundsSO;
            var ids = so.RoundIDLookup;
            for (int i = 0; i < ids.Count; i++)
            {
                string id = ids[i];
                if (string.IsNullOrEmpty(id)) continue;

                Round round = null;
                so.Rounds.TryGetValue(id, out round);
                if (round == null || round.IsUGC()) continue;

                var data = round.SceneData;
                if (data == null || data.PrimeLevel == null) continue;
                string scene = data.PrimeLevel.SceneName;
                if (string.IsNullOrEmpty(scene)) continue;

                string shown = round.DisplayNameUnindented;
                if (!_roundLabels.ContainsKey(scene)) _scenes.Add(scene);
                _roundLabels[scene] = string.IsNullOrEmpty(shown) ? id : shown + "   " + id;
                if (!string.IsNullOrEmpty(round.LoadingScreenName))
                    _roundSplashes[scene] = round.LoadingScreenName;
            }
            _scenes.Sort((a, b) => string.Compare(_roundLabels[a], _roundLabels[b], System.StringComparison.OrdinalIgnoreCase));
            Plugin.Log.LogInfo($"{_scenes.Count} unity round scenes in the CMS round table");
        }

        public static List<string> Scenes()
        {
            if (!_roundsRead) ReadRounds();
            return _scenes;
        }

        public static string RoundLabel(string sceneName)
        {
            if (!_roundsRead) ReadRounds();
            string got;
            return _roundLabels.TryGetValue(sceneName, out got) ? got : null;
        }

        public static string SplashKey(string sceneName)
        {
            if (!_roundsRead) ReadRounds();
            string got;
            return _roundSplashes.TryGetValue(sceneName, out got) ? got : null;
        }
    }
}
