using System;
using System.Collections.Generic;
using System.Linq;
using Xbim.Common.Geometry;
using Xbim.Ifc2x3.GeometricModelResource;
using Xbim.Ifc2x3.GeometryResource;
using Xbim.Ifc2x3.ProfileResource;
using Xbim.Ifc2x3.RepresentationResource;

namespace Polymetal.Pdms.IfcServer
{
    class Representation
    {
        private const double ARCTOLERANCE = 10.0;
        private static Dictionary<int, Dictionary<int, IfcRepresentationItem>> _styledItems;
        internal static XbimMatrix3D RootMatrix;

        internal static void Init()
        {
            _styledItems = new Dictionary<int, Dictionary<int, IfcRepresentationItem>>();
        }

        //internal static IfcRepresentation CreateMappedRepresentation(List<string> geometryLines, ref string layerName)
        //{
        //    var mappedItemList = new List<IfcRepresentationItem>();
        //    foreach (var geometryLine in geometryLines)
        //    {
        //        IfcRepresentationItem mappedItem = CreateMappedItem(geometryLine, ref layerName);
        //        if (mappedItem != null)
        //        {
        //            mappedItemList.Add(mappedItem);
        //        }
        //    }
        //    if (mappedItemList.Count == 0) return null;
        //    var mapShapeRepresentation = EntityCreator.CreateShapeRepresentation(mappedItemList, "Body",
        //        "MappedRepresentation");

        //    return mapShapeRepresentation;
        //}

        //private static IfcMappedItem CreateMappedItem(string geometryLine, ref string layerName)
        //{
        //    var geometry = PdmsGeometry.StringToGeometry(geometryLine);
        //    layerName = string.IsNullOrEmpty(layerName) ? geometry.LayerName : layerName;

        //    var representationItem = CreateRepresentationItem(geometry);

        //    if (representationItem == null) return null;

        //    var diffColourRgb = EntityCreator.CreateColourRgb(geometry.Color);
        //    var surfaceStyleRendering = EntityCreator.CreateSurfaceStyleRendering(diffColourRgb, (double)geometry.Translucency / 100);
        //    var surfaceStyle = EntityCreator.CreateSurfaceStyle(surfaceStyleRendering, geometry.ColorIndex);
        //    var presentationStyleAssignment = EntityCreator.CreatePresentationStyleAssignment(surfaceStyle);

        //    if (_styledItems.ContainsKey(representationItem.EntityLabel))
        //    {
        //        var valueDict = _styledItems[representationItem.EntityLabel];
        //        if (!valueDict.ContainsKey(presentationStyleAssignment.EntityLabel))
        //        {
        //            representationItem = representationItem.Copy();
        //            valueDict.Add(presentationStyleAssignment.EntityLabel, representationItem);
        //        }
        //        else
        //        {
        //            representationItem = valueDict[presentationStyleAssignment.EntityLabel];
        //        }
        //    }
        //    else
        //    {
        //        _styledItems.Add(representationItem.EntityLabel, new Dictionary<int, IfcRepresentationItem>());
        //        _styledItems[representationItem.EntityLabel].Add(presentationStyleAssignment.EntityLabel,representationItem);
        //    }

        //    EntityCreator.CreateStyledItem(representationItem, presentationStyleAssignment);

        //    var shapeRepresentationType = string.Empty;
        //    if (representationItem is IfcFacetedBrep)
        //        shapeRepresentationType = "Brep";
        //    else if (representationItem is IfcExtrudedAreaSolid || representationItem is IfcRevolvedAreaSolid)
        //        shapeRepresentationType = "SweptSolid";
        //    else if (representationItem is IfcBooleanClippingResult)
        //        shapeRepresentationType = "Clipping";
        //    else if (representationItem is IfcBooleanResult)
        //        shapeRepresentationType = "CSG";

        //    var shapeRepresentation = EntityCreator.CreateShapeRepresentation(new List<IfcRepresentationItem> {representationItem},
        //        "Body", shapeRepresentationType);

        //    var mapAxis2Placement3D = EntityCreator.CreateAxis2Placement3D(new XbimPoint3D(0.0, 0.0, 0.0),
        //        new XbimVector3D(0.0, 0.0, 1.0), new XbimVector3D(1.0, 0.0, 0.0), XbimMatrix3D.Identity);

        //    var representationMap = EntityCreator.CreateRepresentationMap(mapAxis2Placement3D, shapeRepresentation);

        //    var rootMatrix = RootMatrix;
        //    rootMatrix.Invert();
        //    var transformationMatrix = geometry.Matrix * rootMatrix;
        //    var cartesianTransformationOperator3D = EntityCreator.CreateCartesianTransformationOperator3D(transformationMatrix);

        //    var mappedItem = EntityCreator.CreateMappedItem(representationMap, cartesianTransformationOperator3D);
        //    return mappedItem;
        //}

        internal static IfcShapeRepresentation GetShapeRepresentation(List<string> geometryLines, ref string layerName)
        {
            var representations = new List<IfcRepresentationItem>();
            foreach (var geometryLine in geometryLines)
            {
                var geometry = PdmsGeometry.StringToGeometry(geometryLine);
                layerName = string.IsNullOrEmpty(layerName) ? geometry.LayerName : layerName;

                var representationItem = CreateRepresentationItem(geometry);

                if (representationItem == null) continue;

                var diffColourRgb = EntityCreator.CreateColourRgb(geometry.Color);
                var surfaceStyleRendering = EntityCreator.CreateSurfaceStyleRendering(diffColourRgb, (double)geometry.Translucency / 100);
                var surfaceStyle = EntityCreator.CreateSurfaceStyle(surfaceStyleRendering, geometry.ColorIndex);
                var presentationStyleAssignment = EntityCreator.CreatePresentationStyleAssignment(surfaceStyle);

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
                    _styledItems[representationItem.EntityLabel].Add(presentationStyleAssignment.EntityLabel, representationItem);
                }

                EntityCreator.CreateStyledItem(representationItem, presentationStyleAssignment);

                representations.Add(representationItem);
            }

