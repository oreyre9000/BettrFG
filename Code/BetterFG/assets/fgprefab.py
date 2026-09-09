import hashlib
import os
import struct
import sys

import UnityPy
import fgshader
from fgmesh import load_mesh

WORK = ""

CLASS_IDS = {
    "GameObject": 1,
    "Transform": 4,
    "RectTransform": 224,
    "MeshRenderer": 23,
    "MeshFilter": 33,
    "SkinnedMeshRenderer": 137,
    "ParticleSystem": 198,
    "ParticleSystemRenderer": 199,
    "TrailRenderer": 96,
    "LineRenderer": 120,
    "Light": 108,
    "Animator": 95,
    "Animation": 111,
    "BoxCollider": 65,
    "SphereCollider": 135,
    "CapsuleCollider": 136,
    "MeshCollider": 64,
    "Rigidbody": 54,
    "AudioSource": 82,
    "Camera": 20,
    "ReflectionProbe": 215,
    "LightProbeGroup": 220,
    "LODGroup": 205,
    "OcclusionArea": 192,
    "NavMeshObstacle": 208,
}

# components we can round-trip faithfully; anything else is skipped rather than emitted broken
KEEP = set(CLASS_IDS.keys()) - {"GameObject"}


TEXTURE_META = """TextureImporter:
  internalIDToNameTable: []
  externalObjects: {}
  serializedVersion: 12
  mipmaps:
    mipMapMode: 0
    enableMipMap: 1
    sRGBTexture: %(srgb)s
    linearTexture: 0
    fadeOut: 0
    borderMipMap: 0
    mipMapsPreserveCoverage: 0
    alphaTestReferenceValue: 0.5
    mipMapFadeDistanceStart: 1
    mipMapFadeDistanceEnd: 3
  bumpmap:
    convertToNormalMap: 0
    externalNormalMap: 0
    heightScale: 0.25
    normalMapFilter: 0
  isReadable: 0
  streamingMipmaps: 0
  streamingMipmapsPriority: 0
  vTOnly: 0
  ignoreMasterTextureLimit: 0
  grayScaleToAlpha: 0
  generateCubemap: 6
  cubemapConvolution: 0
  seamlessCubemap: 0
  textureFormat: 1
  maxTextureSize: 2048
  textureSettings:
    serializedVersion: 2
    filterMode: 1
    aniso: 1
    mipBias: 0
    wrapU: 0
    wrapV: 0
    wrapW: 0
  nPOTScale: 1
  lightmap: 0
  compressionQuality: 50
  spriteMode: 0
  spriteExtrude: 1
  spriteMeshType: 1
  alignment: 0
  spritePivot: {x: 0.5, y: 0.5}
  spritePixelsToUnits: 100
  spriteBorder: {x: 0, y: 0, z: 0, w: 0}
  spriteGenerateFallbackPhysicsShape: 1
  alphaUsage: 1
  alphaIsTransparency: 0
  spriteTessellationDetail: -1
  textureType: 0
  textureShape: 1
  singleChannelComponent: 0
  flipbookRows: 1
  flipbookColumns: 1
  maxTextureSizeSet: 0
  compressionQualitySet: 0
  textureFormatSet: 0
  ignorePngGamma: 0
  applyGammaDecoding: 0
  cookieLightType: 0
  platformSettings:
  - serializedVersion: 3
    buildTarget: DefaultTexturePlatform
    maxTextureSize: 2048
    resizeAlgorithm: 0
    textureFormat: -1
    textureCompression: 2
    compressionQuality: 50
    crunchedCompression: 0
    allowsAlphaSplitting: 0
    overridden: 0
    androidETC2FallbackOverride: 0
    forceMaximumCompressionQuality_BC6H_BC7: 0
  - serializedVersion: 3
    buildTarget: Standalone
    maxTextureSize: 2048
    resizeAlgorithm: 0
    textureFormat: -1
    textureCompression: 2
    compressionQuality: 50
    crunchedCompression: 0
    allowsAlphaSplitting: 0
    overridden: 0
    androidETC2FallbackOverride: 0
    forceMaximumCompressionQuality_BC6H_BC7: 0
  - serializedVersion: 3
    buildTarget: Server
    maxTextureSize: 2048
    resizeAlgorithm: 0
    textureFormat: -1
    textureCompression: 2
    compressionQuality: 50
    crunchedCompression: 0
    allowsAlphaSplitting: 0
    overridden: 0
    androidETC2FallbackOverride: 0
    forceMaximumCompressionQuality_BC6H_BC7: 0
  spriteSheet:
    serializedVersion: 2
    sprites: []
    outline: []
    physicsShape: []
    bones: []
    spriteID: 
    internalID: 0
    vertices: []
    indices: 
    edges: []
    weights: []
    secondaryTextures: []
    nameFileIdTable: {}
  spritePackingTag: 
  pSDRemoveMatte: 0
  pSDShowRemoveMatteOption: 0
  userData: 
  assetBundleName: 
  assetBundleVariant: 
"""


def say(msg):
    print(msg, flush=True)
    try:
        with open(os.environ.get("BETTRFG_PROGRESS") or os.path.join(WORK, "progress.txt"),
                  "w", encoding="utf-8") as fh:
            fh.write(msg)
    except OSError:
        pass


