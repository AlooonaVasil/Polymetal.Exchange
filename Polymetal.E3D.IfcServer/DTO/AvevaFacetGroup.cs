using System;
using System.Collections.Generic;
using System.Text;
using Xbim.Common.Geometry;
using Xbim.Ifc2x3.GeometryResource;

namespace Polymetal.E3D.IfcServer.DTO
{
    internal class AvevaFacetGroup : AvevaGeometry
    {
        internal List<List<XbimPoint3D>> Faces;

        internal AvevaFacetGroup(string line)
        {
            Faces = new List<List<XbimPoint3D>>();
            var data = line.Split(';');

            for (var i = 1; i < data.Length; i++)
            {
                var face = new List<XbimPoint3D>();
                var doubleStr = data[i].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                for (var j = 0; j < doubleStr.Length; j += 3)
                {
                    face.Add(new XbimPoint3D(double.Parse(doubleStr[j]), double.Parse(doubleStr[j + 1]), double.Parse(doubleStr[j + 2])));
                }
                Faces.Add(face);
            }

            var sb = new StringBuilder();
            sb.Append(string.Format("{0}-{1}", GetType().Name, Faces.Count));
            foreach (var face in Faces)
            {
                sb.Append(";");
                foreach (var point in face)
                {
                    sb.Append(string.Format("-{0}-{1}-{2}", point.X, point.Y, point.Z));
                }
            }
            Hash = sb.ToString().GetHashCode();
        }

        internal IfcRepresentationItem GetShapeRepresentation()
        {
            var faces = new List<List<XbimPoint3D>>();
            foreach (var face in Faces)
            {
                var f = new List<XbimPoint3D>();
                foreach (var point in face)
                {
                    f.Add(Matrix.Transform(point));
                }
                faces.Add(f);
            }
            return Representation.CreateFacetedBrep(faces);
        }
    }
}
