using System;
using System.Collections.Generic;
using System.Text;
using Xbim.Common.Geometry;

namespace Polymetal.Pdms.IfcServer
{
    internal class AvevaGeometry
    {
        internal XbimMatrix3D Matrix;
        internal int Translucency;
        internal int ColorIndex;
        internal string LayerName;
        internal double[] Color;
        internal int Hash;
        internal List<AvevaGeometry> Negatives;

        internal AvevaGeometry()
        {
            Negatives = new List<AvevaGeometry>();
        }

        internal static AvevaGeometry StringToGeometry(string line)
        {
            var splitedLine = line.Split(new[] {'#'}, StringSplitOptions.RemoveEmptyEntries);
            if (splitedLine.Length == 0) return null;
            var positive = GetGeometry(splitedLine[0]);
            for (var i=1;i<splitedLine.Length;i++)
                positive.Negatives.Add(GetGeometry(splitedLine[i]));

            var rootMatrix = Representation.RootMatrix;
            rootMatrix.Invert();
            //var posM = positive.Matrix;
            //posM.Invert();
            positive.Matrix = positive.Matrix*rootMatrix;
            foreach (var n in positive.Negatives)
            {
                n.Matrix = n.Matrix * rootMatrix;
            }

            return positive;
        }

        private static AvevaGeometry GetGeometry(string line)
        {
            var data = line.Split(new[] {' '}, StringSplitOptions.RemoveEmptyEntries);
            var type = data[0];
            AvevaGeometry geometry =null;
            switch (type)
            {
                case "AvevaPyramid":
                case "PdmsPyramid":
                    geometry = new AvevaPyramid(data);
                    break;
                case "AvevaBox":
                case "PdmsBox":
                    geometry = new AvevaBox(data);
                    break;
                case "AvevaRectTorus":
                case "PdmsRectTorus":
                    geometry = new AvevaRectTorus(data);
                    break;
                case "AvevaCircTorus":
                case "PdmsCircTorus":
                    geometry = new AvevaCircTorus(data);
                    break;
                case "AvevaEllipticalDish":
                case "PdmsEllipticalDish":
                    geometry = new AvevaEllipticalDish(data);
                    break;
                case "AvevaSphericalDish":
                case "PdmsSphericalDish":
                    geometry = new AvevaSphericalDish(data);
                    break;
                case "AvevaSnout":
                case "PdmsSnout":
                    geometry = new AvevaSnout(data);
                    break;
                case "AvevaSlcy":
                case "PdmsSlcy":
                    geometry = new AvevaSlcy(data);
                    break;
                case "AvevaCone":
                case "PdmsCone":
                    geometry = new AvevaCone(data);
                    break;
                case "AvevaCylinder":
                case "PdmsCylinder":
                    geometry = new AvevaCylinder(data);
                    break;
                case "AvevaSphere":
                case "PdmsSphere":
                    geometry = new AvevaSphere(data);
                    break;
                case "AvevaRevolution":
                case "PdmsRevolution":
                    geometry = new AvevaRevolution(data);
                    break;
                case "AvevaExtrusion":
                case "PdmsExtrusion":
                    geometry = new AvevaExtrusion(data);
                    break;
                case "AvevaFacetGroup":
                case "PdmsFacetGroup":
                    geometry = new AvevaFacetGroup(line);
                    break;
            }

            geometry.Matrix = new XbimMatrix3D(
                double.Parse(data[1]), double.Parse(data[2]), double.Parse(data[3]), 0.0,
                double.Parse(data[4]), double.Parse(data[5]), double.Parse(data[6]), 0.0,
                double.Parse(data[7]), double.Parse(data[8]), double.Parse(data[9]), 0.0,
                double.Parse(data[10]), double.Parse(data[11]), double.Parse(data[12]), 1.0);
            geometry.ColorIndex = int.Parse(data[13]);
            geometry.Translucency = int.Parse(data[14].Replace(";",string.Empty));
            var clrData = Program.LayersDictionary[geometry.ColorIndex];
            geometry.LayerName = (string) clrData[0];
            geometry.Color = (double[]) clrData[1];
            return geometry;
        }
    }

