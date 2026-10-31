using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BetterFG.Customization.Social;
using BetterFG.Services;
using BetterFG.Utilities;
using Character;
using FG.Common.Character;
using FGClient;
using FGClient.FallFeed;
using MPG.Utility;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using PlayerUtils = FallGuysLib.Players.PlayerUtils;

namespace BetterFG.Features.TextChat
{
    internal static partial class FeatureTextChat
    {
        private const string EnabledKey = "text_chat.enabled";
        private const string ModeKey = "text_chat.mode";
        private const string LobbyRequestsKey = "text_chat.lobby_requests";

        public static bool Enabled => SettingsService.Get(EnabledKey, "false") == "true";
        public static bool GameMode => SettingsService.Get(ModeKey, "party") == "game";
        public static bool LobbyRequests => SettingsService.Get(LobbyRequestsKey, "true") == "true";

        public static void SetLobbyRequests(bool on)
        {
            SettingsService.Set(LobbyRequestsKey, on ? "true" : "false");
            Plugin.Log.LogInfo($"lobby level requests {(on ? "on" : "off")}");
        }

        public static void SetEnabled(bool on)
        {
            SettingsService.Set(EnabledKey, on ? "true" : "false");
            if (!on) Cancel();
            else
            {
                _retryAt = 0f;
                _backoff = 15f;
            }
            Plugin.Log.LogInfo($"text chat {(on ? "on, opted in" : "off")}");
        }

        public static void SetGameMode(bool game)
        {
            SettingsService.Set(ModeKey, game ? "game" : "party");
            Plugin.Log.LogInfo($"chat mode -> {(game ? "game" : "party")}");
        }

        private const int TypingId = 79990;
        private const int SentGameId = 79991;
        private const int RemoteGameId = 79992;
        private const int SentPartyId = 79993;
        private const int RemotePartyId = 79994;
        private const int LimitedId = 79995;
        private const int RemoteTypingId = 79996;
        private const string Dots = "​•••";
        private const string TypingOn = "{\"t\":\"typing\",\"on\":true}";
        private const string TypingOff = "{\"t\":\"typing\",\"on\":false}";
        private const string RelayUrl = "wss://chat.bettrfg.com/chat";
        private const int MaxLength = 20;
        private const float MinGap = 1f;
        private const string PartyPrefix = "/p";
        private const string Banned = "\\​‌‎‏‪‫‬‭‮⁠⁡⁢⁣⁤⁦⁧⁨⁩﻿";
        private const float SentSeconds = 6f;
        private const float FeedAlpha = 0.9f;
        private const float Height = 56f;
        private const string TipsPath = "UICanvas_Client_V2(Clone)/Default/InGameUiManager(Clone)/GameStates/PlayingState/GameplayInstructionsViewModel/TopLeftContainer/TipsContainer";
        private static GameObject _tip;
        private const string ContainerPath ="UICanvas_Client_V2(Clone)/Default/InGameUiManager(Clone)/GameStates/PlayingState/Generic_UI_Speech_Bubble_Prefab_Container";

        private static readonly Color Clear = new Color(0f, 0f, 0f, 0f);
        private static readonly Color LightBlue = new Color(0.82f, 0.92f, 1f, 1f);
        private static readonly Color PaleYellow = new Color(1f, 0.96f, 0.74f, 1f);
        private static readonly Color LimitRed = new Color(0.86f, 0.12f, 0.12f, 1f);
        private const string TypingMarker = "​|";

        private static bool _typing;
        private static bool _skipFrame;
        private static string _text = "";
        private static SpeechBubbleViewModel _bubble;
        private static readonly HashSet<System.IntPtr> _styled = new();
        private static Look _stock;

        private class Channel
        {
            public string name;
            public string room;
            public bool joined;
            public float retryAt;
            public float backoff = 15f;
        }

