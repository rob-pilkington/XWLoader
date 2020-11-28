using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PolygonCutter
{
    /// <summary>
    /// The input output class. Contains a list of triangle indices and a colour.
    /// </summary>
    class Triangle
    {
        public int[] VertexIndex = new int[3];
        public Vector3 FaceNormal;
        public Triangle(int V0, int V1, int V2, Vector3 FaceNormal)
        {
            VertexIndex[0] = V0;
            VertexIndex[1] = V1;
            VertexIndex[2] = V2;

            this.FaceNormal = FaceNormal;
        }
        public static List<Triangle> BuildTriangleList(int[] triangleIndices, int[] vertIndices, List<UnityEngine.Vector3> originalVerts, UnityEngine.Vector3[] sectionNormals)
        {
            List<Triangle> result = new List<Triangle>();

            for (int j = 0; j < triangleIndices.Length; j += 3)
            {
                int v0 = vertIndices[triangleIndices[j]];
                int v1 = vertIndices[triangleIndices[j + 1]];
                int v2 = vertIndices[triangleIndices[j + 2]];

                var uN = UnityEngine.Vector3.Cross(originalVerts[vertIndices[1]] - originalVerts[vertIndices[0]], originalVerts[vertIndices[2]] - originalVerts[vertIndices[0]]).normalized;

                result.Add(new Triangle(v0, v1, v2, new Vector3(uN.x, uN.y, uN.z)));
            }

            return result;
        }

        public static void ReverseTriangleList(List<Triangle> triList, List<PositionAndNormal> triMeshPoints, List<UnityEngine.Vector3> originalVerts, out UnityEngine.Vector3[] sectionNormals, out int[] triangleIndices, out int[] vertIndices)
        {
            Dictionary<int, int> origVertToListIndex = new Dictionary<int, int>();

            // Build the vertIndices
            List<int> lstVertIndices = new List<int>();
            foreach (Triangle tri in triList)
            {
                if (!origVertToListIndex.ContainsKey(tri.VertexIndex[0]))
                {
                    origVertToListIndex.Add(tri.VertexIndex[0], lstVertIndices.Count);
                    lstVertIndices.Add(tri.VertexIndex[0]);
                }
                if (!origVertToListIndex.ContainsKey(tri.VertexIndex[1]))
                {
                    origVertToListIndex.Add(tri.VertexIndex[1], lstVertIndices.Count);
                    lstVertIndices.Add(tri.VertexIndex[1]);
                }
                if (!origVertToListIndex.ContainsKey(tri.VertexIndex[2]))
                {
                    origVertToListIndex.Add(tri.VertexIndex[2], lstVertIndices.Count);
                    lstVertIndices.Add(tri.VertexIndex[2]);
                }
            }

            // Build the triIndices
            List<int> lstTriIndices = new List<int>();
            foreach (Triangle tri in triList)
            {
                lstTriIndices.Add(origVertToListIndex[tri.VertexIndex[0]]);
                lstTriIndices.Add(origVertToListIndex[tri.VertexIndex[1]]);
                lstTriIndices.Add(origVertToListIndex[tri.VertexIndex[2]]);
            }

            // Assign the new "original" verts/normals
            originalVerts.Clear();

            List<UnityEngine.Vector3> lstSectionNormals = new List<UnityEngine.Vector3>();
            foreach (PositionAndNormal p in triMeshPoints)
            {
                lstSectionNormals.Add(new UnityEngine.Vector3(p.Normal.x, p.Normal.y, p.Normal.z));
                originalVerts.Add(new UnityEngine.Vector3(p.Pos.x, p.Pos.y, p.Pos.z));
            }

            sectionNormals = lstSectionNormals.ToArray();
            triangleIndices = lstTriIndices.ToArray();
            vertIndices = lstVertIndices.ToArray();
        }
    }
}
