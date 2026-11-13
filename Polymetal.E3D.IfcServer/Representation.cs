using System;
using System.Collections.Generic;
using System.Linq;
using Xbim.Common.Geometry;
using Xbim.Ifc2x3.GeometricModelResource;
using Xbim.Ifc2x3.GeometryResource;
using Xbim.Ifc2x3.ProfileResource;
using Xbim.Ifc2x3.RepresentationResource;

namespace Polymetal.E3D.IfcServer
{
    class Representation
    {
        internal const double ARCTOLERANCE = 10.0;
        private static Dictionary<int, Dictionary<int, IfcRepresentationItem>> _styledItems;
        internal static XbimMatrix3D RootMatrix;

        internal static void Init()
        {
            _styledItems = new Dictionary<int, Dictionary<int, IfcRepresentationItem>>();
        }

        internal static IfcShapeRepresentation GetShapeRepresentation(List<string> geometryLines, ref string layerName)
        {
            var representations = new List<IfcRepresentationItem>();
            foreach (var geometryLine in geometryLines)
            {
                var geometry = AvevaGeometry.StringToGeometry(geometryLine);
                layerName = string.IsNullOrEmpty(layerName) ? geometry.LayerName : layerName;

                if (geometry is DTO.AvevaDimension aveveDimension)
                    return aveveDimension.GetShapeRepresentation();

                if (geometry is DTO.AvevaLine avevaLine)
                {
                    representations.Add(avevaLine.CreateRepresentation());
                    continue;
                }

                var representationItem = CreateRepresentationItem(geometry);

                if (representationItem == null)
                    continue;

                var diffColourRgb = EntityCreator.CreateColourRgb(geometry.Color);

                var surfaceStyleRendering =
                    EntityCreator.CreateSurfaceStyleRendering(diffColourRgb, (double)geometry.Translucency / 100);
                var presentationStyle = EntityCreator.CreateSurfaceStyle(surfaceStyleRendering, geometry.ColorIndex);

                var presentationStyleAssignment = EntityCreator.CreatePresentationStyleAssignment(presentationStyle);

                if (_styledItems.ContainsKey(representationItem.EntityLabel))
                {
                    var valueDict = _styledItems[representationItem.EntityLabel];
                    if (!valueDict.ContainsKey(presentationStyleAssignment.EntityLabel))
                    {
                        representationItem = representationItem.Copy();
                        valueDict.Add(presentationStyleAssignment.EntityLabel, representationItem);
                    }
                    else
                    {
                        representationItem = valueDict[presentationStyleAssignment.EntityLabel];
                    }
                }
                else
                {
                    _styledItems.Add(representationItem.EntityLabel, new Dictionary<int, IfcRepresentationItem>());
                    _styledItems[representationItem.EntityLabel]
                        .Add(presentationStyleAssignment.EntityLabel, representationItem);
                }

                EntityCreator.CreateStyledItem(representationItem, presentationStyleAssignment);

                representations.Add(representationItem);
            }

            if (representations.Count == 0)
                return null;

            return EntityCreator.CreateShapeRepresentation(representations, "Body", "SweptSolid");
        }

