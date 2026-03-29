#nullable enable

using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using VoxGeoFol.Features.Vegetation.Authoring;
using VoxGeoFol.Features.Vegetation.Editor;

[TestFixture]
public sealed class CanopyShellGenerationTests
{
    private const float BoundsEpsilon = 0.0001f;
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
    public void Voxelize_SimpleCube_AllVoxelsOccupied()
    {
        Mesh cubeMesh = CreateClosedCubeMesh("VoxelCube", Vector3.one);

        VoxelGrid voxelGrid = Voxelizer.VoxelizeSolid(cubeMesh, 4);

        Assert.AreEqual(64, voxelGrid.OccupiedCount);
    }

    [Test]
    public void Voxelize_EmptyMesh_NoVoxelsOccupied()
    {
        Mesh emptyMesh = CreateMesh("EmptyMesh", Array.Empty<Vector3>(), Array.Empty<int>());

        VoxelGrid voxelGrid = Voxelizer.VoxelizeSolid(emptyMesh, 4);

        Assert.AreEqual(0, voxelGrid.OccupiedCount);
    }

    [Test]
    public void Voxelize_FoliageMesh_ApproximateVolumeCorrect()
    {
        Mesh tetrahedronMesh = CreateTetrahedronMesh("VoxelTetrahedron");

        VoxelGrid voxelGrid = Voxelizer.VoxelizeSolid(tetrahedronMesh, 6);

        Assert.Greater(voxelGrid.OccupiedCount, 24);
        Assert.Less(voxelGrid.OccupiedCount, 160);
    }

    [Test]
    public void ShellExtract_L0Resolution_HigherTrisThanL1()
    {
        BranchPrototypeSO prototype = CreatePrototypeForShellBake();

        CanopyShellGenerator.BakeCanopyShells(prototype, CreateShellBakeSettings(10, 6, 4, 384, 192, 96));
        TrackGeneratedShells(prototype);

        Assert.Greater(GetTriangleCount(prototype.ShellL0Mesh!), GetTriangleCount(prototype.ShellL1Mesh!));
    }

    [Test]
    public void ShellExtract_L1Resolution_HigherTrisThanL2()
    {
        BranchPrototypeSO prototype = CreatePrototypeForShellBake();

        CanopyShellGenerator.BakeCanopyShells(prototype, CreateShellBakeSettings(10, 6, 4, 384, 192, 96));
        TrackGeneratedShells(prototype);

        Assert.Greater(GetTriangleCount(prototype.ShellL1Mesh!), GetTriangleCount(prototype.ShellL2Mesh!));
    }

    [Test]
    public void ShellExtract_OutputMesh_HasValidNormals()
    {
        BranchPrototypeSO prototype = CreatePrototypeForShellBake();

        CanopyShellGenerator.BakeCanopyShells(prototype, CreateShellBakeSettings(10, 6, 4, 384, 192, 96));
        TrackGeneratedShells(prototype);

        Assert.IsTrue(HasValidNormals(prototype.ShellL0Mesh!));
        Assert.IsTrue(HasOutwardFacingTriangles(prototype.ShellL0Mesh!));
    }

    [Test]
    public void ShellExtract_OutputMesh_NoDegenTriangles()
    {
        BranchPrototypeSO prototype = CreatePrototypeForShellBake();

        CanopyShellGenerator.BakeCanopyShells(prototype, CreateShellBakeSettings(10, 6, 4, 384, 192, 96));
        TrackGeneratedShells(prototype);

        Assert.IsTrue(HasNoDegenerateTriangles(prototype.ShellL0Mesh!));
        Assert.IsTrue(HasNoDegenerateTriangles(prototype.ShellL1Mesh!));
        Assert.IsTrue(HasNoDegenerateTriangles(prototype.ShellL2Mesh!));
    }

