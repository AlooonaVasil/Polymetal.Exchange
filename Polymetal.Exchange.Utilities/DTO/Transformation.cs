using System.Xml.Serialization;

namespace Polymetal.Exchange.Utilities.DTO
{
    public class Transformation
    {
        [XmlElement("VectorX")] public Point VectorX;
        [XmlElement("VectorY")] public Point VectorY;
        [XmlElement("VectorZ")] public Point VectorZ;
        [XmlElement("Origin")] public Point Origin;

        public Transformation()
        {
        }

        public Transformation(double[] transformationArray)
        {
            VectorX = new Point(transformationArray[0], transformationArray[1], transformationArray[2]);
            VectorY = new Point(transformationArray[3], transformationArray[4], transformationArray[5]);
            VectorZ = new Point(transformationArray[6], transformationArray[7], transformationArray[8]);
            Origin = new Point(transformationArray[9], transformationArray[10], transformationArray[11]);
        }
    }
}