BUILTIN_SHADERS = {
    'Autodesk Interactive': (47, "0000000000000000f000000000000000"),
    'FX/Flare': (101, "0000000000000000f000000000000000"),
    'GUI/Text Shader': (10101, "0000000000000000e000000000000000"),
    'Hidden/BlitCopy': (66, "0000000000000000f000000000000000"),
    'Hidden/BlitCopyDepth': (67, "0000000000000000f000000000000000"),
    'Hidden/BlitCopyHDRTonemap': (111, "0000000000000000f000000000000000"),
    'Hidden/BlitCopyWithDepth': (107, "0000000000000000f000000000000000"),
    'Hidden/BlitToDepth': (109, "0000000000000000f000000000000000"),
    'Hidden/BlitToDepth_MSAA': (110, "0000000000000000f000000000000000"),
    'Hidden/Compositing': (17000, "0000000000000000f000000000000000"),
    'Hidden/ConvertTexture': (68, "0000000000000000f000000000000000"),
    'Hidden/CubeBlend': (15106, "0000000000000000f000000000000000"),
    'Hidden/CubeBlur': (15104, "0000000000000000f000000000000000"),
    'Hidden/CubeCopy': (15105, "0000000000000000f000000000000000"),
    'Hidden/FrameDebuggerRenderTargetDisplay': (10755, "0000000000000000e000000000000000"),
    'Hidden/GIDebug/ShowLightMask': (15101, "0000000000000000f000000000000000"),
    'Hidden/GIDebug/TextureUV': (15100, "0000000000000000f000000000000000"),
    'Hidden/GIDebug/UV1sAsPositions': (15102, "0000000000000000f000000000000000"),
    'Hidden/GIDebug/VertexColors': (15103, "0000000000000000f000000000000000"),
    'Hidden/GraphView/GraphViewUIE': (9104, "0000000000000000f000000000000000"),
    'Hidden/Internal-Colored': (69, "0000000000000000e000000000000000"),
    'Hidden/Internal-CombineDepthNormals': (65, "0000000000000000f000000000000000"),
    'Hidden/Internal-CubemapToEquirect': (15309, "0000000000000000f000000000000000"),
    'Hidden/Internal-DeferredReflections': (74, "0000000000000000f000000000000000"),
    'Hidden/Internal-DeferredShading': (69, "0000000000000000f000000000000000"),
    'Hidden/Internal-DepthNormalsTexture': (62, "0000000000000000f000000000000000"),
    'Hidden/Internal-Flare': (102, "0000000000000000f000000000000000"),
    'Hidden/Internal-GUIRoundedRect': (9004, "0000000000000000f000000000000000"),
    'Hidden/Internal-GUIRoundedRectWithColorPerBorder': (9007, "0000000000000000f000000000000000"),
    'Hidden/Internal-GUITexture': (9002, "0000000000000000f000000000000000"),
    'Hidden/Internal-GUITextureBlit': (9003, "0000000000000000f000000000000000"),
    'Hidden/Internal-GUITextureClip': (9000, "0000000000000000f000000000000000"),
    'Hidden/Internal-GUITextureClipText': (9001, "0000000000000000f000000000000000"),
    'Hidden/Internal-Halo': (105, "0000000000000000f000000000000000"),
    'Hidden/Internal-Loading': (70, "0000000000000000e000000000000000"),
    'Hidden/Internal-MotionVectors': (75, "0000000000000000f000000000000000"),
    'Hidden/Internal-ODSWorldTexture': (15308, "0000000000000000f000000000000000"),
    'Hidden/Internal-PrePassLighting': (63, "0000000000000000f000000000000000"),
    'Hidden/Internal-ScreenSpaceShadows': (64, "0000000000000000f000000000000000"),
    'Hidden/Internal-StencilWrite': (19, "0000000000000000f000000000000000"),
    'Hidden/Internal-UIE-ColorConversionBlit': (9105, "0000000000000000f000000000000000"),
    'Hidden/Internal-UIRAtlasBlitCopy': (9101, "0000000000000000f000000000000000"),
    'Hidden/Internal-UIRDefault': (9100, "0000000000000000f000000000000000"),
    'Hidden/Internal-UIRDefaultWorld': (9102, "0000000000000000f000000000000000"),
    'Hidden/InternalClear': (68, "0000000000000000e000000000000000"),
    'Hidden/InternalErrorShader': (17, "0000000000000000e000000000000000"),
    'Hidden/Nature/Terrain/Utilities': (10490, "0000000000000000f000000000000000"),
    'Hidden/Nature/Tree Creator Albedo Rendertex': (10608, "0000000000000000f000000000000000"),
    'Hidden/Nature/Tree Creator Bark Optimized': (10604, "0000000000000000f000000000000000"),
    'Hidden/Nature/Tree Creator Bark Rendertex': (10602, "0000000000000000f000000000000000"),
    'Hidden/Nature/Tree Creator Leaves Fast Optimized': (10607, "0000000000000000f000000000000000"),
    'Hidden/Nature/Tree Creator Leaves Optimized': (10605, "0000000000000000f000000000000000"),
    'Hidden/Nature/Tree Creator Leaves Rendertex': (10603, "0000000000000000f000000000000000"),
    'Hidden/Nature/Tree Creator Normal Rendertex': (10609, "0000000000000000f000000000000000"),
    'Hidden/Nature/Tree Soft Occlusion Bark Rendertex': (10508, "0000000000000000f000000000000000"),
    'Hidden/Nature/Tree Soft Occlusion Leaves Rendertex': (10510, "0000000000000000f000000000000000"),
    'Hidden/TerrainEngine/BillboardTree': (10507, "0000000000000000f000000000000000"),
    'Hidden/TerrainEngine/BrushPreview': (18004, "0000000000000000f000000000000000"),
    'Hidden/TerrainEngine/CameraFacingBillboardTree': (10513, "0000000000000000f000000000000000"),
    'Hidden/TerrainEngine/CrossBlendNeighbors': (18005, "0000000000000000f000000000000000"),
    'Hidden/TerrainEngine/Details/BillboardWavingDoublePass': (10502, "0000000000000000f000000000000000"),
    'Hidden/TerrainEngine/Details/Vertexlit': (10500, "0000000000000000f000000000000000"),
    'Hidden/TerrainEngine/Details/WavingDoublePass': (10501, "0000000000000000f000000000000000"),
    'Hidden/TerrainEngine/GenerateNormalmap': (18002, "0000000000000000f000000000000000"),
    'Hidden/TerrainEngine/HeightBlitCopy': (18001, "0000000000000000f000000000000000"),
    'Hidden/TerrainEngine/PaintHeight': (18000, "0000000000000000f000000000000000"),
    'Hidden/TerrainEngine/Splatmap/Diffuse-AddPass': (10503, "0000000000000000f000000000000000"),
    'Hidden/TerrainEngine/Splatmap/Diffuse-Base': (10504, "0000000000000000f000000000000000"),
    'Hidden/TerrainEngine/Splatmap/Diffuse-BaseGen': (10506, "0000000000000000f000000000000000"),
    'Hidden/TerrainEngine/Splatmap/Specular-AddPass': (10621, "0000000000000000f000000000000000"),
    'Hidden/TerrainEngine/Splatmap/Specular-Base': (10622, "0000000000000000f000000000000000"),
    'Hidden/TerrainEngine/Splatmap/Standard-AddPass': (10624, "0000000000000000f000000000000000"),
    'Hidden/TerrainEngine/Splatmap/Standard-Base': (10625, "0000000000000000f000000000000000"),
    'Hidden/TerrainEngine/Splatmap/Standard-BaseGen': (10626, "0000000000000000f000000000000000"),
    'Hidden/TerrainEngine/TerrainBlitCopyZWrite': (18006, "0000000000000000f000000000000000"),
    'Hidden/TerrainEngine/TerrainLayerUtils': (18003, "0000000000000000f000000000000000"),
    'Hidden/TextCore/Distance Field': (19010, "0000000000000000f000000000000000"),
    'Hidden/TextCore/Distance Field SSD': (19011, "0000000000000000f000000000000000"),
    'Hidden/TextCore/Sprite': (19012, "0000000000000000f000000000000000"),
    'Hidden/UI/CompositeOverdraw': (10784, "0000000000000000f000000000000000"),
    'Hidden/UI/Overdraw': (10785, "0000000000000000f000000000000000"),
    'Hidden/UIElements/EditorUIE': (9103, "0000000000000000f000000000000000"),
    'Hidden/VR/BlitFromTex2DToTexArraySlice': (15312, "0000000000000000f000000000000000"),
    'Hidden/VR/BlitTexArraySlice': (15304, "0000000000000000f000000000000000"),
    'Hidden/VR/BlitTexArraySliceToDepth': (15306, "0000000000000000f000000000000000"),
    'Hidden/VR/BlitTexArraySliceToDepth_MSAA': (15307, "0000000000000000f000000000000000"),
    'Hidden/VR/Internal-VRDistortion': (15305, "0000000000000000f000000000000000"),
    'Hidden/VideoComposite': (16000, "0000000000000000f000000000000000"),
    'Hidden/VideoDecode': (16001, "0000000000000000f000000000000000"),
    'Hidden/VideoDecodeAndroid': (16003, "0000000000000000f000000000000000"),
    'Hidden/VideoDecodeOSX': (16002, "0000000000000000f000000000000000"),
    'Legacy Shaders/Bumped Diffuse': (2, "0000000000000000f000000000000000"),
    'Legacy Shaders/Bumped Specular': (4, "0000000000000000f000000000000000"),
    'Legacy Shaders/Decal': (100, "0000000000000000f000000000000000"),
    'Legacy Shaders/Diffuse': (7, "0000000000000000f000000000000000"),
    'Legacy Shaders/Diffuse Detail': (5, "0000000000000000f000000000000000"),
    'Legacy Shaders/Diffuse Fast': (1, "0000000000000000f000000000000000"),
    'Legacy Shaders/Lightmapped/Bumped Diffuse': (42, "0000000000000000f000000000000000"),
    'Legacy Shaders/Lightmapped/Bumped Specular': (44, "0000000000000000f000000000000000"),
    'Legacy Shaders/Lightmapped/Diffuse': (41, "0000000000000000f000000000000000"),
    'Legacy Shaders/Lightmapped/Specular': (43, "0000000000000000f000000000000000"),
    'Legacy Shaders/Lightmapped/VertexLit': (40, "0000000000000000f000000000000000"),
    'Legacy Shaders/Parallax Diffuse': (8, "0000000000000000f000000000000000"),
    'Legacy Shaders/Parallax Specular': (9, "0000000000000000f000000000000000"),
    'Legacy Shaders/Particles/Additive': (200, "0000000000000000f000000000000000"),
    'Legacy Shaders/Particles/Additive (Soft)': (202, "0000000000000000f000000000000000"),
    'Legacy Shaders/Particles/Alpha Blended': (203, "0000000000000000f000000000000000"),
    'Legacy Shaders/Particles/Alpha Blended Premultiply': (207, "0000000000000000f000000000000000"),
    'Legacy Shaders/Particles/Anim Alpha Blended': (209, "0000000000000000f000000000000000"),
    'Legacy Shaders/Particles/Multiply': (205, "0000000000000000f000000000000000"),
    'Legacy Shaders/Particles/Multiply (Double)': (206, "0000000000000000f000000000000000"),
    'Legacy Shaders/Particles/VertexLit Blended': (208, "0000000000000000f000000000000000"),
    'Legacy Shaders/Particles/~Additive-Multiply': (201, "0000000000000000f000000000000000"),
    'Legacy Shaders/Reflective/Bumped Diffuse': (21, "0000000000000000f000000000000000"),
    'Legacy Shaders/Reflective/Bumped Specular': (23, "0000000000000000f000000000000000"),
    'Legacy Shaders/Reflective/Bumped Unlit': (25, "0000000000000000f000000000000000"),
    'Legacy Shaders/Reflective/Bumped VertexLit': (26, "0000000000000000f000000000000000"),
    'Legacy Shaders/Reflective/Diffuse': (20, "0000000000000000f000000000000000"),
    'Legacy Shaders/Reflective/Parallax Diffuse': (27, "0000000000000000f000000000000000"),
    'Legacy Shaders/Reflective/Parallax Specular': (28, "0000000000000000f000000000000000"),
    'Legacy Shaders/Reflective/Specular': (22, "0000000000000000f000000000000000"),
    'Legacy Shaders/Reflective/VertexLit': (24, "0000000000000000f000000000000000"),
    'Legacy Shaders/Self-Illumin/Bumped Diffuse': (11, "0000000000000000f000000000000000"),
    'Legacy Shaders/Self-Illumin/Bumped Specular': (13, "0000000000000000f000000000000000"),
    'Legacy Shaders/Self-Illumin/Diffuse': (10, "0000000000000000f000000000000000"),
    'Legacy Shaders/Self-Illumin/Parallax Diffuse': (15, "0000000000000000f000000000000000"),
    'Legacy Shaders/Self-Illumin/Parallax Specular': (16, "0000000000000000f000000000000000"),
    'Legacy Shaders/Self-Illumin/Specular': (12, "0000000000000000f000000000000000"),
    'Legacy Shaders/Self-Illumin/VertexLit': (14, "0000000000000000f000000000000000"),
    'Legacy Shaders/Specular': (3, "0000000000000000f000000000000000"),
    'Legacy Shaders/Transparent/Bumped Diffuse': (31, "0000000000000000f000000000000000"),
    'Legacy Shaders/Transparent/Bumped Specular': (33, "0000000000000000f000000000000000"),
    'Legacy Shaders/Transparent/Cutout/Bumped Diffuse': (52, "0000000000000000f000000000000000"),
    'Legacy Shaders/Transparent/Cutout/Bumped Specular': (54, "0000000000000000f000000000000000"),
    'Legacy Shaders/Transparent/Cutout/Diffuse': (51, "0000000000000000f000000000000000"),
    'Legacy Shaders/Transparent/Cutout/Soft Edge Unlit': (10512, "0000000000000000f000000000000000"),
    'Legacy Shaders/Transparent/Cutout/Specular': (53, "0000000000000000f000000000000000"),
    'Legacy Shaders/Transparent/Cutout/VertexLit': (50, "0000000000000000f000000000000000"),
    'Legacy Shaders/Transparent/Diffuse': (30, "0000000000000000f000000000000000"),
    'Legacy Shaders/Transparent/Parallax Diffuse': (35, "0000000000000000f000000000000000"),
    'Legacy Shaders/Transparent/Parallax Specular': (36, "0000000000000000f000000000000000"),
    'Legacy Shaders/Transparent/Specular': (32, "0000000000000000f000000000000000"),
    'Legacy Shaders/Transparent/VertexLit': (34, "0000000000000000f000000000000000"),
    'Legacy Shaders/VertexLit': (6, "0000000000000000f000000000000000"),
    'Mobile/Bumped Diffuse': (10704, "0000000000000000f000000000000000"),
    'Mobile/Bumped Specular': (10705, "0000000000000000f000000000000000"),
    'Mobile/Bumped Specular (1 Directional Realtime Light)': (10706, "0000000000000000f000000000000000"),
    'Mobile/Diffuse': (10703, "0000000000000000f000000000000000"),
    'Mobile/Particles/Additive': (10720, "0000000000000000f000000000000000"),
    'Mobile/Particles/Alpha Blended': (10721, "0000000000000000f000000000000000"),
    'Mobile/Particles/Multiply': (10723, "0000000000000000f000000000000000"),
    'Mobile/Particles/VertexLit Blended': (10722, "0000000000000000f000000000000000"),
    'Mobile/Skybox': (10700, "0000000000000000f000000000000000"),
    'Mobile/Unlit (Supports Lightmap)': (10708, "0000000000000000f000000000000000"),
    'Mobile/VertexLit': (10701, "0000000000000000f000000000000000"),
    'Mobile/VertexLit (Only Directional Lights)': (10707, "0000000000000000f000000000000000"),
    'Nature/SpeedTree': (14000, "0000000000000000f000000000000000"),
    'Nature/SpeedTree Billboard': (14001, "0000000000000000f000000000000000"),
    'Nature/SpeedTree8': (14002, "0000000000000000f000000000000000"),
    'Nature/Terrain/Diffuse': (10505, "0000000000000000f000000000000000"),
    'Nature/Terrain/Specular': (10620, "0000000000000000f000000000000000"),
    'Nature/Terrain/Standard': (10623, "0000000000000000f000000000000000"),
    'Nature/Tree Creator Bark': (10600, "0000000000000000f000000000000000"),
    'Nature/Tree Creator Leaves': (10601, "0000000000000000f000000000000000"),
    'Nature/Tree Creator Leaves Fast': (10606, "0000000000000000f000000000000000"),
    'Nature/Tree Soft Occlusion Bark': (10509, "0000000000000000f000000000000000"),
    'Nature/Tree Soft Occlusion Leaves': (10511, "0000000000000000f000000000000000"),
    'Particles/Standard Surface': (210, "0000000000000000f000000000000000"),
    'Particles/Standard Unlit': (211, "0000000000000000f000000000000000"),
    'Skybox/6 Sided': (104, "0000000000000000f000000000000000"),
    'Skybox/Cubemap': (103, "0000000000000000f000000000000000"),
    'Skybox/Panoramic': (108, "0000000000000000f000000000000000"),
    'Skybox/Procedural': (106, "0000000000000000f000000000000000"),
    'Sprites/Default': (10753, "0000000000000000f000000000000000"),
    'Sprites/Diffuse': (10800, "0000000000000000f000000000000000"),
    'Sprites/Mask': (10757, "0000000000000000f000000000000000"),
    'Standard': (46, "0000000000000000f000000000000000"),
    'Standard (Specular setup)': (45, "0000000000000000f000000000000000"),
    'UI/Default': (10770, "0000000000000000f000000000000000"),
    'UI/Default Font': (10782, "0000000000000000f000000000000000"),
    'UI/DefaultETC1': (10783, "0000000000000000f000000000000000"),
    'UI/Lit/Bumped': (10765, "0000000000000000f000000000000000"),
    'UI/Lit/Detail': (10766, "0000000000000000f000000000000000"),
    'UI/Lit/Refraction': (10767, "0000000000000000f000000000000000"),
    'UI/Lit/Refraction Detail': (10768, "0000000000000000f000000000000000"),
    'UI/Lit/Transparent': (10764, "0000000000000000f000000000000000"),
    'UI/Unlit/Detail': (10761, "0000000000000000f000000000000000"),
    'UI/Unlit/Text': (10762, "0000000000000000f000000000000000"),
    'UI/Unlit/Text Detail': (10763, "0000000000000000f000000000000000"),
    'UI/Unlit/Transparent': (10760, "0000000000000000f000000000000000"),
    'Unlit/Color': (10755, "0000000000000000f000000000000000"),
    'Unlit/Texture': (10752, "0000000000000000f000000000000000"),
    'Unlit/Transparent': (10750, "0000000000000000f000000000000000"),
    'Unlit/Transparent Cutout': (10751, "0000000000000000f000000000000000"),
    'VR/SpatialMapping/Occlusion': (15300, "0000000000000000f000000000000000"),
    'VR/SpatialMapping/Wireframe': (15301, "0000000000000000f000000000000000"),
}


