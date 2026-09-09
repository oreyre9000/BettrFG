import os
import struct
import sys
from array import array

import UnityPy
from UnityPy.helpers.MeshHelper import MeshHandler

WORK = ""


def say(msg):
    print(msg, flush=True)
    try:
        with open(os.environ.get("BETTRFG_PROGRESS") or os.path.join(WORK, "progress.txt"),
                  "w", encoding="utf-8") as fh:
            fh.write(msg)
    except OSError:
        pass


def index_path():
    base = os.environ.get("LOCALAPPDATA") or os.path.expanduser("~")
    folder = os.path.join(base, "BettrFG")
    os.makedirs(folder, exist_ok=True)
    return os.path.join(folder, "mesh_index.txt")


def build_index(bundle_dir, out_path):
    files = sorted(f for f in os.listdir(bundle_dir) if f.endswith(".bundle"))
    say("indexing %d bundles, one off, this takes a while" % len(files))
    with open(out_path, "w", encoding="utf-8") as out:
        for i, name in enumerate(files):
            try:
                env = UnityPy.load(os.path.join(bundle_dir, name))
                for obj in env.objects:
                    if obj.type.name != "Mesh":
                        continue
                    try:
                        out.write("%s\t%d\t%s\n" % (name, obj.path_id, obj.peek_name() or ""))
                    except Exception:
                        pass
                del env
            except Exception as err:
                print("  %s skipped: %r" % (name, err), flush=True)
            if i % 25 == 0:
                say("indexing bundles, %d of %d" % (i, len(files)))
                out.flush()
    say("index built")


def load_index(path):
    table = {}
    with open(path, encoding="utf-8") as fh:
        for line in fh:
            parts = line.rstrip("\n").split("\t")
            if len(parts) == 3:
                table.setdefault(parts[2], []).append((parts[0], int(parts[1])))
    return table


def load_mesh(obj):
    handler = MeshHandler(obj.read())
    handler.process()
    weights = handler.m_BoneWeights or []
    for i, w in enumerate(weights):
        if abs(w[0] + w[1] + w[2] + w[3] - 1.0) > 1e-3:
            weights[i] = (w[0], w[1], w[2], max(0.0, 1.0 - (w[0] + w[1] + w[2])))
    return handler


def pack(name, handler, src):
    count = handler.m_VertexCount
    verts = handler.m_Vertices or []
    norms = handler.m_Normals or []
    uvs = handler.m_UV0 or []
    bone_idx = getattr(handler, "m_BoneIndices", None) or []
    bone_wgt = getattr(handler, "m_BoneWeights", None) or []
    tris = handler.get_triangles()
    if tris and not isinstance(tris[0], list):
        tris = [tris]

    has_norm = len(norms) >= count
    has_uv = len(uvs) >= count
    has_skin = len(bone_idx) >= count and len(bone_wgt) >= count

    raw = name.encode("utf-8")
    buf = bytearray()
    buf += struct.pack("<H", len(raw)) + raw
    buf += struct.pack("<IB", count, (1 if has_norm else 0) | (2 if has_uv else 0) | (4 if has_skin else 0))
    buf += array("f", [c for v in verts[:count] for c in v[:3]]).tobytes()
    if has_norm:
        buf += array("f", [c for v in norms[:count] for c in v[:3]]).tobytes()
    if has_uv:
        buf += array("f", [c for v in uvs[:count] for c in v[:2]]).tobytes()
    if has_skin:
        buf += array("i", [int(c) for v in bone_idx[:count] for c in v[:4]]).tobytes()
        buf += array("f", [float(c) for v in bone_wgt[:count] for c in v[:4]]).tobytes()

    bind = getattr(src, "m_BindPose", None) or []
    buf += struct.pack("<I", len(bind))
    if bind:
        rows = []
        for m in bind:
            rows.extend([m.e00, m.e01, m.e02, m.e03,
                         m.e10, m.e11, m.e12, m.e13,
                         m.e20, m.e21, m.e22, m.e23,
                         m.e30, m.e31, m.e32, m.e33])
        buf += array("f", rows).tobytes()

    buf += struct.pack("<I", len(tris))
    for sub in tris:
        flat = [i for tri in sub for i in tri]
        buf += struct.pack("<I", len(flat))
        buf += array("i", flat).tobytes()
    return bytes(buf)


def extract(bundle_dir, table, wanted, blobs):
    per_bundle = {}
    for name, vcount in wanted:
        if (name, vcount) in blobs:
            continue
        for bundle, path_id in table.get(name, []):
            per_bundle.setdefault(bundle, []).append((name, vcount, path_id))

    for bundle in sorted(per_bundle):
        todo = [i for i in per_bundle[bundle] if (i[0], i[1]) not in blobs]
        if not todo:
            continue
        say("reading %s" % bundle)
        try:
            env = UnityPy.load(os.path.join(bundle_dir, bundle))
        except Exception as err:
            print("  %s failed: %r" % (bundle, err), flush=True)
            continue
        by_id = {o.path_id: o for o in env.objects if o.type.name == "Mesh"}
        for name, vcount, path_id in todo:
            obj = by_id.get(path_id)
            if obj is None:
                continue
            try:
                data = obj.read()
                handler = load_mesh(obj)
            except Exception as err:
                print("  mesh %s failed: %r" % (name, err), flush=True)
                continue
            if handler.m_VertexCount != vcount:
                continue
            blobs[(name, vcount)] = pack(name, handler, data)
        del env


def main():
    global WORK
    WORK = sys.argv[1]
    bundle_dir = sys.argv[2]

    wanted = []
    with open(os.path.join(WORK, "meshes.txt"), encoding="utf-8") as fh:
        for line in fh:
            parts = line.rstrip("\n").split("\t")
            if len(parts) == 2:
                wanted.append((parts[0], int(parts[1])))
    if not wanted:
        say("nothing to fetch")
        return 1

    idx = index_path()
    if not os.path.exists(idx):
        build_index(bundle_dir, idx)
    table = load_index(idx)

    blobs = {}
    extract(bundle_dir, table, wanted, blobs)

    if len(blobs) < len(wanted):
        say("%d of %d meshes missing, reindexing" % (len(wanted) - len(blobs), len(wanted)))
        build_index(bundle_dir, idx)
        table = load_index(idx)
        extract(bundle_dir, table, wanted, blobs)

    ordered = [blobs[k] for k in wanted if k in blobs]
    with open(os.path.join(WORK, "geometry.bin"), "wb") as out:
        out.write(b"FGGE")
        out.write(struct.pack("<I", len(ordered)))
        for blob in ordered:
            out.write(blob)

    if not ordered:
        say("no geometry found in the bundles for any of those meshes")
        return 1
    say("%d of %d meshes pulled" % (len(ordered), len(wanted)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
