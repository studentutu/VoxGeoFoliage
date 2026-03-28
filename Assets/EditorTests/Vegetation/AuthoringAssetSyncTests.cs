#nullable enable

using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VoxGeoFol.Features.Vegetation.Authoring;
using VoxGeoFol.Features.Vegetation.Editor;

[TestFixture]
public sealed class AuthoringAssetSyncTests
{
    private const string TestAssetRoot = "Assets/__GeneratedTests__/VegetationAuthoringSync";
    private readonly List<UnityEngine.Object> createdObjects = new List<UnityEngine.Object>();

    [TearDown]
    public void TearDown()
    {
        for (int i = createdObjects.Count - 1; i >= 0; i--)
        {
            UnityEngine.Object createdObject = createdObjects[i];
            if (createdObject != null && !AssetDatabase.Contains(createdObject))
            {
                UnityEngine.Object.DestroyImmediate(createdObject);
            }
        }

        createdObjects.Clear();

        if (AssetDatabase.IsValidFolder(TestAssetRoot))
        {
            AssetDatabase.DeleteAsset(TestAssetRoot);
        }

        AssetDatabase.Refresh();
    }

    [Test]
    public void RefreshBranchPrototypeLocalBounds_EncapsulatesWoodAndFoliageMeshes()
    {
        BranchPrototypeSO prototype = ScriptableObject.CreateInstance<BranchPrototypeSO>();
        Mesh woodMesh = CreateMeshAsset(
            "Wood.asset",
            new[]
            {
                new Vector3(-2f, -1f, -1f),
                new Vector3(-1f, 1f, -1f),
                new Vector3(-2f, -1f, 1f)
            });
        Mesh foliageMesh = CreateMeshAsset(
            "Foliage.asset",
            new[]
            {
                new Vector3(1f, -0.5f, -0.5f),
                new Vector3(3f, 2f, -0.5f),
                new Vector3(1f, -0.5f, 2f)
            });

        SetPrivateField(prototype, "woodMesh", woodMesh);
        SetPrivateField(prototype, "foliageMesh", foliageMesh);

        VegetationPhaseAAuthoringSync.RefreshBranchPrototypeLocalBounds(prototype);

        Bounds expectedBounds = woodMesh.bounds;
        expectedBounds.Encapsulate(foliageMesh.bounds);
        AssertBoundsApproximatelyEqual(expectedBounds, prototype.LocalBounds);
    }

