#nullable enable

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VoxGeoFol.Features.Vegetation.Authoring;

namespace VoxGeoFol.Features.Vegetation.Editor
{
    /// <summary>
    /// Editor-side asset sync for Phase A vegetation authoring data.
    /// </summary>
    public static class VegetationPhaseAAuthoringSync
    {
        private const string DemoAssetRoot = "Assets/Tree/VoxFoliage";
        private const string DemoAssemblyPrefabPath = "Assets/Tree/tree_dense_branches.prefab";
        private const string DemoBlueprintPath = DemoAssetRoot + "/TreeBlueprint_branch_leaves_fullgeo.asset";
        private const string DemoLodProfilePath = DemoAssetRoot + "/VegetationLODProfile.asset";
        private const float ScaleStep = 0.25f;
        private const float UniformScaleTolerance = 0.001f;

        [MenuItem("Tools/VoxGeoFol/Vegetation/Refresh Demo Phase A Assets", priority = 2000)]
        public static void RefreshDemoPhaseAAssets()
        {
            // Range: runs only inside the Unity Editor with the demo assets present. Condition: rebuilds demo prototype bounds and blueprint branch placements from prefab authoring. Output: saved and validated Phase A demo assets.
            BranchPrototypeSO[] branchPrototypes = LoadAssetsAtPath<BranchPrototypeSO>(DemoAssetRoot);
            if (branchPrototypes.Length == 0)
            {
                throw new InvalidOperationException($"No BranchPrototypeSO assets were found under '{DemoAssetRoot}'.");
            }

            TreeBlueprintSO blueprint = LoadAssetAtPath<TreeBlueprintSO>(DemoBlueprintPath);
            LODProfileSO lodProfile = LoadAssetAtPath<LODProfileSO>(DemoLodProfilePath);

            for (int i = 0; i < branchPrototypes.Length; i++)
            {
                RefreshBranchPrototypeLocalBounds(branchPrototypes[i]);
                RefreshBranchPrototypeSourceBudgets(branchPrototypes[i]);
                ThrowIfValidationHasErrors(branchPrototypes[i].name, branchPrototypes[i].Validate());
            }

            RefreshBlueprintFromAssemblyAsset(blueprint, DemoAssemblyPrefabPath, branchPrototypes, lodProfile);
            ThrowIfValidationHasErrors(blueprint.name, blueprint.Validate());

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log(
                $"Refreshed Phase A vegetation demo assets. Branch prototypes: {branchPrototypes.Length}, blueprint placements: {blueprint.Branches.Length}.");
        }