            //var shapeRepresentationType = string.Empty;
            //if (representationItem is IfcFacetedBrep)
            //    shapeRepresentationType = "Brep";
            //else if (representationItem is IfcExtrudedAreaSolid || representationItem is IfcRevolvedAreaSolid)
            //    shapeRepresentationType = "SweptSolid";
            //else if (representationItem is IfcBooleanClippingResult)
            //    shapeRepresentationType = "Clipping";
            //else if (representationItem is IfcBooleanResult)
            //    shapeRepresentationType = "CSG";
            if (representations.Count == 0) return null;
            return EntityCreator.CreateShapeRepresentation(representations, "Body", "SweptSolid");
        }

        private static IfcRepresentationItem CreateRepresentationItem(PdmsGeometry geometry)
        {
            IfcRepresentationItem representationItem = null;
            //if (!(geometry is PdmsExtrusion)) return null;

            if (geometry is PdmsPyramid)
            {
                representationItem = CreateRepresentationPyramid(geometry as PdmsPyramid);
            }
            else if (geometry is PdmsBox)
            {
                representationItem = CreateRepresentationBox(geometry as PdmsBox);
            }
            else if (geometry is PdmsRectTorus)
            {
                representationItem = CreateRepresentationRectTorus(geometry as PdmsRectTorus);
            }
            else if (geometry is PdmsCircTorus)
            {
                representationItem = CreateRepresentationCircTorus(geometry as PdmsCircTorus);
            }
            else if (geometry is PdmsEllipticalDish)
            {
                representationItem = CreateRepresentationEllipticalDish(geometry as PdmsEllipticalDish);
            }
            else if (geometry is PdmsSphericalDish)
            {
                representationItem = CreateRepresentationSphericalDish(geometry as PdmsSphericalDish);
            }
            else if (geometry is PdmsSnout)
            {
                representationItem = CreateRepresentationSnout(geometry as PdmsSnout);
            }
            else if (geometry is PdmsCone)
            {
                representationItem = CreateRepresentationCone(geometry as PdmsCone);
            }
            else if (geometry is PdmsCylinder)
            {
                representationItem = CreateRepresentationCylinder(geometry as PdmsCylinder);
            }
            else if (geometry is PdmsSphere)
            {
                representationItem = CreateRepresentationSphere(geometry as PdmsSphere);
            }
            else if (geometry is PdmsRevolution)
            {
                representationItem = CreateRepresentationRevolution(geometry as PdmsRevolution);
            }
            else if (geometry is PdmsExtrusion)
            {
                representationItem = CreateRepresentationExtrusion(geometry as PdmsExtrusion);
            }
            else if (geometry is PdmsSlcy)
            {
                representationItem = CreateRepresentationSlcy(geometry as PdmsSlcy);
            }
            else if (geometry is PdmsFacetGroup)
            {
                representationItem = CreateRepresentationFacetGroup(geometry as PdmsFacetGroup);
            }
            else
            {
                Console.WriteLine(geometry.GetType().Name);
            }

            if (representationItem != null && geometry.Negatives.Count > 0)
            {
                //поочередное добавление негативов к позитивному элементу
                IfcBooleanResult booleanRepresentation = null;
                var firstOperand = representationItem;
                foreach (var negative in geometry.Negatives)
                {
                    var nRepresentation = CreateRepresentationItem(negative);
                    if (nRepresentation == null) continue;

                    IfcBooleanOperand first, second;
                    if (firstOperand is IfcSolidModel)
                        first = (IfcSolidModel)firstOperand;
                    else
                        first = (IfcBooleanResult)firstOperand;

                    if (nRepresentation is IfcSolidModel)
                        second = (IfcSolidModel)nRepresentation;
                    else
                        second = (IfcBooleanResult)nRepresentation;

                    booleanRepresentation = EntityCreator.CreateBooleanResult(first, second,
                        IfcBooleanOperator.DIFFERENCE);

                    firstOperand = booleanRepresentation;
                }
                if (booleanRepresentation!=null)
                    representationItem = booleanRepresentation;

                //создание объединенного негатива и вычитание его из позитивного
                //IfcBooleanResult booleanRepresentation = null;
                //var firstOperand = CreateRepresentationItem(geometry.Negatives[0]);
                //for (int i = 1; i < geometry.Negatives.Count; i++)
                //{
                //    var negative = geometry.Negatives[i];
                //    var nRepresentation = CreateRepresentationItem(negative);
                //    if (nRepresentation == null) continue;

                //    IfcBooleanOperand first, second;
                //    if (firstOperand is IfcSolidModel)
                //        first = (IfcSolidModel)firstOperand;
                //    else
                //        first = (IfcBooleanResult)firstOperand;

                //    if (nRepresentation is IfcSolidModel)
                //        second = (IfcSolidModel)nRepresentation;
                //    else
                //        second = (IfcBooleanResult)nRepresentation;

                //    booleanRepresentation = EntityCreator.CreateBooleanResult(first, second,
                //        IfcBooleanOperator.UNION);

                //    firstOperand = booleanRepresentation;
                //}
                //if (firstOperand is IfcSolidModel)
                //    representationItem = EntityCreator.CreateBooleanResult((IfcSolidModel)representationItem,
                //        (IfcSolidModel)firstOperand,
                //        IfcBooleanOperator.DIFFERENCE);
                //else
                //    representationItem = EntityCreator.CreateBooleanResult((IfcSolidModel)representationItem,
                //        (IfcBooleanResult)firstOperand,
                //        IfcBooleanOperator.DIFFERENCE);

                //объединение пересекающихся частей и вычитание их из позитивного элемента
                //var intersects = new List<IfcBooleanResult>();
                //foreach (var negative in geometry.Negatives)
                //{
                //    var nRepresentation = CreateRepresentationItem(negative);
                //    if (nRepresentation == null) continue;
                //    var firstOperand = representationItem;
                //    var secondOperand = nRepresentation;
                //    IfcBooleanOperand first, second;
                //    if (firstOperand is IfcSolidModel)
                //        first = (IfcSolidModel)firstOperand;
                //    else
                //        first = (IfcBooleanResult)firstOperand;

                //    if (nRepresentation is IfcSolidModel)
                //        second = (IfcSolidModel)nRepresentation;
                //    else
                //        second = (IfcBooleanResult)nRepresentation;

                //    intersects.Add(EntityCreator.CreateBooleanResult(first, second,IfcBooleanOperator.INTERSECTION));
                //}

                //IfcBooleanResult intersectResult = intersects[0];
                //for (var i = 1; i < intersects.Count; i++)
                //{
                //    intersectResult = EntityCreator.CreateBooleanResult(intersectResult, intersects[i],
                //        IfcBooleanOperator.UNION);
                //}

                ////representationItem = intersectResult;
                //if (representationItem is IfcSolidModel)
                //    representationItem = EntityCreator.CreateBooleanResult((IfcSolidModel)representationItem,
                //        (IfcBooleanResult)intersectResult,
                //        IfcBooleanOperator.DIFFERENCE);
                //else
                //    representationItem = EntityCreator.CreateBooleanResult((IfcBooleanResult)representationItem,
                //        (IfcBooleanResult)intersectResult,
                //        IfcBooleanOperator.DIFFERENCE);
            }

            return representationItem;
        }

