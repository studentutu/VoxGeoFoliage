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
    public void BranchPrototype_TriangleBudget_FailsValidation()
    {
        BranchPrototypeSO prototype = CreateValidBranchPrototype();
        SetPrivateField(prototype, "triangleBudgetWood", 1);

        VegetationValidationResult result = prototype.Validate();

        AssertHasError(result, "woodMesh triangle count");
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
    public void BranchPrototype_ShellTriangleOrder_L0GreaterThanL1GreaterThanL2()
    {
        BranchPrototypeSO prototype = CreateValidBranchPrototype(includeShells: true);
        SetPrivateField(prototype, "shellL0Mesh", CreateMesh("ShellL0", 3, new Bounds(Vector3.zero, Vector3.one)));
        SetPrivateField(prototype, "shellL1Mesh", CreateMesh("ShellL1", 5, new Bounds(Vector3.zero, Vector3.one)));
        SetPrivateField(prototype, "shellL2Mesh", CreateMesh("ShellL2", 2, new Bounds(Vector3.zero, Vector3.one)));

        VegetationValidationResult result = prototype.Validate();

        AssertHasError(result, "Shell triangle counts must strictly decrease");
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
    public void TreeBlueprint_EmptyBranches_FailsValidation()
    {
        TreeBlueprintSO blueprint = CreateValidTreeBlueprint();
        SetPrivateField(blueprint, "branches", Array.Empty<BranchPlacement>());

        VegetationValidationResult result = blueprint.Validate();

        AssertHasError(result, "branches must contain at least one branch placement.");
    }

    [Test]
    public void TreeBlueprint_LODThresholds_MonotonicallyDecreasing()
    {
        TreeBlueprintSO blueprint = CreateValidTreeBlueprint();
        LODProfileSO lodProfile = CreateValidLodProfile();
        SetPrivateField(lodProfile, "shellL0MinProjectedArea", 0.25f);
        SetPrivateField(blueprint, "lodProfile", lodProfile);

        VegetationValidationResult result = blueprint.Validate();

        AssertHasError(result, "LOD thresholds must strictly decrease");
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
            SetPrivateField(prototype, "shellL0Mesh", CreateMesh("ShellL0", 7, new Bounds(Vector3.zero, Vector3.one)));
            SetPrivateField(prototype, "shellL1Mesh", CreateMesh("ShellL1", 5, new Bounds(Vector3.zero, Vector3.one * 0.8f)));
            SetPrivateField(prototype, "shellL2Mesh", CreateMesh("ShellL2", 3, new Bounds(Vector3.zero, Vector3.one * 0.6f)));
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
        Material trunkMaterial = CreateOpaqueMaterial("TrunkMaterial");

        SetPrivateField(placement, "prototype", prototype);
        SetPrivateField(placement, "localPosition", new Vector3(3f, 1f, 0f));
        SetPrivateField(placement, "localRotation", Quaternion.Euler(0f, 30f, 0f));
        SetPrivateField(placement, "scale", scale);

        SetPrivateField(blueprint, "trunkMesh", trunkMesh);
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
        SetPrivateField(lodProfile, "r0MinProjectedArea", 0.5f);
        SetPrivateField(lodProfile, "r1MinProjectedArea", 0.25f);
        SetPrivateField(lodProfile, "shellL0MinProjectedArea", 0.12f);
        SetPrivateField(lodProfile, "shellL1MinProjectedArea", 0.06f);
        SetPrivateField(lodProfile, "shellL2MinProjectedArea", 0.02f);
        SetPrivateField(lodProfile, "absoluteCullProjectedMin", 0.005f);
        SetPrivateField(lodProfile, "backsideBiasScale", 0.2f);
        SetPrivateField(lodProfile, "silhouetteKeepThreshold", 0.8f);
        return lodProfile;
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
