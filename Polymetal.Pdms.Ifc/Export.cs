//using System;
//using System.Collections.Generic;
//using System.Diagnostics;
//using System.IO;
//using Aveva.PDMS.PMLNet;
//using System.Threading;
//using Aveva.Pdms.Database;
//using Aveva.Pdms.Shared;
//using System.Globalization;
//using System.Text;
//using cmd =Aveva.Pdms.Utilities.CommandLine.Command;

//namespace Polymetal.Pdms.Ifc
//{
//    [PMLNetCallable()]
//    public class Export
//    {
//        private static Process _serverProcess;
//        private static string _serverProcessPath= @"E:\polinchuk\visual studio\projects\Polymetal.Dashboard.Ifc\Polymetal.Pdms.IfcServer\bin\Debug\Polymetal.Pdms.IfcServer.exe";
//        internal static Dictionary<DbElement, ItemInfo> ItemInfoDictionary;
//        private static List<DbElementType> _endTypes;
//        internal static List<string> DebugList;
//        internal static List<string[]> ColorRules;
//        [PMLNetCallable()]
//        public Export()
//        {
//        }

//        [PMLNetCallable()]
//        public void Assign(Export that)
//        {
            
//        }

//        /// <summary>
//        /// Выгрузка заданий в dwg
//        /// </summary>
//        [PMLNetCallable()]
//        public void Run()
//        {
//            DebugList = new List<string>();
//            InitColorRules(@"E:\AVEVA\temp\ifc\2018_04_03\rules.txt");
//            Thread.CurrentThread.CurrentCulture = CultureInfo.CreateSpecificCulture("en-GB");

//            cmd.CreateCommand("REPRESENTATION DARCTOL 10").Run();
//            cmd.CreateCommand("REPRESENTATION OBST ON TRANSLUCENCY 75").RunInPdms();
//            cmd.CreateCommand("REPRESENTATION CABLE OBST ON").RunInPdms();
//            ItemInfoDictionary = new Dictionary<DbElement, ItemInfo>();
//            var items = new List<DbElement>();
//            //foreach (var item in CurrentElement.Element.Members())
//            //{
//            //    if (!item.GetAsString(DbAttributeInstance.FLNM).Contains("KM")) continue;
//            //    items.Add(item);
//            //}
//            //items.Add(CurrentElement.Element);
//            items.Add(DbElement.GetElement("/0200200-SO"));
//            items.Add(DbElement.GetElement("/0200200-SANO"));
//            items.Add(DbElement.GetElement("/0200200-EO"));
//            items.Add(DbElement.GetElement("/0200200-OA"));
//            items.Add(DbElement.GetElement("/0200200-GMO"));
//            items.Add(DbElement.GetElement("/0200200-OOR"));
//            //items.Add( DbElement.GetElement("=17722/1951999"));
//            //items.Add(DbElement.GetElement("=17722/1952027"));
//            _endTypes = new List<DbElementType>
//            {
//                DbElementTypeInstance.EQUIPMENT,
//                DbElementTypeInstance.GWALL,
//                DbElementTypeInstance.FLOOR,
//                DbElementTypeInstance.PANEL,
//                DbElementTypeInstance.SCTN,
//                DbElementTypeInstance.CWBRAN,
//                DbElementTypeInstance.CABLE
//            };

//            CollectData(items);

//            var zones = new List<ItemInfo>();
//            foreach (var value in ItemInfoDictionary.Values)
//            {
//                if (value.Type != DbElementTypeInstance.SITE) continue;
//                foreach (var memb in value.Children)
//                    zones.Add(memb);
//            }
//            ItemInfoDictionary.Clear();

//            if (File.Exists(@"E:\AVEVA\temp\ifc\2018_04_03\export.txt"))
//                File.Delete(@"E:\AVEVA\temp\ifc\2018_04_03\export.txt");

//            using (var writer = new StreamWriter(@"E:\AVEVA\temp\ifc\2018_04_03\export.txt", true))
//            {
//                writer.WriteLine("CREATE MODEL");
//                writer.WriteLine("CREATE PROJECT");
//                writer.Close();
//            }

//            foreach (var root in zones)
//            {
//                var geometryItems = new List<ItemInfo>();
//                geometryItems.AddRange(GetEndItems(root));
//                for (var i = 0; i < geometryItems.Count; i++)
//                {
//                    var item = geometryItems[i];
//                    SetGeometry(item);
//                    Console.Write("\r{0} {1}/{2}", root.Name, i, geometryItems.Count);
//                }
//            }
//            return;
//            using (var writer = new StreamWriter(@"E:\AVEVA\temp\ifc\2018_04_03\export.txt", true))
//            {
//                foreach (var root in zones)
//                {

