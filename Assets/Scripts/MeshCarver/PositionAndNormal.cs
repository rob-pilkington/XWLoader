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
    }
}
