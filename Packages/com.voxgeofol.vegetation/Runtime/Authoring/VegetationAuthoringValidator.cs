#nullable enable

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace VoxGeoFol.Features.Vegetation.Authoring
{

/// <summary>
/// Central validation entry point for vegetation authoring assets.
/// </summary>
public static class VegetationAuthoringValidator
{
    private const float ScaleStep = 0.25f;
    private const float ScaleTolerance = 0.0001f;
    private const float BoundsContainmentEpsilon = 0.0001f;
    private const int RecommendedImpostorTriangleBudget = 200;

    /// <summary>
    /// [INTEGRATION] Validates one branch prototype before shell baking, preview, or runtime registration use it.
    /// </summary>
    public static VegetationValidationResult ValidateBranchPrototype(BranchPrototypeSO? prototype)
    {
        // Range: accepts null so callers get explicit missing-asset errors. Condition: enforces only Task1 branch authoring rules. Output: aggregated branch prototype validation issues.
        VegetationValidationResult result = new VegetationValidationResult();
        if (prototype == null)
        {
            result.AddError("Branch prototype asset is missing.");
            return result;
        }

        ValidateRequiredReadableMesh(prototype.WoodMesh, "woodMesh", result);
        ValidateRequiredReadableMesh(prototype.FoliageMesh, "foliageMesh", result);
        ValidateRequiredOpaqueMaterial(prototype.WoodMaterial, "woodMaterial", result);
        ValidateRequiredOpaqueMaterial(prototype.FoliageMaterial, "foliageMaterial", result);
        ValidateTriangleBudget(prototype.WoodMesh, prototype.TriangleBudgetWood, "woodMesh", result);
        ValidateTriangleBudget(prototype.FoliageMesh, prototype.TriangleBudgetFoliage, "foliageMesh", result);
        ValidateLocalBoundsContainSourceMeshes(prototype, result);
        ValidateShellChain(prototype, result);

        return result;
    }

    /// <summary>
    /// [INTEGRATION] Validates one tree blueprint before preview, shell/impostor baking, or runtime gather consume it.
    /// </summary>
    public static VegetationValidationResult ValidateTreeBlueprint(TreeBlueprintSO? blueprint)
    {
        // Range: accepts null so scene bindings and tests can report explicit missing references. Condition: enforces only Task1 tree authoring rules. Output: aggregated tree blueprint validation issues.
        VegetationValidationResult result = new VegetationValidationResult();
        if (blueprint == null)
        {
            result.AddError("Tree blueprint asset is missing.");
            return result;
        }

        ValidateRequiredReadableMesh(blueprint.TrunkMesh, "trunkMesh", result);
        ValidateRequiredOpaqueMaterial(blueprint.TrunkMaterial, "trunkMaterial", result);
        ValidateBranchPlacements(blueprint, result);
        result.Merge(ValidateLodProfile(blueprint.LodProfile));
        ValidateImpostor(blueprint, result);
        ValidateTreeBounds(blueprint, result);

        return result;
    }

    /// <summary>
    /// [INTEGRATION] Validates one LOD profile before CPU or GPU classification flatten it into runtime buffers.
    /// </summary>
    public static VegetationValidationResult ValidateLodProfile(LODProfileSO? lodProfile)
    {
        // Range: accepts null for explicit missing-profile reporting. Condition: projected-area thresholds must be monotonic and non-negative. Output: aggregated LOD-profile validation issues.
        VegetationValidationResult result = new VegetationValidationResult();
        if (lodProfile == null)
        {
            result.AddError("lodProfile is missing.");
            return result;
        }

        if (lodProfile.R0MinProjectedArea < 0f ||
            lodProfile.R1MinProjectedArea < 0f ||
            lodProfile.ShellL0MinProjectedArea < 0f ||
            lodProfile.ShellL1MinProjectedArea < 0f ||
            lodProfile.ShellL2MinProjectedArea < 0f ||
            lodProfile.AbsoluteCullProjectedMin < 0f)
        {
            result.AddError("LOD projected-area thresholds must be non-negative.");
        }

        if (!(lodProfile.R0MinProjectedArea > lodProfile.R1MinProjectedArea &&
              lodProfile.R1MinProjectedArea > lodProfile.ShellL0MinProjectedArea &&
              lodProfile.ShellL0MinProjectedArea > lodProfile.ShellL1MinProjectedArea &&
              lodProfile.ShellL1MinProjectedArea > lodProfile.ShellL2MinProjectedArea &&
              lodProfile.ShellL2MinProjectedArea > lodProfile.AbsoluteCullProjectedMin))
        {
            result.AddError("LOD thresholds must strictly decrease: r0 > r1 > shellL0 > shellL1 > shellL2 > absoluteCull.");
        }

        if (lodProfile.BacksideBiasScale < 0f)
        {
            result.AddError("backsideBiasScale must be zero or greater.");
        }

        if (lodProfile.SilhouetteKeepThreshold < 0f)
        {
            result.AddError("silhouetteKeepThreshold must be zero or greater.");
        }

        return result;
    }

    /// <summary>
    /// [INTEGRATION] Validates the scene binding that points at a tree blueprint.
    /// </summary>
    public static VegetationValidationResult ValidateTreeAuthoring(VegetationTreeAuthoring? authoring)
    {
        // Range: accepts null so tooling can validate missing scene bindings explicitly. Condition: scene binding must reference a valid tree blueprint. Output: aggregated scene-authoring validation issues.
        VegetationValidationResult result = new VegetationValidationResult();
        if (authoring == null)
        {
            result.AddError("VegetationTreeAuthoring component is missing.");
            return result;
        }

        if (authoring.Blueprint == null)
        {
            result.AddError("VegetationTreeAuthoring is missing blueprint.");
            return result;
        }

        result.Merge(ValidateTreeBlueprint(authoring.Blueprint));
        return result;
    }

    private static void ValidateShellChain(BranchPrototypeSO prototype, VegetationValidationResult result)
    {
        BranchShellNode[] shellNodes = prototype.ShellNodes;
        Mesh? shellL1WoodMesh = prototype.ShellL1WoodMesh;
        Mesh? shellL2WoodMesh = prototype.ShellL2WoodMesh;
        bool hasAnyShell = (shellNodes != null && shellNodes.Length > 0) ||
                           shellL1WoodMesh != null ||
                           shellL2WoodMesh != null;

        if (!hasAnyShell)
        {
            return;
        }

        if (shellNodes == null || shellNodes.Length == 0 || shellL1WoodMesh == null || shellL2WoodMesh == null)
        {
            result.AddError("shellNodes, shellL1WoodMesh, and shellL2WoodMesh must all be assigned together.");
            return;
        }

        ValidateOptionalReadableMesh(shellL1WoodMesh, "shellL1WoodMesh", result);
        ValidateOptionalReadableMesh(shellL2WoodMesh, "shellL2WoodMesh", result);

        if (prototype.ShellMaterial == null)
        {
            result.AddError("shellMaterial is required when shell meshes are assigned.");
        }
        else if (!TryValidateOpaqueMaterial(prototype.ShellMaterial, out string reason))
        {
            result.AddError($"shellMaterial must be opaque. {reason}");
        }

        Mesh? sourceWoodMesh = prototype.WoodMesh;
        if (sourceWoodMesh == null ||
            !sourceWoodMesh.isReadable ||
            !shellL1WoodMesh.isReadable ||
            !shellL2WoodMesh.isReadable)
        {
            ValidateShellNodeHierarchy(shellNodes, prototype, result);
            return;
        }

        int sourceWoodTriangles = GetTriangleCount(sourceWoodMesh);
        int shellL1WoodTriangles = GetTriangleCount(shellL1WoodMesh);
        int shellL2WoodTriangles = GetTriangleCount(shellL2WoodMesh);

        if (!(sourceWoodTriangles >= shellL1WoodTriangles && shellL1WoodTriangles >= shellL2WoodTriangles))
        {
            result.AddError("Wood triangle counts must not increase: source >= L1Wood >= L2Wood.");
        }

        ValidateShellNodeHierarchy(shellNodes, prototype, result);
    }

    private static void ValidateShellNodeHierarchy(BranchShellNode[] shellNodes, BranchPrototypeSO prototype, VegetationValidationResult result)
    {
        if (shellNodes.Length == 0)
        {
            result.AddError("shellNodes must contain at least one occupied hierarchy node.");
            return;
        }

        if (shellNodes[0] == null)
        {
            result.AddError("shellNodes[0] is missing.");
            return;
        }

        if (shellNodes[0].Depth != 0)
        {
            result.AddError("shellNodes[0] must be the root node at depth 0.");
        }

        for (int i = 0; i < shellNodes.Length; i++)
        {
            BranchShellNode? node = shellNodes[i];
            if (node == null)
            {
                result.AddError($"shellNodes[{i}] is missing.");
                continue;
            }

            if (node.ShellL0Mesh == null || node.ShellL1Mesh == null || node.ShellL2Mesh == null)
            {
                result.AddError($"shellNodes[{i}] must assign shellL0Mesh, shellL1Mesh, and shellL2Mesh together.");
                continue;
            }

            ValidateOptionalReadableMesh(node.ShellL0Mesh, $"shellNodes[{i}].shellL0Mesh", result);
            ValidateOptionalReadableMesh(node.ShellL1Mesh, $"shellNodes[{i}].shellL1Mesh", result);
            ValidateOptionalReadableMesh(node.ShellL2Mesh, $"shellNodes[{i}].shellL2Mesh", result);

            if (node.FirstChildIndex >= 0 && node.FirstChildIndex <= i)
            {
                result.AddError($"shellNodes[{i}] firstChildIndex must point to a later preorder node.");
            }

            if (node.ChildMask == 0)
            {
                if (node.FirstChildIndex >= 0)
                {
                    result.AddError($"shellNodes[{i}] cannot declare firstChildIndex without childMask.");
                }
            }
            else
            {
                int childCount = CountBits(node.ChildMask);
                if (node.FirstChildIndex < 0)
                {
                    result.AddError($"shellNodes[{i}] childMask requires firstChildIndex.");
                }
                else if (node.FirstChildIndex + childCount > shellNodes.Length)
                {
                    result.AddError($"shellNodes[{i}] child range exceeds shellNodes length.");
                }
                else
                {
                    for (int childOffset = 0; childOffset < childCount; childOffset++)
                    {
                        BranchShellNode? childNode = shellNodes[node.FirstChildIndex + childOffset];
                        if (childNode == null)
                        {
                            result.AddError($"shellNodes[{i}] child {childOffset} is missing.");
                            continue;
                        }

                        if (childNode.Depth != node.Depth + 1)
                        {
                            result.AddError($"shellNodes[{i}] children must be exactly one depth level deeper.");
                        }

                        if (!ContainsBounds(node.LocalBounds, childNode.LocalBounds))
                        {
                            result.AddError($"shellNodes[{i}] child bounds must stay inside parent bounds.");
                        }
                    }
                }
            }

            if (!node.ShellL0Mesh.isReadable || !node.ShellL1Mesh.isReadable || !node.ShellL2Mesh.isReadable)
            {
                continue;
            }

            if (!ContainsBounds(node.LocalBounds, node.ShellL0Mesh.bounds) ||
                !ContainsBounds(node.LocalBounds, node.ShellL1Mesh.bounds) ||
                !ContainsBounds(node.LocalBounds, node.ShellL2Mesh.bounds))
            {
                result.AddError($"shellNodes[{i}] mesh bounds must stay inside node localBounds.");
            }

            int nodeL0Triangles = GetTriangleCount(node.ShellL0Mesh);
            int nodeL1Triangles = GetTriangleCount(node.ShellL1Mesh);
            int nodeL2Triangles = GetTriangleCount(node.ShellL2Mesh);
            if (!(nodeL0Triangles >= nodeL1Triangles && nodeL1Triangles >= nodeL2Triangles))
            {
                result.AddError($"shellNodes[{i}] triangle counts must not increase toward lower detail.");
            }
        }

        List<BranchShellNode> leafNodes = BranchShellNodeUtility.CollectLeafNodes(shellNodes);
        if (leafNodes.Count == 0)
        {
            result.AddError("shellNodes must contain at least one renderable leaf node.");
            return;
        }

        int shellL0Triangles = BranchShellNodeUtility.GetTriangleCountForLeafFrontier(shellNodes, 0);
        int shellL1Triangles = BranchShellNodeUtility.GetTriangleCountForLeafFrontier(shellNodes, 1);
        int shellL2Triangles = BranchShellNodeUtility.GetTriangleCountForLeafFrontier(shellNodes, 2);
        if (!(shellL0Triangles > shellL1Triangles && shellL1Triangles > shellL2Triangles))
        {
            result.AddError("Leaf-frontier shell triangle counts must strictly decrease: L0 > L1 > L2.");
        }

        if (shellL0Triangles > prototype.TriangleBudgetShellL0)
        {
            result.AddError($"Leaf-frontier shellL0 triangle count {shellL0Triangles} exceeds budget {prototype.TriangleBudgetShellL0}.");
        }

        if (shellL1Triangles > prototype.TriangleBudgetShellL1)
        {
            result.AddError($"Leaf-frontier shellL1 triangle count {shellL1Triangles} exceeds budget {prototype.TriangleBudgetShellL1}.");
        }

        if (shellL2Triangles > prototype.TriangleBudgetShellL2)
        {
            result.AddError($"Leaf-frontier shellL2 triangle count {shellL2Triangles} exceeds budget {prototype.TriangleBudgetShellL2}.");
        }
    }

    private static void ValidateBranchPlacements(TreeBlueprintSO blueprint, VegetationValidationResult result)
    {
        BranchPlacement[] placements = blueprint.Branches;
        if (placements == null || placements.Length == 0)
        {
            result.AddError("branches must contain at least one branch placement.");
            return;
        }

        for (int i = 0; i < placements.Length; i++)
        {
            BranchPlacement? placement = placements[i];
            if (placement == null)
            {
                result.AddError($"branches[{i}] is missing.");
                continue;
            }

            if (placement.Prototype == null)
            {
                result.AddError($"branches[{i}] is missing prototype.");
            }

            if (!IsScaleOnQuarterStep(placement.Scale))
            {
                result.AddError($"branches[{i}].scale must be a positive multiple of 0.25.");
            }
        }
    }

    private static void ValidateImpostor(TreeBlueprintSO blueprint, VegetationValidationResult result)
    {
        Mesh? impostorMesh = blueprint.ImpostorMesh;
        Material? impostorMaterial = blueprint.ImpostorMaterial;
        bool hasImpostorMesh = impostorMesh != null;
        bool hasImpostorMaterial = impostorMaterial != null;

        if (hasImpostorMesh != hasImpostorMaterial)
        {
            result.AddError("impostorMesh and impostorMaterial must both be assigned or both be omitted.");
            return;
        }

        if (!hasImpostorMesh || impostorMesh == null || impostorMaterial == null)
        {
            return;
        }

        ValidateOptionalReadableMesh(impostorMesh, "impostorMesh", result);
        if (!TryValidateOpaqueMaterial(impostorMaterial, out string reason))
        {
            result.AddError($"impostorMaterial must be opaque. {reason}");
        }

        if (!impostorMesh.isReadable)
        {
            return;
        }

        if (GetTriangleCount(impostorMesh) > RecommendedImpostorTriangleBudget)
        {
            result.AddError($"impostorMesh must stay at or below {RecommendedImpostorTriangleBudget} triangles.");
        }
    }

    private static void ValidateLocalBoundsContainSourceMeshes(BranchPrototypeSO prototype, VegetationValidationResult result)
    {
        Mesh? woodMesh = prototype.WoodMesh;
        Mesh? foliageMesh = prototype.FoliageMesh;
        if (woodMesh == null || foliageMesh == null || !woodMesh.isReadable || !foliageMesh.isReadable)
        {
            return;
        }

        Bounds expectedBounds = woodMesh.bounds;
        expectedBounds.Encapsulate(foliageMesh.bounds);

        if (!ContainsBounds(prototype.LocalBounds, expectedBounds))
        {
            result.AddError("localBounds must fully contain woodMesh and foliageMesh bounds.");
        }
    }

    private static void ValidateTreeBounds(TreeBlueprintSO blueprint, VegetationValidationResult result)
    {
        Mesh? trunkMesh = blueprint.TrunkMesh;
        BranchPlacement[] placements = blueprint.Branches;
        if (trunkMesh == null || !trunkMesh.isReadable || placements == null || placements.Length == 0)
        {
            return;
        }

        Bounds expectedBounds = trunkMesh.bounds;
        for (int i = 0; i < placements.Length; i++)
        {
            BranchPlacement? placement = placements[i];
            if (placement?.Prototype == null)
            {
                continue;
            }

            Bounds branchBounds = TransformBounds(
                placement.Prototype.LocalBounds,
                Matrix4x4.TRS(
                    placement.LocalPosition,
                    placement.LocalRotation,
                    Vector3.one * placement.Scale));

            expectedBounds.Encapsulate(branchBounds);
        }

        if (!ContainsBounds(blueprint.TreeBounds, expectedBounds))
        {
            result.AddError("treeBounds must fully contain trunkMesh and every placed branch localBounds.");
        }
    }

    private static void ValidateRequiredReadableMesh(Mesh? mesh, string fieldName, VegetationValidationResult result)
    {
        if (mesh == null)
        {
            result.AddError($"{fieldName} is required.");
            return;
        }

        if (!mesh.isReadable)
        {
            result.AddError($"{fieldName} must be readable.");
        }
    }

    private static void ValidateOptionalReadableMesh(Mesh? mesh, string fieldName, VegetationValidationResult result)
    {
        if (mesh != null && !mesh.isReadable)
        {
            result.AddError($"{fieldName} must be readable.");
        }
    }

    private static void ValidateRequiredOpaqueMaterial(Material? material, string fieldName, VegetationValidationResult result)
    {
        if (material == null)
        {
            result.AddError($"{fieldName} is required.");
            return;
        }

        if (!TryValidateOpaqueMaterial(material, out string reason))
        {
            result.AddError($"{fieldName} must be opaque. {reason}");
        }
    }

    private static void ValidateTriangleBudget(Mesh? mesh, int budget, string fieldName, VegetationValidationResult result)
    {
        if (budget <= 0)
        {
            result.AddError($"{fieldName} triangle budget must be greater than zero.");
            return;
        }

        if (mesh == null || !mesh.isReadable)
        {
            return;
        }

        int triangleCount = GetTriangleCount(mesh);
        if (triangleCount > budget)
        {
            result.AddError($"{fieldName} triangle count {triangleCount} exceeds budget {budget}.");
        }
    }

    private static bool TryValidateOpaqueMaterial(Material material, out string reason)
    {
        if (material.renderQueue >= (int)RenderQueue.AlphaTest)
        {
            reason = $"renderQueue {material.renderQueue} is not in the opaque range.";
            return false;
        }

        string renderType = material.GetTag("RenderType", false, string.Empty);
        if (renderType == "Transparent" || renderType == "TransparentCutout")
        {
            reason = $"RenderType tag '{renderType}' is not opaque.";
            return false;
        }

        if (material.HasProperty("_Surface") && !Mathf.Approximately(material.GetFloat("_Surface"), 0f))
        {
            reason = "_Surface is not Opaque.";
            return false;
        }

        if (material.HasProperty("_AlphaClip") && material.GetFloat("_AlphaClip") > 0.5f)
        {
            reason = "_AlphaClip is enabled.";
            return false;
        }

        if (material.IsKeywordEnabled("_ALPHATEST_ON") ||
            material.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT") ||
            material.IsKeywordEnabled("_ALPHAPREMULTIPLY_ON"))
        {
            reason = "alpha test or transparent keywords are enabled.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private static int GetTriangleCount(Mesh mesh)
    {
        return mesh.triangles.Length / 3;
    }

    private static int CountBits(byte value)
    {
        int count = 0;
        while (value != 0)
        {
            count += value & 1;
            value >>= 1;
        }

        return count;
    }

    private static bool IsScaleOnQuarterStep(float scale)
    {
        if (scale <= 0f)
        {
            return false;
        }

        float quantizedScale = scale / ScaleStep;
        return Mathf.Abs(quantizedScale - Mathf.Round(quantizedScale)) <= ScaleTolerance;
    }

    private static bool ContainsBounds(Bounds container, Bounds candidate)
    {
        Vector3 containerMin = container.min - Vector3.one * BoundsContainmentEpsilon;
        Vector3 containerMax = container.max + Vector3.one * BoundsContainmentEpsilon;
        Vector3 candidateMin = candidate.min;
        Vector3 candidateMax = candidate.max;

        return candidateMin.x >= containerMin.x &&
               candidateMin.y >= containerMin.y &&
               candidateMin.z >= containerMin.z &&
               candidateMax.x <= containerMax.x &&
               candidateMax.y <= containerMax.y &&
               candidateMax.z <= containerMax.z;
    }

private static Bounds TransformBounds(Bounds bounds, Matrix4x4 matrix)
    {
        // Range: input bounds are branch-local authoring bounds. Condition: matrix contains the placement TRS only. Output: transformed AABB in tree-local space.
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
}
}
