using UnityEngine;
using BetterFG.Services;

namespace BetterFG.Customization.UI
{
    public partial class MenuCustomizationApplication
    {
        // ── Banner colour overrides (Qualified / Eliminated) ──────────────────
        // each banner subtree gets its Image fills + TMP text/outline/underlay swapped to user colours.
        // four per-banner channels: bg, text, outline, underlay. each with its own on flag + rgb.
        // applied straight from the OnOpened patch — the banner builds its visuals before OnOpened fires,
        // so a single sweep of its children is enough.

        // per-banner master enable. when off, ApplyBannerColours skips the recolour entirely and the
        // banner stays stock — regardless of per-slot .on flags. live-saved from the UI toggle.
        public const string KEY_BANNER_QUAL_ENABLED  = "menu.banner.qual.enabled";
        public const string KEY_BANNER_ELIM_ENABLED  = "menu.banner.elim.enabled";
        public const string KEY_BANNER_WIN_ENABLED   = "menu.banner.win.enabled";
        public const string KEY_BANNER_ROUND_ENABLED = "menu.banner.round.enabled";

        // per-banner replacement colours (cyan / pink / black / white). same shape as KEY_FG_*:
        // each replacement remaps the matching source colour wherever it appears on the banner's
        // Images, TMP text fills, outline material colour, and underlay material colour.
        public const string KEY_BANNER_QUAL_CYAN_ON  = "menu.banner.qual.cyan.on";
        public const string KEY_BANNER_QUAL_CYAN_R   = "menu.banner.qual.cyan.r";
        public const string KEY_BANNER_QUAL_CYAN_G   = "menu.banner.qual.cyan.g";
        public const string KEY_BANNER_QUAL_CYAN_B   = "menu.banner.qual.cyan.b";
        public const string KEY_BANNER_QUAL_PINK_ON  = "menu.banner.qual.pink.on";
        public const string KEY_BANNER_QUAL_PINK_R   = "menu.banner.qual.pink.r";
        public const string KEY_BANNER_QUAL_PINK_G   = "menu.banner.qual.pink.g";
        public const string KEY_BANNER_QUAL_PINK_B   = "menu.banner.qual.pink.b";
        public const string KEY_BANNER_QUAL_BLACK_ON = "menu.banner.qual.black.on";
        public const string KEY_BANNER_QUAL_BLACK_R  = "menu.banner.qual.black.r";
        public const string KEY_BANNER_QUAL_BLACK_G  = "menu.banner.qual.black.g";
        public const string KEY_BANNER_QUAL_BLACK_B  = "menu.banner.qual.black.b";
        public const string KEY_BANNER_QUAL_WHITE_ON     = "menu.banner.qual.white.on";
        public const string KEY_BANNER_QUAL_WHITE_R      = "menu.banner.qual.white.r";
        public const string KEY_BANNER_QUAL_WHITE_G      = "menu.banner.qual.white.g";
        public const string KEY_BANNER_QUAL_WHITE_B      = "menu.banner.qual.white.b";
        public const string KEY_BANNER_QUAL_HIGHLIGHT_ON = "menu.banner.qual.highlight.on";
        public const string KEY_BANNER_QUAL_HIGHLIGHT_R  = "menu.banner.qual.highlight.r";
        public const string KEY_BANNER_QUAL_HIGHLIGHT_G  = "menu.banner.qual.highlight.g";
        public const string KEY_BANNER_QUAL_HIGHLIGHT_B  = "menu.banner.qual.highlight.b";

        public const string KEY_BANNER_ELIM_CYAN_ON  = "menu.banner.elim.cyan.on";
        public const string KEY_BANNER_ELIM_CYAN_R   = "menu.banner.elim.cyan.r";
        public const string KEY_BANNER_ELIM_CYAN_G   = "menu.banner.elim.cyan.g";
        public const string KEY_BANNER_ELIM_CYAN_B   = "menu.banner.elim.cyan.b";
        public const string KEY_BANNER_ELIM_PINK_ON  = "menu.banner.elim.pink.on";
        public const string KEY_BANNER_ELIM_PINK_R   = "menu.banner.elim.pink.r";
        public const string KEY_BANNER_ELIM_PINK_G   = "menu.banner.elim.pink.g";
        public const string KEY_BANNER_ELIM_PINK_B   = "menu.banner.elim.pink.b";
        public const string KEY_BANNER_ELIM_BLACK_ON = "menu.banner.elim.black.on";
        public const string KEY_BANNER_ELIM_BLACK_R  = "menu.banner.elim.black.r";
        public const string KEY_BANNER_ELIM_BLACK_G  = "menu.banner.elim.black.g";
        public const string KEY_BANNER_ELIM_BLACK_B  = "menu.banner.elim.black.b";
        public const string KEY_BANNER_ELIM_WHITE_ON     = "menu.banner.elim.white.on";
        public const string KEY_BANNER_ELIM_WHITE_R      = "menu.banner.elim.white.r";
        public const string KEY_BANNER_ELIM_WHITE_G      = "menu.banner.elim.white.g";
        public const string KEY_BANNER_ELIM_WHITE_B      = "menu.banner.elim.white.b";
        public const string KEY_BANNER_ELIM_HIGHLIGHT_ON = "menu.banner.elim.highlight.on";
        public const string KEY_BANNER_ELIM_HIGHLIGHT_R  = "menu.banner.elim.highlight.r";
        public const string KEY_BANNER_ELIM_HIGHLIGHT_G  = "menu.banner.elim.highlight.g";
        public const string KEY_BANNER_ELIM_HIGHLIGHT_B  = "menu.banner.elim.highlight.b";