    [Test]
    public void RefreshBlueprintFromAssemblyAsset_RebuildsPlacementsAssignsLodProfileAndProducesValidBlueprint()
    {
        EnsureTestFolders();

        Mesh woodMesh = CreateMeshAsset(
            "wood.asset",
            new[]
            {
                new Vector3(-0.25f, -0.25f, -0.25f),
                new Vector3(0.25f, 0.75f, -0.25f),
                new Vector3(-0.25f, -0.25f, 0.25f)
            });
        Mesh foliageMesh = CreateMeshAsset(
            "foliage.asset",
            new[]
            {
                new Vector3(-0.5f, 0f, -0.5f),
                new Vector3(0.5f, 1f, -0.5f),
                new Vector3(-0.5f, 0f, 0.5f)
            });
        Mesh trunkMesh = CreateMeshAsset(
            "trunk.asset",
            new[]
            {
                new Vector3(-0.25f, -1f, -0.25f),
                new Vector3(0.25f, 2f, -0.25f),
                new Vector3(-0.25f, -1f, 0.25f)
            });
        Material woodMaterial = CreateMaterialAsset("wood.mat");
        Material foliageMaterial = CreateMaterialAsset("foliage.mat");
        Material trunkMaterial = CreateMaterialAsset("trunk.mat");

        BranchPrototypeSO prototype = CreateAsset<BranchPrototypeSO>("Prototype.asset");
        SetPrivateField(prototype, "woodMesh", woodMesh);
        SetPrivateField(prototype, "woodMaterial", woodMaterial);
        SetPrivateField(prototype, "foliageMesh", foliageMesh);
        SetPrivateField(prototype, "foliageMaterial", foliageMaterial);
        SetPrivateField(prototype, "localBounds", new Bounds(Vector3.zero, Vector3.one * 0.1f));
        SetPrivateField(prototype, "triangleBudgetWood", 8);
        SetPrivateField(prototype, "triangleBudgetFoliage", 8);

        LODProfileSO lodProfile = CreateAsset<LODProfileSO>("Lod.asset");
        SetPrivateField(lodProfile, "r0MinProjectedArea", 0.5f);
        SetPrivateField(lodProfile, "r1MinProjectedArea", 0.25f);
        SetPrivateField(lodProfile, "shellL0MinProjectedArea", 0.12f);
        SetPrivateField(lodProfile, "shellL1MinProjectedArea", 0.06f);
        SetPrivateField(lodProfile, "shellL2MinProjectedArea", 0.02f);
        SetPrivateField(lodProfile, "absoluteCullProjectedMin", 0.005f);
        SetPrivateField(lodProfile, "backsideBiasScale", 0.2f);
        SetPrivateField(lodProfile, "silhouetteKeepThreshold", 0.7f);

        TreeBlueprintSO blueprint = CreateAsset<TreeBlueprintSO>("Blueprint.asset");
        SetPrivateField(blueprint, "trunkMesh", trunkMesh);
        SetPrivateField(blueprint, "trunkMaterial", trunkMaterial);
        SetPrivateField(blueprint, "treeBounds", new Bounds(Vector3.zero, Vector3.one * 0.1f));

        string branchPrefabPath = CreateBranchPrefabAsset("Branch.prefab", woodMesh, woodMaterial, foliageMesh, foliageMaterial);
        string assemblyPrefabPath = CreateAssemblyPrefabAsset("Tree.prefab", branchPrefabPath);

        VegetationPhaseAAuthoringSync.RefreshBranchPrototypeLocalBounds(prototype);
        VegetationPhaseAAuthoringSync.RefreshBlueprintFromAssemblyAsset(
            blueprint,
            assemblyPrefabPath,
            new[] { prototype },
            lodProfile);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Assert.AreEqual(lodProfile, blueprint.LodProfile);
        Assert.AreEqual(2, blueprint.Branches.Length);

        float[] scales = { blueprint.Branches[0].Scale, blueprint.Branches[1].Scale };
        Array.Sort(scales);
        Assert.AreEqual(0.75f, scales[0], 0.0001f);
        Assert.AreEqual(1.25f, scales[1], 0.0001f);

        for (int i = 0; i < blueprint.Branches.Length; i++)
        {
            Assert.AreEqual(prototype, blueprint.Branches[i].Prototype);
        }

        VegetationValidationResult validationResult = blueprint.Validate();
        Assert.IsFalse(
            validationResult.HasErrors,
            string.Join(Environment.NewLine, GetMessages(validationResult)));
    }

    [Test]
    public void ReconstructFromDataAndOriginalBranch_RebuildsBranchHierarchyFromBlueprint()
    {
        EnsureTestFolders();

        Mesh woodMesh = CreateMeshAsset(
            "reconstruct_wood.asset",
            new[]
            {
                new Vector3(-0.25f, -0.25f, -0.25f),
                new Vector3(0.25f, 0.75f, -0.25f),
                new Vector3(-0.25f, -0.25f, 0.25f)
            });
        Mesh foliageMesh = CreateMeshAsset(
            "reconstruct_foliage.asset",
            new[]
            {
                new Vector3(-0.5f, 0f, -0.5f),
                new Vector3(0.5f, 1f, -0.5f),
                new Vector3(-0.5f, 0f, 0.5f)
            });
        Material woodMaterial = CreateMaterialAsset("reconstruct_wood.mat");
        Material foliageMaterial = CreateMaterialAsset("reconstruct_foliage.mat");

        BranchPrototypeSO prototype = CreateTransientScriptableObject<BranchPrototypeSO>();
        SetPrivateField(prototype, "woodMesh", woodMesh);
        SetPrivateField(prototype, "woodMaterial", woodMaterial);
        SetPrivateField(prototype, "foliageMesh", foliageMesh);
        SetPrivateField(prototype, "foliageMaterial", foliageMaterial);

        BranchPlacement firstPlacement = CreateBranchPlacement(
            prototype,
            new Vector3(1f, 2f, 3f),
            Quaternion.Euler(0f, 45f, 0f),
            1.25f);
        BranchPlacement secondPlacement = CreateBranchPlacement(
            prototype,
            new Vector3(-2f, 1f, 0.5f),
            Quaternion.Euler(10f, 0f, 90f),
            0.75f);

        TreeBlueprintSO blueprint = CreateTransientScriptableObject<TreeBlueprintSO>();
        SetPrivateField(blueprint, "branches", new[] { firstPlacement, secondPlacement });

        GameObject authoringObject = CreateTransientGameObject("Authoring");
        GameObject branchRoot = CreateTransientGameObject("BranchRoot");
        branchRoot.transform.SetParent(authoringObject.transform, false);

        VegetationTreeAuthoring authoring = authoringObject.AddComponent<VegetationTreeAuthoring>();
        SetPrivateField(authoring, "blueprint", blueprint);
        SetPrivateField(authoring, "_rootForBranches", branchRoot);

        authoring.ReconstructFromDataAndOriginalBranch();

        Assert.AreEqual(2, branchRoot.transform.childCount);

        Transform firstBranch = branchRoot.transform.GetChild(0);
        Assert.AreEqual(firstPlacement.LocalPosition, firstBranch.localPosition);
        Assert.AreEqual(firstPlacement.LocalRotation, firstBranch.localRotation);
        Assert.AreEqual(Vector3.one * firstPlacement.Scale, firstBranch.localScale);
        Assert.AreEqual(2, firstBranch.childCount);
        Assert.AreEqual(woodMesh, firstBranch.GetChild(0).GetComponent<MeshFilter>().sharedMesh);
        Assert.AreEqual(woodMaterial, firstBranch.GetChild(0).GetComponent<MeshRenderer>().sharedMaterial);
        Assert.AreEqual(foliageMesh, firstBranch.GetChild(1).GetComponent<MeshFilter>().sharedMesh);
        Assert.AreEqual(foliageMaterial, firstBranch.GetChild(1).GetComponent<MeshRenderer>().sharedMaterial);
    }

