using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Tekla.Structures.Geometry3d;
using Tekla.Structures.Model;
using Tekla.Structures.Solid;
using TS = Tekla.Structures;

namespace Polymetal.Exchange.Tekla.v211
{
    internal class PartInfoClass
    {
        public struct FaceInfo
        {
            public Face Face;
            public List<Point> Points;
        }

        public struct BooleanInfo
        {
            public TS.Model.Boolean Boolean;
            public Plane Plane;
            public BooleanTypeEnum Type;
            public List<FaceInfo> Faces;
            public PartInfoClass CutPart;
        }

        public enum BooleanTypeEnum
        {
            Fitting,
            CutPlane,
            ContourPlate,
            PolyBeam,
            Beam,
            Unknown
        }

        public struct SolidInfo
        {
            public List<Point> Points;
            public Point MinPoint;
            public Point MaxPoint;
            public CoordinateSystem CoordinateSystem;
            public List<ExtrusionInfo> Extrusions;
            public OBB Obb;
            public bool Cutted;
        }

        public struct ExtrusionInfo
        {
            public List<Point> VertexList;
            public double Height;
            public CoordinateSystem System;
        }

        public Part Object
        {
            get { return _object; }
        }

        public List<Point> CenterPoints
        {
            get { return _centerPoints; }
        }

        public List<LineSegment> Segments
        {
            get { return _segments; }
        }

        public List<CoordinateSystem> CoordinateSystems
        {
            get { return _coordinateSystems; }
        }

        public List<FaceInfo> Faces
        {
            get { return _faces; }
        }

        public List<BooleanInfo> Booleans
        {
            get { return _booleans; }
        }

        public List<SolidInfo> ObjectSolid
        {
            get { return _objectSolid; }
        }

        private Part _object;
        private List<Point> _centerPoints;
        private List<LineSegment> _segments;
        private List<CoordinateSystem> _coordinateSystems;
        private List<FaceInfo> _faces;
        private List<BooleanInfo> _booleans;
        private List<SolidInfo> _objectSolid;

        internal PartInfoClass(Part part)
        {
            _object = part;
            _centerPoints = new List<Point>();
            _segments = new List<LineSegment>();
            _coordinateSystems = new List<CoordinateSystem>();
            _faces = new List<FaceInfo>();
            _booleans = new List<BooleanInfo>();
            _objectSolid = new List<SolidInfo>();

            SetFaces();
            if (_faces.Count == 0)
            {
                _centerPoints.Clear();
                return;
            }
            SetCenterPoints();
            if (_centerPoints.Count < 2)
            {
                _centerPoints.Clear();
                return;
            }
            SetSegments();
            SetCoordinateSystems();
            SetBooleans();
            SetCornerPoints();
            SetExtrusions();
        }

