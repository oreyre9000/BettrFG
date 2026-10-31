using System;
using System.Collections.Generic;
using BetterFG.Utilities;
using FG.Common;
using LevelEditor;
using Levels.Obstacles;
using UnityEngine;

namespace BetterFG.Features.CreativeExpansion
{
    internal static class CarryLooks
    {
        private const string SpawnerPrefix = "Placeable_Obstacle_SpawnBasket_";
        private const string LookName = "BettrFG_CarryLook";

        private sealed class Item
        {
            public CarryObject Carry;
            public string Key;
            public Renderer[] Hidden;
            public GameObject Look;
        }

        private static readonly Dictionary<int, (Transform Root, string Key)> _spawners = new Dictionary<int, (Transform, string)>();
        private static readonly Dictionary<int, Item> _items = new Dictionary<int, Item>();

        internal static void Apply(Transform spawner, string key)
        {
            Prune();
            _spawners[spawner.gameObject.GetInstanceID()] = (spawner, key);
            foreach (var co in ItemsOf(spawner)) Style(co, key);
            foreach (var item in new List<Item>(_items.Values))
                if (SpawnerOf(item.Carry.transform.position) == spawner) Style(item.Carry, key);
        }

        internal static void OnSpawned(CarryObject co)
        {
            var col = LevelEditorPlaceableObject.Collection;
            if (col == null || col.Count == 0) return;
            Prune();
            var spawner = SpawnerOf(co.transform.position);
            string key = spawner != null && _spawners.TryGetValue(spawner.gameObject.GetInstanceID(), out var s) ? s.Key : null;
            Style(co, key);
        }

        internal static void Tick()
        {
            if (_spawners.Count == 0) return;
            Prune();
            foreach (var item in _items.Values)
                if (item.Key != null && !item.Carry.gameObject.activeInHierarchy) Unstyle(item);
            foreach (var s in _spawners.Values)
                foreach (var co in ItemsOf(s.Root)) Style(co, s.Key);
        }

        private static void Prune()
        {
            List<int> dead = null;
            foreach (var kv in _items) if (kv.Value.Carry == null) (dead ?? (dead = new List<int>())).Add(kv.Key);
            if (dead != null) foreach (int id in dead) _items.Remove(id);
            dead = null;
            foreach (var kv in _spawners) if (kv.Value.Root == null) (dead ?? (dead = new List<int>())).Add(kv.Key);
            if (dead != null) foreach (int id in dead) _spawners.Remove(id);
        }

        private static List<CarryObject> ItemsOf(Transform spawner)
        {
            var list = new List<CarryObject>();
            var basket = spawner.GetComponentInChildren<COMMON_SpawnBasket>(true);
            var gos = basket != null ? basket._spawnedItemGOs : null;
            if (gos == null) return list;
            for (int i = 0; i < gos.Count; i++)
            {
                var co = gos[i] != null ? gos[i].GetComponentInChildren<CarryObject>(true) : null;
                if (co != null) list.Add(co);
            }
            return list;
        }

        private static Transform SpawnerOf(Vector3 pos)
        {
            var col = LevelEditorPlaceableObject.Collection;
            if (col == null) return null;
            Transform best = null;
            float bestSq = float.MaxValue;
            for (int i = 0; i < col.Count; i++)
            {
                var lepo = col[i];
                if (lepo == null || !lepo.name.StartsWith(SpawnerPrefix, StringComparison.Ordinal)) continue;
                float d = (lepo.transform.position - pos).sqrMagnitude;
                if (d >= bestSq) continue;
                bestSq = d;
                best = lepo.transform;
            }
            return best;
        }

        private static void Style(CarryObject co, string key)
        {
            int id = co.gameObject.GetInstanceID();
            if (!_items.TryGetValue(id, out var item)) _items[id] = item = new Item { Carry = co };
            if (item.Key == key && (key == null || item.Look != null)) return;
            Unstyle(item);
            if (key == null) return;

            var prefab = AssetLoads.Get<GameObject>(key);
            if (prefab == null) return;

            var toItem = co.transform.worldToLocalMatrix;
            var stock = new List<Renderer>();
            var have = new Bounds();
            bool any = false;
            foreach (var r in co.GetComponentsInChildren<Renderer>(true))
            {
                if (!r.enabled) continue;
                var mesh = MeshOf(r);
                if (mesh == null) continue;
                stock.Add(r);
                Grow(ref have, ref any, mesh.bounds, toItem * r.transform.localToWorldMatrix);
            }
            if (!any)
            {
                Plugin.Log.LogWarning($"carry look: {co.name} has nothing visible to swap out");
                return;
            }

            var look = new GameObject(LookName);
            look.transform.SetParent(co.transform, false);
            var art = new GameObject("Art").transform;
            art.SetParent(look.transform, false);

            var fromPrefab = prefab.transform.worldToLocalMatrix;
            var want = new Bounds();
            bool got = false;
            foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
            {
                if (!r.enabled || !r.gameObject.activeSelf || r.name.Contains("_COL") || (r.name.Contains("LOD") && !r.name.Contains("LOD0"))) continue;
                var mesh = MeshOf(r);
                if (mesh == null) continue;
                var m = fromPrefab * r.transform.localToWorldMatrix;
                var part = new GameObject(r.name);
                part.transform.SetParent(art, false);
                part.transform.localPosition = m.GetColumn(3);
                part.transform.localRotation = m.rotation;
                part.transform.localScale = m.lossyScale;
                part.AddComponent<MeshFilter>().sharedMesh = mesh;
                part.AddComponent<MeshRenderer>().sharedMaterials = r.sharedMaterials;
                Grow(ref want, ref got, mesh.bounds, m);
            }
            if (!got)
            {
                UnityEngine.Object.DestroyImmediate(look);
                Plugin.Log.LogWarning($"carry look {key} loaded but there's no mesh in it");
                return;
            }

            float scale = Mathf.Max(have.size.x, have.size.y, have.size.z) / Mathf.Max(want.size.x, want.size.y, want.size.z, 0.0001f);
            art.localScale = Vector3.one * scale;
            art.localPosition = have.center - want.center * scale;

            foreach (var r in stock) r.forceRenderingOff = true;
            item.Hidden = stock.ToArray();
            item.Look = look;
            item.Key = key;
        }

        private static void Unstyle(Item item)
        {
            if (item.Look != null) UnityEngine.Object.DestroyImmediate(item.Look);
            if (item.Hidden != null) foreach (var r in item.Hidden) if (r != null) r.forceRenderingOff = false;
            item.Look = null;
            item.Hidden = null;
            item.Key = null;
        }

        private static Mesh MeshOf(Renderer r)
        {
            var skinned = r.TryCast<SkinnedMeshRenderer>();
            if (skinned != null) return skinned.sharedMesh;
            if (r.TryCast<MeshRenderer>() == null) return null;
            var filter = r.GetComponent<MeshFilter>();
            return filter != null ? filter.sharedMesh : null;
        }

        private static void Grow(ref Bounds b, ref bool any, Bounds local, Matrix4x4 m)
        {
            var c = local.center;
            var e = local.extents;
            for (int i = 0; i < 8; i++)
            {
                var p = m.MultiplyPoint3x4(c + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z));
                if (!any) { b = new Bounds(p, Vector3.zero); any = true; }
                else b.Encapsulate(p);
            }
        }
    }
}
