using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml;
using Tekla.Structures.Geometry3d;
using Tekla.Structures.Solid;
using Point = Tekla.Structures.Geometry3d.Point;

namespace Polymetal.Exchange.Tekla.v2021.Helpers
{
    public class ConcreteGeometry
    {

        public CoordinateSystem CoordSystem { get { return _cs; } set { _cs = new CoordinateSystem(value.Origin, value.AxisX, value.AxisY); } }
        private CoordinateSystem _cs;
        public GeometryType GeoType;

        public ConcreteGeometry()
        {
            _cs = new CoordinateSystem();            
            GeoType = GeometryType.UNDEF;
        }

        public enum GeometryType
        {
            BOX,
            CYLI,
            PYRA,
            EXTR,
            POLYHE,
            CONE,
            MESH,
            UNDEF
        }

        internal XmlElement GetXmlElement(XmlDocument xmlDocument)
        {
            var vx = CoordSystem.AxisX.GetNormal();
            var vy = CoordSystem.AxisY.GetNormal();
            var xmlItem = xmlDocument.CreateElement(string.Empty, "Item", string.Empty);
            xmlItem.SetAttribute("vx",
                $"{Math.Round(vx.X, 4).ToString(CultureInfo.InvariantCulture)} " +
                $"{Math.Round(vx.Y, 4).ToString(CultureInfo.InvariantCulture)} " +
                $"{Math.Round(vx.Z, 4).ToString(CultureInfo.InvariantCulture)}");
            xmlItem.SetAttribute("vy",
                $"{Math.Round(vy.X, 4).ToString(CultureInfo.InvariantCulture)} " +
                $"{Math.Round(vy.Y, 4).ToString(CultureInfo.InvariantCulture)} " +
                $"{Math.Round(vy.Z, 4).ToString(CultureInfo.InvariantCulture)}");
            xmlItem.SetAttribute("orig",
                $"{Math.Round(CoordSystem.Origin.X, 4).ToString(CultureInfo.InvariantCulture)} " +
                $"{Math.Round(CoordSystem.Origin.Y, 4).ToString(CultureInfo.InvariantCulture)} " +
                $"{Math.Round(CoordSystem.Origin.Z, 4).ToString(CultureInfo.InvariantCulture)}");
            xmlItem.SetAttribute("type", $"{GeoType}");

            var parameters = string.Empty;
            if (GeoType == GeometryType.BOX)
            {
                var geo = this as BoxGeometry;
                parameters = $"{geo.Width} {geo.Length} {geo.Height}";
            }
            else if (GeoType == GeometryType.CYLI)
            {
                var geo = this as CyliGeometry;
                parameters = $"{geo.Diameter} {geo.Height}";
            }
            else if (GeoType == GeometryType.CONE)
            {
                var geo = this as ConeGeometry;
                parameters = $"{geo.BottomDiameter} {geo.TopDiameter} {geo.Height}";
            }
            else if (GeoType == GeometryType.PYRA)
            {
                var geo = this as PyraGeometry;
                parameters = $"{geo.BottomX} {geo.BottomY} {geo.TopX} {geo.TopY} {geo.Height}";                
            }
            else if (GeoType == GeometryType.EXTR)
            {
                var geo = this as ExtrGeometry;
                parameters = $"{geo.Height}";
                foreach (var point in geo.Points)
                {
                    var xmlPoint = xmlDocument.CreateElement(string.Empty, "Point", string.Empty);
                    xmlPoint.SetAttribute("x", Math.Round(point.X, 4).ToString(CultureInfo.InvariantCulture));
                    xmlPoint.SetAttribute("y", Math.Round(point.Y, 4).ToString(CultureInfo.InvariantCulture));
                    xmlPoint.SetAttribute("z", Math.Round(point.Z, 4).ToString(CultureInfo.InvariantCulture));
                    xmlItem.AppendChild(xmlPoint);
                }
            }
            else if (GeoType == GeometryType.POLYHE)
            {
                var geo = this as PolyHedronGeometry;
                foreach (var pair in geo.PointsDictionary)
                {
                    var point = pair.Key;
                    var xmlPoint = xmlDocument.CreateElement(string.Empty, "Point", string.Empty);
                    xmlPoint.SetAttribute("x", Math.Round(point.X, 4).ToString(CultureInfo.InvariantCulture));
                    xmlPoint.SetAttribute("y", Math.Round(point.Y, 4).ToString(CultureInfo.InvariantCulture));
                    xmlPoint.SetAttribute("z", Math.Round(point.Z, 4).ToString(CultureInfo.InvariantCulture));
                    xmlItem.AppendChild(xmlPoint);
                }

                foreach (var face in geo.PolyFaceList)
                {
                    var xmlFace = xmlDocument.CreateElement(string.Empty, "Face", string.Empty);
                    xmlFace.InnerText = string.Join(" ", face.PointIndexList);
                    xmlItem.AppendChild(xmlFace);
                }
            }
            if (!string.IsNullOrEmpty(parameters)) xmlItem.SetAttribute("para", $"{parameters}");
            return xmlItem;
        }

