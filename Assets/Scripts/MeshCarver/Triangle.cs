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
        public int Colour;

        public Triangle(int V0, int V1, int V2, int ColourIndex)
        {
            VertexIndex[0] = V0;
            VertexIndex[1] = V1;
            VertexIndex[2] = V2;

            Colour = ColourIndex;

        }
    }
}