        private static IfcRepresentationItem CreateRepresentationPyramid(PdmsPyramid pdmsPyramid)
        {
            if (pdmsPyramid.Height <= 0.0 || (pdmsPyramid.Xbottom <= 0.0 && pdmsPyramid.Xtop <= 0.0) || (pdmsPyramid.Ybottom == 0.0 && pdmsPyramid.Ytop == 0.0)) return null;

            var b1 = new XbimPoint3D(-pdmsPyramid.Xoffset / 2.0 - pdmsPyramid.Xbottom / 2.0,
                -pdmsPyramid.Yoffset / 2.0 - pdmsPyramid.Ybottom / 2.0, -pdmsPyramid.Height / 2.0);
            var b2 = new XbimPoint3D(b1.X, b1.Y + pdmsPyramid.Ybottom, b1.Z);
            var b3 = new XbimPoint3D(b1.X + pdmsPyramid.Xbottom, b1.Y + pdmsPyramid.Ybottom, b1.Z);
            var b4 = new XbimPoint3D(b1.X + pdmsPyramid.Xbottom, b1.Y, b1.Z);

            var t1 = new XbimPoint3D(pdmsPyramid.Xoffset / 2.0 - pdmsPyramid.Xtop / 2.0,
                pdmsPyramid.Yoffset / 2.0 - pdmsPyramid.Ytop / 2.0, pdmsPyramid.Height / 2.0);
            var t2 = new XbimPoint3D(t1.X, t1.Y + pdmsPyramid.Ytop, t1.Z);
            var t3 = new XbimPoint3D(t1.X + pdmsPyramid.Xtop, t1.Y + pdmsPyramid.Ytop, t1.Z);
            var t4 = new XbimPoint3D(t1.X + pdmsPyramid.Xtop, t1.Y, t1.Z);

            
                var m = pdmsPyramid.Matrix;
                b1 = m.Transform(b1);
                b2 = m.Transform(b2);
                b3 = m.Transform(b3);
                b4 = m.Transform(b4);

                t1 = m.Transform(t1);
                t2 = m.Transform(t2);
                t3 = m.Transform(t3);
                t4 = m.Transform(t4);
            
            var faces = new List<List<XbimPoint3D>>
            {
                new List<XbimPoint3D> { b1, b2, b3, b4},
                new List<XbimPoint3D> {t4, t3, t2, t1},
                new List<XbimPoint3D> {t1, t2, b2, b1},
                new List<XbimPoint3D> {t2, t3, b3, b2},
                new List<XbimPoint3D> {t3, t4, b4, b3},
                new List<XbimPoint3D> {t4, t1, b1, b4}
            };

            return CreateFacetedBrep(faces);
        }

        private static IfcRepresentationItem CreateRepresentationBox(PdmsBox pdmsBox)
        {
            if (pdmsBox.Xlength < 1.0 || pdmsBox.Ylength < 1.0 || pdmsBox.Zlength < 1.0) return null;

            var rectangleProfileDef = EntityCreator.CreateRectangleProfileDef(pdmsBox.Xlength, pdmsBox.Ylength);

            var axis2Placement3D = EntityCreator.CreateAxis2Placement3D(new XbimPoint3D(0.0, 0.0, -pdmsBox.Zlength / 2.0),
                new XbimVector3D(0.0, 0.0, 1.0), new XbimVector3D(1.0, 0.0, 0.0), pdmsBox.Matrix);

            return CreateExtrudedAreaSolid(axis2Placement3D, rectangleProfileDef,
                new XbimVector3D(0, 0, 1), pdmsBox.Zlength);
        }

        private static IfcRepresentationItem CreateRepresentationRectTorus(PdmsRectTorus pdmsRectTorus)
        {
            if (pdmsRectTorus.Rinside < 1.0)
                pdmsRectTorus.Rinside = 1.0;
            if (pdmsRectTorus.Angle <= 0.0 || pdmsRectTorus.Height <= 0.0 || pdmsRectTorus.Routside <= pdmsRectTorus.Rinside) return null;

            var rectangleProfileDef = EntityCreator.CreateRectangleProfileDef(pdmsRectTorus.Height,
                pdmsRectTorus.Routside - pdmsRectTorus.Rinside,
                pdmsRectTorus.Rinside + (pdmsRectTorus.Routside - pdmsRectTorus.Rinside) / 2.0);

            var axis2Placement3D =
                EntityCreator.CreateAxis2Placement3D(
                    new XbimPoint3D(0.0, 0.0, 0.0),
                    new XbimVector3D(0.0, 1.0, 0.0), new XbimVector3D(0.0, 0.0, 1.0), pdmsRectTorus.Matrix);

            return CreateRevolvedAreaSolid(axis2Placement3D, rectangleProfileDef, pdmsRectTorus.Angle);
        }

