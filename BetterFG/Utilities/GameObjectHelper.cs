using UnityEngine;
namespace BetterFG.Utilities
{
    public static class GameObjectHelper
    {
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

        public static void SetLayerRecursively(GameObject obj, int layer)
        {
            if (obj == null) return;
            obj.layer = layer;
            for (int i = 0; i < obj.transform.childCount; i++)
                SetLayerRecursively(obj.transform.GetChild(i).gameObject, layer);
        }
    }
}