        private static IfcRepresentationItem CreateRepresentationItem(AvevaGeometry geometry)
        {
            IfcRepresentationItem representationItem = null;        

            switch (geometry)
            {
                case DTO.AvevaPyramid avevaPyramid:
                    representationItem = avevaPyramid.GetShapeRepresentation();
                    break;
                case DTO.AvevaBox avevaBox:
                    representationItem = avevaBox.GetShapeRepresentation();
                    break;
                case DTO.AvevaRectTorus avevaRectTorus:
                    representationItem = avevaRectTorus.GetShapeRepresentation();
                    break;
                case DTO.AvevaCircTorus avevaCircTorus:
                    representationItem = avevaCircTorus.GetShapeRepresentation();
                    break;
                case DTO.AvevaEllipticalDish avevaEllipticalDish:
                    representationItem = avevaEllipticalDish.GetShapeRepresentation();
                    break;
                case DTO.AvevaSphericalDish avevaSphericalDish:
                    representationItem = avevaSphericalDish.GetShapeRepresentation();
                    break;
                case DTO.AvevaSnout avevaSnout:
                    representationItem = avevaSnout.GetShapeRepresentation();
                    break;
                case DTO.AvevaCone avevaCone:
                    representationItem = avevaCone.GetShapeRepresentation();
                    break;
                case DTO.AvevaCylinder avevaCylinder:
                    representationItem = avevaCylinder.GetShapeRepresentation();
                    break;
                case DTO.AvevaSphere avevaSphere:
                    representationItem = avevaSphere.GetShapeRepresentation();
                    break;
                case DTO.AvevaRevolution avevaRevolution:
                    representationItem = avevaRevolution.GetShapeRepresentation();
                    break;
                case DTO.AvevaExtrusion avevaExtrusion:
                    representationItem = avevaExtrusion.GetShapeRepresentation();
                    break;
                case DTO.AvevaSlcy avevaSlcy:
                    representationItem = avevaSlcy.GetShapeRepresentation();
                    break;
                case DTO.AvevaFacetGroup avevaFacetGroup:
                    representationItem = avevaFacetGroup.GetShapeRepresentation();
                    break;
                default:
                    Console.WriteLine(geometry.GetType().Name);
                    break;
            }

            if (representationItem == null || geometry.Negatives.Count <= 0) 
                return representationItem;

            //поочередное добавление негативов к позитивному элементу
            IfcBooleanResult booleanRepresentation = null;
            var firstOperand = representationItem;
            foreach (var negative in geometry.Negatives)
            {
                var nRepresentation = CreateRepresentationItem(negative);
                if (nRepresentation == null) 
                    continue;

                IfcBooleanOperand first, second;
                if (firstOperand is IfcSolidModel)
                {
                    first = (IfcSolidModel)firstOperand;
                }
                else
                {
                    first = (IfcBooleanResult)firstOperand;
                }

                if (nRepresentation is IfcSolidModel)
                {
                    second = (IfcSolidModel)nRepresentation;
                }
                else
                {
                    second = (IfcBooleanResult)nRepresentation;
                }

                booleanRepresentation = EntityCreator.CreateBooleanResult(first, second,
                    IfcBooleanOperator.DIFFERENCE);

                firstOperand = booleanRepresentation;
            }

            if (booleanRepresentation!=null)
                representationItem = booleanRepresentation;

            return representationItem;
        }

        internal static IfcFacetedBrep CreateFacetedBrep(List<List<XbimPoint3D>> faces)
        {
            var tFaces = new List<List<XbimPoint3D>>();
            for (var i = 0; i < faces.Count; i++)
            {
                var f = faces[i];
                var points = f;
                var tpoints = new List<XbimPoint3D>();
                for (var j = 0; j < points.Count; j++)
                {
                    var k = j == points.Count - 1 ? 0 : j + 1;
                    var cp = points[j].Round();
                    var np = points[k].Round();
                    if (XbimPoint3D.Subtract(cp, np).Length < 0.5) continue;
                    tpoints.Add(cp);
                }
                if (tpoints.Count < 3) continue;
                tFaces.Add(tpoints);
            }
            faces = tFaces;
            if (faces.Count == 0) return null;

            var closedShell = EntityCreator.CreateClosedShell(faces);
            var facetedBrep = EntityCreator.CreateFacetedBrep(closedShell);
            return facetedBrep;
        }

