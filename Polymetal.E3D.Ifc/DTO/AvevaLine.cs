using System.Text;
using Aveva.Core.Database;

namespace Polymetal.E3D.Ifc.DTO
{
    internal class AvevaLine : AvevaGeometry
    {
        internal double Length;

        internal AvevaLine(DbElement item)
        {
            Length = item.GetDouble(DbAttributeInstance.ZLEN);
            Matrix = GetMatrix(item);
            Hash = GetHash();
        }

        internal AvevaLine(double length, double[] matrix)
        {
            Length = length;
            Matrix = matrix;
            Hash = GetHash();
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append($"{GetType().Name} ");
            for (var i = 0; i < Matrix.Length; i++)
                sb.Append($"{Matrix[i]:0.0000} ");
            sb.Append($"{Color} {Translucency} {Length}");
            return sb.ToString();
        }

        private int GetHash()
        {
            var sb = new StringBuilder();
            sb.Append(GetType().Name);
            foreach (var t in Matrix)
                sb.Append(" " + t);
            sb.Append($" 0 {Length}");
            return sb.ToString().GetHashCode();
        }
    }
}
