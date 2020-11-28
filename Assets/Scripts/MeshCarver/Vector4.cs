using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PolygonCutter
{
    /// <summary>
    /// A 3d line formed out of two points a and b.
    /// </summary>
    class Vector3Line
    {
        public float x, y, z;
        public float dx, dy, dz;
        public Vector3Line(float x, float y, float z, float dx, float dy, float dz)
        {
            this.x = x;
            this.y = y;
            this.z = z;
            this.dx = dx;
            this.dy = dy;
            this.dz = dz;
        }

        public Vector3Line(Vector3 PointA, Vector3 PointB)
        {
            x = PointA.x;
            y = PointA.y;
            z = PointA.z;

            dx = PointB.x - PointA.x;
            dy = PointB.y - PointA.y;
            dz = PointB.z - PointA.z;
        }

        public Vector3Line(Vector3Line vClone)
        {
            this.x = vClone.x;
            this.y = vClone.y;
            this.z = vClone.z;
            this.dx = vClone.dx;
            this.dy = vClone.dy;
            this.dz = vClone.dz;
        }

        public Vector3 Dir()
        {
            return new Vector3(dx, dy, dz);
        }

        public Vector3 Origin()
        {
            return new Vector3(x, y, z);
        }

        public static Vector3Line FromVector3(Vector3 v3Origin, Vector3 v3End)
        {
            Vector3 dir = v3End - v3Origin;

            return new Vector3Line(v3Origin.x, v3Origin.y, v3Origin.z, dir.x, dir.y, dir.z);
        }

        public Vector3 IntersectPlane(Plane plane)
        {
            float? s = IntersectPlaneDistance(plane);

            // Intersection point is Original + Dir * s
            if (s.HasValue)
                return new Vector3(x, y, z) + Dir().Normalise() * s.Value;
            else
                return null;
        }

        public float? IntersectPlaneDistance(Plane plane)
        {
            float nu = Vector3.Dot(plane.Normal(), Dir().Normalise());
            if (nu == 0.0f)
                return null;

            // -(ax0 + by0 + cz0 + d) / (n . u)
            float s = -(plane.a * x + plane.b * y + plane.c * z + plane.d) / nu;

            return s;
        }

        /// <summary>
        /// Returns the nearest point between a line and a point. Distance is a number between 0 and 1
        /// </summary>
        /// <param name="vTest"></param>
        /// <param name="Distance"></param>
        /// <returns></returns>
        public Vector3 FindNearestPoint(Vector3 vTest, out float Distance)
        {
            float LineLen = Dir().Len();

            float t = Vector3.Dot(Dir(), (vTest - Origin())) / Vector3.Dot(Dir(), Dir());

            Distance = t;

            Vector3 vFound = Origin() + t * Dir();

            return vFound;
        }
    }
}
