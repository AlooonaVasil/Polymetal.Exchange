using Xbim.Common.Geometry;
using Xbim.Ifc2x3.GeometryResource;

namespace Polymetal.E3D.IfcServer.DTO
{
    internal class AvevaCircTorus : AvevaGeometry
    {
        internal double Rcentre;
        internal double Radius;
        internal double Angle;

        internal AvevaCircTorus(string[] data)
        {
            Rcentre = double.Parse(data[15]);
            Radius = double.Parse(data[16]);
            Angle = double.Parse(data[17]);
            var str = string.Format("{0}-{1}-{2}-{3}", GetType().Name, Rcentre, Radius, Angle);
            Hash = str.GetHashCode();
        }

        internal IfcRepresentationItem GetShapeRepresentation()
        {
            if (Angle <= 0.0175 || Radius <= 0.0 || Rcentre <= 0.0)
                return null;

            if (Radius == Rcentre)
                Rcentre += 0.01;

            var circleProfileDef = EntityCreator.CreateCircleProfileDef(Radius, Rcentre);

            var axis2Placement3D = EntityCreator.CreateAxis2Placement3D(new XbimPoint3D(0.0, 0.0, 0.0),
                new XbimVector3D(0.0, 1.0, 0.0), new XbimVector3D(0.0, 0.0, 1.0), Matrix);

            return Representation.CreateRevolvedAreaSolid(axis2Placement3D, circleProfileDef, Angle);
        }
    }
}