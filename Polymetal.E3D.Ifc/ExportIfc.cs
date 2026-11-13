using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
using System.Runtime;
using System.Text;
using System.Threading;
using cmd = Aveva.Core.Utilities.CommandLine.Command;
using TypeFilter = Aveva.Core.Database.Filters.TypeFilter;
using Aveva.Core.PMLNet;
using Aveva.Core.Database;
using Aveva.Core.Database.Filters;
using Aveva.Core.Maths.Geometry;
using System.Text.RegularExpressions;
using Polymetal.Pdms.Common.DataBaseUtilites;

namespace Polymetal.E3D.Ifc
{
    [PMLNetCallable()]
    public class ExportIfc
    {
        internal static List<string[]> ColorRules;
        private const double ARCTOLERANCE = 10.0;
        private const int OBST_TRANSLUCENCY = 75;
        internal const int REPRE_LEVEL = 6;
        internal static Dictionary<string, DbExpression> ExpressionsDict = new Dictionary<string, DbExpression>();
        internal static Dictionary<string,DbExpression> ExpressionDictionary = new Dictionary<string, DbExpression>();
        public static Dictionary<DbElement, int> ElementsCustomColours = new Dictionary<DbElement, int>();
        internal static string ConnectionString = Polymetal.Core.Common.SQL.ConnectionString;
        private Dictionary<string, string> _marksDictionary;
        private Dictionary<string, string> _departmentsDictionary;
        private Dictionary<string, string> _functionsDictionary;
        private string _modelTransformation;

        private static Helpers.Enumerators.ConsumerEnum _consumer;

        public static Helpers.Enumerators.ConsumerEnum Consumer
        {
            get => _consumer;
            set => _consumer = value;
        }

        public static AvevaProjectDataClass AvevaPrjData = GetProjectDataClass.GetAvevaProjectInfo();

        public static string PathToExtraAtrributesFiles => Properties.Resources.pathToExtraAtrributesFiles;


        private static byte _structureVersion;

        private static byte StructureVersion
        {
            get
            {
                if (_structureVersion == 0)
                {
                    using (var sqlConnection = new SqlConnection(ConnectionString))
                    {
                        sqlConnection.Open();
                        var cmd = $"select StructureVersion from vPdmsProjects where Project = '{Project.CurrentProject.Name}' and MDB = '{MDB.CurrentMDB.Name}'";
                        using (var sqlCmd = new SqlCommand(cmd, sqlConnection))
                        {
                            var res = sqlCmd.ExecuteScalar();
                            if (res != null)
                            {
                                _structureVersion = (byte) res;
                            }
                        }
                        sqlConnection.Close();
                    }
                }

                return _structureVersion;
            }
        }

        [PMLNetCallable()]
        public ExportIfc()
        {
        }

        [PMLNetCallable()]
        public void Assign(ExportIfc that)
        {
            
        }

        [PMLNetCallable()]
        public void ExportTest()
        {
            ExportReferenceModels(@"C:\AVEVA\temp\E3D\IfcModels", "1 0 0 0 1 0 0 0 1 0 -13500.0 0");
        }

        [PMLNetCallable()]
        public void ExportCurrentElement()
        {           
            var dictionary = new Dictionary<string, List<DbElement>>();
            var item = CurrentElement.Element;
            var itemName = item.GetBool(DbAttributeInstance.ISNAME)
                ? item.GetAsString(DbAttributeInstance.FLNN)
                : $"{item.ElementType.ShortName.ToUpper()}_{item.RefNo()[0]}_{item.RefNo()[1]}";
            var directoryPath = Path.Combine(new[] {
                Environment.GetEnvironmentVariable("AVEVA_DESIGN_USER"),
                "ExportedModels",
                Project.CurrentProject.Name,
                MDB.CurrentMDB.Name});

            var path = Path.Combine(directoryPath, $"{itemName}.ifc");
            dictionary.Add(path, new List<DbElement> {item});

            var exportData = new Dictionary<string, Dictionary<DbElement, List<DbElement>>>();
            foreach (var pair in dictionary)
            {
                var rootDictionary = new Dictionary<DbElement, List<DbElement>>();
                foreach (var dbItem in pair.Value)
                {
                    if (dbItem.IsNull) continue;

                    var root = dbItem.EvaluateElement(GetOrAddExpression("SITE"));
                    if (!rootDictionary.ContainsKey(root))
                        rootDictionary.Add(root, new List<DbElement>());
                    var children = CollectElements(dbItem);
                    foreach (var child in children)
                    {
                        if (rootDictionary[root].Contains(child)) continue;
                        rootDictionary[root].Add(child);
                    }
                }
                exportData.Add(pair.Key, rootDictionary);
            }

            Run(exportData);

            Process.Start(directoryPath);
        }

        [PMLNetCallable()]
        public void ExportMembersOfCurrentElement()
        {            
            var ce = CurrentElement.Element;
            var ceName = ce.GetBool(DbAttributeInstance.ISNAME)
                    ? ce.GetAsString(DbAttributeInstance.FLNN)
                    : $"{ce.ElementType.ShortName.ToUpper()}_{ce.RefNo()[0]}_{ce.RefNo()[1]}";
            foreach (var item in ce.Members())
            {
                var dictionary = new Dictionary<string, List<DbElement>>();
                var itemName = item.GetBool(DbAttributeInstance.ISNAME)
                    ? item.GetAsString(DbAttributeInstance.FLNN)
                    : $"{item.ElementType.ShortName.ToUpper()}_{item.RefNo()[0]}_{item.RefNo()[1]}";
                var path = Path.Combine(Environment.GetEnvironmentVariable("AVEVA_DESIGN_USER"), $"{Project.CurrentProject.Name}\\{MDB.CurrentMDB.Name}\\{itemName}.ifc");
                dictionary.Add(path, new List<DbElement> { item });

                var exportData = new Dictionary<string, Dictionary<DbElement, List<DbElement>>>();
                foreach (var pair in dictionary)
                {
                    var rootDictionary = new Dictionary<DbElement, List<DbElement>>();
                    foreach (var dbItem in pair.Value)
                    {
                        if (dbItem.IsNull) continue;

                        var root = dbItem.EvaluateElement(GetOrAddExpression("SITE"));
                        if (!rootDictionary.ContainsKey(root))
                            rootDictionary.Add(root, new List<DbElement>());
                        var children = CollectElements(dbItem);
                        foreach (var child in children)
                        {
                            if (rootDictionary[root].Contains(child)) continue;
                            rootDictionary[root].Add(child);
                        }
                    }
                    exportData.Add(pair.Key, rootDictionary);
                }
                Run(exportData);
            }            
        }

