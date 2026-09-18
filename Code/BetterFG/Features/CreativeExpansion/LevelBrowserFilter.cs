using System;
using System.Collections.Generic;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Core;
using BetterFG.Services;
using BetterFG.UI;
using BetterFG.Utilities;
using FGClient;
using FGClient.UI;
using FGClient.UI.Core;
using MPG.Utility;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using LB = Wushu.LevelEditor.Runtime.UI.LevelBrowser;

namespace BetterFG.Features.CreativeExpansion
{
    internal static class LevelBrowserFilter
    {
        private static readonly string[] SortKeys =
        {
            "level_browse.sort_recent", "level_browse.sort_az", "level_browse.sort_za",
            "level_browse.sort_likes", "level_browse.sort_plays"
        };

        private static readonly string[] ShowKeys =
        {
            "level_browse.show_all", "level_browse.show_changes",
            "level_browse.show_progress", "level_browse.show_completed"
        };

        private static LB.LevelBrowserScreenViewModel _screen;
        private static IntPtr _listPtr;
        private static GameObject _searchChip, _filterChip;
        private static NavPromptHandle _filterPrompt;
        private static readonly List<LB.TileData> _all = new List<LB.TileData>();
        private static int _sort, _show, _shown;
        private static string _search = "";
        private static bool _searchOpen;

        internal static void Tick(LB.LevelBrowserTileViewModel tile)
        {
            if (!FeatureCreativeExpansion.On("levelbrowse") || tile == null || !tile.gameObject.activeInHierarchy) return;

            var screen = tile.GetComponentInParent<LB.LevelBrowserScreenViewModel>();
            if (screen == null) return;

            if (_filterPrompt != null && _filterPrompt.IsAlive && !ReportMenuShell.AnyOpen && !_searchOpen)
                _filterPrompt.IsPressed();

            var list = screen._browserData.TilesData;
            if (screen == _screen && list.Pointer == _listPtr) return;

            _screen = screen;
            _listPtr = list.Pointer;
            _sort = 0;
            _show = 0;
            _search = "";
            _all.Clear();
            for (int i = 0; i < list.Count; i++) _all.Add(list[i]);
            _shown = _all.Count;

            var safe = _screen.transform.Find("SafeArea");
            for (int i = safe.childCount - 1; i >= 0; i--)
            {
                var c = safe.GetChild(i);
                if (c.name.StartsWith("BFG_")) UnityEngine.Object.DestroyImmediate(c.gameObject);
            }
            var proto = safe.Find("Filter Tabs/Prime_UI_LE_LevelFilterTab");
            _searchChip = MakeChip(safe, proto, "BFG_SearchChip", -323f, OpenSearch);
            _filterChip = MakeChip(safe, proto, "BFG_FilterChip", -125f, OpenMenu);

            // the chip keeps its own art and its own label; the prompt nested inside it is there for
            // the glyph and the Options press, so a pad can reach the menu without a second chip.
            // empty label so the chip's Tag stays the only text in there.
            _filterPrompt = NavPromptCore.From(NavPrompt.Report)
                .WithLabel("", "bfg_levelbrowse_filter")
                .CustomAnchors(new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                    new Vector2(-14f, 0f))
                .NoAutoResize()
                .PollActions(RewiredConsts.Action.Default_OpenInGameMenu)
                .JoystickOnly()
                .AlsoAcceptKey(KeyCode.F)
                .OwnGlyph()
                .AllowWhileUnfocused()
                .OnPress(OpenMenu)
                .SpawnOn(_filterChip.transform);
            RefreshChips();
            Plugin.Log.LogInfo($"level browser up, {_all.Count} levels behind the search chip");
        }

        private static GameObject MakeChip(Transform parent, Transform proto, string name, float x, Action onClick)
        {
            var go = UnityEngine.Object.Instantiate(proto.gameObject, parent);
            go.name = name;
            UnityEngine.Object.DestroyImmediate(go.GetComponent<UICustomToggle>());
            UnityEngine.Object.DestroyImmediate(go.GetComponent<LevelEditorTweenOnToggleSubMenu>());
            UnityEngine.Object.DestroyImmediate(go.GetComponent<LB.LevelBrowserFilterButton>());
            UnityEngine.Object.DestroyImmediate(go.GetComponent<UnityEngine.EventSystems.EventTrigger>());

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(x, -67f);
            rt.localScale = new Vector3(0.68f, 0.68f, 1f);

            go.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0f);
            var cg = go.GetComponent<CanvasGroup>();
            cg.alpha = 1f;
            cg.blocksRaycasts = true;
            cg.interactable = true;
            go.AddComponent<Button>().onClick.AddListener((UnityAction)(() => onClick()));
            return go;
        }

        private static void ChipLabel(GameObject chip, string text, bool lit)
        {
            chip.transform.Find("Tab On").gameObject.SetActive(lit);
            chip.transform.Find("Tab Off").gameObject.SetActive(!lit);
            GameObjectHelper.RelabelGameLabel(chip.transform, "Tab On/Content/Tag", text);
            GameObjectHelper.RelabelGameLabel(chip.transform, "Tab Off/Content/Tag", text);
        }

