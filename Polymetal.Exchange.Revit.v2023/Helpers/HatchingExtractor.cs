using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Polymetal.Exchange.Revit.v2023.Helpers
{
    internal class HatchingExtractor
    {
        private static readonly double MinLineLength = UnitUtils.ConvertToInternalUnits(100, UnitTypeId.Millimeters);

        internal static List<Utilities.DTO.HatchLine> GetHatchLines(Element element, out string materialName)
        {
            materialName = string.Empty;
            if (!(element is Ceiling ceiling))
                return null;

            Reference reference = HostObjectUtils.GetBottomFaces(ceiling).FirstOrDefault();
            var planarFace = ceiling.GetGeometryObjectFromReference(reference) as PlanarFace;

            var hatchLines = AnalyzeHatch(ceiling, reference, out materialName);
            if (hatchLines == null || hatchLines.Count == 0)
                return null;

            var planarFaceTransform = planarFace.ComputeDerivatives(UV.Zero);

            var curveLoopList = planarFace.GetEdgesAsCurveLoops().ToList();

            var result = new List<Utilities.DTO.HatchLine>();

            var boundingLines = GetBoundingLines(planarFace);

            foreach (var hatchLine in hatchLines)
            {
                var step = Math.Abs(hatchLine.step);
                if (step == 0.0)
                    continue;

                var point = planarFaceTransform.Inverse.OfPoint(hatchLine.point);

                point = new XYZ(point.X, point.Y, 0.0);
                if (!planarFace.IsInside(new UV(point.X, point.Y)))
                {
                    if (boundingLines.Count > 0)
                    {
                        var boundPoint = boundingLines[0].GetEndPoint(0);
                        boundPoint = planarFaceTransform.Inverse.OfPoint(boundPoint);

                        boundPoint = new XYZ(boundPoint.X, boundPoint.Y, 0.0);
                        point = new XYZ(point.X, point.Y, 0.0);

                        var offsetVector = (point - boundPoint).Normalize();
                        offsetVector = new XYZ(Math.Sign(offsetVector.X) * Math.Abs(point.X - boundPoint.X) % step, Math.Sign(offsetVector.Y) * Math.Abs(point.Y - boundPoint.Y) % step, 0.0);
                        point = boundPoint.Add(offsetVector);
                    }
                }

                point = planarFaceTransform.OfPoint(point);

                var direction = planarFaceTransform.Inverse.OfVector(hatchLine.direction);
                direction = planarFaceTransform.OfVector(new XYZ(direction.X, direction.Y, 0.0)).Normalize();

                var line = Line.CreateBound(point.Add(direction.Multiply(-1000)), point.Add(direction.Multiply(1000)));

                var index = 0;
                while (true)
                {
                    var tmpLine = line.CreateOffset(step * index, planarFaceTransform.BasisZ) as Line;
                    var intersectionPoints = GetIntersectionPoints(tmpLine, curveLoopList);

                    if (!LineIntersectBound(tmpLine, boundingLines) && index > 1)
                    {
                        if (step < 0)
                            break;

                        step *= -1;
                        index = 1;
                        continue;
                    }

                    for (var i = 1; i < intersectionPoints.Count; i++)
                    {
                        var p1 = intersectionPoints[i - 1];
                        var p2 = intersectionPoints[i];

                        if ((p2 - p1).GetLength() < MinLineLength)
                            continue;

                        var midPoint = planarFaceTransform.Inverse.OfPoint(p1.Add(p2).Multiply(0.5));
                        if (!planarFace.IsInside(new UV(midPoint.X, midPoint.Y)))
                            continue;

                        var start = new Utilities.DTO.Point(
                            Math.Round(UnitUtils.ConvertFromInternalUnits(p1.X, UnitTypeId.Millimeters), 4),
                            Math.Round(UnitUtils.ConvertFromInternalUnits(p1.Y, UnitTypeId.Millimeters), 4),
                            Math.Round(UnitUtils.ConvertFromInternalUnits(p1.Z, UnitTypeId.Millimeters), 4));
                        var end = new Utilities.DTO.Point(
                            Math.Round(UnitUtils.ConvertFromInternalUnits(p2.X, UnitTypeId.Millimeters), 4),
                            Math.Round(UnitUtils.ConvertFromInternalUnits(p2.Y, UnitTypeId.Millimeters), 4),
                            Math.Round(UnitUtils.ConvertFromInternalUnits(p2.Z, UnitTypeId.Millimeters), 4));

                        result.Add(new Utilities.DTO.HatchLine(start, end));
                    }

                    index++;
                }
            }

            return result;
        }

        private static List<Line> GetBoundingLines(PlanarFace planarFace)
        {
            var boundingBox = planarFace.GetBoundingBox();
            var minBouningBox = planarFace.Evaluate(boundingBox.Min);
            var maxBouningBox = planarFace.Evaluate(boundingBox.Max);

            var boundingLines = new List<Line> {
                Line.CreateBound(new XYZ(minBouningBox.X, minBouningBox.Y, planarFace.Origin.Z), new XYZ(maxBouningBox.X, minBouningBox.Y, planarFace.Origin.Z)),
                Line.CreateBound(new XYZ(maxBouningBox.X, minBouningBox.Y, planarFace.Origin.Z), new XYZ(maxBouningBox.X, maxBouningBox.Y, planarFace.Origin.Z)),
                Line.CreateBound(new XYZ(maxBouningBox.X, maxBouningBox.Y, planarFace.Origin.Z), new XYZ(minBouningBox.X, maxBouningBox.Y, planarFace.Origin.Z)),
                Line.CreateBound(new XYZ(minBouningBox.X, maxBouningBox.Y, planarFace.Origin.Z), new XYZ(minBouningBox.X, minBouningBox.Y, planarFace.Origin.Z)) };

            return boundingLines;
        }

        private static bool LineIntersectBound(Line line, List<Line> boundingLines)
        {
            foreach (var boundingLine in boundingLines)
            {
                var intersectResult = boundingLine.Intersect(line, out var intersectionResultArray);
                if (intersectResult == SetComparisonResult.Overlap && intersectionResultArray != null)
                {
                    return true;
                }
            }

            return false;
        }

        private static List<XYZ> GetIntersectionPoints(Line line, List<CurveLoop> curveLoopList)
        {
            var result = new List<XYZ>();

            foreach (var curveLoop in curveLoopList)
            {
                foreach (var curve in curveLoop)
                {
                    var intersectResult = curve.Intersect(line, out var intersectionResultArray);
                    if (intersectResult == SetComparisonResult.Overlap && intersectionResultArray != null)
                    {
                        foreach (IntersectionResult intersectionResult in intersectionResultArray)
                        {
                            result.Add(intersectionResult.XYZPoint);
                        }
                    }
                }
            }

            var linePoint = line.GetEndPoint(0);

            return result.OrderBy(x => x.DistanceTo(linePoint)).ToList();
        }

        private static List<(XYZ point, XYZ direction, double step)> AnalyzeHatch(Element element, Reference hatchface, out string materialName)
        {
            materialName = string.Empty;

            var document = element.Document;
            if (!(element.GetGeometryObjectFromReference(hatchface) is PlanarFace planarFace))
                return null;

            if (!(document.GetElement(planarFace.MaterialElementId) is Material material))
                return null;

            materialName = material.Name;
            if (!materialName.ToLower().StartsWith("pm_отделка_потолок") && !materialName.ToLower().StartsWith("adsk_потолок_"))
                return null;

            if (!(document.GetElement(material.SurfaceForegroundPatternId) is FillPatternElement fillPatternElement))
                return null;

            var fillPattern = fillPatternElement.GetFillPattern();
            if (fillPattern.IsSolidFill || fillPattern.Target == FillPatternTarget.Drafting)
                return null;

            var result = new List<(XYZ, XYZ, double)>();

            var gridCount = fillPattern.GridCount;
            var stableRef = hatchface.ConvertToStableRepresentation(document);
            using (var transaction = new Transaction(document, "analyse hatch"))
            {
                if (!document.IsModifiable)
                    transaction.Start();

                for (var hatchIndex = 0; hatchIndex < gridCount; hatchIndex++)
                {
                    var referenceArray = new ReferenceArray();
                    for (var i = 0; i < 2; i++)
                    {
                        try
                        {
                            var hatchReference = Reference.ParseFromStableRepresentation(document, $"{stableRef}/{(hatchIndex + 1) + (i * gridCount * 2)}");
                            if (hatchReference != null)
                                referenceArray.Append(hatchReference);
                        }
                        catch
                        { }
                    }

                    if (referenceArray.Size <= 1) 
                        continue;

                    using (var subTransaction = new SubTransaction(document))
                    {
                        subTransaction.Start();

                        var dimension = document.Create.NewDimension(document.ActiveView, Line.CreateBound(XYZ.Zero, new XYZ(10, 0, 0)), referenceArray);
                        ElementTransformUtils.MoveElement(document, dimension.Id, new XYZ(.1, 0, 0));

                        var line = dimension.Curve as Line;
                        var direction = line.Direction;
                        var hatchDirection = direction.CrossProduct(planarFace.FaceNormal).Normalize();
                        var step = (double)dimension.Value;
                        var point = dimension.Origin.Subtract(direction.Multiply(step / 2));

                        result.Add((point, hatchDirection, step));

                        subTransaction.RollBack();
                    }
                }
            }

            return result;
        }
    }
}
