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
    private readonly List<IDisposable> createdDisposables = new List<IDisposable>();

    [TearDown]
    public void TearDown()
    {
        for (int i = createdDisposables.Count - 1; i >= 0; i--)
        {
            createdDisposables[i].Dispose();
        }

        createdDisposables.Clear();

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
    public void RuntimeRegistryBuilder_AssignsCellsAndKeepsBlueprintPlacementsShared()
    {
        VegetationTreeAuthoring firstAuthoring = CreateAuthoring("Tree_A", new Vector3(0f, 0f, 10f));
        VegetationTreeAuthoring secondAuthoring = CreateAuthoring("Tree_B", new Vector3(120f, 0f, 10f));
        Hash128 containerIdHash = Hash128.Compute("RuntimeRegistryBuilder_AssignsCells");
        VegetationTreeAuthoringRuntime firstRuntimeAuthoring = CreateRuntimeAuthoring(firstAuthoring, containerIdHash, 0);
        VegetationTreeAuthoringRuntime secondRuntimeAuthoring = CreateRuntimeAuthoring(secondAuthoring, containerIdHash, 1);

        VegetationRuntimeRegistry registry = new VegetationRuntimeRegistryBuilder(Vector3.zero, new Vector3(64f, 64f, 64f))
            .Build(new[] { firstRuntimeAuthoring, secondRuntimeAuthoring });

        Assert.AreEqual(2, registry.TreeInstances.Count);
        Assert.AreEqual("Tree_A", registry.TreeInstances[0].Authoring.DebugName);
        Assert.AreEqual("Tree_B", registry.TreeInstances[1].Authoring.DebugName);
        Assert.AreEqual(2, registry.SpatialGrid.Cells.Count);
        Assert.AreNotEqual(registry.TreeInstances[0].CellIndex, registry.TreeInstances[1].CellIndex);
        Assert.AreEqual(1, registry.TreeBlueprints.Count);
        Assert.AreEqual(1, registry.BlueprintBranchPlacements.Count);
        Assert.AreEqual(1, registry.BranchPrototypes.Count);
        Assert.AreEqual(0, registry.BlueprintBranchPlacements[0].PrototypeIndex);
        Matrix4x4 expectedLocalToTree = Matrix4x4.TRS(Vector3.zero + new Vector3(0f, 1f, 0f), Quaternion.identity, Vector3.one);
        AssertMatrixApproximatelyEqual(expectedLocalToTree, registry.BlueprintBranchPlacements[0].LocalToTree);
        AssertMatrixApproximatelyEqual(expectedLocalToTree.inverse, registry.BlueprintBranchPlacements[0].TreeToLocal);
    }

    [Test]
    public void RuntimeRegistryBuilder_ThrowsWhenRegisteredDrawSlotCapIsExceeded()
    {
        VegetationTreeAuthoring authoring = CreateAuthoring("CappedSlotsTree", new Vector3(0f, 0f, 10f));
        Hash128 containerIdHash = Hash128.Compute("RuntimeRegistryBuilder_ThrowsWhenRegisteredDrawSlotCapIsExceeded");
        VegetationTreeAuthoringRuntime runtimeAuthoring = CreateRuntimeAuthoring(authoring, containerIdHash, 0);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            new VegetationRuntimeRegistryBuilder(Vector3.zero, new Vector3(64f, 64f, 64f), 1)
                .Build(new[] { runtimeAuthoring }));

        StringAssert.Contains("registered draw-slot cap", exception!.Message);
    }

    [Test]
    public void IndirectRenderer_BindGpuResidentFrame_ExposesPreparedViewSnapshotsWithoutExactCounts()
    {
        VegetationTreeAuthoring authoring = CreateAuthoring("RuntimeTree", new Vector3(0f, 0f, 10f));
        VegetationTreeAuthoringRuntime runtimeAuthoring =
            CreateRuntimeAuthoring(authoring, Hash128.Compute("IndirectRenderer_BindGpuResidentFrame"), 0);
        VegetationRuntimeRegistry registry = new VegetationRuntimeRegistryBuilder(Vector3.zero, new Vector3(64f, 64f, 64f))
            .Build(new[] { runtimeAuthoring });

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
            uint[] emittedCounts = new uint[Mathf.Max(1, registry.DrawSlots.Count)];
            emittedCounts[0] = 3u;
            if (registry.DrawSlots.Count > 1)
            {
                emittedCounts[registry.DrawSlots.Count - 1] = 2u;
            }

            using (ComputeBuffer slotEmittedInstanceCountsBuffer = new ComputeBuffer(
                       Mathf.Max(1, registry.DrawSlots.Count),
                       sizeof(uint)))
            {
                slotEmittedInstanceCountsBuffer.SetData(emittedCounts);
                VegetationIndirectRenderer.PreparedViewHandle? preparedView = indirectRenderer.BindGpuResidentFrame(
                    instanceBuffer,
                    argsBuffer,
                    slotPackedStartsBuffer,
                    slotEmittedInstanceCountsBuffer);
                Assert.IsNotNull(preparedView);

                List<VegetationIndirectDrawBatchSnapshot> snapshots = new List<VegetationIndirectDrawBatchSnapshot>();
                indirectRenderer.GetDebugSnapshots(preparedView!, snapshots);

                int expectedActiveSlotCount = registry.DrawSlots.Count;
                Assert.AreEqual(expectedActiveSlotCount, preparedView!.ActiveSlotIndices.Count);
                Assert.AreEqual(expectedActiveSlotCount, snapshots.Count);
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
    }

    [Test]
    public void IndirectRenderer_BindGpuResidentFrame_ReturnsIndependentPreparedViewHandles()
    {
        VegetationTreeAuthoring authoring = CreateAuthoring("IndependentPreparedViewTree", new Vector3(0f, 0f, 10f));
        VegetationTreeAuthoringRuntime runtimeAuthoring =
            CreateRuntimeAuthoring(authoring, Hash128.Compute("IndirectRenderer_IndependentPreparedViews"), 0);
        VegetationRuntimeRegistry registry = new VegetationRuntimeRegistryBuilder(Vector3.zero, new Vector3(64f, 64f, 64f))
            .Build(new[] { runtimeAuthoring });

        int indirectArgsUintCount = registry.DrawSlots.Count * (GraphicsBuffer.IndirectDrawIndexedArgs.size / sizeof(uint));

        using (VegetationIndirectRenderer indirectRenderer = new VegetationIndirectRenderer(registry, 7))
        using (GraphicsBuffer firstInstanceBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 16, 16))
        using (GraphicsBuffer secondInstanceBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 16, 16))
        using (GraphicsBuffer firstArgsBuffer = new GraphicsBuffer(
                   GraphicsBuffer.Target.Raw | GraphicsBuffer.Target.IndirectArguments,
                   Mathf.Max(1, indirectArgsUintCount),
                   sizeof(uint)))
        using (GraphicsBuffer secondArgsBuffer = new GraphicsBuffer(
                   GraphicsBuffer.Target.Raw | GraphicsBuffer.Target.IndirectArguments,
                   Mathf.Max(1, indirectArgsUintCount),
                   sizeof(uint)))
        using (ComputeBuffer firstSlotPackedStartsBuffer = new ComputeBuffer(Mathf.Max(1, registry.DrawSlots.Count), sizeof(uint)))
        using (ComputeBuffer secondSlotPackedStartsBuffer = new ComputeBuffer(Mathf.Max(1, registry.DrawSlots.Count), sizeof(uint)))
        {
            VegetationIndirectRenderer.PreparedViewHandle? firstPreparedView = indirectRenderer.BindGpuResidentFrame(
                firstInstanceBuffer,
                firstArgsBuffer,
                firstSlotPackedStartsBuffer);
            VegetationIndirectRenderer.PreparedViewHandle? secondPreparedView = indirectRenderer.BindGpuResidentFrame(
                secondInstanceBuffer,
                secondArgsBuffer,
                secondSlotPackedStartsBuffer);

            Assert.IsNotNull(firstPreparedView);
            Assert.IsNotNull(secondPreparedView);
            Assert.AreNotSame(firstPreparedView, secondPreparedView);
            Assert.AreSame(firstInstanceBuffer, firstPreparedView!.InstanceBuffer);
            Assert.AreSame(firstArgsBuffer, firstPreparedView.ArgsBuffer);
            Assert.AreSame(firstSlotPackedStartsBuffer, firstPreparedView.SlotPackedStartsBuffer);
            Assert.AreSame(secondInstanceBuffer, secondPreparedView!.InstanceBuffer);
            Assert.AreSame(secondArgsBuffer, secondPreparedView.ArgsBuffer);
            Assert.AreSame(secondSlotPackedStartsBuffer, secondPreparedView.SlotPackedStartsBuffer);
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

    [Test]
    public void ActiveRuntimeRegistry_ClassicProviderReplacesSubSceneProviderForSameContainerId()
    {
        VegetationTreeAuthoring authoring = CreateAuthoring("SharedTree", new Vector3(0f, 0f, 10f));
        Hash128 containerIdHash = Hash128.Compute("SharedRuntimeRegistryContainer");
        VegetationTreeAuthoringRuntime runtimeAuthoring = CreateRuntimeAuthoring(authoring, containerIdHash, 0);
        AuthoringContainerRuntime subSceneRuntime = CreateRuntimeOwner(
            containerIdHash.ToString(),
            VegetationRuntimeProviderKind.SubScene,
            "SharedSubScene",
            runtimeAuthoring);
        AuthoringContainerRuntime classicRuntime = CreateRuntimeOwner(
            containerIdHash.ToString(),
            VegetationRuntimeProviderKind.ClassicScene,
            "SharedClassic",
            runtimeAuthoring);

        Assert.IsTrue(subSceneRuntime.Activate());

        List<AuthoringContainerRuntime> activeRuntimes = new List<AuthoringContainerRuntime>();
        VegetationActiveAuthoringContainerRuntimes.GetActive(activeRuntimes);
        Assert.AreEqual(1, activeRuntimes.Count);
        Assert.AreSame(subSceneRuntime, activeRuntimes[0]);

        Assert.IsTrue(classicRuntime.Activate());
        VegetationActiveAuthoringContainerRuntimes.GetActive(activeRuntimes);
        Assert.AreEqual(1, activeRuntimes.Count);
        Assert.AreSame(classicRuntime, activeRuntimes[0]);
    }

    [Test]
    public void AuthoringContainerRuntime_PrepareFrameForCamera_MissingClassifyShader_ReturnsFalseWithoutFault()
    {
        VegetationTreeAuthoring authoring = CreateAuthoring("NoShaderTree", new Vector3(0f, 0f, 10f));
        Hash128 containerIdHash = Hash128.Compute("PrepareFrame_NoClassifyShader");
        VegetationTreeAuthoringRuntime runtimeAuthoring = CreateRuntimeAuthoring(authoring, containerIdHash, 0);
        AuthoringContainerRuntime runtimeOwner = CreateRuntimeOwner(
            containerIdHash.ToString(),
            VegetationRuntimeProviderKind.ClassicScene,
            "NoShaderRuntime",
            runtimeAuthoring);
        GameObject cameraObject = new GameObject("RuntimeCamera");
        createdObjects.Add(cameraObject);
        Camera camera = cameraObject.AddComponent<Camera>();

        Assert.IsTrue(runtimeOwner.Activate());

        bool prepared = runtimeOwner.PrepareFrameForCamera(camera, null, false);

        Assert.IsFalse(prepared);
        Assert.IsFalse(runtimeOwner.IsRenderRuntimeFaulted);
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
        Mesh branchL1CanopyMesh = CreateCubeMesh($"{name}_CanopyL1", new Vector3(1.4f, 1.1f, 1.4f));
        Mesh branchL2CanopyMesh = CreateCubeMesh($"{name}_CanopyL2", new Vector3(1.0f, 0.8f, 1.0f));
        Mesh branchL3CanopyMesh = CreateCubeMesh($"{name}_CanopyL3", new Vector3(0.75f, 0.6f, 0.75f));
        Mesh branchL1WoodMesh = CreateCubeMesh($"{name}_WoodL1", new Vector3(0.28f, 0.65f, 0.28f));
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
        SetPrivateField(prototype, "branchL1CanopyMesh", branchL1CanopyMesh);
        SetPrivateField(prototype, "branchL2CanopyMesh", branchL2CanopyMesh);
        SetPrivateField(prototype, "branchL3CanopyMesh", branchL3CanopyMesh);
        SetPrivateField(prototype, "branchL1WoodMesh", branchL1WoodMesh);
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
        Mesh treeL3Mesh = CreateCubeMesh($"{name}_TreeL3", new Vector3(2.0f, 2.5f, 2.0f));
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
        SetPrivateField(blueprint, "treeL3Mesh", treeL3Mesh);
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

    private VegetationTreeAuthoringRuntime CreateRuntimeAuthoring(
        VegetationTreeAuthoring authoring,
        Hash128 containerIdHash,
        int sourceOrder)
    {
        return new VegetationTreeAuthoringRuntime(
            VegetationRuntimeIdentityUtility.BuildTreeIdHash(containerIdHash, sourceOrder),
            authoring.name,
            authoring.Blueprint ?? throw new InvalidOperationException($"{authoring.name} is missing blueprint."),
            authoring.transform.localToWorldMatrix,
            true,
            authoring);
    }

    private AuthoringContainerRuntime CreateRuntimeOwner(
        string containerId,
        VegetationRuntimeProviderKind providerKind,
        string debugName,
        params VegetationTreeAuthoringRuntime[] runtimeTrees)
    {
        AuthoringContainerRuntime runtimeOwner = new AuthoringContainerRuntime(
            containerId,
            providerKind,
            debugName,
            null,
            0,
            Vector3.zero,
            new Vector3(64f, 64f, 64f),
            new VegetationRuntimeBudget(
                new VegetationViewRuntimeBudget(32, 32, 32),
                new VegetationViewRuntimeBudget(32, 32, 32),
                128),
            runtimeTrees);
        createdDisposables.Add(runtimeOwner);
        return runtimeOwner;
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

    private static void AssertMatrixApproximatelyEqual(Matrix4x4 expected, Matrix4x4 actual)
    {
        for (int row = 0; row < 4; row++)
        {
            for (int column = 0; column < 4; column++)
            {
                Assert.That(actual[row, column], Is.EqualTo(expected[row, column]).Within(0.0001f));
            }
        }
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
