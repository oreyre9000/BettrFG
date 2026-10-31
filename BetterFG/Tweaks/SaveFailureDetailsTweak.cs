using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using BetterFG.Core;
using BetterFG.Services;
using BetterFG.UI;
using BetterFG.Utilities;
using FG.Common.UGCNetworking;
using FG.Common.UI;
using HarmonyLib;
using Il2CppInterop.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.Tweaks
{
    public class SaveFailureDetailsTweak : BfgTweak
    {
        public SaveFailureDetailsTweak(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "save_failure_details";
        public override string TweakLabel => "tweak.save_failure_details";
        public override string TweakTooltip => "tweak.save_failure_details.tip";
        public override bool DefaultEnabled => true;

        private const string PopupPath = "UICanvas_Client_V2(Clone)/ModalMessage/Generic_UI_ModalImagePopup_Variant(Clone)";
        private const float DetailsSlotCollapsed = 76f;
        private const float DetailsSlotExpanded = 56f;
        private const float BoxMargin = 120f;

        internal static string Details, Report;
        private static float _pendingUntil = -1f;
        private static GameObject _popup, _box;
        private static NavPromptHandle _detailsPrompt, _savePrompt;
        private static LayoutElement _detailsSlot;
        private static bool _expanded;

        internal static void Queue(string details, string report)
        {
            Details = details;
            Report = report;
            _pendingUntil = Time.unscaledTime + 3f;
        }

        void Update()
        {
            if (_pendingUntil > 0f) TryDecorate();
            if (_detailsPrompt == null) return;
            if (_popup == null || !_popup.activeInHierarchy) { Teardown(); return; }
            _detailsPrompt.IsPressed();
            if (_expanded) _savePrompt.IsPressed();
        }

        public override void DisableTweak() => Teardown();

        private static void TryDecorate()
        {
            if (Time.unscaledTime > _pendingUntil) { _pendingUntil = -1f; return; }
            var popup = GameObject.Find(PopupPath);
            if (popup == null || !popup.activeInHierarchy) return;
            _pendingUntil = -1f;
            Teardown();
            Build(popup);
        }

        private static void Build(GameObject popup)
        {
            _popup = popup;
            _expanded = false;
            var content = popup.transform.Find("Content");

            _detailsPrompt = Prompt(content, "save_failure.details", "bfg_save_failure_details",
                RewiredConsts.Action.LevelEditorUINavigation_LevelEditorUINavRulebook, ToggleDetails);
            _detailsSlot = _detailsPrompt.GameObject.AddComponent<LayoutElement>();
            _detailsSlot.preferredHeight = DetailsSlotCollapsed;

            _box = new GameObject("BettrFG_SaveFailureDetails", Il2CppType.Of<RectTransform>());
            _box.transform.SetParent(content, false);
            var slot = _box.AddComponent<LayoutElement>();

            Image roundedTemplate = null;
            foreach (var img in Resources.FindObjectsOfTypeAll<Image>())
                if (!img.gameObject.scene.IsValid() && img.name == "BG" && img.transform.parent?.name == "ToolTip"
                    && img.sprite?.name == "UI_RoundedPanel3_Image") { roundedTemplate = img; break; }
            TextMeshProUGUI textTemplate = null;
            foreach (var t in Resources.FindObjectsOfTypeAll<TextMeshProUGUI>())
                if (!t.gameObject.scene.IsValid() && t.name == "Content_Text" && t.transform.root.name.StartsWith("Generic_UI_Modal")) { textTemplate = t; break; }

            var bg = UnityEngine.Object.Instantiate(roundedTemplate.gameObject, _box.transform);
            bg.name = "Background";
            for (int i = bg.transform.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(bg.transform.GetChild(i).gameObject);
            bg.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.2f);
            var bgRect = bg.GetComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.pivot = new Vector2(0.5f, 0.5f);
            bgRect.offsetMin = new Vector2(BoxMargin, 0f);
            bgRect.offsetMax = new Vector2(-BoxMargin, 0f);

            var textGo = UnityEngine.Object.Instantiate(textTemplate.gameObject, bg.transform);
            textGo.name = "Text";
            foreach (var c in textGo.GetComponents<Component>())
                if (c.GetIl2CppType().Name.EndsWith("Binding")) UnityEngine.Object.Destroy(c);
            var textRect = textGo.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.pivot = new Vector2(0.5f, 0.5f);
            textRect.offsetMin = new Vector2(30f, 20f);
            textRect.offsetMax = new Vector2(-30f, -20f);
            var text = textGo.GetComponent<TextMeshProUGUI>();
            text.alignment = TextAlignmentOptions.TopLeft;
            text.enableAutoSizing = false;
            text.fontSize = 26f;
            text.enableWordWrapping = true;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.text = Details;
            float width = content.GetComponent<RectTransform>().rect.width - BoxMargin * 2f - 60f;
            slot.preferredHeight = text.GetPreferredValues(Details, width, 0f).y + 40f;
            _box.SetActive(false);

            _savePrompt = Prompt(content, "save_failure.save_report", "bfg_save_failure_save",
                RewiredConsts.Action.LevelEditorUINavigation_LevelEditorUINavDuplicateLevel, SaveFailureCapture.SaveReport);
            _savePrompt.GameObject.AddComponent<LayoutElement>().preferredHeight = 64f;
            _savePrompt.GameObject.SetActive(false);

            Relayout();
        }

        private static NavPromptHandle Prompt(Transform parent, string labelId, string cmsKey, int action, Action pressed)
        {
            string label = LocalizationService.Get(labelId);
            NavPromptInjection.BuildData(NavPrompt.Report, label, cmsKey, action, RewiredConsts.Category.LevelEditorUINavigation);
            return NavPromptCore.From(NavPrompt.Report)
                .WithLabel(label, cmsKey)
                .NoAutoResize()
                .AllowWhileUnfocused()
                .PollActions(action)
                .OnPress(pressed)
                .SpawnOn(parent);
        }

        private static void ToggleDetails()
        {
            _expanded = !_expanded;
            _box.SetActive(_expanded);
            _savePrompt.GameObject.SetActive(_expanded);
            _detailsSlot.preferredHeight = _expanded ? DetailsSlotExpanded : DetailsSlotCollapsed;
            Relayout();
        }

        private static void Relayout() => LayoutRebuilder.MarkLayoutForRebuild(_popup.GetComponent<RectTransform>());

        private static void Teardown()
        {
            _detailsPrompt?.Destroy();
            _savePrompt?.Destroy();
            if (_box != null) UnityEngine.Object.Destroy(_box);
            _detailsPrompt = null;
            _savePrompt = null;
            _box = null;
            _popup = null;
        }
    }

    internal static class SaveFailureCapture
    {
        internal static void Wrap(UGCNEndpointParameters p)
        {
            var original = p._responseHandler;
            string json = p._levelJSON._value, name = p._levelName._value, code = p._shareCode._value;
            var sent = DateTime.Now;
            var clock = Stopwatch.StartNew();
            p._responseHandler = DelegateSupport.ConvertDelegate<ResponseHandler>(new Action<ResponseHandlerObject>(r =>
            {
                clock.Stop();
                original.Invoke(r);
                if (r.Failed) Capture(r, json, name, code, sent, clock.ElapsedMilliseconds);
            }));
        }

        private static void Capture(ResponseHandlerObject r, string json, string name, string code, DateTime sent, long ms)
        {
            var web = r.Response?.TryCast<UGCNetworkAzure.WebResponse>();
            string body = web?.text ?? "";
            string correlation = web?.headers != null && web.headers.ContainsKey("X-Correlation-ID") ? web.headers["X-Correlation-ID"] : "";
            int bytes = json?.Length ?? 0;

            var rows = new List<(string, string)>
            {
                ("save_failure.server", web != null ? JsonUtil.GetValue(body, "title") : r.ResponseOutcome.ToString()),
                ("save_failure.status", web != null ? web.responseCode.ToString() : ""),
                ("save_failure.error_code", JsonUtil.GetInt(body, "errorCode").ToString()),
                ("save_failure.level_data", $"{bytes / 1048576f:0.00} MB ({bytes:N0})"),
                ("save_failure.response_time", ms + " ms"),
                ("save_failure.sent", sent.ToString("yyyy-MM-dd HH:mm:ss")),
                ("save_failure.share_code", code),
                ("save_failure.correlation_id", correlation),
            };

            var details = new StringBuilder();
            var report = new StringBuilder();
            report.AppendLine(LocalizationService.Get("save_failure.report_title"));
            report.AppendLine();
            report.AppendLine($"{LocalizationService.Get("save_failure.level")} {name}");
            report.AppendLine($"{LocalizationService.Get("save_failure.game_version")} {Application.version}");
            report.AppendLine($"UTC {sent.ToUniversalTime():yyyy-MM-dd HH:mm:ss.fff}");
            foreach (var (key, value) in rows)
            {
                string label = LocalizationService.Get(key);
                if (details.Length > 0) details.Append('\n');
                details.Append($"<b>{label}</b> {value}");
                report.AppendLine($"{label} {value}");
            }
            report.AppendLine();
            report.AppendLine(web != null ? web.ToString(true) : r.ToString());
            report.AppendLine();
            report.AppendLine(LocalizationService.Get("save_failure.report_json"));
            report.AppendLine(json);

            Plugin.Log.LogWarning($"level save bounced: {rows[0].Item2} ({rows[1].Item2}), {bytes} bytes, {ms} ms");
            SaveFailureDetailsTweak.Queue(details.ToString(), report.ToString());
        }

        internal static void SaveReport()
        {
            string text = SaveFailureDetailsTweak.Report;
            WinDialogs.SaveFile(LocalizationService.Get("save_failure.save_report"), "txt",
                $"save_failure_{DateTime.Now:yyyyMMdd_HHmmss}.txt", path =>
                {
                    if (path == null) return;
                    System.IO.File.WriteAllText(path, text);
                    Plugin.Log.LogInfo($"save failure report written to {path}");
                });
        }
    }

    [BfgPatchGate("tweak.save_failure_details", defaultOn: true)]
    [HarmonyPatch]
    internal static class SaveFailureRequestPatches
    {
        [HarmonyPatch(typeof(UGCNetworkRequests), nameof(UGCNetworkRequests.CreateUpdateDraftRequest))]
        [HarmonyPrefix]
        private static void UpdateDraft(UpdateDraftParameters parameters) => SaveFailureCapture.Wrap(parameters);

        [HarmonyPatch(typeof(UGCNetworkRequests), nameof(UGCNetworkRequests.CreateUploadDraftRequest))]
        [HarmonyPrefix]
        private static void UploadDraft(UploadDraftParameters parameters) => SaveFailureCapture.Wrap(parameters);
    }
}
