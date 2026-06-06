#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using VoxGeoFol.Features.Vegetation.Authoring;
using VoxGeoFol.Features.Vegetation.Editor;
using VoxGeoFol.Features.Vegetation.Rendering;

[TestFixture]
public sealed class FoliageCompiledAssetCompilerTests
{
    private readonly List<UnityEngine.Object> createdObjects = new List<UnityEngine.Object>();

    [TearDown]
    public void TearDown()
    {
        VegetationRenderWorld.Shared.Reset();
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
    public void CompileContainer_IsDeterministic_ForSameAuthoringSnapshot()
    {
        VegetationRuntimeContainer container = CreateContainer(
            "DeterministicContainer",
            new Vector3(0f, 0f, 0f),
            new Vector3(40f, 0f, 0f));

        FoliageCompilationResult first = CompileAndTrack(container, FoliageCompilerSettings.Default);
        FoliageCompilationResult second = CompileAndTrack(container, FoliageCompilerSettings.Default);

        Assert.IsTrue(first.Succeeded, string.Join("; ", first.FailureMessages));
        Assert.IsTrue(second.Succeeded, string.Join("; ", second.FailureMessages));
        Assert.AreEqual(first.BuildReport.PageCount, second.BuildReport.PageCount);
        Assert.AreEqual(first.BuildReport.TreeCount, second.BuildReport.TreeCount);
        Assert.AreEqual(first.BuildReport.PacketCount, second.BuildReport.PacketCount);
        Assert.AreEqual(first.BuildReport.AssetGroupCount, second.BuildReport.AssetGroupCount);

        FoliagePageAsset firstPage = first.PageAssets[0];
        FoliagePageAsset secondPage = second.PageAssets[0];
        Assert.AreEqual(firstPage.Packets.Count, secondPage.Packets.Count);
        for (int i = 0; i < firstPage.Packets.Count; i++)
        {
            Assert.AreEqual(firstPage.Packets[i].AssetGroupIndex, secondPage.Packets[i].AssetGroupIndex);
            Assert.AreEqual(firstPage.Packets[i].RepresentationKind, secondPage.Packets[i].RepresentationKind);
            Assert.AreEqual(firstPage.Packets[i].SourceTreeIndex, secondPage.Packets[i].SourceTreeIndex);
            Assert.AreEqual(firstPage.Packets[i].DebugLabel, secondPage.Packets[i].DebugLabel);
        }
    }

    [Test]
    public void CompileContainer_EnforcesCellTreeCap()
    {
        VegetationRuntimeContainer container = CreateContainer(
            "CellCapContainer",
            new Vector3(0f, 0f, 0f),
            new Vector3(2f, 0f, 0f));
        FoliageCompilerSettings settings = new FoliageCompilerSettings(
            new Vector3(64f, 64f, 64f),
            64,
            1,
            8192,
            4096);

        FoliageCompilationResult result = CompileAndTrack(container, settings);

        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains("maxTreesPerCell=1", string.Join("; ", result.FailureMessages));
    }

    [Test]
    public void CompileContainer_SplitsPagesByHardTreeCap()
    {
        VegetationRuntimeContainer container = CreateContainer(
            "PageCapContainer",
            new Vector3(0f, 0f, 0f),
            new Vector3(80f, 0f, 0f));
        FoliageCompilerSettings settings = new FoliageCompilerSettings(
            new Vector3(32f, 32f, 32f),
            1,
            8,
            8192,
            4096);

        FoliageCompilationResult result = CompileAndTrack(container, settings);

        Assert.IsTrue(result.Succeeded, string.Join("; ", result.FailureMessages));
        Assert.AreEqual(2, result.BuildReport.PageCount);
        Assert.AreEqual(2, result.BuildReport.TreeCount);
        Assert.AreEqual(1, result.PageAssets[0].Trees.Count);
        Assert.AreEqual(1, result.PageAssets[1].Trees.Count);
        Assert.AreEqual(1, result.BuildReport.PageSplitCount);
    }

    [Test]
    public void CompileContainer_EmitsContiguousPacketsAndNoLegacyAssetGroups()
    {
        VegetationRuntimeContainer container = CreateContainer(
            "ContiguousContainer",
            new Vector3(0f, 0f, 0f));

        FoliageCompilationResult result = CompileAndTrack(container, FoliageCompilerSettings.Default);

        Assert.IsTrue(result.Succeeded, string.Join("; ", result.FailureMessages));
        Assert.IsNotNull(result.AssemblyAsset);
        FoliagePageAsset page = result.PageAssets[0];
        int previousAssetGroupIndex = -1;
        int previousInstanceEnd = 0;
        for (int i = 0; i < page.Packets.Count; i++)
        {
            FoliageRepresentationPacket packet = page.Packets[i];
            Assert.GreaterOrEqual(packet.AssetGroupIndex, previousAssetGroupIndex);
            previousAssetGroupIndex = packet.AssetGroupIndex;
            Assert.GreaterOrEqual(packet.FirstInstance, previousInstanceEnd);
            Assert.Greater(packet.InstanceCount, 0);
            Assert.LessOrEqual(packet.FirstInstance + packet.InstanceCount, page.Instances.Count);
            for (int instanceOffset = 0; instanceOffset < packet.InstanceCount; instanceOffset++)
            {
                Assert.AreEqual(
                    packet.AssetGroupIndex,
                    page.Instances[packet.FirstInstance + instanceOffset].AssetGroupIndex);
            }

            previousInstanceEnd = packet.FirstInstance + packet.InstanceCount;
        }

        Assert.AreEqual(page.Instances.Count, previousInstanceEnd);
    }

    [Test]
    public void CompileContainer_EmitsAlwaysResidentHlodAndNearDetailPackets()
    {
        VegetationRuntimeContainer container = CreateContainer(
            "HlodContainer",
            new Vector3(0f, 0f, 0f),
            new Vector3(80f, 0f, 0f));

        FoliageCompilationResult result = CompileAndTrack(container, FoliageCompilerSettings.Default);

        Assert.IsTrue(result.Succeeded, string.Join("; ", result.FailureMessages));
        FoliagePageAsset page = result.PageAssets[0];
        AssertHasPacket(page, FoliageRepresentationKind.PageHLOD, FoliagePacketResidency.AlwaysResident);
        AssertHasPacket(page, FoliageRepresentationKind.CellHLOD, FoliagePacketResidency.AlwaysResident);
        AssertHasPacket(page, FoliageRepresentationKind.TreeL0, FoliagePacketResidency.NearDetail);
        AssertHasPacket(page, FoliageRepresentationKind.TreeL1, FoliagePacketResidency.NearDetail);
        AssertHasPacket(page, FoliageRepresentationKind.TreeL2, FoliagePacketResidency.NearDetail);
        AssertNoGeneratedHlodMeshes(result.AssemblyAsset!);
        Assert.Greater(result.BuildReport.AlwaysResidentBytes, 0L);
        Assert.Greater(result.BuildReport.NearDetailColdBytes, 0L);
    }

    [Test]
    public void CompileContainer_HlodUsesOneImpostorInstancePerTree()
    {
        VegetationRuntimeContainer container = CreateContainer(
            "ImpostorHlodContainer",
            new Vector3(0f, 0f, 0f));

        FoliageCompilationResult result = CompileAndTrack(container, FoliageCompilerSettings.Default);

        Assert.IsTrue(result.Succeeded, string.Join("; ", result.FailureMessages));
        FoliagePageAsset page = result.PageAssets[0];
        Assert.AreEqual(1, CountRepresentationInstances(page, FoliageRepresentationKind.PageHLOD));
        Assert.AreEqual(1, CountRepresentationInstances(page, FoliageRepresentationKind.CellHLOD));
        for (int i = 0; i < page.Packets.Count; i++)
        {
            FoliageRepresentationPacket packet = page.Packets[i];
            if (packet.RepresentationKind == FoliageRepresentationKind.PageHLOD ||
                packet.RepresentationKind == FoliageRepresentationKind.CellHLOD)
            {
                StringAssert.Contains("Impostor", packet.DebugLabel);
            }
        }
    }

    [Test]
    public void CompileContainer_CompilesShadowMappingAndWindMetadata()
    {
        VegetationRuntimeContainer container = CreateContainer(
            "ShadowWindContainer",
            new Vector3(0f, 0f, 0f));

        FoliageCompilationResult result = CompileAndTrack(container, FoliageCompilerSettings.Default);

        Assert.IsTrue(result.Succeeded, string.Join("; ", result.FailureMessages));
        FoliagePageAsset page = result.PageAssets[0];
        float treeAnchorHeight = page.Trees[0].WorldBounds.min.y;
        float trunkPhase = FindWindPhase(page, "TreeL0:TrunkFull");
        bool sawCheapTreeShadow = false;
        bool sawBranchWind = false;
        bool sawWindFlutter = false;
        for (int i = 0; i < page.Packets.Count; i++)
        {
            FoliageRepresentationPacket packet = page.Packets[i];
            if (packet.ShadowMode != FoliageShadowPacketMode.None)
            {
                Assert.AreEqual(i, packet.ShadowPacketIndex);
                AssertBoundsContain(packet.WorldBounds, page.Packets[packet.ShadowPacketIndex].WorldBounds);
            }

            if (packet.RepresentationKind == FoliageRepresentationKind.PageHLOD ||
                packet.RepresentationKind == FoliageRepresentationKind.CellHLOD)
            {
                Assert.AreEqual(FoliageShadowPacketMode.Hlod, packet.ShadowMode);
            }

            if (packet.RepresentationKind == FoliageRepresentationKind.TreeL2 &&
                packet.DebugLabel.Contains("TrunkL3"))
            {
                Assert.AreEqual(FoliageShadowPacketMode.CheapTree, packet.ShadowMode);
                sawCheapTreeShadow = true;
            }

            FoliageWindMetadata wind = page.Instances[packet.FirstInstance].WindMetadata;
            Assert.GreaterOrEqual(wind.Phase01, 0f);
            Assert.Less(wind.Phase01, 1f);
            Assert.AreEqual(treeAnchorHeight, wind.AnchorHeight, 0.0001f);
            if (packet.DebugLabel.Contains(":TreeL0:Wood") ||
                packet.DebugLabel.Contains(":TreeL0:Foliage") ||
                packet.DebugLabel.Contains(":TreeL1:Wood") ||
                packet.DebugLabel.Contains(":TreeL1:Canopy") ||
                packet.DebugLabel.Contains(":TreeL2:Wood") ||
                packet.DebugLabel.Contains(":TreeL2:Canopy"))
            {
                Assert.AreEqual(trunkPhase, wind.Phase01, 0.0001f);
                Assert.AreEqual(1f, wind.TrunkBendWeight, 0.0001f);
                sawBranchWind = true;
            }

            sawWindFlutter |= wind.BranchFlutterWeight > 0f;
        }

        Assert.IsTrue(sawCheapTreeShadow);
        Assert.IsTrue(sawBranchWind);
        Assert.IsTrue(sawWindFlutter);
    }

    [Test]
    public void CompileContainer_EnforcesNearDetailByteCap()
    {
        VegetationRuntimeContainer container = CreateContainer(
            "NearDetailCapContainer",
            new Vector3(0f, 0f, 0f));
        FoliageCompilerSettings settings = new FoliageCompilerSettings(
            new Vector3(32f, 32f, 32f),
            64,
            8,
            8192,
            4096,
            1);

        FoliageCompilationResult result = CompileAndTrack(container, settings);

        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains("maxNearDetailBytesPerPage=1", string.Join("; ", result.FailureMessages));
    }

    [Test]
    public void CompileContainer_IgnoresAuthoringBudgetAndTierTriangleWarnings()
    {
        VegetationRuntimeContainer container = CreateContainer(
            "AuthoringWarningContainer",
            new Vector3(0f, 0f, 0f),
            new Vector3(12f, 0f, 0f),
            new Vector3(24f, 0f, 0f));
        BranchPrototypeSO prototype = container.RegisteredAuthorings[0].Blueprint!.Branches[0].Prototype!;
        SetPrivateField(prototype, "triangleBudgetFoliage", 1);
        SetPrivateField(prototype, "branchL2WoodMesh", CreateTriangleMesh("AuthoringWarning_WoodL2Larger", 7, new Vector3(0.4f, 0.8f, 0.4f)));

        FoliageCompilationResult result = CompileAndTrack(container, FoliageCompilerSettings.Default);

        Assert.IsTrue(result.Succeeded, string.Join("; ", result.FailureMessages));
        Assert.Greater(result.BuildReport.PacketCount, 0);
    }

    [Test]
    public void ShaderContract_InstanceBufferIsOnlyDeclaredForProceduralNonDotsVariants()
    {
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string shaderCommonPath = Path.Combine(
            projectRoot,
            "Packages/com.voxgeofol.vegetation/Runtime/Shaders/VegetationIndirectCommon.hlsl");
        string shaderText = File.ReadAllText(shaderCommonPath).Replace("\r\n", "\n");

        StringAssert.Contains(
            "#if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED) && !defined(UNITY_DOTS_INSTANCING_ENABLED)",
            shaderText);
        StringAssert.DoesNotContain("_VOXGEOFOL_INDIRECT_RENDERING", shaderText);
        StringAssert.Contains("UNITY_DOTS_INSTANCED_PROP(uint, _VegetationPackedLeafTint)", shaderText);
        StringAssert.Contains("StructuredBuffer<VegetationInstanceData> _VegetationInstanceData;", shaderText);
        StringAssert.Contains("void SetupVegetation()", shaderText);
        StringAssert.Contains("instanceData.objectToWorld = GetObjectToWorldMatrix();", shaderText);
        StringAssert.Contains("instanceData.worldToObject = GetWorldToObjectMatrix();", shaderText);

        string[] shaderRelativePaths =
        {
            "Packages/com.voxgeofol.vegetation/Runtime/Shaders/VegetationTrunkLit.shader",
            "Packages/com.voxgeofol.vegetation/Runtime/Shaders/VegetationCanopyLit.shader",
            "Packages/com.voxgeofol.vegetation/Runtime/Shaders/VegetationFarMeshLit.shader",
            "Packages/com.voxgeofol.vegetation/Runtime/Shaders/VegetationDepthOnly.shader"
        };
        for (int i = 0; i < shaderRelativePaths.Length; i++)
        {
            string passShaderText = File.ReadAllText(Path.Combine(projectRoot, shaderRelativePaths[i])).Replace("\r\n", "\n");
            Assert.AreEqual(
                CountSubstring(passShaderText, "#pragma multi_compile_instancing"),
                CountSubstring(passShaderText, "#pragma instancing_options procedural:SetupVegetation"),
                shaderRelativePaths[i]);
            Assert.AreEqual(
                CountSubstring(passShaderText, "#pragma multi_compile_instancing"),
                CountSubstring(passShaderText, "#pragma multi_compile _ DOTS_INSTANCING_ON"),
                shaderRelativePaths[i]);
            Assert.AreEqual(
                0,
                CountSubstring(passShaderText, "_VOXGEOFOL_INDIRECT_RENDERING"),
                shaderRelativePaths[i]);
        }
    }

    [Test]
    public void RenderWorld_RenderGraphPrepareCamera_SelectsNearDetailThroughCompiledPages()
    {
        VegetationRuntimeContainer container = CreateContainer(
            "RenderWorldNearContainer",
            new Vector3(0f, 0f, 0f));
        FoliageCompilationResult result = CompileAndTrack(container, FoliageCompilerSettings.Default);
        Camera camera = CreateCamera("RenderWorldNearCamera", new Vector3(0f, 6f, -18f));
        VegetationFoliageFeatureSettings settings = new VegetationFoliageFeatureSettings
        {
            NearDetailDistance = 256f,
            ColorWorkBudget = 131072,
            MaxVisiblePacketInstances = 131072
        };

        Assert.IsTrue(result.Succeeded, string.Join("; ", result.FailureMessages));
        VegetationRenderWorld.Shared.RegisterProvider("near-provider", "near-provider", result.AssemblyAsset!, result.PageAssets);
        bool prepared = VegetationRenderWorld.Shared.PrepareRenderGraphCameraImmediateForTests(camera, VegetationRenderPassMode.Color, settings);

        Assert.IsTrue(prepared);
        Assert.Greater(VegetationRenderWorld.Shared.LastPreparedVisiblePageCount, 0);
        Assert.Greater(VegetationRenderWorld.Shared.LastPreparedVisibleCellCount, 0);
        Assert.Greater(VegetationRenderWorld.Shared.LastPreparedNearDetailPacketCount, 0);
        Assert.Greater(VegetationRenderWorld.Shared.LastPreparedNearDetailLoadRequestCount, 0);
        Assert.Greater(VegetationRenderWorld.Shared.LastPreparedNearDetailLoadedBytes, 0L);
        Assert.Greater(VegetationRenderWorld.Shared.NearDetailResidentPageCount, 0);
        Assert.Greater(VegetationRenderWorld.Shared.NearDetailResidentBytes, 0L);
        Assert.AreEqual(0, VegetationRenderWorld.Shared.LastPreparedShadowPacketCount);
    }

    [Test]
    public void RenderWorld_RenderGraphPrepareCamera_UsesHlodWhenNearDetailStreamBudgetBlocksLoad()
    {
        VegetationRuntimeContainer container = CreateContainer(
            "RenderWorldStreamBlockedContainer",
            new Vector3(0f, 0f, 0f));
        FoliageCompilationResult result = CompileAndTrack(container, FoliageCompilerSettings.Default);
        Camera camera = CreateCamera("RenderWorldStreamBlockedCamera", new Vector3(0f, 6f, -18f));
        VegetationFoliageFeatureSettings settings = new VegetationFoliageFeatureSettings
        {
            NearDetailDistance = 256f,
            ColorWorkBudget = 131072,
            MaxVisiblePacketInstances = 131072,
            NearDetailResidentByteBudget = 1,
            NearDetailUploadByteBudget = 1
        };

        Assert.IsTrue(result.Succeeded, string.Join("; ", result.FailureMessages));
        VegetationRenderWorld.Shared.RegisterProvider("stream-blocked-provider", "stream-blocked-provider", result.AssemblyAsset!, result.PageAssets);
        bool prepared = VegetationRenderWorld.Shared.PrepareRenderGraphCameraImmediateForTests(camera, VegetationRenderPassMode.Color, settings);

        Assert.IsTrue(prepared);
        Assert.Greater(VegetationRenderWorld.Shared.LastPreparedNearDetailLoadRequestCount, 0);
        Assert.AreEqual(0, VegetationRenderWorld.Shared.LastPreparedNearDetailLoadedBytes);
        Assert.AreEqual(0, VegetationRenderWorld.Shared.NearDetailResidentPageCount);
        Assert.AreEqual(0, VegetationRenderWorld.Shared.LastPreparedNearDetailPacketCount);
        Assert.Greater(VegetationRenderWorld.Shared.LastPreparedHlodPacketCount, 0);
    }

    [Test]
    public void RenderWorld_RenderGraphPrepareCamera_LoadsNearestCellWhenWholePageExceedsUploadBudget()
    {
        VegetationRuntimeContainer container = CreateContainer(
            "RenderWorldCellUploadContainer",
            new Vector3(0f, 0f, 0f),
            new Vector3(0f, 0f, 40f));
        FoliageCompilationResult result = CompileAndTrack(container, FoliageCompilerSettings.Default);
        Camera camera = CreateCamera("RenderWorldCellUploadCamera", new Vector3(0f, 6f, -18f));
        Assert.IsTrue(result.Succeeded, string.Join("; ", result.FailureMessages));
        long pageNearDetailBytes = result.PageAssets[0].BuildReport!.NearDetailColdBytes;
        int uploadBudget = Mathf.Max(1, (int)(pageNearDetailBytes / 2L + 1L));
        VegetationFoliageFeatureSettings settings = new VegetationFoliageFeatureSettings
        {
            NearDetailDistance = 256f,
            ColorWorkBudget = 131072,
            MaxVisiblePacketInstances = 131072,
            NearDetailResidentByteBudget = uploadBudget,
            NearDetailUploadByteBudget = uploadBudget
        };

        Assert.Greater(pageNearDetailBytes, uploadBudget);
        VegetationRenderWorld.Shared.RegisterProvider("cell-upload-provider", "cell-upload-provider", result.AssemblyAsset!, result.PageAssets);
        bool prepared = VegetationRenderWorld.Shared.PrepareRenderGraphCameraImmediateForTests(camera, VegetationRenderPassMode.Color, settings);

        Assert.IsTrue(prepared);
        Assert.Greater(VegetationRenderWorld.Shared.LastPreparedNearDetailPacketCount, 0);
        Assert.Greater(VegetationRenderWorld.Shared.LastPreparedHlodPacketCount, 0);
        Assert.Greater(VegetationRenderWorld.Shared.NearDetailResidentCellCount, 0);
    }

    [Test]
    public void RenderWorld_RenderGraphPrepareCamera_UsesCellBoundsDistanceForNearTierSelection()
    {
        VegetationRuntimeContainer container = CreateContainer(
            "RenderWorldCellBoundsDistanceContainer",
            new Vector3(0f, 0f, 0f),
            new Vector3(80f, 0f, 0f));
        FoliageCompilerSettings compilerSettings = new FoliageCompilerSettings(
            new Vector3(128f, 128f, 128f),
            128,
            128,
            8192,
            4096);
        FoliageCompilationResult result = CompileAndTrack(container, compilerSettings);
        Camera camera = CreateCamera("RenderWorldCellBoundsDistanceCamera", new Vector3(0f, 6f, -12f));
        VegetationFoliageFeatureSettings settings = new VegetationFoliageFeatureSettings
        {
            NearDetailDistance = 45f,
            ColorWorkBudget = 131072,
            MaxVisiblePacketInstances = 131072,
            NearDetailResidentByteBudget = 64 * 1024 * 1024,
            NearDetailUploadByteBudget = 8 * 1024 * 1024
        };

        Assert.IsTrue(result.Succeeded, string.Join("; ", result.FailureMessages));
        Assert.AreEqual(1, result.PageAssets[0].Cells.Count);
        VegetationRenderWorld.Shared.RegisterProvider("cell-bounds-distance-provider", "cell-bounds-distance-provider", result.AssemblyAsset!, result.PageAssets);
        bool prepared = VegetationRenderWorld.Shared.PrepareRenderGraphCameraImmediateForTests(camera, VegetationRenderPassMode.Color, settings);

        Assert.IsTrue(prepared);
        Assert.Greater(VegetationRenderWorld.Shared.LastPreparedTreeL0PacketCount, 0);
        Assert.AreEqual(0, VegetationRenderWorld.Shared.LastPreparedTreeL1PacketCount);
        Assert.AreEqual(0, VegetationRenderWorld.Shared.LastPreparedTreeL2PacketCount);
        Assert.AreEqual(0, VegetationRenderWorld.Shared.LastPreparedHlodPacketCount);
    }

    [Test]
    public void RenderWorld_RenderGraphPrepareCamera_RendersHlodWhenNearDetailWorkBudgetIsExhausted()
    {
        VegetationRuntimeContainer container = CreateContainer(
            "RenderWorldBudgetFallbackContainer",
            new Vector3(0f, 0f, 0f),
            new Vector3(2f, 0f, 0f));
        FoliageCompilationResult result = CompileAndTrack(container, FoliageCompilerSettings.Default);
        Camera camera = CreateCamera("RenderWorldBudgetFallbackCamera", new Vector3(0f, 6f, -18f));
        VegetationFoliageFeatureSettings settings = new VegetationFoliageFeatureSettings
        {
            NearDetailDistance = 256f,
            ColorWorkBudget = 1,
            MaxVisiblePacketInstances = 1,
            NearDetailResidentByteBudget = 64 * 1024 * 1024,
            NearDetailUploadByteBudget = 8 * 1024 * 1024
        };

        Assert.IsTrue(result.Succeeded, string.Join("; ", result.FailureMessages));
        VegetationRenderWorld.Shared.RegisterProvider("budget-fallback-provider", "budget-fallback-provider", result.AssemblyAsset!, result.PageAssets);
        bool prepared = VegetationRenderWorld.Shared.PrepareRenderGraphCameraImmediateForTests(camera, VegetationRenderPassMode.Color, settings);

        Assert.IsTrue(prepared);
        Assert.AreEqual(0, VegetationRenderWorld.Shared.LastPreparedNearDetailPacketCount);
        Assert.Greater(VegetationRenderWorld.Shared.LastPreparedHlodPacketCount, 0);
        Assert.AreEqual(2, VegetationRenderWorld.Shared.LastPreparedInstanceCount);
    }

    [Test]
    public void RenderWorld_RenderGraphPrepareCamera_DegradesCloseCellToCheaperNearDetailBeforeHlod()
    {
        VegetationRuntimeContainer container = CreateContainer(
            "RenderWorldBudgetDegradeContainer",
            new Vector3(0f, 0f, 0f));
        TreeBlueprintSO blueprint = container.RegisteredAuthorings[0].Blueprint!;
        BranchPrototypeSO prototype = blueprint.Branches[0].Prototype!;
        SetPrivateField(blueprint, "trunkMesh", CreateTriangleMesh("BudgetDegrade_HeavyTrunk", 2048, new Vector3(0.8f, 3.0f, 0.8f)));
        SetPrivateField(prototype, "woodMesh", CreateTriangleMesh("BudgetDegrade_HeavyWood", 2048, new Vector3(0.6f, 1.2f, 0.6f)));
        SetPrivateField(prototype, "foliageMesh", CreateTriangleMesh("BudgetDegrade_HeavyFoliage", 2048, new Vector3(2.0f, 1.6f, 2.0f)));
        FoliageCompilationResult result = CompileAndTrack(container, FoliageCompilerSettings.Default);
        Camera camera = CreateCamera("RenderWorldBudgetDegradeCamera", new Vector3(0f, 6f, -18f));
        VegetationFoliageFeatureSettings settings = new VegetationFoliageFeatureSettings
        {
            NearDetailDistance = 256f,
            ColorWorkBudget = 8,
            MaxVisiblePacketInstances = 3,
            NearDetailResidentByteBudget = 64 * 1024 * 1024,
            NearDetailUploadByteBudget = 8 * 1024 * 1024
        };

        Assert.IsTrue(result.Succeeded, string.Join("; ", result.FailureMessages));
        VegetationRenderWorld.Shared.RegisterProvider("budget-degrade-provider", "budget-degrade-provider", result.AssemblyAsset!, result.PageAssets);
        bool prepared = VegetationRenderWorld.Shared.PrepareRenderGraphCameraImmediateForTests(camera, VegetationRenderPassMode.Color, settings);

        Assert.IsTrue(prepared);
        Assert.Greater(VegetationRenderWorld.Shared.LastPreparedNearDetailPacketCount, 0);
        Assert.AreEqual(0, VegetationRenderWorld.Shared.LastPreparedHlodPacketCount);
        Assert.AreEqual(3, VegetationRenderWorld.Shared.LastPreparedInstanceCount);
    }

    [Test]
    public void RenderWorld_RenderGraphPrepareCamera_ReusesDepthSelectionForColorPassInSameFrame()
    {
        VegetationRuntimeContainer container = CreateContainer(
            "RenderWorldCacheContainer",
            new Vector3(0f, 0f, 0f));
        FoliageCompilationResult result = CompileAndTrack(container, FoliageCompilerSettings.Default);
        Camera camera = CreateCamera("RenderWorldCacheCamera", new Vector3(0f, 6f, -18f));
        VegetationFoliageFeatureSettings settings = new VegetationFoliageFeatureSettings
        {
            NearDetailDistance = 256f,
            ColorWorkBudget = 131072,
            MaxVisiblePacketInstances = 131072
        };

        Assert.IsTrue(result.Succeeded, string.Join("; ", result.FailureMessages));
        VegetationRenderWorld.Shared.RegisterProvider("cache-provider", "cache-provider", result.AssemblyAsset!, result.PageAssets);

        Assert.IsTrue(VegetationRenderWorld.Shared.PrepareRenderGraphCameraImmediateForTests(camera, VegetationRenderPassMode.Depth, settings));
        Assert.IsFalse(VegetationRenderWorld.Shared.LastPrepareUsedCameraCache);
        Assert.IsTrue(VegetationRenderWorld.Shared.PrepareRenderGraphCameraImmediateForTests(camera, VegetationRenderPassMode.Color, settings));
        Assert.IsTrue(VegetationRenderWorld.Shared.LastPrepareUsedCameraCache);
    }

    [Test]
    public void RenderWorld_RenderGraphPrepareCamera_EvictsNearDetailWhenResidentBudgetShrinks()
    {
        VegetationRuntimeContainer container = CreateContainer(
            "RenderWorldStreamEvictContainer",
            new Vector3(0f, 0f, 0f));
        FoliageCompilationResult result = CompileAndTrack(container, FoliageCompilerSettings.Default);
        Camera camera = CreateCamera("RenderWorldStreamEvictCamera", new Vector3(0f, 6f, -18f));
        VegetationFoliageFeatureSettings settings = new VegetationFoliageFeatureSettings
        {
            NearDetailDistance = 256f,
            ColorWorkBudget = 131072,
            MaxVisiblePacketInstances = 131072,
            NearDetailResidentByteBudget = 64 * 1024 * 1024,
            NearDetailUploadByteBudget = 8 * 1024 * 1024
        };

        Assert.IsTrue(result.Succeeded, string.Join("; ", result.FailureMessages));
        VegetationRenderWorld.Shared.RegisterProvider("stream-evict-provider", "stream-evict-provider", result.AssemblyAsset!, result.PageAssets);
        Assert.IsTrue(VegetationRenderWorld.Shared.PrepareRenderGraphCameraImmediateForTests(camera, VegetationRenderPassMode.Color, settings));
        Assert.Greater(VegetationRenderWorld.Shared.NearDetailResidentPageCount, 0);

        settings.NearDetailResidentByteBudget = 1;
        settings.NearDetailUploadByteBudget = 1;
        bool prepared = VegetationRenderWorld.Shared.PrepareRenderGraphCameraImmediateForTests(camera, VegetationRenderPassMode.Color, settings);

        Assert.IsTrue(prepared);
        Assert.Greater(VegetationRenderWorld.Shared.LastPreparedNearDetailEvictedCellCount, 0);
        Assert.AreEqual(0, VegetationRenderWorld.Shared.NearDetailResidentPageCount);
        Assert.AreEqual(0, VegetationRenderWorld.Shared.LastPreparedNearDetailPacketCount);
        Assert.Greater(VegetationRenderWorld.Shared.LastPreparedHlodPacketCount, 0);
    }

    [Test]
    public void RenderWorld_RenderGraphPrepareCamera_FallsBackToHlodOutsideNearDetailDistance()
    {
        VegetationRuntimeContainer container = CreateContainer(
            "RenderWorldHlodContainer",
            new Vector3(0f, 0f, 0f));
        FoliageCompilationResult result = CompileAndTrack(container, FoliageCompilerSettings.Default);
        Camera camera = CreateCamera("RenderWorldHlodCamera", new Vector3(0f, 6f, -18f));
        VegetationFoliageFeatureSettings settings = new VegetationFoliageFeatureSettings
        {
            NearDetailDistance = 1f,
            ColorWorkBudget = 131072,
            MaxVisiblePacketInstances = 131072
        };

        Assert.IsTrue(result.Succeeded, string.Join("; ", result.FailureMessages));
        VegetationRenderWorld.Shared.RegisterProvider("hlod-provider", "hlod-provider", result.AssemblyAsset!, result.PageAssets);
        bool prepared = VegetationRenderWorld.Shared.PrepareRenderGraphCameraImmediateForTests(camera, VegetationRenderPassMode.Color, settings);

        Assert.IsTrue(prepared);
        Assert.Greater(VegetationRenderWorld.Shared.LastPreparedHlodPacketCount, 0);
        Assert.AreEqual(0, VegetationRenderWorld.Shared.LastPreparedNearDetailPacketCount);
    }

    [Test]
    public void RenderWorld_RenderGraphPrepareFrustum_UsesCompiledCheapTreeShadowPackets()
    {
        VegetationRuntimeContainer container = CreateContainer(
            "RenderWorldShadowContainer",
            new Vector3(0f, 0f, 0f));
        FoliageCompilationResult result = CompileAndTrack(container, FoliageCompilerSettings.Default);
        Camera camera = CreateCamera("RenderWorldShadowCamera", new Vector3(0f, 6f, -18f));
        Plane[] planes = GeometryUtility.CalculateFrustumPlanes(camera);
        VegetationFoliageFeatureSettings settings = new VegetationFoliageFeatureSettings
        {
            ShadowMode = VegetationShadowMode.CheapTree,
            NearDetailDistance = 256f,
            ShadowWorkBudget = 131072,
            MaxVisiblePacketInstances = 131072
        };

        Assert.IsTrue(result.Succeeded, string.Join("; ", result.FailureMessages));
        VegetationRenderWorld.Shared.RegisterProvider("shadow-provider", "shadow-provider", result.AssemblyAsset!, result.PageAssets);
        bool prepared = VegetationRenderWorld.Shared.PrepareRenderGraphFrustumImmediateForTests(camera.transform.position, planes, settings);

        Assert.IsTrue(prepared);
        Assert.Greater(VegetationRenderWorld.Shared.LastPreparedShadowPacketCount, 0);
        Assert.AreEqual(
            VegetationRenderWorld.Shared.LastPreparedPacketCount,
            VegetationRenderWorld.Shared.LastPreparedShadowPacketCount);
    }

    private FoliageCompilationResult CompileAndTrack(
        VegetationRuntimeContainer container,
        FoliageCompilerSettings settings)
    {
        FoliageCompilationResult result = FoliageCompiledAssetCompiler.CompileContainer(container, settings);
        if (result.AssemblyAsset != null)
        {
            createdObjects.Add(result.AssemblyAsset);
            for (int i = 0; i < result.AssemblyAsset.AssetGroups.Count; i++)
            {
                Mesh mesh = result.AssemblyAsset.AssetGroups[i].Mesh;
                if ((mesh.hideFlags & HideFlags.DontSave) != 0 && !createdObjects.Contains(mesh))
                {
                    createdObjects.Add(mesh);
                }
            }
        }

        for (int i = 0; i < result.PageAssets.Count; i++)
        {
            createdObjects.Add(result.PageAssets[i]);
        }

        return result;
    }

    private static void AssertHasPacket(
        FoliagePageAsset page,
        FoliageRepresentationKind representationKind,
        FoliagePacketResidency residency)
    {
        for (int i = 0; i < page.Packets.Count; i++)
        {
            if (page.Packets[i].RepresentationKind == representationKind &&
                page.Packets[i].Residency == residency)
            {
                return;
            }
        }

        Assert.Fail($"Expected packet {representationKind} with residency {residency}.");
    }

    private static int CountRepresentationInstances(
        FoliagePageAsset page,
        FoliageRepresentationKind representationKind)
    {
        int count = 0;
        for (int i = 0; i < page.Packets.Count; i++)
        {
            if (page.Packets[i].RepresentationKind == representationKind)
            {
                count += page.Packets[i].InstanceCount;
            }
        }

        return count;
    }

    private static void AssertNoPacket(FoliagePageAsset page, FoliageRepresentationKind representationKind)
    {
        for (int i = 0; i < page.Packets.Count; i++)
        {
            Assert.AreNotEqual(representationKind, page.Packets[i].RepresentationKind);
        }
    }

    private static void AssertNoGeneratedHlodMeshes(FoliageAssemblyAsset assembly)
    {
        for (int i = 0; i < assembly.AssetGroups.Count; i++)
        {
            Mesh mesh = assembly.AssetGroups[i].Mesh;
            bool generatedHlodMesh = (mesh.hideFlags & HideFlags.DontSave) != 0 &&
                                     (mesh.name.Contains(nameof(FoliageRepresentationKind.PageHLOD)) ||
                                      mesh.name.Contains(nameof(FoliageRepresentationKind.CellHLOD)));
            Assert.IsFalse(generatedHlodMesh, assembly.AssetGroups[i].DebugLabel);
        }
    }

    private static void AssertBoundsContain(Bounds outer, Bounds inner)
    {
        Vector3 outerMin = outer.min - Vector3.one * 0.001f;
        Vector3 outerMax = outer.max + Vector3.one * 0.001f;
        Assert.GreaterOrEqual(inner.min.x, outerMin.x);
        Assert.GreaterOrEqual(inner.min.y, outerMin.y);
        Assert.GreaterOrEqual(inner.min.z, outerMin.z);
        Assert.LessOrEqual(inner.max.x, outerMax.x);
        Assert.LessOrEqual(inner.max.y, outerMax.y);
        Assert.LessOrEqual(inner.max.z, outerMax.z);
    }

    private static float FindWindPhase(FoliagePageAsset page, string debugLabelPart)
    {
        for (int i = 0; i < page.Packets.Count; i++)
        {
            FoliageRepresentationPacket packet = page.Packets[i];
            if (packet.DebugLabel.Contains(debugLabelPart))
            {
                return page.Instances[packet.FirstInstance].WindMetadata.Phase01;
            }
        }

        Assert.Fail($"Missing packet containing '{debugLabelPart}'.");
        return 0f;
    }

    private static int CountSubstring(string text, string value)
    {
        int count = 0;
        int startIndex = 0;
        while (startIndex < text.Length)
        {
            int index = text.IndexOf(value, startIndex, StringComparison.Ordinal);
            if (index < 0)
            {
                return count;
            }

            count++;
            startIndex = index + value.Length;
        }

        return count;
    }

    private VegetationRuntimeContainer CreateContainer(string name, params Vector3[] treePositions)
    {
        TreeBlueprintSO blueprint = CreateBlueprint(name);
        GameObject containerObject = new GameObject(name);
        createdObjects.Add(containerObject);
        VegetationRuntimeContainer container = containerObject.AddComponent<VegetationRuntimeContainer>();
        List<VegetationTreeAuthoring> authorings = new List<VegetationTreeAuthoring>();
        for (int i = 0; i < treePositions.Length; i++)
        {
            GameObject authoringObject = new GameObject($"{name}_Tree_{i}");
            createdObjects.Add(authoringObject);
            authoringObject.transform.SetParent(containerObject.transform, false);
            authoringObject.transform.localPosition = treePositions[i];
            VegetationTreeAuthoring authoring = authoringObject.AddComponent<VegetationTreeAuthoring>();
            SetPrivateField(authoring, "blueprint", blueprint);
            authorings.Add(authoring);
        }

        SetPrivateField(container, "registeredAuthorings", authorings);
        return container;
    }

    private TreeBlueprintSO CreateBlueprint(string name)
    {
        BranchPrototypeSO prototype = CreateScriptableObject<BranchPrototypeSO>($"{name}_Prototype");
        Material woodMaterial = CreateOpaqueMaterial($"{name}_WoodMat");
        Material foliageMaterial = CreateOpaqueMaterial($"{name}_FoliageMat");
        Material shellMaterial = CreateOpaqueMaterial($"{name}_ShellMat");
        SetPrivateField(prototype, "woodMesh", CreateTriangleMesh($"{name}_Wood", 6, new Vector3(0.6f, 1.2f, 0.6f)));
        SetPrivateField(prototype, "woodMaterial", woodMaterial);
        SetPrivateField(prototype, "foliageMesh", CreateTriangleMesh($"{name}_Foliage", 8, new Vector3(2.0f, 1.6f, 2.0f)));
        SetPrivateField(prototype, "foliageMaterial", foliageMaterial);
        SetPrivateField(prototype, "branchL1WoodMesh", CreateTriangleMesh($"{name}_WoodL1", 5, new Vector3(0.5f, 1.0f, 0.5f)));
        SetPrivateField(prototype, "branchL2WoodMesh", CreateTriangleMesh($"{name}_WoodL2", 3, new Vector3(0.4f, 0.8f, 0.4f)));
        SetPrivateField(prototype, "branchL3WoodMesh", CreateTriangleMesh($"{name}_WoodL3", 1, new Vector3(0.3f, 0.6f, 0.3f)));
        SetPrivateField(prototype, "branchL1CanopyMesh", CreateTriangleMesh($"{name}_CanopyL1", 6, new Vector3(1.6f, 1.2f, 1.6f)));
        SetPrivateField(prototype, "branchL2CanopyMesh", CreateTriangleMesh($"{name}_CanopyL2", 4, new Vector3(1.2f, 0.9f, 1.2f)));
        SetPrivateField(prototype, "branchL3CanopyMesh", CreateTriangleMesh($"{name}_CanopyL3", 2, new Vector3(0.9f, 0.7f, 0.9f)));
        SetPrivateField(prototype, "shellMaterial", shellMaterial);
        SetPrivateField(prototype, "leafColorTint", Color.green);
        SetPrivateField(prototype, "localBounds", new Bounds(Vector3.zero, new Vector3(2.2f, 2.2f, 2.2f)));

        BranchPlacement placement = new BranchPlacement();
        SetPrivateField(placement, "prototype", prototype);
        SetPrivateField(placement, "localPosition", new Vector3(0f, 1f, 0f));
        SetPrivateField(placement, "localRotation", Quaternion.identity);
        SetPrivateField(placement, "scale", 1f);

        TreeBlueprintSO blueprint = CreateScriptableObject<TreeBlueprintSO>($"{name}_Blueprint");
        Material trunkMaterial = CreateOpaqueMaterial($"{name}_TrunkMat");
        Material impostorMaterial = CreateOpaqueMaterial($"{name}_ImpostorMat");
        LODProfileSO lodProfile = CreateScriptableObject<LODProfileSO>($"{name}_LodProfile");
        SetPrivateField(lodProfile, "l0Distance", 5f);
        SetPrivateField(lodProfile, "l1Distance", 15f);
        SetPrivateField(lodProfile, "l2Distance", 30f);
        SetPrivateField(lodProfile, "hlodDistance", 80f);
        SetPrivateField(lodProfile, "absoluteCullDistance", 160f);

        SetPrivateField(blueprint, "trunkMesh", CreateTriangleMesh($"{name}_Trunk", 8, new Vector3(0.8f, 3.0f, 0.8f)));
        SetPrivateField(blueprint, "trunkL3Mesh", CreateTriangleMesh($"{name}_TrunkL3", 2, new Vector3(0.5f, 2.4f, 0.5f)));
        SetPrivateField(blueprint, "trunkMaterial", trunkMaterial);
        SetPrivateField(blueprint, "impostorMesh", CreateTriangleMesh($"{name}_Impostor", 2, new Vector3(2.8f, 4.6f, 2.8f)));
        SetPrivateField(blueprint, "impostorMaterial", impostorMaterial);
        SetPrivateField(blueprint, "hlodMaterial", impostorMaterial);
        SetPrivateField(blueprint, "lodProfile", lodProfile);
        SetPrivateField(blueprint, "branches", new[] { placement });
        SetPrivateField(blueprint, "treeBounds", new Bounds(new Vector3(0f, 0.75f, 0f), new Vector3(3.2f, 5.0f, 3.2f)));
        return blueprint;
    }

    private Mesh CreateTriangleMesh(string name, int triangleCount, Vector3 size)
    {
        Mesh mesh = new Mesh
        {
            name = name
        };
        Vector3 half = size * 0.5f;
        Vector3[] vertices = new Vector3[triangleCount * 3];
        int[] triangles = new int[triangleCount * 3];
        for (int i = 0; i < triangleCount; i++)
        {
            float t = triangleCount == 1 ? 0f : (float)i / (triangleCount - 1);
            float x = Mathf.Lerp(-half.x, half.x, t);
            vertices[i * 3] = new Vector3(x, -half.y, -half.z);
            vertices[i * 3 + 1] = new Vector3(x, half.y, -half.z);
            vertices[i * 3 + 2] = new Vector3(x, 0f, half.z);
            triangles[i * 3] = i * 3;
            triangles[i * 3 + 1] = i * 3 + 1;
            triangles[i * 3 + 2] = i * 3 + 2;
        }

        mesh.vertices = vertices;
        mesh.triangles = triangles;
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

    private Camera CreateCamera(string name, Vector3 position)
    {
        GameObject cameraObject = new GameObject(name);
        createdObjects.Add(cameraObject);
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.transform.position = position;
        camera.transform.rotation = Quaternion.LookRotation(Vector3.zero - position, Vector3.up);
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 512f;
        camera.fieldOfView = 60f;
        return camera;
    }

    private T CreateScriptableObject<T>(string name) where T : ScriptableObject
    {
        T instance = ScriptableObject.CreateInstance<T>();
        instance.name = name;
        createdObjects.Add(instance);
        return instance;
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
