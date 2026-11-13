using System.Collections.Generic;
using Xbim.Common.Geometry;
using Xbim.Ifc2x3.GeometryResource;

namespace Polymetal.E3D.IfcServer.DTO
{
    internal class AvevaLine : AvevaGeometry
    {
        internal double Length;

        internal AvevaLine(string[] data)
        {
            Length = double.Parse(data[15]);
            var str = $"{GetType().Name}-{Length}";
            Hash = str.GetHashCode();
        }

        internal IfcPolyline CreateRepresentation()
        {
            var cartesionPoint1 = EntityCreator.CreateCartesianPoint(Matrix.Transform(new XbimPoint3D()));
            var cartesionPoint2 = EntityCreator.CreateCartesianPoint(Matrix.Transform(new XbimPoint3D(0, 0, Length)));

            return EntityCreator.CreatePolyLine(new List<IfcCartesianPoint> { cartesionPoint1, cartesionPoint2 });
        }
    }
}