BUILTIN_GUIDS = {
    "unity default resources": "0000000000000000e000000000000000",
    "unity_builtin_extra": "0000000000000000f000000000000000",
    "resources/unity_builtin_extra": "0000000000000000f000000000000000",
}


def builtin_guid(path):
    leaf = str(path or "").replace("\\", "/").lower()
    if leaf in BUILTIN_GUIDS:
        return BUILTIN_GUIDS[leaf]
    return BUILTIN_GUIDS.get(leaf.rsplit("/", 1)[-1])


def guid_for(seed):
    return hashlib.md5(seed.encode("utf-8")).hexdigest()


def safe(name):
    out = "".join(c if (c.isalnum() or c in "._-") else "_" for c in (name or ""))
    return out or "unnamed"


def is_pptr(v):
    return isinstance(v, dict) and "m_PathID" in v and "m_FileID" in v


def fmt_scalar(v):
    if isinstance(v, bool):
        return "1" if v else "0"
    if isinstance(v, float):
        if v != v or v in (float("inf"), float("-inf")):
            return "0"
        s = "%.6g" % v
        return s
    if isinstance(v, int):
        return str(v)
    if isinstance(v, (bytes, bytearray)):
        return "".join("%02x" % b for b in v)
    s = str(v)
    if s == "":
        return "''"
    if any(c in s for c in ":#{}[],&*?|>-'\"%@`") or s[0].isdigit() or s in ("yes", "no", "true", "false", "null"):
        return "'" + s.replace("'", "''") + "'"
    return s


