using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Assets.Scripts.LfdReader;
using Assets.Scripts.Palette;

namespace Assets.Scripts
{
    public class MeshCreater
    {
        private GameObject _baseObject;
        private GameObject _baseSection;
        private GameObject _baseHardpoint;
        private Transform _baseTransform;
        private Material _baseMaterial;
        private Material _markingMaterial;
        private IPaletteMapper _paletteMapper;
        private CoordinateConverter _coordinateConverter;

        public MeshCreater(CoordinateConverter coordinateConverter, GameObject baseObject, GameObject baseSection, GameObject baseHardpoint, Transform transform, Material baseMaterial, Material markingMaterial, IPaletteMapper paletteMapper)
        {
            _coordinateConverter = coordinateConverter;
            _baseObject = baseObject;
            _baseSection = baseSection;
            _baseHardpoint = baseHardpoint;
            _baseTransform = transform;
            _baseMaterial = baseMaterial;
            _markingMaterial = markingMaterial;
            _paletteMapper = paletteMapper;
        }

        public GameObject CreateGameObject(SectionRecord[] sections, HardpointRecord[][] hardpoints, int lodLevel = 0, Color? flightGroupColor = null, int[] disabledMarkingSectionIndices = null)
        {
            var gameObject = UnityEngine.Object.Instantiate(_baseObject, _baseTransform);

            for (var sectionIndex = 0; sectionIndex < sections.Length; sectionIndex++)
            {
                var section = sections[sectionIndex];
                var sectionObject = UnityEngine.Object.Instantiate(_baseSection, _baseTransform ?? gameObject.transform);

                var transforms = sectionObject.GetComponentsInChildren<Transform>();

                var geometry = transforms.First(x => x.name == "Geometry");
                var marking = transforms.First(x => x.name == "Marking");

                SetMesh(sectionObject, geometry, marking, section, flightGroupColor, !disabledMarkingSectionIndices?.Contains(sectionIndex) ?? true, lodLevel);
                sectionObject.name = $"Section{sectionIndex}";

                if (hardpoints.Length > sectionIndex)
                {
                    for (var hardpointIndex = 0; hardpointIndex < hardpoints[sectionIndex].Length; hardpointIndex++)
                    {
                        var hardpoint = hardpoints[sectionIndex][hardpointIndex];

                        var hardpointObject = UnityEngine.Object.Instantiate(_baseHardpoint, sectionObject.transform);
                        hardpointObject.name = $"Hardpoint{hardpointIndex}";
                        hardpointObject.transform.position = _coordinateConverter.ScaleFactor * hardpoint.Position;
                    }
                }
            }

            return gameObject;
        }

