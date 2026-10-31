using System.Collections.Generic;
using UnityEngine;

namespace BetterFG.Customization.UI.Styles
{
    public sealed class TabArt
    {
        public string Icon;
        public string TitleKey;
        public Vector2 IconOffset;
    }

    public sealed class TopBarSpec
    {
        public string Layout;
        public string Carousel;
        public string[] TabTemplates;
        public string[] TemplateTabs;
        public string TabIcon;
        public float TabWidth;
        public float TabGap;
        public float ForeignIconSize;
        public Vector2 ForeignIconOffset;
        public string Title;
        public string Crowns;
        public string Kudos;
        public string PrevGlyph;
        public string NextGlyph;
        public TabArt Settings;
        public Dictionary<string, TabArt> Tabs = new Dictionary<string, TabArt>();
    }

    public sealed class LobbySpec
    {
        public string Layout;
        public string Root;
        public string[] Disable = new string[0];
        public string Play;
        public string Invite;
        public string ShowSelect;
        public string ShowSelectParent;
        public Vector2? InvitePosition;
        public Vector2 InviteAnchor;
        public float InviteSpacing;
        public string SeasonLevel;
        public string SeasonCounter;
        public string SeasonFill;
        public string SeasonDone;
        public string NameplateBacking;
        public string Name;
        public string Nickname;
        public float NameY;
        public float NicknameY;
    }
}
