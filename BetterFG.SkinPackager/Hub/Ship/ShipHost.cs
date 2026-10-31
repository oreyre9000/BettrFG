using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BetterFG.Utilities;
using BettrFG.uGUI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BetterFG.Editor
{
    internal sealed class ShipHost
    {
        public RenderTexture Texture { get; private set; }
        public RectTransform Root { get; private set; }
        public Canvas Canvas { get; private set; }
        public Camera Camera { get; private set; }
        public EventSystem Events { get; private set; }
        public float Width { get; private set; }
        public float Height { get; private set; }
        public float Scale { get; private set; }

        private readonly Scene _scene;
        private readonly GameObject _camGo, _canvasGo, _esGo;
        private double _last;
        private readonly Dictionary<Animation, float> _anims = new Dictionary<Animation, float>();

        private static readonly FieldInfo TmpInstance = typeof(TMPro.TMP_Settings).GetField("s_Instance", BindingFlags.Static | BindingFlags.NonPublic);
        private static Material _leather;
        private static Shader[] _tmpShaders;
        private static bool _wired, _tmpFailed;

        private static AssetBundle Bundle(string name, string resource)
        {
            foreach (var b in AssetBundle.GetAllLoadedAssetBundles())
                if (b != null && b.name == name) return b;
            using (var s = typeof(ShipHost).Assembly.GetManifestResourceStream(resource))
            {
                var bytes = new byte[s.Length];
                s.Read(bytes, 0, bytes.Length);
                return AssetBundle.LoadFromMemory(bytes);
            }
        }

        public static bool EnsureTmp()
        {
            if (TmpInstance.GetValue(null) != null) return true;
            if (_tmpFailed) return false;
            var own = Resources.Load<TMPro.TMP_Settings>("TMP Settings");
            if (own != null) { TmpInstance.SetValue(null, own); return true; }

            var bundle = Bundle("bettrfg_hub_tmp", "BetterFG.Creator.bettrfg_hub_tmp");
            var settings = bundle != null ? bundle.LoadAllAssets<TMPro.TMP_Settings>() : null;
            if (settings == null || settings.Length == 0)
            {
                _tmpFailed = true;
                Debug.LogWarning("hub TMP bundle didn't load, no font for the Creator Hub");
                return false;
            }
            _tmpShaders = bundle.LoadAllAssets<Shader>();
            TmpInstance.SetValue(null, settings[0]);
            return true;
        }

        private static void Wire()
        {
            if (_wired) return;
            _wired = true;
            UGUIShip.Log = Plugin.Log;
            UGUIShip.ResourceAssembly = typeof(ShipHost).Assembly;
            UGUIShip.LoadTexture = EmbeddedResourceandUnity.LoadTexture;
            UGUIShip.LoadSprite = res => EmbeddedResourceandUnity.LoadSprite(res);
            UGUIShip.LoadSlicedSprite = EmbeddedResourceandUnity.LoadSprite;
            UGUIShip.PlayClick = () => { };
            UGUIShip.PlayHover = () => { };
            UGUIShip.RegisterCanvas = c => c.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1;
            UGUIShip.ProtectFont = _ => { };
            UGUIShip.RegisterShine = _ => { };
            UGUIShip.RegisterFill = (img, c) => img.color = c;
            UGUIShip.DropdownPops = _ => { };
            UGUIShip.LeatherMaterial = LeatherMaterial;
            UGUIShip.Tint = () => Color.white;
            UGUIShip.AddTooltip = (go, tip) => { };
            UGUIShip.LocalizeGet = ShipLoc.Get;
            UGUIShip.SetFontByCode("tmp_default", false);
        }

        private static Material LeatherMaterial()
        {
            if (_leather != null) return _leather;
            var bundle = Bundle("bettrfg_hub_leather", "BetterFG.Creator.bettrfg_hub_leather");
            if (bundle == null) { Debug.LogWarning("leather bundle wouldn't load, hub falls back to flat delux"); return null; }
            foreach (var m in bundle.LoadAllAssets<Material>())
                if (m.name.ToLowerInvariant() == "bettrfg_ui_leather") _leather = m;
            return _leather;
        }

        public ShipHost(float w, float h, float scale, Color clear)
        {
            Wire();
            _scene = EditorSceneManager.NewPreviewScene();

            _camGo = new GameObject("ShipCam");
            SceneManager.MoveGameObjectToScene(_camGo, _scene);
            Camera = _camGo.AddComponent<Camera>();
            Camera.enabled = false;
            Camera.scene = _scene;
            Camera.orthographic = true;
            Camera.clearFlags = CameraClearFlags.SolidColor;
            Camera.backgroundColor = clear;
            Camera.nearClipPlane = 0.1f;
            Camera.farClipPlane = 100f;
            Camera.cullingMask = ~0;

            _esGo = new GameObject("ShipEvents");
            SceneManager.MoveGameObjectToScene(_esGo, _scene);
            Events = _esGo.AddComponent<EventSystem>();
            Events.sendNavigationEvents = false;
            typeof(EventSystem).GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(Events, null);
            EventSystem.current = Events;

            _canvasGo = new GameObject("ShipCanvas", typeof(RectTransform));
            SceneManager.MoveGameObjectToScene(_canvasGo, _scene);
            Canvas = _canvasGo.AddComponent<Canvas>();
            Canvas.renderMode = RenderMode.WorldSpace;
            Canvas.worldCamera = Camera;
            Canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1;
            _canvasGo.AddComponent<CanvasScaler>();
            _canvasGo.AddComponent<GraphicRaycaster>();

            var rootGo = new GameObject("Root", typeof(RectTransform));
            rootGo.transform.SetParent(_canvasGo.transform, false);
            Root = (RectTransform)rootGo.transform;
            Root.anchorMin = Vector2.zero;
            Root.anchorMax = Vector2.one;
            Root.offsetMin = Root.offsetMax = Vector2.zero;

            Resize(w, h, scale);
            _last = EditorApplication.timeSinceStartup;
        }

        public void Resize(float w, float h, float scale)
        {
            w = Mathf.Max(w, 16f);
            h = Mathf.Max(h, 16f);
            Width = w; Height = h; Scale = scale;
            int pw = Mathf.CeilToInt(w * scale), ph = Mathf.CeilToInt(h * scale);

            var crt = (RectTransform)_canvasGo.transform;
            crt.pivot = Vector2.zero;
            crt.sizeDelta = new Vector2(w, h);
            crt.localScale = new Vector3(scale, scale, 1f);
            crt.position = Vector3.zero;
            _canvasGo.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = scale;

            Camera.orthographicSize = ph * 0.5f;
            Camera.aspect = (float)pw / ph;
            Camera.transform.position = new Vector3(pw * 0.5f, ph * 0.5f, -10f);

            if (Texture != null && Texture.width == pw && Texture.height == ph) return;
            if (Texture != null) { Camera.targetTexture = null; Texture.Release(); UnityEngine.Object.DestroyImmediate(Texture); }
            Texture = new RenderTexture(pw, ph, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = "ShipHub", hideFlags = HideFlags.HideAndDontSave, antiAliasing = 1 };
            Texture.Create();
            Camera.targetTexture = Texture;
        }

        public void Clear()
        {
            for (int i = Root.childCount - 1; i >= 0; i--)
                UnityEngine.Object.DestroyImmediate(Root.GetChild(i).gameObject);
            Events.SetSelectedGameObject(null);
            _anims.Clear();
        }

        public void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            float dt = Mathf.Clamp((float)(now - _last), 0f, 0.1f);
            _last = now;
            BettrFG.uGUI.Time.unscaledDeltaTime = BettrFG.uGUI.Time.deltaTime = dt;
            BettrFG.uGUI.Time.unscaledTime = BettrFG.uGUI.Time.time = (float)now;
            BettrFG.uGUI.Time.frameCount++;

            UGUIShip.PumpTextMirrors();
            UGUIShip.TickLeather();
            BettrFG.uGUI.Input.Down = BettrFG.uGUI.Input.Up = false;
            TickAnimations(dt);
        }

        private void TickAnimations(float dt)
        {
            var live = Root.GetComponentsInChildren<Animation>(false);
            var seen = new HashSet<Animation>();
            foreach (var a in live)
            {
                seen.Add(a);
                if (!_anims.TryGetValue(a, out float t)) t = 0f;
                var clip = a.clip;
                if (clip == null || t > clip.length) { _anims[a] = t; continue; }
                t += dt;
                _anims[a] = t;
                clip.SampleAnimation(a.gameObject, Mathf.Min(t, clip.length));
            }
            var gone = new List<Animation>();
            foreach (var k in _anims.Keys) if (!seen.Contains(k)) gone.Add(k);
            foreach (var k in gone) _anims.Remove(k);
        }

        public bool Animating
        {
            get
            {
                foreach (var kv in _anims)
                    if (kv.Key != null && kv.Key.clip != null && kv.Value <= kv.Key.clip.length) return true;
                return false;
            }
        }

        public void Render()
        {
            Canvas.ForceUpdateCanvases();
            Camera.Render();
        }

        public void Dispose()
        {
            typeof(EventSystem).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(Events, null);
            if (Texture != null) { Camera.targetTexture = null; Texture.Release(); UnityEngine.Object.DestroyImmediate(Texture); }
            EditorSceneManager.ClosePreviewScene(_scene);
        }
    }
}
