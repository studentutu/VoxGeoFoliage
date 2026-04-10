#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;
using VoxGeoFol.Features.Vegetation.Authoring;

namespace VoxGeoFol.Features.Vegetation.Rendering
{
    /// <summary>
    /// Builds the frozen Phase D runtime registry from scene authoring data without touching editor-only fields.
    /// </summary>
    public sealed class VegetationRuntimeRegistryBuilder
    {
        private readonly Vector3 gridOrigin;
        private readonly Vector3 cellSize;
        private readonly List<VegetationDrawSlot> drawSlots = new List<VegetationDrawSlot>();
        private readonly Dictionary<LODProfileSO, int> lodProfileIndices = new Dictionary<LODProfileSO, int>();
        private readonly Dictionary<TreeBlueprintSO, int> blueprintIndices = new Dictionary<TreeBlueprintSO, int>();
        private readonly Dictionary<BranchPrototypeSO, int> prototypeIndices = new Dictionary<BranchPrototypeSO, int>();
        private readonly Dictionary<DrawSlotKey, int> drawSlotIndices = new Dictionary<DrawSlotKey, int>();
        private readonly List<VegetationLodProfileRuntime> lodProfiles = new List<VegetationLodProfileRuntime>();
        private readonly List<VegetationTreeBlueprintRuntime> treeBlueprints = new List<VegetationTreeBlueprintRuntime>();
        private readonly List<VegetationBlueprintBranchPlacementRuntime> blueprintBranchPlacements = new List<VegetationBlueprintBranchPlacementRuntime>();
        private readonly List<VegetationBranchPrototypeRuntime> branchPrototypes = new List<VegetationBranchPrototypeRuntime>();
        private readonly List<VegetationBranchShellNodeRuntimeBfs> shellNodesL1 = new List<VegetationBranchShellNodeRuntimeBfs>();
        private readonly List<VegetationBranchShellNodeRuntimeBfs> shellNodesL2 = new List<VegetationBranchShellNodeRuntimeBfs>();
        private readonly List<VegetationBranchShellNodeRuntimeBfs> shellNodesL3 = new List<VegetationBranchShellNodeRuntimeBfs>();
        private readonly List<VegetationTreeInstanceRuntime> treeInstances = new List<VegetationTreeInstanceRuntime>();
        private readonly List<VegetationSceneBranchRuntime> sceneBranches = new List<VegetationSceneBranchRuntime>();
        private int totalNodeDecisionCapacity;

        public VegetationRuntimeRegistryBuilder(Vector3 gridOrigin, Vector3 cellSize)
        {
            this.gridOrigin = gridOrigin;
            this.cellSize = cellSize;
        }