//                    writer.WriteLine("CREATE SITE");
//                    writer.WriteLine("{0}", root.Name);

//                    var geometryItems = new List<ItemInfo>();
//                    geometryItems.AddRange(GetEndItems(root));

//                    for (var i = 0; i < geometryItems.Count; i++)
//                    {
//                        var item = geometryItems[i];
//                        SetGeometry(item);
//                        writer.WriteLine("CREATE PRODUCT");
//                        writer.WriteLine(item.Name);
//                        writer.WriteLine(item.IfcType);

//                        foreach (var g in item.Geometry)
//                        {
//                            var sb = new StringBuilder();
//                            sb.Append(g);
//                            foreach (var negativ in g.Negatives)
//                            {
//                                sb.Append("#");
//                                sb.Append(negativ);
//                            }
//                            writer.WriteLine(sb);
//                        }

//                        writer.WriteLine("END");
//                        Console.Write("\r{0} {1}/{2}", root.Name, i, geometryItems.Count);
//                        writer.Flush();
//                    }
//                    geometryItems.Clear();
//                }
//                writer.Close();
//            }

//            using (var writer = new StreamWriter(@"E:\AVEVA\temp\ifc\2018_04_03\export.txt", true))
//            {
//                writer.WriteLine("SAVE MODEL");
//                writer.WriteLine(@"E:\AVEVA\temp\ifc\2018_04_03\pdms.ifc");
//                writer.Close();
//            }
           
//            Console.WriteLine();
//            //for (int index = 0; index < geometryItems.Count; index++)
//            //{
//            //    var info = geometryItems[index];
//            //    SetGeometry(info);
//            //    Console.Write("\r{0}/{1}", index, geometryItems.Count);
//            //}

//            //CreateIfc(@"E:\AVEVA\temp\ifc\2018_04_03\pdms.ifc", geometryItems);
//            Process.Start(
//                @"E:\polinchuk\visual studio\projects\Polymetal.Dashboard.Ifc\Polymetal.Pdms.IfcServer\bin\Debug\Polymetal.Pdms.IfcServer.exe");
//            foreach (var debug in DebugList)
//            {
//                Console.WriteLine(debug);
//            }
//        }

//        private static void InitColorRules(string rulesPath)
//        {
//            ColorRules = new List<string[]>();
//            using (var sr = new StreamReader(rulesPath))
//            {
//                string line;
//                while ((line = sr.ReadLine()) != null)
//                {
//                    if (string.IsNullOrEmpty(line)) continue;
//                    line = line.ToUpper();
//                    var rule = new string[3];
//                    var split = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
//                    for (var i = 0; i < split.Length; i++)
//                    {
//                        if (i == 0)
//                        {
//                            if (split[i] == "ALL") continue;
//                            rule[0] = split[i];
//                        }
//                        else if (i == split.Length - 1 || i == split.Length - 2)
//                        {
//                            rule[2] += split[i] + " ";
//                        }
//                        else
//                        {
//                            if (split[i] == "WITH") continue;
//                            rule[1] += split[i] + " ";
//                        }
//                    }
//                    rule[0] = string.IsNullOrEmpty(rule[0]) ? "CE" : rule[0].Trim();
//                    rule[1] = string.IsNullOrEmpty(rule[1]) ? "TRUE" : rule[1].Trim();
//                    rule[2] = rule[2].Replace("COLOUR",string.Empty).Trim();
//                    ColorRules.Add(rule);
//                }
//                sr.Close();
//            }
//        }

//        private StringBuilder WriteGeometry(ItemInfo info)
//        {
//            var sb = new StringBuilder();
//            var geometries = new List<PdmsGeometry>();
//            if (info.Type == DbElementTypeInstance.EQUIPMENT)
//            {
//                var geomList = GetEquiGeometry(info.Item);
//                foreach (var geom in geomList)
//                {
//                    if (geom.GetElementType() == DbElementTypeInstance.NOZZLE ||
//                        geom.GetElementType() == DbElementTypeInstance.ELCONN ||
//                        geom.GetElementType() == DbElementTypeInstance.SCTN ||
//                        geom.Owner.GetElementType() == DbElementTypeInstance.BRANCH)
//                    {
//                        geometries.AddRange(PdmsGeometry.GetCatalogueGeometry(geom));
//                        continue;
//                    }

