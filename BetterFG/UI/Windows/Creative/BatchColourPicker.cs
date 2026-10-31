using System;
using System.Collections.Generic;
using BettrFG.uGUI;
using BetterFG.Core;
using BetterFG.Tweaks;
using FG.Common;
using FG.Common.CMS;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace BetterFG.UI.Windows.Creative
{
    internal static class BatchColourPicker
    {
        private enum Section { Palette, Custom, Sliders }

        private static readonly Color Focus = new Color(1f, 0.8f, 0f, 1f);
        private static readonly Color Empty = new Color(0.247f, 0.247f, 0.247f, 1f);
        private static readonly string[] SliderNames = { "UI_HueSlider_Prefab", "UI_SaturationSlider_Prefab", "UI_ValueSlider_Prefab" };

        private static GameObject _panel;
        private static readonly List<GameObject> _hidden = new List<GameObject>();
        private static Transform _grid, _swatchT;
        private static int _columns;
        private static TextMeshProUGUI _paletteName;
        private static readonly List<GameObject> _swatches = new List<GameObject>();
        private static Image[] _custom;
        private static Color?[] _favs;
        private static readonly Slider[] _sliders = new Slider[3];
        private static readonly bool[] _inverted = new bool[3];
        private static readonly Image[] _sliderBorders = new Image[3];
        private static readonly Material[] _tints = new Material[3];
        private static Image _preview;
        private static readonly List<TextMeshProUGUI> _hex = new List<TextMeshProUGUI>();
        private static Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Color> _colours;
        private static Action<Color> _changed;
        private static Action _closed;
        private static Section _section;
        private static int _palette, _index, _repX, _repY;
        private static float _repAtX, _repAtY, _h, _s, _v;
        private static Color _current;
        private static bool _edited;

        internal static bool IsOpen => _panel != null;

        internal static void Open(Transform root, Transform pickerT, Transform swatchT, Color start, Action<Color> changed, Action closed)
        {
            if (_panel != null) return;
            _swatchT = swatchT;
            _changed = changed;
            _closed = closed;
            _edited = false;
            _repX = _repY = 0;

            _hidden.Clear();
            for (int i = 0; i < root.childCount; i++)
            {
                var child = root.GetChild(i).gameObject;
                if (!child.activeSelf) continue;
                _hidden.Add(child);
                child.SetActive(false);
            }

            var templateSliders = pickerT.Find("Content/UI_ColorPickerV2_Prefab/CustomColours/ColorPickSliders");
            for (int k = 0; k < 3; k++)
                _inverted[k] = templateSliders.Find(SliderNames[k]).GetComponent<LevelEditorColourPickerSliderItem>().IsInverted;

            _panel = BatchEditMenu.Clone(pickerT.gameObject, root, true);
            _panel.SetActive(true);
            BatchEditMenu.TopRight(_panel, -170f);

            var header = _panel.transform.Find("Title/Text");
            foreach (var c in header.GetComponents<Component>())
                if (c.GetIl2CppType().Name == "LocalisedStaticLabel") Object.DestroyImmediate(c);
            header.GetComponent<TextMeshProUGUI>().text = BatchEditMenu.L("batchedit.title").ToUpperInvariant();
            _panel.transform.Find("Title/Object Cost").gameObject.SetActive(false);

            var picker = _panel.transform.Find("Content/UI_ColorPickerV2_Prefab");
            var selector = picker.Find("ColourPalette/PaletteSelector");
            _paletteName = selector.Find("Title").GetComponent<TextMeshProUGUI>();
            BatchEditMenu.Trigger(selector.Find("Left_ControlGlyphButton").gameObject, EventTriggerType.PointerClick, _ => StepPalette(-1));
            BatchEditMenu.Trigger(selector.Find("Right_ControlGlyphButton").gameObject, EventTriggerType.PointerClick, _ => StepPalette(1));

            _grid = picker.Find("ColourPalette/Swatches");
            _columns = Mathf.Max(1, _grid.GetComponent<GridLayoutGroup>().constraintCount);
            for (int i = _grid.childCount - 1; i >= 0; i--) Object.DestroyImmediate(_grid.GetChild(i).gameObject);

            var row = picker.Find("CustomColours/Colors");
            var code = row.Find("CodeInputGroup");
            code.Find("Highlight").gameObject.SetActive(false);
            _hex.Clear();
            _hex.AddRange(code.GetComponentsInChildren<TextMeshProUGUI>(true));

            var slots = new List<Image>();
            for (int i = 0; i < row.childCount; i++)
                if (row.GetChild(i).name.StartsWith("Customizable_Colour")) slots.Add(row.GetChild(i).GetComponent<Image>());
            _custom = slots.ToArray();
            _favs = new Color?[_custom.Length];
            var hexes = LevelEditorColourPaletteSettings.GetFavouriteCustomColoursHexCodes();
            for (int k = 0; k < _custom.Length; k++)
            {
                if (hexes != null && k < hexes.Length && UGUIShip.HexToColor(hexes[k], out float r, out float g, out float b)) _favs[k] = new Color(r, g, b, 1f);
                int kk = k;
                BatchEditMenu.Trigger(_custom[k].gameObject, EventTriggerType.PointerClick, _ => Pick(Section.Custom, kk));
            }

            var sliders = picker.Find("CustomColours/ColorPickSliders");
            _preview = sliders.Find("Color").GetComponent<Image>();
            for (int k = 0; k < 3; k++)
            {
                var t = sliders.Find(SliderNames[k] + "/Slider");
                var s = _sliders[k] = t.GetComponent<Slider>();
                s.minValue = 0f;
                s.maxValue = 1f;
                s.interactable = true;
                s.onValueChanged.AddListener(new Action<float>(_ => { _edited = true; FromSliders(); }));
                _sliderBorders[k] = t.Find("Border").GetComponent<Image>();
                var values = t.Find("Values").GetComponent<Image>();
                values.material = _tints[k] = new Material(values.material);
                int kk = k;
                BatchEditMenu.Trigger(t.gameObject, EventTriggerType.PointerDown, _ => { _section = Section.Sliders; _index = kk; Paint(); });
            }

            _current = start;
            _section = Section.Palette;
            _palette = 0;
            _index = 0;
            if (!FindStart(start))
                for (int k = 0; k < _favs.Length; k++)
                    if (_favs[k].HasValue && Near(_favs[k].Value, start)) { _section = Section.Custom; _index = k; break; }

            ShowPalette();
            SyncSliders();
            Paint();
            Plugin.Log.LogInfo($"batch colour picker up on palette {_palette}, starting from #{ColorUtility.ToHtmlStringRGB(start)}");
        }

        internal static void Close(bool notify = true)
        {
            if (_panel == null) return;
            if (_edited) SaveCustom();
            Object.Destroy(_panel);
            _panel = null;
            for (int k = 0; k < 3; k++) { Object.Destroy(_tints[k]); _tints[k] = null; }
            _swatches.Clear();
            foreach (var go in _hidden) if (go != null) go.SetActive(true);
            _hidden.Clear();
            var closed = _closed;
            _closed = null;
            _changed = null;
            if (notify) closed?.Invoke();
        }

        internal static void Tick()
        {
            if (BfgBinds.BatchEditBack.Down || Input.GetKeyDown(KeyCode.Escape))
            {
                BatchEditMenu.Sound(AudioManager.EventMasterData?.CreativeModeItemParamsExit);
                Close();
                return;
            }
            if (BfgBinds.BatchEditSelect.Down || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                BatchEditMenu.Sound(AudioManager.EventMasterData?.CreativeModeItemEditConfirm);
                Close();
                return;
            }
            if (Input.GetKeyDown(KeyCode.Q) || NavPromptCore.ElementDownByName(ShowRoundsPopup.PrevNames)) StepPalette(-1);
            else if (Input.GetKeyDown(KeyCode.E) || NavPromptCore.ElementDownByName(ShowRoundsPopup.NextNames)) StepPalette(1);

            BatchEditMenu.ReadNav(out int dx, out int dy);
            float now = Time.unscaledTime;
            if (dy == 0) _repY = 0;
            else if (dy != _repY || now >= _repAtY)
            {
                _repAtY = now + (dy != _repY ? 0.32f : 0.09f);
                _repY = dy;
                Move(0, dy);
            }

            if (_section == Section.Sliders)
            {
                _repX = 0;
                float amount = BatchEditMenu.SlideAmount(dx);
                if (Mathf.Abs(amount) < 0.01f) return;
                var s = _sliders[_index];
                s.value = Mathf.Clamp01(s.value + amount * (_inverted[_index] ? -0.5f : 0.5f) * Time.unscaledDeltaTime);
                return;
            }

            if (dx == 0) { _repX = 0; return; }
            if (dx == _repX && now < _repAtX) return;
            _repAtX = now + (dx != _repX ? 0.32f : 0.09f);
            _repX = dx;
            Move(dx, 0);
        }

        private static bool FindStart(Color start)
        {
            int count = LevelEditorColourPaletteSettings.ColourPaletteData.Count;
            for (int p = 0; p < count; p++)
            {
                var colours = LevelEditorColourPaletteSettings.GetPaletteInfoFromIndex(p)?.Colours;
                if (colours == null) continue;
                for (int i = 0; i < colours.Length; i++)
                {
                    if (!Near(colours[i], start)) continue;
                    _palette = p;
                    _index = i;
                    return true;
                }
            }
            return false;
        }

        private static bool Near(Color a, Color b) => Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) < 0.01f;

        private static void StepPalette(int d)
        {
            int count = LevelEditorColourPaletteSettings.ColourPaletteData.Count;
            if (count < 2) return;
            _palette = (_palette + d + count) % count;
            ShowPalette();
            if (_section == Section.Palette) Pick(Section.Palette, Mathf.Min(_index, _colours.Length - 1));
            else Paint();
            BatchEditMenu.Sound(AudioManager.EventMasterData?.CreativeModeLibraryTab);
        }

        private static void ShowPalette()
        {
            var palette = LevelEditorColourPaletteSettings.GetPaletteInfoFromIndex(_palette);
            _colours = palette.Colours;
            string name = CMSLoader.Instance._localisedStrings.GetString(palette.PaletteLocalisationID);
            _paletteName.text = string.IsNullOrEmpty(name) ? palette.PaletteID : name;

            foreach (var go in _swatches) Object.DestroyImmediate(go);
            _swatches.Clear();
            for (int i = 0; i < _colours.Length; i++)
            {
                var go = BatchEditMenu.Clone(_swatchT.gameObject, _grid);
                go.SetActive(true);
                go.GetComponent<Image>().color = _colours[i];
                go.transform.Find("NormalBorder").gameObject.SetActive(true);
                int ii = i;
                BatchEditMenu.Trigger(go, EventTriggerType.PointerClick, _ => Pick(Section.Palette, ii));
                _swatches.Add(go);
            }
        }

        private static void Move(int dx, int dy)
        {
            int n = _colours.Length;
            var section = _section;
            int index = _index;
            switch (_section)
            {
                case Section.Palette:
                    if (dx != 0) index = Mathf.Clamp(index + dx, 0, n - 1);
                    else if (dy < 0 && index - _columns >= 0) index -= _columns;
                    else if (dy > 0 && index + _columns < n) index += _columns;
                    else if (dy > 0) { section = Section.Custom; index = Mathf.Min(index % _columns, _custom.Length - 1); }
                    break;
                case Section.Custom:
                    if (dx != 0) index = Mathf.Clamp(index + dx, 0, _custom.Length - 1);
                    else if (dy < 0) { section = Section.Palette; index = Mathf.Min((n - 1) / _columns * _columns + index, n - 1); }
                    else if (dy > 0) { section = Section.Sliders; index = 0; }
                    break;
                case Section.Sliders:
                    if (dy < 0 && index == 0) section = Section.Custom;
                    else index = Mathf.Clamp(index + dy, 0, 2);
                    break;
            }
            if (section == _section && index == _index) return;
            BatchEditMenu.Sound(AudioManager.EventMasterData?.CreativeModeLibraryTab);
            Pick(section, index);
        }

        private static void Pick(Section section, int index)
        {
            _section = section;
            _index = index;
            if (section == Section.Palette) SetColour(_colours[index]);
            else if (section == Section.Custom && _favs[index].HasValue) SetColour(_favs[index].Value);
            Paint();
        }

        private static void SetColour(Color c)
        {
            c.a = 1f;
            _current = c;
            SyncSliders();
            _changed?.Invoke(c);
        }

        private static void SyncSliders()
        {
            Color.RGBToHSV(_current, out _h, out _s, out _v);
            float[] hsv = { _h, _s, _v };
            for (int k = 0; k < 3; k++) _sliders[k].SetValueWithoutNotify(_inverted[k] ? 1f - hsv[k] : hsv[k]);
        }

        private static void FromSliders()
        {
            float[] hsv = new float[3];
            for (int k = 0; k < 3; k++) hsv[k] = _inverted[k] ? 1f - _sliders[k].value : _sliders[k].value;
            _h = hsv[0];
            _s = hsv[1];
            _v = hsv[2];
            _current = Color.HSVToRGB(_h, _s, _v);
            _changed?.Invoke(_current);
            Paint();
        }

        private static void Paint()
        {
            for (int i = 0; i < _swatches.Count; i++)
                _swatches[i].transform.Find("SelectedBorder").gameObject.SetActive(_section == Section.Palette && i == _index);
            for (int k = 0; k < _custom.Length; k++)
            {
                _custom[k].color = _favs[k] ?? Empty;
                _custom[k].transform.Find("SelectedBorder").gameObject.SetActive(_section == Section.Custom && k == _index);
            }
            var tint = Color.HSVToRGB(_h, 1f, 1f);
            for (int k = 0; k < 3; k++)
            {
                _sliderBorders[k].color = _section == Section.Sliders && k == _index ? Focus : Color.white;
                if (k > 0) _tints[k].SetColor("_Color", tint);
            }
            _preview.color = _current;
            string hex = ColorUtility.ToHtmlStringRGB(_current);
            foreach (var t in _hex) t.text = hex;
        }

        private static void SaveCustom()
        {
            string hex = "#" + ColorUtility.ToHtmlStringRGB(_current);
            var old = LevelEditorColourPaletteSettings.GetFavouriteCustomColoursHexCodes();
            var list = new List<string> { hex };
            for (int i = 0; old != null && i < old.Length && list.Count < _custom.Length; i++)
            {
                string h = old[i];
                if (!string.IsNullOrEmpty(h) && !string.Equals(h.TrimStart('#'), hex.TrimStart('#'), StringComparison.OrdinalIgnoreCase)) list.Add(h);
            }
            LevelEditorColourPaletteSettings.SetFavouriteCustomColours(list.ToArray());
            Plugin.Log.LogInfo($"{hex} into the custom colours");
        }
    }
}
