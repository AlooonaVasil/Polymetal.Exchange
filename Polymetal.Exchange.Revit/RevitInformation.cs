using System.Collections.Generic;
using Xbim.Common.Geometry;

namespace Polymetal.Exchange.Revit
{
    public class RevitElement
    {
        public string Id;
        public RepresentationType RepresentationType;
        public List<Representation> Representations;
        public List<RevitElement> FillElements;
        public RevitProperties Properties;
    }

    public enum RepresentationType
    {
        Brep,
        Solid
    }

    public enum ElementType
    {
        Wall,
        Door,
        Window,
        Roof,
        Slab,
        Beam
    }

    public class RevitProperties
    {
        public double Elev;
        public string Func;
        public string Desc;
        public ElementType Type;
    }

    public class Representation
    {
        public Geometry Positive;
        public List<Geometry> Negativ;
    }

    public class Geometry
    {
        public XbimVector3D Normal;
        public double Length;
    }

    public class Circle : Geometry
    {
        public double Radius;
        public XbimPoint3D Position;
    }

    public class Extrusion : Geometry
    {
        public List<XbimPoint3D> Points;
    }
}
