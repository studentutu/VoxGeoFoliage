#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using VoxGeoFol.Features.Vegetation.Authoring;

[TestFixture]
public sealed class AuthoringValidationTests
{
    private readonly List<UnityEngine.Object> createdObjects = new List<UnityEngine.Object>();

    [TearDown]
    public void TearDown()
    {
        for (int i = createdObjects.Count - 1; i >= 0; i--)
        {
            UnityEngine.Object createdObject = createdObjects[i];
            if (createdObject != null)
            {
                UnityEngine.Object.DestroyImmediate(createdObject);
            }
        }

        createdObjects.Clear();
    }

    [Test]
    public void BranchPrototype_NullWoodMesh_FailsValidation()
    {
        BranchPrototypeSO prototype = CreateValidBranchPrototype();
        SetPrivateField(prototype, "woodMesh", null);

        VegetationValidationResult result = prototype.Validate();

        AssertHasError(result, "woodMesh is required.");
    }

    [Test]
    public void BranchPrototype_NullFoliageMesh_FailsValidation()
    {
        BranchPrototypeSO prototype = CreateValidBranchPrototype();
        SetPrivateField(prototype, "foliageMesh", null);

        VegetationValidationResult result = prototype.Validate();

        AssertHasError(result, "foliageMesh is required.");
    }

    [Test]
    public void BranchPrototype_SourceMeshesMustBeReadable()
    {
        BranchPrototypeSO prototype = CreateValidBranchPrototype();
        Mesh unreadableMesh = CreateMesh("UnreadableWood", 4, new Bounds(Vector3.zero, Vector3.one), true);
        SetPrivateField(prototype, "woodMesh", unreadableMesh);

        VegetationValidationResult result = prototype.Validate();

        AssertHasError(result, "woodMesh must be readable.");
    }

    [Test]
    public void BranchPrototype_TransparentMaterial_FailsValidation()
    {
        BranchPrototypeSO prototype = CreateValidBranchPrototype();
        Material transparentMaterial = CreateOpaqueMaterial("TransparentFoliage");
        transparentMaterial.renderQueue = (int)RenderQueue.Transparent;
        if (transparentMaterial.HasProperty("_Surface"))
        {
            transparentMaterial.SetFloat("_Surface", 1f);
        }

        SetPrivateField(prototype, "foliageMaterial", transparentMaterial);

        VegetationValidationResult result = prototype.Validate();

        AssertHasError(result, "foliageMaterial must be opaque.");
    }

    [Test]
    public void BranchPrototype_LocalBoundsContainWoodAndFoliage()
    {
        BranchPrototypeSO prototype = CreateValidBranchPrototype();
        SetPrivateField(prototype, "localBounds", new Bounds(Vector3.zero, Vector3.one * 0.1f));

        VegetationValidationResult result = prototype.Validate();

        AssertHasError(result, "localBounds must fully contain woodMesh and foliageMesh bounds.");
    }

    [Test]
    public void BranchPrototype_ShellHierarchyTriangleOrder_FailsValidation()
    {
        BranchPrototypeSO prototype = CreateValidBranchPrototype(includeShells: true);
        SetShellHierarchies(
            prototype,
            new[] { CreateShellNodeForLevel(0, -1, 0, 3, new Bounds(Vector3.zero, Vector3.one), 0) },
            new[] { CreateShellNodeForLevel(0, -1, 0, 5, new Bounds(Vector3.zero, Vector3.one), 1) },
            new[] { CreateShellNodeForLevel(0, -1, 0, 2, new Bounds(Vector3.zero, Vector3.one), 2) });

        VegetationValidationResult result = prototype.Validate();

        AssertHasError(result, "Leaf-frontier shell triangle counts must strictly decrease");
    }

