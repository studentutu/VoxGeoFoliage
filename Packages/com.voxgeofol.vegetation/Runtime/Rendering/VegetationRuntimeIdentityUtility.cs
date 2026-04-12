#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;

namespace VoxGeoFol.Features.Vegetation.Rendering
{
    /// <summary>
    /// Builds deterministic runtime identifiers from authored scene ownership.
    /// </summary>
    public static class VegetationRuntimeIdentityUtility
    {
        /// <summary>
        /// [INTEGRATION] Builds the deterministic container identifier hash from scene path plus hierarchy path.
        /// </summary>
        public static Hash128 BuildContainerIdHash(GameObject containerGameObject)
        {
            if (containerGameObject == null)
            {
                throw new ArgumentNullException(nameof(containerGameObject));
            }

            string sceneIdentifier = string.IsNullOrEmpty(containerGameObject.scene.path)
                ? containerGameObject.scene.name ?? string.Empty
                : containerGameObject.scene.path;
            string hierarchyPath = BuildHierarchyPath(containerGameObject.transform);
            return Hash128.Compute($"{sceneIdentifier}|{hierarchyPath}");
        }

        /// <summary>
        /// [INTEGRATION] Builds the deterministic tree identifier hash from container identity plus source order.
        /// </summary>
        public static Hash128 BuildTreeIdHash(Hash128 containerIdHash, int sourceOrder)
        {
            return Hash128.Compute($"{containerIdHash}|{sourceOrder:D8}");
        }

        /// <summary>
        /// [INTEGRATION] Builds a deterministic hierarchy path including sibling order for scene ownership checks.
        /// </summary>
        public static string BuildHierarchyPath(Transform transform)
        {
            if (transform == null)
            {
                throw new ArgumentNullException(nameof(transform));
            }

            Stack<string> pathParts = new Stack<string>();
            Transform? current = transform;
            while (current != null)
            {
                pathParts.Push($"{current.GetSiblingIndex():D4}:{current.name}");
                current = current.parent;
            }

            return string.Join("/", pathParts.ToArray());
        }
    }
}