        /// <summary>
        /// [INTEGRATION] Builds the runtime registration/flattening snapshot for all active scene vegetation authorings.
        /// </summary>
        public VegetationRuntimeRegistry Build(IReadOnlyList<VegetationTreeAuthoring> authorings)
        {
            if (authorings == null)
            {
                throw new ArgumentNullException(nameof(authorings));
            }

            List<VegetationTreeAuthoring> orderedAuthorings = new List<VegetationTreeAuthoring>(authorings);
            orderedAuthorings.Sort(CompareAuthorings);

            for (int authoringIndex = 0; authoringIndex < orderedAuthorings.Count; authoringIndex++)
            {
                VegetationTreeAuthoring authoring = orderedAuthorings[authoringIndex] ??
                                                    throw new InvalidOperationException($"VegetationTreeAuthoring[{authoringIndex}] is missing.");

                TreeBlueprintSO blueprint = authoring.Blueprint ??
                                            throw new InvalidOperationException($"{authoring.name} is missing blueprint and cannot enter Phase D runtime registration.");

                int blueprintIndex = RegisterBlueprint(blueprint);
                Matrix4x4 treeMatrix = authoring.transform.localToWorldMatrix;
                Bounds treeWorldBounds = VegetationRuntimeMathUtility.TransformBounds(blueprint.TreeBounds, treeMatrix);
                Vector3 treeSphereCenter = treeWorldBounds.center;
                float treeSphereRadius = treeWorldBounds.extents.magnitude;
                int sceneBranchStart = sceneBranches.Count;

                for (int branchPlacementIndex = 0; branchPlacementIndex < blueprint.Branches.Length; branchPlacementIndex++)
                {
                    BranchPlacement placement = blueprint.Branches[branchPlacementIndex] ??
                                               throw new InvalidOperationException($"{blueprint.name}.branches[{branchPlacementIndex}] is missing.");
                    BranchPrototypeSO prototype = placement.Prototype ??
                                                  throw new InvalidOperationException($"{blueprint.name}.branches[{branchPlacementIndex}] is missing prototype.");

                    int prototypeIndex = RegisterPrototype(prototype);
                    Matrix4x4 branchLocalMatrix = Matrix4x4.TRS(
                        placement.LocalPosition,
                        placement.LocalRotation,
                        Vector3.one * placement.Scale);
                    Matrix4x4 branchWorldMatrix = treeMatrix * branchLocalMatrix;
                    Bounds branchWorldBounds = VegetationRuntimeMathUtility.TransformBounds(prototype.LocalBounds, branchWorldMatrix);
                    Vector3 branchSphereCenter = branchWorldBounds.center;
                    float branchSphereRadius = branchWorldBounds.extents.magnitude;

                    VegetationBranchPrototypeRuntime prototypeRuntime = branchPrototypes[prototypeIndex];
                    VegetationSceneBranchRuntime sceneBranch = new VegetationSceneBranchRuntime
                    {
                        TreeIndex = treeInstances.Count,
                        BranchPlacementIndex = branchPlacementIndex,
                        PrototypeIndex = prototypeIndex,
                        LocalToWorld = branchWorldMatrix,
                        WorldBounds = branchWorldBounds,
                        SphereCenterWorld = branchSphereCenter,
                        BoundingSphereRadius = branchSphereRadius,
                        DecisionStartL1 = totalNodeDecisionCapacity,
                        DecisionCountL1 = prototypeRuntime.ShellNodeCountL1,
                        DecisionStartL2 = totalNodeDecisionCapacity + prototypeRuntime.ShellNodeCountL1,
                        DecisionCountL2 = prototypeRuntime.ShellNodeCountL2,
                        DecisionStartL3 = totalNodeDecisionCapacity + prototypeRuntime.ShellNodeCountL1 + prototypeRuntime.ShellNodeCountL2,
                        DecisionCountL3 = prototypeRuntime.ShellNodeCountL3
                    };

                    totalNodeDecisionCapacity += prototypeRuntime.ShellNodeCountL1;
                    totalNodeDecisionCapacity += prototypeRuntime.ShellNodeCountL2;
                    totalNodeDecisionCapacity += prototypeRuntime.ShellNodeCountL3;
                    sceneBranches.Add(sceneBranch);
                }

                treeInstances.Add(new VegetationTreeInstanceRuntime
                {
                    Authoring = authoring,
                    LocalToWorld = treeMatrix,
                    WorldBounds = treeWorldBounds,
                    SphereCenterWorld = treeSphereCenter,
                    BoundingSphereRadius = treeSphereRadius,
                    BlueprintIndex = blueprintIndex,
                    SceneBranchStartIndex = sceneBranchStart,
                    SceneBranchCount = sceneBranches.Count - sceneBranchStart,
                    CellIndex = -1
                });
            }

            VegetationSpatialGrid spatialGrid = VegetationSpatialGrid.Build(gridOrigin, cellSize, treeInstances);
            return new VegetationRuntimeRegistry(
                drawSlots.ToArray(),
                lodProfiles.ToArray(),
                treeBlueprints.ToArray(),
                blueprintBranchPlacements.ToArray(),
                branchPrototypes.ToArray(),
                shellNodesL1.ToArray(),
                shellNodesL2.ToArray(),
                shellNodesL3.ToArray(),
                treeInstances.ToArray(),
                sceneBranches.ToArray(),
                spatialGrid,
                totalNodeDecisionCapacity);
        }