        private static string _key;
        private static bool _keyRead;
        private static readonly Channel _party = new() { name = "party" };
        private static readonly Channel _game = new() { name = "game" };
        private static readonly Channel _lobby = new() { name = "lobby" };
        private static ClientWebSocket _ws;
        private static Task _sending = Task.CompletedTask;
        private static bool _dropped;
        private static float _retryAt;
        private static float _backoff = 15f;
        private static float _nextCheck;
        private static float _lastSend = -10f;
        private static float _limitedUntil;
        private static string _account;
        private static readonly Dictionary<string, GameObject> _beans = new();
        private static readonly ConcurrentQueue<string> _inbox = new();
        private static readonly List<(string text, float at, Color tint)> _recent = new();
        private static readonly Dictionary<IntPtr, Color> _tintedRows = new();
        private static readonly List<(SpeechBubbleViewModel vm, TextMeshProUGUI text)> _dots = new();
        private static Channel _typingCh;

        static partial void DevServerKey(ref string key);

        public static bool Typing => _typing;

        private class Look
        {
            public RectOffset padding;
            public Vector2 shadowPos, shadowSize, rootSize;
            public float pillY;
            public int shadowIndex;
            public Vector4 margin;
            public Color text, shadow, pill, arrow, arrowShadow;
            public bool richText, parseCtrl;
        }

        private struct Parts
        {
            public RectTransform root, shadow, pill;
            public HorizontalLayoutGroup layout;
            public TextMeshProUGUI text;
            public Image shadowImg, pillImg, arrow, arrowShadow;
            public GameObject circle, circleShadow;
        }

        public static void Tick()
        {
            PumpRelay();
            LobbySuggestions.Tick();
            for (int i = _dots.Count - 1; i >= 0; i--)
            {
                var (vm, label) = _dots[i];
                if (vm == null || vm.CurrentSpeechId != RemoteTypingId || !vm.isActiveAndEnabled) { _dots.RemoveAt(i); continue; }
                label.ForceMeshUpdate();
                var info = label.textInfo;
                float hop = label.fontSize * 0.25f;
                int n = 0;
                for (int c = 0; c < info.characterCount; c++)
                {
                    var ci = info.characterInfo[c];
                    if (!ci.isVisible) continue;
                    float y = Mathf.Max(0f, Mathf.Sin(Time.unscaledTime * 8f - n++ * 0.9f)) * hop;
                    var verts = info.meshInfo[ci.materialReferenceIndex].vertices;
                    for (int v = ci.vertexIndex; v < ci.vertexIndex + 4; v++)
                    {
                        var p = verts[v];
                        p.y += y;
                        verts[v] = p;
                    }
                }
                label.UpdateVertexData(TMP_VertexDataUpdateFlags.Vertices);
            }
            if (_tip != null)
            {
                bool show = Enabled
                    && Rewired.ReInput.controllers.GetLastActiveController()?.type != Rewired.ControllerType.Joystick
                    && SettingsService.Get("tweak.disable_instruction_controls", "false") != "true";
                if (_tip.activeSelf != show) _tip.SetActive(show);
            }
            if (!_typing)
            {
                if (Enabled && !FGInputLockService.IsLocked && PlayerUtils.PlayerController != null && KeybindService.KeyDown(KeyCode.T))
                    Begin();
                return;
            }

            if (PlayerUtils.PlayerController == null || KeybindService.KeyDown(KeyCode.Escape)) { Cancel(); return; }
            if (_skipFrame) { _skipFrame = false; return; }

            string before = _text;
            string composing = Input.compositionString;
            foreach (char c in Input.inputString)
            {
                if (c == '\n' || c == '\r') { Send(); return; }
                if (c == '\b')
                {
                    if (_text.Length > 0) _text = _text.Substring(0, StringInfo.ParseCombiningCharacters(_text)[^1]);
                }
                else if (c >= ' ' && Banned.IndexOf(c) < 0)
                {
                    string next = _text + c;
                    if (new StringInfo(Body(next, out _)).LengthInTextElements <= MaxLength) _text = next;
                }
            }
            if (_text != before || composing != _composing) { _composing = composing; ShowTyped(); }
        }