        internal static IfcArbitraryClosedProfileDef CreatePointsProfileDef(List<double[]> profilePoints)
        {
            var points = new List<XbimPoint3D>();
            var rounds = new List<double>();
            var hasRounds = false;

            foreach (var profilePoint in profilePoints)
            {
                var point = new XbimPoint3D(
                    profilePoint[0].Round(),
                    profilePoint[1].Round(),
                    double.MinValue);
                points.Add(point);
                rounds.Add(profilePoint[3].Round());
                if (profilePoint[3] != 0.0)
                    hasRounds = true;
            }

            if (!hasRounds)
            {
                var cPoints = points.Select(p => EntityCreator.CreateCartesianPoint(p)).ToList();
                cPoints.Add(EntityCreator.CreateCartesianPoint(points.First()));
                var polyLine = EntityCreator.CreatePolyLine(cPoints);

                var simpleArbitraryClosedProfileDef = EntityCreator.CreateArbitraryClosedProfileDef(polyLine, IfcProfileTypeEnum.AREA);
                return simpleArbitraryClosedProfileDef;
            }

            var curveObjects = new object[points.Count];
            var senseAgreements = new List<bool>();
            for (var i = 0; i < points.Count; i++)
            {
                if (rounds[i] == 0.0)
                {
                    curveObjects[i] = points[i];
                    senseAgreements.Add(false);
                    continue;
                }

                var p1 = points[i == 0 ? points.Count - 1 : i - 1];
                var p2 = points[i];
                var p3 = points[i == points.Count - 1 ? 0 : i + 1];

                var v1 = (p1 - p2).Normalized();
                var v2 = (p3 - p2).Normalized();
                senseAgreements.Add(v1.CrossProduct(v2).Z < 0);
                var alfa = v1.Angle(v2) / 2;
               
                XbimPoint3D center,intersect1,intersect2;
                if (rounds[i] > 0.0)
                {
                    var gipotenuza = rounds[i] / Math.Sin(alfa);
                    center = p2 + (XbimVector3D.Add(v1, v2).Normalized()) * gipotenuza;
                    intersect1 = p2 + v1*(gipotenuza*Math.Cos(alfa));
                    intersect2 = p2 + v2*(gipotenuza*Math.Cos(alfa));
                }
                else
                {
                    center = p2;
                    intersect1 = p2 + v1.Negated() * rounds[i];
                    intersect2 = p2 + v2.Negated() * rounds[i];
                }
                if (Math.Abs(Math.Pow(center.X - intersect1.X, 2.0) + Math.Pow(center.Y - intersect1.Y, 2.0) -
                             Math.Pow(rounds[i], 2.0)) > 0.1)
                {
                    var v = (intersect1 - center).Normalized() * rounds[i];
                    intersect1 = new XbimPoint3D(v.X + center.X, v.Y + center.Y, v.Z);
                }

                if (Math.Abs(Math.Pow(intersect2.X - center.X, 2.0) + Math.Pow(intersect2.Y - center.Y, 2.0) -
                             Math.Pow(rounds[i], 2.0)) > 0.1)
                {
                    var v = (intersect2 - center).Normalized() * rounds[i];
                    intersect2 = new XbimPoint3D(v.X + center.X, v.Y + center.Y, v.Z);
                }

                var ci1 = new XbimPoint3D(Math.Round(intersect1.X, 4), Math.Round(intersect1.Y, 4), double.MinValue);
                var ci2 = new XbimPoint3D(Math.Round(intersect2.X, 4), Math.Round(intersect2.Y, 4), double.MinValue);
                var cc = new XbimPoint3D(Math.Round(center.X, 4), Math.Round(center.Y, 4), double.MinValue);
                curveObjects[i] = new[] { ci1, ci2, cc };
            }

            FixPoints(ref points, ref rounds, ref curveObjects, ref senseAgreements);

            var segments = new List<IfcCompositeCurveSegment>();
            var polyLinePoints = new List<XbimPoint3D>();
            for (var i = 0; i < curveObjects.Length; i++)
            {
                if (curveObjects[i] is XbimPoint3D)
                {
                    if (polyLinePoints.Count == 0 || !polyLinePoints.Last().Equals((XbimPoint3D)curveObjects[i]))
                    {
                        polyLinePoints.Add((XbimPoint3D) curveObjects[i]);
                    }

                    if (i == curveObjects.Length - 1)
                    {
                        XbimPoint3D firstPoint;
                        if (curveObjects[0] is XbimPoint3D)
                        {
                            firstPoint = (XbimPoint3D)curveObjects[0];
                        }
                        else
                        {
                            firstPoint = ((XbimPoint3D[])curveObjects[0])[0];
                        }

                        if (polyLinePoints.Count == 0 || !polyLinePoints.Last().Equals(firstPoint))
                        {
                            polyLinePoints.Add(firstPoint);
                        }
                    }
                    continue;
                }
                var data = (XbimPoint3D[])curveObjects[i];

                if (polyLinePoints.Count > 0)
                {
                    if (!polyLinePoints.Last().Equals(data[0]))
                        polyLinePoints.Add(data[0]);
                    polyLinePoints = RemoveExcessPoints(polyLinePoints);
                    if (polyLinePoints.Count > 1)
                    {
                        var polyLine = EntityCreator.CreatePolyLine(polyLinePoints.Select(p => EntityCreator.CreateCartesianPoint(p)).ToList());
                        var polyLinecompositeCurveSegment = EntityCreator.CreateCompositeCurveSegment(polyLine, IfcTransitionCode.CONTINUOUS, true);
                        segments.Add(polyLinecompositeCurveSegment);
                    }
                    polyLinePoints.Clear();
                }
            
                var radius = rounds[i];
                var axis2Placement2D = EntityCreator.CreateAxis2Placement2D(data[2]);

                var circle = EntityCreator.CreateCircle(Math.Abs(radius.Round()), axis2Placement2D);
               
                var trimP1 = data[0];
                var trimP2 = data[1];
                var trimmedCurve = EntityCreator.CreateTrimmedCurve(circle, IfcTrimmingPreference.CARTESIAN,
                    EntityCreator.CreateCartesianPoint(trimP1), EntityCreator.CreateCartesianPoint(trimP2),
                    radius < 0.0 ? !senseAgreements[i] : senseAgreements[i]);

                var compositeCurveSegment = EntityCreator.CreateCompositeCurveSegment(trimmedCurve, IfcTransitionCode.CONTINUOUS, true);
                segments.Add(compositeCurveSegment);

                polyLinePoints.Add(data[1]);
                if (i == curveObjects.Length - 1)
                {
                    XbimPoint3D firstPoint;
                    if (curveObjects[0] is XbimPoint3D)
                    {
                        firstPoint = (XbimPoint3D)curveObjects[0];
                    }
                    else
                    {
                        firstPoint = ((XbimPoint3D[])curveObjects[0])[0];
                    }

                    if (polyLinePoints.Count == 0 || !polyLinePoints.Last().Equals(firstPoint))
                    {
                        polyLinePoints.Add(firstPoint);
                    }
                }
            }

            polyLinePoints = RemoveExcessPoints(polyLinePoints);
            if (polyLinePoints.Count > 1)
            {
                var polyLine = EntityCreator.CreatePolyLine(polyLinePoints.Select(p => EntityCreator.CreateCartesianPoint(p)).ToList());
                var polyLineCompositeCurveSegment = EntityCreator.CreateCompositeCurveSegment(polyLine, IfcTransitionCode.CONTINUOUS,
                    true);
                segments.Add(polyLineCompositeCurveSegment);
            }
            polyLinePoints.Clear();            

            var compositeCurve = EntityCreator.CreateCompositeCurve(segments, true);

            var arbitraryClosedProfileDef = EntityCreator.CreateArbitraryClosedProfileDef(compositeCurve, IfcProfileTypeEnum.AREA);

            return arbitraryClosedProfileDef;
        }

