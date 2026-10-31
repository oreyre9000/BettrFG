using System.Collections.Generic;
using BetterFG.Features.TimePlacement;
using BetterFG.Utilities;
using UnityEngine;

namespace BetterFG.Customization.Player
{
    internal static class ItemPreview
    {
        static readonly Vector3 Parked = new Vector3(0f, -1100f, 0f);
        static readonly Vector3 FrameCenter = new Vector3(0.06f, 0.82f, 0.31f);
        const float FrameHalfHeight = 1.28f;
        static readonly string[] Bones = { "Wrist_L_jnt", "Wrist_R_jnt1" };
        static readonly HashSet<string> Junk = new HashSet<string> { "HolderParent", "PB_TailTag", "AdditionalColliders", "Ragdoll", "DEBUG" };

        static GameObject _holder, _camHost;
        static Camera _cam;
        static Light[] _lights;
        static RenderTexture _rt;
        static readonly Transform[] _items = new Transform[2];
        static ItemHandInfo[] _hands;

        public static Texture Show(SkinInfo skin, float aspect)
        {
            Hide();
            _hands = new[] { skin.left, skin.right };

            _holder = new GameObject("BettrFG_ItemPreviewBean") { hideFlags = HideFlags.HideAndDontSave };
            _holder.SetActive(false);
            Object.DontDestroyOnLoad(_holder);
            var bean = Object.Instantiate(GameObjectHelper.FindDefaultBotBean(), _holder.transform);
            bean.SetActive(true);

            var junk = new List<GameObject>();
            foreach (var t in bean.GetComponentsInChildren<Transform>(true))
                if (Junk.Contains(t.name) || t.name.StartsWith("Body_LOD") && t.name != "Body_LOD0") junk.Add(t.gameObject);
            foreach (var g in junk) Object.DestroyImmediate(g);

            for (int pass = 0; pass < 2; pass++)
            {
                var comps = bean.GetComponentsInChildren<Component>(true);
                for (int i = comps.Length - 1; i >= 0; i--)
                {
                    var c = comps[i];
                    if (c == null || c.TryCast<Transform>() != null || c.TryCast<SkinnedMeshRenderer>() != null || c.TryCast<Animator>() != null) continue;
                    Object.DestroyImmediate(c);
                }
            }

            if (CustomizationServices.ApplicationService.TryGetLoadedBundle(skin.file, out var bundle))
            {
                var prefab = bundle.LoadAsset(skin.file).Cast<GameObject>();
                var bones = bean.GetComponentsInChildren<Transform>(true);
                for (int h = 0; h < 2; h++)
                {
                    if (_hands[h] == null) continue;
                    Transform wrist = null;
                    foreach (var t in bones) if (t.name == Bones[h]) wrist = t;
                    var item = Object.Instantiate(prefab, wrist, false);
                    GameObjectHelper.StripPhysics(item);
                    item.transform.localScale = Vector3.one * skin.scale;
                    _items[h] = item.transform;
                }
            }
            else Plugin.Log.LogWarning($"{skin.file} has no loaded bundle, item preview is just the bean");

            GameObjectHelper.SetLayerRecursively(bean, LeaderboardMugshotScene.Layer);
            foreach (var t in bean.GetComponentsInChildren<Transform>(true)) t.gameObject.hideFlags = HideFlags.HideAndDontSave;
            bean.transform.SetPositionAndRotation(Parked, Quaternion.identity);
            _holder.SetActive(true);

            _camHost = new GameObject("BettrFG_ItemPreviewCam") { hideFlags = HideFlags.HideAndDontSave };
            Object.DontDestroyOnLoad(_camHost);
            _cam = LeaderboardMugshotScene.BuildCamera(_camHost, out _lights);
            _cam.aspect = aspect;
            _cam.backgroundColor = new Color(0.08f, 0.09f, 0.12f, 1f);
            _cam.orthographicSize = FrameHalfHeight;
            _camHost.transform.SetPositionAndRotation(Parked + FrameCenter + Vector3.forward * LeaderboardMugshotScene.Distance,
                Quaternion.LookRotation(Vector3.back, Vector3.up));

            _rt = new RenderTexture(Mathf.RoundToInt(432f * aspect), 432, 16, RenderTextureFormat.RGB565) { hideFlags = HideFlags.HideAndDontSave };
            _cam.targetTexture = _rt;
            SetLive(true);
            return _rt;
        }

        public static void SetLive(bool live)
        {
            if (_cam == null) return;
            _cam.enabled = live;
            foreach (var l in _lights) l.enabled = live;
        }

        public static void SetHand(int hand, bool on, Vector3 offset, Vector3 rot)
        {
            var item = _items[hand];
            if (item == null) return;
            item.gameObject.SetActive(on);
            item.localPosition = _hands[hand].Position + offset;
            item.localEulerAngles = _hands[hand].Rotation + rot;
        }

        public static void Hide()
        {
            if (_holder != null) Object.Destroy(_holder);
            if (_camHost != null) Object.Destroy(_camHost);
            if (_rt != null) Object.Destroy(_rt);
            _holder = _camHost = null;
            _cam = null;
            _rt = null;
            _items[0] = _items[1] = null;
        }
    }
}