        private static string _composing = "";

        private static string Body(string text, out bool party)
        {
            party = GameMode && text.StartsWith(PartyPrefix, StringComparison.Ordinal) && (text.Length == PartyPrefix.Length || text[PartyPrefix.Length] == ' ');
            return party ? text.Substring(PartyPrefix.Length).TrimStart() : text;
        }

        private static void Begin()
        {
            _typing = true;
            _skipFrame = true;
            _text = "";
            _composing = "";
            Input.imeCompositionMode = IMECompositionMode.On;
            FGInputLockService.SetChatTypeLock(true);
            Play(PlayerUtils.PlayerController, TypingId, TypingMarker, false);
            _bubble = BubbleOf(PlayerUtils.PlayerController);
            if (_bubble == null) Plugin.Log.LogWarning("chat: typing bubble never showed up, typing blind");
            _typingCh = GameMode ? _game : _party;
            Relay(_typingCh, TypingOn);
        }

        private static SpeechBubbleViewModel BubbleOf(FallGuysCharacterController fgcc)
        {
            var container = GameObject.Find(ContainerPath)?.GetComponent<SpeechBubblesControllerViewModel>();
            return container != null && container._speechBubbleByCharacter.TryGetValue(fgcc, out var vm) ? vm : null;
        }

        private static void ShowTyped()
        {
            if (_bubble == null || _bubble.CurrentSpeechId != TypingId) return;
            var p = Find(_bubble);
            Body(_text, out bool party);
            p.text.text = _text + _composing + "|";
            p.text.color = party ? PaleYellow : Color.white;
            p.text.ForceMeshUpdate();
            LayoutRebuilder.ForceRebuildLayoutImmediate(p.shadow);
        }

        private static void Send()
        {
            if (Time.unscaledTime - _lastSend < MinGap) return;
            string msg = Body(_text, out bool party).Trim();
            if (msg.Length == 0) { Cancel(); return; }
            bool toParty = party || !GameMode;
            var ch = toParty ? _party : _game;
            bool limited = Time.unscaledTime < _limitedUntil;
            bool relayed = !limited && ch.joined && _ws?.State == WebSocketState.Open;
            End(!relayed);
            if (limited) { ShowLimited(Mathf.CeilToInt(_limitedUntil - Time.unscaledTime)); return; }
            _lastSend = Time.unscaledTime;
            if (relayed) Relay(ch, $"{{\"t\":\"say\",\"text\":\"{JsonUtil.Escape(msg)}\"}}");
            else Play(PlayerUtils.PlayerController, toParty ? SentPartyId : SentGameId, msg, true);
            Plugin.Log.LogInfo($"chat said ({(toParty ? "party" : "game")}): {msg}");
        }

        private static void ShowLimited(int seconds)
        {
            var fgcc = PlayerUtils.PlayerController;
            if (fgcc == null) return;
            Play(fgcc, LimitedId, LocalizationService.Format("text_chat.rate_limited", seconds), true);
        }

        private static void Cancel()
        {
            if (!_typing) return;
            if (_bubble != null && _bubble.CurrentSpeechId == TypingId) _bubble.ForceRelease();
            End();
        }

        private static void End(bool relayOff = true)
        {
            _typing = false;
            _bubble = null;
            if (relayOff) Relay(_typingCh, TypingOff);
            Input.imeCompositionMode = IMECompositionMode.Auto;
            FGInputLockService.SetChatTypeLock(false);
        }

