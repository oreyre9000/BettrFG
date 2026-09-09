using UnityEngine;
using System.Collections.Generic;

namespace BetterFG.Customization.Player
{
    public class CostumePollerComponent : MonoBehaviour
    {
        public Transform beanGEO;
        public GameObject skinClone;
        public bool isRemote = false;
        public bool keepLocalDonor = false;
        public int beanId;

        private static readonly List<CostumePollerComponent> Live = new List<CostumePollerComponent>();

        private struct RendererState
        {
            public Material[] mats;
            public UnityEngine.Rendering.ShadowCastingMode shadowCasting;
            public bool receiveShadows;
        }

        // renderer -> original state, restored on destroy
        private Dictionary<Renderer, RendererState> _savedMats = new Dictionary<Renderer, RendererState>();

        // one fully-transparent material shared across all invisible renderers
        private static Material _invisibleMat;

        public static Material PeekInvisibleMat() => _invisibleMat;

        public static Material GetInvisibleMat()
        {
            if (_invisibleMat != null) return _invisibleMat;

            // If the project has an embedded prefab named "material_invisible", use its material as a template.
            try
            {
                if (BetterFG.Core.AssetManager.Instance != null && BetterFG.Core.AssetManager.Instance.prefabs != null)
                {
                    if (BetterFG.Core.AssetManager.Instance.prefabs.TryGetValue("material_invisible", out var matPrefab) && matPrefab != null)
                    {
                        var srcR = matPrefab.GetComponent<Renderer>() ?? matPrefab.GetComponentInChildren<Renderer>(true);
                        if (srcR != null && srcR.materials != null && srcR.materials.Length > 0)
                        {
                            // clone the first material from the prefab so we don't share instances
                            _invisibleMat = new Material(srcR.materials[0]);
                            // ensure fully transparent settings (in case the prefab isn't exact)
                            _invisibleMat.color = new Color(0f, 0f, 0f, 0f);
                            _invisibleMat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                            return _invisibleMat;
                        }
                    }
                }
            }
            catch { }

            // Fallback: create a standard transparent material
            _invisibleMat = new Material(Shader.Find("Standard"));
            _invisibleMat.SetFloat("_Mode", 3f);                          // Transparent
            _invisibleMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
            _invisibleMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _invisibleMat.SetInt("_ZWrite", 0);
            _invisibleMat.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            _invisibleMat.color = new Color(0f, 0f, 0f, 0f);
            _invisibleMat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return _invisibleMat;
        }

        // Call this right after configuring the poller so the base bean is hidden on the same
        // frame the skin clone appears, instead of waiting a frame for Start(). Without this the
        // UGC costume + base bean both show for a beat on first menu entry.
        public void HideNow() => HideBeans();

        void Awake() => Live.Add(this);

        void Start() => HideBeans();

        public static void RehideForBean(int beanId)
        {
            for (int i = Live.Count - 1; i >= 0; i--)
            {
                var p = Live[i];
                if (p is null || p.m_CachedPtr == System.IntPtr.Zero) { Live.RemoveAt(i); continue; }
                if (p.beanId == beanId) p.HideBeans();
            }
        }

        private void HideBeans()
        {
            if (beanGEO is null || beanGEO.m_CachedPtr == System.IntPtr.Zero) return;
            if (isRemote) HideRemoteBeans();
            else HideLocalBeans();
        }

        private void Guard(Transform child)
        {
            if (child.gameObject.GetComponent<BaseBodyGuard>() != null) return;
            child.gameObject.AddComponent<BaseBodyGuard>().owner = skinClone;
        }

        private void HideRemoteBeans()
        {
            for (int i = 0; i < beanGEO.childCount; i++)
            {
                Transform child = beanGEO.GetChild(i).Cast<Transform>();
                if (child == null) continue;
                if (child.gameObject == skinClone) continue;
                if (!ShouldHide(child.name)) continue;

                if (child.name.Contains("Body_LOD"))
                {
                    MakeInvisible(child);
                }
                else
                {
                    if (child.gameObject.activeSelf)
                        child.gameObject.SetActive(false);
                    Guard(child);
                }
            }
        }

        private void HideLocalBeans()
        {
            Transform boneDonor = null;
            for (int i = 0; i < beanGEO.childCount; i++)
            {
                Transform child = beanGEO.GetChild(i).Cast<Transform>();
                if (child == null) continue;
                if (child.gameObject == skinClone) continue;
                if (!ShouldHide(child.name)) continue;
                if (child.name.Contains("Body_LOD0")) { boneDonor = child; break; }
            }
            if (boneDonor == null)
            {
                for (int i = 0; i < beanGEO.childCount; i++)
                {
                    Transform child = beanGEO.GetChild(i).Cast<Transform>();
                    if (child == null) continue;
                    if (child.gameObject == skinClone) continue;
                    if (!ShouldHide(child.name)) continue;
                    if (child.name.Contains("Body_LOD")) { boneDonor = child; break; }
                }
            }

            for (int i = 0; i < beanGEO.childCount; i++)
            {
                Transform child = beanGEO.GetChild(i).Cast<Transform>();
                if (child == null) continue;
                if (child.gameObject == skinClone) continue;
                if (!ShouldHide(child.name)) continue;

                if (keepLocalDonor && boneDonor != null && child == boneDonor)
                {
                    if (!child.gameObject.activeSelf) child.gameObject.SetActive(true);
                    continue;
                }

                if (child.gameObject.activeSelf)
                    child.gameObject.SetActive(false);
                Guard(child);
            }
        }

        private void MakeInvisible(Transform target)
        {
            var renderers = target.GetComponentsInChildren<Renderer>(true);
            if (renderers == null) return;
            Material invis = GetInvisibleMat();
            foreach (var r in renderers)
            {
                if (r == null || _savedMats.ContainsKey(r)) continue;
                if (r.materials == null || r.materials.Length == 0) continue;
                _savedMats[r] = new RendererState
                {
                    mats = r.materials,
                    shadowCasting = r.shadowCastingMode,
                    receiveShadows = r.receiveShadows
                };
                var invisible = new Material[r.materials.Length];
                for (int j = 0; j < invisible.Length; j++)
                    invisible[j] = invis;
                r.materials = invisible;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
        }

        private static bool ShouldHide(string name) =>
            name.Contains("Top") || name.Contains("Bottom") ||
            name.Contains("CH_") || name.Contains("Body_LOD") || name.Contains("LOD");

        void OnDestroy()
        {
            Live.Remove(this);
            foreach (var kv in _savedMats)
            {
                if (kv.Key == null) continue;
                kv.Key.materials = kv.Value.mats;
                kv.Key.shadowCastingMode = kv.Value.shadowCasting;
                kv.Key.receiveShadows = kv.Value.receiveShadows;
            }
            _savedMats.Clear();
        }
    }
}