        private static IfcRepresentationItem CreateRepresentationCircTorus(PdmsCircTorus pdmsCircTorus)
        {
            if (pdmsCircTorus.Angle <= 0.0175 || pdmsCircTorus.Radius <= 0.0 || pdmsCircTorus.Rcentre <= 0.0) return null;

            if (pdmsCircTorus.Radius == pdmsCircTorus.Rcentre)
                pdmsCircTorus.Rcentre += 0.01;

            var circleProfileDef = EntityCreator.CreateCircleProfileDef(pdmsCircTorus.Radius, pdmsCircTorus.Rcentre);

            var axis2Placement3D = EntityCreator.CreateAxis2Placement3D(new XbimPoint3D(0.0, 0.0, 0.0),
                new XbimVector3D(0.0, 1.0, 0.0), new XbimVector3D(0.0, 0.0, 1.0), pdmsCircTorus.Matrix);

            return CreateRevolvedAreaSolid(axis2Placement3D, circleProfileDef, pdmsCircTorus.Angle);
        }

        private static IfcRepresentationItem CreateRepresentationEllipticalDish(PdmsEllipticalDish pdmsEllipticalDish)
        {
            if (pdmsEllipticalDish.Radius <= 0.0) return null;

            IfcAxis2Placement3D axis2Placement3D;
            if (pdmsEllipticalDish.Height <= 0.0)
            {
                pdmsEllipticalDish.Height = 1.0;
                var circleProfileDef = EntityCreator.CreateCircleProfileDef(pdmsEllipticalDish.Radius);

                axis2Placement3D = EntityCreator.CreateAxis2Placement3D(new XbimPoint3D(0.0, 0.0, -pdmsEllipticalDish.Height / 2.0),
                   new XbimVector3D(0.0, 0.0, 1.0), new XbimVector3D(1.0, 0.0, 0.0), pdmsEllipticalDish.Matrix);

                return CreateExtrudedAreaSolid(axis2Placement3D, circleProfileDef,
                    new XbimVector3D(0, 0, 1), pdmsEllipticalDish.Height);
            }

            var ellipseProfileDef = CreateEllipticalDishProfileDef(pdmsEllipticalDish.Radius, pdmsEllipticalDish.Height);

            axis2Placement3D = EntityCreator.CreateAxis2Placement3D(new XbimPoint3D(0.0, 0.0, 0.0),
                new XbimVector3D(0.0, 1.0, 0.0), new XbimVector3D(0.0, 0.0, 1.0), pdmsEllipticalDish.Matrix);

            return CreateRevolvedAreaSolid(axis2Placement3D, ellipseProfileDef, 2.0 * Math.PI);
        }

        private static IfcRepresentationItem CreateRepresentationSphericalDish(PdmsSphericalDish pdmsSphericalDish)
        {
            if (pdmsSphericalDish.Radius <= 0.0) return null;

            IfcAxis2Placement3D axis2Placement3D;
            if (pdmsSphericalDish.Height <= 0.0)
            {
                pdmsSphericalDish.Height = 1;
                var circleProfileDef = EntityCreator.CreateCircleProfileDef(pdmsSphericalDish.Radius);

                axis2Placement3D = EntityCreator.CreateAxis2Placement3D(new XbimPoint3D(0.0, 0.0, -pdmsSphericalDish.Height / 2.0),
                   new XbimVector3D(0.0, 0.0, 1.0), new XbimVector3D(1.0, 0.0, 0.0), pdmsSphericalDish.Matrix);

                return CreateExtrudedAreaSolid(axis2Placement3D, circleProfileDef,
                    new XbimVector3D(0, 0, 1), pdmsSphericalDish.Height);
            }

            var r = pdmsSphericalDish.Radius;
            var H = pdmsSphericalDish.Height;

            var sphereRadius = Math.Sqrt(r * r + Math.Pow((H * H - r * r) / (2 * H), 2.0));

            var axis2Placement2D = EntityCreator.CreateAxis2Placement2D(new XbimPoint3D(0.0, 0.0, double.MinValue));

            var circle = EntityCreator.CreateCircle(sphereRadius, axis2Placement2D);

            var trimmedCurve = EntityCreator.CreateTrimmedCurve(circle, IfcTrimmingPreference.CARTESIAN,
                EntityCreator.CreateCartesianPoint(new XbimPoint3D(sphereRadius - H, pdmsSphericalDish.Radius, double.MinValue)),
                EntityCreator.CreateCartesianPoint(new XbimPoint3D(sphereRadius, 0.0, double.MinValue)), false);

            var polyLine = EntityCreator.CreatePolyLine(new List<IfcCartesianPoint>
            {
                EntityCreator.CreateCartesianPoint(new XbimPoint3D(sphereRadius, 0.0, double.MinValue)),
                EntityCreator.CreateCartesianPoint(new XbimPoint3D(sphereRadius - H, 0.0, double.MinValue)),
                EntityCreator.CreateCartesianPoint(new XbimPoint3D(sphereRadius - H, pdmsSphericalDish.Radius, double.MinValue))
            });

            var compositeCurveSegment1 = EntityCreator.CreateCompositeCurveSegment(trimmedCurve, IfcTransitionCode.CONTINUOUS, true);
            var compositeCurveSegment2 = EntityCreator.CreateCompositeCurveSegment(polyLine, IfcTransitionCode.CONTINUOUS, true);
            var compositeCurve = EntityCreator.CreateCompositeCurve(new List<IfcCompositeCurveSegment> { compositeCurveSegment1, compositeCurveSegment2 }, false);
            var arbitraryClosedProfileDef = EntityCreator.CreateArbitraryClosedProfileDef(compositeCurve, IfcProfileTypeEnum.AREA);
            
            axis2Placement3D = EntityCreator.CreateAxis2Placement3D(new XbimPoint3D(0.0, 0.0, H - sphereRadius),
                new XbimVector3D(0.0, 1.0, 0.0), new XbimVector3D(0.0, 0.0, 1.0), pdmsSphericalDish.Matrix);

            return CreateRevolvedAreaSolid(axis2Placement3D, arbitraryClosedProfileDef, 2.0 * Math.PI);
        }

