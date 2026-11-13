using System;
using System.Text;
using Aveva.Core.Database;
using Aveva.Core.Geometry;
using Aveva.Core3D.CSG;

namespace Polymetal.E3D.Ifc.DTO
{
    internal class AvevaBox : AvevaGeometry
    {
        internal double Xlength;
        internal double Ylength;
        internal double Zlength;

        internal AvevaBox(DbElement item, bool byVolume)
        {
            Spatial.Instance.LimitsBox(item, out LimitsBox box);
            Xlength = Math.Abs(box.Maximum.X - box.Minimum.X);
            Ylength = Math.Abs(box.Maximum.Y - box.Minimum.Y);
            Zlength = Math.Abs(box.Maximum.Z - box.Minimum.Z);

            var centre = box.Centre();
            Matrix = new[]
                {
                    1.0, 0.0, 0.0,
                    0.0, 1.0, 0.0,
                    0.0, 0.0, 1.0,
                    centre.X, centre.Y,centre.Z
                };
            Hash = GetHash();
        }

        internal AvevaBox(DbElement item)
        {
            Xlength = item.GetDouble(DbAttributeInstance.XLEN);
            Ylength = item.GetDouble(DbAttributeInstance.YLEN);
            Zlength = item.GetDouble(DbAttributeInstance.ZLEN);
            Matrix = GetMatrix(item);
            Hash = GetHash();
        }

        internal AvevaBox(CSGBox csg)
        {
            Xlength = csg.XLength;
            Ylength = csg.YLength;
            Zlength = csg.ZLength;
            Matrix = csg.Transform.AsArray12();
            Translucency = csg.Translucency;
            Hash = GetHash();
        }

        internal AvevaBox(DbElement root, DbElement gmItem)
        {
            if (gmItem.ElementType == DbElementTypeInstance.SRECTANGLE)
            {
                var poss = root.GetPosition(DbAttributeInstance.POSS);
                var pose = root.GetPosition(DbAttributeInstance.POSE);

                Xlength = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PXLE));
                Ylength = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PYLE));
                Zlength = poss.Distance(pose);

                var px = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PX));
                var py = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PY));

                EvaluatePointDirPos(root, gmItem.GetAsString(DbAttributeInstance.PLAX), out Direction directionY, out Position positionY);

                var orientation = Orientation.Create("Y IS " + directionY);

                Matrix = GetMatrix(root, orientation, Position.Create());

                var jusl = root.GetAsString(DbAttributeInstance.JUSL).Replace("unset", "NA");
                var positionPlin = root.EvaluatePosition(ExportIfc.GetOrAddExpression("PPLIN " + jusl + " OFFS WRT CE"));
                Matrix[9] -= positionPlin.X;
                Matrix[10] -= positionPlin.Y;
                Matrix[11] -= positionPlin.Z;
            }
            else if (gmItem.ElementType == DbElementTypeInstance.BOXING && root.ElementType == DbElementTypeInstance.TUBING)
            {
                Xlength = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PXLE));
                Ylength = root.GetDouble(DbAttributeInstance.ITLE);
                Zlength = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PZLE));

                Matrix = GetMatrix(root);
            }
            else
            {
                Xlength = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PXLE));
                Ylength = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PYLE));
                Zlength = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PZLE));

                var px = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PX));
                var py = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PY));
                var pz = EvaluateDouble(root, gmItem.GetAsString(DbAttributeInstance.PZ));

                var orientation = Orientation.Create("Z IS UP");
                var position = Position.Create(px, py, pz);
                Matrix = GetMatrix(root, orientation, position);
            }

            Hash = GetHash();
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append($"{GetType().Name} ");
            for (var i = 0; i < Matrix.Length; i++)
                sb.Append($"{Matrix[i]:0.0000} ");
            sb.Append($"{Color} {Translucency} {Xlength} {Ylength} {Zlength}");
            return sb.ToString();
        }

        private int GetHash()
        {
            var sb = new StringBuilder();
            sb.Append(GetType().Name);
            foreach (var t in Matrix)
                sb.Append(" " + t);
            sb.Append($" 0 {Xlength} {Ylength} {Zlength}");
            return sb.ToString().GetHashCode();
        }
    }
}
