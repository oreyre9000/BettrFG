using System;
using BetterFG.Core;
using BetterFG.Nametag;
using UnityEngine;
using UnityEngine.UI;
using FGClient;
using BettrFG.uGUI;

namespace BetterFG.UI.Tabs
{
    // the live name+icon+crown preview clone - a plain helper (not a MonoBehaviour, never registered)
    // so both the Nametag hub and its Colour/Icon/Crown editors can mount the same preview without
    // duplicating the clone logic. Nameplate has no use for this - it has its own backing preview.
    public class NametagPreview
    {
        public static NametagPreview Active { get; private set; }

        public PlayerInfoDisplayCanvas Canvas => _previewCanvas;

        private const string SAMPLE_NAME = "Example";

        private RectTransform _previewPanelRt;
        private GameObject _previewClone;
        private PlayerInfoDisplayCanvas _previewCanvas;
        private Action _refresh;

        public void Build(RectTransform parent, Rect area, Action refresh)
        {
            _refresh = refresh;

            var panelGo = new GameObject("Preview");
            panelGo.transform.SetParent(parent, false);
            UGUIShip.SetPixelRect(panelGo.AddComponent<RectTransform>(), area);
            UGUIShip.ApplyDeluxPanelDark(panelGo.AddComponent<Image>());

            var holderGo = new GameObject("PreviewHolder");
            holderGo.transform.SetParent(panelGo.transform, false);
            _previewPanelRt = holderGo.AddComponent<RectTransform>();
            _previewPanelRt.anchorMin = _previewPanelRt.anchorMax = new Vector2(0.5f, 0.5f);
            _previewPanelRt.pivot = new Vector2(0.5f, 0.5f);
            _previewPanelRt.sizeDelta = new Vector2(area.width, area.height);
            _previewPanelRt.anchoredPosition = Vector2.zero;

            Active = this;
            Refresh();
        }

        public void Refresh() => _refresh?.Invoke();

        // instantiate a real PlayerInfoDisplayCanvas into the preview once one exists in the scene. cheap
        // after the first success. does nothing while no live canvas exists yet (menus pre-nametag).
        private bool EnsureClone()
        {
            if (_previewClone != null && _previewCanvas != null) return true;
            if (_previewPanelRt == null) return false;

            PlayerInfoDisplayCanvas src = null;
            foreach (var c in Resources.FindObjectsOfTypeAll<PlayerInfoDisplayCanvas>())
            {
                if (c == null || c.gameObject.name == "PreviewNametagClone") continue;
                if (c._text != null) { src = c; break; }
            }
            if (src == null) return false;

            _previewClone = UnityEngine.Object.Instantiate(src.gameObject, _previewPanelRt);
            _previewClone.name = "PreviewNametagClone";
            _previewCanvas = _previewClone.GetComponent<PlayerInfoDisplayCanvas>();

            var crt = _previewClone.GetComponent<RectTransform>();
            if (crt != null)
            {
                crt.anchorMin = crt.anchorMax = new Vector2(0.5f, 0.5f);
                crt.pivot = new Vector2(0.5f, 0.5f);
                crt.anchoredPosition = Vector2.zero;
                crt.localScale = Vector3.one;
            }
            _previewClone.SetActive(true);
            return true;
        }

        private static string PreviewName() => SAMPLE_NAME;

        public void Apply(NametagIconApplicator.NametagCfg nameCfg, CrownRankService.CrownCfg crownCfg,
            bool platformHide, string platformCustom)
        {
            if (!EnsureClone()) return;
            var tmp = _previewCanvas != null ? _previewCanvas._text : null;
            if (tmp == null) return;

            CrownRankService.InvalidateCache();
            tmp.text = PreviewName();
            CrownRankService.SetCrownRankSafe(_previewCanvas, crownCfg.enabled ? 1 : 0);

            NametagIconApplicator.ApplyNametagTo(_previewCanvas, nameCfg);
            CrownRankService.ApplyCrownTo(_previewCanvas, crownCfg);
            NametagIconApplicator.ApplyPlatformIcon(_previewClone, platformHide, platformCustom ?? "");
            tmp.text = SAMPLE_NAME;
        }
    }
}
