using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Assets.Scripts.LfdReader;
using Assets.Scripts.Palette;

namespace PolygonCutter
{
    class MarkingDetail
    {
        public List<Vector3> TriangleList = new List<Vector3>();
        public List<PolygonCutter.Triangle> DrawTriangles = new List<Triangle>();
        public ColorInfo Colour;
        public bool TwoSidedFlag;
        public bool ShadeFlag;
    }
}