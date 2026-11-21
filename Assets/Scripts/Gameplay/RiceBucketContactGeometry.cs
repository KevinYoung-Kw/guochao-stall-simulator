using System.Collections.Generic;
using UnityEngine;

namespace Guochao
{
    internal static class RiceBucketContactGeometry
    {
        private const float BottomHeightRatio = 0.01f;
        private const int MinContactVertexCount = 10;
        private const float MinFootprintSize = 0.001f;
        private const float MinValidHeight = 1e-5f;

        internal static bool TryMeasure(Transform root, out Bounds contact)
        {
            contact = default(Bounds);
            if (root == null)
            {
                return false;
            }

            try
            {
                MeshFilter[] filters = root.GetComponentsInChildren<MeshFilter>(true);
                if (filters == null || filters.Length == 0)
                {
                    return false;
                }

                Vector3 rootPosition = root.position;
                Quaternion inverseRootRotation = Quaternion.Inverse(root.rotation);
                List<Vector3> localPoints = new List<Vector3>();
                List<Vector3> meshVertices = new List<Vector3>();

                float minY = float.PositiveInfinity;
                float maxY = float.NegativeInfinity;

                for (int i = 0; i < filters.Length; i++)
                {
                    MeshFilter filter = filters[i];
                    if (filter == null)
                    {
                        continue;
                    }

                    Mesh mesh = filter.sharedMesh;
                    if (mesh == null)
                    {
                        continue;
                    }

                    if (!mesh.isReadable)
                    {
                        return false;
                    }

                    int vertexCount = mesh.vertexCount;
                    if (vertexCount <= 0)
                    {
                        continue;
                    }

                    meshVertices.Clear();
                    mesh.GetVertices(meshVertices);
                    if (meshVertices.Count != vertexCount)
                    {
                        return false;
                    }

                    Transform filterTransform = filter.transform;
                    for (int j = 0; j < meshVertices.Count; j++)
                    {
                        Vector3 localPoint = inverseRootRotation * (filterTransform.TransformPoint(meshVertices[j]) - rootPosition);
                        if (!IsFinite(localPoint))
                        {
                            return false;
                        }

                        localPoints.Add(localPoint);

                        if (localPoint.y < minY)
                        {
                            minY = localPoint.y;
                        }

                        if (localPoint.y > maxY)
                        {
                            maxY = localPoint.y;
                        }
                    }
                }

                if (localPoints.Count < MinContactVertexCount)
                {
                    return false;
                }

                float height = maxY - minY;
                if (!IsFinite(minY) || !IsFinite(maxY) || !IsFinite(height) || height <= MinValidHeight)
                {
                    return false;
                }

                float maxContactY = minY + height * BottomHeightRatio;
                if (!IsFinite(maxContactY))
                {
                    return false;
                }

                int contactVertexCount = 0;
                Vector3 contactMin = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
                Vector3 contactMax = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);

                for (int i = 0; i < localPoints.Count; i++)
                {
                    Vector3 point = localPoints[i];
                    if (point.y > maxContactY)
                    {
                        continue;
                    }

                    contactVertexCount++;
                    contactMin = Vector3.Min(contactMin, point);
                    contactMax = Vector3.Max(contactMax, point);
                }

                if (contactVertexCount < MinContactVertexCount)
                {
                    return false;
                }

                Bounds measured = default(Bounds);
                measured.SetMinMax(contactMin, contactMax);
                if (!IsFinite(measured.center) || !IsFinite(measured.size))
                {
                    return false;
                }

                if (measured.size.x <= MinFootprintSize || measured.size.z <= MinFootprintSize)
                {
                    return false;
                }

                contact = measured;
                return true;
            }
            catch
            {
                contact = default(Bounds);
                return false;
            }
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }
    }
}
