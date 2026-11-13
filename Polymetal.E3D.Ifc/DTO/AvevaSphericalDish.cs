using System.Text;
using Aveva.Core.Database;
using Aveva.Core.Geometry;
using Aveva.Core3D.CSG;

namespace Polymetal.E3D.Ifc.DTO
{
    internal class AvevaSphericalDish : AvevaGeometry
    {
        internal double Radius;
        internal double Height;

        internal AvevaSphericalDish(DbElement item)
        {
            Radius = item.GetDouble(DbAttributeInstance.DIAM) / 2.0;
            Height = item.GetDouble(DbAttributeInstance.HEIG);
            Matrix = GetMatrix(item);
            Hash = GetHash();
        }

        internal AvevaSphericalDish(CSGDish csg)
        {
            Radius = csg.Radius;
            Height = csg.Height;
            Translucency = csg.Translucency;
            Matrix = csg.Transform.AsArray12();
            Hash = GetHash();
        }

        internal AvevaSphericalDish(DbElement root, DbElement gmItem)
        {
            Radius = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PDIA)) / 2.0;
            Height = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PHEI));

            var pdis = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PDIS));

            Direction directionZ;
            Position positionZ;
            EvaluatePointDirPos(root, gmItem.GetAsString(DbAttributeInstance.PAXI), out directionZ, out positionZ);

            var position = Position.Create();
            position.MoveBy(directionZ, pdis);

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