        // winner banner: yellow / orange / white / black (+ highlight)
        public const string KEY_BANNER_WIN_YELLOW_ON = "menu.banner.win.yellow.on";
        public const string KEY_BANNER_WIN_YELLOW_R  = "menu.banner.win.yellow.r";
        public const string KEY_BANNER_WIN_YELLOW_G  = "menu.banner.win.yellow.g";
        public const string KEY_BANNER_WIN_YELLOW_B  = "menu.banner.win.yellow.b";
        public const string KEY_BANNER_WIN_ORANGE_ON = "menu.banner.win.orange.on";
        public const string KEY_BANNER_WIN_ORANGE_R  = "menu.banner.win.orange.r";
        public const string KEY_BANNER_WIN_ORANGE_G  = "menu.banner.win.orange.g";
        public const string KEY_BANNER_WIN_ORANGE_B  = "menu.banner.win.orange.b";
        public const string KEY_BANNER_WIN_WHITE_ON  = "menu.banner.win.white.on";
        public const string KEY_BANNER_WIN_WHITE_R   = "menu.banner.win.white.r";
        public const string KEY_BANNER_WIN_WHITE_G   = "menu.banner.win.white.g";
        public const string KEY_BANNER_WIN_WHITE_B   = "menu.banner.win.white.b";
        public const string KEY_BANNER_WIN_BLACK_ON  = "menu.banner.win.black.on";
        public const string KEY_BANNER_WIN_BLACK_R   = "menu.banner.win.black.r";
        public const string KEY_BANNER_WIN_BLACK_G   = "menu.banner.win.black.g";
        public const string KEY_BANNER_WIN_BLACK_B   = "menu.banner.win.black.b";
        public const string KEY_BANNER_WIN_HIGHLIGHT_ON = "menu.banner.win.highlight.on";
        public const string KEY_BANNER_WIN_HIGHLIGHT_R  = "menu.banner.win.highlight.r";
        public const string KEY_BANNER_WIN_HIGHLIGHT_G  = "menu.banner.win.highlight.g";
        public const string KEY_BANNER_WIN_HIGHLIGHT_B  = "menu.banner.win.highlight.b";

        // round over banner: black / pink / blue / white (+ highlight)
        public const string KEY_BANNER_ROUND_BLACK_ON = "menu.banner.round.black.on";
        public const string KEY_BANNER_ROUND_BLACK_R  = "menu.banner.round.black.r";
        public const string KEY_BANNER_ROUND_BLACK_G  = "menu.banner.round.black.g";
        public const string KEY_BANNER_ROUND_BLACK_B  = "menu.banner.round.black.b";
        public const string KEY_BANNER_ROUND_PINK_ON  = "menu.banner.round.pink.on";
        public const string KEY_BANNER_ROUND_PINK_R   = "menu.banner.round.pink.r";
        public const string KEY_BANNER_ROUND_PINK_G   = "menu.banner.round.pink.g";
        public const string KEY_BANNER_ROUND_PINK_B   = "menu.banner.round.pink.b";
        public const string KEY_BANNER_ROUND_BLUE_ON  = "menu.banner.round.blue.on";
        public const string KEY_BANNER_ROUND_BLUE_R   = "menu.banner.round.blue.r";
        public const string KEY_BANNER_ROUND_BLUE_G   = "menu.banner.round.blue.g";
        public const string KEY_BANNER_ROUND_BLUE_B   = "menu.banner.round.blue.b";
        public const string KEY_BANNER_ROUND_WHITE_ON = "menu.banner.round.white.on";
        public const string KEY_BANNER_ROUND_WHITE_R  = "menu.banner.round.white.r";
        public const string KEY_BANNER_ROUND_WHITE_G  = "menu.banner.round.white.g";
        public const string KEY_BANNER_ROUND_WHITE_B  = "menu.banner.round.white.b";
        public const string KEY_BANNER_ROUND_HIGHLIGHT_ON = "menu.banner.round.highlight.on";
        public const string KEY_BANNER_ROUND_HIGHLIGHT_R  = "menu.banner.round.highlight.r";
        public const string KEY_BANNER_ROUND_HIGHLIGHT_G  = "menu.banner.round.highlight.g";
        public const string KEY_BANNER_ROUND_HIGHLIGHT_B  = "menu.banner.round.highlight.b";