        private void SetExtrusions()
        {
            foreach (var boo in _booleans)
            {
                if (boo.Type == BooleanTypeEnum.Fitting || boo.Type == BooleanTypeEnum.CutPlane)
                {
                    var plane = boo.Plane;
                    var planeCS = new CoordinateSystem(plane.Origin, plane.AxisX, plane.AxisY);
                    /*foreach (var face in boo.Faces)
                    {
                        var commonCorners = new List<Point>();
                        foreach (var cp in _objectSolid)
                        {
                            var matrixTo = MatrixFactory.ToCoordinateSystem(cp.CoordinateSystem);
                            var minPoint = matrixTo.Transform(cp.MinPoint);
                            var maxPoint = matrixTo.Transform(cp.MaxPoint);
                            var intersect = face.Points.Select(p => matrixTo.Transform(p)).Any(np => PointInVolume(np, minPoint, maxPoint));

                            if (!intersect) continue;
                            commonCorners.AddRange(cp.Points);

                            var edges = _object.GetSolid(Solid.SolidCreationTypeEnum.RAW).GetEdgeEnumerator();
                            while (edges.MoveNext())
                            {
                                var edge = edges.Current as Edge;
                                if (edge == null) continue;

                                if (matrixTo.Transform(edge.StartPoint).Z >= 0) commonCorners.Add(edge.StartPoint);
                                if (matrixTo.Transform(edge.EndPoint).Z >= 0) commonCorners.Add(edge.EndPoint);
                            }

                            var extrusionInfo = GetExtrusionPoints(commonCorners, face.Points, planeCS);
                            cp.Extrusions.Add(extrusionInfo);
                        }
                    }*/
                    foreach (var cp in _objectSolid)
                    {
                        var matrixTo = MatrixFactory.ToCoordinateSystem(cp.CoordinateSystem);
                        var minPoint = matrixTo.Transform(cp.MinPoint);
                        var maxPoint = matrixTo.Transform(cp.MaxPoint);
                        var intersectFaces = new List<FaceInfo>();
                        foreach (var face in boo.Faces)
                        {
                            var intersect = face.Points.Select(p => matrixTo.Transform(p)).Any(np => PointInVolume(np, minPoint, maxPoint));
                            if (!intersect) continue;
                            intersectFaces.Add(face);
                        }
                        if (intersectFaces.Count == 0) continue;

                        var commonCorners = new List<Point>();
                        commonCorners.AddRange(cp.Points);

                        var edges = _object.GetSolid(Solid.SolidCreationTypeEnum.RAW).GetEdgeEnumerator();
                        while (edges.MoveNext())
                        {
                            var edge = edges.Current as Edge;
                            if (edge == null) continue;

                            if (matrixTo.Transform(edge.StartPoint).Z >= 0) commonCorners.Add(edge.StartPoint);
                            if (matrixTo.Transform(edge.EndPoint).Z >= 0) commonCorners.Add(edge.EndPoint);
                        }

                        var facePoints = new List<Point>();
                        foreach (var face in intersectFaces)
                        {
                            facePoints.AddRange(face.Points);
                        }
                        var extrusionInfo = GetExtrusionPoints(commonCorners, facePoints, planeCS);
                        cp.Extrusions.Add(extrusionInfo);
                    }
                }
                else if (boo.Type == BooleanTypeEnum.PolyBeam || boo.Type == BooleanTypeEnum.Beam)
                {
                    for (int i = 0; i < _objectSolid.Count; i++)
                    {
                        var posSolid = _objectSolid[i];
                        for (int j = 0; j < boo.CutPart._objectSolid.Count; j++)
                        {
                            var negSolid = boo.CutPart.ObjectSolid[j];
                            if (posSolid.Obb.Intersects(negSolid.Obb))
                            {
                                //вырезание объемом
                                /*var negMatrixTo = MatrixFactory.ToCoordinateSystem(negSolid.CoordinateSystem);
                                var extrusionPoints = new List<Point>();
                                var negMinPoint = negMatrixTo.Transform(negSolid.MinPoint);
                                foreach (var c in negSolid.Points)
                                {
                                    var tc = negMatrixTo.Transform(c);
                                    if (Math.Abs(tc.X - negMinPoint.X) < 0.001)
                                    {
                                        extrusionPoints.Add(c);
                                    }
                                }

                                var cs = new CoordinateSystem(negSolid.CoordinateSystem.Origin, negSolid.CoordinateSystem.AxisY, negSolid.CoordinateSystem.AxisX.Cross(negSolid.CoordinateSystem.AxisY));
                                negSolid.CoordinateSystem = cs;
                                SortExtrusionPoints(ref extrusionPoints, cs);
                                var height = boo.CutPart.Segments[j].Length();
                                posSolid.Extrusions.Add(new ExtrusionInfo { Height = height, System = negSolid.CoordinateSystem, VertexList = extrusionPoints });
                                 */
                                //вырезание по профилю
                                var volumeFaces = new List<FaceInfo>();
                                foreach (var f in boo.CutPart.Faces)
                                {
                                    var faceInVolume = true;
                                    foreach (var p in f.Points)
                                    {
                                        if (!PointInVolume(p, negSolid.MinPoint, negSolid.MaxPoint))
                                        {
                                            faceInVolume = false;
                                            break;
                                        }
                                    }
                                    if (!faceInVolume) continue;
                                    volumeFaces.Add(f);
                                }
                                if (volumeFaces.Count == 0) continue;
                                var profileFace = new FaceInfo() {Points = new List<Point>()};
                                foreach (var f in volumeFaces)
                                {
                                    var angle = f.Face.Normal.GetAngleBetween(negSolid.CoordinateSystem.AxisX)*180/
                                                Math.PI;
                                    if (angle%180 < 1)
                                    {
                                        profileFace = f;
                                        break;
                                    }
                                }
                                var negMatrixTo = MatrixFactory.ToCoordinateSystem(negSolid.CoordinateSystem);
                                var negMatrixFrom = MatrixFactory.FromCoordinateSystem(negSolid.CoordinateSystem);
                                var extrusionPoints = new List<Point>();
                                var negMinPoint = negMatrixTo.Transform(negSolid.MinPoint);
                                foreach (var p in profileFace.Points)
                                {
                                    var tp = negMatrixTo.Transform(p);
                                    if (Math.Abs(tp.X - negMinPoint.X) < 0.001)
                                    {
                                        extrusionPoints.Add(p);
                                    }
                                    else
                                    {
                                        tp.X = negMinPoint.X;
                                        extrusionPoints.Add(negMatrixFrom.Transform(tp));
                                    }
                                }
                                var cs = new CoordinateSystem(negSolid.CoordinateSystem.Origin,
                                    negSolid.CoordinateSystem.AxisY,
                                    negSolid.CoordinateSystem.AxisX.Cross(negSolid.CoordinateSystem.AxisY));
                                negSolid.CoordinateSystem = cs;
                                var height = boo.CutPart.Segments[j].Length();
                                if (extrusionPoints.Count > 0)
                                {
                                    posSolid.Extrusions.Add(new ExtrusionInfo
                                    {
                                        Height = height,
                                        System = negSolid.CoordinateSystem,
                                        VertexList = extrusionPoints
                                    });
                                }
                            }
                        }
                    }
                }
                else if (boo.Type == BooleanTypeEnum.ContourPlate)
                {
                    for (int i = 0; i < _objectSolid.Count; i++)
                    {
                        var posSolid = _objectSolid[i];
                        for (int j = 0; j < boo.CutPart._objectSolid.Count; j++)
                        {
                            var negSolid = boo.CutPart.ObjectSolid[j];
                            if (posSolid.Obb.Intersects(negSolid.Obb))
                            {
                                var negMatrixTo = MatrixFactory.ToCoordinateSystem(negSolid.CoordinateSystem);
                                var extrusionPoints = new List<Point>();
                                var height = 0.0;
                                boo.CutPart.Object.GetReportProperty("PROFILE.HEIGHT", ref height);
                                var cMatrixTo = MatrixFactory.ToCoordinateSystem(negSolid.CoordinateSystem);
                                var cMatrixFrom = MatrixFactory.FromCoordinateSystem(negSolid.CoordinateSystem);

                                foreach (var cfInfo in boo.CutPart.Faces)
                                {
                                    foreach (var cutPoint in cfInfo.Points)
                                    {
                                        var tp = cMatrixTo.Transform(cutPoint);
                                        tp.Z = -height/2;
                                        var ftp = cMatrixFrom.Transform(tp);
                                        if (extrusionPoints.Contains(ftp)) continue;
                                        extrusionPoints.Add(ftp);
                                    }
                                }

                                posSolid.Extrusions.Add(new ExtrusionInfo
                                {
                                    Height = height,
                                    System = negSolid.CoordinateSystem,
                                    VertexList = extrusionPoints
                                });
                            }
                        }
                    }
                }
            }

            for (int i = 0; i < _objectSolid.Count; i++)
            {
                var posSolid = _objectSolid[i];
                if (!posSolid.Cutted) continue;

                var matrixTo = MatrixFactory.ToCoordinateSystem(posSolid.CoordinateSystem);
                var minPoint = matrixTo.Transform(posSolid.MinPoint);
                var maxPoint = matrixTo.Transform(posSolid.MaxPoint);
                var height = Math.Abs(minPoint.X - maxPoint.X);
                var extrusionPoints = new List<Point>();
                foreach (var p in posSolid.Points)
                {
                    var tp = matrixTo.Transform(p);
                    if (Math.Abs(tp.X - minPoint.X) < 0.001)
                    {
                        extrusionPoints.Add(p);
                    }
                }

                var cs = new CoordinateSystem(posSolid.CoordinateSystem.Origin, posSolid.CoordinateSystem.AxisY,
                    posSolid.CoordinateSystem.AxisX.Cross(posSolid.CoordinateSystem.AxisY));
                posSolid.CoordinateSystem = cs;
                SortExtrusionPoints(ref extrusionPoints, cs);
                posSolid.Extrusions.Add(new ExtrusionInfo
                {
                    Height = height,
                    System = posSolid.CoordinateSystem,
                    VertexList = extrusionPoints
                });
            }
        }