        /// <summary>
        /// [INTEGRATION] Batch entry point used by automation to refresh Phase A demo assets and exit the editor.
        /// </summary>
        public static void RefreshDemoPhaseAAssetsAndExit()
        {
            try
            {
                RefreshDemoPhaseAAssets();
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        /// <summary>
        /// [INTEGRATION] Recomputes one branch prototype localBounds from its source wood and foliage meshes.
        /// </summary>
        public static void RefreshBranchPrototypeLocalBounds(BranchPrototypeSO prototype)
        {
            if (prototype == null)
            {
                throw new ArgumentNullException(nameof(prototype));
            }

            Mesh woodMesh = prototype.WoodMesh ?? throw new InvalidOperationException($"{prototype.name} is missing woodMesh.");
            Mesh foliageMesh = prototype.FoliageMesh ?? throw new InvalidOperationException($"{prototype.name} is missing foliageMesh.");
            Bounds combinedBounds = woodMesh.bounds;
            combinedBounds.Encapsulate(foliageMesh.bounds);

            SerializedObject serializedPrototype = new SerializedObject(prototype);
            serializedPrototype.FindProperty("localBounds").boundsValue = combinedBounds;
            serializedPrototype.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(prototype);
        }

        private static void RefreshBranchPrototypeSourceBudgets(BranchPrototypeSO prototype)
        {
            Mesh woodMesh = prototype.WoodMesh ?? throw new InvalidOperationException($"{prototype.name} is missing woodMesh.");
            Mesh foliageMesh = prototype.FoliageMesh ?? throw new InvalidOperationException($"{prototype.name} is missing foliageMesh.");
            int woodTriangles = GetTriangleCount(woodMesh);
            int foliageTriangles = GetTriangleCount(foliageMesh);

            SerializedObject serializedPrototype = new SerializedObject(prototype);
            SerializedProperty woodBudgetProperty = serializedPrototype.FindProperty("triangleBudgetWood");
            SerializedProperty foliageBudgetProperty = serializedPrototype.FindProperty("triangleBudgetFoliage");
            bool budgetsRaised = false;

            if (woodBudgetProperty.intValue < woodTriangles)
            {
                woodBudgetProperty.intValue = woodTriangles;
                budgetsRaised = true;
            }

            if (foliageBudgetProperty.intValue < foliageTriangles)
            {
                foliageBudgetProperty.intValue = foliageTriangles;
                budgetsRaised = true;
            }

            if (!budgetsRaised)
            {
                return;
            }

            serializedPrototype.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(prototype);
        }

        /// <summary>
        /// [INTEGRATION] Refreshes one blueprint from an assembled tree prefab asset and known branch prototypes.
        /// </summary>
        public static void RefreshBlueprintFromAssemblyAsset(
            TreeBlueprintSO blueprint,
            string assemblyPrefabPath,
            IReadOnlyList<BranchPrototypeSO> branchPrototypes,
            LODProfileSO lodProfile)
        {
            if (blueprint == null)
            {
                throw new ArgumentNullException(nameof(blueprint));
            }

            if (string.IsNullOrWhiteSpace(assemblyPrefabPath))
            {
                throw new ArgumentException("Assembly prefab path is required.", nameof(assemblyPrefabPath));
            }

            if (branchPrototypes == null || branchPrototypes.Count == 0)
            {
                throw new ArgumentException("At least one branch prototype is required.", nameof(branchPrototypes));
            }

            if (lodProfile == null)
            {
                throw new ArgumentNullException(nameof(lodProfile));
            }

            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(assemblyPrefabPath);
            try
            {
                RefreshBlueprintFromAssemblyHierarchy(blueprint, prefabRoot, branchPrototypes, lodProfile);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }

        private static void RefreshBlueprintFromAssemblyHierarchy(
            TreeBlueprintSO blueprint,
            GameObject assemblyRoot,
            IReadOnlyList<BranchPrototypeSO> branchPrototypes,
            LODProfileSO lodProfile)
        {
            Mesh trunkMesh = blueprint.TrunkMesh ?? throw new InvalidOperationException($"{blueprint.name} is missing trunkMesh.");
            List<BranchPlacementRecord> placements = CollectBranchPlacements(
                assemblyRoot.transform,
                trunkMesh,
                branchPrototypes,
                out int quantizedPlacementCount,
                out float maxQuantizationDelta);

            if (placements.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Assembly root '{assemblyRoot.name}' did not resolve any branch placements for blueprint '{blueprint.name}'.");
            }

            SerializedObject serializedBlueprint = new SerializedObject(blueprint);
            SerializedProperty branchesProperty = serializedBlueprint.FindProperty("branches");
            branchesProperty.arraySize = placements.Count;

            for (int i = 0; i < placements.Count; i++)
            {
                SerializedProperty placementProperty = branchesProperty.GetArrayElementAtIndex(i);
                BranchPlacementRecord placement = placements[i];

                placementProperty.FindPropertyRelative("prototype").objectReferenceValue = placement.Prototype;
                placementProperty.FindPropertyRelative("localPosition").vector3Value = placement.LocalPosition;
                placementProperty.FindPropertyRelative("localRotation").quaternionValue = placement.LocalRotation;
                placementProperty.FindPropertyRelative("scale").floatValue = placement.Scale;
            }

            serializedBlueprint.FindProperty("lodProfile").objectReferenceValue = lodProfile;
            serializedBlueprint.FindProperty("treeBounds").boundsValue = ComputeTreeBounds(trunkMesh.bounds, placements);
            serializedBlueprint.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(blueprint);

            if (quantizedPlacementCount > 0)
            {
                Debug.LogWarning(
                    $"Refreshed '{blueprint.name}' with {placements.Count} branch placements. " +
                    $"Quantized {quantizedPlacementCount} placement scales to 0.25 steps (max delta {maxQuantizationDelta:F4}).");
            }
        }

        private static List<BranchPlacementRecord> CollectBranchPlacements(
            Transform assemblyRoot,
            Mesh trunkMesh,
            IReadOnlyList<BranchPrototypeSO> branchPrototypes,
            out int quantizedPlacementCount,
            out float maxQuantizationDelta)
        {
            List<BranchPlacementRecord> placements = new List<BranchPlacementRecord>();
            List<string> unresolvedPrefabRoots = new List<string>();
            Transform[] transforms = assemblyRoot.GetComponentsInChildren<Transform>(true);
            quantizedPlacementCount = 0;
            maxQuantizationDelta = 0f;

            for (int i = 0; i < transforms.Length; i++)
            {
                Transform candidate = transforms[i];
                if (candidate == assemblyRoot || !PrefabUtility.IsAnyPrefabInstanceRoot(candidate.gameObject))
                {
                    continue;
                }

                if (TryResolvePrototype(candidate.gameObject, branchPrototypes, out BranchPrototypeSO? prototype))
                {
                    BranchPlacementRecord placement = CreatePlacementRecord(assemblyRoot, candidate, prototype!);
                    placements.Add(placement);

                    float quantizationDelta = Mathf.Abs(ExtractUniformScale(assemblyRoot, candidate) - placement.Scale);
                    if (quantizationDelta > 0f)
                    {
                        quantizedPlacementCount++;
                        maxQuantizationDelta = Mathf.Max(maxQuantizationDelta, quantizationDelta);
                    }

                    continue;
                }

                if (ContainsMesh(candidate.gameObject, trunkMesh))
                {
                    continue;
                }

                if (candidate.GetComponentsInChildren<MeshFilter>(true).Length > 0)
                {
                    string prefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(candidate.gameObject);
                    unresolvedPrefabRoots.Add(
                        string.IsNullOrEmpty(prefabPath)
                            ? candidate.name
                            : $"{candidate.name} ({prefabPath})");
                }
            }

            if (unresolvedPrefabRoots.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Assembly root '{assemblyRoot.name}' contains prefab instances without a matching BranchPrototypeSO: " +
                    string.Join(", ", unresolvedPrefabRoots));
            }

            return placements;
        }

        private static BranchPlacementRecord CreatePlacementRecord(
            Transform assemblyRoot,
            Transform branchRoot,
            BranchPrototypeSO prototype)
        {
            Matrix4x4 relativeMatrix = assemblyRoot.worldToLocalMatrix * branchRoot.localToWorldMatrix;
            Vector3 position = relativeMatrix.GetColumn(3);
            Quaternion rotation = ExtractRotation(relativeMatrix);
            float scale = QuantizeScaleToQuarterStep(ExtractUniformScale(assemblyRoot, branchRoot));

            return new BranchPlacementRecord(prototype, position, rotation, scale);
        }

        private static Quaternion ExtractRotation(Matrix4x4 matrix)
        {
            Vector3 right = matrix.GetColumn(0);
            Vector3 up = matrix.GetColumn(1);
            Vector3 forward = matrix.GetColumn(2);

            if (right.sqrMagnitude <= 0f || up.sqrMagnitude <= 0f || forward.sqrMagnitude <= 0f)
            {
                throw new InvalidOperationException("Cannot extract rotation from a zero-scale branch placement.");
            }

            right.Normalize();
            up.Normalize();
            forward.Normalize();

            float handedness = Vector3.Dot(Vector3.Cross(right, up), forward);
            if (handedness <= 0f)
            {
                throw new InvalidOperationException("Mirrored branch placements are not supported by the vegetation authoring contract.");
            }

            return Quaternion.LookRotation(forward, up);
        }

        private static float ExtractUniformScale(Transform assemblyRoot, Transform branchRoot)
        {
            Matrix4x4 relativeMatrix = assemblyRoot.worldToLocalMatrix * branchRoot.localToWorldMatrix;
            float scaleX = relativeMatrix.GetColumn(0).magnitude;
            float scaleY = relativeMatrix.GetColumn(1).magnitude;
            float scaleZ = relativeMatrix.GetColumn(2).magnitude;

            if (!Mathf.Approximately(scaleX, 0f) &&
                (Mathf.Abs(scaleX - scaleY) > UniformScaleTolerance || Mathf.Abs(scaleX - scaleZ) > UniformScaleTolerance))
            {
                throw new InvalidOperationException(
                    $"Branch placement '{branchRoot.name}' is not uniformly scaled ({scaleX:F4}, {scaleY:F4}, {scaleZ:F4}).");
            }

            return scaleX;
        }

        private static float QuantizeScaleToQuarterStep(float scale)
        {
            if (scale <= 0f)
            {
                throw new InvalidOperationException($"Branch placement scale must stay positive, but got {scale:F4}.");
            }

            return Mathf.Round(scale / ScaleStep) * ScaleStep;
        }

        private static bool TryResolvePrototype(
            GameObject prefabInstanceRoot,
            IReadOnlyList<BranchPrototypeSO> branchPrototypes,
            out BranchPrototypeSO? prototype)
        {
            List<BranchPrototypeSO> matches = new List<BranchPrototypeSO>();
            MeshFilter[] meshFilters = prefabInstanceRoot.GetComponentsInChildren<MeshFilter>(true);

            for (int i = 0; i < branchPrototypes.Count; i++)
            {
                BranchPrototypeSO candidate = branchPrototypes[i];
                Mesh? woodMesh = candidate.WoodMesh;
                Mesh? foliageMesh = candidate.FoliageMesh;
                if (woodMesh == null || foliageMesh == null)
                {
                    continue;
                }

                bool hasWoodMesh = false;
                bool hasFoliageMesh = false;
                for (int meshIndex = 0; meshIndex < meshFilters.Length; meshIndex++)
                {
                    Mesh? sharedMesh = meshFilters[meshIndex].sharedMesh;
                    hasWoodMesh |= sharedMesh == woodMesh;
                    hasFoliageMesh |= sharedMesh == foliageMesh;
                }

                if (hasWoodMesh && hasFoliageMesh)
                {
                    matches.Add(candidate);
                }
            }

            if (matches.Count > 1)
            {
                throw new InvalidOperationException(
                    $"Prefab instance '{prefabInstanceRoot.name}' matches multiple branch prototypes: {string.Join(", ", GetAssetNames(matches))}.");
            }

            prototype = matches.Count == 1 ? matches[0] : null;
            return prototype != null;
        }

        private static Bounds ComputeTreeBounds(Bounds trunkBounds, IReadOnlyList<BranchPlacementRecord> placements)
        {
            Bounds treeBounds = trunkBounds;
            for (int i = 0; i < placements.Count; i++)
            {
                BranchPlacementRecord placement = placements[i];
                Bounds branchBounds = TransformBounds(
                    placement.Prototype.LocalBounds,
                    Matrix4x4.TRS(placement.LocalPosition, placement.LocalRotation, Vector3.one * placement.Scale));

                treeBounds.Encapsulate(branchBounds);
            }

            return treeBounds;
        }

        private static Bounds TransformBounds(Bounds bounds, Matrix4x4 matrix)
        {
            Vector3 center = bounds.center;
            Vector3 extents = bounds.extents;
            Vector3[] corners =
            {
                center + new Vector3(-extents.x, -extents.y, -extents.z),
                center + new Vector3(-extents.x, -extents.y, extents.z),
                center + new Vector3(-extents.x, extents.y, -extents.z),
                center + new Vector3(-extents.x, extents.y, extents.z),
                center + new Vector3(extents.x, -extents.y, -extents.z),
                center + new Vector3(extents.x, -extents.y, extents.z),
                center + new Vector3(extents.x, extents.y, -extents.z),
                center + new Vector3(extents.x, extents.y, extents.z)
            };

            Vector3 transformedCorner = matrix.MultiplyPoint3x4(corners[0]);
            Bounds transformedBounds = new Bounds(transformedCorner, Vector3.zero);
            for (int i = 1; i < corners.Length; i++)
            {
                transformedBounds.Encapsulate(matrix.MultiplyPoint3x4(corners[i]));
            }

            return transformedBounds;
        }

        private static bool ContainsMesh(GameObject root, Mesh mesh)
        {
            MeshFilter[] meshFilters = root.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < meshFilters.Length; i++)
            {
                if (meshFilters[i].sharedMesh == mesh)
                {
                    return true;
                }
            }

            return false;
        }

        private static int GetTriangleCount(Mesh mesh)
        {
            return mesh.triangles.Length / 3;
        }

        private static T LoadAssetAtPath<T>(string assetPath) where T : UnityEngine.Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(assetPath);
            if (asset == null)
            {
                throw new InvalidOperationException($"Expected asset '{assetPath}' was not found.");
            }

            return asset;
        }

