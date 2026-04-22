using UnityEngine;
#if ENABLE_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM_PACKAGE
using UnityEngine.InputSystem;
#endif

namespace Zerls.CameraSystem
{
    // 强制要求挂载此脚本的物体上必须有 FreeCamera 组件，防止忘挂报错
    [RequireComponent(typeof(UnityEngine.Rendering.FreeCamera))]
    public class FreeCameraController : MonoBehaviour
    {
        [Header("控制设置")]
        [Tooltip("当前相机是否处于受控飞行状态")]
        public bool isCameraEnabled = true;

        // 原生的 FreeCamera 引用
        private UnityEngine.Rendering.FreeCamera targetCameraScript;

        void Start()
        {
            targetCameraScript = GetComponent<UnityEngine.Rendering.FreeCamera>();
            
            // 初始化状态
            if (targetCameraScript != null)
            {
                targetCameraScript.enabled = isCameraEnabled;
            }
        }

        void Update()
        {
            if (UnityEngine.Rendering.DebugManager.instance.displayRuntimeUI)
                return;
            
            bool togglePressed = false;
            
#if ENABLE_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM_PACKAGE
            togglePressed = (Keyboard.current?.leftAltKey?.wasPressedThisFrame == true) || 
                            (Keyboard.current?.backquoteKey?.wasPressedThisFrame == true);
#else
            togglePressed = Input.GetKeyDown(KeyCode.LeftAlt) || Input.GetKeyDown(KeyCode.BackQuote);
#endif
            
            if (togglePressed)
            {
                isCameraEnabled = !isCameraEnabled;
                
                if (targetCameraScript != null)
                {
                    targetCameraScript.enabled = isCameraEnabled;
                }
            }
        }

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;

            GUIStyle style = new GUIStyle();
            style.alignment = TextAnchor.LowerRight;
            style.fontSize = 18;
            style.fontStyle = FontStyle.Bold;

            string statusText = isCameraEnabled 
                ? "相机模式: 自由飞行 (按 Left Alt 或 ~ 锁定) " 
                : "相机模式: 已锁定 (按 Left Alt 或 ~ 解锁) ";
            
            Rect rect = new Rect(Screen.width - 430, Screen.height - 40, 420, 30);
            
            // 绘制底色阴影 (黑色偏移)，防止在亮色天空盒下看不清字体
            style.normal.textColor = Color.black;
            GUI.Label(new Rect(rect.x + 1, rect.y + 1, rect.width, rect.height), statusText, style);
            
            // 绘制主体颜色
            style.normal.textColor = isCameraEnabled ? Color.green : Color.yellow;
            GUI.Label(rect, statusText, style);
        }
    }
}