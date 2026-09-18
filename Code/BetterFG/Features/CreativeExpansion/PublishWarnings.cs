using System;
using System.Collections.Generic;
using BetterFG.Core;
using BetterFG.Features.UnityRound;
using BetterFG.Features.UnityRound.Editor;
using BetterFG.Services;
using BetterFG.Utilities;
using FallGuysLib.UI;
using FGClient.UI;
using LevelEditor;

namespace BetterFG.Features.CreativeExpansion
{
    internal static class PublishWarnings
    {
        internal static bool Enabled => FeatureCreativeExpansion.On("publishwarning");

        internal static bool TryCollect(LevelEditorPublishPopupViewModel vm, out string body)
        {
            body = null;
            if (!Enabled) return false;

            var options = LevelEditorOptionsSingleton.Instance;
            if (options == null || !options.LevelHasFinishLine) return false;

            if (BetterFGUnityRounds.DescriptionIsBfgRound(vm.LevelDescription))
            {
                body = LocalizationService.Get("publishwarning.exclusive");
                return true;
            }

            int themes = 0, visuals = 0, materials = 0;
            var col = LevelEditorPlaceableObject.Collection;
            if (col != null)
                for (int i = 0; i < col.Count; i++)
                {
                    var lepo = col[i];
                    if (lepo == null) continue;

                    var kind = MarkerKinds.Of(lepo);
                    if (kind == MarkerKinds.ObjectTheme) themes++;
                    else if (kind == MarkerKinds.CustomLight || kind == MarkerKinds.Thruster) visuals++;
                    else if (kind == MarkerKinds.RuleColour || kind == MarkerKinds.SlimeColour) materials++;
                    else if (NeonMaterial.IsMarkerObject(lepo)) materials++;
                }

            var bullets = new List<string>();
            if (Definers.FindEntry() != null) bullets.Add(LocalizationService.Get("publishwarning.background"));
            if (themes > 0) bullets.Add(string.Format(LocalizationService.Get("publishwarning.themes_fmt"), themes));
            if (visuals > 0) bullets.Add(string.Format(LocalizationService.Get("publishwarning.visualobjects_fmt"), visuals));
            if (materials > 0) bullets.Add(string.Format(LocalizationService.Get("publishwarning.materials_fmt"), materials));

            bool missingPublish = UnityRoundLoader.HasSpawned;
            if (bullets.Count == 0 && !missingPublish) return false;

            var full = new List<string> { LocalizationService.Get("publishwarning.intro"), "" };
            full.AddRange(bullets);
            if (missingPublish)
            {
                full.Add("");
                full.Add($"<color=#FF5555>{LocalizationService.Get("publishwarning.missing_publish")}</color>");
            }

            body = string.Join("\n", full);
            return true;
        }

        internal static void Show(string body, Action onOk)
        {
            NavPromptCore.RegisterCmsString("bfg_publishwarning_title", LocalizationService.Get("publishwarning.title"));
            NavPromptCore.SetCmsString("bfg_publishwarning_body", body);
            PopUp.ShowPopup("bfg_publishwarning_title", "bfg_publishwarning_body",
                PopupInteractionType.Info, UIModalMessage.ModalType.MT_OK,
                UIModalMessage.OKButtonType.Default, (Action<bool>)(_ => onOk()));
        }
    }
}