        [PMLNetCallable()]
        public void ExportFromFile()
        {
            var items = new List<DbElement>();
            using (var sr = new StreamReader(@"c:\aveva\temp\CurrentDrawList.txt"))
            {
                string line;
                while ((line = sr.ReadLine()) != null)
                {
                    if (string.IsNullOrEmpty(line) || !line.Contains("=")) continue;
                    items.Add(DbElement.GetElement(line.Replace("add", string.Empty).Trim()));
                }
                sr.Close();
            }
            ///DrawListManager.Instance.CurrentDrawList.Add(items.ToArray());
            var exp = new Polymetal.E3D.Ifc.ExportIfc();
            var dict = new Dictionary<string, List<DbElement>>();
            dict.Add(@"c:\aveva\temp\out.ifc", items);
            exp.ExportEngineering(dict);
        }

        [PMLNetCallable()]
        public void ExportEngineering(Dictionary<string, List<DbElement>> input, Dictionary<DbElement, Dictionary<string,string>> task3DProperty = null, DbElement refTransformElement = null, bool debug = false )
        {
            AvevaGeometry.ReferenceTransformElement = refTransformElement;

            var exportData = new Dictionary<string, Dictionary<DbElement, List<DbElement>>>();
            foreach (var pair in input)
            {
                var rootDictionary = new Dictionary<DbElement, List<DbElement>>();
                foreach (var dbItem in pair.Value)
                {
                    if (dbItem.IsNull) continue;

                    var root = dbItem.EvaluateElement(GetOrAddExpression("SITE"));
                    if (!rootDictionary.ContainsKey(root))
                        rootDictionary.Add(root, new List<DbElement>());
                    var children = CollectElements(dbItem);
                    foreach (var child in children)
                    {
                        if (rootDictionary[root].Contains(child)) continue;
                        rootDictionary[root].Add(child);
                    }
                }
                exportData.Add(pair.Key, rootDictionary);
            }

            if (debug)
                Run_DEBUG(exportData, task3DProperty);
            else
                Run(exportData, task3DProperty);

        }
        