VEC_KEYSETS = (
    ("x", "y"),
    ("x", "y", "z"),
    ("x", "y", "z", "w"),
    ("r", "g", "b", "a"),
    ("e00",),
)


def flow_dict(d):
    """Unity writes vectors, colours and pointers inline; the block form its parser accepts for
    plain records is not what it expects here"""
    keys = tuple(d.keys())
    if keys in VEC_KEYSETS[:4]:
        return "{" + ", ".join("%s: %s" % (k, fmt_scalar(d[k])) for k in keys) + "}"
    return None


def map_keys(obj):
    names = set()
    try:
        stack = [obj.serialized_type.node]
    except Exception:
        return names
    while stack:
        node = stack.pop()
        if node.m_Type == "map":
            names.add(node.m_Name)
        stack.extend(node.m_Children)
    return names


def version_map(obj):
    names, root_version = {}, 1
    try:
        root = obj.serialized_type.node
    except Exception:
        return names, root_version
    root_version = getattr(root, "m_Version", 1) or 1

    stack = [root]
    while stack:
        node = stack.pop()
        for child in node.m_Children:
            version = getattr(child, "m_Version", 1) or 1
            if version > 1:
                names[child.m_Name] = version
            kids = child.m_Children
            if kids and kids[0].m_Type == "Array" and len(kids[0].m_Children) > 1:
                element = kids[0].m_Children[1]
                ev = getattr(element, "m_Version", 1) or 1
                if ev > 1:
                    names[child.m_Name + "[]"] = ev
            stack.append(child)
    return names, root_version


class Writer:
    """emits Unity's YAML dialect from UnityPy typetrees, remapping every PPtr as it goes.
    list items sit at the same indent as their key and vectors go inline, matching what Unity
    itself writes; its reader is a strict subset parser, not a general YAML one."""

    def __init__(self, resolve, maps=(), versions=None):
        self.lines = []
        self.resolve = resolve
        self.maps = set(maps)
        self.versions = versions or {}

    def empty(self, key):
        return "{}" if key in self.maps else "[]"

    def value_str(self, v):
        if is_pptr(v):
            return self.resolve(v)
        if isinstance(v, dict):
            if not v:
                return "{}"
            return flow_dict(v)
        return None

    def emit(self, key, value, indent):
        pad = "  " * indent
        inline = self.value_str(value)
        if inline is not None:
            self.lines.append("%s%s: %s" % (pad, key, inline))
            return
        if isinstance(value, dict):
            self.lines.append("%s%s:" % (pad, key))
            v = self.versions.get(key)
            if v:
                self.lines.append("%s  serializedVersion: %d" % (pad, v))
            self.block(value, indent + 1)
            return
        if isinstance(value, list):
            if not value:
                self.lines.append("%s%s: %s" % (pad, key, self.empty(key)))
                return
            self.lines.append("%s%s:" % (pad, key))
            self.seq(value, indent, self.versions.get(key + "[]"))
            return
        self.lines.append("%s%s: %s" % (pad, key, fmt_scalar(value)))

    def seq(self, values, indent, item_version=None):
        pad = "  " * indent
        for item in values:
            if isinstance(item, tuple) and len(item) == 2 and isinstance(item[0], str):
                k, v = item
                inline = self.value_str(v)
                if inline is not None:
                    self.lines.append("%s- %s: %s" % (pad, k, inline))
                elif isinstance(v, dict):
                    self.lines.append("%s- %s:" % (pad, k))
                    self.block(v, indent + 2)
                elif isinstance(v, list):
                    if not v:
                        self.lines.append("%s- %s: %s" % (pad, k, self.empty(k)))
                        continue
                    self.lines.append("%s- %s:" % (pad, k))
                    self.seq(v, indent + 2)
                else:
                    self.lines.append("%s- %s: %s" % (pad, k, fmt_scalar(v)))
                continue

            inline = self.value_str(item)
            if inline is not None:
                self.lines.append("%s- %s" % (pad, inline))
            elif isinstance(item, dict):
                first = True
                if item_version:
                    self.lines.append("%s- serializedVersion: %d" % (pad, item_version))
                    first = False
                for k, v in item.items():
                    lead = "- " if first else "  "
                    first = False
                    sub = self.value_str(v)
                    if sub is not None:
                        self.lines.append("%s%s%s: %s" % (pad, lead, k, sub))
                    elif isinstance(v, dict):
                        self.lines.append("%s%s%s:" % (pad, lead, k))
                        self.block(v, indent + 2)
                    elif isinstance(v, list):
                        if not v:
                            self.lines.append("%s%s%s: %s" % (pad, lead, k, self.empty(k)))
                            continue
                        self.lines.append("%s%s%s:" % (pad, lead, k))
                        self.seq(v, indent + 2)
                    else:
                        self.lines.append("%s%s%s: %s" % (pad, lead, k, fmt_scalar(v)))
            else:
                self.lines.append("%s- %s" % (pad, fmt_scalar(item)))

    def block(self, d, indent):
        for k, v in d.items():
            self.emit(k, v, indent)


