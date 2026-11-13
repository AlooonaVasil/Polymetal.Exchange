using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Aveva.Core.Database;
using Aveva.Core.Geometry;
using Aveva.Core.Database.Filters;
using ZetaLongPaths;
using cmd = Aveva.Core.Utilities.CommandLine.Command;

namespace Polymetal.Exchange.E3D
{
    class MagicExport
    {
        private static string _localStpPath;
        private static string _serverStpPath;
        private static Dictionary<string, List<DbElement>> _exportDictionary;

        internal static bool Run()
        {
            _localStpPath = Path.Combine(@"C:\Aveva\temp\ReferenceModels\MagiCad", RunE3D.ProjectInfo.ProjectId.ToString());
            _serverStpPath = Path.Combine(@"C:\AVEVA\Plant\ReferenceModels\MagiCad", RunE3D.ProjectInfo.ProjectId.ToString());
            _exportDictionary = new Dictionary<string, List<DbElement>>();
            ClearLocalDirectory(new ZlpDirectoryInfo(_localStpPath));
            var filter = new AndFilter(new TypeFilter(DbElementTypeInstance.SITE), new AttributeStringFilter(DbAttributeInstance.PURP, FilterOperator.Equals, "JOB"));
            var collection = new DBElementCollection(MDB.CurrentMDB.GetFirstWorld(DbType.Design), filter) { IncludeRoot = false }.Cast<DbElement>().ToList();
            PrepareDictionary(new List<DbElement> { DbElement.GetElement("/0000000-SANO") });
            RunExportCommands();
            Console.WriteLine("Moving files to server");
            MoveStpFilesToServer();
            //StepConverter.Run(_serverStpPath);
            Console.WriteLine("End of STEP export part 1");
            return true;
        }

