﻿using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Assets.Scripts.LfdReader;
using Assets.Scripts.Palette;

namespace Assets.Scripts
{
    public class MeshCreator
    {
        private readonly GameObject _baseObject;
        private readonly GameObject _baseSection;
        private readonly GameObject _baseHardpoint;
        private readonly Transform _baseTransform;
        private readonly IPaletteMapper _paletteMapper;
        private readonly CoordinateConverter _coordinateConverter;

        public struct MeshInfo
        {
            public MeshInfo(Mesh mesh, Vector3 center)
            {
                Mesh = mesh;
                Center = center;
            }

            public readonly Mesh Mesh;
            public readonly Vector3 Center;
        }

        public MeshCreator(CoordinateConverter coordinateConverter, GameObject baseObject, GameObject baseSection, GameObject baseHardpoint, Transform transform, IPaletteMapper paletteMapper)
        {
            _coordinateConverter = coordinateConverter;
            _baseObject = baseObject;
            _baseSection = baseSection;
            _baseHardpoint = baseHardpoint;
            _baseTransform = transform;
            _paletteMapper = paletteMapper;
        }

        public GameObject CreateGameObject(SectionRecord[] sections, HardpointRecord[][] hardpoints, int lodLevel = 0, int? flightGroupColor = null, int[] disabledMarkingSectionIndices = null)
        {
            var meshInfos = CreateMeshes(sections, flightGroupColor, disabledMarkingSectionIndices, lodLevel);

            return CreateGameObject(meshInfos, hardpoints);
        }

        public GameObject CreateGameObject(MeshInfo[] meshInfos, HardpointRecord[][] hardpoints)
        {
            var gameObject = UnityEngine.Object.Instantiate(_baseObject, _baseTransform);

            for (var meshInfoIndex = 0; meshInfoIndex < meshInfos.Length; meshInfoIndex++)
            {
                var sectionObject = UnityEngine.Object.Instantiate(_baseSection, _baseTransform != null ? _baseTransform : gameObject.transform);

                SetMesh(sectionObject, meshInfos[meshInfoIndex]);
                sectionObject.name = $"Section{meshInfoIndex}";

                if (hardpoints.Length > meshInfoIndex)
                {
                    for (var hardpointIndex = 0; hardpointIndex < hardpoints[meshInfoIndex].Length; hardpointIndex++)
                    {
                        var hardpoint = hardpoints[meshInfoIndex][hardpointIndex];

                        var hardpointObject = UnityEngine.Object.Instantiate(_baseHardpoint, sectionObject.transform);
                        hardpointObject.name = $"Hardpoint{hardpointIndex}";
                        hardpointObject.transform.position = _coordinateConverter.ScaleFactor * hardpoint.Position;
                    }
                }
            }

            return gameObject;
        }

        public void SetMesh(GameObject sectionObject, SectionRecord section, int? flightGroupColor, bool enableMarkings, int lodLevel = 0)
        {
            var meshInfo = CreateMesh(section, flightGroupColor, enableMarkings, lodLevel);

            SetMesh(sectionObject, meshInfo);
        }

        public void SetMesh(GameObject sectionObject, MeshInfo meshInfo)
        {
            var meshFilter = sectionObject.GetComponent<MeshFilter>();

            meshFilter.mesh = meshInfo.Mesh;
            sectionObject.transform.localPosition = meshInfo.Center;
        }

        public MeshInfo[] CreateMeshes(SectionRecord[] sections, int? flightGroupColor, int[] disabledMarkingSectionIndices = null, int lodLevel = 0)
        {
            var meshInfos = new MeshInfo[sections.Length];

            for (var i = 0; i < sections.Length; i++)
                meshInfos[i] = CreateMesh(sections[i], flightGroupColor, !disabledMarkingSectionIndices?.Contains(i) ?? true, lodLevel);

            return meshInfos;
        }

