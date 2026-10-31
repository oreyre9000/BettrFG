using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Customization.Pets;
using BetterFG.Customization.Player;
using BetterFG.Services;
using UnityEngine;
using UnityEngine.UI;
using LayoutElement = UnityEngine.UI.LayoutElement;
using BettrFG.uGUI;

namespace BetterFG.UI.Tabs
{
    public class PetsTab : Tab
    {
        public PetsTab(IntPtr ptr) : base(ptr) { }

        public override string TabTitle => "Pets";
        protected override string TitleId => "ui.pets";
        protected override string BgResource => "BetterFG.assets.ui.tab.petstab.png";

        static readonly Color BTN_DARK = UGUIShip.BTN_DARK;
        static readonly Color BTN_EQUIP = new Color(0.25f, 0.45f, 0.25f, 1f);
        static readonly Color BTN_REMOVE = UGUIShip.BTN_REMOVE;
        static readonly Color BTN_ADD = new Color(0.3f, 0.3f, 0.15f, 1f);
        static readonly Color HINT = new Color(1f, 1f, 1f, 0.35f);
        static readonly Color DIM = new Color(1f, 1f, 1f, 0.4f);
        static readonly Color WHITE = UGUIShip.WHITE;

        static float ROW_H => 30f * UIScale.S;

        RectTransform _content;
        string _selectedId;
        bool _thumbing;

        struct RowRefs
        {
            public Button Row;
            public Button Equip;
            public Text EquipLbl;
            public Text Name;
            public RawImage Thumb;
            public string Id;
        }
        readonly List<RowRefs> _rows = new List<RowRefs>();

        protected override void BuildContent(RectTransform contentRoot)
        {
            float w = TabWidth - PAD * 2f;
            float y = VPAD;

            float listH = TabHeight - y - VPAD;
            var scroll = UGUIShip.CreateScrollView(contentRoot, new Rect(PAD, y, w, listH));
            _content = scroll.content;
            var vlg = _content.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(2, 2, 2, 2);
            vlg.spacing = 2f;
            vlg.childControlHeight = false;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true;
            vlg.childForceExpandWidth = true;
            _content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            RefreshList();
        }

        public override void OnOpened() => RefreshList();

        void OnDisable() => _thumbing = false;

        IEnumerator FillThumbs()
        {
            _thumbing = true;
            foreach (var pet in new List<PetData>(PetService.Instance.Pets))
            {
                if (PetThumb.Load(pet.id) != null) continue;
                yield return PetThumb.CaptureRoutine(pet);
                var tex = PetThumb.Load(pet.id);
                foreach (var row in _rows)
                    if (row.Id == pet.id && row.Thumb != null && tex != null) { row.Thumb.texture = tex; row.Thumb.color = WHITE; }
            }
            _thumbing = false;
        }

