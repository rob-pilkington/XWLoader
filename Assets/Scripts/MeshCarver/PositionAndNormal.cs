using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PolygonCutter
{
    class PositionAndNormal
    {
        public PositionAndNormal(Vector3 p, Vector3 n)
        {
            this.Pos = p;
            this.Normal = n;
        }

        public Vector3 Pos;
        public Vector3 Normal;

        public static bool operator == (PositionAndNormal a, PositionAndNormal b)
        {
            if (a is null || b is null)
            {
                return (object)a == (object)b;
            }

            return a.Pos == b.Pos && a.Normal == b.Normal;
        }

        public static bool operator !=(PositionAndNormal a, PositionAndNormal b)
        {
            if (a is null || b is null)
            {
                return (object)a != (object)b;
            }

            return (a.Pos != b.Pos) || (a.Normal != b.Normal);
        }

        public static List<PositionAndNormal> BuildPositionAndNormalList(List<UnityEngine.Vector3> originalVerts, UnityEngine.Vector3[] sectionNormals, UnityEngine.Vector3 FaceNormal)
        {
            List<PositionAndNormal> result = new List<PositionAndNormal>();

            for (int i = 0; i < originalVerts.Count; i++)
            {
                if (i < sectionNormals.Length)
                    result.Add(new PositionAndNormal(new Vector3(originalVerts[i].x, originalVerts[i].y, originalVerts[i].z), new Vector3(sectionNormals[i].x, sectionNormals[i].y, sectionNormals[i].z)));
                else
                    result.Add(new PositionAndNormal(new Vector3(originalVerts[i].x, originalVerts[i].y, originalVerts[i].z), new Vector3(FaceNormal.x, FaceNormal.y, FaceNormal.z)));
            }

            return result;
        }
    }
}