        private static void RefreshChips()
        {
            bool searching = _search.Length != 0;
            string query = _search.Length > 12 ? _search.Substring(0, 12) + "..." : _search;
            ChipLabel(_searchChip, searching ? query.ToUpperInvariant() : LocalizationService.Get("level_browse.chip_search"), searching);

            bool narrowed = _sort != 0 || _show != 0;
            ChipLabel(_filterChip,
                narrowed ? string.Format(LocalizationService.Get("level_browse.chip_count"), _shown, _all.Count)
                         : LocalizationService.Get("level_browse.chip_filter"),
                narrowed);
        }

        private static void Apply()
        {
            var keep = new List<LB.TileData>();
            string q = _search.ToLowerInvariant();
            for (int i = 0; i < _all.Count; i++)
            {
                var t = _all[i];
                if (q.Length != 0 && !(t.Name ?? "").ToLowerInvariant().Contains(q)
                                  && !(t.LevelCode ?? "").ToLowerInvariant().Contains(q)) continue;
                if (_show == 1 && !t.HasUnpublishedChanges) continue;
                if (_show == 2 && !t.IsInProgress) continue;
                if (_show == 3 && !t.IsCompleted) continue;
                keep.Add(t);
            }

            if (_sort == 1) keep.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            if (_sort == 2) keep.Sort((a, b) => string.Compare(b.Name, a.Name, StringComparison.OrdinalIgnoreCase));
            if (_sort == 3) keep.Sort((a, b) => b.LikeCount.CompareTo(a.LikeCount));
            if (_sort == 4) keep.Sort((a, b) => b._playCount.CompareTo(a._playCount));

            _shown = keep.Count;
            var list = _screen._browserData.TilesData;
            list.Clear();
            for (int i = 0; i < keep.Count; i++) list.Add(keep[i]);
            _screen.SetCurrentFilterData(_screen._currentFilter.FilterType, true);

            RefreshChips();
            RelabelMenu();
            Plugin.Log.LogInfo($"browser list now {_shown} of {_all.Count}");
        }

        private static void OpenSearch()
        {
            var d = new ModalMessageWithInputFieldData
            {
                Title = LocalizationService.Get("level_browse.search_title"),
                Message = LocalizationService.Get("level_browse.search_body"),
                LocaliseTitle = UIModalMessage.LocaliseOption.NotLocalised,
                LocaliseMessage = UIModalMessage.LocaliseOption.NotLocalised,
                ModalType = UIModalMessage.ModalType.MT_OK_CANCEL,
                InputText = _search,
                InputTextPlaceholder = LocalizationService.Get("level_browse.search_placeholder"),
                TrimSpacesOnClose = true
            };
            d.OnInputFieldModalClosed = (Action<bool, string>)((ok, txt) =>
            {
                _searchOpen = false;
                if (!ok) return;
                _search = txt ?? "";
                Apply();
            });
            _searchOpen = true;
            SingletonBehaviour<PopupManager>.Instance.Show(PopupInteractionType.Query, d, null);
        }

        private static void OpenMenu()
        {
            LevelBrowserPortPrompt.Instance.StartCoroutine(
                ReportMenuShell.Open("level_browse.menu_title", "level_browse.menu_body", false, WireMenu).WrapToIl2Cpp());
        }

        private static void WireMenu()
        {
            ReportMenuShell.SetHeader(0, "level_browse.header_sort");
            ReportMenuShell.SetHeader(1, "level_browse.header_filter");
            ReportMenuShell.SetRow(0, SortLabel(), () => { _sort = (_sort + 1) % SortKeys.Length; Apply(); });
            ReportMenuShell.HideRow(1);
            ReportMenuShell.HideRow(2);
            ReportMenuShell.SetRow(3, ShowLabel(), () => { _show = (_show + 1) % ShowKeys.Length; Apply(); });
            ReportMenuShell.SetRow(4, SearchLabel(), OpenSearch);
            ReportMenuShell.SetRow(5, ResetLabel(), () => { _sort = 0; _show = 0; _search = ""; Apply(); });
        }

        private static void RelabelMenu()
        {
            if (!ReportMenuShell.AnyOpen) return;
            ReportMenuShell.Relabel(0, SortLabel());
            ReportMenuShell.Relabel(3, ShowLabel());
            ReportMenuShell.Relabel(4, SearchLabel());
            ReportMenuShell.Relabel(5, ResetLabel());
        }

        private static string SortLabel()
            => string.Format(LocalizationService.Get("level_browse.row_sort"), LocalizationService.Get(SortKeys[_sort]));

        private static string ShowLabel()
            => string.Format(LocalizationService.Get("level_browse.row_show"), LocalizationService.Get(ShowKeys[_show]));

        private static string SearchLabel()
            => string.Format(LocalizationService.Get("level_browse.row_search"),
                _search.Length == 0 ? LocalizationService.Get("level_browse.any") : _search);

        private static string ResetLabel()
            => string.Format(LocalizationService.Get("level_browse.row_reset"), _shown, _all.Count);
    }
}
