#nullable enable

using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VoxGeoFol.Features.Vegetation.Authoring;
using VoxGeoFol.Features.Vegetation.Editor;

[TestFixture]
public sealed class VegetationEditorAuthoringTests
{
    private const string TestAssetRoot = "Assets/__GeneratedTests__/VegetationEditorAuthoring";
    private const string GeneratedMeshAssetRoot = TestAssetRoot + "/GeneratedMeshes";
    private readonly List<UnityEngine.Object> createdObjects = new List<UnityEngine.Object>();
    private readonly List<string> createdGeneratedMeshAssetPaths = new List<string>();

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

        for (int i = createdGeneratedMeshAssetPaths.Count - 1; i >= 0; i--)
        {
            string assetPath = createdGeneratedMeshAssetPaths[i];
            if (!string.IsNullOrEmpty(assetPath))
            {
                AssetDatabase.DeleteAsset(assetPath);
            }
        }

        createdGeneratedMeshAssetPaths.Clear();

        if (AssetDatabase.IsValidFolder(TestAssetRoot))
        {
            AssetDatabase.DeleteAsset(TestAssetRoot);
        }

        AssetDatabase.Refresh();
    }

    [Test]
    public void ShowPreview_R0Full_RebuildsTransientHierarchyFromBlueprint()
    {
        Mesh trunkMesh = CreateMeshAsset(
            "reconstruct_trunk.asset",
            new[]
            {
                new Vector3(-0.2f, -1f, -0.2f),
                new Vector3(0.2f, 1.5f, -0.2f),
                new Vector3(-0.2f, -1f, 0.2f)
            });
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
        Mesh shellL0Mesh = CreateMeshAsset(
            "reconstruct_shell_l0.asset",
            new[]
            {
                new Vector3(-0.6f, -0.1f, -0.6f),
                new Vector3(0.6f, 1.1f, -0.6f),
                new Vector3(-0.6f, -0.1f, 0.6f)
            });
        Material trunkMaterial = CreateMaterialAsset("reconstruct_trunk.mat");
        Material woodMaterial = CreateMaterialAsset("reconstruct_wood.mat");
        Material foliageMaterial = CreateMaterialAsset("reconstruct_foliage.mat");
        Material shellMaterial = CreateMaterialAsset("reconstruct_shell_l0.mat");

        BranchPrototypeSO prototype = CreateTransientScriptableObject<BranchPrototypeSO>();
        SetPrivateField(prototype, "woodMesh", woodMesh);
        SetPrivateField(prototype, "woodMaterial", woodMaterial);
        SetPrivateField(prototype, "foliageMesh", foliageMesh);
        SetPrivateField(prototype, "foliageMaterial", foliageMaterial);
        SetPrivateField(prototype, "shellNodes", new[] { CreateShellNode(shellL0Mesh, shellL0Mesh, shellL0Mesh) });
        SetPrivateField(prototype, "shellMaterial", shellMaterial);

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
        SetPrivateField(blueprint, "trunkMesh", trunkMesh);
        SetPrivateField(blueprint, "trunkMaterial", trunkMaterial);
        SetPrivateField(blueprint, "branches", new[] { firstPlacement, secondPlacement });

        GameObject authoringObject = CreateTransientGameObject("Authoring");
        GameObject branchRoot = CreateTransientGameObject("BranchRoot");
        branchRoot.transform.SetParent(authoringObject.transform, false);

        VegetationTreeAuthoring authoring = authoringObject.AddComponent<VegetationTreeAuthoring>();
        SetPrivateField(authoring, "blueprint", blueprint);
        SetPrivateField(authoring, "_rootForBranches", branchRoot);

        VegetationEditorPreview.ShowPreview(authoring, VegetationPreviewTier.R0Full);

        Assert.AreEqual(1, branchRoot.transform.childCount);

        Transform previewRoot = branchRoot.transform.GetChild(0);
        Assert.AreEqual(HideFlags.DontSave | HideFlags.NotEditable, previewRoot.gameObject.hideFlags);
        Assert.AreEqual(3, previewRoot.childCount);
        Assert.AreEqual(trunkMesh, previewRoot.GetChild(0).GetComponent<MeshFilter>().sharedMesh);
        Assert.AreEqual(trunkMaterial, previewRoot.GetChild(0).GetComponent<MeshRenderer>().sharedMaterial);

        Transform firstBranch = previewRoot.GetChild(1);
        Assert.AreEqual(firstPlacement.LocalPosition, firstBranch.localPosition);
        AssertQuaternionApproximatelyEqual(firstPlacement.LocalRotation, firstBranch.localRotation);
        Assert.AreEqual(Vector3.one * firstPlacement.Scale, firstBranch.localScale);
        Assert.AreEqual(3, firstBranch.childCount);
        Assert.AreEqual(woodMesh, firstBranch.GetChild(0).GetComponent<MeshFilter>().sharedMesh);
        Assert.AreEqual(woodMaterial, firstBranch.GetChild(0).GetComponent<MeshRenderer>().sharedMaterial);
        Assert.AreEqual(foliageMesh, firstBranch.GetChild(1).GetComponent<MeshFilter>().sharedMesh);
        Assert.AreEqual(foliageMaterial, firstBranch.GetChild(1).GetComponent<MeshRenderer>().sharedMaterial);
        Assert.AreEqual(shellL0Mesh, firstBranch.GetChild(2).GetComponent<MeshFilter>().sharedMesh);
        Assert.AreEqual(shellMaterial, firstBranch.GetChild(2).GetComponent<MeshRenderer>().sharedMaterial);
    }

    [Test]
    public void ClearPreview_RemovesAllChildrenFromBranchRoot()
    {
        GameObject authoringObject = CreateTransientGameObject("Authoring");
        GameObject branchRoot = CreateTransientGameObject("BranchRoot");
        branchRoot.transform.SetParent(authoringObject.transform, false);
        CreateTransientGameObject("OriginalBranchA").transform.SetParent(branchRoot.transform, false);
        CreateTransientGameObject("OriginalBranchB").transform.SetParent(branchRoot.transform, false);

        VegetationTreeAuthoring authoring = authoringObject.AddComponent<VegetationTreeAuthoring>();
        SetPrivateField(authoring, "_rootForBranches", branchRoot);

        VegetationEditorPreview.ClearPreview(authoring);

        Assert.AreEqual(0, branchRoot.transform.childCount);
    }

    [Test]
    public void ShowPreview_ShellL1Only_RebuildsBranchHierarchyFromLeafFrontier()
    {
        Mesh woodMesh = CreateMeshAsset(
            "reconstruct_shell_l1_wood.asset",
            new[]
            {
                new Vector3(-0.1f, -0.4f, -0.1f),
                new Vector3(0.1f, 0.4f, -0.1f),
                new Vector3(-0.1f, -0.4f, 0.1f)
            });
        Mesh shellMesh = CreateMeshAsset(
            "reconstruct_shell.asset",
            new[]
            {
                new Vector3(-0.5f, -0.5f, -0.5f),
                new Vector3(0.5f, 0.5f, -0.5f),
                new Vector3(-0.5f, -0.5f, 0.5f)
            });
        Material woodMaterial = CreateMaterialAsset("reconstruct_shell_l1_wood.mat");
        Material shellMaterial = CreateMaterialAsset("reconstruct_shell.mat");

        BranchPrototypeSO prototype = CreateTransientScriptableObject<BranchPrototypeSO>();
        SetPrivateField(prototype, "woodMaterial", woodMaterial);
        SetPrivateField(prototype, "shellL1WoodMesh", woodMesh);
        SetPrivateField(prototype, "shellNodes", new[] { CreateShellNode(shellMesh, shellMesh, shellMesh) });
        SetPrivateField(prototype, "shellMaterial", shellMaterial);

        BranchPlacement placement = CreateBranchPlacement(
            prototype,
            new Vector3(2f, 1f, -3f),
            Quaternion.Euler(0f, 60f, 15f),
            1.5f);

        TreeBlueprintSO blueprint = CreateTransientScriptableObject<TreeBlueprintSO>();
        SetPrivateField(blueprint, "branches", new[] { placement });

        GameObject authoringObject = CreateTransientGameObject("Authoring");
        GameObject branchRoot = CreateTransientGameObject("BranchRoot");
        branchRoot.transform.SetParent(authoringObject.transform, false);

        VegetationTreeAuthoring authoring = authoringObject.AddComponent<VegetationTreeAuthoring>();
        SetPrivateField(authoring, "blueprint", blueprint);
        SetPrivateField(authoring, "_rootForBranches", branchRoot);

        VegetationEditorPreview.ShowPreview(authoring, VegetationPreviewTier.ShellL1Only);

        Assert.AreEqual(1, branchRoot.transform.childCount);
        Transform previewRoot = branchRoot.transform.GetChild(0);
        Transform branch = previewRoot.GetChild(0);
        Assert.AreEqual(placement.LocalPosition, branch.localPosition);
        AssertQuaternionApproximatelyEqual(placement.LocalRotation, branch.localRotation);
        Assert.AreEqual(Vector3.one * placement.Scale, branch.localScale);
        Assert.AreEqual(2, branch.childCount);
        Assert.AreEqual(woodMesh, branch.GetChild(0).GetComponent<MeshFilter>().sharedMesh);
        Assert.AreEqual(woodMaterial, branch.GetChild(0).GetComponent<MeshRenderer>().sharedMaterial);
        Assert.AreEqual(shellMesh, branch.GetChild(1).GetComponent<MeshFilter>().sharedMesh);
        Assert.AreEqual(shellMaterial, branch.GetChild(1).GetComponent<MeshRenderer>().sharedMaterial);
    }

    [Test]
    public void ShowPreview_R3Impostor_CreatesSingleImpostorObject()
    {
        Mesh impostorMesh = CreateMeshAsset(
            "preview_impostor.asset",
            new[]
            {
                new Vector3(-1f, 0f, 0f),
                new Vector3(1f, 2f, 0f),
                new Vector3(-1f, 2f, 0f)
            });
        Material impostorMaterial = CreateMaterialAsset("preview_impostor.mat");

        TreeBlueprintSO blueprint = CreateTransientScriptableObject<TreeBlueprintSO>();
        SetPrivateField(blueprint, "impostorMesh", impostorMesh);
        SetPrivateField(blueprint, "impostorMaterial", impostorMaterial);
        SetPrivateField(blueprint, "branches", new[] { CreateBranchPlacement(CreateTransientScriptableObject<BranchPrototypeSO>(), Vector3.zero, Quaternion.identity, 1f) });

        GameObject authoringObject = CreateTransientGameObject("Authoring");
        GameObject branchRoot = CreateTransientGameObject("BranchRoot");
        branchRoot.transform.SetParent(authoringObject.transform, false);

        VegetationTreeAuthoring authoring = authoringObject.AddComponent<VegetationTreeAuthoring>();
        SetPrivateField(authoring, "blueprint", blueprint);
        SetPrivateField(authoring, "_rootForBranches", branchRoot);

        VegetationEditorPreview.ShowPreview(authoring, VegetationPreviewTier.R3Impostor);

        Assert.AreEqual(1, branchRoot.transform.childCount);
        Transform previewRoot = branchRoot.transform.GetChild(0);
        Assert.AreEqual(1, previewRoot.childCount);
        Assert.AreEqual(impostorMesh, previewRoot.GetChild(0).GetComponent<MeshFilter>().sharedMesh);
        Assert.AreEqual(impostorMaterial, previewRoot.GetChild(0).GetComponent<MeshRenderer>().sharedMaterial);
    }

    [Test]
    public void BakeCanopyShellsAndImpostor_FromEditorUtility_PopulatesGeneratedMeshes()
    {
        EnsureTestFolders();

        Mesh woodMesh = CreateMeshAsset(
            "bake_entry_wood.asset",
            new[]
            {
                new Vector3(-0.1f, -0.4f, -0.1f),
                new Vector3(0.1f, 0.4f, -0.1f),
                new Vector3(-0.1f, -0.4f, 0.1f)
            });
        Mesh foliageMesh = CreateClosedCubeMeshAsset("bake_entry_foliage.asset", new Vector3(1.2f, 1f, 1.2f));
        Mesh trunkMesh = CreateClosedCubeMeshAsset("bake_entry_trunk.asset", new Vector3(0.5f, 2f, 0.5f));
        Material shellMaterial = CreateMaterialAsset("bake_entry_shell.mat");

        BranchPrototypeSO prototype = CreateAsset<BranchPrototypeSO>("BakeEntryPrototype.asset");
        SetPrivateField(prototype, "woodMesh", woodMesh);
        SetPrivateField(prototype, "foliageMesh", foliageMesh);
        SetPrivateField(prototype, "shellMaterial", shellMaterial);
        SetPrivateField(prototype, "triangleBudgetShellL0", 384);
        SetPrivateField(prototype, "triangleBudgetShellL1", 192);
        SetPrivateField(prototype, "triangleBudgetShellL2", 96);

        BranchPlacement placement = CreateBranchPlacement(
            prototype,
            new Vector3(0f, 1f, 0f),
            Quaternion.identity,
            1f);

        TreeBlueprintSO blueprint = CreateAsset<TreeBlueprintSO>("BakeEntryBlueprint.asset");
        SetPrivateField(blueprint, "trunkMesh", trunkMesh);
        SetPrivateField(blueprint, "branches", new[] { placement });

        GameObject authoringObject = CreateTransientGameObject("Authoring");
        VegetationTreeAuthoring authoring = authoringObject.AddComponent<VegetationTreeAuthoring>();
        SetPrivateField(authoring, "blueprint", blueprint);

        VegetationTreeAuthoringEditorUtility.BakeCanopyShellsAndImpostor(authoring);

        Assert.Greater(prototype.ShellNodes.Length, 0);
        for (int i = 0; i < prototype.ShellNodes.Length; i++)
        {
            AssertGeneratedMeshStored(prototype.ShellNodes[i].ShellL0Mesh);
            AssertGeneratedMeshStored(prototype.ShellNodes[i].ShellL1Mesh);
            AssertGeneratedMeshStored(prototype.ShellNodes[i].ShellL2Mesh);
        }

        AssertGeneratedMeshStored(prototype.ShellL1WoodMesh);
        AssertGeneratedMeshStored(prototype.ShellL2WoodMesh);
        AssertGeneratedMeshStored(blueprint.ImpostorMesh);
    }

    private void AssertGeneratedMeshStored(Mesh? mesh)
    {
        Assert.IsNotNull(mesh);
        Mesh nonNullMesh = mesh!;

        string assetPath = AssetDatabase.GetAssetPath(nonNullMesh);
        TrackGeneratedMeshAssetPath(assetPath);

        Assert.IsTrue(AssetDatabase.Contains(nonNullMesh));
        StringAssert.StartsWith($"{GeneratedMeshAssetRoot}/", assetPath);
        StringAssert.EndsWith(".mesh", assetPath);
    }

    private void TrackGeneratedMeshAssetPath(string assetPath)
    {
        if (!string.IsNullOrEmpty(assetPath) && !createdGeneratedMeshAssetPaths.Contains(assetPath))
        {
            createdGeneratedMeshAssetPaths.Add(assetPath);
        }
    }

    private static void AssertQuaternionApproximatelyEqual(Quaternion expected, Quaternion actual)
    {
        Assert.AreEqual(expected.x, actual.x, 0.0001f);
        Assert.AreEqual(expected.y, actual.y, 0.0001f);
        Assert.AreEqual(expected.z, actual.z, 0.0001f);
        Assert.AreEqual(expected.w, actual.w, 0.0001f);
    }

    private void EnsureTestFolders()
    {
        if (!AssetDatabase.IsValidFolder("Assets/__GeneratedTests__"))
        {
            AssetDatabase.CreateFolder("Assets", "__GeneratedTests__");
        }

        if (!AssetDatabase.IsValidFolder(TestAssetRoot))
        {
            AssetDatabase.CreateFolder("Assets/__GeneratedTests__", "VegetationEditorAuthoring");
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
        return CreateMeshAsset(fileName, vertices, new[] { 0, 1, 2 });
    }

    private Mesh CreateClosedCubeMeshAsset(string fileName, Vector3 size)
    {
        Vector3 halfSize = size * 0.5f;
        return CreateMeshAsset(
            fileName,
            new[]
            {
                new Vector3(-halfSize.x, -halfSize.y, -halfSize.z),
                new Vector3(halfSize.x, -halfSize.y, -halfSize.z),
                new Vector3(halfSize.x, halfSize.y, -halfSize.z),
                new Vector3(-halfSize.x, halfSize.y, -halfSize.z),
                new Vector3(-halfSize.x, -halfSize.y, halfSize.z),
                new Vector3(halfSize.x, -halfSize.y, halfSize.z),
                new Vector3(halfSize.x, halfSize.y, halfSize.z),
                new Vector3(-halfSize.x, halfSize.y, halfSize.z)
            },
            new[]
            {
                0, 2, 1, 0, 3, 2,
                4, 5, 6, 4, 6, 7,
                0, 7, 3, 0, 4, 7,
                1, 2, 6, 1, 6, 5,
                3, 7, 6, 3, 6, 2,
                0, 1, 5, 0, 5, 4
            });
    }

    private Mesh CreateMeshAsset(string fileName, Vector3[] vertices, int[] triangles)
    {
        EnsureTestFolders();
        Mesh mesh = new Mesh
        {
            name = fileName
        };

        mesh.vertices = vertices;
        mesh.triangles = triangles;
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

    private BranchShellNode CreateShellNode(Mesh l0Mesh, Mesh l1Mesh, Mesh l2Mesh)
    {
        return new BranchShellNode(new Bounds(Vector3.zero, Vector3.one), 0, -1, 0, l0Mesh, l1Mesh, l2Mesh);
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
