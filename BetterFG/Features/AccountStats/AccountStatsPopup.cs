using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Core;
using BetterFG.Customization.UI;
using BetterFG.Services;
using BettrFG.uGUI;
using FGClient;
using FGClient.UI.Core;
using Mediatonic.Tools.MVVM;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using Pop = BetterFG.Tweaks.ShowRoundsPopup;

namespace BetterFG.Features.AccountStats
{
    internal sealed class AccountStatsPopup
    {
        const string RowName = "BFG_AccountStatsRow";
        const float ContentW = 1348f, Gap = 20f, ScrollSpeed = 2800f;
        const int TileColumns = 4;

        static readonly Color Cyan = new Color32(0x32, 0xC9, 0xEB, 0xFF);
        static readonly Color Pink = new Color32(0xF7, 0x35, 0xA2, 0xFF);
        static readonly Color Yellow = new Color32(0xFF, 0xC6, 0x00, 0xFF);
        static readonly Color Green = new Color32(0x00, 0xD8, 0x50, 0xFF);
        static readonly Color GridLine = new Color(1f, 1f, 1f, 0.18f);
        static readonly Color Soft = new Color(1f, 1f, 1f, 0.8f);
        static readonly Color Rim = new Color32(0x00, 0x61, 0xB6, 0xFF);
        static readonly Color Track = new Color(0f, 0.1f, 0.35f, 0.45f);
        static readonly Color TileBlue = new Color32(0x1D, 0x7E, 0xD8, 0xFF);

        static readonly HashSet<string> ViewModels = new HashSet<string> { "LiveEventsChallengeViewModel", "LiveEventsChallengeAnims", "ChallengesEntryViewModel", "UITweenTriggerComponent",
            "UITweenData", "RewardViewModel", "GenericItemViewModel", "EnabledComponentBinding", "GraphicColorBinding", "RemainingTimeViewModel", "SliderBinding", "ImageFillBinding",
            "ProgressViewModel", "PlayerLevelViewModel", "ToggleActiveWithFeatureFlag", "UIButtonSolo", "NavigateToScreenButton", "NameTagViewModel", "CrownRankBadgeViewModel",
            "UIButtonMenuTab", "SeasonProgressViewModel", "OnInputKeyDownClickUIButton", "ChallengesMainMenuViewModel", "ScrollTextTween", "CrownRankViewModel", "FocusableGridOption",
            "InstantiateGameObjectOneShotBinding", "ScaleOnEnable", "ImageWithRoundedCorners", "SkinPatternWithRoundedCorners", "FaceplateWithRoundedCorners", "EmoteOptionViewModel",
            "SkinPatternOptionViewModel", "ColourOptionViewModel", "FaceplateOptionViewModel", "EmoticonOptionViewModel", "PhraseOptionViewModel", "SpritePivotOffset", "Button" };

        static AccountStatsPopup _open;
        static Dictionary<string, ItemDefinitionSO> _defs;
        static Dictionary<string, Sprite> _art;
        static GameObject _kit, _rowPf, _crownPf, _tilePf, _barPf, _headerPf, _nameTagPf, _crownBadgePf;

        readonly AccountStatsData _d;
        readonly EventSystem _es;
        readonly GameObject _prevSel;
        readonly List<string> _tabIds = new List<string>();
        readonly List<Toggle> _tabs = new List<Toggle>();
        readonly List<(GameObject go, CanvasGroup selected, Action act)> _buttons = new List<(GameObject, CanvasGroup, Action)>();
        readonly Dictionary<int, float> _repeatAt = new Dictionary<int, float>();
        readonly bool[] _held = new bool[4];
        GameObject _root;
        RectTransform _content;
        ScrollRect _scroll;
        TMP_FontAsset _titleFont, _bodyFont;
        Material _titleMat, _bodyMat;
        Sprite _plate, _rim, _capsule;
        GameObject[] _kbNav, _padNav;
        int _cur = -1, _sel = -1, _openFrame;
        bool _closing;

        internal static void InjectProfileRow(SettingsScreenViewModel screen)
        {
            var page = screen.transform.Find("SettingsSubMenu_Prime_Canvas/SafeArea/ProfileSwitchboard_Prime_Canvas");
            var list = page.Find("Mask/TabScrollContainer/TabViewport/TabContent");
            if (list.Find(RowName) != null) return;
            var template = list.Find("CopyEpicAccountID");

            var row = Object.Instantiate(template.gameObject, list, false);
            row.name = RowName;
            row.transform.SetSiblingIndex(template.GetSiblingIndex() + 1);
            var label = row.transform.Find("SettingsText");
            label.GetComponent("LocalisedStaticLabel").TryCast<Behaviour>().enabled = false;
            UGUIShip.RelabelText(label.GetComponent<TextMeshProUGUI>(), "account_stats.open");

            var cfg = row.GetComponent<SettingConfigurationViewModel>();
            Pop.Mute(cfg._OnPress);
            cfg._OnPress.AddListener(new Action(Open));

            var input = page.GetComponent<SettingsInputHandler>();
            var elems = input._settingElements;
            int at = elems.IndexOf(template.GetComponent<SettingConfigurationViewModel>()) + 1;
            input.AddSettingElement(cfg, false);
            elems.Remove(cfg);
            elems.Insert(at, cfg);
            Plugin.Log.LogInfo($"account stats row slotted into profile settings at {at}");
        }

        internal static void Open()
        {
            if (_open != null || !Pop.FindPrefabs() || !FindKit()) return;
            _open = new AccountStatsPopup(AccountStatsData.Read());
            MenuCustomizationApplication.Instance.StartCoroutine(Run().WrapToIl2Cpp());
        }

        static bool FindKit()
        {
            if (_defs == null)
            {
                _defs = new Dictionary<string, ItemDefinitionSO>();
                foreach (var d in Resources.FindObjectsOfTypeAll<ItemDefinitionSO>()) _defs[d.ItemId] = d;
            }
            if (_kit != null) return true;
            var builder = MenuLayoutStage.BuilderPrefab();
            if (builder == null) return false;
            var cfg = builder.GetComponent<MainMenuBuilder>()._builderConfig.GetSharedConfig();
            var main = cfg.MainMenuPrefab.transform;
            var top = main.Find("SafeAreaTop/LayoutGroup/TopLeft_Group/NameTagAndCrowns");
            _kit = new GameObject("BFG_AccountStatsKit") { hideFlags = HideFlags.HideAndDontSave };
            _kit.SetActive(false);
            Object.DontDestroyOnLoad(_kit);
            _rowPf = Keep(cfg.LiveEventsScreenPrefab.transform.Find("SafeArea/ChallengesPanel").GetComponent<CollectionBinding>()._prefab);
            _crownPf = Keep(cfg.SeasonCrownRanksScreenPrefab.transform.Find("Prime_UI_MenuCrownRank_Prefab/SafeArea/Generic_UI_CrownRankRewardScroll_Prefab/CrownRankRewardsViewport/CrownRankRewardsContent/CrownRankRewards").GetComponent<CollectionBinding>()._prefab);
            _nameTagPf = Keep(top.Find("PB_UI_NameTag"));
            _crownBadgePf = Keep(top.Find("CrownRank"));
            _barPf = Keep(main.Find("SafeArea/BottomLeft_Group/AnimContainer/SeasonPassBanner/SeasonPassHolder/Prime_UI_SeasonBannerProgressBar_3Variant"));
            _headerPf = Keep(main.Find("SafeArea/TopRight_Group/Prime_UI_ChallengesTab_Prefab/ChallengesHolder/Header"));
            _tilePf = Keep(cfg.CustomiserPrefab.transform.Find("Generic_UI_CustomizerLandingPage_Prefab/SafeArea/HorizontalLayout/Generic_UI_CustomizerLandingOutfitsButton_Variant"));
            foreach (var go in new[] { _nameTagPf, _crownBadgePf })
                foreach (var comp in go.GetComponents<Component>())
                    if (comp.GetIl2CppType().Name is "NavigateToScreenButton" or "UIButtonSolo" or "ToggleActiveWithFeatureFlag") Object.DestroyImmediate(comp);
            Plugin.Log.LogInfo("stats kit copied out of the menu prefabs, parked it hidden");
            return true;
        }

