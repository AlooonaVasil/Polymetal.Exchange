using System;
using System.Collections.Generic;
using Xbim.Common.Geometry;

namespace Polymetal.E3D.IfcServer
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
            positive.Matrix *= rootMatrix;
            foreach (var n in positive.Negatives)
            {
                n.Matrix *= rootMatrix;
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
                    geometry = new DTO.AvevaPyramid(data);
                    break;
                case "AvevaBox":
                    geometry = new DTO.AvevaBox(data);
                    break;
                case "AvevaRectTorus":
                    geometry = new DTO.AvevaRectTorus(data);
                    break;
                case "AvevaCircTorus":
                    geometry = new DTO.AvevaCircTorus(data);
                    break;
                case "AvevaEllipticalDish":
                    geometry = new DTO.AvevaEllipticalDish(data);
                    break;
                case "AvevaSphericalDish":
                    geometry = new DTO.AvevaSphericalDish(data);
                    break;
                case "AvevaSnout":
                    geometry = new DTO.AvevaSnout(data);
                    break;
                case "AvevaSlcy":
                    geometry = new DTO.AvevaSlcy(data);
                    break;
                case "AvevaCone":
                    geometry = new DTO.AvevaCone(data);
                    break;
                case "AvevaCylinder":
                    geometry = new DTO.AvevaCylinder(data);
                    break;
                case "AvevaSphere":
                    geometry = new DTO.AvevaSphere(data);
                    break;
                case "AvevaRevolution":
                    geometry = new DTO.AvevaRevolution(data);
                    break;
                case "AvevaExtrusion":
                    geometry = new DTO.AvevaExtrusion(data);
                    break;
                case "AvevaFacetGroup":
                    geometry = new DTO.AvevaFacetGroup(line);
                    break;
                case "AvevaLine":
                    geometry = new DTO.AvevaLine(data);
                    break;
                case "AvevaDimension":
                    geometry = new DTO.AvevaDimension(data);
                    break;
            }

            geometry.Matrix = new XbimMatrix3D(
                double.Parse(data[1]), double.Parse(data[2]), double.Parse(data[3]), 0.0,
                double.Parse(data[4]), double.Parse(data[5]), double.Parse(data[6]), 0.0,
                double.Parse(data[7]), double.Parse(data[8]), double.Parse(data[9]), 0.0,
                double.Parse(data[10]), double.Parse(data[11]), double.Parse(data[12]), 1.0);
            geometry.ColorIndex = int.Parse(data[13]);
            geometry.Translucency = int.Parse(data[14].Replace(";",string.Empty));
            if (Program.LayersDictionary.ContainsKey(geometry.ColorIndex))
            {
                var clrData = Program.LayersDictionary[geometry.ColorIndex];
                geometry.LayerName = (string)clrData[0];
                geometry.Color = (double[])clrData[1];
            }
            else
            {
                geometry.LayerName = "Прочее";
                geometry.Color = Program.RgbIndexDictionary[geometry.ColorIndex];
            }
            return geometry;
        }
    }
}