        [PMLNetCallable()]
        public List<string> ExportReferenceModels(string modelsPath, string modelTransformation)
        {
            var stopWatch = new Stopwatch();
            stopWatch.Start();
            _marksDictionary = new Dictionary<string, string>();
            _departmentsDictionary = new Dictionary<string, string>();
            _functionsDictionary = new Dictionary<string, string>();
            _modelTransformation = modelTransformation;
            using (var sqlConnection = new SqlConnection(ConnectionString))
            {
                sqlConnection.Open();
                var sqlCommand = "select distinct code=RTRIM(a.DepartmentCode), abbr=RTRIM(a.DepartmentAbbreviation) from Department a";
                using (var sqlCmd = new SqlCommand(sqlCommand, sqlConnection))
                {
                    var reader = sqlCmd.ExecuteReader();
                    while (reader.Read())
                    {
                        var key = (string)reader["code"];
                        if (_departmentsDictionary.ContainsKey(key)) continue;
                        _departmentsDictionary.Add(key, (string)reader["abbr"]);
                    }
                    reader.Close();
                }
                sqlCommand = "select distinct code=RTRIM(a.DocumentMarkCode), abbr=RTRIM(a.DocumentMarkAbbreviation) from DocumentMark a";
                using (var sqlCmd = new SqlCommand(sqlCommand, sqlConnection))
                {
                    var reader = sqlCmd.ExecuteReader();
                    while (reader.Read())
                    {
                        var key = (string)reader["code"];
                        if (_marksDictionary.ContainsKey(key)) continue;
                        _marksDictionary.Add(key, (string)reader["abbr"]);
                    }
                    reader.Close();
                }

                sqlCommand = "select distinct code=RTRIM(a.StructClassInfoCode), abbr=RTRIM(a.StructClassInfoDesc) from StructClassInfo a union " +
                    "select distinct code=RTRIM(b.KMIdentCode), abbr=RTRIM(b.KMIdentDesc) from KMIdent b union " +
                    "select distinct code=RTRIM(c.KGIdentCode), abbr=RTRIM(c.KGIdentDesc) from KGIdent c";
                using (var sqlCmd = new SqlCommand(sqlCommand, sqlConnection))
                {
                    var reader = sqlCmd.ExecuteReader();
                    while (reader.Read())
                    {
                        var key = (string)reader["code"];
                        if (_functionsDictionary.ContainsKey(key)) continue;
                        _functionsDictionary.Add(key, (string)reader["abbr"]);
                    }
                    reader.Close();
                }
                sqlConnection.Close();
            }

            if (!Directory.Exists(modelsPath))
                Directory.CreateDirectory(modelsPath);

            var oldFiles = Directory.GetFiles(modelsPath, "*.*", SearchOption.AllDirectories);
            foreach (var oldFile in oldFiles)
            {
                try
                {
                    File.Delete(oldFile);
                }
                catch
                {
                }
            }

            var exportData = new Dictionary<string, Dictionary<DbElement, List<DbElement>>>();

            var typeFilter = new TypeFilter(DbElementTypeInstance.SITE);
            var purpFilter = new AttributeValueFilter(DbAttributeInstance.PURP, TestOperator.Equals, "JOB");
            //var nameFilter = new AttributeLikeFilter(DbAttributeInstance.FLNN, $"{MDB.CurrentMDB.Name.Replace("-", string.Empty)}-*");
            var andFilter = new AndFilter(new BaseFilter[] { typeFilter, purpFilter });
            var jobSites = new DBElementCollection(MDB.CurrentMDB.GetFirstWorld(DbType.Design), andFilter).Cast<DbElement>().ToList();
            var mdbName = MDB.CurrentMDB.Name.Replace("-", string.Empty);
            foreach (var site in jobSites)
            {
                var siteCode = site.GetAsString(DbAttributeInstance.FLNN).Split('-').FirstOrDefault();
                if (StructureVersion == 2)
                {
                    if (siteCode.Length < 6) 
                        continue;

                    //проверка принадлежности к MDB
                    if (mdbName != $"{siteCode.Substring(2, 2)}{siteCode.Substring(6)}") 
                        continue;
                }
                else if (mdbName != siteCode)
                {
                    continue;
                }

                var children = CollectElements(site);
                foreach (var child in children)
                {
                    var fileName = Path.Combine(modelsPath, $"{GetFileNameForElement(site, child)}.ifc");
                    if (!exportData.ContainsKey(fileName))
                        exportData.Add(fileName, new Dictionary<DbElement, List<DbElement>>());

                    var siteDict = exportData[fileName];
                    if (!siteDict.ContainsKey(site))
                        siteDict.Add(site, new List<DbElement>());

                    siteDict[site].Add(child);
                }
            }

           
            purpFilter = new AttributeValueFilter(DbAttributeInstance.PURP, TestOperator.Equals, "SCAN");
            andFilter = new AndFilter(new BaseFilter[] { typeFilter, purpFilter });
            var scanSites = new DBElementCollection(MDB.CurrentMDB.GetFirstWorld(DbType.Design), andFilter).Cast<DbElement>().ToList();

            foreach (var site in scanSites)
            {                
                foreach (var item in site.Members())
                {
                    var dictionary = new Dictionary<string, List<DbElement>>();
                    var itemName = item.GetBool(DbAttributeInstance.ISNAME)
                        ? item.GetAsString(DbAttributeInstance.FLNN)
                        : $"{item.ElementType.ShortName.ToUpper()}_{item.RefNo()[0]}_{item.RefNo()[1]}";

                    var path = Path.Combine(modelsPath, $"{itemName}.ifc");
                    dictionary.Add(path, new List<DbElement> { item });

                    foreach (var pair in dictionary)
                    {
                        var rootDictionary = new Dictionary<DbElement, List<DbElement>>();
                        foreach (var dbItem in pair.Value)
                        {
                            if (dbItem.IsNull) 
                                continue;

                            var root = dbItem.EvaluateElement(GetOrAddExpression("SITE"));
                            if (!rootDictionary.ContainsKey(root)) 
                                rootDictionary.Add(root, new List<DbElement>());

                            var children = CollectElements(dbItem);
                            foreach (var child in children)
                            {
                                if (rootDictionary[root].Contains(child)) 
                                    continue;

                                rootDictionary[root].Add(child);
                            }
                        }
                        exportData.Add(pair.Key, rootDictionary);
                    }                   
                }
            }

            purpFilter = new AttributeValueFilter(DbAttributeInstance.PURP, TestOperator.Equals, "IMP");
            andFilter = new AndFilter(new BaseFilter[] { typeFilter, purpFilter });
            var impSites = new DBElementCollection(MDB.CurrentMDB.GetFirstWorld(DbType.Design), andFilter).Cast<DbElement>().ToList();

            foreach (var site in impSites)
            {
                var siteCode = site.GetAsString(DbAttributeInstance.FLNN).Split('-').FirstOrDefault();
                if (StructureVersion == 2)
                {
                    if (siteCode.Length < 6)
                        continue;

                    //проверка принадлежности к MDB
                    if (mdbName != $"{siteCode.Substring(2, 2)}{siteCode.Substring(6)}")
                        continue;
                }
                else if (mdbName != siteCode)
                {
                    continue;
                }

                foreach (var item in site.Members())
                {
                    var itemName = item.GetBool(DbAttributeInstance.ISNAME)
                        ? item.GetString(DbAttributeInstance.FLNN)
                        : $"{item.ElementType.ShortName.ToUpper()}_{item.RefNo()[0]}_{item.RefNo()[1]}";
                    
                    var andZoneFilter = new AndFilter(new BaseFilter[] { new TypeFilter(DbElementTypeInstance.ZONE),
                        new AttributeValueFilter(DbAttributeInstance.PURP, TestOperator.Equals, "CONT") });
                    var zonesContactor =  new DBElementCollection(item, andZoneFilter).Cast<DbElement>().ToList();

                    if (!itemName.Contains("-PMEKZ") && !itemName.Contains("-GMOKZ") && zonesContactor.Count == 0) 
                        continue;

                    var path = Path.Combine(modelsPath, $"{itemName}.ifc");

                    var children = CollectElements(item);

                    var rootDictionary = new Dictionary<DbElement, List<DbElement>> { { site, children } };
                    exportData.Add(path, rootDictionary);
                }
            }

            purpFilter = new AttributeValueFilter(DbAttributeInstance.PURP, TestOperator.Equals, "TASK");
            andFilter = new AndFilter(new BaseFilter[] { typeFilter, purpFilter });
            var taskSites = new DBElementCollection(MDB.CurrentMDB.GetFirstWorld(DbType.Design), andFilter).Cast<DbElement>().ToList();
            foreach (var site in taskSites)
            {
                var siteCode = site.GetString(DbAttributeInstance.FLNN).Split('-').FirstOrDefault();
                if (StructureVersion == 2)
                {
                    if (siteCode.Length < 6)
                        continue;

                    //проверка принадлежности к MDB
                    if (mdbName != $"{siteCode.Substring(2, 2)}{siteCode.Substring(6)}")
                        continue;
                }
                else if (mdbName != siteCode)
                {
                    continue;
                }

                foreach (var zone in site.Members(DbElementTypeInstance.ZONE))
                {
                    if (!zone.GetString(DbAttributeInstance.FUNC).Equals("EXPORT", StringComparison.OrdinalIgnoreCase))
                    {
                        //проверяем внутренние зоны. по идее нужен рефакторинг
                        foreach (var zoneLevel1 in zone.Members(DbElementTypeInstance.ZONE))
                        {
                            if (!zoneLevel1.GetString(DbAttributeInstance.FUNC).Equals("EXPORT", StringComparison.OrdinalIgnoreCase))
                                continue;

                            var zoneName = zoneLevel1.GetBool(DbAttributeInstance.ISNAME)
                                ? zoneLevel1.GetString(DbAttributeInstance.FLNN)
                                : $"{zoneLevel1.ElementType.ShortName.ToUpper()}_{zone.RefNo()[0]}_{zone.RefNo()[1]}";

                            var path = Path.Combine(modelsPath, $"{zoneName}.ifc");

                            var children = CollectElements(zoneLevel1);

                            var rootDictionary = new Dictionary<DbElement, List<DbElement>> { { site, children } };
                            exportData.Add(path, rootDictionary);
                        }
                    }
                    else
                    {
                        var zoneName = zone.GetBool(DbAttributeInstance.ISNAME)
                            ? zone.GetString(DbAttributeInstance.FLNN)
                            : $"{zone.ElementType.ShortName.ToUpper()}_{zone.RefNo()[0]}_{zone.RefNo()[1]}";

                        var path = Path.Combine(modelsPath, $"{zoneName}.ifc");

                        var children = CollectElements(zone);

                        var rootDictionary = new Dictionary<DbElement, List<DbElement>> { { site, children } };
                        exportData.Add(path, rootDictionary);
                    }
                }
            }

            Run(exportData);
            stopWatch.Stop();

            cmd.CreateCommand($"$P{stopWatch.Elapsed:hh\\:mm\\:ss}").Run();         
            
            return exportData.Keys.ToList();
        }

