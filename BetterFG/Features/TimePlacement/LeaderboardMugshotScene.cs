using UnityEngine;
using UnityEngine.Rendering;

namespace BetterFG.Features.TimePlacement
{
    internal static class LeaderboardMugshotScene
    {
        public const int Width = 88;
        public const int Height = 64;
        public const int Layer = 31;

        public const float HeadDrop = 0.24f;
        public const float HeadSize = 0.30f;
        public const float Distance = 8f;

        // the bean's body shader writes alpha 0, so a transparent target renders a floating costume
        // with an invisible player in it. two shots on these backgrounds, and the difference is the mask
        public static readonly Color MaskA = Color.black;
        public static readonly Color MaskB = Color.white;

        public static readonly Color Ambient = new Color(0.17f, 0.17f, 0.2f);

        // NOT real alpha - per the note above, the body shader's forced alpha 0 means an ARGB
        // target with real transparency drops the whole body, leaving only whatever else renders
        // real alpha (a bare-bean preview like EyePreview's ends up showing nothing but the eye
        // overlay). RGB565 has no alpha channel at all, so it's immune to that lie by construction -
        // this is a solid, opaque backdrop meant to be colour-keyed out downstream, not composited
        // with real transparency. Real transparency needs the two-shot MaskA/MaskB diff above.
        public static readonly Color KeyBackdrop = new Color(1f, 239f / 255f, 246f / 255f, 1f);

        public static readonly Vector3 KeyAngles = new Vector3(40f, -30f, 0f);
        public static readonly Color KeyColour = new Color(1f, 0.97f, 0.9f);
        public const float KeyIntensity = 0.8f;

        public static readonly Vector3 FillAngles = new Vector3(18f, 58f, 0f);
        public static readonly Color FillColour = new Color(0.74f, 0.82f, 1f);
        public const float FillIntensity = 0.3f;

        public static readonly Vector3 RimAngles = new Vector3(12f, 165f, 0f);
        public static readonly Color RimColour = new Color(1f, 0.94f, 0.82f);
        public const float RimIntensity = 0.55f;

        public static Camera BuildCamera(GameObject host, out Light[] lights)
        {
            var cam = host.AddComponent<Camera>();
            cam.enabled = false;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.orthographic = true;
            cam.aspect = (float)Width / Height;
            cam.cullingMask = 1 << Layer;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = Distance * 4f;
            cam.allowHDR = false;
            cam.allowMSAA = false;
            cam.useOcclusionCulling = false;

            lights = new Light[]
            {
                AddLight(host.transform, "Key", KeyAngles, KeyColour, KeyIntensity),
                AddLight(host.transform, "Fill", FillAngles, FillColour, FillIntensity),
                AddLight(host.transform, "Rim", RimAngles, RimColour, RimIntensity),
            };
            return cam;
        }

        // off until PushLighting arms them for a single bean's two renders — they'd otherwise sit lit
        // across the frames the capture coroutine yields on
        static Light AddLight(Transform camT, string name, Vector3 angles, Color colour, float intensity)
        {
            var go = new GameObject(name);
            go.transform.SetParent(camT, false);
            go.transform.localRotation = Quaternion.Euler(angles);
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.cullingMask = 1 << Layer;
            light.color = colour;
            light.intensity = intensity;
            light.shadows = LightShadows.None;
            light.enabled = false;
            return light;
        }

        // yawForward is the bean's facing flattened to yaw so a mid-drop tilt doesn't tip the portrait over
        public static void FrameHead(Camera cam, Bounds body, Vector3 yawForward)
        {
            float h = Mathf.Max(0.01f, body.size.y);
            var head = new Vector3(body.center.x, body.max.y - h * HeadDrop, body.center.z);
            cam.transform.position = head + yawForward * Distance;
            cam.transform.rotation = Quaternion.LookRotation(-yawForward, Vector3.up);
            cam.orthographicSize = h * HeadSize;
        }

        // same rig as FrameHead, framed on the whole bean instead of just the head
        public static void FrameBody(Camera cam, Bounds body, Vector3 yawForward, float margin = 1.1f)
        {
            float h = Mathf.Max(0.01f, body.size.y);
            cam.transform.position = body.center + yawForward * Distance;
            cam.transform.rotation = Quaternion.LookRotation(-yawForward, Vector3.up);
            cam.orthographicSize = h * 0.5f * margin;
        }

        static bool _fog;
        static AmbientMode _ambientMode;
        static Color _ambientColour;

        public static void PushLighting(Light[] lights)
        {
            _fog = RenderSettings.fog;
            _ambientMode = RenderSettings.ambientMode;
            _ambientColour = RenderSettings.ambientLight;
            RenderSettings.fog = false;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Ambient;
            foreach (var l in lights) l.enabled = true;
        }

        public static void PopLighting(Light[] lights)
        {
            foreach (var l in lights) l.enabled = false;
            RenderSettings.fog = _fog;
            RenderSettings.ambientMode = _ambientMode;
            RenderSettings.ambientLight = _ambientColour;
        }

        // black-bg and white-bg shots of the same alpha-0 bean: alpha = how little the backdrop
        // bled through, and the black shot is premultiplied by it so divide it back out. mutates
        // `black` in place into the straight-alpha cutout. same trick BeanPortraits/PetThumb use.
        public static void AlphaFromAB(Color[] black, Color[] white)
        {
            for (int i = 0; i < black.Length; i++)
            {
                Color ca = black[i], cb = white[i];
                float diff = (Mathf.Abs(cb.r - ca.r) + Mathf.Abs(cb.g - ca.g) + Mathf.Abs(cb.b - ca.b)) / 3f;
                float alpha = Mathf.Clamp01(1f - diff);
                black[i] = alpha <= 0.02f
                    ? new Color(0f, 0f, 0f, 0f)
                    : new Color(ca.r / alpha, ca.g / alpha, ca.b / alpha, alpha);
            }
        }
    }
}
