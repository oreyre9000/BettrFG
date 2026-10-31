using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Core;
using BetterFG.Customization.Pets;
using BetterFG.Customization.Player;
using BetterFG.Customization.Profiles;
using BetterFG.Customization.UI;
using BetterFG.Features.TimePlacement;
using BetterFG.Services;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace BetterFG.Customization.Users
{
    internal static class UserPreviewStage
    {
        static readonly Vector3 Spot = new Vector3(-1500f, -1000f, 0f);
        static readonly Vector3 BuildSpot = new Vector3(-1440f, -1000f, 0f);
        static readonly Vector3 IconSpot = new Vector3(-1380f, -1000f, 0f);
        static readonly Quaternion IconView = Quaternion.Euler(35f, 225f, 0f);

        public static RenderTexture Texture { get; private set; }

        static Camera _cam;
        static Light[] _lights;
        static GameObject _holder, _bean;
        static int _gen;
        static bool _framed;
        static List<EmotesOption> _emotes;
        static PlayableGraph _graph;
        static AnimationLayerMixerPlayable _mixer;
        static bool _graphLive, _loading;
        static float _clipStart, _clipLen, _nextEmote;
        static readonly System.Random Rng = new System.Random();

        public static void Open()
        {
            var host = new GameObject("BettrFG_UserPreviewCam") { hideFlags = HideFlags.HideAndDontSave };
            _cam = LeaderboardMugshotScene.BuildCamera(host, out _lights);
            _lights[0].color = Color.Lerp(_lights[0].color, new Color(1f, 0.7f, 0.4f), 0.45f);
            _lights[0].intensity *= 1.15f;
            _lights[2].color = new Color(1f, 0.75f, 0.45f);
            _lights[2].intensity *= 1.6f;
            _cam.backgroundColor = Color.black;
            Texture = new RenderTexture(1200, 1216, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8, hideFlags = HideFlags.HideAndDontSave };
            Texture.Create();
            _cam.aspect = 1200f / 1216f;
            _cam.targetTexture = Texture;
            _framed = false;
            _emotes = new List<EmotesOption>();
            foreach (var e in Resources.FindObjectsOfTypeAll<EmotesOption>()) _emotes.Add(e);
        }

        public static void Close()
        {
            _gen++;
            KillGraph();
            _loading = false;
            if (_holder != null) UnityEngine.Object.Destroy(_holder);
            _holder = _bean = null;
            if (_cam != null) UnityEngine.Object.Destroy(_cam.gameObject);
            if (Texture != null) { Texture.Release(); UnityEngine.Object.Destroy(Texture); }
            _cam = null;
            Texture = null;
        }

        public static void Show(string user)
        {
            _gen++;
            BeanMonitorService.Instance.StartCoroutine(BuildRoutine(_gen, user).WrapToIl2Cpp());
        }

        public static void Render()
        {
            if (_bean == null || _cam == null) return;
            _bean.transform.SetPositionAndRotation(Spot, Quaternion.identity);
            LeaderboardMugshotScene.PushLighting(_lights);
            _cam.Render();
            LeaderboardMugshotScene.PopLighting(_lights);
            try { TickEmotes(); }
            catch (Exception ex) { _nextEmote = Time.time + 5f; Plugin.Log.LogWarning($"logon emote hiccup: {ex.Message}"); }
        }

        static PetData LookFor(Dictionary<string, string> s)
        {
            var look = ProfileService.LocalLook();
            Func<string, string, string> get = (k, d) => s.TryGetValue(k, out var v) && v.Length > 0 ? v : d;
            var data = new PetData
            {
                name = "BettrFG_UserPreview", scale = 1f, colour = look.colour, pattern = look.pattern,
                faceplate = look.faceplate, costumeTop = look.costumeTop, costumeBottom = look.costumeBottom,
            };
            data.skins.AddRange(BfgProfile.ReadLoadout(get));
            data.skinTexEntries = SkinApplicationService.ReadTexEntries(get);
            return data;
        }

        static IEnumerator BuildRoutine(int gen, string user)
        {
            var settings = SettingsService.ReadUserSettings(user);
            GameObject bean = null;
            yield return PetBeanBuilder.Build(LookFor(settings), new Action<GameObject>(b => bean = b), forPreview: true);
            if (bean == null) { Plugin.Log.LogWarning($"no preview bean for {user}"); yield break; }
            if (gen != _gen) { UnityEngine.Object.Destroy(bean); yield break; }

            var holder = PetPreview.Park(bean, "BettrFG_UserPreviewBean", BuildSpot);
            foreach (var rb in bean.GetComponentsInChildren<Rigidbody>(true)) { rb.isKinematic = true; rb.useGravity = false; }
            foreach (var an in bean.GetComponentsInChildren<Animator>(true)) an.applyRootMotion = false;
            bean.transform.SetPositionAndRotation(BuildSpot, Quaternion.identity);

            var stage = new GameObject("Stage").transform;
            stage.SetParent(holder.transform, false);
            stage.SetPositionAndRotation(BuildSpot, Quaternion.identity);
            yield return BuildPlinth(stage, settings).WrapToIl2Cpp();

            for (float t = 0f; t < 0.35f; t += Time.deltaTime) yield return null;
            if (gen != _gen) { UnityEngine.Object.Destroy(holder); yield break; }

            holder.transform.position += Spot - BuildSpot;
            bean.transform.SetPositionAndRotation(Spot, Quaternion.identity);
            if (!_framed) Frame(bean);

            bool swap = _holder != null;
            KillGraph();
            if (_holder != null) UnityEngine.Object.Destroy(_holder);
            _holder = holder;
            _bean = bean;
            _nextEmote = Time.time + 0.8f;
            if (swap) Poof();
        }

        static void Frame(GameObject bean)
        {
            Bounds b = default;
            bool any = false;
            foreach (var r in bean.GetComponentsInChildren<SkinnedMeshRenderer>(false))
            {
                if (!r.enabled) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            var fwd = Quaternion.Euler(0f, 28f, 0f) * Vector3.forward;
            float ortho = Mathf.Max(0.5f, b.size.y) * 1.05f;
            _cam.transform.position = Spot + Vector3.up * (ortho * 0.24f) + fwd * 20f;
            _cam.transform.rotation = Quaternion.LookRotation(-fwd, Vector3.up);
            _cam.orthographicSize = ortho;
            _framed = true;
        }

        static IEnumerator BuildPlinth(Transform stage, Dictionary<string, string> s)
        {
            string Get(string k) => s.TryGetValue(k, out var v) ? v : "";
            var files = Get("skin.multi.files").Split(',');
            var types = Get("skin.multi.types").Split(',');
            var sources = Get("skin.multi.sources").Split(',');
            var paths = Get("skin.multi.paths").Split(',');
            var repos = Get("skin.multi.repos").Split(',');
            var folders = Get("skin.multi.folders").Split(',');
            string file = "", source = "", path = "", repo = "", folder = "";
            for (int i = 0; i < files.Length; i++)
                if (i < types.Length && SkinTypeParser.FromString(types[i].Trim()) == SkinType.Plinth && files[i].Trim().Length > 0)
                {
                    file = files[i].Trim();
                    source = i < sources.Length ? sources[i].Trim() : "";
                    path = i < paths.Length ? paths[i].Trim() : "";
                    repo = i < repos.Length ? repos[i].Trim() : "";
                    folder = i < folders.Length ? folders[i].Trim() : "";
                    break;
                }

            GameObject clone = null;
            if (source == "game")
            {
                MenuCustomizationApplication.GamePlinth gp = null;
                foreach (var g in MenuCustomizationApplication.GamePlinths) if (g.Id == file) gp = g;
                if (gp != null)
                {
                    var h = Addressables.LoadAssetAsync<GameObject>(gp.Guid);
                    while (!h.IsDone) yield return null;
                    if (h.Result != null)
                    {
                        clone = UnityEngine.Object.Instantiate(h.Result, stage);
                        clone.transform.localScale = MenuCustomizationApplication.Instance.GameScaleFor(gp);
                    }
                }
            }
            else if (file.Length > 0)
            {
                var bundle = BetterFG.Utilities.Bundles.Get(file);
                if (bundle == null && source == "local" && File.Exists(Path.Combine(path, file)))
                {
                    var req = AssetBundle.LoadFromFileAsync(Path.Combine(path, file));
                    while (!req.isDone) yield return null;
                    bundle = req.assetBundle;
                    if (bundle != null) BetterFG.Utilities.Bundles.Register(file, bundle);
                }
                if (bundle == null && source != "local")
                {
                    var info = new SkinInfo { file = file, name = file, sourceRepo = repo, repoFolder = folder, type = "Plinth" };
                    yield return SkinApplicationService.Instance.LoadPlinthBundle(info, MenuCustomizationApplication.Instance, ab => bundle = ab).WrapToIl2Cpp();
                }
                string prefabName = bundle == null ? null : MenuCustomizationApplication.FindPrefabName(bundle);
                var prefab = prefabName == null ? null : bundle.LoadAsset<GameObject>(prefabName);
                if (prefab != null) clone = UnityEngine.Object.Instantiate(prefab, stage);
                else Plugin.Log.LogWarning($"plinth {file} wouldn't load for the logon preview, stock one it is");
            }
            if (clone == null)
            {
                var stock = GameObject.Find(MenuCustomizationApplication.PLINTH_PATH)?.transform.Find("ENV_Plinth_MO")?.gameObject;
                if (stock != null)
                {
                    clone = UnityEngine.Object.Instantiate(stock, stage);
                    clone.SetActive(true);
                }
            }
            if (clone == null) yield break;

            clone.name = "Plinth";
            clone.transform.localRotation = Quaternion.identity;
            foreach (var t in clone.GetComponentsInChildren<Transform>(true))
            {
                t.gameObject.layer = LeaderboardMugshotScene.Layer;
                t.gameObject.hideFlags = HideFlags.HideAndDontSave;
            }
            foreach (var r in clone.GetComponentsInChildren<Renderer>(true)) r.enabled = true;
            yield return null;

            Bounds pb = default;
            bool any = false;
            foreach (var r in clone.GetComponentsInChildren<Renderer>(true))
            {
                if (!any) { pb = r.bounds; any = true; } else pb.Encapsulate(r.bounds);
            }
            if (!any) yield break;
            var top = stage.position.y - 0.01f;
            clone.transform.position += new Vector3(stage.position.x - pb.center.x, top - pb.max.y, stage.position.z - pb.center.z);
        }

        static void Poof()
        {
            var am = AssetManager.Instance;
            if (am != null && am.prefabs.TryGetValue("vfx_poof", out var prefab) && prefab != null)
            {
                var go = UnityEngine.Object.Instantiate(prefab, Spot + Vector3.up * 0.9f, Quaternion.identity);
                foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = LeaderboardMugshotScene.Layer;
                foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
                {
                    ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                    ps.Play(false);
                }
                UnityEngine.Object.Destroy(go, 1.6f);
            }
            AudioService.PlayPoof();
        }

        static void KillGraph()
        {
            if (_graphLive) _graph.Destroy();
            _graphLive = false;
        }

        static void TickEmotes()
        {
            if (_emotes == null || _emotes.Count == 0) return;
            if (_graphLive)
            {
                float e = Time.time - _clipStart;
                _mixer.SetInputWeight(1, Mathf.Clamp01(e / 0.15f) * Mathf.Clamp01((_clipLen - e) / 0.15f));
                if (e >= _clipLen) { KillGraph(); _nextEmote = Time.time + 0.6f + (float)Rng.NextDouble() * 0.8f; }
                return;
            }
            if (_loading || Time.time < _nextEmote) return;
            BeanMonitorService.Instance.StartCoroutine(PlayRandomEmote(_bean).WrapToIl2Cpp());
        }

        static IEnumerator PlayRandomEmote(GameObject bean)
        {
            _loading = true;
            AnimationClip clip = null;
            for (int tries = 0; tries < 4 && clip == null; tries++)
            {
                string guid = _emotes[Rng.Next(_emotes.Count)].AnimationClipLoadable?.AssetGUID;
                if (string.IsNullOrEmpty(guid)) continue;
                var h = Addressables.LoadAssetAsync<AnimationClip>(guid);
                while (!h.IsDone) yield return null;
                clip = h.Result;
            }
            _loading = false;
            if (clip == null || bean == null || bean != _bean) yield break;

            var animator = bean.GetComponentInChildren<Animator>(true);
            if (animator == null || animator.runtimeAnimatorController == null) yield break;
            KillGraph();
            _graph = PlayableGraph.Create("BettrFG_UserPreviewEmote");
            _graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            var output = AnimationPlayableOutput.Create(_graph, "out", animator);
            _mixer = AnimationLayerMixerPlayable.Create(_graph, 2);
            var ctrl = AnimatorControllerPlayable.Create(_graph, animator.runtimeAnimatorController);
            var cp = AnimationClipPlayable.Create(_graph, clip);
            cp.SetApplyFootIK(false);
            _graph.Connect(ctrl, 0, _mixer, 0);
            _graph.Connect(cp, 0, _mixer, 1);
            _mixer.SetInputWeight(0, 1f);
            _mixer.SetInputWeight(1, 0f);
            output.SetSourcePlayable(_mixer);
            _graph.Play();
            _graphLive = true;
            _clipStart = Time.time;
            _clipLen = Mathf.Clamp(clip.length, 1.5f, 4.5f);
        }

        public static string IconPath(string user) => Path.Combine(SettingsService.UsersDir, user, "icon.png");

        static bool IconStale(string user)
        {
            var icon = new FileInfo(IconPath(user));
            if (!icon.Exists) return true;
            var settings = new FileInfo(Path.Combine(SettingsService.UsersDir, user, "settings.txt"));
            return settings.Exists && settings.LastWriteTimeUtc > icon.LastWriteTimeUtc;
        }

        public static IEnumerator RefreshIcons(List<string> users, Action<string> done)
        {
            foreach (var user in users)
            {
                if (!IconStale(user)) continue;
                GameObject bean = null;
                yield return PetBeanBuilder.Build(LookFor(SettingsService.ReadUserSettings(user)), new Action<GameObject>(b => bean = b), forPreview: true);
                if (bean == null) continue;
                var holder = PetPreview.Park(bean, "BettrFG_UserIconBean", IconSpot);
                for (float t = 0f; t < 0.6f; t += Time.unscaledDeltaTime) yield return null;
                var png = PetThumb.RenderPng(bean, IconView);
                UnityEngine.Object.Destroy(holder);
                if (png == null) continue;
                File.WriteAllBytes(IconPath(user), png);
                Plugin.Log.LogInfo($"fresh logon icon for {user}");
                done(user);
            }
        }
    }
}
