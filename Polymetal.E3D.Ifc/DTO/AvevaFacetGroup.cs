using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Aveva.Core.Database;
using Aveva.Core.Geometry;
using Aveva.Core.Maths.Geometry;
using Aveva.Core3D.CSG;

namespace Polymetal.E3D.Ifc.DTO
{
    internal class AvevaFacetGroup : AvevaGeometry
    {
        internal List<List<double[]>> Faces;

        internal AvevaFacetGroup(List<List<double[]>> faces, double[] matrix)
        {
            Faces = faces;
            Matrix = SiteMatrix.Multiply(D3Transform.Create(matrix)).AsArray12();
            Hash = GetHash();
        }

        internal AvevaFacetGroup(DbElement item)
        {
            Faces = new List<List<double[]>>();
            if (item.ElementType == DbElementTypeInstance.POLYHEDRON ||
                item.ElementType == DbElementTypeInstance.NPOLYHEDRON)
            {
                var pointDict = new Dictionary<DbElement, Position>();
                var polptl = item.Members(DbElementTypeInstance.POLPTLIST).FirstOrDefault();
                if (polptl != null && !polptl.IsNull)
                {
                    foreach (var poin in polptl.Members())
                        pointDict.Add(poin, poin.GetPosition(DbAttributeInstance.POS));
                }

                foreach (var polfac in item.Members(DbElementTypeInstance.POLFACE))
                {
                    foreach (var poloop in polfac.Members(DbElementTypeInstance.POLOOP))
                    {
                        foreach (var loopts in poloop.Members(DbElementTypeInstance.LOOPTS))
                        {
                            var vxrefs = loopts.GetElementArray(DbAttributeInstance.VXREF);

                            var face = new List<double[]>();
                            foreach (var vxref in vxrefs)
                            {
                                if (!pointDict.ContainsKey(vxref)) continue;
                                var vert = pointDict[vxref];
                                face.Add(new[] { vert.X, vert.Y, vert.Z });
                            }

                            Faces.Add(face);
                        }
                    }
                }
            }
            else
            {
                throw new NotImplementedException();
            }

            Matrix = SiteMatrix.Multiply(D3Transform.Create(GetMatrix(item))).AsArray12();
            Hash = GetHash();
        }

        internal AvevaFacetGroup(CSGFacetGroup csg)
        {
            Faces = new List<List<double[]>>();
            foreach (var facet in csg.Facets)
            {
                foreach (var loop in facet.Loops)
                {
                    var face = new List<double[]>();
                    foreach (var vert in loop.Vertices)
                    {
                        face.Add(new[] { vert.X, vert.Y, vert.Z });
                    }

                    Faces.Add(face);
                }
            }

            Matrix = SiteMatrix.Multiply(csg.Transform).AsArray12();
            Translucency = csg.Translucency;
            Hash = GetHash();
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            AppendTo(sb);
            return sb.ToString();
        }

        internal void AppendTo(StringBuilder sb)
        {
            sb.Append(GetType().Name);
            sb.Append(' ');
            for (var i = 0; i < Matrix.Length; i++)
            {
                sb.Append(Matrix[i].ToString("0.0000"));
                sb.Append(' ');
            }
            sb.Append(Color);
            sb.Append(' ');
            sb.Append(Translucency);
            sb.Append(';');

            if (Faces == null)
                return;

            foreach (var face in Faces)
            {
                foreach (var arr in face)
                {
                    if (arr == null || arr.Length < 3)
                        continue;
                    sb.Append(' ');
                    sb.Append(arr[0].ToString("0.####"));
                    sb.Append(' ');
                    sb.Append(arr[1].ToString("0.####"));
                    sb.Append(' ');
                    sb.Append(arr[2].ToString("0.####"));
                }

                sb.Append(';');
            }
        }

        private int GetHash()
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + GetType().Name.GetHashCode();
                if (Matrix != null)
                {
                    for (var i = 0; i < Matrix.Length; i++)
                        hash = hash * 31 + Matrix[i].GetHashCode();
                }

                if (Faces == null)
                    return hash;

                foreach (var face in Faces)
                {
                    hash = hash * 31 + face.Count;
                    foreach (var arr in face)
                    {
                        if (arr == null || arr.Length < 3)
                            continue;
                        hash = hash * 31 + arr[0].GetHashCode();
                        hash = hash * 31 + arr[1].GetHashCode();
                        hash = hash * 31 + arr[2].GetHashCode();
                    }
                }

                return hash;
            }
        }
    }
}