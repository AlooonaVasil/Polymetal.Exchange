using Xbim.Common.Geometry;
using Xbim.Ifc2x3.GeometricModelResource;
using Xbim.Ifc2x3.GeometryResource;

namespace Polymetal.E3D.IfcServer.DTO
{
    internal class AvevaSlcy : AvevaGeometry
    {
        internal double Radius;
        internal double Height;
        internal double Angle1;
        internal double Angle2;
        internal double Angle3;
        internal double Angle4;

        internal AvevaSlcy(string[] data)
        {
            Radius = double.Parse(data[15]);
            Height = double.Parse(data[16]);

            Angle1 = double.Parse(data[17]);
            Angle2 = double.Parse(data[18]);
            Angle3 = double.Parse(data[19]);
            Angle4 = double.Parse(data[20]);
            var str = string.Format("{0}-{1}-{2}-{3}-{4}-{5}-{6}", GetType().Name, Radius, Height, Angle1, Angle2,
                Angle3, Angle4);
            Hash = str.GetHashCode();
        }

        internal IfcRepresentationItem GetShapeRepresentation()
        {
            if (Radius <= 0.0 || Height <= 0.0)
                return null;

            var circleProfileDef = EntityCreator.CreateCircleProfileDef(Radius);

            var axis2Placement3D = EntityCreator.CreateAxis2Placement3D(new XbimPoint3D(0.0, 0.0, -Height),
                new XbimVector3D(0.0, 0.0, 1.0), new XbimVector3D(1.0, 0.0, 0.0), Matrix);

            var extrudedAreaSolid = Representation.CreateExtrudedAreaSolid(axis2Placement3D, circleProfileDef,
                new XbimVector3D(0, 0, 1), Height * 2);

            if (Angle1 == 0.0 && Angle2 == 0.0 && Angle3 == 0.0 && Angle4 == 0.0)
            {
                return extrudedAreaSolid;
            }

            var mbot = new XbimMatrix3D();
            mbot.RotateAroundYAxis(-Angle1);
            mbot.RotateAroundXAxis(Angle2);

            var mtop = new XbimMatrix3D();
            mtop.RotateAroundYAxis(-Angle3);
            mtop.RotateAroundXAxis(Angle4);

            var axisPlaneBottom = EntityCreator.CreateAxis2Placement3D(new XbimPoint3D(0, 0, -Height / 2), mbot.Forward,
                mbot.Right, Matrix);
            var axisPlaneTop = EntityCreator.CreateAxis2Placement3D(new XbimPoint3D(0, 0, Height / 2), mtop.Backward,
                mtop.Right, Matrix);

            var planeBottom = EntityCreator.CreatePlane(axisPlaneBottom);
            var halfSpaceSolidBottom = EntityCreator.CreateHalfSpaceSolid(planeBottom, false);
            var booleanClippingResult1 = EntityCreator.CreateBooleanClippingResult(extrudedAreaSolid,
                halfSpaceSolidBottom, IfcBooleanOperator.DIFFERENCE);

            var planeTop = EntityCreator.CreatePlane(axisPlaneTop);
            var halfSpaceSolidTop = EntityCreator.CreateHalfSpaceSolid(planeTop, false);
            var booleanClippingResult = EntityCreator.CreateBooleanClippingResult(booleanClippingResult1,
                halfSpaceSolidTop, IfcBooleanOperator.DIFFERENCE);

            return booleanClippingResult;
        }
    }
}