        /*private ExtrusionInfo GetExtrusionPoints(List<Point> cornerPoints, List<Point> facePoints,
            CoordinateSystem planeCS)
        {
            var extrusionPoints = new List<Point>();
            var geomPlane = new GeometricPlane(planeCS);
            var matrixPlaneCS = MatrixFactory.ToCoordinateSystem(planeCS);
            var cornerProjection = new List<Point>();
            var height = 0.0;
            foreach (var c in cornerPoints)
            {
                var nc = matrixPlaneCS.Transform(c);
                if (nc.Z >= 0)
                {
                    height = height < nc.Z ? nc.Z : height;
                    cornerProjection.Add(Projection.PointToPlane(c, geomPlane));
                }
            }

            extrusionPoints.AddRange(facePoints);

            foreach (var cP in cornerProjection)
            {
                if (extrusionPoints.Contains(cP)) continue;
                extrusionPoints.Add(cP);
            }

            SortExtrusionPoints(ref extrusionPoints, planeCS);
            height += 1;
            return new ExtrusionInfo() {Height = height, System = planeCS, VertexList = extrusionPoints};
        }*/

        private ExtrusionInfo GetExtrusionPoints(List<Point> cornerPoints, List<Point> facePoints,
            CoordinateSystem planeCS)
        {
            var extrusionPoints = new List<Point>();
            var geomPlane = new GeometricPlane(planeCS);
            var matrixPlaneCS = MatrixFactory.ToCoordinateSystem(planeCS);
            var matrixPlaneCSFrom = MatrixFactory.FromCoordinateSystem(planeCS);
            var cornerProjection = new List<Point>();
            var height = 0.0;
            foreach (var c in cornerPoints)
            {
                var nc = matrixPlaneCS.Transform(c);
                if (nc.Z >= 0)
                {
                    height = height < nc.Z ? nc.Z : height;
                    cornerProjection.Add(Projection.PointToPlane(c, geomPlane));
                }
            }

            extrusionPoints.AddRange(facePoints);

            foreach (var cP in cornerProjection)
            {
                if (extrusionPoints.Contains(cP)) continue;
                extrusionPoints.Add(cP);
            }

            var min = matrixPlaneCS.Transform(extrusionPoints.First());
            var max = matrixPlaneCS.Transform(extrusionPoints.First());
            foreach (var ePoint in extrusionPoints)
            {
                var p = matrixPlaneCS.Transform(ePoint);

                min.X = Math.Min(min.X, p.X);
                min.Y = Math.Min(min.Y, p.Y);

                max.X = Math.Max(max.X, p.X);
                max.Y = Math.Max(max.Y, p.Y);
            }

            var xLength = 20;//Math.Abs(max.X - min.X) / 2;
            var yLength = 20; //Math.Abs(max.Y - min.Y) / 2;

            min.X -= xLength;
            min.Y -= yLength;

            max.X += xLength;
            max.Y += yLength;

            extrusionPoints.Clear();
            extrusionPoints.Add(matrixPlaneCSFrom.Transform(new Point(min.X, min.Y)));
            extrusionPoints.Add(matrixPlaneCSFrom.Transform(new Point(min.X, max.Y)));
            extrusionPoints.Add(matrixPlaneCSFrom.Transform(new Point(max.X, max.Y)));
            extrusionPoints.Add(matrixPlaneCSFrom.Transform(new Point(max.X, min.Y)));

            height += 10;
            return new ExtrusionInfo() { Height = height, System = planeCS, VertexList = extrusionPoints };
        }

        private static void SortExtrusionPoints(ref List<Point> extrPoints, CoordinateSystem planeCS)
        {
            var planeMatrixTo = MatrixFactory.ToCoordinateSystem(planeCS);
            var planeMatrixFrom = MatrixFactory.FromCoordinateSystem(planeCS);

            var tempPoints = extrPoints.Select(ex => planeMatrixTo.Transform(ex)).ToList();

            QuickHull(ref tempPoints);

            extrPoints = tempPoints.Select(ex => planeMatrixFrom.Transform(ex)).ToList();
        }

        private static int PointLocation(Point A, Point B, Point P)
        {
            var cp1 = (B.X - A.X)*(P.Y - A.Y) - (B.Y - A.Y)*(P.X - A.X);
            return (cp1 > 0) ? 1 : -1;
        }

        private static double PointDistance(Point A, Point B, Point C)
        {
            var ABx = B.X - A.X;
            var ABy = B.Y - A.Y;
            var num = ABx*(A.Y - C.Y) - ABy*(A.X - C.X);
            if (num < 0) num = -num;
            return num;
        }