        private static void Play(FallGuysCharacterController fgcc, int id, string text, bool timed)
        {
            if (id != TypingId) text = text.Replace("<", "<​");
            var fn = fgcc.SpeechMotorFunction;
            var lookup = SingletonBehaviour<SpeechOptionsManager>.Instance._speechOptionsLookup;
            var opt = SpeechOptionBuilder.Build($"bfg_chat_{id}", text, SpeechOptionBuilder.FindReferencePhraseOption());
            opt._speechId = id;
            opt._speechHasDuration = timed;
            opt._speechDuration = SentSeconds;
            lookup[id] = opt;
            if (IsChat(id)) _recent.Add((text, Time.unscaledTime, Pill(id)));

            var states = fn.OriginalStates;
            for (int i = 0; i < states.Length; i++)
            {
                var active = states[i]?.TryCast<MotorFunctionSpeechStateActive>();
                if (active == null) continue;
                active.PlaySpeechOption(id);
                return;
            }
            Plugin.Log.LogWarning($"chat: no active speech state on {fgcc.name}");
        }

        internal static void OnBubbleInit(SpeechBubbleViewModel vm, SocialOption option)
        {
            int id = option.SpeechId;
            _dots.RemoveAll(d => d.vm.Pointer == vm.Pointer);
            if (id == RemoteTypingId) _dots.Add((vm, Find(vm).text));
            if (IsOwnBubble(id)) Style(vm, id);
            else if (_styled.Remove(vm.Pointer)) Restore(vm);
        }

        internal static bool IsChat(int id) => id >= SentGameId && id <= RemotePartyId;

        internal static bool IsOwnBubble(int id) => id >= TypingId && id <= RemoteTypingId;

        private static Color Pill(int id) => id == SentPartyId || id == RemotePartyId ? PaleYellow : LightBlue;

        internal static void OnFallFeedRow(FallFeedNotificationViewModel vm, string body)
        {
            if (body.EndsWith(TypingMarker, StringComparison.Ordinal) || body.EndsWith(Dots, StringComparison.Ordinal)) { vm.DisposeNotification(); return; }

            _recent.RemoveAll(r => Time.unscaledTime - r.at > 10f);
            int hit = _recent.FindIndex(r => body.EndsWith(r.text, StringComparison.Ordinal));
            if (hit < 0) { OnFallFeedDispose(vm); return; }

            var t = _recent[hit].tint;
            _recent.RemoveAt(hit);
            var bg = vm._bgImage;
            if (!_tintedRows.ContainsKey(bg.Pointer)) _tintedRows[bg.Pointer] = bg.color;
            bg.color = new Color(t.r, t.g, t.b, FeedAlpha);
        }

        internal static void OnFallFeedDispose(FallFeedNotificationViewModel vm)
        {
            var bg = vm._bgImage;
            if (_tintedRows.Remove(bg.Pointer, out var stock)) bg.color = stock;
        }

        internal static void OnPlayerSpawned(string accountId, GameObject bean, bool isLocal)
        {
            if (string.IsNullOrEmpty(accountId)) return;
            var gone = new List<string>();
            foreach (var kv in _beans) if (kv.Value == null) gone.Add(kv.Key);
            foreach (var k in gone) _beans.Remove(k);
            _beans[accountId] = bean;
            if (!isLocal) return;
            _account = accountId;
            if (_tip == null) BuildTip();
        }

        private static void BuildTip()
        {
            var tips = GameObject.Find(TipsPath);
            if (tips == null) return;
            var donor = tips.transform.Find("ViewPlayerNames");
            _tip = UnityEngine.Object.Instantiate(donor.gameObject, tips.transform, false);
            _tip.name = "BFG_TextChatTip";
            _tip.transform.SetSiblingIndex(donor.GetSiblingIndex() + 1);
            _tip.GetComponent<CanvasGroup>().alpha = 1f;

            var label = _tip.transform.Find("ViewPlayerNamesText");
            label.GetComponent("LocalisedStaticLabel").TryCast<Behaviour>().enabled = false;
            BettrFG.uGUI.UGUIShip.RelabelText(label.GetComponent<TextMeshProUGUI>(), "text_chat.tip");

            var acu = _tip.transform.Find("ControlGlyphButton").GetComponent<ActiveControllerUI>();
            acu._mappeable = false;
            acu._notMappeableKeyboardSprite = BetterFG.Core.NavPromptCore.KeyboardGlyph(KeyCode.T);
            acu._notMappeableJoystickSprite = null;
            acu._notMappeableKeyboardGlyphText = "";
            acu._notMappeableJoystickGlyphText = "";
            _tip.SetActive(false);
        }

