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
        public int[] FaceCuts;

        public Vector3Line[] Edges;
        public int[] EdgeIndex; // Each triangle has 3 line edges
        private bool[] EdgeInternal;    // Some edges are internal
        private int[] EdgeVertOriginal; // The original vertex which generated this edge
        private Vector3[] BuildVertOriginal;

        private Plane [] StashedFaces;
        private Vector3[] StashedEdgeOrigin;
        public float StashFaceThreshold = 0.01f;

        public static CookieCutter MakeCC(List<Vector3> v3Triangles, Vector3 FaceNormal)
        {
            List<Triangle> triBuildup = new List<Triangle>();
            List<Vector3> PolyMesh = new List<Vector3>();
            
            for (int i = 0; i < v3Triangles.Count; i += 3)
            {
                int V0 = PolyMesh.IndexOf(v3Triangles[i]);
                int V1 = PolyMesh.IndexOf(v3Triangles[i + 1]);
                int V2 = PolyMesh.IndexOf(v3Triangles[i + 2]);

                if (V0 == -1)
                {
                    V0 = PolyMesh.Count;
                    PolyMesh.Add(v3Triangles[i]);
                }

                if (V1 == -1)
                {
                    V1 = PolyMesh.Count;
                    PolyMesh.Add(v3Triangles[i + 1]);
                }

                if (V2 == -1)
                {
                    V2 = PolyMesh.Count;
                    PolyMesh.Add(v3Triangles[i + 2]);
                }

                Triangle t = new Triangle(V0, V1, V2, FaceNormal);
                triBuildup.Add(t);
            }

            // Make sure the triangles have the correct face direction
            foreach (Triangle t in triBuildup)
            {
                if (Vector3.Dot(Vector3.Cross(PolyMesh[t.VertexIndex[1]] - PolyMesh[t.VertexIndex[0]], PolyMesh[t.VertexIndex[2]] - PolyMesh[t.VertexIndex[0]]), FaceNormal) < 0f)
                {
                    // This triangle is not facing the direction it should. Reverse it.
                    int V1 = t.VertexIndex[2];
                    int V2 = t.VertexIndex[1];

                    t.VertexIndex[1] = V1;
                    t.VertexIndex[2] = V2;
                }
            }

            return new CookieCutter(PolyMesh, triBuildup, FaceNormal);
        }

        /// <summary>
        /// Returns a new cookie cutter based off the triangle and the normal along which to project the face cuts. All polygons which make up a cookie cutter must share at least 2 verts with another polygon in the triangle list
        /// </summary>
        public CookieCutter(List<Vector3> v3PolyMesh, List<Triangle> triangles, Vector3 FaceNormal)
        {
            Faces = new Plane[triangles.Count * 3];
            StashedFaces = new Plane[triangles.Count * 3];  // For dealing with cut faces which are flat up against a triangle edge

            Edges = new Vector3Line[triangles.Count * 3];   // Maximum, we might not use them all
            StashedEdgeOrigin = new Vector3[triangles.Count * 3];

            FaceInternal = new bool[triangles.Count * 3];
            FaceOpposite = new int[triangles.Count * 3];
            FaceCuts = new int[triangles.Count * 3];

            EdgeIndex = new int[triangles.Count * 3];
            EdgeInternal = new bool[triangles.Count * 3];
            EdgeVertOriginal = new int[triangles.Count * 3];
            BuildVertOriginal = new Vector3[triangles.Count * 3];

            for (int i = 0; i < EdgeVertOriginal.Length; i++)
                EdgeVertOriginal[i] = -1;   // Ensure none of these are used

            for (int i = 0; i < FaceOpposite.Length; i++)
            {
                FaceOpposite[i] = -1;
                FaceCuts[i] = 0;
            }

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

                if(float.IsNaN(Face01.a) || float.IsNaN(Face12.a) || float.IsNaN(Face20.a))
                {
                    UnityEngine.Debug.LogError("Cutter Planes were invalid");
                }

                Faces[i * 3 + 0] = Face01;
                Faces[i * 3 + 1] = Face12;
                Faces[i * 3 + 2] = Face20;

                EdgeIndex[i * 3 + 0] = FindEdge(t.VertexIndex[0], V0, V0p);
                EdgeVertOriginal[i * 3 + 0] = t.VertexIndex[0];
                EdgeIndex[i * 3 + 1] = FindEdge(t.VertexIndex[1], V1, V1p);
                EdgeVertOriginal[i * 3 + 1] = t.VertexIndex[1];

                EdgeIndex[i * 3 + 2] = FindEdge(t.VertexIndex[2], V2, V2p);
                EdgeVertOriginal[i * 3 + 2] = t.VertexIndex[2];

                BuildVertOriginal[i * 3 + 0] = V0;
                BuildVertOriginal[i * 3 + 1] = V1;
                BuildVertOriginal[i * 3 + 2] = V2;
            }

            int iInternalCount = 0;
            // Determine if a face is internal
            for (int i = 0; i < triangles.Count; i++)
            {
                Triangle t = triangles[i];

                // 3 edges - 01, 12, 20
                // If we find the opposite original verts in another triangle
                int iOpposite = FindEdgeCCW(t.VertexIndex[0], t.VertexIndex[1]);
                if (iOpposite > -1)
                {
                    iInternalCount++;
                    FaceInternal[i * 3 + 0] = true;
                    FaceOpposite[i * 3 + 0] = iOpposite;
                }

                iOpposite = FindEdgeCCW(t.VertexIndex[1], t.VertexIndex[2]);
                if (iOpposite > -1)
                {
                    iInternalCount++;
                    FaceInternal[i * 3 + 1] = true;
                    FaceOpposite[i * 3 + 1] = iOpposite;
                }

                iOpposite = FindEdgeCCW(t.VertexIndex[2], t.VertexIndex[0]);
                if (iOpposite > -1)
                {
                    iInternalCount++;
                    FaceInternal[i * 3 + 2] = true;
                    FaceOpposite[i * 3 + 2] = iOpposite;
                }
            }

            if (iInternalCount != (triangles.Count - 1) * 2)
            {
                Console.WriteLine("Breakpoint");
            }

            // Determine if an edge is internal? Necessary?
        }

        private bool CutterIsOnPlane(Plane p)
        {
            float Tolerance = 0.001f;
            for (int i = 0; i < Edges.Length; i++)
            {
                if (Edges[i] == null)
                    continue;

                float Score = Math.Abs(p.PointValue(Edges[i].Origin()));

                if (Score > Tolerance)
                    return false;
            }

            return true;
        }

        private void ReseatCutterOnPlane(Plane p)
        {
            for(int i = 0; i < Edges.Length; i++)
            {
                if (Edges[i] == null)
                    continue;

                Vector3Line InterceptLine = new Vector3Line(Edges[i]);
                InterceptLine.x = BuildVertOriginal[i].x;
                InterceptLine.y = BuildVertOriginal[i].y;
                InterceptLine.z = BuildVertOriginal[i].z;

                Vector3 vNewOrigin = InterceptLine.IntersectPlane(p);
                Edges[i].x = vNewOrigin.x;
                Edges[i].y = vNewOrigin.y;
                Edges[i].z = vNewOrigin.z;
            }
        }

        /// <summary>
        /// Finds all prism external edges which match edges in the triangle and adjust them slightly to ensure we cut correctly
        /// </summary>
        /// <param name="V0"></param>
        /// <param name="V1"></param>
        /// <param name="V2"></param>
        public void StashFaces(Vector3 V0, Vector3 V1, Vector3 V2, bool CullExternalTriangles)
        {
            Vector3 vCentre = (V0 + V1 + V2) / 3;
            Vector3Line TriLine01 = new Vector3Line(V0, V1);
            Vector3Line TriLine12 = new Vector3Line(V1, V2);
            Vector3Line TriLine20 = new Vector3Line(V2, V0);

            if (!CutterIsOnPlane(new Plane(V0, V1, V2)))
                ReseatCutterOnPlane(new Plane(V0, V1, V2));

            bool Changes = false;

            for (int i = 0; i < Faces.Length; i++)
            {
                if (FaceInternal[i])
                    continue;

                float PV0 = Math.Abs(Faces[i].PointValue(V0));
                float PV1 = Math.Abs(Faces[i].PointValue(V1));
                float PV2 = Math.Abs(Faces[i].PointValue(V2));

                bool TriEdge0 = (PV0 < StashFaceThreshold && PV1 < StashFaceThreshold);
                bool TriEdge1 = (PV1 < StashFaceThreshold && PV2 < StashFaceThreshold);
                bool TriEdge2 = (PV2 < StashFaceThreshold && PV0 < StashFaceThreshold);

                // Get the two edge lines for this face
                int CurrentEdgeIndex = EdgeIndex[i];
                int NextEdgeIndex = EdgeIndex[FindNextEdge(CurrentEdgeIndex)];

                // There is a condition where the faces indicate that we aren't close but the actual point are on an almost perfect straight line
                float Line01CDistance, Line01NDistance;
                Vector3 v3Line01C = TriLine01.FindNearestPoint(Edges[CurrentEdgeIndex].Origin(), out Line01CDistance);
                Vector3 v3Line01N = TriLine01.FindNearestPoint(Edges[NextEdgeIndex].Origin(), out Line01NDistance);

                float Line12CDistance, Line12NDistance;
                Vector3 v3Line12C = TriLine12.FindNearestPoint(Edges[CurrentEdgeIndex].Origin(), out Line12CDistance);
                Vector3 v3Line12N = TriLine12.FindNearestPoint(Edges[NextEdgeIndex].Origin(), out Line12NDistance);

                float Line20CDistance, Line20NDistance;
                Vector3 v3Line20C = TriLine20.FindNearestPoint(Edges[CurrentEdgeIndex].Origin(), out Line20CDistance);
                Vector3 v3Line20N = TriLine20.FindNearestPoint(Edges[NextEdgeIndex].Origin(), out Line20NDistance);

                if (Line01CDistance > -0.001f && Line01CDistance < 1.001f && Line01NDistance > -0.001f && Line01NDistance < 1.001f && (v3Line01C - Edges[CurrentEdgeIndex].Origin()).Len() < StashFaceThreshold && (v3Line01N - Edges[NextEdgeIndex].Origin()).Len() < StashFaceThreshold)
                {
                    // Both cutter edges are very close to the triangle edge 01, TriEdge0 should be true
                    TriEdge0 = true;
                }

                if (Line12CDistance > -0.001f && Line12CDistance < 1.001f && Line12NDistance > -0.001f && Line12NDistance < 1.001f && (v3Line12C - Edges[CurrentEdgeIndex].Origin()).Len() < StashFaceThreshold && (v3Line12N - Edges[NextEdgeIndex].Origin()).Len() < StashFaceThreshold)
                {
                    // Both cutter edges are very close to the triangle edge 01, TriEdge0 should be true
                    TriEdge1 = true;
                }

                if (Line20CDistance > -0.001f && Line20CDistance < 1.001f && Line20NDistance > -0.001f && Line20NDistance < 1.001f && (v3Line20C - Edges[CurrentEdgeIndex].Origin()).Len() < StashFaceThreshold && (v3Line20N - Edges[NextEdgeIndex].Origin()).Len() < StashFaceThreshold)
                {
                    // Both cutter edges are very close to the triangle edge 01, TriEdge0 should be true
                    TriEdge2 = true;
                }


                if (TriEdge0 && TriEdge1 && TriEdge2)
                {
                    // Sliver triangles - find the lowest 2 triangles and handle correctly.
                    if (PV0 <= PV2 && PV1 <= PV2)
                    {
                        TriEdge1 = false;
                        TriEdge2 = false;
                    } else if (PV1 <= PV0 && PV2 <= PV0)
                    {
                        TriEdge0 = false;
                        TriEdge2 = false;
                    } else
                    {
                        TriEdge1 = false;
                        TriEdge0 = false;
                    }    
                }

                if (TriEdge0 || TriEdge1 || TriEdge2)
                {
                    // Stash this edge slightly outwards
                    float Dir = Faces[i].PointValue(vCentre);
                    if (Dir < 0f)// ^ CullExternalTriangles)
                    {
                        StashedFaces[i] = Faces[i];
                        Faces[i] = new Plane(Faces[i].a, Faces[i].b, Faces[i].c, Faces[i].d - 0.1f);    // Offset by 10cm
                    }
                    else
                    {
                        StashedFaces[i] = Faces[i];
                        Faces[i] = new Plane(Faces[i].a, Faces[i].b, Faces[i].c, Faces[i].d + 0.1f);    // Offset by 10cm
                    }

                    Changes = true;
                }
            }

            if (Changes)
                CreateNewStashedFaces();
        }

        private void CreateNewStashedFaces()
        {
            // Now take a copy of all the edge origins
            for (int i = 0; i < Edges.Length; i++)
            {
                if (Edges[i] == null)
                    continue;

                Vector3Line v3lNew = GetStashedEdgeLine(i);
                StashedEdgeOrigin[i] = Edges[i].Origin();

                Edges[i].x = v3lNew.x;
                Edges[i].y = v3lNew.y;
                Edges[i].z = v3lNew.z;
            }

            // Now that we have moved (or not moved) the edges to their new positions, recreate the cut faces so that we maintain the prisms
            for (int i = 0; i < Edges.Length; i += 3)
            {
                // Generate the 3 faces
                Vector3 V0 = Edges[EdgeIndex[i + 0]].Origin();
                Vector3 V1 = Edges[EdgeIndex[i + 1]].Origin();
                Vector3 V2 = Edges[EdgeIndex[i + 2]].Origin();

                Vector3 V0p = V0 + Edges[EdgeIndex[i + 0]].Dir();
                Vector3 V1p = V1 + Edges[EdgeIndex[i + 1]].Dir();
                Vector3 V2p = V2 + Edges[EdgeIndex[i + 2]].Dir();

                Plane Face01 = Plane.FromTriangle(V0, V1, V1p);
                Plane Face12 = Plane.FromTriangle(V1, V2, V2p);
                Plane Face20 = Plane.FromTriangle(V2, V0, V0p);

                Faces[i + 0] = Face01;
                Faces[i + 1] = Face12;
                Faces[i + 2] = Face20;
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

            for (int i = 0; i < StashedEdgeOrigin.Length; i++)
            {
                if(StashedEdgeOrigin[i] != null)
                {
                    Edges[i].x = StashedEdgeOrigin[i].x;
                    Edges[i].y = StashedEdgeOrigin[i].y;
                    Edges[i].z = StashedEdgeOrigin[i].z;
                    StashedEdgeOrigin[i] = null;
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
            for (int i = 0; i < Faces.Length; i++)
            {
                int TriIndex = i / 3;

                int EdgeVert0 = i % 3;
                int EdgeVert1 = (i % 3) + 1;

                if (EdgeVert1 > 2)
                    EdgeVert1 = 0;

                EdgeVert0 += TriIndex * 3;
                EdgeVert1 += TriIndex * 3;

                EdgeVert0 = EdgeVertOriginal[EdgeVert0];
                EdgeVert1 = EdgeVertOriginal[EdgeVert1];

                // Looking for CCw, so A must be B + 1
                if (EdgeVert1 == OriginalA && EdgeVert0 == OriginalB)
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
                        if (TestVertex(vInt, i) || TestValidFaceCut(vInt, i))
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
                        if (TestVertex(vInt, i) || TestValidFaceCut(vInt, i))
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
                        if (TestVertex(vInt, i) || TestValidFaceCut(vInt, i))
                            return true;
                    }
                }
            }
            
            return false;
        }

        /// <summary>
        /// A pair of edges which are the last clip face we clipped against. Stored to prevent repeated clipping at 1e-8 distances due to rounding errors.
        /// </summary>
        public int [] PreviousFoundEdge = { -1, -1, -1, -1, -1 };

        public void ResetPreviousClipEdges()
        {
            PreviousFoundEdge[0] = -1;
            PreviousFoundEdge[1] = -1;
            PreviousFoundEdge[2] = -1;
            PreviousFoundEdge[3] = -1;
            PreviousFoundEdge[4] = -1;
        }

        public void ResetFaceCuts()
        {
            for (int i = 0; i < FaceCuts.Length; i++)
                FaceCuts[i] = 0;
        }

        private void FindTooNearCutPlanes(Vector3 EdgeStart, Vector3 EdgeEnd, Plane pNormal)
        {
            List<int> tooNear = new List<int>();

            float Threshold = 0.001f;
            if ((EdgeStart - EdgeEnd).Len() > 40f)
                Threshold = 0.00001f;   // Hack for ISD

            for(int i = 0; i < Faces.Length; i++)
            {
                if (FaceInternal[i])
                    continue;

                float fStart = Math.Abs(Faces[i].PointValue(EdgeStart));
                float fEnd = Math.Abs(Faces[i].PointValue(EdgeEnd));

                if (fStart < Threshold)
                    tooNear.Add(i);

                if (fEnd < Threshold)
                    tooNear.Add(i);
            }

            for (int i = 0; i < Math.Min(tooNear.Count, 4); i++)
                PreviousFoundEdge[i] = tooNear[i];
        }

        public bool VertexOnCutFace(Vector3 vTest, Plane pNormal)
        {
            for (int i = 0; i < Faces.Length; i++)
            {
                if (FaceInternal[i])
                    continue;

                float fStart = Math.Abs(Faces[i].PointValue(vTest));

                if (fStart < 0.00001f && TestValidFaceCut(vTest, i))    // Needs to be close to the face and also within the limits for the face
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Returns the first cut
        /// </summary>
        /// <param name="TriEdge"></param>
        /// <param name="EdgeLen"></param>
        /// <returns></returns>
        public Vector3 FirstEdgeAgainstCutter(Vector3 EdgeStart, Vector3 EdgeEnd, Plane pNormal, out int FaceIndex)
        {
            FindTooNearCutPlanes(EdgeStart, EdgeEnd, pNormal);

            /*int iStartEdge = FindNearEdge(EdgeStart, pNormal);

            int iPrevEdge = FindPreviousEdge(iStartEdge);
            int iNextEdge = FindNextEdge(iStartEdge);

            if (iStartEdge != -1)
            {
                PreviousFoundEdge[0] = iStartEdge;
                PreviousFoundEdge[1] = iPrevEdge;
            }

            iStartEdge = FindNearEdge(EdgeEnd, pNormal);

            iPrevEdge = FindPreviousEdge(iStartEdge);
            iNextEdge = FindNextEdge(iStartEdge);

            if (iStartEdge != -1)
            {
                PreviousFoundEdge[2] = iStartEdge;
                PreviousFoundEdge[3] = iPrevEdge;
            }*/

            Vector3Line TriEdge = new Vector3Line(EdgeStart, EdgeEnd);
            float EdgeLen = TriEdge.Dir().Len();

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
                    if (EDist.HasValue && EDist >= 0f && EDist.Value <= BestDistance)
                    {
                        Vector3 vInt = TriEdge.IntersectPlane(p);
                        if (TestValidFaceCut(vInt, index))
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
                    if (EDist.HasValue && EDist >= 0f && EDist.Value <= BestDistance)
                    {
                        Vector3 vInt = TriEdge.IntersectPlane(p);
                        if (TestValidFaceCut(vInt, index))
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
                    if (EDist.HasValue && EDist >= 0f && EDist.Value <= BestDistance)
                    {
                        Vector3 vInt = TriEdge.IntersectPlane(p);
                        if (TestValidFaceCut(vInt, index))
                        {
                            BestDistance = EDist.Value;
                            Result = vInt;
                            EdgeID = index;
                        }
                    }
                }
            }

            ResetPreviousClipEdges();
            PreviousFoundEdge[4] = EdgeID;

            FaceIndex = EdgeID;

            return Result;
        }        

        /// <summary>
        /// Tests whether a point is within any of our cutter triangles
        /// </summary>
        /// <param name="vTest"></param>
        /// <returns></returns>
        public bool TestVertex(Vector3 vTest)
        {
            // Check all of our cut prism
            for (int i = 0; i < Faces.Length / 3; i++)
            {
                if (TestVertexByFace(vTest, i))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Gets the  true edge line for an edge with a stashed face (or a non-stashed face)
        /// </summary>
        /// <param name="Edge"></param>
        /// <returns></returns>
        public Vector3Line GetStashedEdgeLine(int Edge)
        {
            Edge = EdgeIndex[Edge];
            int FaceCurrent = FindFaceStartingHere(Edge);
            int FacePrevious = FindNextFace(FaceCurrent);

            int EdgePrev = FindPreviousEdge(Edge);
            int EdgeNext = FindNextEdge(Edge);

            EdgePrev = EdgeIndex[EdgePrev];
            EdgeNext = EdgeIndex[EdgeNext];

            // Get the planes for these faces
            Plane planeCurrent = Faces[FaceCurrent];
            Plane planePrevious = Faces[FacePrevious];

            Vector3Line v3Line = new Vector3Line( Edges[Edge]);
            Vector3 origin = Edges[Edge].Origin();

            // 
            Vector3Line v3PrevLine = new Vector3Line(Edges[EdgePrev].Origin(), Edges[Edge].Origin());
            Vector3Line v3CurrentLine = new Vector3Line(Edges[EdgeNext].Origin(), Edges[Edge].Origin());

            // Project the previous line into the current face to get an adjustment
            Vector3 adjPrev = v3PrevLine.IntersectPlane(planeCurrent) - origin;

            // Project the current line backwards into the previous face to get an adjustment
            Vector3 adjCurrent = v3CurrentLine.IntersectPlane(planePrevious) - origin;

            origin += adjPrev;
            origin += adjCurrent;

            v3Line.x = origin.x;
            v3Line.y = origin.y;
            v3Line.z = origin.z;

            // some debug logic to check that we are still sat on the same plane
            float fP = planePrevious.PointValue(origin);
            float fC = planeCurrent.PointValue(origin);


            return v3Line;
        }

        /// <summary>
        /// Tests whether a point is within 1 of out cutter triangles, or not, using the prism edges
        /// </summary>
        /// <param name="vTest"></param>
        /// <returns></returns>
        public bool TestVertex(Vector3 vTest, int FaceIndex)
        {
            float u, v, w;
            Cutter.Barycentric(vTest, Edges[EdgeIndex[FaceIndex * 3]].Origin(), Edges[EdgeIndex[FaceIndex * 3 + 1]].Origin(), Edges[EdgeIndex[FaceIndex * 3 + 2]].Origin(), out u, out v, out w);

            if (u < -0.001f || u > 1.001f)
                return false;

            if (v < -0.001f || v > 1.001f)
                return false;

            if (w < -0.001f || w > 1.001f)
                return false;

            return true;
            /*
            // Allow a very small amount of deviation to cope with rounding errors
            if (Faces[FaceIndex * 3 + 0].PointValue(vTest) < 0.0001f && Faces[FaceIndex * 3 + 1].PointValue(vTest) < 0.0001f && Faces[FaceIndex * 3 + 2].PointValue(vTest) < 0.0001f)
                return true;

            return false;*/
        }

        /// <summary>
        /// Tests whether a point is within 1 of out cutter triangles, or not, using the prism faces
        /// </summary>
        /// <param name="vTest"></param>
        /// <returns></returns>
        public bool TestVertexByFace(Vector3 vTest, int FaceIndex)
        {
            // Allow a very small amount of deviation to cope with rounding errors
            if (Faces[FaceIndex * 3 + 0].PointValue(vTest) < 0.0001f && Faces[FaceIndex * 3 + 1].PointValue(vTest) < 0.0001f && Faces[FaceIndex * 3 + 2].PointValue(vTest) < 0.0001f)
                return true;

            return false;
        }

        private bool TestValidFaceCut(Vector3 vTest, int Face)
        {
            int TriIndex = Face / 3;

            int NextFace = (Face % 3) + 1;
            int PrevFace = (Face % 3) - 1;

            if (NextFace > 2)
                NextFace = 0;
            if (PrevFace < 0)
                PrevFace = 2;

            NextFace += TriIndex * 3;
            PrevFace += TriIndex * 3;

            if (Faces[NextFace].PointValue(vTest) < 0.0001f && Faces[PrevFace].PointValue(vTest) < 0.0001f)
                return true;

            return false;
            /*
            int EdgeStart = Face % 3;
            int EdgeEnd = EdgeStart + 1;

            if (EdgeEnd > 2)
                EdgeEnd = 0;

            EdgeStart += TriIndex * 3;
            EdgeEnd += TriIndex * 3;

            EdgeStart = EdgeIndex[EdgeStart];   // Get the vector array index
            EdgeEnd = EdgeIndex[EdgeEnd];   // Get the vector array index

            Vector3 vStart = Edges[EdgeStart].Origin();
            Vector3 vEnd = Edges[EdgeEnd].Origin();

            Plane pStartCap = Plane.FromOriginAndNormal(vStart, vEnd - vStart);
            Plane pEndCap = Plane.FromOriginAndNormal(vEnd, vStart - vEnd);

            float StartVal = pStartCap.PointValue(vTest);
            float EndVal = pEndCap.PointValue(vTest);

            if (StartVal >= -0.001f && EndVal >= -0.001f)   // We have 1mm tolerance at either end
                return true;
            return false;*/
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

        public bool AllPrismsWithinTriangle(Vector3 V0, Vector3 V1, Vector3 V2)
        {
            Plane pTri = Plane.FromTriangle(V0, V1, V2);

            // Check to see if all the cookie cutter prism centres are within the triangle.
            for (int i = 0; i < EdgeIndex.Length; i += 3)
            {
                Vector3 vHit = (Edges[EdgeIndex[i]].IntersectPlane(pTri) + Edges[EdgeIndex[i+1]].IntersectPlane(pTri) + Edges[EdgeIndex[i+2]].IntersectPlane(pTri)) / 3f;

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

        public bool PrismEdgeWithinTriangle(int Edge, Vector3 V0, Vector3 V1, Vector3 V2)
        {
            Plane pTri = Plane.FromTriangle(V0, V1, V2);

            // Check to see if all the cookie cutter prism centres are within the triangle.
            Vector3 vHit = Edges[EdgeIndex[Edge]].IntersectPlane(pTri);

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

        /// <summary>
        /// Returns the Edge number on which this point sits, or -1 if it does not sit on a cut prism edge.
        /// </summary>
        /// <param name="vHit"></param>
        /// <param name="pNormal"></param>
        /// <returns></returns>
        public int FindNearEdge(Vector3 vHit, Plane pNormal)
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

                    if (D < 0.001f && D < fBestDistance)    // Within 1mm
                    {
                        fBestDistance = D;
                        BestIndex = i;
                    }
                }
            }

            return BestIndex;
        }

        public int FindPreviousEdge(int Edge)
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

                if (EdgeIndex[iEdgeCurrent] == Edge && !FaceInternal[iEdgePrev])
                    return iEdgePrev;
            }

            return -1;
        }

        public int FindNextEdge(int Edge)
        {
            // We are looking for a triangle external face
            for (int i = 0; i < EdgeIndex.Length; i++)
            {
                int iFaceNumber = i / 3;
                int iEdgeCurrent = (i % 3);
                int iEdgeNext = (i % 3) + 1;
                if (iEdgeNext > 2)
                    iEdgeNext = 0;

                iEdgeCurrent += iFaceNumber * 3;
                iEdgeNext += iFaceNumber * 3;

                if (EdgeIndex[iEdgeCurrent] == Edge && !FaceInternal[iEdgeCurrent])
                    return iEdgeNext;
            }

            return -1;
        }

        /// <summary>
        /// Finds the next face in a Clockwise direction from the face provided
        /// </summary>
        /// <param name="Face"></param>
        /// <returns></returns>
        public int FindNextFace(int Face)
        {
            int FaceEdge0 = Face;

            FaceEdge0 = EdgeIndex[FaceEdge0];

            for (int i = 0; i < EdgeIndex.Length; i++)
            {
                if (FaceInternal[i])
                    continue;   // We don't want to find internal faces

                int iTriNumber = i / 3;
                int iEdgeNext = (i % 3) + 1;
                if (iEdgeNext > 2)
                    iEdgeNext = 0;

                iEdgeNext += iTriNumber * 3;

                iEdgeNext = EdgeIndex[iEdgeNext];

                if (iEdgeNext == FaceEdge0)
                    return i;
            }
            
            return -1;
        }
        
        /// <summary>
        /// Finds the next face in an Anti Clockwise direction from the face provided
        /// </summary>
        /// <param name="Face"></param>
        /// <returns></returns>
        public int FindPreviousFace(int Face)
        {
            int FaceEdge0 = Face;
            int FaceEdge1 = (Face % 3) + 1;
            if (FaceEdge1 > 2)
                FaceEdge1 = 0;

            FaceEdge1 += (Face / 3) * 3;

            FaceEdge1 = EdgeIndex[FaceEdge1];

            for (int i = 0; i < EdgeIndex.Length; i++)
            {
                if (FaceInternal[i])
                    continue;   // We don't want to find internal faces

                int iTriNumber = i / 3;
                int iEdgeCurrent = (i % 3);

                iEdgeCurrent += iTriNumber * 3;

                iEdgeCurrent = EdgeIndex[iEdgeCurrent];

                if (iEdgeCurrent == FaceEdge1)
                    return i;
            }

            return -1;
        }

        public int FindFaceStartingHere(int Edge)
        {
            for(int i = 0; i < Edges.Length; i++)
            {
                if (FaceInternal[i])
                    continue;

                int iTriNumber = i / 3;
                int iEdgeCurrent = (i % 3);

                iEdgeCurrent += iTriNumber * 3;

                iEdgeCurrent = EdgeIndex[iEdgeCurrent];

                if (iEdgeCurrent == Edge)
                    return i;
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

                if (iFaceNumber * 3 + iEdgePrev < 0)
                {
                    UnityEngine.Debug.LogError("Invalid Clipping Face");
                }

                return Edges[EdgeIndex[iFaceNumber * 3 + iEdgePrev]].IntersectPlane(pNormal);
            } else
            {
                // We have found an edge. Find a triangle which has an external face with this as it's destination
                int iEdge = FindPreviousEdge(iStartEdge);
                if (iEdge == -1 || EdgeIndex[iEdge] == -1)
                {
                    UnityEngine.Debug.LogError("Invalid Clipping Edge");
                }

                return Edges[EdgeIndex[iEdge]].IntersectPlane(pNormal);
            }
        }

        public Vector3 PreviousSegment(Vector3 vStart, Plane pNormal)
        {
            // Check that we haven't started on an edge
            int iStartEdge = FindNearEdge(vStart, pNormal);

            if (iStartEdge == -1)
            {
                // We have hit a regular prism face, find the face
                int iClipFace = FindClippingFace(vStart);

                int iFaceNumber = iClipFace / 3;
                int iEdgeNext = (iClipFace % 3) + 1;  // Face 0 has verts 0->1, Face 1 has verts 1->2 etc., so this is the next prism edge
                if (iEdgeNext > 2)
                    iEdgeNext = 0;

                return Edges[EdgeIndex[iFaceNumber * 3 + iEdgeNext]].IntersectPlane(pNormal);
            }
            else
            {
                // We have found an edge. Find a triangle which has an external face with this as it's destination
                int iEdge = FindNextEdge(iStartEdge);
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

        private int NextPointInLine(Vector3Line v3Line, ref float MinD, List<PositionAndNormal> v3MeshData, float Threshold = 0.001f)
        {
            float BestT = 10f;
            int BestResult = -1;
            for (int i = 0 ; i < v3MeshData.Count ; i++)
            {
                var vCandidate = v3MeshData[i];
                float t = Vector3.Dot(v3Line.Dir(), (vCandidate.Pos - v3Line.Origin())) / Vector3.Dot(v3Line.Dir(), v3Line.Dir());

                if (t >= -0.001f && t < 1.001f && t < BestT && t > MinD)
                {
                    Vector3 Rejoin = v3Line.Origin() + v3Line.Dir() * t;
                    float Distance = (Rejoin - vCandidate.Pos).Len();

                    if (Distance < Threshold)
                    {
                        BestT = t;
                        BestResult = i;
                    }
                }
            }
            
            MinD = BestT;
            return BestResult;
        }

        public List<int> MarkVerts(List<PositionAndNormal> v3MeshData)
        {
            List<int> HullPoints = new List<int>();

            // Find a point to start
            Vector3 vstart = null;

            for (int i = 0; i < Faces.Length; i++)
            {
                if (!FaceInternal[i])
                {
                    vstart = Edges[EdgeIndex[i]].Origin();
                    break;
                }
            }

            Plane pNormal = new Plane(Edges[EdgeIndex[0]].Origin(), Edges[EdgeIndex[1]].Origin(), Edges[EdgeIndex[2]].Origin());

            // Get the list of external edges
            List<Vector3> lstEdges = new List<Vector3>();
            while (lstEdges.FindIndex(x => (x - vstart).Len() < 0.001f) == -1)
            {
                lstEdges.Add(vstart);

                vstart = NextSegment(vstart, pNormal);
            } 

            lstEdges.Reverse();

            for (int i = 0; i < lstEdges.Count ; i++)
            {
                int iNextVert = i + 1;

                if (iNextVert >= lstEdges.Count)
                    iNextVert = 0;

                Vector3Line v3Line = new Vector3Line(lstEdges[i], lstEdges[iNextVert]);

                float D = -1f;
                int iFound = NextPointInLine(v3Line, ref D, v3MeshData, 0.025f);

                while(iFound != -1)
                {
                    HullPoints.Add(iFound);
                    iFound = NextPointInLine(v3Line, ref D, v3MeshData, 0.025f);
                }
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