        private static void QuickHull(ref List<Point> points)
        {
            var convexHull = new List<Point>();
            if (points.Count <= 3) return;
            var countBefore = points.Count;

            foreach (var p in points)
            {
                p.X = Math.Round(p.X, 2);
                p.Y = Math.Round(p.Y, 2);
                p.Z = Math.Round(p.Z, 2);
            }

            int minPoint = -1, maxPoint = -1;
            var minX = Double.MaxValue;
            var maxX = Double.MinValue;
            for (int i = 0; i < points.Count; i++)
            {
                if (points[i].X < minX)
                {
                    minX = points[i].X;
                    minPoint = i;
                }
                if (points[i].X > maxX)
                {
                    maxX = points[i].X;
                    maxPoint = i;
                }
            }
            var A = points[minPoint];
            var B = points[maxPoint];
            convexHull.Add(A);
            convexHull.Add(B);
            points.Remove(A);
            points.Remove(B);

            var leftSet = new List<Point>();
            var rightSet = new List<Point>();

            for (int i = 0; i < points.Count; i++)
            {
                var p = points[i];
                if (PointLocation(A, B, p) == -1)
                {
                    leftSet.Add(p);
                }
                else
                {
                    rightSet.Add(p);
                }
            }
            HullSet(A, B, rightSet, convexHull);
            HullSet(B, A, leftSet, convexHull);

            points = convexHull;
            if (points.Count != countBefore)
                QuickHull(ref points);
        }

        private static void HullSet(Point A, Point B, List<Point> set, List<Point> hull)
        {
            var insertPosition = hull.IndexOf(B);
            if (set.Count == 0) return;
            if (set.Count == 1)
            {
                var p = set[0];
                set.Remove(p);
                hull.Insert(insertPosition, p);
                return;
            }

            var dist = double.MinValue;
            var furthestPoint = -1;
            for (int i = 0; i < set.Count; i++)
            {
                var p = set[i];
                var distance = PointDistance(A, B, p);
                if (distance > dist)
                {
                    dist = distance;
                    furthestPoint = i;
                }
            }
            var P = set[furthestPoint];
            set.RemoveAt(furthestPoint);
            hull.Insert(insertPosition, P);

            var leftSetAP = new List<Point>();
            for (int i = 0; i < set.Count; i++)
            {
                var M = set[i];
                if (PointLocation(A, P, M) == 1)
                {
                    leftSetAP.Add(M);
                }
            }

            var leftSetPB = new List<Point>();
            for (int i = 0; i < set.Count; i++)
            {
                var M = set[i];
                if (PointLocation(P, B, M) == 1)
                {
                    leftSetPB.Add(M);
                }
            }
            HullSet(A, P, leftSetAP, hull);
            HullSet(P, B, leftSetPB, hull);
        }

