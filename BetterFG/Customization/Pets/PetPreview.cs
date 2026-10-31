using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Features.TimePlacement;
using BetterFG.Utilities;
using UnityEngine;

namespace BetterFG.Customization.Pets
{
    // live "as you tweak it" preview for the pet wizard - same clone-into-a-hidden-holder-and-render
    // shape as EyePreview, but the source is a freshly-built pet bean (PetBeanBuilder) instead of a
    // clone of the local player's live bean, so it needs a coroutine host for the costume download.
    internal static class PetPreview
    {
        public const int Width = 256;
        public const int Height = 192;

        // offset from EyePreview's parked spot - both previews share layer 31 and a camera there,
        // so two clones parked at the same coordinates bled into each other's render
        static readonly Vector3 Parked = new Vector3(500f, -1000f, 0f);

        static RenderTexture _rt;
        static Camera _cam;
        static Light[] _lights;
        static GameObject _camHost;
        static GameObject _holder;
        static int _gen;
        static readonly List<Renderer> _rends = new List<Renderer>();

        public static GameObject Clone { get; private set; }

        public static Texture Ensure()
        {
            if (_rt == null) Build();
            return _rt;
        }

        public static void Invalidate()
        {
            _gen++;
            Clone = null;
            _rends.Clear();
            if (_holder != null) { UnityEngine.Object.Destroy(_holder); _holder = null; }
        }

        static MonoBehaviour _lastRunner;
        static PetData _lastData;

        public static void Rebuild(MonoBehaviour runner, PetData data)
        {
            _lastRunner = runner;
            _lastData = data;
            Invalidate();
            int gen = _gen;
            runner.StartCoroutine(RebuildRoutine(gen, data).WrapToIl2Cpp());
        }

        // a pet tab can be open before the menu's costume/pattern/faceplate options have loaded,
        // which leaves the build with nothing to resolve names against and the preview stuck blank
        // forever since nothing else retries it. OnMainMenuEntered fires once those options are
        // guaranteed loaded, so retry the same build if a consumer is still around to show it.
        public static void RetryOnMainMenuEntered()
        {
            if (_lastRunner == null || _lastData == null) return;
            Rebuild(_lastRunner, _lastData);
        }

        static IEnumerator RebuildRoutine(int gen, PetData data)
        {
            GameObject bean = null;
            yield return PetBeanBuilder.Build(data, b => bean = b, forPreview: true);
            if (gen != _gen) { if (bean != null) UnityEngine.Object.Destroy(bean); yield break; }
            if (bean == null) yield break;

            _holder = Park(bean, "BettrFG_PetPreviewBean", Parked);

            // the reparent+layer-change above happens AFTER PetBeanBuilder already ran its own
            // Apply/ApplyLater, and lands in the same "bean mid-rebuild" window that makes
            // EyeGeometry.Attach fail silently (see eye-overlay-attach-fails-silently) - re-push once
            // more now that the bean is done settling into its parked, layer-31 state
            BetterFG.Features.CustomizeFallGuys.FeatureCustomizeFallGuys.ApplyLater(bean, 0.2f, true);

            Clone = bean;
            foreach (var r in Clone.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (r.enabled && r.gameObject.activeInHierarchy) { r.updateWhenOffscreen = true; _rends.Add(r); }
            foreach (var r in Clone.GetComponentsInChildren<MeshRenderer>(true))
                if (r.enabled && r.gameObject.activeInHierarchy) _rends.Add(r);
        }

        public static GameObject Park(GameObject bean, string holderName, Vector3 parked)
        {
            var holder = new GameObject(holderName);
            holder.SetActive(false);
            UnityEngine.Object.DontDestroyOnLoad(holder);
            bean.transform.SetParent(holder.transform, false);

            foreach (var t in bean.GetComponentsInChildren<TMPro.TMP_Text>(true))
                if (t != null) UnityEngine.Object.DestroyImmediate(t.gameObject);

            GameObjectHelper.SetLayerRecursively(bean, LeaderboardMugshotScene.Layer);
            foreach (var t in bean.GetComponentsInChildren<Transform>(true))
                t.gameObject.hideFlags = HideFlags.HideAndDontSave;
            holder.hideFlags = HideFlags.HideAndDontSave;

            bean.transform.SetPositionAndRotation(parked, Quaternion.identity);
            holder.SetActive(true);
            foreach (var r in bean.GetComponentsInChildren<SkinnedMeshRenderer>(true)) r.updateWhenOffscreen = true;
            return holder;
        }

        public static void Render()
        {
            if (Clone == null) return;
            if (Clone.m_CachedPtr == IntPtr.Zero) { Invalidate(); return; }

            // a pet whose look came from ApplyLookManually (no live round to bake NPCCustomization
            // in at spawn - true for every pet built outside a round) gets its costume/pattern/
            // faceplate pushed through FallguyCustomisationHandler AFTER this bean was already
            // handed to us, which can swap out the body's SkinnedMeshRenderer for a new instance.
            // used to just Invalidate() and give up the moment that happened, freezing the preview
            // on its last frame forever - re-scan the still-alive bean instead
            bool stale = _rends.Count == 0;
            for (int i = 0; i < _rends.Count && !stale; i++)
                if (_rends[i] == null || _rends[i].m_CachedPtr == IntPtr.Zero) stale = true;

            if (stale)
            {
                _rends.Clear();
                foreach (var r in Clone.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    if (r.enabled && r.gameObject.activeInHierarchy) { r.updateWhenOffscreen = true; _rends.Add(r); }
                foreach (var r in Clone.GetComponentsInChildren<MeshRenderer>(true))
                    if (r.enabled && r.gameObject.activeInHierarchy) _rends.Add(r);
                if (_rends.Count == 0) return;
            }

            if (_rt == null) Build();

            var body = _rends[0].bounds;
            for (int i = 1; i < _rends.Count; i++) body.Encapsulate(_rends[i].bounds);

            LeaderboardMugshotScene.FrameBody(_cam, body, Vector3.forward);

            var prevActive = RenderTexture.active;
            _cam.targetTexture = _rt;
            LeaderboardMugshotScene.PushLighting(_lights);
            _cam.Render();
            LeaderboardMugshotScene.PopLighting(_lights);
            _cam.targetTexture = null;
            RenderTexture.active = prevActive;
        }

        static void Build()
        {
            _camHost = new GameObject("BettrFG_PetPreviewCam");
            _camHost.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(_camHost);

            _cam = LeaderboardMugshotScene.BuildCamera(_camHost, out _lights);
            _cam.aspect = (float)Width / Height;

            // no alpha channel, so the body shader's forced alpha 0 can't blank the bean. the fill is
            // the pet panel's own backdrop colour (PetPreviewPanel frame Image) so the render blends
            // into the panel instead of sitting on a visible keyed-out rectangle.
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.055f, 0.055f, 0.06f, 1f);

            _rt = new RenderTexture(Width, Height, 16, RenderTextureFormat.RGB565);
            _rt.filterMode = FilterMode.Bilinear;
            _rt.Create();
        }
    }
}
