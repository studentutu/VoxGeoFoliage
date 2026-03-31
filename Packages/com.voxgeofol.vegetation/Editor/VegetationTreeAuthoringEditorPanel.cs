#nullable enable

using System;
using System.Text;
using UnityEditor;
using UnityEngine;
using VoxGeoFol.Features.Vegetation.Authoring;

namespace VoxGeoFol.Features.Vegetation.Editor
{
    /// <summary>
    /// Shared IMGUI panel for vegetation tree authoring inspector and window controls.
    /// </summary>
    public static class VegetationTreeAuthoringEditorPanel
    {
        public static void Draw(SerializedObject serializedAuthoring, VegetationTreeAuthoring authoring)
        {
            if (serializedAuthoring == null)
            {
                throw new ArgumentNullException(nameof(serializedAuthoring));
            }

            if (authoring == null)
            {
                throw new ArgumentNullException(nameof(authoring));
            }

            bool hadActivePreview = VegetationEditorPreview.TryGetActivePreviewTier(authoring, out VegetationPreviewTier activeTierBeforeEdit);
            GameObject? previousBranchRoot = authoring.BranchRoot;

            serializedAuthoring.Update();

            SerializedProperty blueprintProperty = serializedAuthoring.FindProperty("blueprint");
            SerializedProperty branchRootProperty = serializedAuthoring.FindProperty("_rootForBranches");
            SerializedProperty runtimeTreeIndexProperty = serializedAuthoring.FindProperty("runtimeTreeIndex");

            EditorGUILayout.PropertyField(blueprintProperty);
            EditorGUILayout.PropertyField(branchRootProperty, new GUIContent("Branch Root"));

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.PropertyField(runtimeTreeIndexProperty);
            }

            bool authoringChanged = serializedAuthoring.ApplyModifiedProperties();
            if (authoringChanged && hadActivePreview)
            {
                if (previousBranchRoot != null && previousBranchRoot != authoring.BranchRoot)
                {
                    ClearPreviewChildren(previousBranchRoot.transform);
                }

                if (authoring.BranchRoot != null)
                {
                    TryRun("Refresh Preview", () => VegetationEditorPreview.ShowPreview(authoring, activeTierBeforeEdit));
                }
            }

            EditorGUILayout.Space();
            DrawSummary(authoring);
            EditorGUILayout.Space();
            DrawValidation(authoring);
            EditorGUILayout.Space();
            DrawPreviewControls(authoring);
            EditorGUILayout.Space();
            DrawBakeControls(authoring);
        }

        private static void DrawSummary(VegetationTreeAuthoring authoring)
        {
            EditorGUILayout.LabelField("Summary", EditorStyles.boldLabel);

            if (authoring.Blueprint == null)
            {
                EditorGUILayout.HelpBox("Assign a TreeBlueprintSO to view authoring summary data.", MessageType.Info);
                return;
            }

            try
            {
                VegetationAuthoringSummary summary = VegetationTreeAuthoringEditorUtility.BuildSummary(authoring);
                EditorGUILayout.LabelField("Branch Count", summary.BranchCount.ToString());
                EditorGUILayout.LabelField("Tree Bounds Center", summary.TreeBounds.center.ToString("F3"));
                EditorGUILayout.LabelField("Tree Bounds Size", summary.TreeBounds.size.ToString("F3"));
                DrawTriangleLabel("R0 Full", summary.R0Triangles);
                DrawTriangleLabel("R1 ShellL1", summary.R1Triangles);
                DrawTriangleLabel("R2 ShellL2", summary.R2Triangles);
                DrawTriangleLabel("R3 Impostor", summary.R3Triangles);
                DrawTriangleLabel("ShellL0 Only", summary.ShellL0OnlyTriangles);
                DrawTriangleLabel("ShellL1 Only", summary.ShellL1OnlyTriangles);
                DrawTriangleLabel("ShellL2 Only", summary.ShellL2OnlyTriangles);
            }
            catch (Exception exception)
            {
                EditorGUILayout.HelpBox(exception.Message, MessageType.Warning);
            }
        }

