using System;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.Customization.UI.Styles.Season1
{
    internal static class S1Fame
    {
        public const int FAME = 15;
        const string ROOT = "UICanvas_Client_V2(Clone)/Default/MainMenuBuilder(Clone)/MainScreensParent/Menu_Screen_Seasons";
        const string BOX = "BFG_S1Box";
        const string STYLE = "SeasonRewardContainerStyle1/";

        static readonly System.Collections.Generic.HashSet<int> _scrolls = new System.Collections.Generic.HashSet<int>();

        static void Img(Transform t, Transform donor)
        {
            if (t == null) return;
            S1Kit.Image(t, donor);
            t.GetComponent<Image>().canvasRenderer.SetAlpha(1f);
        }

        public static void Arm()
        {
            var root = GameObject.Find(ROOT);
            if (root == null) return;
            var follower = S1Kit.Follow(root, FAME, 2);
            follower.Arm();
            foreach (var sr in root.GetComponentsInChildren<ScrollRect>(true))
                if (_scrolls.Add(sr.GetInstanceID()))
                    sr.onValueChanged.AddListener(DelegateSupport.ConvertDelegate<UnityEngine.Events.UnityAction<Vector2>>(new Action<Vector2>(_ => follower.Arm())));
        }

        public static void Apply(UIStyle style, Transform root)
        {
            var d = S1Kit.Donor(style, "SeasonRewardContainer");
            var un = d.Find(STYLE + "SeasonRewardUnlockedLayout");
            var locked = d.Find(STYLE + "SeasonRewardLockedLayout");
            foreach (var item in root.GetComponentsInChildren<Transform>(true))
            {
                if (item.name != "Generic_UI_SeasonPassItem_Prefab") continue;
                var fr = item.Find("Frame");
                var box = item.Find(BOX);
                if (box == null)
                {
                    var go = new GameObject(BOX);
                    StyleUndo.Own(go);
                    StyleUndo.Rect(fr);
                    StyleUndo.Active(fr.Find("Corner").gameObject);
                    go.AddComponent<RectTransform>();
                    go.AddComponent<Image>().raycastTarget = false;
                    go.transform.SetParent(item, false);
                    box = go.transform;
                    var rt = go.GetComponent<RectTransform>();
                    rt.anchorMin = Vector2.zero;
                    rt.anchorMax = Vector2.one;
                    rt.offsetMin = new Vector2(4, 4);
                    rt.offsetMax = new Vector2(-4, -4);
                    fr.SetSiblingIndex(1);
                    box.SetSiblingIndex(2);
                    foreach (var p in new[] { "BackgroundMask/Background_Fill", "BackgroundMask/Background_DotsPattern", "BackgroundMask/Background_Selected", "BackgroundMask/Background_Selected/Background_SelectedDotsPattern", "Frame/Frame_Shadow", "Frame/Frame_Outline", "Frame/Frame_Inline", "Frame/SelectedState/Selected_Inline", "Frame/SelectedState/Selected_Shadow" })
                        S1Kit.Off(item.Find(p));
                    fr.Find("Corner").gameObject.SetActive(false);
                    Img(fr.Find("SelectedState/Selected_Outline"), un.Find("Container/SeasonRewardHighlight"));
                    Img(item.Find("TopElementsContainer/TopRight_Container/LockIcon"), locked.Find("Root/SeasonRewardLockIcon"));
                    var bottom = item.parent.Find("Generic_UI_SeasonRewardBottomPanel_Prefab");
                    if (bottom != null)
                    {
                        Img(bottom.Find("RewardLevelIcon"), un.Find("Container/SeasonRewardLevel"));
                        S1Kit.Off(bottom.Find("ProgressBar"));
                        S1Kit.Off(bottom.Find("ProgressBar/WhiteOutline"));
                    }
                }
                Img(box, item.Find("TopElementsContainer/TopRight_Container/LockIcon").gameObject.activeSelf ? locked : un);
            }
        }
    }
}
