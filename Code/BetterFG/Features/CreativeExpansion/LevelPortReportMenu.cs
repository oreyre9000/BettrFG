using System;
using System.Collections;
using System.IO;
using BetterFG.Core;
using BetterFG.Services;
using BetterFG.UI;
using FallGuysLib.UI;
using FG.Common.UGCNetworking;
using FGClient;
using FGClient.UI;
using Il2CppInterop.Runtime;
using MPG.Utility;
using UnityEngine;
using LB = Wushu.LevelEditor.Runtime.UI.LevelBrowser;

namespace BetterFG.Features.CreativeExpansion
{
    // Reuses the game's own UGC "report level" popup (Generic_UI_UgcReportPopup_Prefab /
    // ReportUGCPopupViewModel) as the Import / Export chooser. Opened via ReportManager; we relabel
    // the first two report-reason rows to IMPORT / EXPORT, hide the rest, and point them at file
    // dialogs. Nothing is ever sent to moderation.
    //
    // Export writes the level's real JSON (LevelEditorLevel._levelJSON, fetched if needed) to a
    // .bfglevel file. Import reads a .bfglevel file and queues it via LevelPortImport; the level
    // then loads from it on every open until the game restarts (or the user saves it for real).
    internal static class LevelPortReportMenu
    {
        internal static bool AnyOpen => ReportMenuShell.AnyOpen;

        private static LevelEditorLevel _exportLevel;

        internal static IEnumerator OpenRoutine(LB.TileData tile)
        {
            string name = string.IsNullOrEmpty(tile?.Name) ? "level" : tile.Name;
            string code = tile?.LevelCode ?? "";
            _exportLevel = tile?.level;

            string body = string.IsNullOrEmpty(code)
                ? string.Format(LocalizationService.Get("level_port.menu_body"), name)
                : string.Format(LocalizationService.Get("level_port.menu_body_code"), name, code);

            return ReportMenuShell.Open("ui.level_import_export", body, true, () =>
            {
                ReportMenuShell.SetRow(0, "ui.import_from_file", () => StartImport(name, code));
                ReportMenuShell.SetRow(1, "ui.export_to_file", () => StartExport(name));
                if (string.IsNullOrEmpty(code)) ReportMenuShell.HideRow(2);
                else ReportMenuShell.SetRow(2, "level_port.copy_code",
                    () => CopyCodePrompt.Instance.CopyResolved(code, $"level \"{name}\""));
                for (int i = 3; i < 6; i++) ReportMenuShell.HideRow(i);
            });
        }

        // ── export ───────────────────────────────────────────────────────────

        private static void StartExport(string name)
        {
            var lvl = _exportLevel;
            if (lvl == null) { Plugin.Log.LogWarning("export: this tile has no editable level"); return; }

            var fj = lvl._levelJSON;
            if (fj != null && fj.Fetched && !string.IsNullOrEmpty(fj.Resource))
            {
                PromptSaveExport(name, fj.Resource);
                return;
            }

            Plugin.Log.LogInfo("export: fetching level JSON");
            var cb = DelegateSupport.ConvertDelegate<ResponseHandler>(new Action<ResponseHandlerObject>(ro =>
            {
                if (ro != null && ro.Failed) { Plugin.Log.LogError("export: FetchLevelJSON failed"); return; }
                var s = lvl._levelJSON != null ? lvl._levelJSON.Resource : null;
                if (string.IsNullOrEmpty(s)) { Plugin.Log.LogError("export: level JSON came back empty"); return; }
                PromptSaveExport(name, s);
            }));
            lvl.FetchLevelJSON(cb);
        }

        private static void PromptSaveExport(string name, string json)
        {
            WinDialogs.SaveFile("Export level", "bfglevel", Sanitise(name) + ".bfglevel", new Action<string>(path =>
            {
                if (string.IsNullOrEmpty(path)) return;
                try
                {
                    File.WriteAllText(path, LevelPortCodec.Encode(json));
                    Plugin.Log.LogInfo($"level exported -> {path} ({json.Length} chars of JSON, encoded)");
                }
                catch (Exception ex) { Plugin.Log.LogError("export write failed: " + ex.Message); }
            }));
        }

        // ── import ───────────────────────────────────────────────────────────

        private static void StartImport(string name, string code)
        {
            WinDialogs.PickFile("Import level", new Action<string>(path =>
            {
                if (!string.IsNullOrEmpty(path)) ConfirmImport(path, name, code);
            }), "BettrFG Level\0*.bfglevel\0All Files\0*.*\0");
        }

        private static void ConfirmImport(string path, string levelName, string code)
        {
            NavPromptCore.RegisterCmsString("bfg_levelport_import_title", "Import over this level?");
            NavPromptCore.RegisterCmsString("bfg_levelport_import_body",
                $"\"{levelName}\" will load from the imported file every time you open it, for the rest of this session. Restart the game to undo it, or save the level in the editor to keep it permanently.");
            PopUp.ShowPopup("bfg_levelport_import_title", "bfg_levelport_import_body",
                PopupInteractionType.Query, UIModalMessage.ModalType.MT_OK_CANCEL,
                UIModalMessage.OKButtonType.Disruptive,
                (Action<bool>)(ok => { if (ok) DoImport(path, levelName, code); }));
        }

        private static void DoImport(string path, string levelName, string code)
        {
            string text;
            try { text = File.ReadAllText(path); }
            catch (Exception ex) { Plugin.Log.LogError("import: couldn't read the file: " + ex.Message); return; }

            if (!LevelPortCodec.TryDecode(text, out string json))
            {
                Plugin.Log.LogError("import: not a BettrFG level file — only .bfglevel files exported by BettrFG work here, not raw Fall Guys JSON");
                return;
            }
            if (string.IsNullOrEmpty(code))
            {
                Plugin.Log.LogWarning("import: this level has no share code — save + publish it once so BettrFG can target it");
                return;
            }

            LevelPortImport.Queue(code, json);
            Plugin.Log.LogInfo($"import queued for {levelName} [{code}] ({json.Length} chars)");

            NavPromptCore.RegisterCmsString("bfg_levelport_done_title", "Import queued");
            NavPromptCore.RegisterCmsString("bfg_levelport_done_body",
                $"Open \"{levelName}\" to load it from the imported file. It stays this way until you restart the game; save the level in the editor to keep it.");
            PopUp.ShowPopup("bfg_levelport_done_title", "bfg_levelport_done_body",
                PopupInteractionType.Info, UIModalMessage.ModalType.MT_OK,
                UIModalMessage.OKButtonType.Default, (Action<bool>)(_ => { }));
        }

        private static string Sanitise(string s)
        {
            if (string.IsNullOrEmpty(s)) return "level";
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s;
        }
    }
}
