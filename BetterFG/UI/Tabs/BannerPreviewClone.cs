using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Customization.UI;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.UI.Tabs
{
    // clones the live game banner for a UIForegroundKind into a caller-supplied viewport and recolours
    // it to match a caller-supplied colour set. Shared between UIForegroundDetailTab (live edit preview,
    // colours come from the in-progress sliders) and UITab's carousel (colours come straight from
    // settings) so the clone/position/recolour logic — and its game-specific quirks — only live once.
    internal class BannerPreviewClone
    {
        private struct CachedImgColor { public Image img; public Color orig; public bool isHighlight; }
        private struct CachedTmpColor { public TMPro.TMP_Text tmp; public Color origFill; public Color origOutline; public Color origUnderlay; public bool hasOutline; public bool hasUnderlay; }

        private readonly List<CachedImgColor> _imgCache = new List<CachedImgColor>();
        private readonly List<CachedTmpColor> _tmpCache = new List<CachedTmpColor>();
        private GameObject _go;
        private UIForegroundKind _kind;
        private Func<MenuCustomizationApplication.BannerColours> _getColours;

        public void Refresh(MonoBehaviour host, UIForegroundKind kind, Transform viewport,
            Vector3 localPos, Vector3 localScale, Func<MenuCustomizationApplication.BannerColours> getColours)
        {
            if (_go != null) { GameObject.Destroy(_go); _go = null; }
            _imgCache.Clear();
            _tmpCache.Clear();
            _kind = kind;
            _getColours = getColours;

            if (viewport == null) return;
            GameObject source = FindSource(kind);
            if (source == null) return;

            _go = GameObject.Instantiate(source);
            _go.name = "BannerPreview";

            host.StartCoroutine(DisableAnimatorsDelayed(_go).WrapToIl2Cpp());

            foreach (var t in _go.GetComponentsInChildren<Transform>(true))
                if (t != null && t.name == "Layout") t.gameObject.SetActive(false);

            _go.transform.SetParent(viewport, false);
            _go.transform.localPosition = localPos;
            _go.transform.localScale = localScale;
            _go.SetActive(true);

            foreach (var g in _go.GetComponentsInChildren<Graphic>(true))
                if (g != null) g.raycastTarget = false;

            if (kind == UIForegroundKind.Winner)
            {
                foreach (var t in _go.GetComponentsInChildren<Transform>(true))
                {
                    if (t == null || t.parent == null || t.parent.name != "Container") continue;
                    if (t.name == "background-starburst-top" || t.name == "UIParticleStars")
                        t.gameObject.SetActive(false);
                }
            }
            else if (kind == UIForegroundKind.RoundOver)
            {
                foreach (var t in _go.GetComponentsInChildren<Transform>(true))
                    if (t != null && t.name == "text-ROUND")
                    {
                        t.localPosition = new Vector3(-5f, 0.3327f, 0f);
                        t.localScale = new Vector3(0.2f, 0.2f, 0.2f);
                        break;
                    }
            }
            else if (kind == UIForegroundKind.EliminatedSquad)
            {
                ApplySquadLayout(_go);
            }

            foreach (var img in _go.GetComponentsInChildren<Image>(true))
            {
                if (img != null)
                {
                    bool hl = MenuCustomizationApplication.BannerColours.IsHighlight(img);
                    _imgCache.Add(new CachedImgColor { img = img, orig = img.color, isHighlight = hl });
                }
            }

            foreach (var binding in _go.GetComponentsInChildren<Mediatonic.Tools.MVVM.TMPTextBinding>(true))
                if (binding != null) GameObject.Destroy(binding);

            host.StartCoroutine(SetTextNextFrame().WrapToIl2Cpp());
        }

        private IEnumerator DisableAnimatorsDelayed(GameObject go)
        {
            yield return new WaitForSeconds(1.7f);
            if (go == null) yield break;
            foreach (var anim in go.GetComponentsInChildren<Animator>(true))
                if (anim != null) anim.enabled = false;
        }

        private static void ApplySquadLayout(GameObject go)
        {
            if (go == null) return;
            foreach (var anim in go.GetComponentsInChildren<Animator>(true))
                if (anim != null) anim.enabled = false;

            foreach (var t in go.GetComponentsInChildren<Transform>(true))
            {
                if (t == null) continue;
                if (t.parent != null && t.parent.name == "Container" && t.name == "Badge")
                    t.gameObject.SetActive(false);
                else if (t.name == "text-title" || t.name == "text-subtitle")
                {
                    var fitter = t.GetComponent<ContentSizeFitter>();
                    if (fitter != null) GameObject.Destroy(fitter);
                    var le = t.GetComponent<LayoutElement>();
                    if (le != null) GameObject.Destroy(le);

                    if (t.name == "text-title")
                    {
                        t.localPosition = new Vector3(-301.564f, -10.9704f, 0f);
                        t.localScale = new Vector3(3f, 3f, 3f);
                        var rt = t as RectTransform;
                        if (rt != null) rt.sizeDelta = new Vector2(320f, -194.8501f);
                    }
                    else
                    {
                        t.localScale = new Vector3(3f, 3f, 3f);
                        t.localPosition = new Vector3(63.6912f, -50.9455f, 0f);
                        var rt = t as RectTransform;
                        if (rt != null) rt.sizeDelta = new Vector2(520f, 0f);
                    }
                }
            }
        }

        private IEnumerator SetTextNextFrame()
        {
            yield return null;
            if (_go == null) yield break;

            foreach (var tmp in _go.GetComponentsInChildren<TMPro.TMP_Text>(true))
            {
                if (tmp == null) continue;
                if (tmp.gameObject.name.StartsWith("text-"))
                    tmp.SetText("BEAUTY");

                tmp.ForceMeshUpdate();
                tmp.enabled = false;
                var entry = new CachedTmpColor { tmp = tmp, origFill = tmp.color };
                if (tmp.fontSharedMaterial != null)
                {
                    var mat = tmp.fontMaterial;
                    entry.hasOutline = mat.HasProperty(TMPro.ShaderUtilities.ID_OutlineColor);
                    if (entry.hasOutline) entry.origOutline = mat.GetColor(TMPro.ShaderUtilities.ID_OutlineColor);
                    entry.hasUnderlay = mat.HasProperty(TMPro.ShaderUtilities.ID_UnderlayColor);
                    if (entry.hasUnderlay) entry.origUnderlay = mat.GetColor(TMPro.ShaderUtilities.ID_UnderlayColor);
                }
                _tmpCache.Add(entry);
            }

            yield return null;

            for (int i = 0; i < _tmpCache.Count; i++)
            {
                var c = _tmpCache[i];
                if (c.tmp != null) c.tmp.enabled = true;
            }
            UpdateColours();
        }

        public void UpdateColours()
        {
            if (_go == null) return;
            var set = _getColours != null ? _getColours() : default;

            Image winnerRoundOverWhiteImg = null;
            bool winnerOverrideOn = false;
            Color winnerOverrideColor = Color.white;
            if (_kind == UIForegroundKind.Winner)
            {
                if (set.slots != null)
                    foreach (var s in set.slots)
                        if (s.bucket == MenuCustomizationApplication.BannerBucket.Yellow)
                        {
                            winnerOverrideOn = true;
                            winnerOverrideColor = s.target;
                            break;
                        }
                foreach (var t in _go.GetComponentsInChildren<Transform>(true))
                {
                    if (t == null || t.gameObject.name != "round-over-white") continue;
                    winnerRoundOverWhiteImg = t.GetComponent<Image>();
                    if (winnerRoundOverWhiteImg != null) break;
                }
            }

            for (int i = 0; i < _imgCache.Count; i++)
            {
                var c = _imgCache[i];
                if (c.img == null) continue;
                if (winnerRoundOverWhiteImg != null && c.img == winnerRoundOverWhiteImg)
                {
                    c.img.color = winnerOverrideOn
                        ? new Color(winnerOverrideColor.r, winnerOverrideColor.g, winnerOverrideColor.b, c.orig.a)
                        : c.orig;
                    continue;
                }
                if (c.isHighlight && set.highlightOn)
                    c.img.color = new Color(set.highlight.r, set.highlight.g, set.highlight.b, c.orig.a);
                else if (set.TryMatch(c.orig, out var t))
                    c.img.color = new Color(t.r, t.g, t.b, c.orig.a);
                else
                    c.img.color = c.orig;
            }

            for (int i = 0; i < _tmpCache.Count; i++)
            {
                var c = _tmpCache[i];
                if (c.tmp == null) continue;
                c.tmp.color = set.TryMatch(c.origFill, out var tFill)
                    ? new Color(tFill.r, tFill.g, tFill.b, c.origFill.a) : c.origFill;

                if (c.tmp.fontSharedMaterial == null) continue;
                var mat = c.tmp.fontMaterial;
                if (c.hasOutline)
                    mat.SetColor(TMPro.ShaderUtilities.ID_OutlineColor,
                        set.TryMatch(c.origOutline, out var tOut)
                            ? new Color(tOut.r, tOut.g, tOut.b, c.origOutline.a) : c.origOutline);
                if (c.hasUnderlay)
                    mat.SetColor(TMPro.ShaderUtilities.ID_UnderlayColor,
                        set.TryMatch(c.origUnderlay, out var tUn)
                            ? new Color(tUn.r, tUn.g, tUn.b, c.origUnderlay.a) : c.origUnderlay);
            }
        }

        private static bool IsPreviewClone(UnityEngine.Object obj)
        {
            if (obj == null) return false;
            var t = (obj as Component)?.transform;
            while (t != null)
            {
                if (t.gameObject.name == "BannerPreview") return true;
                t = t.parent;
            }
            return false;
        }

        private static GameObject FindSource(UIForegroundKind what)
        {
            switch (what)
            {
                case UIForegroundKind.Qualified:
                    foreach (var vm in Resources.FindObjectsOfTypeAll<FGClient.UI.QualifiedScreenViewModel>())
                        if (vm != null && vm.gameObject != null && !IsPreviewClone(vm)) return vm.gameObject;
                    break;
                case UIForegroundKind.Eliminated:
                    foreach (var vm in Resources.FindObjectsOfTypeAll<FGClient.EliminatedScreenViewModel>())
                        if (vm != null && vm.gameObject != null && !IsPreviewClone(vm)) return vm.gameObject;
                    break;
                case UIForegroundKind.EliminatedSquad:
                    foreach (var vm in Resources.FindObjectsOfTypeAll<FGClient.EliminatedSquadScreenViewModel>())
                        if (vm != null && vm.gameObject != null && !IsPreviewClone(vm)) return vm.gameObject;
                    break;
                case UIForegroundKind.Winner:
                    foreach (var vm in Resources.FindObjectsOfTypeAll<FGClient.UI.WinnerScreenViewModel>())
                        if (vm != null && vm.gameObject != null && !IsPreviewClone(vm)) return vm.gameObject;
                    break;
                case UIForegroundKind.RoundOver:
                    foreach (var vm in Resources.FindObjectsOfTypeAll<FGClient.RoundEndedScreenViewModel>())
                        if (vm != null && vm.gameObject != null && !IsPreviewClone(vm)) return vm.gameObject;
                    break;
            }
            return null;
        }
    }
}
