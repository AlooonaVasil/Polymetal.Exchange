using System.Text;
using Aveva.Core.Database;
using Aveva.Core.Geometry;
using Aveva.Core3D.CSG;

namespace Polymetal.E3D.Ifc.DTO
{
    internal class AvevaCylinder : AvevaGeometry
    {
        internal double Radius;
        internal double Height;

        internal AvevaCylinder(DbElement item)
        {
            Radius = item.GetDouble(DbAttributeInstance.DIAM) / 2.0;
            Height = item.GetDouble(DbAttributeInstance.HEIG);
            Matrix = GetMatrix(item);
            Hash = GetHash();
        }

        internal AvevaCylinder(double radius, double height, double[] matrix)
        {
            Radius = radius;
            Height = height;
            Matrix = matrix;
            Hash = GetHash();
        }

        internal AvevaCylinder(DbElement root, DbElement gmItem)
        {
            if (gmItem.ElementType == DbElementTypeInstance.SCYLINDER ||
                gmItem.ElementType == DbElementTypeInstance.NSCYLINDER)
            {
                Height = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PHEI));
                var pdist = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PDIS));
                Radius = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PDIA)) / 2.0;

                if (Height < 0)
                {
                    Height = -Height;
                    pdist = pdist - Height;
                }

                Direction directionZ;
                Position positionZ;
                EvaluatePointDirPos(root, gmItem.GetAsString(DbAttributeInstance.PAXI), out directionZ, out positionZ);

                var position = positionZ;
                position.MoveBy(directionZ, pdist + Height / 2.0);

                var orientation = Orientation.Create("Z IS " + directionZ);
                Matrix = GetMatrix(root, orientation, position);

            }
            else if (gmItem.ElementType == DbElementTypeInstance.LCYLINDER ||
                     gmItem.ElementType == DbElementTypeInstance.NLCYLINDER)
            {
                var ptdi = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PTDI));
                var pbdi = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PBDI));

                if (ptdi < pbdi)
                {
                    var tp = ptdi;
                    ptdi = pbdi;
                    pbdi = tp;
                }

                Height = ptdi - pbdi;

                Radius = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PDIA)) / 2.0;
                Direction directionZ;
                Position positionZ;
                EvaluatePointDirPos(root, gmItem.GetAsString(DbAttributeInstance.PAXI), out directionZ, out positionZ);

                var position = positionZ;
                position.MoveBy(directionZ, pbdi + Height / 2.0);

                var orientation = Orientation.Create("Z IS " + directionZ);
                Matrix = GetMatrix(root, orientation, position);
            }
            else if (gmItem.ElementType == DbElementTypeInstance.TUBE)
            {
                Height = root.GetDouble(DbAttributeInstance.ITLE);
                Radius = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PDIA)) / 2.0;

                var orientation = Orientation.Create("Z IS " + root.EvaluateOrientation(Expr_OriWrtSite).YDir());
                var position = root.EvaluatePosition(Expr_PosWrtSite);
                var xdir = orientation.XDir();
                var ydir = orientation.YDir();
                var zdir = orientation.ZDir();
                Matrix = new[]
                {
                    xdir.East, xdir.North, xdir.Up,
                    ydir.East, ydir.North, ydir.Up,
                    zdir.East, zdir.North, zdir.Up,
                    position.X, position.Y, position.Z
                };
            }

            Hash = GetHash();
        }

        internal AvevaCylinder(CSGCylinder csg)
        {
            Radius = csg.Radius;
            Height = csg.Height;
            Matrix = csg.Transform.AsArray12();
            Translucency = csg.Translucency;
            Hash = GetHash();
        }

        internal AvevaCylinder(CSGSnout csg)
        {
            Radius = csg.BottomRadius;
            Height = csg.Height;
            Matrix = csg.Transform.AsArray12();
            Translucency = csg.Translucency;
            Hash = GetHash();
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append($"{GetType().Name} ");
            for (var i = 0; i < Matrix.Length; i++)
                sb.Append($"{Matrix[i]:0.0000} ");
            sb.Append($"{Color} {Translucency} {Radius} {Height}");
            return sb.ToString();
        }

        private int GetHash()
        {
            var sb = new StringBuilder();
            sb.Append(GetType().Name);
            foreach (var t in Matrix)
                sb.Append(" " + t);
            sb.Append($" 0 {Radius} {Height}");
            return sb.ToString().GetHashCode();
        }
    }
}