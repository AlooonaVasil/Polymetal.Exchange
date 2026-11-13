using System.Collections.Generic;
using System.Xml.Serialization;

namespace Polymetal.Exchange.Utilities.DTO
{
    [XmlType(TypeName = "Information")]
    public class IfcElementInformation
    {
        [XmlAttribute("id")] public string Id;
        public bool ShouldSerializeId() => !string.IsNullOrEmpty(Id);

        [XmlAttribute("uniqueid")] public string UniqueId;
        public bool ShouldSerializeUniqueId() => !string.IsNullOrEmpty(UniqueId);

        [XmlAttribute("disable")] public bool IsDisabled;
        public bool ShouldSerializeIsDisabled() => IsDisabled;

        [XmlAttribute("type")] public Helpers.Enumerators.ElementType Type;

        [XmlElement("Location")] public Point Location;

        public bool ShouldSerializeLocation() => Location != null;

        [XmlArray("Geometries")] [XmlArrayItem("Geometry")]
        public List<Geometry> Geometries;

        [XmlArray("Properties")] [XmlArrayItem("Property")]
        public List<Property> Properties;

        public bool ShouldSerializeProperties() => Properties != null && Properties.Count > 0;

        [XmlArray("AttachedItems")] [XmlArrayItem("AttachedItem")]
        public List<string> AttachedItems;

        public bool ShouldSerializeAttachedItems() => AttachedItems != null && AttachedItems.Count > 0;

        [XmlArray("Connectors")] [XmlArrayItem("Connector")]
        public List<Connector> Connectors;

        public bool ShouldSerializeConnectors() => Connectors != null && Connectors.Count > 0;

        [XmlArray("HatchLines")] [XmlArrayItem("HatchLine")]
        public List<HatchLine> HatchLines;

        public bool ShouldSerializeHatchLines() => HatchLines != null && HatchLines.Count > 0;

        [XmlArray("Children")] [XmlArrayItem("Child")]
        public List<IfcElementInformation> Children;

        public bool ShouldSerializeChildren() => Children != null && Children.Count > 0;

        public void AddProperty(string name, object value, bool isAttribute = false)
        {
            var stringValue = value.ToString().Trim();
            if (string.IsNullOrEmpty(stringValue))
                return;

            if (Properties == null)
            {
                Properties = new List<Property>();
            }

            Properties.Add(new Property(name, stringValue, isAttribute));
        }

        public void AddAttachedItems(List<string> itemUniqueIdList)
        {
            if (itemUniqueIdList.Count == 0)
                return;

            if (AttachedItems == null)
            {
                AttachedItems = new List<string>();
            }

            AttachedItems.AddRange(itemUniqueIdList);
            AttachedItems.Sort();
        }

        public void AddGeometry(Geometry geometry)
        {
            if (Geometries == null)
            {
                Geometries = new List<Geometry>();
            }

            Geometries.Add(geometry);
        }

        public void AddChild(IfcElementInformation child)
        {
            if (Children == null)
            {
                Children = new List<IfcElementInformation>();
            }

            Children.Add(child);
        }

        public IfcElementInformation Clone()
        {
            var result = new IfcElementInformation
            {
                Id = this.Id,
                UniqueId = this.UniqueId,
                IsDisabled = this.IsDisabled,
                Type = this.Type
            };

            if (this.Geometries != null)
            {
                result.Geometries = new List<Geometry>();
                result.Geometries.AddRange(this.Geometries);
            }

            if (this.Properties != null)
            {
                result.Properties = new List<Property>();
                result.Properties.AddRange(this.Properties);
            }

            if (this.AttachedItems != null)
            {
                result.AttachedItems = new List<string>();
                result.AttachedItems = this.AttachedItems;
            }

            if (this.Children != null)
            {
                result.Children = new List<IfcElementInformation>();
                result.Children.AddRange(this.Children);
            }

            if (this.Connectors != null)
            {
                result.Connectors = new List<Connector>();
                result.Connectors.AddRange(this.Connectors);
            }

            if (this.HatchLines != null)
            {
                result.HatchLines = new List<HatchLine>();
                result.HatchLines.AddRange(this.HatchLines);
            }

            return result;
        }
    }
}