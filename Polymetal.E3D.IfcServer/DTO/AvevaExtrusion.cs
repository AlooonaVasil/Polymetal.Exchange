using System.Collections.Generic;
using System.Text;
using Xbim.Common.Geometry;
using Xbim.Ifc2x3.GeometryResource;

namespace Polymetal.E3D.IfcServer.DTO
{
    internal class AvevaExtrusion : AvevaGeometry
    {
        internal double Height;
        internal List<double[]> Profile;

        internal AvevaExtrusion(string[] data)
        {
            Height = double.Parse(data[15]);
            Profile = new List<double[]>();
            for (var i = 16; i < data.Length; i += 4)
            {
                Profile.Add(new[]
                {
                    double.Parse(data[i]), double.Parse(data[i + 1]), double.Parse(data[i + 2]),
                    double.Parse(data[i + 3])
                });
            }

            var sb = new StringBuilder();
            sb.Append(string.Format("{0}-{1}", GetType().Name, Height));
            foreach (var v in Profile)
            {
                sb.Append(string.Format("-{0}-{1}-{2}-{3}", v[0], v[1], v[2], v[3]));
            }

            Hash = sb.ToString().GetHashCode();
        }

        internal IfcRepresentationItem GetShapeRepresentation()
        {
            if (Height <= 0.0 || Profile.Count < 3)
                return null;

            var pointsProfileDef = Representation.CreatePointsProfileDef(Profile);

            var axis2Placement3D =
                EntityCreator.CreateAxis2Placement3D(
                    new XbimPoint3D(0.0, 0.0, 0.0),
                    new XbimVector3D(0.0, 0.0, 1.0), new XbimVector3D(1.0, 0.0, 0.0), Matrix);

            return Representation.CreateExtrudedAreaSolid(axis2Placement3D, pointsProfileDef, new XbimVector3D(0, 0, 1),
                Height);
        }
    }
}