//                    var pdmsGeometry = PdmsGeometry.GetItemGeometry(geom, false);
//                    pdmsGeometry.Color = GetItemColor(geom);
//                    if (pdmsGeometry is PdmsExtrusion)
//                    {
//                        var profiles = PdmsGeometry.GetProfilePoints(geom);
//                        var pdmsExtrusion = pdmsGeometry as PdmsExtrusion;
//                        foreach (var profile in profiles)
//                        {
//                            var extrusionGeometry = new PdmsExtrusion(profile, pdmsExtrusion.Height,
//                                pdmsExtrusion.Matrix, pdmsExtrusion.Negatives)
//                            {
//                                Color = pdmsGeometry.Color,
//                                Translucency = pdmsGeometry.Translucency
//                            };
//                            geometries.Add(extrusionGeometry);
//                        }
//                        continue;
//                    }

//                    if (pdmsGeometry is PdmsRevolution)
//                    {
//                        var profiles = PdmsGeometry.GetProfilePoints(geom);
//                        var pdmsRevolution = pdmsGeometry as PdmsRevolution;
//                        foreach (var profile in profiles)
//                        {
//                            var revolutionGeometry = new PdmsRevolution(profile, pdmsRevolution.Angle,
//                                pdmsRevolution.Matrix, pdmsRevolution.Negatives)
//                            {
//                                Color = pdmsGeometry.Color,
//                                Translucency = pdmsGeometry.Translucency
//                            };
//                            geometries.Add(revolutionGeometry);
//                        }
//                        continue;
//                    }
//                    geometries.Add(pdmsGeometry);
//                }
//            }
//            else
//            {
//                geometries.AddRange(PdmsGeometry.GetCatalogueGeometry(info.Item));
//            }

//            sb.Append("CREATE PRODUCT");
//            sb.Append(info.Name);
//            sb.Append(info.IfcType);
//            foreach (var g in geometries)
//            {
//                var geometry = g.ToString();
//                foreach (var negativ in g.Negatives)
//                {
//                    geometry += string.Format("#{0}", negativ);
//                }
//                sb.Append(geometry);
//            }
//            sb.Append("END");
//            info = null;
//            return sb;
//        }

//        private void SetGeometry(ItemInfo info)
//        {
//            if (info.Type == DbElementTypeInstance.EQUIPMENT)
//            {
//                var geomList = GetEquiGeometry(info.Item);
//                foreach (var geom in geomList)
//                {
//                    if (geom.GetElementType() == DbElementTypeInstance.NOZZLE ||
//                        geom.GetElementType() == DbElementTypeInstance.ELCONN ||
//                        geom.GetElementType() == DbElementTypeInstance.SCTN ||
//                        geom.Owner.GetElementType() == DbElementTypeInstance.BRANCH)
//                    {
//                        info.Geometry.AddRange(PdmsGeometry.GetCatalogueGeometry(geom));
//                        continue;
//                    }

//                    var pdmsGeometry = PdmsGeometry.GetItemGeometry(geom,false);
//                    pdmsGeometry.Color = GetItemColor(geom);
//                    if (pdmsGeometry is PdmsExtrusion)
//                    {
//                        var profiles = PdmsGeometry.GetProfilePoints(geom);
//                        var pdmsExtrusion = pdmsGeometry as PdmsExtrusion;
//                        foreach (var profile in profiles)
//                        {
//                            var extrusionGeometry = new PdmsExtrusion(profile, pdmsExtrusion.Height,
//                                pdmsExtrusion.Matrix, pdmsExtrusion.Negatives)
//                            {
//                                Color = pdmsGeometry.Color,
//                                Translucency = pdmsGeometry.Translucency
//                            };
//                            info.Geometry.Add(extrusionGeometry);
//                        }
//                        continue;
//                    }

//                    if (pdmsGeometry is PdmsRevolution)
//                    {
//                        var profiles = PdmsGeometry.GetProfilePoints(geom);
//                        var pdmsRevolution = pdmsGeometry as PdmsRevolution;
//                        foreach (var profile in profiles)
//                        {
//                            var revolutionGeometry = new PdmsRevolution(profile, pdmsRevolution.Angle,
//                                pdmsRevolution.Matrix, pdmsRevolution.Negatives)
//                            {
//                                Color = pdmsGeometry.Color,
//                                Translucency = pdmsGeometry.Translucency
//                            };
//                            info.Geometry.Add(revolutionGeometry);
//                        }
//                        continue;
//                    }
//                    info.Geometry.Add(pdmsGeometry);
//                }
//            }
//            else 
//            {
//                info.Geometry.AddRange(PdmsGeometry.GetCatalogueGeometry(info.Item));
//            }
//        }

//        internal static int GetItemColor(DbElement root)
//        {
//            var result = 0;
//            foreach (var rule in ColorRules)
//            {
//                var item = DbElement.GetElement();
//                var condition = false;
//                if (!root.EvaluateValidElement(DbExpression.Parse(rule[0]), ref item) || 
//                    item.IsNull ||
//                    !item.EvaluateValidBool(DbExpression.Parse(rule[1]), ref condition) || 
//                    !condition) continue;

