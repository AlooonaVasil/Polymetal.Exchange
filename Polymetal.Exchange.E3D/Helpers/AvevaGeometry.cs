using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using Aveva.Core.Database;
using Aveva.Core.Geometry;
using Aveva.Core.Maths.Geometry;
using Tekla.Structures.Geometry3d;

namespace Polymetal.Exchange.E3D.Helpers
{
    public class AvevaGeometry
    {
        public CoordinateSystem CoordSystem;
        public GeometryType GeoType;

        public AvevaGeometry()
        {
            CoordSystem = new CoordinateSystem();
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

        public DbElementType AvevaElementType;

        internal static AvevaGeometry GetAvevaGeometry(XmlNode xmlNode, bool isPositive)
        {
            var attributes = xmlNode.Attributes;
            var type = (GeometryType)Enum.Parse(typeof(GeometryType), attributes["type"].Value);
            var result = new AvevaGeometry();

            var vxData = attributes["vx"].Value.Replace(".", ",").Split(' ');
            var vyData = attributes["vy"].Value.Replace(".", ",").Split(' ');
            var origData = attributes["orig"].Value.Replace(".", ",").Split(' ');
            var vx = new Vector(double.Parse(vxData[0]), double.Parse(vxData[1]), double.Parse(vxData[2]));
            var vy = new Vector(double.Parse(vyData[0]), double.Parse(vyData[1]), double.Parse(vyData[2]));
            var orig = new Point(double.Parse(origData[0]), double.Parse(origData[1]), double.Parse(origData[2]));
            var cs = new CoordinateSystem(orig, vx, vy); 
            
            if (type == GeometryType.BOX)
            {
                var parameters = attributes["para"].Value.Replace(".", ",").Split(' ');
                result = new BoxGeometry(cs, double.Parse(parameters[0]), double.Parse(parameters[1]), double.Parse(parameters[2]), isPositive);
            }
            else if (type == GeometryType.CYLI)
            {
                var parameters = attributes["para"].Value.Replace(".", ",").Split(' ');
                result = new CyliGeometry(cs, double.Parse(parameters[0]), double.Parse(parameters[1]), isPositive);
            }
            else if (type == GeometryType.CONE)
            {
                var parameters = attributes["para"].Value.Replace(".", ",").Split(' ');
                result = new ConeGeometry(cs, double.Parse(parameters[0]), double.Parse(parameters[1]),double.Parse(parameters[2]), isPositive);
            }
            else if (type == GeometryType.EXTR)
            {
                var parameters = attributes["para"].Value.Replace(".", ",").Split(' ');
                var points = new List<Position>();
                foreach (XmlNode pointNode in xmlNode.ChildNodes)
                {
                    if (pointNode.Name != "Point")
                        continue;

                    var pointAttributes = pointNode.Attributes;
                    var pointX = double.Parse(pointAttributes["x"].Value.Replace(".", ","));
                    var pointY = double.Parse(pointAttributes["y"].Value.Replace(".", ","));
                    var pointZ = double.Parse(pointAttributes["z"].Value.Replace(".", ","));
                    points.Add(Position.Create(pointX, pointY, pointZ));
                }

                result = new ExtrGeometry(cs, points, double.Parse(parameters[0]), isPositive);
            }
            else if (type == GeometryType.PYRA)
            {
                var parameters = attributes["para"].Value.Replace(".", ",").Split(' ');
                result = new PyraGeometry(cs, double.Parse(parameters[0]), double.Parse(parameters[1]), double.Parse(parameters[2]), double.Parse(parameters[3]), double.Parse(parameters[4]), isPositive);
            }
            else if (type == GeometryType.POLYHE)
            {
                var points = new List<Position>();
                var faces = new List<PolyHedronGeometry.PolyFace>();
                foreach (XmlNode node in xmlNode.ChildNodes)
                {
                    if (node.Name == "Point")
                    {
                        var pointAttributes = node.Attributes;
                        var pointX = double.Parse(pointAttributes["x"].Value.Replace(".", ","));
                        var pointY = double.Parse(pointAttributes["y"].Value.Replace(".", ","));
                        var pointZ = double.Parse(pointAttributes["z"].Value.Replace(".", ","));
                        points.Add(Position.Create(pointX, pointY, pointZ));
                    }
                    else if (node.Name == "Face")
                    {
                        var face = new PolyHedronGeometry.PolyFace
                        {
                            PointIndexList = node.InnerText.Split(' ').Select(x => int.Parse(x)).ToList()
                        };
                        faces.Add(face);
                    }
                }                

                result = new PolyHedronGeometry(cs, points, faces, isPositive);
            }
            return result;
        }

        internal void SetGeometryAttributes(DbElement item, DbQualifier wrtSiteQualifier)
        {
            var rootMatrixInverse = GetAvevaD3Transofrm(item.Owner, wrtSiteQualifier).Inverse();

            var position = TeklaImport.TransformPosition(CoordSystem.Origin).ConvertToPosition();
            var orientation = CoordSystem.AxisX.CreateOrientation(CoordSystem.AxisY);

            var currentOriginMatrix = CreateD3Transform(position, orientation);
            var currentMatrix = rootMatrixInverse.Multiply(currentOriginMatrix);
            var currentMatrixArray = currentMatrix.AsArray12();

            var ydir = Direction.Create(Position.Create(currentMatrixArray[3], currentMatrixArray[4], currentMatrixArray[5]));
            var zdir = Direction.Create(Position.Create(currentMatrixArray[6], currentMatrixArray[7], currentMatrixArray[8]));
            orientation = Orientation.Create($"Y is {ydir} and Z is {zdir}");
            position = Position.Create(currentMatrixArray[9], currentMatrixArray[10], currentMatrixArray[11]);
            item.SetAttributeIfChanged(DbAttributeInstance.POS, position);
            item.SetAttributeIfChanged(DbAttributeInstance.ORI, orientation);
            //if (item.GetPosition(DbAttributeInstance.POS, wrtQualifier) != position)
            //    item.SetAttribute(DbAttributeInstance.POS, wrtQualifier, position, out PdmsMessage floorError);

            //if (item.GetOrientation(DbAttributeInstance.ORI, wrtQualifier) != orientation)
            //    item.SetAttribute(DbAttributeInstance.ORI, wrtQualifier, orientation, out PdmsMessage floorError);

            if (GeoType == GeometryType.BOX && this is BoxGeometry boxGeometry)
            {
                item.SetAttributeIfChanged(DbAttributeInstance.XLEN, boxGeometry.Width);
                item.SetAttributeIfChanged(DbAttributeInstance.YLEN, boxGeometry.Length);
                item.SetAttributeIfChanged(DbAttributeInstance.ZLEN, boxGeometry.Height);
            }
            else if (GeoType == GeometryType.CYLI && this is CyliGeometry cyliGeometry)
            {
                item.SetAttributeIfChanged(DbAttributeInstance.DIAM, cyliGeometry.Diameter);
                item.SetAttributeIfChanged(DbAttributeInstance.HEIG, cyliGeometry.Height);
            }
            else if (GeoType == GeometryType.CONE && this is ConeGeometry coneGeometry)
            {
                item.SetAttributeIfChanged(DbAttributeInstance.DBOT, coneGeometry.BottomDiameter);
                item.SetAttributeIfChanged(DbAttributeInstance.DTOP, coneGeometry.TopDiameter);
                item.SetAttributeIfChanged(DbAttributeInstance.HEIG, coneGeometry.Height);
            }
            else if (GeoType == GeometryType.PYRA && this is PyraGeometry pyraGeometry)
            {
                item.SetAttributeIfChanged(DbAttributeInstance.XBOT, pyraGeometry.BottomX);
                item.SetAttributeIfChanged(DbAttributeInstance.YBOT, pyraGeometry.BottomY);
                item.SetAttributeIfChanged(DbAttributeInstance.XTOP, pyraGeometry.TopX);
                item.SetAttributeIfChanged(DbAttributeInstance.YTOP, pyraGeometry.TopY);
                item.SetAttributeIfChanged(DbAttributeInstance.HEIG, pyraGeometry.Height);
            }
            else if (GeoType == GeometryType.EXTR && this is ExtrGeometry extrGeometry)
            {
                item.SetAttributeIfChanged(DbAttributeInstance.HEIG, extrGeometry.Height);

                var loop = item.Members(DbElementTypeInstance.LOOP).FirstOrDefault();
                if (loop == null || loop.IsNull)
                {
                    loop = item.CreateLast(DbElementTypeInstance.LOOP);
                }

                //var itemMatrix = MatrixFactory.ToCoordinateSystem(orientation.ConvertToCoordinateSystem(position));

                var currentOriginMatrixInverse = currentOriginMatrix.Inverse();

                var vertArray = loop.Members(DbElementTypeInstance.VERTEX);
                var diff = vertArray.Length - extrGeometry.Points.Count;
                if (diff < 0)
                {
                    for (var i = Math.Abs(diff); i > 0; i--)
                    {
                        loop.CreateLast(DbElementTypeInstance.VERTEX);
                    }
                }
                else if (diff > 0)
                {
                    for (var i = 0; i < diff; i++)
                    {
                        vertArray[vertArray.Length - 1 - i].Delete();
                    }
                }

                vertArray = loop.Members(DbElementTypeInstance.VERTEX);

                for (var i = 0; i < vertArray.Length; i++)
                {
                    var vert = vertArray[i];
                    var vertexPos = TeklaImport.TransformPosition(extrGeometry.Points[i]);
                    var vertexD3Point = D3Point.Create(vertexPos.X, vertexPos.Y, vertexPos.Z);
                    vertexD3Point = currentOriginMatrixInverse.Multiply(vertexD3Point);
                    vertexPos = Position.Create(vertexD3Point.X, vertexD3Point.Y, vertexD3Point.Z);
                    vert.SetAttributeIfChanged(DbAttributeInstance.POS, vertexPos);
                    //if (vert.GetPosition(DbAttributeInstance.POS, wrtSiteQualifier) == vertexPos) continue;
                    //vert.SetAttribute(DbAttributeInstance.POS, itemMatrix.Transform(vertexPos.ConvertToPoint()).ConvertToPosition());
                }
            }
            else if (GeoType == GeometryType.POLYHE && this is PolyHedronGeometry polyHedronGeometry)
            {
                var polptlist = item.Members(DbElementTypeInstance.POLPTLIST).FirstOrDefault();
                if (polptlist == null || polptlist.IsNull)
                {
                    polptlist = item.CreateLast(DbElementTypeInstance.POLPTLIST);
                }

                var currentOriginMatrixInverse = currentOriginMatrix.Inverse();

                var pointArray = polptlist.Members(DbElementTypeInstance.POINT);
                var diff = pointArray.Length - polyHedronGeometry.Points.Count;
                if (diff < 0)
                {
                    for (var i = Math.Abs(diff); i > 0; i--)
                    {
                        polptlist.CreateLast(DbElementTypeInstance.POINT);
                    }
                }
                else if (diff > 0)
                {
                    for (var i = 0; i < diff; i++)
                    {
                        pointArray[pointArray.Length - 1 - i].Delete();
                    }
                }

                pointArray = polptlist.Members(DbElementTypeInstance.POINT);

                for (var i = 0; i < pointArray.Length; i++)
                {
                    var point = pointArray[i];
                    var pointPos = TeklaImport.TransformPosition(polyHedronGeometry.Points[i]);
                    var pointD3Point = D3Point.Create(pointPos.X, pointPos.Y, pointPos.Z);
                    pointD3Point = currentOriginMatrixInverse.Multiply(pointD3Point);
                    pointPos = Position.Create(pointD3Point.X, pointD3Point.Y, pointD3Point.Z);
                    point.SetAttributeIfChanged(DbAttributeInstance.POS, pointPos);
                }

                var faceArray = item.Members(DbElementTypeInstance.POLFACE);
                diff = faceArray.Length - polyHedronGeometry.PolyFaceList.Count;
                if (diff < 0)
                {
                    for (var i = Math.Abs(diff); i > 0; i--)
                    {
                        item.CreateLast(DbElementTypeInstance.POLFACE);
                    }
                }
                else if (diff > 0)
                {
                    for (var i = 0; i < diff; i++)
                    {
                        faceArray[faceArray.Length - 1 - i].Delete();
                    }
                }

                faceArray = item.Members(DbElementTypeInstance.POLFACE);
                for (var i = 0; i < faceArray.Length; i++)
                {
                    var faceItem = faceArray[i];
                    var polyFace = polyHedronGeometry.PolyFaceList[i];
                    var poloop = faceItem.Members(DbElementTypeInstance.POLOOP).FirstOrDefault();
                    if (poloop == null || poloop.IsNull)
                    {
                        poloop = faceItem.CreateLast(DbElementTypeInstance.POLOOP);
                    }

                    var loopts = poloop.Members(DbElementTypeInstance.LOOPTS).FirstOrDefault();
                    if (loopts == null || loopts.IsNull)
                    {
                        loopts = poloop.CreateLast(DbElementTypeInstance.LOOPTS);
                    }

                    var vxref = polyFace.PointIndexList.Select(x => polptlist.Member(x)).ToArray();
                    loopts.SetAttributeIfChanged(DbAttributeInstance.VXREF, vxref);
                }
            }
        }

        internal D3Transform GetAvevaD3Transofrm(DbElement item, DbQualifier wrtSiteQualifier)
        {
            var wrtSitePos = item.GetPosition(DbAttributeInstance.POS, wrtSiteQualifier);
            var wrtSiteOri = item.GetOrientation(DbAttributeInstance.ORI, wrtSiteQualifier);
            var xdir = wrtSiteOri.XDir();
            var ydir = wrtSiteOri.YDir();
            var zdir = wrtSiteOri.ZDir();
            var m = new[]
            {
                xdir.East, xdir.North, xdir.Up,
                ydir.East, ydir.North, ydir.Up,
                zdir.East, zdir.North, zdir.Up,
                wrtSitePos.X,  wrtSitePos.Y, wrtSitePos.Z
            };

            if (item.GetElementType() == DbElementTypeInstance.FLOOR && item.Members(DbElementTypeInstance.PLOOP).Length > 0)
            {
                var ploo = item.Members(DbElementTypeInstance.PLOOP).First();
                var sjus = ploo.GetAsString(DbAttributeInstance.SJUS).ToUpper();
                if (ploo.GetAsString(DbAttributeInstance.SJUS).ToUpper() == "UCEN")
                {
                    var height = ploo.GetDouble(DbAttributeInstance.HEIG);
                    var transform = D3Transform.Create(m);
                    var transformPoint = transform.Multiply(D3Point.Create(0, 0, -height / 2));
                    m[9] = transformPoint.X;
                    m[10] = transformPoint.Y;
                    m[11] = transformPoint.Z;
                }
                else if (sjus != "UNSET")
                {
                    Console.WriteLine($"{item} has SJUS {sjus}");
                }
            }

            return D3Transform.Create(m);
        }

        private D3Transform CreateD3Transform(Position pos, Orientation ori)
        {
            var vx = ori.XDir();
            var vy = ori.YDir();
            var vz = ori.ZDir();
            var matrix = D3Transform.Create(new double[] {
                vx.East,vx.North,vx.Up,
                vy.East,vy.North,vy.Up,
                vz.East,vz.North,vz.Up,
                pos.X,pos.Y,pos.Z
            });

            return matrix;
        }
    }

