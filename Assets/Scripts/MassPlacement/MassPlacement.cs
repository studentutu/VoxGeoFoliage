using System;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

/// <summary>
///     Simple mass placements onto the physical ground (terrain/any other physical object of specified mask).
/// </summary>
public class MassPlacement : MonoBehaviour
{
    private const float RayOriginOffset = 0.5f;

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
        if (!TryValidatePlacementSetup())
        {
            return;
        }

        int placementAttempts = Mathf.RoundToInt(Density);
        int placedInstances = 0;
        Transform rootTransform = RootForInstancesOnScene.transform;
        Quaternion instanceRotation = Prefab.transform.rotation;

        // Range: random points over the serialized box top face. Condition: valid setup and non-zero density. Output: instantiates prefab copies at successful ground hits.
        for (int i = 0; i < placementAttempts; i++)
        {
            Vector3 rayOrigin = CreateRandomRayOrigin();
            if (!Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hitInfo, Mathf.Infinity, UnityGroundLayer, QueryTriggerInteraction.Ignore))
            {
                continue;
            }

            GameObject instance = InstantiatePlacementPrefab();
            Transform instanceTransform = instance.transform;
            instanceTransform.SetPositionAndRotation(hitInfo.point, instanceRotation);
            instanceTransform.SetParent(rootTransform, true);
            placedInstances++;
        }

        Debug.Log($"MassPlacement placed {placedInstances}/{placementAttempts} instances for '{name}'.", this);
    }

    private void ClearRootObjects()
    {
        if (RootForInstancesOnScene == null)
        {
            Debug.LogError($"MassPlacement on '{name}' is missing {nameof(RootForInstancesOnScene)}.", this);
            return;
        }

        Transform rootTransform = RootForInstancesOnScene.transform;
        for (int i = rootTransform.childCount - 1; i >= 0; i--)
        {
            DestroyPlacementObject(rootTransform.GetChild(i).gameObject);
        }
    }

    private bool TryValidatePlacementSetup()
    {
        if (RootForInstancesOnScene == null)
        {
            Debug.LogError($"MassPlacement on '{name}' is missing {nameof(RootForInstancesOnScene)}.", this);
            return false;
        }

        if (Prefab == null)
        {
            Debug.LogError($"MassPlacement on '{name}' is missing {nameof(Prefab)}.", this);
            return false;
        }

        if (AreaToPlace == null)
        {
            Debug.LogError($"MassPlacement on '{name}' is missing {nameof(AreaToPlace)}.", this);
            return false;
        }

        if (UnityGroundLayer.value == 0)
        {
            Debug.LogError($"MassPlacement on '{name}' is missing a configured {nameof(UnityGroundLayer)} mask.", this);
            return false;
        }

        return true;
    }

    private Vector3 CreateRandomRayOrigin()
    {
        Vector3 halfSize = AreaToPlace.size * 0.5f;
        Vector3 localPoint = AreaToPlace.center + new Vector3(
            UnityEngine.Random.Range(-halfSize.x, halfSize.x),
            halfSize.y + RayOriginOffset,
            UnityEngine.Random.Range(-halfSize.z, halfSize.z));

        return AreaToPlace.transform.TransformPoint(localPoint);
    }

    private GameObject InstantiatePlacementPrefab()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            return (GameObject)PrefabUtility.InstantiatePrefab(Prefab);
        }
#endif

        return Instantiate(Prefab);
    }

    private static void DestroyPlacementObject(GameObject gameObjectToDestroy)
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            DestroyImmediate(gameObjectToDestroy);
            return;
        }
#endif

        Destroy(gameObjectToDestroy);
    }
}
