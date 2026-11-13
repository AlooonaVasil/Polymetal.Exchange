using System.Xml.Serialization;

namespace Polymetal.Exchange.Utilities.DTO
{
    public class Connector
    {
        /// <summary>
        /// Направление коннектора
        /// </summary>
        [XmlElement("Direction")] public Point Direction { get; set; }

        /// <summary>
        /// Позиция коннектора
        /// </summary>
        [XmlElement("Origin")] public Point Origin { get; set; }

        /// <summary>
        /// Описание соединителя
        /// </summary>
        [XmlAttribute("description")] public string Description { get; set; }

        public Connector()
        {
        }
    }
}