#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;

namespace VoxGeoFol.Features.Vegetation.Authoring
{
    /// <summary>
    /// Shared hierarchy helpers for branch shell authoring data.
    /// </summary>
    public static class BranchShellNodeUtility
    {
        /// <summary>
        /// [INTEGRATION] Returns the persisted hierarchy array for one authored shell tier.
        /// </summary>
        public static BranchShellNode[] GetHierarchyForLevel(BranchPrototypeSO prototype, int shellLevel)
        {
            if (prototype == null)
            {
                throw new ArgumentNullException(nameof(prototype));
            }

            return shellLevel switch
            {
                0 => prototype.ShellNodesL0,
                1 => prototype.ShellNodesL1,
                2 => prototype.ShellNodesL2,
                _ => throw new ArgumentOutOfRangeException(nameof(shellLevel), shellLevel, "Shell level must be 0, 1, or 2.")
            };
        }

        /// <summary>
        /// [INTEGRATION] Enumerates the current leaf frontier for one branch shell hierarchy.
        /// </summary>
        public static List<BranchShellNode> CollectLeafNodes(BranchShellNode[]? shellNodes)
        {
            // Range: accepts null or empty node arrays. Condition: node hierarchy uses preorder child storage. Output: all nodes that should render on the active leaf frontier.
            List<BranchShellNode> leafNodes = new List<BranchShellNode>();
            if (shellNodes == null || shellNodes.Length == 0)
            {
                return leafNodes;
            }

            for (int i = 0; i < shellNodes.Length; i++)
            {
                BranchShellNode node = shellNodes[i] ?? throw new InvalidOperationException($"shellNodes[{i}] is missing.");
                if (!HasChildren(node))
                {
                    leafNodes.Add(node);
                }
            }

            return leafNodes;
        }

        public static bool HasChildren(BranchShellNode node)
        {
            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }

            return node.ChildMask != 0 && node.FirstChildIndex >= 0;
        }

        public static Mesh? GetShellMesh(BranchShellNode node, int shellLevel)
        {
            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }

            return shellLevel switch
            {
                0 => node.ShellL0Mesh,
                1 => node.ShellL1Mesh,
                2 => node.ShellL2Mesh,
                _ => throw new ArgumentOutOfRangeException(nameof(shellLevel), shellLevel, "Shell level must be 0, 1, or 2.")
            };
        }

        public static int GetTriangleCountForLeafFrontier(BranchShellNode[]? shellNodes, int shellLevel)
        {
            List<BranchShellNode> leaves = CollectLeafNodes(shellNodes);
            int triangleCount = 0;
            for (int i = 0; i < leaves.Count; i++)
            {
                Mesh? shellMesh = GetShellMesh(leaves[i], shellLevel);
                if (shellMesh != null)
                {
                    triangleCount += checked((int)(shellMesh.GetIndexCount(0) / 3L));
                }
            }

            return triangleCount;
        }
    }
}
