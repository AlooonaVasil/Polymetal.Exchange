using Ionic.Zlib;
using Polymetal.Exchange.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Media3D;
using System.Xml;
using System.Xml.Serialization;
using Polymetal.Exchange.Utilities.Helpers;
using Xbim.Common;
using Xbim.Common.Geometry;
using Xbim.Common.XbimExtensions;
using Xbim.Ifc;
using Xbim.Ifc2x3.Extensions;
using Xbim.Ifc2x3.GeometricModelResource;
using Xbim.Ifc2x3.GeometryResource;
using Xbim.Ifc2x3.Interfaces;
using Xbim.Ifc2x3.ProductExtension;
using Xbim.Ifc2x3.ProfileResource;
using Xbim.Ifc2x3.SharedBldgElements;
using Xbim.IO.Step21;
using Xbim.ModelGeometry.Scene;
using Xbim.Presentation;

namespace Polymetal.Exchange.Geometry
{
    public class RevitIfcParser
    {
        private static Dictionary<XbimPoint3D, int> _points;
        private static Dictionary<int, XbimPoint3D> _pointsInverse;
        private static Dictionary<XbimVector3D, int> _normals;
        private static Dictionary<int, XbimVector3D> _normalsInverse;
        private static List<int[]> _faces;
        private static Dictionary<IfcElement, List<DTO.MeshInfo>> _solids;
        private static XbimMatrix3D _modelMatrix3D;

        public static void Main()
        {
            AppDomain currentDomain = AppDomain.CurrentDomain;
            currentDomain.AssemblyResolve += CurrentDomain_AssemblyResolve;
            var path = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            //var res = Run(@"C:\AVEVA\temp\PandC\Plant 3D Models\3D Model\3D Binder Area Sub Assembly.ifc", Common.Source.Unknown);
            var res = Run(@"F:\81 02 07 211 00 - АР Сил 1-21.ifc", Common.Source.Revit, new double[0]);
        }

        /// <summary>
        /// Загрузка библиотек из папки в случае необходимости
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        /// <returns></returns>
        /// <exception cref="NotImplementedException"></exception>
        private static Assembly CurrentDomain_AssemblyResolve(object sender, ResolveEventArgs args)
        {
            var folderPath = @"C:\AVEVA\Exchange";
            var assemblyPath = Path.Combine(folderPath, new AssemblyName(args.Name).Name + ".dll");
            if (!File.Exists(assemblyPath))
                return null;

            var assembly = Assembly.LoadFrom(assemblyPath);
            return assembly;
        }

        public static List<Utilities.DTO.IfcElementInformation> Run(string path, Common.Source source, double[] matrix)
        {
            _modelMatrix3D = new XbimMatrix3D();
            if (matrix.Length == 12)
            {
                _modelMatrix3D = new XbimMatrix3D(
                    matrix[0], matrix[1], matrix[2], 0,
                    matrix[3], matrix[4], matrix[5], 0,
                    matrix[6], matrix[7], matrix[8], 0,
                    matrix[9], matrix[10], matrix[11], 1);
            }

            _points = new Dictionary<XbimPoint3D, int>();
            _normals = new Dictionary<XbimVector3D, int>();
            _faces = new List<int[]>();
            _solids = new Dictionary<IfcElement, List<DTO.MeshInfo>>();

            switch (source)
            {
                case Common.Source.Revit:
                    return GetArchitectureInfo(path);
                case Common.Source.MagiCAD:
                    return GetDuctInfo(path);
                case Common.Source.Unknown:
                    return GetUnknownInfo(path);
                default:
                    return new List<Utilities.DTO.IfcElementInformation>();
            }
        }

