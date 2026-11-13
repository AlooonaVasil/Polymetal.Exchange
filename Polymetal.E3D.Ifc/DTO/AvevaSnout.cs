using System.Text;
using Aveva.Core.Database;
using Aveva.Core.Geometry;
using Aveva.Core.Utilities.Messaging;
using Aveva.Core3D.CSG;

namespace Polymetal.E3D.Ifc.DTO
{
    internal class AvevaSnout : AvevaGeometry
    {
        internal double Rbottom;
        internal double Rtop;
        internal double Height;
        internal double Xoffset;
        internal double Yoffset;

        internal AvevaSnout(DbElement item)
        {
            Rbottom = item.GetDouble(DbAttributeInstance.DBOT) / 2.0;
            Rtop = item.GetDouble(DbAttributeInstance.DTOP) / 2.0;
            Height = item.GetDouble(DbAttributeInstance.HEIG);
            Xoffset = item.GetDouble(DbAttributeInstance.XOFF);
            Yoffset = item.GetDouble(DbAttributeInstance.YOFF);
            Matrix = GetMatrix(item);
            Hash = GetHash();
        }

        internal AvevaSnout(CSGSnout csg)
        {
            Rbottom = csg.BottomRadius;
            Rtop = csg.TopRadius;
            Height = csg.Height;
            Xoffset = csg.XAxisDisplacement;
            Yoffset = csg.YAxisDisplacement;
            Matrix = csg.Transform.AsArray12();
            Translucency = csg.Translucency;
            Hash = GetHash();
        }

        internal AvevaSnout(DbElement root, DbElement gmItem)
        {
            var ptdi = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PTDI));
            var pbdi = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PBDI));
            Rbottom = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PBDM)) / 2.0;
            Rtop = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PTDM)) / 2.0;
            Xoffset = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.POFF));

            if (ptdi < pbdi)
            {
                var tp = ptdi;
                ptdi = pbdi;
                pbdi = tp;
                tp = Rbottom;
                Rbottom = Rtop;
                Rtop = tp;
                Xoffset = -Xoffset;
            }

            Height = ptdi - pbdi;

            Direction directionX, directionZ;
            Position positionX, positionZ;
            EvaluatePointDirPos(root, gmItem.GetAsString(DbAttributeInstance.PBAX), out directionX, out positionX);
            EvaluatePointDirPos(root, gmItem.GetAsString(DbAttributeInstance.PAAX), out directionZ, out positionZ);

            var position = positionZ;
            position.MoveBy(directionZ, pbdi + Height / 2.0);
            position.MoveBy(directionX, Xoffset / 2.0);

            Orientation orientation;
            if (!Orientation.Parse("X IS " + directionX + " AND Z IS " + directionZ, out orientation,
                    out PdmsMessage error) || orientation.ZDir() != directionZ)
            {
                orientation = Orientation.Create("Z IS " + directionZ);
                directionX = orientation.YDir();
                orientation = Orientation.Create("X IS " + directionX + " AND Z IS " + directionZ);
            }

            Matrix = GetMatrix(root, orientation, position);

            Hash = GetHash();
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append($"{GetType().Name} ");
            for (var i = 0; i < Matrix.Length; i++)
                sb.Append($"{Matrix[i]:0.0000} ");
            sb.Append($"{Color} {Translucency} {Rbottom} {Rtop} {Height} {Xoffset} {Yoffset}");
            return sb.ToString();
        }

        private int GetHash()
        {
            var sb = new StringBuilder();
            sb.Append(GetType().Name);
            foreach (var t in Matrix)
                sb.Append(" " + t);
            sb.Append($" 0 {Rbottom} {Rtop} {Height} {Xoffset} {Yoffset}");
            return sb.ToString().GetHashCode();
        }
    }
}