        private static void PrepareDictionary(List<DbElement> sites)
        {
            var mdbName = MDB.CurrentMDB.Name.Replace("-", string.Empty);
            foreach (var site in sites)
            {
                var siteName = site.GetAsString(DbAttributeInstance.FLNN);
                if (site.IsNull || !site.IsValid || !siteName.StartsWith(mdbName))
                    continue;
                try
                {
                    site.SetAttributeDefault(DbAttributeInstance.ORI);
                    site.SetAttributeDefault(DbAttributeInstance.POS);
                }
                catch 
                {
                    if (!site.GetOrientation(DbAttributeInstance.ORI).Equals(Orientation.Create()) &&
                        !site.GetPosition(DbAttributeInstance.POS).Equals(Position.Create())) continue;
                }

                if (siteName.Contains("-SO"))
                {
                    //строители
                    foreach (var zone in site.Members())
                    {
                        var zoneName = zone.GetAsString(DbAttributeInstance.FLNM);
                        if (zoneName.Contains("-SO-KG"))
                        {
                            AddToDictionary("СО КЖ фундаменты", new List<DbElement> { zone });
                        }
                        else if (zoneName.Contains("-SO-AR"))
                        {
                            foreach (var subItem in zone.Members())
                            {
                                AddToDictionary("СО АР " + subItem.GetAsString(DbAttributeInstance.DESC).Trim(),
                                    new List<DbElement> { subItem });
                            }
                        }
                    }
                }
                else
                {
                    DBElementCollection collection;
                    if (siteName.Contains("-SANO"))
                    {
                        //сантехники
                        collection = new DBElementCollection(site, new TypeFilter(DbElementTypeInstance.HVAC));
                        AddToDictionary("САНО воздуховоды", collection.Cast<DbElement>().ToList());
                        collection = new DBElementCollection(site, new TypeFilter(DbElementTypeInstance.PIPE));
                        AddToDictionary("САНО трубопроводы", collection.Cast<DbElement>().ToList());
                        collection = new DBElementCollection(site, new TypeFilter(DbElementTypeInstance.EQUIPMENT));
                        AddToDictionary("САНО оборудование", collection.Cast<DbElement>().ToList());

                        collection = new DBElementCollection(site, new TypeFilter(new[] {DbElementTypeInstance.FLOOR}));
                        AddToDictionary("СО полы", collection.Cast<DbElement>().ToList());
                        collection = new DBElementCollection(site, new TypeFilter(new[] { DbElementTypeInstance.PANEL }));
                        AddToDictionary("СО кровля", collection.Cast<DbElement>().ToList());
                        collection = new DBElementCollection(site, new TypeFilter(new[] { DbElementTypeInstance.GWALL }));
                        AddToDictionary("СО стены", collection.Cast<DbElement>().ToList());
                        collection = new DBElementCollection(site, new TypeFilter(new[] { DbElementTypeInstance.SCTN }));
                        AddToDictionary("СО металлоконструкции", collection.Cast<DbElement>().ToList());
                    }
                    else if (siteName.Contains("-OOR"))
                    {
                        //обогатители
                        collection = new DBElementCollection(site, new TypeFilter(DbElementTypeInstance.PIPE));
                        AddToDictionary("ООР трубопроводы", collection.Cast<DbElement>().ToList());
                        collection = new DBElementCollection(site, new TypeFilter(DbElementTypeInstance.EQUIPMENT));
                        AddToDictionary("ООР оборудование", collection.Cast<DbElement>().ToList());
                    }
                    else if (siteName.Contains("-TO"))
                    {
                        //обогатители
                        collection = new DBElementCollection(site, new TypeFilter(DbElementTypeInstance.PIPE));
                        AddToDictionary("ТО трубопроводы", collection.Cast<DbElement>().ToList());
                        collection = new DBElementCollection(site, new TypeFilter(DbElementTypeInstance.EQUIPMENT));
                        AddToDictionary("ТО оборудование", collection.Cast<DbElement>().ToList());
                    }
                    else if (siteName.Contains("-EO"))
                    {
                        //электрики
                        collection = new DBElementCollection(site, new TypeFilter(DbElementTypeInstance.CWAY));
                        AddToDictionary("ЭО кабельные трассы", collection.Cast<DbElement>().ToList());
                        collection = new DBElementCollection(site, new TypeFilter(DbElementTypeInstance.STRUCTURE));
                        AddToDictionary("ЭО строительные конструкции", collection.Cast<DbElement>().ToList());
                        collection = new DBElementCollection(site, new TypeFilter(DbElementTypeInstance.EQUIPMENT));
                        AddToDictionary("ЭО оборудование", collection.Cast<DbElement>().ToList());
                    }
                    else if (siteName.Contains("-OA"))
                    {
                        //автоматчики
                        collection = new DBElementCollection(site, new TypeFilter(DbElementTypeInstance.CWAY));
                        AddToDictionary("ОА кабельные трассы", collection.Cast<DbElement>().ToList());
                        collection = new DBElementCollection(site, new TypeFilter(DbElementTypeInstance.STRUCTURE));
                        AddToDictionary("ОА строительные конструкции", collection.Cast<DbElement>().ToList());
                        collection = new DBElementCollection(site, new TypeFilter(DbElementTypeInstance.EQUIPMENT));
                        AddToDictionary("ОА оборудование", collection.Cast<DbElement>().ToList());
                    }
                    else if (siteName.Contains("-GMO"))
                    {
                        //механики
                        collection = new DBElementCollection(site, new TypeFilter(DbElementTypeInstance.PIPE));
                        AddToDictionary("ГМО трубопроводы", collection.Cast<DbElement>().ToList());
                        collection = new DBElementCollection(site, new TypeFilter(DbElementTypeInstance.EQUIPMENT));
                        AddToDictionary("ГМО оборудование", collection.Cast<DbElement>().ToList());
                    }
                }
            }
        }

        private static void AddToDictionary(string name, List<DbElement> list)
        {
            if (list.Count == 0) return;
            var elementsToExport = new List<DbElement>();
            foreach (var item in list)
            {
                if (item.GetElementType().Equals(DbElementTypeInstance.EQUIPMENT) && (item.Members().Length == 0 || item.Members(DbElementTypeInstance.GENPRI).Length > 0 || item.GetAsString(DbAttributeInstance.FUNC).Equals("MagiCAD"))) continue;
                elementsToExport.Add(item);
            }
            if (elementsToExport.Count == 0) return;
            elementsToExport.Sort(Comparison);
            if (_exportDictionary.ContainsKey(name))
            {
                _exportDictionary[name].AddRange(elementsToExport);
            }
            else
            {
                _exportDictionary.Add(name, elementsToExport);
            }
        }