def cab_index_path():
    base = os.environ.get("LOCALAPPDATA") or os.path.expanduser("~")
    folder = os.path.join(base, "BettrFG")
    os.makedirs(folder, exist_ok=True)
    return os.path.join(folder, "cab_index.txt")


def load_cab_index(bundle_dir):
    """maps a serialized-file (CAB) name to the bundle holding it, so a pointer into another
    bundle can be followed. built once and cached beside the mesh index."""
    path = cab_index_path()
    table = {}
    if os.path.exists(path):
        for line in open(path, encoding="utf-8"):
            parts = line.rstrip("\n").split("\t")
            if len(parts) == 2:
                table.setdefault(parts[0], parts[1])
        if table:
            return table

    files = sorted(f for f in os.listdir(bundle_dir) if f.endswith(".bundle"))
    say("indexing %d bundles for cross-references, one off" % len(files))
    with open(path, "w", encoding="utf-8") as out:
        for n, f in enumerate(files):
            try:
                env = UnityPy.load(os.path.join(bundle_dir, f))
                for cab in env.files:
                    out.write("%s\t%s\n" % (cab, f))
                    table.setdefault(cab, f)
                del env
            except Exception:
                continue
            if n % 50 == 0:
                say("cross-reference index, %d of %d" % (n, len(files)))
    return table


