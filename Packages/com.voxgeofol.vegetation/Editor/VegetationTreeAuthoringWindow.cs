#nullable enable

using UnityEditor;
using VoxGeoFol.Features.Vegetation.Authoring;

namespace VoxGeoFol.Features.Vegetation.Editor
{
    /// <summary>
    /// Dedicated window for driving vegetation tree authoring preview and bake utilities.
    /// </summary>
    public sealed class VegetationTreeAuthoringWindow : EditorWindow
    {
        private VegetationTreeAuthoring? authoring;

        [MenuItem("Tools/VoxGeoFol/Vegetation/Tree Authoring Window", priority = 2001)]
        public static void Open()
        {
            GetWindow<VegetationTreeAuthoringWindow>("Vegetation Tree");
        }

        public static void Open(VegetationTreeAuthoring authoring)
        {
            VegetationTreeAuthoringWindow window = GetWindow<VegetationTreeAuthoringWindow>("Vegetation Tree");
            window.authoring = authoring;
            window.Repaint();
        }

        private void OnSelectionChange()
        {
            if (Selection.activeGameObject == null)
            {
                return;
            }

            VegetationTreeAuthoring? selectionAuthoring = Selection.activeGameObject.GetComponent<VegetationTreeAuthoring>();
            if (selectionAuthoring != null)
            {
                authoring = selectionAuthoring;
                Repaint();
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Vegetation Tree Authoring", EditorStyles.boldLabel);
            authoring = (VegetationTreeAuthoring?)EditorGUILayout.ObjectField(
                "Target",
                authoring,
                typeof(VegetationTreeAuthoring),
                true);

            if (authoring == null)
            {
                EditorGUILayout.HelpBox("Select a VegetationTreeAuthoring component to use the preview and bake utilities.", MessageType.Info);
                return;
            }

            SerializedObject authoringObject = new SerializedObject(authoring);
            VegetationTreeAuthoringEditorPanel.Draw(authoringObject, authoring, showOpenWindowButton: false);
        }
    }
}
