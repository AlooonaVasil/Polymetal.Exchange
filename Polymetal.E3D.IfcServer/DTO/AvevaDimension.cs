using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Xbim.Common.Geometry;
using Xbim.Ifc2x3.GeometricModelResource;
using Xbim.Ifc2x3.GeometryResource;
using Xbim.Ifc2x3.ProfileResource;
using Xbim.Ifc2x3.RepresentationResource;

namespace Polymetal.E3D.IfcServer.DTO
{
    internal class AvevaDimension :AvevaGeometry
    {
        private const double LINE_WIDTH = 2.0;
        private const double DEPTH = 2.0;
        internal double TextHeight { get; }
        internal string[] TextArray { get; }
        internal XbimPoint3D[] PositionArray { get; }

        internal AvevaDimension(string[] data)
        {
            TextHeight = double.Parse(data[15]);
            var count = int.Parse(data[16]);
            PositionArray = new XbimPoint3D[count];
            TextArray = new string[count - 1];

            var j = 0;
            for (var i = 17; i < count*3+17; i+=3)
            {
                PositionArray[j++] = new XbimPoint3D(
                    double.Parse(data[i]), 
                    double.Parse(data[i+1]),
                    double.Parse(data[i+2]));
            }

            j = 0;
            for (var i = 17+count*3; i < data.Length; i++)
            {
                TextArray[j++] = data[i];
            }

            Hash = $"{GetType().Name}-{TextHeight}-{count}-{string.Join(" ", PositionArray.Select(p => $"{p.X} {p.Y} {p.Z}"))}-{string.Join(" ", TextArray)}".GetHashCode();
        }

        internal IfcShapeRepresentation GetShapeRepresentation()
        {
            double dpiX;
            using (var graphics = System.Drawing.Graphics.FromHwnd(IntPtr.Zero))
            {
                dpiX = graphics.DpiX;
            }

            var axis2Placement3DText = EntityCreator.CreateAxis2Placement3D(new XbimPoint3D(), new XbimVector3D(0.0, 0.0, -1.0), new XbimVector3D(1.0, 0.0, 0.0), Matrix);
            var representations = new List<IfcRepresentationItem>();
            for (var i = 0; i < PositionArray.Length - 1; i++)
            {
                var pos1 = PositionArray[i];
                var pos2 = PositionArray[i + 1];

                var cartesianPoint1 = EntityCreator.CreateCartesianPoint(Matrix.Transform(new XbimPoint3D(pos1.X, pos1.Y, 0.0)));
                var cartesianPoint2 = EntityCreator.CreateCartesianPoint(Matrix.Transform(new XbimPoint3D(pos1.X, -TextHeight * Math.Sign(pos1.Y), 0.0)));
                var polyLine = EntityCreator.CreatePolyLine(new List<IfcCartesianPoint>
                    { cartesianPoint1, cartesianPoint2 });
                representations.Add(polyLine);

                cartesianPoint1 = EntityCreator.CreateCartesianPoint(Matrix.Transform(new XbimPoint3D(pos2.X, pos2.Y, 0.0)));
                cartesianPoint2 = EntityCreator.CreateCartesianPoint(Matrix.Transform(new XbimPoint3D(pos2.X, -TextHeight * Math.Sign(pos2.Y), 0.0)));
                polyLine = EntityCreator.CreatePolyLine(new List<IfcCartesianPoint>
                    { cartesianPoint1, cartesianPoint2 });
                representations.Add(polyLine);

                if (Math.Abs(pos1.Z)>1)
                {
                    cartesianPoint1 = EntityCreator.CreateCartesianPoint(Matrix.Transform(pos1));
                    cartesianPoint2 = EntityCreator.CreateCartesianPoint(Matrix.Transform(new XbimPoint3D(pos1.X, pos1.Y, 0.0)));
                    polyLine = EntityCreator.CreatePolyLine(new List<IfcCartesianPoint>
                        { cartesianPoint1, cartesianPoint2 });
                    representations.Add(polyLine);
                }

                if (Math.Abs(pos2.Z) > 1)
                {
                    cartesianPoint1 = EntityCreator.CreateCartesianPoint(Matrix.Transform(pos2));
                    cartesianPoint2 = EntityCreator.CreateCartesianPoint(Matrix.Transform(new XbimPoint3D(pos2.X, pos2.Y, 0.0)));
                    polyLine = EntityCreator.CreatePolyLine(new List<IfcCartesianPoint>
                        { cartesianPoint1, cartesianPoint2 });
                    representations.Add(polyLine);
                }

                representations.AddRange(GetArrowPolyLines(new XbimPoint3D(pos1.X, 0.0, 0.0), new XbimPoint3D(pos2.X, 0.0, 0.0)));

                var text = TextArray[i];
                var formattedText = new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight, new Typeface("ISOCPEUR"), TextHeight,
                    Brushes.Black, dpiX / 96.0);

                var textGeometry = formattedText.BuildGeometry(new Point((pos1.X + pos2.X) / 2.0 - formattedText.Width / 2.0, -TextHeight));
                var pathGeometries = ExtractPathGeometries(textGeometry);

                representations.AddRange(CreateExtrudedSolidsFromPathGeometry(pathGeometries, axis2Placement3DText));
            }

