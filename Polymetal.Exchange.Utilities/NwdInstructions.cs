using System.Collections.Generic;
using System.IO;
using System.Xml;
using System.Xml.Serialization;

namespace Polymetal.Exchange.Utilities
{
    public class NwdInstructions
    {
        public string NwdPath;
        public List<string> NwdFiles;
        public List<string> HiddenElements;
        public double[] Transform;
        private XmlSerializer _xmlFormat = new XmlSerializer(typeof (NwdInstructions));

        public NwdInstructions()
        {
            NwdFiles = new List<string>();
            HiddenElements = new List<string>();
        }

        public NwdInstructions(string input)
        {
            using (var stream = new StringReader(input))
            {
                var obj = (NwdInstructions)_xmlFormat.Deserialize(stream);
                this.NwdPath = obj.NwdPath;
                this.HiddenElements = obj.HiddenElements;
                this.NwdFiles = obj.NwdFiles;
                this.Transform = obj.Transform;
                stream.Close();
            }
        }

        public string Serialize()
        {
            string result;
            using (var stream = new StringWriter())
            {
                using (var xmlWriter = XmlWriter.Create(stream, new XmlWriterSettings { Indent = false}))
                {
                    _xmlFormat.Serialize(xmlWriter, this);
                }
                result = stream.ToString();
            }

            return result;
        }
    }
}