        private static List<XbimPoint3D> RemoveExcessPoints(List<XbimPoint3D> input)
        {
            var output = new List<XbimPoint3D>();
            for (var i = 0; i < input.Count; i++)
            {
                if (i == 0)
                {
                    output.Add(input[i]);
                    continue;
                }
                var prev = output.Last();
                var curr = input[i];
                if ((prev - curr).Length >= 1.0)
                {
                    output.Add(curr);
                    continue;
                }
                if (i < input.Count - 1)continue;
            
                output[output.Count-1] =curr;
            }
            return output;
        }

        private static void FixPoints(ref List<XbimPoint3D> points, ref List<double> rounds, ref object[] curves, ref List<bool> agreements)
        {
            for (var i = 0; i < points.Count; i++)
            {
                var c = curves[i];
                if (c == null)
                    c = curves[i - 1];
                var k = i == points.Count - 1 ? 0 : i + 1;
                var n = curves[k];
                XbimPoint3D first, second;

                if (c is XbimPoint3D)
                {
                    first = (XbimPoint3D)c;
                }
                else
                {
                    first = ((XbimPoint3D[])c)[1];
                    if (((XbimPoint3D[])c)[0].Equals(first))
                    {
                        points.RemoveAt(i);
                        rounds.RemoveAt(i);
                        agreements.RemoveAt(i);
                        curves = curves.Where(x => x != c).ToArray();
                        FixPoints(ref points, ref rounds, ref curves, ref agreements);
                        break;
                    }
                }
                if (n is XbimPoint3D)
                {
                    second = (XbimPoint3D)n;
                }
                else
                {
                    second = ((XbimPoint3D[])n)[0];
                    if (((XbimPoint3D[])n)[1].Equals(second))
                    {
                        points.RemoveAt(k);
                        rounds.RemoveAt(k);
                        agreements.RemoveAt(k);
                        curves = curves.Where(x => x != n).ToArray();
                        FixPoints(ref points, ref rounds, ref curves, ref agreements);
                        break;
                    }
                }

                var v1 = (points[i] - points[k]).Normalized();
                var v2 = (first - second).Normalized();

                if (!(v1.Angle(v2) > Math.PI / 2.0)) continue;

                if (c is XbimPoint3D[] && n is XbimPoint3D[])
                {
                    var cCircle = (XbimPoint3D[])c;
                    var nCircle = (XbimPoint3D[])n;
                    XbimPoint3D firstIntersect, secondIntersect;
                    var intersectCount = GetCircleIntersection(cCircle[2].X, cCircle[2].Y, rounds[i],
                        nCircle[2].X,
                        nCircle[2].Y,
                        rounds[k], out firstIntersect, out secondIntersect);
                    if (intersectCount == 0) continue;

                    var interPoint = intersectCount > 1
                        ? ((first - firstIntersect).Length < (first - secondIntersect).Length
                            ? firstIntersect
                            : secondIntersect)
                        : firstIntersect;
                    cCircle[1] = interPoint;
                    nCircle[0] = interPoint;
                }
                else if (c is XbimPoint3D && n is XbimPoint3D[])
                {
                    points.RemoveAt(i);
                    rounds.RemoveAt(i);
                    agreements.RemoveAt(i);
                    curves = curves.Where(x => x != c).ToArray();
                    FixPoints(ref points, ref rounds, ref curves, ref agreements);
                    break;
                }
                else if (c is XbimPoint3D[] && n is XbimPoint3D)
                {
                    points.RemoveAt(k);
                    rounds.RemoveAt(k);
                    agreements.RemoveAt(k);
                    curves = curves.Where(x => x != n).ToArray();
                    FixPoints(ref points, ref rounds, ref curves, ref agreements);
                    break;
                }
            }
        }