        private static int Comparison(DbElement item1, DbElement item2)
        {
            var ref1 = item1.GetAsString(DbAttributeInstance.REF);
            var ref2 = item2.GetAsString(DbAttributeInstance.REF);
            return String.Compare(ref1, ref2, StringComparison.Ordinal);
        }

        private static void RunExportCommands()
        {
            var errorList = new List<string>();
            cmd.CreateCommand("repre level 6").Run();
            cmd.CreateCommand("repre level pipe 6").Run();
            cmd.CreateCommand("repre level nozz 6").Run();
            cmd.CreateCommand("repre level stru 6").Run();
            cmd.CreateCommand("repre rnode off").Run();
            cmd.CreateCommand("repre ratta off").Run();
            cmd.CreateCommand("repre hatta off").Run();
            cmd.CreateCommand("repre obstruct on").Run();
            cmd.CreateCommand("repre cable obstruct on").Run();
            cmd.CreateCommand("repre arctol 1mm").Run();
            cmd.CreateCommand("repre holes on").Run();
            cmd.CreateCommand("repre update").Run();
            Console.WriteLine("Export from E3D");
            var total = _exportDictionary.Count;
            var current = 0;
            foreach (var item in _exportDictionary)
            {
                current++;
                Console.WriteLine("Current progress: {0}/{1}   ", current, total);
                cmd.CreateCommand("export system /explantmcad").Run();

                var modelName = Path.Combine(_localStpPath, item.Key);
                if (modelName.Contains("оборудование") && !modelName.Contains("ЭО "))
                {
                    foreach (var element in item.Value)
                    {
                        try
                        {
                            if (!element.GetBool(DbAttributeInstance.ISNAME))
                            {
                                element.SetAttribute(DbAttributeInstance.NAME, "/" +
                                    element.GetAsString(DbAttributeInstance.REF)
                                        .Replace("=", string.Empty)
                                        .Replace(@"/", "-"));
                                Console.WriteLine("element {0} named as {1}",element.GetElementType().Name, element.GetAsString(DbAttributeInstance.FLNM));
                            }
                        }
                        catch
                        {
                            Console.WriteLine("can't rename {0}", element.GetElementType().Name);
                        }
                        

                        var eName = element.GetBool(DbAttributeInstance.ISNAME)
                            ? element.GetAsString(DbAttributeInstance.FLNN)
                            : element.GetAsString(DbAttributeInstance.REF).Replace("=", string.Empty).Replace(@"/", "-");
                        cmd.CreateCommand("export file \"" +
                                          Path.Combine(modelName, eName + ".stp") + "\" over").Run();
                        cmd.CreateCommand("export holes on").Run();
                        cmd.CreateCommand("export " + element.GetAsString(DbAttributeInstance.REF)).Run();
                        var command = cmd.CreateCommand("export finish");
                        if (!command.Run())
                        {
                            errorList.Add(modelName + " " + command.Error.MessageText());
                        }
                    }
                }
                else
                {
                    cmd.CreateCommand("export file \"" +
                                      Path.Combine(modelName, item.Key + ".stp") + "\" over").Run();
                    cmd.CreateCommand("export holes on").Run();

                    foreach (var element in item.Value)
                    {
                        try
                        {
                            if (!element.GetBool(DbAttributeInstance.ISNAME))
                            {
                                element.SetAttribute(DbAttributeInstance.NAME, "/" +
                                    element.GetAsString(DbAttributeInstance.REF)
                                        .Replace("=", string.Empty)
                                        .Replace(@"/", "-"));
                                Console.WriteLine("element {0} named as {1}", element.GetElementType().Name, element.GetAsString(DbAttributeInstance.FLNM));
                            }
                        }
                        catch
                        {
                            Console.WriteLine("can't rename {0}", element.GetElementType().Name);
                        }
                        cmd.CreateCommand("export " + element.GetAsString(DbAttributeInstance.REF)).Run();
                    }
                    var command = cmd.CreateCommand("export finish");
                    if (!command.Run())
                    {
                        errorList.Add(modelName + " " + command.Error.MessageText());
                    }
                }
            }
            Console.WriteLine();
            foreach (var err in errorList)
            {
                Console.WriteLine(err);
            }
        }