    [Test]
    public void ShellExtract_OutputMesh_WithinBudget()
    {
        BranchPrototypeSO prototype = CreatePrototypeForShellBake();

        CanopyShellGenerator.BakeCanopyShells(prototype, CreateShellBakeSettings(10, 6, 4, 500, 500, 500));
        TrackGeneratedShells(prototype);

        Assert.LessOrEqual(GetTriangleCount(prototype.ShellL0Mesh!), prototype.TriangleBudgetShellL0);
        Assert.LessOrEqual(GetTriangleCount(prototype.ShellL1Mesh!), prototype.TriangleBudgetShellL1);
        Assert.LessOrEqual(GetTriangleCount(prototype.ShellL2Mesh!), prototype.TriangleBudgetShellL2);
    }

    [Test]
    public void ShellExtract_OutputWoodMeshes_StrictlyDecreaseFromSourceWood()
    {
        BranchPrototypeSO prototype = CreatePrototypeForShellBake();

        CanopyShellGenerator.BakeCanopyShells(prototype, CreateShellBakeSettings(10, 6, 4, 384, 192, 96));
        TrackGeneratedShells(prototype);

        Assert.IsNotNull(prototype.ShellL1WoodMesh);
        Assert.IsNotNull(prototype.ShellL2WoodMesh);
        Assert.Greater(GetTriangleCount(prototype.WoodMesh!), GetTriangleCount(prototype.ShellL1WoodMesh!));
        Assert.Greater(GetTriangleCount(prototype.ShellL1WoodMesh!), GetTriangleCount(prototype.ShellL2WoodMesh!));
    }

    [Test]
    public void ShellExtract_OutputMesh_BoundsContainedInInput()
    {
        BranchPrototypeSO prototype = CreatePrototypeForShellBake();

        CanopyShellGenerator.BakeCanopyShells(prototype, CreateShellBakeSettings(10, 6, 4, 384, 192, 96));
        TrackGeneratedShells(prototype);

        Bounds foliageBounds = prototype.FoliageMesh!.bounds;
        Assert.IsTrue(ContainsBounds(foliageBounds, prototype.ShellL0Mesh!.bounds));
        Assert.IsTrue(ContainsBounds(foliageBounds, prototype.ShellL1Mesh!.bounds));
        Assert.IsTrue(ContainsBounds(foliageBounds, prototype.ShellL2Mesh!.bounds));
    }

    [Test]
    public void ImpostorGenerate_FromTreeAssemblyShellL2_UnderTriangleBudget()
    {
        TreeBlueprintSO blueprint = CreateBlueprintForImpostorBake();

        ImpostorMeshGenerator.BakeImpostorMesh(blueprint, CreateImpostorBakeSettings(64, 0.05f));
        TrackGeneratedImpostor(blueprint);

        Assert.LessOrEqual(GetTriangleCount(blueprint.ImpostorMesh!), 64);
    }

    [Test]
    public void ImpostorGenerate_MergesBranchShellsInTreeSpace()
    {
        TreeBlueprintSO blueprint = CreateBlueprintForImpostorBake();

        ImpostorMeshGenerator.BakeImpostorMesh(blueprint, CreateImpostorBakeSettings(96, 0.05f));
        TrackGeneratedImpostor(blueprint);

        Bounds impostorBounds = blueprint.ImpostorMesh!.bounds;
        Assert.Less(impostorBounds.min.x, -1.5f);
        Assert.Greater(impostorBounds.max.x, 1.5f);
    }

    [Test]
    public void ImpostorGenerate_OutputMesh_HasOutwardNormals()
    {
        TreeBlueprintSO blueprint = CreateBlueprintForImpostorBake();

        ImpostorMeshGenerator.BakeImpostorMesh(blueprint, CreateImpostorBakeSettings(96, 0.05f));
        TrackGeneratedImpostor(blueprint);

        Assert.IsTrue(HasValidNormals(blueprint.ImpostorMesh!));
        Assert.Greater(ComputeSignedVolume(blueprint.ImpostorMesh!), 0f);
    }

