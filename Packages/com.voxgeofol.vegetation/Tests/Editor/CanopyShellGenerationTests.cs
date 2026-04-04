#nullable enable

using System;
using System.Collections.Generic;
using System.Reflection;
using MeshVoxelizerProject;
using NUnit.Framework;
using UnityEngine;
using VoxGeoFol.Features.Vegetation.Authoring;
using VoxGeoFol.Features.Vegetation.Editor;

[TestFixture]
public sealed class CanopyShellGenerationTests
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
    public void BuildHierarchy_SeparatedClusters_CreateHierarchyNodes()
    {
        Mesh hierarchyMesh = CreateSeparatedClusterMesh("HierarchyFoliage");

        MeshVoxelizerHierarchyNode[] hierarchyNodes = MeshVoxelizerHierarchyBuilder.BuildHierarchy(hierarchyMesh, 80, 16, 6, 4, 1);
        TrackHierarchy(hierarchyNodes);

        Assert.Greater(hierarchyNodes.Length, 1);
        Assert.AreEqual(0, hierarchyNodes[0].Depth);
        Assert.AreNotEqual(0, hierarchyNodes[0].ChildMask);
    }

    [Test]
    public void BakeCanopyShells_PersistedChildRangesStayValid()
    {
        BranchPrototypeSO prototype = CreatePrototypeForShellBake(CreateSeparatedClusterMesh("TopologyFoliage"));

        CanopyShellGenerator.BakeCanopyShells(prototype, CreateShellBakeSettings());
        TrackGeneratedShells(prototype);

        for (int i = 0; i < prototype.ShellNodes.Length; i++)
        {
            BranchShellNode node = prototype.ShellNodes[i];
            if (node.ChildMask == 0)
            {
                Assert.Less(node.FirstChildIndex, 0);
                continue;
            }

            int childCount = CountBits(node.ChildMask);
            Assert.GreaterOrEqual(node.FirstChildIndex, 0);
            Assert.Less(node.FirstChildIndex + childCount - 1, prototype.ShellNodes.Length);

            for (int childOffset = 0; childOffset < childCount; childOffset++)
            {
                BranchShellNode child = prototype.ShellNodes[node.FirstChildIndex + childOffset];
                Assert.AreEqual(node.Depth + 1, child.Depth);
                Assert.IsTrue(ContainsBounds(node.LocalBounds, child.LocalBounds));
            }
        }
    }

    [Test]
    public void ImpostorGenerate_FromOriginalTreeMeshes_CreatesReadableCoarseMesh()
    {
        BranchPrototypeSO prototype = CreatePrototypeForShellBake(CreateSeparatedClusterMesh("ImpostorBoundsFoliage"));

        TreeBlueprintSO blueprint = CreateBlueprintForImpostorBake(prototype);
        ImpostorMeshGenerator.BakeImpostorMesh(blueprint, CreateImpostorBakeSettings(4));
        TrackGeneratedImpostor(blueprint);

        Assert.NotNull(blueprint.ImpostorMesh);
        Assert.IsTrue(blueprint.ImpostorMesh!.isReadable);
        Assert.Greater(GetTriangleCount(blueprint.ImpostorMesh), 0);

        Bounds impostorBounds = blueprint.ImpostorMesh!.bounds;
        Assert.Less(impostorBounds.min.x, -1.5f);
        Assert.Greater(impostorBounds.max.x, 1.5f);
    }

    private BranchPrototypeSO CreatePrototypeForShellBake(Mesh foliageMesh)
    {
        BranchPrototypeSO prototype = CreateScriptableObject<BranchPrototypeSO>();
        Mesh woodMesh = CreateSegmentedBranchMesh("ShellBakeWood", new Vector3(0.2f, 0.8f, 0.2f), 6);
        Material shellMaterial = CreateOpaqueMaterial("ShellMaterial");

        SetPrivateField(prototype, "woodMesh", woodMesh);
        SetPrivateField(prototype, "foliageMesh", foliageMesh);
        SetPrivateField(prototype, "shellMaterial", shellMaterial);
        SetPrivateField(prototype, "triangleBudgetShellL0", 384);
        SetPrivateField(prototype, "triangleBudgetShellL1", 192);
        SetPrivateField(prototype, "triangleBudgetShellL2", 96);
        return prototype;
    }

    private TreeBlueprintSO CreateBlueprintForImpostorBake(BranchPrototypeSO prototype)
    {
        TreeBlueprintSO blueprint = CreateScriptableObject<TreeBlueprintSO>();
        Mesh trunkMesh = CreateClosedCubeMesh("ImpostorTrunk", new Vector3(0.4f, 2f, 0.4f));
        BranchPlacement firstPlacement = CreateBranchPlacement(prototype, new Vector3(-2f, 0.5f, 0f), Quaternion.identity, 1f);
        BranchPlacement secondPlacement = CreateBranchPlacement(prototype, new Vector3(2f, 0.5f, 0f), Quaternion.identity, 1f);

        SetPrivateField(blueprint, "trunkMesh", trunkMesh);
        SetPrivateField(blueprint, "branches", new[] { firstPlacement, secondPlacement });
        return blueprint;
    }

    private ShellBakeSettings CreateShellBakeSettings()
    {
        ShellBakeSettings settings = new ShellBakeSettings();
        SetPrivateField(settings, "maxOctreeDepth", 4);
        SetPrivateField(settings, "voxelResolutionL0", 80);
        SetPrivateField(settings, "voxelResolutionL1", 16);
        SetPrivateField(settings, "voxelResolutionL2", 6);
        SetPrivateField(settings, "woodVoxelResolutionL1", 12);
        SetPrivateField(settings, "woodVoxelResolutionL2", 6);
        SetPrivateField(settings, "minimumSurfaceVoxelCountToSplit", 1);
        SetPrivateField(settings, "skipReduction", true);
        SetPrivateField(settings, "skipL0Reduction", true);
        SetPrivateField(settings, "skipSimplifyFallback", true);
        return settings;
    }

    private ImpostorBakeSettings CreateImpostorBakeSettings(int voxelResolution)
    {
        ImpostorBakeSettings settings = new ImpostorBakeSettings();
        SetPrivateField(settings, "voxelResolution", voxelResolution);
        SetPrivateField(settings, "skipReduction", true);
        SetPrivateField(settings, "skipSimplifyFallback", true);
        return settings;
    }

    private void TrackHierarchy(MeshVoxelizerHierarchyNode[] hierarchyNodes)
    {
        for (int i = 0; i < hierarchyNodes.Length; i++)
        {
            TrackObject(hierarchyNodes[i].ShellL0Mesh);
            TrackObject(hierarchyNodes[i].ShellL1Mesh);
            TrackObject(hierarchyNodes[i].ShellL2Mesh);
        }
    }

    private void TrackGeneratedShells(BranchPrototypeSO prototype)
    {
        for (int i = 0; i < prototype.ShellNodes.Length; i++)
        {
            TrackObject(prototype.ShellNodes[i].ShellL0Mesh);
            TrackObject(prototype.ShellNodes[i].ShellL1Mesh);
            TrackObject(prototype.ShellNodes[i].ShellL2Mesh);
        }

        TrackObject(prototype.ShellL1WoodMesh);
        TrackObject(prototype.ShellL2WoodMesh);
    }

    private void TrackGeneratedImpostor(TreeBlueprintSO blueprint)
    {
        TrackObject(blueprint.ImpostorMesh);
    }

    private void TrackObject(UnityEngine.Object? obj)
    {
        if (obj != null && !createdObjects.Contains(obj))
        {
            createdObjects.Add(obj);
        }
    }

    private T CreateScriptableObject<T>() where T : ScriptableObject
    {
        T instance = ScriptableObject.CreateInstance<T>();
        createdObjects.Add(instance);
        return instance;
    }

    private Mesh CreateClosedCubeMesh(string name, Vector3 size)
    {
        Vector3 halfSize = size * 0.5f;
        Vector3[] vertices =
        {
            new Vector3(-halfSize.x, -halfSize.y, -halfSize.z),
            new Vector3(halfSize.x, -halfSize.y, -halfSize.z),
            new Vector3(halfSize.x, halfSize.y, -halfSize.z),
            new Vector3(-halfSize.x, halfSize.y, -halfSize.z),
            new Vector3(-halfSize.x, -halfSize.y, halfSize.z),
            new Vector3(halfSize.x, -halfSize.y, halfSize.z),
            new Vector3(halfSize.x, halfSize.y, halfSize.z),
            new Vector3(-halfSize.x, halfSize.y, halfSize.z)
        };

        int[] triangles =
        {
            0, 2, 1, 0, 3, 2,
            4, 5, 6, 4, 6, 7,
            0, 7, 3, 0, 4, 7,
            1, 2, 6, 1, 6, 5,
            3, 7, 6, 3, 6, 2,
            0, 1, 5, 0, 5, 4
        };

        return CreateMesh(name, vertices, triangles);
    }

    private Mesh CreateSeparatedClusterMesh(string name)
    {
        CombineInstance[] combineInstances =
        {
            CreateCombineInstance(CreateClosedCubeMesh($"{name}_Left", new Vector3(0.9f, 0.9f, 0.9f)), Matrix4x4.TRS(new Vector3(-1.4f, 0f, 0f), Quaternion.identity, Vector3.one)),
            CreateCombineInstance(CreateClosedCubeMesh($"{name}_Right", new Vector3(0.9f, 0.9f, 0.9f)), Matrix4x4.TRS(new Vector3(1.4f, 0f, 0f), Quaternion.identity, Vector3.one))
        };

        return CombineMeshes(name, combineInstances);
    }

    private Mesh CreateSegmentedBranchMesh(string name, Vector3 size, int segments)
    {
        Vector3 halfSize = size * 0.5f;
        List<Vector3> vertices = new List<Vector3>();
        List<int> triangles = new List<int>();

        for (int segmentIndex = 0; segmentIndex <= segments; segmentIndex++)
        {
            float t = segments == 0 ? 0f : (float)segmentIndex / segments;
            float y = Mathf.Lerp(-halfSize.y, halfSize.y, t);
            float taper = Mathf.Lerp(1f, 0.6f, t);
            float x = halfSize.x * taper;
            float z = halfSize.z * taper;

            vertices.Add(new Vector3(-x, y, -z));
            vertices.Add(new Vector3(x, y, -z));
            vertices.Add(new Vector3(x, y, z));
            vertices.Add(new Vector3(-x, y, z));
        }

        for (int segmentIndex = 0; segmentIndex < segments; segmentIndex++)
        {
            int ringStart = segmentIndex * 4;
            int nextRingStart = (segmentIndex + 1) * 4;

            AddQuadTriangles(triangles, ringStart + 0, ringStart + 1, nextRingStart + 1, nextRingStart + 0);
            AddQuadTriangles(triangles, ringStart + 1, ringStart + 2, nextRingStart + 2, nextRingStart + 1);
            AddQuadTriangles(triangles, ringStart + 2, ringStart + 3, nextRingStart + 3, nextRingStart + 2);
            AddQuadTriangles(triangles, ringStart + 3, ringStart + 0, nextRingStart + 0, nextRingStart + 3);
        }

        int bottomCenter = vertices.Count;
        vertices.Add(new Vector3(0f, -halfSize.y, 0f));
        int topCenter = vertices.Count;
        vertices.Add(new Vector3(0f, halfSize.y, 0f));
        int topRingStart = segments * 4;

        triangles.Add(bottomCenter);
        triangles.Add(1);
        triangles.Add(0);
        triangles.Add(bottomCenter);
        triangles.Add(2);
        triangles.Add(1);
        triangles.Add(bottomCenter);
        triangles.Add(3);
        triangles.Add(2);
        triangles.Add(bottomCenter);
        triangles.Add(0);
        triangles.Add(3);

        triangles.Add(topCenter);
        triangles.Add(topRingStart + 0);
        triangles.Add(topRingStart + 1);
        triangles.Add(topCenter);
        triangles.Add(topRingStart + 1);
        triangles.Add(topRingStart + 2);
        triangles.Add(topCenter);
        triangles.Add(topRingStart + 2);
        triangles.Add(topRingStart + 3);
        triangles.Add(topCenter);
        triangles.Add(topRingStart + 3);
        triangles.Add(topRingStart + 0);

        return CreateMesh(name, vertices.ToArray(), triangles.ToArray());
    }

    private Mesh CombineMeshes(string name, CombineInstance[] combineInstances)
    {
        Mesh combinedMesh = new Mesh
        {
            name = name
        };

        combinedMesh.CombineMeshes(combineInstances, true, true, false);
        combinedMesh.RecalculateBounds();
        combinedMesh.RecalculateNormals();
        createdObjects.Add(combinedMesh);
        return combinedMesh;
    }

    private CombineInstance CreateCombineInstance(Mesh mesh, Matrix4x4 matrix)
    {
        return new CombineInstance
        {
            mesh = mesh,
            transform = matrix
        };
    }

    private Mesh CreateMesh(string name, Vector3[] vertices, int[] triangles)
    {
        Mesh mesh = new Mesh
        {
            name = name
        };

        mesh.vertices = vertices;
        mesh.triangles = triangles;
        if (triangles.Length > 0)
        {
            mesh.RecalculateNormals();
        }

        mesh.RecalculateBounds();
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
            name = name
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

    private static BranchPlacement CreateBranchPlacement(BranchPrototypeSO prototype, Vector3 position, Quaternion rotation, float scale)
    {
        BranchPlacement placement = new BranchPlacement();
        SetPrivateField(placement, "prototype", prototype);
        SetPrivateField(placement, "localPosition", position);
        SetPrivateField(placement, "localRotation", rotation);
        SetPrivateField(placement, "scale", scale);
        return placement;
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

    private static bool ContainsBounds(Bounds container, Bounds candidate)
    {
        const float epsilon = 0.0001f;
        Vector3 containerMin = container.min - Vector3.one * epsilon;
        Vector3 containerMax = container.max + Vector3.one * epsilon;
        Vector3 candidateMin = candidate.min;
        Vector3 candidateMax = candidate.max;

        return candidateMin.x >= containerMin.x &&
               candidateMin.y >= containerMin.y &&
               candidateMin.z >= containerMin.z &&
               candidateMax.x <= containerMax.x &&
               candidateMax.y <= containerMax.y &&
               candidateMax.z <= containerMax.z;
    }

    private static int GetTriangleCount(Mesh mesh)
    {
        return mesh.triangles.Length / 3;
    }

    private static void AddQuadTriangles(List<int> triangles, int bottomLeft, int bottomRight, int topRight, int topLeft)
    {
        triangles.Add(bottomLeft);
        triangles.Add(bottomRight);
        triangles.Add(topRight);
        triangles.Add(bottomLeft);
        triangles.Add(topRight);
        triangles.Add(topLeft);
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