            var diffColourRgb = EntityCreator.CreateColourRgb(Color);
            var surfaceStyleRendering =
                EntityCreator.CreateSurfaceStyleRendering(diffColourRgb, (double)Translucency / 100);
            var surfaceStyle = EntityCreator.CreateSurfaceStyle(surfaceStyleRendering, ColorIndex);

            var presentationStyleAssignment = EntityCreator.CreatePresentationStyleAssignment(surfaceStyle);

            foreach (var representationItem in representations)
            {
                EntityCreator.CreateStyledItem(representationItem, presentationStyleAssignment);
            }

            return EntityCreator.CreateShapeRepresentation(representations, "Body", "SweptSolid");
        }

        private List<IfcPolyline> GetArrowPolyLines(XbimPoint3D point1, XbimPoint3D point2)
        {
            var result = new List<IfcPolyline>();
            if (point1.X > point2.X)
                (point1, point2) = (point2, point1);
            var length = Math.Abs(point1.X - point2.X);

            var cartesianPoint1 = EntityCreator.CreateCartesianPoint(Matrix.Transform(point1));
            var cartesianPoint2 = EntityCreator.CreateCartesianPoint(Matrix.Transform(point2));
            var polyLine = EntityCreator.CreatePolyLine(new List<IfcCartesianPoint> { cartesianPoint1, cartesianPoint2 });
            result.Add(polyLine);

            var arrowLength = TextHeight / 2.0;
            if (arrowLength >= length / 2.0 && length / 2.0 - 5.0 > 0.0)
            {
                arrowLength = length / 2.0 - 5.0;
            }

            var points = new List<XbimPoint3D>();

            points.Add(new XbimPoint3D(point1.X + arrowLength, point1.Y + arrowLength / 4.0, point1.Z));
            points.Add(new XbimPoint3D(point1.X, point1.Y, point1.Z));
            points.Add(new XbimPoint3D(point1.X + arrowLength, point1.Y - arrowLength / 4.0, point1.Z));
            var cartesianPoints = points.Select(x => EntityCreator.CreateCartesianPoint(Matrix.Transform(x))).ToList();
            polyLine = EntityCreator.CreatePolyLine(cartesianPoints);
            result.Add(polyLine);

            points.Clear();
            points.Add(new XbimPoint3D(point2.X - arrowLength, point2.Y + arrowLength / 4.0, point2.Z));
            points.Add(new XbimPoint3D(point2.X, point2.Y, point2.Z));
            points.Add(new XbimPoint3D(point2.X - arrowLength, point2.Y - arrowLength / 4.0, point2.Z));
            cartesianPoints = points.Select(x => EntityCreator.CreateCartesianPoint(Matrix.Transform(x))).ToList();
            polyLine = EntityCreator.CreatePolyLine(cartesianPoints);
            result.Add(polyLine);

            return result;
        }