class Exporter:
    def __init__(self, env, work, name, bundle_dir=None, cabs=None):
        self.env = env
        self.work = work
        self.name = name
        self.bundle_dir = bundle_dir
        self.cabs = cabs or {}
        self.envs = {}
        self.by_id = {o.path_id: o for o in env.objects}
        self.local = {}
        self.next_id = 1000
        self.materials = {}
        self.textures = {}
        self.meshes = {}
        self.shaders = {}
        self.root_tr_id = None
        self.pending = []
        self.unresolved = []
        self.current_file = None
        self.file_maps = {}
        self.seen_objs = {}

    def lid(self, path_id):
        if path_id not in self.local:
            self.next_id += 1
            self.local[path_id] = self.next_id
        return self.local[path_id]

    def deref(self, ptr, owner=None):
        if ptr is None or ptr.get("m_PathID", 0) == 0:
            return None
        fid = ptr.get("m_FileID", 0)
        if fid != 0:
            return self.deref_external(ptr, fid, owner)

        # file id 0 means "my own file", and for a material pulled out of another bundle that is
        # not the prefab's file, so the lookup has to follow whoever owns the pointer
        if owner is None or owner is self.env.file:
            return self.by_id.get(ptr["m_PathID"])
        return self.in_file(owner, ptr["m_PathID"])

    def in_file(self, src, path_id):
        key = id(src)
        table = self.file_maps.get(key)
        if table is None:
            table = {}
            try:
                for o in src.objects.values() if hasattr(src.objects, "values") else src.objects:
                    table[o.path_id] = o
            except Exception:
                pass
            self.file_maps[key] = table
        return table.get(path_id)

    def deref_external(self, ptr, fid, owner):
        """a material or texture usually lives in a different bundle than the prefab that uses it"""
        src = owner if owner is not None else self.env.file
        try:
            externals = src.externals
        except Exception:
            return None
        if fid - 1 >= len(externals):
            return None
        cab = os.path.basename(str(getattr(externals[fid - 1], "path", "")).replace("\\", "/"))
        bundle = self.cabs.get(cab)
        if bundle is None or self.bundle_dir is None:
            return None
        if bundle not in self.envs:
            if len(self.envs) > 6:
                self.envs.clear()
            try:
                self.envs[bundle] = UnityPy.load(os.path.join(self.bundle_dir, bundle))
            except Exception:
                return None
        for o in self.envs[bundle].objects:
            if o.path_id == ptr["m_PathID"]:
                return o
        return None

    def resolve(self, ptr):
        """a pointer either stays inside this prefab file or becomes an external asset reference"""
        obj = self.deref(ptr, self.current_file)
        if obj is None:
            if ptr.get("m_PathID"):
                fid = ptr.get("m_FileID", 0)
                if fid:
                    try:
                        ext = self.current_file.externals[fid - 1]
                        g = builtin_guid(getattr(ext, "path", ""))
                    except Exception:
                        g = None
                    if g:
                        return "{fileID: %d, guid: %s, type: 0}" % (ptr["m_PathID"], g)
                where = ""
                try:
                    fid = ptr.get("m_FileID", 0)
                    if fid:
                        ext = self.current_file.externals[fid - 1]
                        where = os.path.basename(str(ext.path).replace("\\", "/"))
                except Exception:
                    pass
                self.unresolved.append((ptr.get("m_PathID"), where))
            return "{fileID: 0}"

        t = obj.type.name
        if t == "Material":
            return self.material_ref(obj)
        if t == "Texture2D":
            return self.texture_ref(obj)
        if t == "Mesh":
            return self.mesh_ref(obj)
        if t == "Shader":
            return self.shader_ref(obj)
        if obj.path_id in self.local or t in KEEP or t == "GameObject":
            return "{fileID: %d}" % self.lid(obj.path_id)
        return "{fileID: 0}"

    # ---- external assets ----

    def tag(self, kind, obj):
        return "%s:%s:%d" % (kind, getattr(obj.assets_file, "name", ""), obj.path_id)

    def stamp(self, tag):
        return guid_for(tag)[:6]

    def material_ref(self, obj):
        t = self.tag("mat", obj)
        if t not in self.materials:
            self.materials[t] = obj
            self.pending.append(("material", obj))
        return "{fileID: 2100000, guid: %s, type: 2}" % self.guid(t)

    def texture_ref(self, obj):
        t = self.tag("tex", obj)
        if t not in self.textures:
            self.textures[t] = obj
            self.pending.append(("texture", obj))
        return "{fileID: 2800000, guid: %s, type: 3}" % self.guid(t)

    def shader_ref(self, obj):
        try:
            builtin = BUILTIN_SHADERS.get(obj.read().m_ParsedForm.m_Name)
        except Exception:
            builtin = None
        if builtin:
            return "{fileID: %d, guid: %s, type: 0}" % builtin
        t = self.tag("shader", obj)
        if t not in self.shaders:
            self.shaders[t] = obj
            self.pending.append(("shader", obj))
        return "{fileID: 4800000, guid: %s, type: 3}" % self.guid(t)

    def mesh_ref(self, obj):
        t = self.tag("mesh", obj)
        if t not in self.meshes:
            self.meshes[t] = obj
            self.pending.append(("mesh", obj))
        return "{fileID: 4300000, guid: %s, type: 2}" % self.guid(t)

    # ---- prefab body ----

    def walk(self, root_tr_id, out):
        stack = [root_tr_id]
        seen = set()
        while stack:
            tr_id = stack.pop()
            if tr_id in seen:
                continue
            seen.add(tr_id)
            tr = self.seen_objs.get(tr_id) or self.by_id.get(tr_id)
            if tr is None:
                continue
            tr_tt = tr.read_typetree()
            go = self.deref(tr_tt.get("m_GameObject"), tr.assets_file)
            if go is None:
                continue
            out.append((go, tr))
            for child in tr_tt.get("m_Children", []) or []:
                c = self.deref(child, tr.assets_file)
                if c is not None:
                    self.seen_objs[c.path_id] = c
                    stack.append(c.path_id)

    def run_material(self, mat):
        """no prefab involved, just the material and everything it points at"""
        out = os.path.join(self.work, "Materials")
        os.makedirs(out, exist_ok=True)
        self.materials[self.tag("mat", mat)] = mat
        self.write_material(mat, out)
        self.flush_assets()
        return True

    def follow_pool(self, root_go):
        """a pool wrapper carries no children of its own, just a script pointing at the real prefab"""
        tt = root_go.read_typetree()
        for comp in tt.get("m_Component", []) or []:
            c = self.deref(comp.get("component") if isinstance(comp, dict) else comp, root_go.assets_file)
            if c is None or c.type.name != "MonoBehaviour":
                continue
            try:
                mb = c.read_typetree()
            except Exception:
                continue
            for k, v in mb.items():
                if k in ("m_GameObject", "m_Script") or not is_pptr(v) or not v.get("m_PathID"):
                    continue
                target = self.deref(v, c.assets_file)
                if target is None:
                    continue
                root = self.root_of(target)
                if root is not None:
                    say("%s is a pool, following %s to %s" % (self.name, k, root.peek_name()))
                    return root
        return None

    def root_of(self, obj):
        """the pool can point at a component rather than the object, so step out to whatever
        GameObject owns it and then climb to the top of that prefab"""
        go = obj
        if obj.type.name != "GameObject":
            try:
                owner = obj.read_typetree().get("m_GameObject")
            except Exception:
                return None
            go = self.deref(owner, obj.assets_file)
            if go is None or go.type.name != "GameObject":
                return None

        for _ in range(32):
            tr = None
            try:
                comps = go.read_typetree().get("m_Component", []) or []
            except Exception:
                return go
            for comp in comps:
                c = self.deref(comp.get("component") if isinstance(comp, dict) else comp, go.assets_file)
                if c is not None and c.type.name in ("Transform", "RectTransform"):
                    tr = c
                    break
            if tr is None:
                return go
            father = self.deref(tr.read_typetree().get("m_Father"), tr.assets_file)
            if father is None:
                return go
            parent_go = self.deref(father.read_typetree().get("m_GameObject"), father.assets_file)
            if parent_go is None:
                return go
            go = parent_go
        return go

    def run(self, root_go):
        go_tt = root_go.read_typetree()
        root_tr = None
        for comp in go_tt.get("m_Component", []) or []:
            c = self.deref(comp.get("component") if isinstance(comp, dict) else comp, root_go.assets_file)
            if c is not None and c.type.name in ("Transform", "RectTransform"):
                root_tr = c
                self.seen_objs[c.path_id] = c
                break
        if root_tr is None:
            say("that prefab has no transform")
            return False

        self.root_tr_id = root_tr.path_id
        nodes = []
        self.walk(root_tr.path_id, nodes)

        if len(nodes) <= 1:
            target = self.follow_pool(root_go)
            if target is not None:
                self.local.clear()
                self.next_id = 1000
                self.seen_objs.clear()
                return self.run(target)

        say("%d object(s) in %s" % (len(nodes), self.name))

        # assign ids first so every cross-reference resolves no matter the emit order
        for go, tr in nodes:
            self.lid(go.path_id)
            self.lid(tr.path_id)
            for comp in go.read_typetree().get("m_Component", []) or []:
                c = self.deref(comp.get("component") if isinstance(comp, dict) else comp, go.assets_file)
                if c is not None and c.type.name in KEEP:
                    self.lid(c.path_id)

        lines = ["%YAML 1.1", "%TAG !u! tag:unity3d.com,2011:"]
        for go, tr in nodes:
            lines += self.emit_gameobject(go)
            for comp in go.read_typetree().get("m_Component", []) or []:
                c = self.deref(comp.get("component") if isinstance(comp, dict) else comp, go.assets_file)
                if c is None or c.type.name not in KEEP:
                    continue
                lines += self.emit_component(c)

        path = os.path.join(self.work, safe(self.name) + ".prefab")
        with open(path, "w", encoding="utf-8") as fh:
            fh.write("\n".join(lines) + "\n")
        write_meta(path, self.guid("prefab:" + self.name), "PrefabImporter:\n  externalObjects: {}\n")

        self.flush_assets()
        say("wrote %s" % os.path.basename(path))
        return True

    def emit_gameobject(self, go):
        tt = go.read_typetree()
        comps = []
        for comp in tt.get("m_Component", []) or []:
            c = self.deref(comp.get("component") if isinstance(comp, dict) else comp, go.assets_file)
            if c is not None and c.type.name in KEEP:
                comps.append("  - component: {fileID: %d}" % self.lid(c.path_id))

        out = [
            "--- !u!1 &%d" % self.lid(go.path_id),
            "GameObject:",
            "  m_ObjectHideFlags: 0",
            "  m_CorrespondingSourceObject: {fileID: 0}",
            "  m_PrefabInstance: {fileID: 0}",
            "  m_PrefabAsset: {fileID: 0}",
            "  serializedVersion: 6",
            "  m_Component:" if comps else "  m_Component: []",
        ]
        out += comps
        out += [
            "  m_Layer: %d" % (tt.get("m_Layer") or 0),
            "  m_Name: %s" % fmt_scalar(tt.get("m_Name") or "GameObject"),
            "  m_TagString: Untagged",
            "  m_Icon: {fileID: 0}",
            "  m_NavMeshLayer: 0",
            "  m_StaticEditorFlags: 0",
            "  m_IsActive: %d" % (1 if tt.get("m_IsActive") else 0),
        ]
        return out

    def emit_component(self, comp):
        t = comp.type.name
        cid = CLASS_IDS[t]
        tt = comp.read_typetree()

        self.current_file = comp.assets_file
        versions, root_version = version_map(comp)
        w = Writer(self.resolve, map_keys(comp), versions)
        skip = {"m_ObjectHideFlags", "m_CorrespondingSourceObject", "m_PrefabInstance", "m_PrefabAsset"}
        for k, v in tt.items():
            if k in skip:
                continue
            w.emit(k, v, 1)

        if root_version > 1:
            at = 0
            for i, line in enumerate(w.lines):
                if line.startswith("  m_GameObject:"):
                    at = i + 1
                    break
            w.lines.insert(at, "  serializedVersion: %d" % root_version)

        # the bundle has this prefab parented under something we never emit, so the root's father
        # would dangle and Unity rejects the whole file with "Local file identifier doesn't exist"
        if comp.path_id == self.root_tr_id:
            for i, line in enumerate(w.lines):
                if line.startswith("  m_Father:"):
                    w.lines[i] = "  m_Father: {fileID: 0}"
                    break

        return [
            "--- !u!%d &%d" % (cid, self.lid(comp.path_id)),
            "%s:" % t,
            "  m_ObjectHideFlags: 0",
            "  m_CorrespondingSourceObject: {fileID: 0}",
            "  m_PrefabInstance: {fileID: 0}",
            "  m_PrefabAsset: {fileID: 0}",
        ] + w.lines

    # ---- side assets ----

    def guid(self, seed):
        return guid_for("%s|%s" % (self.name, seed))

    def flush_assets(self):
        mat_dir = os.path.join(self.work, "Materials")
        tex_dir = os.path.join(self.work, "Textures")
        i = 0
        while i < len(self.pending):
            kind, obj = self.pending[i]
            i += 1
            try:
                if kind == "material":
                    os.makedirs(mat_dir, exist_ok=True)
                    self.write_material(obj, mat_dir)
                elif kind == "mesh":
                    mesh_dir = os.path.join(self.work, "Meshes")
                    os.makedirs(mesh_dir, exist_ok=True)
                    self.write_mesh(obj, mesh_dir)
                elif kind == "shader":
                    shader_dir = os.path.join(self.work, "Shaders")
                    os.makedirs(shader_dir, exist_ok=True)
                    self.write_shader(obj, shader_dir)
                else:
                    os.makedirs(tex_dir, exist_ok=True)
                    self.write_texture(obj, tex_dir)
            except Exception as err:
                print("  %s %s failed: %r" % (kind, obj.path_id, err), flush=True)
        del self.pending[:]
        say("%d material(s), %d texture(s), %d shader(s), %d mesh(es)"
            % (len(self.materials), len(self.textures), len(self.shaders), len(self.meshes)))
        if self.unresolved:
            seen = sorted(set(self.unresolved))
            print("  %d reference(s) could not be followed and were written as none:"
                  % len(seen), flush=True)
            for path_id, where in seen[:20]:
                print("    pathID %d%s" % (path_id, (" in " + where) if where else ""), flush=True)

    def write_material(self, obj, out_dir):
        self.current_file = obj.assets_file
        tt = dict(obj.read_typetree())
        saved = tt.get("m_SavedProperties")
        if isinstance(saved, dict) and "serializedVersion" not in saved:
            fixed = {"serializedVersion": 3}
            fixed.update(saved)
            tt["m_SavedProperties"] = fixed
        name = safe(tt.get("m_Name") or ("Material_%d" % obj.path_id))
        w = Writer(self.resolve, map_keys(obj))
        for k, v in tt.items():
            if k in ("m_ObjectHideFlags", "m_CorrespondingSourceObject", "m_PrefabInstance", "m_PrefabAsset"):
                continue
            w.emit(k, v, 1)

        lines = ["%YAML 1.1", "%TAG !u! tag:unity3d.com,2011:",
                 "--- !u!21 &2100000", "Material:", "  serializedVersion: 8",
                 "  m_ObjectHideFlags: 0",
                 "  m_CorrespondingSourceObject: {fileID: 0}",
                 "  m_PrefabInstance: {fileID: 0}",
                 "  m_PrefabAsset: {fileID: 0}"] + w.lines
        t = self.tag("mat", obj)
        path = os.path.join(out_dir, name + "_%s.mat" % self.stamp(t))
        with open(path, "w", encoding="utf-8") as fh:
            fh.write("\n".join(lines) + "\n")
        write_meta(path, self.guid(t),
                   "NativeFormatImporter:\n  externalObjects: {}\n  mainObjectFileID: 2100000\n")

    def write_mesh(self, obj, out_dir):
        """FG ships these packed into m_CompressedMesh, so the raw typetree carries an empty vertex
        buffer. MeshHandler unpacks it the same way the fbx path does, and the result is written back
        out as a plain uncompressed mesh."""
        self.current_file = obj.assets_file
        tt = obj.read_typetree()
        name = safe(tt.get("m_Name") or ("Mesh_%d" % obj.path_id))

        h = load_mesh(obj)

        verts = h.m_Vertices or []
        n = h.m_VertexCount or len(verts)
        if n == 0 or not verts:
            print("  mesh %s decompressed to nothing" % name, flush=True)
            return

        bidx = getattr(h, "m_BoneIndices", None) or []
        bwt = getattr(h, "m_BoneWeights", None) or []
        skinned = len(bidx) >= n and len(bwt) >= n

        tris = h.get_triangles()
        if tris and not isinstance(tris[0], list):
            tris = [tris]

        # the vertex shaders read tangents, vertex colour and the higher UVs as well as
        # position/normal/uv0; a channel the mesh does not carry stays at dimension 0
        colors = getattr(h, "m_Colors", None)
        if colors and max((max(c[:4]) for c in colors[:64]), default=0.0) > 1.0:
            colors = [tuple(x / 255.0 for x in c) for c in colors]

        streams = [(1, 3, 0.0, h.m_Normals),
                   (2, 4, 1.0, getattr(h, "m_Tangents", None)),
                   (3, 4, 1.0, colors)]
        streams += [(4 + i, 2, 0.0, getattr(h, "m_UV%d" % i, None)) for i in range(8)]
        streams = [(c, w, pad, src) for c, w, pad, src in streams if src and len(src) >= n]

        buf = bytearray()
        for i in range(n):
            v = verts[i]
            buf += struct.pack("<3f", v[0], v[1], v[2])
            for _c, w, pad, src in streams:
                buf += struct.pack("<%df" % w,
                                   *[float(x) for x in (list(src[i]) + [pad] * w)[:w]])
            if skinned:
                wv = bwt[i]
                iv = bidx[i]
                buf += struct.pack("<4f", *[float(x) for x in (list(wv) + [0, 0, 0, 0])[:4]])
                buf += struct.pack("<4I", *[int(x) & 0xFFFFFFFF for x in (list(iv) + [0, 0, 0, 0])[:4]])

        # channel table is fixed length; only the ones actually present get a dimension
        off = 0
        chan = [{"stream": 0, "offset": 0, "format": 0, "dimension": 0} for _ in range(14)]
        chan[0] = {"stream": 0, "offset": off, "format": 0, "dimension": 3}; off += 12
        for c, w, _pad, _src in streams:
            chan[c] = {"stream": 0, "offset": off, "format": 0, "dimension": w}; off += w * 4
        if skinned:
            chan[12] = {"stream": 0, "offset": off, "format": 0, "dimension": 4}; off += 16
            chan[13] = {"stream": 0, "offset": off, "format": 10, "dimension": 4}; off += 16

        wide = n > 65535
        idx_bytes = bytearray()
        src_subs = tt.get("m_SubMeshes") or []
        subs = []
        first_byte = 0
        for si, sub in enumerate(tris):
            flat = [i for tri in sub for i in tri]
            for i in flat:
                idx_bytes += struct.pack("<I" if wide else "<H", i)
            lo = min(flat) if flat else 0
            hi = max(flat) if flat else 0
            sv = [verts[i] for i in flat] or [(0, 0, 0)]
            cx = [p[0] for p in sv]; cy = [p[1] for p in sv]; cz = [p[2] for p in sv]
            centre = ((min(cx) + max(cx)) / 2, (min(cy) + max(cy)) / 2, (min(cz) + max(cz)) / 2)
            ext = ((max(cx) - min(cx)) / 2, (max(cy) - min(cy)) / 2, (max(cz) - min(cz)) / 2)
            box = {"m_Center": {"x": centre[0], "y": centre[1], "z": centre[2]},
                   "m_Extent": {"x": ext[0], "y": ext[1], "z": ext[2]}}
            if si < len(src_subs) and isinstance(src_subs[si], dict):
                box = src_subs[si].get("localAABB") or box
            subs.append({
                "serializedVersion": 2,
                "firstByte": first_byte,
                "indexCount": len(flat),
                "topology": 0,
                "baseVertex": 0,
                "firstVertex": lo,
                "vertexCount": hi - lo + 1,
                "localAABB": box,
            })
            first_byte += len(flat) * (4 if wide else 2)

        xs = [p[0] for p in verts]; ys = [p[1] for p in verts]; zs = [p[2] for p in verts]
        aabb = {"m_Center": {"x": (min(xs) + max(xs)) / 2, "y": (min(ys) + max(ys)) / 2,
                             "z": (min(zs) + max(zs)) / 2},
                "m_Extent": {"x": (max(xs) - min(xs)) / 2, "y": (max(ys) - min(ys)) / 2,
                             "z": (max(zs) - min(zs)) / 2}}

        mesh = {
            "serializedVersion": 10,
            "m_Name": tt.get("m_Name") or name,
            "m_SubMeshes": subs,
            "m_Shapes": {"vertices": [], "shapes": [], "channels": [], "fullWeights": []},
            "m_BindPose": tt.get("m_BindPose") or [],
            "m_BoneNameHashes": tt.get("m_BoneNameHashes") or [],
            "m_RootBoneNameHash": tt.get("m_RootBoneNameHash") or 0,
            "m_BonesAABB": tt.get("m_BonesAABB") or [],
            "m_VariableBoneCountWeights": tt.get("m_VariableBoneCountWeights") or {"m_Data": []},
            "m_MeshCompression": 0,
            "m_IsReadable": 1,
            "m_KeepVertices": 1,
            "m_KeepIndices": 1,
            "m_IndexFormat": 1 if wide else 0,
            "m_IndexBuffer": bytes(idx_bytes),
            "m_VertexData": {
                "serializedVersion": 3,
                "m_VertexCount": n,
                "m_Channels": chan,
                "m_DataSize": len(buf),
                "_typelessdata": bytes(buf),
            },
            "m_CompressedMesh": {
                "m_Vertices": {"m_NumItems": 0, "m_Range": 0, "m_Start": 0, "m_Data": b"", "m_BitSize": 0},
                "m_UV": {"m_NumItems": 0, "m_Range": 0, "m_Start": 0, "m_Data": b"", "m_BitSize": 0},
                "m_Normals": {"m_NumItems": 0, "m_Range": 0, "m_Start": 0, "m_Data": b"", "m_BitSize": 0},
                "m_Tangents": {"m_NumItems": 0, "m_Range": 0, "m_Start": 0, "m_Data": b"", "m_BitSize": 0},
                "m_Weights": {"m_NumItems": 0, "m_Data": b"", "m_BitSize": 0},
                "m_NormalSigns": {"m_NumItems": 0, "m_Data": b"", "m_BitSize": 0},
                "m_TangentSigns": {"m_NumItems": 0, "m_Data": b"", "m_BitSize": 0},
                "m_FloatColors": {"m_NumItems": 0, "m_Range": 0, "m_Start": 0, "m_Data": b"", "m_BitSize": 0},
                "m_BoneIndices": {"m_NumItems": 0, "m_Data": b"", "m_BitSize": 0},
                "m_Triangles": {"m_NumItems": 0, "m_Data": b"", "m_BitSize": 0},
                "m_UVInfo": 0,
            },
            "m_LocalAABB": tt.get("m_LocalAABB") or aabb,
            "m_MeshUsageFlags": tt.get("m_MeshUsageFlags") or 0,
            "m_CookingOptions": tt.get("m_CookingOptions") or 30,
            "m_BakedConvexCollisionMesh": b"",
            "m_BakedTriangleCollisionMesh": b"",
            "m_MeshMetrics[0]": tt.get("m_MeshMetrics[0]") or 1,
            "m_MeshMetrics[1]": tt.get("m_MeshMetrics[1]") or 1,
            "m_StreamData": {"serializedVersion": 2, "offset": 0, "size": 0, "path": ""},
        }

        w = Writer(self.resolve, map_keys(obj))
        for k, v in mesh.items():
            w.emit(k, v, 1)

        lines = ["%YAML 1.1", "%TAG !u! tag:unity3d.com,2011:",
                 "--- !u!43 &4300000", "Mesh:", "  m_ObjectHideFlags: 0"] + w.lines
        t = self.tag("mesh", obj)
        path = os.path.join(out_dir, name + "_%s.asset" % self.stamp(t))
        with open(path, "w", encoding="utf-8") as fh:
            fh.write("\n".join(lines) + "\n")
        write_meta(path, self.guid(t),
                   "NativeFormatImporter:\n  externalObjects: {}\n  mainObjectFileID: 4300000\n")
        print("  mesh %s: %d verts, %d submesh(es)%s"
              % (name, n, len(subs), ", skinned" if skinned else ""), flush=True)

    def write_shader(self, obj, out_dir):
        fgshader.write_shader(obj, out_dir, self.guid(self.tag("shader", obj)),
                              write_meta, safe, say)

    def write_texture(self, obj, out_dir):
        data = obj.read()
        name = safe(getattr(data, "m_Name", None) or ("Texture_%d" % obj.path_id))
        t = self.tag("tex", obj)
        path = os.path.join(out_dir, name + "_%s.png" % self.stamp(t))
        data.image.save(path)
        srgb = 1 if (obj.read_typetree().get("m_ColorSpace") or 0) else 0
        write_meta(path, self.guid(t), TEXTURE_META % {"srgb": srgb})

