using System;
using System.Collections.Generic;
using BetterFG.Customization.Player;
using BetterFG.Services;
using UnityEngine;
using UnityEngine.UI;
using BettrFG.uGUI;

namespace BetterFG.UI.Tabs
{
    public class PlinthsUgcTab : PlinthsTab
    {
        public PlinthsUgcTab(IntPtr ptr) : base(ptr) { }

        public override string TabTitle => "Plinths - UGC";
        protected override string TitleId => "ui.plinths_ugc";

        protected override string SwitchLabel => "ui.in_game";
        protected override Tab MakeSwitchTarget() => BetterFGTabRegistry.NewTab<PlinthsInGameTab>();

        readonly Dictionary<string, Image> _coverImgs = new Dictionary<string, Image>();

        protected override float HeaderY() => RepoSelectorTab.BuildCurrentRepoRow(this, ContentRoot, PAD, ListW);

        protected override void OnCatalogChanged() => Rebuild();

        protected override void OnCoverLoaded(string key, Texture2D tex)
        {
            if (tex == null) return;
            if (_coverImgs.TryGetValue(key, out var img) && img != null) ApplyCover(img, tex);
        }

        protected override void BuildRows()
        {
            _coverImgs.Clear();

            var catalog = Catalog;
            if (catalog == null) { SetStatus("ui.catalog_isn_t_up_yet"); return; }

            var plinths = new List<SkinInfo>();
            foreach (var skin in catalog.AvailableSkins)
                if (SkinTypeParser.FromString(skin.type) == SkinType.Plinth && skin.sourceRepo == SelectedRaw)
                    plinths.Add(skin);

            if (plinths.Count == 0)
            {
                bool fetching = FetchSelectedRepo();

                var hintGo = new GameObject("Empty");
                hintGo.transform.SetParent(ListContent, false);
                hintGo.AddComponent<RectTransform>();
                hintGo.AddComponent<LayoutElement>().preferredHeight = ROW_H;
                UGUIShip.CreateStretchLabel(hintGo.transform,
                    fetching ? "ui.fetching" : "ui.no_plinths_in_this_repo_pick_another_above", FS_SM, HINT);
                SetStatus(fetching ? "ui.fetching" : "ui.nothing_here_yet");
                return;
            }

            string activeFile = PlinthApp?.ActiveFile;
            foreach (var skin in plinths)
            {
                var captured = skin;
                var img = BuildRow(skin.name, LocalizationService.Format("ui.by_author_fmt", skin.author), skin.file == activeFile, new Action(() =>
                {
                    PlinthApp?.ApplyPlinthFromSource(captured, new Action<string>(SetStatus));
                    SetStatus($"Applying {captured.name}...");
                }), skin.name, skin.author);

                string key = (string.IsNullOrEmpty(skin.sourceRepo) ? "" : skin.sourceRepo) + "|" + skin.file;
                _coverImgs[key] = img;

                if (catalog.TryGetCover(skin, out var tex) && tex != null) ApplyCover(img, tex);
                else
                {
                    catalog.EnsureCover(skin, true);
                    UGUIShip.CreateStretchLabel(img.transform, "ui.no_preview", FS_SM, HINT);
                }
            }

            SetStatus($"{plinths.Count} UGC plinth(s).");
        }
    }
}