        private static IfcRepresentationItem CreateRepresentationSnout(PdmsSnout pdmsSnout)
        {
            if (pdmsSnout.Height <= 0.0 || (pdmsSnout.Rbottom <= 0.0 && pdmsSnout.Rtop <= 0.0)) return null;

            if (pdmsSnout.Xoffset == 0.0 && pdmsSnout.Yoffset == 0.0)
            {
                IfcAxis2Placement3D axis2Placement3D;
                if (pdmsSnout.Rbottom == pdmsSnout.Rtop)
                {
                    if (pdmsSnout.Rbottom == 0.0) return null;
                    var circleProfileDef = EntityCreator.CreateCircleProfileDef(pdmsSnout.Rbottom);

                    axis2Placement3D = EntityCreator.CreateAxis2Placement3D(new XbimPoint3D(0.0, 0.0, -pdmsSnout.Height / 2.0),
                        new XbimVector3D(0.0, 0.0, 1.0), new XbimVector3D(1.0, 0.0, 0.0), pdmsSnout.Matrix);

                    return CreateExtrudedAreaSolid(axis2Placement3D, circleProfileDef,
                        new XbimVector3D(0, 0, 1), pdmsSnout.Height);
                }

                //pdmsSnout.Rbottom != pdmsSnout.Rtop
                var points = new List<double[]>
                {
                    new[] {-pdmsSnout.Height/2.0, 0.0, 0.0, 0.0},
                    new[] {pdmsSnout.Height/2.0, 0, 0, 0.0},
                    new[] {pdmsSnout.Height/2.0, pdmsSnout.Rtop, 0, 0.0},
                    new[] {-pdmsSnout.Height/2.0, pdmsSnout.Rbottom, 0, 0.0}
                };

                var pointsProfileDef = CreatePointsProfileDef(points);

                axis2Placement3D =
                    EntityCreator.CreateAxis2Placement3D(
                        new XbimPoint3D(0.0, 0.0, 0.0),
                        new XbimVector3D(1.0, 0.0, 0.0), new XbimVector3D(0.0, 0.0, 1.0), pdmsSnout.Matrix);

                return CreateRevolvedAreaSolid(axis2Placement3D, pointsProfileDef, 2.0*Math.PI);
            }

            //if (pdmsSnout.Rbottom == pdmsSnout.Rtop)
            //{
            //    var v =
            //        (new XbimPoint3D(pdmsSnout.Xoffset/2, pdmsSnout.Yoffset/2, pdmsSnout.Height/2) -
            //         new XbimPoint3D(-pdmsSnout.Xoffset/2, -pdmsSnout.Yoffset/2, -pdmsSnout.Height/2)).Normalized();
            //    var y = new XbimVector3D(0, 1, 0);
            //    var x = v.CrossProduct(y);

            //    var alfa = Math.PI - x.Angle(new XbimVector3D(1, 0, 0));
            //    var d = alfa*180/Math.PI;
            //    pdmsSnout.Rtop = pdmsSnout.Rtop*Math.Cos(alfa);
            //    pdmsSnout.Rbottom = pdmsSnout.Rbottom*Math.Cos(alfa);
            //    var height = pdmsSnout.Height*20;
            //    points = new List<double[]>
            //    {
            //        new[] {-height/2.0, 0.0, 0.0, 0.0},
            //        new[] {height/2.0, 0, 0, 0.0},
            //        new[] {height/2.0, pdmsSnout.Rtop, 0, 0.0},
            //        new[] {-height/2.0, pdmsSnout.Rbottom, 0, 0.0}
            //    };

            //    pointsProfileDef = CreatePointsProfileDef(points);

            //    axis2Placement3D = EntityCreator.CreateAxis2Placement3D(new XbimPoint3D(0.0, 0.0, 0.0), x, v);

            //    revolvedAreaSolid = CreateRevolvedAreaSolid(axis2Placement3D, pointsProfileDef, Math.PI*2);

            //    var halfSpaceSolidBottom = _modelStore.Instances.New<IfcHalfSpaceSolid>();
            //    var planeBottom = _modelStore.Instances.New<IfcPlane>();
            //    halfSpaceSolidBottom.BaseSurface = planeBottom;
            //    halfSpaceSolidBottom.AgreementFlag = false;
            //    planeBottom.Position = EntityCreator.CreateAxis2Placement3D(new XbimPoint3D(0, 0, -pdmsSnout.Height/2),
            //        new XbimVector3D(0, 0, -1), new XbimVector3D(1, 0, 0));

            //    var booleanClippingResult1 = _modelStore.Instances.New<IfcBooleanClippingResult>();
            //    booleanClippingResult1.Operator = IfcBooleanOperator.DIFFERENCE;
            //    booleanClippingResult1.FirstOperand = revolvedAreaSolid;
            //    booleanClippingResult1.SecondOperand = halfSpaceSolidBottom;

            //    var halfSpaceSolidTop = _modelStore.Instances.New<IfcHalfSpaceSolid>();
            //    var planeTop = _modelStore.Instances.New<IfcPlane>();
            //    halfSpaceSolidTop.BaseSurface = planeTop;
            //    halfSpaceSolidTop.AgreementFlag = false;
            //    planeTop.Position = EntityCreator.CreateAxis2Placement3D(new XbimPoint3D(0, 0, pdmsSnout.Height/2),
            //        new XbimVector3D(0, 0, 1), new XbimVector3D(1, 0, 0));

            //    var booleanClippingResult = _modelStore.Instances.New<IfcBooleanClippingResult>();
            //    booleanClippingResult.Operator = IfcBooleanOperator.DIFFERENCE;
            //    booleanClippingResult.FirstOperand = booleanClippingResult1;
            //    booleanClippingResult.SecondOperand = halfSpaceSolidTop;

            //    return CreateShapeRepresentation(new List<IfcRepresentationItem> {booleanClippingResult}, "Body",
            //        "Clipping");
            //}

            var mbot = new XbimMatrix3D
            {
                OffsetX = -pdmsSnout.Xoffset / 2.0,
                OffsetY = -pdmsSnout.Yoffset / 2.0,
                OffsetZ = -pdmsSnout.Height / 2.0
            };

            var mtop = new XbimMatrix3D
            {
                OffsetX = pdmsSnout.Xoffset / 2.0,
                OffsetY = pdmsSnout.Yoffset / 2.0,
                OffsetZ = pdmsSnout.Height / 2.0
            };

            var faces = new List<List<XbimPoint3D>>();
            var pointCount = Math.Ceiling(2 * Math.PI / Math.Asin(ARCTOLERANCE / 100));
            var angle = Math.PI * 2 / pointCount;
            var faceBottom = new List<XbimPoint3D>();
            var faceTop = new List<XbimPoint3D>();

            var pBot = new XbimPoint3D(pdmsSnout.Rbottom, 0, 0);
            var pTop = new XbimPoint3D(pdmsSnout.Rtop, 0, 0);

            for (var i = 0; i < pointCount; i++)
            {
                faceBottom.Add(mbot.Transform(pBot));
                faceTop.Add(mtop.Transform(pTop));
                mbot.RotateAroundZAxis(angle);
                mtop.RotateAroundZAxis(angle);
            }

            faces.Add(faceBottom);
            faces.Add(faceTop);

            for (var i = 0; i < pointCount; i++)
            {
                var face = new List<XbimPoint3D>
                {
                    faceBottom[i],
                    faceTop[i],
                    faceTop[i + 1 >= pointCount ? 0 : i + 1],
                    faceBottom[i + 1 >= pointCount ? 0 : i + 1]
                };
                faces.Add(face);
            }

            var trFaces = new List<List<XbimPoint3D>>();
            foreach (var face in faces)
            {
                var f = new List<XbimPoint3D>();
                foreach (var point in face)
                {
                    f.Add(pdmsSnout.Matrix.Transform(point));
                }
                trFaces.Add(f);
            }
            
            return CreateFacetedBrep(trFaces);
        }