    public class CyliGeometry : AvevaGeometry
    {
        public double Diameter { get; }
        public double Height { get; }

        public CyliGeometry(CoordinateSystem cs, double d, double h, bool isPositive)
        {
            CoordSystem = cs;
            Diameter = d;
            Height = h;
            GeoType = GeometryType.CYLI;
            AvevaElementType = isPositive ? DbElementTypeInstance.CYLINDER : DbElementTypeInstance.NCYLINDER;
        }
    }

    public class ConeGeometry : AvevaGeometry
    {
        public double BottomDiameter { get; }
        public double TopDiameter { get; }
        public double Height { get; }

        public ConeGeometry(CoordinateSystem cs, double d1, double d2, double h, bool isPositive)
        {
            CoordSystem = cs;
            BottomDiameter = d1;
            TopDiameter = d2;
            Height = h;
            GeoType = GeometryType.CONE;
            AvevaElementType = isPositive ? DbElementTypeInstance.CONE : DbElementTypeInstance.NCONE;
        }
    }

    public class BoxGeometry : AvevaGeometry
    {
        public double Width { get; }
        public double Length { get; }
        public double Height { get; }

        public BoxGeometry(CoordinateSystem cs, double w, double l, double h, bool isPositive)
        {
            CoordSystem = cs;
            Width = w;
            Length = l;
            Height = h;
            GeoType = GeometryType.BOX;
            AvevaElementType = isPositive ? DbElementTypeInstance.BOX : DbElementTypeInstance.NBOX;
        }
    }

