using System;
using System.Collections.Generic;
using System.Text;
using Xbim.Common.Geometry;

namespace Polymetal.Pdms.IfcServer
{
    internal class PdmsGeometry
    {
        internal XbimMatrix3D Matrix;
        internal int Translucency;
        internal int ColorIndex;
        internal string LayerName;
        internal double[] Color;
        internal int Hash;
        internal List<PdmsGeometry> Negatives;

        internal PdmsGeometry()
        {
            Negatives = new List<PdmsGeometry>();
        }

        internal static PdmsGeometry StringToGeometry(string line)
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

        private static PdmsGeometry GetGeometry(string line)
        {
            var data = line.Split(new[] {' '}, StringSplitOptions.RemoveEmptyEntries);
            var type = data[0];
            PdmsGeometry geometry =null;
            switch (type)
            {
                case "PdmsPyramid":
                    geometry = new PdmsPyramid(data);
                    break;
                case "PdmsBox":
                    geometry = new PdmsBox(data);
                    break;
                case "PdmsRectTorus":
                    geometry = new PdmsRectTorus(data);
                    break;
                case "PdmsCircTorus":
                    geometry = new PdmsCircTorus(data);
                    break;
                case "PdmsEllipticalDish":
                    geometry = new PdmsEllipticalDish(data);
                    break;
                case "PdmsSphericalDish":
                    geometry = new PdmsSphericalDish(data);
                    break;
                case "PdmsSnout":
                    geometry = new PdmsSnout(data);
                    break;
                case "PdmsSlcy":
                    geometry = new PdmsSlcy(data);
                    break;
                case "PdmsCone":
                    geometry = new PdmsCone(data);
                    break;
                case "PdmsCylinder":
                    geometry = new PdmsCylinder(data);
                    break;
                case "PdmsSphere":
                    geometry = new PdmsSphere(data);
                    break;
                case "PdmsRevolution":
                    geometry = new PdmsRevolution(data);
                    break;
                case "PdmsExtrusion":
                    geometry = new PdmsExtrusion(data);
                    break;
                case "PdmsFacetGroup":
                    geometry = new PdmsFacetGroup(line);
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

    internal class PdmsPyramid : PdmsGeometry
    {
        internal double Xbottom;
        internal double Ybottom;
        internal double Xtop;
        internal double Ytop;
        internal double Xoffset;
        internal double Yoffset;
        internal double Height;

        internal PdmsPyramid(string[] data)
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

    internal class PdmsBox : PdmsGeometry
    {
        internal double Xlength;
        internal double Ylength;
        internal double Zlength;

        internal PdmsBox(string[] data)
        {
            Xlength = double.Parse(data[15]);
            Ylength = double.Parse(data[16]);
            Zlength = double.Parse(data[17]);
            var str = string.Format("{0}-{1}-{2}-{3}", GetType().Name, Xlength, Ylength, Zlength);
            Hash = str.GetHashCode();
        }
    }

    internal class PdmsRectTorus : PdmsGeometry
    {
        internal double Rinside;
        internal double Routside;
        internal double Height;
        internal double Angle;

        internal PdmsRectTorus(string[] data)
        {
            Rinside = double.Parse(data[15]);
            Routside = double.Parse(data[16]);
            Height = double.Parse(data[17]);
            Angle = double.Parse(data[18]);
            var str = string.Format("{0}-{1}-{2}-{3}-{4}", GetType().Name, Rinside, Routside, Height, Angle);
            Hash = str.GetHashCode();
        }
    }

    internal class PdmsCircTorus : PdmsGeometry
    {
        internal double Rcentre;
        internal double Radius;
        internal double Angle;

        internal PdmsCircTorus(string[] data)
        {
            Rcentre = double.Parse(data[15]);
            Radius = double.Parse(data[16]);
            Angle = double.Parse(data[17]);
            var str = string.Format("{0}-{1}-{2}-{3}", GetType().Name, Rcentre, Radius, Angle);
            Hash = str.GetHashCode();
        }
    }

    internal class PdmsEllipticalDish : PdmsGeometry
    {
        internal double Radius;
        internal double Height;

        internal PdmsEllipticalDish(string[] data)
        {
            Radius = double.Parse(data[15]);
            Height = double.Parse(data[16]);
            var str = string.Format("{0}-{1}-{2}", GetType().Name, Radius, Height);
            Hash = str.GetHashCode();
        }
    }

    internal class PdmsSphericalDish : PdmsGeometry
    {
        internal double Radius;
        internal double Height;

        internal PdmsSphericalDish(string[] data)
        {
            Radius = double.Parse(data[15]);
            Height = double.Parse(data[16]);
            var str = string.Format("{0}-{1}-{2}", GetType().Name, Radius, Height);
            Hash = str.GetHashCode();
        }
    }

    internal class PdmsSnout : PdmsGeometry
    {
        internal double Rbottom;
        internal double Rtop;
        internal double Height;
        internal double Xoffset;
        internal double Yoffset;

        internal PdmsSnout(string[] data)
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

    internal class PdmsSlcy : PdmsGeometry
    {
        internal double Radius;
        internal double Height;
        internal double Angle1;
        internal double Angle2;
        internal double Angle3;
        internal double Angle4;

        internal PdmsSlcy(string[] data)
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

    internal class PdmsCone : PdmsGeometry
    {
        internal double Rbottom;
        internal double Rtop;
        internal double Height;

        internal PdmsCone(string[] data)
        {
            Rbottom = double.Parse(data[15]);
            Rtop = double.Parse(data[16]);
            Height = double.Parse(data[17]);
            var str = string.Format("{0}-{1}-{2}-{3}", GetType().Name, Rbottom, Rtop, Height);
            Hash = str.GetHashCode();
        }
    }

    internal class PdmsCylinder : PdmsGeometry
    {
        internal double Radius;
        internal double Height;

        internal PdmsCylinder(string[] data)
        {
            Radius = double.Parse(data[15]); 
            Height = double.Parse(data[16]);
            var str = string.Format("{0}-{1}-{2}", GetType().Name, Radius, Height);
            Hash = str.GetHashCode();
        }
    }

    internal class PdmsSphere : PdmsGeometry
    {
        internal double Radius;

        internal PdmsSphere(string[] data)
        {
            Radius = double.Parse(data[15]); 
            var str = string.Format("{0}-{1}", GetType().Name, Radius);
            Hash = str.GetHashCode();
        }
    }

    internal class PdmsRevolution : PdmsGeometry
    {
        internal double Angle;
        internal List<double[]> Profile;

        internal PdmsRevolution(string[] data)
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

    internal class PdmsExtrusion : PdmsGeometry
    {
        internal double Height;
        internal List<double[]> Profile;

        internal PdmsExtrusion(string[] data)
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

    internal class PdmsFacetGroup : PdmsGeometry
    {
        internal List<List<XbimPoint3D>> Faces;

        internal PdmsFacetGroup(string line)
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