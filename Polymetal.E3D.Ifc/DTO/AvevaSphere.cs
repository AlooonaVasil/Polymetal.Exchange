using System.Text;
using Aveva.Core.Database;
using Aveva.Core.Geometry;
using Aveva.Core3D.CSG;

namespace Polymetal.E3D.Ifc.DTO
{
    internal class AvevaSphere : AvevaGeometry
    {
        internal double Radius;

        internal AvevaSphere(DbElement item)
        {
            Radius = item.GetDouble(DbAttributeInstance.DIAM) / 2.0;
            Matrix = GetMatrix(item);
            Hash = GetHash();
        }

        internal AvevaSphere(CSGSphere csg)
        {
            Radius = csg.Radius;
            Translucency = csg.Translucency;
            Matrix = csg.Transform.AsArray12();
            Hash = GetHash();
        }

        internal AvevaSphere(DbElement root, DbElement gmItem)
        {
            var pdist = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PDIS));
            Radius = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PDIA)) / 2.0;

            Direction directionZ;
            Position positionZ;
            EvaluatePointDirPos(root, gmItem.GetAsString(DbAttributeInstance.PAXI), out directionZ, out positionZ);

            if (root.ElementType == DbElementTypeInstance.TUBING)
            {
                directionZ = root.GetDirection(DbAttributeInstance.PDIR, new DbQualifier() { wrtQualifier = root });
            }

            var position = positionZ;
            position.MoveBy(directionZ, pdist);

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
            sb.Append($"{Color} {Translucency} {Radius}");
            return sb.ToString();
        }

        private int GetHash()
        {
            var sb = new StringBuilder();
            sb.Append(GetType().Name);
            foreach (var t in Matrix)
                sb.Append(" " + t);
            sb.Append($" 0 {Radius}");
            return sb.ToString().GetHashCode();
        }
    }
}