        private static IfcRepresentationItem CreateRepresentationCone(PdmsCone pdmsCone)
        {
            if (pdmsCone.Height <= 0.0 || (pdmsCone.Rbottom <= 0.0 && pdmsCone.Rtop <= 0.0)) return null;

            //todo: проблемы с конусами в Revit (маленькие размеры валят оборудование)
            IfcAxis2Placement3D axis2Placement3D;
            if (pdmsCone.Rbottom == pdmsCone.Rtop)
            {
                var circleProfileDef = EntityCreator.CreateCircleProfileDef(pdmsCone.Rbottom);

                axis2Placement3D = EntityCreator.CreateAxis2Placement3D(new XbimPoint3D(0.0, 0.0, -pdmsCone.Height / 2.0),
                    new XbimVector3D(0.0, 0.0, 1.0), new XbimVector3D(1.0, 0.0, 0.0), pdmsCone.Matrix);

                return CreateExtrudedAreaSolid(axis2Placement3D, circleProfileDef,
                    new XbimVector3D(0, 0, 1), pdmsCone.Height);
            }

            var points = new List<double[]>
            {
                new[] {-pdmsCone.Height/2.0, 0.0, 0.0, 0.0},
                new[] {pdmsCone.Height/2.0, 0, 0, 0.0},
                new[] {pdmsCone.Height/2.0, pdmsCone.Rtop, 0, 0.0},
                new[] {-pdmsCone.Height/2.0, pdmsCone.Rbottom, 0, 0.0}
            };
            points.Reverse();
            
            var pointsProfileDef = CreatePointsProfileDef(points);

            axis2Placement3D =
                EntityCreator.CreateAxis2Placement3D(
                    new XbimPoint3D(0.0, 0.0, 0.0),
                    new XbimVector3D(1.0, 0.0, 0.0), new XbimVector3D(0.0, 0.0, 1.0), pdmsCone.Matrix);

            return CreateRevolvedAreaSolid(axis2Placement3D, pointsProfileDef, 2.0 * Math.PI);
        }

        private static IfcRepresentationItem CreateRepresentationCylinder(PdmsCylinder pdmsCylinder)
        {
            if (pdmsCylinder.Radius <= 0.0 || pdmsCylinder.Height <= 0.0) return null;

            var circleProfileDef = EntityCreator.CreateCircleProfileDef(pdmsCylinder.Radius);

            var axis2Placement3D = EntityCreator.CreateAxis2Placement3D(new XbimPoint3D(0.0, 0.0, -pdmsCylinder.Height / 2.0),
                new XbimVector3D(0.0, 0.0, 1.0), new XbimVector3D(1.0, 0.0, 0.0), pdmsCylinder.Matrix);

            return CreateExtrudedAreaSolid(axis2Placement3D, circleProfileDef,
                new XbimVector3D(0, 0, 1), pdmsCylinder.Height);
        }

        private static IfcRepresentationItem CreateRepresentationSphere(PdmsSphere pdmsSphere)
        {
            if (pdmsSphere.Radius <= 5.0) return null;

            //return CreateSphereSolid(pdmsSphere.Radius);

            var axis2Placement2D = EntityCreator.CreateAxis2Placement2D(new XbimPoint3D(0.0, 0.0, double.MinValue));

            var circle = EntityCreator.CreateCircle(pdmsSphere.Radius, axis2Placement2D);

            var trimmedCurve = EntityCreator.CreateTrimmedCurve(circle, IfcTrimmingPreference.CARTESIAN,
                EntityCreator.CreateCartesianPoint(new XbimPoint3D(pdmsSphere.Radius, 0.0, double.MinValue)),
                EntityCreator.CreateCartesianPoint(new XbimPoint3D(-pdmsSphere.Radius, 0.0, double.MinValue)), false);

            var polyLine =
                EntityCreator.CreatePolyLine(new List<IfcCartesianPoint>
                {
                    EntityCreator.CreateCartesianPoint(new XbimPoint3D(-pdmsSphere.Radius, 0.0, double.MinValue)),
                    EntityCreator.CreateCartesianPoint(new XbimPoint3D(pdmsSphere.Radius, 0.0, double.MinValue))
                });

            var compositeCurveSegment1 = EntityCreator.CreateCompositeCurveSegment(trimmedCurve, IfcTransitionCode.CONTINUOUS, true);
            var compositeCurveSegment2 = EntityCreator.CreateCompositeCurveSegment(polyLine, IfcTransitionCode.CONTINUOUS, true);
            var compositeCurve =
                EntityCreator.CreateCompositeCurve(
                    new List<IfcCompositeCurveSegment> {compositeCurveSegment1, compositeCurveSegment2}, false);

            var arbitraryClosedProfileDef = EntityCreator.CreateArbitraryClosedProfileDef(compositeCurve, IfcProfileTypeEnum.AREA);
            
            
            var axis2Placement3D = EntityCreator.CreateAxis2Placement3D(new XbimPoint3D(0.0, 0.0, 0.0),
                new XbimVector3D(0.0, 1.0, 0.0), new XbimVector3D(0.0, 0.0, 1.0), pdmsSphere.Matrix);

            return CreateRevolvedAreaSolid(axis2Placement3D, arbitraryClosedProfileDef, 2.0 * Math.PI);
        }