        private static T[] LoadAssetsAtPath<T>(string assetFolder) where T : UnityEngine.Object
        {
            string[] guids = AssetDatabase.FindAssets($"t:{typeof(T).Name}", new[] { assetFolder });
            T[] assets = new T[guids.Length];
            for (int i = 0; i < guids.Length; i++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);
                assets[i] = LoadAssetAtPath<T>(assetPath);
            }

            return assets;
        }

        private static void ThrowIfValidationHasErrors(string assetName, VegetationValidationResult validationResult)
        {
            if (!validationResult.HasErrors)
            {
                return;
            }

            List<string> messages = new List<string>();
            for (int i = 0; i < validationResult.Issues.Count; i++)
            {
                VegetationValidationIssue issue = validationResult.Issues[i];
                messages.Add($"{issue.Severity}: {issue.Message}");
            }

            throw new InvalidOperationException($"Validation failed for '{assetName}': {string.Join(" | ", messages)}");
        }

        private static string[] GetAssetNames(IReadOnlyList<BranchPrototypeSO> prototypes)
        {
            string[] names = new string[prototypes.Count];
            for (int i = 0; i < prototypes.Count; i++)
            {
                names[i] = prototypes[i].name;
            }

            return names;
        }

        private readonly struct BranchPlacementRecord
        {
            public BranchPlacementRecord(
                BranchPrototypeSO prototype,
                Vector3 localPosition,
                Quaternion localRotation,
                float scale)
            {
                Prototype = prototype;
                LocalPosition = localPosition;
                LocalRotation = localRotation;
                Scale = scale;
            }

            public BranchPrototypeSO Prototype { get; }

            public Vector3 LocalPosition { get; }

            public Quaternion LocalRotation { get; }

            public float Scale { get; }
        }
    }
}
