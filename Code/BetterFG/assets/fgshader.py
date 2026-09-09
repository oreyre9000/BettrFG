import ctypes
import os
import re
import struct

from UnityPy.export.ShaderConverter import ShaderProgram
from UnityPy.helpers import CompressionHelper
from UnityPy.streams import EndianBinaryReader

STAGES = (("progVertex", "vp"), ("progFragment", "fp"), ("progGeometry", "gp"),
          ("progHull", "hp"), ("progDomain", "dp"))

PLATFORM_NAMES = {4: "d3d11", 5: "gles", 9: "gles3", 11: "pssl", 14: "metal",
                  15: "glcore", 18: "vulkan"}

TEX_DIMS = {"texture2d": 2, "texture3d": 3, "texturecube": 4, "texture2darray": 5,
            "texturecubearray": 6, "texture2dms": 2}

AUTO_DECLARED = {
    "_CosTime", "_LightPositionRange", "_LightProjectionParams", "_LightShadowData",
    "_LightSplitsFar", "_LightSplitsNear", "_ProjectionParams", "_ScreenParams", "_SinTime",
    "_Time", "_UnityFBInput", "_WorldSpaceCameraPos", "_WorldSpaceLightPos0", "_ZBufferParams",
    "clipPos", "glstate_lightmodel_ambient", "glstate_matrix_projection",
    "glstate_matrix_transpose_modelview0", "tex", "unity_4LightAtten0", "unity_4LightPosX0",
    "unity_4LightPosY0", "unity_4LightPosZ0", "unity_AmbientEquator", "unity_AmbientGround",
    "unity_AmbientSky", "unity_BaseInstanceID", "unity_CameraInvProjection",
    "unity_CameraProjection", "unity_CameraToWorld", "unity_CameraWorldClipPlanes",
    "unity_DeltaTime", "unity_DynamicDirectionality", "unity_DynamicLightmap",
    "unity_DynamicLightmapST", "unity_DynamicNormal", "unity_FogColor", "unity_FogParams",
    "unity_HalfStereoSeparation", "unity_IndirectSpecColor", "unity_InstanceCount",
    "unity_LODFade", "unity_LightAtten", "unity_LightColor", "unity_LightPosition",
    "unity_LightShadowBias", "unity_Lightmap", "unity_LightmapInd", "unity_LightmapST",
    "unity_MatrixInvV", "unity_MatrixV", "unity_MatrixVP", "unity_ObjectToWorld",
    "unity_OcclusionMaskSelector", "unity_OrthoParams", "unity_ProbeVolumeMin",
    "unity_ProbeVolumeParams", "unity_ProbeVolumeSH", "unity_ProbeVolumeSizeInv",
    "unity_ProbeVolumeWorldToObject", "unity_ProbesOcclusion", "unity_RenderingLayer",
    "unity_SHAb", "unity_SHAg", "unity_SHAr", "unity_SHBb", "unity_SHBg", "unity_SHBr",
    "unity_SHC", "unity_ShadowColor", "unity_ShadowFadeCenterAndType", "unity_ShadowMask",
    "unity_ShadowSplitSpheres", "unity_ShadowSplitSqRadii", "unity_SpecCube0",
    "unity_SpecCube0_BoxMax", "unity_SpecCube0_BoxMin", "unity_SpecCube0_HDR",
    "unity_SpecCube0_ProbePosition", "unity_SpecCube1", "unity_SpecCube1_BoxMax",
    "unity_SpecCube1_BoxMin", "unity_SpecCube1_HDR", "unity_SpecCube1_ProbePosition",
    "unity_SpotDirection", "unity_StereoCameraInvProjection", "unity_StereoCameraProjection",
    "unity_StereoCameraToWorld", "unity_StereoEyeIndex", "unity_StereoMatrixInvV",
    "unity_StereoMatrixP", "unity_StereoMatrixV", "unity_StereoMatrixVP",
    "unity_StereoScaleOffset", "unity_StereoWorldSpaceCameraPos", "unity_StereoWorldToCamera",
    "unity_WorldToCamera", "unity_WorldToObject", "unity_WorldToShadow",
    "unity_WorldTransformParams"
}


AUTO_SAMPLERS = {
    "sampler_UnityFBInput", "samplertex", "samplerunity_DynamicLightmap",
    "samplerunity_Lightmap", "samplerunity_LightmapInd", "samplerunity_ProbeVolumeSH",
    "samplerunity_ShadowMask", "samplerunity_SpecCube0"
}

_dll = None

DUMP = bool(os.environ.get("BETTRFG_SHADER_DUMP"))


def disassembler():
    global _dll
    if _dll is None:
        _dll = ctypes.WinDLL(os.path.join(os.environ.get("SystemRoot", r"C:\Windows"),
                                          "System32", "D3DCompiler_47.dll"))
        _dll.D3DDisassemble.argtypes = [ctypes.c_void_p, ctypes.c_size_t, ctypes.c_uint,
                                        ctypes.c_char_p, ctypes.POINTER(ctypes.c_void_p)]
        _dll.D3DDisassemble.restype = ctypes.c_long
    return _dll


def disassemble(dxbc):
    dll = disassembler()
    blob = ctypes.c_void_p()
    if dll.D3DDisassemble(dxbc, len(dxbc), 0, None, ctypes.byref(blob)) != 0:
        return None
    vt = ctypes.cast(blob, ctypes.POINTER(ctypes.POINTER(ctypes.c_void_p)))[0]
    ptr = ctypes.CFUNCTYPE(ctypes.c_void_p, ctypes.c_void_p)(vt[3])(blob)
    size = ctypes.CFUNCTYPE(ctypes.c_size_t, ctypes.c_void_p)(vt[4])(blob)
    text = ctypes.string_at(ptr, size).decode("utf-8", "replace")
    ctypes.CFUNCTYPE(ctypes.c_ulong, ctypes.c_void_p)(vt[2])(blob)
    return text


def dxbc_of(code):
    i = code.find(b"DXBC")
    return code[i:] if i >= 0 else None


class Blob:
    def __init__(self, shader, platform_index):
        self.entries = []
        comp = bytes(shader.compressedBlob)
        off = shader.offsets[platform_index][0]
        clen = shader.compressedLengths[platform_index][0]
        dlen = shader.decompressedLengths[platform_index][0]
        self.raw = CompressionHelper.decompress_lz4(comp[off:off + clen], dlen)
        count = struct.unpack_from("<i", self.raw, 0)[0]
        self.table = [struct.unpack_from("<3i", self.raw, 4 + i * 12) for i in range(count)]
        self.programs = ShaderProgram(EndianBinaryReader(self.raw, endian="<"),
                                      shader.object_reader.version)

    def code(self, index):
        return bytes(self.programs.m_SubPrograms[index].m_ProgramCode)

    def keywords(self, index):
        sub = self.programs.m_SubPrograms[index]
        return list(sub.m_Keywords or []) + list(getattr(sub, "m_LocalKeywords", None) or [])

    def payload(self, index):
        off, length, _ = self.table[index]
        return self.raw[off:off + length]


def blob_strings(payload):
    out = []
    pos = 24
    limit = len(payload)
    while pos + 4 <= limit:
        n = struct.unpack_from("<i", payload, pos)[0]
        if 1 <= n <= 128 and pos + 4 + n <= limit:
            text = payload[pos + 4:pos + 4 + n]
            if re.fullmatch(rb"[A-Za-z_$][A-Za-z0-9_$]*", text):
                after = pos + 4 + n
                after += (4 - (after % 4)) % 4
                tail = struct.unpack_from("<6i", payload, after) if after + 24 <= limit \
                    else (0, 0, 0, 0, 0, 0)
                out.append((text.decode(), pos, tail))
                pos = after
                continue
        pos += 4
    return out


def texture_names(payload, dims, exclude=()):
    named = [s for s in blob_strings(payload)
             if not s[0].startswith("$") and s[0] not in exclude]
    for start in range(len(named)):
        window = named[start:start + len(dims)]
        if len(window) < len(dims):
            break
        if all(w[2][1] == i for i, w in enumerate(window)):
            return [w[0] for w in window]
    tail = [w for w in named if 0 <= w[2][1] < 32]
    for start in range(len(tail) - len(dims) + 1):
        window = tail[start:start + len(dims)]
        if [w[2][1] for w in window] == list(range(len(dims))):
            return [w[0] for w in window]
    return None