    [Test]
    public void DeleteOriginals_RemovesAllChildrenFromBranchRoot()
    {
        GameObject authoringObject = CreateTransientGameObject("Authoring");
        GameObject branchRoot = CreateTransientGameObject("BranchRoot");
        branchRoot.transform.SetParent(authoringObject.transform, false);
        CreateTransientGameObject("OriginalBranchA").transform.SetParent(branchRoot.transform, false);
        CreateTransientGameObject("OriginalBranchB").transform.SetParent(branchRoot.transform, false);

        VegetationTreeAuthoring authoring = authoringObject.AddComponent<VegetationTreeAuthoring>();
        SetPrivateField(authoring, "_rootForBranches", branchRoot);

        authoring.DeleteOriginals();

        Assert.AreEqual(0, branchRoot.transform.childCount);
    }

    private static void AssertBoundsApproximatelyEqual(Bounds expected, Bounds actual)
    {
        Assert.AreEqual(expected.center.x, actual.center.x, 0.0001f);
        Assert.AreEqual(expected.center.y, actual.center.y, 0.0001f);
        Assert.AreEqual(expected.center.z, actual.center.z, 0.0001f);
        Assert.AreEqual(expected.size.x, actual.size.x, 0.0001f);
        Assert.AreEqual(expected.size.y, actual.size.y, 0.0001f);
        Assert.AreEqual(expected.size.z, actual.size.z, 0.0001f);
    }

    private static string[] GetMessages(VegetationValidationResult result)
    {
        string[] messages = new string[result.Issues.Count];
        for (int i = 0; i < result.Issues.Count; i++)
        {
            messages[i] = result.Issues[i].Message;
        }

        return messages;
    }

    private void EnsureTestFolders()
    {
        if (!AssetDatabase.IsValidFolder("Assets/__GeneratedTests__"))
        {
            AssetDatabase.CreateFolder("Assets", "__GeneratedTests__");
        }

        if (!AssetDatabase.IsValidFolder(TestAssetRoot))
        {
            AssetDatabase.CreateFolder("Assets/__GeneratedTests__", "VegetationAuthoringSync");
        }
    }

    private T CreateAsset<T>(string fileName) where T : ScriptableObject
    {
        EnsureTestFolders();
        T asset = ScriptableObject.CreateInstance<T>();
        string assetPath = $"{TestAssetRoot}/{fileName}";
        AssetDatabase.CreateAsset(asset, assetPath);
        createdObjects.Add(asset);
        return asset;
    }

    private T CreateTransientScriptableObject<T>() where T : ScriptableObject
    {
        T instance = ScriptableObject.CreateInstance<T>();
        createdObjects.Add(instance);
        return instance;
    }

    private GameObject CreateTransientGameObject(string name)
    {
        GameObject gameObject = new GameObject(name);
        createdObjects.Add(gameObject);
        return gameObject;
    }

    private Mesh CreateMeshAsset(string fileName, Vector3[] vertices)
    {
        EnsureTestFolders();
        Mesh mesh = new Mesh
        {
            name = fileName
        };

        mesh.vertices = vertices;
        mesh.triangles = new[] { 0, 1, 2 };
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();

        string assetPath = $"{TestAssetRoot}/{fileName}";
        AssetDatabase.CreateAsset(mesh, assetPath);
        createdObjects.Add(mesh);
        return mesh;
    }