        private void SetCornerPoints()
        {
            double profileHeight = 0.0, profileWidth = 0.0, profileDiameter = 0.0;
            _object.GetReportProperty("PROFILE.HEIGHT", ref profileHeight);
            _object.GetReportProperty("PROFILE.WIDTH", ref profileWidth);
            _object.GetReportProperty("PROFILE.DIAMETER", ref profileDiameter);
            if (profileHeight + profileWidth + profileDiameter <= 0)
            {
                _object.GetReportProperty("HEIGHT", ref profileHeight);
                _object.GetReportProperty("WIDTH", ref profileWidth);
            }
            if ((profileHeight + profileWidth) <= 0)
            {
                profileHeight = profileDiameter;
                profileWidth = profileDiameter;
            }

            profileHeight = profileHeight > 0 ? profileHeight : profileWidth;
            profileWidth = profileWidth > 0 ? profileWidth : profileHeight;

            if (_object is ContourPlate)
            {
                var matrixTo = MatrixFactory.ToCoordinateSystem(_coordinateSystems[0]);
                var matrixFrom = MatrixFactory.FromCoordinateSystem(_coordinateSystems[0]);

                var minPoint = matrixTo.Transform(_centerPoints[0]);
                var maxPoint = matrixTo.Transform(_centerPoints[0]);

                for (int i = 1; i < _centerPoints.Count; i++)
                {
                    var tp = matrixTo.Transform(_centerPoints[i]);
                    if (tp.X < minPoint.X) minPoint.X = tp.X;
                    if (tp.Y < minPoint.Y) minPoint.Y = tp.Y;
                    if (tp.Z < minPoint.Z) minPoint.X = tp.Z;
                    if (tp.X > maxPoint.X) maxPoint.X = tp.X;
                    if (tp.Y > maxPoint.Y) maxPoint.Y = tp.Y;
                    if (tp.Z > maxPoint.Z) maxPoint.Z = tp.Z;
                }

                minPoint.Z -= profileHeight/2;
                maxPoint.Z += profileHeight/2;

                var xlength = Math.Abs(minPoint.X - maxPoint.X)/2;
                var ylength = Math.Abs(minPoint.Y - maxPoint.Y)/2;
                var zlength = Math.Abs(minPoint.Z - maxPoint.Z)/2;
                var centrPoint = new Point();
                centrPoint.X = Math.Min(minPoint.X, maxPoint.X) + xlength;
                centrPoint.Y = Math.Min(minPoint.Y, maxPoint.Y) + ylength;
                centrPoint.Z = Math.Min(minPoint.Z, maxPoint.Z) + zlength;

                var cornerPoints = GetCornerPoints(minPoint, maxPoint, matrixFrom);
                var obb = new OBB(matrixFrom.Transform(centrPoint), _coordinateSystems[0].AxisX,
                    _coordinateSystems[0].AxisY, _coordinateSystems[0].AxisX.Cross(_coordinateSystems[0].AxisY), xlength,
                    ylength, zlength);

                var cutted = true;
                foreach (var fInfo in _faces)
                {
                    foreach (var p in fInfo.Points)
                    {
                        var tp = matrixTo.Transform(p);
                        if (PointInVolume(tp, minPoint, maxPoint))
                        {
                            cutted = false;
                            break;
                        }
                    }
                    if (!cutted) break;
                }


                _objectSolid.Add(new SolidInfo()
                {
                    CoordinateSystem = _coordinateSystems[0],
                    MinPoint = matrixFrom.Transform(minPoint),
                    MaxPoint = matrixFrom.Transform(maxPoint),
                    Points = cornerPoints,
                    Extrusions = new List<ExtrusionInfo>(),
                    Obb = obb,
                    Cutted = cutted
                });
                return;
            }

            //получение границ объема по центральным точкам
            var volumeEdges = new List<List<LineSegment>>();
            var volumeCornersPoints = new List<List<Point>>();
            for (var i = 0; i < _coordinateSystems.Count; i++)
            {
                var currentCS = _coordinateSystems[i];
                var currentSegment = _segments[i];
                var matrixTo = MatrixFactory.ToCoordinateSystem(currentCS);
                var matrixFrom = MatrixFactory.FromCoordinateSystem(currentCS);

                var startPoint = matrixTo.Transform(currentSegment.Point1);
                var endPoint = matrixTo.Transform(currentSegment.Point2);
                var minPoint = new Point(startPoint.X, startPoint.Y - profileHeight/2, startPoint.Z - profileWidth/2);
                var maxPoint = new Point(endPoint.X, endPoint.Y + profileHeight/2, endPoint.Z + profileWidth/2);

                var cornerPoints = GetCornerPoints(minPoint, maxPoint, matrixFrom);
                volumeEdges.Add(GetEdgesOfVolume(cornerPoints));
            }

            //корректировка длины с учетом поворотов в случае PolyBeam и точек Face
            var prevDelta = 0.0;
            for (var i = 0; i < _coordinateSystems.Count; i++)
            {
                var currentCS = _coordinateSystems[i];
                var currentSegment = _segments[i];
                var currentEdges = volumeEdges[i];
                var matrixTo = MatrixFactory.ToCoordinateSystem(currentCS);
                var matrixFrom = MatrixFactory.FromCoordinateSystem(currentCS);
                var delta = 0.0;
                if (i < _coordinateSystems.Count - 1)
                {
                    var nextSegment = _segments[i + 1];
                    var direction = currentSegment.GetDirectionVector();

                    var origin = Projection.PointToLine(currentSegment.Point1, new Line(nextSegment));
                    var xVector = new LineSegment(origin, currentSegment.Point1).GetDirectionVector();
                    var yVector = new LineSegment(origin, nextSegment.Point2).GetDirectionVector();
                    var geomPlane = new GeometricPlane(origin, xVector, yVector);

                    var projectionPoint = Projection.PointToPlane(currentSegment.Point1, geomPlane);
                    var width = 0.0;
                    foreach (var edge in currentEdges)
                    {
                        if (!Parallel.VectorToVector(edge.GetDirectionVector(), direction)) continue;
                        var projectionEdge = Projection.LineSegmentToPlane(edge, geomPlane);
                        if (projectionEdge == null) continue;
                        var dist = Distance.PointToLineSegment(projectionPoint, projectionEdge);
                        width = dist > width ? dist : width;
                    }

                    delta = width;
                    if (width > 0.0)
                    {
                        var v1 = new LineSegment(currentSegment.Point2, currentSegment.Point1).GetDirectionVector();
                        var v2 = nextSegment.GetDirectionVector();
                        var angle = (v1.GetAngleBetween(v2)*180/Math.PI)/2;

                        if (angle > 1)
                        {
                            delta = width*Math.Tan((90 - angle)*Math.PI/180);
                        }
                    }
                }

                var startPoint = matrixTo.Transform(currentSegment.Point1);
                var endPoint = matrixTo.Transform(currentSegment.Point2);
                var minPoint = new Point(startPoint.X - prevDelta, startPoint.Y - profileHeight/2,
                    startPoint.Z - profileWidth/2);
                var maxPoint = new Point(endPoint.X + delta, endPoint.Y + profileHeight/2, endPoint.Z + profileWidth/2);
                prevDelta = delta;

                var xlength = Math.Abs(minPoint.X - maxPoint.X)/2;
                var ylength = Math.Abs(minPoint.Y - maxPoint.Y)/2;
                var zlength = Math.Abs(minPoint.Z - maxPoint.Z)/2;
                var centrPoint = new Point();
                centrPoint.X = Math.Min(minPoint.X, maxPoint.X) + xlength;
                centrPoint.Y = Math.Min(minPoint.Y, maxPoint.Y) + ylength;
                centrPoint.Z = Math.Min(minPoint.Z, maxPoint.Z) + zlength;

                var cornerPoints = GetCornerPoints(minPoint, maxPoint, matrixFrom);
                var obb = new OBB(matrixFrom.Transform(centrPoint), currentCS.AxisX, currentCS.AxisY,
                    currentCS.AxisX.Cross(currentCS.AxisY), xlength, ylength, zlength);

                var cutted = true;
                foreach (var fInfo in _faces)
                {
                    foreach (var p in fInfo.Points)
                    {
                        var tp = matrixTo.Transform(p);
                        if (PointInVolume(tp, minPoint, maxPoint))
                        {
                            cutted = false;
                            break;
                        }
                    }
                    if (!cutted) break;
                }

                _objectSolid.Add(new SolidInfo()
                {
                    CoordinateSystem = _coordinateSystems[i],
                    MinPoint = matrixFrom.Transform(minPoint),
                    MaxPoint = matrixFrom.Transform(maxPoint),
                    Points = cornerPoints,
                    Extrusions = new List<ExtrusionInfo>(),
                    Obb = obb,
                    Cutted = cutted
                });
            }
        }

        private bool PointInVolume(Point point, Point min, Point max)
        {
            point.X = Math.Round(point.X, 4);
            point.Y = Math.Round(point.Y, 4);
            point.Z = Math.Round(point.Z, 4);

            var minX = Math.Round(Math.Min(min.X, max.X), 4);
            var maxX = Math.Round(Math.Max(min.X, max.X), 4);

            var minY = Math.Round(Math.Min(min.Y, max.Y), 4);
            var maxY = Math.Round(Math.Max(min.Y, max.Y), 4);

            var minZ = Math.Round(Math.Min(min.Z, max.Z), 4);
            var maxZ = Math.Round(Math.Max(min.Z, max.Z), 4);

            return point.X >= minX && point.X <= maxX && point.Y >= minY && point.Y <= maxY && point.Z >= minZ &&
                   point.Z <= maxZ;
        }

