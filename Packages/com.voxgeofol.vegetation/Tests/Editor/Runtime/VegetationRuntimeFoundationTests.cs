#nullable enable

using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using VoxGeoFol.Features.Vegetation.Authoring;
using VoxGeoFol.Features.Vegetation.Rendering;

[TestFixture]
public sealed class VegetationRuntimeFoundationTests
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
    public void RuntimeRegistryBuilder_AssignsCellsAndKeepsPrototypeShellDataShared()
    {
        VegetationTreeAuthoring firstAuthoring = CreateAuthoring("Tree_A", new Vector3(0f, 0f, 10f));
        VegetationTreeAuthoring secondAuthoring = CreateAuthoring("Tree_B", new Vector3(120f, 0f, 10f));

        VegetationRuntimeRegistry registry = new VegetationRuntimeRegistryBuilder(Vector3.zero, new Vector3(64f, 64f, 64f))
            .Build(new[] { secondAuthoring, firstAuthoring });

        Assert.AreEqual(2, registry.TreeInstances.Count);
        Assert.AreEqual("Tree_A", registry.TreeInstances[0].Authoring.name);
        Assert.AreEqual("Tree_B", registry.TreeInstances[1].Authoring.name);
        Assert.AreEqual(2, registry.SpatialGrid.Cells.Count);
        Assert.AreNotEqual(registry.TreeInstances[0].CellIndex, registry.TreeInstances[1].CellIndex);
        Assert.AreEqual(2, registry.SceneBranches.Count);
        Assert.AreEqual(2, registry.ShellNodesL1.Count);
        Assert.AreEqual(2, registry.ShellNodesL2.Count);
        Assert.AreEqual(2, registry.ShellNodesL3.Count);
        Assert.AreEqual(registry.SceneBranches[0].PrototypeIndex, registry.SceneBranches[1].PrototypeIndex);
    }

    [Test]
    public void IndirectRenderer_BindGpuResidentFrame_ExposesConservativeSnapshots()
    {
        VegetationTreeAuthoring authoring = CreateAuthoring("RuntimeTree", new Vector3(0f, 0f, 10f));
        VegetationRuntimeRegistry registry = new VegetationRuntimeRegistryBuilder(Vector3.zero, new Vector3(64f, 64f, 64f))
            .Build(new[] { authoring });

        int indirectArgsUintCount = registry.DrawSlots.Count * (GraphicsBuffer.IndirectDrawIndexedArgs.size / sizeof(uint));

        using (VegetationIndirectRenderer indirectRenderer = new VegetationIndirectRenderer(registry, 7))
        using (GraphicsBuffer instanceBuffer = new GraphicsBuffer(
                   GraphicsBuffer.Target.Structured,
                   64,
                   16))
        using (GraphicsBuffer argsBuffer = new GraphicsBuffer(
                   GraphicsBuffer.Target.Raw | GraphicsBuffer.Target.IndirectArguments,
                   Mathf.Max(1, indirectArgsUintCount),
                   sizeof(uint)))
        using (ComputeBuffer slotPackedStartsBuffer = new ComputeBuffer(
                   Mathf.Max(1, registry.DrawSlots.Count),
                   sizeof(uint)))
        {
            indirectRenderer.BindGpuResidentFrame(instanceBuffer, argsBuffer, slotPackedStartsBuffer);

            List<VegetationIndirectDrawBatchSnapshot> snapshots = new List<VegetationIndirectDrawBatchSnapshot>();
            indirectRenderer.GetDebugSnapshots(snapshots);

            Assert.AreEqual(registry.DrawSlots.Count, snapshots.Count);
            for (int i = 0; i < snapshots.Count; i++)
            {
                VegetationIndirectDrawBatchSnapshot snapshot = snapshots[i];
                Assert.IsFalse(snapshot.HasExactInstanceCount);
                Assert.AreEqual(0, snapshot.InstanceCount);
                Bounds expectedBounds = registry.DrawSlotConservativeWorldBounds[snapshot.SlotIndex];
                AssertBoundsEqual(snapshot.WorldBounds, expectedBounds);
            }
        }
    }

    [Test]
    public void RuntimeMathUtility_TransformBounds_MatchesCornerSweepReference()
    {
        Bounds localBounds = new Bounds(new Vector3(0.4f, 1.2f, -0.6f), new Vector3(2.5f, 3.75f, 1.5f));
        Matrix4x4 transformMatrix = Matrix4x4.TRS(
            new Vector3(-4.5f, 2.25f, 7.5f),
            Quaternion.Euler(17f, 33f, 12f),
            new Vector3(1.5f, 0.75f, 2.25f));

        Bounds actualBounds = VegetationRuntimeMathUtility.TransformBounds(localBounds, transformMatrix);
        Bounds expectedBounds = TransformBoundsByCornerSweep(localBounds, transformMatrix);

        AssertBoundsEqual(actualBounds, expectedBounds);
    }

    private VegetationTreeAuthoring CreateAuthoring(string name, Vector3 worldPosition)
    {
        BranchPrototypeSO prototype = CreateScriptableObject<BranchPrototypeSO>();
        prototype.name = $"{name}_Prototype";
        Mesh woodMesh = CreateCubeMesh($"{name}_Wood", new Vector3(0.3f, 0.8f, 0.3f));
        Mesh foliageMesh = CreateCubeMesh($"{name}_Foliage", new Vector3(1.6f, 1.2f, 1.6f));
        Mesh shellL0Root = CreateCubeMesh($"{name}_ShellL0Root", new Vector3(1.2f, 1.0f, 1.2f));
        Mesh shellL0Leaf = CreateCubeMesh($"{name}_ShellL0Leaf", new Vector3(0.6f, 0.6f, 0.6f));
        Mesh shellL1Root = CreateCubeMesh($"{name}_ShellL1Root", new Vector3(0.9f, 0.8f, 0.9f));
        Mesh shellL1Leaf = CreateCubeMesh($"{name}_ShellL1Leaf", new Vector3(0.45f, 0.45f, 0.45f));
        Mesh shellL2Root = CreateCubeMesh($"{name}_ShellL2Root", new Vector3(0.7f, 0.6f, 0.7f));
        Mesh shellL2Leaf = CreateCubeMesh($"{name}_ShellL2Leaf", new Vector3(0.35f, 0.35f, 0.35f));
        Mesh shellL1WoodMesh = CreateCubeMesh($"{name}_WoodL2", new Vector3(0.25f, 0.5f, 0.25f));
        Mesh shellL2WoodMesh = CreateCubeMesh($"{name}_WoodL3", new Vector3(0.2f, 0.4f, 0.2f));
        Material woodMaterial = CreateOpaqueMaterial($"{name}_WoodMat");
        Material foliageMaterial = CreateOpaqueMaterial($"{name}_FoliageMat");
        Material shellMaterial = CreateOpaqueMaterial($"{name}_ShellMat");

        SetPrivateField(prototype, "woodMesh", woodMesh);
        SetPrivateField(prototype, "woodMaterial", woodMaterial);
        SetPrivateField(prototype, "foliageMesh", foliageMesh);
        SetPrivateField(prototype, "foliageMaterial", foliageMaterial);
        SetPrivateField(prototype, "shellMaterial", shellMaterial);
        SetPrivateField(prototype, "shellL1WoodMesh", shellL1WoodMesh);
        SetPrivateField(prototype, "shellL2WoodMesh", shellL2WoodMesh);
        SetPrivateField(prototype, "leafColorTint", Color.green);
        SetPrivateField(prototype, "localBounds", new Bounds(new Vector3(0f, 0.6f, 0f), new Vector3(2f, 2f, 2f)));
        SetPrivateField(prototype, "shellNodesL0", CreateShellHierarchy(shellL0Root, shellL0Leaf, 0));
        SetPrivateField(prototype, "shellNodesL1", CreateShellHierarchy(shellL1Root, shellL1Leaf, 1));
        SetPrivateField(prototype, "shellNodesL2", CreateShellHierarchy(shellL2Root, shellL2Leaf, 2));

        TreeBlueprintSO blueprint = CreateScriptableObject<TreeBlueprintSO>();
        blueprint.name = $"{name}_Blueprint";
        Mesh trunkMesh = CreateCubeMesh($"{name}_Trunk", new Vector3(0.5f, 2.2f, 0.5f));
        Mesh trunkL3Mesh = CreateCubeMesh($"{name}_TrunkL3", new Vector3(0.3f, 1.8f, 0.3f));
        Mesh impostorMesh = CreateCubeMesh($"{name}_Impostor", new Vector3(2.4f, 2.8f, 2.4f));
        Material trunkMaterial = CreateOpaqueMaterial($"{name}_TrunkMat");
        Material impostorMaterial = CreateOpaqueMaterial($"{name}_ImpostorMat");
        LODProfileSO lodProfile = CreateScriptableObject<LODProfileSO>();

        SetPrivateField(lodProfile, "l0Distance", 5f);
        SetPrivateField(lodProfile, "l1Distance", 15f);
        SetPrivateField(lodProfile, "l2Distance", 30f);
        SetPrivateField(lodProfile, "impostorDistance", 80f);
        SetPrivateField(lodProfile, "absoluteCullDistance", 140f);

        BranchPlacement placement = new BranchPlacement();
        SetPrivateField(placement, "prototype", prototype);
        SetPrivateField(placement, "localPosition", new Vector3(0f, 1f, 0f));
        SetPrivateField(placement, "localRotation", Quaternion.identity);
        SetPrivateField(placement, "scale", 1f);

        SetPrivateField(blueprint, "trunkMesh", trunkMesh);
        SetPrivateField(blueprint, "trunkL3Mesh", trunkL3Mesh);
        SetPrivateField(blueprint, "trunkMaterial", trunkMaterial);
        SetPrivateField(blueprint, "impostorMesh", impostorMesh);
        SetPrivateField(blueprint, "impostorMaterial", impostorMaterial);
        SetPrivateField(blueprint, "lodProfile", lodProfile);
        SetPrivateField(blueprint, "branches", new[] { placement });
        SetPrivateField(blueprint, "treeBounds", new Bounds(new Vector3(0f, 1f, 0f), new Vector3(3f, 4f, 3f)));

        GameObject authoringObject = new GameObject(name);
        createdObjects.Add(authoringObject);
        authoringObject.transform.position = worldPosition;
        VegetationTreeAuthoring authoring = authoringObject.AddComponent<VegetationTreeAuthoring>();
        SetPrivateField(authoring, "blueprint", blueprint);
        return authoring;
    }

    private BranchShellNode[] CreateShellHierarchy(Mesh rootMesh, Mesh leafMesh, int shellLevel)
    {
        Bounds rootBounds = new Bounds(new Vector3(0f, 0.5f, 0f), new Vector3(1.4f, 1.4f, 1.4f));
        Bounds childBounds = new Bounds(new Vector3(0.35f, 0.85f, 0.35f), new Vector3(0.7f, 0.7f, 0.7f));
        return new[]
        {
            CreateShellNode(rootBounds, 0, 1, 1, rootMesh, shellLevel),
            CreateShellNode(childBounds, 1, -1, 0, leafMesh, shellLevel)
        };
    }

    private BranchShellNode CreateShellNode(Bounds bounds, int depth, int firstChildIndex, byte childMask, Mesh mesh, int shellLevel)
    {
        Mesh? shellL0Mesh = null;
        Mesh? shellL1Mesh = null;
        Mesh? shellL2Mesh = null;
        switch (shellLevel)
        {
            case 0:
                shellL0Mesh = mesh;
                break;
            case 1:
                shellL1Mesh = mesh;
                break;
            case 2:
                shellL2Mesh = mesh;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(shellLevel), shellLevel, "Shell level must be 0, 1, or 2.");
        }

        return new BranchShellNode(bounds, depth, firstChildIndex, childMask, shellL0Mesh, shellL1Mesh, shellL2Mesh);
    }

    private Mesh CreateCubeMesh(string name, Vector3 size)
    {
        Vector3 halfSize = size * 0.5f;
        Mesh mesh = new Mesh
        {
            name = name
        };
        mesh.vertices = new[]
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
        mesh.triangles = new[]
        {
            0, 2, 1, 0, 3, 2,
            4, 5, 6, 4, 6, 7,
            0, 7, 3, 0, 4, 7,
            1, 2, 6, 1, 6, 5,
            3, 7, 6, 3, 6, 2,
            0, 1, 5, 0, 5, 4
        };
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();
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

    private T CreateScriptableObject<T>() where T : ScriptableObject
    {
        T instance = ScriptableObject.CreateInstance<T>();
        createdObjects.Add(instance);
        return instance;
    }

    private static Bounds TransformBoundsByCornerSweep(Bounds bounds, Matrix4x4 matrix)
    {
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

    private static void AssertBoundsEqual(Bounds actualBounds, Bounds expectedBounds)
    {
        Assert.That(actualBounds.center.x, Is.EqualTo(expectedBounds.center.x).Within(0.0001f));
        Assert.That(actualBounds.center.y, Is.EqualTo(expectedBounds.center.y).Within(0.0001f));
        Assert.That(actualBounds.center.z, Is.EqualTo(expectedBounds.center.z).Within(0.0001f));
        Assert.That(actualBounds.size.x, Is.EqualTo(expectedBounds.size.x).Within(0.0001f));
        Assert.That(actualBounds.size.y, Is.EqualTo(expectedBounds.size.y).Within(0.0001f));
        Assert.That(actualBounds.size.z, Is.EqualTo(expectedBounds.size.z).Within(0.0001f));
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
