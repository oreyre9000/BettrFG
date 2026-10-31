import os
import sys

import UnityPy
import fgprefab
from fgprefab import CLASS_IDS, KEEP, Exporter, Writer, load_cab_index, map_keys, safe, say, version_map, write_meta

SETTINGS = {
    "OcclusionCullingSettings": 29,
    "RenderSettings": 104,
    "LightmapSettings": 157,
    "NavMeshSettings": 196,
}


def index_path():
    base = os.environ.get("LOCALAPPDATA") or os.path.expanduser("~")
    folder = os.path.join(base, "BettrFG")
    os.makedirs(folder, exist_ok=True)
    return os.path.join(folder, "scene_index.txt")


def build_index(bundle_dir, out_path):
    files = sorted(f for f in os.listdir(bundle_dir) if f.endswith(".bundle"))
    say("indexing scenes across %d bundles, one off" % len(files))
    with open(out_path, "w", encoding="utf-8") as out:
        for i, name in enumerate(files):
            try:
                env = UnityPy.load(os.path.join(bundle_dir, name))
                for obj in env.objects:
                    if obj.type.name != "AssetBundle":
                        continue
                    tt = obj.read_typetree()
                    if tt.get("m_IsStreamedSceneAssetBundle"):
                        for entry in tt.get("m_Container") or []:
                            key = entry[0] if isinstance(entry, (tuple, list)) else entry
                            out.write("%s\t%s\n" % (leaf_of_scene(key), name))
                    break
                del env
            except Exception as err:
                print("  %s skipped: %r" % (name, err), flush=True)
            if i % 25 == 0:
                say("indexing scenes, %d of %d bundles" % (i, len(files)))
                out.flush()


def leaf_of_scene(key):
    leaf = str(key).replace("\\", "/").rsplit("/", 1)[-1]
    return leaf[:-6] if leaf.lower().endswith(".unity") else leaf


def find_scene(bundle_dir, scene):
    path = index_path()
    for attempt in (0, 1):
        if not os.path.exists(path) or os.path.getsize(path) == 0 or attempt:
            build_index(bundle_dir, path)
        for line in open(path, encoding="utf-8"):
            parts = line.rstrip("\n").split("\t")
            if len(parts) == 2 and parts[0].lower() == scene.lower():
                return parts[1]
        say("%s is not in the scene index" % scene)
    return None


class SceneExporter(Exporter):
    def roots(self):
        out = []
        for obj in self.env.objects:
            if obj.type.name not in ("Transform", "RectTransform"):
                continue
            tt = obj.read_typetree()
            father = tt.get("m_Father") or {}
            if not father.get("m_PathID"):
                out.append(obj)
        return out

    def settings_block(self, obj):
        cid = SETTINGS[obj.type.name]
        self.current_file = obj.assets_file
        versions, root_version = version_map(obj)
        w = Writer(self.resolve, map_keys(obj), versions)
        for k, v in obj.read_typetree().items():
            if k == "m_ObjectHideFlags":
                continue
            w.emit(k, v, 1)
        head = ["--- !u!%d &%d" % (cid, self.lid(obj.path_id)), "%s:" % obj.type.name,
                "  m_ObjectHideFlags: 0"]
        if root_version > 1:
            head.append("  serializedVersion: %d" % root_version)
        return head + w.lines

    def run_scene(self):
        roots = self.roots()
        if not roots:
            say("that scene has no root objects in it")
            return False

        nodes = []
        for tr in roots:
            self.seen_objs[tr.path_id] = tr
            self.walk(tr.path_id, nodes)
        say("%d object(s) in %s" % (len(nodes), self.name))

        for go, tr in nodes:
            self.lid(go.path_id)
            self.lid(tr.path_id)
            for comp in go.read_typetree().get("m_Component", []) or []:
                c = self.deref(comp.get("component") if isinstance(comp, dict) else comp, go.assets_file)
                if c is not None and c.type.name in KEEP:
                    self.lid(c.path_id)

        lines = ["%YAML 1.1", "%TAG !u! tag:unity3d.com,2011:"]
        for obj in self.env.objects:
            if obj.type.name in SETTINGS:
                lines += self.settings_block(obj)

        done = 0
        for go, tr in nodes:
            lines += self.emit_gameobject(go)
            for comp in go.read_typetree().get("m_Component", []) or []:
                c = self.deref(comp.get("component") if isinstance(comp, dict) else comp, go.assets_file)
                if c is None or c.type.name not in KEEP:
                    continue
                lines += self.emit_component(c)
            done += 1
            if done % 500 == 0:
                say("writing %s, %d of %d objects" % (self.name, done, len(nodes)))

        lines += ["--- !u!1660057539 &%d" % self.lid("SceneRoots"), "SceneRoots:",
                  "  m_ObjectHideFlags: 0", "  m_Roots:"]
        for tr in roots:
            lines.append("  - {fileID: %d}" % self.lid(tr.path_id))

        path = os.path.join(self.work, safe(self.name) + ".unity")
        with open(path, "w", encoding="utf-8") as fh:
            fh.write("\n".join(lines) + "\n")
        write_meta(path, self.guid("scene:" + self.name),
                   "DefaultImporter:\n  externalObjects: {}\n  userData: \n")
        say("wrote %s, pulling the assets it uses" % os.path.basename(path))

        self.flush_assets()
        return True


def main():
    work = sys.argv[1]
    bundle_dir = sys.argv[2]
    scene = sys.argv[3]
    fgprefab.WORK = work

    bundle = find_scene(bundle_dir, scene)
    if bundle is None:
        say("could not find a scene called %s in the bundles" % scene)
        return 1

    say("reading %s" % scene)
    env = UnityPy.load(os.path.join(bundle_dir, bundle))
    exporter = SceneExporter(env, work, scene, bundle_dir, load_cab_index(bundle_dir))
    return 0 if exporter.run_scene() else 1


if __name__ == "__main__":
    sys.exit(main())