        public MeshInfo CreateMesh(SectionRecord section, int? flightGroupColor, bool enableMarkings, int lodLevel = 0)
        {
            var mesh = new Mesh();

            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            var markTriangles = new List<int>();
            var normals = new List<Vector3>();
            var uv = new List<Vector2>();

            var lodRecord = section.LodRecords[Math.Min(lodLevel, section.LodRecords.Count - 1)];

            var originalVertices = lodRecord.Vertices.Select(x => _coordinateConverter.ConvertCoordinates(x)).ToList();
            var sectionNormals = lodRecord.VertexNormals.Select(x => _coordinateConverter.ConvertCoordinates(-x).normalized).ToArray();

            for (var i = 0; i < lodRecord.PolygonLineRecords.Length; i++)
            {
                var normal = _coordinateConverter.ConvertCoordinates(-lodRecord.Normals[i]).normalized;

                var polygon = lodRecord.PolygonLineRecords[i];

                var colorInfo = _paletteMapper.GetColorInfo(lodRecord.Colors[i], flightGroupColor);

                if (polygon.VertexIndices.Length == 2)
                {
                    // Line
                    var lineVertices = new Vector3[2];
                    for (var j = 0; j < 2; j++)
                        lineVertices[j] = _coordinateConverter.ConvertCoordinates(polygon.Vertices[j]);

                    var radius = polygon.LineRadius * _coordinateConverter.ScaleFactor;

                    var linePolygonCount = CreateCylinderFromLine(lineVertices[0], lineVertices[1], normal, radius, out var linePolygonVertices, out var lineVertexNormals, out var lineVertexIndices, out var linePolygonNormals);

                    for (var j = 0; j < linePolygonCount; j++)
                    {
                        NormalizeAndReorder(linePolygonNormals[j], linePolygonVertices[j], lineVertexNormals[j]);

                        var lineTriangleIndices = GetTriangleIndices(linePolygonVertices[j].ToArray());

                        CopyVertices(linePolygonVertices[j], lineVertexNormals[j].ToArray(), colorInfo.IsFlatShaded ? false : polygon.ShadeFlag, lineVertexIndices[j].ToArray(), lineTriangleIndices, colorInfo, linePolygonNormals[j], vertices, triangles, normals, uv);
                    }
                }
                else
                {
                    // Polygon
                    var vertexIndices = polygon.VertexIndices.Reverse().ToArray();

                    if (Vector3.Dot(Vector3.Cross(originalVertices[vertexIndices[1]] - originalVertices[vertexIndices[0]], originalVertices[vertexIndices[2]] - originalVertices[vertexIndices[0]]), normal) < 0f)
                        normal = -normal;   // The normal for this section is flipsy turvey

                    var triangleIndices = GetTriangleIndices(polygon.Vertices);

                    List<PolygonCutter.Triangle> baseTris = PolygonCutter.Triangle.BuildTriangleList(triangleIndices, vertexIndices, originalVertices, sectionNormals);
                    var baseMeshPoints = PolygonCutter.PositionAndNormal.BuildPositionAndNormalList(originalVertices, sectionNormals, normal);

                    PolygonCutter.Cutter.SanitizeNormals(baseTris, baseMeshPoints);

                    List<PolygonCutter.MarkingDetail> markDetails = new List<PolygonCutter.MarkingDetail>();

                    if (enableMarkings)
                        markDetails = GetMarkingsOnMesh(
                            lodRecord.MarkRecords.Where(x => x.Key == i).SelectMany(x => x.Value),
                            flightGroupColor,
                            originalVertices.ToArray(),
                            vertices,
                            markTriangles,
                            normals,
                            uv,
                            sectionNormals,
                            normal,
                            polygon);

                    if (markDetails.Count > 0)
                    {
                        int DebugIndex = -1;
                        List<PolygonCutter.Triangle> newBaseTris = new List<PolygonCutter.Triangle>();
                        List<PolygonCutter.Triangle> markingTris = new List<PolygonCutter.Triangle>(baseTris);  // Used for creating the marking cut
                        List<PolygonCutter.Triangle> selfCutMarkingTris = null;   // Used for cutting higher priorty markings into lower priority ones

                        for (int j = 0; j < markDetails.Count; j++)
                        {
                            var markDetail = markDetails[j];
                            PolygonCutter.CookieCutter cc = PolygonCutter.CookieCutter.MakeCC(markDetail.TriangleList, new PolygonCutter.Vector3(normal.x, normal.y, normal.z));

                            foreach (PolygonCutter.Triangle t in baseTris)
                            {
                                if ((j <= DebugIndex || DebugIndex == -1) && PolygonCutter.Cutter.ChopPolygonCheck(baseMeshPoints, t, cc))
                                {
                                    newBaseTris.AddRange(PolygonCutter.Cutter.ChopPolygon(t, cc, baseMeshPoints));  // Replace the old triangle with whatever we spit out
                                }
                                else
                                {
                                    newBaseTris.Add(t); // No cuts on this face, add the output
                                }
                            }

                            // Clear out the list of triangles as we want to replace it and generate a new list
                            baseTris.Clear();
                            baseTris.AddRange(newBaseTris);
                            newBaseTris.Clear();
                        }

                        // Carving leaves a lot of extra verts which could be killed, clean up the new triangles
                        int CulledVerts = PolygonCutter.Cutter.RemoveUselessPoints(baseTris, baseMeshPoints, 0.99999999f);  // 0.08 degrees


                        // Now get the marking triangles
                        for (int j = 0; j < markDetails.Count; j++)
                        {
                            var markDetail = markDetails[j];
                            PolygonCutter.CookieCutter cc = PolygonCutter.CookieCutter.MakeCC(markDetail.TriangleList, new PolygonCutter.Vector3(normal.x, normal.y, normal.z));

                            // Use newBaseTris for the marking tris
                            newBaseTris.Clear();

                            foreach (PolygonCutter.Triangle t in markingTris)
                            {
                                if ((j <= DebugIndex || DebugIndex == -1) && PolygonCutter.Cutter.ChopPolygonCheck(baseMeshPoints, t, cc))
                                {
                                    // Get the inner cutter triangles
                                    newBaseTris.AddRange(PolygonCutter.Cutter.ChopPolygon(t, cc, baseMeshPoints, true));
                                } // If we don't cut this triangle at all exclude it from the output
                            }

                            // Carve up newBaseTris with higher priority marks to get final mark set
                            for (int k = j + 1; k < markDetails.Count; k++)
                            {
                                selfCutMarkingTris = new List<PolygonCutter.Triangle>();
                                bool CutsMade = false;

                                // Generate the new cookie cutter
                                cc = PolygonCutter.CookieCutter.MakeCC(markDetails[k].TriangleList, new PolygonCutter.Vector3(normal.x, normal.y, normal.z));

                                foreach (PolygonCutter.Triangle t2 in newBaseTris)
                                {
                                    if ((k <= DebugIndex || DebugIndex == -1) && PolygonCutter.Cutter.ChopPolygonCheck(baseMeshPoints, t2, cc))
                                    {
                                        // Remove high priority meshes
                                        selfCutMarkingTris.AddRange(PolygonCutter.Cutter.ChopPolygon(t2, cc, baseMeshPoints));
                                        CutsMade = true;
                                    }
                                    else
                                    {
                                        selfCutMarkingTris.Add(t2);
                                    }
                                }

                                if (CutsMade)
                                {
                                    newBaseTris.Clear();
                                    newBaseTris.AddRange(selfCutMarkingTris);
                                    selfCutMarkingTris.Clear();
                                }
                            }

                            // Clear up the extra unnecessary verts
                            CulledVerts += PolygonCutter.Cutter.RemoveUselessPoints(newBaseTris, baseMeshPoints, 0.99999999f);

                            // Now store
                            markDetail.DrawTriangles.AddRange(newBaseTris);
                        }

                        //if (PolygonCutter.Cutter.ReportMessages && CulledVerts > 0)
                        //    UnityEngine.Debug.Log($"Removed {CulledVerts} carving verts");
                    }



                    PolygonCutter.Triangle.ReverseTriangleList(baseTris, baseMeshPoints, originalVertices, out sectionNormals, out triangleIndices, out vertexIndices);

                    CopyVertices(originalVertices, sectionNormals, colorInfo.IsFlatShaded ? false : polygon.ShadeFlag, vertexIndices, triangleIndices, colorInfo, normal, vertices, triangles, normals, uv);

                    if (triangleIndices.Length > 0 && polygon.TwoSidedFlag)
                        CopyVertices(originalVertices, sectionNormals, colorInfo.IsFlatShaded ? false : polygon.ShadeFlag, vertexIndices, triangleIndices.Reverse().ToArray(), colorInfo, -normal, vertices, triangles, normals, uv);

                    foreach (var markDetail in markDetails)
                    {
                        PolygonCutter.Triangle.ReverseTriangleList(markDetail.DrawTriangles, baseMeshPoints, originalVertices, out sectionNormals, out triangleIndices, out vertexIndices);

                        CopyVertices(originalVertices, sectionNormals, markDetail.Colour.IsFlatShaded ? false : markDetail.ShadeFlag, vertexIndices, triangleIndices, markDetail.Colour, normal, vertices, triangles, normals, uv);

                        if (triangleIndices.Length > 0 && markDetail.TwoSidedFlag)
                            CopyVertices(originalVertices, sectionNormals, markDetail.Colour.IsFlatShaded ? false : markDetail.ShadeFlag, vertexIndices, triangleIndices.Reverse().ToArray(), markDetail.Colour, -normal, vertices, triangles, normals, uv);
                    }
                }
            }

            // May want to make recentering an optional feature for the library rather than mandatory if there turns out to
            // be a practical benefit to doing so.
            var center = _coordinateConverter.ConvertCoordinates((lodRecord.BoundingBox1 + lodRecord.BoundingBox2) / 2);

            var verticesArray = vertices.Select(x => x - center).ToArray();

            mesh.vertices = verticesArray;

            mesh.triangles = triangles.ToArray();

            if (normals.Count > 0)
                mesh.normals = normals.ToArray();

            mesh.uv = uv.ToArray();

            return new MeshInfo(mesh, center);
        }

