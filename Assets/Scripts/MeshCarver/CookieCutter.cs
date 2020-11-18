using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PolygonCutter
{
    /// <summary>
    /// A class which hold a 3d solid formed of connected planar (or approximately planar) triangles. Used to carve up meshes.
    /// If the triangles which make up the cookie cutter intersect then this should be broken down into 2 cookie cutters.
    /// </summary>
    class CookieCutter
    {
        /// <summary>
        /// The planes which make up this cookie cutter.
        /// </summary>
        public Plane [] Faces;
        private bool[] FaceInternal;    // Some faces are internal
        private int[] FaceOpposite;

        public Vector3Line[] Edges;
        public int[] EdgeIndex; // Each triangle has 3 line edges
        private bool[] EdgeInternal;    // Some edges are internal
        private int[] EdgeVertOriginal; // The original vertex which generated this edge

        private Plane[] StashedFaces;
        public float StashFaceThreshold = 0.01f;

        /// <summary>
        /// Returns a new cookie cutter based off the triangle and the normal along which to project the face cuts. All polygons which make up a cookie cutter must share at least 2 verts with another polygon in the triangle list
        /// </summary>
        public CookieCutter(List<Vector3> v3PolyMesh, List<Triangle> triangles, Vector3 FaceNormal)
        {
            Faces = new Plane[triangles.Count * 3];
            StashedFaces = new Plane[triangles.Count * 3];  // For dealing with cut faces which are flat up against a triangle edge

            Edges = new Vector3Line[triangles.Count * 3];   // Maximum, we might not use them all

            FaceInternal = new bool[triangles.Count * 3];
            FaceOpposite = new int[triangles.Count * 3];

            EdgeIndex = new int[triangles.Count * 3];
            EdgeInternal = new bool[triangles.Count * 3];
            EdgeVertOriginal = new int[triangles.Count * 3];

            for (int i = 0; i < EdgeVertOriginal.Length; i++)
                EdgeVertOriginal[i] = -1;   // Ensure none of these are used

            for (int i = 0; i < FaceOpposite.Length; i++)
                FaceOpposite[i] = -1;

            // Each triangle in the cookie cutter generates a prism with 3 faces and 3 edges
            for (int i = 0; i < triangles.Count; i++)
            {
                Triangle t = triangles[i];

                // Generate the 3 faces
                Vector3 V0 = v3PolyMesh[t.VertexIndex[0]];
                Vector3 V1 = v3PolyMesh[t.VertexIndex[1]];
                Vector3 V2 = v3PolyMesh[t.VertexIndex[2]];

                Vector3 V0p = V0 + FaceNormal;
                Vector3 V1p = V1 + FaceNormal;
                Vector3 V2p = V2 + FaceNormal;

                Plane Face01 = Plane.FromTriangle(V0, V1, V1p);
                Plane Face12 = Plane.FromTriangle(V1, V2, V2p);
                Plane Face20 = Plane.FromTriangle(V2, V0, V0p);

                Faces[i * 3 + 0] = Face01;
                Faces[i * 3 + 1] = Face12;
                Faces[i * 3 + 2] = Face20;

                EdgeIndex[i * 3 + 0] = FindEdge(t.VertexIndex[0], V0, V0p);
                EdgeVertOriginal[i * 3 + 0] = t.VertexIndex[0];
                EdgeIndex[i * 3 + 1] = FindEdge(t.VertexIndex[1], V1, V1p);
                EdgeVertOriginal[i * 3 + 1] = t.VertexIndex[1];

                EdgeIndex[i * 3 + 2] = FindEdge(t.VertexIndex[2], V2, V2p);
                EdgeVertOriginal[i * 3 + 2] = t.VertexIndex[2];
            }

            // Determine if a face is internal
            for (int i = 0; i < triangles.Count; i++)
            {
                Triangle t = triangles[i];

                // 3 edges - 01, 12, 20
                // If we find the opposite original verts in another triangle
                int iOpposite = FindEdgeCCW(t.VertexIndex[0], t.VertexIndex[1]);
                if (iOpposite > -1)
                {
                    FaceInternal[i * 3 + 0] = true;
                    FaceOpposite[i * 3 + 0] = i;
                }

                iOpposite = FindEdgeCCW(t.VertexIndex[1], t.VertexIndex[2]);
                if (iOpposite > -1)
                {
                    FaceInternal[i * 3 + 1] = true;
                    FaceOpposite[i * 3 + 1] = i;
                }

                iOpposite = FindEdgeCCW(t.VertexIndex[2], t.VertexIndex[0]);
                if (iOpposite > -1)
                {
                    FaceInternal[i * 3 + 2] = true;
                    FaceOpposite[i * 3 + 2] = i;
                }
            }

            // Determine if an edge is internal? Necessary?
        }

        /// <summary>
        /// Finds all prism external edges which match edges in the triangle and adjust them slightly to ensure we cut correctly
        /// </summary>
        /// <param name="V0"></param>
        /// <param name="V1"></param>
        /// <param name="V2"></param>
        public void StashFaces(Vector3 V0, Vector3 V1, Vector3 V2)
        {
            Vector3 vCentre = (V0 + V1 + V2) / 3;

            for (int i = 0; i < Faces.Length; i++)
            {
                float PV0 = Math.Abs(Faces[i].PointValue(V0));
                float PV1 = Math.Abs(Faces[i].PointValue(V1));
                float PV2 = Math.Abs(Faces[i].PointValue(V2));

                if ((PV0 < StashFaceThreshold && PV1 < StashFaceThreshold) || (PV1 < StashFaceThreshold && PV2 < StashFaceThreshold) || (PV2 < StashFaceThreshold && PV0 < StashFaceThreshold))
                {
                    // Stash this edge slightly outwards
                    float Dir = Faces[i].PointValue(vCentre);
                    if (Dir < 0f)
                    {
                        StashedFaces[i] = Faces[i];
                        Faces[i] = new Plane(Faces[i].a, Faces[i].b, Faces[i].c, Faces[i].d - 2f);
                    }
                    else
                    {
                        StashedFaces[i] = Faces[i];
                        Faces[i] = new Plane(Faces[i].a, Faces[i].b, Faces[i].c, Faces[i].d + 2f);
                    }
                    
                }
            }
        }

        public void UnstashFaces()
        {
            for (int i = 0; i < StashedFaces.Length; i++)
            {
                if (StashedFaces[i] != null)
                {
                    Faces[i] = StashedFaces[i];
                    StashedFaces[i] = null;
                }
            }
        }

        /// <summary>
        /// Finds the edge line based on the original vertex, to allow duplication of edge lines
        /// </summary>
        /// <param name="OriginalVertexIndex"></param>
        /// <param name="VStart"></param>
        /// <param name="VDest"></param>
        /// <returns></returns>
        private int FindEdge(int OriginalVertexIndex, Vector3 VStart, Vector3 VDest)
        {
            int i = 0;
            for (i = 0; i < EdgeVertOriginal.Length; i++)
            {
                // Unused edge
                if (EdgeVertOriginal[i] == -1)
                    break;

                if (EdgeVertOriginal[i] == OriginalVertexIndex)
                    return i;
            }

            Vector3Line Edge = new Vector3Line(VStart, VDest);
            Edges[i] = Edge;

            return i;
        }

        private int FindEdgeCCW(int OriginalA, int OriginalB)
        {
            for (int i = 0; i < Faces.Length; i += 3)
            {
                int IndexA = 0, IndexB = 0;

                if (EdgeVertOriginal[i + 0] == OriginalA)
                    IndexA = 0;
                else if (EdgeVertOriginal[i + 1] == OriginalA)
                    IndexA = 1;
                else if (EdgeVertOriginal[i + 2] == OriginalA)
                    IndexA = 2;

                if (EdgeVertOriginal[i + 0] == OriginalB)
                    IndexB = 0;
                else if (EdgeVertOriginal[i + 1] == OriginalB)
                    IndexB = 1;
                else if (EdgeVertOriginal[i + 2] == OriginalB)
                    IndexB = 2;

                if (IndexA == 0)
                    IndexA = 3;

                // Looking for CCw, so A must be B + 1
                if (IndexA == IndexB + 1)
                    return i;
            }

            return -1;
        }

        /// <summary>
        /// Tests to see if this triangle edge will be cut by any of our cutter planes
        /// </summary>
        /// <param name="TriEdge"></param>
        /// <param name="EdgeLen"></param>
        /// <param name="CookieCutEdge"></param>
        /// <param name="tCut"></param>
        /// <returns></returns>
        public bool TestEdgeAgainstCutter(Vector3Line TriEdge, float EdgeLen)
        {
            // Go through all our cutter planes and see if any of them cut it properly
            for (int i = 0; i < Faces.Length / 3; i++)
            {
                if (!FaceInternal[i * 3 + 0])
                {
                    Plane p = Faces[i * 3 + 0];
                    float? EDist = TriEdge.IntersectPlaneDistance(p);
                    if (EDist.HasValue && EDist > 0f && EDist.Value < EdgeLen)
                    {
                        Vector3 vInt = TriEdge.IntersectPlane(p);
                        if (TestVertex(vInt, i))
                            return true;
                    }
                }

                if (!FaceInternal[i * 3 + 1])
                {
                    Plane p = Faces[i * 3 + 1];
                    float? EDist = TriEdge.IntersectPlaneDistance(p);
                    if (EDist.HasValue && EDist > 0f && EDist.Value < EdgeLen)
                    {
                        Vector3 vInt = TriEdge.IntersectPlane(p);
                        if (TestVertex(vInt, i))
                            return true;
                    }
                }

                if (!FaceInternal[i * 3 + 2])
                {
                    Plane p = Faces[i * 3 + 2];
                    float? EDist = TriEdge.IntersectPlaneDistance(p);
                    if (EDist.HasValue && EDist > 0f && EDist.Value < EdgeLen)
                    {
                        Vector3 vInt = TriEdge.IntersectPlane(p);
                        if (TestVertex(vInt, i))
                            return true;
                    }
                }
            }
            
            return false;
        }

        /// <summary>
        /// A pair of edges which are the last clip face we clipped against. Stored to prevent repeated clipping at 1e-8 distances due to rounding errors.
        /// </summary>
        public int [] PreviousFoundEdge = { -1, -1 };

        public void ResetPreviousClipEdges()
        {
            PreviousFoundEdge[0] = -1;
            PreviousFoundEdge[1] = -1;
        }

        /// <summary>
        /// Returns the first cut
        /// </summary>
        /// <param name="TriEdge"></param>
        /// <param name="EdgeLen"></param>
        /// <returns></returns>
        public Vector3 FirstEdgeAgainstCutter(Vector3Line TriEdge, float EdgeLen)
        {
            // Go through all our cutter planes and see if any of them cut it properly
            float BestDistance = EdgeLen;
            Vector3 Result = null;
            int EdgeID = -1;

            for (int i = 0; i < Faces.Length / 3; i++)
            {
                int index = i * 3 + 0;
                if (!FaceInternal[index] && !PreviousFoundEdge.Contains(index) && (EdgeID == -1 || FaceOpposite[EdgeID] != index))
                {
                    Plane p = Faces[index];
                    float? EDist = TriEdge.IntersectPlaneDistance(p);
                    if (EDist.HasValue && EDist > 0f && EDist.Value < BestDistance)
                    {
                        Vector3 vInt = TriEdge.IntersectPlane(p);
                        if (TestVertex(vInt, i))
                        {
                            BestDistance = EDist.Value;
                            Result = vInt;
                            EdgeID = index;
                        }
                    }
                }

                index = i * 3 + 1;
                if (!FaceInternal[index] && !PreviousFoundEdge.Contains(index) && (EdgeID == -1 || FaceOpposite[EdgeID] != index))
                {
                    Plane p = Faces[index];
                    float? EDist = TriEdge.IntersectPlaneDistance(p);
                    if (EDist.HasValue && EDist > 0f && EDist.Value < BestDistance)
                    {
                        Vector3 vInt = TriEdge.IntersectPlane(p);
                        if (TestVertex(vInt, i))
                        {
                            BestDistance = EDist.Value;
                            Result = vInt;
                            EdgeID = index;
                        }
                    }
                }

                index = i * 3 + 2;
                if (!FaceInternal[index] && !PreviousFoundEdge.Contains(index) && (EdgeID == -1 || FaceOpposite[EdgeID] != index))
                {
                    Plane p = Faces[index];
                    float? EDist = TriEdge.IntersectPlaneDistance(p);
                    if (EDist.HasValue && EDist > 0f && EDist.Value < BestDistance)
                    {
                        Vector3 vInt = TriEdge.IntersectPlane(p);
                        if (TestVertex(vInt, i))
                        {
                            BestDistance = EDist.Value;
                            Result = vInt;
                            EdgeID = index;
                        }
                    }
                }
            }

            PreviousFoundEdge[0] = EdgeID;
            if (EdgeID != -1)
                PreviousFoundEdge[1] = FaceOpposite[EdgeID];

            return Result;
        }

        /// <summary>
        /// Returns the first cut
        /// </summary>
        /// <param name="TriEdge"></param>
        /// <param name="EdgeLen"></param>
        /// <returns></returns>
        public Vector3 LastEdgeAgainstCutter(Vector3Line TriEdge, float EdgeLen)
        {
            // Go through all our cutter planes and see if any of them cut it properly
            float BestDistance = 0f;
            Vector3 Result = null;

            for (int i = 0; i < Faces.Length / 3; i++)
            {
                if (!FaceInternal[i * 3 + 0])
                {
                    Plane p = Faces[i * 3 + 0];
                    float? EDist = TriEdge.IntersectPlaneDistance(p);
                    if (EDist.HasValue && EDist > BestDistance && EDist.Value < EdgeLen)
                    {
                        Vector3 vInt = TriEdge.IntersectPlane(p);
                        if (TestVertex(vInt, i))
                        {
                            BestDistance = EDist.Value;
                            Result = vInt;
                        }
                    }
                }

                if (!FaceInternal[i * 3 + 1])
                {
                    Plane p = Faces[i * 3 + 1];
                    float? EDist = TriEdge.IntersectPlaneDistance(p);
                    if (EDist.HasValue && EDist > BestDistance && EDist.Value < EdgeLen)
                    {
                        Vector3 vInt = TriEdge.IntersectPlane(p);
                        if (TestVertex(vInt, i))
                        {
                            BestDistance = EDist.Value;
                            Result = vInt;
                        }
                    }
                }

                if (!FaceInternal[i * 3 + 2])
                {
                    Plane p = Faces[i * 3 + 2];
                    float? EDist = TriEdge.IntersectPlaneDistance(p);
                    if (EDist.HasValue && EDist > BestDistance && EDist.Value < EdgeLen)
                    {
                        Vector3 vInt = TriEdge.IntersectPlane(p);
                        if (TestVertex(vInt, i))
                        {
                            BestDistance = EDist.Value;
                            Result = vInt;
                        }
                    }
                }
            }

            return Result;
        }

        /// <summary>
        /// Tests whether a point is within any of out cutter triangles
        /// </summary>
        /// <param name="vTest"></param>
        /// <returns></returns>
        public bool TestVertex(Vector3 vTest)
        {
            // Check all of our cut prism
            for (int i = 0; i < Faces.Length / 3; i++)
            {
                if (TestVertex(vTest, i))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Tests whether a point is within 1 of out cutter triangles, or not
        /// </summary>
        /// <param name="vTest"></param>
        /// <returns></returns>
        public bool TestVertex(Vector3 vTest, int FaceIndex)
        {
            // Allow a very small amount of deviation to cope with rounding errors
            if (Faces[FaceIndex * 3 + 0].PointValue(vTest) < 0.0001f && Faces[FaceIndex * 3 + 1].PointValue(vTest) < 0.0001f && Faces[FaceIndex * 3 + 2].PointValue(vTest) < 0.0001f)
                return true;

            return false;
        }

        public bool EdgesWithinTriangle(Vector3 V0, Vector3 V1, Vector3 V2)
        {
            Plane pTri = Plane.FromTriangle(V0, V1, V2);

            for (int i = 0; i < EdgeIndex.Length; i++)
            {
                Vector3 vHit = Edges[EdgeIndex[i]].IntersectPlane(pTri);

                if (vHit != null)
                {
                    float u, v, w;
                    Cutter.Barycentric(vHit, V0, V1, V2, out u, out v, out w);

                    if (u < 0f || u > 1f)
                        continue;
                    if (v < 0f || v > 1f)
                        continue;
                    if (w < 0f || w > 1f)
                        continue;

                    // The hit was valid and was within barycentric limits
                    return true;
                }
            }

            return false;
        }

        public bool AllEdgesWithinTriangle(Vector3 V0, Vector3 V1, Vector3 V2)
        {
            Plane pTri = Plane.FromTriangle(V0, V1, V2);

            // Check to see if all the cookie cutter edges are within the triangle. This could be optimised because we check each edge potentially more than once
            for (int i = 0; i < EdgeIndex.Length; i++)
            {
                Vector3 vHit = Edges[EdgeIndex[i]].IntersectPlane(pTri);

                if (vHit != null)
                {
                    float u, v, w;
                    Cutter.Barycentric(vHit, V0, V1, V2, out u, out v, out w);

                    // Check to see if this hit location resides within our parent triangle. If it doesn't not all edges are within triangle
                    if (u < 0f || u > 1f)
                        return false;
                    if (v < 0f || v > 1f)
                        return false;
                    if (w < 0f || w > 1f)
                        return false;
                }
            }

            return true;
        }

        private int FindClippingFace(Vector3 vHit)
        {
            // This point has been generated by clipping a line through our clip faces.
            // So find the External face which has done the deed
            float BestMatch = 100000000f;
            int BestIndex = -1;

            for (int i = 0; i < Faces.Length; i++)
            {
                if (!FaceInternal[i])
                {
                    float fDist = Math.Abs(Faces[i].PointValue(vHit));
                    if (fDist < BestMatch)
                    {
                        BestMatch = fDist;
                        BestIndex = i;
                    }
                }
            }

            return BestIndex;
        }

        private int FindNearEdge(Vector3 vHit, Plane pNormal)
        {
            // Each prism edge will intersect with a pNormal at a point.
            // If the point is really close to our vHit point, then we start at this edge

            float fBestDistance = 1000000000f;
            int BestIndex = -1;

            for(int i = 0; i < EdgeIndex.Length; i++)
            {
                Vector3 pHit = Edges[EdgeIndex[i]].IntersectPlane(pNormal);
                if (pHit != null)
                {
                    pHit = pHit - vHit;
                    float D = pHit.Len();

                    if (D < 0.0001f && D < fBestDistance)
                    {
                        fBestDistance = D;
                        BestIndex = i;
                    }
                }
            }

            return BestIndex;
        }

        private int FindPreviousEdge(int Edge)
        {
            // We are looking for a triangle external face
            for (int i = 0; i < EdgeIndex.Length; i++)
            {
                int iFaceNumber = i / 3;
                int iEdgeCurrent = (i % 3);
                int iEdgePrev = (i % 3) - 1;
                if (iEdgePrev < 0)
                    iEdgePrev = 2;

                iEdgeCurrent += iFaceNumber * 3;
                iEdgePrev += iFaceNumber * 3;

                iEdgeCurrent = EdgeIndex[iEdgeCurrent];
                if (iEdgeCurrent == Edge && !FaceInternal[iEdgePrev])
                    return iEdgePrev;
            }

            return -1;
        }

        public Vector3 NextSegment(Vector3 vStart, Plane pNormal)
        {
            // Check that we haven't started on an edge
            int iStartEdge = FindNearEdge(vStart, pNormal);

            if (iStartEdge == -1)
            {
                // We have hit a regular prism face, find the face
                int iClipFace = FindClippingFace(vStart);

                int iFaceNumber = iClipFace / 3;
                int iEdgePrev = iClipFace % 3;  // Face 0 has verts 0->1, Face 1 has verts 1->2 etc., so this is the previous prism edge
                return Edges[EdgeIndex[iFaceNumber * 3 + iEdgePrev]].IntersectPlane(pNormal);
            } else
            {
                // We have found an edge. Find a triangle which has an external face with this as it's destination
                int iEdge = FindPreviousEdge(iStartEdge);
                return Edges[EdgeIndex[iEdge]].IntersectPlane(pNormal);
            }
        }

        public int FirstExternalEdge()
        {
            for(int i = 0; i < Faces.Length; i++)
            {
                if (!FaceInternal[i])
                    return EdgeIndex[i];   // EdgeIndex[i] is the first point of Face[i]
            }

            return -1;
        }

        private float EdgeDistanceToPoint(Vector3Line Edge, Vector3 vTest)
        {
            Vector3 vClosest = (Edge.Origin() - vTest) - Vector3.Dot(Edge.Origin(), Edge.Dir().Normalise()) * Edge.Dir().Normalise();

            return vClosest.Len();
        }

        public List<int> MarkVerts(List<PositionAndNormal> v3MeshData)
        {
            List<int> HullPoints = new List<int>();

            for(int i = 0; i < Faces.Length; i++)
            {
                Plane Face = Faces[i];
                if (FaceInternal[i])
                    continue;   // Ignore internal cut faces

                List<Tuple<int, float>> FaceVerts = new List<Tuple<int, float>>();

                int iFaceNumber = i / 3;
                int iEdgePrev = i % 3;
                int iEdgeNext = (i % 3) + 1;
                if (iEdgeNext > 2)
                    iEdgeNext = 0;

                int iFacePrev = (i % 3) - 1;
                int iFaceNext = (i % 3) + 1;

                if (iFacePrev < 0)
                    iFacePrev = 2;
                if (iFaceNext > 2)
                    iFaceNext = 0;

                iEdgeNext += iFaceNumber * 3;
                iEdgePrev += iFaceNumber * 3;
                iFaceNext += iFaceNumber * 3;
                iFacePrev += iFaceNumber * 3;

                float EDistMax = Faces[iFacePrev].PointValue(Edges[EdgeIndex[iEdgeNext]].Origin());

                for (int j = 0; j < v3MeshData.Count; j++)
                {
                    PositionAndNormal pos = v3MeshData[j];

                    float Proximity = Face.PointValue(pos.Pos);
                    
                    if (Math.Abs(Proximity) < 0.001f)
                    {
                        // This Vector3 is on this plane
                        float EDistP = EdgeDistanceToPoint(Edges[EdgeIndex[iEdgePrev]], pos.Pos);
                        float EDistN = EdgeDistanceToPoint(Edges[EdgeIndex[iEdgeNext]], pos.Pos);
                        if (EDistP < 0.001f)
                        {
                            FaceVerts.Add(new Tuple<int, float>(j, 0f));
                        } else if (EDistN < 0.001f)
                        {
                            FaceVerts.Add(new Tuple<int, float>(j, 1f));
                        } else
                        {
                            // Verify that the point isn't outside of the triangle, and if it isn't then order by -ve Point Value

                            if (Faces[iFacePrev].PointValue(pos.Pos) < 0f && Faces[iFaceNext].PointValue(pos.Pos) < 0f)
                            {
                                // Super, add the point
                                FaceVerts.Add(new Tuple<int, float>(j, Faces[iFacePrev].PointValue(pos.Pos) / EDistMax));
                            }
                        }
                    }
                }

                var Ordered = FaceVerts.OrderBy(x => x.Item2);  // Get the list of points which lie on this plane in the clockwise order

                foreach (var v in Ordered)
                    HullPoints.Add(v.Item1);
            }

            // We now have a list of hull points. Go through them creating edges
            for (int i = 0; i < HullPoints.Count;)
            {
                int iNext = i + 1;
                if (iNext >= HullPoints.Count)
                    iNext = 0;

                if (HullPoints[i] == HullPoints[iNext])
                {
                    HullPoints.RemoveAt(iNext); // This is a cutter edge where two planes meet
                } else
                {
                    i++;
                }
            }

            // Now we have a list of points which can make up a hull
            return HullPoints;
        }
    }
}
