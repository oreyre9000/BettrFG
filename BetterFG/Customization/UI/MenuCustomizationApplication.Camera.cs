using System;
using System.Collections;
using Cinemachine;
using UnityEngine;
using BetterFG.Services;
using Vector3 = UnityEngine.Vector3;
using Quaternion = UnityEngine.Quaternion;
using FGClient;

namespace BetterFG.Customization.UI
{
    public partial class MenuCustomizationApplication
    {
        // camera
        // fallback base only — the real base is the vcam's untouched localPosition, cached once
        // in the OnMainMenuEntered postfix (content updates move the cam, so a hardcoded base
        // de-centers the bean). never re-cached anywhere else.
        private static readonly Vector3 CAM_BASE_POS = new Vector3(0f, 3.43f, -5.2f);
        private Vector3 _camBasePos = CAM_BASE_POS;
        private bool _camBaseCached;
        private CinemachineVirtualCamera _vcam;
        private Vector3 _camOffset;
        private float _camFov = 40f;
        private Vector3 _camLookAtOffset;

        public const string KEY_CAM_ENABLED = "menu.cam.enabled";
        public const string KEY_CAM_FOV = "menu.cam.fov";
        public const string KEY_CAM_X = "menu.cam.x";
        public const string KEY_CAM_Y = "menu.cam.y";
        public const string KEY_CAM_Z = "menu.cam.z";
        public const string KEY_CAM_LOOKAT_X = "menu.cam.lookat.x";
        public const string KEY_CAM_LOOKAT_Y = "menu.cam.lookat.y";
        public const string KEY_CAM_LOOKAT_Z = "menu.cam.lookat.z";

        private static readonly Vector3 LOOKAT_BASE = new Vector3(0f, 2.44f, 0f);

        public static IEnumerator AutoApplyCamFromSettingsCoroutine()
        {
            yield return null;
            yield return new WaitForSeconds(0.1f);
            AutoApplyCamFromSettings();
        }

        private void EnsureVcam()
        {
            // the game rebuilds the lobby vcam per menu entry, so a cached ref goes dead. an il2cpp
            // object whose native side is gone compares != null but throws/no-ops on use, so re-fetch
            // the live one whenever we can find a MainMenuManager rather than trusting the cache.
            var mm = FindObjectOfType<MainMenuManager>();
            if (mm != null && mm._lobbyVirtualCam != null) _vcam = mm._lobbyVirtualCam;
        }

        // called from the OnMainMenuEntered postfix. the game rebuilds the lobby vcam on every menu
        // entry, so always adopt the live one — holding a stale ref means ApplyCam writes to a dead
        // cam while the real one sits at base (the "camera resets on re-entry" bug).
        // base is snapshotted once, straight off the live transform. we never touch the transform
        // before this caches (ApplyCam bails until _camBaseCached), so the live pos IS the pristine
        // base — don't subtract _camOffset, that folds the saved offset into the base and cancels it.
        public void CacheCamBase(CinemachineVirtualCamera vcam)
        {
            if (vcam == null) return;
            _vcam = vcam;
            if (_camBaseCached) return;
            _camBasePos = vcam.gameObject.transform.localPosition;
            _camBaseCached = true;
        }

        public void ApplyCam(Vector3 offset, float fov, Vector3 lookAtOffset = default)
        {
            _camOffset = offset;
            _camFov = fov;
            _camLookAtOffset = lookAtOffset;

            // don't touch the cam until OnMainMenuEntered has cached the real base, otherwise we'd
            // offset from the stale hardcoded fallback and the bean sits off-centre. the postfix
            // re-applies once the base is in, so skipping here is safe.
            if (!_camBaseCached) return;

            EnsureVcam();
            if (_vcam == null) return;

            _vcam.gameObject.transform.localPosition = _camBasePos + _camOffset;
            var lens = _vcam.m_Lens;
            lens.FieldOfView = _camFov;
            _vcam.m_Lens = lens;

            if (_vcam.LookAt != null)
                _vcam.LookAt.localPosition = LOOKAT_BASE + _camLookAtOffset;
        }

        public void ResetCam()
        {
            _camOffset = Vector3.zero;
            _camFov = 40f;
            _camLookAtOffset = Vector3.zero;

            EnsureVcam();
            if (_vcam == null) return;

            _vcam.gameObject.transform.localPosition = _camBasePos;
            var lens = _vcam.m_Lens;
            lens.FieldOfView = 40f;
            _vcam.m_Lens = lens;

            if (_vcam.LookAt != null)
                _vcam.LookAt.localPosition = LOOKAT_BASE;
        }

        public static void AutoApplyCamFromSettings()
        {
            // off by default — the game moves the lobby cam around on content updates, so we don't
            // touch it unless the user explicitly turns on custom camera position.
            if (SettingsService.Get(KEY_CAM_ENABLED, "false") != "true") return;

            var ci = System.Globalization.CultureInfo.InvariantCulture;
            float P(string key, float def) =>
                float.TryParse(SettingsService.Get(key, ""), System.Globalization.NumberStyles.Float, ci, out float v) ? v : def;

            bool hasCam = SettingsService.Get(KEY_CAM_FOV, "") != "" || SettingsService.Get(KEY_CAM_X, "") != "";
            bool hasLookAt = SettingsService.Get(KEY_CAM_LOOKAT_X, "") != ""
                          || SettingsService.Get(KEY_CAM_LOOKAT_Y, "") != ""
                          || SettingsService.Get(KEY_CAM_LOOKAT_Z, "") != "";

            if (!hasCam && !hasLookAt) return;

            Instance?.ApplyCam(
                new Vector3(P(KEY_CAM_X, 0f), P(KEY_CAM_Y, 0f), P(KEY_CAM_Z, 0f)),
                P(KEY_CAM_FOV, 40f),
                new Vector3(P(KEY_CAM_LOOKAT_X, 0f), P(KEY_CAM_LOOKAT_Y, 0f), P(KEY_CAM_LOOKAT_Z, 0f))
            );
        }
    }
}
