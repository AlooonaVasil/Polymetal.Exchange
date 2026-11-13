using System;
using System.Collections.Generic;
using Xbim.Common.Geometry;
using Xbim.Ifc2x3.GeometryResource;

namespace Polymetal.E3D.IfcServer.DTO
{
    internal class AvevaCone : AvevaGeometry
    {
        internal double Rbottom;
        internal double Rtop;
        internal double Height;

        internal AvevaCone(string[] data)
        {
            Rbottom = double.Parse(data[15]);
            Rtop = double.Parse(data[16]);
            Height = double.Parse(data[17]);
            var str = string.Format("{0}-{1}-{2}-{3}", GetType().Name, Rbottom, Rtop, Height);
            Hash = str.GetHashCode();
        }

        internal IfcRepresentationItem GetShapeRepresentation()
        {
            if (Height < 1.0 || (Rbottom <= 0.0 && Rtop <= 0.0)) 
                return null;

            //todo: проблемы с конусами в Revit (маленькие размеры валят оборудование)
            IfcAxis2Placement3D axis2Placement3D;
            if (Rbottom == Rtop)
            {
                var circleProfileDef = EntityCreator.CreateCircleProfileDef(Rbottom);

                axis2Placement3D = EntityCreator.CreateAxis2Placement3D(new XbimPoint3D(0.0, 0.0, -Height / 2.0),
                    new XbimVector3D(0.0, 0.0, 1.0), new XbimVector3D(1.0, 0.0, 0.0), Matrix);

                return Representation.CreateExtrudedAreaSolid(axis2Placement3D, circleProfileDef, new XbimVector3D(0, 0, 1), Height);
            }

            var points = new List<double[]>
            {
                new[] {-Height/2.0, 0.0, 0.0, 0.0},
                new[] {Height/2.0, 0, 0, 0.0},
                new[] {Height/2.0, Rtop, 0, 0.0},
                new[] {-Height/2.0, Rbottom, 0, 0.0}
            };
            points.Reverse();

            var pointsProfileDef = Representation.CreatePointsProfileDef(points);

            axis2Placement3D =
                EntityCreator.CreateAxis2Placement3D(
                    new XbimPoint3D(0.0, 0.0, 0.0),
                    new XbimVector3D(1.0, 0.0, 0.0), new XbimVector3D(0.0, 0.0, 1.0), Matrix);

            return Representation.CreateRevolvedAreaSolid(axis2Placement3D, pointsProfileDef, 2.0 * Math.PI);
        }
    }
}