        /// <summary>
        /// Определение имени файла, в которой будет выгружен элемент
        /// </summary>
        /// <param name="site"></param>
        /// <param name="item"></param>
        /// <returns></returns>
        private string GetFileNameForElement(DbElement site, DbElement item)
        {
            var department = GetDepartmentFromName(site.GetAsString(DbAttributeInstance.FLNN));
            var owners = item.GetElementArray(DbAttributeInstance.OWNLST);

            //если среди владельцев есть STRU и отдел из архитектурно строительного управления, то берем STRU. Иначе берем ближайший к элементу ZONE
            var ownerItem = (department == "СО" || department == "ОКЖ" || department == "ОКМ" || department == "ОАР") &&
                            owners.Any(x => x.ElementType == DbElementTypeInstance.STRUCTURE)
                ? owners.First(x => x.ElementType == DbElementTypeInstance.STRUCTURE)
                : owners.Last(x => x.ElementType == DbElementTypeInstance.ZONE);
            var ownerName = ownerItem.GetAsString(DbAttributeInstance.FLNN);

            //добавление шифра только для проекта АГМК ГМЦ№2 и CIL, т.к. внутри сайта зоны с разными шифрмами
            var divCode =
                Project.CurrentProject.Name == "AMU" &&
                (MDB.CurrentMDB.Name == "02-210-00" || MDB.CurrentMDB.Name == "02-211-00")
                    ? ownerName.Split('-').First()
                    : string.Empty;

            var mark = GetMarkFromName(ownerName);

            var area = GetAreaFromName(department, ownerName);

            var func = string.Empty;
            switch (department)
            {
                case "ОАР":
                    func = GetOARFunction(ownerName);
                    break;
                case "ОКМ":
                    func = GetOKMFunction(item);
                    break;
                case "ОКЖ":
                    func = GetOKGFunction(ownerName);
                    break;
            }

            //получение функционального назначения по умолчанию, в том числе, если не удалось определить его для ОАР и ОКМ
            if (string.IsNullOrEmpty(func))
            {
                var funcCode = ownerName.Split(StructureVersion == 1 ? '_' : '-').Last();
                //у ЭО дописана высотная отметка в имени зоны, она не нужна
                if (department == "ЭО")
                {
                    funcCode = funcCode.Split('_').First();
                }
                func = _functionsDictionary.ContainsKey(funcCode)
                    ? _functionsDictionary[funcCode]
                    : ownerItem.GetString(DbAttributeInstance.DESC);
            }

            func = func.Trim().ToLower().Replace("other", "прочее").Replace("/", string.Empty)
                .Replace("\\", string.Empty);
            //добавление постфикса для элементов из MagiCAD
            func = item.ElementType == DbElementTypeInstance.EQUIPMENT &&
                   item.GetAsString(DbAttributeInstance.FUNC).Equals("MagiCAD")
                ? $"{func} MagiCAD"
                : func;

            return $"{divCode} {department} {mark} {area} {func}".Replace("  ", " ").Trim();
        }

        /// <summary>
        /// Получение участка/системы из имени
        /// </summary>
        /// <param name="department"></param>
        /// <param name="name"></param>
        /// <returns></returns>
        private string GetAreaFromName(string department, string name)
        {
            //в строительных сайтах нет информации
            if (department == "СО" || department == "ОКЖ" || department == "ОКМ" || department == "ОАР")
                return string.Empty;

            switch (StructureVersion)
            {
                case 1:
                {
                    var data = name.Split('_');
                    return (department == "ТО" || department == "ЭО") && data.Length > 1 ? data[1] : string.Empty;
                }
                case 2:
                {
                    var data = name.Split('-');
                    return data.Length > 3 ? data[3] : string.Empty;
                }
                default:
                    return string.Empty;
            }
        }