        void RefreshList()
        {
            if (_content == null) return;

            for (int i = _content.childCount - 1; i >= 0; i--)
                GameObject.Destroy(_content.GetChild(i).gameObject);
            _rows.Clear();

            var pets = PetService.Instance?.Pets ?? new List<PetData>();
            float rowW = TabWidth - PAD * 2f - 8f;

            for (int i = 0; i < pets.Count; i++)
            {
                int idx = i;
                var pet = pets[i];
                string petId = pet.id;

                var rowGo = new GameObject("PetRow_" + i);
                rowGo.transform.SetParent(_content, false);
                rowGo.AddComponent<RectTransform>().sizeDelta = new Vector2(0f, ROW_H);
                var le = rowGo.AddComponent<LayoutElement>();
                le.preferredHeight = ROW_H;
                le.flexibleWidth = 1f;
                rowGo.AddComponent<Image>().color = WHITE;

                var rowBtn = rowGo.AddComponent<Button>();
                var nav = rowBtn.navigation;
                nav.mode = UnityEngine.UI.Navigation.Mode.None;
                rowBtn.navigation = nav;
                rowBtn.onClick.AddListener(new Action(() => { AudioService.PlayButtonClick(); SelectRow(petId); }));

                float thumbSz = (ROW_H - 6f) * 1.4f;
                var thumbGo = new GameObject("Thumb");
                thumbGo.transform.SetParent(rowBtn.transform, false);
                var thumbRt = thumbGo.AddComponent<RectTransform>();
                UGUIShip.SetPixelRect(thumbRt, new Rect(3f + thumbSz * 0.2f, 3f - thumbSz * 0.2f, thumbSz, thumbSz));
                var raw = thumbGo.AddComponent<RawImage>();
                raw.raycastTarget = false;
                var thumb = PetThumb.Load(petId);
                if (thumb != null) raw.texture = thumb;
                else raw.color = Color.clear;

                float editW = 30f * UIScale.S, equipW = 30f * UIScale.S, removeW = 22f * UIScale.S;
                float nameX = 3f + thumbSz * 1.2f + 6f;
                float nameW = rowW - editW - equipW - removeW - nameX - 10f;

                var nameLbl = UGUIShip.CreateLabel(rowBtn.transform,
                    new Rect(nameX, 0f, nameW, ROW_H), pet.name, FS_SM, WHITE, TextAnchor.MiddleLeft);

                UGUIShip.CreateRowEndButton(rowBtn.transform, -(removeW + equipW + editW + 4f), editW, ROW_H,
                    "ui.edit_2", BTN_DARK, () => OpenCreator(idx));

                var equipBtn = UGUIShip.CreateRowEndButton(rowBtn.transform, -(removeW + equipW + 2f), equipW, ROW_H,
                    "ui.on_2", BTN_EQUIP, () => { PetService.Instance?.ToggleEquipped(petId); RefreshList(); });

                UGUIShip.CreateRowEndButton(rowBtn.transform, -2f, removeW, ROW_H, "x", BTN_REMOVE,
                    () => { PetService.Instance?.RemovePet(petId); if (_selectedId == petId) _selectedId = null; RefreshList(); });

                _rows.Add(new RowRefs
                {
                    Row = rowBtn,
                    Equip = equipBtn,
                    EquipLbl = equipBtn.GetComponentInChildren<Text>(),
                    Name = nameLbl,
                    Thumb = raw,
                    Id = petId,
                });
            }

            PaintRows();
            if (!_thumbing && isActiveAndEnabled) StartCoroutine(FillThumbs().WrapToIl2Cpp());

            var addBtn = UGUIShip.CreateButton(_content, new Rect(0f, 0f, rowW, ROW_H),
                "ui.create_pet", BTN_ADD, WHITE, FS, new Action(() => OpenCreator(-1)));
            var addLe = addBtn.gameObject.AddComponent<LayoutElement>();
            addLe.preferredHeight = ROW_H;
            addLe.flexibleWidth = 1f;

            if (pets.Count == 0)
                UGUIShip.CreateLabel(_content, new Rect(6f, 0f, TabWidth, ROW_H),
                    "ui.no_pets_yet_create_one_below", FS_SM, HINT);
        }

        void PaintRows()
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                bool equipped = PetService.Instance?.IsEquipped(row.Id) ?? false;

                UGUIShip.PaintListRow(row.Row, i, row.Id == _selectedId);
                UGUIShip.SetButtonColor(row.Equip, equipped ? BTN_EQUIP : BTN_DARK);
                if (row.EquipLbl != null) UGUIShip.RelabelText(row.EquipLbl, equipped ? "ui.on_2" : "ui.off_2");
                if (row.Name != null) row.Name.color = equipped ? WHITE : DIM;
            }
        }

        void SelectRow(string id)
        {
            _selectedId = _selectedId == id ? null : id;
            PaintRows();
        }

        void OpenCreator(int editIdx)
        {
            var pets = PetService.Instance?.Pets;
            var pet = pets != null && editIdx >= 0 && editIdx < pets.Count ? pets[editIdx] : new PetData();
            BetterFGUIMan.Instance?.SetVisible(false);
            BeanCreatorScreen.Open(BeanLook.From(pet), new Action<BeanLook>(look => OnCreatorClosed(pet, look)), withOwner: true);
        }

        void OnCreatorClosed(PetData pet, BeanLook look)
        {
            BetterFGUIMan.Instance?.SetVisible(true);
            if (look == null) return;
            if (string.IsNullOrWhiteSpace(look.name)) look.name = pet.name;
            look.Into(pet);
            PetService.Instance?.SavePet(pet);
            Plugin.Log.LogInfo($"pet saved: {pet.name} ({pet.costumeTop}/{pet.costumeBottom}/{pet.pattern}, scale {pet.scale}, {pet.skins.Count} ugc)");
            _selectedId = pet.id;
            RefreshList();
        }
    }
}