    internal class AvevaPyramid : AvevaGeometry
    {
        internal double Xbottom;
        internal double Ybottom;
        internal double Xtop;
        internal double Ytop;
        internal double Xoffset;
        internal double Yoffset;
        internal double Height;

        internal AvevaPyramid(string[] data)
        {
            Xbottom = double.Parse(data[15]);
            Ybottom = double.Parse(data[16]);
            Xtop = double.Parse(data[17]);
            Ytop = double.Parse(data[18]);
            Xoffset = double.Parse(data[19]);
            Yoffset = double.Parse(data[20]);
            Height = double.Parse(data[21]);
            var str = string.Format("{0}-{1}-{2}-{3}-{4}-{5}-{6}-{7}", GetType().Name, Xbottom, Ybottom, Xtop, Ytop,
                Xoffset, Yoffset, Height);
            Hash = str.GetHashCode();
        }
    }

    internal class AvevaBox : AvevaGeometry
    {
        internal double Xlength;
        internal double Ylength;
        internal double Zlength;

        internal AvevaBox(string[] data)
        {
            Xlength = double.Parse(data[15]);
            Ylength = double.Parse(data[16]);
            Zlength = double.Parse(data[17]);
            var str = string.Format("{0}-{1}-{2}-{3}", GetType().Name, Xlength, Ylength, Zlength);
            Hash = str.GetHashCode();
        }
    }

    internal class AvevaRectTorus : AvevaGeometry
    {
        internal double Rinside;
        internal double Routside;
        internal double Height;
        internal double Angle;

        internal AvevaRectTorus(string[] data)
        {
            Rinside = double.Parse(data[15]);
            Routside = double.Parse(data[16]);
            Height = double.Parse(data[17]);
            Angle = double.Parse(data[18]);
            var str = string.Format("{0}-{1}-{2}-{3}-{4}", GetType().Name, Rinside, Routside, Height, Angle);
            Hash = str.GetHashCode();
        }
    }

    internal class AvevaCircTorus : AvevaGeometry
    {
        internal double Rcentre;
        internal double Radius;
        internal double Angle;

        internal AvevaCircTorus(string[] data)
        {
            Rcentre = double.Parse(data[15]);
            Radius = double.Parse(data[16]);
            Angle = double.Parse(data[17]);
            var str = string.Format("{0}-{1}-{2}-{3}", GetType().Name, Rcentre, Radius, Angle);
            Hash = str.GetHashCode();
        }
    }

    internal class AvevaEllipticalDish : AvevaGeometry
    {
        internal double Radius;
        internal double Height;

        internal AvevaEllipticalDish(string[] data)
        {
            Radius = double.Parse(data[15]);
            Height = double.Parse(data[16]);
            var str = string.Format("{0}-{1}-{2}", GetType().Name, Radius, Height);
            Hash = str.GetHashCode();
        }
    }

    internal class AvevaSphericalDish : AvevaGeometry
    {
        internal double Radius;
        internal double Height;

        internal AvevaSphericalDish(string[] data)
        {
            Radius = double.Parse(data[15]);
            Height = double.Parse(data[16]);
            var str = string.Format("{0}-{1}-{2}", GetType().Name, Radius, Height);
            Hash = str.GetHashCode();
        }
    }

    internal class AvevaSnout : AvevaGeometry
    {
        internal double Rbottom;
        internal double Rtop;
        internal double Height;
        internal double Xoffset;
        internal double Yoffset;

        internal AvevaSnout(string[] data)
        {
            Rbottom = double.Parse(data[15]);
            Rtop = double.Parse(data[16]);
            Height = double.Parse(data[17]);
            Xoffset = double.Parse(data[18]);
            Yoffset = double.Parse(data[19]);
            var str = string.Format("{0}-{1}-{2}-{3}-{4}-{5}", GetType().Name, Rbottom, Rtop, Height, Xoffset, Yoffset);
            Hash = str.GetHashCode();
        }
    }

    internal class AvevaSlcy : AvevaGeometry
    {
        internal double Radius;
        internal double Height;
        internal double Angle1;
        internal double Angle2;
        internal double Angle3;
        internal double Angle4;