        /// <summary>
        /// Получение функционального назначения для ОКМ
        /// </summary>
        /// <param name="name"></param>
        /// <param name="item"></param>
        /// <returns></returns>
        private string GetOKMFunction(DbElement item)
        {
            var itemClass=0;
            item.GetValidInteger(Core.Common.UserAttributes.PolyTeklaClass, ref itemClass);

            switch (itemClass)
            {
                case 29:
                case 99:
                    return "детали узлов";
                case 23:
                    return "лестницы";
                case 79:
                    return "навесы";
                case 1:
                case 22:
                case 27:
                case 32:
                case 39:
                    return "настилы";
                case 28:
                case 35:
                case 56:
                case 60:
                    return "оборудование";
                case 13:
                    return "ограждения";
                case 41:
                case 55:
                case 69:
                case 83:
                case 97:
                case 111:
                    return "опорные конструкции";
                case 36:
                case 48:
                    return "перекрытия";
                case 10:
                case 45:
                case 52:
                    return "площадки";
                case 3:
                case 17:
                case 18:
                case 20:
                case 24:
                case 31:
                case 38:
                    return "подкрановые пути";
                case 4:
                case 6:
                case 7:
                case 11:
                case 14:
                case 25:
                case 34:
                case 37:
                case 42:
                case 46:
                case 65:
                    return "покрытие";
                case 68:
                case 70:
                    return "пролетные строения";
                case 5:
                case 8:
                    return "связи";
                case 2:
                case 9:
                case 51:
                    return "стойки, колонны";
                case 21:
                    return "фахверк внутренний";
                case 12:
                    return "фахверк наружный";
                default:
                    return string.Empty;
            }
        }

        /// <summary>
        /// Получение функционального назначения для ОКЖ
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        private string GetOKGFunction(string name)
        {
            var funcCode = name.Split('_').Last();
            
            return _functionsDictionary.ContainsKey(funcCode)
                ? _functionsDictionary[funcCode]
                : string.Empty;
        }

        /// <summary>
        /// Получение функционального назначения для ОАР
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        private string GetOARFunction(string name)
        {
            var funcCode = name.Split('_').Last();

            switch (funcCode)
            {
                case "STAIR":
                case "RAILING":
                    return "лестницы, ограждения";
                case "ROOF":
                case "SLAB":
                case "COVERING":
                    return "перекрытия, кровля";
                case "WALL":
                case "WINDOW":
                case "DOOR":
                case "CURTAIN":
                    return "стены, окна, двери";
                case "WALLTEMP":
                    return "стены КМ-КЖ";
                case "SLABTEMP":
                    return "перекрытия КМ-КЖ";
                default:
                    return string.Empty;
            }
        }

        /// <summary>
        /// Получение марки из имени элемента
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        private string GetMarkFromName(string name)
        {
            var mark = name.Split('-').Length > 2 ? name.Split('-')[2] : "X";
            if (mark.Contains("_")) mark = mark.Split('_').First();
            if (_marksDictionary.ContainsKey(mark)) return _marksDictionary[mark];

            //марки могут быть с индексами, код необходимо отделить для последующей замены кода на имя марки и добавления индекса
            var code = Regex.Replace(mark, @"[\d-_\.]", string.Empty);
            return _marksDictionary.ContainsKey(code) ? mark.Replace(code, _marksDictionary[code]) : mark;
        }

        /// <summary>
        /// Получени отдела из имени элемента
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        private string GetDepartmentFromName(string name)
        {
            var nameData = name.Split('-');
            if (nameData.Length < 2) return name;
            var department = name.Split('-').Last().Replace("OOR","TO");
            return _departmentsDictionary.ContainsKey(department) ? _departmentsDictionary[department] : department;
        }

        private const long MemoryCollectThreshold = 800L * 1024 * 1024;
        private static readonly char[] WriteBuffer = new char[8192];

        private static string DescribeElement(DbElement item)
        {
            try
            {
                if (item.IsNull)
                    return "element=null";

                var type = item.ElementType.ShortName;
                var hasName = item.GetBool(DbAttributeInstance.ISNAME);
                var name = hasName ? item.GetAsString(DbAttributeInstance.FLNN) : string.Empty;
                var fullName = hasName ? item.GetAsString(DbAttributeInstance.FLNM) : string.Empty;
                var reference = item.GetAsString(DbAttributeInstance.REF);
                return $"type={type} name={name} flnm={fullName} ref={reference}";
            }
            catch (Exception exc)
            {
                return $"element=describe-failed {exc.GetType().Name}";
            }
        }

        private static void LogElementError(string file, int index, int count, string elementText, Exception exc)
        {
            try
            {
                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\tExportIfc.Run\t{exc.GetType().FullName}\tfile={file}\tindex={index + 1}/{count}\t{elementText}\t{exc.Message}";
                File.AppendAllText(@"c:\aveva\exchange.error", line + Environment.NewLine);
            }
            catch
            {
            }
        }

        private static void WriteBuilder(StreamWriter writer, StringBuilder sb)
        {
            if (writer == null || sb == null || sb.Length == 0)
                return;

            for (var offset = 0; offset < sb.Length; offset += WriteBuffer.Length)
            {
                var count = Math.Min(WriteBuffer.Length, sb.Length - offset);
                sb.CopyTo(offset, WriteBuffer, 0, count);
                writer.Write(WriteBuffer, 0, count);
            }
        }

        private static void ReleaseBuilder(StringBuilder sb)
        {
            if (sb == null)
                return;

            sb.Clear();
            if (sb.Capacity > 1024)
                sb.Capacity = 1024;
        }

        private static void CollectMemory(bool waitFinalizers)
        {
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect();
            if (!waitFinalizers)
                return;

            GC.WaitForPendingFinalizers();
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect();
        }

