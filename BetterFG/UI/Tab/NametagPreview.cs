using System;
using BetterFG.Nametag;
using BetterFG.Services;
using FGClient;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.UI.Tab
{
    public class NametagPreview
    {
        public static NametagPreview Active { get; private set; }

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
            panelGo.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.35f);

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

        public static bool PlatformHideFromSettings()
        {
            string m = SettingsService.Get("nametag.platform.hide", "none");
            return m == "self" || m == "everyone";
        }

        public static string PlatformCustomFromSettings() => SettingsService.Get("nametag.platform.custom", "");

        public void Apply(NametagIconApplicator.NametagCfg nameCfg, CrownRankService.CrownCfg crownCfg,
            bool platformHide, string platformCustom)
        {
            if (!EnsureClone()) return;
            var tmp = _previewCanvas != null ? _previewCanvas._text : null;
            if (tmp == null) return;

            CrownRankService.InvalidateCache();
            string n = BetterFG.Core.LocalPlayerInfo.DisplayName;
            tmp.text = string.IsNullOrEmpty(n) ? SAMPLE_NAME : n;
            _previewCanvas.SetCrownRankByCrownsEarned(crownCfg.enabled ? 1 : 0);

            NametagIconApplicator.ApplyNametagTo(_previewCanvas, nameCfg);
            CrownRankService.ApplyCrownTo(_previewCanvas, crownCfg);
            NametagIconApplicator.ApplyPlatformIcon(_previewClone, platformHide, platformCustom ?? "");
        }
    }
}
