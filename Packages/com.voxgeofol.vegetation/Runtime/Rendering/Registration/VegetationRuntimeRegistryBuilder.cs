#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;
using VoxGeoFol.Features.Vegetation.Authoring;

namespace VoxGeoFol.Features.Vegetation.Rendering
{
    /// <summary>
    /// Builds the frozen runtime registry from scene authoring data without touching editor-only fields.
    /// The urgent-path shape is tree-first: reusable blueprint placements and prototype tier meshes are static,
    /// while expanded branch work is derived later per frame from accepted trees only.
    /// </summary>
    public sealed class VegetationRuntimeRegistryBuilder
    {
        // Range-Condition-Output: convert per-instance index count into coarse work units so
        // one huge canopy mesh cannot consume the same acceptance budget as one tiny impostor.
        private const int IndirectWorkCostIndexQuantum = 1024;
        private readonly Vector3 gridOrigin;
        private readonly Vector3 cellSize;
        private readonly int maxRegisteredDrawSlots;
        private readonly List<VegetationDrawSlot> drawSlots = new List<VegetationDrawSlot>();
        private readonly Dictionary<LODProfileSO, int> lodProfileIndices = new Dictionary<LODProfileSO, int>();
        private readonly Dictionary<TreeBlueprintSO, int> blueprintIndices = new Dictionary<TreeBlueprintSO, int>();
        private readonly Dictionary<BranchPrototypeSO, int> prototypeIndices = new Dictionary<BranchPrototypeSO, int>();
        private readonly Dictionary<DrawSlotKey, int> drawSlotIndices = new Dictionary<DrawSlotKey, int>();
        private readonly List<VegetationLodProfileRuntime> lodProfiles = new List<VegetationLodProfileRuntime>();
        private readonly List<VegetationTreeBlueprintRuntime> treeBlueprints = new List<VegetationTreeBlueprintRuntime>();
        private readonly List<VegetationBlueprintBranchPlacementRuntime> blueprintBranchPlacements = new List<VegetationBlueprintBranchPlacementRuntime>();
        private readonly List<VegetationBranchPrototypeRuntime> branchPrototypes = new List<VegetationBranchPrototypeRuntime>();
        private readonly List<VegetationTreeInstanceRuntime> treeInstances = new List<VegetationTreeInstanceRuntime>();

        public VegetationRuntimeRegistryBuilder(Vector3 gridOrigin, Vector3 cellSize, int maxRegisteredDrawSlots = int.MaxValue)
        {
            this.gridOrigin = gridOrigin;
            this.cellSize = cellSize;
            this.maxRegisteredDrawSlots = Mathf.Max(1, maxRegisteredDrawSlots);
        }

        /// <summary>
        /// [INTEGRATION] Builds the runtime registration/flattening snapshot for all active scene vegetation authorings.
        /// </summary>
        public VegetationRuntimeRegistry Build(IReadOnlyList<VegetationTreeAuthoringRuntime> authorings)
        {
            if (authorings == null)
            {
                throw new ArgumentNullException(nameof(authorings));
            }

            for (int authoringIndex = 0; authoringIndex < authorings.Count; authoringIndex++)
            {
                VegetationTreeAuthoringRuntime authoring = authorings[authoringIndex] ??
                                                           throw new InvalidOperationException($"VegetationTreeAuthoringRuntime[{authoringIndex}] is missing.");
                if (!authoring.IsActive)
                {
                    continue;
                }

                TreeBlueprintSO blueprint = authoring.Blueprint ??
                                            throw new InvalidOperationException($"{authoring.DebugName} is missing blueprint and cannot enter urgent runtime registration.");

                int blueprintIndex = RegisterBlueprint(blueprint);
                VegetationTreeBlueprintRuntime blueprintRuntime = treeBlueprints[blueprintIndex];
                Matrix4x4 treeMatrix = authoring.LocalToWorld;
                Matrix4x4 treeWorldToObject = treeMatrix.inverse;
                Bounds treeWorldBounds = VegetationRuntimeMathUtility.TransformBounds(blueprint.TreeBounds, treeMatrix);
                Vector3 treeSphereCenter = treeWorldBounds.center;
                float treeSphereRadius = treeWorldBounds.extents.magnitude;

                treeInstances.Add(new VegetationTreeInstanceRuntime
                {
                    Authoring = authoring,
                    LocalToWorld = treeMatrix,
                    WorldToObject = treeWorldToObject,
                    WorldBounds = treeWorldBounds,
                    TrunkFullWorldBounds = TransformDrawSlotBounds(blueprintRuntime.TrunkFullDrawSlot, treeMatrix),
                    TrunkL3WorldBounds = TransformDrawSlotBounds(blueprintRuntime.TrunkL3DrawSlot, treeMatrix),
                    TreeL3WorldBounds = TransformDrawSlotBounds(blueprintRuntime.TreeL3DrawSlot, treeMatrix),
                    ImpostorWorldBounds = TransformDrawSlotBounds(blueprintRuntime.ImpostorDrawSlot, treeMatrix),
                    SphereCenterWorld = treeSphereCenter,
                    BoundingSphereRadius = treeSphereRadius,
                    BlueprintIndex = blueprintIndex,
                    CellIndex = -1,
                    UploadInstanceData = CreateUploadInstanceData(treeMatrix, treeWorldToObject, 0u)
                });
            }

            VegetationSpatialGrid spatialGrid = VegetationSpatialGrid.Build(gridOrigin, cellSize, treeInstances);
            return new VegetationRuntimeRegistry(
                drawSlots.ToArray(),
                BuildDrawSlotConservativeBounds(),
                lodProfiles.ToArray(),
                treeBlueprints.ToArray(),
                blueprintBranchPlacements.ToArray(),
                branchPrototypes.ToArray(),
                treeInstances.ToArray(),
                spatialGrid);
        }