        private List<Point> GetCornerPoints(Point min, Point max, Matrix matrix)
        {
            var locCornerPoints = new List<Point>();

            locCornerPoints.Add(min);
            locCornerPoints.Add(new Point(min.X, max.Y, min.Z));
            locCornerPoints.Add(new Point(max.X, max.Y, min.Z));
            locCornerPoints.Add(new Point(max.X, min.Y, min.Z));
            locCornerPoints.Add(new Point(min.X, min.Y, max.Z));
            locCornerPoints.Add(new Point(min.X, max.Y, max.Z));
            locCornerPoints.Add(max);
            locCornerPoints.Add(new Point(max.X, min.Y, max.Z));

            var cornerPoints = new List<Point>();
            foreach (var p in locCornerPoints)
            {
                cornerPoints.Add(matrix.Transform(p));
            }

            return cornerPoints;
        }

        private List<LineSegment> GetEdgesOfVolume(List<Point> points)
        {
            return new List<LineSegment>
            {
                new LineSegment(points[0], points[1]),
                new LineSegment(points[1], points[2]),
                new LineSegment(points[2], points[3]),
                new LineSegment(points[3], points[0]),
                new LineSegment(points[4], points[5]),
                new LineSegment(points[5], points[6]),
                new LineSegment(points[6], points[7]),
                new LineSegment(points[7], points[4]),
                new LineSegment(points[0], points[4]),
                new LineSegment(points[1], points[5]),
                new LineSegment(points[2], points[6]),
                new LineSegment(points[3], points[7])
            };
        }

        /*private void SetFaces()
        {
            var solid = _object.GetSolid(Solid.SolidCreationTypeEnum.NORMAL);
            var faces = solid.GetFaceEnumerator();
            while (faces.MoveNext())
            {
                var f = (Face) faces.Current;
                var facePoints = new List<Point>();
                var loops = f.GetLoopEnumerator();
                while (loops.MoveNext())
                {
                    var l = (Loop) loops.Current;
                    var vertexes = l.GetVertexEnumerator();
                    while (vertexes.MoveNext())
                    {
                        var v = (Point) vertexes.Current;
                        facePoints.Add(v);
                    }
                }
                if (facePoints.Count == 0) continue;

                _faces.Add(new FaceInfo {Face = f, Points = facePoints});
            }
        }*/

        double PolygonArea(List<Point> input, Vector normal)
        {
            var p1 = input[0];
            var p2 = input[1];
            var vectorX = new Vector(p2.X - p1.X, p2.Y - p1.Y, p2.Z - p1.Z);
            var vectorY = normal.Cross(vectorX);
            var cs = new CoordinateSystem(p1, vectorX, vectorY);
            var matrixTo = MatrixFactory.ToCoordinateSystem(cs);
            var polygon = input.Select(p => matrixTo.Transform(p)).ToList();
            int i, j;
            double area = 0;
            for (i = 0; i < polygon.Count; i++)
            {
                j = (i + 1) % polygon.Count;
                area += polygon[i].X * polygon[j].Y;
                area -= polygon[i].Y * polygon[j].X;
            }
            area /= 2;

            return Math.Abs(area);
        }

        private void SetFaces()
        {
            var solid = _object.GetSolid(Solid.SolidCreationTypeEnum.NORMAL);
            var faces = solid.GetFaceEnumerator();
            while (faces.MoveNext())
            {
                var f = faces.Current;
                var facePoints = new List<Point>();
                var loops = f.GetLoopEnumerator();
                while (loops.MoveNext())
                {
                    var l = loops.Current;
                    var vertexes = l.GetVertexEnumerator();
                    while (vertexes.MoveNext())
                    {
                        var v = vertexes.Current;
                        facePoints.Add(v);
                    }
                }
                if (facePoints.Count < 3) continue;
                var area = PolygonArea(facePoints, f.Normal);

                if (area > 5)
                {
                    _faces.Add(new FaceInfo { Face = f, Points = facePoints });
                }
            }
        }

