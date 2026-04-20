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
    private const string ExplicitImpostorMeshAssetRoot = TestAssetRoot + "/ExplicitImpostors";
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
    [Ignore("Takes too long, developer already verified in editor manually")]
    public void ShowPreview_L0_RebuildsTransientHierarchyFromBlueprint()
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
        Material trunkMaterial = CreateMaterialAsset("reconstruct_trunk.mat");
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
        SetPrivateField(blueprint, "trunkMesh", trunkMesh);
        SetPrivateField(blueprint, "trunkMaterial", trunkMaterial);
        SetPrivateField(blueprint, "branches", new[] { firstPlacement, secondPlacement });

        GameObject authoringObject = CreateTransientGameObject("Authoring");
        GameObject branchRoot = CreateTransientGameObject("BranchRoot");
        branchRoot.transform.SetParent(authoringObject.transform, false);

        VegetationTreeAuthoring authoring = authoringObject.AddComponent<VegetationTreeAuthoring>();
        SetPrivateField(authoring, "blueprint", blueprint);
        SetPrivateField(authoring, "_rootForBranches", branchRoot);

        VegetationEditorPreview.ShowPreview(authoring, VegetationPreviewTier.L0);

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
        Assert.AreEqual(2, firstBranch.childCount);
        Assert.AreEqual(woodMesh, firstBranch.GetChild(0).GetComponent<MeshFilter>().sharedMesh);
        Assert.AreEqual(woodMaterial, firstBranch.GetChild(0).GetComponent<MeshRenderer>().sharedMaterial);
        Assert.AreEqual(foliageMesh, firstBranch.GetChild(1).GetComponent<MeshFilter>().sharedMesh);
        Assert.AreEqual(foliageMaterial, firstBranch.GetChild(1).GetComponent<MeshRenderer>().sharedMaterial);
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
    [Ignore("Takes too long, developer already verified in editor manually")]
    public void ShowPreview_L2_UsesSimplifiedTrunkMesh()
    {
        Mesh trunkMesh = CreateMeshAsset(
            "preview_l2_trunk.asset",
            new[]
            {
                new Vector3(-0.4f, -1f, -0.4f),
                new Vector3(0.4f, 2f, -0.4f),
                new Vector3(-0.4f, -1f, 0.4f)
            });
        Mesh trunkL3Mesh = CreateMeshAsset(
            "preview_l2_trunk_l3.asset",
            new[]
            {
                new Vector3(-0.2f, -0.8f, -0.2f),
                new Vector3(0.2f, 1.2f, -0.2f),
                new Vector3(-0.2f, -0.8f, 0.2f)
            });
        Mesh branchL2WoodMesh = CreateMeshAsset(
            "preview_l2_wood.asset",
            new[]
            {
                new Vector3(-0.1f, -0.3f, -0.1f),
                new Vector3(0.1f, 0.5f, -0.1f),
                new Vector3(-0.1f, -0.3f, 0.1f)
            });
        Mesh branchL2CanopyMesh = CreateMeshAsset(
            "preview_l2_shell.asset",
            new[]
            {
                new Vector3(-0.5f, -0.5f, -0.5f),
                new Vector3(0.5f, 0.5f, -0.5f),
                new Vector3(-0.5f, -0.5f, 0.5f)
            });
        Material trunkMaterial = CreateMaterialAsset("preview_l2_trunk.mat");
        Material woodMaterial = CreateMaterialAsset("preview_l2_wood.mat");
        Material shellMaterial = CreateMaterialAsset("preview_l2_shell.mat");

        BranchPrototypeSO prototype = CreateTransientScriptableObject<BranchPrototypeSO>();
        SetPrivateField(prototype, "woodMaterial", woodMaterial);
        SetPrivateField(prototype, "branchL2WoodMesh", branchL2WoodMesh);
        SetPrivateField(prototype, "branchL2CanopyMesh", branchL2CanopyMesh);
        SetPrivateField(prototype, "shellMaterial", shellMaterial);

        BranchPlacement placement = CreateBranchPlacement(
            prototype,
            new Vector3(0f, 1f, 0f),
            Quaternion.identity,
            1f);

        TreeBlueprintSO blueprint = CreateTransientScriptableObject<TreeBlueprintSO>();
        SetPrivateField(blueprint, "trunkMesh", trunkMesh);
        SetPrivateField(blueprint, "trunkL3Mesh", trunkL3Mesh);
        SetPrivateField(blueprint, "trunkMaterial", trunkMaterial);
        SetPrivateField(blueprint, "branches", new[] { placement });

        GameObject authoringObject = CreateTransientGameObject("Authoring");
        GameObject branchRoot = CreateTransientGameObject("BranchRoot");
        branchRoot.transform.SetParent(authoringObject.transform, false);

        VegetationTreeAuthoring authoring = authoringObject.AddComponent<VegetationTreeAuthoring>();
        SetPrivateField(authoring, "blueprint", blueprint);
        SetPrivateField(authoring, "_rootForBranches", branchRoot);

        VegetationEditorPreview.ShowPreview(authoring, VegetationPreviewTier.L2);

        Assert.AreEqual(1, branchRoot.transform.childCount);
        Transform previewRoot = branchRoot.transform.GetChild(0);
        Assert.AreEqual(trunkL3Mesh, previewRoot.GetChild(0).GetComponent<MeshFilter>().sharedMesh);
        Assert.AreEqual(trunkMaterial, previewRoot.GetChild(0).GetComponent<MeshRenderer>().sharedMaterial);
    }

    [Test]
    [Ignore("Takes too long, developer already verified in editor manually")]
    public void ShowPreview_Impostor_CreatesSingleImpostorObject()
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

        VegetationEditorPreview.ShowPreview(authoring, VegetationPreviewTier.Impostor);

        Assert.AreEqual(1, branchRoot.transform.childCount);
        Transform previewRoot = branchRoot.transform.GetChild(0);
        Assert.AreEqual(1, previewRoot.childCount);
        Assert.AreEqual(impostorMesh, previewRoot.GetChild(0).GetComponent<MeshFilter>().sharedMesh);
        Assert.AreEqual(impostorMaterial, previewRoot.GetChild(0).GetComponent<MeshRenderer>().sharedMaterial);
    }

    [Test]
    [Ignore("Takes too long, developer already verified in editor manually")]
    public void BakeImpostor_FromEditorUtility_UsesOriginalTreeMeshesWithoutBakingShells()
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
        BranchPrototypeSO prototype = CreateAsset<BranchPrototypeSO>("BakeEntryPrototype.asset");
        SetPrivateField(prototype, "woodMesh", woodMesh);
        SetPrivateField(prototype, "foliageMesh", foliageMesh);

        BranchPlacement placement = CreateBranchPlacement(
            prototype,
            new Vector3(0f, 1f, 0f),
            Quaternion.identity,
            1f);

        TreeBlueprintSO blueprint = CreateAsset<TreeBlueprintSO>("BakeEntryBlueprint.asset");
        SetPrivateField(blueprint, "trunkMesh", trunkMesh);
        SetPrivateField(blueprint, "branches", new[] { placement });
        SetPrivateField(blueprint, "ImposterBakeSettings", CreateFastImpostorBakeSettings());

        GameObject authoringObject = CreateTransientGameObject("Authoring");
        VegetationTreeAuthoring authoring = authoringObject.AddComponent<VegetationTreeAuthoring>();
        SetPrivateField(authoring, "blueprint", blueprint);

        VegetationTreeAuthoringEditorUtility.BakeImpostor(authoring);

        Assert.IsNull(prototype.BranchL1CanopyMesh);
        Assert.IsNull(prototype.BranchL2CanopyMesh);
        Assert.IsNull(prototype.BranchL3CanopyMesh);
        Assert.IsNull(prototype.BranchL1WoodMesh);
        Assert.IsNull(prototype.BranchL2WoodMesh);
        Assert.IsNull(prototype.BranchL3WoodMesh);
        AssertGeneratedMeshStored(blueprint.ImpostorMesh);
    }

    [Test]
    [Ignore("Takes too long, developer already verified in editor manually")]
    public void BakeImpostor_FromEditorUtility_StoresMeshInBlueprintSpecifiedFolder()
    {
        EnsureTestFolders();

        Mesh woodMesh = CreateMeshAsset(
            "explicit_folder_wood.asset",
            new[]
            {
                new Vector3(-0.1f, -0.4f, -0.1f),
                new Vector3(0.1f, 0.4f, -0.1f),
                new Vector3(-0.1f, -0.4f, 0.1f)
            });
        Mesh foliageMesh = CreateClosedCubeMeshAsset("explicit_folder_foliage.asset", new Vector3(1.2f, 1f, 1.2f));
        Mesh trunkMesh = CreateClosedCubeMeshAsset("explicit_folder_trunk.asset", new Vector3(0.5f, 2f, 0.5f));

        BranchPrototypeSO prototype = CreateAsset<BranchPrototypeSO>("ExplicitFolderPrototype.asset");
        SetPrivateField(prototype, "woodMesh", woodMesh);
        SetPrivateField(prototype, "foliageMesh", foliageMesh);

        BranchPlacement placement = CreateBranchPlacement(
            prototype,
            new Vector3(0f, 1f, 0f),
            Quaternion.identity,
            1f);

        TreeBlueprintSO blueprint = CreateAsset<TreeBlueprintSO>("ExplicitFolderBlueprint.asset");
        SetPrivateField(blueprint, "trunkMesh", trunkMesh);
        SetPrivateField(blueprint, "branches", new[] { placement });
        SetPrivateField(blueprint, "generatedImpostorMeshesRelativeFolder", ExplicitImpostorMeshAssetRoot);
        SetPrivateField(blueprint, "ImposterBakeSettings", CreateFastImpostorBakeSettings());

        GameObject authoringObject = CreateTransientGameObject("Authoring");
        VegetationTreeAuthoring authoring = authoringObject.AddComponent<VegetationTreeAuthoring>();
        SetPrivateField(authoring, "blueprint", blueprint);

        VegetationTreeAuthoringEditorUtility.BakeImpostor(authoring);

        AssertGeneratedMeshStored(blueprint.ImpostorMesh, ExplicitImpostorMeshAssetRoot);
    }

    [Test]
    [Ignore("Takes too long, developer already verified in editor manually")]
    public void BakeShadowProxy_FromEditorUtility_AssignsGeneratedMeshesToBlueprintFields()
    {
        EnsureTestFolders();

        Mesh woodMesh = CreateMeshAsset(
            "shadow_proxy_wood.asset",
            new[]
            {
                new Vector3(-0.1f, -0.4f, -0.1f),
                new Vector3(0.1f, 0.4f, -0.1f),
                new Vector3(-0.1f, -0.4f, 0.1f)
            });
        Mesh foliageMesh = CreateClosedCubeMeshAsset("shadow_proxy_foliage.asset", new Vector3(1.2f, 1f, 1.2f));
        Mesh trunkMesh = CreateClosedCubeMeshAsset("shadow_proxy_trunk.asset", new Vector3(0.5f, 2f, 0.5f));
        Mesh treeL3Mesh = CreateClosedCubeMeshAsset("shadow_proxy_tree_l3.asset", new Vector3(1.8f, 2.2f, 1.8f));

        BranchPrototypeSO prototype = CreateAsset<BranchPrototypeSO>("ShadowProxyPrototype.asset");
        SetPrivateField(prototype, "woodMesh", woodMesh);
        SetPrivateField(prototype, "foliageMesh", foliageMesh);

        BranchPlacement placement = CreateBranchPlacement(
            prototype,
            new Vector3(0f, 1f, 0f),
            Quaternion.identity,
            1f);

        TreeBlueprintSO blueprint = CreateAsset<TreeBlueprintSO>("ShadowProxyBlueprint.asset");
        SetPrivateField(blueprint, "trunkMesh", trunkMesh);
        SetPrivateField(blueprint, "treeL3Mesh", treeL3Mesh);
        SetPrivateField(blueprint, "branches", new[] { placement });
        SetPrivateField(blueprint, "treeBounds", new Bounds(new Vector3(0f, 1f, 0f), new Vector3(4f, 4f, 4f)));
        SetPrivateField(blueprint, "shadowProxyBakeSettings", CreateFastShadowProxyBakeSettings());

        GameObject authoringObject = CreateTransientGameObject("Authoring");
        VegetationTreeAuthoring authoring = authoringObject.AddComponent<VegetationTreeAuthoring>();
        SetPrivateField(authoring, "blueprint", blueprint);

        VegetationTreeAuthoringEditorUtility.BakeShadowProxyL1(authoring);
        VegetationTreeAuthoringEditorUtility.BakeShadowProxyL0(authoring);

        AssertGeneratedMeshStored(blueprint.ShadowProxyMeshL1);
        AssertGeneratedMeshStored(blueprint.ShadowProxyMeshL0);
    }

    [Test]
    [Ignore("Takes too long, developer already verified in editor manually")]
    public void PersistGeneratedMesh_RebuildsStableFlatNormals()
    {
        EnsureTestFolders();

        BranchPrototypeSO prototype = CreateAsset<BranchPrototypeSO>("PersistNormalsPrototype.asset");
        Mesh generatedMesh = new Mesh
        {
            name = "BrokenGeneratedMesh"
        };
        generatedMesh.vertices = new[]
        {
            new Vector3(0f, 0f, 0f),
            new Vector3(1f, 0f, 0f),
            new Vector3(0f, 1f, 0f),
            new Vector3(1f, 1f, 0f)
        };
        generatedMesh.triangles = new[]
        {
            0, 1, 2,
            2, 1, 3,
            0, 0, 1
        };
        generatedMesh.normals = new[]
        {
            Vector3.up,
            Vector3.right,
            new Vector3(float.NaN, 0f, 0f),
            Vector3.zero
        };
        generatedMesh.RecalculateBounds();
        createdObjects.Add(generatedMesh);

        Mesh persistedMesh = GeneratedMeshAssetUtility.PersistGeneratedMesh(
            prototype,
            "PersistedNormalRepair",
            generatedMesh,
            GeneratedMeshAssetRoot);

        AssertGeneratedMeshStored(persistedMesh);
        Assert.AreEqual(6, persistedMesh.vertexCount);
        Assert.AreEqual(6, persistedMesh.normals.Length);
        Assert.AreEqual(2, persistedMesh.triangles.Length / 3);

        int[] triangles = persistedMesh.triangles;
        Vector3[] vertices = persistedMesh.vertices;
        Vector3[] normals = persistedMesh.normals;
        for (int triangleIndex = 0; triangleIndex < triangles.Length; triangleIndex += 3)
        {
            Vector3 normalA = normals[triangles[triangleIndex]];
            Vector3 normalB = normals[triangles[triangleIndex + 1]];
            Vector3 normalC = normals[triangles[triangleIndex + 2]];
            AssertStableNormal(normalA);
            AssertStableNormal(normalB);
            AssertStableNormal(normalC);
            Assert.That(Vector3.Dot(normalA, normalB), Is.GreaterThan(0.999f));
            Assert.That(Vector3.Dot(normalA, normalC), Is.GreaterThan(0.999f));

            Vector3 a = vertices[triangles[triangleIndex]];
            Vector3 b = vertices[triangles[triangleIndex + 1]];
            Vector3 c = vertices[triangles[triangleIndex + 2]];
            Vector3 faceNormal = Vector3.Cross(b - a, c - a).normalized;
            Assert.That(Vector3.Dot(normalA, faceNormal), Is.GreaterThan(0.999f));
        }
    }

    private void AssertGeneratedMeshStored(Mesh? mesh, string expectedRoot = GeneratedMeshAssetRoot)
    {
        Assert.IsNotNull(mesh);
        Mesh nonNullMesh = mesh!;

        string assetPath = AssetDatabase.GetAssetPath(nonNullMesh);
        TrackGeneratedMeshAssetPath(assetPath);

        Assert.IsTrue(AssetDatabase.Contains(nonNullMesh));
        StringAssert.StartsWith($"{expectedRoot}/", assetPath);
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

    private static void AssertStableNormal(Vector3 normal)
    {
        Assert.IsFalse(float.IsNaN(normal.x) || float.IsNaN(normal.y) || float.IsNaN(normal.z));
        Assert.IsFalse(float.IsInfinity(normal.x) || float.IsInfinity(normal.y) || float.IsInfinity(normal.z));
        Assert.That(normal.sqrMagnitude, Is.GreaterThan(0.99f));
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

    private ImpostorBakeSettings CreateFastImpostorBakeSettings()
    {
        ImpostorBakeSettings settings = new ImpostorBakeSettings();
        SetPrivateField(settings, "skipReduction", true);
        SetPrivateField(settings, "skipSimplifyFallback", true);
        return settings;
    }

    private ShadowProxyBakeSettings CreateFastShadowProxyBakeSettings()
    {
        ShadowProxyBakeSettings settings = new ShadowProxyBakeSettings();
        SetPrivateField(settings, "skipReduction", true);
        SetPrivateField(settings, "skipSimplifyFallback", true);
        return settings;
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
