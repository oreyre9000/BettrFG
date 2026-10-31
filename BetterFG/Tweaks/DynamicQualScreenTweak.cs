using System;
using System.Collections;
using FG.Common;
using FGClient;
using UnityEngine;
using UnityEngine.SceneManagement;
using BetterFG.Services;
using BepInEx.Unity.IL2CPP.Utils.Collections;

namespace BetterFG.Tweaks
{
    public class DynamicQualScreenTweak : BfgTweak
    {
        public DynamicQualScreenTweak(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "dynamic_qual_screen";
        public override string TweakLabel => "tweak.dynamic_qual_screen";
        public override bool DefaultEnabled => false;

        public static DynamicQualScreenTweak Instance { get; private set; }
        void Awake() => Instance = this;

        internal static void OnLocalPlayerDisplayed(CellBehaviour cell)
        {
            var inst = Instance;
            if (inst == null) { Plugin.Log.LogWarning("dynamic qual screen: no Instance, tweak MonoBehaviour never woke up"); return; }
            if (!inst.IsEnabled) { Plugin.Log.LogInfo("dynamic qual screen: displayed but tweak is off, skipping"); return; }
            Plugin.Log.LogInfo("dynamic qual screen: local player displayed, starting camera sequence");
            inst.StartCoroutine(RunQualCameraSequence(cell).WrapToIl2Cpp());
        }

        private static IEnumerator RunQualCameraSequence(CellBehaviour cell)
        {
            var camerasRoot = FindQualCamerasRoot();
            if (camerasRoot == null) { Plugin.Log.LogWarning("dynamic qual screen: no CAMERAS root found in any loaded scene, bailing"); yield break; }
            Plugin.Log.LogInfo($"dynamic qual screen: camerasRoot={camerasRoot.name}, {camerasRoot.childCount} children");

            Camera mainCam = null;
            for (int i = 0; i < camerasRoot.childCount; i++)
            {
                var child = camerasRoot.GetChild(i);
                Plugin.Log.LogInfo($"dynamic qual screen: child[{i}]={child.name}");
                if (child.name == "Main Camera")
                {
                    child.gameObject.SetActive(true);
                    mainCam = child.GetComponent<Camera>();
                }
                else
                {
                    child.gameObject.SetActive(false);
                }
            }

            if (mainCam == null) { Plugin.Log.LogWarning("dynamic qual screen: no 'Main Camera' child under camerasRoot, bailing"); yield break; }

            var cellPos = cell.transform.position;
            var targetPos = new Vector3(cellPos.x, cellPos.y, cellPos.z - 10f);
            var originalPos = mainCam.transform.position;
            var originalRot = mainCam.transform.rotation;

            mainCam.transform.position = targetPos;
            mainCam.transform.LookAt(cellPos);

            yield return new WaitForSeconds(1f);

            float elapsed = 0f;
            var startPos = mainCam.transform.position;
            var startRot = mainCam.transform.rotation;
            while (elapsed < 2f)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = 1f - Mathf.Pow(1f - Mathf.Clamp01(elapsed / 2f), 3f);
                mainCam.transform.position = Vector3.Lerp(startPos, originalPos, t);
                mainCam.transform.rotation = Quaternion.Slerp(startRot, originalRot, t);
                yield return null;
            }

            mainCam.transform.position = originalPos;
            mainCam.transform.rotation = originalRot;

            for (int i = 0; i < camerasRoot.childCount; i++)
                camerasRoot.GetChild(i).gameObject.SetActive(true);
        }

        // FallGuysLib's CameraLocator.GetCamerasRoot() now derives from the round's CameraDirector,
        // which it caches statically forever and which doesn't exist on the qual screen's own camera
        // rig - scan loaded scenes for the "CAMERAS" root directly instead, like FGL used to. that
        // root used to hold a nested LevelCameras_/Cameras_ child; live dump on the qual screen shows
        // "Main Camera" sitting straight under "----------------CAMERAS" now, no nested layer, so
        // fall back to the root itself when it holds "Main Camera" directly.
        private static Transform FindQualCamerasRoot()
        {
            int sceneCount = SceneManager.sceneCount;
            for (int i = 0; i < sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;

                foreach (var root in scene.GetRootGameObjects())
                {
                    if (root == null || root.name.IndexOf("CAMERAS", StringComparison.Ordinal) < 0) continue;

                    var t = root.transform;
                    for (int j = 0; j < t.childCount; j++)
                    {
                        var child = t.GetChild(j);
                        if (child.name.StartsWith("LevelCameras_", StringComparison.Ordinal) || child.name.StartsWith("Cameras_", StringComparison.Ordinal))
                            return child;
                        if (child.name == "Main Camera")
                            return t;
                    }
                }
            }
            return null;
        }
    }
}
