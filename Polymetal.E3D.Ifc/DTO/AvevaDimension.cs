using System;
using System.Linq;
using System.Text;
using Aveva.Core.Database;
using Aveva.Core.Geometry;
using Aveva.Core.Maths.Geometry;
using Aveva.Core3D.Interaction.Generic.Entities.Dimensions.Common;
using Aveva.Core3D.Interaction.Generic.Entities.Dimensions.LinDim.Model;
using Aveva.Core3D.Interaction.Generic.Entities.Vertex;

namespace Polymetal.E3D.Ifc.DTO
{
    internal class AvevaDimension : AvevaGeometry
    {
        internal double TextHeight { get; }
        internal string[] TextArray { get; }
        internal Position[] PositionArray { get; }

        internal AvevaDimension(DbElement lindim)
        {
            var desiLinDimProxy = new DesiLinDimProxy(lindim);
            var dimensionPoints = desiLinDimProxy.GetDimensionPoints().ToList();

            TextHeight = lindim.GetDouble(DbAttributeInstance.CHEI);
            PositionArray = new Position[dimensionPoints.Count];
            TextArray = new string[dimensionPoints.Count - 1];

            Matrix = GetMatrix(lindim);

            for (var i = 0; i < dimensionPoints.Count - 1; i++)
            {
                var pos1 = GetPosition(dimensionPoints[i], lindim);
                var pos2 = GetPosition(dimensionPoints[i + 1], lindim);

                PositionArray[i + 1] = pos2;
                if (i == 0)
                    PositionArray[0] = pos1;
            }

            Array.Sort(PositionArray, (x, y) => x.X.CompareTo(y.X));

            for (var i = 0; i < PositionArray.Length - 1; i++)
            {
                TextArray[i] = Math.Abs(Math.Round(PositionArray[i].X - PositionArray[i + 1].X, 2)).ToString();
            }

            //black
            Color = 15;

            Hash = GetHash();
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            
            sb.Append($"{GetType().Name} " +
                      $"{string.Join(" ", Matrix.Select(x => x.ToString("0.0000")))} " +
                      $"{Color} {Translucency} {TextHeight} {PositionArray.Length} " +
                      $"{string.Join(" ", PositionArray.Select(p => $"{p.X} {p.Y} {p.Z}"))} " +
                      $"{string.Join(" ", TextArray)}");

            return sb.ToString();
        }

        private int GetHash()
        {
            var sb = new StringBuilder();

            sb.Append($"{GetType().Name} " +
                      $"{string.Join(" ",Matrix)} " +
                      "0 " +
                      $"{TextHeight} {PositionArray.Length} " +
                      $"{string.Join(" ", PositionArray.Select(p => $"{p.X} {p.Y} {p.Z}"))} " +
                      $"{string.Join(" ", TextArray)}");

            return sb.ToString().GetHashCode();
        }

        private Position GetPosition(VertexBase point, DbElement lindim)
        {
            if (point is DimensionPointPosition pointPosition)
            {
                return Position.Create(pointPosition.PosOwner.X, pointPosition.PosOwner.Y, pointPosition.PosOwner.Z);
            }

            if (point is DimensionPointPpoint pointPpoint && !pointPpoint.Reference.IsNull && pointPpoint.Reference.IsValid)
            {
                var d3Transform = D3Transform.Create(Matrix).Inverse();
                var transformedPoint = d3Transform.Multiply(D3Point.Create(pointPpoint.WorldPos.X, pointPpoint.WorldPos.Y,
                    pointPpoint.WorldPos.Z));
                return Position.Create(transformedPoint.X, transformedPoint.Y, transformedPoint.Z);

                //var qualifier = new DbQualifier();
                //qualifier.wrtQualifier = lindim;
                //qualifier.doubleQualifiers = new double[] { pointPpoint.PpointNumber };
                //return pointPpoint.Reference.GetPosition(DbAttributeInstance.PPOS, qualifier);
            }

            return Position.Create();
        }
    }
}
