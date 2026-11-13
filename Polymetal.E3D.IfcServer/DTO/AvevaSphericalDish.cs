using System;
using System.Collections.Generic;
using Xbim.Common.Geometry;
using Xbim.Ifc2x3.GeometryResource;
using Xbim.Ifc2x3.ProfileResource;

namespace Polymetal.E3D.IfcServer.DTO
{
    internal class AvevaSphericalDish : AvevaGeometry
    {
        internal double Radius;
        internal double Height;

        internal AvevaSphericalDish(string[] data)
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
                Height = 1;
                var circleProfileDef = EntityCreator.CreateCircleProfileDef(Radius);

                axis2Placement3D = EntityCreator.CreateAxis2Placement3D(new XbimPoint3D(0.0, 0.0, -Height / 2.0),
                   new XbimVector3D(0.0, 0.0, 1.0), new XbimVector3D(1.0, 0.0, 0.0), Matrix);

                return Representation.CreateExtrudedAreaSolid(axis2Placement3D, circleProfileDef,
                    new XbimVector3D(0, 0, 1), Height);
            }

            var r = Radius;
            var H = Height;

            var sphereRadius = Math.Sqrt(r * r + Math.Pow((H * H - r * r) / (2 * H), 2.0));

            var axis2Placement2D = EntityCreator.CreateAxis2Placement2D(new XbimPoint3D(0.0, 0.0, double.MinValue));

            var circle = EntityCreator.CreateCircle(sphereRadius, axis2Placement2D);

            var trimmedCurve = EntityCreator.CreateTrimmedCurve(circle, IfcTrimmingPreference.CARTESIAN,
                EntityCreator.CreateCartesianPoint(new XbimPoint3D(sphereRadius - H, Radius, double.MinValue)),
                EntityCreator.CreateCartesianPoint(new XbimPoint3D(sphereRadius, 0.0, double.MinValue)), false);

            var polyLine = EntityCreator.CreatePolyLine(new List<IfcCartesianPoint>
            {
                EntityCreator.CreateCartesianPoint(new XbimPoint3D(sphereRadius, 0.0, double.MinValue)),
                EntityCreator.CreateCartesianPoint(new XbimPoint3D(sphereRadius - H, 0.0, double.MinValue)),
                EntityCreator.CreateCartesianPoint(new XbimPoint3D(sphereRadius - H, Radius, double.MinValue))
            });

            var compositeCurveSegment1 = EntityCreator.CreateCompositeCurveSegment(trimmedCurve, IfcTransitionCode.CONTINUOUS, true);
            var compositeCurveSegment2 = EntityCreator.CreateCompositeCurveSegment(polyLine, IfcTransitionCode.CONTINUOUS, true);
            var compositeCurve = EntityCreator.CreateCompositeCurve(new List<IfcCompositeCurveSegment> { compositeCurveSegment1, compositeCurveSegment2 }, false);
            var arbitraryClosedProfileDef = EntityCreator.CreateArbitraryClosedProfileDef(compositeCurve, IfcProfileTypeEnum.AREA);

            axis2Placement3D = EntityCreator.CreateAxis2Placement3D(new XbimPoint3D(0.0, 0.0, H - sphereRadius),
                new XbimVector3D(0.0, 1.0, 0.0), new XbimVector3D(0.0, 0.0, 1.0), Matrix);

            return Representation.CreateRevolvedAreaSolid(axis2Placement3D, arbitraryClosedProfileDef, 2.0 * Math.PI);
        }
    }
}
