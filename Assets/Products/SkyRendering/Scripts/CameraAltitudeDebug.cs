using UnityEngine;

[ExecuteAlways]
public class CameraAltitudeDebug : MonoBehaviour
{
    [Header("Settings Assets")]
    [Tooltip("Atmosphere settings SO (for sea level/planet radius)")]
    public AtmosphereSettings atmosphereSettings;

    [Tooltip("Cloud settings SO (for云层起始高度等)")]
    public CloudSettings cloudSettings;

    [Header("Display")]
    public bool showGUI = true;
    public Vector2 guiPosition = new Vector2(10, 10);
    public int fontSize = 16;
    public Color fontColor = Color.cyan;
    public bool useSphericalAltitude = false;

    private Camera targetCamera;

    void OnEnable()
    {
        targetCamera = GetComponent<Camera>();
        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }
    }

    void Update()
    {
        // 保证运行时即刻显示
        if (targetCamera == null)
        {
            targetCamera = GetComponent<Camera>();
            if (targetCamera == null)
                targetCamera = Camera.main;
        }
    }

    void OnGUI()
    {
        if (!showGUI || targetCamera == null)
            return;

        GUIStyle s = new GUIStyle(GUI.skin.label);
        s.fontSize = fontSize;
        s.normal.textColor = fontColor;

        Vector3 camPos = targetCamera.transform.position;

        float seaLevel = (atmosphereSettings != null) ? atmosphereSettings.SeaLevel : 0f;
        float planetRadius = (atmosphereSettings != null) ? atmosphereSettings.PlanetRadius : 0f;

        float altitudeFromSea = camPos.y - seaLevel;
        float altitudeFromPlanet = 0f;
        if (atmosphereSettings != null && planetRadius > 0f)
        {
            altitudeFromPlanet = camPos.magnitude - planetRadius;
        }

        float cloudStart = (cloudSettings != null) ? cloudSettings.CloudAreaStartHeight : 0f;
        float cloudThickness = (cloudSettings != null) ? cloudSettings.CloudAreaThickness : 0f;

        string altitudeText;

        if (useSphericalAltitude && atmosphereSettings != null && planetRadius > 0f)
        {
            altitudeText = string.Format("相机高度（球面量）: {0:0.00}m (PlanetRadius {1:0.0}m)", altitudeFromPlanet + planetRadius, planetRadius);
        }
        else
        {
            altitudeText = string.Format("相机海拔高度（相对于海平面）：{0:0.00}m (SeaLevel {1:0.00}m)", altitudeFromSea, seaLevel);
        }

        string cloudText = string.Format("云层起始高度：{0:0.00}m，厚度：{1:0.00}m\n离云层起始高度：{2:0.00}m",
            cloudStart,
            cloudThickness,
            camPos.y - cloudStart);

        float NormalizedCloudHeight = (cloudSettings != null && cloudThickness > 0f) ? Mathf.Clamp01((camPos.y - cloudStart) / cloudThickness) : 0f;
        cloudText += string.Format("\n云层高度归一化：{0:0.00}", NormalizedCloudHeight);
        
        int lineCount = 4 + (cloudSettings != null ? 1 : 0) + 1; // 标题 + 海拔 + 云层 + 位置
        float textHeight = s.lineHeight + 4f;
        float areaHeight = textHeight * lineCount + 10f;

        GUILayout.BeginArea(new Rect(guiPosition.x, guiPosition.y, 500, areaHeight));
        GUILayout.BeginVertical(GUI.skin.box);
        GUILayout.Label("==== Camera Altitude Debug ====", s);
        GUILayout.Label(altitudeText, s);
        if (cloudSettings != null)
        {
            GUILayout.Label(cloudText, s);
        }
        GUILayout.Label(string.Format("Camera World Pos: {0:0.00}, {1:0.00}, {2:0.00}", camPos.x, camPos.y, camPos.z), s);
        GUILayout.EndVertical();
        GUILayout.EndArea();
    }
}