        private static string Sha(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();

        internal static string ChatId(string room, string accountId) => Sha(room + accountId).Substring(0, 16);

        internal static string LobbyRoom => _lobby.room;

        internal static string ProfileId => FGClient.CatapultServices.CatapultServices.Instance?.PlayerProfileService?
            .TryCast<FGClient.CatapultServices.PlayerProfileService>()?.Profile?.Id;

        internal static bool SendSuggestion(string code)
        {
            if (!_lobby.joined || _ws?.State != WebSocketState.Open) return false;
            Relay(_lobby, $"{{\"t\":\"suggest\",\"code\":\"{code}\"}}");
            return true;
        }

        private static void PumpRelay()
        {
            while (_inbox.TryDequeue(out var json)) OnRelay(json);
            if (Time.unscaledTime < _nextCheck) return;
            _nextCheck = Time.unscaledTime + 2f;

            if (!_keyRead) { _keyRead = true; DevServerKey(ref _key); }
            bool on = Enabled && _key != null && EOSManagerFG.HasLoggedIn;
            string party = on && _account != null ? PartyStateManager.Instance?.CurrentPartyId : null;
            string game = on && _account != null && GameMode ? GlobalGameStateClient.Instance?.CachedEpisodeGuid : null;
            string profile = on ? ProfileId : null;
            string lobby = profile != null && LobbyRequests && PrivateLobbyUtils.IsInPrivateLobby() ? PrivateLobbyUtils.GetPrivateLobbyId() : null;

            if (_dropped)
            {
                _dropped = false;
                _ws = null;
                _retryAt = Time.unscaledTime + _backoff * UnityEngine.Random.Range(0.5f, 1.5f);
                _backoff = Mathf.Min(_backoff * 2f, 300f);
            }
            if (!string.IsNullOrEmpty(party) || !string.IsNullOrEmpty(game) || !string.IsNullOrEmpty(lobby))
            {
                if (_ws == null && Time.unscaledTime >= _retryAt) Open();
            }
            else if (_ws != null)
            {
                _ws.Abort();
                _ws = null;
            }
            if (_ws?.State != WebSocketState.Open)
            {
                _party.room = _game.room = _lobby.room = null;
                _party.joined = _game.joined = _lobby.joined = false;
                return;
            }
            var joins = new List<string> { Keep(_party, party, _account), Keep(_game, game, _account), Keep(_lobby, lobby, profile) };
            joins.RemoveAll(j => j == null);
            if (joins.Count > 0) Raw($"{{\"t\":\"joins\",\"list\":[{string.Join(",", joins)}]}}");
        }

        private static string Keep(Channel ch, string source, string account)
        {
            string room = string.IsNullOrEmpty(source) ? null : Sha(source);
            if (room == ch.room && (ch.joined || room == null || Time.unscaledTime < ch.retryAt)) return null;
            ch.room = room;
            ch.joined = false;
            ch.retryAt = Time.unscaledTime + 5f;
            return room == null
                ? $"{{\"ch\":\"{ch.name}\",\"room\":null}}"
                : $"{{\"ch\":\"{ch.name}\",\"room\":\"{room}\",\"from\":\"{ChatId(room, account)}\"}}";
        }

        private static void Open()
        {
            var o = new IdTokenOptions { ApiVersion = 1, LocalUserId = EOSManagerFG.UserID.InnerHandle };
            int result = EOS_Connect_CopyIdToken(EOSManagerFG.GetConnectInterface().InnerHandle, ref o, out var token);
            if (result != 0)
            {
                Plugin.Log.LogWarning($"no eos id token for chat (eos result {result}), trying later");
                _dropped = true;
                return;
            }
            string jwt = Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(token, 16));
            EOS_Connect_IdToken_Release(token);
            Connect(jwt);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IdTokenOptions
        {
            public int ApiVersion;
            public IntPtr LocalUserId;
        }

        [DllImport("EOSSDK-Win64-Shipping.dll")]
        private static extern int EOS_Connect_CopyIdToken(IntPtr connect, ref IdTokenOptions options, out IntPtr token);

        [DllImport("EOSSDK-Win64-Shipping.dll")]
        private static extern void EOS_Connect_IdToken_Release(IntPtr token);

        private static async void Connect(string idToken)
        {
            var ws = new ClientWebSocket();
            ws.Options.SetRequestHeader("X-BFG-Key", _key);
            ws.Options.SetRequestHeader("X-BFG-Id", idToken);
            string fg = BetterFG.Core.LocalPlayerInfo.FGlocalplayerusername;
            string shown = BetterFG.Core.LocalPlayerInfo.DisplayName;
            ws.Options.SetRequestHeader("X-BFG-Name", Uri.EscapeDataString(string.IsNullOrEmpty(fg) || shown == fg ? shown ?? "" : $"{shown} / {fg}"));
            _ws = ws;
            try
            {
                await ws.ConnectAsync(new Uri(RelayUrl), CancellationToken.None);
                _backoff = 15f;
                Plugin.Log.LogInfo("chat socket up");
                var buf = new byte[4096];
                while (ws.State == WebSocketState.Open)
                {
                    var r = await ws.ReceiveAsync(new ArraySegment<byte>(buf), CancellationToken.None);
                    if (r.MessageType == WebSocketMessageType.Close) break;
                    if (r.EndOfMessage) _inbox.Enqueue(Encoding.UTF8.GetString(buf, 0, r.Count));
                }
            }
            catch (Exception ex)
            {
                if (_ws == ws) Plugin.Log.LogWarning($"chat dropped: {ex.Message}");
            }
            if (_ws == ws) _dropped = true;
        }

        private static void Relay(Channel ch, string json)
        {
            if (ch.joined) Raw($"{{\"ch\":\"{ch.name}\",{json.Substring(1)}");
        }

        private static void Raw(string json)
        {
            var ws = _ws;
            if (ws == null || ws.State != WebSocketState.Open) return;
            var bytes = new ArraySegment<byte>(Encoding.UTF8.GetBytes(json));
            _sending = _sending.ContinueWith(_ => ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None)).Unwrap();
        }