        private void SetBooleans()
        {
            var booleans = _object.GetBooleans();
            while (booleans.MoveNext())
            {
                var boo = booleans.Current;

                var booInfo = new BooleanInfo
                {
                    Boolean = boo as TS.Model.Boolean,
                    Type = BooleanTypeEnum.Unknown,
                    Plane = new Plane(),
                    Faces = new List<FaceInfo>(),
                    CutPart = null
                };

                if (boo is Fitting || boo is CutPlane)
                {
                    booInfo.Type = boo is Fitting ? BooleanTypeEnum.Fitting : BooleanTypeEnum.CutPlane;
                    var plane = boo is Fitting ? (boo as Fitting).Plane : (boo as CutPlane).Plane;
                    var geomPlane = new GeometricPlane(plane.Origin, plane.AxisX, plane.AxisY);
                    foreach (var faceInfo in _faces)
                    {
                        var isCutFace = true;
                        foreach (var p in faceInfo.Points)
                        {
                            if (Distance.PointToPlane(p, geomPlane) > 1)
                            {
                                isCutFace = false;
                                break;
                            }
                        }

                        if (!isCutFace) continue;

                        booInfo.Faces.Add(faceInfo);
                    }

                    if (booInfo.Faces.Count == 0) continue;

                    var defFaceInfo = booInfo.Faces.First();
                    var planeNormal = plane.AxisX.Cross(plane.AxisY);
                    for (var i = 1; i < booInfo.Faces.Count; i++)
                    {
                        if (planeNormal.GetAngleBetween(defFaceInfo.Face.Normal) >
                            planeNormal.GetAngleBetween(booInfo.Faces[i].Face.Normal))
                        {
                            defFaceInfo = booInfo.Faces[i];
                        }
                    }

                    var faceNormal = defFaceInfo.Face.Normal;
                    var facePoint = defFaceInfo.Points.First();

                    var booPlane = new Plane();
                    booPlane.Origin = facePoint;

                    var angle = plane.AxisX.GetAngleBetween(plane.AxisY)*180/Math.PI;
                    if (Math.Abs(angle - 90) > 0.001)
                    {
                        var axisZ = plane.AxisX.Cross(plane.AxisY);
                        var axisY = axisZ.Cross(plane.AxisX);
                        plane.AxisY = axisY;
                    }

                    plane.AxisX.Normalize();
                    plane.AxisY.Normalize();

                    planeNormal = plane.AxisX.Cross(plane.AxisY);
                    angle = planeNormal.GetAngleBetween(faceNormal)*180/Math.PI;
                    if (Math.Abs(angle) > 1)
                    {
                        booPlane.AxisX = plane.AxisY;
                        booPlane.AxisY = plane.AxisX;
                    }
                    else
                    {
                        booPlane.AxisX = plane.AxisX;
                        booPlane.AxisY = plane.AxisY;
                    }

                    booInfo.Plane = booPlane;

                    _booleans.Add(booInfo);

                }
                else if (boo is BooleanPart)
                {
                    var booleanPart = boo as BooleanPart;
                    var cutPart = booleanPart.OperativePart;
                    if (cutPart is ContourPlate) booInfo.Type = BooleanTypeEnum.ContourPlate;
                    else booInfo.Type = cutPart is PolyBeam ? BooleanTypeEnum.PolyBeam : BooleanTypeEnum.Beam;
                    var cutObject = new PartInfoClass(cutPart);
                    booInfo.Faces = new List<FaceInfo>();
                    booInfo.CutPart = cutObject;
                    _booleans.Add(booInfo);
                }
            }
        }

        private void SetCoordinateSystems()
        {
            var system = _object.GetCoordinateSystem();

            if (_object is Beam && _centerPoints.Count > 2)
            {
                var beamDir = new LineSegment(_centerPoints[0], _centerPoints[1]).GetDirectionVector();
                if (system.AxisX.GetAngleBetween(beamDir) > 0)
                {
                    system.AxisX = beamDir;
                }
            }

            //добавляем общую систему координат элемента в качестве системы для первого участка PolyBeam
            _coordinateSystems.Add(system);

            //если точек меньше, чем 3, то нет смысла делать что-то дальше
            if (_centerPoints.Count < 3 || _object is ContourPlate) return;

            //обрабатываем все участки составного элемента, кроме первого (для него система координат получена выше)
            for (int i = 1; i < _centerPoints.Count - 1; i++)
            {
                //точка начала системы координат
                var origin = _centerPoints[i];
                //предыдущая точка ломанной
                var prevPoint = _centerPoints[i - 1];
                //следующая точка ломанной
                var nextPoint = _centerPoints[i + 1];

                //линия по которой строится текущий участок
                var currentLine = new Line(origin, nextPoint);

                //линия по которой строился предыдущий участок
                var prevLine = new Line(origin,
                    new Point(origin.X + (origin.X - prevPoint.X), origin.Y + (origin.Y - prevPoint.Y),
                        origin.Z + (origin.Z - prevPoint.Z)));

                var prevSystem = _coordinateSystems[i - 1];
                var prevSystemAxisZ = prevSystem.AxisX.Cross(prevSystem.AxisY);
                prevSystemAxisZ.Normalize();
                //получение плоскостей с предыдущей системы координат
                var planeXY = new GeometricPlane(prevPoint, prevSystemAxisZ);
                var planeYZ = new GeometricPlane(prevPoint, prevSystem.AxisX);

                //получение проекции линий построения на плоскости
                var currentLineProjYZ = Projection.LineToPlane(currentLine, planeYZ);

                //получение плоскойстей с нормалями совпадающими с направлением линия построения
                var prevYZ = new GeometricPlane(origin, prevLine.Direction);
                var currYZ = new GeometricPlane(origin, currentLine.Direction);

                //берем пересечение плоскостей и в качестве вектора Y берем обратный к направлению линии пересечения
                var planeIntersect = Intersection.PlaneToPlane(prevYZ, currYZ);
                //TODO: необходимо рассмотреть этот случай, падает программа
                if (planeIntersect == null)
                {
                    Console.WriteLine("Невозможно обработать систему координат " + _object.Identifier.GUID);
                    return;
                }
                var yVector = planeIntersect.Direction.Cross(currentLine.Direction).Cross(currentLine.Direction);

                //определение угла и направления поворота вокруг оси X
                if (currentLineProjYZ.Direction.GetLength() > 0)
                {
                    var aboutX = currentLineProjYZ.Direction.GetAngleBetween(prevSystemAxisZ);
                    if (aboutX > 0)
                    {
                        var dotY = prevSystem.AxisY.Dot(currentLineProjYZ.Direction);
                        var matrix = MatrixFactory.Rotate(dotY > 0 ? -aboutX : aboutX, currentLine.Direction);
                        var tyVectorPoint = matrix.Transform(new Point(yVector.X, yVector.Y, yVector.Z));
                        var tyVector = new Vector(tyVectorPoint);
                        tyVector.Normalize();
                        yVector = tyVector;
                    }
                }

                _coordinateSystems.Add(new CoordinateSystem(origin, currentLine.Direction, yVector));
            }
        }