        public void Run(Dictionary<string, Dictionary<DbElement, List<DbElement>>> exportDictionary, Dictionary<DbElement,Dictionary<string,string>> task3DPropertyByElement = null)
        {
            var modelMatrix = D3Transform.Create();
            if (!string.IsNullOrEmpty(_modelTransformation))
                modelMatrix =
                    D3Transform.Create(_modelTransformation.Replace(".", ",").Split().Select(double.Parse).ToArray()).Inverse();

            var badSymbols = Path.GetInvalidFileNameChars();
            var currentCulture = Thread.CurrentThread.CurrentCulture;
            Thread.CurrentThread.CurrentCulture = CultureInfo.CreateSpecificCulture("en-GB");
            InitColorRules();
            cmd.CreateCommand("REPRESENTATION HOLE ON").RunInPdms();
            cmd.CreateCommand("REPRESENTATION PNODE OFF").RunInPdms();
            cmd.CreateCommand("REPRESENTATION SNODE OFF").RunInPdms();
            cmd.CreateCommand("REPRESENTATION HATTA OFF").RunInPdms();
            cmd.CreateCommand("REPRESENTATION RATTA OFF").RunInPdms();
            cmd.CreateCommand("REPRESENTATION RNODE OFF").RunInPdms();
            cmd.CreateCommand("REPRESENTATION LEVEL 6").RunInPdms();
            cmd.CreateCommand("REPRESENTATION LEVEL PIPE 6").RunInPdms();
            cmd.CreateCommand("REPRESENTATION LEVEL NOZZ 6").RunInPdms();
            cmd.CreateCommand("REPRESENTATION LEVEL STRU 6").RunInPdms();
            cmd.CreateCommand("REPRESENTATION DARCTOL " + ARCTOLERANCE).Run();
            cmd.CreateCommand("REPRESENTATION OBST ON TRANSLUCENCY " + OBST_TRANSLUCENCY).RunInPdms();
            cmd.CreateCommand("REPRESENTATION CABLEWAY OBSTRUCTION ON").RunInPdms();

            var serverGuid = Guid.NewGuid().ToString();
            var ifcServerProcess = StartIfcServer(serverGuid);
            Thread.Sleep(1000);
            var client = new NamedPipeClientStream($"IfcE3DPipe{serverGuid}");
            client.Connect();
            using (var writer = new StreamWriter(client))
            {
                foreach (var exportPair in exportDictionary)
                {
                    writer.WriteLine("CREATE MODEL");
                    writer.WriteLine("CREATE PROJECT");
                    writer.WriteLine(Project.CurrentProject.Name);
                    foreach (var rootPair in exportPair.Value)
                    {
                        writer.WriteLine("CREATE SITE");
                        writer.WriteLine(rootPair.Key.EvaluateString(GetOrAddExpression("NAME OF SITE")));
                        AvevaGeometry.CurrentSite = rootPair.Key;
                        writer.WriteLine(string.Join(" ", modelMatrix.Multiply(AvevaGeometry.ReferenceCoordinateSystem).AsArray12()));
                        writer.Flush();

                        var itemCount = rootPair.Value.Count;
                        for (var i = 0; i < itemCount; i++)
                        {
                            DbElement item = rootPair.Value[i];
                            var elementText = DescribeElement(item);
                            AvevaGeometry geometryWriter = null;
                            StringBuilder geometryText = null;
                            StringBuilder propertyText = null;
                            try
                            {
                                if (ElementsCustomColours != null && ElementsCustomColours.ContainsKey(item))
                                    geometryWriter = new AvevaGeometry(item, ElementsCustomColours[item]);
                                else
                                    geometryWriter = new AvevaGeometry(item);

                                writer.WriteLine("CREATE PRODUCT");
                                writer.WriteLine(item.GetBool(DbAttributeInstance.ISNAME)
                                    ? item.GetAsString(DbAttributeInstance.FLNM)
                                    : item.GetAsString(DbAttributeInstance.REF));
                                if (item.Owner.ElementType == DbElementTypeInstance.BRANCH)
                                {
                                    writer.WriteLine(item.Owner.Owner.ElementType.ShortName);
                                }
                                else if (item.ElementType == DbElementTypeInstance.EQUIPMENT &&
                                         item.GetAsString(DbAttributeInstance.FUNC).Equals("MagiCAD"))
                                {
                                    writer.WriteLine($"{DbElementTypeInstance.HVAC.ShortName} MCAD");
                                }
                                else
                                {
                                    writer.WriteLine(item.ElementType.ShortName);
                                }
                                writer.WriteLine("GEOMETRY");
                                geometryText = geometryWriter.GetText();
                                WriteBuilder(writer, geometryText);
                                writer.WriteLine("GEOMETRY_END");

                                writer.WriteLine("PROPERTIES");
                                var propertyWriter = new AvevaProperty(item);

                                //for 3DZadanie Revit Tekla
                                if (task3DPropertyByElement != null && task3DPropertyByElement.ContainsKey(item))
                                {
                                    Dictionary<string, string> dictprop = task3DPropertyByElement[item];
                                    propertyText = dictprop.Count > 0
                                        ? propertyWriter.GetText(dictprop)
                                        : propertyWriter.GetText();
                                }
                                else
                                    propertyText = propertyWriter.GetText();

                                WriteBuilder(writer, propertyText);
                                writer.WriteLine("PROPERTIES_END");
                                writer.WriteLine("END");
                                writer.Flush();
                            }
                            catch (Exception exc)
                            {
                                CollectMemory(waitFinalizers: true);
                                LogElementError(exportPair.Key, i, itemCount, elementText, exc);
                                throw new Exception(
                                    $"{exc.GetType().Name} file={exportPair.Key} index={i + 1}/{itemCount} {elementText}",
                                    exc);
                            }
                            finally
                            {
                                geometryWriter = null;
                                ReleaseBuilder(geometryText);
                                ReleaseBuilder(propertyText);
                            }

                            if ((i + 1) % 20 == 0 || GC.GetTotalMemory(false) > MemoryCollectThreshold)
                                CollectMemory(waitFinalizers: false);
                        }
                    }

                    writer.WriteLine("SAVE MODEL");

                    var fileName = exportPair.Key;
                    foreach (var symb in badSymbols)
                    {
                        if (!fileName.Contains(symb)) continue;
                        fileName = fileName.Replace(symb, '_');
                    }

                    writer.WriteLine(exportPair.Key);
                    writer.Flush();
                    CollectMemory(waitFinalizers: true);
                }
                writer.WriteLine("CLOSE");
                writer.Flush();
                writer.Close();
            }

            while (!ifcServerProcess.HasExited)
            {
                Thread.Sleep(500);
            }

            ifcServerProcess.CancelOutputRead();
            ifcServerProcess.OutputDataReceived -= IfcServerProcess_OutputDataReceived;
            Thread.CurrentThread.CurrentCulture = currentCulture;
        }

