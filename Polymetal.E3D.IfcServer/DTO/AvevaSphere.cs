using System;
using System.Collections.Generic;
using Xbim.Common.Geometry;
using Xbim.Ifc2x3.GeometryResource;
using Xbim.Ifc2x3.ProfileResource;

namespace Polymetal.E3D.IfcServer.DTO
{
    internal class AvevaSphere : AvevaGeometry
    {
        internal double Radius;

        internal AvevaSphere(string[] data)
        {
            Radius = double.Parse(data[15]);
            var str = string.Format("{0}-{1}", GetType().Name, Radius);
            Hash = str.GetHashCode();
        }

        internal IfcRepresentationItem GetShapeRepresentation()
        {
            if (Radius <= 5.0)
                return null;

            var axis2Placement2D = EntityCreator.CreateAxis2Placement2D(new XbimPoint3D(0.0, 0.0, double.MinValue));

            var circle = EntityCreator.CreateCircle(Radius, axis2Placement2D);

            var trimmedCurve = EntityCreator.CreateTrimmedCurve(circle, IfcTrimmingPreference.CARTESIAN,
                EntityCreator.CreateCartesianPoint(new XbimPoint3D(Radius, 0.0, double.MinValue)),
                EntityCreator.CreateCartesianPoint(new XbimPoint3D(-Radius, 0.0, double.MinValue)), false);

            var polyLine =
                EntityCreator.CreatePolyLine(new List<IfcCartesianPoint>
                {
                    EntityCreator.CreateCartesianPoint(new XbimPoint3D(-Radius, 0.0, double.MinValue)),
                    EntityCreator.CreateCartesianPoint(new XbimPoint3D(Radius, 0.0, double.MinValue))
                });

            var compositeCurveSegment1 =
                EntityCreator.CreateCompositeCurveSegment(trimmedCurve, IfcTransitionCode.CONTINUOUS, true);
            var compositeCurveSegment2 =
                EntityCreator.CreateCompositeCurveSegment(polyLine, IfcTransitionCode.CONTINUOUS, true);
            var compositeCurve =
                EntityCreator.CreateCompositeCurve(
                    new List<IfcCompositeCurveSegment> { compositeCurveSegment1, compositeCurveSegment2 }, false);

            var arbitraryClosedProfileDef =
                EntityCreator.CreateArbitraryClosedProfileDef(compositeCurve, IfcProfileTypeEnum.AREA);


            var axis2Placement3D = EntityCreator.CreateAxis2Placement3D(new XbimPoint3D(0.0, 0.0, 0.0),
                new XbimVector3D(0.0, 1.0, 0.0), new XbimVector3D(0.0, 0.0, 1.0), Matrix);

            return Representation.CreateRevolvedAreaSolid(axis2Placement3D, arbitraryClosedProfileDef, 2.0 * Math.PI);
        }
    }
}