        /// <summary>
        /// Получение атрибутивной и геометрической информации по архитектурным элементам
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        private static List<Utilities.DTO.IfcElementInformation> GetArchitectureInfo(string path)
        {
            var ifcDictionary = new Dictionary<IfcElement, Utilities.DTO.IfcElementInformation>();
            Console.WriteLine("opening ifc");
            var model = IfcStore.Open(path);
            if (model.GeometryStore.IsEmpty)
            {
                var context = new Xbim3DModelContext(model);
                context.CreateContext();
            }

            var elementInformationList = DeserializeInformation(path.Replace(".ifc", ".info"));

            var collection = model.Instances.OfType<IfcElement>().ToList();

            if (elementInformationList.Any(x => x.HatchLines != null && x.HatchLines.Count > 0))
            {
                Helpers.HatchingGenerator.ModifyRepresentation(model, collection, elementInformationList);

                var context = new Xbim3DModelContext(model);
                context.CreateContext();
            }

            var doneItems = new Dictionary<int, bool>();

            var progressCurrent = 0;
            var progressTotal = collection.Count;

            foreach (var ifcElement in collection)
            {
                Console.Write($"\ranalysis: {++progressCurrent}/{progressTotal}");

                if (ifcElement is IfcOpeningElement)
                    continue;

                if (doneItems.ContainsKey(ifcElement.EntityLabel))
                    continue;

                doneItems.Add(ifcElement.EntityLabel, true);

                var elementInformation = elementInformationList.FirstOrDefault(x => x.Id == ifcElement.Tag.ToString());
                if (elementInformation == null)
                {
                    elementInformation = new Utilities.DTO.IfcElementInformation
                    {
                        UniqueId = CalculateMD5Hash(ifcElement.GlobalId)
                    };
                }
                else if (ifcElement.FriendlyName.Contains(ifcElement.Tag.ToString()))
                {
                    var extraNumber = ifcElement.FriendlyName
                        .Substring(ifcElement.FriendlyName.IndexOf(ifcElement.Tag.ToString()))
                        .Replace(ifcElement.Tag.ToString(), string.Empty);
                    if (!string.IsNullOrEmpty(extraNumber))
                    {
                        elementInformation = elementInformation.Clone();
                        elementInformation.UniqueId += extraNumber;
                    }
                }

                ifcDictionary.Add(ifcElement, elementInformation);

                foreach (var relDecomposes in ifcElement.IsDecomposedBy)
                {
                    foreach (IfcElement child in relDecomposes.RelatedObjects)
                    {
                        if (!doneItems.ContainsKey(child.EntityLabel))
                            doneItems.Add(child.EntityLabel, true);

                        //если IfcRailing, то добавляем отдельным элементом
                        if (child is IfcRailing ifcRailing)
                        {
                            if (ifcDictionary.ContainsKey(ifcRailing))
                                continue;

                            var childInformation = elementInformationList.FirstOrDefault(x => x.Id == child.Tag.ToString());
                            if (childInformation == null)
                            {
                                childInformation = new Utilities.DTO.IfcElementInformation
                                {
                                    UniqueId = child.GlobalId.ToString()
                                };
                            }

                            ifcDictionary.Add(child, childInformation);
                        }
                        else if (ifcDictionary.ContainsKey(child))
                        {
                            //если добавлен отдельным элементом, то убираем из общего списка
                            ifcDictionary.Remove(child);
                        }
                    }
                }
            }

            Console.WriteLine();

            //больше не нужен
            doneItems.Clear();

            //обработка геометрии элементов
            var meshDictionary = new Dictionary<IfcElement, List<DTO.MeshInfo>>();
            var relationDictionary = new Dictionary<IfcElement, List<IfcElement>>();

            var ifcDictionaryKeys = ifcDictionary.Keys.ToList();

            progressCurrent = 0;
            progressTotal = ifcDictionaryKeys.Count;

            foreach (var ifcElement in ifcDictionaryKeys)
            {
                Console.Write($"\rgeometry: {++progressCurrent}/{progressTotal}");

                var elementInformation = ifcDictionary[ifcElement];
                if (elementInformation.IsDisabled)
                {
                    ifcDictionary.Remove(ifcElement);
                    continue;
                }

                var functionProperty = elementInformation.Properties?.FirstOrDefault(x => x.Name == "function");
                elementInformation.Type = functionProperty == null
                    ? GetElementType(ifcElement)
                    : GetElementType(ifcElement, functionProperty.Value);

                //выгрузка всех элементов в виде genpri
                var meshes = GetMeshGeometry(model, ifcElement);
                if (meshes.Count > 0)
                    meshDictionary.Add(ifcElement, meshes);

                foreach (var relDecomposes in ifcElement.IsDecomposedBy)
                {
                    foreach (var relatedObject in relDecomposes.RelatedObjects)
                    {
                        if (!(relatedObject is IfcElement ifcElementChild))
                            continue;

                        //если элемент уже добавлен отдельной позицией
                        if (ifcDictionary.ContainsKey(ifcElementChild))
                            continue;

                        var childMeshes = GetMeshGeometry(model, ifcElementChild);
                        if (childMeshes.Count <= 0)
                            continue;

                        meshDictionary.Add(ifcElementChild, childMeshes);

                        if (!relationDictionary.ContainsKey(ifcElement))
                            relationDictionary.Add(ifcElement, new List<IfcElement>());

                        relationDictionary[ifcElement].Add(ifcElementChild);
                    }
                }
            }

            Console.WriteLine();

            _pointsInverse = _points.ToDictionary(pair => pair.Value, pair => pair.Key);
            _normalsInverse = _normals.ToDictionary(pair => pair.Value, pair => pair.Key);

            ifcDictionaryKeys = ifcDictionary.Keys.ToList();

            progressCurrent = 0;
            progressTotal = ifcDictionaryKeys.Count;

            //заполнение информации данными о геометрии
            foreach (var ifcElement in ifcDictionaryKeys)
            {
                Console.Write($"\rupdating info: {++progressCurrent}/{progressTotal}");
                var elementInformation = ifcDictionary[ifcElement];
                if (meshDictionary.ContainsKey(ifcElement))
                {
                    var meshes = meshDictionary[ifcElement];
                    foreach (var mesh in meshes)
                    {
                        FixMesh(mesh);
                    }

                    var materialDict = DevideMeshesByMaterial(meshes, ifcElement is IfcWindow);
                    foreach (var materialPair in materialDict)
                    {
                        var bytes = GetEldata(materialPair.Value);
                        var geometry = new Utilities.DTO.Geometry
                        {
                            Type = Utilities.Helpers.Enumerators.GeometryType.Mesh,
                            Data = GeometryDataToString(bytes),
                            ColorARGB = materialPair.Key
                        };

                        elementInformation.AddGeometry(geometry);
                    }

                    meshDictionary.Remove(ifcElement);
                }

                //если нет дочерних элементов
                if (!relationDictionary.ContainsKey(ifcElement))
                    continue;

                //если нет собственной геометрии, то добавляется гоеметрия дочерних элементов
                if (elementInformation.Geometries?.Count(x => !x.IsNegative) == 0)
                {
                    var childMeshesList = new List<DTO.MeshInfo>();
                    foreach (var child in relationDictionary[ifcElement])
                    {
                        if (!meshDictionary.ContainsKey(child))
                            continue;

                        var childMeshes = meshDictionary[child];
                        foreach (var mesh in childMeshes)
                        {
                            FixMesh(mesh);
                        }

                        childMeshesList.AddRange(childMeshes);
                    }

                    var materialDict = DevideMeshesByMaterial(childMeshesList, ifcElement is IfcCurtainWall);
                    foreach (var materialPair in materialDict)
                    {
                        var bytes = GetEldata(materialPair.Value);
                        var geometry = new Utilities.DTO.Geometry()
                        {
                            Type = Utilities.Helpers.Enumerators.GeometryType.Mesh,
                            Data = GeometryDataToString(bytes),
                            ColorARGB = materialPair.Key
                        };

                        elementInformation.AddGeometry(geometry);
                    }

                    continue;
                }

                foreach (var child in relationDictionary[ifcElement])
                {
                    if (!meshDictionary.ContainsKey(child))
                        continue;

                    var childInformation = ifcDictionary.ContainsKey(child)
                        ? ifcDictionary[child]
                        : elementInformationList.FirstOrDefault(x => x.Id == child.Tag.ToString());
                    if (childInformation == null)
                    {
                        childInformation = new Utilities.DTO.IfcElementInformation
                        {
                            UniqueId = child.GlobalId
                        };
                    }

                    if (childInformation.IsDisabled)
                        continue;

                    var childMeshes = meshDictionary[child];
                    foreach (var mesh in childMeshes)
                    {
                        FixMesh(mesh);
                    }

                    var materialDict =
                        DevideMeshesByMaterial(childMeshes, child is IfcCurtainWall || child is IfcWindow);
                    foreach (var materialPair in materialDict)
                    {
                        var bytes = GetEldata(materialPair.Value);
                        var geometry = new Utilities.DTO.Geometry()
                        {
                            Type = Utilities.Helpers.Enumerators.GeometryType.Mesh,
                            Data = GeometryDataToString(bytes),
                            ColorARGB = materialPair.Key
                        };

                        childInformation.AddGeometry(geometry);
                    }

                    elementInformation.AddChild(childInformation);
                }
            }

            Console.WriteLine();

            //убираем элементы с пустой геометрией
            ifcDictionaryKeys = ifcDictionary.Keys.ToList();

            progressCurrent = 0;
            progressTotal = ifcDictionaryKeys.Count;

            foreach (var ifcElement in ifcDictionaryKeys)
            {
                Console.Write($"\rsearch empty: {++progressCurrent}/{progressTotal}");

                var elementInformation = ifcDictionary[ifcElement];
                if (elementInformation.Geometries?.Count(x => !x.IsNegative) > 0)
                    continue;

                ifcDictionary.Remove(ifcElement);
            }

            Console.WriteLine();

            var result = ifcDictionary.Values.ToList();
            var roomInfoList = elementInformationList.Where(x=>x.Type == Enumerators.ElementType.Room).ToList();
            if (roomInfoList.Count >0)
                result.AddRange(roomInfoList);

            return result;
        }

        /// <summary>
        /// Покрытия не вкладываются в родителя. Полная сетка изоляции в XML воздуховода
        /// не помещается в память при импорте, элемент пропускается, и его зона удаляется как пустая.
        /// </summary>
        private static void CountCoverings(IfcElement element,
            Dictionary<IfcElement, Utilities.DTO.IfcElementInformation> ifcDictionary,
            ref int detachedCoverings, ref int missingCoverings)
        {
            if (element is IfcCovering || element.HasCoverings == null)
                return;

            foreach (var relation in element.HasCoverings)
            {
                if (relation?.RelatedCoverings == null)
                    continue;

                foreach (var relatedCovering in relation.RelatedCoverings)
                {
                    if (relatedCovering == null)
                        continue;

                    if (!ifcDictionary.TryGetValue(relatedCovering, out var coveringInfo) || coveringInfo == null)
                    {
                        missingCoverings++;
                        continue;
                    }

                    detachedCoverings++;
                }
            }
        }

