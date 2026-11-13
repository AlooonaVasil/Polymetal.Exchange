using System;
using System.Collections.Generic;
using Xbim.Common.Geometry;
using Xbim.Ifc2x3.GeometryResource;
using Xbim.Ifc2x3.ProfileResource;

namespace Polymetal.E3D.IfcServer.DTO
{
    internal class AvevaEllipticalDish : AvevaGeometry
    {
        internal double Radius;
        internal double Height;

        internal AvevaEllipticalDish(string[] data)
        {
            Radius = double.Parse(data[15]);
            Height = double.Parse(data[16]);
            var str = string.Format("{0}-{1}-{2}", GetType().Name, Radius, Height);
            Hash = str.GetHashCode();
        }

        internal IfcRepresentationItem GetShapeRepresentation()
        {
            if (Radius <= 0.0) 
                return null;

            IfcAxis2Placement3D axis2Placement3D;
            if (Height <= 0.0)
            {
                Height = 1.0;
                var circleProfileDef = EntityCreator.CreateCircleProfileDef(Radius);

                axis2Placement3D = EntityCreator.CreateAxis2Placement3D(new XbimPoint3D(0.0, 0.0, -Height / 2.0),
                    new XbimVector3D(0.0, 0.0, 1.0), new XbimVector3D(1.0, 0.0, 0.0), Matrix);

                return Representation.CreateExtrudedAreaSolid(axis2Placement3D, circleProfileDef, new XbimVector3D(0, 0, 1), Height);
            }

            var ellipseProfileDef = CreateEllipticalDishProfileDef(Radius, Height);

            axis2Placement3D = EntityCreator.CreateAxis2Placement3D(new XbimPoint3D(0.0, 0.0, 0.0),
                new XbimVector3D(0.0, 1.0, 0.0), new XbimVector3D(0.0, 0.0, 1.0), Matrix);

            return Representation.CreateRevolvedAreaSolid(axis2Placement3D, ellipseProfileDef, 2.0 * Math.PI);
        }

        private IfcArbitraryClosedProfileDef CreateEllipticalDishProfileDef(double radius1, double radius2)
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
                    new List<IfcCompositeCurveSegment> { compositeCurveSegment1, compositeCurveSegment2 }, false);

            var arbitraryClosedProfileDef = EntityCreator.CreateArbitraryClosedProfileDef(compositeCurve, IfcProfileTypeEnum.AREA);

            return arbitraryClosedProfileDef;
        }
    }
}