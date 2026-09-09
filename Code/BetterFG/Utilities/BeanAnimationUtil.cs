using System;
using BetterFG.Services;
using UnityEngine;

namespace BetterFG.Utilities
{
    public static class BeanAnimationUtil
    {
        static bool _paramsCached;
        static int _grounded, _slope, _zVel, _xVel, _yVel, _airY, _moving;
        static float _velMod, _castRadius;
        static LayerMask _groundMask;

        public static int MovingParam
        {
            get
            {
                if (!_paramsCached) CacheParams();
                return _moving;
            }
        }

        static void CacheParams()
        {
            _grounded = FallGuysCharacterController.groundedParam;
            _slope = FallGuysCharacterController.slopeAngleParam;
            _zVel = FallGuysCharacterController.zVelParam;
            _xVel = FallGuysCharacterController.xVelParam;
            _yVel = FallGuysCharacterController.yVelParam;
            _airY = FallGuysCharacterController.airOnlyYVelParam;
            _moving = FG.Common.Character.MotorFunctionMovementStateMove.movingParam;
            _velMod = FallGuysCharacterController.AnimationVelocityParamModifier;
            _castRadius = FallGuysCharacterController.GroundCheckSphereCastRadius;
            _groundMask = FallGuysCharacterController.groundMask;
            _paramsCached = true;
        }

        public static Animator FindAnimator(GameObject bean)
        {
            if (bean == null) return null;
            var wrapper = bean.transform.Find(PlayerScaleService.WRAPPER_NAME);
            var character = (wrapper != null ? wrapper : bean.transform).Find("Character");
            return character?.GetComponent<Animator>();
        }

        public static bool CheckGrounded(Transform bean, out float slopeAngle)
        {
            if (!_paramsCached) CacheParams();
            float r = _castRadius;
            bool grounded = Physics.SphereCast(bean.position + Vector3.up * (r + 0.1f),
                r, Vector3.down, out var hit, 0.4f,
                _groundMask, QueryTriggerInteraction.Ignore);
            slopeAngle = grounded ? Vector3.Angle(hit.normal, Vector3.up) : 0f;
            return grounded;
        }

        public static void DriveLocomotion(Animator anim, Transform bean, Vector3 v)
        {
            if (anim is null || bean is null || anim.m_CachedPtr == IntPtr.Zero || bean.m_CachedPtr == IntPtr.Zero) return;
            bool grounded = CheckGrounded(bean, out float slopeAngle);
            DriveLocomotion(anim, bean, v, grounded, slopeAngle);
        }

        // grounded/slope passed in directly — for callers throttling their own CheckGrounded calls
        // instead of paying a SphereCast every frame.
        // replay drives one of these per bean per frame, so the destroyed-check is m_CachedPtr —
        // Object.op_Equality is a boxed il2cpp invoke and there are two of them here.
        public static void DriveLocomotion(Animator anim, Transform bean, Vector3 v, bool grounded, float slopeAngle = 0f)
        {
            if (anim is null || bean is null || anim.m_CachedPtr == IntPtr.Zero || bean.m_CachedPtr == IntPtr.Zero) return;
            if (!_paramsCached) CacheParams();

            anim.SetBool(_grounded, grounded);
            anim.SetFloat(_slope, slopeAngle);

            Vector3 local = Quaternion.Inverse(bean.rotation) * new Vector3(v.x, 0f, v.z);
            float mod = _velMod;
            anim.SetFloat(_zVel, new Vector2(v.x, v.z).magnitude * mod);
            anim.SetFloat(_xVel, local.x * mod);
            anim.SetFloat(_yVel, v.y * mod);
            anim.SetFloat(_airY, grounded ? 0f : v.y * mod);
        }
    }
}
