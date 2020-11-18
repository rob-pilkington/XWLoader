using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PolygonCutter
{
    /// <summary>
    /// A hull edge/segment used to build up the hull perimeters, used for winding operations and such.
    /// </summary>
    class HullEdge
    {
        public int V0, V1;

        public HullEdge NextEdge;
        public HullEdge PrevEdge;

        public HullEdge()
        {
            V0 = -1;
            V1 = -1;
        }

        public void SetNext(HullEdge Next)
        {
            if (NextEdge != null)
                NextEdge.PrevEdge = null;

            NextEdge = Next;

            if (NextEdge != null)
                Next.PrevEdge = this;
        }

        public void SetPrev(HullEdge Prev)
        {
            if (PrevEdge != null)
                PrevEdge.NextEdge = null;

            PrevEdge = Prev;

            if (PrevEdge != null)
                Prev.NextEdge = this;
        }
    }
}
