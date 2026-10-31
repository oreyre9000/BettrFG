using System;
using System.Collections.Generic;
using BetterFG.Features.Replay;
using BetterFG.Utilities;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Levels.DoorDash;
using UnityEngine;

namespace BetterFG.Features.CreativeExpansion
{
    internal sealed class DoorDashLook
    {
        public string Frame;
        public string Panel;
        public string Pillar;
        public string SmallFrame;
    }

    internal static class DoorDashArt
    {
        internal static readonly DoorDashLook Beta = new DoorDashLook
        {
            Frame = "04dab449747482346b377a7a09d38506|PB_Door_A_End",
            Panel = "74373a12cb47b4f4f996e4776e50e8b3|Door_Unbreakable_Piece",
            Pillar = "420eb6248acb83f44bf5957f2d2bda36|ENV_Door_Pillar",
            SmallFrame = "e0b7df218bca6f247a961fa5af7b8003|ENV_Door_B_End",
        };

        private const string RootPrefix = "Placeable_Obstacle_CompoundDoorDash";
        private const string SlotPrefix = "PB_DoorDashDoor_LevelEditor";
        private const string RoundSlotPrefix = "PB_DOD_Door";
        private const string SmallSlotPrefix = "PB_DOD_DoorSmall";
        private const string PillarPrefix = "ENV_DOD_PillarMedium_Vanilla";
        private const string FrameName = "BettrFG_ThemeDoorFrame";
        private const string FrameTwinName = "BettrFG_ThemeDoorFrame2";
        private const string PanelName = "BettrFG_ThemeDoorPanel";
        private const string PillarName = "BettrFG_ThemeDoorPillar";
        private const string GlowName = "BettrFG_BetaGlow";
        private const string AngryClip = "VFX_DoorDash_AngryDoor";

        private static readonly Vector3 PanelInFrame = new Vector3(3.99f, 2.43f, 0.57f);
        private static readonly Vector3 SmallInFrame = new Vector3(3.07f, 2.43f, 0.51f);
        private static readonly Vector3 SmallPanelScale = new Vector3(0.76f, 0.94f, 1f);
        private static readonly Vector3 PanelLift = new Vector3(0f, 0.07f, 0f);
        private static readonly Vector3 EyesAt = new Vector3(0f, 5.4f, 0.6f);
        private static readonly Vector3 GlowAt = new Vector3(0f, 5.4f, 1.1f);
        private static readonly Color GlowRed = new Color(1f, 0.12f, 0.06f, 0.55f);
        private static readonly Quaternion Flip = Quaternion.Euler(0f, 180f, 0f);

        private sealed class Piece
        {
            public Mesh Mesh;
            public Il2CppReferenceArray<Material> Materials;
        }

        private sealed class FrameSet
        {
            public Piece Medium;
            public Piece Small;
            public bool All;
        }

        private static readonly Dictionary<string, Piece> _pieces = new Dictionary<string, Piece>();
        private static readonly Dictionary<int, FrameSet> _frames = new Dictionary<int, FrameSet>();
        private static readonly Dictionary<int, string> _active = new Dictionary<int, string>();
        private static readonly HashSet<int> _dressed = new HashSet<int>();
        private static Material _glow;

        private static bool IsSlot(string name) =>
            name.StartsWith(SlotPrefix, StringComparison.Ordinal) || name.StartsWith(RoundSlotPrefix, StringComparison.Ordinal);

        private static bool IsSmall(string name) => name.StartsWith(SmallSlotPrefix, StringComparison.Ordinal);

        internal static bool Apply(Transform root, DoorDashLook look, bool all = false)
        {
            var frame = PieceOf(look.Frame);
            var small = look.SmallFrame != null ? PieceOf(look.SmallFrame) : null;
            var panel = PieceOf(look.Panel);
            var pillar = PieceOf(look.Pillar);
            if (frame == null || panel == null || pillar == null) return false;

            Clear(root);
            int id = root.gameObject.GetInstanceID();
            var set = _frames[id] = new FrameSet { Medium = frame, Small = small, All = all };

            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name.StartsWith(PillarPrefix, StringComparison.Ordinal))
                {
                    Add(t, PillarName, pillar);
                    continue;
                }
                if (!IsSlot(t.name)) continue;

                Panels(t, panel);
            }

