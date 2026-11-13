using System;
using System.Collections.Generic;
using Xbim.Common.Geometry;
using Xbim.Ifc2x3.GeometryResource;

namespace Polymetal.E3D.IfcServer.DTO
{
    internal class AvevaSnout : AvevaGeometry
    {
        internal double Rbottom;
        internal double Rtop;
        internal double Height;
        internal double Xoffset;
        internal double Yoffset;

        internal AvevaSnout(string[] data)
        {
            Rbottom = double.Parse(data[15]);
            Rtop = double.Parse(data[16]);
            Height = double.Parse(data[17]);
            Xoffset = double.Parse(data[18]);
            Yoffset = double.Parse(data[19]);
            var str = string.Format("{0}-{1}-{2}-{3}-{4}-{5}", GetType().Name, Rbottom, Rtop, Height, Xoffset, Yoffset);
            Hash = str.GetHashCode();
        }

        internal IfcRepresentationItem GetShapeRepresentation()
        {
            if (Height <= 0.0 || (Rbottom <= 0.0 && Rtop <= 0.0)) 
                return null;

            if (Xoffset == 0.0 && Yoffset == 0.0)
            {
                IfcAxis2Placement3D axis2Placement3D;
                if (Rbottom == Rtop)
                {
                    if (Rbottom == 0.0) 
                        return null;

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

                var pointsProfileDef = Representation.CreatePointsProfileDef(points);

                axis2Placement3D =
                    EntityCreator.CreateAxis2Placement3D(
                        new XbimPoint3D(0.0, 0.0, 0.0),
                        new XbimVector3D(1.0, 0.0, 0.0), new XbimVector3D(0.0, 0.0, 1.0), Matrix);

                return Representation.CreateRevolvedAreaSolid(axis2Placement3D, pointsProfileDef, 2.0 * Math.PI);
            }

            var mbot = new XbimMatrix3D
            {
                OffsetX = -Xoffset / 2.0,
                OffsetY = -Yoffset / 2.0,
                OffsetZ = -Height / 2.0
            };

            var mtop = new XbimMatrix3D
            {
                OffsetX = Xoffset / 2.0,
                OffsetY = Yoffset / 2.0,
                OffsetZ = Height / 2.0
            };

            var faces = new List<List<XbimPoint3D>>();
            var pointCount = Math.Ceiling(2 * Math.PI / Math.Asin(Representation.ARCTOLERANCE / 100));
            var angle = Math.PI * 2 / pointCount;
            var faceBottom = new List<XbimPoint3D>();
            var faceTop = new List<XbimPoint3D>();

            var pBot = new XbimPoint3D(Rbottom, 0, 0);
            var pTop = new XbimPoint3D(Rtop, 0, 0);

            for (var i = 0; i < pointCount; i++)
            {
                faceBottom.Add(mbot.Transform(pBot));
                faceTop.Add(mtop.Transform(pTop));
                mbot.RotateAroundZAxis(angle);
                mtop.RotateAroundZAxis(angle);
            }

            faces.Add(faceBottom);
            faces.Add(faceTop);

            for (var i = 0; i < pointCount; i++)
            {
                var face = new List<XbimPoint3D>
                {
                    faceBottom[i],
                    faceTop[i],
                    faceTop[i + 1 >= pointCount ? 0 : i + 1],
                    faceBottom[i + 1 >= pointCount ? 0 : i + 1]
                };
                faces.Add(face);
            }

            var trFaces = new List<List<XbimPoint3D>>();
            foreach (var face in faces)
            {
                var f = new List<XbimPoint3D>();
                foreach (var point in face)
                {
                    f.Add(Matrix.Transform(point));
                }
                trFaces.Add(f);
            }

            return Representation.CreateFacetedBrep(trFaces);
        }
    }
}
