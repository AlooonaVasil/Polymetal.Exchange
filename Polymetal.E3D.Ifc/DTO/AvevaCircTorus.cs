using System;
using System.Text;
using Aveva.Core.Database;
using Aveva.Core.Geometry;
using Aveva.Core3D.CSG;

namespace Polymetal.E3D.Ifc.DTO
{
    internal class AvevaCircTorus : AvevaGeometry
    {
        internal double Rcentre;
        internal double Radius;
        internal double Angle;

        internal AvevaCircTorus(DbElement item)
        {
            if (item.ElementType == DbElementTypeInstance.BEND)
            {
                Rcentre = item.GetDouble(DbAttributeInstance.RADI);
                Radius = item.GetDouble(DbAttributeInstance.AOD) / 2.0;
                var directionZ = item.GetDirection(DbAttributeInstance.PDIR, new DbQualifier() { doubleQualifiers = new[] { 1.0 }, wrtQualifier = item });
                var positionZ = item.GetPosition(DbAttributeInstance.PPOS, new DbQualifier() { doubleQualifiers = new[] { 1.0 }, wrtQualifier = item });
                var directionX = item.GetDirection(DbAttributeInstance.PDIR, new DbQualifier() { doubleQualifiers = new[] { 2.0 }, wrtQualifier = item });
                var positionX = item.GetPosition(DbAttributeInstance.PPOS, new DbQualifier() { doubleQualifiers = new[] { 2.0 }, wrtQualifier = item });

                var positionZ2 = Position.Create(positionZ.X, positionZ.Y, positionZ.Z);
                positionZ2.MoveBy(directionZ, 1.0);
                var positionX2 = Position.Create(positionX.X, positionX.Y, positionX.Z);
                positionX2.MoveBy(directionX, 1.0);

                var line1 = Line.Create(positionZ, positionZ2);
                var line2 = Line.Create(positionX, positionX2);
                var position = line1.Intersection(line2);

                var aSide = position.Distance(positionZ);
                var alfa = (Math.PI / 180) * directionZ.Angle(directionX) / 2.0;

                var dist = aSide / Math.Cos(alfa);
                var dir = directionX.Midway(directionZ);
                position.MoveBy(dir, dist);

                var orientation = Orientation.Create("X IS " + Direction.Create(position, positionX) + " AND Y IS " + directionX.Opposite());

                Matrix = GetMatrix(item, orientation, position);
            }
            else
            {
                var rins = item.GetDouble(DbAttributeInstance.RINS);
                var rout = item.GetDouble(DbAttributeInstance.ROUT);

                Radius = (rout - rins) / 2.0;
                Rcentre = rins + Radius;

                Matrix = GetMatrix(item);
            }
            Angle = (Math.PI / 180) * item.GetDouble(DbAttributeInstance.ANGL);
            Hash = GetHash();
        }

        internal AvevaCircTorus(CSGCTorus csg)
        {
            Radius = csg.SectionRadius;
            Rcentre = csg.ArcRadius;
            Angle = csg.Angle;
            Matrix = csg.Transform.AsArray12();
            Translucency = csg.Translucency;
            Hash = GetHash();
        }

        internal AvevaCircTorus(DbElement root, DbElement gmItem)
        {
            Radius = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PDIA)) / 2.0;

            Direction directionX, directionZ;
            Position position, positionX, positionZ;
            EvaluatePointDirPos(root, gmItem.GetAsString(DbAttributeInstance.PBAX), out directionX, out positionX);
            EvaluatePointDirPos(root, gmItem.GetAsString(DbAttributeInstance.PAAX), out directionZ, out positionZ);

            var positionZ2 = Position.Create(positionZ.X, positionZ.Y, positionZ.Z);
            positionZ2.MoveBy(directionZ, 1.0);
            var positionX2 = Position.Create(positionX.X, positionX.Y, positionX.Z);
            positionX2.MoveBy(directionX, 1.0);

            var line1 = Line.Create(positionZ, positionZ2);
            var line2 = Line.Create(positionX, positionX2);
            var pposI = line1.Intersection(line2);
            if (pposI == null)
            {
                position = positionZ.MidPoint(positionX);
                Rcentre = position.Distance(positionZ);
                Angle = Math.PI;
            }
            else
            {
                var aSide = pposI.Distance(positionZ);
                var alfa = (Math.PI / 180) * directionZ.Angle(directionX) / 2.0;
                Rcentre = aSide * Math.Tan(alfa);
                var dist = aSide / Math.Cos(alfa);
                var dir = directionX.Midway(directionZ);
                pposI.MoveBy(dir, dist);
                Angle = (Math.PI / 180) * pposI.Angle(positionZ, positionX);
                position = pposI;
            }

            //var orientation =Orientation.Create("X IS "+Direction.Create(ppos,pposA)+" AND Y IS " +directionA);
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
            sb.Append($"{Color} {Translucency} {Rcentre} {Radius} {Angle}");
            return sb.ToString();
        }

        private int GetHash()
        {
            var sb = new StringBuilder();
            sb.Append(GetType().Name);
            foreach (var t in Matrix)
                sb.Append(" " + t);
            sb.Append($" 0 {Rcentre} {Radius} {Angle}");
            return sb.ToString().GetHashCode();
        }
    }
}