        /*private void SetCenterPoints()
        {
            _centerPoints = new List<Point>(_object.GetCenterLine(false).Cast<Point>());

            if (_object is Beam && _centerPoints.Count == 2 && _object.GetBooleans().MoveNext())
            {
                var cs = _object.GetCoordinateSystem();
                var matrixTo = MatrixFactory.ToCoordinateSystem(cs);
                var matrixFrom = MatrixFactory.FromCoordinateSystem(cs);
                var p1 = matrixTo.Transform(_centerPoints[0]);
                var p2 = matrixTo.Transform(_centerPoints[1]);
                var ls = new LineSegment((p1.X < p2.X ? p1 : p2), (p1.X < p2.X ? p2 : p1));
                var minX = ls.Point1.X;
                var maxX = ls.Point2.X;
                foreach (var f in _faces)
                {
                    foreach (var p in f.Points)
                    {
                        var tp = matrixTo.Transform(p);
                        if (tp.X < ls.Point1.X) ls.Point1.X = tp.X;
                        if (tp.X > ls.Point2.X) ls.Point2.X = tp.X;
                    }
                }

                _centerPoints[0] = matrixFrom.Transform(ls.Point1);
                _centerPoints[1] = matrixFrom.Transform(ls.Point2);
                return;
            }
        }*/

        private void SetCenterPoints()
        {
            if (_object is ContourPlate)
            {
                var cs = _object.GetCoordinateSystem();
                var matrixTo = MatrixFactory.ToCoordinateSystem(cs);
                var matrixFrom = MatrixFactory.FromCoordinateSystem(cs);
                var plane = new GeometricPlane(cs);
                var edgePoints = new List<Point>();
                var edges = _object.GetSolid(Solid.SolidCreationTypeEnum.RAW).GetEdgeEnumerator();
                while (edges.MoveNext())
                {
                    var edge = edges.Current as Edge;
                    if (edge == null) continue;

                    var proj = Projection.PointToPlane(edge.StartPoint, plane);
                    var p = matrixTo.Transform(proj);
                    if (edgePoints.Contains(p)) continue;
                    edgePoints.Add(p);
                }

                foreach (var p in edgePoints)
                {
                    _centerPoints.Add(matrixFrom.Transform(p));
                }
                return;
            }

            _centerPoints = new List<Point>(_object.GetCenterLine(false).Cast<Point>());

            if (_object is Beam && _centerPoints.Count == 2 && _object.GetBooleans().MoveNext())
            {
                var cs = _object.GetCoordinateSystem();
                var matrixTo = MatrixFactory.ToCoordinateSystem(cs);
                var matrixFrom = MatrixFactory.FromCoordinateSystem(cs);
                var p1 = matrixTo.Transform(_centerPoints[0]);
                var p2 = matrixTo.Transform(_centerPoints[1]);
                var ls = new LineSegment((p1.X < p2.X ? p1 : p2), (p1.X < p2.X ? p2 : p1));
                var minX = ls.Point1.X;
                var maxX = ls.Point2.X;
                foreach (var f in _faces)
                {
                    foreach (var p in f.Points)
                    {
                        var tp = matrixTo.Transform(p);
                        if (tp.X < ls.Point1.X) ls.Point1.X = tp.X;
                        if (tp.X > ls.Point2.X) ls.Point2.X = tp.X;
                    }
                }

                _centerPoints[0] = matrixFrom.Transform(ls.Point1);
                _centerPoints[1] = matrixFrom.Transform(ls.Point2);
                return;
            }
        }

        private void SetSegments()
        {
            for (int i = 0; i < _centerPoints.Count - 1; i++)
            {
                var ls = new LineSegment(_centerPoints[i], _centerPoints[i + 1]);
                _segments.Add(ls);
            }
        }
    }

    public class PartHistory
    {
        private static DateTime _createdDate;
        private static string _createdBy;
        private static DateTime _modifiedDate;
        private static string _modifiedBy;
        private static DateTime minDateTime = DateTime.Parse("01.01.1900 00:00");
        public DateTime CreatedDate
        {
            get { return _createdDate == DateTime.MinValue ? minDateTime : _createdDate; ; }
        }

        public string CreatedBy
        {
            get { return _createdBy; }
        }

        public DateTime ModifiedDate
        {
            get { return _modifiedDate == DateTime.MinValue ? minDateTime : _modifiedDate; }
        }

        public string ModifiedBy
        {
            get { return _modifiedBy; }
        }

        public PartHistory(Part part)
        {
            var properties = new ArrayList()
            {
                "OWNER",
                "HISTORY.CREATED",
                "HISTORY.CREATED_BY",
                "HISTORY.MODIFIED",
                "HISTORY.MODIFIED_BY"
            };
            var hashtable = new Hashtable();
            part.GetStringReportProperties(properties, ref hashtable);

            var strCreatedDate = (string)hashtable["HISTORY.CREATED"];
            var strModifiedDate = (string)hashtable["HISTORY.MODIFIED"];
            var strCreatedBy = (string)hashtable["HISTORY.CREATED_BY"];
            var strModifiedBy = (string)hashtable["HISTORY.MODIFIED_BY"];
            var owner = (string)hashtable["OWNER"];

            if (owner == null && strCreatedBy == null && strModifiedBy == null)
            {
                Standalone.SendMessage("Не удалось определить владельцы элемента " + part.Identifier.GUID);
                strCreatedBy = strModifiedBy = string.Empty;
            }
            else
            {
                strCreatedBy = strCreatedBy ?? (owner ?? strModifiedBy);
                strModifiedBy = strModifiedBy ?? strCreatedBy;
            }

            _createdBy = strCreatedBy.Replace("POLY/", "");
            _modifiedBy = strModifiedBy.Replace("POLY/", "");

            if (_createdBy.Contains("MicrosoftAccount/a.francych@outlook.com") || _createdBy.Contains("Anafran-VAIO/Anafran"))
                _createdBy = "KolendoAF";
            if (_modifiedBy.Contains("MicrosoftAccount/a.francych@outlook.com") || _modifiedBy.Contains("Anafran-VAIO/Anafran"))
                _modifiedBy = "KolendoAF";

            strCreatedDate = strCreatedDate ?? strModifiedDate;
            strModifiedDate = strModifiedDate ?? strCreatedDate;
            DateTime.TryParse(strCreatedDate, out _createdDate);
            DateTime.TryParse(strModifiedDate, out _modifiedDate);
        }
    }
}