        private Bounds[] BuildDrawSlotConservativeBounds()
        {
            int slotCount = drawSlots.Count;
            Bounds[] drawSlotConservativeWorldBounds = new Bounds[slotCount];
            bool[] hasBounds = new bool[slotCount];
            bool hasSceneBounds = false;
            Bounds sceneBounds = default;

            for (int treeIndex = 0; treeIndex < treeInstances.Count; treeIndex++)
            {
                VegetationTreeInstanceRuntime treeInstance = treeInstances[treeIndex];
                VegetationTreeBlueprintRuntime blueprint = treeBlueprints[treeInstance.BlueprintIndex];

                if (!hasSceneBounds)
                {
                    sceneBounds = treeInstance.WorldBounds;
                    hasSceneBounds = true;
                }
                else
                {
                    sceneBounds.Encapsulate(treeInstance.WorldBounds);
                }

                AccumulateSlot(drawSlotConservativeWorldBounds, hasBounds, blueprint.ImpostorDrawSlot, treeInstance.ImpostorWorldBounds);
                AccumulateSlot(drawSlotConservativeWorldBounds, hasBounds, blueprint.TreeL3DrawSlot, treeInstance.TreeL3WorldBounds);
                AccumulateSlot(drawSlotConservativeWorldBounds, hasBounds, blueprint.TrunkFullDrawSlot, treeInstance.TrunkFullWorldBounds);
                AccumulateSlot(drawSlotConservativeWorldBounds, hasBounds, blueprint.TrunkL3DrawSlot, treeInstance.TrunkL3WorldBounds);

                for (int branchOffset = 0; branchOffset < blueprint.BranchPlacementCount; branchOffset++)
                {
                    VegetationBlueprintBranchPlacementRuntime placement = blueprintBranchPlacements[blueprint.BranchPlacementStartIndex + branchOffset];
                    VegetationBranchPrototypeRuntime prototype = branchPrototypes[placement.PrototypeIndex];
                    Matrix4x4 branchWorldMatrix = treeInstance.LocalToWorld * placement.LocalToTree;
                    Bounds branchWorldBounds = TransformBounds(prototype.LocalBoundsCenter, prototype.LocalBoundsExtents, branchWorldMatrix);

                    AccumulateSlot(drawSlotConservativeWorldBounds, hasBounds, prototype.WoodDrawSlotL0, branchWorldBounds);
                    AccumulateSlot(drawSlotConservativeWorldBounds, hasBounds, prototype.FoliageDrawSlotL0, branchWorldBounds);
                    AccumulateSlot(drawSlotConservativeWorldBounds, hasBounds, prototype.WoodDrawSlotL1, branchWorldBounds);
                    AccumulateSlot(drawSlotConservativeWorldBounds, hasBounds, prototype.CanopyDrawSlotL1, branchWorldBounds);
                    AccumulateSlot(drawSlotConservativeWorldBounds, hasBounds, prototype.WoodDrawSlotL2, branchWorldBounds);
                    AccumulateSlot(drawSlotConservativeWorldBounds, hasBounds, prototype.CanopyDrawSlotL2, branchWorldBounds);
                    AccumulateSlot(drawSlotConservativeWorldBounds, hasBounds, prototype.WoodDrawSlotL3, branchWorldBounds);
                    AccumulateSlot(drawSlotConservativeWorldBounds, hasBounds, prototype.CanopyDrawSlotL3, branchWorldBounds);
                }
            }

            Bounds fallbackBounds = hasSceneBounds
                ? sceneBounds
                : new Bounds(Vector3.zero, Vector3.zero);

            for (int slotIndex = 0; slotIndex < slotCount; slotIndex++)
            {
                if (!hasBounds[slotIndex])
                {
                    drawSlotConservativeWorldBounds[slotIndex] = fallbackBounds;
                }
            }

            return drawSlotConservativeWorldBounds;
        }