def stage_textures(prog, names):
    if prog is None or prog.m_CommonParameters is None:
        return {}
    out = {}
    for t in prog.m_CommonParameters.m_TextureParams or []:
        nm = names.get(t.m_NameIndex)
        if nm:
            out[t.m_Index] = nm
    return out


def known_cb_names(parsed):
    found = set()
    for sub in parsed.m_SubShaders:
        for pass_obj in sub.m_Passes:
            ni = pass_obj.m_NameIndices
            names = {v: k for k, v in (ni.items() if isinstance(ni, dict) else ni)}
            for stage_attr, _ in STAGES:
                prog = getattr(pass_obj, stage_attr, None)
                if prog is None or prog.m_CommonParameters is None:
                    continue
                for cb in prog.m_CommonParameters.m_ConstantBuffers or []:
                    nm = names.get(cb.m_NameIndex)
                    if nm:
                        found.add(nm)
    return found


def stage_params(prog, names, groups=None, matrices=None):
    groups = {} if groups is None else groups
    matrices = {} if matrices is None else matrices
    if prog is None or prog.m_CommonParameters is None:
        return groups, matrices
    for cb in prog.m_CommonParameters.m_ConstantBuffers or []:
        cb_name = names.get(cb.m_NameIndex)
        if not cb_name:
            continue
        for v in cb.m_VectorParams or []:
            nm = names.get(v.m_NameIndex)
            if nm:
                groups.setdefault(cb_name, {})[nm] = (v.m_Dim or 4, v.m_Index)
        for m in cb.m_MatrixParams or []:
            nm = names.get(m.m_NameIndex)
            if not nm:
                continue
            base = m.m_Index // 16
            for row in range(m.m_RowCount):
                matrices.setdefault(cb_name, {})[base + row] = "%s[%d]" % (nm, row)
    return groups, matrices


def as_slots(groups, matrices):
    whole, parts = slot_components(groups)
    for cb_name, table in matrices.items():
        whole.setdefault(cb_name, {}).update(table)
        for slot in table:
            parts.get(cb_name, {}).pop(slot, None)
    return whole, parts


def fill_gaps(whole, parts, extra_whole, extra_parts):
    for cb_name in set(extra_whole) | set(extra_parts):
        mine_w = whole.get(cb_name, {})
        mine_p = parts.get(cb_name, {})
        theirs_w = extra_whole.get(cb_name, {})
        theirs_p = extra_parts.get(cb_name, {})
        for slot, nm in theirs_w.items():
            if slot not in mine_w and slot not in mine_p:
                whole.setdefault(cb_name, {})[slot] = nm
        for slot, comps in theirs_p.items():
            if slot not in mine_w and slot not in mine_p:
                parts.setdefault(cb_name, {})[slot] = dict(comps)


def merged_params(parsed):
    groups, matrices = {}, {}
    for sub in parsed.m_SubShaders:
        for pass_obj in sub.m_Passes:
            ni = pass_obj.m_NameIndices
            names = {v: k for k, v in (ni.items() if isinstance(ni, dict) else ni)}
            for stage_attr, _ in STAGES:
                stage_params(getattr(pass_obj, stage_attr, None), names, groups, matrices)
    return groups, matrices


def combine(base_whole, base_parts, groups):
    whole = {k: dict(v) for k, v in base_whole.items()}
    parts = {k: {slot: dict(c) for slot, c in v.items()} for k, v in base_parts.items()}
    extra_whole, extra_parts = slot_components(groups)
    for cb_name, table in extra_whole.items():
        whole.setdefault(cb_name, {}).update(table)
        for slot in table:
            parts.get(cb_name, {}).pop(slot, None)
    for cb_name, table in extra_parts.items():
        for slot, comps in table.items():
            parts.setdefault(cb_name, {}).setdefault(slot, {}).update(comps)
            whole.get(cb_name, {}).pop(slot, None)
    return whole, parts


def material_constants(payload, props, cbs, anchors):
    by_name = {p.m_Name: p for p in props}
    groups, current = {}, None
    for name, _, tail in blob_strings(payload):
        if name in cbs:
            current = name
            continue
        prop = by_name.get(name)
        if current is None or prop is None or prop.m_Type == 4:
            continue
        dim, offset = tail[2], tail[5]
        if dim < 1 or dim > 4 or offset < 0 or offset % 4:
            continue
        groups.setdefault(current, {})[name] = (dim, offset)

    for cb_name, wanted in anchors.items():
        found = groups.get(cb_name, {})
        for name, offset in wanted.items():
            if name in found and found[name][1] != offset:
                return None
    return groups


def slot_components(groups):
    whole, parts = {}, {}
    for cb_name, params in groups.items():
        for name, (dim, offset) in params.items():
            slot, comp = offset // 16, (offset % 16) // 4
            if dim == 4 and comp == 0:
                whole.setdefault(cb_name, {})[slot] = name
            else:
                for k in range(dim):
                    parts.setdefault(cb_name, {}).setdefault(slot, {})[comp + k] = (name, dim, comp)
    return whole, parts


def stage_anchors(prog, names):
    anchors = {}
    if prog is None or prog.m_CommonParameters is None:
        return anchors
    for cb in prog.m_CommonParameters.m_ConstantBuffers or []:
        cb_name = names.get(cb.m_NameIndex)
        if not cb_name:
            continue
        for v in cb.m_VectorParams or []:
            nm = names.get(v.m_NameIndex)
            if nm:
                anchors.setdefault(cb_name, {})[nm] = v.m_Index
    return anchors


def blob_cb_order(payload, count, known):
    order = []
    for name, _, _ in blob_strings(payload):
        if name in known and name not in order:
            order.append(name)
            if len(order) == count:
                break
    return order


