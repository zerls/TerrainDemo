using System;
using UnityEngine;

namespace WindSystem
{
    public enum MotorType
    {
        Directional,
        Omni,
        Vortex,
        Moving,
        Cylinder
    }

    [System.Serializable]
    public struct MotorDirectional
    {
        public Vector3 position;
        public float radiusSq;
        public Vector3 force;
    }

    [System.Serializable]
    public struct MotorOmni
    {
        public Vector3 position;
        public float radiusSq;
        public float force;
    }

    [System.Serializable]
    public struct MotorVortex
    {
        public Vector3 position;
        public Vector3 axis;
        public float radiusSq;
        public float force;
    }

    [System.Serializable]
    public struct MotorMoving
    {
        public Vector3 prePosition;
        public float moveLen;
        public Vector3 moveDir;
        public float radiusSq;
        public float force;
    }

    [System.Serializable]
    public struct MotorCylinder
    {
        public Vector3 position;
        public Vector3 axis;
        public float height;
        public float radiusBottonSq;
        public float radiusTopSq;
        public float force;
    }

    public class WindMotor : MonoBehaviour
    {
        public MotorType MotorType;
        
        [HideInInspector] public MotorDirectional motorDirectional;
        [HideInInspector] public MotorOmni motorOmni;
        [HideInInspector] public MotorVortex motorVortex;
        [HideInInspector] public MotorMoving motorMoving;
        [HideInInspector] public MotorCylinder motorCylinder;

        private static readonly MotorDirectional emptyMotorDirectional = new MotorDirectional();
        private static readonly MotorOmni emptyMotorOmni = new MotorOmni();
        private static readonly MotorVortex emptyMotorVortex = new MotorVortex();
        private static readonly MotorMoving emptyMotorMoving = new MotorMoving();
        private static readonly MotorCylinder emptyMotorCylinder = new MotorCylinder();

        public static MotorDirectional GetEmptyMotorDirectional() => emptyMotorDirectional;
        public static MotorOmni GetEmptyMotorOmni() => emptyMotorOmni;
        public static MotorVortex GetEmptyMotorVortex() => emptyMotorVortex;
        public static MotorMoving GetEmptyMotorMoving() => emptyMotorMoving;
        public static MotorCylinder GetEmptyMotorCylinder() => emptyMotorCylinder;

        private float m_CreateTime;
        public bool Loop = true;
        public float LifeTime = 5f;
        [Range(0.001f, 100f)] public float Radius = 1f;
        public AnimationCurve RadiusCurve = AnimationCurve.Linear(0, 1, 1, 1);
        public Vector3 Asix = Vector3.up;
        [Range(-12f, 12f)] public float Force = 1f;
        public AnimationCurve ForceCurve = AnimationCurve.Linear(0, 1, 1, 1);
        public float Duration = 0f;
        public float MoveLength;
        public AnimationCurve MoveLengthCurve = AnimationCurve.Linear(0, 1, 1, 1);

        private Vector3 m_prePosition = Vector3.zero;

        private void OnEnable()
        {
            if (WindManager.Instance != null)
                WindManager.Instance.AddWindMotor(this);
            m_CreateTime = Time.time; // 统一使用 Time.time 适配渲染管线
        }

        private void OnDisable()
        {
            if (WindManager.Instance != null)
                WindManager.Instance.RemoveWindMotor(this);
        }

        /// <summary>
        /// 由 WindManager 每帧驱动，返回 false 表示该生命周期结束，需要被回收
        /// </summary>
        public bool Tick(float currentTime)
        {
            float duration = currentTime - m_CreateTime;
            
            // 生命周期判断
            if (duration > LifeTime)
            {
                if (Loop)
                {
                    m_CreateTime = currentTime;
                    duration = 0f;
                }
                else
                {
                    return false; // 告诉 Manager 我死了
                }
            }

            float timePerc = duration / LifeTime;
            Duration = timePerc;

            // 更新具体类型数据
            switch (MotorType)
            {
                case MotorType.Directional: UpdateDirectionalWind(timePerc); break;
                case MotorType.Omni:        UpdateOmniWind(timePerc); break;
                case MotorType.Vortex:      UpdateVortexWind(timePerc); break;
                case MotorType.Moving:      UpdateMovingWind(timePerc); break;
            }

            return true; // 存活状态
        }

        private float GetForce(float timePerc)
        {
            return Mathf.Clamp(ForceCurve.Evaluate(timePerc) * Force, -12f, 12f);
        }

        private void UpdateDirectionalWind(float timePerc)
        {
            float radius = Radius * RadiusCurve.Evaluate(timePerc);
            motorDirectional.position = transform.position;
            motorDirectional.radiusSq = radius * radius;
            motorDirectional.force = transform.forward * GetForce(timePerc);
        }

        private void UpdateOmniWind(float timePerc)
        {
            float radius = Radius * RadiusCurve.Evaluate(timePerc);
            motorOmni.position = transform.position;
            motorOmni.radiusSq = radius * radius;
            motorOmni.force = GetForce(timePerc);
        }

        private void UpdateVortexWind(float timePerc)
        {
            float radius = Radius * RadiusCurve.Evaluate(timePerc);
            motorVortex.position = transform.position;
            motorVortex.axis = Vector3.Normalize(Asix);
            motorVortex.radiusSq = radius * radius;
            motorVortex.force = GetForce(timePerc);
        }

        private void UpdateMovingWind(float timePerc)
        {
            float moveLen = MoveLength * MoveLengthCurve.Evaluate(timePerc);
            float radius = Radius * RadiusCurve.Evaluate(timePerc);
            Vector3 position = transform.position;
            Vector3 prePosition = m_prePosition == Vector3.zero ? position : m_prePosition;
            
            motorMoving.prePosition = prePosition;
            motorMoving.moveLen = moveLen;
            motorMoving.moveDir = position - prePosition;
            motorMoving.radiusSq = radius * radius;
            motorMoving.force = GetForce(timePerc);
            
            m_prePosition = position;
        }
    }
}