        private static void AccumulateSlot(
            Bounds[] drawSlotConservativeWorldBounds,
            bool[] hasBounds,
            int slotIndex,
            Bounds worldBounds)
        {
            if (!hasBounds[slotIndex])
            {
                drawSlotConservativeWorldBounds[slotIndex] = worldBounds;
                hasBounds[slotIndex] = true;
                return;
            }

            drawSlotConservativeWorldBounds[slotIndex].Encapsulate(worldBounds);
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
                                      throw new InvalidOperationException($"{blueprint.name} is missing lodProfile and cannot enter urgent runtime registration.");

            Mesh trunkMesh = blueprint.TrunkMesh ??
                             throw new InvalidOperationException($"{blueprint.name} is missing trunkMesh and cannot enter urgent runtime registration.");
            Material trunkMaterial = blueprint.TrunkMaterial ??
                                     throw new InvalidOperationException($"{blueprint.name} is missing trunkMaterial and cannot enter urgent runtime registration.");
            Mesh trunkL3Mesh = blueprint.TrunkL3Mesh ??
                               throw new InvalidOperationException($"{blueprint.name} is missing trunkL3Mesh and cannot enter urgent runtime registration.");
            Mesh treeL3Mesh = blueprint.TreeL3Mesh ??
                              throw new InvalidOperationException($"{blueprint.name} is missing treeL3Mesh and cannot enter urgent runtime registration.");
            Mesh impostorMesh = blueprint.ImpostorMesh ??
                                throw new InvalidOperationException($"{blueprint.name} is missing impostorMesh and cannot enter urgent runtime registration.");
            Material impostorMaterial = blueprint.ImpostorMaterial ??
                                        throw new InvalidOperationException($"{blueprint.name} is missing impostorMaterial and cannot enter urgent runtime registration.");

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
                Matrix4x4 localToTree = Matrix4x4.TRS(
                    placement.LocalPosition,
                    placement.LocalRotation,
                    Vector3.one * placement.Scale);
                blueprintBranchPlacements.Add(new VegetationBlueprintBranchPlacementRuntime
                {
                    LocalToTree = localToTree,
                    TreeToLocal = localToTree.inverse,
                    PrototypeIndex = prototypeIndex,
                    LocalBoundsCenter = prototype.LocalBounds.center,
                    LocalBoundsExtents = prototype.LocalBounds.extents,
                    BoundingSphereRadius = branchRadius
                });
            }

