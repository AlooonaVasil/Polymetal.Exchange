using System.Xml.Serialization;

namespace Polymetal.Exchange.Utilities.DTO
{
    public class Geometry
    {
        [XmlAttribute("type")]
        public Helpers.Enumerators.GeometryType Type;

        [XmlAttribute("color")]
        public string ColorARGB;

        [XmlAttribute("negative")]
        public bool IsNegative;

        public bool ShouldSerializeIsNegative() => IsNegative;

        [XmlText]
        public string Data;
    }
}
