using System.Collections.Generic;
using Xbim.Common.Geometry;
using Xbim.Ifc2x3.GeometryResource;

namespace Polymetal.E3D.IfcServer.DTO
{
    internal class AvevaPyramid : AvevaGeometry
    {
        internal double Xbottom;
        internal double Ybottom;
        internal double Xtop;
        internal double Ytop;
        internal double Xoffset;
        internal double Yoffset;
        internal double Height;

        internal AvevaPyramid(string[] data)
        {
            Xbottom = double.Parse(data[15]);
            Ybottom = double.Parse(data[16]);
            Xtop = double.Parse(data[17]);
            Ytop = double.Parse(data[18]);
            Xoffset = double.Parse(data[19]);
            Yoffset = double.Parse(data[20]);
            Height = double.Parse(data[21]);
            var str = string.Format("{0}-{1}-{2}-{3}-{4}-{5}-{6}-{7}", GetType().Name, Xbottom, Ybottom, Xtop, Ytop,
                Xoffset, Yoffset, Height);
            Hash = str.GetHashCode();
       }

        internal IfcRepresentationItem GetShapeRepresentation()
        {
            if (Height <= 0.0 || (Xbottom <= 0.0 && Xtop <= 0.0) || (Ybottom == 0.0 && Ytop == 0.0)) 
                return null;

            var b1 = new XbimPoint3D(-Xoffset / 2.0 - Xbottom / 2.0,
                -Yoffset / 2.0 - Ybottom / 2.0, -Height / 2.0);
            var b2 = new XbimPoint3D(b1.X, b1.Y + Ybottom, b1.Z);
            var b3 = new XbimPoint3D(b1.X + Xbottom, b1.Y + Ybottom, b1.Z);
            var b4 = new XbimPoint3D(b1.X + Xbottom, b1.Y, b1.Z);

            var t1 = new XbimPoint3D(Xoffset / 2.0 - Xtop / 2.0,
                Yoffset / 2.0 - Ytop / 2.0, Height / 2.0);
            var t2 = new XbimPoint3D(t1.X, t1.Y + Ytop, t1.Z);
            var t3 = new XbimPoint3D(t1.X + Xtop, t1.Y + Ytop, t1.Z);
            var t4 = new XbimPoint3D(t1.X + Xtop, t1.Y, t1.Z);


            var m = Matrix;
            b1 = m.Transform(b1);
            b2 = m.Transform(b2);
            b3 = m.Transform(b3);
            b4 = m.Transform(b4);

            t1 = m.Transform(t1);
            t2 = m.Transform(t2);
            t3 = m.Transform(t3);
            t4 = m.Transform(t4);

            var faces = new List<List<XbimPoint3D>>
            {
                new List<XbimPoint3D> { b1, b2, b3, b4},
                new List<XbimPoint3D> {t4, t3, t2, t1},
                new List<XbimPoint3D> {t1, t2, b2, b1},
                new List<XbimPoint3D> {t2, t3, b3, b2},
                new List<XbimPoint3D> {t3, t4, b4, b3},
                new List<XbimPoint3D> {t4, t1, b1, b4}
            };

            return Representation.CreateFacetedBrep(faces);
        }
    }
}
