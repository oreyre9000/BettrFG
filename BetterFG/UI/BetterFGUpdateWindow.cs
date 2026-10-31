using System;
using System.Collections;
using System.Collections.Generic;
using BetterFG.Core;
using BetterFG.Services;
using BetterFG.Utilities;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace BetterFG.UI
{
    /// <summary>
    /// Top-left update-check window. Fetches releases/latest from GitHub, compares tag
    /// against running version. Shows only when they differ and this version isn't on the
    /// user's "don't show again" list.
    /// </summary>
    public class BetterFGUpdateWindow : MonoBehaviour
    {
        public BetterFGUpdateWindow(IntPtr ptr) : base(ptr) { }

        private const string API_URL = "https://api.github.com/repos/oreyre9000/BettrFG/releases/latest";
        private const string INSTALLER_URL = "https://github.com/oreyre9000/BettrFG/releases/tag/installer";

        private const float W = 520f;
        private const float H = 440f;
        private const float PAD = 16f;
        private const float IMG_GAP = 6f;

        private static readonly Color WHITE = Color.white;
        private static readonly Color TITLE = new Color(1f, 1f, 1f, 0.85f);
        private static readonly Color BODY = new Color(1f, 1f, 1f, 0.8f);
        private static readonly Color HINT = new Color(1f, 1f, 1f, 0.4f);
        private static readonly Color BTN = new Color(0.18f, 0.18f, 0.18f, 1f);
        private static readonly Color BTN_MAIN = new Color(0.22f, 0.34f, 0.55f, 1f);

        private const float RECHECK_INTERVAL = 300f; // 5 min

        private string _latest;
        private string _current;
        private string _changelog;

        // Once we've surfaced an update we stop polling until the game restarts.
        private static bool _found;

        public static BetterFGUpdateWindow Show()
        {
            var go = new GameObject("BetterFG_UpdateWindow");
            go.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(go);
            return go.AddComponent<BetterFGUpdateWindow>();
        }

        void Awake() => StartCoroutine(PollLoop().WrapToIl2Cpp());

        private IEnumerator PollLoop()
        {
            while (!_found)
            {
                yield return CheckOnce().WrapToIl2Cpp();
                if (_found) yield break;
                yield return new WaitForSeconds(RECHECK_INTERVAL);
            }
        }

        private IEnumerator CheckOnce()
        {
            var req = UnityWebRequest.Get(API_URL);
            req.SetRequestHeader("User-Agent", "BettrFG");
            req.SetRequestHeader("Accept", "application/vnd.github+json");
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Plugin.Log.LogInfo($"couldn't reach GitHub for update check ({req.result})");
                yield break;
            }

            string json = req.downloadHandler.text;
            string latest = ParseTag(json);
            string changelog = ParseBody(json);
            req.Dispose();

            if (string.IsNullOrEmpty(latest))
            {
                Plugin.Log.LogInfo("no tag_name in releases/latest response");
                yield break;
            }

            string current = BetterFGInfo.Version;
            if (Norm(latest) == Norm(current))
                yield break;

            if (SettingsService.IsUpdateIgnored(Norm(latest)))
            {
                Plugin.Log.LogInfo($"{latest} is on ignore list, skipping");
                yield break;
            }

            _found = true;
            _latest = latest;
            _current = current;
            _changelog = changelog;
            Plugin.Log.LogInfo($"version mismatch: running {current}, latest release {latest}");
            while (!PrivacyNotice.Seen) yield return new WaitForSecondsRealtime(0.5f);
            yield return new WaitForSecondsRealtime(10f);
            Build();
        }

        private static string ParseTag(string json)
        {
            int i = json.IndexOf("\"tag_name\"", StringComparison.Ordinal);
            if (i < 0) return null;
            int colon = json.IndexOf(':', i);
            if (colon < 0) return null;
            int open = json.IndexOf('"', colon + 1);
            if (open < 0) return null;
            int close = json.IndexOf('"', open + 1);
            if (close < 0) return null;
            return json.Substring(open + 1, close - open - 1);
        }

        // pulls the release "body" (changelog markdown) out of the releases/latest json and
        // unescapes the json string so \n etc. render as real newlines in the scroll view.
        private static string ParseBody(string json)
        {
            int i = json.IndexOf("\"body\"", StringComparison.Ordinal);
            if (i < 0) return null;
            int colon = json.IndexOf(':', i);
            if (colon < 0) return null;
            int open = json.IndexOf('"', colon + 1);
            if (open < 0) return null;

            var sb = new System.Text.StringBuilder();
            for (int p = open + 1; p < json.Length; p++)
            {
                char c = json[p];
                if (c == '\\' && p + 1 < json.Length)
                {
                    char n = json[++p];
                    switch (n)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 'r': break;
                        case 't': sb.Append('\t'); break;
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'u':
                            if (p + 4 < json.Length &&
                                int.TryParse(json.Substring(p + 1, 4), System.Globalization.NumberStyles.HexNumber,
                                    System.Globalization.CultureInfo.InvariantCulture, out int code))
                            {
                                sb.Append((char)code);
                                p += 4;
                            }
                            break;
                        default: sb.Append(n); break;
                    }
                }
                else if (c == '"') break;
                else sb.Append(c);
            }
            var body = sb.ToString().Trim();
            return body.Length == 0 ? null : body;
        }

        private static string Inline(string s)
        {
            var sb = new System.Text.StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '<')
                {
                    int end = s.IndexOf('>', i);
                    if (end < 0) break;
                    var inner = s.Substring(i + 1, end - i - 1);
                    if (inner.StartsWith("http", StringComparison.OrdinalIgnoreCase)) sb.Append(inner);
                    i = end;
                    continue;
                }
                sb.Append(s[i]);
            }

            s = sb.ToString();
            int lb;
            while ((lb = s.IndexOf('[')) >= 0)
            {
                int rb = s.IndexOf(']', lb);
                if (rb < 0 || rb + 1 >= s.Length || s[rb + 1] != '(') break;
                int rp = s.IndexOf(')', rb);
                if (rp < 0) break;
                s = s.Substring(0, lb) + s.Substring(lb + 1, rb - lb - 1) + s.Substring(rp + 1);
            }

            var parts = s.Replace("`", "").Split(new[] { "**" }, StringSplitOptions.None);
            var outSb = new System.Text.StringBuilder();
            for (int i = 0; i < parts.Length; i++)
            {
                if (i % 2 == 0) outSb.Append(parts[i]);
                else if (i < parts.Length - 1) outSb.Append("<b>").Append(parts[i]).Append("</b>");
                else outSb.Append("**").Append(parts[i]);
            }
            return outSb.ToString().Trim();
        }

        private static string ImageUrl(string line)
        {
            int i = line.IndexOf("<img", StringComparison.OrdinalIgnoreCase);
            if (i >= 0)
            {
                int s = line.IndexOf("src=\"", i, StringComparison.OrdinalIgnoreCase);
                if (s < 0) return null;
                s += 5;
                int e = line.IndexOf('"', s);
                return e < 0 ? null : line.Substring(s, e - s);
            }
            i = line.IndexOf("![", StringComparison.Ordinal);
            if (i < 0) return null;
            int a = line.IndexOf("](", i, StringComparison.Ordinal);
            if (a < 0) return null;
            a += 2;
            int b = line.IndexOf(')', a);
            return b < 0 ? null : line.Substring(a, b - a).Split(' ')[0];
        }

        private static string Norm(string v) => v.Trim().TrimStart('v', 'V');

        private void Build()
        {
            var canvasGo = new GameObject("BetterFGUpdate_Canvas");
            canvasGo.hideFlags = HideFlags.HideAndDontSave;
            canvasGo.transform.SetParent(transform, false);

            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            UIScaleService.Register(canvas);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            var win = new GameObject("Window");
            win.transform.SetParent(canvasGo.transform, false);
            var rt = win.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(W, H);
            rt.anchoredPosition = new Vector2(24f, -150f);
            var rootT = win.transform;

            var bgTex = EmbeddedResourceandUnity.LoadTexture("BetterFG.assets.ui.windows.generalbg_2.png");
            float bw = W * 712f / 596f, bh = H * 712f / 437f;
            var bgGo = new GameObject("BG");
            bgGo.transform.SetParent(rootT, false);
            var bgRt = bgGo.AddComponent<RectTransform>();
            bgRt.anchorMin = bgRt.anchorMax = new Vector2(0f, 1f);
            bgRt.pivot = new Vector2(0f, 1f);
            bgRt.sizeDelta = new Vector2(bw, bh);
            bgRt.anchoredPosition = new Vector2(-45f / 712f * bw, 142f / 712f * bh);
            var bgImg = bgGo.AddComponent<RawImage>();
            bgImg.texture = bgTex;
            bgImg.raycastTarget = false;

            var title = UGUIShip.CreateLabel(rootT, new Rect(PAD, 8f, W - PAD * 2f, 26f), "BETTRFG UPDATE AVAILABLE", 13, TITLE, TextAnchor.MiddleLeft);
            title.fontStyle = FontStyle.Bold;
            UGUIShip.CreateLabel(rootT, new Rect(PAD, 8f, W - PAD * 2f, 26f), $"{Norm(_current)}  →  {Norm(_latest)}", 12, HINT, TextAnchor.MiddleRight);

            float footH = 22f, footY = H - PAD - footH;
            float clTop = 40f, clH = footY - 10f - clTop;
            var (_, content) = UGUIShip.CreateScrollView(rootT, new Rect(PAD, clTop, W - PAD * 2f + 14f, clH));
            _content = content;
            _cw = W - PAD * 2f - 12f;
            var vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = 3f;
            vlg.padding = new RectOffset(0, 0, 2, 8);
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            BuildChangelog(string.IsNullOrEmpty(_changelog) ? "(no changelog for this release)" : _changelog);
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);

            _installerPath = SafeGetInstallerPath();
            UGUIShip.CreateButton(rootT, new Rect(PAD, footY, 70f, footH), "Close", BTN, WHITE, 11, new Action(Dismiss));
            UGUIShip.CreateButton(rootT, new Rect(PAD + 76f, footY, 150f, footH), $"Don't show {Norm(_latest)} again", BTN, WHITE, 11, new Action(DontShowAgain));
            UGUIShip.CreateButton(rootT, new Rect(W - PAD - 190f, footY, 190f, footH),
                string.IsNullOrEmpty(_installerPath) ? "Get the installer" : "Open installer and close game",
                BTN_MAIN, WHITE, 11, new Action(OpenInstallerAndQuit));
        }

        private RectTransform _content;
        private float _cw;
        private readonly System.Text.StringBuilder _para = new System.Text.StringBuilder();
        private readonly List<string> _imgs = new List<string>();
        private int _blocks;

        private void BuildChangelog(string md)
        {
            foreach (var raw in md.Replace("\r", "").Split('\n'))
            {
                var line = raw.Trim();
                var url = ImageUrl(line);
                if (url != null) { FlushPara(); _imgs.Add(url); continue; }
                if (line.Length == 0) { FlushPara(); continue; }
                FlushImages();
                if (line == "---" || line == "***")
                {
                    FlushPara();
                    AddGap(4f);
                    var d = new GameObject("Rule");
                    d.transform.SetParent(_content, false);
                    d.AddComponent<RectTransform>();
                    var im = d.AddComponent<Image>();
                    im.color = new Color(1f, 1f, 1f, 0.12f);
                    im.raycastTarget = false;
                    d.AddComponent<LayoutElement>().preferredHeight = 1f;
                    AddGap(4f);
                    continue;
                }
                if (line.StartsWith("#"))
                {
                    FlushPara();
                    int lvl = 0;
                    while (lvl < line.Length && line[lvl] == '#') lvl++;
                    if (_blocks > 0) AddGap(lvl <= 2 ? 8f : 4f);
                    var h = AddText(_content, Inline(line.Substring(lvl)), lvl == 1 ? 16 : lvl == 2 ? 14 : 12, lvl >= 3 ? TITLE : WHITE);
                    h.fontStyle = FontStyle.Bold;
                    _blocks++;
                    continue;
                }
                if (line.StartsWith("- ") || line.StartsWith("* ") || line.StartsWith("+ "))
                {
                    FlushPara();
                    var row = new GameObject("Bullet");
                    row.transform.SetParent(_content, false);
                    row.AddComponent<RectTransform>();
                    var hl = row.AddComponent<HorizontalLayoutGroup>();
                    hl.childControlWidth = true;
                    hl.childControlHeight = true;
                    hl.childForceExpandWidth = false;
                    hl.childForceExpandHeight = false;
                    hl.spacing = 2f;
                    hl.padding = new RectOffset(6, 0, 0, 0);
                    var dot = AddText(row.transform, "•", 12, HINT).gameObject.AddComponent<LayoutElement>();
                    dot.preferredWidth = dot.minWidth = 10f;
                    dot.flexibleWidth = 0f;
                    AddText(row.transform, Inline(line.Substring(2)), 12, BODY).gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
                    _blocks++;
                    continue;
                }
                _para.Append(line).Append(' ');
            }
            FlushPara();
            FlushImages();
        }

        private Text AddText(Transform parent, string s, int fs, Color c)
        {
            var t = UGUIShip.CreateLabel(parent, new Rect(0f, 0f, _cw, 20f), s, fs, c, TextAnchor.UpperLeft);
            t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        private void AddGap(float h)
        {
            var g = new GameObject("Gap");
            g.transform.SetParent(_content, false);
            g.AddComponent<RectTransform>();
            g.AddComponent<LayoutElement>().preferredHeight = h;
        }

        private void FlushPara()
        {
            if (_para.Length == 0) return;
            AddText(_content, Inline(_para.ToString()), 12, BODY);
            _para.Clear();
            _blocks++;
        }

        private void FlushImages()
        {
            if (_imgs.Count == 0) return;
            var row = new GameObject("Images");
            row.transform.SetParent(_content, false);
            row.AddComponent<RectTransform>();
            var hl = row.AddComponent<HorizontalLayoutGroup>();
            hl.childControlWidth = true;
            hl.childControlHeight = true;
            hl.childForceExpandWidth = false;
            hl.childForceExpandHeight = false;
            hl.spacing = IMG_GAP;
            hl.childAlignment = TextAnchor.UpperLeft;
            foreach (var url in _imgs)
            {
                var ig = new GameObject("Image");
                ig.transform.SetParent(row.transform, false);
                ig.AddComponent<RectTransform>();
                var ri = ig.AddComponent<RawImage>();
                ri.raycastTarget = false;
                ig.AddComponent<LayoutElement>();
                ig.SetActive(false);
                StartCoroutine(LoadImage(url, ri).WrapToIl2Cpp());
            }
            AddGap(2f);
            _imgs.Clear();
            _blocks++;
        }

        private IEnumerator LoadImage(string url, RawImage target)
        {
            var req = UnityWebRequestTexture.GetTexture(url);
            req.SetRequestHeader("User-Agent", "BettrFG");
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Plugin.Log.LogInfo($"changelog image didn't load ({req.result}): {url}");
                req.Dispose();
                yield break;
            }

            var tex = DownloadHandlerTexture.GetContent(req);
            req.Dispose();
            if (target == null) yield break;
            tex.wrapMode = TextureWrapMode.Clamp;
            target.texture = tex;

            var row = target.transform.parent;
            float asp = 0f;
            int n = 0;
            for (int i = 0; i < row.childCount; i++)
            {
                var t = row.GetChild(i).GetComponent<RawImage>().texture;
                if (t == null) continue;
                asp += t.width / (float)t.height;
                n++;
            }
            float h = (_cw - IMG_GAP * (n - 1)) / asp;
            for (int i = 0; i < row.childCount; i++)
            {
                var c = row.GetChild(i);
                var t = c.GetComponent<RawImage>().texture;
                if (t == null) continue;
                var le = c.GetComponent<LayoutElement>();
                le.preferredHeight = h;
                le.preferredWidth = h * t.width / t.height;
                c.gameObject.SetActive(true);
            }
            LayoutRebuilder.MarkLayoutForRebuild(_content);
        }

        private string _installerPath;

        private static string SafeGetInstallerPath()
        {
            // the installer writes its own exe path here on launch. env vars don't reach a
            // steam-launched game, so we read the file the installer stamps instead.
            var stamp = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BettrFG", "installer_path.txt");
            if (!System.IO.File.Exists(stamp))
                return null;

            var p = System.IO.File.ReadAllText(stamp).Trim();
            if (string.IsNullOrEmpty(p) || !p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !System.IO.File.Exists(p))
                return null;
            return p;
        }

        private void OpenInstallerAndQuit()
        {
            if (string.IsNullOrEmpty(_installerPath))
            {
                Application.OpenURL(INSTALLER_URL);
                return;
            }

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = _installerPath,
                    UseShellExecute = true
                });
                Application.Quit();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"couldn't launch installer at {_installerPath}, opening the page instead ({ex.Message})");
                Application.OpenURL(INSTALLER_URL);
            }
        }

        private void DontShowAgain()
        {
            if (!string.IsNullOrEmpty(_latest))
                SettingsService.IgnoreUpdate(Norm(_latest));
            Dismiss();
        }

        private void Dismiss()
        {
            if (gameObject != null) Destroy(gameObject);
        }
    }
}
