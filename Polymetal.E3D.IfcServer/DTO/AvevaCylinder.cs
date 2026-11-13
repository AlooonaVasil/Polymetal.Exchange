using Xbim.Common.Geometry;
using Xbim.Ifc2x3.GeometryResource;

namespace Polymetal.E3D.IfcServer.DTO
{
    internal class AvevaCylinder : AvevaGeometry
    {
        internal double Radius;
        internal double Height;

        internal AvevaCylinder(string[] data)
        {
            Radius = double.Parse(data[15]);
            Height = double.Parse(data[16]);
            var str = string.Format("{0}-{1}-{2}", GetType().Name, Radius, Height);
            Hash = str.GetHashCode();
        }

        internal IfcRepresentationItem GetShapeRepresentation()
        {
            if (Radius <= 0.0 || Height <= 0.0)
                return null;

            var circleProfileDef = EntityCreator.CreateCircleProfileDef(Radius);

            var axis2Placement3D = EntityCreator.CreateAxis2Placement3D(new XbimPoint3D(0.0, 0.0, -Height / 2.0),
                new XbimVector3D(0.0, 0.0, 1.0), new XbimVector3D(1.0, 0.0, 0.0), Matrix);

            return Representation.CreateExtrudedAreaSolid(axis2Placement3D, circleProfileDef, new XbimVector3D(0, 0, 1),
                Height);
        }
    }
}