        static void ClearLocalDirectory(ZlpDirectoryInfo rootDir)
        {
            if (rootDir.Exists)
            {
                var dirs = rootDir.GetDirectories();
                foreach (var dir in dirs)
                {
                    if (dir.Exists)
                    {
                        dir.Delete(true);
                    }
                }
            }
            else
            {
                rootDir.Create();
            }
        }

        static void MoveStpFilesToServer()
        {
            var serverDir = new ZlpDirectoryInfo(_serverStpPath);
            var localDir = new ZlpDirectoryInfo(_localStpPath);

            if (!serverDir.Exists)
            {
                serverDir.Create();
            }

            var serverFiles = serverDir.GetFiles("*.stp", SearchOption.AllDirectories);
            var localFiles = localDir.GetFiles("*.stp", SearchOption.AllDirectories);

            var distinctFiles = new List<ZlpFileInfo>();
            //отсутствуют в текущем списке, т.е. необходимо удалить с сервера
            foreach (var f in serverFiles)
            {
                var tFile = new ZlpFileInfo(f.FullName.Replace(_serverStpPath, _localStpPath));
                if (!tFile.Exists)
                {
                    distinctFiles.Add(f);
                }
            }

            if (distinctFiles.Count > 0)
            {
                Console.WriteLine(distinctFiles.Count + " files to delete");
                foreach (var f in distinctFiles)
                {
                    try
                    {
                        Console.WriteLine("Delete file from server " + f.Name);
                        f.Delete();
                    }
                    catch
                    {
                        Console.WriteLine("Can't delete " + f.Name);
                    }
                }
            }

            foreach (var f in localFiles)
            {
                var tFile = new ZlpFileInfo(f.FullName.Replace(_localStpPath, _serverStpPath));

                if (tFile.Exists && IsFileEquals(f.FullName, tFile.FullName)) continue;

                try
                {
                    Console.WriteLine((tFile.Exists ? "Replace file " : "Insert file ") + tFile.Name);
                    var tFilePath = tFile.FullName;
                    var tFileDirectory = tFile.Directory;
                    if (tFile.Exists)
                    {
                        tFile.Delete();
                    }
                    if (!tFileDirectory.Exists)
                    {
                        tFileDirectory.Create();
                    }
                    f.MoveTo(tFilePath);
                }
                catch{}
            }
        }

        static bool IsFileEquals(string file1, string file2)
        {
            var result = false;
            var strings1 = new List<string>();
            var strings2 = new List<string>();
            using (var str1 = new StreamReader(file1, Encoding.Default))
            {
                string currentLine;
                while ((currentLine = str1.ReadLine()) != null)
                {
                    if (currentLine.Contains("FILE_NAME") || currentLine.Contains("CALENDAR_DATE") ||
                        currentLine.Contains("LOCAL_TIME")) continue;
                    strings1.Add(currentLine);
                }
                str1.Close();
            }

            using (var str1 = new StreamReader(file2, Encoding.Default))
            {
                string currentLine;
                while ((currentLine = str1.ReadLine()) != null)
                {
                    if (currentLine.Contains("FILE_NAME") || currentLine.Contains("CALENDAR_DATE") ||
                        currentLine.Contains("LOCAL_TIME")) continue;
                    strings2.Add(currentLine);
                }
                str1.Close();
            }

            if (strings1.Count != strings2.Count) return result;
            for (var i = 0; i < strings1.Count; i++)
            {
                if (strings1[i] != strings2[i])
                {
                    result = false;
                    break;
                }
                if (i == strings1.Count - 1)
                {
                    result = true;
                }
            }
            return result;
        }
    }
}