    public class PyraGeometry : AvevaGeometry
    {
        public double BottomX { get; }
        public double BottomY { get; }
        public double TopX { get; }
        public double TopY { get; }
        public double Height { get; }

        public PyraGeometry(CoordinateSystem cs, double bx, double by, double tx, double ty, double h, bool isPositive)
        {
            CoordSystem = cs;
            BottomX = bx;
            BottomY = by;
            TopX = tx;
            TopY = ty;
            Height = h;
            GeoType = GeometryType.PYRA;
            AvevaElementType = isPositive ? DbElementTypeInstance.PYRAMID : DbElementTypeInstance.NPYRAMID;
        }
    }

    public class ExtrGeometry : AvevaGeometry
    {
        public List<Position> Points { get; }
        public double Height { get; }

        public ExtrGeometry(CoordinateSystem cs, List<Position> pl, double h, bool isPositive)
        {
            CoordSystem = cs;
            Points = pl;
            Height = h;
            GeoType = GeometryType.EXTR;
            AvevaElementType = isPositive ? DbElementTypeInstance.EXTRUSION : DbElementTypeInstance.NXTRUSION;
        }
    }

    public class PolyHedronGeometry : AvevaGeometry
    {
        public class PolyFace
        {
            public List<int> PointIndexList;
            public PolyFace()
            {
                PointIndexList = new List<int>();
            }
        }

        public List<Position> Points { get; }
        public List<PolyFace> PolyFaceList;

        public PolyHedronGeometry(CoordinateSystem cs, List<Position> pl, List<PolyFace> f, bool isPositive)
        {
            CoordSystem = cs;
            Points = pl;
            PolyFaceList = f;
            GeoType = GeometryType.POLYHE;
            AvevaElementType = isPositive ? DbElementTypeInstance.POLYHEDRON : DbElementTypeInstance.NPOLYHEDRON;
        }
    }
}
