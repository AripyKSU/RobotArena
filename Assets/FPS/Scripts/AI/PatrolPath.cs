using System.Collections.Generic;
using UnityEngine;

namespace Unity.FPS.AI
{
    public class PatrolPath : MonoBehaviour
    {
        [Tooltip("Enemies that will be assigned to this path on Start")]
        public List<EnemyController> EnemiesToAssign = new List<EnemyController>();

        [Tooltip("The Nodes making up the path")]
        public List<Transform> PathNodes = new List<Transform>();

        void Start()
        {
            foreach (var enemy in EnemiesToAssign)
            {
                if (enemy == null)
                {
                    Debug.LogWarning(
                        $"PatrolPath '{name}' contains a missing enemy reference. " +
                        "The missing entry will be ignored.",
                        this);
                    continue;
                }

                enemy.PatrolPath = this;
            }
        }

        public float GetDistanceToNode(Vector3 origin, int destinationNodeIndex)
        {
            if (destinationNodeIndex < 0 || destinationNodeIndex >= PathNodes.Count ||
                PathNodes[destinationNodeIndex] == null)
            {
                return -1f;
            }

            return (PathNodes[destinationNodeIndex].position - origin).magnitude;
        }

        public Vector3 GetPositionOfPathNode(int nodeIndex)
        {
            if (nodeIndex < 0 || nodeIndex >= PathNodes.Count || PathNodes[nodeIndex] == null)
            {
                return Vector3.zero;
            }

            return PathNodes[nodeIndex].position;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            for (int i = 0; i < PathNodes.Count; i++)
            {
                int nextIndex = i + 1;
                if (nextIndex >= PathNodes.Count)
                {
                    nextIndex -= PathNodes.Count;
                }

                Transform currentNode = PathNodes[i];
                Transform nextNode = PathNodes[nextIndex];
                if (currentNode == null || nextNode == null)
                {
                    continue;
                }

                Gizmos.DrawLine(currentNode.position, nextNode.position);
                Gizmos.DrawSphere(currentNode.position, 0.1f);
            }
        }
    }
}
