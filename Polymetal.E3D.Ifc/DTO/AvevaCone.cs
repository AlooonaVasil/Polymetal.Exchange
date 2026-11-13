using System.Text;
using Aveva.Core.Database;
using Aveva.Core3D.CSG;

namespace Polymetal.E3D.Ifc.DTO
{
    internal class AvevaCone : AvevaGeometry
    {
        internal double Rbottom;
        internal double Rtop;
        internal double Height;

        internal AvevaCone(DbElement item)
        {
            Rbottom = item.GetDouble(DbAttributeInstance.DBOT) / 2.0;
            Rtop = item.GetDouble(DbAttributeInstance.DTOP) / 2.0;
            Height = item.GetDouble(DbAttributeInstance.HEIG);
            Matrix = GetMatrix(item);
            Hash = GetHash();
        }

        internal AvevaCone(double rbottom, double rtop, double height, double[] matrix)
        {
            Rbottom = rbottom;
            Rtop = rtop;
            Height = height;
            Matrix = matrix;
            Hash = GetHash();
        }

        internal AvevaCone(CSGSnout csg)
        {
            Rbottom = csg.BottomRadius;
            Rtop = csg.TopRadius;
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
            sb.Append($"{Color} {Translucency} {Rbottom} {Rtop} {Height}");
            return sb.ToString();
        }

        private int GetHash()
        {
            var sb = new StringBuilder();
            sb.Append(GetType().Name);
            foreach (var t in Matrix)
                sb.Append(" " + t);
            sb.Append($" 0 {Rbottom} {Rtop} {Height}");
            return sb.ToString().GetHashCode();
        }
    }
}