        /// <summary>
        /// Получение атрибутивной и геометрической информации по воздуховодным элементам
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        private static List<Utilities.DTO.IfcElementInformation> GetDuctInfo(string path)
        {
            try
            {
                var ifcDictionary = new Dictionary<IfcElement, Utilities.DTO.IfcElementInformation>();

                Console.WriteLine("opening ifc");

                XbimP21Parser.MaxErrorCount = 10000;
                var model = IfcStore.Open(path);
                var context = new Xbim3DModelContext(model);
                var prevState = string.Empty;
                context.CreateContext((progress, state) =>
                {
                    if (state.ToString() != prevState)
                    {
                        if (!string.IsNullOrEmpty(prevState))
                            Console.WriteLine();

                        Console.Write($"{state}\t{progress}");
                        prevState = state.ToString();
                    }
                    else
                    {
                        Console.Write($"\r{state}\t{progress}");
                    }
                }
                );

                Console.WriteLine();

                var elementInformationList = DeserializeInformation(path.Replace(".ifc", ".info"));

                var collection = model.Instances.OfType<IfcElement>().ToList();

                var progressCurrent = 0;
                var progressTotal = collection.Count;
                var usedInformation = new HashSet<Utilities.DTO.IfcElementInformation>();
                var usedUniqueIds = new HashSet<string>();

                foreach (var ifcElement in collection)
                {
                    Console.Write($"\ranalysis: {++progressCurrent}/{progressTotal}");

                    if (ifcElement.Representation == null)
                        continue;

                    var elementInformation = elementInformationList.FirstOrDefault(x => x.Id == ifcElement.Tag.ToString());
                    var repeatedTag = elementInformation != null && usedInformation.Contains(elementInformation);
                    if (elementInformation == null)
                    {
                        elementInformation = new Utilities.DTO.IfcElementInformation
                        {
                            UniqueId = CalculateMD5Hash(ifcElement.GlobalId)
                        };
                    }
                    else if (repeatedTag)
                    {
                        var source = elementInformation;
                        elementInformation = source.Clone();
                        elementInformation.Location = source.Location;
                        elementInformation.UniqueId = CalculateMD5Hash(ifcElement.GlobalId);
                        if (elementInformation.UniqueId == source.UniqueId)
                            elementInformation.UniqueId = source.UniqueId + "_" + ifcElement.EntityLabel;
                        Console.WriteLine($"Повтор Tag {ifcElement.Tag}, отдельный элемент {elementInformation.UniqueId}");
                    }

                    if (elementInformation.IsDisabled)
                        continue;

                    if (string.IsNullOrEmpty(elementInformation.UniqueId) || usedUniqueIds.Contains(elementInformation.UniqueId))
                    {
                        elementInformation.UniqueId = CalculateMD5Hash(ifcElement.GlobalId + "_" + ifcElement.EntityLabel);
                        Console.WriteLine($"Повтор UniqueId, отдельный элемент {elementInformation.UniqueId}");
                    }

                    usedInformation.Add(elementInformation);
                    usedUniqueIds.Add(elementInformation.UniqueId);

                    if (!repeatedTag && !_modelMatrix3D.IsIdentity)
                    {
                        if (elementInformation.Location != null)
                        {
                            var tp = _modelMatrix3D.Transform(new XbimPoint3D(elementInformation.Location.X,
                                elementInformation.Location.Y, elementInformation.Location.Z));
                            elementInformation.Location =
                                new Utilities.DTO.Point(tp.X, tp.Y, tp.Z);
                        }

                        if (elementInformation.Connectors != null)
                        {
                            foreach (var connector in elementInformation.Connectors)
                            {
                                var tp = _modelMatrix3D.Transform(new XbimPoint3D(connector.Origin.X, connector.Origin.Y, connector.Origin.Z));
                                var td = _modelMatrix3D.Transform(new XbimVector3D(connector.Direction.X, connector.Direction.Y, connector.Direction.Z));

                                connector.Origin = new Utilities.DTO.Point(tp.X, tp.Y, tp.Z);
                                connector.Direction = new Utilities.DTO.Point(td.X, td.Y, td.Z);
                            }
                        }
                    }

                    ifcDictionary.Add(ifcElement, elementInformation);
                }

                Console.WriteLine();

                progressCurrent = 0;
                progressTotal = ifcDictionary.Count;

                foreach (var ifcElement in ifcDictionary.Keys)
                {
                    Console.Write($"\rgeometry: {++progressCurrent}/{progressTotal}");

                    var meshes = GetXbimShapeTriangulation(ifcElement, context);
                    _solids.Add(ifcElement, meshes);
                }

                Console.WriteLine();

                _pointsInverse = _points.ToDictionary(pair => pair.Value, pair => pair.Key);
                _normalsInverse = _normals.ToDictionary(pair => pair.Value, pair => pair.Key);

                progressCurrent = 0;
                progressTotal = _solids.Count;

                foreach (var pair in _solids)
                {
                    Console.Write($"\rupdating info: {++progressCurrent}/{progressTotal}");

                    var bytes = GetEldata(pair.Value);

                    var materialKey = string.Empty;

                    #region передача цвета в Aveva, отключил из-за жалоб Куликовой

                    //if (pair.Value.Count > 0)
                    //{
                    //    var mesh = pair.Value.First();
                    //    var texture = mesh.Texture ?? mesh.DefaultTexture;
                    //    var colorMap = texture.ColourMap.FirstOrDefault();
                    //    var clr = System.Windows.Media.Color.FromScRgb(colorMap.Alpha, colorMap.Red, colorMap.Green,
                    //        colorMap.Blue);

                    //    var alpha = mesh.IsReserveGeometry ? clr.ScA.ToString().Replace(",", ".") : "1";
                    //    materialKey = $"{clr.R} {clr.G} {clr.B} {alpha}";
                    //}

                    #endregion

                    var geometry = new Utilities.DTO.Geometry
                    {
                        Type = Utilities.Helpers.Enumerators.GeometryType.Mesh,
                        Data = GeometryDataToString(bytes),
                        ColorARGB = string.IsNullOrEmpty(materialKey) ? null : materialKey
                    };

                    ifcDictionary[pair.Key].AddGeometry(geometry);
                }

                Console.WriteLine();

                progressCurrent = 0;
                progressTotal = ifcDictionary.Count;
                var detachedCoverings = 0;
                var missingCoverings = 0;

                foreach (var pair in ifcDictionary)
                {
                    Console.Write($"\rupdating covering: {++progressCurrent}/{progressTotal}");
                    CountCoverings(pair.Key, ifcDictionary, ref detachedCoverings, ref missingCoverings);
                }

                Console.WriteLine();
                if (detachedCoverings > 0)
                    Console.WriteLine($"Изоляция не вложена в воздуховоды: {detachedCoverings}. Сетка покрытия не добавляется в XML родителя.");
                if (missingCoverings > 0)
                    Console.WriteLine($"Покрытия без геометрии пропущены: {missingCoverings}");

                return ifcDictionary.Where(x => !(x.Key is IfcCovering)).Select(i => i.Value).ToList();
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                Console.WriteLine(e.StackTrace);

            }


            return null;
        }

        /// <summary>
        /// Получение геометрической информации по элементам не специализированного файла
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        private static List<Utilities.DTO.IfcElementInformation> GetUnknownInfo(string path)
        {
            var ifcDictionary = new Dictionary<IfcElement, Utilities.DTO.IfcElementInformation>();
            var model = IfcStore.Open(path);
            var context = new Xbim3DModelContext(model);
            context.CreateContext();

            foreach (var ifcElement in model.Instances.OfType<IfcElement>())
            {
                if (ifcElement.Representation == null)
                    continue;

                var info = new Utilities.DTO.IfcElementInformation
                    { UniqueId = CalculateMD5Hash(ifcElement.GlobalId), Type = Utilities.Helpers.Enumerators.ElementType.Other };

                ifcDictionary.Add(ifcElement, info);

                var meshes = GetXbimShapeTriangulation(ifcElement, context);
                _solids.Add(ifcElement, meshes);
            }

            _pointsInverse = _points.ToDictionary(pair => pair.Value, pair => pair.Key);
            _normalsInverse = _normals.ToDictionary(pair => pair.Value, pair => pair.Key);

            foreach (var pair in _solids)
            {
                var bytes = GetEldata(pair.Value);
                var geometry = new Utilities.DTO.Geometry
                {
                    Type = Utilities.Helpers.Enumerators.GeometryType.Mesh,
                    Data = GeometryDataToString(bytes)
                };

                ifcDictionary[pair.Key].AddGeometry(geometry);
            }

            var detachedCoverings = 0;
            var missingCoverings = 0;
            foreach (var pair in ifcDictionary)
                CountCoverings(pair.Key, ifcDictionary, ref detachedCoverings, ref missingCoverings);

            return ifcDictionary.Where(x => !(x.Key is IfcCovering)).Select(i => i.Value).ToList();
        }

        private static string CalculateMD5Hash(string input)
        {
            var md5 = MD5.Create();
            var inputBytes = Encoding.ASCII.GetBytes(input);

            var hash = md5.ComputeHash(inputBytes);

            var sb = new StringBuilder();
            foreach (var b in hash)
            {
                sb.Append(b.ToString("X2"));
            }

            return sb.ToString();

        }

        /// <summary>
        /// Разделение геометрии по цветам
        /// </summary>
        /// <param name="input"></param>
        /// <param name="alphaEnabled"></param>
        /// <returns></returns>
        private static Dictionary<string, List<DTO.MeshInfo>> DevideMeshesByMaterial(List<DTO.MeshInfo> input,
            bool alphaEnabled)
        {
            var output = new Dictionary<string, List<DTO.MeshInfo>>();
            foreach (var mesh in input)
            {
                var texture = mesh.Texture ?? mesh.DefaultTexture;
                var colorMap = texture.ColourMap.FirstOrDefault();
                var clr = System.Windows.Media.Color.FromScRgb(colorMap.Alpha, colorMap.Red, colorMap.Green,
                    colorMap.Blue);

                var alpha = alphaEnabled || mesh.IsReserveGeometry ? clr.ScA.ToString().Replace(",", ".") : "1";
                var materialKey = $"{clr.R} {clr.G} {clr.B} {alpha}";

                if (!output.ContainsKey(materialKey))
                    output.Add(materialKey, new List<DTO.MeshInfo>());

                output[materialKey].Add(mesh);
            }

            return output;
        }

        /// <summary>
        /// Десериализация данных из файла
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        private static List<Utilities.DTO.IfcElementInformation> DeserializeInformation(string path)
        {
            if (!File.Exists(path))
                return new List<Utilities.DTO.IfcElementInformation>();

            List<Utilities.DTO.IfcElementInformation> result;
            using (var streamReader = new StreamReader(path))
            {
                var xmlSerializer = new XmlSerializer(typeof(List<Utilities.DTO.IfcElementInformation>));
                result = (List<Utilities.DTO.IfcElementInformation>)xmlSerializer.Deserialize(streamReader);
            }

            return result;
        }