        internal void MoveOrigin(Point offset)
        {
            var matrix = MatrixFactory.FromCoordinateSystem(CoordSystem);
            CoordSystem.Origin = matrix.Transform(offset);
        }
    }

    public class CyliGeometry : ConcreteGeometry
    {
        public double Diameter { get; }
        public double Height { get; }

        public CyliGeometry(CoordinateSystem cs, double d, double h)
        {
            CoordSystem = cs;
            Diameter = d;
            Height = h;
            GeoType = GeometryType.CYLI;
            MoveOrigin(new Point(0,0,h/2));
        }
    }

    public class ConeGeometry : ConcreteGeometry
    {
        public double BottomDiameter { get; }
        public double TopDiameter { get; }
        public double Height { get; }

        public ConeGeometry(CoordinateSystem cs, double d1, double d2, double h)
        {
            CoordSystem = cs;
            BottomDiameter = d1;
            TopDiameter = d2;
            Height = h;
            GeoType = GeometryType.CONE;
            MoveOrigin(new Point(0, 0, h / 2));
        }
    }

    public class BoxGeometry : ConcreteGeometry
    {
        public double Width { get; }
        public double Length { get; }
        public double Height { get; }

        public BoxGeometry(CoordinateSystem cs, double w, double l, double h)
        {
            CoordSystem = cs;           
            Width = w;
            Length = l;
            Height = h;
            GeoType = GeometryType.BOX;
            MoveOrigin(new Point(0, 0, h / 2));
        }
    }

    public class PyraGeometry : ConcreteGeometry
    {
        public double BottomX { get; }
        public double BottomY { get; }
        public double TopX { get; }
        public double TopY { get; }
        public double Height { get; }

        public PyraGeometry(CoordinateSystem cs, double bx, double by, double tx, double ty, double h)
        {
            CoordSystem = cs;
            BottomX = bx;
            BottomY = by;
            TopX = tx;
            TopY = ty;
            Height = h;
            GeoType = GeometryType.PYRA;
            MoveOrigin(new Point(0, 0, h / 2));
        }
    }

    public class ExtrGeometry : ConcreteGeometry
    {
        public List<Point> Points { get; }
        public double Height { get; }

        public ExtrGeometry(CoordinateSystem cs, List<Point> pl, double h, bool needOffset = true)
        {
            CoordSystem = cs;
            Points = pl;
            Height = h;
            GeoType = GeometryType.EXTR;
            if (needOffset) MoveOrigin(new Point(0, 0, -h / 2));
        }
    }

    public class PolyHedronGeometry : ConcreteGeometry
    {
        public class PolyFace
        {
            public List<int> PointIndexList;
            public PolyFace()
            {
                PointIndexList = new List<int>();
            }
        }

        public Dictionary<Point, int> PointsDictionary;
        public List<PolyFace> PolyFaceList;

        public PolyHedronGeometry(CoordinateSystem cs, FaceEnumerator faceEnum)
        {
            PointsDictionary = new Dictionary<Point, int>();
            PolyFaceList = new List<PolyFace>();
            while (faceEnum.MoveNext())
            {
                var face = faceEnum.Current;
                var loopEnum = face.GetLoopEnumerator();
                var vertexList = new List<Point>();
                while (loopEnum.MoveNext())
                {
                    var vertexEnum = loopEnum.Current.GetVertexEnumerator();
                    while (vertexEnum.MoveNext())
                    {
                        vertexList.Add(vertexEnum.Current);
                    }
                }

                var groups = PolygonSplitter.SplitPolygon(vertexList, face.Normal);
                foreach (var group in groups)
                {
                    if (group.Count < 3)
                        continue;

                    var polyFace = new PolyFace();
                    foreach (var point in group)
                    {
                        if (!PointsDictionary.ContainsKey(point))
                            PointsDictionary.Add(point, PointsDictionary.Count + 1);

                        polyFace.PointIndexList.Add(PointsDictionary[point]);
                    }

                    PolyFaceList.Add(polyFace);
                }
            }

            GeoType = GeometryType.POLYHE;
        }
    }
}
