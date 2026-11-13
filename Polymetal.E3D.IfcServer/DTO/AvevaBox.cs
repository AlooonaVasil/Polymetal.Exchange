using Xbim.Common.Geometry;
using Xbim.Ifc2x3.GeometryResource;

namespace Polymetal.E3D.IfcServer.DTO
{
    internal class AvevaBox : AvevaGeometry
    {
        internal double Xlength;
        internal double Ylength;
        internal double Zlength;

        internal AvevaBox(string[] data)
        {
            Xlength = double.Parse(data[15]);
            Ylength = double.Parse(data[16]);
            Zlength = double.Parse(data[17]);
            var str = string.Format("{0}-{1}-{2}-{3}", GetType().Name, Xlength, Ylength, Zlength);
            Hash = str.GetHashCode();
        }

        internal IfcRepresentationItem GetShapeRepresentation()
        {
            if (Xlength < 1.0 || Ylength < 1.0 || Zlength < 1.0)
                return null;

            var rectangleProfileDef = EntityCreator.CreateRectangleProfileDef(Xlength, Ylength);

            var axis2Placement3D = EntityCreator.CreateAxis2Placement3D(new XbimPoint3D(0.0, 0.0, -Zlength / 2.0),
                new XbimVector3D(0.0, 0.0, 1.0), new XbimVector3D(1.0, 0.0, 0.0), Matrix);

            return Representation.CreateExtrudedAreaSolid(axis2Placement3D, rectangleProfileDef, new XbimVector3D(0, 0, 1), Zlength);
        }
    }
}