        private int RegisterLodProfile(LODProfileSO lodProfile)
        {
            if (lodProfileIndices.TryGetValue(lodProfile, out int existingIndex))
            {
                return existingIndex;
            }

            int index = lodProfiles.Count;
            lodProfiles.Add(new VegetationLodProfileRuntime
            {
                L0Distance = lodProfile.L0Distance,
                L1Distance = lodProfile.L1Distance,
                L2Distance = lodProfile.L2Distance,
                ImpostorDistance = lodProfile.ImpostorDistance,
                AbsoluteCullDistance = lodProfile.AbsoluteCullDistance
            });
            lodProfileIndices.Add(lodProfile, index);
            return index;
        }

        private int RegisterBlueprint(TreeBlueprintSO blueprint)
        {
            if (blueprintIndices.TryGetValue(blueprint, out int existingIndex))
            {
                return existingIndex;
            }

            LODProfileSO lodProfile = blueprint.LodProfile ??
                                      throw new InvalidOperationException($"{blueprint.name} is missing lodProfile and cannot enter Phase D runtime registration.");

            Mesh trunkMesh = blueprint.TrunkMesh ??
                             throw new InvalidOperationException($"{blueprint.name} is missing trunkMesh and cannot enter Phase D runtime registration.");
            Material trunkMaterial = blueprint.TrunkMaterial ??
                                     throw new InvalidOperationException($"{blueprint.name} is missing trunkMaterial and cannot enter Phase D runtime registration.");
            Mesh trunkL3Mesh = blueprint.TrunkL3Mesh ??
                               throw new InvalidOperationException($"{blueprint.name} is missing trunkL3Mesh and cannot enter Phase D runtime registration.");
            Mesh impostorMesh = blueprint.ImpostorMesh ??
                                throw new InvalidOperationException($"{blueprint.name} is missing impostorMesh and cannot enter Phase D runtime registration.");
            Material impostorMaterial = blueprint.ImpostorMaterial ??
                                        throw new InvalidOperationException($"{blueprint.name} is missing impostorMaterial and cannot enter Phase D runtime registration.");

            int blueprintIndex = treeBlueprints.Count;
            int branchPlacementStart = blueprintBranchPlacements.Count;
            for (int i = 0; i < blueprint.Branches.Length; i++)
            {
                BranchPlacement placement = blueprint.Branches[i] ??
                                           throw new InvalidOperationException($"{blueprint.name}.branches[{i}] is missing.");
                BranchPrototypeSO prototype = placement.Prototype ??
                                              throw new InvalidOperationException($"{blueprint.name}.branches[{i}] is missing prototype.");

                int prototypeIndex = RegisterPrototype(prototype);
                float branchRadius = prototype.LocalBounds.extents.magnitude * placement.Scale;
                blueprintBranchPlacements.Add(new VegetationBlueprintBranchPlacementRuntime
                {
                    LocalPosition = placement.LocalPosition,
                    LocalRotation = placement.LocalRotation,
                    Scale = placement.Scale,
                    PrototypeIndex = prototypeIndex,
                    LocalBoundsCenter = prototype.LocalBounds.center,
                    LocalBoundsExtents = prototype.LocalBounds.extents,
                    BoundingSphereRadius = branchRadius
                });
            }

            treeBlueprints.Add(new VegetationTreeBlueprintRuntime
            {
                LodProfileIndex = RegisterLodProfile(lodProfile),
                BranchPlacementStartIndex = branchPlacementStart,
                BranchPlacementCount = blueprint.Branches.Length,
                TrunkFullDrawSlot = RegisterDrawSlot(trunkMesh, trunkMaterial, VegetationRenderMaterialKind.Trunk, $"{blueprint.name}:TrunkFull"),
                TrunkL3DrawSlot = RegisterDrawSlot(trunkL3Mesh, trunkMaterial, VegetationRenderMaterialKind.Trunk, $"{blueprint.name}:TrunkL3"),
                ImpostorDrawSlot = RegisterDrawSlot(impostorMesh, impostorMaterial, VegetationRenderMaterialKind.FarMesh, $"{blueprint.name}:Impostor")
            });

            blueprintIndices.Add(blueprint, blueprintIndex);
            return blueprintIndex;
        }

