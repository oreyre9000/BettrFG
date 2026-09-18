using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BetterFG.Customization.Player;
using BetterFG.Utilities;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using FG.Common;
using UnityEngine;
using UnityEngine.Networking;
using TMPro;
using BetterFG.Nametag;
using FGClient;
using BetterFG.Services;
using BetterFG.UI.Tabs;

namespace BetterFG.Network
{
    public class RemoteNametagInfo
    {
        public float r, g, b;
        public bool bold, italic;
        public string customName;
        public string iconMode;
        public string iconCountry;
        public string iconPath;
        public float iconScale;
        public float iconOffX, iconOffY;
        public string platformHide;
        public string platformCustom;
        public string nameStyle;
        public string gradientColors;
        public float gradientAngle;
        public bool outlineEnabled;
        public float outlineR, outlineG, outlineB;
        public float outlineWidth;
        public string outlineMode;
        public bool backingEnabled;
        public string backingPath;
        public float backingOffX, backingOffY;
        public float backingScale;
        public string nickname;

        public RemoteNametagInfo WithoutCustomName()
        {
            var copy = (RemoteNametagInfo)MemberwiseClone();
            copy.customName = "";
            return copy;
        }
    }

    public class RemoteSkinEntry
    {
        public string file;
        public string type;
        public string source;
        public string localPath;
        public string repoUrl; // raw base URL, e.g. https://raw.githubusercontent.com/oreyre9000/BetterFGPublicSkins/main
        public string folder;
        // items only: 0 whatever the skin authored, 1 left, 2 right, 3 both
        public int hand;
        public string bundleB64;
    }

    public class NetworkClient : MonoBehaviour
    {
        public NetworkClient(IntPtr ptr) : base(ptr) { }

        public static NetworkClient Instance { get; private set; }

        private List<BfgProfile> _profiles = new List<BfgProfile>();

        void Awake() => Instance = this;

        public void OnRoundStart()
        {
            RemoteProfileStore.Clear();
            LoadProfilesFromFile();
        }

        // lobby path: just (re)build the profile lookup maps so LobbyProfileService can match by name.
        // does NOT run the in-round apply coroutine (no round beans exist in the menu). throttled —
        // the party-menu nameplate updates call this once PER member PER graphics refresh, which
        // otherwise re-reads + re-unpacks every profile from disk many times a second.
        private static float _lastPrime = -999f;
        public static void PrimeProfilesForLobby(bool force = false)
        {
#if PROFILES
            if (!force && Time.realtimeSinceStartup - _lastPrime < 3f) return;
            _lastPrime = Time.realtimeSinceStartup;
            BetterFG.Customization.Profiles.ProfileService.GetRemoteProfiles();
#endif
        }

        // keyed only, never positional: without a key this lands in _pending and ResolvePending
        // stamps our loadout onto some remote bean
        public void RegisterLocalProfile()
        {
            string key;
            try { key = GlobalGameStateClient.Instance?.GetLocalPlayerKey() ?? ""; }
            catch { key = ""; }
            if (string.IsNullOrEmpty(key)) return;

            var profile = BfgProfile.FromLocal();
            profile.username = key;

            RemoteProfileStore.Register(profile, key);
        }

        private void LoadProfilesFromFile()
        {
            _profiles.Clear();

            string path = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "debug_profiles.json");
            if (File.Exists(path))
            {
                string json = null;
                try { json = File.ReadAllText(path); }
                catch (Exception ex) { Plugin.Log.LogError("NetworkClient: read err: " + ex.Message); }

                if (json != null)
                    foreach (string entry in JsonUtil.GetRootArray(json))
                    {
                        var p = BfgProfile.FromJson(entry);
                        if (p != null) _profiles.Add(p);
                    }
            }
            else Plugin.Log.LogInfo("NetworkClient: no debug_profiles.json");

#if PROFILES
            // saved player profiles (.bfgprofile) ride the same pipeline
            _profiles.AddRange(BetterFG.Customization.Profiles.ProfileService.GetRemoteProfiles());
#endif

            foreach (var profile in _profiles)
                RemoteProfileStore.Register(profile);

            if (_profiles.Count > 0)
                StartCoroutine(ApplyProfilesCoroutine().WrapToIl2Cpp());
        }

        private IEnumerator ApplyProfilesCoroutine()
        {
            yield return new WaitForSeconds(1f);

            var localBean = BeanMonitorService.LocalPlayerBean;
            var remotes = BeanNetworkUtil.GetRemotePlayerBeansSorted(localBean);
            if (remotes.Count == 0 && localBean == null) yield break;

            foreach (var profile in _profiles)
                RemoteProfileStore.Register(profile);

            RemoteProfileStore.ResolvePending(localBean);

            string localKey = "";
            try { localKey = GlobalGameStateClient.Instance?.GetLocalPlayerKey() ?? ""; } catch { }
            localKey = FallGuysLib.Players.PlayerUtils.CleanPlayerName(localKey);

            for (int i = 0; i < _profiles.Count; i++)
            {
                var profile = _profiles[i];
                GameObject bean = null;

                if (profile.requireKeyMatch)
                {
                    string want = profile.CleanName;

                    if (localBean != null && profile.KeyMatches(localKey))
                        bean = localBean;

                    if (bean == null)
                        foreach (var r in remotes)
                        {
                            if (!profile.KeyMatches(BeanNetworkUtil.TryGetPlayerKeyForBean(r))) continue;
                            bean = r; break;
                        }

                    if (bean == null) { Plugin.Log.LogInfo($"nobody in this round is called '{want}', skipping that profile"); continue; }
                    profile.resolvedPlayerKey = profile.username;
                    Plugin.Log.LogInfo($"profile '{want}' -> {bean.name}");
                }
                else
                {
                    if (i >= remotes.Count) continue;
                    bean = remotes[i];
                }

                StartCoroutine(CustomizationHandler.Apply(profile, bean).WrapToIl2Cpp());
            }
        }

        internal static IEnumerator PollAndApplyPlatformIcon(GameObject bean, bool hide, string customSprite)
        {
            var fgcc = bean.GetComponent<FallGuysCharacterController>();
            if (fgcc == null) yield break;

            float elapsed = 0f;
            float wait = 0.1f;
            while (elapsed < 5f)
            {
                var huds = UnityEngine.Object.FindObjectsOfType<PlayerInfoHUDBase>(true);
                wait = huds == null || huds.Length == 0 ? 1f : 0.1f;
                if (huds != null)
                {
                    bool found = false;
                    for (int h = 0; h < huds.Length; h++)
                    {
                        var spawned = huds[h]?._spawnedInfoObjects;
                        if (spawned == null) continue;
                        for (int i = 0; i < spawned.Count; i++)
                        {
                            var row = spawned[i];
                            if (row == null || row.fgcc != fgcc) continue;
                            NametagIconApplicator.ApplyPlatformIcon(row.playerInfo?.gameObject, hide, customSprite);
                            found = true;
                            break;
                        }
                        if (found) yield break;
                    }
                }
                yield return new WaitForSeconds(wait);
                elapsed += wait;
            }
            Plugin.Log.LogWarning($"NetworkClient: timed out for '{bean.name}'");
        }

    }

    public class AppliedRemoteSkin
    {
        public GameObject instance;
        public SkinType type;
    }
}