        private static IfcRepresentationItem CreateRepresentationRevolution(PdmsRevolution pdmsRevolution)
        {
            if (pdmsRevolution.Profile.Count < 3) return null;
            if (pdmsRevolution.Angle == 0.0)
                pdmsRevolution.Angle = Math.PI;

            var pointsProfileDef = CreatePointsProfileDef(pdmsRevolution.Profile);

            var axis2Placement3D =
                EntityCreator.CreateAxis2Placement3D(
                    new XbimPoint3D(0.0, 0.0, 0.0),
                    new XbimVector3D(0.0, 0.0, 1.0), new XbimVector3D(1.0, 0.0, 0.0), pdmsRevolution.Matrix);

            return CreateRevolvedAreaSolid(axis2Placement3D, pointsProfileDef, pdmsRevolution.Angle);
        }

        private static IfcRepresentationItem CreateRepresentationExtrusion(PdmsExtrusion pdmsExtrusion)
        {
            if (pdmsExtrusion.Height <= 0.0 || pdmsExtrusion.Profile.Count < 3) return null;

            var pointsProfileDef = CreatePointsProfileDef(pdmsExtrusion.Profile);

            var axis2Placement3D =
                EntityCreator.CreateAxis2Placement3D(
                    new XbimPoint3D(0.0, 0.0, 0.0),
                    new XbimVector3D(0.0, 0.0, 1.0), new XbimVector3D(1.0, 0.0, 0.0), pdmsExtrusion.Matrix);

            return CreateExtrudedAreaSolid(axis2Placement3D, pointsProfileDef,new XbimVector3D(0, 0, 1), pdmsExtrusion.Height);
        }

        private static IfcRepresentationItem CreateRepresentationSlcy(PdmsSlcy pdmsSlcy)
        {
            if (pdmsSlcy.Radius <= 0.0 || pdmsSlcy.Height <= 0.0) return null;
            var circleProfileDef = EntityCreator.CreateCircleProfileDef(pdmsSlcy.Radius);

            var axis2Placement3D = EntityCreator.CreateAxis2Placement3D(new XbimPoint3D(0.0, 0.0, -pdmsSlcy.Height),
                new XbimVector3D(0.0, 0.0, 1.0), new XbimVector3D(1.0, 0.0, 0.0), pdmsSlcy.Matrix);

            var extrudedAreaSolid = CreateExtrudedAreaSolid(axis2Placement3D, circleProfileDef,
                new XbimVector3D(0, 0, 1), pdmsSlcy.Height * 2);

            if (pdmsSlcy.Angle1 == 0.0 && pdmsSlcy.Angle2 == 0.0 && pdmsSlcy.Angle3 == 0.0 && pdmsSlcy.Angle4 == 0.0)
            {
                return extrudedAreaSolid;
            }

            var mbot = new XbimMatrix3D();
            mbot.RotateAroundYAxis(-pdmsSlcy.Angle1);
            mbot.RotateAroundXAxis(pdmsSlcy.Angle2);

            var mtop = new XbimMatrix3D();
            mtop.RotateAroundYAxis(-pdmsSlcy.Angle3);
            mtop.RotateAroundXAxis(pdmsSlcy.Angle4);

            var axisPlaneBottom= EntityCreator.CreateAxis2Placement3D(new XbimPoint3D(0, 0, -pdmsSlcy.Height / 2), mbot.Forward,
                mbot.Right, pdmsSlcy.Matrix );
            var axisPlaneTop = EntityCreator.CreateAxis2Placement3D(new XbimPoint3D(0, 0, pdmsSlcy.Height/2), mtop.Backward,
                mtop.Right,  pdmsSlcy.Matrix );

            var planeBottom = EntityCreator.CreatePlane(axisPlaneBottom);
            var halfSpaceSolidBottom = EntityCreator.CreateHalfSpaceSolid(planeBottom,false);
            var booleanClippingResult1 = EntityCreator.CreateBooleanClippingResult(extrudedAreaSolid, halfSpaceSolidBottom, IfcBooleanOperator.DIFFERENCE);

            var planeTop = EntityCreator.CreatePlane(axisPlaneTop);
            var halfSpaceSolidTop = EntityCreator.CreateHalfSpaceSolid(planeTop, false);
            var booleanClippingResult = EntityCreator.CreateBooleanClippingResult(booleanClippingResult1, halfSpaceSolidTop, IfcBooleanOperator.DIFFERENCE);

            return booleanClippingResult;
        }

        private static IfcRepresentationItem CreateRepresentationFacetGroup(PdmsFacetGroup pdmsFacetGroup)
        {
            var faces = new List<List<XbimPoint3D>>();
            foreach (var face in pdmsFacetGroup.Faces)
            {
                var f = new List<XbimPoint3D>();
                foreach (var point in face)
                {
                    f.Add(pdmsFacetGroup.Matrix.Transform(point));
                }
                faces.Add(f);
            }
            return CreateFacetedBrep(faces);
        }

