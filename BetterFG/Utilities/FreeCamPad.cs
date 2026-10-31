using BetterFG.Core;
using UnityEngine;

namespace BetterFG.Utilities
{
    internal static class FreeCamPad
    {
        internal static readonly string[] ToggleNames = { "Y", "Triangle" };
        static readonly string[] MoveXNames = { "Left Stick X", "Left Stick Horizontal" };
        static readonly string[] MoveYNames = { "Left Stick Y", "Left Stick Vertical" };
        static readonly string[] LookXNames = { "Right Stick X", "Right Stick Horizontal" };
        static readonly string[] LookYNames = { "Right Stick Y", "Right Stick Vertical" };
        internal static readonly string[] BoostNames = { "A", "Cross" };
        internal static readonly string[] ZoomOutNames = { "Left Shoulder", "LB", "L1" };
        internal static readonly string[] ZoomInNames = { "Right Shoulder", "RB", "R1" };
        internal static readonly string[] ZoomHoldNames = { "Left Stick Button", "Left Stick Click", "Left Stick", "L3", "LS" };

        const float Deadzone = 0.2f;
        const float LookSpeed = 110f;
        const float ZoomSpeed = 35f;

        static Vector2 Stick(string[] x, string[] y)
        {
            float vx = NavPromptCore.AxisByName(x), vy = NavPromptCore.AxisByName(y);
            return Vector2.ClampMagnitude(new Vector2(Mathf.Abs(vx) > Deadzone ? vx : 0f, Mathf.Abs(vy) > Deadzone ? vy : 0f), 1f);
        }

        internal static Vector2 Move => Stick(MoveXNames, MoveYNames);
        internal static Vector2 Look => Stick(LookXNames, LookYNames);

        internal static bool Fly(Camera cam, ref float yaw, ref float pitch, float speed, float boostMul, float minFov, float maxFov)
        {
            var move = Move;
            var look = Look;
            float zoom = (NavPromptCore.ElementHeldByName(ZoomOutNames) ? 1f : 0f) - (NavPromptCore.ElementHeldByName(ZoomInNames) ? 1f : 0f);
            if (NavPromptCore.ElementHeldByName(ZoomHoldNames))
            {
                zoom -= look.y;
                look = Vector2.zero;
            }
            if (move == Vector2.zero && look == Vector2.zero && zoom == 0f) return false;

            float dt = Time.unscaledDeltaTime;
            var t = cam.transform;
            yaw += look.x * LookSpeed * dt;
            pitch = Mathf.Clamp(pitch - look.y * LookSpeed * dt, -89f, 89f);
            t.rotation = Quaternion.Euler(pitch, yaw, 0f);

            if (zoom != 0f) cam.fieldOfView = Mathf.Clamp(cam.fieldOfView + zoom * ZoomSpeed * dt, minFov, maxFov);

            if (move != Vector2.zero)
            {
                float mul = NavPromptCore.ElementHeldByName(BoostNames) ? boostMul : 1f;
                t.position += t.rotation * new Vector3(move.x, 0f, move.y) * speed * mul * dt;
            }
            return true;
        }
    }
}