        private List<PolygonCutter.MarkingDetail> GetMarkingsOnMesh(IEnumerable<MarkRecord> markRecords, int? flightGroupColor, Vector3[] originalVertices, List<Vector3> vertices, List<int> markTriangles, List<Vector3> normals, List<Vector2> uv, Vector3[] sectionNormals, Vector3 normal, PolygonLineRecord polygon)
        {
            List<PolygonCutter.MarkingDetail> markPatches = new List<PolygonCutter.MarkingDetail>();

            foreach (var markRecord in markRecords)
            {
                var dataIndices = 0;
                if (markRecord.MarkType >= 3 && markRecord.MarkType <= 9)
                    dataIndices = markRecord.MarkType;
                else if (markRecord.MarkType >= 0x11 && markRecord.MarkType <= 0xfe)
                    dataIndices = 2;

                if (dataIndices == 0)
                    continue;

                var markVertices = new List<Vector3>();

                for (var dataIndex = 0; dataIndex < dataIndices * 3; dataIndex += 3)
                {
                    var index = (markRecord.Data[dataIndex] / 2 - 1) % polygon.VertexIndices.Length;

                    if (index < 0)
                        index += polygon.VertexIndices.Length;

                    var referenceVertex = originalVertices[polygon.VertexIndices[index]];

                    var leftVertexIndex = index - 1;
                    if (leftVertexIndex < 0)
                        leftVertexIndex += polygon.VertexIndices.Length;

                    var rightVertexIndex = index + 1;
                    if (rightVertexIndex >= polygon.VertexIndices.Length)
                        rightVertexIndex -= polygon.VertexIndices.Length;

                    var leftVertex = originalVertices[polygon.VertexIndices[leftVertexIndex]];
                    var rightVertex = originalVertices[polygon.VertexIndices[rightVertexIndex]];

                    var leftFactor = markRecord.Data[dataIndex + 1];
                    var rightFactor = markRecord.Data[dataIndex + 2];

                    var vertex = referenceVertex + ((leftVertex - referenceVertex) * leftFactor + (rightVertex - referenceVertex) * rightFactor) / 32f;

                    markVertices.Add(vertex);
                }

                var markColorInfo = _paletteMapper.GetColorInfo(markRecord.MarkColor, flightGroupColor);

                if (dataIndices == 2)
                {
                    // Need to generate our own vertices for lines
                    var scaleFactor = markRecord.MarkType * _coordinateConverter.ScaleFactor / 2;

                    // TODO: modify the corners based on additional data in the record (once deciphered)
                    var lineVertices = new List<Vector3>
                    {
                        markVertices[0] + Quaternion.LookRotation(markVertices[1] - markVertices[0], normal) * Vector3.left * scaleFactor,
                        markVertices[0] + Quaternion.LookRotation(markVertices[1] - markVertices[0], normal) * Vector3.right * scaleFactor,
                        markVertices[1] + Quaternion.LookRotation(markVertices[0] - markVertices[1], normal) * Vector3.left * scaleFactor,
                        markVertices[1] + Quaternion.LookRotation(markVertices[0] - markVertices[1], normal) * Vector3.right * scaleFactor
                    };

                    PolygonCutter.MarkingDetail markDetail = new PolygonCutter.MarkingDetail();
                    markDetail.Colour = markColorInfo;
                    markDetail.TwoSidedFlag = polygon.TwoSidedFlag;
                    markDetail.ShadeFlag = polygon.ShadeFlag;

                    markDetail.TriangleList.Add(new PolygonCutter.Vector3(lineVertices[0].x, lineVertices[0].y, lineVertices[0].z));
                    markDetail.TriangleList.Add(new PolygonCutter.Vector3(lineVertices[1].x, lineVertices[1].y, lineVertices[1].z));
                    markDetail.TriangleList.Add(new PolygonCutter.Vector3(lineVertices[2].x, lineVertices[2].y, lineVertices[2].z));

                    markDetail.TriangleList.Add(new PolygonCutter.Vector3(lineVertices[0].x, lineVertices[0].y, lineVertices[0].z));
                    markDetail.TriangleList.Add(new PolygonCutter.Vector3(lineVertices[2].x, lineVertices[2].y, lineVertices[2].z));
                    markDetail.TriangleList.Add(new PolygonCutter.Vector3(lineVertices[3].x, lineVertices[3].y, lineVertices[3].z));

                    markPatches.Add(markDetail);
                }
                else
                {
                    if (HasIntersectionPoint(markVertices, out var intersectionPoint, out var splitPoint1, out var splitPoint2))
                    {
                        // If the polygon intersects with itself, we need to split it into two separate polygons.
                        //Debug.Log($"Model has intersection point between vertices {splitPoint1} and {splitPoint2} of {markVertices.Count}.");

                        var vertexRange1 = markVertices.Take(splitPoint1).ToList();
                        var vertexRange2 = markVertices.Skip(splitPoint1).Take(splitPoint2 - splitPoint1).ToList();

                        vertexRange1.Add(intersectionPoint);
                        vertexRange1.AddRange(markVertices.Skip(splitPoint2));

                        vertexRange2.Insert(0, intersectionPoint);

                        PolygonCutter.MarkingDetail markDetail = null;

                        // Range1
                        markDetail = new PolygonCutter.MarkingDetail();
                        markDetail.Colour = markColorInfo;
                        markDetail.TwoSidedFlag = polygon.TwoSidedFlag;
                        markDetail.ShadeFlag = polygon.ShadeFlag;
                        markDetail.TriangleList = PolygonCutter.Cutter.Triangulate(vertexRange1, new PolygonCutter.Vector3(-normal.x, -normal.y, -normal.z));

                        markPatches.Add(markDetail);

                        // Range 2 (or 3 & 4)
                        if (HasIntersectionPoint(vertexRange2, out intersectionPoint, out splitPoint1, out splitPoint2))
                        {
                            // If the polygon intersects with itself, we need to split it into two separate polygons.
                            // Debug.Log($"Model has intersection point between vertices {splitPoint1} and {splitPoint2} of {vertexRange2.Count}.");

                            var vertexRange3 = vertexRange2.Take(splitPoint1).ToList();
                            var vertexRange4 = vertexRange2.Skip(splitPoint1).Take(splitPoint2 - splitPoint1).ToList();

                            vertexRange3.Add(intersectionPoint);
                            vertexRange3.AddRange(vertexRange2.Skip(splitPoint2));

                            vertexRange4.Insert(0, intersectionPoint);

                            markDetail = new PolygonCutter.MarkingDetail();
                            markDetail.Colour = markColorInfo;
                            markDetail.TwoSidedFlag = polygon.TwoSidedFlag;
                            markDetail.ShadeFlag = polygon.ShadeFlag;
                            markDetail.TriangleList = PolygonCutter.Cutter.Triangulate(vertexRange3, new PolygonCutter.Vector3(-normal.x, -normal.y, -normal.z));

                            markPatches.Add(markDetail);

                            vertexRange4.Reverse();
                            markDetail = new PolygonCutter.MarkingDetail();
                            markDetail.Colour = markColorInfo;
                            markDetail.TwoSidedFlag = polygon.TwoSidedFlag;
                            markDetail.ShadeFlag = polygon.ShadeFlag;
                            markDetail.TriangleList = PolygonCutter.Cutter.Triangulate(vertexRange4, new PolygonCutter.Vector3(-normal.x, -normal.y, -normal.z));

                            markPatches.Add(markDetail);

                        }
                        else
                        {
                            vertexRange2.Reverse();
                            markDetail = new PolygonCutter.MarkingDetail();
                            markDetail.Colour = markColorInfo;
                            markDetail.TwoSidedFlag = polygon.TwoSidedFlag;
                            markDetail.ShadeFlag = polygon.ShadeFlag;
                            markDetail.TriangleList = PolygonCutter.Cutter.Triangulate(vertexRange2, new PolygonCutter.Vector3(-normal.x, -normal.y, -normal.z));

                            markPatches.Add(markDetail);
                        }
                    }
                    else
                    {
                        PolygonCutter.MarkingDetail markDetail = new PolygonCutter.MarkingDetail();
                        markDetail.Colour = markColorInfo;
                        markDetail.TwoSidedFlag = polygon.TwoSidedFlag;
                        markDetail.ShadeFlag = polygon.ShadeFlag;
                        markDetail.TriangleList = PolygonCutter.Cutter.Triangulate(markVertices, new PolygonCutter.Vector3(-normal.x, -normal.y, -normal.z));

                        markPatches.Add(markDetail);
                    }

                    bool HasIntersectionPoint(List<Vector3> verts, out Vector3 intersection, out int firstSplit, out int secondSplit)
                    {
                        // TODO: this needs to be cleaned up
                        var projection = Get2dProjection(normal, verts.ToArray()).ToList();
                        for (var i = 0; i < projection.Count; i++)
                        {
                            var point1Start = projection[i];
                            var point1End = GetEndPoint2d(i);

                            // Skip the adjacent vertex because it won't intersect.
                            for (var j = i + 2; j < projection.Count; j++)
                            {
                                // Don't test the first and last together since they will be adjacent.
                                if (i == 0 && j == projection.Count - 1)
                                    continue;

                                var point2Start = projection[j];
                                var point2End = GetEndPoint2d(j);

                                if (AreLineSegmentsCrossing(point1Start, point1End, point2Start, point2End))
                                {
                                    var point1Start3 = verts[i];
                                    var point1End3 = GetEndPoint3d(i);
                                    var vector1 = point1End3 - point1Start3;

                                    var point2Start3 = verts[j];
                                    var point2End3 = GetEndPoint3d(j);
                                    var vector2 = point2End3 - point2Start3;

                                    firstSplit = i + 1;
                                    secondSplit = j + 1;

                                    if (ClosestPointsOnTwoLines(out var closestPoint1, out var closestPoint2, point1Start3, vector1, point2Start3, vector2))
                                    {
                                        intersection = (closestPoint1 + closestPoint2) / 2;
                                        return true;
                                    }
                                }
                            }
                        }

                        intersection = Vector3.zero;
                        firstSplit = 0;
                        secondSplit = 0;
                        return false;

                        Vector2 GetEndPoint2d(int currentIndex) => projection[(currentIndex + 1) % projection.Count];
                        Vector3 GetEndPoint3d(int currentIndex) => verts[(currentIndex + 1) % verts.Count];
                    }
                }
            }

            return markPatches;
        }

