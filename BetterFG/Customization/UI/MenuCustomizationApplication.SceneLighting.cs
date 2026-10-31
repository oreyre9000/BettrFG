using UnityEngine;
using UnityEngine.Rendering;
using BetterFG.Services;
using Quaternion = UnityEngine.Quaternion;

namespace BetterFG.Customization.UI
{
    public partial class MenuCustomizationApplication
    {
        // ── Ambient light (flat) ──────────────────────────────────────────────

        public const string KEY_AMBIENT_ON = "menu.ambient.on";
        public const string KEY_AMBIENT_R = "menu.ambient.r";
        public const string KEY_AMBIENT_G = "menu.ambient.g";
        public const string KEY_AMBIENT_B = "menu.ambient.b";

        private bool _ambientSaved;
        private AmbientMode _ambientOldMode;
        private Color _ambientOldLight;

        public void SetAmbientEnabled(bool enabled)
        {
            SettingsService.Set(KEY_AMBIENT_ON, enabled ? "true" : "false");
            if (enabled)
                ApplyAmbient(new Color(ParseF(KEY_AMBIENT_R, 0.5f), ParseF(KEY_AMBIENT_G, 0.5f), ParseF(KEY_AMBIENT_B, 0.5f)));
            else
                RevertAmbient();
        }

        public void ApplyAmbient(Color color)
        {
            if (!_ambientSaved)
            {
                _ambientOldMode = RenderSettings.ambientMode;
                _ambientOldLight = RenderSettings.ambientLight;
                _ambientSaved = true;
            }
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = color;
        }

        public void RevertAmbient()
        {
            if (!_ambientSaved) return;
            RenderSettings.ambientMode = _ambientOldMode;
            RenderSettings.ambientLight = _ambientOldLight;
            _ambientSaved = false;
        }

        public void ApplyAmbientFromSettings()
        {
            if (SettingsService.Get(KEY_AMBIENT_ON, "false") != "true") return;
            if (!InMenuScene) return;
            ApplyAmbient(new Color(ParseF(KEY_AMBIENT_R, 0.5f), ParseF(KEY_AMBIENT_G, 0.5f), ParseF(KEY_AMBIENT_B, 0.5f)));
        }

        // ── Main sun rotation ─────────────────────────────────────────────────

        private const string SUN_LIGHT_PATH = "3D Environment/SUN Light";
        public const string KEY_SUN_ON = "menu.sun.on";
        public const string KEY_SUN_ROT_X = "menu.sun.rot.x";
        public const string KEY_SUN_ROT_Y = "menu.sun.rot.y";
        public const string KEY_SUN_ROT_Z = "menu.sun.rot.z";

        private bool _sunSaved;
        private Quaternion _sunOldRot;

        private Transform FindSun()
        {
            var go = GameObject.Find(SUN_LIGHT_PATH);
            return go != null ? go.transform : null;
        }

        public void SetSunEnabled(bool enabled)
        {
            SettingsService.Set(KEY_SUN_ON, enabled ? "true" : "false");
            if (enabled)
                ApplySunRotation(ParseF(KEY_SUN_ROT_X, 50f), ParseF(KEY_SUN_ROT_Y, 0f), ParseF(KEY_SUN_ROT_Z, 0f));
            else
                RevertSun();
        }

        public void ApplySunRotation(float x, float y, float z)
        {
            var sun = FindSun();
            if (sun == null) return;
            if (!_sunSaved)
            {
                _sunOldRot = sun.localRotation;
                _sunSaved = true;
            }
            sun.localRotation = Quaternion.Euler(x, y, z);
        }

        public void RevertSun()
        {
            if (!_sunSaved) return;
            var sun = FindSun();
            if (sun != null) sun.localRotation = _sunOldRot;
            _sunSaved = false;
        }

        public void ApplySunFromSettings()
        {
            if (SettingsService.Get(KEY_SUN_ON, "false") != "true") return;
            if (!InMenuScene) return;
            ApplySunRotation(ParseF(KEY_SUN_ROT_X, 50f), ParseF(KEY_SUN_ROT_Y, 0f), ParseF(KEY_SUN_ROT_Z, 0f));
        }

        // ── Plinth colour ─────────────────────────────────────────────────────

        public const string KEY_PLINTH_COL_ON = "menu.plinth.col.on";
        public const string KEY_PLINTH_COL_R = "menu.plinth.col.r";
        public const string KEY_PLINTH_COL_G = "menu.plinth.col.g";
        public const string KEY_PLINTH_COL_B = "menu.plinth.col.b";
        private bool _plinthColSaved;
        private Color _plinthColOld;

        private Renderer FindPlinthRenderer()
        {
            var go = GameObject.Find(PLINTH_MESH_PATH);
            return go != null ? go.GetComponent<Renderer>() : null;
        }

        public void SetPlinthColorEnabled(bool enabled)
        {
            SettingsService.Set(KEY_PLINTH_COL_ON, enabled ? "true" : "false");
            if (enabled)
                ApplyPlinthColor(new Color(ParseF(KEY_PLINTH_COL_R, 1f), ParseF(KEY_PLINTH_COL_G, 1f), ParseF(KEY_PLINTH_COL_B, 1f)));
            else
                RevertPlinthColor();
        }

        public void ApplyPlinthColor(Color color)
        {
            var rend = FindPlinthRenderer();
            if (rend == null || rend.material == null) return;
            if (!_plinthColSaved)
            {
                _plinthColOld = rend.material.color;
                _plinthColSaved = true;
            }
            rend.material.color = new Color(color.r, color.g, color.b, rend.material.color.a);
        }

        public void RevertPlinthColor()
        {
            if (!_plinthColSaved) return;
            var rend = FindPlinthRenderer();
            if (rend != null && rend.material != null)
                rend.material.color = _plinthColOld;
            _plinthColSaved = false;
        }

        public void ApplyPlinthColorFromSettings()
        {
            if (GameObject.Find("MainMenuManager") == null) return;
            if (SettingsService.Get(KEY_PLINTH_COL_ON, "false") != "true") return;
            ApplyPlinthColor(new Color(ParseF(KEY_PLINTH_COL_R, 1f), ParseF(KEY_PLINTH_COL_G, 1f), ParseF(KEY_PLINTH_COL_B, 1f)));
        }
    }
}
