using System;
using System.Collections.Generic;
using System.IO;
using FG.Common;
using FG.Common.LevelEditor.Serialization;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using LevelEditor;
using UnityEngine;

namespace BetterFG.Features.CreativeExpansion
{
    public static class SavedGroups
    {
        public sealed class Saved
        {
            public string Name;
            public string Json;
            public string Image;
            public Texture2D Preview;
            public bool PreviewTried;
        }

        private const int ShotSize = 256;
        private const int ShotLayer = 31;

        private static readonly List<Saved> _all = new List<Saved>();

        private static string Dir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BettrFG", "Settings", "saved_groups");

        public static List<Saved> All()
        {
            _all.Clear();

            string dir = Dir;
            if (!Directory.Exists(dir)) return _all;

            foreach (string path in Directory.GetFiles(dir, "*.json"))
            {
                string name = Path.GetFileNameWithoutExtension(path);
                _all.Add(new Saved
                {
                    Name = name,
                    Json = path,
                    Image = Path.ChangeExtension(path, ".png"),
                });
            }
            _all.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            Plugin.Log.LogInfo($"{_all.Count} saved group(s) in {dir}");
            return _all;
        }

        public static bool Exists(string name)
        {
            string clean = Sanitise(name);
            foreach (var g in All())
                if (string.Equals(g.Name, clean, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public static Texture2D PreviewOf(Saved g)
        {
            if (g.PreviewTried) return g.Preview;
            g.PreviewTried = true;

            byte[] bytes = null;
            try
            {
                string b64 = BetterFG.Utilities.JsonUtil.GetValue(File.ReadAllText(g.Json), "Image");
                if (!string.IsNullOrEmpty(b64)) bytes = Convert.FromBase64String(b64);
                // pre-embed saves left the cover art as a sidecar png next to the json
                else if (File.Exists(g.Image)) bytes = File.ReadAllBytes(g.Image);
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"preview for {g.Name} couldn't be read: {ex.Message}"); }
            if (bytes == null) return null;

            try
            {
                var tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
                if (tex.LoadImage(bytes))
                {
                    tex.wrapMode = TextureWrapMode.Clamp;
                    tex.hideFlags = HideFlags.HideAndDontSave;
                    g.Preview = tex;
                    return tex;
                }
                UnityEngine.Object.Destroy(tex);
                Plugin.Log.LogWarning($"preview for {g.Name} didn't decode, leaving it to retry next time");
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"preview for {g.Name} threw: {ex.Message}"); }
            return null;
        }

        public static int Save(string name, out string status)
        {
            var sel = LevelEditorMultiSelectionHandler.Selection();
            if (sel == null || sel.Count == 0) { status = "nothing selected"; return 0; }

            var objs = new List<LevelEditorPlaceableObject>();
            foreach (var o in sel) if (o != null) objs.Add(o);

            var schemas = new List<UGCObjectDataSchema>();
            var schemaIndex = new int[objs.Count];
            var centre = Vector3.zero;
            int noSchema = 0;
            for (int k = 0; k < objs.Count; k++)
            {
                var schema = LevelSaver.GetObjectSchema(objs[k]);
                if (schema == null || schema.Position == null || schema.Position.Length < 3) { schemaIndex[k] = -1; noSchema++; continue; }
                schemaIndex[k] = schemas.Count;
                schemas.Add(schema);
                centre += new Vector3(schema.Position[0], schema.Position[1], schema.Position[2]);
            }

            if (schemas.Count == 0)
            {
                status = "none of that could be written out";
                Plugin.Log.LogWarning($"tried to save a group of {objs.Count} object(s) and got no schemas back at all");
                return 0;
            }
            centre /= schemas.Count;

            var flat = new List<UGCObjectDataSchema>();
            foreach (var s in schemas) BetterFG.Utilities.UGCSchemaGuids.Walk(s, flat.Add);
            foreach (var node in flat)
            {
                Offset(node.Position, -centre);
                Offset(node.PainterPos, -centre);
                node.SnapStatus = new Il2CppSystem.Nullable<bool>(false);
                node.SnapTargetGuid = new Il2CppSystem.Nullable<Il2CppSystem.Guid>();
                node.SnappedWith = new Il2CppSystem.Collections.Generic.List<UGCObjectDataSchema.SnapTarget>();
                node.SnapExceptions = new Il2CppSystem.Collections.Generic.List<UGCObjectDataSchema.SnapTarget>();
            }

            var level = new UGCLevelDataSchema
            {
                Version = LevelIO.CurrentVersion.ToString(),
                LevelName = name,
                OtherObjects = new Il2CppReferenceArray<UGCObjectDataSchema>(schemas.Count),
            };
            for (int i = 0; i < schemas.Count; i++) level.OtherObjects[i] = schemas[i];

            string clean = Sanitise(name);
            string dir = Dir;
            Directory.CreateDirectory(dir);
            string jsonPath = Path.Combine(dir, clean + ".json");
            bool replacing = File.Exists(jsonPath);

            var links = new List<string>();
            for (int t = 0; t < objs.Count; t++)
            {
                if (schemaIndex[t] < 0) continue;
                var ctrl = objs[t].LinkController;
                if (ctrl == null || !LinkingSystemExtensions.IsMoverType(ctrl.GetTransmitterLinkType())) continue;
                for (int r = 0; r < objs.Count; r++)
                {
                    if (r == t || schemaIndex[r] < 0 || objs[r].LinkController == null) continue;
                    try { if (ctrl.IsReceiverLinkedTo(objs[r])) links.Add($"{schemaIndex[t]}:{schemaIndex[r]}"); }
                    catch (Exception ex) { Plugin.Log.LogWarning($"link probe {t}->{r} threw, skipping it: {ex.Message}"); }
                }
            }

            string levelText = UGCJsonSerializer.SerializeObject(level, true);
            var png = Snapshot(objs);
            string imageField = png != null ? $"\"{Convert.ToBase64String(png)}\"" : "null";
            File.WriteAllText(jsonPath, $"{{\"Links\":\"{string.Join(",", links)}\",\"Level\":{levelText},\"Image\":{imageField}}}");

            // pre-embed saves left a sidecar png next to the json — clean it up now that it's redundant
            string legacyImage = Path.Combine(dir, clean + ".png");
            if (File.Exists(legacyImage)) File.Delete(legacyImage);

            status = $"{(replacing ? "replaced" : "saved")} {clean}, {schemas.Count} object(s)";
            Plugin.Log.LogInfo($"group '{clean}' {(replacing ? "overwritten" : "saved")} — {schemas.Count} object(s), "
                + $"{flat.Count} node(s) once children/links are counted, centred on {centre}"
                + (links.Count > 0 ? $", {links.Count} controller link(s) noted" : "")
                + (noSchema > 0 ? $", {noSchema} wouldn't serialise" : "")
                + (png == null ? ", no preview (nothing renderable in there)" : $", preview {png.Length / 1024}kb"));
            return schemas.Count;
        }

        public static void Delete(Saved g)
        {
            if (File.Exists(g.Json)) File.Delete(g.Json);
            if (File.Exists(g.Image)) File.Delete(g.Image);
            if (g.Preview != null) UnityEngine.Object.Destroy(g.Preview);
            Plugin.Log.LogInfo($"binned the saved group {g.Name}");
        }

        public static int Place(Saved g, out string status, out LevelEditorPlaceableObject middle)
        {
            middle = null;
            var mgr = LevelEditorManager.Instance;
            if (mgr == null) { status = "not in the editor"; return 0; }

            string raw = File.ReadAllText(g.Json);
            string levelText = BetterFG.Utilities.JsonUtil.GetObject(raw, "Level") ?? raw;
            var level = UGCJsonSerializer.DeserializeLevelData(levelText);
            var schemas = level != null ? level.OtherObjects : null;
            if (schemas == null || schemas.Length == 0)
            {
                status = "that group file came back empty";
                Plugin.Log.LogWarning($"{g.Name}.json parsed but held no objects");
                return 0;
            }

            var reticle = mgr.GetReticleGameObject();
            var origin = reticle != null ? reticle.transform.position : Vector3.zero;

            var flat = new List<UGCObjectDataSchema>();
            for (int i = 0; i < schemas.Length; i++) BetterFG.Utilities.UGCSchemaGuids.Walk(schemas[i], flat.Add);

            BetterFG.Utilities.UGCSchemaGuids.Remap(flat);
            foreach (var node in flat)
            {
                Offset(node.Position, origin);
                Offset(node.PainterPos, origin);
            }

            var placed = new List<LevelEditorPlaceableObject>();
            var placedByIndex = new LevelEditorPlaceableObject[schemas.Length];
            int registered = 0, noLepo = 0;
            for (int i = 0; i < schemas.Length; i++)
            {
                var go = LevelLoader.LoadObject(schemas[i], false);
                if (go == null) { noLepo++; continue; }

                var lepo = go.GetComponent<LevelEditorPlaceableObject>();
                if (lepo == null) { noLepo++; continue; }
                placedByIndex[i] = lepo;

                var drawable = lepo.DrawableData;
                var shaderScale = schemas[i].ShaderScale;
                if (drawable != null && shaderScale != null && shaderScale.Length >= 3)
                    drawable.SetShaderScale(new Vector3(shaderScale[0], shaderScale[1], shaderScale[2]));

                try { lepo.OnDeselectInGameEditor(true); }
                catch (Exception ex) { Plugin.Log.LogWarning($"post-load finalise on {lepo.name} threw: {ex.Message}"); }

                var bodies = lepo.GetComponentsInChildren<Rigidbody>(true);
                for (int b = 0; b < bodies.Length; b++)
                {
                    var body = bodies[b];
                    if (body == null) continue;
                    body.position = body.transform.position;
                    body.rotation = body.transform.rotation;
                }

                if (!LevelIO.IsObjectRegistered(lepo))
                {
                    mgr.RegisterObject(lepo, true, false, false);
                    registered++;
                }
                RestoreWaypoints(lepo, schemas[i].WaypointObjects);
                placed.Add(lepo);
            }

            if (placed.Count == 0)
            {
                status = "nothing came out of that group";
                Plugin.Log.LogWarning($"{g.Name}: {schemas.Length} schema(s) in, zero objects out");
                return 0;
            }

            int relinked = Relink(BetterFG.Utilities.JsonUtil.GetValue(raw, "Links"), placedByIndex);

            var centre = Vector3.zero;
            foreach (var p in placed) centre += p.Position;
            centre /= placed.Count;
            float nearest = float.MaxValue;
            foreach (var p in placed)
            {
                float d = (p.Position - centre).sqrMagnitude;
                if (d >= nearest) continue;
                nearest = d;
                middle = p;
            }

            int group = CreativeGroups.CreateGroup(g.Name, placed);
            status = $"{placed.Count} object(s) placed";
            Plugin.Log.LogInfo($"'{g.Name}' dropped at {origin} — {placed.Count} of {schemas.Length} object(s), group {group}, "
                + (registered == 0 ? "the loader registered them all itself" : $"{registered} needed registering by hand")
                + (relinked > 0 ? $", put back {relinked} controller link(s)" : "")
                + (noLepo > 0 ? $", {noLepo} never turned into a placeable" : ""));
            return placed.Count;
        }

        private static void RestoreWaypoints(LevelEditorPlaceableObject lepo, Il2CppReferenceArray<UGCObjectDataSchema> waypoints)
        {
            if (waypoints == null || waypoints.Length == 0) return;

            var mover = lepo.GetComponentInChildren<LevelEditorMovementActiveBase>(true);
            var live = mover != null ? mover._waypointsList : null;
            if (live == null || live.Count == 0)
            {
                Plugin.Log.LogWarning($"{lepo.name} came out of the group with {waypoints.Length} waypoint(s) in its schema "
                    + "and none on the object, so its mover keeps the default sideways path");
                return;
            }

            int n = Math.Min(live.Count, waypoints.Length);
            for (int i = 0; i < n; i++)
            {
                var p = waypoints[i].Position;
                if (p == null || p.Length < 3 || live[i] == null) continue;
                live[i].transform.position = new Vector3(p[0], p[1], p[2]);
            }

            if (live.Count != waypoints.Length)
                Plugin.Log.LogWarning($"{lepo.name}: {waypoints.Length} saved waypoint(s) against {live.Count} real ones, "
                    + $"put back the first {n}");
        }

        private static int Relink(string spec, LevelEditorPlaceableObject[] placed)
        {
            if (string.IsNullOrEmpty(spec)) return 0;

            int relinked = 0;
            foreach (var pair in spec.Split(','))
            {
                var bits = pair.Split(':');
                if (bits.Length != 2 || !int.TryParse(bits[0], out int ti) || !int.TryParse(bits[1], out int ri)) continue;
                if (ti < 0 || ti >= placed.Length || ri < 0 || ri >= placed.Length) continue;

                var tx = placed[ti];
                var rx = placed[ri];
                var ctrl = tx != null ? tx.LinkController : null;
                var rc = rx != null ? rx.LinkController : null;
                if (ctrl == null || rc == null || ctrl.IsReceiverLinkedTo(rx)) continue;
                if (!ctrl.CanTransmitterLinkToReceiver(rc, out var why)
                    || why != ObjectLinkController.LinkConnectionCheckResult.ValidConnection) continue;

                ctrl.LinkReceiver(rx, false);
                relinked++;
            }
            return relinked;
        }

        private static void Offset(Il2CppStructArray<float> pos, Vector3 by)
        {
            if (pos == null || pos.Length < 3 || pos.Length % 3 != 0) return;
            for (int i = 0; i < pos.Length; i += 3)
            {
                pos[i] += by.x;
                pos[i + 1] += by.y;
                pos[i + 2] += by.z;
            }
        }

        private static string Sanitise(string name)
        {
            var sb = new System.Text.StringBuilder(32);
            var bad = Path.GetInvalidFileNameChars();
            foreach (char c in name ?? "")
            {
                if (char.IsControl(c) || Array.IndexOf(bad, c) >= 0) continue;
                sb.Append(c);
                if (sb.Length >= 40) break;
            }
            string clean = sb.ToString().Trim(' ', '.');
            return clean.Length == 0 ? "Group" : clean;
        }

        // shoots the REAL renderers in place (layer-swapped onto ShotLayer, every other light in the
        // scene switched off for the frame) instead of cloning mesh+sharedMaterials onto a stage double.
        // the clone approach silently dropped anything driven off the live renderer state instead of the
        // mesh/material asset — painter floor scale (a shader property, not a transform scale) and block
        // colour (a per-instance MaterialPropertyBlock via LevelEditorColourChangerListener) both live
        // there, so cloned previews rendered them at their defaults. rendering the originals picks up
        // whatever the object is actually doing, present parameter or a future one alike.
        private static byte[] Snapshot(List<LevelEditorPlaceableObject> objs)
        {
            Bounds bounds = default;
            bool any = false;
            var rends = new List<Renderer>();
            foreach (var o in objs)
            {
                var list = o.GetComponentsInChildren<Renderer>(false);
                for (int i = 0; list != null && i < list.Length; i++)
                {
                    var r = list[i];
                    if (r == null || !r.enabled) continue;
                    rends.Add(r);
                    if (!any) { bounds = r.bounds; any = true; }
                    else bounds.Encapsulate(r.bounds);
                }
            }
            if (!any) return null;

            var otherLights = UnityEngine.Object.FindObjectsOfType<Light>();
            var wasLightOn = new bool[otherLights.Length];
            for (int i = 0; i < otherLights.Length; i++)
            {
                wasLightOn[i] = otherLights[i].enabled;
                otherLights[i].enabled = false;
            }

            var savedLayer = new int[rends.Count];
            for (int i = 0; i < rends.Count; i++)
            {
                savedLayer[i] = rends[i].gameObject.layer;
                rends[i].gameObject.layer = ShotLayer;
            }

            float radius = Mathf.Max(bounds.extents.magnitude, 0.5f);

            var lightGo = new GameObject("BettrFG_GroupShotLight");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.15f;
            light.cullingMask = 1 << ShotLayer;
            lightGo.transform.rotation = Quaternion.Euler(42f, 145f, 0f);

            var camGo = new GameObject("BettrFG_GroupShotCam");
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = radius * 1.08f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.07f, 0.08f, 0.1f, 1f);
            cam.cullingMask = 1 << ShotLayer;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = radius * 12f + 200f;
            var back = new Vector3(-0.62f, 0.55f, -0.92f).normalized;
            camGo.transform.position = bounds.center + back * (radius * 5f);
            camGo.transform.LookAt(bounds.center);

            var rt = new RenderTexture(ShotSize, ShotSize, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.Render();

            var was = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(ShotSize, ShotSize, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0f, 0f, ShotSize, ShotSize), 0, 0, false);
            tex.Apply(false);
            RenderTexture.active = was;

            cam.targetTexture = null;
            var png = tex.EncodeToPNG();

            rt.Release();
            UnityEngine.Object.Destroy(rt);
            UnityEngine.Object.Destroy(tex);
            UnityEngine.Object.Destroy(camGo);
            UnityEngine.Object.Destroy(lightGo);

            for (int i = 0; i < rends.Count; i++)
                if (rends[i] != null) rends[i].gameObject.layer = savedLayer[i];
            for (int i = 0; i < otherLights.Length; i++)
                if (otherLights[i] != null) otherLights[i].enabled = wasLightOn[i];

            Plugin.Log.LogInfo($"group preview: {rends.Count} renderer(s), {radius:0.#} unit radius, {ShotSize}x{ShotSize}");
            return png;
        }
    }
}
