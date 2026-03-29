#nullable enable

using UnityEngine;

namespace VoxGeoFol.Features.Vegetation.Authoring
{
    /// <summary>
    /// Authoring-time placement of one reusable branch prototype inside a tree blueprint.
    /// </summary>
    [System.Serializable]
    public sealed class BranchPlacement
    {
        [SerializeField] private BranchPrototypeSO? prototype;
        [SerializeField] private Vector3 localPosition = Vector3.zero;
        [SerializeField] private Quaternion localRotation = Quaternion.identity;
        [SerializeField] private float scale = 1f;

        public BranchPrototypeSO? Prototype => prototype;

        public Vector3 LocalPosition => localPosition;

        public Quaternion LocalRotation => localRotation;

        public float Scale => scale;
    }
}