        private static List<DTO.MeshInfo> GetXbimShapeTriangulation(IfcElement ifcElement,
            Xbim3DModelContext context)
        {
            var productShape = context.ShapeInstancesOf(ifcElement).Where(s =>
                s.RepresentationType != XbimGeometryRepresentationType.OpeningsAndAdditionsExcluded).ToList();
            var xbimShapeTriangulationList = new List<XbimShapeTriangulation>();
            var mesh = new DTO.MeshInfo { GeometryId = 0 };
            foreach (var shapeInstance in productShape)
            {
                var geometry = context.ShapeGeometry(shapeInstance.ShapeGeometryLabel) as IXbimShapeGeometryData;
                using (var ms = new MemoryStream(geometry.ShapeData))
                {
                    using (var br = new BinaryReader(ms))
                    {
                        var xbimShapeTriangulation = br.ReadShapeTriangulation();
                        xbimShapeTriangulation =
                            xbimShapeTriangulation.Transform(shapeInstance.Transformation * _modelMatrix3D);
                        xbimShapeTriangulationList.Add(xbimShapeTriangulation);
                    }
                }
            }

            if (ifcElement.Representation?.Representations.Count > 0)
            {
                var shapeRepresentation = ifcElement.Representation.Representations.First;
                if (shapeRepresentation.Items.Count > 0 && shapeRepresentation.Items.First.StyledByItem.Any())
                {
                    var styledItem = shapeRepresentation.Items.First.StyledByItem.FirstOrDefault();
                    if (styledItem != null && styledItem.Styles.Count > 0 &&
                        styledItem.Styles.First.SurfaceStyles.Any())
                    {
                        var surfaceStyle = styledItem.Styles.First.SurfaceStyles.FirstOrDefault();
                        if (surfaceStyle != null)
                        {
                            mesh.Texture = XbimTexture.Create(surfaceStyle);
                        }
                    }
                }
            }

            var facesList = new List<XbimLine[]>();
            foreach (var xbimShapeTriangulation in xbimShapeTriangulationList)
            {
                var points = xbimShapeTriangulation.Vertices;
                foreach (var face in xbimShapeTriangulation.Faces)
                {
                    for (var i = 0; i < face.TriangleCount; i++)
                    {
                        var p1 = points[face.Indices[i * 3]];
                        var p2 = points[face.Indices[i * 3 + 1]];
                        var p3 = points[face.Indices[i * 3 + 2]];

                        var n1 = face.NormalCount > i * 3
                            ? face.Normals[i * 3].Normal.Normalized()
                            : face.Normals[0].Normal.Normalized();
                        var n2 = face.NormalCount > i * 3 + 1
                            ? face.Normals[i * 3 + 1].Normal.Normalized()
                            : face.Normals[0].Normal.Normalized();
                        var n3 = face.NormalCount > i * 3 + 2
                            ? face.Normals[i * 3 + 2].Normal.Normalized()
                            : face.Normals[0].Normal.Normalized();

                        if (!_points.ContainsKey(p1))
                        {
                            _points.Add(p1, _points.Count + 1);
                        }

                        if (!_points.ContainsKey(p2))
                        {
                            _points.Add(p2, _points.Count + 1);
                        }

                        if (!_points.ContainsKey(p3))
                        {
                            _points.Add(p3, _points.Count + 1);
                        }

                        if (!_normals.ContainsKey(n1))
                        {
                            _normals.Add(n1, _normals.Count + 1);
                        }

                        if (!_normals.ContainsKey(n2))
                        {
                            _normals.Add(n2, _normals.Count + 1);
                        }

                        if (!_normals.ContainsKey(n3))
                        {
                            _normals.Add(n3, _normals.Count + 1);
                        }

                        var p = new XbimPoint3D((p1.X + p2.X + p3.X) / 3, (p1.Y + p2.Y + p3.Y) / 3,
                            (p1.Z + p2.Z + p3.Z) / 3);
                        var n = (p1 - p2).CrossProduct(p3 - p2).Normalized();

                        facesList.Add(new[]
                        {
                            new XbimLine { Pnt = p, Orientation = n },
                            new XbimLine { Pnt = p1, Orientation = n1 },
                            new XbimLine { Pnt = p2, Orientation = n2 },
                            new XbimLine { Pnt = p3, Orientation = n3 }
                        });
                    }
                }
            }

            foreach (var face in facesList)
            {
                XbimLine[] tFace = null;
                var countDict = new Dictionary<int, int[]>();
                var ray = face[0];
                for (var j = 0; j < 4; j++)
                {
                    var v = (ray.Pnt - face[j].Pnt);
                    if (j == 0 || v.Length > 1)
                    {
                        v = v.Normalized();
                    }
                    else
                    {
                        v = XbimVector3D.Multiply(v.Length / 2, v);
                    }

                    ray.Pnt = XbimPoint3D.Add(face[j].Pnt, v);

                    if (facesList.Count > 10000)
                    {
                        tFace = new[] { face[1], face[2], face[3] };
                        break;
                    }

                    var distanceDict = Intersections(facesList, ray);
                    if (distanceDict[true] == 0)
                    {
                        tFace = new[] { face[1], face[3], face[2] };
                        for (var k = 0; k < 3; k++)
                        {
                            tFace[k].Orientation = tFace[k].Orientation.Negated();
                            if (!_normals.ContainsKey(tFace[k].Orientation))
                                _normals.Add(tFace[k].Orientation, _normals.Count + 1);
                        }

                        break;
                    }

                    if (distanceDict[false] == 0)
                    {
                        tFace = new[] { face[1], face[2], face[3] };
                        break;
                    }

                    countDict.Add(j, new[] { distanceDict[true], distanceDict[false] });
                }

                if (tFace != null)
                {
                    mesh.FaceIndexes.Add(_faces.Count);
                    _faces.Add(new[]
                    {
                        _points[tFace[0].Pnt], _normals[tFace[0].Orientation],
                        _points[tFace[1].Pnt], _normals[tFace[1].Orientation],
                        _points[tFace[2].Pnt], _normals[tFace[2].Orientation]
                    });

                    continue;
                }

                for (var j = 0; j < 4; j++)
                {
                    if (countDict[j][0] % 2 == 0 && countDict[j][1] % 2 != 0)
                    {
                        tFace = new[] { face[1], face[3], face[2] };
                        for (var k = 0; k < 3; k++)
                        {
                            tFace[k].Orientation = tFace[k].Orientation.Negated();
                            if (!_normals.ContainsKey(tFace[k].Orientation))
                                _normals.Add(tFace[k].Orientation, _normals.Count + 1);
                        }

                        break;
                    }

                    if (countDict[j][0] % 2 != 0 && countDict[j][1] % 2 == 0)
                    {
                        tFace = new[] { face[1], face[2], face[3] };
                        break;
                    }
                }

                if (tFace == null)
                {
                    tFace = new[] { face[1], face[2], face[3] };
                }

                mesh.FaceIndexes.Add(_faces.Count);
                _faces.Add(new[]
                {
                    _points[tFace[0].Pnt], _normals[tFace[0].Orientation],
                    _points[tFace[1].Pnt], _normals[tFace[1].Orientation],
                    _points[tFace[2].Pnt], _normals[tFace[2].Orientation]
                });
            }

            return new List<DTO.MeshInfo> { mesh };
        }

        private static Dictionary<bool, int> Intersections(List<XbimLine[]> facesList, XbimLine ray)
        {
            var result = new Dictionary<bool, int>
            {
                { true, 0 },
                { false, 0 }
            };
            var distances = new List<double>();

            foreach (var line in facesList)
            {
                var res = DistanceToIntersection(line[1].Pnt, line[2].Pnt, line[3].Pnt, ray);
                if (Math.Abs(0.0 - res) < 0.001)
                    continue;

                var isExist = false;
                foreach (var dist in distances)
                {
                    if (Math.Abs(dist - res) > 0.001)
                        continue;

                    isExist = true;

                    break;
                }

                if (isExist)
                    continue;

                distances.Add(res);
                result[res > 0]++;
            }

            return result;
        }

