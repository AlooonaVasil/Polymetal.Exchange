using System;
using System.Text;
using Aveva.Core.Database;
using Aveva.Core.Geometry;
using Aveva.Core.Maths.Geometry;
using Aveva.Core3D.CSG;

namespace Polymetal.E3D.Ifc.DTO
{
    internal class AvevaSlcy : AvevaGeometry
    {
        internal double Radius;
        internal double Height;
        internal double Angle1;
        internal double Angle2;
        internal double Angle3;
        internal double Angle4;

        internal AvevaSlcy(DbElement item)
        {
            Radius = item.GetDouble(DbAttributeInstance.DIAM) / 2.0;
            Height = item.GetDouble(DbAttributeInstance.HEIG);

            Angle1 = (Math.PI / 180) * item.GetDouble(DbAttributeInstance.XBSH);
            Angle2 = (Math.PI / 180) * item.GetDouble(DbAttributeInstance.YBSH);
            Angle3 = (Math.PI / 180) * item.GetDouble(DbAttributeInstance.XTSH);
            Angle4 = (Math.PI / 180) * item.GetDouble(DbAttributeInstance.YTSH);
            Matrix = GetMatrix(item);
            Hash = GetHash();
        }

        internal AvevaSlcy(CSGSnout csg)
        {
            Radius = csg.BottomRadius;
            Height = csg.Height;

            Angle1 = csg.BottomXShear;
            Angle2 = csg.BottomYShear;
            Angle3 = csg.TopXShear;
            Angle4 = csg.TopYShear;
            Matrix = csg.Transform.AsArray12();
            Translucency = csg.Translucency;
            Hash = GetHash();
        }

        internal AvevaSlcy(DbElement root, DbElement gmItem)
        {
            Height = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PHEI));
            var pdist = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PDIS));
            Radius = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PDIA)) / 2.0;

            Angle1 = (Math.PI / 180) * EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PXBS));
            Angle2 = (Math.PI / 180) * EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PYBS));
            Angle3 = (Math.PI / 180) * EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PXTS));
            Angle4 = (Math.PI / 180) * EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PYTS));

            Direction directionZ;
            Position positionZ;
            EvaluatePointDirPos(root, gmItem.GetAsString(DbAttributeInstance.PAXI), out directionZ, out positionZ);

            var position = positionZ;
            position.MoveBy(directionZ, pdist + Height / 2.0);

            var zAngle = D3Vector.Create(directionZ.East, directionZ.North, directionZ.Up).Angle(D3Vector.D3UP);
            if (zAngle > 0)
            {
                var a = Angle1;
                Angle1 = zAngle >= Math.PI / 2 ? -Angle2 : Angle2;
                Angle2 = zAngle >= Math.PI / 2 ? -a : a;
                a = Angle3;
                Angle3 = zAngle >= Math.PI / 2 ? -Angle4 : Angle4;
                Angle4 = zAngle >= Math.PI / 2 ? -a : a;
            }

            var orientation = Orientation.Create("Z IS " + directionZ);
            Matrix = GetMatrix(root, orientation, position);

            Hash = GetHash();
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append($"{GetType().Name} ");
            for (var i = 0; i < Matrix.Length; i++)
                sb.Append($"{Matrix[i]:0.0000} ");
            sb.Append($"{Color} {Translucency} {Radius} {Height} {Angle1} {Angle2} {Angle3} {Angle4}");
            return sb.ToString();
        }

        private int GetHash()
        {
            var sb = new StringBuilder();
            sb.Append(GetType().Name);
            foreach (var t in Matrix)
                sb.Append(" " + t);
            sb.Append($" 0 {Radius} {Height} {Angle1} {Angle2} {Angle3} {Angle4}");
            return sb.ToString().GetHashCode();
        }
    }
}