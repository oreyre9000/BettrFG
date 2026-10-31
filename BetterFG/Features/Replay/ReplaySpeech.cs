using System;
using System.Collections.Generic;
using BetterFG.Features.TextChat;
using BetterFG.Utilities;
using Character;
using FGClient;
using MPG.Utility;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.Features.Replay
{
    public class ReplaySpeechOption
    {
        public string itemId = "";
        public int speechId;
        public float duration = 3f;
        public string text = "";
        public bool hasText;
        public bool shiny;
        public byte[] image;
    }

    public struct ReplaySpeechEvent
    {
        public float t;
        public float end;
        public uint playerId;
        public int option;
    }

    internal class ReplaySpeechPlayer
    {
        const float POP_IN = 0.18f;
        const float FADE_OUT = 0.22f;

        class Bubble
        {
            public GameObject go;
            public SpeechBubbleViewModel vm;
            public CanvasGroup group;
            public Vector3 offset;
            public Vector3 baseScale = Vector3.one;
            public bool text, chat;
            public int shown = -1;
            public int settleUntil;
        }

        class Speaker
        {
            public Transform bean;
            public readonly List<ReplaySpeechEvent> events = new List<ReplaySpeechEvent>();
            public readonly Bubble bubble = new Bubble();
        }

        readonly ReplayRecording _rec;
        readonly Transform _root;
        readonly Dictionary<uint, Speaker> _speakers = new Dictionary<uint, Speaker>();
        readonly Sprite[] _sprites;
        readonly AnimatedImage.Result[] _anims;
        readonly int[] _animFrame;
        readonly List<UnityEngine.Object> _owned = new List<UnityEngine.Object>();
        SpeechBubbleViewModel _imagePrefab;
        SpeechBubbleViewModel _textPrefab;
        Camera _cam;

        public bool PhrasesVisible = true;
        public Func<uint, bool> PlayerVisible;

        public ReplaySpeechPlayer(ReplayRecording rec, Transform root)
        {
            _rec = rec;
            _root = root;
            _sprites = new Sprite[rec.speechOptions.Count];
            _anims = new AnimatedImage.Result[_sprites.Length];
            _animFrame = new int[_sprites.Length];
        }

        public void Prepare(Dictionary<uint, Transform> beans)
        {
            if (_rec.speechEvents.Count == 0) return;

            foreach (var vm in Resources.FindObjectsOfTypeAll<SpeechBubbleViewModel>())
            {
                if (vm == null || vm.name.EndsWith("(Clone)")) continue;
                if (vm.TryCast<SpeechBubbleActionViewModel>() != null) continue;

                if (vm.name.IndexOf("AndText", StringComparison.OrdinalIgnoreCase) >= 0) _textPrefab = _textPrefab ?? vm;
                else _imagePrefab = _imagePrefab ?? vm;
            }

            var lookup = SingletonBehaviour<SpeechOptionsManager>.Instance?._speechOptionsLookup;
            var usable = new bool[_sprites.Length];
            int packed = 0;
            int live = 0;
            int chats = 0;

            for (int i = 0; i < _sprites.Length; i++)
            {
                var saved = _rec.speechOptions[i];
                if (FeatureTextChat.IsChat(saved.speechId)) { chats++; usable[i] = true; continue; }
                if (Unpack(saved, i)) { packed++; usable[i] = true; continue; }

                var option = Find(lookup, saved);
                var image = option?.TryCast<ImageSpeechOption>();
                if (image == null)
                {
                    Plugin.Log.LogWarning($"nothing left to draw '{saved.itemId}' with — no art packed with the replay and it's gone from the wheel, skipping those bubbles");
                    continue;
                }

                if (string.IsNullOrEmpty(saved.text))
                {
                    saved.text = option.DisplayName ?? "";
                    saved.hasText = option.TryCast<TextAndImageSpeechOption>() != null;
                    saved.shiny = option.IsShiny;
                }

                live++;
                usable[i] = true;
                _sprites[i] = image.CurrentSprite;
                int slot = i;
                image.LoadSprite(new Action<Sprite>(sprite => { if (sprite != null) _sprites[slot] = sprite; }));
            }

            int dropped = 0;
            foreach (var speech in _rec.speechEvents)
            {
                if (!usable[speech.option]) { dropped++; continue; }
                if (!beans.TryGetValue(speech.playerId, out var bean) || bean == null) continue;

                if (!_speakers.TryGetValue(speech.playerId, out var speaker))
                {
                    speaker = new Speaker { bean = bean };
                    _speakers[speech.playerId] = speaker;
                }
                speaker.events.Add(speech);
            }

            Plugin.Log.LogInfo($"replay speech: {_rec.speechEvents.Count} bubbles from {_speakers.Count} players, art for {packed} packed in and {live} off the live wheel out of {_sprites.Length}, {chats} chat lines"
                + (dropped > 0 ? $", {dropped} dropped with nothing to draw them" : "")
                + (_imagePrefab == null ? " — no bubble prefab anywhere, none of them will show" : ""));
        }

        bool Unpack(ReplaySpeechOption saved, int slot)
        {
            if (saved.image == null || saved.image.Length == 0) return false;

            var decoded = AnimatedImage.Decode(saved.image);
            if (decoded == null)
            {
                Plugin.Log.LogWarning($"the packed art for '{saved.text}' wouldn't decode, falling back to whatever the wheel has");
                return false;
            }

            var tex = UnityEngine.Object.Instantiate(decoded.Frames[0]);
            tex.hideFlags = HideFlags.HideAndDontSave;
            foreach (var f in decoded.Frames) { f.hideFlags = HideFlags.HideAndDontSave; _owned.Add(f); }
            if (decoded.Frames.Length > 1) _anims[slot] = decoded;

            var sprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            sprite.hideFlags = HideFlags.HideAndDontSave;
            _sprites[slot] = sprite;
            _owned.Add(tex);
            _owned.Add(sprite);
            return true;
        }

        static SocialOption Find(Il2CppSystem.Collections.Generic.Dictionary<int, SocialOption> lookup, ReplaySpeechOption saved)
        {
            if (lookup == null) return null;

            foreach (var kvp in lookup)
            {
                var option = kvp.Value;
                if (option != null && FeatureReplay.ItemId(option) == saved.itemId) return option;
            }

            return lookup.ContainsKey(saved.speechId) ? lookup[saved.speechId] : null;
        }

        public void Apply(float time, Camera cam)
        {
            _cam = cam;

            foreach (var kvp in _speakers)
            {
                var speaker = kvp.Value;
                int at = Collect(speaker, time);
                if (!PhrasesVisible || speaker.bean == null || (PlayerVisible != null && !PlayerVisible(kvp.Key))) at = -1;

                if (at != speaker.bubble.shown) Show(speaker, speaker.bubble, at);
                if (speaker.bubble.shown >= 0) Place(speaker, speaker.bubble, speaker.events[at], time);
            }
        }

        static int Collect(Speaker speaker, float time)
        {
            var events = speaker.events;
            for (int i = events.Count - 1; i >= 0; i--)
                if (events[i].t <= time) return time < events[i].end ? i : -1;
            return -1;
        }

        void Place(Speaker speaker, Bubble b, ReplaySpeechEvent speech, float time)
        {
            if (Time.frameCount <= b.settleUntil) Relayout(b);
            float pop = ReplayEase.Apply(ReplayEasingCurve.Back, ReplayEasingDirection.Out, (time - speech.t) / POP_IN);
            b.go.transform.localScale = b.baseScale * pop;
            b.go.transform.SetPositionAndRotation(speaker.bean.position + b.offset, _cam.transform.rotation);
            b.group.alpha = Mathf.Clamp01((speech.end - time) / FADE_OUT);

            var anim = _anims[speech.option];
            if (anim == null) return;
            float total = 0f;
            foreach (var d in anim.Delays) total += d;
            float at = Mathf.Repeat(time - speech.t, total);
            int frame = 0;
            while (frame < anim.Delays.Length - 1 && at >= anim.Delays[frame]) at -= anim.Delays[frame++];
            if (_animFrame[speech.option] == frame) return;
            _animFrame[speech.option] = frame;
            Graphics.CopyTexture(anim.Frames[frame], _sprites[speech.option].texture);
        }

        void Show(Speaker speaker, Bubble b, int at)
        {
            b.shown = at;

            if (at < 0)
            {
                if (b.go != null) b.go.SetActive(false);
                return;
            }

            int slot = speaker.events[at].option;
            var saved = _rec.speechOptions[slot];
            bool chat = FeatureTextChat.IsChat(saved.speechId);
            if (b.go == null || b.text != saved.hasText || b.chat != chat) Build(speaker, b, saved.hasText);
            if (b.go == null) { b.shown = -1; return; }
            b.chat = chat;

            b.go.SetActive(true);
            b.vm.SpeechImageSprite = _sprites[slot];
            b.vm.SpeechText = saved.hasText ? saved.text : "";
            b.vm.IsShiny = saved.shiny;
            b.vm.Alpha = 1f;
            b.vm.RaiseAllPropertiesChanged();
            if (chat) FeatureTextChat.Style(b.vm, saved.speechId);

            b.settleUntil = Time.frameCount + 10;
            Relayout(b);
        }

        static void Relayout(Bubble b)
        {
            foreach (var label in b.go.GetComponentsInChildren<TMP_Text>(true)) label.ForceMeshUpdate();

            var rects = b.go.GetComponentsInChildren<RectTransform>(true);
            for (int i = rects.Length - 1; i >= 0; i--) LayoutRebuilder.ForceRebuildLayoutImmediate(rects[i]);
        }

        void Build(Speaker speaker, Bubble b, bool wantsText)
        {
            if (b.go != null) UnityEngine.Object.Destroy(b.go);

            var prefab = wantsText && _textPrefab != null ? _textPrefab : _imagePrefab;
            if (prefab == null) { b.go = null; return; }

            var clone = UnityEngine.Object.Instantiate(prefab.gameObject, _root);
            clone.name = "BettrFG_ReplayBubble";
            clone.SetActive(true);

            int layer = speaker.bean.gameObject.layer;
            foreach (var t in clone.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
            foreach (var canvas in clone.GetComponentsInChildren<Canvas>(true)) canvas.worldCamera = _cam;

            b.go = clone;
            b.text = wantsText;
            b.baseScale = clone.transform.localScale;
            b.vm = clone.GetComponent<SpeechBubbleViewModel>();
            b.offset = b.vm._billboardPositionOffset;
            b.vm.enabled = false;

            b.group = clone.GetComponent<CanvasGroup>();
            if (b.group == null) b.group = clone.AddComponent<CanvasGroup>();
        }

        public void Release()
        {
            foreach (var kvp in _speakers)
                if (kvp.Value.bubble.go != null) UnityEngine.Object.Destroy(kvp.Value.bubble.go);
            _speakers.Clear();

            foreach (var asset in _owned)
                if (asset != null) UnityEngine.Object.Destroy(asset);
            _owned.Clear();
        }
    }
}