        static GameObject Keep(Component source) => Object.Instantiate(source.gameObject, _kit.transform, false);

        static Sprite Art(string name)
        {
            if (_art == null)
            {
                _art = new Dictionary<string, Sprite>();
                foreach (var s in Resources.FindObjectsOfTypeAll<Sprite>()) _art[s.name] = s;
            }
            return _art.TryGetValue(name, out var sprite) ? sprite : null;
        }

        static IEnumerator Run()
        {
            while (_open != null && _open.Tick()) yield return null;
            _open = null;
        }

        static string L(string key) => LocalizationService.Get("account_stats." + key);
        static string F(string key, params object[] args) => LocalizationService.Format("account_stats." + key, args);
        static string N(long v) => v.ToString("N0");
        static string Date(DateTime d) => d.ToString("d MMM yyyy");
        static string Hi(string s) => "<color=#31C9EBFF>" + s + "</color>";

        AccountStatsPopup(AccountStatsData data)
        {
            _d = data;
            _es = EventSystem.current;
            _prevSel = _es.currentSelectedGameObject;
            _openFrame = Time.frameCount;

            _root = new GameObject("BFG_AccountStats");
            _root.SetActive(false);
            var canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30000;
            var scaler = _root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 1f;
            _root.AddComponent<GraphicRaycaster>();

            var popup = Object.Instantiate(Pop.PopupPrefab, _root.transform);
            var tabTpl = Object.Instantiate(Pop.TabPrefab, _root.transform);
            var c = popup.transform.Find("Container");
            foreach (var p in new[] { "CodeInputGroup", "MissingInfo_Text", "Mobile Confirm Button", "Title/EnterCodeText", "ShowInfo", "ShowsScrollList/ShowsListViewport/ShowsListContent/Tooltip", "ShowsScrollList/ShowsListViewport/ShowsListContent/EnterCodeButton" })
                Object.DestroyImmediate(c.Find(p).gameObject);
            Pop.StripBindings(popup, tabTpl);

            var tileTitle = _tilePf.transform.Find("Title_Text").GetComponent<TextMeshProUGUI>();
            _titleFont = tileTitle.font;
            _titleMat = tileTitle.fontSharedMaterial;
            var rowDesc = _rowPf.transform.Find("AlwaysActive/ChallengesDescription/ChallengesDescriptionText").GetComponent<TextMeshProUGUI>();
            _bodyFont = rowDesc.font;
            _bodyMat = rowDesc.fontSharedMaterial;
            _plate = _rowPf.transform.Find("AlwaysActive/Background").GetComponent<Image>().sprite;
            _rim = _rowPf.transform.Find("AlwaysActive/Background/BackgroundBorder").GetComponent<Image>().sprite;
            _capsule = _rowPf.transform.Find("Overlay").GetComponent<Image>().sprite;

            Pop.Place(c, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -20), new Vector2(1500, 880));
            var title = c.Find("Title");
            Pop.Place(title, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(40, -24), title.GetComponent<RectTransform>().rect.size);
            title.Find("ShowInfoText").GetComponent<TextMeshProUGUI>().text = L("title");
            var nameTag = BuildIdentity(c);

            _scroll = c.Find("ShowsScrollList").GetComponent<ScrollRect>();
            Pop.Stretch(_scroll.transform, 40, 32, 40, 232);
            Pop.Stretch(_scroll.viewport, 0, 0, 44, 0);
            Pop.Place(c.Find("ShowsScrollList/TabScrollbar"), new Vector2(1, 0), new Vector2(1, 1), new Vector2(1, 0.5f), Vector2.zero, new Vector2(24, -24));
            _content = _scroll.content;
            var vlg = _content.GetComponent<VerticalLayoutGroup>();
            vlg.spacing = Gap;
            vlg.padding = new RectOffset(14, 14, 14, 30);
            vlg.childControlHeight = vlg.childControlWidth = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            _tabIds.AddRange(new[] { "overview", "activity", "crowns", "fame", "locker", "achievements" });
            if (_d.Levels.Count > 0) _tabIds.Add("creative");

            var carousel = c.Find("TabCarrouselShows");
            Pop.Place(carousel, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -142), new Vector2(0, 66));
            var tabsRoot = carousel.Find("TabsContent");
            var group = tabsRoot.GetComponent<ToggleGroup>();
            group.allowSwitchOff = false;
            tabTpl.SetActive(false);
            tabTpl.transform.Find("PrivateLobbyShowTab/Content/SubMenuCreativeIcon").gameObject.SetActive(false);
            tabTpl.transform.Find("PrivateLobbyShowTab/Content/SubMenuFavouriteIcon").gameObject.SetActive(false);
            for (int i = 0; i < _tabIds.Count; i++) BuildTab(tabTpl, tabsRoot, group, i);
            carousel.Find("TabCarrouselNavigationLeft").GetComponent<Button>().onClick.AddListener(new Action(() => Select(_cur - 1)));
            carousel.Find("TabCarrouselNavigationRight").GetComponent<Button>().onClick.AddListener(new Action(() => Select(_cur + 1)));
            _kbNav = new[] { carousel.Find("TabCarrouselNavigationLeft").gameObject, carousel.Find("TabCarrouselNavigationRight").gameObject };
            _padNav = new[] { carousel.Find("TabCarrouselNavigationLeft_Controller").gameObject, carousel.Find("TabCarrouselNavigationRight_Controller").gameObject };

            _root.SetActive(true);
            nameTag.UpdateDisplayWithLocalPlayer();
            c.GetComponent<TweenOnPopup>().BeginTween();
            Select(0);
            LayoutRebuilder.ForceRebuildLayoutImmediate(tabsRoot.GetComponent<RectTransform>());
            LayoutRebuilder.ForceRebuildLayoutImmediate(carousel.GetComponent<RectTransform>());
            FGInputLockService.SetScreenLock(true);

