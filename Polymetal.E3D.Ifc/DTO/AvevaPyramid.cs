using System.Text;
using Aveva.Core.Database;
using Aveva.Core.Geometry;
using Aveva.Core.Utilities.Messaging;
using Aveva.Core3D.CSG;

namespace Polymetal.E3D.Ifc.DTO
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

        internal AvevaPyramid(DbElement item)
        {
            Xbottom = item.GetDouble(DbAttributeInstance.XBOT);
            Ybottom = item.GetDouble(DbAttributeInstance.YBOT);
            Xtop = item.GetDouble(DbAttributeInstance.XTOP);
            Ytop = item.GetDouble(DbAttributeInstance.YTOP);
            Xoffset = item.GetDouble(DbAttributeInstance.XOFF);
            Yoffset = item.GetDouble(DbAttributeInstance.YOFF);
            Height = item.GetDouble(DbAttributeInstance.HEIG);
            Matrix = GetMatrix(item);
            Hash = GetHash();
        }

        internal AvevaPyramid(CSGPyramid csg)
        {
            Xbottom = csg.XBottom;
            Ybottom = csg.YBottom;
            Xtop = csg.XTop;
            Ytop = csg.YTop;
            Xoffset = csg.XOffset;
            Yoffset = csg.YOffset;
            Height = csg.Height;
            Translucency = csg.Translucency;
            Matrix = csg.Transform.AsArray12();
            Hash = GetHash();
        }

        internal AvevaPyramid(double xbottom, double ybottom, double xtop, double ytop, double xoffset, double yoffset, double height, double[] matrix)
        {
            Xbottom = xbottom;
            Ybottom = ybottom;
            Xtop = xtop;
            Ytop = ytop;
            Xoffset = xoffset;
            Yoffset = yoffset;
            Height = height;
            Matrix = matrix;
            Hash = GetHash();
        }

        internal AvevaPyramid(DbElement root, DbElement gmItem)
        {
            Xbottom = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PBBT));
            Ybottom = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PCBT));
            Xtop = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PBTP));
            Ytop = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PCTP));

            Xoffset = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PBOF));
            Yoffset = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PCOF));

            var ptdi = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PTDI));
            var pbdi = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PBDI));

            if (ptdi < pbdi)
            {
                var tp = ptdi;
                ptdi = pbdi;
                pbdi = tp;

                tp = Xbottom;
                Xbottom = Xtop;
                Xtop = tp;

                tp = Ybottom;
                Ybottom = Ytop;
                Ytop = tp;

                Xoffset = -Xoffset;
                Yoffset = -Yoffset;
            }
            Height = ptdi - pbdi;

            EvaluatePointDirPos(root, gmItem.GetAsString(DbAttributeInstance.PBAX), out Direction directionX, out Position position);
            EvaluatePointDirPos(root, gmItem.GetAsString(DbAttributeInstance.PCAX), out Direction directionY, out position);
            EvaluatePointDirPos(root, gmItem.GetAsString(DbAttributeInstance.PAAX), out Direction directionZ, out position);

            if (!Orientation.Parse("X IS " + directionX + " AND Z IS " + directionZ, out Orientation orientation, out PdmsMessage error) || orientation.ZDir() != directionZ)
            {
                orientation = Orientation.Create("Z IS " + directionZ);
                directionX = orientation.YDir();
                orientation = Orientation.Create("X IS " + directionX + " AND Z IS " + directionZ);
            }

            directionX = orientation.XDir();
            directionY = orientation.YDir();
            directionZ = orientation.ZDir();
            position.MoveBy(directionZ, pbdi + Height / 2.0);
            position.MoveBy(directionX, Xoffset / 2.0);
            position.MoveBy(directionY, Yoffset / 2.0);

            Matrix = GetMatrix(root, orientation, position);

            Hash = GetHash();
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append(GetType().Name + " ");
            for (var i = 0; i < Matrix.Length; i++)
                sb.Append($"{Matrix[i]:0.0000} ");
            sb.Append($"{Color} {Translucency} {Xbottom} {Ybottom} {Xtop} {Ytop} {Xoffset} {Yoffset} {Height}");
            return sb.ToString();
        }

        private int GetHash()
        {
            var sb = new StringBuilder();
            sb.Append(GetType().Name);
            foreach (var t in Matrix)
                sb.Append(" " + t);
            sb.Append($" 0 {Xbottom} {Ybottom} {Xtop} {Ytop} {Xoffset} {Yoffset} {Height}");
            return sb.ToString().GetHashCode();
        }
    }
}
