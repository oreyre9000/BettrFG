using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BetterFG.Customization.UI.Styles.Season1
{
    internal static class S1Modal
    {
        const string HOST = "BFG_S1Modal";
        const string BACK = "ModalMessageContainer/ModalMessageBacking/";
        static readonly string[] StockVisuals = { "Panel", "ButtonContainer", "Content" };

        sealed class Live
        {
            public Transform Stock, S1, Ok, Cancel, SrcOk, SrcCancel;
            public TMP_Text Title, Msg, SrcTitle, SrcMsg;
        }

        static readonly Dictionary<int, Live> _live = new Dictionary<int, Live>();
        static bool _hooked;

        public static bool Handles(Component modal) => modal.GetIl2CppType().Name == "ModalMessagePopupViewModel";

        public static void Show(UIStyle style, Transform stock)
        {
            if (!_live.TryGetValue(stock.gameObject.GetInstanceID(), out var l) || l.S1 == null) l = Build(style, stock);
            Sync(l);
        }

        static Live Build(UIStyle style, Transform stock)
        {
            if (!_hooked)
            {
                _hooked = true;
                TMPro_EventManager.TEXT_CHANGED_EVENT.Add(DelegateSupport.ConvertDelegate<Il2CppSystem.Action<UnityEngine.Object>>(new Action<UnityEngine.Object>(OnText)));
            }
            var old = stock.Find(HOST);
            if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
            var s1 = StyleLayout.Build(style.Assets, "Prefab_UI_Modal_Message", stock).transform;
            s1.name = HOST;
            S1Kit.Stretch(s1);
            var bg = s1.GetComponent<Image>();
            if (bg != null) bg.enabled = false;
            S1Kit.Show(s1);
            foreach (var p in StockVisuals) StyledUI.Hide(stock.Find(p), true);
            var l = new Live
            {
                Stock = stock,
                S1 = s1,
                Title = s1.Find(BACK + "ModalMessageTitle").GetComponent<TMP_Text>(),
                Msg = s1.Find(BACK + "ModalMessageText").GetComponent<TMP_Text>(),
                Ok = s1.Find(BACK + "ModalMessageButtonsContainer/ModalMessageSubmitButton"),
                Cancel = s1.Find(BACK + "ModalMessageButtonsContainer/ModalMessageCancelButton"),
                SrcTitle = stock.Find("Panel/Title/Title_Text").GetComponent<TMP_Text>(),
                SrcMsg = stock.Find("Content/Content_Text").GetComponent<TMP_Text>(),
                SrcOk = stock.Find("ButtonContainer/RightButton"),
                SrcCancel = stock.Find("ButtonContainer/LeftButton"),
            };
            l.Msg.richText = true;
            Wire(l.Ok, l.SrcOk);
            Wire(l.Cancel, l.SrcCancel);
            _live[stock.gameObject.GetInstanceID()] = l;
            return l;
        }

        static void Wire(Transform s1, Transform stock)
        {
            if (s1 == null || stock == null) return;
            var target = stock.gameObject;
            s1.GetComponent<Button>().onClick.AddListener(DelegateSupport.ConvertDelegate<UnityEngine.Events.UnityAction>(new Action(() =>
                ExecuteEvents.Execute(target, new PointerEventData(EventSystem.current), ExecuteEvents.pointerClickHandler))));
            StyleGlyphs.Bind(s1.Find("ControlGlyphButton")?.GetComponent<Image>(), StyleGlyphs.Live(stock));
        }

        static void OnText(UnityEngine.Object o)
        {
            if (o == null) return;
            foreach (var l in _live.Values)
                if (l.S1 != null && (o.Pointer == l.SrcTitle.Pointer || o.Pointer == l.SrcMsg.Pointer)) { Sync(l); return; }
        }

        static void Sync(Live l)
        {
            l.Title.text = l.SrcTitle.text;
            l.Msg.text = l.SrcMsg.text;
            Button(l.Ok, l.SrcOk, "ButtonSubmitText");
            Button(l.Cancel, l.SrcCancel, "ButtonCancelText");
        }

        static void Button(Transform s1, Transform stock, string label)
        {
            bool on = stock != null && stock.gameObject.activeSelf;
            s1.gameObject.SetActive(on);
            var src = stock?.Find("Content/Text")?.GetComponent<TMP_Text>();
            if (on && src != null) s1.Find(label).GetComponent<TMP_Text>().text = src.text;
        }

        public static void Teardown()
        {
            foreach (var l in _live.Values)
            {
                if (l.S1 != null) UnityEngine.Object.Destroy(l.S1.gameObject);
                if (l.Stock != null) foreach (var p in StockVisuals) StyledUI.Hide(l.Stock.Find(p), false);
            }
            _live.Clear();
        }
    }
}