    [Test]
    public void ImpostorGenerate_OutputMesh_NoInternalCavities()
    {
        TreeBlueprintSO blueprint = CreateBlueprintForImpostorBake(singleBranchAtOrigin: true);

        ImpostorMeshGenerator.BakeImpostorMesh(blueprint, CreateImpostorBakeSettings(96, 0.05f));
        TrackGeneratedImpostor(blueprint);

        VoxelGrid voxelGrid = Voxelizer.VoxelizeSolid(blueprint.ImpostorMesh!, 6);
        int centerIndex = voxelGrid.Resolution / 2;
        Assert.IsTrue(voxelGrid.IsOccupied(centerIndex, centerIndex, centerIndex));
    }

    private BranchPrototypeSO CreatePrototypeForShellBake()
    {
        BranchPrototypeSO prototype = CreateScriptableObject<BranchPrototypeSO>();
        Mesh woodMesh = CreateSegmentedBranchMesh("ShellBakeWood", new Vector3(0.2f, 0.8f, 0.2f), 6);
        Mesh foliageMesh = CreateClosedCubeMesh("ShellBakeFoliage", new Vector3(1.6f, 1.2f, 1.4f));
        Material shellMaterial = CreateOpaqueMaterial("ShellMaterial");

        SetPrivateField(prototype, "woodMesh", woodMesh);
        SetPrivateField(prototype, "foliageMesh", foliageMesh);
        SetPrivateField(prototype, "shellMaterial", shellMaterial);
        SetPrivateField(prototype, "triangleBudgetShellL0", 384);
        SetPrivateField(prototype, "triangleBudgetShellL1", 192);
        SetPrivateField(prototype, "triangleBudgetShellL2", 96);
        return prototype;
    }

    private TreeBlueprintSO CreateBlueprintForImpostorBake(bool singleBranchAtOrigin = false)
    {
        TreeBlueprintSO blueprint = CreateScriptableObject<TreeBlueprintSO>();
        BranchPrototypeSO prototype = CreateScriptableObject<BranchPrototypeSO>();
        Mesh trunkMesh = CreateClosedCubeMesh("ImpostorTrunk", new Vector3(0.4f, 2f, 0.4f));
        Mesh shellMesh = CreateClosedCubeMesh("ImpostorShell", Vector3.one);
        Mesh shellWoodMesh = CreateClosedCubeMesh("ImpostorShellWood", new Vector3(0.2f, 0.8f, 0.2f));

        SetPrivateField(prototype, "shellL2Mesh", shellMesh);
        SetPrivateField(prototype, "shellL2WoodMesh", shellWoodMesh);

        BranchPlacement firstPlacement = CreateBranchPlacement(
            prototype,
            singleBranchAtOrigin ? Vector3.zero : new Vector3(-2f, 0.5f, 0f),
            Quaternion.identity,
            1f);
        BranchPlacement secondPlacement = CreateBranchPlacement(
            prototype,
            singleBranchAtOrigin ? Vector3.zero : new Vector3(2f, 0.5f, 0f),
            Quaternion.identity,
            1f);

        SetPrivateField(blueprint, "trunkMesh", trunkMesh);
        SetPrivateField(blueprint, "branches", singleBranchAtOrigin ? new[] { firstPlacement } : new[] { firstPlacement, secondPlacement });
        return blueprint;
    }

    private ShellBakeSettings CreateShellBakeSettings(int l0Resolution, int l1Resolution, int l2Resolution, int l0Target, int l1Target, int l2Target)
    {
        ShellBakeSettings settings = new ShellBakeSettings();
        SetPrivateField(settings, "voxelResolutionL0", l0Resolution);
        SetPrivateField(settings, "voxelResolutionL1", l1Resolution);
        SetPrivateField(settings, "voxelResolutionL2", l2Resolution);
        SetPrivateField(settings, "targetTrianglesL0", l0Target);
        SetPrivateField(settings, "targetTrianglesL1", l1Target);
        SetPrivateField(settings, "targetTrianglesL2", l2Target);
        return settings;
    }

    private ImpostorBakeSettings CreateImpostorBakeSettings(int targetTriangles, float weldThreshold)
    {
        ImpostorBakeSettings settings = new ImpostorBakeSettings();
        SetPrivateField(settings, "targetTriangles", targetTriangles);
        SetPrivateField(settings, "weldThreshold", weldThreshold);
        return settings;
    }

