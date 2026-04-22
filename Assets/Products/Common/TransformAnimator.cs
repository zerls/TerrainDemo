using UnityEngine;

namespace Zerls.Utilities
{
    /// <summary>
    /// 通用物体运动控制器，支持多维度组合运动与动画曲线精细控制
    /// </summary>
    public class TransformAnimator : MonoBehaviour
    {
        
        [Header("--- 1. 往返移动 (Linear Oscillation) ---")]
        public bool enableMovement = false;
        [Tooltip("移动方向 (局部空间)")]
        public Vector3 moveDirection = Vector3.up;
        [Tooltip("移动距离")]
        public float moveDistance = 2f;
        [Tooltip("移动频率 (次/秒)")]
        public float moveSpeed = 1f;
        [Tooltip("移动曲线：X轴(0~1)代表时间周期，Y轴(-1~1)代表运动极值")]
        public AnimationCurve moveCurve = new AnimationCurve(new Keyframe(0, -1), new Keyframe(0.5f, 1), new Keyframe(1, -1));

        [Header("--- 2. 周期摆动 (Oscillating Rotation) ---")]
        public bool enableSwing = false;
        [Tooltip("摆动轴向 (局部空间)")]
        public Vector3 swingAxis = Vector3.forward;
        [Tooltip("摆动幅度 (最大角度)")]
        public float swingAmplitude = 45f;
        public float swingSpeed = 1f;
        [Tooltip("摆动曲线：推荐设置为平滑的正弦波形")]
        public AnimationCurve swingCurve = new AnimationCurve(new Keyframe(0, -1), new Keyframe(0.5f, 1), new Keyframe(1, -1));

        [Header("--- 3. 连续自转 (Continuous Spin) ---")]
        public bool enableSpin = false;
        [Tooltip("自转轴向 (局部空间)")]
        public Vector3 spinAxis = Vector3.up;
        [Tooltip("自转速度 (度/秒)")]
        public float spinSpeed = 90f;

        [Header("--- 4. 环绕旋转 (Orbit) ---")]
        public bool enableOrbit = false;
        [Tooltip("要环绕的目标物体")]
        public Transform orbitTarget;
        [Tooltip("环绕轴向 (世界空间)")]
        public Vector3 orbitAxis = Vector3.up;
        public float orbitSpeed = 60f;
        public bool alwaysLookAtTarget = true;

        [Header("--- 5. 呼吸缩放 (Scale Pulse) ---")]
        public bool enableScale = false;
        [Tooltip("缩放影响的轴向掩码，比如 (1,1,1) 就是整体缩放")]
        public Vector3 scaleAxis = Vector3.one;
        [Tooltip("额外增加的缩放倍率")]
        public float scaleMultiplier = 0.5f;
        public float scaleSpeed = 1f;
        [Tooltip("缩放曲线：Y轴 0 代表原始大小，1 代表原始大小 + Multiplier")]
        public AnimationCurve scaleCurve = new AnimationCurve(new Keyframe(0, 0), new Keyframe(0.5f, 1), new Keyframe(1, 0));

        [Header("--- 全局设置 ---")]
        [Tooltip("相位偏移 (用于让场景里挂载了相同脚本的多个物体运动错开)")]
        public float globalPhaseOffset = 0f;

        // 状态缓存 (用于防止多重运动叠加导致的数值漂移)
        private Vector3 startLocalPosition;
        private Quaternion startLocalRotation;
        private Vector3 startLocalScale;

        // 持续自转的累加值
        private float currentSpinAngle = 0f;

        void Start()
        {
            // 缓存所有初始的 Transform 状态
            startLocalPosition = transform.localPosition;
            startLocalRotation = transform.localRotation;
            startLocalScale = transform.localScale;
        }

        void Update()
        {
            float time = Time.time;

            // -------------------------------------------------------------
            // 1. 连续自转与周期摆动 (Rotation)
            // -------------------------------------------------------------
            Quaternion finalRotation = startLocalRotation;

            if (enableSpin)
            {
                currentSpinAngle += spinSpeed * Time.deltaTime;
                // 将持续自转叠加进去
                finalRotation *= Quaternion.AngleAxis(currentSpinAngle, spinAxis.normalized);
            }

            if (enableSwing)
            {
                // 计算当前周期内的归一化时间 [0, 1]
                float swingTime = Mathf.Repeat(time * swingSpeed + globalPhaseOffset, 1f);
                float angle = swingCurve.Evaluate(swingTime) * swingAmplitude;
                finalRotation *= Quaternion.AngleAxis(angle, swingAxis.normalized);
            }

            // 应用旋转
            if (enableSpin || enableSwing)
            {
                transform.localRotation = finalRotation;
            }

            // -------------------------------------------------------------
            // 2. 往返移动 (Translation)
            // -------------------------------------------------------------
            if (enableMovement)
            {
                float moveTime = Mathf.Repeat(time * moveSpeed + globalPhaseOffset, 1f);
                float offsetValue = moveCurve.Evaluate(moveTime);
                
                // 基于初始位置进行偏移，不会发生漂移
                transform.localPosition = startLocalPosition + moveDirection.normalized * (offsetValue * moveDistance);
            }

            // -------------------------------------------------------------
            // 3. 呼吸缩放 (Scale)
            // -------------------------------------------------------------
            if (enableScale)
            {
                float scaleTime = Mathf.Repeat(time * scaleSpeed + globalPhaseOffset, 1f);
                float curveVal = scaleCurve.Evaluate(scaleTime);

                Vector3 extraScale = Vector3.Scale(scaleAxis, new Vector3(curveVal, curveVal, curveVal)) * scaleMultiplier;
                transform.localScale = startLocalScale + extraScale;
            }

            // -------------------------------------------------------------
            // 4. 环绕旋转 (Orbit) 
            // -------------------------------------------------------------
            if (enableOrbit && orbitTarget != null)
            {
                transform.RotateAround(orbitTarget.position, orbitAxis, orbitSpeed * Time.deltaTime);

                if (alwaysLookAtTarget)
                {
                    transform.LookAt(orbitTarget);
                }
            }
        }
    }
}