            Frames(root, set);
            var compound = root.GetComponent<CompoundDoorDashBehaviour>();
            if (compound != null) DressCopies(compound);
            return true;
        }

        internal static bool Fit(Transform root)
        {
            if (root == null || !_frames.TryGetValue(root.gameObject.GetInstanceID(), out var set)) return false;
            return Frames(root, set);
        }

        private static void Panels(Transform slot, Piece panel)
        {
            var spot = slot.Find("DoorPosition");
            if (spot == null) return;
            for (int i = 0; i < spot.childCount; i++) Panel(slot, spot.GetChild(i), panel);
        }

        private static void Panel(Transform slot, Transform leaf, Piece panel)
        {
            bool tiny = IsSmall(slot.name);
            var half = leaf.Find(tiny ? "ENV_DOD_DoorBlockSmall" : "ENV_DOD_DoorBlockMedium");
            if (half == null || half.Find(PanelName) != null) return;

            var p = Add(half, PanelName, panel);
            p.position = leaf.TransformPoint(PanelLift);
            p.rotation = leaf.rotation * Flip;
            if (tiny) p.localScale = SmallPanelScale;
        }

        internal static void HideStock(Transform root)
        {
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                string n = r.name;
                if (n.StartsWith("BettrFG", StringComparison.Ordinal) || n.StartsWith("VFX_", StringComparison.Ordinal)
                    || n.StartsWith("Obstacle_", StringComparison.Ordinal)) continue;
                r.forceRenderingOff = true;
            }
        }

        internal static void Dress(Transform slot)
        {
            if (!_dressed.Add(slot.gameObject.GetInstanceID())) return;

            HideStock(slot);
            Panels(slot, PieceOf(Beta.Panel));
            Eyes(slot);
            foreach (var pick in slot.GetComponentsInChildren<BoxCollider>(true))
                if (pick.name == ObjectThemes.PickName) pick.enabled = false;

            var controller = slot.GetComponent<FakeDoorController>();
            controller.OnRealDoorCreated.AddListener(new Action(() => Real(slot)));
            if (controller.RealDoor != null) Real(slot);
        }

        private static void Real(Transform slot)
        {
            HideStock(slot);
            Panels(slot, PieceOf(Beta.Panel));
        }

        internal static void Eyes(Transform door)
        {
            Transform fx = null, eyes = null, locator = null;
            foreach (var t in door.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "Prefab_VFX_DoorDash_Fake") fx = t;
                else if (t.name == "VFX_DoorDash_StunnedEyes") eyes = t;
                else if (t.name == "VFX_Locator") locator = t;
            }
            if (fx == null || eyes.Find(GlowName) != null) return;

            fx.GetComponent<DestroyOnAnimationEnd>().enabled = false;
            var anim = fx.GetComponent<Animation>();
            anim.playAutomatically = false;
            anim.wrapMode = WrapMode.Once;
            anim[AngryClip].wrapMode = WrapMode.Once;
            anim[AngryClip + "_NoAppear"].wrapMode = WrapMode.Once;
            float len = anim.GetClip(AngryClip).length, inf = float.PositiveInfinity;
            anim.GetClip(AngryClip).SetCurve(AngryClip, Il2CppType.Of<GameObject>(), "m_IsActive", new AnimationCurve(new[]
            {
                new Keyframe(0f, 0f, inf, inf), new Keyframe(0.03f, 1f, inf, inf),
                new Keyframe(len - 0.02f, 1f, inf, inf), new Keyframe(len, 0f, inf, inf),
            }));
            fx.Find(AngryClip).gameObject.SetActive(false);
            fx.gameObject.SetActive(true);
            anim.Stop();

            bool tiny = IsSmall(door.name);
            fx.localRotation = Quaternion.Euler(0f, 180f, 0f);
            fx.localScale = tiny ? new Vector3(0.79f, 0.79f, 0.79f) : Vector3.one;
            fx.position += door.TransformPoint(EyesAt) - eyes.TransformPoint(eyes.GetComponent<MeshFilter>().sharedMesh.bounds.center);
            if (locator != null) locator.position = door.TransformPoint(GlowAt);

            var glow = Glow();
            if (glow == null) return;
            float spread = tiny ? 1.5f : 1.9f, size = tiny ? 5.4f : 6.8f;
            var scale = eyes.lossyScale;
            foreach (float side in new[] { -spread, spread })
            {
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = GlowName;
                UnityEngine.Object.DestroyImmediate(quad.GetComponent<Collider>());
                quad.transform.SetParent(eyes, false);
                quad.transform.SetPositionAndRotation(door.TransformPoint(EyesAt + new Vector3(side, 0f, 0.15f)), door.rotation);
                quad.transform.localScale = new Vector3(size / scale.x, size / scale.y, 1f);
                var r = quad.GetComponent<MeshRenderer>();
                r.sharedMaterial = glow;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        private static Material Glow()
        {
            if (_glow != null) return _glow;
            var mat = ReplayViewer.AdditiveMaterial();
            if (mat == null) return null;
            var tex = new Texture2D(64, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    float fall = 1f - Mathf.Clamp01(Vector2.Distance(new Vector2(x, y), new Vector2(31.5f, 31.5f)) / 32f);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, fall * fall));
                }
            tex.Apply();
            mat.mainTexture = tex;
            if (mat.HasProperty("_TintColor")) mat.SetColor("_TintColor", GlowRed);
            else if (mat.HasProperty("_Color")) mat.color = GlowRed;
            mat.renderQueue = 3100;
            return _glow = mat;
        }

        private static bool Frames(Transform root, FrameSet set)
        {
            var slots = new List<Transform>();
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (IsSlot(t.name)) slots.Add(t);

            float min = float.MaxValue;
            var sig = new System.Text.StringBuilder();
            foreach (var s in slots)
            {
                if (!set.All && !s.gameObject.activeInHierarchy) continue;
                float x = root.InverseTransformPoint(s.position).x;
                min = Mathf.Min(min, x);
                sig.Append(Mathf.RoundToInt(x * 10f)).Append(',');
            }

            int id = root.gameObject.GetInstanceID();
            string key = sig.ToString();
            if (_active.TryGetValue(id, out var last) && last == key) return false;
            _active[id] = key;

            foreach (var s in slots)
            {
                Drop(s, FrameName);
                Drop(s, FrameTwinName);
                if (!set.All && !s.gameObject.activeInHierarchy) continue;
                FrameSlot(root, s, set, min);
            }
            return true;
        }

        private static void FrameSlot(Transform root, Transform s, FrameSet set, float min)
        {
            var spot = s.Find("DoorPosition");
            if (spot == null) return;
            var anchor = spot.localPosition + PanelLift;
            bool first = Mathf.Abs(root.InverseTransformPoint(s.position).x - min) < 0.1f;
            bool tiny = IsSmall(s.name) && set.Small != null;
            var frame = tiny ? set.Small : set.Medium;
            var inFrame = tiny ? SmallInFrame : PanelInFrame;

            Edge(Add(s, FrameName, frame), anchor, root.right, inFrame);
            if (first) Edge(Add(s, FrameTwinName, frame), anchor, -root.right, inFrame);
        }

        internal static void DressCopies(CompoundDoorDashBehaviour compound)
        {
            if (IdentifierObjects.InEditor() && !GravityZoneArt.InPlaytest()) return;
            var root = compound.transform;
            var copies = compound._copyDoors;
            if (copies == null || copies.Count == 0 || !_frames.TryGetValue(root.gameObject.GetInstanceID(), out var set)) return;

            float min = float.MaxValue;
            for (int i = 0; i < copies.Count; i++) min = Mathf.Min(min, root.InverseTransformPoint(copies[i].transform.position).x);
            for (int i = 0; i < copies.Count; i++)
            {
                var slot = copies[i].transform;
                if (_dressed.Contains(slot.gameObject.GetInstanceID())) continue;
                Drop(slot, FrameName);
                Drop(slot, FrameTwinName);
                FrameSlot(root, slot, set, min);
                Dress(slot);
            }
            Plugin.Log.LogInfo($"door dash: {copies.Count} beta door slots dressed in the live level");
        }

        private static void Edge(Transform f, Vector3 anchor, Vector3 outward, Vector3 inFrame)
        {
            Place(f, anchor, 1f, inFrame);
            if (Vector3.Dot(f.TransformDirection(Vector3.right), outward) < 0f) Place(f, anchor, -1f, inFrame);
        }

        private static void Place(Transform f, Vector3 anchor, float sx, Vector3 inFrame)
        {
            f.localRotation = Flip;
            f.localScale = new Vector3(sx, 1f, 1f);
            f.localPosition = anchor - Flip * new Vector3(inFrame.x * sx, inFrame.y, inFrame.z);
        }

        internal static void Clear(Transform root)
        {
            if (root == null || !root.name.StartsWith(RootPrefix, StringComparison.Ordinal)) return;
            int id = root.gameObject.GetInstanceID();
            _frames.Remove(id);
            _active.Remove(id);

            var dead = new List<GameObject>();
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == FrameName || t.name == FrameTwinName || t.name == PanelName || t.name == PillarName) dead.Add(t.gameObject);
            foreach (var go in dead) UnityEngine.Object.DestroyImmediate(go);
        }

        private static void Drop(Transform parent, string name)
        {
            var old = parent.Find(name);
            if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
        }

        private static Transform Add(Transform parent, string name, Piece piece)
        {
            Drop(parent, name);
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = piece.Mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = piece.Materials;
            return go.transform;
        }

        private static Piece PieceOf(string key)
        {
            if (_pieces.TryGetValue(key, out var hit) && hit.Mesh != null) return hit;

            var bits = key.Split('|');
            var prefab = AssetLoads.Get<GameObject>(bits[0]);
            if (prefab == null)
            {
                Plugin.Log.LogWarning($"door dash theme: {bits[0]} didn't load");
                return null;
            }
            foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null || mf.sharedMesh.name != bits[1]) continue;
                var piece = new Piece { Mesh = mf.sharedMesh, Materials = mf.GetComponent<MeshRenderer>().sharedMaterials };
                _pieces[key] = piece;
                return piece;
            }
            Plugin.Log.LogWarning($"door dash theme: no {bits[1]} inside {prefab.name}");
            return null;
        }
    }

    [HarmonyPatch(typeof(CompoundDoorDashBehaviour), nameof(CompoundDoorDashBehaviour.GenerateNewDoors))]
    internal static class DoorDashCopiesPatch
    {
        [HarmonyPostfix]
        public static void Postfix(CompoundDoorDashBehaviour __instance) => DoorDashArt.DressCopies(__instance);
    }
}