    [Test]
    public void BranchPrototype_ShellHierarchyTopology_FailsValidation()
    {
        BranchPrototypeSO prototype = CreateValidBranchPrototype(includeShells: true);
        BranchShellNode invalidRootL0 = CreateShellNodeForLevel(0, 0, 1, 7, new Bounds(Vector3.zero, Vector3.one), 0);
        BranchShellNode invalidChildL0 = CreateShellNodeForLevel(2, -1, 0, 4, new Bounds(Vector3.zero, Vector3.one * 0.5f), 0);
        BranchShellNode invalidRootL1 = CreateShellNodeForLevel(0, 0, 1, 5, new Bounds(Vector3.zero, Vector3.one), 1);
        BranchShellNode invalidChildL1 = CreateShellNodeForLevel(2, -1, 0, 3, new Bounds(Vector3.zero, Vector3.one * 0.5f), 1);
        BranchShellNode invalidRootL2 = CreateShellNodeForLevel(0, 0, 1, 3, new Bounds(Vector3.zero, Vector3.one), 2);
        BranchShellNode invalidChildL2 = CreateShellNodeForLevel(2, -1, 0, 2, new Bounds(Vector3.zero, Vector3.one * 0.5f), 2);
        SetShellHierarchies(
            prototype,
            new[] { invalidRootL0, invalidChildL0 },
            new[] { invalidRootL1, invalidChildL1 },
            new[] { invalidRootL2, invalidChildL2 });

        VegetationValidationResult result = prototype.Validate();

        AssertHasError(result, "firstChildIndex must point to a later node index in the flattened hierarchy");
    }

    [Test]
    public void BranchPrototype_WoodTriangleOrder_SourceAndL1MustNotIncreaseTowardL2()
    {
        BranchPrototypeSO prototype = CreateValidBranchPrototype(includeShells: true);
        SetPrivateField(prototype, "shellL1WoodMesh", CreateMesh("ShellL1Wood", 3, new Bounds(Vector3.zero, Vector3.one)));
        SetPrivateField(prototype, "shellL2WoodMesh", CreateMesh("ShellL2Wood", 4, new Bounds(Vector3.zero, Vector3.one * 0.5f)));

        VegetationValidationResult result = prototype.Validate();

        AssertHasError(result, "Wood triangle counts must not increase");
    }

    [Test]
    public void BranchPrototype_ValidHierarchy_PassesValidation()
    {
        BranchPrototypeSO prototype = CreateValidBranchPrototype(includeShells: true);

        VegetationValidationResult result = prototype.Validate();

        Assert.IsFalse(result.HasErrors, string.Join(Environment.NewLine, result.Issues.Select(issue => issue.Message)));
    }

    [Test]
    public void TreeBlueprint_NullTrunk_FailsValidation()
    {
        TreeBlueprintSO blueprint = CreateValidTreeBlueprint();
        SetPrivateField(blueprint, "trunkMesh", null);

        VegetationValidationResult result = blueprint.Validate();

        AssertHasError(result, "trunkMesh is required.");
    }

    [Test]
    public void TreeBlueprint_NullTrunkL3_FailsValidation()
    {
        TreeBlueprintSO blueprint = CreateValidTreeBlueprint();
        SetPrivateField(blueprint, "trunkL3Mesh", null);

        VegetationValidationResult result = blueprint.Validate();

        AssertHasError(result, "trunkL3Mesh is required.");
    }

    [Test]
    public void TreeBlueprint_EmptyBranches_FailsValidation()
    {
        TreeBlueprintSO blueprint = CreateValidTreeBlueprint();
        SetPrivateField(blueprint, "branches", Array.Empty<BranchPlacement>());

        VegetationValidationResult result = blueprint.Validate();

        AssertHasError(result, "branches must contain at least one branch placement.");
    }

    [Test]
    public void TreeBlueprint_LODDistances_MonotonicallyIncreasing()
    {
        TreeBlueprintSO blueprint = CreateValidTreeBlueprint();
        LODProfileSO lodProfile = CreateValidLodProfile();
        SetPrivateField(lodProfile, "l2Distance", 10f);
        SetPrivateField(blueprint, "lodProfile", lodProfile);

        VegetationValidationResult result = blueprint.Validate();

        AssertHasError(result, "LOD distances must strictly increase");
    }

