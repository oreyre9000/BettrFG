using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BetterFG.Customization.TexturePacks;
using BetterFG.Services;
using BetterFG.UI.Windows.Creative;
using BetterFG.Utilities;
using FG.Common;
using FG.Common.LevelEditor.Serialization;
using LevelEditor;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace BetterFG.Features.CreativeExpansion
{
    internal static class Foliage
    {
        internal static bool Enabled => FeatureCreativeContent.On("customobjects");

        private static readonly MarkerKind Kind = MarkerKinds.Foliage;
        internal static string MarkerName => Kind.Prefab;
        private static Vector3 IdentityRotation => Kind.Rotation;
        private const float Carrier = 0.01f;
        internal const float MinSize = 0.1f;
        internal const float MaxSize = 10f;
        private const string VisualName = "BettrFG_Foliage";
        private const string PlantMaterial = "0fd7ee1761f6cfb4db9c24d14f7ea2ab";
        private const string VineMaterial = "2fc1cc7be86bb004d88d502dd9ed39dd";
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private const int BakeSize = 1024;

        internal sealed class Item
        {
            public string Prefab;
            public string Mesh;
            public string Material;
            public float Scale;
            public bool Fixed;
            public string Group = "BettrFGJungleFoliage";
        }

        private sealed class Look
        {
            public Texture2D White;
            public Color Default;
        }

        internal static readonly Item[] All =
        {
            new Item { Prefab = "604a3604a92d1ea458a069ab70d0653d", Mesh = "ENV_prp_jungleplant_1_var_1_MO", Material = PlantMaterial, Scale = 2f },
            new Item { Prefab = "8f63d2969ab8f4749a225045f7017d6b", Mesh = "ENV_prp_jungleplant_1_var_2_MO", Material = PlantMaterial, Scale = 2f },
            new Item { Prefab = "75f8b7112fb50644eb83420ea3e9c0e8", Mesh = "ENV_prp_jungleplant_2_var_1_MO", Material = PlantMaterial, Scale = 10f },
            new Item { Prefab = "dc253866b384ba042a08419a1e14378b", Mesh = "ENV_prp_jungleplant_2_var_2_MO", Material = PlantMaterial, Scale = 10f },
            new Item { Prefab = "b01101db0bd9e734da9829c86802078f", Mesh = "ENV_prp_jungleplant_2_var_3_MO", Material = PlantMaterial, Scale = 10f },
            new Item { Prefab = "a40b6716f36ba074d91cf22dacea544a", Mesh = "ENV_prp_jungleplant_2_var_4_MO", Material = PlantMaterial, Scale = 5f },
            new Item { Prefab = "e4a77383437b5ab40a126cbaf61082d7", Mesh = "ENV_prp_jungleplant_2_var_5_MO", Material = PlantMaterial, Scale = 5f },
            new Item { Prefab = "430eea92b3531a245b414ca3464f448d", Mesh = "ENV_prp_jungleplant_2_var_6_MO", Material = PlantMaterial, Scale = 5f },
            new Item { Prefab = "360c07bd181ea8941b9ac388f45b5340", Mesh = "ENV_S05_Vines_01_MO", Material = VineMaterial, Scale = 1.5f },
            new Item { Prefab = "11745cb28201b3747aeaf3c5b6b51878", Mesh = "ENV_S05_Vines_02_MO", Material = VineMaterial, Scale = 1.5f },
            new Item { Prefab = "d3a34768a9488fa4ebdfeecfcf625c85", Mesh = "ENV_S05_Vines_03_MO", Material = VineMaterial, Scale = 1.5f },
            new Item { Prefab = "a0d5028f70620e048b6889b9413b9a6c", Mesh = "ENV_S05_Vines_04_MO", Material = VineMaterial, Scale = 1.5f },
            new Item { Prefab = "ddae23d24ada8ac439a063520d6ed096", Mesh = "ENV_S05_Vines_Knot01_MO", Material = VineMaterial, Scale = 1.5f },
            new Item { Prefab = "3cb60a009de6f4f429bf804f950873dd", Mesh = "ENV_S05_Vines_Knot_02_MO", Material = VineMaterial, Scale = 1.5f },
            new Item { Prefab = "1fe250973ce47db44a9ed9d7850e9bf6", Mesh = "ENV_SBlueJay_PalmTree_MO", Scale = 0.4f, Fixed = true, Group = "BettrFGTrees" },
            new Item { Prefab = "a8dc78b9d4a2e524db1ec887245b5e14", Mesh = "ENV_Grass_01_MO", Scale = 6f, Fixed = true, Group = "BettrFGGrass" },
            new Item { Prefab = "1ed87c6792fcfbf4ca478a583ac983c2", Mesh = "ENV_Grass_02_MO", Scale = 6f, Fixed = true, Group = "BettrFGGrass" },
            new Item { Prefab = "8b90fb0a391e6374eb774b475787cfc4", Mesh = "ENV_Grass_03_MO", Scale = 6f, Fixed = true, Group = "BettrFGGrass" },
            new Item { Prefab = "2683b61e5f777a54a8b8dcf7ea9fc519", Mesh = "ENV_Grass_04_MO", Scale = 6f, Fixed = true, Group = "BettrFGGrass" },
            new Item { Prefab = "797dc09abacb70d4ab200f6ead42692b", Mesh = "ENV_Grass_05_MO", Scale = 6f, Fixed = true, Group = "BettrFGGrass" },
            new Item { Prefab = "7a11d5c3e605a104c81b496b96f5534e", Mesh = "ENV_S05_Moss_Patch_A", Scale = 1.5f, Fixed = true, Group = "BettrFGMoss" },
            new Item { Prefab = "d6d5ef17b9c5762418e9246b2b3e8d10", Mesh = "ENV_S05_Moss_Patch_B", Scale = 1.5f, Fixed = true, Group = "BettrFGMoss" },
            new Item { Prefab = "50b6cebb9a061494d81cce3e8198d733", Mesh = "ENV_S05_Moss_Patch_C", Scale = 1.5f, Fixed = true, Group = "BettrFGMoss" },
            new Item { Prefab = "27659efda0a6dcd40b2437cd4b205781", Mesh = "ENV_S05_Moss_Patch_D", Scale = 1.5f, Fixed = true, Group = "BettrFGMoss" },
            new Item { Prefab = "000e5ae39f97b164c840da86d317e14b", Mesh = "ENV_S09_Coral_Flower_Piece_01c_MO", Scale = 0.1f, Fixed = true, Group = "BettrFGCoral" },
            new Item { Prefab = "1be352b602f240e40863b208e57f10a1", Mesh = "ENV_S09_Coral_Flower_Piece_01b_MO", Scale = 0.1f, Fixed = true, Group = "BettrFGCoral" },
            new Item { Prefab = "bbc4a55b5c0c6d54b92e561719180b04", Mesh = "ENV_S09_Coral_Flower_Piece_01a_MO", Scale = 0.3f, Fixed = true, Group = "BettrFGCoral" },
            new Item { Prefab = "511786671bb11b7458c16a05e69c08a5", Mesh = "ENV_S09_Coral_Flower_Piece_01d_MO", Scale = 0.12f, Fixed = true, Group = "BettrFGCoral" },
            new Item { Prefab = "98e89625f71d51a47899ae7661b00640", Mesh = "ENV_RoundTree", Material = "e9288f79328070041a5677775ae04d38", Scale = 0.11f, Fixed = true, Group = "BettrFGTrees" },
            new Item { Prefab = "e83110dbabb6e454ca1a9c3dd1612f6e", Mesh = "ENV_RoundTree02", Material = "e9288f79328070041a5677775ae04d38", Scale = 0.11f, Fixed = true, Group = "BettrFGTrees" },
            new Item { Prefab = "7c01cf034f4ffe548a0be0184671d06c", Mesh = "ENV_RoundTree03", Material = "e9288f79328070041a5677775ae04d38", Scale = 0.11f, Fixed = true, Group = "BettrFGTrees" },
            new Item { Prefab = "b1771d8b7939c1b4c8ce3a3b3ac34111", Mesh = "ENV_S06_BG_RoundTree_A_MO", Material = "9ca503d366daeb945ac288419476c576", Scale = 0.4f, Fixed = true, Group = "BettrFGTrees" },
            new Item { Prefab = "cb36983a1882cc044862497637ffc339", Mesh = "ENV_S06_BG_RoundTree_B_MO", Material = "9ca503d366daeb945ac288419476c576", Scale = 0.4f, Fixed = true, Group = "BettrFGTrees" },
            new Item { Prefab = "982668bcbd88bcb40a8318f881af84e1", Mesh = "ENV_S06_BG_RoundTree_C_MO", Material = "9ca503d366daeb945ac288419476c576", Scale = 0.4f, Fixed = true, Group = "BettrFGTrees" },
            new Item { Prefab = "d6aca5b640a3e184fb5b78f8e0f5c1a0", Mesh = "ENV_RoundBush", Material = "e9288f79328070041a5677775ae04d38", Scale = 0.08f, Fixed = true, Group = "BettrFGBushes" },
            new Item { Prefab = "0fa9cb4d0a4268a4b8dfb503c398f98c", Mesh = "ENV_RoundBush_Blue", Material = "e9288f79328070041a5677775ae04d38", Scale = 0.08f, Fixed = true, Group = "BettrFGBushes" },
            new Item { Prefab = "5a7323c9cde41284883f1a28893698cf", Mesh = "ENV_RoundBush_Red", Material = "e9288f79328070041a5677775ae04d38", Scale = 0.08f, Fixed = true, Group = "BettrFGBushes" },
            new Item { Prefab = "ff482b10f95d983449bcfdc2f82817bc", Mesh = "ENV_S06_BG_RoundHedge_Simple_MO", Material = "9ca503d366daeb945ac288419476c576", Scale = 0.3f, Fixed = true, Group = "BettrFGBushes" },
            new Item { Prefab = "779c80e7f925c064ebb6b72d8dbc7365", Mesh = "ENV_S05_OrnamentalFlower_A_MO", Material = "a98a824b655e1154da256ac02fc1a4b2", Scale = 0.15f, Fixed = true, Group = "BettrFGBushes" },
            new Item { Prefab = "ed7643bf16a94174ea4a1e024d1aaf19", Mesh = "ENV_S05_TreeTopCluster_A_MO", Material = "a98a824b655e1154da256ac02fc1a4b2", Scale = 0.1f, Fixed = true, Group = "BettrFGBushes" },
            new Item { Prefab = "c7330ac8e758049419e2d06ac10737e5", Mesh = "ENV_S05_TreeTopCluster_B_MO", Material = "a98a824b655e1154da256ac02fc1a4b2", Scale = 0.13f, Fixed = true, Group = "BettrFGBushes" },
            new Item { Prefab = "e4be5176abb1f4a40a8a98e1d20591a6", Mesh = "ENV_RoundHedge", Material = "e9288f79328070041a5677775ae04d38", Scale = 0.12f, Fixed = true, Group = "BettrFGHedges" },
            new Item { Prefab = "9ba4df7875ae8f0459f6ef1f24cbc9b5", Mesh = "ENV_S06_BG_Roundhedge_A_MO", Material = "9ca503d366daeb945ac288419476c576", Scale = 0.45f, Fixed = true, Group = "BettrFGHedges" },
            new Item { Prefab = "b6e759785fbd5b346a36f64b4a5d1809", Mesh = "ENV_S06_BG_Roundhedge_B_MO", Material = "9ca503d366daeb945ac288419476c576", Scale = 0.45f, Fixed = true, Group = "BettrFGHedges" },
            new Item { Prefab = "6e31fdf10f71dc74ca5fafcec804803e", Mesh = "ENV_S06_BG_Roundhedge_C_MO", Material = "9ca503d366daeb945ac288419476c576", Scale = 0.45f, Fixed = true, Group = "BettrFGHedges" },
        };

        private static readonly Dictionary<string, Mesh> _pickMeshes = new Dictionary<string, Mesh>();
        private static readonly Dictionary<string, Look> _looks = new Dictionary<string, Look>();
        private static MaterialPropertyBlock _block;
        private static bool _syncing;

        internal static bool IsFoliage(LevelEditorPlaceableObject lepo)
        {
            return lepo != null && lepo.name.StartsWith(MarkerName, StringComparison.Ordinal)
                && TryDecode(ScaleOf(lepo), out _, out _) && MarkerKinds.Of(lepo) == Kind;
        }

        private static bool OtherMarker(Vector3 rotation)
        {
            foreach (var kind in MarkerKinds.All)
                if (kind != Kind && kind.Prefab == Kind.Prefab && kind.MatchesRotation(rotation)) return true;
            return false;
        }

        internal static void HideInSchema(LevelEditorPlaceableObject lepo, UGCObjectDataSchema schema)
        {
            if (!IsFoliage(lepo)) return;
            schema.VisibilityParam = new Il2CppSystem.Nullable<int>(0);
            schema.CollisionEnabled = new Il2CppSystem.Nullable<bool>(false);
        }

        private static bool TryDecode(Vector3 s, out int index, out float size)
        {
            float steps = s.y / Carrier;
            index = Mathf.Clamp(Mathf.RoundToInt(steps) - 1, 0, All.Length - 1);
            size = s.z / Carrier;
            return Mathf.Abs(s.x - Carrier) < 0.0005f && Mathf.Abs(steps - Mathf.Round(steps)) < 0.05f
                && steps > 0.5f && steps < All.Length + 0.5f && size > 0f;
        }

        private static Vector3 Encode(int index, float size)
            => new Vector3(Carrier, Carrier * (index + 1), Carrier * Mathf.Clamp(size, MinSize, MaxSize));

        private static Vector3 ScaleOf(LevelEditorPlaceableObject lepo) => lepo._levelEditorScaleParameter.CurrentScale;

        internal static int IndexOf(LevelEditorPlaceableObject lepo)
        {
            TryDecode(ScaleOf(lepo), out int index, out _);
            return index;
        }

        internal static float SizeOf(LevelEditorPlaceableObject lepo)
        {
            TryDecode(ScaleOf(lepo), out _, out float size);
            return size;
        }

        internal static bool Tintable(LevelEditorPlaceableObject lepo) => !All[IndexOf(lepo)].Fixed;

        private static string DefaultHex(int index)
            => All[index].Fixed ? "#FFFFFF" : "#" + ColorUtility.ToHtmlStringRGB(LookOf(All[index]).Default);

        internal static void SetSize(LevelEditorPlaceableObject lepo, float size)
        {
            lepo._levelEditorScaleParameter.SetScale(Encode(IndexOf(lepo), size), true);
            Sync();
        }

        internal static void PlaceAtReticle(int index)
        {
            if (!Enabled) return;
            var origin = IdentifierObjects.ReticleOrigin(LevelEditorManager.Instance);
            if (IdentifierObjects.Spawn(MarkerName, origin, IdentityRotation, Encode(index, 1f), DefaultHex(index), selectAtReticle: true) != null) Sync();
        }

        internal static void Sync()
        {
            if (_syncing) return;
            bool inEditor = IdentifierObjects.InEditor();
            if (inEditor && !Enabled) return;
            _syncing = true;
            try
            {
                bool pickable = inEditor && !Thrusters.InPlaytest();
                var handled = new HashSet<int>();
                var lepos = LevelEditorPlaceableObject.Collection;
                if (lepos != null)
                    for (int i = 0; i < lepos.Count; i++)
                    {
                        var lepo = lepos[i];
                        if (!IsFoliage(lepo)) continue;
                        handled.Add(lepo.gameObject.GetInstanceID());
                        TryDecode(ScaleOf(lepo), out int index, out float size);
                        var param = BatchTargets.GetColourParam(lepo);
                        Apply(lepo.transform, lepo, index, size * All[index].Scale, param != null ? param.CurrentColour : Color.white, pickable);
                    }

                if (inEditor) return;

                foreach (var mr in UnityEngine.Object.FindObjectsOfType<MeshRenderer>())
                {
                    if (mr == null) continue;
                    var t = mr.transform;
                    var root = t.parent != null && t.parent.name.StartsWith(MarkerName, StringComparison.Ordinal) ? t.parent : t;
                    if (!root.name.StartsWith(MarkerName, StringComparison.Ordinal)) continue;
                    if (handled.Contains(root.gameObject.GetInstanceID())) continue;

                    var colour = Color.white;
                    var scale = root.localScale;
                    foreach (var m in IdentifierObjects.ReadRound(MarkerName))
                    {
                        if ((m.Position - root.position).sqrMagnitude > 1f || OtherMarker(m.Rotation)) continue;
                        scale = m.Scale;
                        if (!string.IsNullOrEmpty(m.Hex)) ColorUtility.TryParseHtmlString(m.Hex, out colour);
                        break;
                    }
                    if (OtherMarker(root.eulerAngles) || !TryDecode(scale, out int index, out float size) || !handled.Add(root.gameObject.GetInstanceID())) continue;
                    Apply(root, null, index, size * All[index].Scale, colour, false);
                }
            }
            finally { _syncing = false; }
        }

        private static void Apply(Transform root, LevelEditorPlaceableObject lepo, int index, float size, Color colour, bool pickable)
        {
            var item = All[index];
            if (lepo != null)
            {
                var col = BatchTargets.GetCollisionParam(lepo);
                if (col != null && col._collisionEnabled) { col._collisionEnabled = false; col.ApplyCollisionParam(true); }
                BatchTargets.EditorCollisionOff(lepo);
            }

            var visual = root.Find(VisualName);
            if (visual != null && visual.GetComponent<MeshFilter>().sharedMesh.name != item.Mesh)
            {
                UnityEngine.Object.DestroyImmediate(visual.gameObject);
                visual = null;
            }
            if (visual == null)
            {
                visual = Build(root, lepo, item);
                if (visual == null) return;
            }

            var lossy = root.lossyScale;
            float world = size;
            visual.localScale = new Vector3(
                lossy.x != 0f ? world / lossy.x : world,
                lossy.y != 0f ? world / lossy.y : world,
                lossy.z != 0f ? world / lossy.z : world);
            visual.position = root.position + visual.up * (-visual.GetComponent<MeshFilter>().sharedMesh.bounds.min.y * world);

            var renderer = visual.GetComponent<MeshRenderer>();
            if (!item.Fixed) Tint(item, renderer, colour);

            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                if (r.transform != visual && !r.forceRenderingOff) r.forceRenderingOff = true;

            foreach (var c in root.GetComponentsInChildren<Collider>(true))
            {
                bool keep = pickable && c.transform.parent == visual;
                if (c.enabled != keep) c.enabled = keep;
            }
            ClearOverlaps(root);

            if (lepo != null) IdentifierObjects.OutlineWith(lepo, new[] { renderer });
        }

        private static void ClearOverlaps(Transform root)
        {
            foreach (var oc in root.GetComponentsInChildren<LevelEditorObjectCollider>(true))
                if (!oc.IsInValidPosition()) oc.ClearInvalidColliders();
        }

        internal static void DropPicks()
        {
            foreach (var c in UnityEngine.Object.FindObjectsOfType<MeshCollider>())
                if (c.name == VisualName + "Pick") c.enabled = false;
        }

        private static IntPtr _held;
        private static Transform _heldRoot;

        internal static void TickHeld()
        {
            var held = LevelEditorManager.Instance?.GetReticleBase()?.SelectedObject;
            if (held == null) { _held = IntPtr.Zero; _heldRoot = null; return; }
            if (held.Pointer != _held)
            {
                _held = held.Pointer;
                _heldRoot = IsFoliage(held) ? held.transform : null;
            }
            if (_heldRoot != null) ClearOverlaps(_heldRoot);
        }

        private static void Tint(Item item, MeshRenderer renderer, Color picked)
        {
            var look = LookOf(item);
            bool legacyWhite = picked.r > 0.999f && picked.g > 0.999f && picked.b > 0.999f;
            var colour = legacyWhite ? look.Default : picked;
            if (_block == null) _block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(_block);
            _block.SetColor(ColorId, new Color(colour.r, colour.g, colour.b, 1f));
            if (look.White != null) _block.SetTexture(MainTexId, look.White);
            renderer.SetPropertyBlock(_block);
        }

        private static Look LookOf(Item item)
        {
            if (_looks.TryGetValue(item.Material, out var look)) return look;
            look = new Look { Default = Color.white };
            _looks[item.Material] = look;

            var material = Load<Material>(item.Material);
            if (material == null)
            {
                _looks.Remove(item.Material);
                return look;
            }
            var tint = material.GetColor(ColorId);
            var src = material.HasProperty(MainTexId) ? material.GetTexture(MainTexId) : null;
            if (src == null)
            {
                look.Default = tint;
                return look;
            }

            var toLinear = new float[256];
            for (int i = 0; i < 256; i++) toLinear[i] = Mathf.GammaToLinearSpace(i / 255f);

            var read = TexturePackProject.Blit(src, BakeSize, BakeSize, false, true);
            var px = TexturePackProject.Raw(read);
            float max = 0f;
            for (int i = 0; i < px.Length; i += 4)
                max = Mathf.Max(max, 0.2126f * toLinear[px[i]] + 0.7152f * toLinear[px[i + 1]] + 0.0722f * toLinear[px[i + 2]]);
            if (max <= 0f) max = 1f;

            double r = 0, g = 0, b = 0, w = 0;
            for (int i = 0; i < px.Length; i += 4)
            {
                float lr = toLinear[px[i]], lg = toLinear[px[i + 1]], lb = toLinear[px[i + 2]];
                float white = (0.2126f * lr + 0.7152f * lg + 0.0722f * lb) / max;
                if (px[i + 3] >= 128) { r += lr; g += lg; b += lb; w += white; }
                byte v = (byte)Mathf.RoundToInt(Mathf.Clamp01(Mathf.LinearToGammaSpace(white)) * 255f);
                px[i] = v;
                px[i + 1] = v;
                px[i + 2] = v;
            }
            TexturePackProject.Upload(read, px);
            look.White = TexturePackProject.Blit(read, BakeSize, BakeSize, true, false);
            UnityEngine.Object.Destroy(read);

            if (w > 0)
            {
                var avg = new Color((float)(r / w), (float)(g / w), (float)(b / w));
                float top = Mathf.Max(avg.r, Mathf.Max(avg.g, avg.b));
                if (top > 1f) avg = new Color(avg.r / top, avg.g / top, avg.b / top);
                look.Default = new Color(avg.r * tint.linear.r, avg.g * tint.linear.g, avg.b * tint.linear.b).gamma;
            }
            Plugin.Log.LogInfo($"foliage {material.name}: whitened {src.name}, natural colour #{ColorUtility.ToHtmlStringRGB(look.Default)}");
            return look;
        }

        private static Transform Build(Transform root, LevelEditorPlaceableObject lepo, Item item)
        {
            var prefab = Load<GameObject>(item.Prefab);
            MeshFilter source = null;
            if (prefab != null)
                foreach (var f in prefab.GetComponentsInChildren<MeshFilter>(true))
                    if (f.sharedMesh != null && f.sharedMesh.name == item.Mesh) { source = f; break; }
            var materials = item.Material != null ? new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Material>(new[] { Load<Material>(item.Material) })
                : source != null ? source.GetComponent<MeshRenderer>().sharedMaterials : null;
            if (source == null || materials == null || materials[0] == null) return null;

            var go = new GameObject(VisualName);
            go.transform.SetParent(root, false);
            go.transform.localRotation = Quaternion.Euler(180f, 0f, 0f);
            var stock = root.GetComponentInChildren<MeshRenderer>(true);
            go.layer = stock != null ? stock.gameObject.layer : root.gameObject.layer;
            go.AddComponent<MeshFilter>().sharedMesh = source.sharedMesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = materials;
            if (lepo == null) return go.transform;

            var pickMesh = PickMesh(item.Mesh);
            if (pickMesh == null)
            {
                Plugin.Log.LogWarning($"no pick mesh shipped for {item.Mesh}, the reticle won't grab it");
                return go.transform;
            }
            var pickGo = new GameObject(VisualName + "Pick");
            pickGo.transform.SetParent(go.transform, false);
            var pick = pickGo.AddComponent<MeshCollider>();
            pick.sharedMesh = pickMesh;
            LevelEditorPlaceableObject.RegisterColliderForPlaceable(lepo, pick);
            return go.transform;
        }

        private static Mesh PickMesh(string name)
        {
            if (_pickMeshes.TryGetValue(name, out var cached)) return cached;
            Mesh mesh = null;
            using (var stream = EmbeddedResourceandUnity.Open($"BetterFG.assets.foliage.{name}.bin"))
            {
                if (stream != null)
                {
                    var reader = new BinaryReader(stream);
                    var verts = new Vector3[reader.ReadInt32()];
                    for (int i = 0; i < verts.Length; i++) verts[i] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                    var tris = new int[reader.ReadInt32()];
                    for (int i = 0; i < tris.Length; i++) tris[i] = reader.ReadInt32();
                    mesh = new Mesh { name = name + "_Pick" };
                    mesh.vertices = verts;
                    mesh.triangles = tris;
                    mesh.RecalculateBounds();
                    mesh.hideFlags = HideFlags.HideAndDontSave;
                }
            }
            _pickMeshes[name] = mesh;
            return mesh;
        }

        private static T Load<T>(string guid) where T : UnityEngine.Object => AssetLoads.Get<T>(guid);
    }
}