        public void SetMesh(GameObject sectionObject, Transform geometryTransform, Transform markingTransform, SectionRecord section, Color? flightGroupColor, bool enableMarkings, int lodLevel = 0)
        {
            var mesh = geometryTransform.GetComponent<MeshFilter>().mesh;
            var markingMesh = markingTransform.GetComponent<MeshFilter>().mesh;

            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            var markTriangles = new List<int>();
            var normals = new List<Vector3>();
            var colors = new List<Color>();

            var lodRecord = section.LodRecords[Math.Min(lodLevel, section.LodRecords.Count - 1)];

            var originalVertices = lodRecord.Vertices.Select(x => _coordinateConverter.ConvertCoordinates(x)).ToList();
            var sectionNormals = lodRecord.VertexNormals.Select(x => _coordinateConverter.ConvertCoordinates(-x).normalized).ToArray();

            for (var i = 0; i < lodRecord.PolygonLineRecords.Length; i++)
            {
                var normal = _coordinateConverter.ConvertCoordinates(-lodRecord.Normals[i]).normalized;

                var polygon = lodRecord.PolygonLineRecords[i];

                if (polygon.VertexIndices.Length == 2)
                {
                    // Line
                    var lineVertices = new Vector3[2];
                    for (var j = 0; j < 2; j++)
                        lineVertices[j] = _coordinateConverter.ConvertCoordinates(polygon.Vertices[j]);

                    var radius = polygon.LineRadius * _coordinateConverter.ScaleFactor;

                    var linePolygonCount = CreateCylinderFromLine(lineVertices[0], lineVertices[1], normal, radius, out var linePolygonVertices, out var lineVertexIndices, out var linePolygonNormals);

                    for (var j = 0; j < linePolygonCount; j++)
                    {
                        NormalizeAndReorder(linePolygonNormals[j], linePolygonVertices[j]);

                        var lineTriangleIndices = GetTriangleIndices(linePolygonVertices[j].ToArray());

                        // TODO: need to generate new normals (from CreateCylinderFromLine) if polygon.ShadeFlag is set.
                        CopyVertices(linePolygonVertices[j], new Vector3[0], polygon.ShadeFlag, lineVertexIndices[j].ToArray(), lineTriangleIndices, _paletteMapper.GetColor(lodRecord.Colors[i], flightGroupColor), linePolygonNormals[j], vertices, triangles, normals, colors);
                    }
                }
                else
                {
                    // Polygon
                    var vertexIndices = polygon.VertexIndices.Reverse().ToArray();

                    var triangleIndices = GetTriangleIndices(polygon.Vertices);

                    CopyVertices(originalVertices, sectionNormals, polygon.ShadeFlag, vertexIndices, triangleIndices, _paletteMapper.GetColor(lodRecord.Colors[i], flightGroupColor), normal, vertices, triangles, normals, colors);

                    if (enableMarkings)
                        SetMarkingsOnMesh(
                            lodRecord.MarkRecords.Where(x => x.Key == i).SelectMany(x => x.Value),
                            flightGroupColor,
                            originalVertices.ToArray(),
                            vertices,
                            markTriangles,
                            normals,
                            colors,
                            sectionNormals,
                            normal,
                            polygon);

                    if (triangleIndices.Length > 0 && polygon.TwoSidedFlag)
                        CopyVertices(originalVertices, sectionNormals, polygon.ShadeFlag, vertexIndices, triangleIndices.Reverse().ToArray(), _paletteMapper.GetColor(lodRecord.Colors[i], flightGroupColor), -normal, vertices, triangles, normals, colors);
                }
            }

            // May want to make recentering an optional feature for the library rather than mandatory if there turns out to
            // be a practical benefit to doing so.
            var center = _coordinateConverter.ConvertCoordinates((lodRecord.BoundingBox1 + lodRecord.BoundingBox2) / 2);
                        
            sectionObject.transform.localPosition = center;
                        
            var verticesArray = vertices.Select(x => x - center).ToArray();
                        
            mesh.vertices = verticesArray;
            markingMesh.vertices = verticesArray;

            mesh.triangles = triangles.ToArray();
            markingMesh.triangles = markTriangles.ToArray();

            if (normals.Count > 0)
            {
                mesh.normals = normals.ToArray();
                markingMesh.normals = normals.ToArray();
            }

            mesh.colors = colors.ToArray();
            markingMesh.colors = colors.ToArray();
        }

