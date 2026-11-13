using System.Xml.Serialization;

namespace Polymetal.Exchange.Utilities.DTO
{
    public class Point
    {
        [XmlAttribute("x")] public double X;
        [XmlAttribute("y")] public double Y;
        [XmlAttribute("z")] public double Z;

        public Point()
        {
        }

        public Point(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }
    }
}