        private static void OnRelay(string json)
        {
            string t = JsonUtil.GetValue(json, "t");
            if (t == "fx")
            {
                PlayFx(JsonUtil.GetValue(json, "id"));
                return;
            }
            string chName = JsonUtil.GetValue(json, "ch");
            var ch = chName == "party" ? _party : chName == "lobby" ? _lobby : _game;
            if (t == "joined" || t == "refused" || t == "kicked")
            {
                if (JsonUtil.GetValue(json, "room") != ch.room) return;
                ch.joined = t == "joined";
                if (ch == _lobby) LobbySuggestions.ResetPresence();
                if (ch.joined) ch.backoff = 15f;
                else
                {
                    ch.retryAt = Time.unscaledTime + ch.backoff;
                    ch.backoff = Mathf.Min(ch.backoff * 2f, 300f);
                }
                Plugin.Log.LogInfo($"{ch.name} chat {t} {ch.room?.Substring(0, 8)} {JsonUtil.GetValue(json, "why")}");
                return;
            }

            if (ch == _lobby)
            {
                if (ch.room == null) return;
                if (t == "suggest") LobbySuggestions.OnRemote(ch.room, JsonUtil.GetValue(json, "from"), JsonUtil.GetValue(json, "code"));
                else if (t == "here") LobbySuggestions.OnHere(JsonUtil.GetValue(json, "from"), JsonUtil.GetBool(json, "on"));
                return;
            }

            if (t == "limited")
            {
                int s = JsonUtil.GetInt(json, "s");
                _limitedUntil = Time.unscaledTime + s;
                ShowLimited(s);
                Plugin.Log.LogInfo($"relay says we're rate limited, {s}s");
                return;
            }

            if (t == "typing")
            {
                var typer = Speaker(ch, JsonUtil.GetValue(json, "from"));
                if (typer == null) return;
                if (JsonUtil.GetBool(json, "on")) { Play(typer, RemoteTypingId, Dots, false); return; }
                var vm = BubbleOf(typer);
                if (vm != null && vm.CurrentSpeechId == RemoteTypingId) vm.ForceRelease();
                return;
            }

            string text = JsonUtil.GetValue(json, "text");
            foreach (char c in Banned) text = text.Replace(c.ToString(), "");
            var si = new StringInfo(text);
            if (si.LengthInTextElements > MaxLength) text = si.SubstringByTextElements(0, MaxLength);
            if (text.Length == 0 || ch.room == null) return;

            if (t == "said")
            {
                var self = PlayerUtils.PlayerController;
                if (self != null) Play(self, ch == _party ? SentPartyId : SentGameId, text, true);
                return;
            }

            var speaker = Speaker(ch, JsonUtil.GetValue(json, "from"));
            if (speaker != null) Play(speaker, ch == _party ? RemotePartyId : RemoteGameId, text, true);
        }