            int trunkFullDrawSlot = RegisterDrawSlot(trunkMesh, trunkMaterial, VegetationRenderMaterialKind.Trunk, $"{blueprint.name}:TrunkFull");
            int trunkL3DrawSlot = RegisterDrawSlot(trunkL3Mesh, trunkMaterial, VegetationRenderMaterialKind.Trunk, $"{blueprint.name}:TrunkL3");
            int treeL3DrawSlot = RegisterDrawSlot(treeL3Mesh, impostorMaterial, VegetationRenderMaterialKind.FarMesh, $"{blueprint.name}:TreeL3");
            int impostorDrawSlot = RegisterDrawSlot(impostorMesh, impostorMaterial, VegetationRenderMaterialKind.FarMesh, $"{blueprint.name}:Impostor");
            int expandedTierCostL2 = ComputeDrawSlotWorkCost(trunkL3DrawSlot);
            int expandedTierCostL1 = ComputeDrawSlotWorkCost(trunkFullDrawSlot);
            int expandedTierCostL0 = ComputeDrawSlotWorkCost(trunkFullDrawSlot);
            for (int branchPlacementIndex = branchPlacementStart;
                 branchPlacementIndex < blueprintBranchPlacements.Count;
                 branchPlacementIndex++)
            {
                VegetationBlueprintBranchPlacementRuntime placement = blueprintBranchPlacements[branchPlacementIndex];
                VegetationBranchPrototypeRuntime prototypeRuntime = branchPrototypes[placement.PrototypeIndex];
                expandedTierCostL2 += ComputePrototypeTierWorkCost(prototypeRuntime.WoodDrawSlotL2, prototypeRuntime.CanopyDrawSlotL2);
                expandedTierCostL1 += ComputePrototypeTierWorkCost(prototypeRuntime.WoodDrawSlotL1, prototypeRuntime.CanopyDrawSlotL1);
                expandedTierCostL0 += ComputePrototypeTierWorkCost(prototypeRuntime.WoodDrawSlotL0, prototypeRuntime.FoliageDrawSlotL0);
            }

