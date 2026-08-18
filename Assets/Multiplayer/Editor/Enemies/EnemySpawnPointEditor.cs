using FPSBasic.Multiplayer.Enemies;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace FPSBasic.Multiplayer.Editor
{
    [CustomEditor(typeof(EnemySpawnPoint))]
    public sealed class EnemySpawnPointEditor : UnityEditor.Editor
    {
        private readonly BoxBoundsHandle _boundsHandle = new();
        private SerializedProperty _areaCenter;
        private SerializedProperty _areaSize;

        private void OnEnable()
        {
            _areaCenter = serializedObject.FindProperty("areaCenter");
            _areaSize = serializedObject.FindProperty("areaSize");
        }

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.Space();

            if (GUILayout.Button("Preview Spawn Positions"))
            {
                foreach (Object selectedTarget in targets)
                {
                    EnemySpawnPoint spawnPoint = (EnemySpawnPoint)selectedTarget;
                    spawnPoint.GeneratePreviewPositions();
                    EditorUtility.SetDirty(spawnPoint);
                }

                SceneView.RepaintAll();
            }

            EditorGUILayout.HelpBox(
                "Select this object and drag the cyan Scene-view box handles to resize the RandomArea. " +
                "Preview points are sampled from the baked NavMesh.",
                MessageType.Info);
        }

        private void OnSceneGUI()
        {
            EnemySpawnPoint spawnPoint = (EnemySpawnPoint)target;
            serializedObject.Update();

            using (new Handles.DrawingScope(
                       new Color(0.1f, 0.9f, 1f, 0.9f),
                       spawnPoint.transform.localToWorldMatrix))
            {
                _boundsHandle.center = _areaCenter.vector3Value;
                _boundsHandle.size = _areaSize.vector3Value;

                EditorGUI.BeginChangeCheck();
                _boundsHandle.DrawHandle();
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(spawnPoint, "Resize Enemy Spawn Area");
                    _areaCenter.vector3Value = _boundsHandle.center;
                    _areaSize.vector3Value = new Vector3(
                        Mathf.Max(0.1f, _boundsHandle.size.x),
                        Mathf.Max(0.1f, _boundsHandle.size.y),
                        Mathf.Max(0.1f, _boundsHandle.size.z));
                    serializedObject.ApplyModifiedProperties();
                    spawnPoint.GeneratePreviewPositions();
                }
            }
        }
    }
}