            _d.FetchAchievements(OnAchievements);
        }

        NameTagViewModel BuildIdentity(Transform container)
        {
            var tag = Object.Instantiate(_nameTagPf, container, false);
            Pop.Place(tag.transform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-160, -26), new Vector2(380, 100));
            var badge = Object.Instantiate(_crownBadgePf, container, false);
            Pop.Place(badge.transform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-36, -16), new Vector2(128, 150));
            badge.transform.localScale = new Vector3(0.78f, 0.78f, 1f);
            return tag.GetComponent<NameTagViewModel>();
        }

        GameObject Clone(GameObject source, Transform parent)
        {
            var go = Object.Instantiate(source, parent, false);
            Pop.StripBindings(ViewModels, go);
            go.SetActive(true);
            return go;
        }

        void OnAchievements()
        {
            if (_root == null) return;
            if (_tabIds[_cur] == "achievements" || _tabIds[_cur] == "overview") Rebuild();
        }

        void BuildTab(GameObject tpl, Transform parent, ToggleGroup group, int index)
        {
            var t = Object.Instantiate(tpl, parent).transform;
            var tt = t.Find("PrivateLobbyShowTab/Content/SubMenuTabText").GetComponent<TextMeshProUGUI>();
            tt.text = L("tab." + _tabIds[index]);
            float w = Mathf.Max(150f, tt.GetPreferredValues().x + 44f);
            var le = t.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = w;
            le.preferredHeight = 65f;
            t.GetComponent<RectTransform>().sizeDelta = new Vector2(w, 65f);
            foreach (var rt in t.GetComponentsInChildren<RectTransform>(true))
            {
                if (rt.transform == t || rt.parent.name == "Content") continue;
                float inset = rt.name == "Content" ? 12f : rt.parent.name == "SubMenuTabActive" && rt.name == "Panel_Fill" ? -1f : 0f;
                Pop.Stretch(rt, inset, inset, inset, inset);
            }
            var tog = t.GetComponentInChildren<Toggle>(true);
            Pop.Mute(tog.onValueChanged);
            tog.isOn = false;
            tog.group = group;
            tog.onValueChanged.AddListener(new Action<bool>(on => { if (on) Select(index); }));
            t.gameObject.SetActive(true);
            _tabs.Add(tog);
        }

        void Select(int index)
        {
            index = (index + _tabIds.Count) % _tabIds.Count;
            if (index == _cur) return;
            _cur = index;
            if (!_tabs[_cur].isOn) _tabs[_cur].isOn = true;
            Rebuild();
        }

        void SelectTab(string id) => Select(_tabIds.IndexOf(id));

        void Rebuild()
        {
            for (int i = _content.childCount - 1; i >= 0; i--) Object.DestroyImmediate(_content.GetChild(i).gameObject);
            _buttons.Clear();
            _sel = -1;
            _es.SetSelectedGameObject(null);
            switch (_tabIds[_cur])
            {
                case "overview": BuildOverview(); break;
                case "activity": BuildActivity(); break;
                case "crowns": BuildCrowns(); break;
                case "fame": BuildFame(); break;
                case "locker": BuildLocker(); break;
                case "achievements": BuildAchievements(); break;
                case "creative": BuildCreative(); break;
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
            _scroll.StopMovement();
            _content.anchoredPosition = new Vector2(_content.anchoredPosition.x, 0f);
            if (_buttons.Count > 0) SelectButton(0);
        }

        void BuildOverview()
        {
            var g = Grid(TileColumns, 230, 8);
            Tile(g, L("crowns.lifetime"), N(_d.EarnedCrowns), F("crowns.spent", N(_d.CrownsSpent)), () => SelectTab("crowns"));
            Tile(g, L("crowns.rank"), _d.CrownRank.ToString(), F("crowns.to_next", N(_d.NextRankCrowns - _d.EarnedCrowns), _d.CrownRank + 1), () => SelectTab("crowns"));
            Tile(g, L("activity.shows_won"), N(_d.Stat("episodes_won_total")), F("activity.best_streak", _d.Stat("episodes_won_streak_best")), () => SelectTab("activity"));
            Tile(g, L("fame.lifetime"), N(_d.LifetimeFame), F("fame.maxed", _d.PassesMaxed, _d.Seasons.Count), () => SelectTab("fame"));
            if (_d.PlaytimeMinutes.HasValue)
                Tile(g, L("activity.playtime"), F("hours", N(_d.PlaytimeMinutes.Value / 60)), _d.Playtime2WeeksMinutes.HasValue ? F("activity.two_weeks", N(_d.Playtime2WeeksMinutes.Value / 60)) : null, () => SelectTab("activity"));
            else
                Tile(g, L("activity.jumps"), N(_d.Stat("actions_done_jumps_total")), null, () => SelectTab("activity"));
            Tile(g, L("locker.owned"), N(_d.Items.Count), F("locker.favourited", _d.Favourites), () => SelectTab("locker"));
            Tile(g, L("achievements.title"), _d.AchievementsLoaded ? _d.AchievementsDone + " / " + _d.Achievements.Count : "...", null, () => SelectTab("achievements"));
            Tile(g, L("activity.jumps"), N(_d.Stat("actions_done_jumps_total")), F("activity.dives_line", N(_d.Stat("actions_done_dived_total"))), () => SelectTab("activity"));

            MonthGraph(Section(L("graph.unlocks"), 380), L("graph.unlocks_legend"));
        }

        void BuildActivity()
        {
            if (_d.StatsSince.HasValue) Heading(F("activity.since", Date(_d.StatsSince.Value)));
            var hero = Grid(4, 300, 4);
            if (_d.PlaytimeMinutes.HasValue)
                ArtTile(hero, L("activity.playtime"), F("hours", N(_d.PlaytimeMinutes.Value / 60)), _d.Playtime2WeeksMinutes.HasValue ? F("activity.two_weeks", N(_d.Playtime2WeeksMinutes.Value / 60)) : null, null, Art("UI_BigClock_Icon"), null, null);
            ArtTile(hero, L("activity.shows_won"), N(_d.Stat("episodes_won_total")), F("activity.best_streak", _d.Stat("episodes_won_streak_best")), null, Art("UI_CrownCurrency_Image"), null, null);
            ArtTile(hero, L("activity.party_shows"), N(_d.Stat("shows_played_in_party_total")), null, null, Art("UI_PartyIcon_Image"), null, null);
            ArtTile(hero, L("activity.skips"), N(_d.Stat("skip_count")), null, null, Art("HUD_party_face_icon_skipped"), null, null);

            Heading(L("activity.moves"));
            var moves = Grid(4, 300, 8);
            ArtTile(moves, L("activity.jumps"), N(_d.Stat("actions_done_jumps_total")), null, null, Art("Icon_Jump"), null, null);
            ArtTile(moves, L("activity.dives"), N(_d.Stat("actions_done_dived_total")), F("activity.best_show_n", N(_d.Stat("actions_done_dived_max"))), null, Art("Icon_Dive"), null, null);
            ArtTile(moves, L("activity.grabs"), N(_d.Stat("actions_done_grabbed_total")), F("activity.best_show_n", N(_d.Stat("actions_done_grabbed_max"))), null, Art("Icon_Grab"), null, null);
            ArtTile(moves, L("activity.hugs"), N(_d.Stat("actions_done_hugged_total")), F("activity.best_show_n", N(_d.Stat("actions_done_hugged_max"))), null, Art("UI_BigHeart_Image"), null, null);
            ArtTile(moves, L("activity.falls"), N(_d.Stat("actions_done_fell_total")), F("activity.best_show_n", N(_d.Stat("actions_done_fell_max"))), null, Art("HUD_party_face_icon_eliminated"), null, null);
            ArtTile(moves, L("activity.emotes"), N(_d.Stat("actions_done_emote_total")), null, null, Art("UI_BigTheatrics_Image"), null, null);
            ArtTile(moves, L("activity.tails"), N(_d.Stat("actions_done_tail_stolen_total")), null, null, Art("UI_Medal_Icon_TailTag"), null, null);
            ArtTile(moves, L("activity.air_time"), F("hours", N(_d.Stat("air_time_total") / 3600)), F("activity.seconds", N(_d.Stat("air_time_total"))), null, Art("UI_Medal_Icon_AirTime"), null, null);

            Heading(L("activity.badges"));
            var badges = Grid(3, 320, 3);
            ArtTile(badges, L("activity.gold"), N(_d.Stat("badges_won_gold_total")), null, null, Art("UI_Medal_Backing_Gold"), null, null);
            ArtTile(badges, L("activity.silver"), N(_d.Stat("badges_won_silver_total")), null, null, Art("UI_Medal_Backing_Silver"), null, null);
            ArtTile(badges, L("activity.bronze"), N(_d.Stat("badges_won_bronze_total")), null, null, Art("UI_Medal_Backing_Bronze"), null, null);

            BarChart(Section(L("activity.rounds"), 330), new[]
            {
                (L("activity.rounds_played"), _d.Stat("rounds_played"), Cyan),
                (L("activity.rounds_qualified"), _d.Stat("rounds_qualified_total"), Cyan),
                (L("activity.finals"), _d.Stat("rounds_reached_final_total"), Yellow),
                (L("activity.rounds_won"), _d.Stat("rounds_won_total"), Pink),
                (L("activity.knockout"), _d.Stat("rounds_played_knockout"), Green),
            });

            var rows = Rows(2);
            if (_d.LastPlayed.HasValue) Row(rows, Date(_d.LastPlayed.Value), L("activity.last_played"), null, null);
            Row(rows, N(_d.Stat("episodes_won_streak_current")), L("activity.current_streak_title"), null, null);
        }

        void BuildCrowns()
        {
            var hero = Grid(4, 300, 4);
            ArtTile(hero, L("crowns.lifetime"), N(_d.EarnedCrowns), null, null, Art("UI_CrownCurrency_Image"), null, null);
            ArtTile(hero, L("crowns.wallet"), N(_d.WalletCrowns), F("crowns.spent", N(_d.CrownsSpent)), null, Art("UI_BorderlessCrown_Image"), null, null);
            ArtTile(hero, L("crowns.rank"), _d.CrownRank + " / " + _d.MaxCrownRank, F("crowns.to_next", N(_d.NextRankCrowns - _d.EarnedCrowns), _d.CrownRank + 1), null, Art("UI_CrownRankBadge_Image"), null, null);
            ArtTile(hero, L("crowns.shards"), N(_d.CrownShards), null, null, Art("UI_CrownShard_Image") ?? Art("UI_GrabCrownBubbleFill_Image"), null, null);

            float span = Mathf.Max(1, _d.NextRankCrowns - _d.ThisRankCrowns);
            Bar(_d.CrownRank.ToString(), (_d.EarnedCrowns - _d.ThisRankCrowns) / span, N(_d.EarnedCrowns) + " / " + N(_d.NextRankCrowns), Cyan);

            Heading(L("crowns.rewards"));
            var rewards = CrownRankManager.Instance.CrownRankRewards;
            var g = Grid(5, 330, rewards.Count);
            for (int i = 0; i < rewards.Count; i++)
            {
                var tile = Object.Instantiate(_crownPf, g, false);
                tile.SetActive(true);
                tile.GetComponent<CrownRankViewModel>().SetCrownReward(rewards[i]);
                var tip = LayTile(tile);
                if (i >= _d.CrownRewards.Count || !_d.CrownRewards[i].ReachedAt.HasValue) continue;
                tip.gameObject.SetActive(true);
                var amount = tip.Find("Panel_Fill/AmountText").GetComponent<TextMeshProUGUI>();
                Object.DestroyImmediate(amount.GetComponent("TextBinding"));
                amount.text = F("crowns.reached", Date(_d.CrownRewards[i].ReachedAt.Value));
            }
        }

        void BuildFame()
        {
            var hero = Grid(3, 300, 3);
            ArtTile(hero, L("fame.lifetime"), N(_d.LifetimeFame), null, null, Art("UI_Famepass_Ticket_Icon"), null, null);
            ArtTile(hero, L("fame.passes"), _d.PassesMaxed + " / " + _d.Seasons.Count, null, null, Art("UI_ToggleButtonSeasonIcon_Image"), null, null);
            ArtTile(hero, L("fame.skipped"), N(_d.PremiumSkipped), null, null, Art("UI_SeasonPass_Lock"), null, null);

            var bars = new List<(string, int, Color)>();
            foreach (var s in _d.Seasons) bars.Add((SeasonName(s.Id), s.Fame, Pink));
            BarChart(Section(L("fame.per_season"), 320), bars.ToArray());

            Heading(L("fame.tiers_title"));
            foreach (var s in _d.Seasons)
                Bar(s.TiersUnlocked.ToString(), (float)s.TiersUnlocked / Mathf.Max(1, s.TiersTotal), SeasonName(s.Id) + "  " + F("fame.tiers", s.TiersUnlocked, s.TiersTotal), Pink);
        }

        static string SeasonName(string id) => string.Format(LocalizationService.Get("account_stats.fame.season"), id.Replace("famepass_", ""));

        void BuildLocker()
        {
            var newest = new List<AccountStatsData.OwnedItem>(_d.Items);
            newest.Sort((a, b) => b.EarnedAt.CompareTo(a.EarnedAt));
            var oldest = newest.FindLast(i => i.EarnedAt.Year >= 2000);

            var hero = Grid(4, 300, 4);
            ArtTile(hero, L("locker.owned"), N(_d.Items.Count), null, null, Art("UI_BigOutfits_Image"), null, null);
            ArtTile(hero, L("locker.favourites"), N(_d.Favourites), null, null, Art("UI_BigHeart_Image"), null, null);
            if (oldest.Id != null) ArtTile(hero, L("locker.first"), Date(oldest.EarnedAt), F("locker.years", _d.YearsPlaying), null, null, null, oldest.Id);
            if (newest.Count > 0) ArtTile(hero, L("locker.newest"), Date(newest[0].EarnedAt), null, null, null, null, newest[0].Id);

            Heading(L("locker.by_type"));
            var cats = _d.Categories.FindAll(kv => kv.Value > 0);
            var catGrid = Grid(4, 300, cats.Count);
            foreach (var kv in cats)
            {
                var sample = newest.Find(i => i.Category == kv.Key);
                ArtTile(catGrid, L("cat." + kv.Key), N(kv.Value), null, null, null, null, sample.Id);
            }
            Heading(L("locker.recent"));
            ItemTiles(newest.GetRange(0, Math.Min(10, newest.Count)), true);

            var favs = _d.Items.FindAll(i => i.Favourite);
            if (favs.Count == 0) return;
            Heading(L("locker.favourites"));
            ItemTiles(favs, false);
        }

        void ItemTiles(List<AccountStatsData.OwnedItem> items, bool dates)
        {
            var g = Grid(5, 330, items.Count);
            foreach (var it in items)
            {
                var tile = Clone(_crownPf, g);
                LayTile(tile);
                var item = tile.transform.Find("Generic_UI_CrownRankItem_2Variant");
                item.Find("BackgroundMask/Background_Selected").gameObject.SetActive(false);
                item.Find("BackgroundMask/Background_Fill").GetComponent<Image>().color = TileBlue;
                item.Find("LockedFilter").gameObject.SetActive(false);
                item.Find("TopElementsContainer/TopRight_Container/UnlockedTick").gameObject.SetActive(false);
                item.Find("MilestoneCrownMarker_PlaceHolder").gameObject.SetActive(false);
                item.Find("Frame/GoldenFrame").gameObject.SetActive(false);
                item.Find("Frame/SelectedState").gameObject.SetActive(false);
                item.Find("Sheen_Mask").gameObject.SetActive(false);
                var styles = item.Find("RewardContainer/GenericItemDisplayContainer/Items");
                for (int i = 0; i < styles.childCount; i++) styles.GetChild(i).gameObject.SetActive(i == 0);
                var img = styles.Find("ItemStyleContainer/ItemStyleSprite").GetComponent<Image>();
                img.preserveAspect = true;
                img.enabled = false;
                if (_defs.TryGetValue(it.Id, out var def))
                    def.RequestMenuDisplaySprite(_root, new Action<Sprite>(s => { if (img == null || s == null) return; img.sprite = s; img.enabled = true; }));

                var bottom = tile.transform.Find("Generic_UI_CrownRankRewardBottomPanel_Prefab");
                for (int i = 0; i < bottom.childCount; i++) bottom.GetChild(i).gameObject.SetActive(bottom.GetChild(i).name == "Generic_UI_CrownRankProgressTooltip_Prefab");
                var pill = bottom.Find("Generic_UI_CrownRankProgressTooltip_Prefab");
                Pop.Place(pill, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(240, 56));
                var tip = pill.Find("Panel_Fill");
                tip.Find("PanelTip").gameObject.SetActive(false);
                tip.Find("CrownIcon").gameObject.SetActive(false);
                tip.Find("MaxText").gameObject.SetActive(false);
                string name = def != null && !string.IsNullOrEmpty(def.DisplayName) ? def.DisplayName : it.Id;
                var label = tip.Find("AmountText").GetComponent<TextMeshProUGUI>();
                label.text = dates ? name + "  <size=70%>" + Date(it.EarnedAt) + "</size>" : name;
                label.fontSize = 22;
                label.enableAutoSizing = true;
                label.fontSizeMin = 14;
                label.fontSizeMax = 24;
            }
        }

        static Transform LayTile(GameObject tile)
        {
            Object.DestroyImmediate(tile.GetComponent<AspectRatioFitter>());
            var t = tile.transform;
            Pop.Place(t.Find("Generic_UI_CrownRankItem_2Variant"), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -16), new Vector2(238, 235));
            var bottom = t.Find("Generic_UI_CrownRankRewardBottomPanel_Prefab");
            Pop.Place(bottom, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(0, 76));
            foreach (var arrows in new[] { "DisabledArrows_Container", "ActiveArrows_Container", "AnimateArrows_Container" }) bottom.Find(arrows).localScale = Vector3.zero;
            var tip = bottom.Find("Generic_UI_CrownRankProgressTooltip_Prefab");
            tip.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, -8);
            return tip;
        }

        void BuildAchievements()
        {
            if (!_d.AchievementsLoaded) { Heading(L("loading")); return; }
            _d.RefreshAchievementIcons();
            AccountStatsData.Achievement first = null, latest = null;
            foreach (var a in _d.Achievements)
            {
                if (!a.CompletedAt.HasValue) continue;
                if (first == null || a.CompletedAt.Value < first.CompletedAt.Value) first = a;
                if (latest == null || a.CompletedAt.Value > latest.CompletedAt.Value) latest = a;
            }
            var rarest = _d.Achievements.Find(a => a.CompletedAt.HasValue && a.Percent >= 0);

            var hero = Grid(4, 300, 4);
            ArtTile(hero, L("achievements.done"), _d.AchievementsDone + " / " + _d.Achievements.Count, null, null, Art("UI_Tick3_Image"), null, null);
            if (rarest != null) ArtTile(hero, L("achievements.rarest"), rarest.Percent.ToString("0.0") + "%", rarest.Name, null, null, rarest.Icon, null);
            if (first != null) ArtTile(hero, L("achievements.first"), Date(first.CompletedAt.Value), first.Name, null, null, first.Icon, null);
            if (latest != null) ArtTile(hero, L("achievements.latest"), Date(latest.CompletedAt.Value), latest.Name, null, null, latest.Icon, null);

            Heading(L("achievements.all"));
            var list = Rows(_d.Achievements.Count);
            foreach (var a in _d.Achievements) AchievementRow(list, a);
        }

        void BuildCreative()
        {
            int published = 0;
            foreach (var l in _d.Levels) if (l.Published) published++;
            var rows = Rows(4);
            Row(rows, N(_d.Levels.Count), L("creative.levels") + "  " + Hi(F("creative.published", published)), null, null);
            Row(rows, N(_d.LevelPlays), L("creative.plays"), null, null);
            Row(rows, N(_d.LevelLikes), L("creative.likes"), null, null);

            var sorted = new List<AccountStatsData.CreativeLevel>(_d.Levels);
            sorted.Sort((a, b) => b.Plays.CompareTo(a.Plays));
            Heading(L("creative.your_levels"));
            var list = Rows(sorted.Count);
            foreach (var l in sorted)
                Row(list, string.IsNullOrEmpty(l.Title) ? l.Code : l.Title, F("creative.level_line", Hi(N(l.Plays)), Hi(N(l.Likes))) + "   " + l.Code, N(l.Likes), L("creative.likes_short"));
        }

        void Tile(Transform parent, string caption, string value, string sub, Action onClick) => ArtTile(parent, caption, value, sub, onClick, null, null, null);

        void ArtTile(Transform parent, string caption, string value, string sub, Action onClick, Sprite art, Texture artTex, string itemId)
        {
            var tile = Clone(_tilePf, parent);
            Object.DestroyImmediate(tile.GetComponent<AspectRatioFitter>());
            var titleText = tile.transform.Find("Title_Text").GetComponent<TextMeshProUGUI>();
            titleText.text = caption;
            titleText.enableAutoSizing = true;
            titleText.fontSizeMin = 18;
            titleText.fontSizeMax = 30;
            titleText.enableWordWrapping = false;

            var icon = tile.transform.Find("Icon").GetComponent<Image>();
            bool hasArt = art != null || artTex != null || itemId != null;
            icon.gameObject.SetActive(hasArt);
            if (hasArt)
            {
                Pop.Stretch(icon.transform, 40, sub == null ? 78 : 102, 40, 84);
                icon.preserveAspect = true;
                icon.sprite = art;
                if (artTex != null)
                {
                    icon.enabled = false;
                    var raw = Rect(icon.transform, "Art").gameObject.AddComponent<RawImage>();
                    raw.texture = artTex;
                    raw.raycastTarget = false;
                    raw.gameObject.AddComponent<AspectRatioFitter>().aspectMode = AspectRatioFitter.AspectMode.FitInParent;
                }
                if (itemId != null)
                {
                    icon.enabled = false;
                    if (_defs.TryGetValue(itemId, out var def))
                        def.RequestMenuDisplaySprite(_root, new Action<Sprite>(s => { if (icon == null || s == null) return; icon.sprite = s; icon.enabled = true; }));
                }
            }

            var big = Text(tile.transform, value, 64, TextAlignmentOptions.Center, Color.white, _titleFont, _titleMat, 18, sub == null ? 16 : 52, 18, 76);
            if (hasArt) Pop.Place(big.transform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, sub == null ? 14 : 38), new Vector2(-36, 62));
            big.enableAutoSizing = true;
            big.fontSizeMin = 24;
            big.fontSizeMax = hasArt ? 50 : 66;
            if (sub != null) Text(tile.transform, sub, 22, TextAlignmentOptions.Bottom, Soft, _bodyFont, _bodyMat, 14, 14, 14, 0);

            var selected = tile.transform.Find("Panel_Selected").GetComponent<CanvasGroup>();
            selected.alpha = 0f;
            if (onClick == null) return;
            var button = tile.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(new Action(() => onClick()));
            var trig = tile.AddComponent<EventTrigger>();
            var hover = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            int index = _buttons.Count;
            hover.callback.AddListener(new Action<BaseEventData>(_ => SelectButton(index)));
            trig.triggers.Add(hover);
            _buttons.Add((tile, selected, onClick));
        }

        Transform BaseRow(Transform parent, string reward)
        {
            var row = Clone(_rowPf, parent).transform;
            row.Find("LockedState").gameObject.SetActive(false);
            row.Find("Overlay").gameObject.SetActive(false);
            row.Find("CoinIcon").gameObject.SetActive(false);
            var state = row.Find("UnlockedState");
            state.Find("CompletedState").gameObject.SetActive(false);
            state.Find("NotCompleted").gameObject.SetActive(false);

            var always = row.Find("AlwaysActive");
            var rewardCol = always.Find("RewardViewModel");
            rewardCol.gameObject.SetActive(reward != null);
            always.Find("Background").GetComponent<RectTransform>().anchorMax = new Vector2(reward != null ? 0.87f : 1f, 1f);
            rewardCol.Find("Rewards/CoinIcon").gameObject.SetActive(false);
            var rt = rewardCol.Find("Rewards/ChallengesRewardText").GetComponent<TextMeshProUGUI>();
            rt.text = reward ?? "";
            Pop.Stretch(rt.transform, 4, 4, 4, 4);
            rt.enableAutoSizing = true;
            rt.fontSizeMin = 14;
            rt.fontSizeMax = 30;
            rt.alignment = TextAlignmentOptions.Center;
            return row;
        }

        Transform Row(Transform parent, string value, string label, string reward, string rewardSub)
        {
            var row = BaseRow(parent, reward == null ? null : rewardSub == null ? reward : reward + "\n<size=60%>" + rewardSub + "</size>");
            var always = row.Find("AlwaysActive");
            always.Find("ChallengesDescription").gameObject.SetActive(false);
            var title = always.Find("ChallengesTitle");
            Pop.Place(title, new Vector2(0.03f, 0f), new Vector2(0.6f, 1f), new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);
            var t = title.Find("ChallengesTitleText").GetComponent<TextMeshProUGUI>();
            t.text = label;
            t.color = Color.white;
            t.alignment = TextAlignmentOptions.MidlineLeft;
            t.enableWordWrapping = false;
            t.enableAutoSizing = true;
            t.fontSizeMin = 18;
            t.fontSizeMax = 30;

            var v = Object.Instantiate(t.gameObject, always, false).GetComponent<TextMeshProUGUI>();
            v.name = "BFG_Value";
            v.text = value;
            Pop.Place(v.transform, new Vector2(0.45f, 0f), new Vector2(reward != null ? 0.86f : 0.985f, 1f), new Vector2(1f, 0.5f), Vector2.zero, Vector2.zero);
            v.alignment = TextAlignmentOptions.MidlineRight;
            v.fontSizeMin = 20;
            v.fontSizeMax = 44;
            return row;
        }

        void AchievementRow(Transform parent, AccountStatsData.Achievement a)
        {
            var row = BaseRow(parent, a.Percent >= 0 ? a.Percent.ToString("0.0") + "%" : null);
            var always = row.Find("AlwaysActive");
            var title = always.Find("ChallengesTitle");
            Pop.Place(title, new Vector2(0.15f, 1f), new Vector2(0.62f, 1f), new Vector2(0f, 1f), new Vector2(0, -8), new Vector2(0, 38));
            var t = title.Find("ChallengesTitleText").GetComponent<TextMeshProUGUI>();
            t.text = a.Name;
            t.color = Color.white;
            t.alignment = TextAlignmentOptions.MidlineLeft;
            t.enableAutoSizing = true;
            t.fontSizeMin = 20;
            t.fontSizeMax = 30;
            var desc = always.Find("ChallengesDescription");
            Pop.Place(desc, new Vector2(0.15f, 1f), new Vector2(0.85f, 1f), new Vector2(0f, 1f), new Vector2(0, -46), new Vector2(0, 28));
            var d = desc.Find("ChallengesDescriptionText").GetComponent<TextMeshProUGUI>();
            d.text = a.Description ?? "";
            d.color = Soft;
            d.alignment = TextAlignmentOptions.MidlineLeft;
            d.enableAutoSizing = true;
            d.fontSizeMin = 14;
            d.fontSizeMax = 20;

            var state = row.Find("UnlockedState");
            bool done = a.CompletedAt.HasValue;
            state.Find(done ? "CompletedState" : "NotCompleted").gameObject.SetActive(true);
            state.Find("CompletedState/BackgroundCompleted").gameObject.SetActive(false);
            state.Find("NotCompleted/ChallengesProgressContainer").gameObject.SetActive(false);
            var date = state.Find("CompletedState/CompletedText").GetComponent<TextMeshProUGUI>();
            if (!done) date = Text(always, "", 20, TextAlignmentOptions.MidlineRight, Soft, _bodyFont, _bodyMat, 0, 0, 0, 0);
            date.text = done ? Date(a.CompletedAt.Value) : F("achievements.progress", N(a.Progress));
            Pop.Place(date.transform, new Vector2(0.6f, 1f), new Vector2(0.86f, 1f), new Vector2(1f, 1f), new Vector2(-10, -8), new Vector2(0, 38));
            date.fontSize = 20;
            date.enableAutoSizing = false;
            date.alignment = TextAlignmentOptions.MidlineRight;
            if (done) date.color = Green;

            if (a.Icon == null) return;
            foreach (var s in new[] { "CompletedState/ChallengesToggleChecked", "NotCompleted/ChallengesToggleUnchecked" }) state.Find(s).gameObject.SetActive(false);
            var coin = always.Find("RewardViewModel/Rewards/CoinIcon").GetComponent<RawImage>();
            coin.transform.SetParent(always, false);
            coin.gameObject.SetActive(true);
            coin.enabled = true;
            coin.texture = a.Icon;
            coin.transform.Find("Spinner").gameObject.SetActive(false);
            Pop.Place(coin.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(49, 0), new Vector2(64, 64));
            if (!done) coin.color = new Color(1f, 1f, 1f, 0.45f);
        }

        void Bar(string crest, float frac, string text, Color fill)
        {
            var holder = Rect(_content, "BarRow");
            holder.gameObject.AddComponent<LayoutElement>().preferredHeight = 120;
            var bar = Clone(_barPf, holder);
            Pop.Place(bar.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(760, 60));
            bar.transform.localScale = new Vector3(1.6f, 1.6f, 1f);
            var slider = bar.transform.Find("Bar").GetComponent<Slider>();
            slider.interactable = false;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.value = Mathf.Clamp01(frac);
            bar.transform.Find("Bar/Fill Area/Fill").GetComponent<Image>().color = fill;
            bar.transform.Find("Bar/Fill Area/Bar Filled Text").GetComponent<TextMeshProUGUI>().text = text;
            bar.transform.Find("Bar/Crest/Text_SeasonNumber").GetComponent<TextMeshProUGUI>().text = crest;
        }

        RectTransform Section(string title, float bodyHeight)
        {
            var sec = Rect(_content, "Section");
            sec.gameObject.AddComponent<LayoutElement>().preferredHeight = bodyHeight + 56;
            var header = Clone(_headerPf, sec);
            header.transform.Find("Pips_Container").gameObject.SetActive(false);
            header.transform.Find("Panel_Fill").GetComponent<Image>().color = Rim;
            Pop.Place(header.transform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), Vector2.zero, new Vector2(0, 56));
            var ht = header.transform.Find("Title_Text").GetComponent<TextMeshProUGUI>();
            ht.text = title;
            var plate = Rect(sec, "Plate");
            Pop.Stretch(plate, 0, 0, 0, 52);
            var img = plate.gameObject.AddComponent<Image>();
            img.sprite = _plate;
            img.type = Image.Type.Sliced;
            img.color = new Color32(0x00, 0x6F, 0xD7, 0xFF);
            var rim = Rect(plate, "Rim").gameObject.AddComponent<Image>();
            rim.sprite = _rim;
            rim.type = Image.Type.Sliced;
            rim.color = Rim;
            rim.raycastTarget = img.raycastTarget = false;
            var body = Rect(plate, "Body");
            Pop.Stretch(body, 26, 20, 26, 18);
            return body;
        }

        void MonthGraph(RectTransform body, string legend)
        {
            if (_d.FirstItem == DateTime.MaxValue) return;
            var start = new DateTime(_d.FirstItem.Year, _d.FirstItem.Month, 1);
            int months = (DateTime.Now.Year - start.Year) * 12 + DateTime.Now.Month - start.Month + 1;
            var counts = new int[months];
            int total = 0;
            foreach (var it in _d.Items)
            {
                if (it.EarnedAt.Year < 2000) continue;
                int m = (it.EarnedAt.Year - start.Year) * 12 + it.EarnedAt.Month - start.Month;
                if (m < 0 || m >= months) continue;
                counts[m]++;
                total++;
            }
            int peak = 1, peakAt = 0;
            for (int i = 0; i < months; i++) if (counts[i] > peak) { peak = counts[i]; peakAt = i; }
            int step = Mathf.Max(5, Mathf.CeilToInt(peak / 4f / 5f) * 5);
            int top = step * 4;

            Text(body, legend + "   " + F("graph.peak", start.AddMonths(peakAt).ToString("MMM yyyy"), counts[peakAt]), 20, TextAlignmentOptions.TopRight, Soft, _bodyFont, _bodyMat, 0, 0, 0, -4);
            var plot = Rect(body, "Plot");
            Pop.Stretch(plot, 56, 30, 64, 30);

            for (int gi = 0; gi <= 4; gi++)
            {
                float y = gi / 4f;
                var line = Rect(plot, "Grid");
                line.gameObject.AddComponent<Image>().color = GridLine;
                line.anchorMin = new Vector2(0, y); line.anchorMax = new Vector2(1, y);
                line.sizeDelta = new Vector2(0, 2); line.anchoredPosition = Vector2.zero;
                Axis(plot, N(step * gi), new Vector2(0, y), new Vector2(1, 0.5f), new Vector2(-10, 0), TextAlignmentOptions.Right, Soft);
                Axis(plot, N(Mathf.RoundToInt(total * y)), new Vector2(1, y), new Vector2(0, 0.5f), new Vector2(10, 0), TextAlignmentOptions.Left, Yellow);
            }

            int running = 0;
            for (int i = 0; i < months; i++)
            {
                float x0 = (float)i / months, x1 = (float)(i + 1) / months;
                if (counts[i] > 0)
                {
                    var bar = Rect(plot, "Bar");
                    bar.gameObject.AddComponent<Image>().color = i == peakAt ? Pink : Cyan;
                    bar.anchorMin = new Vector2(x0 + 0.12f / months, 0);
                    bar.anchorMax = new Vector2(x1 - 0.12f / months, Mathf.Min(1f, (float)counts[i] / top));
                    bar.offsetMin = bar.offsetMax = Vector2.zero;
                }
                running += counts[i];
                var dot = Rect(plot, "Total");
                dot.gameObject.AddComponent<Image>().color = Yellow;
                dot.anchorMin = dot.anchorMax = new Vector2((x0 + x1) / 2f, (float)running / Mathf.Max(1, total));
                dot.sizeDelta = new Vector2(8, 8);
                dot.anchoredPosition = Vector2.zero;

                var month = start.AddMonths(i);
                if (month.Month != 1 && i != 0) continue;
                var tick = Rect(plot, "Tick");
                tick.gameObject.AddComponent<Image>().color = GridLine;
                tick.anchorMin = new Vector2(x0, 0); tick.anchorMax = new Vector2(x0, 1);
                tick.sizeDelta = new Vector2(2, 0); tick.anchoredPosition = Vector2.zero;
                Axis(plot, month.Year.ToString(), new Vector2(x0, 0), new Vector2(0f, 1f), new Vector2(6, -4), TextAlignmentOptions.TopLeft, Color.white);
            }
        }

        void Axis(RectTransform parent, string text, Vector2 anchor, Vector2 pivot, Vector2 offset, TextAlignmentOptions align, Color color)
        {
            var t = Text(parent, text, 18, align, color, _bodyFont, _bodyMat, 0, 0, 0, 0);
            var rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.sizeDelta = new Vector2(90, 24);
            rt.anchoredPosition = offset;
        }

        void BarChart(RectTransform body, (string label, int value, Color color)[] data)
        {
            int max = 1;
            foreach (var d in data) max = Math.Max(max, d.value);
            var hl = body.gameObject.AddComponent<HorizontalLayoutGroup>();
            hl.spacing = 60;
            hl.childAlignment = TextAnchor.MiddleCenter;
            hl.childControlWidth = hl.childControlHeight = true;
            hl.childForceExpandWidth = false;
            hl.childForceExpandHeight = true;
            foreach (var d in data)
            {
                var col = Rect(body, "Col");
                col.gameObject.AddComponent<LayoutElement>().preferredWidth = 150;
                var track = Rect(col, "Track");
                Capsule(track.gameObject.AddComponent<Image>(), Track);
                Pop.Stretch(track, 34, 38, 34, 40);
                float h = (float)d.value / max;
                var bar = Rect(track, "Bar");
                Capsule(bar.gameObject.AddComponent<Image>(), d.color);
                bar.anchorMin = Vector2.zero; bar.anchorMax = new Vector2(1, h);
                bar.offsetMin = new Vector2(5, 5); bar.offsetMax = new Vector2(-5, -5);
                var v = Text(track, N(d.value), 30, TextAlignmentOptions.Bottom, Color.white, _titleFont, _titleMat, 0, 0, 0, 0);
                var vrt = v.rectTransform;
                vrt.anchorMin = new Vector2(0.5f, h); vrt.anchorMax = new Vector2(0.5f, h); vrt.pivot = new Vector2(0.5f, 0f);
                vrt.sizeDelta = new Vector2(160, 36); vrt.anchoredPosition = new Vector2(0, 6);
                var k = Text(col, d.label, 26, TextAlignmentOptions.Bottom, Color.white, _bodyFont, _bodyMat, -20, 0, -20, 0);
                k.enableAutoSizing = true;
                k.fontSizeMin = 12;
                k.fontSizeMax = 26;
            }
        }

        void Capsule(Image img, Color color)
        {
            img.sprite = _capsule;
            img.type = Image.Type.Sliced;
            img.color = color;
            img.raycastTarget = false;
        }

        void Heading(string text)
        {
            var row = Rect(_content, "Heading");
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 48;
            Text(row, text, 34, TextAlignmentOptions.BottomLeft, Color.white, _titleFont, _titleMat, 6, 0, 0, 0);
        }

        Transform Rows(int count) => Grid(2, 88, count);

        Transform Grid(int cols, float cellH, int count)
        {
            var row = Rect(_content, "Grid");
            var grid = row.gameObject.AddComponent<GridLayoutGroup>();
            float w = (ContentW - Gap * (cols - 1)) / cols;
            grid.cellSize = new Vector2(w, cellH);
            grid.spacing = new Vector2(Gap, Gap * 0.6f);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = cols;
            int rows = (count + cols - 1) / cols;
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = rows * cellH + Mathf.Max(0, rows - 1) * Gap * 0.6f;
            return row;
        }

        RectTransform Rect(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            Pop.Stretch(rt, 0, 0, 0, 0);
            return rt;
        }

        TextMeshProUGUI Text(Transform parent, string text, float size, TextAlignmentOptions align, Color color, TMP_FontAsset font, Material mat, float l, float b, float r, float t)
        {
            var rt = Rect(parent, "Text");
            Pop.Stretch(rt, l, b, r, t);
            var tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
            tmp.font = font;
            tmp.fontSharedMaterial = mat;
            tmp.fontSize = size;
            tmp.alignment = align;
            tmp.color = color;
            tmp.text = text;
            tmp.raycastTarget = false;
            tmp.enableWordWrapping = false;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            return tmp;
        }

        void SelectButton(int i)
        {
            if (_buttons.Count == 0) return;
            _sel = Mathf.Clamp(i, 0, _buttons.Count - 1);
            _es.SetSelectedGameObject(_buttons[_sel].go);
            for (int k = 0; k < _buttons.Count; k++) _buttons[k].selected.alpha = k == _sel ? 1f : 0f;
        }

        bool Repeat(int slot, bool held)
        {
            bool was = _held[slot];
            _held[slot] = held;
            if (!held) return false;
            if (!was) { _repeatAt[slot] = Time.unscaledTime + 0.4f; return true; }
            if (Time.unscaledTime < _repeatAt[slot]) return false;
            _repeatAt[slot] = Time.unscaledTime + 0.12f;
            return true;
        }

        static bool Kb(KeyCode k) => KeybindService.KeyHeld(k);

        bool Tick()
        {
            if (_root == null) { Dispose(); return false; }
            if (_closing)
            {
                if (Kb(KeyCode.Escape) || NavPromptCore.ElementHeldByName(Pop.BackNames)) return true;
                Dispose();
                return false;
            }

            bool pad = Rewired.ReInput.controllers.GetLastActiveControllerType() == Rewired.ControllerType.Joystick;
            foreach (var g in _kbNav) g.SetActive(!pad);
            foreach (var g in _padNav) g.SetActive(pad);

            float lx = NavPromptCore.AxisByName(Pop.StickXNames), ly = NavPromptCore.AxisByName(Pop.StickYNames);
            bool upHeld = Kb(KeyCode.UpArrow) || Kb(KeyCode.W) || NavPromptCore.ElementHeldByName(Pop.UpNames) || ly > 0.5f;
            bool downHeld = Kb(KeyCode.DownArrow) || Kb(KeyCode.S) || NavPromptCore.ElementHeldByName(Pop.DownNames) || ly < -0.5f;
            bool up = Repeat(0, upHeld), down = Repeat(1, downHeld);
            bool left = Repeat(2, Kb(KeyCode.LeftArrow) || Kb(KeyCode.A) || NavPromptCore.ElementHeldByName(Pop.LeftNames) || lx < -0.5f);
            bool right = Repeat(3, Kb(KeyCode.RightArrow) || Kb(KeyCode.D) || NavPromptCore.ElementHeldByName(Pop.RightNames) || lx > 0.5f);
            if (Time.frameCount == _openFrame) return true;

            if (KeybindService.KeyDown(KeyCode.Escape) || NavPromptCore.ElementDownByName(Pop.BackNames))
            {
                _closing = true;
                _root.SetActive(false);
                return true;
            }
            if (KeybindService.KeyDown(KeyCode.Q) || NavPromptCore.ElementDownByName(Pop.PrevNames)) Select(_cur - 1);
            if (KeybindService.KeyDown(KeyCode.E) || NavPromptCore.ElementDownByName(Pop.NextNames)) Select(_cur + 1);

            float scroll = NavPromptCore.AxisByName(Pop.ScrollNames);
            if (_buttons.Count > 0)
            {
                if (up && _sel - TileColumns >= 0) SelectButton(_sel - TileColumns);
                else if (down && _sel + TileColumns < _buttons.Count) SelectButton(_sel + TileColumns);
                else if (upHeld || downHeld) scroll += upHeld ? 1f : -1f;
                if (left && _sel % TileColumns > 0) SelectButton(_sel - 1);
                if (right && _sel % TileColumns < TileColumns - 1 && _sel + 1 < _buttons.Count) SelectButton(_sel + 1);
                if (KeybindService.KeyDown(KeyCode.Return) || KeybindService.KeyDown(KeyCode.Space) || NavPromptCore.ElementDownByName(Pop.SubmitNames))
                {
                    _buttons[_sel].act();
                    return true;
                }
            }
            else scroll += (upHeld ? 1f : 0f) - (downHeld ? 1f : 0f);

            if (Mathf.Abs(scroll) > 0.2f)
            {
                float span = Mathf.Max(1f, _content.rect.height - _scroll.viewport.rect.height);
                _scroll.verticalNormalizedPosition = Mathf.Clamp01(_scroll.verticalNormalizedPosition + Mathf.Clamp(scroll, -1f, 1f) * ScrollSpeed * Time.unscaledDeltaTime / span);
            }
            return true;
        }

        void Dispose()
        {
            if (_root != null) Object.Destroy(_root);
            _root = null;
            _d.Dispose();
            FGInputLockService.SetScreenLock(false);
            if (_prevSel != null) _es.SetSelectedGameObject(_prevSel);
        }
    }
}
