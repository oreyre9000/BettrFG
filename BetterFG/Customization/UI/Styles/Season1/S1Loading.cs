using System;
using System.Collections.Generic;
using FGClient;
using Il2CppInterop.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.Customization.UI.Styles.Season1
{
    internal static class S1Loading
    {
        const string HOST = "BFG_S1Loading";
        static readonly (string Stock, string S1)[] Texts =
        {
            ("SafeArea/TopLeft_Group/RoundName_Panel/RoundName_Text", "SafeArea/LoadingGameLeftPanel/LoadingGameLevelName/LevelNameBanner/LevelNameText"),
            ("SafeArea/InfoGroup/HowToPlay_Text", "SafeArea/LoadingGameRightPanel/LoadingGameHowToPlayText"),
            ("SafeArea/InfoGroup/Description_Text", "SafeArea/LoadingGameRightPanel/LoadingGameRoundDescriptionText"),
            ("SafeArea/BottomBanner/BottomBanner_Text", "LoadingGameWaitingForPlayersBackground/WaitingForPlayersText"),
        };
        static readonly string[] Replaced = { "SafeArea/TopLeft_Group", "SafeArea/InfoGroup", "SafeArea/SelectedShow", "SafeArea/BottomBanner" };
        static readonly List<(Transform Stock, Transform S1, TMP_Text Src, TMP_Text Dst)> _live = new List<(Transform, Transform, TMP_Text, TMP_Text)>();
        static bool _hooked;

        public static void Apply(UIStyle style, Transform stock)
        {
            var existing = stock.Find(HOST);
            if (existing != null) { Sync(stock, existing); return; }
            var s1 = StyleLayout.Build(style.Assets, "Prefab_UI_Loading_Game", stock).transform;
            s1.name = HOST;
            StyleUndo.Own(s1.gameObject);
            S1Kit.Stretch(s1);
            s1.Find("LoadingGameBackground")?.gameObject.SetActive(false);
            var bg = s1.GetComponent<Image>();
            if (bg != null) bg.enabled = false;
            var cg = s1.GetComponent<CanvasGroup>();
            if (cg != null) { cg.alpha = 1f; cg.blocksRaycasts = false; }
            foreach (var p in Replaced) StyledUI.Hide(stock.Find(p), true);
            s1.SetSiblingIndex(stock.Find("SafeArea")?.GetSiblingIndex() ?? 0);
            var holder = s1.Find("SafeArea/LoadingGameLevelImage/LoadingGameScreenshotImageHolder").gameObject;
            holder.AddComponent<RectMask2D>();
            var holderFit = holder.AddComponent<AspectRatioFitter>();
            holderFit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            holderFit.aspectRatio = 16f / 9f;
            holder.transform.Find("LoadingGameScreenshotImage").gameObject.AddComponent<AspectRatioFitter>().aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            _live.RemoveAll(l => l.Stock == null);
            foreach (var (src, dst) in Texts)
            {
                var a = stock.Find(src)?.GetComponent<TMP_Text>();
                var b = s1.Find(dst)?.GetComponent<TMP_Text>();
                if (a != null && b != null) _live.Add((stock, s1, a, b));
            }
            if (!_hooked)
            {
                _hooked = true;
                TMPro_EventManager.TEXT_CHANGED_EVENT.Add(DelegateSupport.ConvertDelegate<Il2CppSystem.Action<UnityEngine.Object>>(new Action<UnityEngine.Object>(OnText)));
            }
            s1.gameObject.SetActive(true);
            Sync(stock, s1);
        }

        static void OnText(UnityEngine.Object o)
        {
            if (o == null) return;
            foreach (var l in _live)
                if (l.Src != null && l.Src.Pointer == o.Pointer && l.S1 != null) { Sync(l.Stock, l.S1); return; }
        }

        static void Sync(Transform stock, Transform s1)
        {
            foreach (var l in _live)
            {
                if (l.Stock != stock || l.Dst == null) continue;
                l.Dst.text = l.Src.text;
            }
            var final = stock.Find("SafeArea/TopLeft_Group/RoundName_Panel/RightBottomBannerContainer/FinalRoundBanner");
            s1.Find("SafeArea/LoadingGameRightPanel/LoadingGameRoundBanner")?.gameObject.SetActive(final != null && final.gameObject.activeSelf);
            var wait = stock.Find("SafeArea/BottomBanner");
            s1.Find("LoadingGameWaitingForPlayersBackground")?.gameObject.SetActive(wait != null && wait.gameObject.activeSelf);

            var tags = s1.Find("SafeArea/LoadingGameRightPanel/LoadingGameTags");
            if (tags != null && tags.childCount > 0)
            {
                var tpl = tags.GetChild(0).gameObject;
                for (int i = tags.childCount - 1; i >= 1; i--) UnityEngine.Object.DestroyImmediate(tags.GetChild(i).gameObject);
                tpl.SetActive(false);
                var src = stock.Find("SafeArea/InfoGroup/TagsLayout/Content");
                if (src != null)
                    for (int i = 0; i < src.childCount; i++)
                    {
                        var st = src.GetChild(i);
                        var label = st.gameObject.activeSelf ? st.GetComponentInChildren<TMP_Text>(true) : null;
                        if (label == null) continue;
                        var tag = UnityEngine.Object.Instantiate(tpl, tags, false);
                        tag.SetActive(true);
                        var name = tag.transform.Find("Name")?.GetComponent<TMP_Text>();
                        if (name != null) name.text = label.text;
                    }
                var grid = tags.GetComponent<GridLayoutGroup>();
                grid.cellSize = new Vector2(tags.childCount > 3 ? tags.GetComponent<RectTransform>().rect.width / 2 : 220f, grid.cellSize.y);
            }

            var sprite = stock.GetComponentInChildren<LoadingGameScreenViewModel>(true)?.CurrentScreen;
            if (sprite != null)
            {
                var img = s1.Find("SafeArea/LoadingGameLevelImage/LoadingGameScreenshotImageHolder/LoadingGameScreenshotImage");
                img.GetComponent<Image>().sprite = sprite;
                img.GetComponent<AspectRatioFitter>().aspectRatio = sprite.rect.width / sprite.rect.height;
            }
        }
    }
}