        internal AvevaSlcy(string[] data)
        {
            Radius = double.Parse(data[15]);
            Height = double.Parse(data[16]);

            Angle1 = double.Parse(data[17]);
            Angle2 = double.Parse(data[18]);
            Angle3 = double.Parse(data[19]);
            Angle4 = double.Parse(data[20]);
            var str = string.Format("{0}-{1}-{2}-{3}-{4}-{5}-{6}", GetType().Name, Radius, Height, Angle1, Angle2,
                Angle3, Angle4);
            Hash = str.GetHashCode();
        }
    }

    internal class AvevaCone : AvevaGeometry
    {
        internal double Rbottom;
        internal double Rtop;
        internal double Height;

        internal AvevaCone(string[] data)
        {
            Rbottom = double.Parse(data[15]);
            Rtop = double.Parse(data[16]);
            Height = double.Parse(data[17]);
            var str = string.Format("{0}-{1}-{2}-{3}", GetType().Name, Rbottom, Rtop, Height);
            Hash = str.GetHashCode();
        }
    }

    internal class AvevaCylinder : AvevaGeometry
    {
        internal double Radius;
        internal double Height;

        internal AvevaCylinder(string[] data)
        {
            Radius = double.Parse(data[15]); 
            Height = double.Parse(data[16]);
            var str = string.Format("{0}-{1}-{2}", GetType().Name, Radius, Height);
            Hash = str.GetHashCode();
        }
    }

    internal class AvevaSphere : AvevaGeometry
    {
        internal double Radius;

        internal AvevaSphere(string[] data)
        {
            Radius = double.Parse(data[15]); 
            var str = string.Format("{0}-{1}", GetType().Name, Radius);
            Hash = str.GetHashCode();
        }
    }

    internal class AvevaRevolution : AvevaGeometry
    {
        internal double Angle;
        internal List<double[]> Profile;

        internal AvevaRevolution(string[] data)
        {
            Angle = double.Parse(data[15]);
            Profile = new List<double[]>();
            for (var i = 16; i < data.Length; i += 4)
            {
                Profile.Add(new []{double.Parse(data[i]), double.Parse(data[i+1]), double.Parse(data[i+2]), double.Parse(data[i+3])});
            }

            var sb = new StringBuilder();
            sb.Append(string.Format("{0}-{1}", GetType().Name, Angle));
            foreach (var v in Profile)
            {
                sb.Append(string.Format("-{0}-{1}-{2}-{3}", v[0], v[1], v[2], v[3]));
            }
            Hash = sb.ToString().GetHashCode();
        }
    }

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
                Profile.Add(new[] { double.Parse(data[i]), double.Parse(data[i + 1]), double.Parse(data[i + 2]), double.Parse(data[i + 3]) });
            }

            var sb = new StringBuilder();
            sb.Append(string.Format("{0}-{1}", GetType().Name, Height));
            foreach (var v in Profile)
            {
                sb.Append(string.Format("-{0}-{1}-{2}-{3}", v[0], v[1], v[2], v[3]));
            }
            Hash = sb.ToString().GetHashCode();
        }
    }

    internal class AvevaFacetGroup : AvevaGeometry
    {
        internal List<List<XbimPoint3D>> Faces;

        internal AvevaFacetGroup(string line)
        {
            Faces =new List<List<XbimPoint3D>>();
            var data = line.Split(';');

            for (var i = 1; i < data.Length; i++)
            {
                var face = new List<XbimPoint3D>();
                var doubleStr = data[i].Split(new []{' '}, StringSplitOptions.RemoveEmptyEntries);
                for (var j = 0; j < doubleStr.Length; j += 3)
                {
                    face.Add(new XbimPoint3D(double.Parse(doubleStr[j]),double.Parse(doubleStr[j + 1]),double.Parse(doubleStr[j + 2])));
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
                    sb.Append(string.Format("-{0}-{1}-{2}", point.X,point.Y,point.Z));
                }
            }
            Hash = sb.ToString().GetHashCode();
        }
    }
}