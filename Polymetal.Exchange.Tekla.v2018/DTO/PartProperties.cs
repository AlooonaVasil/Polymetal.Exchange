using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Serialization;
using Tekla.Structures.Catalogs;
using Tekla.Structures.Model;

namespace Polymetal.Exchange.Tekla.v2018.DTO
{
    [XmlRoot("Properties")]
    public class PartProperties
    {
        private static XmlSerializer _xmlFormat = new XmlSerializer(typeof(PartProperties));
        private static XmlSerializerNamespaces _xmlSerializerNamespaces = new XmlSerializerNamespaces(new[] { XmlQualifiedName.Empty });
        private static XmlWriterSettings _xmlWriterSettings = new XmlWriterSettings { Indent = false, OmitXmlDeclaration = true };

        private static ArrayList _propertyNamesList;

        private static ArrayList PropertyNamesList
        {
            get
            {
                if (_propertyNamesList == null)
                {
                    _propertyNamesList = new ArrayList { "WEIGHT", "VOLUME_NET", "AREA_NET" };
                }

                return _propertyNamesList;
            }
        }

        private static Dictionary<string, MaterialItem.MaterialItemTypeEnum> _materialTypesDictionary;

        private static Dictionary<string, MaterialItem.MaterialItemTypeEnum> MaterialTypesDictionary
        {
            get
            {
                if (_materialTypesDictionary == null)
                {
                    _materialTypesDictionary = new Dictionary<string, MaterialItem.MaterialItemTypeEnum>();

                    var materialsEnum = new CatalogHandler().GetMaterialItems();
                    while (materialsEnum.MoveNext())
                    {
                        _materialTypesDictionary.Add(materialsEnum.Current.MaterialName, materialsEnum.Current.Type);
                    }
                }

                return _materialTypesDictionary;
            }
        }

        /// <summary>
        /// Масса детали (кг)
        /// </summary>
        [XmlElement] public double Weight { get; set; }
        public bool ShouldSerializeWeight() => Weight != 0.0;

        /// <summary>
        /// Площадь детали (мм2)
        /// </summary>
        [XmlElement] public double Area { get; set; }

        public bool ShouldSerializeArea() => Area != 0.0;

        /// <summary>
        /// Объем детали (мм3)
        /// </summary>
        [XmlElement] public double Volume { get; set; }
        public bool ShouldSerializeVolume() => Volume != 0.0;

        /// <summary>
        /// Имя материала детали
        /// </summary>
        [XmlElement] public string MaterialName { get; set; }
        public bool ShouldSerializeMaterialName() => !string.IsNullOrEmpty(MaterialName);

        /// <summary>
        /// Тип материала детали
        /// </summary>
        [XmlElement] public string MaterialType { get; set; }
        public bool ShouldSerializeMaterialType() => !string.IsNullOrEmpty(MaterialType);

        /// <summary>
        /// Стадия детали
        /// </summary>
        [XmlElement] public string Phase { get; set; }
        public bool ShouldSerializePhase() => !string.IsNullOrEmpty(Phase);

        public PartProperties()
        {
        }

        public PartProperties(Part part)
        {
            var hashtable = new Hashtable();
            if (part.GetDoubleReportProperties(PropertyNamesList, ref hashtable))
            {
                var weight = hashtable["WEIGHT"];
                if (weight != null)
                    Weight = Math.Round((double)weight, 3);

                var area = hashtable["AREA_NET"];
                if (area != null)
                    Area = Math.Round((double)area);

                var volume = hashtable["VOLUME_NET"];
                if (volume != null)
                    Volume = Math.Round((double)volume);
            }

            var materialName = part.Material.MaterialString;
            MaterialName = materialName;
            MaterialType = MaterialTypesDictionary.ContainsKey(materialName)
                ? MaterialTypesDictionary[materialName].ToString()
                : string.Empty;

            Phase = part.GetPhase(out var phase) ? phase.PhaseName : string.Empty;
            Phase = SanitizeForXml(Phase);
        }

        public string GetSerializedString()
        {
            string serializedProperties;
            using (var stream = new StringWriter())
            {
                using (var xmlWriter = XmlWriter.Create(stream, _xmlWriterSettings))
                {
                    _xmlFormat.Serialize(xmlWriter, this, _xmlSerializerNamespaces);
                    serializedProperties = stream.ToString();
                }
            }

            return serializedProperties;
        }

        private string SanitizeForXml(string input)
        {
            return string.IsNullOrEmpty(input)
                ? input
                : Regex.Replace(input, @"[\x00-\x08\x0B\x0C\x0E-\x1F\x7F-\x9F]", string.Empty);
        }
    }
}
