using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Xbim.Common.Geometry;
using Xbim.Ifc;
using Xbim.Ifc2x3.ProductExtension;
using Xbim.IO;
using System.IO.Pipes;

namespace Polymetal.Pdms.IfcServer
{
    internal class Program
    {
        private static IfcStore _modelStore;
        private static string _ifcPath;
        internal static Dictionary<int, object[]> LayersDictionary;

        private static int Main(string[] args)
        {
            Thread.CurrentThread.CurrentCulture = CultureInfo.CreateSpecificCulture("en-GB");
            InitLayersDictionary();

            var server = new NamedPipeServerStream($"IfcPdmsPipe{args[0]}", PipeDirection.InOut);
            server.WaitForConnection();

            //var reader = new StreamReader(server);
            //var writer = new StreamWriter(server);
            using (var reader = new StreamReader(server))
            {
                var working = true;
                IfcSite currentSite = null;
                var name = string.Empty;
                var type = string.Empty;
                string[] matrixData;
                var geometryLines = new List<string>();
                var properties = new Dictionary<string, string>();
                while (working)
                {
                    var line = reader.ReadLine();
                    switch (line)
                    {
                        case "SAVE MODEL":
                            _ifcPath = reader.ReadLine();
                            SaveModel();
                            Console.WriteLine("ifc done\t{0}", _ifcPath);
                            break;
                        case "CREATE MODEL":
                            EntityCreator.Init();
                            _modelStore = EntityCreator.CreateModel();
                            break;
                        case "CREATE PROJECT":
                            using (var tr = _modelStore.BeginTransaction("Initialise Project"))
                            {
                                EntityCreator.CreateProject();
                                tr.Commit();
                            }
                            break;
                        case "CREATE SITE":
                            var siteName = reader.ReadLine();
                            using (var tr = _modelStore.BeginTransaction("Initialise Site " + siteName))
                            {
                                currentSite = EntityCreator.CreateSite(siteName);
                                tr.Commit();
                            }
                            break;
                        case "CREATE PRODUCT":
                            name = reader.ReadLine();
                            type = reader.ReadLine();
                            properties=new Dictionary<string, string>();
                            geometryLines = new List<string>();
                            break;
                        case "GEOMETRY":
                            line = reader.ReadLine();
                            if (line == "GEOMETRY_END") break;
                            matrixData = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                            Representation.RootMatrix = new XbimMatrix3D(
                                double.Parse(matrixData[0]), double.Parse(matrixData[1]),
                                double.Parse(matrixData[2]), 0.0,
                                double.Parse(matrixData[3]), double.Parse(matrixData[4]),
                                double.Parse(matrixData[5]), 0.0,
                                double.Parse(matrixData[6]), double.Parse(matrixData[7]),
                                double.Parse(matrixData[8]), 0.0,
                                double.Parse(matrixData[9]), double.Parse(matrixData[10]),
                                double.Parse(matrixData[11]), 1.0);
                            while ((line = reader.ReadLine()) != "GEOMETRY_END")
                            {
                                geometryLines.Add(line);
                            }
                            break;
                        case "PROPERTIES":
                            while ((line = reader.ReadLine()) != "PROPERTIES_END")
                            {
                                var data = line.Split(new []{'$'}, StringSplitOptions.RemoveEmptyEntries);
                                properties.Add(data.First(), data.Last());
                            }
                            break;
                        case "END":
                            if (geometryLines.Count <= 0) break;
                            using (var tr = _modelStore.BeginTransaction("Initialise Product"))
                            {
                                var layerName = string.Empty;
                                //var mappedRepresentation = Representation.CreateMappedRepresentation(geometryLines, ref layerName);
                                //if (mappedRepresentation != null)
                                //{
                                //    EntityCreator.CreatePresentationLayerAssignment(layerName, mappedRepresentation);
                                //    var product = EntityCreator.CreateProduct(name, type, mappedRepresentation, currentSite);
                                //    tr.Commit();
                                //}
                                var shapeRepresentation = Representation.GetShapeRepresentation(geometryLines,ref layerName);
                                if (shapeRepresentation != null)
                                {
                                    EntityCreator.CreatePresentationLayerAssignment(layerName,
                                        shapeRepresentation);
                                    var product = EntityCreator.CreateProduct(name, type, shapeRepresentation,
                                        currentSite, properties);
                                    tr.Commit();
                                }
                            }
                            break;
                        case "CLOSE":
                            working = false;
                            break;
                        default:
                            ProcessLine(line);
                            break;
                    }
                }
                Thread.Sleep(1000);
                server.Close();
            }
            return 1;
        }

        private static void InitLayersDictionary()
        {
            var rgbIndexDictionary = ColorHelper.GetPdmsColors();
            LayersDictionary = new Dictionary<int, object[]>();
            LayersDictionary.Add(0, new object[] { "Прочее", rgbIndexDictionary[0] });
            using (
                var stream =
                    System.Reflection.Assembly.GetExecutingAssembly()
                        .GetManifestResourceStream("Polymetal.Pdms.IfcServer.Resources.Materials.xml"))
            {
                using (var sr = new StreamReader(stream))
                {
                    string line;
                    while ((line = sr.ReadLine()) != null)
                    {
                        if (!line.Contains("MaterialName")) continue;
                        var data = line.Split(new[] {'\"'}, StringSplitOptions.RemoveEmptyEntries);
                        var materialName = data[1];
                        var layerName = data[3];
                        var index = int.Parse(materialName.ToLower().Replace("material", string.Empty));
                        if (LayersDictionary.ContainsKey(index) || !rgbIndexDictionary.ContainsKey(index)) continue;
                        LayersDictionary.Add(index, new object[] {layerName, rgbIndexDictionary[index]});
                    }
                    sr.Close();
                }
            }
        }

        private static void ProcessLine(string line)
        {
            Console.WriteLine("ProcessLine\t{0}", line);
        }

        private static void SaveModel()
        {
            var fileInfo = new FileInfo(_ifcPath);
            if (!fileInfo.Directory.Exists)
            {
                fileInfo.Directory.Create();
            }
            _modelStore.SaveAs(_ifcPath, IfcStorageType.Ifc);
            _modelStore.Close();
            _modelStore.Dispose();
        }
    }
}
