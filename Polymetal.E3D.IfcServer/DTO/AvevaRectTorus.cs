using Xbim.Common.Geometry;
using Xbim.Ifc2x3.GeometryResource;

namespace Polymetal.E3D.IfcServer.DTO
{
    internal class AvevaRectTorus : AvevaGeometry
    {
        internal double Rinside;
        internal double Routside;
        internal double Height;
        internal double Angle;

        internal AvevaRectTorus(string[] data)
        {
            Rinside = double.Parse(data[15]);
            Routside = double.Parse(data[16]);
            Height = double.Parse(data[17]);
            Angle = double.Parse(data[18]);
            var str = string.Format("{0}-{1}-{2}-{3}-{4}", GetType().Name, Rinside, Routside, Height, Angle);
            Hash = str.GetHashCode();
        }

        internal IfcRepresentationItem GetShapeRepresentation()
        {
            if (Rinside < 1.0)
                Rinside = 1.0;

            if (Angle <= 0.0 || Height <= 0.0 || Routside <= Rinside)
                return null;

            var rectangleProfileDef =
                EntityCreator.CreateRectangleProfileDef(Height, Routside - Rinside,
                    Rinside + (Routside - Rinside) / 2.0);

            var axis2Placement3D =
                EntityCreator.CreateAxis2Placement3D(
                    new XbimPoint3D(0.0, 0.0, 0.0),
                    new XbimVector3D(0.0, 1.0, 0.0), new XbimVector3D(0.0, 0.0, 1.0), Matrix);

            return Representation.CreateRevolvedAreaSolid(axis2Placement3D, rectangleProfileDef, Angle);
        }
    }
}
