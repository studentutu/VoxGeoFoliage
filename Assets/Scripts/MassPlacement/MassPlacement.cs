using System;
using UnityEngine;

/// <summary>
///     Simple mass placements onto the physical ground (terrain/any other physical object of specified mask).
/// </summary>
public class MassPlacement : MonoBehaviour
{
    [SerializeField] private GameObject RootForInstancesOnScene;
    [SerializeField] private GameObject Prefab;
    [SerializeField] private BoxCollider AreaToPlace;
    [SerializeField] private LayerMask UnityGroundLayer;
    [SerializeField] [Range(10, 10_000)] private float Density = 1_00;

    [Header("Editor quick and dirty utility.")]
    [SerializeField] private bool ClearRoot = false;
    [SerializeField] private bool Replace = false;

    private void OnValidate()
    {
        if (ClearRoot)
        {
            ClearRoot = false;
            Replace = false;

            ClearRootObjects();
            return;
        }

        if (Replace)
        {
            ClearRoot = false;
            Replace = false;
            ClearRootObjects();
            PlaceOntoGround();
        }
    }

    private void PlaceOntoGround()
    {
        // 1. Area projectition from top to down
        // 2. Generate random density points on top of the box
        // 3. Use physics to calculate where to place (gather Vector3[] positions )
        // 4. For each of the position instantiate Prefab under RootForInstancesOnScene
    }

    private void ClearRootObjects()
    {
        // Remove all objects under RootForInstancesOnScene
    }
}