    private void TrackGeneratedShells(BranchPrototypeSO prototype)
    {
        TrackObject(prototype.ShellL0Mesh);
        TrackObject(prototype.ShellL1Mesh);
        TrackObject(prototype.ShellL1WoodMesh);
        TrackObject(prototype.ShellL2Mesh);
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

    private Mesh CreateTetrahedronMesh(string name)
    {
        Vector3[] vertices =
        {
            new Vector3(-0.5f, -0.5f, -0.5f),
            new Vector3(0.5f, -0.5f, -0.5f),
            new Vector3(0f, 0.75f, -0.25f),
            new Vector3(0f, 0f, 0.75f)
        };

        int[] triangles =
        {
            0, 1, 2,
            0, 3, 1,
            1, 3, 2,
            0, 2, 3
        };

        return CreateMesh(name, vertices, triangles);
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

    private static bool HasValidNormals(Mesh mesh)
    {
        Vector3[] normals = mesh.normals;
        if (normals.Length != mesh.vertexCount)
        {
            return false;
        }

        for (int i = 0; i < normals.Length; i++)
        {
            Vector3 normal = normals[i];
            if (float.IsNaN(normal.x) || float.IsNaN(normal.y) || float.IsNaN(normal.z) ||
                float.IsInfinity(normal.x) || float.IsInfinity(normal.y) || float.IsInfinity(normal.z))
            {
                return false;
            }

            if (normal.sqrMagnitude < 0.8f)
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasOutwardFacingTriangles(Mesh mesh)
    {
        Vector3[] vertices = mesh.vertices;
        int[] triangles = mesh.triangles;
        Vector3 boundsCenter = mesh.bounds.center;

        for (int triangleIndex = 0; triangleIndex < triangles.Length; triangleIndex += 3)
        {
            Vector3 a = vertices[triangles[triangleIndex]];
            Vector3 b = vertices[triangles[triangleIndex + 1]];
            Vector3 c = vertices[triangles[triangleIndex + 2]];
            Vector3 triangleCenter = (a + b + c) / 3f;
            Vector3 outwardDirection = triangleCenter - boundsCenter;
            if (outwardDirection.sqrMagnitude <= 0.000001f)
            {
                continue;
            }

            Vector3 faceNormal = Vector3.Cross(b - a, c - a).normalized;
            if (Vector3.Dot(faceNormal, outwardDirection.normalized) <= 0f)
            {
                return false;
            }
        }

        return true;
    }

    private static float ComputeSignedVolume(Mesh mesh)
    {
        Vector3[] vertices = mesh.vertices;
        int[] triangles = mesh.triangles;
        float signedVolume = 0f;
        for (int triangleIndex = 0; triangleIndex < triangles.Length; triangleIndex += 3)
        {
            Vector3 a = vertices[triangles[triangleIndex]];
            Vector3 b = vertices[triangles[triangleIndex + 1]];
            Vector3 c = vertices[triangles[triangleIndex + 2]];
            signedVolume += Vector3.Dot(a, Vector3.Cross(b, c)) / 6f;
        }

        return signedVolume;
    }

    private static bool HasNoDegenerateTriangles(Mesh mesh)
    {
        Vector3[] vertices = mesh.vertices;
        int[] triangles = mesh.triangles;
        for (int triangleIndex = 0; triangleIndex < triangles.Length; triangleIndex += 3)
        {
            Vector3 a = vertices[triangles[triangleIndex]];
            Vector3 b = vertices[triangles[triangleIndex + 1]];
            Vector3 c = vertices[triangles[triangleIndex + 2]];
            if (Vector3.Cross(b - a, c - a).sqrMagnitude <= 0.000001f)
            {
                return false;
            }
        }

        return true;
    }

    private static bool ContainsBounds(Bounds container, Bounds candidate)
    {
        Vector3 containerMin = container.min - Vector3.one * BoundsEpsilon;
        Vector3 containerMax = container.max + Vector3.one * BoundsEpsilon;
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