        private static double DistanceToIntersection(XbimPoint3D p1, XbimPoint3D p2, XbimPoint3D p3, XbimLine ray)
        {
            var e1 = p2 - p1;
            var e2 = p3 - p1;

            var pvec = XbimVector3D.CrossProduct(ray.Orientation, e2);
            var det = XbimVector3D.DotProduct(e1, pvec);

            if (Math.Abs(det) < 1e-8)
                return 0;


            var inv_det = 1 / det;
            var tvec = ray.Pnt - p1;
            var u = XbimVector3D.DotProduct(tvec, pvec) * inv_det;
            if (u < 0 || u > 1)
                return 0;

            var qvec = XbimVector3D.CrossProduct(tvec, e1);
            var v = XbimVector3D.DotProduct(ray.Orientation, qvec) * inv_det;
            if (v < 0 || u + v > 1)
                return 0;

            return XbimVector3D.DotProduct(e2, qvec) * inv_det;
        }

        /// <summary>
        /// Определение типа элемента
        /// </summary>
        /// <param name="ifcElement"></param>
        /// <param name="func"></param>
        /// <returns></returns>
        private static Utilities.Helpers.Enumerators.ElementType GetElementType(IfcElement ifcElement, string func = "")
        {
            if (func == "WALK")
            {
                return Utilities.Helpers.Enumerators.ElementType.WALK;
            }

            if (func == "TEXT")
            {
                return Utilities.Helpers.Enumerators.ElementType.TEXT;
            }

            if (ifcElement.Name.HasValue && ifcElement.Name.Value.ToString().Contains("ПМИ_Зона_резервирования"))
            {
                return Utilities.Helpers.Enumerators.ElementType.REZV;
            }

            switch (ifcElement)
            {
                case IfcWall _:
                    return Utilities.Helpers.Enumerators.ElementType.Wall;
                case IfcCurtainWall _:
                    return Utilities.Helpers.Enumerators.ElementType.CurtainWall;
                case IfcBeam _:
                    return Utilities.Helpers.Enumerators.ElementType.Beam;
                case IfcSlab _:
                    return Utilities.Helpers.Enumerators.ElementType.Slab;
                case IfcDoor _:
                    return Utilities.Helpers.Enumerators.ElementType.Door;
                case IfcWindow _:
                    return Utilities.Helpers.Enumerators.ElementType.Window;
                case IfcStair _:
                    return Utilities.Helpers.Enumerators.ElementType.Stair;
                case IfcRailing _:
                    return Utilities.Helpers.Enumerators.ElementType.Railing;
                case IfcCovering _:
                    return Utilities.Helpers.Enumerators.ElementType.Covering;
                case IfcRoof _:
                    return Utilities.Helpers.Enumerators.ElementType.Roof;
                default:
                    return Utilities.Helpers.Enumerators.ElementType.Other;
            }
        }
        
        /// <summary>
        /// Получение геометрии элемента
        /// </summary>
        /// <param name="model"></param>
        /// <param name="ifcElement"></param>
        /// <returns></returns>
        private static List<DTO.MeshInfo> GetMeshGeometry(IfcStore model, IfcElement ifcElement)
        {
            var modelPositions = new XbimModelPositioningCollection();
            short userDefinedId = 0;
            model.UserDefinedId = userDefinedId;
            modelPositions.AddModel(model.ReferencingModel);

            if (model.IsFederation)
            {
                foreach (var refModel in model.ReferencedModels)
                {
                    refModel.Model.UserDefinedId = ++userDefinedId;

                    if (refModel.Model is IfcStore ifcStore)
                        modelPositions.AddModel(ifcStore.ReferencingModel);
                }
            }

            var wpfMeshGeometry3D = GetGeometry(ifcElement);
            var meshes = AnalyzeTriangles(wpfMeshGeometry3D, model);

            return meshes;
        }

        [Obsolete]
        private static Utilities.DTO.Geometry GetSolidGeometry(IfcElement ifcElement,
            IfcRepresentationItem shapeItem)
        {
            var extrudedSolid = shapeItem as IfcExtrudedAreaSolid;
            var solidMatrix = extrudedSolid.Position.ToMatrix3D();
            var itemMatrix = ifcElement.Transform();
            var extrude = extrudedSolid.ExtrudedDirection.Z < 0 ? -1 : 1;
            if (extrudedSolid.SweptArea is IfcArbitraryClosedProfileDef)
            {
                var profileDef = (IfcArbitraryClosedProfileDef)extrudedSolid.SweptArea;
                if (profileDef.OuterCurve is IfcPolyline)
                {
                    var zDirection = itemMatrix.Transform(solidMatrix.Transform(new XbimVector3D(0, 0, 1)));
                    var geometry = new Utilities.DTO.Geometry()
                        { Type = Utilities.Helpers.Enumerators.GeometryType.Extrusion };

                    var normal = zDirection * extrude;
                    var data = new List<double> { Math.Round(extrudedSolid.Depth), normal.X, normal.Y, normal.Z };

                    var ifcPolyline = profileDef.OuterCurve as IfcPolyline;
                    var points = ifcPolyline.Points;
                    foreach (var point in points)
                    {
                        var xbimPoint = point.XbimPoint3D();
                        xbimPoint = FixPoint(xbimPoint);
                        var p = itemMatrix.Transform(solidMatrix.Transform(xbimPoint));
                        data.Add(p.X);
                        data.Add(p.Y);
                        data.Add(p.Z);
                    }

                    geometry.Data = GeometryDataToString(data.ToArray());
                    return geometry;
                }
            }
            else if (extrudedSolid.SweptArea is IfcRectangleProfileDef)
            {
                var profileDef = (IfcRectangleProfileDef)extrudedSolid.SweptArea;
                var profileDefMatrix = profileDef.Position.ToMatrix3D();

                var zDirection =
                    itemMatrix.Transform(solidMatrix.Transform(profileDefMatrix.Transform(new XbimVector3D(0, 0, 1))));

                var xDim = profileDef.XDim / 2;
                var yDim = profileDef.YDim / 2;
                var points = new List<XbimPoint3D>
                {

                    itemMatrix.Transform(
                        solidMatrix.Transform(
                            profileDefMatrix.Transform(new XbimPoint3D(xDim, yDim, 0)))),

                    itemMatrix.Transform(
                        solidMatrix.Transform(
                            profileDefMatrix.Transform(new XbimPoint3D(-xDim, yDim, 0)))),

                    itemMatrix.Transform(
                        solidMatrix.Transform(
                            profileDefMatrix.Transform(new XbimPoint3D(-xDim, -yDim, 0)))),

                    itemMatrix.Transform(
                        solidMatrix.Transform(
                            profileDefMatrix.Transform(new XbimPoint3D(xDim, -yDim, 0)))),
                };

                var geometry = new Utilities.DTO.Geometry()
                    { Type = Utilities.Helpers.Enumerators.GeometryType.Extrusion };
                var normal = zDirection * extrude;
                var data = new List<double> { Math.Round(extrudedSolid.Depth), normal.X, normal.Y, normal.Z };

                foreach (var p in points)
                {
                    data.Add(p.X);
                    data.Add(p.Y);
                    data.Add(p.Z);
                }

                geometry.Data = GeometryDataToString(data.ToArray());
                return geometry;
            }
            //else if (extrudedSolid.SweptArea is IfcCircleProfileDef)
            //{
            //    return null;
            //    var profileDef = (IfcCircleProfileDef)extrudedSolid.SweptArea;
            //    var profileDefMatrix = profileDef.Position.ToMatrix3D();

            //    var locationPoint = itemMatrix.Transform(solidMatrix.Transform(profileDefMatrix.Transform(new XbimPoint3D())));

            //    var zDirection =
            //        itemMatrix.Transform(solidMatrix.Transform(profileDefMatrix.Transform(new XbimVector3D(0, 0, 1))));

            //    geometry.Type = IfcElementInformation.GeometryType.Cylinder;
            //    var normal = zDirection * extrude;
            //    var data = new[]
            //    {
            //        Math.Round(extrudedSolid.Depth), Math.Round(profileDef.Radius, 1), normal.X, normal.Y, normal.Z,
            //        locationPoint.X, locationPoint.Y, locationPoint.Z
            //    };

            //    geometry.Data = GeometryDataToString(data);
            //}

            return null;
        }

