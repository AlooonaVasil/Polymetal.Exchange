using System.Xml.Serialization;

namespace Polymetal.Exchange.Utilities.DTO
{
    public class HatchLine
    {
        /// <summary>
        /// Позиция начала
        /// </summary>
        [XmlElement("Start")] public Point Start { get; set; }

        /// <summary>
        /// Позиция начала
        /// </summary>
        [XmlElement("End")] public Point End { get; set; }

        public HatchLine()
        {
        }

        public HatchLine(Point start, Point end)
        {
            this.Start = start;
            this.End = end;
        }
    }
}