        private static void PlayFx(string id)
        {
            var self = PlayerUtils.PlayerController;
            if (self == null) return;
            if (id == "checkpoint")
                self.GetComponentInChildren<FallGuyVFXController>(true)?.HandleCheckpointEvent(new VfxCheckpointEvent());
            else if (id == "confetti")
            {
                ClientGameManager cgm = null;
                GlobalGameStateClient.Instance?.GameStateView?.GetLiveClientGameManager(out cgm);
                if (cgm == null || cgm.CameraDirector == null) return;
                cgm.CameraDirector.PlayConfetti(new FG.Common.CameraDirector.ConfettiTimeEvent());
            }
            else return;
            Plugin.Log.LogInfo($"relay played {id} on us");
        }

        private static FallGuysCharacterController Speaker(Channel ch, string from)
        {
            if (ch.room == null) return null;
            foreach (var kv in _beans)
                if (kv.Value != null && ChatId(ch.room, kv.Key) == from) return kv.Value.GetComponent<FallGuysCharacterController>();
            Plugin.Log.LogInfo($"{ch.name} chat from {from}, no bean for it");
            return null;
        }

        private static Parts Find(SpeechBubbleViewModel vm)
        {
            var p = new Parts { root = vm.transform.Find("Canvas/SpeechBubble").Cast<RectTransform>() };
            for (int i = 0; i < p.root.childCount; i++)
            {
                var ch = p.root.GetChild(i);
                if (ch.name == "BackgroundShadow" && ch.gameObject.activeSelf) p.shadow = ch.Cast<RectTransform>();
            }
            p.pill = p.shadow.Find("Background").Cast<RectTransform>();
            p.layout = p.shadow.GetComponent<HorizontalLayoutGroup>();
            p.text = p.shadow.Find("Text").GetComponent<TextMeshProUGUI>();
            p.shadowImg = p.shadow.GetComponent<Image>();
            p.pillImg = p.pill.GetComponent<Image>();
            p.arrow = p.root.Find("Arrow").GetComponent<Image>();
            p.arrowShadow = p.root.Find("ArrowShadow").GetComponent<Image>();
            p.circle = p.root.Find("BackCircle").gameObject;
            p.circleShadow = p.root.Find("BackCircleShadow").gameObject;
            return p;
        }

