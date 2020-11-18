using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PolygonCutter
{
    /// <summary>
    /// A simple Plane implementation - can probably be replaced by UnityEngine.Plane
    /// </summary>
    class Plane
    {
        public float a, b, c, d;

        public Plane(float x, float y, float z, float d)
        {
            a = x;
            b = y;
            c = z;
            this.d = d;
        }

        public Plane(Vector3 a, Vector3 b, Vector3 c)
        {
            // b - a
            Vector3 ab = (b - a).Normalise();

            // c - a
            Vector3 ac = (c - a).Normalise();

            Vector3 n = Vector3.Cross(ab, ac);

            n = n.Normalise();

            //ax + by + cz + d = 0

            float d = -(n.x * a.x + n.y * a.y + n.z * a.z);
            this.a = n.x;
            this.b = n.y;
            this.c = n.z;
            this.d = d;
        }

        public Vector3 Normal()
        {
            return new Vector3(a, b, c);
        }

        public static Plane FromTriangle(Vector3 a, Vector3 b, Vector3 c)
        {
            // b - a
            Vector3 ab = (b - a).Normalise();

            // c - a
            Vector3 ac = (c - a).Normalise();

            Vector3 n = Vector3.Cross(ab, ac);

            n = n.Normalise();

            //ax + by + cz + d = 0

            float d = -(n.x * a.x + n.y * a.y + n.z * a.z);
            return new Plane(n.x, n.y, n.z, d);
        }

        public float PointValue(Vector3 v)
        {
            return (a * v.x + b * v.y + c * v.z + d);
        }
    }
}
