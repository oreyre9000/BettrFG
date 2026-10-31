using System;
using System.Collections.Generic;
using BetterFG.Features.TimePlacement;
using BetterFG.Utilities;
using UnityEngine;

namespace BetterFG.Features.CustomizeFallGuys
{
    internal static class EyePreview
    {
        public const int Width = 256;
        public const int Height = 192;

        // the clone is permanent and sits on the mugshot layer, so park it somewhere no game camera
        // could ever frame it rather than trusting every camera in the game to cull layer 31.
        // offset from PetPreview's own parked spot - both previews share layer 31 and a camera
        // there, so two clones parked at the same coordinates bled into each other's render
        static readonly Vector3 Parked = new Vector3(0f, -1000f, 0f);

        static RenderTexture _rt;
        static Camera _cam;
        static Light[] _lights;
        static GameObject _host;

        static GameObject _source;
        static GameObject _holder;
        static readonly List<Renderer> _rends = new List<Renderer>();

        public static GameObject Clone { get; private set; }

        public static Texture Ensure()
        {
            if (_rt == null) Build();
            return _rt;
        }

        public static void Invalidate()
        {
            _source = null;
            Clone = null;
            _rends.Clear();
            if (_holder != null) { UnityEngine.Object.Destroy(_holder); _holder = null; }
        }

        public static void SetBean(GameObject bean)
        {
            if (_source == bean && Clone != null) return;
            Invalidate();
            _source = bean;
            if (bean == null) return;

            Clone = StripClone(bean);
            foreach (var r in Clone.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (r.enabled && r.gameObject.activeInHierarchy) { r.updateWhenOffscreen = true; _rends.Add(r); }
            foreach (var r in Clone.GetComponentsInChildren<MeshRenderer>(true))
                if (r.enabled && r.gameObject.activeInHierarchy) _rends.Add(r);

            FeatureCustomizeFallGuys.Apply(Clone);
        }

        public static void Render()
        {
            if (_rends.Count == 0 || Clone == null) return;

            for (int i = 0; i < _rends.Count; i++)
            {
                if (_rends[i] != null && _rends[i].m_CachedPtr != IntPtr.Zero) continue;
                Invalidate();
                return;
            }

            if (_rt == null) Build();

            var body = _rends[0].bounds;
            for (int i = 1; i < _rends.Count; i++) body.Encapsulate(_rends[i].bounds);

            LeaderboardMugshotScene.FrameHead(_cam, body, Vector3.forward);

            var prevActive = RenderTexture.active;
            _cam.targetTexture = _rt;
            LeaderboardMugshotScene.PushLighting(_lights);
            _cam.Render();
            LeaderboardMugshotScene.PopLighting(_lights);
            _cam.targetTexture = null;
            RenderTexture.active = prevActive;
        }

        // a copy with nothing but transforms and renderers left on it: no customisation handler, no
        // controller, no colliders, nothing any FindObjectOfType or GetComponent sweep can land on.
        // instantiated under an INACTIVE holder so not one of those components ever gets an Awake in
        // the first place — stripping after they'd already registered themselves would be too late
        static GameObject StripClone(GameObject src) => StripCopy(src, "BettrFG_EyePreviewBean", Parked, out _holder);

        public static GameObject StripCopy(GameObject src, string holderName, Vector3 parked, out GameObject holder)
        {
            holder = new GameObject(holderName);
            holder.SetActive(false);
            UnityEngine.Object.DontDestroyOnLoad(holder);

            var clone = UnityEngine.Object.Instantiate(src, holder.transform);
            // PB_FallGuyBot (the default-preview source) is kept loaded inactive - Instantiate copies
            // that activeSelf flag onto the clone, so without this the body stays out of every
            // activeInHierarchy check below and only the separately-attached eye overlay ever shows
            clone.SetActive(true);

            foreach (var t in clone.GetComponentsInChildren<TMPro.TMP_Text>(true))
                if (t != null) UnityEngine.Object.DestroyImmediate(t.gameObject);

            // the source's own eye overlay came along for the ride; drop it and let the feature attach
            // a fresh one to the clone, otherwise the two z-fight and the tint never updates
            foreach (var t in clone.GetComponentsInChildren<Transform>(true))
                if (t != null && t.gameObject.name == "BettrFG_Eyes") UnityEngine.Object.DestroyImmediate(t.gameObject);

            // ponytail: two reverse passes clear one level of RequireComponent, which is as deep as a
            // bean goes. anything stubborn survives as a dead script on an unreachable object
            int killed = 0;
            for (int pass = 0; pass < 2; pass++)
            {
                var comps = clone.GetComponentsInChildren<Component>(true);
                for (int i = comps.Length - 1; i >= 0; i--)
                {
                    var c = comps[i];
                    if (c == null) continue;
                    if (c.TryCast<Transform>() != null || c.TryCast<SkinnedMeshRenderer>() != null
                        || c.TryCast<MeshRenderer>() != null || c.TryCast<MeshFilter>() != null) continue;
                    UnityEngine.Object.DestroyImmediate(c);
                    killed++;
                }
            }

            GameObjectHelper.SetLayerRecursively(clone, LeaderboardMugshotScene.Layer);
            foreach (var t in clone.GetComponentsInChildren<Transform>(true))
                t.gameObject.hideFlags = HideFlags.HideAndDontSave;
            holder.hideFlags = HideFlags.HideAndDontSave;

            clone.transform.SetPositionAndRotation(parked, Quaternion.identity);
            holder.SetActive(true);

            Plugin.Log.LogInfo($"preview bean cloned off {src.name}, {killed} components stripped, parked at {parked}");
            return clone;
        }

        static void Build()
        {
            _host = new GameObject("BettrFG_EyePreviewCam");
            _host.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(_host);

            _cam = LeaderboardMugshotScene.BuildCamera(_host, out _lights);
            _cam.aspect = (float)Width / Height;
            _cam.backgroundColor = LeaderboardMugshotScene.KeyBackdrop;

            // RGB565, not ARGB32 - the format has no alpha channel at all, which is what makes it
            // immune to the body shader's forced alpha 0 (see LeaderboardMugshotScene.KeyBackdrop)
            _rt = new RenderTexture(Width, Height, 16, RenderTextureFormat.RGB565);
            _rt.filterMode = FilterMode.Bilinear;
            _rt.Create();
        }
    }
}
