using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(BaseNoise), true)]
public class BaseNoiseEditor : Editor {
    private BaseNoise instance;
    private int previewSlice;

    private void OnEnable() {
        instance = target as BaseNoise;
        previewSlice = 0;
    }

    public override void OnInspectorGUI() {
        serializedObject.Update();

        EditorGUI.BeginChangeCheck();
        base.OnInspectorGUI();
        bool settingsChanged = EditorGUI.EndChangeCheck();
        serializedObject.ApplyModifiedProperties();

        if (settingsChanged && instance.autoRealtimePreview) {
            instance.Generate();
            EditorUtility.SetDirty(instance);
        }

        GUILayout.Space(30);
        if (GUILayout.Button("Generate", GUILayout.Height(30))) {
            instance.Generate();
        }

        GUILayout.Space(30);
        if (GUILayout.Button("SaveToDisk", GUILayout.Height(30))) {
            instance.SaveToDisk();
        }

        DrawPreview();
    }

    private void DrawPreview() {
        GUILayout.Space(20);
        GUILayout.Label("Preview", EditorStyles.boldLabel);

        if (!instance.HasGeneratedData()) {
            EditorGUILayout.HelpBox("Click Generate to preview the noise texture.", MessageType.Info);
            return;
        }

        if (instance.is3D) {
            previewSlice = EditorGUILayout.IntSlider("Slice", previewSlice, 0, Mathf.Max(0, instance.resolution - 1));
        }
        else {
            previewSlice = 0;
        }

        Texture2D previewTexture = instance.GetPreviewTexture2D(previewSlice);
        if (previewTexture == null) {
            EditorGUILayout.HelpBox("Preview is not available.", MessageType.Warning);
            return;
        }

        Rect previewRect = GUILayoutUtility.GetAspectRect(1f, GUILayout.MinHeight(220));
        EditorGUI.DrawPreviewTexture(previewRect, previewTexture, null, ScaleMode.ScaleToFit);
    }
}