        public void Run_DEBUG(Dictionary<string, Dictionary<DbElement, List<DbElement>>> exportDictionary, Dictionary<DbElement, Dictionary<string, string>> task3DPropertyByElement = null)
        {
            var modelMatrix = D3Transform.Create();
            if (!string.IsNullOrEmpty(_modelTransformation))
                modelMatrix =
                    D3Transform.Create(_modelTransformation.Replace(".", ",").Split().Select(double.Parse).ToArray()).Inverse();

            var badSymbols = Path.GetInvalidFileNameChars();
            var currentCulture = Thread.CurrentThread.CurrentCulture;
            Thread.CurrentThread.CurrentCulture = CultureInfo.CreateSpecificCulture("en-GB");
            InitColorRules();
            cmd.CreateCommand("REPRESENTATION HOLE ON").RunInPdms();
            cmd.CreateCommand("REPRESENTATION PNODE OFF").RunInPdms();
            cmd.CreateCommand("REPRESENTATION SNODE OFF").RunInPdms();
            cmd.CreateCommand("REPRESENTATION HATTA OFF").RunInPdms();
            cmd.CreateCommand("REPRESENTATION RATTA OFF").RunInPdms();
            cmd.CreateCommand("REPRESENTATION RNODE OFF").RunInPdms();
            cmd.CreateCommand("REPRESENTATION LEVEL 6").RunInPdms();
            cmd.CreateCommand("REPRESENTATION LEVEL PIPE 6").RunInPdms();
            cmd.CreateCommand("REPRESENTATION LEVEL NOZZ 6").RunInPdms();
            cmd.CreateCommand("REPRESENTATION LEVEL STRU 6").RunInPdms();
            cmd.CreateCommand("REPRESENTATION DARCTOL " + ARCTOLERANCE).Run();
            cmd.CreateCommand("REPRESENTATION OBST ON TRANSLUCENCY " + OBST_TRANSLUCENCY).RunInPdms();
            cmd.CreateCommand("REPRESENTATION CABLEWAY OBSTRUCTION ON").RunInPdms();

            var serverGuid = Guid.NewGuid().ToString();
            //var ifcServerProcess = StartIfcServer(serverGuid);
            Thread.Sleep(1000);
            //var client = new NamedPipeClientStream($"IfcE3DPipe{serverGuid}");
            //client.Connect();
            using (var writer = new StreamWriter($"C:\\Test\\IfcFiles\\{DateTime.Now.ToString("t").Replace(":", "#")}_input.log"))
            {
                foreach (var exportPair in exportDictionary)
                {
                    writer.WriteLine("CREATE MODEL");
                    writer.WriteLine("CREATE PROJECT");
                    foreach (var rootPair in exportPair.Value)
                    {
                        writer.WriteLine("CREATE SITE");
                        writer.WriteLine(rootPair.Key.EvaluateString(GetOrAddExpression("NAME OF SITE")));
                        AvevaGeometry.CurrentSite = rootPair.Key;
                        writer.WriteLine(string.Join(" ", modelMatrix.AsArray12()));
                        writer.Flush();

                        var itemCount = rootPair.Value.Count;
                        for (var i = 0; i < itemCount; i++)
                        {
                            var item = rootPair.Value[i];

                            AvevaGeometry geometryWriter;
                            if (ElementsCustomColours != null && ElementsCustomColours.ContainsKey(item))
                                geometryWriter = new AvevaGeometry(item, ElementsCustomColours[item]);
                            else
                                geometryWriter = new AvevaGeometry(item);


                            var sb = new StringBuilder();
                            sb.AppendLine("CREATE PRODUCT");
                            sb.AppendLine(item.GetBool(DbAttributeInstance.ISNAME)
                                ? item.GetAsString(DbAttributeInstance.FLNM)
                                : item.GetAsString(DbAttributeInstance.REF));
                            if (item.Owner.ElementType == DbElementTypeInstance.BRANCH)
                            {
                                sb.AppendLine(item.Owner.Owner.ElementType.ShortName);
                            }
                            else if (item.ElementType == DbElementTypeInstance.EQUIPMENT &&
                                     item.GetAsString(DbAttributeInstance.FUNC).Equals("MagiCAD"))
                            {
                                sb.AppendLine($"{DbElementTypeInstance.HVAC.ShortName} MCAD");
                            }
                            else
                            {
                                sb.AppendLine(item.ElementType.ShortName);
                            }
                            sb.AppendLine("GEOMETRY");
                            sb.Append(geometryWriter.GetText());
                            sb.AppendLine("GEOMETRY_END");

                            sb.AppendLine("PROPERTIES");
                            var propertyWriter = new AvevaProperty(item);

                            //for 3DZadanie Revit Tekla
                            if (task3DPropertyByElement != null && task3DPropertyByElement.ContainsKey(item))
                            {
                                Dictionary<string, string> dictprop = task3DPropertyByElement[item];
                                sb.Append(dictprop.Count > 0
                                    ? propertyWriter.GetText(dictprop)
                                    : propertyWriter.GetText());
                            }
                            else
                                sb.Append(propertyWriter.GetText());

                            sb.AppendLine("PROPERTIES_END");
                            sb.AppendLine("END");
                            writer.Write(sb);
                            writer.Flush();
                        }
                    }

                    writer.WriteLine("SAVE MODEL");

                    var fileName = exportPair.Key;
                    foreach (var symb in badSymbols)
                    {
                        if (!fileName.Contains(symb)) continue;
                        fileName = fileName.Replace(symb, '_');
                    }

                    writer.WriteLine(exportPair.Key);
                    writer.Flush();
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                }
                writer.WriteLine("CLOSE");
                writer.Flush();
                writer.Close();
            }

            //while (!ifcServerProcess.HasExited)
            //{
            //    Thread.Sleep(500);
            //}

            //ifcServerProcess.CancelOutputRead();
            //ifcServerProcess.OutputDataReceived -= IfcServerProcess_OutputDataReceived;
            Thread.CurrentThread.CurrentCulture = currentCulture;
        }
        
        private Process StartIfcServer(string serverGuid)
        {
            var path = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            path = Path.Combine(path, "Polymetal.E3D.IfcServer.exe");


            var ifcServerProcess = new Process();
            ifcServerProcess.StartInfo.FileName = path;
            ifcServerProcess.StartInfo.Arguments = serverGuid;
            ifcServerProcess.StartInfo.UseShellExecute = false;
            ifcServerProcess.StartInfo.RedirectStandardOutput = true;

            ifcServerProcess.EnableRaisingEvents = true;
            ifcServerProcess.Start();
            ifcServerProcess.OutputDataReceived += IfcServerProcess_OutputDataReceived;
            ifcServerProcess.BeginOutputReadLine();
            return ifcServerProcess;
        }

        private void IfcServerProcess_OutputDataReceived(object sender, DataReceivedEventArgs e)
        {

        }

        internal static DbExpression GetOrAddExpression(string input)
        {
            if (ExpressionDictionary.ContainsKey(input))
                return ExpressionDictionary[input];

            var expr = DbExpression.Parse(input);
            ExpressionDictionary.Add(input, expr);
            return expr;
        }

        public static List<DbElement> GetSubElements(DbElement element)
        {
            var children = CollectElements(element);
            
            return children;
        }
        
        public static void AddCustomColoursRange(List<DbElement> elements, ELEMENT_CUSTOM_COLOR color)
        {
            foreach (var element in elements)
            {
                if (!ElementsCustomColours.ContainsKey(element))
                    ElementsCustomColours.Add(element, (int)color);
            }
        }

        public static List<DbElement> CollectElements(DbElement root)
        {
            var res = new List<DbElement>();
            var typeFilter = new TypeFilter(new[]
            {
                DbElementTypeInstance.EQUIPMENT,
                DbElementTypeInstance.VOLMODEL,
                DbElementTypeInstance.GWALL,
                DbElementTypeInstance.FLOOR,
                DbElementTypeInstance.PANEL,
                DbElementTypeInstance.SCTN,
                DbElementTypeInstance.CWBRAN,
                DbElementTypeInstance.CABLE,
                DbElementTypeInstance.BRANCH,
                DbElementTypeInstance.GENSEC,
                DbElementTypeInstance.STRFLT,
                DbElementTypeInstance.HANDRA,
                DbElementTypeInstance.LINDIMENSION
            });

            var collection = new DBElementCollection(root, typeFilter).Cast<DbElement>().ToList();
            foreach (var item in collection)
            {
                if (item.ElementType == DbElementTypeInstance.BRANCH)
                {
                    res.AddRange(item.Members());
                }
                else if (item.ElementType == DbElementTypeInstance.LINDIMENSION)
                {
                    res.Add(item);
                }
                else
                {
                    var ownersTypeList = item.GetElementTypeArray(DbAttributeInstance.AHLIST).ToList();
                    if (ownersTypeList.Contains(DbElementTypeInstance.EQUIPMENT) || ownersTypeList.Contains(DbElementTypeInstance.STRFLT)) 
                        continue;

                    res.Add(item);
                }
            }

            return res;
        }

        public static void InitColorRules()
        {
            ColorRules = new List<string[]>();
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Polymetal.E3D.Ifc.Resources.rules.txt"))
            {
                using (var sr = new StreamReader(stream))
                {
                    string line;
                    while ((line = sr.ReadLine()) != null)
                    {
                        if (string.IsNullOrEmpty(line)) continue;
                        line = line.ToUpper();
                        var rule = new string[3];
                        var split = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        for (var i = 0; i < split.Length; i++)
                        {
                            if (i == 0)
                            {
                                if (split[i] == "ALL") continue;
                                rule[0] = split[i];
                            }
                            else if (i == split.Length - 1 || i == split.Length - 2)
                            {
                                rule[2] += split[i] + " ";
                            }
                            else
                            {
                                if (split[i] == "WITH") continue;
                                rule[1] += split[i] + " ";
                            }
                        }
                        rule[0] = string.IsNullOrEmpty(rule[0]) ? "CE" : rule[0].Trim();
                        rule[1] = string.IsNullOrEmpty(rule[1]) ? "TRUE" : rule[1].Trim();
                        rule[2] = rule[2].Replace("COLOUR", string.Empty).Trim();
                        ColorRules.Add(rule);
                    }
                    sr.Close();
                }
            }
        }

        private static Dictionary<string, string> InitDepartmentDict()
        {
            var dict = new Dictionary<string, string>();
            using (var sqlConnection = new SqlConnection(ConnectionString))
            {
                sqlConnection.Open();
                var sqlCommand = "select RTRIM(DepartmentCode), RTRIM(DepartmentAbbreviation) from Department";
                using (var sqlCmd = new SqlCommand(sqlCommand, sqlConnection))
                {
                    var reader = sqlCmd.ExecuteReader();
                    while (reader.Read())
                    {
                        dict.Add(reader[0].ToString(), reader[1].ToString());
                    }
                    reader.Close();
                }
                sqlConnection.Close();
            }

            if (!dict.ContainsKey("OOR")) dict.Add("OOR", "ООР");
            return dict;
        }

        private static Dictionary<string, string> InitStructClassInfoDict()
        {
            var dict = new Dictionary<string, string>();
            using (var sqlConnection = new SqlConnection(ConnectionString))
            {
                sqlConnection.Open();
                var sqlCommand = "USE Aveva_Poly_Db " +
                                 "SELECT DISTINCT " +
                                 "  RTRIM(UPPER(StructClassInfoCode)), " +
                                 "  RTRIM(LOWER(StructClassInfoDesc)) " +
                                 "FROM " +
                                 "  StructClassInfo";
                using (var sqlCmd = new SqlCommand(sqlCommand, sqlConnection))
                {
                    var reader = sqlCmd.ExecuteReader();
                    while (reader.Read())
                    {
                        dict.Add(reader[0].ToString(), reader[1].ToString());
                    }
                    reader.Close();
                }
                sqlConnection.Close();
            }
            return dict;
        }        
    }
}
