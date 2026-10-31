using System.Collections.Generic;
using BetterFG.Customization.TexturePacks;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Levels.Obstacles;
using UnityEngine;

namespace BetterFG.Features.CreativeExpansion
{
    internal sealed class IceTileLook
    {
        public string[] Meshes;
        public string Material = "eed195f5d7c98004e919280323ff4dd3";
        public string Normal;
        public float RimPower;
        public float Shade = 1f;
    }

    internal static class RespawnTileArt
    {
        private const float Neutral = 0.8f;
        private const int CellSize = 1024;

        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int MetalTexId = Shader.PropertyToID("_MetallicSmoothness");
        private static readonly int NormalTexId = Shader.PropertyToID("_BumpMap");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        private static readonly int RimPowerId = Shader.PropertyToID("_RimPower");
        private static readonly int NormalMapId = Shader.PropertyToID("_NormalMap");

        private static readonly Dictionary<long, Material[]> _looks = new Dictionary<long, Material[]>();
        private static readonly Dictionary<int, long> _seen = new Dictionary<int, long>();
        private static readonly Dictionary<int, Color> _iced = new Dictionary<int, Color>();

        internal static void ApplyIce(Transform root, Mesh[] meshes, Material material, Texture normal, IceTileLook look, bool force)
        {
            var compound = root.GetComponent<LevelEditorCompoundSurvivalFallingPlatformsTiles>();
            if (compound == null) return;
            var tiles = compound._platforms;
            if (tiles == null) return;

            int id = root.gameObject.GetInstanceID();
            long sig = tiles.Count == 0 ? 0 : tiles.Count * 1000003L ^ tiles[0].Pointer.ToInt64();
            if (!force && _seen.TryGetValue(id, out long last) && last == sig) return;
            _seen[id] = sig;
            if (tiles.Count == 0) return;

            var stockPos = compound._objectToSpawn.GetComponentInChildren<COMMON_RespawningTile>(true)._meshRenderer.transform.localPosition;
            var pool = new Il2CppReferenceArray<Mesh>(meshes);
            var colour = (material.GetColor(ColorId).linear * look.Shade).gamma;
            colour.a = 1f;

            for (int i = 0; i < tiles.Count; i++)
            {
                var tile = tiles[i]?.TryCast<COMMON_RespawningTile>();
                if (tile == null) continue;
                var renderer = tile._meshRenderer;

                tile._useRandomMesh = true;
                tile._randomMesh = pool;
                tile._meshFilter.sharedMesh = meshes[(int)(((uint)i * 2654435761u) >> 16) % meshes.Length];
                renderer.sharedMaterial = material;
                renderer.transform.localPosition = stockPos;
                tile._originalVisualLocalPos = stockPos;

                if (!_iced.ContainsKey(tile.GetInstanceID())) _iced[tile.GetInstanceID()] = tile._visualsColour;
                tile._visualsColour = colour;
                var block = tile._visualsMaterialPropertyBlock ?? new MaterialPropertyBlock();
                tile._visualsMaterialPropertyBlock = block;
                block.SetColor(ColorId, colour);
                block.SetFloat(RimPowerId, look.RimPower);
                block.SetTexture(NormalMapId, normal);
                renderer.SetPropertyBlock(block);
            }
            Plugin.Log.LogInfo($"hex tiles under {root.name}: {tiles.Count} iced over, rim {look.RimPower}");
        }

        private static void Thaw(COMMON_RespawningTile tile)
        {
            if (!_iced.Remove(tile.GetInstanceID(), out var applied)) return;
            tile._visualsColour = applied;
            var old = tile._visualsMaterialPropertyBlock;
            var block = new MaterialPropertyBlock();
            block.SetColor(ColorId, applied);
            if (old != null && old.HasColor(EmissionId)) block.SetColor(EmissionId, old.GetColor(EmissionId));
            tile._visualsMaterialPropertyBlock = block;
            tile._meshRenderer.SetPropertyBlock(block);
        }

        internal static void Apply(Transform root, Mesh mesh, Material material, int atlas, bool grey, Vector3 lift, bool force)
        {
            var compound = root.GetComponent<LevelEditorCompoundSurvivalFallingPlatformsTiles>();
            if (compound == null) return;
            var tiles = compound._platforms;
            if (tiles == null) return;

            int id = root.gameObject.GetInstanceID();
            long sig = tiles.Count == 0 ? 0 : tiles.Count * 1000003L ^ tiles[0].Pointer.ToInt64();
            if (!force && _seen.TryGetValue(id, out long last) && last == sig) return;
            _seen[id] = sig;
            if (tiles.Count == 0) return;

            var stock = compound._objectToSpawn.GetComponentInChildren<COMMON_RespawningTile>(true);
            var stockMeshes = stock._randomMesh;
            var stockMaterial = stock._meshRenderer.sharedMaterial;
            var stockPos = stock._meshRenderer.transform.localPosition;
            var looks = mesh != null ? LooksOf(material, atlas, grey) : new[] { stockMaterial };
            var single = mesh != null ? new Il2CppReferenceArray<Mesh>(new[] { mesh }) : null;

            int done = 0;
            for (int i = 0; i < tiles.Count; i++)
            {
                var tile = tiles[i]?.TryCast<COMMON_RespawningTile>();
                if (tile == null) continue;
                Thaw(tile);
                var filter = tile._meshFilter;
                var renderer = tile._meshRenderer;

                if (mesh == null)
                {
                    tile._useRandomMesh = stock._useRandomMesh;
                    tile._randomMesh = stockMeshes;
                    if (!Holds(stockMeshes, filter.sharedMesh))
                        filter.sharedMesh = stockMeshes[Random.Range(0, stockMeshes.Length)];
                }
                else
                {
                    tile._useRandomMesh = true;
                    tile._randomMesh = single;
                    filter.sharedMesh = mesh;
                }

                int pick = looks.Length > 1 ? (int)(((uint)i * 2654435761u) >> 16) % looks.Length : 0;
                renderer.sharedMaterial = looks[pick];
                renderer.transform.localPosition = stockPos + lift;
                tile._originalVisualLocalPos = stockPos + lift;
                done++;
            }
            Plugin.Log.LogInfo($"hex tiles under {root.name}: {done} {(mesh != null ? "now " + mesh.name : "back to stock")}");
        }