        internal static void Style(SpeechBubbleViewModel vm, int id)
        {
            bool typing = id == TypingId || id == RemoteTypingId;
            var p = Find(vm);
            if (_styled.Add(vm.Pointer) && _stock == null)
                _stock = new Look
                {
                    padding = new RectOffset(p.layout.padding.left, p.layout.padding.right, p.layout.padding.top, p.layout.padding.bottom),
                    shadowPos = p.shadow.anchoredPosition,
                    shadowSize = p.shadow.sizeDelta,
                    rootSize = p.root.sizeDelta,
                    pillY = p.pill.anchoredPosition.y,
                    shadowIndex = p.shadow.GetSiblingIndex(),
                    margin = p.text.margin,
                    richText = p.text.richText,
                    parseCtrl = p.text.parseCtrlCharacters,
                    text = p.text.color,
                    shadow = p.shadowImg.color,
                    pill = p.pillImg.color,
                    arrow = p.arrow.color,
                    arrowShadow = p.arrowShadow.color,
                };

            p.circle.SetActive(false);
            p.circleShadow.SetActive(false);
            p.layout.padding = new RectOffset(24, 24, 6, 14);
            p.text.margin = Vector4.zero;
            p.text.richText = false;
            p.text.parseCtrlCharacters = false;
            p.shadow.sizeDelta = new Vector2(p.shadow.sizeDelta.x, Height);
            p.root.sizeDelta = new Vector2(p.root.sizeDelta.x, Height);
            p.shadow.anchoredPosition = _stock.shadowPos + new Vector2(-20f, -7f);
            p.pill.anchoredPosition = new Vector2(p.pill.anchoredPosition.x, 5f);
            p.shadow.SetSiblingIndex(p.arrowShadow.transform.GetSiblingIndex());

            Color pill = typing ? Color.black : id == LimitedId ? _stock.pill : Pill(id);
            p.text.color = typing ? Color.white : id == LimitedId ? LimitRed : _stock.text;
            p.shadowImg.color = typing ? Clear : _stock.shadow;
            p.pillImg.color = pill;
            p.arrow.color = pill;
            p.arrowShadow.color = typing ? Clear : _stock.arrowShadow;

            var textCanvas = p.text.GetComponent<Canvas>();
            if (textCanvas == null) textCanvas = p.text.gameObject.AddComponent<Canvas>();
            var bubbleCanvas = vm.transform.Find("Canvas").GetComponent<Canvas>();
            textCanvas.overrideSorting = true;
            textCanvas.sortingLayerID = bubbleCanvas.sortingLayerID;
            textCanvas.sortingOrder = bubbleCanvas.sortingOrder + 1;
        }

        private static void Restore(SpeechBubbleViewModel vm)
        {
            var p = Find(vm);
            p.circle.SetActive(true);
            p.circleShadow.SetActive(true);
            p.layout.padding = new RectOffset(_stock.padding.left, _stock.padding.right, _stock.padding.top, _stock.padding.bottom);
            p.text.margin = _stock.margin;
            p.text.richText = _stock.richText;
            p.text.parseCtrlCharacters = _stock.parseCtrl;
            p.shadow.sizeDelta = new Vector2(p.shadow.sizeDelta.x, _stock.shadowSize.y);
            p.root.sizeDelta = _stock.rootSize;
            p.shadow.anchoredPosition = _stock.shadowPos;
            p.pill.anchoredPosition = new Vector2(p.pill.anchoredPosition.x, _stock.pillY);
            p.shadow.SetSiblingIndex(_stock.shadowIndex);
            p.text.color = _stock.text;
            p.shadowImg.color = _stock.shadow;
            p.pillImg.color = _stock.pill;
            p.arrow.color = _stock.arrow;
            p.arrowShadow.color = _stock.arrowShadow;
            var textCanvas = p.text.GetComponent<Canvas>();
            if (textCanvas != null) textCanvas.overrideSorting = false;
        }
    }
}
