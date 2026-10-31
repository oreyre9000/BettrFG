using System;
using System.Collections.Generic;
using BetterFG.Customization.UI;
using BetterFG.Utilities;
using UnityEngine;
using BettrFG.uGUI;

namespace BetterFG.UI.Tabs
{
    public class PlinthsInGameTab : PlinthsTab
    {
        public PlinthsInGameTab(IntPtr ptr) : base(ptr) { }

        public override string TabTitle => "Plinths";
        protected override string TitleId => "ui.plinths";
        protected override string TitleDisplay => "Plinths - In-game";

        protected override string SwitchLabel => "ui.ugc";
        protected override Tab MakeSwitchTarget() => BetterFGTabRegistry.NewTab<PlinthsUgcTab>();

        static readonly Dictionary<string, Texture2D> _covers = new Dictionary<string, Texture2D>();

        protected override void BuildRows()
        {
            string active = PlinthApp?.ActiveGameId;
            foreach (var gp in MenuCustomizationApplication.GamePlinths)
            {
                var captured = gp;
                var img = BuildRow(gp.Label, "plinths.from_game", gp.Id == active, new Action(() =>
                {
                    PlinthApp?.ApplyGamePlinth(captured);
                    SetStatus($"Applying {captured.Label}...");
                }));

                if (!_covers.TryGetValue(gp.Id, out var tex) || tex == null)
                {
                    tex = EmbeddedResourceandUnity.LoadTexture(gp.Cover);
                    if (tex != null) _covers[gp.Id] = tex;
                }
                if (tex != null) ApplyCover(img, tex);
                else UGUIShip.CreateStretchLabel(img.transform, "ui.no_preview", FS_SM, HINT);
            }

            SetStatus(BetterFG.Services.LocalizationService.Format("plinths.game_count", MenuCustomizationApplication.GamePlinths.Length));
        }
    }
}