def write_meta(asset_path, guid, importer_block):
    with open(asset_path + ".meta", "w", encoding="utf-8") as fh:
        fh.write("fileFormatVersion: 2\nguid: %s\n%s" % (guid, importer_block))


FACES = ("PosX", "NegX", "PosY", "NegY", "PosZ", "NegZ")


def write_faces(obj, out_dir):
    from UnityPy.export import Texture2DConverter

    data = obj.read()
    name = safe(getattr(data, "m_Name", None) or ("Texture_%d" % obj.path_id))
    raw = data.get_image_data()
    count = getattr(data, "m_ImageCount", 1) or 1
    if count <= 1:
        path = os.path.join(out_dir, name + ".png")
        data.image.save(path)
        say("wrote %s" % os.path.basename(path))
        return path

    reader = getattr(data, "object_reader", None)
    version = getattr(reader, "version", (0, 0, 0, 0))
    platform = getattr(reader, "platform", 0)
    blob = getattr(data, "m_PlatformBlob", None)
    per = len(raw) // count
    last = None
    for i in range(count):
        img = Texture2DConverter.parse_image_data(
            raw[i * per:(i + 1) * per], data.m_Width, data.m_Height,
            data.m_TextureFormat, version, platform, blob)
        face = FACES[i] if i < len(FACES) else str(i)
        last = os.path.join(out_dir, "%s_%s.png" % (name, face))
        img.save(last)
    say("%s: %d face(s) out" % (name, count))
    return last