        private static void NormalizeAndReorder(Vector3 normal, List<Vector3> vertices, List<Vector3> vertexNormals = null)
        {
            if (Vector3.Angle(normal, CalculateNormal(vertices)) > 91) // uhh, wrong direction, so flip it
            {
                vertices.Reverse();

                if (vertexNormals != null)
                    vertexNormals.Reverse();
            }
        }

        private static Vector3 CalculateNormal(List<Vector3> vertices)
        {
            var normal = Vector3.zero;
            for (var i = 0; i < vertices.Count; i++)
            {
                var nextIndex = i + 1;
                if (nextIndex >= vertices.Count)
                    nextIndex = 0;

                normal += Vector3.Cross(vertices[i], vertices[nextIndex]);
            }

            return normal.normalized;
        }

        protected int CreateCylinderFromLine(Vector3 point1, Vector3 point2, Vector3 normal, float radius, out List<List<Vector3>> vertices, out List<List<Vector3>> vertexNormals, out List<List<int>> polygonIndices, out List<Vector3> polygonNormals)
        {
            // TODO: paramatarize granularity
            const int Sides = 6;
            // TODO: this seems to make a lot of lines too big. There may be a bit more going on with line size.
            //const float LineScaleFactor = 1.75f; // additional tweak fo the line radius for potentially better results. 
            //radius *= LineScaleFactor;

            vertices = new List<List<Vector3>>();
            vertexNormals = new List<List<Vector3>>();
            polygonIndices = new List<List<int>>();
            polygonNormals = new List<Vector3>();

            var rotateHalfStep = Quaternion.AngleAxis(360 / (Sides * 2), point2 - point1);

            var allVertices = new List<List<Vector3>>();
            var allVertexNormals = new List<Vector3>();
            var thisNormal = normal;
            for (var side = 0; side < Sides; side++)
            {
                allVertices.Add(new List<Vector3>
                {
                    point1 + thisNormal * radius,
                    point2 + thisNormal * radius
                });

                allVertexNormals.Add(thisNormal);

                thisNormal = rotateHalfStep * thisNormal;
                polygonNormals.Add(thisNormal);

                thisNormal = rotateHalfStep * thisNormal;
            }

            for (var side = 0; side < Sides; side++)
            {
                var nextSide = (side + 1) % Sides;

                var sideVertices = new List<Vector3>
                {
                    allVertices[side][0],
                    allVertices[side][1],
                    allVertices[nextSide][1],
                    allVertices[nextSide][0]
                };

                polygonIndices.Add(Enumerable.Range(0, 4).ToList());

                vertices.Add(sideVertices);

                vertexNormals.Add(new List<Vector3>
                {
                    allVertexNormals[side],
                    allVertexNormals[side],
                    allVertexNormals[nextSide],
                    allVertexNormals[nextSide]
                });
            }

            var cap1Normal = (point1 - point2).normalized;
            var cap2Normal = (point2 - point1).normalized;

            var cap1 = new List<Vector3>();
            var cap2 = new List<Vector3>();
            var cap1VertexNormals = new List<Vector3>();
            var cap2VertexNormals = new List<Vector3>();
            for (var side = 0; side < Sides; side++)
            {
                cap1.Add(allVertices[side][0]);
                cap2.Add(allVertices[side][1]);

                cap1VertexNormals.Add(cap1Normal);
                cap2VertexNormals.Add(cap2Normal);
            }

            vertices.Add(cap1);
            vertices.Add(cap2);
            vertexNormals.Add(cap1VertexNormals);
            vertexNormals.Add(cap2VertexNormals);
            polygonIndices.Add(Enumerable.Range(0, Sides).ToList());
            polygonIndices.Add(Enumerable.Range(0, Sides).ToList());

            polygonNormals.Add(cap1Normal);
            polygonNormals.Add(cap2Normal);

            return Sides + 2;
        }