        internal static IfcExtrudedAreaSolid CreateExtrudedAreaSolid(IfcAxis2Placement3D axis2Placement3D,
            IfcProfileDef profileDef, XbimVector3D extrudeVector, double depth)
        {
            extrudeVector = extrudeVector.Round();
            depth = depth < 1.0 ? 1.0 : depth.Round();
            var extrudedAreaSolid = EntityCreator.CreateExtrudedAreaSolid(profileDef, axis2Placement3D, EntityCreator.CreateDirection(extrudeVector), depth);
            return extrudedAreaSolid;
        }

        internal static IfcRevolvedAreaSolid CreateRevolvedAreaSolid(IfcAxis2Placement3D axis2Placement3D,
            IfcProfileDef profileDef, double angle)
        {
            var axis1Placement = EntityCreator.CreateAxis1Placement(
                EntityCreator.CreateCartesianPoint(new XbimPoint3D(0.0, 0.0, 0.0)),
                EntityCreator.CreateDirection(new XbimVector3D(angle < 0.0 ? -1.0 : 1.0, 0.0, 0.0)));
            var revolvedAreaSolid = EntityCreator.CreateRevolvedAreaSolid(profileDef, axis2Placement3D, axis1Placement, angle);

            return revolvedAreaSolid;
        }

        private static int GetCircleIntersection(double x10, double y10, double r1,//координаты центра и радиус
                        double x20, double y20, double r2,
                        out XbimPoint3D first, out XbimPoint3D second) //возвращаем результаты
        {
            double x0, y0;//координаты точки пересечения всех линий
            double d;//расстояние между центрами окружностей
            double a;//расстояние от r1 до точки пересечения всех линий
            double h;//расстояние от точки пересеч окружностей до точки пересеч всех линий
            first = new XbimPoint3D();
            second = new XbimPoint3D();

            d = Math.Sqrt(Math.Pow(Math.Abs(x10 - x20), 2) + Math.Pow(Math.Abs(y10 - y20), 2));

            if (d > r1 + r2) return 0; //окружности не пересекаются

            a = (r1 * r1 - r2 * r2 + d * d) / (2 * d);
            h = Math.Sqrt(Math.Pow(r1, 2) - Math.Pow(a, 2));


            x0 = x10 + a * (x20 - x10) / d;
            y0 = y10 + a * (y20 - y10) / d;

            first = new XbimPoint3D(Math.Round(x0 + h * (y20 - y10) / d, 4), Math.Round(y0 - h * (x20 - x10) / d, 4), double.MinValue);
            if (a == r1) return 1; //окружности соприкасаются

            second = new XbimPoint3D(Math.Round(x0 - h * (y20 - y10) / d, 4), Math.Round(y0 + h * (x20 - x10) / d, 4), double.MinValue);

            return 2;//окружности пересекаются
        }
    }
}