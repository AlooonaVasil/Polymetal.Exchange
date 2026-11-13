using System.Xml.Serialization;

namespace Polymetal.Exchange.Utilities.DTO
{
    public class Property
    {
        [XmlAttribute("name")] public string Name { get; set; }

        [XmlAttribute("value")] public string Value { get; set; }

        [XmlAttribute("isattribute")] public bool IsAttribute { get; set; }

        public bool ShouldSerializeIsAttribute() => IsAttribute;

        public Property()
        {
        }

        public Property(string name, string value, bool isAttribute)
        {
            Name = name;
            Value = value;
            IsAttribute = isAttribute;
        }
    }
}