            treeBlueprints.Add(new VegetationTreeBlueprintRuntime
            {
                LodProfileIndex = RegisterLodProfile(lodProfile),
                BranchPlacementStartIndex = branchPlacementStart,
                BranchPlacementCount = blueprint.Branches.Length,
                TrunkFullDrawSlot = trunkFullDrawSlot,
                TrunkL3DrawSlot = trunkL3DrawSlot,
                TreeL3DrawSlot = treeL3DrawSlot,
                ImpostorDrawSlot = impostorDrawSlot,
                TreeL3WorkCost = ComputeDrawSlotWorkCost(treeL3DrawSlot),
                ImpostorWorkCost = ComputeDrawSlotWorkCost(impostorDrawSlot),
                ExpandedTierCostL2 = expandedTierCostL2,
                ExpandedTierCostL1 = expandedTierCostL1,
                ExpandedTierCostL0 = expandedTierCostL0
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
                            throw new InvalidOperationException($"{prototype.name} is missing woodMesh and cannot enter urgent runtime registration.");
            Material woodMaterial = prototype.WoodMaterial ??
                                    throw new InvalidOperationException($"{prototype.name} is missing woodMaterial and cannot enter urgent runtime registration.");
            Mesh foliageMesh = prototype.FoliageMesh ??
                               throw new InvalidOperationException($"{prototype.name} is missing foliageMesh and cannot enter urgent runtime registration.");
            Material foliageMaterial = prototype.FoliageMaterial ??
                                       throw new InvalidOperationException($"{prototype.name} is missing foliageMaterial and cannot enter urgent runtime registration.");
            Material shellMaterial = prototype.ShellMaterial ??
                                     throw new InvalidOperationException($"{prototype.name} is missing shellMaterial and cannot enter urgent runtime registration.");
            Mesh branchL1WoodMesh = prototype.BranchL1WoodMesh ??
                                    throw new InvalidOperationException($"{prototype.name} is missing branchL1WoodMesh and cannot enter urgent runtime registration.");
            Mesh branchL2WoodMesh = prototype.BranchL2WoodMesh ??
                                    throw new InvalidOperationException($"{prototype.name} is missing branchL2WoodMesh and cannot enter urgent runtime registration.");
            Mesh branchL3WoodMesh = prototype.BranchL3WoodMesh ??
                                    throw new InvalidOperationException($"{prototype.name} is missing branchL3WoodMesh and cannot enter urgent runtime registration.");
            Mesh branchL1CanopyMesh = prototype.BranchL1CanopyMesh ??
                                      throw new InvalidOperationException($"{prototype.name} is missing branchL1CanopyMesh and cannot enter urgent runtime registration.");
            Mesh branchL2CanopyMesh = prototype.BranchL2CanopyMesh ??
                                      throw new InvalidOperationException($"{prototype.name} is missing branchL2CanopyMesh and cannot enter urgent runtime registration.");
            Mesh branchL3CanopyMesh = prototype.BranchL3CanopyMesh ??
                                      throw new InvalidOperationException($"{prototype.name} is missing branchL3CanopyMesh and cannot enter urgent runtime registration.");

            int prototypeIndex = branchPrototypes.Count;
            branchPrototypes.Add(new VegetationBranchPrototypeRuntime
            {
                WoodDrawSlotL0 = RegisterDrawSlot(woodMesh, woodMaterial, VegetationRenderMaterialKind.Trunk, $"{prototype.name}:WoodL0"),
                FoliageDrawSlotL0 = RegisterDrawSlot(foliageMesh, foliageMaterial, VegetationRenderMaterialKind.CanopyFoliage, $"{prototype.name}:FoliageL0"),
                WoodDrawSlotL1 = RegisterDrawSlot(branchL1WoodMesh, woodMaterial, VegetationRenderMaterialKind.Trunk, $"{prototype.name}:WoodL1"),
                CanopyDrawSlotL1 = RegisterDrawSlot(branchL1CanopyMesh, shellMaterial, VegetationRenderMaterialKind.CanopyShell, $"{prototype.name}:CanopyL1"),
                WoodDrawSlotL2 = RegisterDrawSlot(branchL2WoodMesh, woodMaterial, VegetationRenderMaterialKind.Trunk, $"{prototype.name}:WoodL2"),
                CanopyDrawSlotL2 = RegisterDrawSlot(branchL2CanopyMesh, shellMaterial, VegetationRenderMaterialKind.CanopyShell, $"{prototype.name}:CanopyL2"),
                WoodDrawSlotL3 = RegisterDrawSlot(branchL3WoodMesh, woodMaterial, VegetationRenderMaterialKind.Trunk, $"{prototype.name}:WoodL3"),
                CanopyDrawSlotL3 = RegisterDrawSlot(branchL3CanopyMesh, shellMaterial, VegetationRenderMaterialKind.CanopyShell, $"{prototype.name}:CanopyL3"),
                PackedLeafTint = VegetationRuntimeMathUtility.PackColorToUint(prototype.LeafColorTint),
                LocalBoundsCenter = prototype.LocalBounds.center,
                LocalBoundsExtents = prototype.LocalBounds.extents
            });

            prototypeIndices.Add(prototype, prototypeIndex);
            return prototypeIndex;
        }

        private Bounds TransformDrawSlotBounds(int drawSlotIndex, Matrix4x4 localToWorld)
        {
            return VegetationRuntimeMathUtility.TransformBounds(drawSlots[drawSlotIndex].LocalBounds, localToWorld);
        }

        private static Bounds TransformBounds(Vector3 localCenter, Vector3 localExtents, Matrix4x4 localToWorld)
        {
            return VegetationRuntimeMathUtility.TransformBounds(new Bounds(localCenter, localExtents * 2f), localToWorld);
        }

        private static VegetationIndirectInstanceData CreateUploadInstanceData(Matrix4x4 localToWorld, Matrix4x4 worldToObject, uint packedLeafTint)
        {
            return new VegetationIndirectInstanceData
            {
                ObjectToWorld = localToWorld,
                WorldToObject = worldToObject,
                PackedLeafTint = packedLeafTint
            };
        }

        private int RegisterDrawSlot(Mesh mesh, Material material, VegetationRenderMaterialKind materialKind, string debugLabel)
        {
            DrawSlotKey key = new DrawSlotKey(mesh.GetInstanceID(), material.GetInstanceID(), materialKind);
            if (drawSlotIndices.TryGetValue(key, out int existingIndex))
            {
                return existingIndex;
            }

            if (drawSlots.Count >= maxRegisteredDrawSlots)
            {
                throw new InvalidOperationException(
                    $"Runtime registration exceeded the configured registered draw-slot cap ({maxRegisteredDrawSlots}) while adding '{debugLabel}'.");
            }

            int slotIndex = drawSlots.Count;
            drawSlots.Add(new VegetationDrawSlot(slotIndex, mesh, material, materialKind, debugLabel));
            drawSlotIndices.Add(key, slotIndex);
            return slotIndex;
        }

        private int ComputeDrawSlotWorkCost(int drawSlotIndex)
        {
            VegetationDrawSlot drawSlot = drawSlots[drawSlotIndex];
            int indexCountPerInstance = checked((int)Math.Max(1u, drawSlot.IndexCountPerInstance));
            return Math.Max(1, (indexCountPerInstance + (IndirectWorkCostIndexQuantum - 1)) / IndirectWorkCostIndexQuantum);
        }

        private int ComputePrototypeTierWorkCost(int woodDrawSlot, int canopyDrawSlot)
        {
            return checked(ComputeDrawSlotWorkCost(woodDrawSlot) + ComputeDrawSlotWorkCost(canopyDrawSlot));
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