        private void SetMarkingsOnMesh(IEnumerable<MarkRecord> markRecords, Color? flightGroupColor, Vector3[] originalVertices, List<Vector3> vertices, List<int> markTriangles, List<Vector3> normals, List<Color> colors, Vector3[] sectionNormals, Vector3 normal, PolygonLineRecord polygon)
        {
            // TODO: terrible; fix
            var markOffset = 0.00f;
            foreach (var markRecord in markRecords)
            {
                markOffset += 0.02f;

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

                    vertex += normal * markOffset;

                    markVertices.Add(vertex);
                }

                Vector3[] CalculateMarkingNormals(List<Vector3> verticesToUse)
                {
                    if (sectionNormals.Length == 0 || !polygon.ShadeFlag)
                        return new Vector3[0];

                    var markNormals = new List<Vector3>();

                    foreach (var vertex in verticesToUse)
                    {
                        var markNormal = Vector3.zero;

                        //markNormal = AzrapseNormalInterpolation(originalVertices, sectionNormals, polygon, vertex, markNormal);
                        markNormal = RobNormalInterpolation(originalVertices, sectionNormals, polygon, vertex, markNormal);

                        markNormal.Normalize();

                        markNormals.Add(markNormal);
                    }

                    return markNormals.ToArray();
                }

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

                    CopyVerticesForMarking(polygon.TwoSidedFlag, markOffset, lineVertices, CalculateMarkingNormals(lineVertices), polygon.ShadeFlag, _paletteMapper.GetColor(markRecord.MarkColor, flightGroupColor), normal, vertices, markTriangles, normals, colors);
                }
                else
                {
                    if (HasIntersectionPoint(out var intersectionPoint, out var splitPoint1, out var splitPoint2))
                    {
                        // If the polygon intersects with itself, we need to split it into two separate polygons.
                        Debug.Log($"Model has intersection point between vertices {splitPoint1} and {splitPoint2} of {markVertices.Count}.");

                        var vertexRange1 = markVertices.Take(splitPoint1).ToList();
                        var vertexRange2 = markVertices.Skip(splitPoint1).Take(splitPoint2 - splitPoint1).ToList();

                        vertexRange1.Add(intersectionPoint);
                        vertexRange1.AddRange(markVertices.Skip(splitPoint2));

                        vertexRange2.Insert(0, intersectionPoint);

                        // First polygon
                        CopyVerticesForMarking(polygon.TwoSidedFlag, markOffset, vertexRange1, CalculateMarkingNormals(vertexRange1), polygon.ShadeFlag, _paletteMapper.GetColor(markRecord.MarkColor, flightGroupColor), normal, vertices, markTriangles, normals, colors);

                        // Second polygon 
                        CopyVerticesForMarking(polygon.TwoSidedFlag, markOffset, vertexRange2, CalculateMarkingNormals(vertexRange2), polygon.ShadeFlag, _paletteMapper.GetColor(markRecord.MarkColor, flightGroupColor), normal, vertices, markTriangles, normals, colors);
                    }
                    else
                    {
                        CopyVerticesForMarking(polygon.TwoSidedFlag, markOffset, markVertices, CalculateMarkingNormals(markVertices), polygon.ShadeFlag, _paletteMapper.GetColor(markRecord.MarkColor, flightGroupColor), normal, vertices, markTriangles, normals, colors);
                    }

                    bool HasIntersectionPoint(out Vector3 intersection, out int firstSplit, out int secondSplit)
                    {
                        // TODO: this needs to be cleaned up
                        var projection = Get2dProjection(normal, markVertices.ToArray()).ToList();
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
                                    var point1Start3 = markVertices[i];
                                    var point1End3 = GetEndPoint3d(i);
                                    var vector1 = point1End3 - point1Start3;

                                    var point2Start3 = markVertices[j];
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
                        Vector3 GetEndPoint3d(int currentIndex) => markVertices[(currentIndex + 1) % markVertices.Count];
                    }
                }
            }
        }

        private static Vector3 AzrapseNormalInterpolation(Vector3[] originalVertices, Vector3[] sectionNormals, PolygonLineRecord polygon, Vector3 vertex, Vector3 markNormal)
        {
            var totalDistance = 0f;
            var polygonVertexCount = polygon.VertexIndices.Length;
            for (int i = 0; i < polygonVertexCount; i++)
            {
                var otherVertexIndex = polygon.VertexIndices[i];
                var distance = Vector3.Distance(vertex, originalVertices[otherVertexIndex]);
                totalDistance += distance;
            }
            for (int i = 0; i < polygonVertexCount; i++)
            {
                var otherVertexIndex = polygon.VertexIndices[i];
                var distance = Vector3.Distance(vertex, originalVertices[otherVertexIndex]);
                var weight = 1 - distance / totalDistance;
                markNormal += weight * sectionNormals[otherVertexIndex];
            }
            markNormal /= polygonVertexCount;
            return markNormal;
        }

        private static Vector3 RobNormalInterpolation(Vector3[] originalVertices, Vector3[] sectionNormals, PolygonLineRecord polygon, Vector3 vertex, Vector3 markNormal)
        {
            foreach (var otherVertexIndex in polygon.VertexIndices)
                markNormal += sectionNormals[otherVertexIndex] / Vector3.Distance(originalVertices[otherVertexIndex], vertex);
            return markNormal;
        }

        private static void NormalizeAndReorder(Vector3 normal, List<Vector3> markVertices)
        {
            if (Vector3.Angle(normal, CalculateNormal(markVertices)) > 91) // uhh, wrong direction, so flip it
                markVertices.Reverse();
        }

        private static Vector3 CalculateNormal(List<Vector3> vertices)
        {
            var normal = Vector3.zero;
            for (var i = 0; i < vertices.Count; i++)
            {
                var nextIndex = i + 1;
                if (nextIndex >= vertices.Count)
                    nextIndex = 0;

                normal = normal + Vector3.Cross(vertices[i], vertices[nextIndex]);
            }

            return normal.normalized;
        }

        protected int CreateCylinderFromLine(Vector3 point1, Vector3 point2, Vector3 normal, float radius, out List<List<Vector3>> vertices, out List<List<int>> polygonIndices, out List<Vector3> polygonNormals)
        {
            // TODO: check to see if we should add vertex normals and add them if necessary
            // TODO: paramatarize granularity
            const int Sides = 6;

            vertices = new List<List<Vector3>>();
            polygonIndices = new List<List<int>>();
            polygonNormals = new List<Vector3>();

            var rotateHalfStep = Quaternion.AngleAxis(360 / (Sides * 2), point2 - point1);

            var allVertices = new List<List<Vector3>>();
            var thisNormal = normal;
            for (var side = 0; side < Sides; side++)
            {
                thisNormal = rotateHalfStep * thisNormal;
                polygonNormals.Add(thisNormal);

                thisNormal = rotateHalfStep * thisNormal;

                allVertices.Add(new List<Vector3>
                {
                    point1 + thisNormal * radius,
                    point2 + thisNormal * radius
                });
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
            }

            var cap1 = new List<Vector3>();
            var cap2 = new List<Vector3>();
            for (var side = 0; side < Sides; side++)
            {
                cap1.Add(allVertices[side][0]);
                cap2.Add(allVertices[side][1]);
            }

            vertices.Add(cap1);
            vertices.Add(cap2);
            polygonIndices.Add(Enumerable.Range(0, Sides).ToList());
            polygonIndices.Add(Enumerable.Range(0, Sides).ToList());

            polygonNormals.Add((point1 - point2).normalized);
            polygonNormals.Add((point2 - point1).normalized);

            return Sides + 2;
        }

        protected void CopyVertices(List<Vector3> originalVertices, Vector3[] sectionNormals, bool shadeFlag, int[] vertexIndices, int[] triangleIndices, Color color, Vector3 normal, List<Vector3> vertices, List<int> triangles, List<Vector3> vertexNormals, List<Color> colors)
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
            colors.AddRange(Enumerable.Repeat(color, currentNormals.Count));
        }

        protected void CopyVerticesForMarking(bool isTwoSided, float twoSidedOffset, List<Vector3> originalVertices, Vector3[] sectionNormals, bool shadeFlag, Color color, Vector3 normal, List<Vector3> vertices, List<int> triangles, List<Vector3> vertexNormals, List<Color> colors)
        {
            NormalizeAndReorder(normal, originalVertices);

            var markVertexIndices = Enumerable.Range(0, originalVertices.Count).ToArray();
            var markTriangleIndices = GetTriangleIndices(originalVertices.ToArray());

            CopyVertices(originalVertices, sectionNormals.ToArray(), shadeFlag, markVertexIndices, markTriangleIndices, color, normal, vertices, triangles, vertexNormals, colors);

            if (isTwoSided)
            {
                var flippedMarkVertices = originalVertices.Select(x => x - normal * twoSidedOffset * 2).ToList();
                CopyVertices(flippedMarkVertices, sectionNormals.ToArray(), shadeFlag, markVertexIndices.Reverse().ToArray(), markTriangleIndices, color, -normal, vertices, triangles, vertexNormals, colors);
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

        #region 2D line interesction stuff

        ///// <summary>
        ///// Line segment-line segment intersection in 2d space by using the dot product
        ///// p1 and p2 belongs to line 1, and p3 and p4 belongs to line 2 
        ///// </summary>
        ///// <remarks>
        ///// Modified from https://www.habrador.com/tutorials/math/5-line-line-intersection/
        ///// Says this is for 2D space but accepts 3d vectors. Not sure if that's a typo or what.
        ///// </remarks>
        //public static bool AreLineSegmentsIntersectingDotProduct(Vector3 p1, Vector3 p2, Vector3 p3, Vector3 p4)
        //    => IsPointsOnDifferentSides(p1, p2, p3, p4) && IsPointsOnDifferentSides(p3, p4, p1, p2);
        //
        ///// <summary>
        ///// Are the points on different sides of a line?
        ///// </summary>
        ///// <remarks>
        ///// Slightly modified from https://www.habrador.com/tutorials/math/5-line-line-intersection/
        ///// </remarks>
        //private static bool IsPointsOnDifferentSides(Vector3 p1, Vector3 p2, Vector3 p3, Vector3 p4)
        //{
        //    // The direction of the line
        //    var lineDir = p2 - p1;
        //
        //    // The normal to a line is just flipping x and z and making z negative
        //    var lineNormal = new Vector3(-lineDir.z, lineDir.y, lineDir.x);
        //
        //    // Now we need to take the dot product between the normal and the points on the other line
        //    var dot1 = Vector3.Dot(lineNormal, p3 - p1);
        //    var dot2 = Vector3.Dot(lineNormal, p4 - p1);
        //
        //    // If you multiply them and get a negative value then p3 and p4 are on different sides of the line
        //    return dot1 * dot2 < 0f;
        //}

        #endregion

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
            Vector3 closestPointA;
            Vector3 closestPointB;
            int sideA;
            int sideB;

            Vector3 lineVecA = pointA2 - pointA1;
            Vector3 lineVecB = pointB2 - pointB1;

            bool valid = ClosestPointsOnTwoLines(out closestPointA, out closestPointB, pointA1, lineVecA.normalized, pointB1, lineVecB.normalized);

            // lines are not parallel
            if (valid)
            {

                sideA = PointOnWhichSideOfLineSegment(pointA1, pointA2, closestPointA);
                sideB = PointOnWhichSideOfLineSegment(pointB1, pointB2, closestPointB);

                if ((sideA == 0) && (sideB == 0))
                {

                    return true;
                }

                else
                {

                    return false;
                }
            }

            // lines are parallel
            else
            {

                return false;
            }
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
