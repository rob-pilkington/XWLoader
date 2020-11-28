using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PolygonCutter
{
    /// <summary>
    /// A simple Vector3 implementation - can probably be replaced by UnityEngine.Vector3
    /// </summary>
    class Vector3
    {
        public Vector3(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public float x, y, z;

        public static Vector3 operator+ (Vector3 a, Vector3 b)
        {
            return new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        }

        public static Vector3 operator -(Vector3 a, Vector3 b)
        {
            return new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        }

        public static Vector3 operator-(Vector3 a)
        {
            return new Vector3(-a.x, -a.y, -a.z);
        }

        public static Vector3 operator *(Vector3 a, Vector3 b)
        {
            return new Vector3(a.x * b.x, a.y * b.y, a.z * b.z);
        }

        public static Vector3 operator*(Vector3 a, float b)
        {
            return new Vector3(a.x * b, a.y * b, a.z * b);
        }

        public static Vector3 operator *(float b, Vector3 a)
        {
            return new Vector3(a.x * b, a.y * b, a.z * b);
        }

        public static Vector3 operator /(Vector3 a, float b)
        {
            return new Vector3(a.x / b, a.y / b, a.z / b);
        }

        public static bool operator == (Vector3 a, Vector3 b)
        {
            if ((object)a == null || (object)b == null)
            {
                return (object)a == (object)b;
            }

            return a.x == b.x && a.y == b.y && a.z == b.z;
        }

        public override bool Equals(object obj)
        {
            if (obj is Vector3)
                return this == (Vector3)obj;
            else
                return base.Equals(obj);
        }

        public override int GetHashCode()
        {
            return (x.GetHashCode() * 3867) ^ (y.GetHashCode() * 47329) ^ (z.GetHashCode() * 2189975);
        }

        public static bool operator !=(Vector3 a, Vector3 b)
        {
            if ((object)a == null || (object)b == null)
                return (object)a != (object)b;

            return a.x != b.x || a.y != b.y || a.z != b.z;
        }

        public static Vector3 lerp(Vector3 a, Vector3 b, float value)
        {
            if (value < 0f)
                value = 0f;
            if (value > 1f)
                value = 1f;

            return new Vector3(a.x * (1f - value) + b.x * value, a.y * (1f - value) + b.y * value, a.z * (1f - value) + b.z * value);
        }

        public static float Dot(Vector3 a, Vector3 b)
        {
            return a.Dot(b);
        }

        public static Vector3 Cross(Vector3 a, Vector3 b)
        {
            return a.Cross(b);
        }

        private float Dot(Vector3 b)
        {
            return this.x * b.x + this.y * b.y + this.z * b.z;
        }

        private Vector3 Cross(Vector3 b)
        {
            return new Vector3(
                y * b.z - z * b.y,
                z * b.x - x * b.z,
                x * b.y - y * b.x
                );
        }

        public float Len()
        {
            return (float)Math.Sqrt(x * x + y * y + z * z);
        }

        public Vector3 Normalise()
        {
            float Length = this.Len();
            return new Vector3(x / Length, y / Length, z / Length);
        }
    }
}