        private int RegisterPrototype(BranchPrototypeSO prototype)
        {
            if (prototypeIndices.TryGetValue(prototype, out int existingIndex))
            {
                return existingIndex;
            }

            Mesh woodMesh = prototype.WoodMesh ??
                            throw new InvalidOperationException($"{prototype.name} is missing woodMesh and cannot enter Phase D runtime registration.");
            Material woodMaterial = prototype.WoodMaterial ??
                                    throw new InvalidOperationException($"{prototype.name} is missing woodMaterial and cannot enter Phase D runtime registration.");
            Mesh foliageMesh = prototype.FoliageMesh ??
                               throw new InvalidOperationException($"{prototype.name} is missing foliageMesh and cannot enter Phase D runtime registration.");
            Material foliageMaterial = prototype.FoliageMaterial ??
                                       throw new InvalidOperationException($"{prototype.name} is missing foliageMaterial and cannot enter Phase D runtime registration.");
            Material shellMaterial = prototype.ShellMaterial ??
                                     throw new InvalidOperationException($"{prototype.name} is missing shellMaterial and cannot enter Phase D runtime registration.");
            Mesh shellL1WoodMesh = prototype.ShellL1WoodMesh ??
                                   throw new InvalidOperationException($"{prototype.name} is missing shellL1WoodMesh and cannot enter Phase D runtime registration.");
            Mesh shellL2WoodMesh = prototype.ShellL2WoodMesh ??
                                   throw new InvalidOperationException($"{prototype.name} is missing shellL2WoodMesh and cannot enter Phase D runtime registration.");

            int prototypeIndex = branchPrototypes.Count;
            int shellStartL1 = shellNodesL1.Count;
            AppendShellHierarchy(shellNodesL1, prototype.ShellNodesL0, 0, shellMaterial, $"{prototype.name}:ShellL1");
            int shellStartL2 = shellNodesL2.Count;
            AppendShellHierarchy(shellNodesL2, prototype.ShellNodesL1, 1, shellMaterial, $"{prototype.name}:ShellL2");
            int shellStartL3 = shellNodesL3.Count;
            AppendShellHierarchy(shellNodesL3, prototype.ShellNodesL2, 2, shellMaterial, $"{prototype.name}:ShellL3");

            branchPrototypes.Add(new VegetationBranchPrototypeRuntime
            {
                WoodDrawSlotL0 = RegisterDrawSlot(woodMesh, woodMaterial, VegetationRenderMaterialKind.Trunk, $"{prototype.name}:WoodL0"),
                WoodDrawSlotL1 = RegisterDrawSlot(woodMesh, woodMaterial, VegetationRenderMaterialKind.Trunk, $"{prototype.name}:WoodL1"),
                WoodDrawSlotL2 = RegisterDrawSlot(shellL1WoodMesh, woodMaterial, VegetationRenderMaterialKind.Trunk, $"{prototype.name}:WoodL2"),
                WoodDrawSlotL3 = RegisterDrawSlot(shellL2WoodMesh, woodMaterial, VegetationRenderMaterialKind.Trunk, $"{prototype.name}:WoodL3"),
                FoliageDrawSlotL0 = RegisterDrawSlot(foliageMesh, foliageMaterial, VegetationRenderMaterialKind.CanopyFoliage, $"{prototype.name}:FoliageL0"),
                ShellNodeStartIndexL1 = shellStartL1,
                ShellNodeCountL1 = prototype.ShellNodesL0.Length,
                ShellNodeStartIndexL2 = shellStartL2,
                ShellNodeCountL2 = prototype.ShellNodesL1.Length,
                ShellNodeStartIndexL3 = shellStartL3,
                ShellNodeCountL3 = prototype.ShellNodesL2.Length,
                PackedLeafTint = VegetationRuntimeMathUtility.PackColorToUint(prototype.LeafColorTint),
                LocalBoundsCenter = prototype.LocalBounds.center,
                LocalBoundsExtents = prototype.LocalBounds.extents
            });

            prototypeIndices.Add(prototype, prototypeIndex);
            return prototypeIndex;
        }

