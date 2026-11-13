using System;
using System.Text;
using Aveva.Core.Database;
using Aveva.Core.Geometry;
using Aveva.Core3D.CSG;

namespace Polymetal.E3D.Ifc.DTO
{
    internal class AvevaRectTorus : AvevaGeometry
    {
        internal double Rinside;
        internal double Routside;
        internal double Height;
        internal double Angle;

        internal AvevaRectTorus(DbElement item)
        {
            Rinside = item.GetDouble(DbAttributeInstance.RINS);
            Routside = item.GetDouble(DbAttributeInstance.ROUT);
            Height = item.GetDouble(DbAttributeInstance.HEIG);
            Angle = (Math.PI / 180) * item.GetDouble(DbAttributeInstance.ANGL);
            Matrix = GetMatrix(item);
            Hash = GetHash();
        }

        internal AvevaRectTorus(CSGRTorus csg)
        {
            Rinside = csg.InsideRadius;
            Routside = csg.OutsideRadius;
            Height = csg.Height;
            Angle = csg.Angle;
            Matrix = csg.Transform.AsArray12();
            Translucency = csg.Translucency;
            Hash = GetHash();
        }

        internal AvevaRectTorus(DbElement root, DbElement gmItem)
        {
            Height = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PHEI));
            var radius = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PDIA)) / 2.0;

            Direction directionX, directionZ;
            Position position, positionX, positionZ;
            EvaluatePointDirPos(root, gmItem.GetAsString(DbAttributeInstance.PBAX), out directionX, out positionX);
            EvaluatePointDirPos(root, gmItem.GetAsString(DbAttributeInstance.PAAX), out directionZ, out positionZ);

            var positionZ2 = Position.Create(positionZ.X, positionZ.Y, positionZ.Z);
            positionZ2.MoveBy(directionZ, 1.0);
            var pposB2 = Position.Create(positionX.X, positionX.Y, positionX.Z);
            pposB2.MoveBy(directionX, 1.0);

            var line1 = Line.Create(positionZ, positionZ2);
            var line2 = Line.Create(positionX, pposB2);
            var pposI = line1.Intersection(line2);

            double rcentre;
            if (pposI == null)
            {
                position = positionZ.MidPoint(positionX);
                rcentre = position.Distance(positionZ);
                Angle = Math.PI;
            }
            else
            {
                var aSide = pposI.Distance(positionZ);
                var alfa = (Math.PI / 180) * directionZ.Angle(directionX) / 2.0;
                rcentre = aSide * Math.Tan(alfa);
                var dist = aSide / Math.Cos(alfa);
                var dir = directionX.Midway(directionZ);
                pposI.MoveBy(dir, dist);
                Angle = (Math.PI / 180) * pposI.Angle(positionZ, positionX);
                position = pposI;
            }

            Rinside = rcentre - radius;
            Routside = rcentre + radius;

            var orientation = Orientation.Create("X IS " + Direction.Create(position, positionX) + " AND Y IS " + directionX.Opposite());
            Matrix = GetMatrix(root, orientation, position);

            Hash = GetHash();
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append($"{GetType().Name} ");
            for (var i = 0; i < Matrix.Length; i++)
                sb.Append($"{Matrix[i]:0.0000} ");
            sb.Append($"{Color} {Translucency} {Rinside} {Routside} {Height} {Angle}");
            return sb.ToString();
        }

        private int GetHash()
        {
            var sb = new StringBuilder();
            sb.Append(GetType().Name);
            foreach (var t in Matrix)
                sb.Append(" " + t);
            sb.Append($" 0 {Rinside} {Routside} {Height} {Angle}");
            return sb.ToString().GetHashCode();
        }
    }
}