    private Material CreateMaterialAsset(string fileName)
    {
        EnsureTestFolders();

        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ??
                        Shader.Find("Universal Render Pipeline/Simple Lit") ??
                        Shader.Find("Standard") ??
                        throw new InvalidOperationException("No supported opaque shader was found for tests.");

        Material material = new Material(shader)
        {
            name = fileName
        };

        if (material.HasProperty("_Surface"))
        {
            material.SetFloat("_Surface", 0f);
        }

        if (material.HasProperty("_AlphaClip"))
        {
            material.SetFloat("_AlphaClip", 0f);
        }

        string assetPath = $"{TestAssetRoot}/{fileName}";
        AssetDatabase.CreateAsset(material, assetPath);
        createdObjects.Add(material);
        return material;
    }

    private string CreateBranchPrefabAsset(
        string fileName,
        Mesh woodMesh,
        Material woodMaterial,
        Mesh foliageMesh,
        Material foliageMaterial)
    {
        EnsureTestFolders();

        GameObject root = new GameObject("BranchRoot");
        createdObjects.Add(root);

        GameObject woodChild = new GameObject("Wood");
        woodChild.transform.SetParent(root.transform, false);
        MeshFilter woodFilter = woodChild.AddComponent<MeshFilter>();
        woodFilter.sharedMesh = woodMesh;
        MeshRenderer woodRenderer = woodChild.AddComponent<MeshRenderer>();
        woodRenderer.sharedMaterial = woodMaterial;

        GameObject foliageChild = new GameObject("Foliage");
        foliageChild.transform.SetParent(root.transform, false);
        MeshFilter foliageFilter = foliageChild.AddComponent<MeshFilter>();
        foliageFilter.sharedMesh = foliageMesh;
        MeshRenderer foliageRenderer = foliageChild.AddComponent<MeshRenderer>();
        foliageRenderer.sharedMaterial = foliageMaterial;

        string prefabPath = $"{TestAssetRoot}/{fileName}";
        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        UnityEngine.Object.DestroyImmediate(root);
        return prefabPath;
    }

    private string CreateAssemblyPrefabAsset(string fileName, string branchPrefabPath)
    {
        EnsureTestFolders();

        GameObject branchPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(branchPrefabPath);
        GameObject root = new GameObject("TreeRoot");
        createdObjects.Add(root);

        GameObject directBranch = (GameObject)PrefabUtility.InstantiatePrefab(branchPrefab);
        directBranch.transform.SetParent(root.transform, false);
        directBranch.transform.localPosition = new Vector3(0f, 1f, 0f);
        directBranch.transform.localRotation = Quaternion.Euler(0f, 30f, 0f);
        directBranch.transform.localScale = Vector3.one * 1.2f;

        GameObject group = new GameObject("GroupedBranches");
        group.transform.SetParent(root.transform, false);
        group.transform.localPosition = new Vector3(1f, 0.5f, 0.25f);
        group.transform.localRotation = Quaternion.Euler(0f, 15f, 0f);
        group.transform.localScale = Vector3.one * 0.5f;

        GameObject groupedBranch = (GameObject)PrefabUtility.InstantiatePrefab(branchPrefab);
        groupedBranch.transform.SetParent(group.transform, false);
        groupedBranch.transform.localPosition = new Vector3(0.5f, 0.25f, 1f);
        groupedBranch.transform.localRotation = Quaternion.Euler(10f, 0f, 45f);
        groupedBranch.transform.localScale = Vector3.one * 1.5f;

        string prefabPath = $"{TestAssetRoot}/{fileName}";
        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        UnityEngine.Object.DestroyImmediate(root);
        return prefabPath;
    }

    private static BranchPlacement CreateBranchPlacement(
        BranchPrototypeSO prototype,
        Vector3 localPosition,
        Quaternion localRotation,
        float scale)
    {
        BranchPlacement placement = new BranchPlacement();
        SetPrivateField(placement, "prototype", prototype);
        SetPrivateField(placement, "localPosition", localPosition);
        SetPrivateField(placement, "localRotation", localRotation);
        SetPrivateField(placement, "scale", scale);
        return placement;
    }

    private static void SetPrivateField(object target, string fieldName, object? value)
    {
        System.Reflection.FieldInfo? fieldInfo = target.GetType().GetField(
            fieldName,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        if (fieldInfo == null)
        {
            throw new InvalidOperationException($"Field '{fieldName}' was not found on '{target.GetType().Name}'.");
        }

        fieldInfo.SetValue(target, value);
    }
}