        private static bool Holds(Il2CppReferenceArray<Mesh> meshes, Mesh mesh)
        {
            if (mesh == null) return false;
            for (int i = 0; i < meshes.Length; i++)
                if (meshes[i] != null && meshes[i].Pointer == mesh.Pointer) return true;
            return false;
        }

        private static Material[] LooksOf(Material src, int atlas, bool grey)
        {
            if (atlas <= 1 && !grey) return new[] { src };

            long key = src.GetInstanceID() * 31L + atlas * 2 + (grey ? 1 : 0);
            if (_looks.TryGetValue(key, out var hit) && hit[0] != null) return hit;

            int cells = Mathf.Max(1, atlas);
            float span = 1f / cells;
            var albedo = src.GetTexture(MainTexId);
            var metal = src.GetTexture(MetalTexId);
            var normal = src.GetTexture(NormalTexId);
            int size = Mathf.Min(albedo.width / cells, CellSize);

            var looks = new Material[cells];
            for (int c = 0; c < cells; c++)
            {
                var rect = new Rect(c * span, 0f, span, span);
                var mat = new Material(src) { name = $"BettrFG_{src.name}_{c}", hideFlags = HideFlags.HideAndDontSave };
                mat.SetTexture(MainTexId, grey ? GreyCell(albedo, rect, size) : Crop(albedo, rect, size, false));
                if (atlas > 1)
                {
                    if (metal != null) mat.SetTexture(MetalTexId, Crop(metal, rect, Mathf.Min(metal.width / cells, CellSize), true));
                    if (normal != null) mat.SetTexture(NormalTexId, Crop(normal, rect, Mathf.Min(normal.width / cells, CellSize), true));
                }
                looks[c] = mat;
            }
            _looks[key] = looks;
            Plugin.Log.LogInfo($"{src.name}: cut {cells} tile look(s){(grey ? ", greyed for the colour picker" : "")}");
            return looks;
        }

        private static Texture2D Crop(Texture src, Rect rect, int size, bool linear, bool readable = false)
        {
            var prev = RenderTexture.active;
            var rt = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32,
                linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB);
            Graphics.Blit(src, rt, rect.size, rect.position);
            RenderTexture.active = rt;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, !readable, linear)
            {
                name = "bfg_cell_" + src.name,
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = src.filterMode,
            };
            tex.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            tex.Apply(!readable, !readable);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return tex;
        }

        private static Texture2D GreyCell(Texture src, Rect rect, int size)
        {
            var read = Crop(src, rect, size, false, true);
            var rgba = TexturePackProject.Raw(read);

            var counts = new int[32768];
            int best = 0;
            for (int i = 0, n = size * size; i < n; i++)
            {
                int o = i * 4;
                int bin = (rgba[o] >> 3) << 10 | (rgba[o + 1] >> 3) << 5 | rgba[o + 2] >> 3;
                if (++counts[bin] > counts[best]) best = bin;
            }
            float kr = ((best >> 10) & 31) / 31f, kg = ((best >> 5) & 31) / 31f, kb = (best & 31) / 31f;
            float key = Mathf.Max(0.299f * kr + 0.587f * kg + 0.114f * kb, 0.06f);

            for (int i = 0, n = size * size; i < n; i++)
            {
                int o = i * 4;
                float l = (0.299f * rgba[o] + 0.587f * rgba[o + 1] + 0.114f * rgba[o + 2]) / 255f;
                byte v = (byte)(Mathf.Clamp01(Neutral * l / key) * 255f);
                rgba[o] = v;
                rgba[o + 1] = v;
                rgba[o + 2] = v;
            }
            TexturePackProject.Upload(read, rgba);

            var baked = TexturePackProject.Blit(read, size, size, true, false);
            Object.Destroy(read);
            baked.name = "bfg_grey_" + src.name;
            baked.wrapMode = TextureWrapMode.Clamp;
            baked.filterMode = src.filterMode;
            baked.hideFlags = HideFlags.HideAndDontSave;
            return baked;
        }
    }
}