    [Test]
    public void TreeBlueprint_BoundsContainAllBranches()
    {
        TreeBlueprintSO blueprint = CreateValidTreeBlueprint();
        SetPrivateField(blueprint, "treeBounds", new Bounds(Vector3.zero, Vector3.one * 0.5f));

        VegetationValidationResult result = blueprint.Validate();

        AssertHasError(result, "treeBounds must fully contain trunkMesh and every placed branch localBounds.");
    }

    [Test]
    public void TreeBlueprint_TrunkL3BoundsStayInsideTrunkBounds()
    {
        TreeBlueprintSO blueprint = CreateValidTreeBlueprint();
        SetPrivateField(blueprint, "trunkL3Mesh", CreateMesh("OversizedTrunkL3", 4, new Bounds(Vector3.zero, new Vector3(4f, 8f, 4f))));

        VegetationValidationResult result = blueprint.Validate();

        AssertHasError(result, "trunkL3Mesh bounds must stay inside trunkMesh bounds.");
    }

    [Test]
    public void TreeBlueprint_ImpostorTriangleBudget_Under200()
    {
        TreeBlueprintSO blueprint = CreateValidTreeBlueprint();
        SetPrivateField(blueprint, "impostorMesh", CreateMesh("HeavyImpostor", 201, new Bounds(Vector3.zero, Vector3.one)));
        SetPrivateField(blueprint, "impostorMaterial", CreateOpaqueMaterial("ImpostorMaterial"));

        VegetationValidationResult result = blueprint.Validate();

        AssertHasError(result, "impostorMesh must stay at or below 200 triangles.");
    }

    [Test]
    public void TreeBlueprint_ScaleConstraint_OnlyAllowedValues()
    {
        TreeBlueprintSO blueprint = CreateValidTreeBlueprint(scale: 0.3f);

        VegetationValidationResult result = blueprint.Validate();

        AssertHasError(result, "branches[0].scale must be a positive multiple of 0.25.");
    }

    [Test]
    public void TreeBlueprint_ValidBlueprint_PassesValidation()
    {
        TreeBlueprintSO blueprint = CreateValidTreeBlueprint(includeImpostor: true);

        VegetationValidationResult result = blueprint.Validate();

        Assert.IsFalse(result.HasErrors, string.Join(Environment.NewLine, result.Issues.Select(issue => issue.Message)));
    }

    private BranchPrototypeSO CreateValidBranchPrototype(bool includeShells = false)
    {
        BranchPrototypeSO prototype = CreateScriptableObject<BranchPrototypeSO>();
        Mesh woodMesh = CreateMesh("WoodMesh", 4, new Bounds(new Vector3(-0.5f, 0f, 0f), new Vector3(1f, 2f, 1f)));
        Mesh foliageMesh = CreateMesh("FoliageMesh", 6, new Bounds(new Vector3(1f, 0f, 0f), new Vector3(2f, 2f, 2f)));
        Material woodMaterial = CreateOpaqueMaterial("WoodMaterial");
        Material foliageMaterial = CreateOpaqueMaterial("FoliageMaterial");

        SetPrivateField(prototype, "woodMesh", woodMesh);
        SetPrivateField(prototype, "woodMaterial", woodMaterial);
        SetPrivateField(prototype, "foliageMesh", foliageMesh);
        SetPrivateField(prototype, "foliageMaterial", foliageMaterial);
        SetPrivateField(prototype, "leafColorTint", Color.green);
        SetPrivateField(prototype, "localBounds", new Bounds(new Vector3(0.5f, 0f, 0f), new Vector3(6f, 4f, 4f)));
        SetPrivateField(prototype, "triangleBudgetWood", 8);
        SetPrivateField(prototype, "triangleBudgetFoliage", 8);
        SetPrivateField(prototype, "triangleBudgetShellL0", 12);
        SetPrivateField(prototype, "triangleBudgetShellL1", 8);
        SetPrivateField(prototype, "triangleBudgetShellL2", 4);

        if (includeShells)
        {
            SetShellHierarchies(
                prototype,
                new[] { CreateShellNodeForLevel(0, -1, 0, 7, new Bounds(Vector3.zero, Vector3.one), 0) },
                new[] { CreateShellNodeForLevel(0, -1, 0, 5, new Bounds(Vector3.zero, Vector3.one), 1) },
                new[] { CreateShellNodeForLevel(0, -1, 0, 3, new Bounds(Vector3.zero, Vector3.one), 2) });
            SetPrivateField(prototype, "shellL1WoodMesh", CreateMesh("ShellL1Wood", 2, new Bounds(Vector3.zero, Vector3.one * 0.5f)));
            SetPrivateField(prototype, "shellL2WoodMesh", CreateMesh("ShellL2Wood", 1, new Bounds(Vector3.zero, Vector3.one * 0.3f)));
            SetPrivateField(prototype, "shellMaterial", CreateOpaqueMaterial("ShellMaterial"));
        }

        return prototype;
    }