        public const string KEY_BANNER_SQUAD_ENABLED     = "menu.banner.squad.enabled";

        // (bucket, key-prefix) describes one slot. the prefix is the settings key minus the trailing
        // .on/.r/.g/.b — e.g. "menu.banner.qual.cyan". highlight is matched by component, not hue.
        private struct BannerSlotKeys { public BannerBucket bucket; public string prefix; }

        private static readonly BannerSlotKeys[] QualSlots =
        {
            new BannerSlotKeys { bucket = BannerBucket.Cyan,  prefix = "menu.banner.qual.cyan"  },
            new BannerSlotKeys { bucket = BannerBucket.Pink,  prefix = "menu.banner.qual.pink"  },
            new BannerSlotKeys { bucket = BannerBucket.Black, prefix = "menu.banner.qual.black" },
            new BannerSlotKeys { bucket = BannerBucket.White, prefix = "menu.banner.qual.white" },
        };
        private static readonly BannerSlotKeys[] ElimSlots =
        {
            new BannerSlotKeys { bucket = BannerBucket.Cyan,  prefix = "menu.banner.elim.cyan"  },
            new BannerSlotKeys { bucket = BannerBucket.Pink,  prefix = "menu.banner.elim.pink"  },
            new BannerSlotKeys { bucket = BannerBucket.Black, prefix = "menu.banner.elim.black" },
            new BannerSlotKeys { bucket = BannerBucket.White, prefix = "menu.banner.elim.white" },
        };
        private static readonly BannerSlotKeys[] WinnerSlots =
        {
            new BannerSlotKeys { bucket = BannerBucket.Yellow, prefix = "menu.banner.win.yellow" },
            new BannerSlotKeys { bucket = BannerBucket.Orange, prefix = "menu.banner.win.orange" },
            new BannerSlotKeys { bucket = BannerBucket.White,  prefix = "menu.banner.win.white"  },
            new BannerSlotKeys { bucket = BannerBucket.BlackGrey, prefix = "menu.banner.win.black"  },
        };
        private static readonly BannerSlotKeys[] RoundOverSlots =
        {
            new BannerSlotKeys { bucket = BannerBucket.BlackGrey, prefix = "menu.banner.round.black" },
            new BannerSlotKeys { bucket = BannerBucket.Pink,  prefix = "menu.banner.round.pink"  },
            new BannerSlotKeys { bucket = BannerBucket.Cyan,  prefix = "menu.banner.round.blue"  },
            new BannerSlotKeys { bucket = BannerBucket.White, prefix = "menu.banner.round.white" },
        };
        // squad-elimination banner (EliminatedSquadScreenViewModel). broader palette than the solo
        // Eliminated one: orange + a black/grey split + pink/blue/yellow/white.
        private static readonly BannerSlotKeys[] SquadSlots =
        {
            new BannerSlotKeys { bucket = BannerBucket.Orange,    prefix = "menu.banner.squad.orange" },
            new BannerSlotKeys { bucket = BannerBucket.BlackGrey, prefix = "menu.banner.squad.black"  },
            new BannerSlotKeys { bucket = BannerBucket.Pink,      prefix = "menu.banner.squad.pink"   },
            new BannerSlotKeys { bucket = BannerBucket.Cyan,      prefix = "menu.banner.squad.blue"   },
            new BannerSlotKeys { bucket = BannerBucket.Yellow,    prefix = "menu.banner.squad.yellow" },
            new BannerSlotKeys { bucket = BannerBucket.White,     prefix = "menu.banner.squad.white"  },
        };

        public enum BannerScreen { Qualified, Eliminated, Winner, RoundOver, Squad }

