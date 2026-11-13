using System.Collections.Generic;
using System.Linq;
using System.Text;
using Aveva.Core.Database;
using Aveva.Core.Geometry;
using Aveva.Core.Utilities.Messaging;

namespace Polymetal.E3D.Ifc.DTO
{
    internal class AvevaExtrusion : AvevaGeometry
    {
        internal double Height;
        internal List<double[]> Profile;

        internal AvevaExtrusion(DbElement item)
        {
            if (item.ElementType == DbElementTypeInstance.EXTRUSION ||
                item.ElementType == DbElementTypeInstance.NXTRUSION)
            {
                Height = item.GetDouble(DbAttributeInstance.HEIG);

                Profile = new List<double[]>();
                var loops = item.Members(DbElementTypeInstance.LOOP);
                if (loops.Length > 0)
                {
                    var loop = loops.First();
                    foreach (var vertex in loop.Members(DbElementTypeInstance.VERTEX))
                    {
                        var pos = vertex.GetPosition(DbAttributeInstance.POS);
                        Profile.Add(new[] { pos.X, pos.Y, pos.Z, vertex.GetDouble(DbAttributeInstance.FRAD) });
                    }
                }
            }
            else if (item.ElementType == DbElementTypeInstance.FLOOR || item.ElementType == DbElementTypeInstance.GWALL || item.ElementType == DbElementTypeInstance.PANEL)
            {
                Profile = new List<double[]>();
                var ploops = item.Members(DbElementTypeInstance.PLOOP);
                if (ploops.Length > 0)
                {
                    var ploop = ploops.First();
                    Height = ploop.GetDouble(DbAttributeInstance.HEIG);
                    foreach (var pavert in ploop.Members(DbElementTypeInstance.PAVERT))
                    {
                        var pos = pavert.GetPosition(DbAttributeInstance.POS);
                        Profile.Add(new[] { pos.X, pos.Y, pos.Z, pavert.GetDouble(DbAttributeInstance.FRAD) });
                    }
                }
            }

            Matrix = GetMatrix(item);
            Hash = GetHash();
        }

        internal AvevaExtrusion(List<double[]> profile, double height, double[] matrix, List<AvevaGeometry> negatives)
        {
            Height = height;
            Profile = profile;
            Matrix = matrix;
            Negatives = negatives;
            Hash = GetHash();
        }

        internal AvevaExtrusion(DbElement root, DbElement gmItem)
        {
            Profile = new List<double[]>();
            if (gmItem.ElementType == DbElementTypeInstance.SPROFILE)
            {
                var poss = root.GetPosition(DbAttributeInstance.POSS);
                var pose = root.GetPosition(DbAttributeInstance.POSE);
                Height = poss.Distance(pose);

                EvaluatePointDirPos(root, gmItem.GetAsString(DbAttributeInstance.PLAX), out Direction directionY, out Position positionY);
                foreach (var spve in gmItem.Members(DbElementTypeInstance.SPVERT))
                {
                    var spveX = EvaluateDouble(root, spve.GetAsString(DbAttributeInstance.PX));
                    var spveY = EvaluateDouble(root, spve.GetAsString(DbAttributeInstance.PY));
                    var spveRad = EvaluateDouble(root, spve.GetAsString(DbAttributeInstance.PRAD));
                    Profile.Add(new[] { spveX, spveY, 0.0, spveRad });
                }

                var orientation = Orientation.Create("Y IS " + directionY);

                Matrix = GetMatrix(root, orientation, Position.Create());

                var jusl = root.GetAsString(DbAttributeInstance.JUSL).Replace("unset", "NA");
                var positionPlin = root.EvaluatePosition(ExportIfc.GetOrAddExpression("PPLIN " + jusl + " OFFS WRT SITE"));
                Matrix[9] -= positionPlin.X;
                Matrix[10] -= positionPlin.Y;
                Matrix[11] -= positionPlin.Z;
            }
            else
            {
                Height = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PHEI));
                var px = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PX));
                var py = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PY));
                var pz = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PZ));
                EvaluatePointDirPos(root, gmItem.GetAsString(DbAttributeInstance.PAAX), out Direction directionX, out Position position);
                EvaluatePointDirPos(root, gmItem.GetAsString(DbAttributeInstance.PBAX), out Direction directionY, out Position positionY);

                if (!Orientation.Parse("X IS " + directionX + " AND Y IS " + directionY, out Orientation orientation, out PdmsMessage error) ||
                    orientation.XDir() != directionX)
                {
                    orientation = Orientation.Create("X IS " + directionX);
                }

                directionX = orientation.XDir();
                directionY = orientation.YDir();
                var directionZ = orientation.ZDir();

                position.MoveBy(directionX, px);
                position.MoveBy(directionY, py);
                position.MoveBy(directionZ, pz);

                Matrix = GetMatrix(root, orientation, position);

                foreach (var sloo in gmItem.Members(DbElementTypeInstance.SLOOP))
                {
                    foreach (var sver in sloo.Members(DbElementTypeInstance.SVERTEX))
                    {
                        var sverX = EvaluateDouble(root, sver.GetAsString(DbAttributeInstance.PX));
                        var sverY = EvaluateDouble(root, sver.GetAsString(DbAttributeInstance.PY));
                        var sverRad = EvaluateDouble(root, sver.GetAsString(DbAttributeInstance.PRAD));
                        Profile.Add(new[] { sverX, sverY, 0.0, sverRad });
                    }
                }
            }
            Hash = GetHash();
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append($"{GetType().Name} ");
            for (var i = 0; i < Matrix.Length; i++)
                sb.Append($"{Matrix[i]:0.0000} ");
            sb.Append($"{Color} {Translucency} {Height} ");
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
            sb.Append($" 0 {Height}");
            foreach (var v in Profile)
            {
                sb.Append($" {v[0]} {v[1]} {v[2]} {v[3]}");
            }
            return sb.ToString().GetHashCode();
        }
    }
}
