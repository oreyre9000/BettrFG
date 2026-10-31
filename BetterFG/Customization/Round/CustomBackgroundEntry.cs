using UnityEngine;

namespace BetterFG.Customization.Round
{
    public readonly struct BackgroundTransform
    {
        public readonly Vector3 Position;
        public readonly Vector3 Rotation;
        public readonly Vector3 Scale;

        public BackgroundTransform(Vector3 position, Vector3 rotation, Vector3 scale)
        {
            Position = position;
            Rotation = rotation;
            Scale = scale;
        }

        public BackgroundTransform(Vector3 position, Vector3 rotation) : this(position, rotation, Vector3.one) { }

        public void ApplyTo(Transform t)
        {
            t.position = Position;
            t.eulerAngles = Rotation;
            t.localScale = Scale;
        }
    }

    public sealed class CustomBackgroundEntry
    {
        public string Package;
        public string Label;
        public string Bundle;
        public string[] Hide;
        public bool NoShadows;
        public BackgroundTransform? Round;
        public BackgroundTransform? Creative;
        public BackgroundTransform? Menu;

        public static readonly CustomBackgroundEntry[] All =
        {
            new CustomBackgroundEntry
            {
                Package = "SpaceRace",
                Label = "round_bg.round.SpaceRace",
                Menu = new BackgroundTransform(new Vector3(0f, 0f, 100f), new Vector3(2.7273f, 67.5617f, 6.7636f)),
            },
            new CustomBackgroundEntry
            {
                Package = "HexATerrestrial",
                Label = "round_bg.round.HexATerrestrial",
                NoShadows = true,
            },
            new CustomBackgroundEntry
            {
                Package = "SpeedSlider",
                Label = "round_bg.round.SpeedSlider",
                Menu = new BackgroundTransform(new Vector3(0f, 146.0226f, 249.3813f), Vector3.zero),
            },
            new CustomBackgroundEntry
            {
                Package = "PuzzlePath",
                Label = "round_bg.round.PuzzlePath",
                Menu = new BackgroundTransform(new Vector3(-16.7007f, -11.9273f, 146.3182f), new Vector3(351.3088f, 0f, 0f)),
            },
            new CustomBackgroundEntry
            {
                Package = "StompinGround",
                Label = "round_bg.round.StompinGround",
                Bundle = "terrain_s5",
                Hide = new[] { "terrain/ENV_Terrain_NonStatic_S04/GroundPlane" },
                Creative = new BackgroundTransform(new Vector3(0f, -62.5684f, 0f), Vector3.zero, Vector3.one * 1.3f),
                Menu = new BackgroundTransform(new Vector3(0f, 22.2182f, 100f), new Vector3(351.2575f, 0f, 0f)),
            },
            new CustomBackgroundEntry
            {
                Package = "TreatThieves",
                Label = "round_bg.round.TreatThieves",
                Bundle = "terrain_ss",
                Creative = new BackgroundTransform(new Vector3(0f, -116.7274f, 0f), Vector3.zero),
                Menu = new BackgroundTransform(new Vector3(0f, 11.2f, 200f), Vector3.zero),
            },
            new CustomBackgroundEntry
            {
                Package = "TrackAttack",
                Label = "round_bg.round.TrackAttack",
                Menu = new BackgroundTransform(new Vector3(-27.1635f, -139.2985f, 491.3091f), new Vector3(349.7692f, 0f, 0f)),
            },
            new CustomBackgroundEntry
            {
                Package = "HoverboardHeroes",
                Label = "round_bg.round.HoverboardHeroes",
                Bundle = "terrain_s4",
                Round = new BackgroundTransform(new Vector3(0f, -0.9946f, -20.5f), new Vector3(0f, 46.3533f, 0f)),
                Creative = new BackgroundTransform(new Vector3(0f, -25.7582f, -20.5f), new Vector3(0f, 46.3533f, 0f), Vector3.one * 1.2f),
                Menu = new BackgroundTransform(new Vector3(-71.7927f, 31.4203f, 151.9909f), new Vector3(359.7815f, 76.0627f, 0f)),
            },
            new CustomBackgroundEntry
            {
                Package = "SnowyScrap",
                Label = "round_bg.round.SnowyScrap",
                Bundle = "terrain_s3",
                Round = new BackgroundTransform(new Vector3(8f, -123.721f, -52f), Vector3.zero),
                Creative = new BackgroundTransform(new Vector3(8f, 25.1f, -52f), Vector3.zero),
                Menu = new BackgroundTransform(new Vector3(8f, -14.4513f, 117.7587f), Vector3.zero),
            },
            new CustomBackgroundEntry
            {
                Package = "ThinIce",
                Label = "round_bg.round.ThinIce",
                Creative = new BackgroundTransform(new Vector3(8f, -62.5748f, -52f), Vector3.zero),
                Menu = new BackgroundTransform(new Vector3(8f, -14.4513f, 117.7587f), Vector3.zero),
            },
            new CustomBackgroundEntry
            {
                Package = "BeanHillZone",
                Label = "round_bg.round.BeanHillZone",
                Creative = new BackgroundTransform(new Vector3(17f, -44.1093f, -47f), Vector3.zero, Vector3.one * 1.2f),
                Menu = new BackgroundTransform(new Vector3(-74.482f, 22.0546f, 321.6801f), Vector3.zero),
            },
            new CustomBackgroundEntry
            {
                Package = "HexAGone",
                Label = "round_bg.round.HexAGone",
                Round = new BackgroundTransform(new Vector3(0f, -20.478f, 0f), Vector3.zero),
                Creative = new BackgroundTransform(new Vector3(0f, -56.6454f, 0f), Vector3.zero),
                Menu = new BackgroundTransform(new Vector3(0f, -7.448f, 234.8359f), Vector3.zero),
            },
            new CustomBackgroundEntry
            {
                Package = "TreetopTumble",
                Label = "round_bg.round.TreetopTumble",
                Creative = new BackgroundTransform(new Vector3(0f, 60.6539f, 0f), Vector3.zero),
                Menu = new BackgroundTransform(new Vector3(33.8439f, 89.9091f, 115.4909f), new Vector3(351.8499f, 332.6321f, 4.8566f)),
            },
            new CustomBackgroundEntry
            {
                Package = "TundraRun",
                Label = "round_bg.round.TundraRun",
                Creative = new BackgroundTransform(new Vector3(0f, -47.7277f, 0f), Vector3.zero),
            },
            new CustomBackgroundEntry
            {
                Package = "SweetThieves",
                Label = "round_bg.round.SweetThieves",
                Creative = new BackgroundTransform(new Vector3(0f, -53.3314f, 0f), Vector3.zero),
            },
            new CustomBackgroundEntry
            {
                Package = "BigFans",
                Label = "round_bg.round.BigFans",
                Round = new BackgroundTransform(new Vector3(0f, -7.0636f, 0f), Vector3.zero),
                Creative = new BackgroundTransform(new Vector3(0f, -47.7277f, 0f), Vector3.zero),
                Menu = new BackgroundTransform(new Vector3(0f, 14.4599f, 0f), Vector3.zero),
            },
        };

        public static CustomBackgroundEntry ByPackage(string package)
        {
            foreach (var e in All) if (e.Package == package) return e;
            return null;
        }

        public static CustomBackgroundEntry ByKey(string key)
        {
            if (key.StartsWith("round:")) return ByPackage(key.Substring(6));
            foreach (var e in All) if (e.Bundle == key) return e;
            return null;
        }

        public BackgroundTransform? For(RoundBackgroundService.Context context) =>
            context == RoundBackgroundService.Context.Menu ? Menu : context == RoundBackgroundService.Context.Creative ? Creative : Round;
    }
}