        private static IfcFacetedBrep CreateFacetedBrep(List<List<XbimPoint3D>> faces)
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
                    if (XbimPoint3D.Subtract(cp, np).Length < 0.5)continue;
                    //if (cp.Equals(np)) continue;
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

        private static IfcArbitraryClosedProfileDef CreatePointsProfileDef(List<double[]> profilePoints)
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
                //if (rounds[i] < 0.0)
                //    alfa = -alfa;
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

            //points = FixSegmentIntersection(points);

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

            //using (var sw = new StreamWriter(@"E:\AVEVA\temp\ifc\2018_04_03\mac.pmlmac"))
            //{
            //    foreach (var c in curveObjects)
            //    {
            //        if (c is XbimPoint3D)
            //        {
            //            var p = (XbimPoint3D)c;
            //            sw.WriteLine("new cyli diam {0} height {1} pos e {2} n {3} u {4}", 0.5, 1, Math.Round(p.X, 4), Math.Round(p.Y), 0.0);
            //            continue;
            //        }
            //        var pps = (XbimPoint3D[])c;
            //        var p1 = pps[0];
            //        var p2 = pps[1];
            //        sw.WriteLine("new cyli diam {0} height {1} pos e {2} n {3} u {4}", 0.5, 1, Math.Round(p1.X), Math.Round(p1.Y), 0.0);
            //        sw.WriteLine("new cyli diam {0} height {1} pos e {2} n {3} u {4}", 0.5, 1, Math.Round(p2.X), Math.Round(p2.Y), 0.0);
            //    }
            //    sw.Close();
            //}

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

        private static IfcArbitraryClosedProfileDef CreateEllipticalDishProfileDef(double radius1, double radius2)
        {
            radius1 = radius1.Round();
            radius2 = radius2.Round();
          
            var axis2Placement2D = EntityCreator.CreateAxis2Placement2D(new XbimPoint3D(0.0, 0.0, double.MinValue));

            var ellipse = EntityCreator.CreateEllipse(axis2Placement2D, radius2, radius1);

            var trimmedCurve = EntityCreator.CreateTrimmedCurve(ellipse, IfcTrimmingPreference.CARTESIAN,
                EntityCreator.CreateCartesianPoint(new XbimPoint3D(radius2, 0.0, double.MinValue)),
                EntityCreator.CreateCartesianPoint(new XbimPoint3D(0.0, radius1, double.MinValue)), true);

            var polyLine =
                EntityCreator.CreatePolyLine(new List<IfcCartesianPoint>
                {
                    EntityCreator.CreateCartesianPoint(new XbimPoint3D(0.0, radius1, double.MinValue)),
                    EntityCreator.CreateCartesianPoint(new XbimPoint3D(0.0, 0.0, double.MinValue)),
                    EntityCreator.CreateCartesianPoint(new XbimPoint3D(radius2, 0.0, double.MinValue))
                });

            var compositeCurveSegment1 = EntityCreator.CreateCompositeCurveSegment(trimmedCurve, IfcTransitionCode.CONTINUOUS, true);
            var compositeCurveSegment2 = EntityCreator.CreateCompositeCurveSegment(polyLine, IfcTransitionCode.CONTINUOUS, true);
            var compositeCurve =
                EntityCreator.CreateCompositeCurve(
                    new List<IfcCompositeCurveSegment> {compositeCurveSegment1, compositeCurveSegment2}, false);
            
            var arbitraryClosedProfileDef = EntityCreator.CreateArbitraryClosedProfileDef(compositeCurve, IfcProfileTypeEnum.AREA);
            
            return arbitraryClosedProfileDef;
        }

        private static IfcExtrudedAreaSolid CreateExtrudedAreaSolid(IfcAxis2Placement3D axis2Placement3D,
            IfcProfileDef profileDef, XbimVector3D extrudeVector, double depth)
        {
            extrudeVector = extrudeVector.Round();
            depth = depth < 1.0 ? 1.0 : depth.Round();
            var extrudedAreaSolid = EntityCreator.CreateExtrudedAreaSolid(profileDef, axis2Placement3D, EntityCreator.CreateDirection(extrudeVector), depth);
            return extrudedAreaSolid;
        }

        private static IfcRevolvedAreaSolid CreateRevolvedAreaSolid(IfcAxis2Placement3D axis2Placement3D,
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

//private static IfcSphere CreateSphereSolid(double radius, double offsetZ = 0.0)
//{
//    var key = new[] { radius, offsetZ };
//    if (_sphereSolidDictionary.ContainsKey(key)) return _sphereSolidDictionary[key];
//    var axis2Placement3D = EntityCreator.CreateAxis2Placement3D(new XbimPoint3D(0.0, 0.0, offsetZ),
//        new XbimVector3D(0.0, 0.0, 1.0), new XbimVector3D(1.0, 0.0, 0.0), XbimMatrix3D.Identity);

//    var sphere = ModelStore.Instances.New<IfcSphere>();
//    sphere.Radius = radius;
//    sphere.Position = axis2Placement3D;

//    _sphereSolidDictionary.Add(key, sphere);
//    return sphere;
//}

//private static List<XbimPoint3D> FixSegmentIntersection(List<XbimPoint3D> points)
// {
//     var segments = new List<HelixToolkit.Wpf.LineSegment>();

//     for (var i = 0; i < points.Count; i++)
//     {
//         var k = i == points.Count - 1 ? 0 : i + 1;
//         var line = new HelixToolkit.Wpf.LineSegment(new System.Windows.Point(points[i].X, points[i].Y),
//             new System.Windows.Point(points[k].X, points[k].Y));
//         if (segments.Count == 0)
//         {
//             segments.Add(line);
//             continue;
//         }
//         for (var j = 0; j < segments.Count; j++)
//         {
//             var l2 = segments[j];
//             if (l2.IntersectsWith(line) && !l2.P2.Equals(line.P1))
//             {
//                 Console.WriteLine();
//             }
//             else
//             {
//                 segments.Add(line);
//             }
//         }
//     }
//     return new List<XbimPoint3D>();
// }