        /// <summary>
        /// Исправление координат точки
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        private static XbimPoint3D FixPoint(XbimPoint3D input)
        {
            var output = new Point3D();
            var delta = 0.01;
            var point = new Point3D(input.X, input.Y, input.Z);
            point.X = point.X.Equals(double.NaN) ? 0.0 : Math.Round(point.X, 4);
            point.Y = point.Y.Equals(double.NaN) ? 0.0 : Math.Round(point.Y, 4);
            point.Z = point.Z.Equals(double.NaN) ? 0.0 : Math.Round(point.Z, 4);

            output.X = Math.Ceiling(point.X) - point.X < delta
                ? Math.Ceiling(point.X)
                : point.X - Math.Floor(point.X) < delta ? Math.Floor(point.X) : point.X;

            output.Y = Math.Ceiling(point.Y) - point.Y < delta
                ? Math.Ceiling(point.Y)
                : point.Y - Math.Floor(point.Y) < delta ? Math.Floor(point.Y) : point.Y;

            output.Z = Math.Ceiling(point.Z) - point.Z < delta
                ? Math.Ceiling(point.Z)
                : point.Z - Math.Floor(point.Z) < delta ? Math.Floor(point.Z) : point.Z;

            return new XbimPoint3D(output.X, output.Y, output.Z);
        }

        /// <summary>
        /// Получение геометрии WPF
        /// </summary>
        /// <param name="selection"></param>
        /// <returns></returns>
        private static WpfMeshGeometry3D GetGeometry(IPersistEntity selection)
        {
            var wpfMeshGeometry3D = new WpfMeshGeometry3D();
            wpfMeshGeometry3D.BeginUpdate();

            using (var geometryStore = selection.Model.GeometryStore)
            {
                using (var geometryStoreReader = geometryStore.BeginRead())
                {
                    foreach (var shapeInstance in geometryStoreReader.ShapeInstancesOfEntity(selection).Where(x =>
                                 x.RepresentationType == XbimGeometryRepresentationType.OpeningsAndAdditionsIncluded))
                    {
                        IXbimShapeGeometryData shapeGeometry =
                            geometryStoreReader.ShapeGeometry(shapeInstance.ShapeGeometryLabel);
                        if (shapeGeometry.Format != (byte)XbimGeometryType.PolyhedronBinary)
                            continue;

                        var transformation = shapeInstance.Transformation * _modelMatrix3D;
                        wpfMeshGeometry3D.Add(
                            shapeGeometry.ShapeData,
                            shapeInstance.IfcTypeId,
                            shapeInstance.IfcProductLabel,
                            shapeInstance.StyleLabel,
                            transformation,
                            (short)selection.Model.UserDefinedId
                        );
                    }
                }
            }

            wpfMeshGeometry3D.EndUpdate();

            if (!wpfMeshGeometry3D.GetBounds().IsEmpty)
                return wpfMeshGeometry3D;


            wpfMeshGeometry3D = new WpfMeshGeometry3D();
            wpfMeshGeometry3D.BeginUpdate();

            using (var geometryStore = selection.Model.GeometryStore)
            {
                using (var geometryStoreReader = geometryStore.BeginRead())
                {
                    foreach (var shapeInstance in geometryStoreReader.ShapeInstancesOfEntity(selection).Where(x =>
                                 x.RepresentationType ==
                                 XbimGeometryRepresentationType.OpeningsAndAdditionsExcluded))
                    {
                        IXbimShapeGeometryData shapeGeometry =
                            geometryStoreReader.ShapeGeometry(shapeInstance.ShapeGeometryLabel);
                        if (shapeGeometry.Format != (byte)XbimGeometryType.PolyhedronBinary)
                            continue;

                        var transform = shapeInstance.Transformation * _modelMatrix3D;
                        wpfMeshGeometry3D.Add(
                            shapeGeometry.ShapeData,
                            shapeInstance.IfcTypeId,
                            shapeInstance.IfcProductLabel,
                            shapeInstance.StyleLabel,
                            transform,
                            (short)selection.Model.UserDefinedId
                        );
                    }
                }
            }

            wpfMeshGeometry3D.EndUpdate();

            return wpfMeshGeometry3D;
        }

        /// <summary>
        /// Преобразование геометрии WPF в массив Mesh объектов
        /// </summary>
        /// <param name="wpfMeshGeometry3D"></param>
        /// <param name="model"></param>
        /// <returns></returns>
        private static List<DTO.MeshInfo> AnalyzeTriangles(IXbimMeshGeometry3D wpfMeshGeometry3D, IModel model)
        {
            var pos = wpfMeshGeometry3D.Positions.ToArray();
            var nor = wpfMeshGeometry3D.Normals.ToArray();
            var meshes = new List<DTO.MeshInfo>();
            foreach (var mesh in wpfMeshGeometry3D.Meshes)
            {
                var meshInfo = new DTO.MeshInfo { GeometryId = mesh.GeometryId };
                if (model.Instances[mesh.GeometryId] is IIfcSurfaceStyle surfaceStyle)
                {
                    meshInfo.SurfaceStyle = surfaceStyle;
                    meshInfo.Texture = XbimTexture.Create((Xbim.Ifc4.Interfaces.IIfcSurfaceStyle)surfaceStyle);
                }
                else
                {
                    meshInfo.Texture = XbimTexture.Create(new XbimColour("red", 1, 1, 1));
                }

                for (var i = mesh.StartTriangleIndex; i < mesh.EndTriangleIndex; i += 3)
                {
                    var i1 = wpfMeshGeometry3D.TriangleIndices[i];
                    var i2 = wpfMeshGeometry3D.TriangleIndices[i + 1];
                    var i3 = wpfMeshGeometry3D.TriangleIndices[i + 2];

                    var p1 = pos[i1];
                    if (!_points.ContainsKey(p1))
                        _points.Add(p1, _points.Count);

                    var p2 = pos[i2];
                    if (!_points.ContainsKey(p2))
                        _points.Add(p2, _points.Count);

                    var p3 = pos[i3];
                    if (!_points.ContainsKey(p3))
                        _points.Add(p3, _points.Count);

                    var n1 = nor[i1];
                    if (!_normals.ContainsKey(n1))
                        _normals.Add(n1, _normals.Count);

                    var n2 = nor[i2];
                    if (!_normals.ContainsKey(n2))
                        _normals.Add(n2, _normals.Count);

                    var n3 = nor[i3];
                    if (!_normals.ContainsKey(n3))
                        _normals.Add(n3, _normals.Count);

                    meshInfo.FaceIndexes.Add(_faces.Count);
                    _faces.Add(new[]
                    {
                        _points[p1], _normals[n1],
                        _points[p2], _normals[n2],
                        _points[p3], _normals[n3]
                    });
                }

                meshes.Add(meshInfo);
            }

            return meshes;
        }

        /// <summary>
        /// Преобразование геометрии в текстовое представление
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        private static string GeometryDataToString(object data)
        {
            byte[] bytes;

            if (data is double[] doubleArray)
            {
                var list = new List<byte>();
                foreach (var d in doubleArray)
                    list.AddRange(BitConverter.GetBytes(d));

                bytes = list.ToArray();
            }
            else
            {
                bytes = (byte[])data;
            }

            string str;
            using (var mw = new MemoryStream())
            {
                using (var zs = new ZlibStream(mw, CompressionMode.Compress, CompressionLevel.Default))
                {
                    using (var mr = new MemoryStream(bytes))
                    {
                        mr.CopyTo(zs);
                    }
                }

                str = Convert.ToBase64String(mw.ToArray());
            }

            return str;
        }