        private static void DrawValidation(VegetationTreeAuthoring authoring)
        {
            EditorGUILayout.LabelField("Validation", EditorStyles.boldLabel);

            VegetationValidationResult result;
            try
            {
                result = VegetationTreeAuthoringEditorUtility.ValidateForEditor(authoring);
            }
            catch (Exception exception)
            {
                EditorGUILayout.HelpBox(exception.Message, MessageType.Error);
                return;
            }

            if (!result.HasErrors && !result.HasWarnings)
            {
                EditorGUILayout.HelpBox("No validation issues.", MessageType.Info);
                return;
            }

            string errorMessages = BuildIssueBlock(result, VegetationValidationSeverity.Error);
            if (!string.IsNullOrEmpty(errorMessages))
            {
                EditorGUILayout.HelpBox(errorMessages, MessageType.Error);
            }

            string warningMessages = BuildIssueBlock(result, VegetationValidationSeverity.Warning);
            if (!string.IsNullOrEmpty(warningMessages))
            {
                EditorGUILayout.HelpBox(warningMessages, MessageType.Warning);
            }
        }

        private static void DrawPreviewControls(VegetationTreeAuthoring authoring)
        {
            EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);

            bool previewActive = VegetationEditorPreview.TryGetActivePreviewTier(authoring, out VegetationPreviewTier activePreviewTier);
            VegetationPreviewTier selectedPreviewTier = previewActive
                ? activePreviewTier
                : VegetationEditorPreview.GetStoredPreviewTier(authoring);

            EditorGUI.BeginChangeCheck();
            bool previewEnabled = EditorGUILayout.Toggle("Preview Enabled", previewActive);
            VegetationPreviewTier nextPreviewTier =
                (VegetationPreviewTier)EditorGUILayout.EnumPopup("Preview Tier", selectedPreviewTier);
            bool previewSelectionChanged = EditorGUI.EndChangeCheck();

            if (selectedPreviewTier != nextPreviewTier)
            {
                VegetationEditorPreview.SetStoredPreviewTier(authoring, nextPreviewTier);
            }

            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                if (previewSelectionChanged)
                {
                    if (previewEnabled)
                    {
                        TryRun("Show Preview", () => VegetationEditorPreview.ShowPreview(authoring, nextPreviewTier));
                    }
                    else if (previewActive && !previewEnabled)
                    {
                        TryRun("Clear Preview", () => VegetationEditorPreview.ClearPreview(authoring));
                    }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Refresh Preview"))
                    {
                        TryRun("Refresh Preview", () => VegetationEditorPreview.ShowPreview(authoring, nextPreviewTier));
                    }

                    if (GUILayout.Button("Clear Preview"))
                    {
                        TryRun("Clear Preview", () => VegetationEditorPreview.ClearPreview(authoring));
                    }
                }
            }
        }

        private static void DrawBakeControls(VegetationTreeAuthoring authoring)
        {
            EditorGUILayout.LabelField("Bake", EditorStyles.boldLabel);

            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Regenerate Shells"))
                    {
                        TryRun("Regenerate Shells", () => VegetationTreeAuthoringEditorUtility.BakeCanopyShells(authoring));
                    }

                    if (GUILayout.Button("Regenerate Impostor"))
                    {
                        TryRun("Regenerate Impostor", () => VegetationTreeAuthoringEditorUtility.BakeImpostor(authoring));
                    }
                }

                if (GUILayout.Button("Regenerate Shells And Impostor"))
                {
                    TryRun(
                        "Regenerate Shells And Impostor",
                        () => VegetationTreeAuthoringEditorUtility.BakeCanopyShellsAndImpostor(authoring));
                }
            }
        }

        private static void DrawTriangleLabel(string label, int triangleCount)
        {
            EditorGUILayout.LabelField(label, triangleCount.ToString());
        }

        private static string BuildIssueBlock(
            VegetationValidationResult result,
            VegetationValidationSeverity severity)
        {
            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < result.Issues.Count; i++)
            {
                VegetationValidationIssue issue = result.Issues[i];
                if (issue.Severity != severity)
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.AppendLine();
                }

                builder.Append("- ");
                builder.Append(issue.Message);
            }

            return builder.ToString();
        }

        private static void TryRun(string operationLabel, Action operation)
        {
            try
            {
                operation();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog(operationLabel, exception.Message, "OK");
            }
        }

        private static void ClearPreviewChildren(Transform rootTransform)
        {
            for (int i = rootTransform.childCount - 1; i >= 0; i--)
            {
                UnityEngine.Object.DestroyImmediate(rootTransform.GetChild(i).gameObject);
            }
        }
    }
}