    private TreeBlueprintSO CreateValidTreeBlueprint(float scale = 1f, bool includeImpostor = false)
    {
        TreeBlueprintSO blueprint = CreateScriptableObject<TreeBlueprintSO>();
        BranchPrototypeSO prototype = CreateValidBranchPrototype();
        BranchPlacement placement = new BranchPlacement();
        LODProfileSO lodProfile = CreateValidLodProfile();
        Mesh trunkMesh = CreateMesh("TrunkMesh", 8, new Bounds(Vector3.zero, new Vector3(2f, 6f, 2f)));
        Mesh trunkL3Mesh = CreateMesh("TrunkL3Mesh", 4, new Bounds(Vector3.zero, new Vector3(1.25f, 5f, 1.25f)));
        Material trunkMaterial = CreateOpaqueMaterial("TrunkMaterial");

        SetPrivateField(placement, "prototype", prototype);
        SetPrivateField(placement, "localPosition", new Vector3(3f, 1f, 0f));
        SetPrivateField(placement, "localRotation", Quaternion.Euler(0f, 30f, 0f));
        SetPrivateField(placement, "scale", scale);

        SetPrivateField(blueprint, "trunkMesh", trunkMesh);
        SetPrivateField(blueprint, "trunkL3Mesh", trunkL3Mesh);
        SetPrivateField(blueprint, "trunkMaterial", trunkMaterial);
        SetPrivateField(blueprint, "branches", new[] { placement });
        SetPrivateField(blueprint, "lodProfile", lodProfile);
        SetPrivateField(blueprint, "treeBounds", new Bounds(new Vector3(1.5f, 1f, 0f), new Vector3(12f, 10f, 12f)));

        if (includeImpostor)
        {
            SetPrivateField(blueprint, "impostorMesh", CreateMesh("ImpostorMesh", 40, new Bounds(Vector3.zero, Vector3.one * 2f)));
            SetPrivateField(blueprint, "impostorMaterial", CreateOpaqueMaterial("ImpostorMaterial"));
        }

        return blueprint;
    }

    private LODProfileSO CreateValidLodProfile()
    {
        LODProfileSO lodProfile = CreateScriptableObject<LODProfileSO>();
        SetPrivateField(lodProfile, "l0Distance", 5f);
        SetPrivateField(lodProfile, "l1Distance", 15f);
        SetPrivateField(lodProfile, "l2Distance", 30f);
        SetPrivateField(lodProfile, "l3Distance", 60f);
        SetPrivateField(lodProfile, "impostorDistance", 120f);
        SetPrivateField(lodProfile, "absoluteCullDistance", 200f);
        return lodProfile;
    }

    private BranchShellNode CreateShellNodeForLevel(int depth, int firstChildIndex, byte childMask, int triangleCount, Bounds bounds, int shellLevel)
    {
        Mesh? shellL0Mesh = null;
        Mesh? shellL1Mesh = null;
        Mesh? shellL2Mesh = null;
        switch (shellLevel)
        {
            case 0:
                shellL0Mesh = CreateMesh($"ShellL0_{depth}", triangleCount, bounds);
                break;
            case 1:
                shellL1Mesh = CreateMesh($"ShellL1_{depth}", triangleCount, bounds);
                break;
            case 2:
                shellL2Mesh = CreateMesh($"ShellL2_{depth}", triangleCount, bounds);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(shellLevel), shellLevel, "Shell level must be 0, 1, or 2.");
        }