        private void AppendShellHierarchy(
            List<VegetationBranchShellNodeRuntimeBfs> targetList,
            BranchShellNode[] sourceNodes,
            int shellLevel,
            Material shellMaterial,
            string debugLabelPrefix)
        {
            if (sourceNodes == null || sourceNodes.Length == 0)
            {
                throw new InvalidOperationException($"{debugLabelPrefix} is missing persisted shell nodes and cannot enter Phase D runtime registration.");
            }

            for (int i = 0; i < sourceNodes.Length; i++)
            {
                BranchShellNode sourceNode = sourceNodes[i] ??
                                             throw new InvalidOperationException($"{debugLabelPrefix}[{i}] is missing.");
                Mesh shellMesh = BranchShellNodeUtility.GetShellMesh(sourceNode, shellLevel) ??
                                 throw new InvalidOperationException($"{debugLabelPrefix}[{i}] is missing shell mesh.");
                targetList.Add(new VegetationBranchShellNodeRuntimeBfs
                {
                    LocalCenter = sourceNode.LocalBounds.center,
                    LocalExtents = sourceNode.LocalBounds.extents,
                    FirstChildIndex = sourceNode.FirstChildIndex,
                    ChildMask = sourceNode.ChildMask,
                    ShellDrawSlot = RegisterDrawSlot(shellMesh, shellMaterial, VegetationRenderMaterialKind.CanopyShell, $"{debugLabelPrefix}[{i}]")
                });
            }
        }

        private int RegisterDrawSlot(Mesh mesh, Material material, VegetationRenderMaterialKind materialKind, string debugLabel)
        {
            DrawSlotKey key = new DrawSlotKey(mesh.GetInstanceID(), material.GetInstanceID(), materialKind);
            if (drawSlotIndices.TryGetValue(key, out int existingIndex))
            {
                return existingIndex;
            }

            int slotIndex = drawSlots.Count;
            drawSlots.Add(new VegetationDrawSlot(slotIndex, mesh, material, materialKind, debugLabel));
            drawSlotIndices.Add(key, slotIndex);
            return slotIndex;
        }

        private static int CompareAuthorings(VegetationTreeAuthoring left, VegetationTreeAuthoring right)
        {
            string leftScenePath = left.gameObject.scene.path ?? string.Empty;
            string rightScenePath = right.gameObject.scene.path ?? string.Empty;
            int compareScenePath = string.CompareOrdinal(leftScenePath, rightScenePath);
            if (compareScenePath != 0)
            {
                return compareScenePath;
            }

            string leftHierarchyPath = BuildHierarchyPath(left.transform);
            string rightHierarchyPath = BuildHierarchyPath(right.transform);
            int compareHierarchyPath = string.CompareOrdinal(leftHierarchyPath, rightHierarchyPath);
            if (compareHierarchyPath != 0)
            {
                return compareHierarchyPath;
            }

            return left.GetInstanceID().CompareTo(right.GetInstanceID());
        }

        private static string BuildHierarchyPath(Transform transform)
        {
            Stack<string> pathParts = new Stack<string>();
            Transform? current = transform;
            while (current != null)
            {
                pathParts.Push($"{current.GetSiblingIndex():D4}:{current.name}");
                current = current.parent;
            }

            return string.Join("/", pathParts.ToArray());
        }

        private readonly struct DrawSlotKey : IEquatable<DrawSlotKey>
        {
            public DrawSlotKey(int meshInstanceId, int materialInstanceId, VegetationRenderMaterialKind materialKind)
            {
                MeshInstanceId = meshInstanceId;
                MaterialInstanceId = materialInstanceId;
                MaterialKind = materialKind;
            }

            public int MeshInstanceId { get; }

            public int MaterialInstanceId { get; }

            public VegetationRenderMaterialKind MaterialKind { get; }

            public bool Equals(DrawSlotKey other)
            {
                return MeshInstanceId == other.MeshInstanceId &&
                       MaterialInstanceId == other.MaterialInstanceId &&
                       MaterialKind == other.MaterialKind;
            }

            public override bool Equals(object? obj)
            {
                return obj is DrawSlotKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = (MeshInstanceId * 397) ^ MaterialInstanceId;
                    return (hash * 397) ^ (int)MaterialKind;
                }
            }
        }
    }
}