        private static List<IfcExtrudedAreaSolid> CreateExtrudedSolidsFromPathGeometry(List<PathGeometry> pathGeometryList,
            IfcAxis2Placement3D axis2Placement3D)
        {
            var result = new List<IfcExtrudedAreaSolid>();

            var figures = pathGeometryList.SelectMany(pathGeometry => pathGeometry.Figures).ToList();

            while (figures.Count > 0)
            {
                var outerFigure = figures.OrderByDescending(fig => GetArea(fig)).First();
                figures.Remove(outerFigure);

                var outerBoundaryPoints = GetPointsFromFigure(outerFigure);
                var outerCartesianPoints = outerBoundaryPoints.Select(EntityCreator.CreateCartesianPoint2).ToList();
                var outerPolyLine = EntityCreator.CreatePolyLine(outerCartesianPoints);

                var innerPolyLineList = new List<IfcPolyline>();

                var innerFigures = figures
                    .Where(fig => IsInnerContour(fig, outerFigure))
                    .ToList();

                foreach (var figure in innerFigures)
                {
                    var innerPoints = GetPointsFromFigure(figure);
                    var innerCartesianPoints = innerPoints.Select(EntityCreator.CreateCartesianPoint2).ToList();

                    innerPolyLineList.Add(EntityCreator.CreatePolyLine(innerCartesianPoints));
                    figures.Remove(figure);
                }

                var profile = innerPolyLineList.Count == 0
                    ? EntityCreator.CreateArbitraryClosedProfileDef(outerPolyLine, IfcProfileTypeEnum.AREA)
                    : EntityCreator.CreateArbitraryProfileDefWithVoids(outerPolyLine, innerPolyLineList,
                        IfcProfileTypeEnum.AREA);

                result.Add(Representation.CreateExtrudedAreaSolid(axis2Placement3D, profile, new XbimVector3D(0, 0, 1),
                    DEPTH));
            }

            return result;
        }

        private static double GetArea(PathFigure figure)
        {
            var points = GetPointsFromFigure(figure);
            double area = 0;

            for (var i = 0; i < points.Count - 1; i++)
            {
                var p1 = points[i];
                var p2 = points[i + 1];
                area += (p1.X * p2.Y - p2.X * p1.Y);
            }

            return Math.Abs(area) / 2;
        }

        private static bool IsInnerContour(PathFigure inner, PathFigure outer)
        {
            var innerBounds = GetBounds(inner);
            var outerBounds = GetBounds(outer);

            return outerBounds.Contains(innerBounds);
        }

        private static Rect GetBounds(PathFigure figure)
        {
            var points = GetPointsFromFigure(figure);
            var xValues = points.Select(p => p.X).ToList();
            var yValues = points.Select(p => p.Y).ToList();

            var minX = xValues.Min();
            var maxX = xValues.Max();
            var minY = yValues.Min();
            var maxY = yValues.Max();

            return new Rect(minX, minY, maxX - minX, maxY - minY);
        }

        private static List<XbimPoint3D> GetPointsFromFigure(PathFigure figure)
        {
            var points = new List<XbimPoint3D>
            {
                new XbimPoint3D(figure.StartPoint.X, figure.StartPoint.Y, 0.0)
            };

            foreach (var segment in figure.Segments)
            {
                if (segment is PolyLineSegment polyLineSegment)
                {
                    foreach (var point in polyLineSegment.Points)
                    {
                        points.Add(new XbimPoint3D(point.X, point.Y, 0.0));
                    }
                }
                else if (segment is LineSegment lineSegment)
                {
                    points.Add(new XbimPoint3D(lineSegment.Point.X, lineSegment.Point.Y, 0.0));
                }
            }

            points.Add(new XbimPoint3D(figure.StartPoint.X, figure.StartPoint.Y, 0.0));

            return points;
        }

        private static List<PathGeometry> ExtractPathGeometries(Geometry geometry)
        {
            var pathGeometries = new List<PathGeometry>();
            var flattenedGeometry = geometry.GetFlattenedPathGeometry();

            foreach (var figure in flattenedGeometry.Figures)
            {
                var pathGeometry = new PathGeometry();
                var pathFigure = new PathFigure
                {
                    StartPoint = figure.StartPoint,
                    IsClosed = true
                };

                foreach (var segment in figure.Segments)
                {
                    switch (segment)
                    {
                        case PolyLineSegment polyLineSegment:
                            pathFigure.Segments.Add(polyLineSegment);
                            break;
                        case LineSegment lineSegment:
                            pathFigure.Segments.Add(lineSegment);
                            break;
                    }
                }

                pathGeometry.Figures.Add(pathFigure);
                pathGeometries.Add(pathGeometry);
            }

            return pathGeometries;
        }
    }
}
