#nullable enable

using MeshVoxelizerProject;
using UnityEditor;
using UnityEngine;
using VoxGeoFol.Features.Vegetation.Authoring;
using VoxGeoFol.Features.Vegetation.Rendering;

namespace VoxGeoFol.Features.Vegetation.Editor
{
    /// <summary>
    /// Custom inspector for vegetation tree authoring scene bindings.
    /// </summary>
    [CustomEditor(typeof(VegetationTreeAuthoring))]
    public sealed class VegetationTreeAuthoringEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            if (MeshVoxelizerHierarchyBuilder.Generating)
                return;

            VegetationTreeAuthoring authoring = (VegetationTreeAuthoring)target;
            VegetationTreeAuthoringEditorPanel.Draw(serializedObject, authoring);
        }
    }

    /// <summary>
    /// Custom inspector for runtime vegetation container bindings.
    /// </summary>
    [CustomEditor(typeof(VegetationRuntimeContainer))]
    public sealed class VegetationRuntimeContainerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawDefaultInspector();

            GUILayout.Space(6f);
            bool fillRegisteredAuthorings = GUILayout.Button("Fill Registered Authorings");
            serializedObject.ApplyModifiedProperties();

            if (!fillRegisteredAuthorings)
            {
                return;
            }

            VegetationTreeAuthoringEditorUtility.FillRuntimeContainerAuthorings((VegetationRuntimeContainer)target);
            GUIUtility.ExitGUI();
        }

        [MenuItem("CONTEXT/VegetationRuntimeContainer/Fill Registered Authorings")]
        private static void FillRegisteredAuthorings(MenuCommand command)
        {
            VegetationTreeAuthoringEditorUtility.FillRuntimeContainerAuthorings((VegetationRuntimeContainer)command.context);
        }
    }
}
