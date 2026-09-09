using System;
using System.Collections;
using BetterFG.Core;
using BetterFG.Services;
using BetterFG.Utilities;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using BettrFG.uGUI;

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

        private const float W = 560f;
        private const float H = 300f;
        private const float MARGIN = 20f;
        private const int FS_TITLE = 22;
        private const int FS_BODY = 13;
        private const int FS_LINK = 15;
        private const int FS_BTN = 12;

        private static readonly Color WHITE = UGUIShip.WHITE;
        private static readonly Color HINT = new Color(1f, 1f, 1f, 0.55f);
        private static readonly Color LINK = new Color(0.55f, 0.80f, 1.00f, 1f);
        private static readonly Color LINK_HOVER = new Color(0.25f, 0.50f, 0.90f, 1f);
        private static readonly Color BTN = new Color(0.20f, 0.20f, 0.22f, 1f);
        private static readonly Color TRANSP = Color.clear;

        private const float RECHECK_INTERVAL = 300f; // 5 min

        // the check can finish while the startup sequence or the tour still owns the screen, and the
        // window would land on top of them. hold it until the tour has written its seen key, then
        // let the loading cover pull back before sliding in.
        private const float POST_ONBOARDING_DELAY = 10f;

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

            while (SettingsService.Get(Features.Onboarding.OnboardingController.KEY_SEEN, "false") != "true")
                yield return new WaitForSecondsRealtime(0.5f);
            yield return new WaitForSecondsRealtime(POST_ONBOARDING_DELAY);

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

        // github release bodies are markdown with the odd <img>/<br> tag. we render them in a plain
        // Text, so drop anything in <...>, unwrap [text](url) to just text, and strip leading #/*/-
        // heading+bullet marks so it reads clean.
        private static string StripMarkup(string s)
        {
            var sb = new System.Text.StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '<')
                {
                    int end = s.IndexOf('>', i);
                    if (end < 0) break;      // dangling '<', drop the rest
                    i = end;                 // skip the whole tag
                    continue;
                }
                sb.Append(s[i]);
            }

            var lines = sb.ToString().Replace("\r", "").Split('\n');
            var outSb = new System.Text.StringBuilder();
            foreach (var raw in lines)
            {
                var line = raw;
                // ![alt](url) is rendered as a real image below the text, so drop the whole thing
                // here instead of letting the link unwrap below leave a stray "!alt".
                int im;
                while ((im = line.IndexOf("![", StringComparison.Ordinal)) >= 0)
                {
                    int irb = line.IndexOf(']', im);
                    if (irb < 0 || irb + 1 >= line.Length || line[irb + 1] != '(') break;
                    int irp = line.IndexOf(')', irb);
                    if (irp < 0) break;
                    line = line.Substring(0, im) + line.Substring(irp + 1);
                }

                // [label](url) -> label
                int lb;
                while ((lb = line.IndexOf('[')) >= 0)
                {
                    int rb = line.IndexOf(']', lb);
                    if (rb < 0 || rb + 1 >= line.Length || line[rb + 1] != '(') break;
                    int rp = line.IndexOf(')', rb);
                    if (rp < 0) break;
                    line = line.Substring(0, lb) + line.Substring(lb + 1, rb - lb - 1) + line.Substring(rp + 1);
                }
                line = line.Replace("**", "").Replace("`", "").Replace("__", "");
                line = line.TrimStart();
                while (line.StartsWith("#")) line = line.Substring(1).TrimStart();
                if (line.StartsWith("* ")) line = "• " + line.Substring(2);
                else if (line.StartsWith("- ")) line = "• " + line.Substring(2);
                outSb.Append(line).Append('\n');
            }
            return outSb.ToString().Trim();
        }

        // markdown ![alt](url) and html <img src="url">, in the order they appear in the body.
        private static System.Collections.Generic.List<string> ExtractImageUrls(string s)
        {
            var urls = new System.Collections.Generic.List<string>();
            if (string.IsNullOrEmpty(s)) return urls;

            int i = 0;
            while ((i = s.IndexOf("![", i, StringComparison.Ordinal)) >= 0)
            {
                int rb = s.IndexOf(']', i);
                if (rb < 0) break;
                if (rb + 1 < s.Length && s[rb + 1] == '(')
                {
                    int rp = s.IndexOf(')', rb);
                    if (rp > 0) urls.Add(TrimUrl(s.Substring(rb + 2, rp - rb - 2)));
                }
                i = rb + 1;
            }

            i = 0;
            while ((i = s.IndexOf("<img", i, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                int close = s.IndexOf('>', i);
                if (close < 0) break;
                string tag = s.Substring(i, close - i);
                int si = tag.IndexOf("src", StringComparison.OrdinalIgnoreCase);
                if (si >= 0)
                {
                    int q1 = tag.IndexOfAny(new[] { '"', '\'' }, si);
                    if (q1 > 0)
                    {
                        int q2 = tag.IndexOf(tag[q1], q1 + 1);
                        if (q2 > 0) urls.Add(TrimUrl(tag.Substring(q1 + 1, q2 - q1 - 1)));
                    }
                }
                i = close + 1;
            }

            urls.RemoveAll(u => string.IsNullOrEmpty(u) || !u.StartsWith("http", StringComparison.OrdinalIgnoreCase));
            return urls;
        }

        // markdown allows ![alt](url "title") — the title isn't part of the url
        private static string TrimUrl(string u)
        {
            u = u.Trim();
            int sp = u.IndexOf(' ');
            return sp > 0 ? u.Substring(0, sp) : u;
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

            // Root: centered anchor + pivot (same as BetterFGInfoWindow so the BG's
            // localPosition/localScale actually line up), then shifted top-left by
            // (-halfW - margin + halfCanvas) via anchoredPosition math.
            var refRes = UIScaleService.CurrentRef;
            _root = new GameObject("Window");
            _root.transform.SetParent(canvasGo.transform, false);
            var rt = _root.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(W, H);
            rt.anchoredPosition = new Vector2(MARGIN + W * 0.5f, -MARGIN - H * 0.5f);

            _root.AddComponent<Image>().color = TRANSP;

            // Background copied from BetterFGInfoWindow verbatim.
            var bgTex = EmbeddedResourceandUnity.LoadTexture("BetterFG.assets.ui.windows.general_update_bg.png");
            if (bgTex != null)
            {
                var bgGo = new GameObject("BG");
                bgGo.transform.SetParent(_root.transform, false);
                var bgRt = bgGo.AddComponent<RectTransform>();
                bgRt.anchorMin = Vector2.zero;
                bgRt.anchorMax = Vector2.one;
                bgRt.offsetMin = bgRt.offsetMax = Vector2.zero;
                bgRt.localPosition = new Vector3(6.4727f, 75.7635f, 0);
                bgRt.localScale = new Vector3(1.2943f, 2.8985f, 1);
                var bgImg = bgGo.AddComponent<RawImage>();
                bgImg.texture = bgTex;
                bgImg.raycastTarget = false;
            }

            float pad = 16f;
            var rootT = _root.transform;

            var titleGo = AddTopLabel(rootT, "ui.bettrfg_update_available", pad - 6f, 6f, 30f, W - pad * 2f,
                FS_TITLE, WHITE, FontStyle.Bold, TextAnchor.MiddleLeft);
            titleGo.transform.localPosition = new Vector3(-264.3782f, 114.6975f, 0f);
            titleGo.transform.localScale = new Vector3(0.8218f, 0.8746f, 1f);

            AddTopLabel(rootT, $"You're on {_current}. Latest release is {_latest}.", pad, 52f, 24f, W - pad * 2f,
                FS_BODY, WHITE, FontStyle.Normal, TextAnchor.MiddleLeft);

            AddTopLabel(rootT, "ui.what_s_new", pad, 86f, 22f, W - pad * 2f,
                FS_BODY, HINT, FontStyle.Normal, TextAnchor.MiddleLeft);

            float btnH = 28f;
            float btnY = H - btnH - pad * 0.86f;

            // action link gets its own row just above the button row so it never gets squeezed
            // by the two buttons next to it.
            float linkY = btnY - btnH - 6f;

            // changelog scroll sits between the header and the link row. narrower than the
            // window — leave a wider right margin so it doesn't run to the very edge.
            float clY = 112f;
            float clH = linkY - clY - 8f;
            float clW = W - pad - 120f;
            var (_, content) = UGUIShip.CreateScrollView(rootT, new Rect(pad, clY, clW, clH));

            string body = string.IsNullOrEmpty(_changelog) ? "(no changelog for this release)" : StripMarkup(_changelog);
            var clText = UGUIShip.CreateFlowLabel(content, body, FS_BODY, new Color(1f, 1f, 1f, 0.85f));
            clText.horizontalOverflow = HorizontalWrapMode.Wrap;
            clText.verticalOverflow = VerticalWrapMode.Overflow;
            clText.alignment = TextAnchor.UpperLeft;
            // size the content to the wrapped text height so the scroll bar has something to move
            float textW = clW - 26f; // minus scrollbar + viewport insets
            var clRt = clText.GetComponent<RectTransform>();
            clRt.anchorMin = new Vector2(0f, 1f);
            clRt.anchorMax = new Vector2(0f, 1f);
            clRt.pivot = new Vector2(0f, 1f);
            clRt.sizeDelta = new Vector2(textW, 0f);

            _clText = clText;
            _clContent = content;
            _clTextW = textW;

            foreach (var url in ExtractImageUrls(_changelog))
            {
                var imgGo = new GameObject("ChangelogImg");
                imgGo.transform.SetParent(content, false);
                var irt = imgGo.AddComponent<RectTransform>();
                irt.anchorMin = new Vector2(0f, 1f);
                irt.anchorMax = new Vector2(0f, 1f);
                irt.pivot = new Vector2(0f, 1f);
                irt.sizeDelta = new Vector2(textW, 0f);
                var raw = imgGo.AddComponent<RawImage>();
                raw.raycastTarget = false;
                _clImages.Add(raw);
                StartCoroutine(LoadChangelogImage(url, raw).WrapToIl2Cpp());
            }

            RelayoutChangelog();

            // primary action is a link on its own row. if the installer left its path behind we
            // relaunch it + quit; otherwise the link just opens the installer download page.
            string installerPath = SafeGetInstallerPath();
            if (!string.IsNullOrEmpty(installerPath))
            {
                _installerPath = installerPath;
                UGUIShip.CreateLinkLabel(rootT,
                    new Rect(pad, linkY, W - pad * 2f, btnH),
                    "ui.open_installer_and_close_game", INSTALLER_URL, FS_LINK, LINK, LINK_HOVER,
                    new Action(OpenInstallerAndQuit));
            }
            else
            {
                UGUIShip.CreateLinkLabel(rootT,
                    new Rect(pad, linkY, W - pad * 2f, btnH),
                    "ui.get_the_installer", INSTALLER_URL, FS_LINK, LINK, LINK_HOVER);
            }

            UGUIShip.CreateButton(rootT,
                new Rect(pad, btnY, 80f, btnH),
                "ui.close_2", BTN, WHITE, FS_BTN, new Action(Dismiss));
            UGUIShip.CreateButton(rootT,
                new Rect(pad + 90f, btnY, 200f, btnH),
                $"Don't show {_latest} again", BTN, WHITE, FS_BTN, new Action(DontShowAgain));
        }

        private Text _clText;
        private RectTransform _clContent;
        private float _clTextW;
        private readonly System.Collections.Generic.List<RawImage> _clImages =
            new System.Collections.Generic.List<RawImage>();

        private IEnumerator LoadChangelogImage(string url, RawImage target)
        {
            var req = UnityWebRequestTexture.GetTexture(url);
            req.SetRequestHeader("User-Agent", "BettrFG");
            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                var tex = DownloadHandlerTexture.GetContent(req);
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.hideFlags = HideFlags.HideAndDontSave;
                if (target != null) target.texture = tex;
            }
            else Plugin.Log.LogInfo($"changelog image didn't load ({req.result}): {url}");

            req.Dispose();
            RelayoutChangelog();
        }

        // text height has to come from the TMP mirror, not the Arial Text underneath it, or the
        // content is sized for fewer lines than get drawn and the tail is unreachable. images stack
        // under the text and only take space once their download has actually landed.
        private void RelayoutChangelog()
        {
            if (_clText == null || _clContent == null) return;

            float y = UGUIShip.PreferredHeight(_clText, _clTextW);
            _clText.GetComponent<RectTransform>().sizeDelta = new Vector2(_clTextW, y);

            foreach (var img in _clImages)
            {
                if (img == null || img.texture == null) continue;
                float h = _clTextW * img.texture.height / (float)img.texture.width;
                var rt = img.GetComponent<RectTransform>();
                rt.sizeDelta = new Vector2(_clTextW, h);
                y += 8f;
                rt.anchoredPosition = new Vector2(0f, -y);
                y += h;
            }

            _clContent.sizeDelta = new Vector2(0f, y);
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

        private GameObject _root;

        private static GameObject AddTopLabel(Transform parent, string text,
            float x, float topY, float height, float width,
            int fs, Color color, FontStyle style, TextAnchor anchor)
        {
            var t = UGUIShip.CreateLabel(parent, new Rect(x, topY, width, height), text, fs, color, anchor);
            t.fontStyle = style;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            return t.gameObject;
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
