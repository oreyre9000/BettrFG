using Il2CppInterop.Runtime;
using UnityEngine;
namespace BetterFG.Utilities
{
    public static class GameObjectHelper
    {
        public static void RelabelGameLabel(Transform root, string path, string idOrText)
        {
            var t = root.Find(path);
            if (t == null) return;
            var loc = t.gameObject.GetComponent("LocalisedStaticLabel")?.TryCast<Behaviour>();
            if (loc != null) loc.enabled = false;
            var bind = t.gameObject.GetComponent("TMPTextBinding")?.TryCast<Behaviour>();
            if (bind != null) bind.enabled = false;
            BettrFG.uGUI.UGUIShip.RelabelText(t.GetComponent<TMPro.TextMeshProUGUI>(), idOrText);
        }

        public static bool IsLobbyCharacter(GameObject bean)
        {
            if (bean == null) return false;
            return bean.name == "LobbyCharacter";
        }
        public static bool IsUICharacter(GameObject bean)
        {
            if (bean == null) return false;
            return bean.name == "PB_UI_Character";
        }
        public static bool IsMainMenuUp() =>
            GameObject.Find("MainMenuManager")?.GetComponent<FGClient.MainMenuManager>() != null;

        public static string GetGameObjectPath(GameObject obj)
        {
            if (obj == null) return "";
            string path = obj.name;
            Transform cur = obj.transform.parent;
            while (cur != null) { path = cur.name + "/" + path; cur = cur.parent; }
            return path;
        }
        public static Transform FindChildStartingWith(Transform parent, string prefix)
        {
            if (parent == null) return null;
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (child != null && child.name.StartsWith(prefix)) return child;
            }
            return null;
        }
        public static Transform FindBoneOnBean(GameObject bean, string boneName)
        {
            if (bean == null || string.IsNullOrEmpty(boneName)) return null;

            Transform hit = null;
            foreach (var smr in bean.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var bones = smr.bones;
                for (int i = 0; bones != null && i < bones.Length && hit == null; i++)
                    if (bones[i] != null && bones[i].name == boneName) hit = bones[i];
                if (hit != null) break;
            }

            if (hit == null)
                foreach (var t in bean.GetComponentsInChildren<Transform>(true))
                    if (t.name == boneName) { hit = t; break; }

            var rig = BetterFG.Services.BeanVisualRig.Get(bean);
            return rig != null ? rig.RealBone(hit) : hit;
        }

        public static int StripPhysics(GameObject clone)
        {
            if (clone == null) return 0;
            int killed = 0;
            foreach (var j in clone.GetComponentsInChildren<Joint>(true))
                if (j != null) { UnityEngine.Object.DestroyImmediate(j); killed++; }
            foreach (var rb in clone.GetComponentsInChildren<Rigidbody>(true))
                if (rb != null) { UnityEngine.Object.DestroyImmediate(rb); killed++; }
            foreach (var c in clone.GetComponentsInChildren<Collider>(true))
                if (c != null) { UnityEngine.Object.DestroyImmediate(c); killed++; }
            return killed;
        }

        // PB_FallGuyBot is a real bot prefab kept loaded (inactive) the whole session - the one
        // resident bean guaranteed to exist even with no live round and no local player bean built
        // yet, so previews can clone off it instead of needing a live player to show anything.
        public static GameObject FindDefaultBotBean()
        {
            foreach (var fgcc in Resources.FindObjectsOfTypeAll<global::FallGuysCharacterController>())
                if (fgcc != null && fgcc.gameObject.name == "PB_FallGuyBot") return fgcc.gameObject;
            return null;
        }

        public static void SetLayerRecursively(GameObject obj, int layer)
        {
            if (obj == null) return;
            obj.layer = layer;
            for (int i = 0; i < obj.transform.childCount; i++)
                SetLayerRecursively(obj.transform.GetChild(i).gameObject, layer);
        }
    }
}