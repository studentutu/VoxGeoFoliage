#nullable enable

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
        Mesh? shellL0Mesh = prototype.ShellL0Mesh;
        Mesh? shellL1Mesh = prototype.ShellL1Mesh;
        Mesh? shellL1WoodMesh = prototype.ShellL1WoodMesh;
        Mesh? shellL2Mesh = prototype.ShellL2Mesh;
        Mesh? shellL2WoodMesh = prototype.ShellL2WoodMesh;
        bool hasAnyShell = shellL0Mesh != null ||
                           shellL1Mesh != null ||
                           shellL1WoodMesh != null ||
                           shellL2Mesh != null ||
                           shellL2WoodMesh != null;

        if (!hasAnyShell)
        {
            return;
        }

        if (shellL0Mesh == null || shellL1Mesh == null || shellL1WoodMesh == null || shellL2Mesh == null || shellL2WoodMesh == null)
        {
            result.AddError("shellL0Mesh, shellL1Mesh, shellL1WoodMesh, shellL2Mesh, and shellL2WoodMesh must all be assigned together.");
            return;
        }

        ValidateOptionalReadableMesh(shellL0Mesh, "shellL0Mesh", result);
        ValidateOptionalReadableMesh(shellL1Mesh, "shellL1Mesh", result);
        ValidateOptionalReadableMesh(shellL1WoodMesh, "shellL1WoodMesh", result);
        ValidateOptionalReadableMesh(shellL2Mesh, "shellL2Mesh", result);
        ValidateOptionalReadableMesh(shellL2WoodMesh, "shellL2WoodMesh", result);
        ValidateTriangleBudget(shellL0Mesh, prototype.TriangleBudgetShellL0, "shellL0Mesh", result);
        ValidateTriangleBudget(shellL1Mesh, prototype.TriangleBudgetShellL1, "shellL1Mesh", result);
        ValidateTriangleBudget(shellL2Mesh, prototype.TriangleBudgetShellL2, "shellL2Mesh", result);

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
            !shellL0Mesh.isReadable ||
            !shellL1Mesh.isReadable ||
            !shellL1WoodMesh.isReadable ||
            !shellL2Mesh.isReadable ||
            !shellL2WoodMesh.isReadable)
        {
            return;
        }

        int shellL0Triangles = GetTriangleCount(shellL0Mesh);
        int shellL1Triangles = GetTriangleCount(shellL1Mesh);
        int shellL2Triangles = GetTriangleCount(shellL2Mesh);
        int sourceWoodTriangles = GetTriangleCount(sourceWoodMesh);
        int shellL1WoodTriangles = GetTriangleCount(shellL1WoodMesh);
        int shellL2WoodTriangles = GetTriangleCount(shellL2WoodMesh);
        if (!(shellL0Triangles > shellL1Triangles && shellL1Triangles > shellL2Triangles))
        {
            result.AddError("Shell triangle counts must strictly decrease: L0 > L1 > L2.");
        }

        if (!(sourceWoodTriangles >= shellL1WoodTriangles && shellL1WoodTriangles >= shellL2WoodTriangles))
        {
            result.AddError("Wood triangle counts must not increase: source >= L1Wood >= L2Wood.");
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