        public void ApplyBannerColours(Component banner, BannerScreen screen)
        {
            switch (screen)
            {
                case BannerScreen.Qualified: ApplyBannerColours(banner, QualSlots, "menu.banner.qual.highlight", KEY_BANNER_QUAL_ENABLED); break;
                case BannerScreen.Eliminated: ApplyBannerColours(banner, ElimSlots, "menu.banner.elim.highlight", KEY_BANNER_ELIM_ENABLED); break;
                case BannerScreen.Winner:
                    ApplyBannerColours(banner, WinnerSlots, "menu.banner.win.highlight", KEY_BANNER_WIN_ENABLED);
                    ApplyWinnerRoundOverWhiteOverride(banner);
                    break;
                case BannerScreen.RoundOver: ApplyBannerColours(banner, RoundOverSlots, "menu.banner.round.highlight", KEY_BANNER_ROUND_ENABLED); break;
                case BannerScreen.Squad: ApplyBannerColours(banner, SquadSlots, "menu.banner.squad.highlight", KEY_BANNER_SQUAD_ENABLED); break;
            }
        }

        // the Winner banner has a "round-over-white" image nested somewhere under it that the hue
        // matcher misses (its colour doesn't sit cleanly in the Yellow bucket). force-recolour it to
        // the Yellow replacement so the banner reads consistently when yellow customisation is on.
        private void ApplyWinnerRoundOverWhiteOverride(Component banner)
        {
            if (banner == null) return;
            if (SettingsService.Get(KEY_BANNER_WIN_ENABLED, "false") != "true") return;
            if (SettingsService.Get("menu.banner.win.yellow.on", "false") != "true") return;
            Color yellow = new Color(ParseF("menu.banner.win.yellow.r", 1f), ParseF("menu.banner.win.yellow.g", 0.85f), ParseF("menu.banner.win.yellow.b", 0f));
            foreach (var t in banner.transform.GetComponentsInChildren<Transform>(true))
            {
                if (t == null || t.gameObject.name != "round-over-white") continue;
                var img = t.GetComponent<UnityEngine.UI.Image>();
                if (img != null) img.color = new Color(yellow.r, yellow.g, yellow.b, img.color.a);
            }
        }

        private void ApplyBannerColours(Component banner, BannerSlotKeys[] slotKeys, string highlightPrefix, string enabledKey)
        {
            if (banner == null) return;
            ApplyBannerColours(banner, ColoursFromSettings(slotKeys, highlightPrefix, enabledKey));
        }

        private static BannerColours ColoursFromSettings(BannerSlotKeys[] slotKeys, string highlightPrefix, string enabledKey)
        {
            var slots = new System.Collections.Generic.List<BannerSlot>();
            if (SettingsService.Get(enabledKey, "false") == "true")
                foreach (var sk in slotKeys)
                {
                    if (SettingsService.Get(sk.prefix + ".on", "false") != "true") continue;
                    slots.Add(new BannerSlot
                    {
                        bucket = sk.bucket,
                        target = new Color(ParseF(sk.prefix + ".r", 1f), ParseF(sk.prefix + ".g", 1f), ParseF(sk.prefix + ".b", 1f)),
                    });
                }

            bool highlightOn = SettingsService.Get(enabledKey, "false") == "true" && SettingsService.Get(highlightPrefix + ".on", "false") == "true";
            Color highlight = new Color(ParseF(highlightPrefix + ".r", 1f), ParseF(highlightPrefix + ".g", 1f), ParseF(highlightPrefix + ".b", 1f));

            return new BannerColours { slots = slots, highlightOn = highlightOn, highlight = highlight };
        }

        // same lookup ApplyBannerColours(Component, BannerScreen) uses, minus the game-object walk —
        // lets the UI tab's carousel preview a saved (unedited) colour set without a live banner around.
        public BannerColours GetBannerColoursFromSettings(BannerScreen screen)
        {
            switch (screen)
            {
                case BannerScreen.Qualified: return ColoursFromSettings(QualSlots, "menu.banner.qual.highlight", KEY_BANNER_QUAL_ENABLED);
                case BannerScreen.Eliminated: return ColoursFromSettings(ElimSlots, "menu.banner.elim.highlight", KEY_BANNER_ELIM_ENABLED);
                case BannerScreen.Winner: return ColoursFromSettings(WinnerSlots, "menu.banner.win.highlight", KEY_BANNER_WIN_ENABLED);
                case BannerScreen.RoundOver: return ColoursFromSettings(RoundOverSlots, "menu.banner.round.highlight", KEY_BANNER_ROUND_ENABLED);
                case BannerScreen.Squad: return ColoursFromSettings(SquadSlots, "menu.banner.squad.highlight", KEY_BANNER_SQUAD_ENABLED);
                default: return default;
            }
        }

