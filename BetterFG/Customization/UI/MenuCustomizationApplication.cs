using System;
using System.Collections;
using System.Collections.Generic;
using Cinemachine;
using UnityEngine;
using UnityEngine.Rendering;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Core;
using BetterFG.Services;
using BetterFG.Customization.Player;
using System.Numerics;
using Vector3 = UnityEngine.Vector3;
using Quaternion = UnityEngine.Quaternion;
using FGClient;

namespace BetterFG.Customization.UI
{
    public partial class MenuCustomizationApplication : MonoBehaviour
    {
        public static MenuCustomizationApplication Instance { get; private set; }

        public event Action<string> OnStatus;

        // set true on main menu enter, consumed (set false) once fg is applied on ShowMainMenu
        public bool _pendingFgReapply = true;

        // true = next ShowMainMenu reapply hits full UICanvas_Client_V2(Clone), then flips false
        public static bool _fullCanvasReapplyPending = false;

        void Awake() { Instance = this; MigrateOldBgKeys(); MigrateOldBgImageEntry(); }

        private static MainMenuManager _menuManager;

        public static void CacheMenuManager(MainMenuManager mgr) => _menuManager = mgr;

        public static bool InMenuScene
        {
            get
            {
                if (_menuManager == null)
                {
                    var go = GameObject.Find("MainMenuManager");
                    if (go == null) return false;
                    _menuManager = go.GetComponent<MainMenuManager>();
                    if (_menuManager == null) return false;
                }
                return _menuManager.gameObject.activeInHierarchy;
            }
        }

        public static MainMenuManager MenuManager => InMenuScene ? _menuManager : null;

        public static FG.Common.UI.SwitchableView MainMenuSwitchableView =>
            !InMenuScene ? null : _menuManager.MainMenuBuilder?.SwitchableView;

        private static float ParseF(string key, float def) =>
            float.TryParse(SettingsService.Get(key, ""), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : def;
    }
}