        /// <summary>
        /// Исправление Mesh объектов, если точки построения заданы не в том порядке
        /// </summary>
        /// <param name="mesh"></param>
        private static void FixMesh(DTO.MeshInfo mesh)
        {
            var cpoints = new XbimPoint3D[6];
            var cboxs = new List<XbimPoint3D[]>();
            var cnormals = new XbimVector3D[6];
            var cmatrixs = new XbimMatrix3D[6];
            var minPoint = new Point3D(double.MaxValue, double.MaxValue, double.MaxValue);
            var maxPoint = new Point3D(double.MinValue, double.MinValue, double.MinValue);
            for (var i = 0; i < mesh.FaceIndexes.Count; i++)
            {
                var face = _faces[mesh.FaceIndexes[i]];
                for (var j = 0; j < 5; j += 2)
                {
                    var p = _pointsInverse[face[j]];
                    if (minPoint.X > p.X)
                        minPoint.X = p.X;
                    if (minPoint.Y > p.Y)
                        minPoint.Y = p.Y;
                    if (minPoint.Z > p.Z)
                        minPoint.Z = p.Z;

                    if (maxPoint.X < p.X)
                        maxPoint.X = p.X;
                    if (maxPoint.Y < p.Y)
                        maxPoint.Y = p.Y;
                    if (maxPoint.Z < p.Z)
                        maxPoint.Z = p.Z;
                }
            }

            var cpoi = new[]
            {
                new XbimPoint3D(minPoint.X, minPoint.Y, minPoint.Z),
                new XbimPoint3D(maxPoint.X, minPoint.Y, minPoint.Z),
                new XbimPoint3D(maxPoint.X, maxPoint.Y, minPoint.Z),
                new XbimPoint3D(minPoint.X, maxPoint.Y, minPoint.Z),
                new XbimPoint3D(minPoint.X, minPoint.Y, maxPoint.Z),
                new XbimPoint3D(maxPoint.X, minPoint.Y, maxPoint.Z),
                new XbimPoint3D(maxPoint.X, maxPoint.Y, maxPoint.Z),
                new XbimPoint3D(minPoint.X, maxPoint.Y, maxPoint.Z)
            };

            cboxs.Add(new[] { cpoi[0], cpoi[1], cpoi[2], cpoi[3] });
            cboxs.Add(new[] { cpoi[4], cpoi[7], cpoi[6], cpoi[5] });
            cboxs.Add(new[] { cpoi[3], cpoi[7], cpoi[4], cpoi[0] });
            cboxs.Add(new[] { cpoi[2], cpoi[1], cpoi[5], cpoi[6] });
            cboxs.Add(new[] { cpoi[0], cpoi[4], cpoi[5], cpoi[1] });
            cboxs.Add(new[] { cpoi[3], cpoi[2], cpoi[6], cpoi[7] });

            var nearestFaceIndex = new Dictionary<int, List<int>>();
            var contFaces = new Dictionary<int, int>();
            var invert = false;
            var gFaces = 0;
            for (var i = 0; i < 6; i++)
            {
                nearestFaceIndex.Add(i, new List<int>());
                cpoints[i] = cboxs[i][0];
                var x = (cboxs[i][1] - cboxs[i][0]).Normalized();
                var y = (cboxs[i][3] - cboxs[i][0]).Normalized();
                cnormals[i] = x.CrossProduct(y).Normalized();
                cmatrixs[i] = new XbimMatrix3D(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, cnormals[i].X, cnormals[i].Y,
                    cnormals[i].Z, 0, cpoints[i].X, cpoints[i].Y, cpoints[i].Z, 1);
                try
                {
                    cmatrixs[i].Invert();
                }
                catch
                {
                    continue;
                }

                var cmatrix = cmatrixs[i];
                var nearestPoints = new List<int>();
                var minLength = double.MaxValue;
                for (var j = 0; j < mesh.FaceIndexes.Count; j++)
                {
                    var face = _faces[mesh.FaceIndexes[j]];
                    for (var k = 0; k < 5; k += 2)
                    {
                        var p = _pointsInverse[face[k]];
                        var tp = cmatrix.Transform(p);
                        var length = tp.Z;
                        if (length > minLength)
                            continue;

                        if (length < minLength)
                        {
                            nearestPoints.Clear();
                            minLength = length;
                            nearestPoints.Add(face[k]);
                        }
                        else if (!nearestPoints.Contains(face[k]))
                        {
                            nearestPoints.Add(face[k]);
                        }
                    }
                }

                var localFaceIndxs = nearestFaceIndex[i];
                for (var j = 0; j < mesh.FaceIndexes.Count; j++)
                {
                    var face = _faces[mesh.FaceIndexes[j]];
                    for (var k = 0; k < 5; k += 2)
                    {
                        if (!nearestPoints.Contains(face[k]))
                            continue;

                        var fInd = mesh.FaceIndexes[j];
                        var p1 = _pointsInverse[face[0]];
                        var p2 = _pointsInverse[face[2]];
                        var p3 = _pointsInverse[face[4]];
                        if (contFaces.ContainsKey(fInd))
                        {
                            var m2 = cmatrixs[contFaces[fInd]];
                            var faceCnt = FindCentroid(new[] { p1, p2, p3 });

                            if (cmatrix.Transform(faceCnt).Z <= m2.Transform(faceCnt).Z)
                            {
                                nearestFaceIndex[contFaces[fInd]].Remove(fInd);
                                contFaces[fInd] = i;
                                localFaceIndxs.Add(fInd);
                            }
                        }
                        else
                        {
                            contFaces.Add(fInd, i);
                            localFaceIndxs.Add(fInd);
                        }

                        if (localFaceIndxs.Contains(fInd))
                        {
                            var dist = cmatrix.Transform(p1).Z + cmatrix.Transform(p2).Z + cmatrix.Transform(p3).Z;
                            if (dist == 0.0)
                            {
                                var vx = new XbimVector3D(p2.X - p1.X, p2.Y - p1.Y, p2.Z - p1.Z).Normalized();
                                var vy = new XbimVector3D(p3.X - p1.X, p3.Y - p1.Y, p3.Z - p1.Z).Normalized();
                                var faceNormal = vx.CrossProduct(vy).Normalized();
                                invert = cmatrix.Transform(faceNormal).Z > 0;
                                gFaces++;
                            }
                        }

                        break;
                    }

                    if (invert)
                        break;
                }

                if (invert)
                    break;
            }

            if (gFaces == 0)
            {
                for (var i = 0; i < 6; i++)
                {
                    if (!nearestFaceIndex.ContainsKey(i) || nearestFaceIndex[i].Count == 0)
                        continue;

                    var cmatrix = cmatrixs[i];
                    var icmatrix = cmatrixs[i];
                    icmatrix.Invert();
                    foreach (var fi in nearestFaceIndex[i])
                    {
                        var face = _faces[fi];
                        var p1 = _pointsInverse[face[0]];
                        var p2 = _pointsInverse[face[2]];
                        var p3 = _pointsInverse[face[4]];
                        var vx = new XbimVector3D(p2.X - p1.X, p2.Y - p1.Y, p2.Z - p1.Z).Normalized();
                        var vy = new XbimVector3D(p3.X - p1.X, p3.Y - p1.Y, p3.Z - p1.Z).Normalized();
                        var faceNormal = vx.CrossProduct(vy).Normalized();

                        var angle = faceNormal.Angle(icmatrix.Backward) * 180 / Math.PI;
                        if (Math.Abs(angle - 90) < 10)
                            continue;

                        if (cmatrix.Transform(faceNormal).Z > 0)
                        {
                            invert = true;
                            i = 6;

                            break;
                        }
                    }
                }
            }

            if (!invert)
                return;

            for (var j = 0; j < mesh.FaceIndexes.Count; j++)
            {
                var face = _faces[mesh.FaceIndexes[j]];
                var n1 = _normalsInverse[face[5]].Negated();
                var n2 = _normalsInverse[face[3]].Negated();
                var n3 = _normalsInverse[face[1]].Negated();
                if (!_normals.ContainsKey(n1))
                {
                    _normalsInverse.Add(_normals.Count, n1);
                    _normals.Add(n1, _normals.Count);
                }

                if (!_normals.ContainsKey(n2))
                {
                    _normalsInverse.Add(_normals.Count, n2);
                    _normals.Add(n2, _normals.Count);
                }

                if (!_normals.ContainsKey(n3))
                {
                    _normalsInverse.Add(_normals.Count, n3);
                    _normals.Add(n3, _normals.Count);
                }

                var fixedFace = new[] { face[4], _normals[n1], face[2], _normals[n2], face[0], _normals[n3] };
                _faces[mesh.FaceIndexes[j]] = fixedFace;
            }
        }

        /// <summary>
        /// Поиск центра для набора точек
        /// </summary>
        /// <param name="p"></param>
        /// <returns></returns>
        private static XbimPoint3D FindCentroid(XbimPoint3D[] p)
        {
            double x = 0;
            double y = 0;
            double z = 0;
            var n = 0;

            foreach (var item in p)
            {
                x += item.X;
                y += item.Y;
                z += item.Z;
                n++;
            }

            if (n <= 0)
                return new XbimPoint3D(x, y, z);

            x /= n;
            y /= n;
            z /= n;

            return new XbimPoint3D(x, y, z);
        }

        /// <summary>
        /// Преобразование геометрических данных для использования в Aveva
        /// </summary>
        /// <param name="meshes"></param>
        /// <returns></returns>
        private static byte[] GetEldata(List<DTO.MeshInfo> meshes)
        {
            /*
             * data type
             * data size 1
             * header 1
             * matrix
             * data size 2
             * header 2
             * matrix invert
             * null byte
             * common
             * para count
             * faces (ref to vertex)
             * block end
             * vertex count
             * vertexes (translated)
             * normal count
             * normals (translated)
             * para count
             * faces (ref to normal)
             * end block
             * end
             */
            byte[] bytes;
            var dataTypeBytes = BitConverter.GetBytes(4);
            var header1Bytes = GetHeaderBytes(true);
            var header2Bytes = GetHeaderBytes(false);
            var nullByte = new byte[] { 0 };
            var commonBytes = new byte[] { 1, 0, 0, 0 };
            var endBytes = new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };

            var minPoint = new Point3D(double.MaxValue, double.MaxValue, double.MaxValue);
            foreach (var mesh in meshes)
            {
                for (var i = 0; i < mesh.FaceIndexes.Count; i++)
                {
                    var face = _faces[mesh.FaceIndexes[i]];

                    for (var j = 0; j < 5; j += 2)
                    {
                        var p = _pointsInverse[face[j]];
                        if (minPoint.X > p.X)
                            minPoint.X = p.X;

                        if (minPoint.Y > p.Y)
                            minPoint.Y = p.Y;

                        if (minPoint.Z > p.Z)
                            minPoint.Z = p.Z;
                    }
                }
            }

            var m = new XbimMatrix3D
            {
                OffsetX = -minPoint.X,
                OffsetY = -minPoint.Y,
                OffsetZ = -minPoint.Z
            };

            var mi = new XbimMatrix3D
            {
                OffsetX = -minPoint.X,
                OffsetY = -minPoint.Y,
                OffsetZ = -minPoint.Z
            };

            mi.Invert();

            var mBytes = GetMatrixBytes(m);
            var miBytes = GetMatrixBytes(mi);
            var meshPoints = new Dictionary<XbimPoint3D, int>();
            var meshNormals = new Dictionary<XbimVector3D, int>();
            var meshFaces = new List<int[]>();

            foreach (var mesh in meshes)
            {
                for (var i = 0; i < mesh.FaceIndexes.Count; i++)
                {
                    var face = _faces[mesh.FaceIndexes[i]];
                    var p1 = _pointsInverse[face[0]];
                    var n1 = _normalsInverse[face[1]];
                    var p2 = _pointsInverse[face[2]];
                    var n2 = _normalsInverse[face[3]];
                    var p3 = _pointsInverse[face[4]];
                    var n3 = _normalsInverse[face[5]];
                    n1 = n1.Normalized();
                    n2 = n2.Normalized();
                    n3 = n3.Normalized();

                    if (!meshPoints.ContainsKey(p1))
                        meshPoints.Add(p1, meshPoints.Count);

                    if (!meshPoints.ContainsKey(p2))
                        meshPoints.Add(p2, meshPoints.Count);

                    if (!meshPoints.ContainsKey(p3))
                        meshPoints.Add(p3, meshPoints.Count);

                    if (!meshNormals.ContainsKey(n1))
                        meshNormals.Add(n1, meshNormals.Count);

                    if (!meshNormals.ContainsKey(n2))
                        meshNormals.Add(n2, meshNormals.Count);

                    if (!meshNormals.ContainsKey(n3))
                        meshNormals.Add(n3, meshNormals.Count);

                    meshFaces.Add(new[]
                    {
                        meshPoints[p1], meshNormals[n1],
                        meshPoints[p2], meshNormals[n2],
                        meshPoints[p3], meshNormals[n3]
                    });
                }
            }

            var paraCount = meshFaces.Count * 5 + 1;
            var vertexCount = meshPoints.Count;
            var normalCount = meshNormals.Count;
            var dataSize2 = header2Bytes.Length + miBytes.Length + nullByte.Length + commonBytes.Length +
                            BitConverter.GetBytes(paraCount).Length + (paraCount * 4) +
                            BitConverter.GetBytes(vertexCount).Length + (vertexCount * 3 * 8) +
                            BitConverter.GetBytes(normalCount).Length + (normalCount * 3 * 8) +
                            BitConverter.GetBytes(paraCount).Length + (paraCount * 4) + endBytes.Length + 1;

            var dataSize1 = header1Bytes.Length + mBytes.Length + BitConverter.GetBytes(dataSize2).Length +
                            dataSize2;

            using (var ms = new MemoryStream())
            {
                using (var bw = new BinaryWriter(ms))
                {
                    bw.Write(dataTypeBytes);
                    bw.Write(BitConverter.GetBytes(dataSize1));
                    bw.Write(header1Bytes);
                    bw.Write(mBytes);
                    bw.Write(BitConverter.GetBytes(dataSize2));
                    bw.Write(header2Bytes);
                    bw.Write(miBytes);
                    bw.Write(nullByte);
                    bw.Write(commonBytes);
                    bw.Write(BitConverter.GetBytes(paraCount));
                    foreach (var face in meshFaces)
                    {
                        //vertex ref
                        bw.Write(BitConverter.GetBytes(face[0]));
                        bw.Write(BitConverter.GetBytes(face[2]));
                        bw.Write(BitConverter.GetBytes(face[4]));
                        bw.Write(BitConverter.GetBytes(-3));
                        bw.Write(BitConverter.GetBytes(-2));
                    }

                    bw.Write(BitConverter.GetBytes(-1));
                    bw.Write(BitConverter.GetBytes(vertexCount));

                    foreach (var p in meshPoints.Keys)
                    {
                        var tp = m.Transform(p);
                        bw.Write(BitConverter.GetBytes(tp.X));
                        bw.Write(BitConverter.GetBytes(tp.Y));
                        bw.Write(BitConverter.GetBytes(tp.Z));
                    }

                    bw.Write(BitConverter.GetBytes(normalCount));

                    foreach (var n in meshNormals.Keys)
                    {
                        var tn = m.Transform(n);
                        tn = tn.Normalized();
                        bw.Write(BitConverter.GetBytes(tn.X));
                        bw.Write(BitConverter.GetBytes(tn.Y));
                        bw.Write(BitConverter.GetBytes(tn.Z));
                    }

                    bw.Write(BitConverter.GetBytes(paraCount));

                    foreach (var face in meshFaces)
                    {
                        //normal ref
                        bw.Write(BitConverter.GetBytes(face[1]));
                        bw.Write(BitConverter.GetBytes(face[3]));
                        bw.Write(BitConverter.GetBytes(face[5]));
                        bw.Write(BitConverter.GetBytes(-3));
                        bw.Write(BitConverter.GetBytes(-2));
                    }

                    bw.Write(BitConverter.GetBytes(-1));
                    bw.Write(endBytes);
                    bw.Close();
                }

                ms.Flush();
                bytes = ms.ToArray();
                ms.Close();
            }

            return bytes;
        }

        /// <summary>
        /// Формирование набора данных для матрицы преобразования
        /// </summary>
        /// <param name="matrix"></param>
        /// <returns></returns>
        private static byte[] GetMatrixBytes(XbimMatrix3D matrix)
        {
            var result = new List<byte>();

            result.AddRange(BitConverter.GetBytes(matrix.M11));
            result.AddRange(BitConverter.GetBytes(matrix.M12));
            result.AddRange(BitConverter.GetBytes(matrix.M13));
            result.AddRange(BitConverter.GetBytes(matrix.M14));
            result.AddRange(BitConverter.GetBytes(matrix.M21));
            result.AddRange(BitConverter.GetBytes(matrix.M22));
            result.AddRange(BitConverter.GetBytes(matrix.M23));
            result.AddRange(BitConverter.GetBytes(matrix.M24));
            result.AddRange(BitConverter.GetBytes(matrix.M31));
            result.AddRange(BitConverter.GetBytes(matrix.M32));
            result.AddRange(BitConverter.GetBytes(matrix.M33));
            result.AddRange(BitConverter.GetBytes(matrix.M34));
            result.AddRange(BitConverter.GetBytes(matrix.OffsetX));
            result.AddRange(BitConverter.GetBytes(matrix.OffsetY));
            result.AddRange(BitConverter.GetBytes(matrix.OffsetZ));
            result.AddRange(BitConverter.GetBytes(matrix.M44));

            return result.ToArray();
        }

        /// <summary>
        /// Формирование набора данных для заголовка
        /// </summary>
        /// <param name="first"></param>
        /// <returns></returns>
        private static byte[] GetHeaderBytes(bool first)
        {
            var result = new List<byte>();
            if (first)
            {
                result.AddRange(BitConverter.GetBytes(-1489520442));
                result.AddRange(BitConverter.GetBytes(1097171314));
                result.AddRange(BitConverter.GetBytes(1841079687));
                result.AddRange(BitConverter.GetBytes(-303774350));
                result.AddRange(BitConverter.GetBytes(2));
            }
            else
            {
                result.AddRange(BitConverter.GetBytes(-1522655104));
                result.AddRange(BitConverter.GetBytes(299307885));
                result.AddRange(BitConverter.GetBytes(33582263));
                result.AddRange(BitConverter.GetBytes(1852124851));
                result.AddRange(BitConverter.GetBytes(2));
            }

            return result.ToArray();
        }
    }
}