        return new BranchShellNode(
            bounds,
            depth,
            firstChildIndex,
            childMask,
            shellL0Mesh,
            shellL1Mesh,
            shellL2Mesh);
    }

    private static void SetShellHierarchies(
        BranchPrototypeSO prototype,
        BranchShellNode[] shellNodesL0,
        BranchShellNode[] shellNodesL1,
        BranchShellNode[] shellNodesL2)
    {
        SetPrivateField(prototype, "shellNodesL0", shellNodesL0);
        SetPrivateField(prototype, "shellNodesL1", shellNodesL1);
        SetPrivateField(prototype, "shellNodesL2", shellNodesL2);
    }

    private T CreateScriptableObject<T>() where T : ScriptableObject
    {
        T instance = ScriptableObject.CreateInstance<T>();
        createdObjects.Add(instance);
        return instance;
    }

    private Mesh CreateMesh(string name, int triangleCount, Bounds bounds, bool makeUnreadable = false)
    {
        Mesh mesh = new Mesh
        {
            name = name
        };

        Vector3 min = bounds.min;
        Vector3 max = bounds.max;
        Vector3[] vertices = new Vector3[triangleCount * 3];
        int[] triangles = new int[triangleCount * 3];

        for (int i = 0; i < triangleCount; i++)
        {
            int vertexIndex = i * 3;
            float t0 = triangleCount == 1 ? 0f : (float)i / triangleCount;
            float t1 = triangleCount == 1 ? 1f : (float)(i + 1) / triangleCount;
            float x0 = Mathf.Lerp(min.x, max.x, t0);
            float x1 = Mathf.Lerp(min.x, max.x, t1);

            vertices[vertexIndex] = new Vector3(x0, min.y, min.z);
            vertices[vertexIndex + 1] = new Vector3(x1, max.y, min.z);
            vertices[vertexIndex + 2] = new Vector3(x0, min.y, max.z);

            triangles[vertexIndex] = vertexIndex;
            triangles[vertexIndex + 1] = vertexIndex + 1;
            triangles[vertexIndex + 2] = vertexIndex + 2;
        }

        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();

        if (makeUnreadable)
        {
            mesh.UploadMeshData(true);
        }

        createdObjects.Add(mesh);
        return mesh;
    }

    private Material CreateOpaqueMaterial(string name)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ??
                        Shader.Find("Universal Render Pipeline/Simple Lit") ??
                        Shader.Find("Standard") ??
                        throw new InvalidOperationException("No supported opaque shader was found for tests.");

        Material material = new Material(shader)
        {
            name = name,
            renderQueue = (int)RenderQueue.Geometry
        };

        if (material.HasProperty("_Surface"))
        {
            material.SetFloat("_Surface", 0f);
        }

        if (material.HasProperty("_AlphaClip"))
        {
            material.SetFloat("_AlphaClip", 0f);
        }

        createdObjects.Add(material);
        return material;
    }

    private static void AssertHasError(VegetationValidationResult result, string expectedMessageFragment)
    {
        Assert.IsTrue(
            result.Issues.Any(issue => issue.Severity == VegetationValidationSeverity.Error && issue.Message.Contains(expectedMessageFragment, StringComparison.Ordinal)),
            $"Expected error containing '{expectedMessageFragment}', but found:{Environment.NewLine}{string.Join(Environment.NewLine, result.Issues.Select(issue => issue.Message))}");
    }

    private static void SetPrivateField(object target, string fieldName, object? value)
    {
        FieldInfo? fieldInfo = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        if (fieldInfo == null)
        {
            throw new InvalidOperationException($"Field '{fieldName}' was not found on '{target.GetType().Name}'.");
        }

        fieldInfo.SetValue(target, value);
    }
}