        protected void CopyVertices(List<Vector3> originalVertices, Vector3[] sectionNormals, bool shadeFlag, int[] vertexIndices, int[] triangleIndices, ColorInfo colorInfo, Vector3 normal, List<Vector3> vertices, List<int> triangles, List<Vector3> vertexNormals, List<Vector2> uv)
        {
            var vertexLookupDictionary = triangleIndices
                .Distinct()
                .ToDictionary(x => x, x => vertexIndices[x]);

            var polygonTriangles = new List<int>();

            var currentNormals = new List<Vector3>();
            var currentVertexCount = vertices.Count();
            foreach (var vertexLookup in vertexLookupDictionary.OrderBy(x => x.Key))
            {
                vertices.Add(originalVertices[vertexLookup.Value]);
                var vertexNormal = (shadeFlag && sectionNormals.Length > 0 && sectionNormals[vertexLookup.Value] != Vector3.zero) ? sectionNormals[vertexLookup.Value] : normal;
                currentNormals.Add(vertexNormal);
            }

            polygonTriangles.AddRange(triangleIndices.Select(x => currentVertexCount + x));
            triangles.AddRange(polygonTriangles);
            vertexNormals.AddRange(currentNormals);

            uv.AddRange(Enumerable.Repeat(_paletteMapper.GetUv(colorInfo), currentNormals.Count));
        }