//                return int.Parse(rule[2]);
//            }
//            return result;
//        }

//        private List<DbElement> GetEquiGeometry(DbElement root)
//        {
//            var res = new List<DbElement>();
//            foreach (var member in root.Members())
//            {
//                var memberType = member.GetElementType();
//                if (memberType == DbElementTypeInstance.TMPLATE ||
//                    memberType == DbElementTypeInstance.SUBEQUIPMENT)
//                {
//                    res.AddRange(GetEquiGeometry(member));
//                    continue;
//                }

//                if (memberType == DbElementTypeInstance.DPSET || 
//                    memberType == DbElementTypeInstance.DDSE ||
//                    memberType == DbElementTypeInstance.TMRPSET) continue;
//                res.Add(member);
//            }
//            return res;
//        }

//        private List<ItemInfo> GetEndItems(ItemInfo rootInfo)
//        {
//            var res = new List<ItemInfo>();
//            if (_endTypes.Contains(rootInfo.Type) || (rootInfo.OwnerInfo!=null && rootInfo.OwnerInfo.Type == DbElementTypeInstance.BRANCH))
//            {
//                rootInfo.Children = new List<ItemInfo>();
//                res.Add(rootInfo);
//                return res;
//            }

//            foreach (var child in rootInfo.Children)
//                res.AddRange(GetEndItems(child));
//            return res;
//        }

//        private void CollectData(List<DbElement> items)
//        {
//            foreach (var item in items)
//            {
//                if (!ItemInfoDictionary.ContainsKey(item))
//                {
//                    ItemInfoDictionary.Add(item, new ItemInfo(item));
//                }
//                var currentElement = item;
//                var owner = item.Owner;
//                while (!owner.GetElementType().Equals(DbElementTypeInstance.WORLD))
//                {
//                    if (!ItemInfoDictionary.ContainsKey(owner))
//                        ItemInfoDictionary.Add(owner, new ItemInfo(owner));
//                    var ownerInfo = ItemInfoDictionary[owner];
//                    var currentInfo = ItemInfoDictionary[currentElement];
//                    ownerInfo.Children.Add(currentInfo);
//                    currentInfo.OwnerInfo = ownerInfo;
//                    currentElement = owner;
//                    owner = owner.Owner;
//                }

//                CollectSubData(item);
//            }
//        }

//        private void CollectSubData(DbElement root)
//        {
//            if (_endTypes.Contains(root.GetElementType())) return;
//            foreach (var memb in root.Members())
//            {
//                if (!ItemInfoDictionary.ContainsKey(memb))
//                {
//                    ItemInfoDictionary.Add(memb, new ItemInfo(memb));
//                }
//                var ownerInfo = ItemInfoDictionary[root];
//                var itemInfo = ItemInfoDictionary[memb];
//                ownerInfo.Children.Add(itemInfo);
//                itemInfo.OwnerInfo = ownerInfo;
//                CollectSubData(memb);
//            }
//        }

//        private void CreateIfc(string path, List<ItemInfo> items)
//        {
//            //StartIfcServer();
//            //Thread.Sleep(1000);
//            //var client = new NamedPipeClientStream("IfcPdmsPipe");
//            //client.Connect();
//            //var reader = new StreamReader(client);
//            //var writer = new StreamWriter(client);
//            using (var writer = new StreamWriter(@"E:\AVEVA\temp\ifc\2018_04_03\export.txt"))
//            {
//                writer.WriteLine("CREATE MODEL");
//                writer.Flush();

//                writer.WriteLine("CREATE PROJECT");
//                writer.Flush();

//                writer.WriteLine("CREATE SITE");
//                writer.WriteLine("0200200-SO");
//                writer.Flush();

//                foreach (var item in items)
//                {
//                    writer.WriteLine("CREATE PRODUCT");
//                    writer.WriteLine(item.Name);
//                    writer.WriteLine(item.IfcType);

//                    foreach (var g in item.Geometry)
//                    {
//                        var geometry = g.ToString();
//                        foreach (var negativ in g.Negatives)
//                        {
//                            geometry += string.Format("#{0}", negativ);
//                        }
//                        writer.WriteLine(geometry);
//                    }

//                    writer.WriteLine("END");
//                    writer.Flush();
//                }

//                writer.WriteLine("SAVE MODEL");
//                writer.WriteLine(path);
//                writer.Flush();
//                writer.Close();
//            }
//        }

//        private void StartIfcServer()
//        {
//            _serverProcess = Process.Start(_serverProcessPath);
//        }
//    }
//}