        // banner colour replacement set + the HSV matcher, shared so the UI tab's live preview
        // recolours banners with the exact same rules as the real apply path. each channel is a
        // target colour + on flag; highlight is matched by component (ScrollUVs) not by colour.
        // the hue/value buckets a banner slot can map. each banner type exposes a different subset
        // (qual/elim: cyan/pink/black/white, winner: yellow/orange/white/black, roundover: black/pink/blue/white).
        public enum BannerBucket { Black, White, Cyan, Pink, Yellow, Orange, Blue, BlackGrey }

        public struct BannerSlot { public BannerBucket bucket; public Color target; }

        public struct BannerColours
        {
            public System.Collections.Generic.List<BannerSlot> slots;
            public bool highlightOn;
            public Color highlight;

            public bool AnyOn => highlightOn || (slots != null && slots.Count > 0);

            private static bool BucketMatches(BannerBucket b, float h, float s, float v)
            {
                switch (b)
                {
                    case BannerBucket.Black:  return v < 0.25f;
                    // dark + greys: low saturation up to (but not into) the white band, any value below it
                    case BannerBucket.BlackGrey: return s < 0.2f && v < 0.85f;
                    case BannerBucket.White:  return v > 0.85f && s < 0.15f;
                    case BannerBucket.Cyan:   return s > 0.3f && v > 0.3f && h >= 0.47f && h <= 0.58f;
                    case BannerBucket.Pink:   return s > 0.3f && v > 0.3f && (h >= 0.88f || h <= 0.05f);
                    case BannerBucket.Yellow: return s > 0.3f && v > 0.3f && h >= 0.13f && h <= 0.19f;
                    case BannerBucket.Orange: return s > 0.3f && v > 0.3f && h >= 0.05f && h <= 0.11f;
                    case BannerBucket.Blue:   return s > 0.3f && v > 0.3f && h >= 0.58f && h <= 0.72f;
                }
                return false;
            }

            public bool TryMatch(Color c, out Color target)
            {
                if (slots != null)
                {
                    Color.RGBToHSV(c, out float h, out float s, out float v);
                    for (int i = 0; i < slots.Count; i++)
                        if (BucketMatches(slots[i].bucket, h, s, v)) { target = slots[i].target; return true; }
                }
                target = default; return false;
            }

            public static bool IsHighlight(UnityEngine.UI.Image img) =>
                img.GetComponent<ScrollUVs>() != null || img.GetComponent<UI_ScrollUvs>() != null;
        }

        // colour-driven overload: same image/TMP recolour walk, but the caller hands us the
        // already-resolved replacement colours + on flags instead of settings keys. lets the UI
        // tab's live preview reuse the exact apply logic with unsaved slider values.
        public void ApplyBannerColours(Component banner, BannerColours set)
        {
            if (banner == null) return;
            var root = banner.transform;
            if (root == null) return;

            bool highlightOn = set.highlightOn;
            Color highlightTarget = set.highlight;
            if (!set.AnyOn) return;

            foreach (var img in root.GetComponentsInChildren<UnityEngine.UI.Image>(true))
            {
                if (img == null) continue;
                if (highlightOn && BannerColours.IsHighlight(img))
                {
                    img.color = new Color(highlightTarget.r, highlightTarget.g, highlightTarget.b, img.color.a);
                    continue;
                }
                if (set.TryMatch(img.color, out var t))
                    img.color = new Color(t.r, t.g, t.b, img.color.a);
            }

            foreach (var tmp in root.GetComponentsInChildren<TMPro.TMP_Text>(true))
            {
                if (tmp == null) continue;
                if (set.TryMatch(tmp.color, out var tFill))
                    tmp.color = new Color(tFill.r, tFill.g, tFill.b, tmp.color.a);

                if (tmp.fontSharedMaterial == null) continue;
                var mat = tmp.fontMaterial;
                if (mat.HasProperty(TMPro.ShaderUtilities.ID_OutlineColor))
                {
                    var oc = mat.GetColor(TMPro.ShaderUtilities.ID_OutlineColor);
                    if (set.TryMatch(oc, out var tOut))
                        mat.SetColor(TMPro.ShaderUtilities.ID_OutlineColor, new Color(tOut.r, tOut.g, tOut.b, oc.a));
                }
                if (mat.HasProperty(TMPro.ShaderUtilities.ID_UnderlayColor))
                {
                    var uc = mat.GetColor(TMPro.ShaderUtilities.ID_UnderlayColor);
                    if (set.TryMatch(uc, out var tUn))
                        mat.SetColor(TMPro.ShaderUtilities.ID_UnderlayColor, new Color(tUn.r, tUn.g, tUn.b, uc.a));
                }
            }
        }
    }
}