        protected void CopyVerticesForMarking(bool isTwoSided, float twoSidedOffset, List<Vector3> originalVertices, Vector3[] sectionNormals, bool shadeFlag, ColorInfo colorInfo, Vector3 normal, List<Vector3> vertices, List<int> triangles, List<Vector3> vertexNormals, List<Vector2> uv)
        {
            NormalizeAndReorder(normal, originalVertices);

            var markVertexIndices = Enumerable.Range(0, originalVertices.Count).ToArray();
            var markTriangleIndices = GetTriangleIndices(originalVertices.ToArray());

            CopyVertices(originalVertices, sectionNormals, shadeFlag, markVertexIndices, markTriangleIndices, colorInfo, normal, vertices, triangles, vertexNormals, uv);

            if (isTwoSided)
            {
                var flippedMarkVertices = originalVertices.Select(x => x - normal * twoSidedOffset * 2).ToList();
                CopyVertices(flippedMarkVertices, sectionNormals, shadeFlag, markVertexIndices.Reverse().ToArray(), markTriangleIndices, colorInfo, -normal, vertices, triangles, vertexNormals, uv);
            }
        }

        protected int[] GetTriangleIndices(Vector3[] vertices) => new Triangulator(Get2dProjection(CalculateNormal(vertices.ToList()), vertices)).Triangulate();

