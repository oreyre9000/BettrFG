using System;
using System.Collections;
using System.Threading;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Utilities;
using FG.Common;
using FGClient;
using TMPro;
using UnityEngine;

namespace BetterFG.Tweaks
{
    public class ShowServerInfoTweak : BfgTweak
    {
        public ShowServerInfoTweak(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "show_server_info";
        public override string TweakLabel => "tweak.show_server_info";
        public override bool DefaultEnabled => false;

        public static ShowServerInfoTweak Instance { get; private set; }

        private static TMP_Text _label;
        // the label survives round transitions (it lives under InGameUiManager), so every
        // round start would stack ANOTHER while(_label != null) update loop on top of the
        // old one — one more per round, forever. each Run() bumps the generation and the loop
        // exits the moment a newer Run takes over.
        private static int _runGen;
        // region info gets cached per server address so we don't hammer the api every round
        private static string _cachedAddr;
        private static string _cachedRegion;

        // byte counters fed by NetByteCounterPatch — long so we don't wrap on long sessions.
        // Interlocked.Add expects ref long; sampler reads + subtracts to get per-second deltas.
        public static long BytesOut;
        public static long BytesIn;

        void Awake() => Instance = this;

        public override void DisableTweak() => DestroyLabel();

        // toggled back on mid-round: respawn the label right away instead of waiting for next round start.
        // only if we're actually in a game (GameStateView is non-null), so flipping it on in the menu
        // doesn't spawn a stray label.
        public override void EnableTweak()
        {
            var gsv = GlobalGameStateClient.Instance?.GameStateView;
            if (gsv == null) return;
            FGClient.ClientGameManager cgm;
            if (!gsv.GetLiveClientGameManager(out cgm)) return;
            StartCoroutine(Run().WrapToIl2Cpp());
        }

        // round start. spins up the label + ms/region readout. raised only on enabled tweaks
        // the two Hazel byte-counter patches sit on Send and OnDataReceived, i.e. one native->managed
        // crossing per packet, and the readout they feed only exists during a round. so they ride the
        // round rather than the toggle — nothing is hooked while you're sat in a menu or a lobby.
        public override void OnRoundStart()
        {
            if (Features.UnityRound.Editor.UnityRoundLoader.InLevelEditor) return;
            Utilities.PatchGate.SetActive("tweak.show_server_info", true);
            StartCoroutine(Run().WrapToIl2Cpp());
        }

        public override void OnBannerShown() => Utilities.PatchGate.SetActive("tweak.show_server_info", false);
        public override void OnMainMenuEntered() => Utilities.PatchGate.SetActive("tweak.show_server_info", false);

        // entering the level editor tears the label down (the VictoryScreenBean state hook used to do
        // this by name). only fires the destroy when we're actually in the editor
        public override void OnStateChanged(GameStateMachine.IGameState newState)
        {
            if (Features.UnityRound.Editor.UnityRoundLoader.InLevelEditor) DestroyLabel();
        }

        internal static void DestroyLabel()
        {
            if (_label != null)
            {
                UnityEngine.Object.Destroy(_label.gameObject);
                _label = null;
            }
        }

        private static string RegionText()
        {
            // already fetched for this server? use the cache. otherwise show a placeholder while it loads.
            string addr = GlobalGameStateClient.Instance != null ? GlobalGameStateClient.Instance.LastConnectedServerAddress : null;
            if (!string.IsNullOrEmpty(addr) && addr == _cachedAddr && !string.IsNullOrEmpty(_cachedRegion))
                return _cachedRegion;
            return "...";
        }

        private static IEnumerator Run()
        {
            int gen = ++_runGen;
            if (_label == null)
            {
                var defaultGo = GameObject.Find("UICanvas_Client_V2(Clone)/Default/InGameUiManager(Clone)");
                if (defaultGo == null) yield break;

                var go = new GameObject("BFG_ServerInfo");
                go.transform.SetParent(defaultGo.transform, false);

                var isolate = go.AddComponent<Canvas>();
                isolate.overrideSorting = true;
                isolate.sortingOrder = 1;

                _label = go.AddComponent<TextMeshProUGUI>();
                _label.fontSize = 20f;
                _label.fontStyle = FontStyles.Bold;
                _label.alignment = TextAlignmentOptions.BottomRight;
                _label.color = Color.white;

                TMP_FontAsset asap = null;
                foreach (var fa in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
                {
                    if (fa == null || string.IsNullOrEmpty(fa.name)) continue;
                    if (fa.name.IndexOf("asap", StringComparison.OrdinalIgnoreCase) >= 0 && fa.material != null) { asap = fa; break; }
                }

                if (asap != null)
                {
                    _label.font = asap;
                    _label.extraPadding = true;
                }

                // pinned to the bottom-right corner so it stays put through any resolution/scaling.
                var rt = _label.rectTransform;
                rt.anchorMin = new Vector2(1f, 0.5f);
                rt.anchorMax = new Vector2(1f, 0.5f);
                rt.pivot = new Vector2(1f, 0.5f);
                rt.anchoredPosition = new Vector2(-40f, -40f);
                rt.sizeDelta = new Vector2(400f, 145f);
            }

            // kick off region lookup if this is a new server
            string addr = GlobalGameStateClient.Instance != null ? GlobalGameStateClient.Instance.LastConnectedServerAddress : null;
            if (!string.IsNullOrEmpty(addr) && addr != _cachedAddr)
            {
                _cachedAddr = addr;
                _cachedRegion = null;
                Instance.StartCoroutine(FetchRegion(addr).WrapToIl2Cpp());
            }

            // packets/sec + loss% sampled once a second off FG_UnityInternetNetworkManager — those
            // counters are monotonically growing ints, so we diff against the prior sample.
            // up/down KB/s come from the Hazel send/recv patch (NetByteCounterPatch).
            //
            // assigning TMP_Text.text dirties the mesh and forces the canvas it lives on to rebatch, so
            // doing it at framerate for a readout that changes a few times a second was costing real frame
            // time in every round. everything here only moves on the 1s sample or when the ping ticks over,
            // so recompose on those and leave the label alone in between.
            int lastSent = -1, lastLost = -1;
            long lastBytesOut = 0, lastBytesIn = 0;
            float lastSampleTime = 0f;
            int ppsOut = 0, lossPct = 0;
            float upKBs = 0f, downKBs = 0f;
            int lastMs = int.MinValue;
            string lastRegion = null;
            float lastFpsTime = Time.unscaledTime;
            int lastFrameCount = Time.frameCount;
            int fps = 0;
            float frameMs = 0f;

            while (gen == _runGen && _label != null)
            {
                var ggsc = GlobalGameStateClient.Instance;
                var nm = ggsc?.NetworkManager;
                int ms = nm != null ? Mathf.RoundToInt(nm.RTT * 1000f) : 0;

                var unm = nm != null ? nm.TryCast<FG_UnityInternetNetworkManager>() : null;
                bool sampled = false;
                if (unm != null)
                {
                    int sent = unm.PacketSent;
                    int lost = unm.PacketLost;
                    long bOut = Interlocked.Read(ref BytesOut);
                    long bIn = Interlocked.Read(ref BytesIn);
                    float now = Time.unscaledTime;
                    if (lastSent < 0)
                    {
                        lastSent = sent; lastLost = lost;
                        lastBytesOut = bOut; lastBytesIn = bIn;
                        lastSampleTime = now;
                        sampled = true;
                    }
                    else if (now - lastSampleTime >= 1f)
                    {
                        float dt = now - lastSampleTime;
                        int dSent = Mathf.Max(0, sent - lastSent);
                        int dLost = Mathf.Max(0, lost - lastLost);
                        ppsOut = Mathf.RoundToInt(dSent / dt);
                        int denom = dSent + dLost;
                        lossPct = denom > 0 ? Mathf.RoundToInt(100f * dLost / denom) : 0;
                        upKBs = (bOut - lastBytesOut) / dt / 1024f;
                        downKBs = (bIn - lastBytesIn) / dt / 1024f;
                        lastSent = sent; lastLost = lost;
                        lastBytesOut = bOut; lastBytesIn = bIn;
                        lastSampleTime = now;
                        sampled = true;
                    }
                }

                float fnow = Time.unscaledTime;
                if (fnow - lastFpsTime >= 1f)
                {
                    int frames = Time.frameCount - lastFrameCount;
                    float span = fnow - lastFpsTime;
                    fps = Mathf.RoundToInt(frames / span);
                    frameMs = frames > 0 ? span * 1000f / frames : 0f;
                    lastFrameCount = Time.frameCount;
                    lastFpsTime = fnow;
                    sampled = true;
                }

                string region = RegionText();
                if (sampled || ms != lastMs || !ReferenceEquals(region, lastRegion))
                {
                    lastMs = ms;
                    lastRegion = region;
                    _label.text = unm != null
                        ? $"{fps} fps  {frameMs:0.00}ms\n{ms}ms  {ppsOut}pps  {lossPct}% loss\n↑ {upKBs:0.0} KB/s   ↓ {downKBs:0.0} KB/s\n{region}"
                        : $"{fps} fps  {frameMs:0.00}ms\n{ms}ms\n{region}";
                }
                yield return _tick;
            }
        }

        private static readonly WaitForSeconds _tick = new WaitForSeconds(0.2f);

        private static IEnumerator FetchRegion(string addr)
        {
            // addr looks like "1.2.3.4:7777" — strip the port for the api
            string ip = addr;
            int colon = ip.IndexOf(':');
            if (colon > 0) ip = ip.Substring(0, colon);

            // try providers in order, first one that gives us a city/country wins. all https — the game
            // has insecure connections disabled in Player Settings so plain http (e.g. ip-api free) gets
            // blocked outright. ipwho.is is primary, freeipapi is the backup. both die -> Unknown.
            // each request gets a hard timeout so a hanging provider can't pin the label on "..." forever.
            string text = null;

            // ipwho.is — https, no key
            {
                var req = UnityEngine.Networking.UnityWebRequest.Get($"https://ipwho.is/{ip}?fields=success,city,country");
                req.timeout = 5;
                yield return req.SendWebRequest();
                if (req.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
                {
                    string json = req.downloadHandler.text;
                    if (JsonUtil.GetBool(json, "success"))
                    {
                        string city = JsonUtil.GetValue(json, "city");
                        string country = JsonUtil.GetValue(json, "country");
                        if (city.Length > 0 && country.Length > 0) text = $"{city}, {country}";
                        else if (city.Length > 0) text = city;
                        else if (country.Length > 0) text = country;
                    }
                }
                req.Dispose();
            }

            // freeipapi fallback
            if (string.IsNullOrEmpty(text))
            {
                var req = UnityEngine.Networking.UnityWebRequest.Get($"https://free.freeipapi.com/api/json/{ip}");
                req.timeout = 5;
                yield return req.SendWebRequest();
                if (req.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
                {
                    string json = req.downloadHandler.text;
                    string city = JsonUtil.GetValue(json, "cityName");
                    string country = JsonUtil.GetValue(json, "countryName");
                    if (city.Length > 0 && country.Length > 0) text = $"{city}, {country}";
                    else if (city.Length > 0) text = city;
                    else if (country.Length > 0) text = country;
                }
                req.Dispose();
            }

            if (addr == _cachedAddr)
                _cachedRegion = string.IsNullOrEmpty(text) ? "Unknown" : text;
        }
    }
}
