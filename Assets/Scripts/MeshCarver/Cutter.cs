using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PolygonCutter
{
    /// <summary>
    /// The Cutter static class performs the work of cutting up a polygon and then winding the triangles.
    /// ChopPolygonCheck - Test to see if a triangle will be cut by the CookieCutter
    /// ChopPolygon - Chops up the triangle into a new list of triangles formed by the operation
    /// GetMarkTriangles - After carving, returns the marking triangles list formed by the operation.
    /// SanitizeNormals - Checks all triangle edges for bad normals and adds in a new vertex with a valid (face) normal where the |normal| would become 0.
    /// </summary>
    static class Cutter
    {
        public static void Barycentric(Vector3 p, Vector3 a, Vector3 b, Vector3 c, out float u, out float v, out float w)
        {
            Vector3 v0 = b - a, v1 = c - a, v2 = p - a;
            float d00 = Vector3.Dot(v0, v0);
            float d01 = Vector3.Dot(v0, v1);
            float d11 = Vector3.Dot(v1, v1);
            float d20 = Vector3.Dot(v2, v0);
            float d21 = Vector3.Dot(v2, v1);
            float denom = d00 * d11 - d01 * d01;
            v = (d11 * d20 - d01 * d21) / denom;
            w = (d00 * d21 - d01 * d20) / denom;
            u = 1.0f - v - w;
        }

        public static Vector3 BarycentricNormal(float u, float v, float w, Vector3 normalu, Vector3 normalv, Vector3 normalw)
        {
            return new Vector3(normalu.x * u + normalv.x * v + normalw.x * w, normalu.y * u + normalv.y * v + normalw.y * w, normalu.z * u + normalv.z * v + normalw.z * w).Normalise();
        }

        public static Vector3 BarycentricNormal(Vector3 p, Vector3 a, Vector3 b, Vector3 c, Vector3 normalu, Vector3 normalv, Vector3 normalw)
        {
            float u, v, w;
            Barycentric(p, a, b, c, out u, out v, out w);

            return new Vector3(normalu.x * u + normalv.x * v + normalw.x * w, normalu.y * u + normalv.y * v + normalw.y * w, normalu.z * u + normalv.z * v + normalw.z * w).Normalise();
        }

        private static int GetMeshIndex(Vector3 NewPoint, Vector3 NewNormal, List<PositionAndNormal> v3MeshPoly)
        {
            int V1Match = v3MeshPoly.FindIndex(x => x.Pos == NewPoint && x.Normal == NewNormal);
            if (V1Match < 0)
            {
                V1Match = v3MeshPoly.FindIndex(x => (x.Pos - NewPoint).Len() < 0.0001f && (x.Normal - NewNormal).Len() < 0.0001f);
                if (V1Match < 0)
                {
                    V1Match = v3MeshPoly.Count;
                    v3MeshPoly.Add(new PositionAndNormal(NewPoint, NewNormal));
                }
            }

            return V1Match;
        }

        /// <summary>
        /// Finds the ReEntry point, if the line passes close enough to one.
        /// </summary>
        /// <param name="ReEntryPoints"></param>
        /// <param name="v3MeshPoly"></param>
        /// <param name="vStart"></param>
        /// <param name="vEnd"></param>
        /// <returns></returns>
        private static int FoundReEntryPoint(List<int> ReEntryPoints, List<PositionAndNormal> v3MeshPoly, Vector3 vStart, Vector3 vEnd)
        {
            Vector3Line v3Line = Vector3Line.FromVector3(vStart, vEnd);
            float LineLen = v3Line.Dir().Len();

            float BestDistance = 0.001f;
            int BestPoint = -1;

            for (int i = 0; i < ReEntryPoints.Count; i++)
            {
                Vector3 vCandidate = v3MeshPoly[ReEntryPoints[i]].Pos;

                // Find distance along this line to get closest point to vCandidate
                float t = Vector3.Dot(v3Line.Dir(), (vCandidate - v3Line.Origin())) / Vector3.Dot(v3Line.Dir(), v3Line.Dir());
                if (t >= 0f && t <= 1f)
                {
                    Vector3 Rejoin = v3Line.Origin() + v3Line.Dir() * t;
                    float Distance = (Rejoin - vCandidate).Len();
                    if (Distance < BestDistance)
                    {
                        BestDistance = Distance;
                        BestPoint = i;
                    }
                }

            }
            return BestPoint;
        }

        public static bool ChopPolygonCheck(List<PositionAndNormal> Verts, Triangle tBase, CookieCutter tCut)
        {
            // Check to see if the planes cut any of our lines between 0 and 1
            Vector3 V0 = Verts[tBase.VertexIndex[0]].Pos;
            Vector3 V1 = Verts[tBase.VertexIndex[1]].Pos;
            Vector3 V2 = Verts[tBase.VertexIndex[2]].Pos;

            Plane pTri = Plane.FromTriangle(V0, V1, V2);

            Vector3Line Edge01 = Vector3Line.FromVector3(V0, V1);
            Vector3Line Edge12 = Vector3Line.FromVector3(V1, V2);
            Vector3Line Edge20 = Vector3Line.FromVector3(V2, V0);

            float Edge01Len = Edge01.Dir().Len();
            float Edge12Len = Edge12.Dir().Len();
            float Edge20Len = Edge20.Dir().Len();

            if (tCut.TestEdgeAgainstCutter(Edge01, Edge01Len))
                return true;

            if (tCut.TestEdgeAgainstCutter(Edge12, Edge12Len))
                return true;

            if (tCut.TestEdgeAgainstCutter(Edge20, Edge20Len))
                return true;



            // Are any of the cutter "verts" going to intersect our tri plane?
            if (tCut.EdgesWithinTriangle(V0, V1, V2))
                return true;

            if (tCut.TestVertex(V0) && tCut.TestVertex(V1) && tCut.TestVertex(V2))
                return true;

            return false;
        }

        private static List<HullEdge> CutterPerimeter(CookieCutter tCut, Triangle tBase, Plane pTri, List<PositionAndNormal> v3MeshPoly)
        {
            int iEdge = tCut.FirstExternalEdge();

            Vector3 vHit = tCut.Edges[iEdge].IntersectPlane(pTri);
            Vector3 vHitNormal = BarycentricNormal(vHit, v3MeshPoly[tBase.VertexIndex[0]].Pos, v3MeshPoly[tBase.VertexIndex[1]].Pos, v3MeshPoly[tBase.VertexIndex[2]].Pos, v3MeshPoly[tBase.VertexIndex[0]].Normal, v3MeshPoly[tBase.VertexIndex[1]].Normal, v3MeshPoly[tBase.VertexIndex[2]].Normal);

            List<int> lstHits = new List<int>();

            int MeshIndex = GetMeshIndex(vHit, vHitNormal, v3MeshPoly);
            while (!lstHits.Contains(MeshIndex))
            {
                lstHits.Add(MeshIndex);

                vHit = tCut.NextSegment(vHit, pTri);
                vHitNormal = BarycentricNormal(vHit, v3MeshPoly[tBase.VertexIndex[0]].Pos, v3MeshPoly[tBase.VertexIndex[1]].Pos, v3MeshPoly[tBase.VertexIndex[2]].Pos, v3MeshPoly[tBase.VertexIndex[0]].Normal, v3MeshPoly[tBase.VertexIndex[1]].Normal, v3MeshPoly[tBase.VertexIndex[2]].Normal);
                MeshIndex = GetMeshIndex(vHit, vHitNormal, v3MeshPoly);
            }

            List<HullEdge> hullEdges = new List<HullEdge>();

            // From the list of vertices
            for (int i = 0; i < lstHits.Count ; i++)
            {
                int ic = i;
                int ni = ic + 1;
                int pi = ic - 1;

                if (ni > lstHits.Count - 1)
                    ni = 0;
                if (pi < 0)
                    pi = lstHits.Count;

                HullEdge he = new HullEdge();
                he.V0 = lstHits[ic];
                he.V1 = lstHits[ni];

                hullEdges.Add(he);
            }

            for (int i = 0; i < lstHits.Count; i++)
            {
                int ni = i + 1;
                int pi = i - 1;

                if (ni > lstHits.Count - 1)
                    ni = 0;
                if (pi < 0)
                    pi = lstHits.Count - 1;

                hullEdges[i].NextEdge = hullEdges[ni];
                hullEdges[i].PrevEdge = hullEdges[pi];
            }

            return hullEdges;
        }

        public static List<Triangle> ChopPolygon(Triangle ChopTriangle, CookieCutter tCut, List<PositionAndNormal> v3MeshPoly)
        {
            // First check if the cookie cutter is entirely contained within our triangle, as this is a special case with no edge cuts.
            Vector3 V0 = v3MeshPoly[ChopTriangle.VertexIndex[0]].Pos;
            Vector3 V1 = v3MeshPoly[ChopTriangle.VertexIndex[1]].Pos;
            Vector3 V2 = v3MeshPoly[ChopTriangle.VertexIndex[2]].Pos;
            Vector3 N0 = v3MeshPoly[ChopTriangle.VertexIndex[0]].Normal;
            Vector3 N1 = v3MeshPoly[ChopTriangle.VertexIndex[1]].Normal;
            Vector3 N2 = v3MeshPoly[ChopTriangle.VertexIndex[2]].Normal;
            Plane pTri = Plane.FromTriangle(V0, V1, V2);

            tCut.StashFaces(V0, V1, V2);    // Make sure we perform any cuts which lie exactly along an edge correctly

            // Check each vertex and see if is alive or dead. If it is culled then loop around the intersection of the triangle and the cookie cutter looking for any external plane cuts to begin a new triangle
            List<HullEdge> lstHullValidate = new List<HullEdge>();

            for (int i = 0; i < 3; i++)
            {
                int ic = i;
                int ni = ic + 1;
                int pi = ic - 1;

                if (ni > 2)
                    ni = 0;
                if (pi < 0)
                    pi = 2;

                HullEdge he = new HullEdge();
                he.V0 = ChopTriangle.VertexIndex[ic];
                he.V1 = ChopTriangle.VertexIndex[ni];

                lstHullValidate.Add(he);
            }

            // Now link up the hull edges

            for (int i = 0; i < 3; i++)
            {
                int ni = i + 1;
                int pi = i - 1;

                if (ni > 2)
                    ni = 0;
                if (pi < 0)
                    pi = 2;

                lstHullValidate[i].NextEdge = lstHullValidate[ni];
                lstHullValidate[i].PrevEdge = lstHullValidate[pi];
            }

            // Handle the special case where the two hulls never meet and we need to force the start by finding a valid quad
            if (tCut.AllEdgesWithinTriangle(V0, V1, V2))
            {
                List<HullEdge> CutterHullEdges = CutterPerimeter(tCut, ChopTriangle, pTri, v3MeshPoly);

                List<int> QuadValidVerts = new List<int>();
                for (int i = 0; i < CutterHullEdges.Count; i++)
                    QuadValidVerts.Add(CutterHullEdges[i].V0);  // Verts which we can't enclose

                // Find a segment in the original triangle and a segment in the cutter edges which forms a quad containing no other points

                for (int i = 0; i < lstHullValidate.Count; i++)
                {
                    for (int j = 0; j < CutterHullEdges.Count; j++)
                    {
                        // Tri Verts
                        Vector3 vTri0 = v3MeshPoly[lstHullValidate[i].V0].Pos;
                        Vector3 vTri1 = v3MeshPoly[lstHullValidate[i].V1].Pos;

                        // Cutter Verts
                        Vector3 vCut0 = v3MeshPoly[CutterHullEdges[j].V0].Pos;
                        Vector3 vCut1 = v3MeshPoly[CutterHullEdges[j].V1].Pos;

                        int[] IgnoreVerts = new int[4];
                        IgnoreVerts[0] = lstHullValidate[i].V0;
                        IgnoreVerts[1] = lstHullValidate[i].V1;
                        IgnoreVerts[2] = CutterHullEdges[i].V0;
                        IgnoreVerts[3] = CutterHullEdges[i].V1;

                        // This forms a quad vTri0, vTri1, vCut0 & vCut0, vCut1, vTri0
                        // Check that both of these triangles don't enclose any other points and that they are wound correctly

                        float Winding = Vector3.Dot(Plane.FromTriangle(vTri0, vTri1, vCut0).Normal(), pTri.Normal());
                        if (Winding <= 0f)
                            continue;

                        Winding = Vector3.Dot(Plane.FromTriangle(vCut0, vCut1, vTri0).Normal(), pTri.Normal());
                        if (Winding <= 0f)
                            continue;

                        Winding = Vector3.Dot(Plane.FromTriangle(vTri0, vTri1, vCut1).Normal(), pTri.Normal());
                        if (Winding <= 0f)
                            continue;

                        Winding = Vector3.Dot(Plane.FromTriangle(vCut0, vCut1, vTri1).Normal(), pTri.Normal());
                        if (Winding <= 0f)
                            continue;

                        bool ValidTriangle = true;

                        for (int k = 0; k < QuadValidVerts.Count; k++)
                        {
                            if (IgnoreVerts.Contains(QuadValidVerts[k]))
                                continue;

                            Vector3 vCheck = v3MeshPoly[QuadValidVerts[k]].Pos;
                            Vector3 vTri0p = vTri0 + pTri.Normal();
                            Vector3 vTri1p = vTri1 + pTri.Normal();
                            Vector3 vCut0p = vCut0 + pTri.Normal();

                            Plane P01 = Plane.FromTriangle(vTri0, vTri1, vTri1p);
                            Plane P12 = Plane.FromTriangle(vTri1, vCut0, vCut0p);
                            Plane P20 = Plane.FromTriangle(vCut0, vTri0, vTri0p);

                            if (P01.PointValue(vCheck) < 0f && P12.PointValue(vCheck) < 0f && P20.PointValue(vCheck) < 0f)
                            {
                                ValidTriangle = false;
                                break;
                            }
                        }

                        if (!ValidTriangle)
                            continue;

                        for (int k = 0; k < QuadValidVerts.Count; k++)
                        {
                            if (IgnoreVerts.Contains(QuadValidVerts[k]))
                                continue;

                            Vector3 vCheck = v3MeshPoly[QuadValidVerts[k]].Pos;

                            Vector3 vCut0p = vCut0 + pTri.Normal();
                            Vector3 vCut1p = vCut1 + pTri.Normal();
                            Vector3 vTri0p = vTri0 + pTri.Normal();

                            Plane P01 = Plane.FromTriangle(vCut0, vCut1, vCut1p);
                            Plane P12 = Plane.FromTriangle(vCut1, vTri0, vTri0p);
                            Plane P20 = Plane.FromTriangle(vTri0, vCut0, vCut0p);

                            if (P01.PointValue(vCheck) < 0f && P12.PointValue(vCheck) < 0f && P20.PointValue(vCheck) < 0f)
                            {
                                ValidTriangle = false;
                                break;
                            }
                        }

                        if (!ValidTriangle)
                            continue;

                        tCut.UnstashFaces();

                        // We now have a valid quad

                        List<Triangle> lstReturn = new List<Triangle>();

                        //Add the quad to the return
                        lstReturn.Add(new Triangle(lstHullValidate[i].V0, lstHullValidate[i].V1, CutterHullEdges[j].V0, ChopTriangle.Colour));
                        lstReturn.Add(new Triangle(CutterHullEdges[j].V0, CutterHullEdges[j].V1, lstHullValidate[i].V0, ChopTriangle.Colour));

                        // The cutter perimiter and the triangle perimeter need to be joined together using 2 new HullEdges
                        HullEdge hullEdge = new HullEdge();
                        hullEdge.V0 = lstHullValidate[i].V0;
                        hullEdge.V1 = CutterHullEdges[j].V1;

                        CutterHullEdges.Add(hullEdge);

                        hullEdge.SetPrev(lstHullValidate[i].PrevEdge);
                        hullEdge.SetNext(CutterHullEdges[j].NextEdge);

                        hullEdge = new HullEdge();
                        hullEdge.V0 = CutterHullEdges[j].V0;
                        hullEdge.V1 = lstHullValidate[i].V1;

                        CutterHullEdges.Add(hullEdge);

                        hullEdge.SetPrev(CutterHullEdges[j].PrevEdge);
                        hullEdge.SetNext(lstHullValidate[i].NextEdge);

                        CutterHullEdges.RemoveAt(j);    // Done

                        lstHullValidate.RemoveAt(i);    // Done

                        CutterHullEdges.AddRange(lstHullValidate);

                        HullEdge heFirst = CutterHullEdges[0];
                        CutterHullEdges.Clear();
                        CutterHullEdges.Add(heFirst);   // Single loop
                        lstReturn.AddRange(WindEdges(CutterHullEdges, v3MeshPoly, pTri.Normal(), ChopTriangle.Colour));

                        return lstReturn;
                    }
                }

                Console.WriteLine("Unable to successfully cut mesh - could not form quad");

                tCut.UnstashFaces();

                List<Triangle> lstDump = new List<Triangle>();
                lstDump.Add(ChopTriangle);
                return lstDump;
            }

            // We now have a connected series of edges forming a hull
            // Apply any cuts to the border
            HullEdge heCurrent = lstHullValidate[0];
            int FirstPoint = heCurrent.V0;

            // Do 1 entire loop, getting the border cuts, 1 after the next
            do
            {
                Vector3 vS = v3MeshPoly[heCurrent.V0].Pos;
                Vector3 vE = v3MeshPoly[heCurrent.V1].Pos;

                Vector3Line v3Line = new Vector3Line(vS, vE);
                float LineLength = v3Line.Dir().Len();

                Vector3 CutPoint = tCut.FirstEdgeAgainstCutter(v3Line, LineLength);

                if (CutPoint != null)
                {
                    // Break up this edge, move to the new segment
                    Vector3 CutNormal = BarycentricNormal(CutPoint, V0, V1, V2, N0, N1, N2);

                    int VNew = GetMeshIndex(CutPoint, CutNormal, v3MeshPoly);

                    // The current segment V1 is replaced by the new cut point, and a new segment is placed into the list

                    HullEdge heNew = new HullEdge();
                    heNew.V0 = VNew;
                    heNew.V1 = heCurrent.V1;

                    // Add the edge into the list
                    lstHullValidate.Add(heNew);

                    // Update the current segment
                    heCurrent.V1 = VNew;

                    HullEdge heNext = heCurrent.NextEdge;

                    // Unlink the current P/N link
                    heCurrent.SetNext(heNew);   // Removes the old next (also updates next.Prev)
                    heNew.SetNext(heNext);  // Add the new next (also updates current.next)

                    // Move to the next segment
                    heCurrent = heNew;

                }
                else
                {
                    // There was no cut to this segment, move to the next line
                    heCurrent = heCurrent.NextEdge;
                }
            } while (heCurrent.V0 != FirstPoint);

            // At this point we have chopped the edges into valid and invalid edges.
            // Go through the list to find valid edges, and then follow these around to get a winding

            List<HullEdge> lstPreFinal = new List<HullEdge>();
            List<int> ReEntryPoints = new List<int>();

            foreach (HullEdge he in lstHullValidate)
            {
                Vector3 lineMid = (v3MeshPoly[he.V0].Pos + v3MeshPoly[he.V1].Pos) / 2f;
                bool bDead = tCut.TestVertex(lineMid);

                if (bDead)
                {
                    // This segment is dead, don't add it into the final list
                    he.SetPrev(null);

                    if (he.NextEdge != null)
                        ReEntryPoints.Add(he.V1);

                    he.SetNext(null);
                }
                else
                {
                    // This segment is alive, add it into the final list
                    lstPreFinal.Add(he);
                }
            }

            return SolveGapsAndWindEdges(tCut, ChopTriangle, lstPreFinal, v3MeshPoly, ReEntryPoints);
        }

        private static List<Triangle> SolveGapsAndWindEdges(CookieCutter tCut, Triangle ChopTriangle, List<HullEdge> lstPreFinal, List<PositionAndNormal> v3MeshPoly, List<int> ReEntryPoints)
        {
            Vector3 V0 = v3MeshPoly[ChopTriangle.VertexIndex[0]].Pos;
            Vector3 V1 = v3MeshPoly[ChopTriangle.VertexIndex[1]].Pos;
            Vector3 V2 = v3MeshPoly[ChopTriangle.VertexIndex[2]].Pos;
            Vector3 N0 = v3MeshPoly[ChopTriangle.VertexIndex[0]].Normal;
            Vector3 N1 = v3MeshPoly[ChopTriangle.VertexIndex[1]].Normal;
            Vector3 N2 = v3MeshPoly[ChopTriangle.VertexIndex[2]].Normal;
            Plane pTri = Plane.FromTriangle(V0, V1, V2);

            // Now go through the lstFinal edges and complete the loops
            List<HullEdge> FinalLoops = new List<HullEdge>();
            HullEdge heCurrent;
            int FirstPoint;

            while (lstPreFinal.Count > 0)
            {
                // Get the first edge segment and complete the loop.
                heCurrent = lstPreFinal[0];
                FinalLoops.Add(heCurrent);

                FirstPoint = heCurrent.V0;

                do
                {
                    lstPreFinal.Remove(heCurrent);

                    if (heCurrent.NextEdge != null)
                    {
                        // Great! Just move to the next edge.
                        heCurrent = heCurrent.NextEdge;
                    }
                    else
                    {
                        // Not great, we need to ask the cookie cutter for the next anticlockwise point 
                        Vector3 vStart = v3MeshPoly[heCurrent.V1].Pos;
                        int V0New = heCurrent.V1;
                        Vector3 vFind = v3MeshPoly[heCurrent.V1].Pos;

                        do
                        {
                            Vector3 vFound = tCut.NextSegment(vFind, pTri);
                            Vector3 vFoundNormal = BarycentricNormal(vFound, V0, V1, V2, N0, N1, N2);

                            // Check that we don't pass through any of our re-entry points, which would replace vEnd and get us back on track.
                            int iFound = FoundReEntryPoint(ReEntryPoints, v3MeshPoly, vStart, vFound);
                            if (iFound > -1)
                            {
                                HullEdge heNew = new HullEdge();
                                heNew.V0 = V0New;
                                heNew.V1 = ReEntryPoints[iFound];

                                heNew.SetPrev(heCurrent);

                                HullEdge heNext = lstPreFinal.Find(x => x.V0 == ReEntryPoints[iFound]);
                                heNew.SetNext(heNext);

                                heCurrent = heNext;
                                break;
                            }
                            else
                            {
                                // Generate a new edge
                                int V1New = GetMeshIndex(vFound, vFoundNormal, v3MeshPoly);

                                HullEdge heNew = new HullEdge();
                                heNew.V0 = V0New;
                                heNew.V1 = V1New;

                                heNew.SetPrev(heCurrent);

                                // The next start point is the current end point
                                vStart = vFound;
                                V0New = V1New;

                                // The next find point is the current end point
                                vFind = vFound;

                                heCurrent = heNew;
                            }
                        } while (true);
                    }
                } while (heCurrent.V0 != FirstPoint);
            }

            tCut.UnstashFaces();
            return WindEdges(FinalLoops, v3MeshPoly, pTri.Normal(), ChopTriangle.Colour);
            
        }

        private static List<Triangle> WindEdges(List<HullEdge> FinalLoops, List<PositionAndNormal> v3MeshPoly, Vector3 FaceNormal, int FaceColour)
        {
            // Create a convex hull from this loop of edges and points. For now, just do a 012, 023, 034, 045 etc. it won't be convex but it's a proof of concept
            List<Triangle> lstReturn = new List<Triangle>();
            foreach (HullEdge he in FinalLoops)
            {
                List<HullEdge> LoopEdges = new List<HullEdge>();
                HullEdge heStartEdge = he;
                HullEdge heLoopEdge = he;

                do
                {
                    LoopEdges.Add(heLoopEdge);
                    heLoopEdge = heLoopEdge.NextEdge;
                } while (heLoopEdge != heStartEdge);

                List<int> UnassignedVertex = new List<int>();
                for (int i = 0; i < LoopEdges.Count; i++)
                    UnassignedVertex.Add(LoopEdges[i].V0);  // Verts which we can't enclose

                while (LoopEdges.Count > 0)
                {
                    float LargestArea = 10f;
                    int LargestAreaIndex = -1;

                    for (int i = 0; i < LoopEdges.Count; i++)
                    {
                        // Check triangle i, i+1, i+2.
                        int[] VertIndex = new int[3];

                        heLoopEdge = LoopEdges[i];
                        Vector3 NV0 = v3MeshPoly[heLoopEdge.V0].Pos;
                        VertIndex[0] = heLoopEdge.V0;
                        heLoopEdge = heLoopEdge.NextEdge;

                        Vector3 NV1 = v3MeshPoly[heLoopEdge.V0].Pos;
                        VertIndex[1] = heLoopEdge.V0;

                        Vector3 NV2 = v3MeshPoly[heLoopEdge.V1].Pos;
                        VertIndex[2] = heLoopEdge.V1;

                        HullEdge hePrev = LoopEdges[i].PrevEdge;
                        HullEdge heNext = heLoopEdge.NextEdge;

                        // The normal.pTri.Normal MUST be positive (same winding direction).
                        float Winding = Vector3.Dot(Plane.FromTriangle(NV0, NV1, NV2).Normal(), FaceNormal);
                        if (Winding <= 0f)
                            continue;

                        // And the resulting triangle cannot contain any other verts (excluding i, i+1, i+2)
                        bool ValidTriangle = true;

                        for (int j = 0; j < UnassignedVertex.Count; j++)
                        {
                            if (VertIndex.Contains(UnassignedVertex[j]))
                                continue;

                            Vector3 vCheck = v3MeshPoly[UnassignedVertex[j]].Pos;
                            Vector3 NV0p = NV0 + FaceNormal;
                            Vector3 NV1p = NV1 + FaceNormal;
                            Vector3 NV2p = NV2 + FaceNormal;

                            Plane P01 = Plane.FromTriangle(NV0, NV1, NV1p);
                            Plane P12 = Plane.FromTriangle(NV1, NV2, NV2p);
                            Plane P20 = Plane.FromTriangle(NV2, NV0, NV0p);

                            if (P01.PointValue(vCheck) < 0.0f && P12.PointValue(vCheck) < 0.0f && P20.PointValue(vCheck) < 0.0f)
                            {
                                ValidTriangle = false;
                                break;
                            }
                        }

                        if (!ValidTriangle)
                            continue;


                        // How close to 60 degrees is this triangle? Avoid slivers
                        float Area = Math.Abs(1.047f - (float)Math.Acos(Vector3.Dot((v3MeshPoly[VertIndex[1]].Pos - v3MeshPoly[VertIndex[0]].Pos).Normalise(), (v3MeshPoly[VertIndex[2]].Pos - v3MeshPoly[VertIndex[0]].Pos).Normalise())));
                        // The closer to 0, the closer to an equilateral triangle we are

                        if (Area < LargestArea)
                        {
                            LargestArea = Area;
                            LargestAreaIndex = i;
                        }
                    }

                    // Add the triangle which ad the biggest area - try to avoid slivers.
                    {
                        int i = LargestAreaIndex;
                        int[] VertIndex = new int[3];

                        heLoopEdge = LoopEdges[i];
                        Vector3 NV0 = v3MeshPoly[heLoopEdge.V0].Pos;
                        VertIndex[0] = heLoopEdge.V0;
                        heLoopEdge = heLoopEdge.NextEdge;

                        Vector3 NV1 = v3MeshPoly[heLoopEdge.V0].Pos;
                        VertIndex[1] = heLoopEdge.V0;

                        Vector3 NV2 = v3MeshPoly[heLoopEdge.V1].Pos;
                        VertIndex[2] = heLoopEdge.V1;

                        HullEdge hePrev = LoopEdges[i].PrevEdge;
                        HullEdge heNext = heLoopEdge.NextEdge;

                        lstReturn.Add(new Triangle(VertIndex[0], VertIndex[1], VertIndex[2], FaceColour));

                        heLoopEdge = LoopEdges[i];

                        LoopEdges.Remove(heLoopEdge);

                        heLoopEdge = heLoopEdge.NextEdge;

                        LoopEdges.Remove(heLoopEdge);

                        // Finally, if the next and the previous edge are the same, we should be done, remove the edge from the LoopEdges
                        // Otherwise add in a new loop edge and connect it up to the Loop properly
                        if (hePrev == heNext)
                        {
                            LoopEdges.Remove(hePrev);
                        }
                        else
                        {
                            HullEdge hullEdge = new HullEdge();
                            hullEdge.V0 = VertIndex[0];
                            hullEdge.V1 = VertIndex[2];

                            hullEdge.SetPrev(hePrev);
                            hullEdge.SetNext(heNext);

                            LoopEdges.Add(hullEdge);
                        }
                    }
                }
            }

            return lstReturn;
        }

        public static void SnapPoints(List<Vector3> vCutPoints, List<PositionAndNormal> v3MeshPoly, float SnapDistance)
        {
            // Go through every point in the v3MeshPoly against every Cut Point. If the distance is less than the snap, adjust.

            foreach (Vector3 vCut in vCutPoints)
            {
                for (int i = 0; i < v3MeshPoly.Count; i++)
                {
                    Vector3 vTest = v3MeshPoly[i].Pos;

                    float D = (vTest - vCut).Len();

                    if (D == 0f)
                        continue;   // Identical, no need to snap again

                    if (D < SnapDistance)
                    {
                        v3MeshPoly[i].Pos = new Vector3(vCut.x, vCut.y, vCut.z);
                    }
                }
            }
        }

        /// <summary>
        /// Breaks up triangle edges which would result in a normal of Zero and adds in a new point with the face normal
        /// </summary>
        /// <param name="tris"></param>
        /// <param name="v3MeshData"></param>
        public static void SanitizeNormals(List<Triangle> tris, List<PositionAndNormal> v3MeshData)
        {
            // inspect every tri and if it has an edge which would have a zero normal, break up the triangle

            for (int i = 0; i < tris.Count; i++)
            {
                Triangle t = tris[i];

                for (int v = 0; v < 3; v++)
                {
                    int nextv = v + 1;
                    int prevv = v - 1;
                    if (nextv > 2)
                        nextv = 0;
                    if (prevv < 0)
                        prevv = 2;

                    Vector3 N0 = v3MeshData[t.VertexIndex[v]].Normal.Normalise();
                    Vector3 N1 = -v3MeshData[t.VertexIndex[nextv]].Normal.Normalise();

                    // Get the normalized N0 and N1 and dot them - if they equal 0 then these normals are opposite and the line needs breaking up.
                    if (Vector3.Dot(N0, N1) == 0f)
                    {
                        float aLen = v3MeshData[t.VertexIndex[v]].Normal.Len();
                        float bLen = v3MeshData[t.VertexIndex[nextv]].Normal.Len();

                        Vector3 v3New = Vector3.lerp(v3MeshData[t.VertexIndex[v]].Pos, v3MeshData[t.VertexIndex[nextv]].Pos, aLen / (aLen + bLen));

                        Plane Face = new Plane(v3MeshData[t.VertexIndex[v]].Pos, v3MeshData[t.VertexIndex[nextv]].Pos, v3MeshData[t.VertexIndex[prevv]].Pos);

                        int NewIndex = GetMeshIndex(v3New, Face.Normal(), v3MeshData);

                        int OldNext = t.VertexIndex[nextv];

                        t.VertexIndex[nextv] = NewIndex;    // Replace the old nextv with our new entry

                        Triangle tNew = new Triangle(t.VertexIndex[prevv], NewIndex, t.VertexIndex[nextv], t.Colour);
                        tris.Add(tNew);
                    }
                }
            }
        }

        /// <summary>
        /// Returns the mark points which have been cut into the parent polygon triangles
        /// </summary>
        /// <param name="v3MeshData"></param>
        /// <param name="Cut">The cookie cutter used to cut the parent triangles</param>
        /// <param name="FaceNormal">The approximate markings normal</param>
        /// <param name="FaceColour">The colour index of any new triangles generated</param>
        /// <returns></returns>
        public static List<Triangle> GetMarkTriangles(List<PositionAndNormal> v3MeshData, CookieCutter Cut, Vector3 FaceNormal, int FaceColour)
        {
            List<Triangle> result = new List<Triangle>();
            List<int> verts = Cut.MarkVerts(v3MeshData);

            if (verts.Count == 0)
                return result;

            List<HullEdge> lstHullValidate = new List<HullEdge>();

            for (int i = 0; i < verts.Count; i++)
            {
                int ic = i;
                int ni = ic + 1;
                int pi = ic - 1;

                if (ni > verts.Count - 1)
                    ni = 0;
                if (pi < 0)
                    pi = verts.Count - 1;

                HullEdge he = new HullEdge();
                he.V0 = verts[ic];
                he.V1 = verts[ni];

                lstHullValidate.Add(he);
            }

            // Now link up the hull edges

            for (int i = 0; i < verts.Count; i++)
            {
                int ni = i + 1;
                int pi = i - 1;

                if (ni > verts.Count - 1)
                    ni = 0;
                if (pi < 0)
                    pi = verts.Count - 1;

                lstHullValidate[i].NextEdge = lstHullValidate[ni];
                lstHullValidate[i].PrevEdge = lstHullValidate[pi];
            }

            HullEdge heFirst = lstHullValidate[0];
            lstHullValidate.Clear();
            lstHullValidate.Add(heFirst);   // Single loop
            result.AddRange(WindEdges(lstHullValidate, v3MeshData, FaceNormal, FaceColour));

            return result;
        }
    }
}