        /// <remarks>
        /// https://answers.unity.com/questions/1522620/converting-a-3d-polygon-into-a-2d-polygon.html
        /// </remarks>
        private Vector2[] Get2dProjection(Vector3 normal, Vector3[] points)
        {
            var u = Mathf.Abs(Vector3.Dot(Vector3.forward, normal)) >= 0.2f
                ? Vector3.ProjectOnPlane(Vector3.right, normal)
                : Vector3.ProjectOnPlane(Vector3.forward, normal);

            var v = Vector3.Cross(u, normal).normalized;

            return points.Select(x => Project(x)).ToArray();

            Vector2 Project(Vector3 point) => new Vector2(Vector3.Dot(point, u), Vector3.Dot(point, v));
        }

        #region 3D line interesction stuff
        /// <remarks>
        /// Slightly modified from http://wiki.unity3d.com/index.php/3d_Math_functions
        /// I wanted a 2D function, but it was easier to find a 3D one.
        /// </remarks>
        public static bool LineLineIntersection(out Vector3 intersection, Vector3 linePoint1, Vector3 lineVec1, Vector3 linePoint2, Vector3 lineVec2)
        {
            const float delta = 0.0001f;

            var lineVec3 = linePoint2 - linePoint1;
            var crossVec1and2 = Vector3.Cross(lineVec1, lineVec2);
            var crossVec3and2 = Vector3.Cross(lineVec3, lineVec2);

            var planarFactor = Vector3.Dot(lineVec3, crossVec1and2);

            // is coplanar, and not parrallel
            if (Mathf.Abs(planarFactor) < delta && crossVec1and2.sqrMagnitude > delta)
            {
                var s = Vector3.Dot(crossVec3and2, crossVec1and2) / crossVec1and2.sqrMagnitude;
                intersection = linePoint1 + (lineVec1 * s);
                return true;
            }
            else
            {
                intersection = Vector3.zero;
                return false;
            }
        }

