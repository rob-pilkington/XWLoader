using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PolygonCutter
{
    /// <summary>
    /// The Cutter static class performs the work of cutting up a polygon and then winding the triangles.
    /// </summary>
    static class Cutter
    {
        public static bool ReportErrors = true;
        public static bool ReportMessages = true;
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
            if ((p - a).Len() < 0.0001f)
                return normalu;
            if ((p - b).Len() < 0.0001f)
                return normalv;
            if ((p - c).Len() < 0.0001f)
                return normalw;

            float u, v, w;
            Barycentric(p, a, b, c, out u, out v, out w);

            return new Vector3(normalu.x * u + normalv.x * v + normalw.x * w, normalu.y * u + normalv.y * v + normalw.y * w, normalu.z * u + normalv.z * v + normalw.z * w).Normalise();
        }

        private static int GetMeshIndex(Vector3 NewPoint, Vector3 NewNormal, List<PositionAndNormal> v3MeshPoly)
        {
            int V1Match = v3MeshPoly.FindIndex(x => x.Pos == NewPoint && x.Normal == NewNormal);
            if (V1Match < 0)
            {
                V1Match = v3MeshPoly.FindIndex(x => (x.Pos - NewPoint).Len() < 0.001f && (x.Normal - NewNormal).Len() < 0.001f);
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
        private static int FoundReEntryPoint(List<int> ReEntryPoints, List<PositionAndNormal> v3MeshPoly, Vector3 vStart, Vector3 vEnd, bool FindFirst = true)
        {
            Vector3Line v3Line = Vector3Line.FromVector3(vStart, vEnd);
            float LineLen = v3Line.Dir().Len();

            float BestDistance = FindFirst ? 2f : -2f;
            int BestPoint = -1;

            for (int i = 0; i < ReEntryPoints.Count; i++)
            {
                Vector3 vCandidate = v3MeshPoly[ReEntryPoints[i]].Pos;

                // Find distance along this line to get closest point to vCandidate
                float t = Vector3.Dot(v3Line.Dir(), (vCandidate - v3Line.Origin())) / Vector3.Dot(v3Line.Dir(), v3Line.Dir());
                if (t >= -0.001f && t <= 1.001f)    // Allow a slight bit of tolerance because the GetMesh function has a small degree of snapping
                {
                    Vector3 Rejoin = v3Line.Origin() + v3Line.Dir() * t;
                    float Distance = (Rejoin - vCandidate).Len();
                    if (Distance < 0.001f && ((FindFirst && t < BestDistance) || (!FindFirst && t > BestDistance)))
                    {
                        BestDistance = t;
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

        public static List<HullEdge> CutterPerimeter(CookieCutter tCut, Triangle tBase, Plane pTri, List<PositionAndNormal> v3MeshPoly, bool CullExternalTriangles)
        {
            int iEdge = tCut.FirstExternalEdge();

            Vector3 vHit = tCut.GetStashedEdgeLine(iEdge).IntersectPlane(pTri);
            Vector3 vHitNormal = BarycentricNormal(vHit, v3MeshPoly[tBase.VertexIndex[0]].Pos, v3MeshPoly[tBase.VertexIndex[1]].Pos, v3MeshPoly[tBase.VertexIndex[2]].Pos, v3MeshPoly[tBase.VertexIndex[0]].Normal, v3MeshPoly[tBase.VertexIndex[1]].Normal, v3MeshPoly[tBase.VertexIndex[2]].Normal);

            List<int> lstHits = new List<int>();
            HashSet<int> EdgeEncountered = new HashSet<int>();

            int MeshIndex = GetMeshIndex(vHit, vHitNormal, v3MeshPoly);
            while (!EdgeEncountered.Contains(iEdge))
            {
                lstHits.Add(MeshIndex);
                EdgeEncountered.Add(iEdge);
                
                iEdge = CullExternalTriangles ? tCut.FindNextEdge(tCut.EdgeIndex[iEdge]) : tCut.FindPreviousEdge(tCut.EdgeIndex[iEdge]);    // # of the triangle edge which is next/previous
                vHit = tCut.GetStashedEdgeLine(iEdge).IntersectPlane(pTri);

                vHitNormal = BarycentricNormal(vHit, v3MeshPoly[tBase.VertexIndex[0]].Pos, v3MeshPoly[tBase.VertexIndex[1]].Pos, v3MeshPoly[tBase.VertexIndex[2]].Pos, v3MeshPoly[tBase.VertexIndex[0]].Normal, v3MeshPoly[tBase.VertexIndex[1]].Normal, v3MeshPoly[tBase.VertexIndex[2]].Normal);
                MeshIndex = GetMeshIndex(vHit, vHitNormal, v3MeshPoly);

                iEdge = tCut.EdgeIndex[iEdge];  // Get the indexed edge #
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

        public static List<Triangle> RemoveEmptySliverTriangles(List<Triangle> ChopTriangles, List<PositionAndNormal> v3MeshPoly)
        {
            int iChopped = 0;

            for (int i = 0; i < ChopTriangles.Count;)
            {
                Triangle ChopTriangle = ChopTriangles[i];
                // First check if the cookie cutter is entirely contained within our triangle, as this is a special case with no edge cuts.
                Vector3 V0 = v3MeshPoly[ChopTriangle.VertexIndex[0]].Pos;
                Vector3 V1 = v3MeshPoly[ChopTriangle.VertexIndex[1]].Pos;
                Vector3 V2 = v3MeshPoly[ChopTriangle.VertexIndex[2]].Pos;

                Plane pTri = Plane.FromTriangle(V0, V1, V2);

                if (float.IsNaN(pTri.a) || float.IsNaN(pTri.b) || float.IsNaN(pTri.c) || float.IsNaN(pTri.d))
                {
                    iChopped++;
                    ChopTriangles.RemoveAt(i);
                } else
                    i++;
            }

            if (iChopped > 0 && ReportMessages)
                UnityEngine.Debug.Log($"Removed {iChopped} empty triangles");

            return ChopTriangles;
        }

        private static List<HullEdge> TriangleToHullEdges(Triangle ChopTriangle, List<PositionAndNormal> v3MeshPoly)
        {
            Vector3 V0 = v3MeshPoly[ChopTriangle.VertexIndex[0]].Pos;
            Vector3 V1 = v3MeshPoly[ChopTriangle.VertexIndex[1]].Pos;
            Vector3 V2 = v3MeshPoly[ChopTriangle.VertexIndex[2]].Pos;
            Vector3 N0 = v3MeshPoly[ChopTriangle.VertexIndex[0]].Normal;
            Vector3 N1 = v3MeshPoly[ChopTriangle.VertexIndex[1]].Normal;
            Vector3 N2 = v3MeshPoly[ChopTriangle.VertexIndex[2]].Normal;
            Plane pTri = Plane.FromTriangle(V0, V1, V2);

            // This triangle is an empty sliver, no result is possible here
            if (float.IsNaN(pTri.a) || float.IsNaN(pTri.b) || float.IsNaN(pTri.c) || float.IsNaN(pTri.d))
                return new List<HullEdge>();

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

            return lstHullValidate;
        }

        private static void CutHullWithCutter(Vector3 V0, Vector3 V1, Vector3 V2, Vector3 N0, Vector3 N1, Vector3 N2, List<HullEdge> lstHullValidate, CookieCutter tCut, Plane pTri, List<PositionAndNormal> v3MeshPoly, List<int> CutVerts)
        {
            // We now have a connected series of edges forming a hull
            // Apply any cuts to the border

            if (lstHullValidate.Count == 0 && ReportErrors)
            {
                UnityEngine.Debug.LogError("Hull did not have any edges to cut");
                return;
            }

            HullEdge heCurrent = lstHullValidate[0];
            int FirstPoint = heCurrent.V0;

            // Do 1 entire loop, getting the border cuts, 1 after the next
            tCut.ResetPreviousClipEdges();  // Reset the edge clipped stored data
            Vector3 vS = v3MeshPoly[heCurrent.V0].Pos;
            bool bCutSpecial = false;

            int TooLongVert = heCurrent.V1;
            int TooLongTime = -1;

            HashSet<int> BoundaryCutterEdges = new HashSet<int>();
            HashSet<int> OverlaidVertEdges = new HashSet<int>();

            int CutTotal = 0;
            do
            {
                bCutSpecial = false;

                Vector3 vE = v3MeshPoly[heCurrent.V1].Pos;

                Vector3Line v3Line = new Vector3Line(vS, vE);
                float LineLength = v3Line.Dir().Len();

                int FaceCutIndex;
                Vector3 CutPoint = tCut.FirstEdgeAgainstCutter(vS, vE, pTri, out FaceCutIndex);

                TooLongTime++;

                int FoundEdge = tCut.FindNearEdge(vE, pTri);    // Find the boundary edge using V1 as a start

                if (CutPoint != null)
                {
                    // Break up this edge, move to the new segment
                    Vector3 CutNormal = BarycentricNormal(CutPoint, V0, V1, V2, N0, N1, N2);
                    CutTotal++;

                    int VNew = GetMeshIndex(CutPoint, CutNormal, v3MeshPoly);
                    
                    // These is a vertex produced from a cut
                    if (!CutVerts.Contains(VNew))
                        CutVerts.Add(VNew);
                    
                    FoundEdge = tCut.FindNearEdge(v3MeshPoly[VNew].Pos, pTri);  // Overwrite the boundary edge using the new point VNew
                    if (FoundEdge != -1)
                    {
                        if (!BoundaryCutterEdges.Contains(FoundEdge))
                            BoundaryCutterEdges.Add(FoundEdge);

                        CutTotal--;
                    }
                    else
                        tCut.FaceCuts[FaceCutIndex]++;  // We have cut this face and it's not a boundary cut

                    if (VNew == heCurrent.V1)
                    {
                        // Sometimes we get a triangle which cuts almost exactly on a vertex, but which is rounded to the vertex
                        // Don't add in a line segment of zero length, just move to next
                        heCurrent = heCurrent.NextEdge;
                        if (!OverlaidVertEdges.Contains(VNew))
                            OverlaidVertEdges.Add(VNew);
                        TooLongVert = heCurrent.V1;
                        TooLongTime = -1;
                        CutTotal--;
                    }
                    else if (VNew == heCurrent.V0)
                    {
                        // Don't adjust the heCurrent, we want the same face just find the next clip point
                        if (!OverlaidVertEdges.Contains(VNew))
                            OverlaidVertEdges.Add(VNew);

                        bCutSpecial = true;
                        CutTotal--;
                    }
                    else
                    {
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

                        TooLongVert = heCurrent.V1;
                        TooLongTime = -1;
                    }

                    // The new start pos is the previous cut pos
                    vS = CutPoint;
                }
                else
                {
                    // If this vert is on a cutter face and it's not a cutter edge, this is a cut we can find later
                    if (FoundEdge == -1 && tCut.VertexOnCutFace(vE, pTri) && !CutVerts.Contains(heCurrent.V1))
                        CutVerts.Add(heCurrent.V1);

                    // Sometimes we don't cut a boundary point but it's sat right on the end vert and we need to recognise that it is a potential discontinuity
                    if (FoundEdge != -1)
                    {
                        if (!BoundaryCutterEdges.Contains(FoundEdge))
                            BoundaryCutterEdges.Add(FoundEdge);
                    }

                    // There was no cut to this segment, move to the next line
                    heCurrent = heCurrent.NextEdge;
                    tCut.ResetPreviousClipEdges();  // Reset the edge clipped stored data
                    vS = v3MeshPoly[heCurrent.V0].Pos;

                    TooLongVert = heCurrent.V1;
                    TooLongTime = -1;
                }

                if (TooLongTime > 2)
                {
                    tCut.ResetFaceCuts();
                    tCut.ResetPreviousClipEdges();
                    if (ReportErrors)
                        UnityEngine.Debug.LogError("ChopPolygon could not find the next edge to cut");
                    lstHullValidate.Clear();
                    return;
                }
            } while (heCurrent.V0 != FirstPoint || bCutSpecial);
        }

        private static int FindConstrainedVert(Vector3 vStart, Vector3 vEnd, List<PositionAndNormal> v3MeshPoly, List<int> CutVerts, int IgnoreIndex)
        {
            int Result = -1;
            float ClosestD = 10000f;

            foreach (int i in CutVerts)
            {
                if (i == IgnoreIndex)
                    continue;

                Vector3 vCut = v3MeshPoly[i].Pos;

                Plane pStartCap = Plane.FromOriginAndNormal(vStart, vEnd - vStart);
                Plane pEndCap = Plane.FromOriginAndNormal(vEnd, vStart - vEnd);

                float StartVal = pStartCap.PointValue(vCut);
                float EndVal = pEndCap.PointValue(vCut);

                if (StartVal < -0.001f || EndVal < -0.001f)
                    continue;   // Not within the lines

                if (StartVal >= ClosestD)
                    continue;

                Vector3 vFound = vStart + (vEnd - vStart).Normalise() * StartVal;

                float D = (vFound - vCut).Len();
                if (D < 0.001f)
                {
                    ClosestD = StartVal;
                    Result = i;
                }
            }

            return Result;
        }

        private static Dictionary<HullEdge, List<int>> FitCutsToEdges(List<HullEdge> lstHullValidate, List<PositionAndNormal> v3MeshPoly, List<int> CutVerts)
        {
            Dictionary<HullEdge, List<int>> result = new Dictionary<HullEdge, List<int>>();

            Dictionary<int, HullEdge> map = new Dictionary<int, HullEdge>();    // Each edge should map to 1 HullEdge really well

            // Each cut vert should map to 1 edge, and 1 edge only
            foreach (int i in CutVerts)
            {
                float ClosestD = 100000f;
                HullEdge Closest = null;
                for (int j = 0; j < 6; j++)
                {
                    float tolerance = 0.00000001f * (float)Math.Pow(10.0, j);

                    foreach (HullEdge he in lstHullValidate)
                    {
                        Vector3 vCut = v3MeshPoly[i].Pos;

                        Vector3 vStart = v3MeshPoly[he.V0].Pos;
                        Vector3 vEnd = v3MeshPoly[he.V1].Pos;

                        Plane pStartCap = Plane.FromOriginAndNormal(vStart, vEnd - vStart);
                        Plane pEndCap = Plane.FromOriginAndNormal(vEnd, vStart - vEnd);

                        float StartVal = pStartCap.PointValue(vCut);
                        float EndVal = pEndCap.PointValue(vCut);

                        if (StartVal < 0f || EndVal < 0f)
                            continue;   // Not within the lines

                        Vector3 vFound = vStart + (vEnd - vStart).Normalise() * StartVal;

                        float D = (vFound - vCut).Len();
                        if (D < tolerance)
                        {
                            ClosestD = StartVal;
                            Closest = he;
                        }
                    }

                    if (Closest != null) 
                        break;
                }

                if (Closest != null)
                    map.Add(i, Closest);
            }

            // Map to nice list
            foreach (var v in map)
            {
                if (!result.ContainsKey(v.Value))
                    result.Add(v.Value, new List<int>());

                result[v.Value].Add(v.Key);
            }

            // Now make sure every hulledge is in the dictionary even if it's a empty list
            foreach (var he in lstHullValidate)
            {
                if (!result.ContainsKey(he))
                    result.Add(he, new List<int>());
            }

            return result;
        }

        private static void CutHullWithCutterConstrained(Vector3 V0, Vector3 V1, Vector3 V2, Vector3 N0, Vector3 N1, Vector3 N2, List<HullEdge> lstHullValidate, List<PositionAndNormal> v3MeshPoly, List<int> CutVerts)
        {
            // We now have a connected series of edges forming a hull
            // Apply any cuts to the border

            if (lstHullValidate.Count == 0)
            {
                if (ReportErrors)
                    UnityEngine.Debug.LogError("Hull did not have any edges to cut");
                return;
            }

            HullEdge heCurrent = lstHullValidate[0];
            int FirstPoint = heCurrent.V0;

            var hullCuts = FitCutsToEdges(lstHullValidate, v3MeshPoly, CutVerts);

            // Do 1 entire loop, getting the border cuts, 1 after the next
            Vector3 vS = v3MeshPoly[heCurrent.V0].Pos;
            bool bCutSpecial = false;
            var currentCuts = hullCuts[heCurrent];

            int TooLongVert = heCurrent.V1;
            int TooLongTime = -1;

            HashSet<int> BoundaryCutterEdges = new HashSet<int>();
            HashSet<int> OverlaidVertEdges = new HashSet<int>();

            int CutTotal = 0;
            int PrevFound = -1;
            do
            {
                bCutSpecial = false;

                Vector3 vE = v3MeshPoly[heCurrent.V1].Pos;

                Vector3Line v3Line = new Vector3Line(vS, vE);
                float LineLength = v3Line.Dir().Len();

                PrevFound = FindConstrainedVert(vS, vE, v3MeshPoly, currentCuts, PrevFound);

                TooLongTime++;

                if (PrevFound != -1)
                {
                    // Break up this edge, move to the new segment
                    int VNew = PrevFound;

                    if (VNew == heCurrent.V1)
                    {
                        // Sometimes we get a triangle which cuts almost exactly on a vertex, but which is rounded to the vertex
                        // Don't add in a line segment of zero length, just move to next
                        heCurrent = heCurrent.NextEdge;
                        if (!OverlaidVertEdges.Contains(VNew))
                            OverlaidVertEdges.Add(VNew);
                        TooLongVert = heCurrent.V1;
                        TooLongTime = -1;
                        CutTotal--;

                        currentCuts = hullCuts[heCurrent];
                    }
                    else if (VNew == heCurrent.V0)
                    {
                        // Don't adjust the heCurrent, we want the same face just find the next clip point
                        if (!OverlaidVertEdges.Contains(VNew))
                            OverlaidVertEdges.Add(VNew);

                        bCutSpecial = true;
                        CutTotal--;
                    }
                    else
                    {
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

                        TooLongVert = heCurrent.V1;
                        TooLongTime = -1;
                    }

                    // The new start pos is the previous cut pos
                    vS = v3MeshPoly[VNew].Pos;
                }
                else
                {
                    // There was no cut to this segment, move to the next line
                    heCurrent = heCurrent.NextEdge;
                    vS = v3MeshPoly[heCurrent.V0].Pos;

                    TooLongVert = heCurrent.V1;
                    TooLongTime = -1;

                    currentCuts = hullCuts[heCurrent];
                }

                if (TooLongTime > 2)
                {
                    if (ReportErrors)
                        UnityEngine.Debug.LogError("ChopPolygon could not find the next edge to cut");
                    lstHullValidate.Clear();
                    return;
                }
            } while (heCurrent.V0 != FirstPoint || bCutSpecial);
        }

        private static List<HullEdge> FilterDeadHullEdges(List<HullEdge> lstHullValidate, List<PositionAndNormal> v3MeshPoly, CookieCutter tCut, bool CullExternalEdges)
        {
            Dictionary<HullEdge, bool> dctHullEdgeDead = new Dictionary<HullEdge, bool>();

            foreach (HullEdge he in lstHullValidate)
            {
                Vector3 lineMid = (v3MeshPoly[he.V0].Pos + v3MeshPoly[he.V1].Pos) / 2f;
                bool bDead = tCut.TestVertex(lineMid);

                dctHullEdgeDead.Add(he, bDead ^ CullExternalEdges);
            }

            List<HullEdge> lstPreFinal = new List<HullEdge>();
            List<int> ReEntryPoints = new List<int>();

            foreach (HullEdge he in lstHullValidate)
            {
                if (dctHullEdgeDead[he])
                {
                    // This segment is dead, don't add it into the final list

                    if (!dctHullEdgeDead[he.NextEdge])
                        ReEntryPoints.Add(he.V1);
                }
            }

            foreach (HullEdge he in lstHullValidate)
            {
                if (dctHullEdgeDead[he])
                {
                    // This segment is dead, don't add it into the final list
                    he.SetPrev(null);
                    he.SetNext(null);
                }
                else
                {
                    // This segment is alive, add it into the final list
                    lstPreFinal.Add(he);
                }
            }
            return lstPreFinal;
        }

        private static bool MatchInHulls(List<HullEdge> BaseHull, List<HullEdge> CutHull)
        {
            HashSet<int> hsBase = new HashSet<int>();

            foreach (HullEdge he in BaseHull)
            {
                if (!hsBase.Contains(he.V0))
                    hsBase.Add(he.V0);
                if (!hsBase.Contains(he.V1))
                    hsBase.Add(he.V1);
            }

            foreach (HullEdge he in CutHull)
            {
                if (hsBase.Contains(he.V0))
                    return true;
                if (hsBase.Contains(he.V1))
                    return true;
            }

            return false;
        }

        private static void RemoveZeroAreaHullLoops(List<HullEdge> lstHull)
        {
            for (int i = 0; i < lstHull.Count; i++)
            {
                for (int j = i + 1; j < lstHull.Count; j++)
                {
                    if (lstHull[i].V0 == lstHull[j].V1 && lstHull[i].V1 == lstHull[j].V0)
                    {
                        lstHull.RemoveAt(j);
                        lstHull.RemoveAt(i);
                        i--;
                        break;
                    }
                }
            }
        }

        public static List<Triangle> ChopPolygon(Triangle ChopTriangle, CookieCutter tCut, List<PositionAndNormal> v3MeshPoly, bool CullExternalTriangles = false)
        {
            // First check if the cookie cutter is entirely contained within our triangle, as this is a special case with no edge cuts.
            Vector3 V0 = v3MeshPoly[ChopTriangle.VertexIndex[0]].Pos;
            Vector3 V1 = v3MeshPoly[ChopTriangle.VertexIndex[1]].Pos;
            Vector3 V2 = v3MeshPoly[ChopTriangle.VertexIndex[2]].Pos;
            Vector3 N0 = v3MeshPoly[ChopTriangle.VertexIndex[0]].Normal;
            Vector3 N1 = v3MeshPoly[ChopTriangle.VertexIndex[1]].Normal;
            Vector3 N2 = v3MeshPoly[ChopTriangle.VertexIndex[2]].Normal;
            Plane pTri = Plane.FromTriangle(V0, V1, V2);

            if (float.IsNaN(pTri.a) || float.IsNaN(pTri.b) || float.IsNaN(pTri.c) || float.IsNaN(pTri.d))
                return new List<Triangle>();    // Sliver triangle with zero area, impossible to carve
            
            bool ReversedCutter = false;

            // We can't cut a face which is facing the wrong way because _maths_
            if (Vector3.Dot(tCut.Edges[0].Dir(), pTri.Normal()) < 0f)
            {
                // The cutter has been built using a backwards face normal, the final list of hulledges will need flipped
                ReversedCutter = true;
            }

            tCut.StashFaces(V0, V1, V2, CullExternalTriangles);    // Make sure we perform any cuts which lie exactly along an edge correctly
            tCut.ResetPreviousClipEdges();  // Reset the edge clipped stored data

            // Get the hull edges from the original triangle
            List<HullEdge> lstUncutTriEdges = TriangleToHullEdges(ChopTriangle, v3MeshPoly);

            // Build a cutter from the original triangle
            CookieCutter tCutTri = CookieCutter.MakeCC(new List<Vector3>() { V0, V1, V2 }, ChopTriangle.FaceNormal);

            // Get the hull edges from the cutter
            List<HullEdge> lstUncutCutterEdges = CutterPerimeter(tCut, ChopTriangle, pTri, v3MeshPoly, CullExternalTriangles);

            // Now chop the hull with the cutter
            List<int> TriNewVerts = new List<int>();
            CutHullWithCutter(V0, V1, V2, N0, N1, N2, lstUncutTriEdges, tCut, pTri, v3MeshPoly, TriNewVerts);

            // Now chop the cutter hull with the triangle
            List<int> CutterNewVerts = new List<int>();
            CutHullWithCutterConstrained(V0, V1, V2, N0, N1, N2, lstUncutCutterEdges, v3MeshPoly, TriNewVerts);

            // Now mark dead hull edges
            List<HullEdge> FinalTriHullEdges = FilterDeadHullEdges(lstUncutTriEdges, v3MeshPoly, tCut, CullExternalTriangles);

            // Now mark dead hull edges
            List<HullEdge> FinalCutterHullEdges = FilterDeadHullEdges(lstUncutCutterEdges, v3MeshPoly, tCutTri, true);

            if (ReversedCutter)
            {
                foreach(var flipHE in FinalCutterHullEdges)
                {
                    int newV1 = flipHE.V0;
                    int newV0 = flipHE.V1;

                    flipHE.V0 = newV0;
                    flipHE.V1 = newV1;
                }
            }

            tCut.UnstashFaces();
            tCut.ResetPreviousClipEdges();
            tCut.ResetFaceCuts();

            RemoveZeroAreaHullLoops(FinalTriHullEdges);
            RemoveZeroAreaHullLoops(FinalCutterHullEdges);

            if (!MatchInHulls(FinalTriHullEdges, FinalCutterHullEdges))
            {
                if (!CullExternalTriangles)
                {
                    if (FinalCutterHullEdges.Count == 0)
                    {
                        if (FinalTriHullEdges.Count == 0)
                            return new List<Triangle>();

                        // Sometimes the cutter just pokes a new vertex into existence without doing any area cuts - in this case just wind the new triangles and solve (in case we have removed a zero area portion)
                        return RemoveEmptySliverTriangles(SolveGapsAndWindEdges(FinalTriHullEdges, FinalCutterHullEdges, v3MeshPoly, ChopTriangle.FaceNormal, CullExternalTriangles), v3MeshPoly);
                    }

                    return ChopTriangleEnclosed(v3MeshPoly, ChopTriangle.FaceNormal, FinalTriHullEdges, FinalCutterHullEdges);
                } else
                {
                    if (FinalCutterHullEdges.Count == 0)
                        return new List<Triangle>();

                    HullEdge heStart = FinalCutterHullEdges[0];
                    FinalCutterHullEdges.Clear();
                    FinalCutterHullEdges.Add(heStart);
                    return WindEdges(FinalCutterHullEdges, v3MeshPoly, ChopTriangle.FaceNormal);
                }
            }

            // Finally solve, wind and remove empty

            return RemoveEmptySliverTriangles( SolveGapsAndWindEdges(FinalTriHullEdges, FinalCutterHullEdges, v3MeshPoly, ChopTriangle.FaceNormal, CullExternalTriangles), v3MeshPoly);
        }

        private static List<Triangle> ChopTriangleEnclosed(List<PositionAndNormal> v3MeshPoly, Vector3 FaceNormal, List<HullEdge> lstHullValidate, List<HullEdge> CutterHullEdges)
        {

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
                    IgnoreVerts[2] = CutterHullEdges[j].V0;
                    IgnoreVerts[3] = CutterHullEdges[j].V1;

                    // This forms a quad vTri0, vTri1, vCut0 & vCut0, vCut1, vTri0
                    // Check that both of these triangles don't enclose any other points and that they are wound correctly

                    bool WindingA = true;
                    bool WindingB = true;

                    float Winding = Vector3.Dot(Plane.FromTriangle(vTri0, vTri1, vCut0).Normal(), FaceNormal);
                    if (Winding <= 0f)
                        WindingA = false;

                    Winding = Vector3.Dot(Plane.FromTriangle(vCut0, vCut1, vTri0).Normal(), FaceNormal);
                    if (Winding <= 0f)
                        WindingA = false;

                    Winding = Vector3.Dot(Plane.FromTriangle(vTri0, vTri1, vCut1).Normal(), FaceNormal);
                    if (Winding <= 0f)
                        WindingB = false;

                    Winding = Vector3.Dot(Plane.FromTriangle(vCut0, vCut1, vTri1).Normal(), FaceNormal);
                    if (Winding <= 0f)
                        WindingB = false;

                    if (!WindingA && !WindingB)
                        continue;

                    bool ValidTriangle = true;

                    for (int k = 0; k < QuadValidVerts.Count; k++)
                    {
                        if (IgnoreVerts.Contains(QuadValidVerts[k]))
                            continue;

                        Vector3 vCheck = v3MeshPoly[QuadValidVerts[k]].Pos;
                        Vector3 vTri0p = vTri0 + FaceNormal;
                        Vector3 vTri1p = vTri1 + FaceNormal;
                        Vector3 vCut0p = vCut0 + FaceNormal;

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

                        Vector3 vCut0p = vCut0 + FaceNormal;
                        Vector3 vCut1p = vCut1 + FaceNormal;
                        Vector3 vTri0p = vTri0 + FaceNormal;

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

                    // We now have a valid quad

                    List<Triangle> lstReturn = new List<Triangle>();

                    //Add the quad to the return
                    if (WindingA)
                    {
                        lstReturn.Add(new Triangle(lstHullValidate[i].V0, lstHullValidate[i].V1, CutterHullEdges[j].V0, FaceNormal));
                        lstReturn.Add(new Triangle(CutterHullEdges[j].V0, CutterHullEdges[j].V1, lstHullValidate[i].V0, FaceNormal));
                    } else
                    {
                        lstReturn.Add(new Triangle(lstHullValidate[i].V0, lstHullValidate[i].V1, CutterHullEdges[j].V1, FaceNormal));
                        lstReturn.Add(new Triangle(CutterHullEdges[j].V0, CutterHullEdges[j].V1, lstHullValidate[i].V1, FaceNormal));
                    }

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
                    lstReturn.AddRange(WindEdges(CutterHullEdges, v3MeshPoly, FaceNormal));

                    return RemoveEmptySliverTriangles(lstReturn, v3MeshPoly);
                }
            }

            if (ReportErrors)
                UnityEngine.Debug.LogError("Unable to successfully cut mesh - could not form quad");

            List<Triangle> lstDump = new List<Triangle>();
            return lstDump;
        }

        private static List<Triangle> SolveGapsAndWindEdges(List<HullEdge> FinalShapeEdges, List<HullEdge> FinalCutterEdges, List<PositionAndNormal> v3MeshPoly, Vector3 FaceNormal, bool Intersection)
        {

            List<HullEdge> DebugShape = new List<HullEdge>(FinalShapeEdges);
            List<HullEdge> DebugCutter = new List<HullEdge>(FinalCutterEdges);

            Dictionary<int, HullEdge> dctShapeEntries = new Dictionary<int, HullEdge>();
            Dictionary<int, HullEdge> dctCutterEntries = new Dictionary<int, HullEdge>();

            foreach(HullEdge he in FinalShapeEdges)
            {
                if (!dctShapeEntries.ContainsKey(he.V0))
                    dctShapeEntries.Add(he.V0, he);

                he.NextEdge = null;
                he.PrevEdge = null;
            }

            foreach (HullEdge he in FinalCutterEdges)
            {
                int PointV0 = he.V0;
                int PointV1 = he.V1;

                if (!dctCutterEntries.ContainsKey(he.V0))
                    dctCutterEntries.Add(he.V0, he);

                he.NextEdge = null;
                he.PrevEdge = null;
            }

            List<HullEdge> TriangleLoops = new List<HullEdge>();

            // Breakout counter incase we manage to generate some loops which will not solve
            int Breakout = 0;
            while (FinalShapeEdges.Count + FinalCutterEdges.Count > 0)
            {
                if (FinalShapeEdges.Count == 0)
                {
                    break;  // Occasionally we get very slight overlap with extra edges alive in the cutter hull
                }

                HullEdge heStart = FinalShapeEdges[0];
                HullEdge heCurrent = heStart;
                bool OnCutterEdges = false;

                TriangleLoops.Add(heStart);

                do
                {
                    int FindPoint = heCurrent.V1;
                    HullEdge heNew = null;

                    if (!OnCutterEdges)
                    {
                        // Cutter edge takes priority
                        if (dctCutterEntries.ContainsKey(FindPoint))
                        {
                            OnCutterEdges = true;
                            heNew = dctCutterEntries[FindPoint];

                            dctCutterEntries.Remove(FindPoint);
                            FinalCutterEdges.Remove(heNew);

                        }
                        else if (dctShapeEntries.ContainsKey(FindPoint))
                        {
                            OnCutterEdges = false;
                            heNew = dctShapeEntries[FindPoint];

                            dctShapeEntries.Remove(FindPoint);
                            FinalShapeEdges.Remove(heNew);

                        }
                        else
                        {
                            if (ReportErrors)
                                UnityEngine.Debug.LogError("Invalid shape");
                            return new List<Triangle>();
                        }
                    }
                    else
                    {
                        // Shape edges take priority
                        if (dctShapeEntries.ContainsKey(FindPoint))
                        {
                            OnCutterEdges = false;
                            heNew = dctShapeEntries[FindPoint];

                            dctShapeEntries.Remove(FindPoint);
                            FinalShapeEdges.Remove(heNew);

                        }
                        else if (dctCutterEntries.ContainsKey(FindPoint))
                        {
                            OnCutterEdges = true;
                            heNew = dctCutterEntries[FindPoint];

                            dctCutterEntries.Remove(FindPoint);
                            FinalCutterEdges.Remove(heNew);

                        }
                        else
                        {
                            if (ReportErrors)
                                UnityEngine.Debug.LogError("Invalid shape");
                            return new List<Triangle>();
                        }
                    }

                    heCurrent.SetNext(heNew);
                    heNew.SetPrev(heCurrent);

                    heCurrent = heNew;

                    
                } while (heCurrent != heStart);
                
                Breakout++;

                if (Breakout > 100)
                {
                    if(ReportErrors)
                        UnityEngine.Debug.LogError("Infinite loop matey");
                    return new List<Triangle>();
                }
            }

            return WindEdges(TriangleLoops, v3MeshPoly, FaceNormal);
        }

        public static List<Vector3> Triangulate(List<UnityEngine.Vector3> PolyPoints, Vector3 FaceNormal)
        {
            List<PositionAndNormal> v3MeshPoly = new List<PositionAndNormal>();
            Dictionary<Vector3, int> VectorIndex = new Dictionary<Vector3, int>();

            for (int i = 0; i < PolyPoints.Count; i++)
            {
                // There exists a condition where the marking verts can contain the same point twice in a row
                // Which marks fine if you're drawing a polyline on screen using GDI
                // but we can't triangulate this, it's garbage.

                int iNext = i + 1;
                if (iNext >= PolyPoints.Count)
                    iNext = 0;

                if (PolyPoints[i] == PolyPoints[iNext])
                {
                    PolyPoints.RemoveAt(iNext);
                    i--;
                    if (ReportErrors)
                        UnityEngine.Debug.Log("Removed duplicate marking point");
                }
            }

            foreach (var pp in PolyPoints)
            {
                Vector3 newpp = new Vector3(pp.x, pp.y, pp.z);
                if (!VectorIndex.ContainsKey(newpp))
                {
                    VectorIndex.Add(newpp, v3MeshPoly.Count);
                    v3MeshPoly.Add(new PositionAndNormal(newpp, FaceNormal));
                }
            }

            List<int> EdgePoints = new List<int>();
            foreach (var pp in PolyPoints)
            {
                Vector3 newpp = new Vector3(pp.x, pp.y, pp.z);
                EdgePoints.Add(VectorIndex[newpp]);
            }

            List<HullEdge> lstHullValidate = BuildHullEdges();
            
            List<HullEdge> BuildHullEdges()
            {
                List<HullEdge> result2 = new List<HullEdge>();
                for (int i = 0; i < EdgePoints.Count; i++)
                {
                    int ic = i;
                    int ni = ic + 1;
                    int pi = ic - 1;

                    if (ni >= EdgePoints.Count)
                        ni = 0;
                    if (pi < 0)
                        pi = EdgePoints.Count - 1;

                    HullEdge he = new HullEdge();
                    he.V0 = EdgePoints[ic];
                    he.V1 = EdgePoints[ni];

                    result2.Add(he);
                }

                // Now link up the hull edges

                for (int i = 0; i < EdgePoints.Count; i++)
                {
                    int ni = i + 1;
                    int pi = i - 1;

                    if (ni >= EdgePoints.Count)
                        ni = 0;
                    if (pi < 0)
                        pi = EdgePoints.Count - 1;

                    result2[i].NextEdge = result2[ni];
                    result2[i].PrevEdge = result2[pi];
                }

                HullEdge heFirst = result2[0];
                result2.Clear();
                result2.Add(heFirst);

                return result2;
            }

            bool bOldReport = ReportErrors;
            ReportErrors = false;
            var lstTri = WindEdges(lstHullValidate, v3MeshPoly, FaceNormal);

            if (lstTri.Count < PolyPoints.Count - 2)
            {
                lstHullValidate = BuildHullEdges();
                FaceNormal = -FaceNormal;
                lstTri = WindEdges(lstHullValidate, v3MeshPoly, FaceNormal);
            }

            ReportErrors = bOldReport;

            List<Vector3> result = new List<Vector3>();
            foreach (var t in lstTri)
            {
                result.Add(v3MeshPoly[t.VertexIndex[0]].Pos);
                result.Add(v3MeshPoly[t.VertexIndex[1]].Pos);
                result.Add(v3MeshPoly[t.VertexIndex[2]].Pos);
            }

            return result;
        }

        private static List<Triangle> WindEdges(List<HullEdge> FinalLoops, List<PositionAndNormal> v3MeshPoly, Vector3 FaceNormal)
        {
            // Create a convex hull from this loop of edges and points. For now, just do a 012, 023, 034, 045 etc. it won't be convex but it's a proof of concept
            List<Triangle> lstReturn = new List<Triangle>();
            foreach (HullEdge he in FinalLoops)
            {
                List<HullEdge> LoopEdges = new List<HullEdge>();
                HullEdge heStartEdge = he;
                HullEdge heLoopEdge = he;

                int InfiniteLoop = 0;
                do
                {
                    if (heLoopEdge == null)
                    {
                        if (ReportErrors)
                            UnityEngine.Debug.LogError("Winding Hull Loop had null segment");
                        return lstReturn;
                    }
                    LoopEdges.Add(heLoopEdge);
                    heLoopEdge = heLoopEdge.NextEdge;
                    InfiniteLoop++;

                    if (InfiniteLoop > 100)
                        return new List<Triangle>();
                } while (heLoopEdge != heStartEdge);

                List<int> UnassignedVertex = new List<int>();
                for (int i = 0; i < LoopEdges.Count; i++)
                    UnassignedVertex.Add(LoopEdges[i].V0);  // Verts which we can't enclose

                while (LoopEdges.Count > 0)
                {
                    float LargestArea = 10f;
                    int LargestAreaIndex = -1;

                    for (int sigma = 0; sigma < 5; sigma++)
                    {
                        float fSigma = 0.0001f - sigma * 0.00005f;

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

                                
                                if (P01.PointValue(vCheck) < fSigma && P12.PointValue(vCheck) < fSigma && P20.PointValue(vCheck) < fSigma)
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

                        if (LargestAreaIndex != -1)
                            break;
                    }

                    // Add the triangle which ad the biggest area - try to avoid slivers.
                    if (LargestAreaIndex == -1)
                    {
                        if (ReportErrors)
                            UnityEngine.Debug.LogError("Could not find an edge which was not invalid???");
                        return lstReturn;
                    }

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

                        lstReturn.Add(new Triangle(VertIndex[0], VertIndex[1], VertIndex[2], FaceNormal));

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

                            if (VertIndex[0] == VertIndex[2] && ReportErrors)
                            {
                                UnityEngine.Debug.LogError("Zero length hull edge");
                            }

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

        public static List<Triangle> WindEdges(List<HullEdge> FinalLoops, List<Vector3> v3MeshPoly, Vector3 FaceNormal)
        {
            // Create a convex hull from this loop of edges and points. For now, just do a 012, 023, 034, 045 etc. it won't be convex but it's a proof of concept
            List<Triangle> lstReturn = new List<Triangle>();
            foreach (HullEdge he in FinalLoops)
            {
                List<HullEdge> LoopEdges = new List<HullEdge>();
                HullEdge heStartEdge = he;
                HullEdge heLoopEdge = he;

                int InfiniteLoop = 0;
                do
                {
                    if (heLoopEdge == null)
                    {
                        if (ReportErrors)
                            UnityEngine.Debug.LogError("Winding Hull Loop had null segment");
                        return lstReturn;
                    }
                    LoopEdges.Add(heLoopEdge);
                    heLoopEdge = heLoopEdge.NextEdge;
                    InfiniteLoop++;

                    if (InfiniteLoop > 100)
                        return new List<Triangle>();
                } while (heLoopEdge != heStartEdge);

                List<int> UnassignedVertex = new List<int>();
                for (int i = 0; i < LoopEdges.Count; i++)
                    UnassignedVertex.Add(LoopEdges[i].V0);  // Verts which we can't enclose

                while (LoopEdges.Count > 0)
                {
                    float LargestArea = 10f;
                    int LargestAreaIndex = -1;

                    for (int sigma = 0; sigma < 5; sigma++)
                    {
                        float fSigma = 0.0001f - sigma * 0.00005f;

                        for (int i = 0; i < LoopEdges.Count; i++)
                        {
                            // Check triangle i, i+1, i+2.
                            int[] VertIndex = new int[3];

                            heLoopEdge = LoopEdges[i];
                            Vector3 NV0 = v3MeshPoly[heLoopEdge.V0];
                            VertIndex[0] = heLoopEdge.V0;
                            heLoopEdge = heLoopEdge.NextEdge;

                            Vector3 NV1 = v3MeshPoly[heLoopEdge.V0];
                            VertIndex[1] = heLoopEdge.V0;

                            Vector3 NV2 = v3MeshPoly[heLoopEdge.V1];
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

                                Vector3 vCheck = v3MeshPoly[UnassignedVertex[j]];
                                Vector3 NV0p = NV0 + FaceNormal;
                                Vector3 NV1p = NV1 + FaceNormal;
                                Vector3 NV2p = NV2 + FaceNormal;

                                Plane P01 = Plane.FromTriangle(NV0, NV1, NV1p);
                                Plane P12 = Plane.FromTriangle(NV1, NV2, NV2p);
                                Plane P20 = Plane.FromTriangle(NV2, NV0, NV0p);


                                if (P01.PointValue(vCheck) < fSigma && P12.PointValue(vCheck) < fSigma && P20.PointValue(vCheck) < fSigma)
                                {
                                    ValidTriangle = false;
                                    break;
                                }
                            }

                            if (!ValidTriangle)
                                continue;


                            // How close to 60 degrees is this triangle? Avoid slivers
                            float Area = Math.Abs(1.047f - (float)Math.Acos(Vector3.Dot((v3MeshPoly[VertIndex[1]] - v3MeshPoly[VertIndex[0]]).Normalise(), (v3MeshPoly[VertIndex[2]] - v3MeshPoly[VertIndex[0]]).Normalise())));
                            // The closer to 0, the closer to an equilateral triangle we are

                            if (Area < LargestArea)
                            {
                                LargestArea = Area;
                                LargestAreaIndex = i;
                            }
                        }

                        if (LargestAreaIndex != -1)
                            break;
                    }

                    // Add the triangle which ad the biggest area - try to avoid slivers.
                    if (LargestAreaIndex == -1)
                    {
                        if (ReportErrors)
                            UnityEngine.Debug.LogError("Could not find an edge which was not invalid???");
                        return lstReturn;
                    }

                    {
                        int i = LargestAreaIndex;
                        int[] VertIndex = new int[3];

                        heLoopEdge = LoopEdges[i];
                        Vector3 NV0 = v3MeshPoly[heLoopEdge.V0];
                        VertIndex[0] = heLoopEdge.V0;
                        heLoopEdge = heLoopEdge.NextEdge;

                        Vector3 NV1 = v3MeshPoly[heLoopEdge.V0];
                        VertIndex[1] = heLoopEdge.V0;

                        Vector3 NV2 = v3MeshPoly[heLoopEdge.V1];
                        VertIndex[2] = heLoopEdge.V1;

                        HullEdge hePrev = LoopEdges[i].PrevEdge;
                        HullEdge heNext = heLoopEdge.NextEdge;

                        lstReturn.Add(new Triangle(VertIndex[0], VertIndex[1], VertIndex[2], FaceNormal));

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

                            if (VertIndex[0] == VertIndex[2] && ReportErrors)
                            {
                                UnityEngine.Debug.LogError("Zero length hull edge");
                            }

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

                    Plane Face = new Plane(v3MeshData[t.VertexIndex[v]].Pos, v3MeshData[t.VertexIndex[nextv]].Pos, v3MeshData[t.VertexIndex[prevv]].Pos);

                    Vector3 N0 = v3MeshData[t.VertexIndex[v]].Normal.Normalise();
                    Vector3 N1 = v3MeshData[t.VertexIndex[nextv]].Normal.Normalise();
                    Vector3 N2 = v3MeshData[t.VertexIndex[prevv]].Normal.Normalise();

                    // If the face normal and the vertex normal aren't within 60 degrees of each other, change the normal to be the average of the current + face
                    if (Vector3.Dot(Face.Normal(), N0) < 0.5f)
                        v3MeshData[t.VertexIndex[v]].Normal = (N0 + Face.Normal()).Normalise();

                    if (Vector3.Dot(Face.Normal(), N1) < 0.5f)
                        v3MeshData[t.VertexIndex[nextv]].Normal = (N1 + Face.Normal()).Normalise();

                    if (Vector3.Dot(Face.Normal(), N2) < 0.5f)
                        v3MeshData[t.VertexIndex[prevv]].Normal = (N2 + Face.Normal()).Normalise();
                }
            }
        }

        /// <summary>
        /// Finds vertices between triangles which can be removed
        /// </summary>
        /// <param name="Tris"></param>
        /// <param name="v3MeshData"></param>
        public static int RemoveUselessPoints(List<Triangle> Tris, List<PositionAndNormal> v3MeshData, float Straightness = 0.9999f)
        {
           int iPointsRemoved = 0;

            bool bRemoved = false;
            do
            {
                bRemoved = false;
                for (int i = 0; i < Tris.Count; i++)
                {
                    List<int> Vertices = new List<int>(Tris[i].VertexIndex);

                    for (int j = i + 1; j < Tris.Count; j++)
                    {
                        List<int> OrigVerts = new List<int>(Vertices);
                        List<int> NewVerts = new List<int>(Tris[j].VertexIndex);

                        var JointEdges = OrigVerts.Intersect(NewVerts);

                        if (JointEdges.Count() == 2)
                        {
                            int OrigPoint = OrigVerts.Except(NewVerts).First();
                            int NewPoint = NewVerts.Except(OrigVerts).First();

                            Vector3 vCandidate0 = v3MeshData[JointEdges.First()].Pos;
                            float Closeness0 = Vector3.Dot((vCandidate0 - v3MeshData[OrigPoint].Pos).Normalise(), (v3MeshData[NewPoint].Pos - vCandidate0).Normalise());

                            Vector3 vCandidate1 = v3MeshData[JointEdges.Last()].Pos;
                            float Closeness1 = Vector3.Dot((vCandidate1 - v3MeshData[OrigPoint].Pos).Normalise(), (v3MeshData[NewPoint].Pos - vCandidate1).Normalise());

                            if (Closeness0 >= Straightness)
                            {
                                bRemoved = true;

                                // Found a triangle that can be removed
                                int ChangeIndex = OrigVerts.IndexOf(JointEdges.First());
                                Tris[i].VertexIndex[ChangeIndex] = NewPoint;

                                iPointsRemoved++;
                                Tris.RemoveAt(j);
                                i--;
                                break;
                            }
                            else if (Closeness1 >= Straightness)
                            {
                                bRemoved = true;
                                // Found a triangle that can be removed
                                int ChangeIndex = OrigVerts.IndexOf(JointEdges.Last());
                                Tris[i].VertexIndex[ChangeIndex] = NewPoint;

                                iPointsRemoved++;
                                Tris.RemoveAt(j);
                                i--;
                                break;
                            }
                        }
                    }
                }
            } while (bRemoved);

            return iPointsRemoved;
        }
    }
}
