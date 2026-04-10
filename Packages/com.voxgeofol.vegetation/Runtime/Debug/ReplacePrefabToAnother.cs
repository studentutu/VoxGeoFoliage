#nullable enable

using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace VoxGeoFol.Features.Vegetation.Debugging
{
    /// <summary>
    /// [INTEGRATION] Editor-only helper that swaps matching prefab instance roots under a chosen hierarchy root.
    /// </summary>
    [AddComponentMenu("VoxGeoFol/Debug/Replace Prefab in hierarchy")]
    [DisallowMultipleComponent]
    public sealed class ReplacePrefabToAnother : MonoBehaviour
    {
        [SerializeField] private Transform? hierarchyRoot;
        [SerializeField] private GameObject? initialPrefab;
        [SerializeField] private GameObject? replacementPrefab;
        [SerializeField] private bool includeInactiveChildren = true;
        [SerializeField] private bool includeHierarchyRoot = false;
        [SerializeField] private bool logEachReplacement = false;

#if UNITY_EDITOR
        private readonly List<Transform> matchingPrefabRoots = new List<Transform>(32);
#endif

        private void Reset()
        {
            RefreshDefaults();
        }

        private void OnValidate()
        {
            RefreshDefaults();
        }

        /// <summary>
        /// [INTEGRATION] Replaces every matching prefab instance root in the selected hierarchy scope.
        /// </summary>
        [ContextMenu("Replace Matching Prefab Instances")]
        public void ReplaceMatchingPrefabInstances()
        {
#if UNITY_EDITOR
            RefreshDefaults();
            if (!TryValidateRequest(out Transform scopeRoot, out GameObject sourcePrefab, out GameObject targetPrefab))
            {
                return;
            }

            CollectMatchingPrefabRoots(scopeRoot, sourcePrefab);
            if (matchingPrefabRoots.Count == 0)
            {
                Debug.LogWarning(
                    $"{nameof(ReplacePrefabToAnother)} found no instances of '{sourcePrefab.name}' under '{scopeRoot.name}'.",
                    this);
                return;
            }

            if (WouldReplaceComponentHierarchy())
            {
                Debug.LogError(
                    $"{nameof(ReplacePrefabToAnother)} cannot run from inside a hierarchy that will be replaced. Move the component outside the matched prefab instances.",
                    this);
                return;
            }

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Replace Matching Prefab Instances");

            int replacedCount = 0;
            for (int index = 0; index < matchingPrefabRoots.Count; index++)
            {
                Transform matchedRoot = matchingPrefabRoots[index];
                if (matchedRoot == null)
                {
                    continue;
                }

                if (!IsMatchingPrefabInstanceRoot(matchedRoot.gameObject, sourcePrefab))
                {
                    continue;
                }

                ReplacePrefabInstanceRoot(matchedRoot, targetPrefab);
                replacedCount++;
            }

            Undo.CollapseUndoOperations(undoGroup);
            Debug.Log(
                $"{nameof(ReplacePrefabToAnother)} replaced {replacedCount} instance(s) of '{sourcePrefab.name}' with '{targetPrefab.name}' under '{scopeRoot.name}'.",
                this);
#else
            Debug.LogWarning($"{nameof(ChangeBranchToPrefab)} only works in the Unity Editor.", this);
#endif
        }

        private void RefreshDefaults()
        {
            if (hierarchyRoot == null)
            {
                hierarchyRoot = transform;
            }
        }

#if UNITY_EDITOR
        private bool TryValidateRequest(out Transform scopeRoot, out GameObject sourcePrefab, out GameObject targetPrefab)
        {
            scopeRoot = hierarchyRoot != null ? hierarchyRoot : transform;
            sourcePrefab = initialPrefab!;
            targetPrefab = replacementPrefab!;

            if (initialPrefab == null)
            {
                Debug.LogError($"{nameof(ReplacePrefabToAnother)} requires an initial prefab asset.", this);
                return false;
            }

            if (replacementPrefab == null)
            {
                Debug.LogError($"{nameof(ReplacePrefabToAnother)} requires a replacement prefab asset.", this);
                return false;
            }

            if (!IsPrefabAssetRoot(initialPrefab))
            {
                Debug.LogError($"{nameof(ReplacePrefabToAnother)} initial prefab must be a prefab root asset.", this);
                return false;
            }

            if (!IsPrefabAssetRoot(replacementPrefab))
            {
                Debug.LogError($"{nameof(ReplacePrefabToAnother)} replacement prefab must be a prefab root asset.", this);
                return false;
            }

            if (initialPrefab == replacementPrefab)
            {
                Debug.LogError($"{nameof(ReplacePrefabToAnother)} source and replacement prefabs must differ.", this);
                return false;
            }

            if (includeHierarchyRoot && scopeRoot == transform && IsMatchingPrefabInstanceRoot(scopeRoot.gameObject, initialPrefab))
            {
                Debug.LogError(
                    $"{nameof(ReplacePrefabToAnother)} cannot replace its own GameObject. Move the component elsewhere or disable '{nameof(includeHierarchyRoot)}'.",
                    this);
                return false;
            }

            return true;
        }

        // Range-Condition-Output: walks the selected hierarchy once, emits only outermost matching prefab roots, and skips their descendants to avoid double replacement.
        private void CollectMatchingPrefabRoots(Transform scopeRoot, GameObject sourcePrefab)
        {
            matchingPrefabRoots.Clear();

            Stack<Transform> pending = new Stack<Transform>();
            if (includeHierarchyRoot)
            {
                pending.Push(scopeRoot);
            }
            else
            {
                PushChildrenInReverseOrder(scopeRoot, pending);
            }

            while (pending.Count > 0)
            {
                Transform current = pending.Pop();
                if (!includeInactiveChildren && !current.gameObject.activeInHierarchy)
                {
                    continue;
                }

                if (IsMatchingPrefabInstanceRoot(current.gameObject, sourcePrefab))
                {
                    matchingPrefabRoots.Add(current);
                    continue;
                }

                PushChildrenInReverseOrder(current, pending);
            }
        }

        // Range-Condition-Output: one matched prefab instance root in the hierarchy. Output: instantiates the replacement under the same parent and preserves sibling slot plus local transform before deleting the old root.
        private void ReplacePrefabInstanceRoot(Transform originalRoot, GameObject targetPrefab)
        {
            Transform? originalParent = originalRoot.parent;
            int siblingIndex = originalRoot.GetSiblingIndex();
            Vector3 localPosition = originalRoot.localPosition;
            Quaternion localRotation = originalRoot.localRotation;
            Vector3 localScale = originalRoot.localScale;
            string originalName = originalRoot.name;

            GameObject replacementInstance = (GameObject)PrefabUtility.InstantiatePrefab(targetPrefab, originalRoot.gameObject.scene);
            Undo.RegisterCreatedObjectUndo(replacementInstance, "Instantiate replacement prefab");

            Transform replacementTransform = replacementInstance.transform;
            if (originalParent != null)
            {
                Undo.SetTransformParent(replacementTransform, originalParent, "Parent replacement prefab");
            }

            Undo.RecordObject(replacementTransform, "Apply replacement local transform");
            replacementTransform.SetSiblingIndex(siblingIndex);
            replacementTransform.localPosition = localPosition;
            replacementTransform.localRotation = localRotation;
            replacementTransform.localScale = localScale;

            if (replacementInstance.name != originalName)
            {
                Undo.RecordObject(replacementInstance, "Preserve replacement object name");
                replacementInstance.name = originalName;
            }

            if (logEachReplacement)
            {
                Debug.Log(
                    $"{nameof(ReplacePrefabToAnother)} replaced '{originalName}' with prefab '{targetPrefab.name}'.",
                    originalRoot);
            }

            Undo.DestroyObjectImmediate(originalRoot.gameObject);
        }

        private static bool IsMatchingPrefabInstanceRoot(GameObject candidate, GameObject sourcePrefab)
        {
            if (!PrefabUtility.IsAnyPrefabInstanceRoot(candidate))
            {
                return false;
            }

            GameObject? candidateSource = PrefabUtility.GetCorrespondingObjectFromSource(candidate);
            return candidateSource == sourcePrefab;
        }

        private static bool IsPrefabAssetRoot(GameObject candidate)
        {
            return PrefabUtility.IsPartOfPrefabAsset(candidate) && candidate.transform.parent == null;
        }

        private bool WouldReplaceComponentHierarchy()
        {
            for (int index = 0; index < matchingPrefabRoots.Count; index++)
            {
                Transform matchedRoot = matchingPrefabRoots[index];
                if (matchedRoot == null)
                {
                    continue;
                }

                if (transform == matchedRoot || transform.IsChildOf(matchedRoot))
                {
                    return true;
                }
            }

            return false;
        }

        private static void PushChildrenInReverseOrder(Transform parent, Stack<Transform> pending)
        {
            for (int childIndex = parent.childCount - 1; childIndex >= 0; childIndex--)
            {
                pending.Push(parent.GetChild(childIndex));
            }
        }
#endif
    }
}
