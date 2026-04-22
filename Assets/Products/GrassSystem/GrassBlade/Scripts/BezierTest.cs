using UnityEngine;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Zerls.GrassSystem
{
    [System.Serializable]
    public class BezierCurve
    {
        [Header("Control Points")]
        public Vector3 p0 = Vector3.zero;
        public Vector3 p1 = Vector3.up;
        public Vector3 p2 = Vector3.up + Vector3.right;
        public Vector3 p3 = Vector3.right;
        
        [Header("Curve Settings")]
        public Color curveColor = Color.green;
        public bool enabled = true;
        
        public Vector3 Evaluate(float t)
        {
            float opt = 1f - t;
            float opt2 = opt * opt;
            float tt = t * t;

            return p0 * (opt * opt2) +
                   p1 * (3f * opt2 * t) +
                   p2 * (3f * opt * tt) +
                   p3 * (tt * t);
        }
    }

    [ExecuteAlways]
    public class BezierTest : MonoBehaviour
    {
        [Header("Bezier Curves")]
        public List<BezierCurve> curves = new List<BezierCurve>();
        
        [Header("Gizmo Settings")]
        public int segments = 20;
        public float gizmoSize = 0.1f;
        public Color controlPointColor = Color.yellow;
        public bool showControlLines = true;
        
        private void Start()
        {
            if (curves.Count == 0)
            {
                curves.Add(new BezierCurve());
            }
        }

        public static Vector3 CubicBezier(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float opt = 1f - t;
            float opt2 = opt * opt;
            float tt = t * t;

            return p0 * (opt * opt2) +
                   p1 * (3f * opt2 * t) +
                   p2 * (3f * opt * tt) +
                   p3 * (tt * t);
        }

        private void OnDrawGizmos()
        {
            for (int curveIndex = 0; curveIndex < curves.Count; curveIndex++)
            {
                BezierCurve curve = curves[curveIndex];
                if (!curve.enabled) continue;

                // Transform points to world space
                Vector3 worldP0 = transform.TransformPoint( curve.p0);
                Vector3 worldP1 = transform.TransformPoint( curve.p1);
                Vector3 worldP2 = transform.TransformPoint( curve.p2);
                Vector3 worldP3 = transform.TransformPoint( curve.p3);
                
                // Draw control points
                Gizmos.color = controlPointColor;
                Gizmos.DrawSphere(worldP0, gizmoSize);
                Gizmos.DrawSphere(worldP1, gizmoSize);
                Gizmos.DrawSphere(worldP2, gizmoSize);
                Gizmos.DrawSphere(worldP3, gizmoSize);

                // Draw control lines
                if (showControlLines)
                {
                    Gizmos.color = Color.gray;
                    Gizmos.DrawLine(worldP0, worldP1);
                    Gizmos.DrawLine(worldP1, worldP2);
                    Gizmos.DrawLine(worldP2, worldP3);
                }
                
                // Draw bezier curve
                Gizmos.color = curve.curveColor;
                Vector3 previousPoint = worldP0;
                for (int i = 1; i <= segments; i++)
                {
                    float t = (float)i / segments;
                    Vector3 localPoint = curve.Evaluate(t);
                    Vector3 currentPoint = transform.TransformPoint(localPoint);
                    Gizmos.DrawLine(previousPoint, currentPoint);
                    previousPoint = currentPoint;
                }
            }
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Handles.matrix = transform.localToWorldMatrix;
            
            for (int curveIndex = 0; curveIndex < curves.Count; curveIndex++)
            {
                BezierCurve curve = curves[curveIndex];
                if (!curve.enabled) continue;
                
                // EditorGUI.BeginChangeCheck();
                //
                // Vector3 newP0 = Handles.PositionHandle(curve.p0, Quaternion.identity);
                // Vector3 newP1 = Handles.PositionHandle(curve.p1, Quaternion.identity);  
                // Vector3 newP2 = Handles.PositionHandle(curve.p2, Quaternion.identity);
                // Vector3 newP3 = Handles.PositionHandle(curve.p3, Quaternion.identity);
                //
                // if (EditorGUI.EndChangeCheck())
                // {
                //     Undo.RecordObject(this, $"Move Bezier Control Points - Curve {curveIndex}");
                //     curve.p0 = newP0;
                //     curve.p1 = newP1;
                //     curve.p2 = newP2;
                //     curve.p3 = newP3;
                // }
                
                // Draw labels with curve index
                Handles.Label(curve.p0, $"C{curveIndex}-P0");
                Handles.Label(curve.p1, $"C{curveIndex}-P1");
                Handles.Label(curve.p2, $"C{curveIndex}-P2");
                Handles.Label(curve.p3, $"C{curveIndex}-P3");
            }
            
            Handles.matrix = Matrix4x4.identity;
        }
#endif
    }
}