def constant_map(params, names):
    slots = {}
    for cb_index, cb in enumerate(params.m_ConstantBuffers or []):
        cb_name = names.get(cb.m_NameIndex, "cb%d" % cb_index)
        for v in cb.m_VectorParams or []:
            slots.setdefault(cb_name, {})[v.m_Index // 16] = names.get(v.m_NameIndex)
        for m in cb.m_MatrixParams or []:
            base = m.m_Index // 16
            for row in range(m.m_RowCount):
                nm = names.get(m.m_NameIndex)
                slots.setdefault(cb_name, {})[base + row] = nm and "%s[%d]" % (nm, row)
    return slots


BUILTIN_CB = {
    "UnityLighting": {
        0: "_WorldSpaceLightPos0",
        7: "unity_LightColor",
        15: "unity_LightPosition",
        39: "unity_SHAr",
        40: "unity_SHAg",
        41: "unity_SHAb",
        42: "unity_SHBr",
        43: "unity_SHBg",
        44: "unity_SHBb",
        45: "unity_SHC",
        46: "unity_OcclusionMaskSelector",
    },
    "UnityPerCamera": {
        0: "_Time",
        1: "_SinTime",
        5: "_ProjectionParams",
        6: "_ScreenParams",
        7: "_ZBufferParams",
        8: "unity_OrthoParams",
    },
    "UnityPerCameraRare": {
        18: "unity_CameraToWorld[0]",
        19: "unity_CameraToWorld[1]",
        20: "unity_CameraToWorld[2]",
        21: "unity_CameraToWorld[3]",
    },
    "UnityPerDraw": {
        0: "unity_ObjectToWorld[0]",
        1: "unity_ObjectToWorld[1]",
        2: "unity_ObjectToWorld[2]",
        3: "unity_ObjectToWorld[3]",
        4: "unity_WorldToObject[0]",
        5: "unity_WorldToObject[1]",
        6: "unity_WorldToObject[2]",
        7: "unity_WorldToObject[3]",
        9: "unity_WorldTransformParams",
    },
    "UnityPerFrame": {
        0: "glstate_lightmodel_ambient",
        1: "unity_AmbientSky",
        5: "glstate_matrix_projection[0]",
        6: "glstate_matrix_projection[1]",
        7: "glstate_matrix_projection[2]",
        8: "glstate_matrix_projection[3]",
        9: "unity_MatrixV[0]",
        10: "unity_MatrixV[1]",
        11: "unity_MatrixV[2]",
        12: "unity_MatrixV[3]",
        13: "unity_MatrixInvV[0]",
        14: "unity_MatrixInvV[1]",
        15: "unity_MatrixInvV[2]",
        16: "unity_MatrixInvV[3]",
        17: "unity_MatrixVP[0]",
        18: "unity_MatrixVP[1]",
        19: "unity_MatrixVP[2]",
        20: "unity_MatrixVP[3]",
    },
    "UnityProbeVolume": {
        0: "unity_ProbeVolumeParams",
        1: "unity_ProbeVolumeWorldToObject[0]",
        2: "unity_ProbeVolumeWorldToObject[1]",
        3: "unity_ProbeVolumeWorldToObject[2]",
        4: "unity_ProbeVolumeWorldToObject[3]",
    },
    "UnityShadows": {
        5: "unity_LightShadowBias",
    },
}


# a variant's reflection often lists only some of the constant buffers its DXBC actually binds -
# UnityPerDraw is routinely missing - so registers left without a name are matched against the
# engine's fixed layouts by the rows they read, and only taken when exactly one covers them all
ENGINE_CBS = {
    "$Globals", "GPUInstancedPerInstanceData",
    "GPUInstancedPerInstanceDataVertexAnimation", "IBuffer", "Props", "UnityLightmaps",
    "UnityLighting", "UnityMetaPass", "UnityPerCamera", "UnityPerCameraRare", "UnityPerDraw",
    "UnityPerDrawSprite", "UnityPerFrame", "UnityProbeVolume", "UnityReflectionProbes",
    "UnityShadows", "UnityTerrain", "VertexExpending",
}


def cb_order(params, names):
    order = []
    for b in getattr(params, "m_ConstantBufferBindings", None) or []:
        reg = getattr(b, "m_Index", None)
        if reg is None or reg < 0:
            continue
        while len(order) <= reg:
            order.append(None)
        order[reg] = names.get(b.m_NameIndex)
    if any(order):
        return order
    return [names.get(cb.m_NameIndex, "cb%d" % i)
            for i, cb in enumerate(params.m_ConstantBuffers or [])]


def cb_candidates(params, names):
    return [names.get(cb.m_NameIndex) for cb in (params.m_ConstantBuffers or [])]


MASK_ORDER = "xyzw"


SW = "xyzw"


def split_swizzle(token):
    head, dot, tail = token.rpartition(".")
    if dot and tail and all(c in SW for c in tail):
        return head, tail
    return token, ""


def dest_indices(dest):
    _, sw = split_swizzle(dest)
    if not sw:
        return [0, 1, 2, 3]
    return [SW.index(c) for c in sw]


def pick_swizzle(src, indices):
    neg = ""
    body = src
    while body and body[0] in "-|":
        neg += body[0]
        body = body[1:]
    if body.endswith("|"):
        body = body[:-1]
    head, sw = split_swizzle(body)
    if sw:
        chars = [sw[i] if i < len(sw) else sw[-1] for i in indices]
        body = head + "." + "".join(chars)
    out = body
    if "|" in neg:
        out = "abs(%s)" % out
    if "-" in neg:
        out = "-" + out
    return out


def literal(token, indices):
    sign = ""
    while token and token[0] in "-|":
        sign += token[0]
        token = token[1:]
    if sign:
        return ("-" if "-" in sign else "") + literal(token, indices)
    inner = token[token.index("(") + 1:token.rindex(")")]
    parts = [p.strip() for p in inner.split(",")]
    vals = [parts[i] if i < len(parts) else parts[-1] for i in indices]
    if len(vals) == 1:
        return vals[0]
    return "float%d(%s)" % (len(vals), ", ".join(vals))


def split_args(text):
    out, depth, cur = [], 0, ""
    for c in text:
        if c == "(":
            depth += 1
        elif c == ")":
            depth -= 1
        if c == "," and depth == 0:
            out.append(cur.strip())
            cur = ""
        else:
            cur += c
    if cur.strip():
        out.append(cur.strip())
    return out


BINARY = {"add": "%s + %s", "mul": "%s * %s", "div": "%s / %s"}

INT_BINARY = {"iadd": "+", "imul": "*", "idiv": "/", "ishl": "<<", "ishr": ">>",
              "and": "&", "or": "|", "xor": "^"}

UINT_BINARY = {"ushr": ">>", "udiv": "/"}

CALL1 = {"rsq": "rsqrt", "sqrt": "sqrt", "rcp": "rcp", "exp": "exp2", "log": "log2",
         "frc": "frac", "round_ni": "floor", "round_pi": "ceil", "round_z": "trunc",
         "round_ne": "round", "deriv_rtx": "ddx", "deriv_rty": "ddy"}

BITCAST1 = {"ftoi": "asfloat(int%(n)s(%(v)s))", "ftou": "asfloat(uint%(n)s(%(v)s))",
            "itof": "float%(n)s(asint(%(v)s))", "utof": "float%(n)s(asuint(%(v)s))",
            "not": "asfloat(~asint(%(v)s))"}

INT_COMPARE = {"ieq": "==", "ine": "!=", "ige": ">=", "ilt": "<"}

UINT_COMPARE = {"uge": ">=", "ult": "<"}

CALL2 = {"min": "min", "max": "max", "imin": "min", "imax": "max", "umin": "min",
         "umax": "max", "dp2": "dot", "dp3": "dot", "dp4": "dot"}

COMPARE = {"eq": "==", "ne": "!=", "ge": ">=", "lt": "<"}


def rename(token, cbs, slots, textures, parts=None):
    m = re.match(r"^([-|]*)cb(\d+)\[(\d+)\](.*)$", token)
    if m:
        sign, cb_index, slot, rest = m.groups()
        cb_index, slot = int(cb_index), int(slot)
        cb_name = cbs[cb_index] if cb_index < len(cbs) else None
        name = slots.get(cb_name, {}).get(slot) if cb_name else None
        if name:
            row = re.match(r"^(\w+)\[(\d+)\]$", name)
            if row:
                return sign + "transpose(%s)[%s]" % (row.group(1), row.group(2)) + rest
            return sign + name + rest
        table = (parts or {}).get(cb_name, {}).get(slot) if cb_name else None
        sw = re.match(r"^\.([xyzw]+)(.*)$", rest) if table else None
        if sw:
            picked_idx = ["xyzw".index(c) for c in sw.group(1)]
            tail = sw.group(2)
            hits = {table.get(i) for i in picked_idx}
            if len(hits) == 1 and None not in hits:
                scalar, dim, base = hits.pop()
                if dim == 1:
                    return sign + scalar + tail
                picked = "".join("xyzw"[i - base] for i in picked_idx)
                return sign + scalar + "." + picked + tail
            parts_of = []
            for i in picked_idx:
                entry = table.get(i)
                if entry is None:
                    parts_of = None
                    break
                scalar, dim, base = entry
                parts_of.append(scalar if dim == 1 else "%s.%s" % (scalar, "xyzw"[i - base]))
            if parts_of:
                if len(parts_of) == 1:
                    return sign + parts_of[0] + tail
                return sign + "float%d(%s)" % (len(parts_of), ", ".join(parts_of)) + tail
        return sign + "cb%d_%d" % (cb_index, slot) + rest
    m = re.match(r"^t(\d+)\b(.*)$", token)
    if m and textures:
        i = int(m.group(1))
        if i < len(textures) and textures[i]:
            return textures[i] + m.group(2)
    m = re.match(r"^([-|]*)(x\d+|icb)\[([^\]]+)\](.*)$", token)
    if m:
        sign, bank, index, rest = m.groups()
        index = re.sub(r"\br(\d+)\.([xyzw])\b", r"asint(r\1.\2)", index.strip())
        return "%s%s[%s]%s" % (sign, bank, index, rest)
    m = re.match(r"^([-|]*)([vo])(\d+)(\..*)?$", token)
    if m:
        sign, kind, num, rest = m.groups()
        return "%s%s.%s%s%s" % (sign, "i" if kind == "v" else "o", kind, num, rest or "")
    return token


def signatures(asm):
    tables, current = {}, None
    for raw in asm.splitlines():
        line = raw.strip()
        if not line.startswith("//"):
            continue
        text = line[2:].strip()
        if text.endswith("signature:"):
            current = "in" if text.lower().startswith("input") else "out"
            tables[current] = {}
            continue
        if current is None or not text or set(text) <= set("- "):
            continue
        if text.startswith("Name"):
            continue
        parts = text.split()
        if len(parts) < 5 or not parts[1].isdigit() or not parts[3].isdigit():
            current = None
            continue
        tables[current][int(parts[3])] = (parts[0], int(parts[1]))
    return tables


def extract_icb(asm):
    marker = "dcl_immediateConstantBuffer"
    start = asm.find(marker)
    if start < 0:
        return [], asm
    open_brace = asm.find("{", start)
    depth, pos = 0, open_brace
    while pos < len(asm):
        if asm[pos] == "{":
            depth += 1
        elif asm[pos] == "}":
            depth -= 1
            if depth == 0:
                break
        pos += 1
    block = asm[open_brace + 1:pos]
    rows = []
    for row in re.findall(r"\{([^{}]*)\}", block):
        vals = [v.strip() for v in row.split(",") if v.strip()]
        if vals:
            rows.append(vals)
    return rows, asm[:start] + asm[pos + 1:]


def add_register(bucket, token):
    name = token.split(".")[0]
    if not any(existing.split(".")[0] == name for existing in bucket):
        bucket.append(token)


def transpile(asm, cbs, slots, textures, parts=None):
    icb_rows, asm = extract_icb(asm)
    body, decls, temps = [], [], 0
    inputs, outputs, resources, samplers, cbuffers = [], [], [], [], {}
    unmapped = set()
    cube_or_volume = set()
    indexable = []
    indent = 2

    def emit(line):
        body.append("    " * indent + line)

    for raw in asm.splitlines():
        line = raw.strip().replace("\0", "")
        if not line or line.startswith("//"):
            continue
        if re.match(r"^(vs|ps|gs|hs|ds|cs)_\d", line):
            continue
        op, _, rest = line.partition(" ")
        op = op.strip()
        rest = rest.strip()
        args = split_args(rest)

        if op.startswith("dcl_temps"):
            temps = int(rest)
            continue
        if op.startswith("dcl_indexableTemp"):
            hit = re.match(r"(x\d+)\[(\d+)\]", rest)
            if hit:
                indexable.append((hit.group(1), int(hit.group(2))))
            continue
        if op.startswith("dcl_constantbuffer"):
            m = re.match(r"CB(\d+)\[(\d+)\]", rest)
            if m:
                cbuffers[int(m.group(1))] = int(m.group(2))
            continue
        if op.startswith("dcl_input"):
            add_register(inputs, (args[0] if args else rest).split()[-1])
            continue
        if op.startswith("dcl_output"):
            add_register(outputs, (args[0] if args else rest).split()[-1])
            continue
        if op.startswith("dcl_resource_"):
            kind = op[len("dcl_resource_"):]
            reg = rest.split()[-1]
            named = rename(reg, cbs, slots, textures, parts).split(".")[0]
            if kind in ("texturecube", "texture3d", "texturecubearray"):
                cube_or_volume.add(named)
            resources.append((kind, reg))
            continue
        if op.startswith("dcl_sampler"):
            samplers.append(args[0] if args else rest)
            continue
        if op.startswith("dcl_") or op in ("ret",):
            if op == "ret":
                emit("return;")
            continue

        sat = op.endswith("_sat")
        base = op[:-4] if sat else op
        args = [rename(a, cbs, slots, textures, parts) for a in args]
        for a in args:
            hit = re.match(r"^-?cb(\d+)_(\d+)", a)
            if hit:
                unmapped.add("cb%s_%s" % hit.groups())

        def val(expr, width):
            if sat:
                return "saturate(%s)" % expr
            return expr

        def src(token, want):
            indices = want if isinstance(want, list) else list(range(want))
            if token.startswith("l(") or re.match(r"^[-|]*float\d\(", token):
                return literal(token, indices)
            return pick_swizzle(token, indices)

        if base in ("if_nz", "if_z"):
            emit("if (asint(%s) %s 0) {" % (src(args[0], 1),
                                            "!=" if base == "if_nz" else "=="))
            indent += 1
            continue
        if base == "else":
            indent -= 1
            emit("} else {")
            indent += 1
            continue
        if base in ("endif", "endloop", "endswitch"):
            indent -= 1
            emit("}")
            continue
        if base == "loop":
            emit("[loop] while (true) {")
            indent += 1
            continue
        if base == "break":
            emit("break;")
            continue
        if base in ("breakc_nz", "breakc_z"):
            emit("if (asint(%s) %s 0) break;" % (src(args[0], 1),
                                                 "!=" if base.endswith("nz") else "=="))
            continue
        if base in ("discard_nz", "discard_z", "discard"):
            if args:
                emit("if (asint(%s) %s 0) discard;" % (src(args[0], 1),
                                                       "!=" if base.endswith("nz") else "=="))
            else:
                emit("discard;")
            continue

        if not args:
            emit("// %s" % line)
            continue

        if base in ("imul", "umul") and len(args) >= 4:
            args = args[1:] if args[0] == "null" else [args[0]] + args[2:]

        # sincos writes two destinations, either of which may be null, so it cannot go through
        # the single-assignment path the other intrinsics use
        if base == "sincos" and len(args) >= 3:
            reg = args[2].split(".")[0].lstrip("-|")
            pairs = [(args[0], "sin"), (args[1], "cos")]
            # both outputs read the ORIGINAL source, so whichever destination shares the source's
            # register has to be written last or it feeds the other one its own result
            pairs.sort(key=lambda pair: pair[0].split(".")[0] == reg)
            for dest_arg, fn in pairs:
                if dest_arg == "null":
                    continue
                w = dest_indices(dest_arg)
                emit("%s = %s;" % (dest_arg, val("%s(%s)" % (fn, src(args[2], w)), w)))
            continue

        dest = args[0]
        width = dest_indices(dest)
        rhs = None

        if base == "mov":
            rhs = src(args[1], width)
        elif base == "movc":
            rhs = "(asint(%s) != 0) ? %s : %s" % (src(args[1], width), src(args[2], width),
                                                  src(args[3], width))
        elif base == "mad":
            rhs = "%s * %s + %s" % (src(args[1], width), src(args[2], width), src(args[3], width))
        elif base == "imad":
            rhs = "asfloat(asint(%s) * asint(%s) + asint(%s))" % (
                src(args[1], width), src(args[2], width), src(args[3], width))
        elif base in BINARY and len(args) >= 3:
            rhs = BINARY[base] % (src(args[1], width), src(args[2], width))
        elif base in INT_BINARY and len(args) >= 3:
            rhs = "asfloat(asint(%s) %s asint(%s))" % (src(args[1], width),
                                                       INT_BINARY[base], src(args[2], width))
        elif base in UINT_BINARY and len(args) >= 3:
            rhs = "asfloat(asuint(%s) %s asuint(%s))" % (src(args[1], width),
                                                         UINT_BINARY[base], src(args[2], width))
        elif base in BITCAST1 and len(args) >= 2:
            rhs = BITCAST1[base] % {"n": "" if len(width) == 1 else str(len(width)),
                                    "v": src(args[1], width)}
        elif base in INT_COMPARE and len(args) >= 3:
            rhs = "asfloat((asint(%s) %s asint(%s)) ? -1 : 0)" % (
                src(args[1], width), INT_COMPARE[base], src(args[2], width))
        elif base in UINT_COMPARE and len(args) >= 3:
            rhs = "asfloat((asuint(%s) %s asuint(%s)) ? -1 : 0)" % (
                src(args[1], width), UINT_COMPARE[base], src(args[2], width))
        elif base in ("dp2", "dp3", "dp4"):
            n = int(base[-1])
            rhs = "dot(%s, %s)" % (src(args[1], n), src(args[2], n))
        elif base in CALL2 and len(args) >= 3:
            rhs = "%s(%s, %s)" % (CALL2[base], src(args[1], width), src(args[2], width))
        elif base in CALL1:
            rhs = "%s(%s)" % (CALL1[base], src(args[1], width))
        elif base in COMPARE and len(args) >= 3:
            rhs = "asfloat((%s %s %s) ? -1 : 0)" % (src(args[1], width), COMPARE[base],
                                                    src(args[2], width))
        elif base.startswith("sample"):
            tex = (args[2] if len(args) > 2 else "t0").split(".")[0]
            smp = "sampler" + tex
            coord_width = 3 if tex in cube_or_volume else 2
            coord = src(args[1], coord_width)
            if base in ("sample", "sample_indexable"):
                rhs = "%s.Sample(%s, %s)" % (tex, smp, coord)
            elif base.startswith("sample_l"):
                rhs = "%s.SampleLevel(%s, %s, %s)" % (tex, smp, coord, src(args[4], 1)
                                                      if len(args) > 4 else "0")
            elif base.startswith("sample_b"):
                rhs = "%s.SampleBias(%s, %s, %s)" % (tex, smp, coord, src(args[4], 1)
                                                     if len(args) > 4 else "0")
            elif base.startswith("sample_d"):
                rhs = "%s.SampleGrad(%s, %s, %s, %s)" % (tex, smp, coord,
                                                         src(args[4], coord_width),
                                                         src(args[5], coord_width))
            elif base.startswith("sample_c"):
                rhs = "%s.SampleCmp(%s, %s, %s)" % (tex, smp, coord, src(args[4], 1)
                                                    if len(args) > 4 else "0")
            else:
                rhs = "%s.Sample(%s, %s)" % (tex, smp, coord)
        elif base == "ld" or base.startswith("ld_"):
            tex = args[2] if len(args) > 2 else "t0"
            rhs = "%s.Load(%s)" % (tex, src(args[1], 3))
        elif base == "nop":
            continue

        if rhs is None:
            emit("// unhandled: %s" % line)
            continue
        emit("%s = %s;" % (dest, val(rhs, width)))

    if temps:
        decls.append("        float4 %s;" % ", ".join("r%d" % i for i in range(temps)))
    for bank, count in indexable:
        decls.append("        float4 %s[%d];" % (bank, count))
    if icb_rows:
        entries = ", ".join("float4(%s)" % ", ".join(row) for row in icb_rows)
        decls.append("        const float4 icb[%d] = { %s };" % (len(icb_rows), entries))
    return {
        "body": body,
        "decls": decls,
        "inputs": inputs,
        "outputs": outputs,
        "resources": resources,
        "samplers": samplers,
        "cbuffers": cbuffers,
        "unmapped": sorted(unmapped),
        "cb_names": list(cbs),
        "slot_names": slots,
        "parts": parts or {},
        "texture_of": {reg: rename(reg, cbs, slots, textures, parts).split(".")[0]
                       for _, reg in resources},
        "used": set(unmapped),
    }


def variant_label(keywords):
    return "_".join(keywords) if keywords else "base"


def stage_variants(pass_obj, stage_attr, blob, platform_index):
    prog = getattr(pass_obj, stage_attr, None)
    if prog is None:
        return []
    groups = [g for g in (prog.m_PlayerSubPrograms or []) if g]
    if not groups:
        groups = [prog.m_SubPrograms] if prog.m_SubPrograms else []
    out = []
    for group in groups:
        for i, sub in enumerate(group):
            out.append((i, sub))
    params = prog.m_ParameterBlobIndices or []
    blob_params = None
    for entry in params:
        if entry:
            blob_params = list(entry)
            break
    return [(i, sub, (blob_params[i] if blob_params and i < len(blob_params) else None))
            for i, sub in out]


PROP_KINDS = {0: "Color", 1: "Vector", 2: "Float", 3: "Range", 4: "2D"}

TEX_DECL = {"texture2d": ("Texture2D", "2D"), "texture3d": ("Texture3D", "3D"),
            "texturecube": ("TextureCube", "Cube"),
            "texture2darray": ("Texture2DArray", "2DArray"),
            "texturecubearray": ("TextureCubeArray", "CubeArray")}

BLEND_NAMES = {0: "Zero", 1: "One", 2: "DstColor", 3: "SrcColor", 4: "OneMinusDstColor",
               5: "SrcAlpha", 6: "OneMinusSrcColor", 7: "DstAlpha", 8: "OneMinusDstAlpha",
               9: "SrcAlphaSaturate", 10: "OneMinusSrcAlpha"}

ZTEST_NAMES = {0: "Off", 1: "Never", 2: "Less", 3: "Equal", 4: "LEqual", 5: "Greater",
               6: "NotEqual", 7: "GEqual", 8: "Always"}

CULL_NAMES = {0: "Off", 1: "Front", 2: "Back"}

MATRIX_NAMES = ("unity_ObjectToWorld", "unity_WorldToObject", "unity_MatrixVP",
                "unity_MatrixV", "unity_MatrixInvV", "unity_MatrixP", "glstate_matrix_projection",
                "unity_ProbeVolumeWorldToObject", "unity_WorldToShadow", "unity_WorldToLight")


def num(value):
    text = "%.6g" % float(value)
    return text


def prop_line(prop):
    kind = PROP_KINDS.get(prop.m_Type, "Float")
    attrs = "".join("[%s] " % a for a in (prop.m_Attributes or []))
    name, desc = prop.m_Name, prop.m_Description or prop.m_Name
    d0, d1, d2, d3 = (prop.m_DefValue_0_, prop.m_DefValue_1_,
                      prop.m_DefValue_2_, prop.m_DefValue_3_)
    if prop.m_Type == 4:
        default = prop.m_DefTexture.m_DefaultName if prop.m_DefTexture else "white"
        return '        %s%s ("%s", %s) = "%s" {}' % (attrs, name, desc, kind,
                                                      default or "white")
    if prop.m_Type == 3:
        return '        %s%s ("%s", Range(%s, %s)) = %s' % (attrs, name, desc, num(d1),
                                                            num(d2), num(d0))
    if prop.m_Type == 2:
        return '        %s%s ("%s", Float) = %s' % (attrs, name, desc, num(d0))
    return '        %s%s ("%s", %s) = (%s,%s,%s,%s)' % (attrs, name, desc, kind, num(d0),
                                                        num(d1), num(d2), num(d3))


def properties_block(props, tex_kinds):
    lines = ["    Properties {"]
    for prop in props:
        line = prop_line(prop)
        if prop.m_Type == 4 and prop.m_Name in tex_kinds and tex_kinds[prop.m_Name] != "2D":
            line = line.replace(", 2D)", ", %s)" % tex_kinds[prop.m_Name])
        lines.append(line)
    lines.append("    }")
    return lines


def bound_property(value):
    name = getattr(value, "name", None)
    if not name:
        return None
    name = str(name).strip()
    if not name or name == "<noninit>":
        return None
    return "[" + name + "]"


def state_lines(state, indent="            "):
    out = []
    tags = dict(state.m_Tags.tags) if state.m_Tags else {}
    light = tags.get("LIGHTMODE") or tags.get("LightMode")
    if light:
        out.append('%sTags { "LightMode" = "%s" }' % (indent, light))

    blend = state.rtBlend0
    src_prop, dst_prop = bound_property(blend.srcBlend), bound_property(blend.destBlend)
    src, dst = int(blend.srcBlend.val), int(blend.destBlend.val)
    if src_prop or dst_prop or not (src == 1 and dst == 0):
        out.append("%sBlend %s %s" % (indent,
                                      src_prop or BLEND_NAMES.get(src, "One"),
                                      dst_prop or BLEND_NAMES.get(dst, "Zero")))

    zwrite = bound_property(state.zWrite)
    out.append("%sZWrite %s" % (indent, zwrite or ("On" if int(state.zWrite.val) else "Off")))

    ztest = bound_property(state.zTest)
    if ztest:
        out.append("%sZTest %s" % (indent, ztest))
    elif int(state.zTest.val) != 4:
        out.append("%sZTest %s" % (indent, ZTEST_NAMES.get(int(state.zTest.val), "LEqual")))

    cull = bound_property(state.culling)
    if cull:
        out.append("%sCull %s" % (indent, cull))
    elif int(state.culling.val) != 2:
        out.append("%sCull %s" % (indent, CULL_NAMES.get(int(state.culling.val), "Back")))

    mask_prop = bound_property(blend.colMask)
    mask = int(blend.colMask.val)
    if mask_prop:
        out.append("%sColorMask %s" % (indent, mask_prop))
    elif mask != 15:
        letters = "".join(c for bit, c in ((8, "R"), (4, "G"), (2, "B"), (1, "A"))
                          if mask & bit)
        out.append("%sColorMask %s" % (indent, letters or "0"))
    return out


def uniform_lines(result, textures, material_props):
    owned = set()
    for prop in material_props:
        owned.add(prop.m_Name)
        if prop.m_Type == 4:
            owned.add(prop.m_Name + "_ST")

    def ours(name):
        return name.split("[")[0] not in AUTO_DECLARED

    seen, lines, stubs = set(), [], []
    packed = {}

    def emit(target, decl, name):
        if name in seen:
            return
        seen.add(name)
        target.append("        %s %s;" % (decl, name))

    for index, size in sorted(result["cbuffers"].items()):
        cb_name = result["cb_names"][index] if index < len(result["cb_names"]) else None
        mapped = result["slot_names"].get(cb_name, {}) if cb_name else {}
        for slot in range(size):
            named = mapped.get(slot)
            if named:
                base = named.split("[")[0]
                decl = "float4x4" if (base in MATRIX_NAMES or "[" in named) else "float4"
                emit(lines if ours(base) else stubs, decl, base)
            else:
                fallback = "cb%d_%d" % (index, slot)
                if fallback in result["used"]:
                    packed[fallback] = (result.get("parts") or {}).get(cb_name, {}).get(slot)
                    if not packed[fallback]:
                        del packed[fallback]
                        emit(lines, "float4", fallback)

    for kind, reg in result["resources"]:
        name = result["texture_of"].get(reg, reg)
        if name in seen:
            continue
        seen.add(name)
        decl = TEX_DECL.get(kind, ("Texture2D", "2D"))[0]
        (lines if ours(name) else stubs).append("        %s %s;" % (decl, name))
        sampler = "sampler" + name
        (stubs if sampler in AUTO_SAMPLERS else lines).append(
            "        SamplerState %s;" % sampler)

    for table in (result.get("parts") or {}).values():
        for slot in table.values():
            for scalar, dim, _ in slot.values():
                decl = "float" if dim == 1 else "float%d" % dim
                emit(lines if ours(scalar) else stubs, decl, scalar)

    for prop in material_props:
        if prop.m_Type == 4:
            emit(lines, "float4", prop.m_Name + "_ST")

    for name, table in packed.items():
        parts_of = []
        for comp in range(4):
            entry = table.get(comp)
            parts_of.append(entry[0] if entry and entry[1] == 1 else "0")
        if all(p == "0" for p in parts_of):
            emit(lines, "float4", name)
            continue
        seen.add(name)
        lines.append("        static float4 %s = float4(%s);" % (name, ", ".join(parts_of)))

    return lines, stubs


def struct_lines(name, tokens, sig, kind):
    out = ["        struct %s {" % name]
    for token in tokens:
        reg_name = token.split(".")[0]
        reg = int("".join(c for c in reg_name if c.isdigit()) or 0)
        entry = sig.get(reg)
        if kind == "frag_out":
            semantic = "SV_Target%d" % reg if reg else "SV_Target"
        elif entry and entry[0].startswith("SV_"):
            semantic = entry[0]
        elif entry:
            semantic = "%s%d" % (entry[0], entry[1])
        elif kind == "v2f":
            semantic = "SV_POSITION" if reg == 0 else "TEXCOORD%d" % reg
        else:
            semantic = "TEXCOORD%d" % reg
        out.append("            float4 %s : %s;" % (reg_name, semantic))
    out.append("        };")
    return out


def interpolator_map(vp_sig, fp_sig):
    by_semantic = {}
    for reg, entry in (vp_sig.get("out") or {}).items():
        by_semantic[(entry[0].upper(), entry[1])] = reg
    out = {}
    for reg, entry in (fp_sig.get("in") or {}).items():
        target = by_semantic.get((entry[0].upper(), entry[1]))
        if target is not None and target != reg:
            out[reg] = target
    return out


def fog_target(state):
    blend = state.rtBlend0 if state else None
    src = int(blend.srcBlend.val) if blend else 1
    dst = int(blend.destBlend.val) if blend else 0
    if dst == 1 and src in (1, 3, 5, 9):
        return "float3(0, 0, 0)"
    if (src, dst) in ((2, 0), (0, 3)):
        return "float3(1, 1, 1)"
    return "unity_FogColor.rgb"


def reg_of(token):
    name = token.split(".")[0]
    return name, int("".join(c for c in name if c.isdigit()) or 0)


def semantic_reg(tokens, sig, semantic):
    for token in tokens:
        name, reg = reg_of(token)
        entry = sig.get(reg)
        if entry and entry[0].upper() == semantic:
            return name
    return None


def drop_ret(body):
    # the transpiled body closes on the DXBC ret, so anything appended after it is dead
    for i in range(len(body) - 1, -1, -1):
        if not body[i].strip():
            continue
        return body[:i] + body[i + 1:] if body[i].strip() == "return;" else body
    return body


def free_texcoord(tokens, sig):
    used = set()
    for token in tokens:
        _, reg = reg_of(token)
        entry = sig.get(reg)
        if entry and entry[0].upper() == "TEXCOORD":
            used.add(entry[1])
        elif not entry and reg:
            used.add(reg)
    return next(i for i in range(16) if i not in used)


def pass_program(vp, fp, vp_sig, fp_sig, textures, material_props, light="", state=None):
    vp_out = vp_sig.get("out", {})
    pos_reg = semantic_reg(vp["outputs"], vp_out, "SV_POSITION")
    color_reg = next((reg_of(t)[0] for t in fp["outputs"] if reg_of(t)[1] == 0), None)
    fog = bool(pos_reg and color_reg) and light.upper() not in ("SHADOWCASTER", "META")
    lines = ["        CGPROGRAM", "        #pragma vertex vert",
             "        #pragma fragment frag", "        #pragma target 4.0"]
    if fog:
        lines.append("        #pragma multi_compile_fog")
    lines.append("")
    decls, stubs, seen = [], [], set()
    for stage in (vp, fp):
        stage_decls, stage_stubs = uniform_lines(stage, textures, material_props)
        for line in stage_decls:
            if line.strip() not in seen:
                seen.add(line.strip())
                decls.append(line)
        for line in stage_stubs:
            if line.strip() not in seen:
                seen.add(line.strip())
                stubs.append(line)
    if fog:
        for decl in ("        float4 unity_FogColor;", "        float4 unity_FogParams;"):
            if decl.strip() not in seen:
                seen.add(decl.strip())
                stubs.append(decl)
    lines += decls
    lines.append("")
    lines += struct_lines("appdata", vp["inputs"], vp_sig.get("in", {}), "appdata")
    lines.append("")
    v2f = struct_lines("v2f", vp["outputs"], vp_out, "v2f")
    if fog:
        v2f.insert(-1, "            float fogCoord : TEXCOORD%d;"
                   % free_texcoord(vp["outputs"], vp_out))
    lines += v2f
    lines.append("")
    lines += struct_lines("fout", fp["outputs"], fp_sig.get("out", {}), "frag_out")
    lines.append("")
    lines.append("        void vert(appdata i, out v2f o) {")
    lines.append("            o = (v2f)0;")
    lines += ["    " + d for d in vp["decls"]]
    lines += ["    " + b for b in (drop_ret(vp["body"]) if fog else vp["body"])]
    if fog:
        # ponytail: clip w is the eye depth under a perspective camera; an
        # orthographic camera needs the _ProjectionParams z remap instead
        lines.append("            o.fogCoord = o.%s.w;" % pos_reg)
    lines.append("        }")
    lines.append("")
    lines.append("        void frag(v2f i, out fout o) {")
    lines.append("            o = (fout)0;")
    interp = interpolator_map(vp_sig, fp_sig)
    lines += ["    " + d for d in fp["decls"]]
    lines += ["    " + re.sub(r"i\.v(\d+)",
                              lambda m: "i.o%d" % interp.get(int(m.group(1)), int(m.group(1))), b)
              for b in (drop_ret(fp["body"]) if fog else fp["body"])]
    if fog:
        lines += ["        #if defined(FOG_LINEAR)",
                  "            float fogFac = i.fogCoord * unity_FogParams.z"
                  " + unity_FogParams.w;",
                  "        #elif defined(FOG_EXP)",
                  "            float fogFac = exp2(-unity_FogParams.y * i.fogCoord);",
                  "        #elif defined(FOG_EXP2)",
                  "            float fogExp = unity_FogParams.x * i.fogCoord;",
                  "            float fogFac = exp2(-fogExp * fogExp);",
                  "        #else",
                  "            float fogFac = 1.0;",
                  "        #endif",
                  "            o.%s.rgb = lerp(%s, o.%s.rgb, saturate(fogFac));"
                  % (color_reg, fog_target(state), color_reg)]
    lines.append("        }")
    lines.append("        ENDCG")
    return lines, stubs


def compile_check(source, entry, target):
    dll = disassembler()
    dll.D3DCompile.argtypes = [ctypes.c_char_p, ctypes.c_size_t, ctypes.c_char_p,
                               ctypes.c_void_p, ctypes.c_void_p, ctypes.c_char_p,
                               ctypes.c_char_p, ctypes.c_uint, ctypes.c_uint,
                               ctypes.POINTER(ctypes.c_void_p),
                               ctypes.POINTER(ctypes.c_void_p)]
    dll.D3DCompile.restype = ctypes.c_long
    raw = source.encode("utf-8")
    code = ctypes.c_void_p()
    errs = ctypes.c_void_p()
    hr = dll.D3DCompile(raw, len(raw), b"pass", None, None, entry.encode(),
                        target.encode(), 0, 0, ctypes.byref(code), ctypes.byref(errs))
    message = ""
    if errs:
        vt = ctypes.cast(errs, ctypes.POINTER(ctypes.POINTER(ctypes.c_void_p)))[0]
        ptr = ctypes.CFUNCTYPE(ctypes.c_void_p, ctypes.c_void_p)(vt[3])(errs)
        size = ctypes.CFUNCTYPE(ctypes.c_size_t, ctypes.c_void_p)(vt[4])(errs)
        message = ctypes.string_at(ptr, size).decode("utf-8", "replace")
    return hr == 0, message


def bare_program(lines):
    out = []
    for line in lines:
        text = line.strip()
        if text.startswith(("#pragma", "#include")) or text in ("CGPROGRAM", "ENDCG"):
            continue
        out.append(line)
    return "\n".join(out)


def write_shader(shader_obj, out_dir, guid, write_meta, safe, say=None):
    data = shader_obj.read()
    parsed = data.m_ParsedForm
    name = parsed.m_Name or ("Shader_%d" % shader_obj.path_id)
    root = os.path.join(out_dir, safe(name.replace("/", "_")) + "~")
    if DUMP:
        os.makedirs(root, exist_ok=True)

    platform_index = 0
    platform = data.platforms[platform_index] if data.platforms else 4
    blob = Blob(data, platform_index)
    known = known_cb_names(parsed)
    merged_whole, merged_parts = as_slots(*merged_params(parsed))

    report = ["shader: %s" % name,
              "platform: %s (%d)" % (PLATFORM_NAMES.get(platform, "unknown"), platform),
              "blob entries: %d" % len(blob.table),
              "keywords: %s" % ", ".join(parsed.m_KeywordNames or []),
              "fallback: %s" % (parsed.m_FallbackName or "none"),
              ""]

    tex_kinds = {}
    pass_blocks = []

    for si, sub in enumerate(parsed.m_SubShaders):
        for pi, pass_obj in enumerate(sub.m_Passes):
            ni = pass_obj.m_NameIndices
            names = {v: k for k, v in (ni.items() if isinstance(ni, dict) else ni)}
            state = pass_obj.m_State
            pass_name = (state.m_Name if state else "") or "Pass%d" % pi
            tags = dict(state.m_Tags.tags) if state and state.m_Tags else {}
            light = tags.get("LIGHTMODE") or tags.get("LightMode") or ""
            label = safe("%d_%s%s" % (pi, pass_name, ("_" + light) if light else ""))
            pass_dir = os.path.join(root, label)
            best = {}

            for stage_attr, tag in STAGES:
                variants = stage_variants(pass_obj, stage_attr, blob, platform_index)
                if not variants:
                    continue
                prog = getattr(pass_obj, stage_attr)
                fallback_cbs = cb_order(prog.m_CommonParameters, names) \
                    if prog.m_CommonParameters else []
                cb_pool = cb_candidates(prog.m_CommonParameters, names) if prog.m_CommonParameters else []
                anchors = stage_anchors(prog, names)
                base_whole, base_parts = as_slots(*stage_params(prog, names))
                if base_whole or base_parts:
                    fill_gaps(base_whole, base_parts, merged_whole, merged_parts)
                else:
                    base_whole, base_parts = merged_whole, merged_parts
                stage_dir = os.path.join(pass_dir, tag)
                if DUMP:
                    os.makedirs(stage_dir, exist_ok=True)

                for vi, sub_prog, param_index in variants:
                    code = blob.code(sub_prog.m_BlobIndex)
                    dxbc = dxbc_of(code)
                    keywords = blob.keywords(sub_prog.m_BlobIndex)
                    stem = os.path.join(stage_dir, safe(variant_label(keywords)))
                    if DUMP:
                        with open(stem + ".dxbc", "wb") as fh:
                            fh.write(dxbc if dxbc else code)
                    if dxbc is None:
                        report.append("%s %s %s: no DXBC payload"
                                      % (label, tag, variant_label(keywords)))
                        continue
                    asm = disassemble(dxbc)
                    if asm is None:
                        report.append("%s %s %s: disassembly failed"
                                      % (label, tag, variant_label(keywords)))
                        continue
                    if DUMP:
                        with open(stem + ".asm", "w", encoding="utf-8") as fh:
                            fh.write(asm)

                    kinds = re.findall(r"dcl_resource_(\w+)[^\n]*?(t\d+)", asm)
                    dims = [TEX_DIMS.get(k, 2) for k, _ in kinds]
                    ncb = 0
                    for hit in re.findall(r"dcl_constantbuffer CB(\d+)", asm):
                        ncb = max(ncb, int(hit) + 1)
                    regs = [int(reg[1:]) for _, reg in kinds]
                    bound = stage_textures(prog, names)
                    textures = None
                    if bound and all(r in bound for r in regs):
                        textures = [bound.get(r) for r in range(max(list(bound) + regs) + 1)]
                    cbs = fallback_cbs
                    slots, parts = combine(base_whole, base_parts, {})
                    if param_index is not None:
                        payload = blob.payload(param_index)
                        if dims and textures is None:
                            textures = texture_names(payload, dims, known | set(cbs))
                        if ncb:
                            found = blob_cb_order(payload, ncb, known | ENGINE_CBS)
                            if len(found) == ncb:
                                cbs = found
                        groups = material_constants(payload, parsed.m_PropInfo.m_Props,
                                                    known | set(cbs), anchors)
                        if groups is None:
                            report.append("%s %s %s: parameter offsets disagreed with the "
                                          "binding table, left unnamed"
                                          % (label, tag, variant_label(keywords)))
                        else:
                            slots, parts = combine(base_whole, base_parts, groups)
                    if textures is None and dims:
                        declared = [p.m_Name for p in parsed.m_PropInfo.m_Props
                                    if p.m_Type == 4]
                        if len(declared) == len(dims):
                            textures = declared
                            report.append("%s %s %s: textures named from the shader's own "
                                          "property list" % (label, tag,
                                                             variant_label(keywords)))
                    for cb_name, layout in BUILTIN_CB.items():
                        for slot, nm in layout.items():
                            slots.setdefault(cb_name, {}).setdefault(slot, nm)
                    result = transpile(asm, cbs, slots, textures, parts)
                    sig = signatures(asm)
                    if DUMP:
                        with open(stem + ".hlsl", "w", encoding="utf-8") as fh:
                            fh.write(render_hlsl(name, pass_name, tag, keywords, result,
                                                 textures, cbs, slots, sig))
                    if textures:
                        for kind, reg in kinds:
                            r = int(reg[1:])
                            tex = textures[r] if r < len(textures) else None
                            if tex:
                                tex_kinds[tex] = TEX_DECL.get(kind, ("Texture2D", "2D"))[1]
                    if result["unmapped"]:
                        report.append("%s %s %s: unnamed constants %s"
                                      % (label, tag, variant_label(keywords),
                                         " ".join(result["unmapped"])))
                    if textures is None and dims:
                        report.append("%s %s %s: %d textures left as registers"
                                      % (label, tag, variant_label(keywords), len(dims)))
                    rank = (0 if "LIGHTPROBE_SH" in keywords else 1, len(keywords))
                    if tag not in best or rank < best[tag][0]:
                        best[tag] = (rank, result, sig)

            if "vp" in best and "fp" in best:
                program, stubs = pass_program(best["vp"][1], best["fp"][1], best["vp"][2],
                                              best["fp"][2], tex_kinds,
                                              parsed.m_PropInfo.m_Props, light, state)
                body = "\n".join(stubs) + "\n" + bare_program(program)
                ok_v, err_v = compile_check(body, "vert", "vs_4_0")
                ok_f, err_f = compile_check(body, "frag", "ps_4_0")
                if ok_v and ok_f:
                    pass_blocks.append((pass_name, state, program))
                    blind = sorted({hit.group(1) for line in program
                                    for hit in [re.match(r"\s*float4 (cb\d+_\d+);", line)]
                                    if hit})
                    if blind:
                        report.append("%s: %d constant slot(s) this pass reads could not be "
                                      "named and will be zero: %s"
                                      % (label, len(blind), " ".join(blind)))
                else:
                    with open(os.path.join(root, label + ".failed.hlsl"), "w",
                              encoding="utf-8") as fh:
                        fh.write("\n".join("%4d %s" % (n, line) for n, line
                                           in enumerate(body.splitlines(), 1)))
                    report.append("%s: generated pass did not compile, left out of the "
                                  ".shader (source in %s.failed.hlsl)" % (label, label))
                    for line in (err_v + err_f).splitlines()[:6]:
                        report.append("    " + line.strip())

    lab_name = "%s_%s" % (name, guid[:6])
    lab_lines = ['Shader "%s" {' % lab_name]
    lab_lines += properties_block(parsed.m_PropInfo.m_Props, tex_kinds)
    sub_tags = dict(parsed.m_SubShaders[0].m_Tags.tags) if parsed.m_SubShaders else {}
    queue = sub_tags.get("QUEUE", "Geometry")
    render_type = sub_tags.get("RenderType", "Opaque")
    lab_lines.append("    SubShader {")
    lab_lines.append('        Tags { "Queue" = "%s" "RenderType" = "%s" }' % (queue, render_type))
    if not pass_blocks:
        report.append("no pass was recovered, a placeholder pass was written so the .shader "
                      "still parses and the material keeps its properties")
        lab_lines.append("")
        lab_lines.append("        Pass {")
        lab_lines += ["            CGPROGRAM",
                      "            #pragma vertex vert",
                      "            #pragma fragment frag",
                      "            struct appdata { float4 vertex : POSITION; };",
                      "            struct v2f { float4 pos : SV_POSITION; };",
                      "            void vert(appdata i, out v2f o) {",
                      "                float4 world = mul(transpose(unity_ObjectToWorld), i.vertex);",
                      "                o.pos = mul(transpose(unity_MatrixVP), world);",
                      "            }",
                      "            void frag(v2f i, out float4 col : SV_Target) {",
                      "                col = float4(1, 1, 1, 1);",
                      "            }",
                      "            ENDCG"]
        lab_lines.append("        }")

    for pass_name, state, program in pass_blocks:
        lab_lines.append("")
        lab_lines.append("        Pass {")
        lab_lines.append('            Name "%s"' % pass_name.upper())
        lab_lines += state_lines(state)
        lab_lines.append("")
        lab_lines += ["    " + line for line in program]
        lab_lines.append("        }")
    lab_lines.append("    }")
    if parsed.m_FallbackName:
        lab_lines.append('    Fallback "%s"' % parsed.m_FallbackName)
    lab_lines.append("}")

    lab = os.path.join(out_dir, safe(name.replace("/", "_")) + ".shader")
    with open(lab, "w", encoding="utf-8") as fh:
        fh.write("\n".join(lab_lines) + "\n")
    write_meta(lab, guid, "ShaderImporter:\n  externalObjects: {}\n  defaultTextures: []\n")

    if DUMP:
        with open(os.path.join(root, "shaderlab.original.txt"), "w", encoding="utf-8") as fh:
            fh.write(data.export())
    report.insert(1, "shaderlab name: %s" % lab_name)
    report.insert(6, "passes emitted into the .shader: %d of %d"
                  % (len(pass_blocks), sum(len(s.m_Passes) for s in parsed.m_SubShaders)))
    if DUMP:
        with open(os.path.join(root, "report.txt"), "w", encoding="utf-8") as fh:
            fh.write("\n".join(report) + "\n")
    if say:
        say("decompiled %s, %d pass(es) rebuilt" % (name, len(pass_blocks)))
    return lab


def render_hlsl(shader_name, pass_name, stage, keywords, result, textures, cbs, slots, sig):
    nl = "\n"
    out = ['// %s' % shader_name,
           '// pass %s, stage %s, variant %s' % (pass_name or "?", stage,
                                                 variant_label(keywords)),
           '// recovered from shipped DXBC; names come from the shader binding tables',
           ""]
    for index, size in sorted(result["cbuffers"].items()):
        cb_name = cbs[index] if index < len(cbs) else "cb%d" % index
        mapped = slots.get(cb_name, {})
        out.append("cbuffer %s {" % cb_name)
        for slot in range(size):
            nm = mapped.get(slot)
            if nm and "[" not in nm:
                out.append("    float4 %s;" % nm)
            else:
                out.append("    float4 cb%d_%d;" % (index, slot))
        out.append("};")
        out.append("")
    for i, (kind, reg) in enumerate(result["resources"]):
        r = int(reg[1:]) if reg[1:].isdigit() else i
        nm = (textures[r] if textures and r < len(textures) and textures[r] else reg)
        hlsl_type = {"texture2d": "Texture2D", "texture3d": "Texture3D",
                     "texturecube": "TextureCube", "texture2darray": "Texture2DArray",
                     "texturecubearray": "TextureCubeArray"}.get(kind, "Texture2D")
        out.append("%s %s;" % (hlsl_type, nm))
        out.append("SamplerState sampler_%s;" % nm)
    if result["resources"]:
        out.append("")
    for kind, label, table in (("inputs", "Input", sig.get("in", {})),
                               ("outputs", "Output", sig.get("out", {}))):
        out.append("struct %s {" % label)
        for token in result[kind]:
            reg_name = token.split(".")[0]
            reg = int(re.sub(r"[^0-9]", "", reg_name) or 0)
            entry = table.get(reg)
            if entry:
                semantic = entry[0] if entry[0].startswith("SV_") else "%s%d" % (entry[0], entry[1])
            else:
                semantic = ("SV_Target%d" % reg) if kind == "outputs" else "TEXCOORD%d" % reg
            out.append("    float4 %s : %s;" % (reg_name, semantic))
        out.append("};")
        out.append("")
    out.pop()
    out.append("")
    out.append("void main(Input i, out Output o) {")
    out.extend(result["decls"])
    out.extend(result["body"])
    out.append("}")
    return nl.join(out) + nl