        /// <summary>
        /// This function finds out on which side of a line segment the point is located.
        /// The point is assumed to be on a line created by linePoint1 and linePoint2. If the point is not on
        /// the line segment, project it on the line using ProjectPointOnLine() first.
        /// Returns 0 if point is on the line segment.
        /// Returns 1 if point is outside of the line segment and located on the side of linePoint1.
        /// Returns 2 if point is outside of the line segment and located on the side of linePoint2.
        /// </summary>
        /// <remarks>
        /// Slightly modified from http://wiki.unity3d.com/index.php/3d_Math_functions
        /// </remarks>
        public static int PointOnWhichSideOfLineSegment(Vector3 linePoint1, Vector3 linePoint2, Vector3 point)
        {
            Vector3 lineVec = linePoint2 - linePoint1;
            Vector3 pointVec = point - linePoint1;

            float dot = Vector3.Dot(pointVec, lineVec);

            //point is on side of linePoint2, compared to linePoint1
            if (dot > 0)
            {
                //point is on the line segment
                if (pointVec.magnitude <= lineVec.magnitude)
                {
                    return 0;
                }

                //point is not on the line segment and it is on the side of linePoint2
                else
                {
                    return 2;
                }
            }

            //Point is not on side of linePoint2, compared to linePoint1.
            //Point is not on the line segment and it is on the side of linePoint1.
            else
            {
                return 1;
            }
        }

        /// <summary>
        /// Returns true if line segment made up of pointA1 and pointA2 is crossing line segment made up of
        /// pointB1 and pointB2. The two lines are assumed to be in the same plane.
        /// </summary>
        /// <remarks>
        /// Slightly modified from http://wiki.unity3d.com/index.php/3d_Math_functions
        /// </remarks>
        public static bool AreLineSegmentsCrossing(Vector3 pointA1, Vector3 pointA2, Vector3 pointB1, Vector3 pointB2)
        {
            var lineVecA = pointA2 - pointA1;
            var lineVecB = pointB2 - pointB1;

            bool valid = ClosestPointsOnTwoLines(out var closestPointA, out var closestPointB, pointA1, lineVecA.normalized, pointB1, lineVecB.normalized);

            return valid
                // lines are not parallel
                ? (PointOnWhichSideOfLineSegment(pointA1, pointA2, closestPointA) == 0)
                    && (PointOnWhichSideOfLineSegment(pointB1, pointB2, closestPointB) == 0)
                // lines are parallel
                : false;
        }

        /// <remarks>
        /// Slightly modified from http://wiki.unity3d.com/index.php/3d_Math_functions
        /// Two non-parallel lines which may or may not touch each other have a point on each line which are closest
        /// to each other. This function finds those two points. If the lines are not parallel, the function
        /// outputs true, otherwise false.
        /// </remarks>
        public static bool ClosestPointsOnTwoLines(out Vector3 closestPointLine1, out Vector3 closestPointLine2, Vector3 linePoint1, Vector3 lineVec1, Vector3 linePoint2, Vector3 lineVec2)
        {
            var a = Vector3.Dot(lineVec1, lineVec1);
            var b = Vector3.Dot(lineVec1, lineVec2);
            var e = Vector3.Dot(lineVec2, lineVec2);

            var d = a * e - b * b;

            // lines are not parallel
            if (d != 0.0f)
            {

                var r = linePoint1 - linePoint2;
                var c = Vector3.Dot(lineVec1, r);
                var f = Vector3.Dot(lineVec2, r);

                var s = (b * f - c * e) / d;
                var t = (a * f - c * b) / d;

                closestPointLine1 = linePoint1 + lineVec1 * s;
                closestPointLine2 = linePoint2 + lineVec2 * t;

                return true;
            }
            else
            {
                closestPointLine1 = Vector3.zero;
                closestPointLine2 = Vector3.zero;

                return false;
            }
        }

        #endregion
    }
}
