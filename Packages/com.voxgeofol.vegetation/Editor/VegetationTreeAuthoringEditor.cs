#nullable enable

using MeshVoxelizerProject;
using UnityEditor;
using VoxGeoFol.Features.Vegetation.Authoring;

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
}