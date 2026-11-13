using System;
using System.Collections.Generic;
using System.Text;
using Aveva.Core.Database;

namespace Polymetal.E3D.Ifc.DTO
{
    internal class AvevaRevolution : AvevaGeometry
    {
        internal double Angle;
        internal List<double[]> Profile;

        internal AvevaRevolution(DbElement item)
        {
            Angle = (Math.PI / 180) * item.GetDouble(DbAttributeInstance.ANGL);
            Profile = new List<double[]>();
            foreach (var loop in item.Members(DbElementTypeInstance.LOOP))
            {
                foreach (var vertex in loop.Members(DbElementTypeInstance.VERTEX))
                {
                    var pos = vertex.GetPosition(DbAttributeInstance.POS);
                    Profile.Add(new[] { Math.Round(pos.X, 3), Math.Round(pos.Y, 3), Math.Round(pos.Z, 3), vertex.GetDouble(DbAttributeInstance.FRAD) });
                }
            }

            Matrix = GetMatrix(item);
            Hash = GetHash();
        }

        internal AvevaRevolution(List<double[]> profile, double angle, double[] matrix, List<AvevaGeometry> negatives)
        {
            Angle = angle;
            Profile = profile;
            Matrix = matrix;
            Negatives = negatives;
            Hash = GetHash();
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append($"{GetType().Name} ");
            for (var i = 0; i < Matrix.Length; i++)
                sb.Append($"{Matrix[i]:0.0000} ");
            sb.Append($"{Color} {Translucency} {Angle} ");
            foreach (var v in Profile)
            {
                sb.Append($"{v[0]:0.####} {v[1]:0.####} {v[2]:0.####} {v[3]:0.####} ");
            }
            return sb.ToString();
        }

        private int GetHash()
        {
            var sb = new StringBuilder();
            sb.Append(GetType().Name);
            foreach (var t in Matrix)
                sb.Append(" " + t);
            sb.Append($" 0 {Angle}");
            foreach (var v in Profile)
            {
                sb.Append($" {v[0]} {v[1]} {v[2]} {v[3]}");
            }
            return sb.ToString().GetHashCode();
        }
    }
}