def leaf_of(wanted):
    leaf = wanted.replace("\\", "/").rsplit("/", 1)[-1]
    for ext in (".prefab", ".mat", ".hdr", ".exr", ".png", ".tga", ".psd"):
        if leaf.lower().endswith(ext):
            return leaf[: -len(ext)]
    return leaf


def find_object(bundle_dir, wanted, type_name):
    """the key is a full addressable path, but the object inside the bundle carries only its name"""
    types = (type_name,) if isinstance(type_name, str) else tuple(type_name)
    leaf = leaf_of(wanted)
    files = sorted(f for f in os.listdir(bundle_dir) if f.endswith(".bundle"))
    for n, f in enumerate(files):
        if n % 40 == 0:
            say("looking for %s, %d of %d bundles" % (leaf, n, len(files)))
        try:
            env = UnityPy.load(os.path.join(bundle_dir, f))
        except Exception:
            continue
        for o in env.objects:
            if o.type.name not in types:
                continue
            try:
                if o.peek_name() == leaf:
                    return env, o
            except Exception:
                continue
        del env
    return None, None


def main():
    global WORK
    WORK = sys.argv[1]
    bundle_dir = sys.argv[2]
    key = sys.argv[3]

    low = key.lower()
    material_mode = low.endswith(".mat")
    texture_mode = low.endswith((".hdr", ".exr", ".png", ".tga", ".psd"))

    if texture_mode:
        env, obj = find_object(bundle_dir, key, ("Cubemap", "Texture2D"))
    elif material_mode:
        env, obj = find_object(bundle_dir, key, "Material")
    else:
        env, obj = find_object(bundle_dir, key, "GameObject")
    if obj is None:
        say("could not find that asset in the bundles")
        return 1

    if texture_mode:
        out = os.path.join(WORK, "Textures")
        os.makedirs(out, exist_ok=True)
        return 0 if write_faces(obj, out) else 1

    cabs = load_cab_index(bundle_dir)
    name = leaf_of(key)
    exporter = Exporter(env, WORK, name, bundle_dir, cabs)
    ok = exporter.run_material(obj) if material_mode else exporter.run(obj)
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
