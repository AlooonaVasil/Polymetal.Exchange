using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using Aveva.Core.Database;
using Aveva.Core.Geometry;
using Aveva.Core.Database.Filters;
using Polymetal.Exchange.Utilities;
using cmd = Aveva.Core.Utilities.CommandLine.Command;
using Polymetal.E3D.Ifc;
using TypeFilter = Aveva.Core.Database.Filters.TypeFilter;
using System.Data;

namespace Polymetal.Exchange.E3D
{
    public class Export
    {
        private static string _dwgModelsPath;
        private const string ERROR_PATH = @"c:\aveva\exchange.error";
        private static Dictionary<DbElementType, List<string[]>> _ruleDictionary = new Dictionary<DbElementType, List<string[]>>();
        private static string _acadTranslator = "AcTranslators.exe";
        private static string _acadAccoreconsole = "accoreconsole.exe";
        //https://knowledge.autodesk.com/customer-service/download-install/activate/find-serial-number-product-key/product-key-look/2019-product-keys        
        private static string _PRODUCT_KEY = "001K1";
        private static string _acadVersion = "2019.0.0.F";
        private static string _licenseTypeAsString = "SA";
        private static string _acadDir = @"C:\Program Files\Autodesk\AutoCAD 2019";
        private static string _cultureName = "ru-RU";
        public static bool ExportFinishFlag = false;
        private static Dictionary<string, string> _structInfoDict;
        private static Dictionary<string, string> _arStructInfo;
        private static Dictionary<string, string> _kgStructInfo;
        private static Dictionary<string, string> _kmStructInfo;
        private static Dictionary<string, string> _documentMarkInfo;
        private static Dictionary<string, string> _departmentInfo;
        private static ProjectInformation _projectInfo;
        private static int _triggerId;
        private static Dictionary<string, DbExpression> ExpressionsDict = new Dictionary<string, DbExpression>();

        private static List<DbElementType> _dirTypes = new List<DbElementType>()
        {
            DbElementTypeInstance.ZONE,
            DbElementTypeInstance.SBFRAMEWORK,
            DbElementTypeInstance.FRMWORK,
            DbElementTypeInstance.STRUCTURE
        };

        private static List<DbElementType> OneByOneTypes = new List<DbElementType>()
        {
            DbElementTypeInstance.EQUIPMENT,
            DbElementTypeInstance.PIPE,
            DbElementTypeInstance.HVAC
        };

        public static bool Run(int triggerId = 0)
        {
            if (RunE3D.ProjectInfo == null && triggerId > 0)
            {
                _triggerId = triggerId;
                Common.GetTriggerInfoById(_triggerId, out decimal projectId, out Common.Source source, out decimal status);
                _projectInfo = new ProjectInformation(projectId);
            }
            else
            {
                _projectInfo = RunE3D.ProjectInfo;
                _triggerId = RunE3D.TriggerId;
            }

            Console.WriteLine($"Process {Process.GetCurrentProcess().Id}; Export reference models; Project {Project.CurrentProject.Name} {MDB.CurrentMDB.Name}");
            var taskName = $"ExportReferenceModel_{Project.CurrentProject.Name}-{MDB.CurrentMDB.Name.Replace("-", string.Empty)}";
            var taskStartTime = InsertShedulerData(taskName);

            var subObjectId = 0M;
            var ifcDictionary = new Dictionary<string, List<string>>();
            var nwdDictionary = new Dictionary<string, List<string>>();
            var dwgDictionary = new Dictionary<string, List<string>>();
            using (var sqlConnection = new SqlConnection(Common.ConnectionString))
            {
                sqlConnection.Open();
                var sqlCommand = string.Format("select IdSubObject from vPdmsProjects where Project = '{0}' and MDB = '{1}'",
                    Project.CurrentProject.Name,
                    MDB.CurrentMDB.Name);
                using (var sqlCmd = new SqlCommand(sqlCommand, sqlConnection))
                {
                    var value = sqlCmd.ExecuteScalar();
                    if (value != null)
                        subObjectId = (decimal)value;
                }

                if (subObjectId > 0)
                {
                    sqlCommand =
                        string.Format(
                            "select ModelPath, Program, Offset from ExchangeProject where NeedExchange = '1' and SubObjectID = {0}",
                            subObjectId);
                    using (var sqlCmd = new SqlCommand(sqlCommand, sqlConnection))
                    {
                        var reader = sqlCmd.ExecuteReader();
                        while (reader.Read())
                        {
                            var modelPath = reader["ModelPath"].ToString();
                            var splitedPath = modelPath.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).First().Trim();
                            if (!File.Exists(splitedPath) && !Directory.Exists(splitedPath)) continue;
                            var attr = File.GetAttributes(splitedPath);
                            var dir = (attr & FileAttributes.Directory) == FileAttributes.Directory
                                ? splitedPath
                                : Path.GetDirectoryName(splitedPath);

                            if (!Directory.Exists(dir)) continue;

                            var program = reader["Program"].ToString().ToLower().Trim();
                            var modelTransformation = reader["Offset"] == DBNull.Value
                                ? string.Empty
                                : (string) reader["Offset"];

                            if (program == "magicad" && splitedPath.EndsWith(".dwg"))
                            {
                                //включить, если нужна будет выгрузка в AutoCAD
                                //pathDictionary["dwg"].Add(dir);
                            }
                            else if (modelPath.ToLower().Contains("подрядчики"))
                            {
                                continue;
                            }
                            else
                            {
                                //ifc формат выгружается для всех программ
                                if (!ifcDictionary.ContainsKey(modelTransformation))
                                    ifcDictionary.Add(modelTransformation, new List<string>());
                                
                                if (!ifcDictionary[modelTransformation].Contains(dir))
                                    ifcDictionary[modelTransformation].Add(dir);

                                //nwd формат выгружаетсят только для Revit/MagiCAD
                                if (program != "magicad" && program != "revit")
                                    continue;
                                
                                if (!nwdDictionary.ContainsKey(modelTransformation))
                                    nwdDictionary.Add(modelTransformation, new List<string>());

                                if (!nwdDictionary[modelTransformation].Contains(dir))
                                    nwdDictionary[modelTransformation].Add(dir);
                            }
                        }
                    }
                }

                sqlConnection.Close();
            }

            if (!Pdms.Common.PseudoAttributeAddin.PseudoAttributeAddinClass.AddinStarted)
            {
                var pseudoAddin = new Pdms.Common.PseudoAttributeAddin.PseudoAttributeAddinClass();
                pseudoAddin.Start();
            }

            if (ifcDictionary.Count > 0)
            {
                Console.WriteLine("ifc export");
                foreach (var pair in ifcDictionary)
                {
                    var modelTransformation = pair.Key;
                    var files = CreateIfcModels(subObjectId, modelTransformation);
                    Console.WriteLine("move ifc files");
                    foreach (var serverPath in pair.Value)
                        MoveFiles(serverPath, files, Helpers.Enumerators.ExportFormat.ifc.ToString());
                    
                    DeleteFiles(files);
                    if (files.Count > 0) 
                        DeleteDirecory(Path.GetDirectoryName(files.First()));
                }
            }

            if (nwdDictionary.Count > 0)
            {
                Console.WriteLine("nwd export");
                foreach (var pair in nwdDictionary)
                {
                    var modelTransformation = pair.Key;
                    var files = CreateNwdModels(subObjectId, modelTransformation);
                    Console.WriteLine("move nwd files");
                    foreach (var serverPath in pair.Value)
                        MoveFiles(serverPath, files, Helpers.Enumerators.ExportFormat.nwd.ToString());
                    
                    DeleteFiles(files);
                    if (files.Count > 0) 
                        DeleteDirecory(Path.GetDirectoryName(files.First()));
                }
            }

            if (subObjectId != 30 && dwgDictionary.Count > 0)
            {
                Console.WriteLine("dwg export");
                foreach (var pair in dwgDictionary)
                {
                    var offset = pair.Key;
                    var files = CreateDwgModels(subObjectId, offset);
                    Console.WriteLine("move dwg files");
                    foreach (var serverPath in pair.Value)
                        MoveFiles(serverPath, files, Helpers.Enumerators.ExportFormat.dwg.ToString());
                    
                    DeleteFiles(files);
                    if (files.Count > 0) 
                        DeleteDirecory(Path.GetDirectoryName(files.First()));
                }
            }            

            UpdateShedulerEndTime(taskName, taskStartTime);
            cmd.CreateCommand("quit finish").Run();
            return true;
        }

        private static DateTime InsertShedulerData(string taskName)
        {
            var startTime = DateTime.Now;
            using (var sqlConnection = new SqlConnection(Common.ConnectionString))
            {
                sqlConnection.Open();
                if (sqlConnection.State == ConnectionState.Open)
                {
                    string cmd =    "USE Aveva_Poly_Db " +
                                    "INSERT INTO SchedulerTasksLog " +
                                    "(SchedulerTaskName, SchedulerTaskStart) " +
                                    "VALUES (" +
                                    "'" + taskName + "', " +
                                    "'" + startTime + "')";

                    using (SqlCommand sqlCmd = new SqlCommand(cmd, sqlConnection))
                    {
                        sqlCmd.ExecuteNonQuery();
                    }
                }
            }
            return startTime;
        }        

        private static void UpdateShedulerEndTime(string nameTask, DateTime startTime)
        {
            using (var sqlConnection = new SqlConnection(Common.ConnectionString))
            {
                sqlConnection.Open();
                if (sqlConnection.State == ConnectionState.Open)
                {
                    var cmd =   "USE Aveva_Poly_Db " +
                                "UPDATE SchedulerTasksLog " +
                                "SET " +
                                "SchedulerTaskEnd = '" + DateTime.Now + "' " +
                                "WHERE " +
                                "SchedulerTaskName = '" + nameTask + "' AND " +
                                "SchedulerTaskStart = '" + startTime + "'";

                    using (SqlCommand sqlCmd = new SqlCommand(cmd, sqlConnection))
                    {
                        sqlCmd.ExecuteNonQuery();
                    }
                }
            }
        }

        private static List<string> CreateDwgModels(decimal subObjectId, string offset)
        {
            if (!string.IsNullOrEmpty(offset))
            {
                using (var sw = new StreamWriter(ERROR_PATH, true))
                {
                    sw.WriteLine($"failed:\t CreateDwgModels of SubObject: {subObjectId} with offset: {offset}");
                    sw.Close();
                }
            }

            var modelsPath = Path.Combine(@"C:\Aveva\Temp\E3D", "DwgModels");
            _dwgModelsPath = Path.Combine(modelsPath, subObjectId.ToString());
            _structInfoDict = GetStructClassInfo();
            _arStructInfo = GetARStructInfo();
            _kgStructInfo = GetKGStructInfo();
            _kmStructInfo = GetKMStructInfo();
            _departmentInfo = GetDepartmentInfo();
            _documentMarkInfo = GetDocumentMarkInfo();

            if (!Directory.Exists(_dwgModelsPath))
                Directory.CreateDirectory(_dwgModelsPath);
            var oldFiles = Directory.GetFiles(_dwgModelsPath, "*.*", SearchOption.AllDirectories);
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

            ExportToStepFormat();

            return MergeDwgFiles();
        }

        public static void CreateDwgModelsByElements(string path, List<DbElement> items)
        {
            _dwgModelsPath = path;
            _structInfoDict = GetStructClassInfo();
            _arStructInfo = GetARStructInfo();
            _kgStructInfo = GetKGStructInfo();
            _kmStructInfo = GetKMStructInfo();
            _departmentInfo = GetDepartmentInfo();
            _documentMarkInfo = GetDocumentMarkInfo();

            if (!Directory.Exists(_dwgModelsPath))
                Directory.CreateDirectory(_dwgModelsPath);
            var oldFiles = Directory.GetFiles(_dwgModelsPath, "*.*", SearchOption.AllDirectories);
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

            ExportToStepFormat(items);
        }

        private static void ExportToStepFormat(List<DbElement> items)
        {            
            if (!Directory.Exists(_acadDir))
            {
                _acadDir = @"C:\Program Files\Autodesk\AutoCAD 2017";
                _acadVersion = "2017.0.0.F";
            }
            var converterThread = new Thread(RunConverter);
            converterThread.Start();
            InitializeColourDictionary();
            cmd.CreateCommand("repre level 6").Run();
            cmd.CreateCommand("repre level pipe 6").Run();
            cmd.CreateCommand("repre level nozz 6").Run();
            cmd.CreateCommand("repre level stru 6").Run();
            cmd.CreateCommand("repre rnode off").Run();
            cmd.CreateCommand("repre ratta off").Run();
            cmd.CreateCommand("repre hatta off").Run();
            cmd.CreateCommand("repre obstruct on").Run();
            cmd.CreateCommand("repre cable obstruct on").Run();
            cmd.CreateCommand("repre darctol 1mm").Run();
            cmd.CreateCommand("repre holes on").Run();
            cmd.CreateCommand("repre update").Run();

            var macList = new List<string>();
            foreach (var item in items)
            {
                macList.AddRange(ExportToDirectory(item, _dwgModelsPath));
            }
            foreach (var mac in macList)
            {
                Console.WriteLine($"{DateTime.Now.ToShortTimeString()}:\t{mac}");
                if (File.Exists(mac))
                {
                    if (cmd.CreateCommand($"$m/{mac}").Run())
                    {
                        File.Delete(mac);
                    }
                    else
                    {
                        using (var sw = new StreamWriter(ERROR_PATH, true))
                        {
                            sw.WriteLine($"failed:\t{mac}");
                            sw.Close();
                        }
                    }
                }
                else
                {
                    using (var sw = new StreamWriter(ERROR_PATH, true))
                    {
                        sw.WriteLine($"file not found:\t{mac}");
                        sw.Close();
                    }
                }
            }
            ExportFinishFlag = true;
            while (converterThread.IsAlive || Process.GetProcessesByName("AcTranslators").Length > 0)
            {
                Thread.Sleep(5000);
            }
        }

        /// <summary>
        /// Создание моделей в формате IFC
        /// </summary>
        /// <param name="subObjectId"></param>
        /// <returns></returns>
        private static List<string> CreateIfcModels(decimal subObjectId, string modelTransformation)
        {
            var modelsPath = Path.Combine(@"C:\Aveva\Temp\E3D", "IfcModels");
            modelsPath = Path.Combine(modelsPath, subObjectId.ToString());
            if (!Directory.Exists(modelsPath))
                Directory.CreateDirectory(modelsPath);

            var ifcExport = new ExportIfc();
            return ifcExport.ExportReferenceModels(modelsPath, modelTransformation);
        }

        /// <summary>
        /// Создание моделей в формате NWD
        /// </summary>
        /// <param name="subObjectId"></param>
        /// <returns></returns>
        private static List<string> CreateNwdModels(decimal subObjectId, string modelTransformation)
        {
            var modelsPath = Path.Combine(@"C:\Aveva\Temp\E3D", "NwdModels");
            modelsPath = Path.Combine(modelsPath, subObjectId.ToString());
            if (!Directory.Exists(modelsPath))
                Directory.CreateDirectory(modelsPath);

            var nwdExport = new Helpers.NwdExport(modelsPath, modelTransformation);
            var result= nwdExport.ExportReferenceModel();

            foreach (var file in Directory.GetFiles(modelsPath))
            {
                if (result.Contains(file)) continue;
                try
                {
                    File.Delete(file);
                }
                catch 
                { }
            }

            return result;
        }

        #region Выгрузка в STEP
        /// <summary>
        /// Сбор правил для изменения цвета элемента
        /// </summary>
        private static void InitializeColourDictionary()
        {
            var cList = new List<string>();
            var path = Path.Combine(Environment.GetEnvironmentVariable("AVEVA_DESIGN_DFLTS").Split(';').First(),
                "des-element-rules.pmldat");
            using (var sr = new StreamReader(path))
            {
                string line;
                var rule = new RuleObject();
                while ((line = sr.ReadLine()) != null)
                {
                    if (line.Contains("!rule "))
                    {
                        rule = new RuleObject();
                    }
                    else if (line.Contains("!rule.name"))
                    {
                        rule.Name = line.Split('|')[1].Trim();
                    }
                    else if (line.Contains("!rule.rule"))
                    {
                        rule.Rule = line.Split('|')[1].Trim();
                    }
                    else if (line.Contains("!rule.types"))
                    {
                        var types = line.Split('|')[1].Trim();
                        rule.Types = new List<DbElementType>();
                        foreach (var t in types.Split(' '))
                        {
                            rule.Types.Add(DbElementType.GetElementType(t));
                        }
                    }
                    else if (line.Contains("!rule.description"))
                    {
                        rule.Description = line.Split('|')[1].Trim();
                        if (!cList.Contains(rule.Description))
                            cList.Add(rule.Description);
                        if (rule.Rule.Equals("false", StringComparison.OrdinalIgnoreCase) ||
                            string.IsNullOrEmpty(rule.Rule)) continue;
                        foreach (var t in rule.Types)
                        {
                            if (_ruleDictionary.ContainsKey(t))
                            {
                                _ruleDictionary[t].Add(new[] { rule.Rule, rule.Description });
                            }
                            else
                            {
                                _ruleDictionary.Add(t, new List<string[]> { new[] { rule.Rule, rule.Description } });
                            }
                        }
                    }
                }
                sr.Close();
            }
        }

        /// <summary>
        /// Сбор сайтов и выгрузка в step формат
        /// </summary>
        /// <param name="rootDirectory"></param>
        private static void ExportToStepFormat()
        {
            if (!Directory.Exists(_acadDir))
            {
                _acadDir = @"C:\Program Files\Autodesk\AutoCAD 2017";
                _acadVersion = "2017.0.0.F";
            }
            var converterThread = new Thread(RunConverter);
            converterThread.Start();
            InitializeColourDictionary();
            cmd.CreateCommand("repre level 6").Run();
            cmd.CreateCommand("repre level pipe 6").Run();
            cmd.CreateCommand("repre level nozz 6").Run();
            cmd.CreateCommand("repre level stru 6").Run();
            cmd.CreateCommand("repre rnode off").Run();
            cmd.CreateCommand("repre ratta off").Run();
            cmd.CreateCommand("repre hatta off").Run();
            cmd.CreateCommand("repre obstruct on").Run();
            cmd.CreateCommand("repre cable obstruct on").Run();
            cmd.CreateCommand("repre darctol 1mm").Run();
            cmd.CreateCommand("repre holes on").Run();
            cmd.CreateCommand("repre update").Run();

            var typeFilter = new TypeFilter(DbElementTypeInstance.SITE);
            var purpFilter = new AttributeValueFilter(DbAttributeInstance.PURP, TestOperator.Equals, "JOB");
            var nameFilter = new AttributeLikeFilter(DbAttributeInstance.FLNN,
                string.Format("{0}-*", MDB.CurrentMDB.Name.Replace("-", string.Empty)));
            var andFilter = new AndFilter(new BaseFilter[] { typeFilter, purpFilter, nameFilter });
            var jobSites = new DBElementCollection(MDB.CurrentMDB.GetFirstWorld(Aveva.Core.Database.DbType.Design), andFilter).Cast<DbElement>().ToList();
            //TODO: remove
            //jobSites.Clear();
            //jobSites.Add(DbElement.GetElement("/0200200-SANO"));
            //
            var macList = new List<string>();
            foreach (var site in jobSites)
            {
                if (!site.GetOrientation(DbAttributeInstance.ORI).Equals(Orientation.Create()) ||
                    !site.GetPosition(DbAttributeInstance.POS).Equals(Position.Create()))
                {
                    try
                    {
                        site.SetAttributeDefault(DbAttributeInstance.ORI);
                        site.SetAttributeDefault(DbAttributeInstance.POS);
                    }
                    catch (Exception exc)
                    {
                        Common.SendMessage(Common.Source.Pdms, _triggerId,
                            "[StepExport Claimed] " + site.GetAsString(DbAttributeInstance.FLNM) + " " + exc.Message);
                        if (!site.GetOrientation(DbAttributeInstance.ORI).Equals(Orientation.Create()) &&
                            !site.GetPosition(DbAttributeInstance.POS).Equals(Position.Create())) continue;
                    }
                }
                macList.AddRange(ExportToDirectory(site, _dwgModelsPath));
            }
            foreach (var mac in macList)
            {
                Console.WriteLine($"{DateTime.Now.ToShortTimeString()}:\t{mac}");
                if (!File.Exists(mac))
                {
                    using (var sw = new StreamWriter(ERROR_PATH, true))
                    {
                        sw.WriteLine($"file not found:\t{mac}");
                        sw.Close();
                    }
                    continue;
                }

                if (cmd.CreateCommand($"$m/{mac}").Run())
                {
                    File.Delete(mac);
                }
                else
                {
                    using (var sw = new StreamWriter(ERROR_PATH, true))
                    {
                        sw.WriteLine($"failed:\t{mac}");
                        sw.Close();
                    }
                }
            }
            
            ExportFinishFlag = true;
            while (converterThread.IsAlive || Process.GetProcessesByName("AcTranslators").Length>0)
            {
                Thread.Sleep(5000);
            }
        }

        /// <summary>
        /// Создание структуры папок с последующей выгрузкой элементов
        /// </summary>
        /// <param name="item"></param>
        /// <param name="parentPath"></param>
        private static List<string> ExportToDirectory(DbElement item, string parentPath)
        {
            var dirName = GetItemName(item);
            var dirPath = Path.Combine(parentPath, dirName);
            var macList = new List<string>();

            if (item.GetAsString(DbAttributeInstance.FLNM).EndsWith("_CWAY") ||
                item.GetAsString(DbAttributeInstance.FLNM).EndsWith("_CABLE"))
            {
                var eoName =
                    item.GetAsString(DbAttributeInstance.FLNM)
                        .Replace(item.Owner.GetAsString(DbAttributeInstance.FLNM), string.Empty).Substring(1);
                var eoStruct = eoName.Split('_');
                foreach (var part in eoStruct)
                {
                    dirPath = Path.Combine(parentPath, part);
                    if (!Directory.Exists(dirPath))
                    {
                        Directory.CreateDirectory(dirPath);
                    }
                    parentPath = dirPath;
                }
            }

            if (!Directory.Exists(dirPath))
            {
                Directory.CreateDirectory(dirPath);
            }

            var members = new Dictionary<DbElementType, List<DbElement>>();

            foreach (var memb in item.Members())
            {
                if (memb.GetElementType().Equals(DbElementTypeInstance.ZONE))
                {
                    if (item.GetElementType() == DbElementTypeInstance.SITE &&
                        !memb.GetAsString(DbAttributeInstance.FLNM).StartsWith(item.GetAsString(DbAttributeInstance.FLNM)))
                    {                        
                        using (var sw = new StreamWriter(ERROR_PATH, true))
                        {
                            sw.WriteLine($"item {memb.GetAsString(DbAttributeInstance.FLNM)} skipped");
                            sw.Close();
                        }
                        continue;
                    }

                    macList.AddRange(ExportToDirectory(memb, dirPath));
                }
                else if ((parentPath.Contains("\\SO\\") || parentPath.EndsWith("\\SO")) && _dirTypes.Contains(memb.GetElementType()))
                {
                    macList.AddRange(ExportToDirectory(memb, dirPath));
                }
                else
                {
                    if (members.ContainsKey(memb.GetElementType()))
                    {
                        members[memb.GetElementType()].Add(memb);
                    }
                    else
                    {
                        members.Add(memb.GetElementType(), new List<DbElement>() { memb });
                    }
                }
               
                //if (!dirType.Contains(memb.GetElementType()))
                //{
                //    if (members.ContainsKey(memb.GetElementType()))
                //    {
                //        members[memb.GetElementType()].Add(memb);
                //    }
                //    else
                //    {
                //        members.Add(memb.GetElementType(), new List<DbElement>() { memb });
                //    }
                //    continue;
                //}
                //ExportToDirectory(memb, dirPath);
            }

            foreach (var pair in members)
            {
                var colourDictionary = new Dictionary<string, List<DbElement>>();
                foreach (var memb in pair.Value)
                {
                    var colour = GetItemColour(memb, pair.Key);
                    if (colourDictionary.ContainsKey(colour))
                    {
                        colourDictionary[colour].Add(memb);
                    }
                    else
                    {
                        colourDictionary.Add(colour, new List<DbElement> { memb });
                    }
                }

                foreach (var colourPair in colourDictionary)
                {
                    macList.AddRange(ExportElements(pair.Key, colourPair.Value, dirPath, colourPair.Key));
                }
            }

            return macList;
        }

        /// <summary>
        /// Экспорт элементов в соответствующую струтктуру папок
        /// </summary>
        /// <param name="type"></param>
        /// <param name="members"></param>
        /// <param name="path"></param>
        /// <param name="colour"></param>
        private static List<string> ExportElements(DbElementType type, List<DbElement> members, string path, string colour)
        {
            var macList = new List<string>();

            //todo: remove after fix
            //if (path.Contains(@"\CWAY") || path.Contains(@"EO\EM-07\02\+3.600\CWAY") || path.Contains(@"EO\EM-07\04\0.000\CWAY")) return macList;

            if (OneByOneTypes.Contains(type))
            {
                foreach (var element in members)
                {
                    macList.AddRange(ExportCommand(type, new List<DbElement> { element }, path, colour));
                }
            }
            else
            {
                macList.AddRange(ExportCommand(type,members,path,colour));
            }
            return macList;
        }

        //private static void ExportCommand(DbElementType type, List<DbElement> members,string path, string colour)
        //{
        //    var list = new List<List<DbElement>>();
        //    var tempList = new List<DbElement>();
        //    for (var i = 0; i < members.Count; i++)
        //    {
        //        tempList.Add(members[i]);
        //        if (tempList.Count % 1000 == 0 || i == members.Count - 1)
        //        {
        //            var a = new List<DbElement>();
        //            a.AddRange(tempList);
        //            list.Add(a);
        //            tempList = new List<DbElement>();
        //        }
        //    }

        //    for (var i = 0; i < list.Count; i++)
        //    {
        //        var l = list[i];
        //        cmd.CreateCommand("export system /explantmcad").Run();
        //        var fileName = members.Count == 1
        //            ? GetItemName(l.First())
        //            : (type + (list.Count > 1 ? ("_part_" + (i + 1)) : string.Empty));

        //        var stpPath = Path.Combine(path, fileName+ "_" + colour + ".stp");
        //        cmd.CreateCommand("export file \"" + stpPath + "\" over").Run();
        //        cmd.CreateCommand("export holes on").Run();
        //        foreach (var element in l)
        //        {
        //            cmd.CreateCommand("export " + element.GetAsString(DbAttributeInstance.REF)).Run();
        //        }

        //        cmd.CreateCommand("export finish").Run();
        //    }
        //}

        private static List<string> ExportCommand(DbElementType type, List<DbElement> members, string path, string colour)
        {
            var list = new List<List<DbElement>>();
            var macList = new List<string>();
            var tempList = new List<DbElement>();
            for (var i = 0; i < members.Count; i++)
            {
                tempList.Add(members[i]);
                if (tempList.Count % 1000 == 0 || i == members.Count - 1)
                {
                    var a = new List<DbElement>();
                    a.AddRange(tempList);
                    list.Add(a);
                    tempList = new List<DbElement>();
                }
            }

            for (var i = 0; i < list.Count; i++)
            {
                var l = list[i];                
                var fileName = members.Count == 1
                    ? GetItemName(l.First())
                    : (type + (list.Count > 1 ? ("_part_" + (i + 1)) : string.Empty));
                fileName = fileName.Replace("/", "_");
                var stpPath = Path.Combine(path, fileName + "_" + colour + ".stp");
                var macPath = stpPath.Replace(".stp", ".pmlmac");
                using (var sw = new StreamWriter(macPath, false, Encoding.UTF8))
                {
                    sw.WriteLine("export system /explantmcad");
                    sw.WriteLine("export file \"" + stpPath + "\" over");
                    sw.WriteLine("export holes on");
                   
                    foreach (var element in l)
                    {
                        sw.WriteLine("export " + element.GetAsString(DbAttributeInstance.REF));
                    }
                    sw.WriteLine("export finish");
                }
                macList.Add(macPath);
            }

            return macList;
        }

        /// <summary>
        /// Формирование имени файла или папки по элементу Aveva
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        private static string GetItemName(DbElement item)
        {
            var name = string.Empty;
            if (item.GetBool(DbAttributeInstance.ISNAME))
            {
                name =
                    item.GetAsString(DbAttributeInstance.FLNN)
                        .Replace(
                            (item.GetElementType().Equals(DbElementTypeInstance.SITE)
                                ? MDB.CurrentMDB.Name.Replace("-", string.Empty)
                                : item.Owner.GetAsString(DbAttributeInstance.FLNN)), string.Empty);
                if (name.StartsWith("-") || name.StartsWith("_"))
                    name = name.Substring(1);
            }
            else
            {
                name = item.RefNo()[0] + "#" + item.RefNo()[1];
            }
            return name;
        }

       private static DbExpression GetOrAddExpression(string input)
        {
            if (!ExpressionsDict.ContainsKey(input))
                ExpressionsDict.Add(input, DbExpression.Parse(input));
                return ExpressionsDict[input];
        }

        /// <summary>
        /// Получение цвета элемента по правилам Aveva
        /// </summary>
        /// <param name="item"></param>
        /// <param name="type"></param>
        /// <returns></returns>
        private static string GetItemColour(DbElement item, DbElementType type)
        {
            if (_ruleDictionary.ContainsKey(type))
            {
                foreach (var rule in _ruleDictionary[type])
                {
                    var flag = false;
                    if (item.EvaluateValidBool(GetOrAddExpression(rule[0]), ref flag) && flag)
                        return rule[1];
                }
            }

            return "lightgrey";
        }

        /// <summary>
        /// Корректировка имен блоков в step файлах
        /// </summary>
        /// <param name="rootDir"></param>
        private static bool FixStepBlockNames(string path)
        {
            try
            {
                var fs = File.Open(path, FileMode.Open);
                fs.Close();
            }
            catch
            {
                return false;
            }

            var fixPath = path.Replace(".stp", "_fix.stp");
            using (var sw = new StreamWriter(fixPath))
            {
                sw.AutoFlush = true;
                using (var sr = new StreamReader(path))
                {
                    string line;
                    while ((line = sr.ReadLine()) != null)
                    {
                        sw.WriteLine(line.Contains("= PRODUCT ") || line.Contains("= MANIFOLD_SOLID_BREP ")
                            ? line.Replace("/", "_")
                            : line);
                    }
                    sr.Close();
                }
                sw.Close();
            }
            File.Delete(path);
            File.Move(fixPath, path);
            return true;
        }

        #endregion

        private static void RunConverter()
        {
            var dwgConvertedList = new List<string>();
            var stpFiles = Directory.GetFiles(_dwgModelsPath, "*.stp", SearchOption.AllDirectories).ToList();
            while (!ExportFinishFlag || dwgConvertedList.Count < stpFiles.Count)
            {
                var acTranslatorProcs = Process.GetProcessesByName("AcTranslators").Length;
                if (acTranslatorProcs >= 10)
                {
                    Thread.Sleep(1000);
                    continue;
                }

                stpFiles = Directory.GetFiles(_dwgModelsPath, "*.stp", SearchOption.AllDirectories).ToList();
                var exceptList = stpFiles.Except(dwgConvertedList).ToList();
                if (exceptList.Count <= 0) continue;

                foreach (var fileToProceed in exceptList)
                {
                    var stp = new FileInfo(fileToProceed);
                    if (stp.Exists)
                    {

                        var outputDir = stp.DirectoryName;
                        if (!Directory.Exists(outputDir))
                        {
                            Directory.CreateDirectory(outputDir);
                        }

                        if (!FixStepBlockNames(stp.FullName)) continue;
                        var inputFullPath = stp.FullName;
                        var outputFullPath = Path.Combine(outputDir, stp.Name.Replace(".stp", ".dwg"));
                        var arguments =
                            string.Format(
                                "-productKey {0} -productVersion {1} -productLicenseType {2} -productResourcePath \"{3}\" -productLicenseLocale {4} -i \"{5}\" -o \"{6}\"",
                                _PRODUCT_KEY, _acadVersion, _licenseTypeAsString,
                                Path.Combine(_acadDir, "AdlmRes\\" + _cultureName),
                                _cultureName.Replace("-", "_"), inputFullPath, outputFullPath);

                        var process = new Process
                        {
                            StartInfo =
                            {
                                FileName = Path.Combine(_acadDir,_acadTranslator),
                                Arguments = arguments,
                                UseShellExecute = false,
                                CreateNoWindow = true
                            }
                        };
                        process.Start();
                    }
                    dwgConvertedList.Add(fileToProceed);
                    break;
                }
            }
        }
        
        private static List<string> MergeDwgFiles()
        {
            var dwgModelsDir = new DirectoryInfo(_dwgModelsPath);
            var departmentDirs = dwgModelsDir.GetDirectories();

            foreach (var departmentDir in departmentDirs)
            {
                var dwgDictionary = new Dictionary<string, List<FileInfo>>();
                var dwgFiles = departmentDir.GetFiles("*.dwg", SearchOption.AllDirectories);
                foreach (var dwg in dwgFiles)
                {
                    var parentName = dwg.Directory.Name.ToUpper();
                    if (!departmentDir.Name.Equals("SO") && !_structInfoDict.ContainsKey(parentName))
                    {
                        parentName = "X";
                    }
                    else if (departmentDir.Name.Equals("SO"))
                    {
                        var name = string.Empty;
                        var parent = dwg.Directory;
                        while (!departmentDir.FullName.Equals(parent.FullName))
                        {
                            if (!parent.Parent.Name.Equals("PER") && !parent.Parent.Name.Equals("GSP"))
                                name = "_" + parent.Name + name;
                            parent = parent.Parent;
                        }
                        parentName = name.Substring(1);
                        var splitParent = parentName.Split('_');
                        if (splitParent[0].ToUpper().Contains("AR"))
                        {
                            if (splitParent.Length > 1 && !_arStructInfo.ContainsKey(splitParent[1]) &&
                                !_arStructInfo.ContainsKey(splitParent[1] + "_" + splitParent[2]))
                            {
                                parentName = "X";
                            }
                            else
                            {
                                parentName = parentName.Replace(splitParent[0], "AR");
                            }
                        }
                        else if (splitParent[0].ToUpper().Contains("KG"))
                        {
                            if (splitParent.Length > 1 && !_kgStructInfo.ContainsKey(splitParent[1]) &&
                                !_kgStructInfo.ContainsKey(splitParent[1] + "_" + splitParent[2]))
                            {
                                parentName = "X";
                            }
                            else
                            {
                                parentName = parentName.Replace(splitParent[0], "KG");
                            }
                        }
                        else if (splitParent[0].ToUpper().Contains("KM"))
                        {
                            if ((splitParent.Length != 3 ||
                                 !_kmStructInfo.ContainsKey(splitParent[1] + "_" + splitParent[2])))
                            {
                                parentName = "X";
                            }
                            else
                            {
                                parentName = parentName.Replace(splitParent[0], "KM");
                            }
                        }
                    }

                    if (dwgDictionary.ContainsKey(parentName))
                    {
                        dwgDictionary[parentName].Add(dwg);
                    }
                    else
                    {
                        dwgDictionary.Add(parentName, new List<FileInfo> {dwg});
                    }
                }

                foreach (var pair in dwgDictionary)
                {
                    var mergerDwgName = Path.Combine(_dwgModelsPath, departmentDir.Name + "_" + pair.Key + ".dwg");
                    File.Copy(GetEmptyDwg(), mergerDwgName, true);
                }
            }

            var mergeFiles = Directory.GetFiles(_dwgModelsPath, "*.dwg", SearchOption.TopDirectoryOnly).ToList();
            var accoreconsoleProcs = Process.GetProcessesByName("accoreconsole").Length;

            var mergeScrPath = Path.Combine(Path.GetTempPath(), "mergedwg.scr");
            using (var sw = new StreamWriter(mergeScrPath, false, Encoding.UTF8))
            {
                sw.WriteLine("(command \"_netload\" \"C:\\\\Program Files\\\\Autodesk\\\\ApplicationPlugins\\\\Polymetal\\\\Polymetal.Exchange.Acad.dll\")");
                sw.WriteLine("(command \"PMMERGEPDMSDWG\")");
                sw.WriteLine("(command \"_QSAVE\")");
                sw.Close();
            }

            foreach (var dwg in mergeFiles)
            {
                Console.WriteLine($"merging {dwg.Split('\\').Last()}");
                while (accoreconsoleProcs >= 5)
                {
                    Thread.Sleep(1000);
                    accoreconsoleProcs = Process.GetProcessesByName("accoreconsole").Length;
                }

                var process = new Process();
                process.StartInfo.FileName = Path.Combine(_acadDir,_acadAccoreconsole);
                process.StartInfo.Arguments = string.Format("/i \"{0}\" /s \"{1}\" /l ru-RU", dwg, mergeScrPath); 
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.CreateNoWindow = true;
                process.Start();

                accoreconsoleProcs = Process.GetProcessesByName("accoreconsole").Length;
            }

            while (accoreconsoleProcs > 0)
            {
                Thread.Sleep(1000);
                accoreconsoleProcs = Process.GetProcessesByName("accoreconsole").Length;
            }
            Thread.Sleep(1000);
            try
            {
                File.Delete(mergeScrPath);
            }
            catch
            {
            }

            var result = new List<string>();
            foreach (var filePath in mergeFiles)
            {
                var fileInfo = new FileInfo(filePath);
                var name = GetModelTitle(fileInfo.FullName).Replace("/", " ");
                var newPath = Path.Combine(fileInfo.DirectoryName, name + fileInfo.Extension);
                fileInfo.MoveTo(newPath);
                result.Add(newPath);
            }

            return result;
        }

        private static string GetEmptyDwg()
        {
            var modelsDir = new DirectoryInfo(_dwgModelsPath);
            var emptyDwgPath = Path.Combine(modelsDir.Parent.FullName, "empty.dwg");
            if (File.Exists(emptyDwgPath)) return emptyDwgPath;
            var assembly = Assembly.GetExecutingAssembly();
            using (var stream = assembly.GetManifestResourceStream("Polymetal.Exchange.E3D.Resources.empty.dwg"))
            {
                var bytes = new byte[(int)stream.Length];
                stream.Read(bytes, 0, bytes.Length);
                File.WriteAllBytes(emptyDwgPath, bytes);
            }
            return emptyDwgPath;
        }

        /// <summary>
        /// Получение кодов и описания для объединения моделей
        /// </summary>
        /// <returns></returns>
        private static Dictionary<string, string> GetStructClassInfo()
        {
            var dict = new Dictionary<string, string>();
            using (var sqlConnection = new SqlConnection(Common.ConnectionString))
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

        private static Dictionary<string, string> GetARStructInfo()
        {
            var dict = new Dictionary<string, string>();
            using (var sqlConnection = new SqlConnection(Common.ConnectionString))
            {
                sqlConnection.Open();
                var sqlCommand = "SELECT " +
                                 "  (RTRIM(UPPER(ARElementsCode))+RTRIM(UPPER(ISNULL('_'+ARFuncCode,'')))), " +
                                 "  RTRIM(LOWER(ARElementsDesc))+RTRIM(LOWER(ISNULL(', '+ARFuncDesc,''))) " +
                                 "FROM " +
                                 "  ARElements " +
                                 "LEFT JOIN " +
                                 "  ARFunc ON ARFunc.ARElementsID = ARElements.ARElementsID";
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

        private static Dictionary<string, string> GetKGStructInfo()
        {
            var dict = new Dictionary<string, string>();
            using (var sqlConnection = new SqlConnection(Common.ConnectionString))
            {
                sqlConnection.Open();
                var sqlCommand = "select distinct " +
                                 "  (RTRIM(UPPER(KGIdentCode)) + RTRIM(UPPER(ISNULL('_'+KGElementsIdentCode,'')))), " +
                                 "  (RTRIM(LOWER(KGIdentDesc)) + RTRIM(LOWER(ISNULL(', '+KGElementsIdentDesc,'')))) " +
                                 "FROM " +
                                 "  KGIdent " +
                                 "  left join KGElementsIdent on KGElementsIdent.KGIdentID = KGIdent.KGIdentID";
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

        private static Dictionary<string, string> GetKMStructInfo()
        {
            var dict = new Dictionary<string, string>();
            using (var sqlConnection = new SqlConnection(Common.ConnectionString))
            {
                sqlConnection.Open();
                var sqlCommand = "USE Aveva_Poly_Db " +
                                 "SELECT " +
                                 "  (RTRIM(UPPER(KMIdentCode))+'_'+RTRIM(UPPER(KMElementsIdentCode))), " +
                                 "  REPLACE(RTRIM(LOWER(KMIdentDesc))+', '+RTRIM(LOWER(KMElementsIdentDesc)),'/',' и ') " +
                                 "FROM " +
                                 "  KMIdent " +
                                 "JOIN " +
                                 "  KMElementsIdent ON KMElementsIdent.KMIdentID = KMIdent.KMIdentID";
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

        private static Dictionary<string, string> GetDepartmentInfo()
        {
            var dict = new Dictionary<string, string>();
            using (var sqlConnection = new SqlConnection(Common.ConnectionString))
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

        private static Dictionary<string, string> GetDocumentMarkInfo()
        {
            var dict = new Dictionary<string, string>();
            using (var sqlConnection = new SqlConnection(Common.ConnectionString))
            {
                sqlConnection.Open();
                var sqlCommand = "select DISTINCT RTRIM(DocumentMarkCode), RTRIM(DocumentMarkAbbreviation) from DocumentMark";
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

        private static void MoveFiles(string serverPath, List<string> files, string extension)
        {
            var modelsPath = Path.Combine(serverPath, "pdms");
            if (!Directory.Exists(modelsPath)) Directory.CreateDirectory(modelsPath);

            var garbageFormats = new List<string> {$".{extension}", ".rvt", ".html", ".txt", ".log"};
            var garbageFiles = Directory.GetFiles(modelsPath, "*.*", SearchOption.TopDirectoryOnly).Where(x=>garbageFormats.Contains(Path.GetExtension(x.ToLower()))).ToList();

            DeleteFiles(garbageFiles);

            foreach (var file in files)
            {
                var fileName = file.Substring(file.LastIndexOf("\\") + 1);
                try
                {
                    File.Copy(file, Path.Combine(modelsPath, fileName));
                }
                catch
                {
                    Console.WriteLine($"can't copy {file}");
                }
            }
        }

        private static void DeleteFiles(List<string> files)
        {
            foreach (var file in files)
            {
                try
                {
                    File.Delete(file);
                }
                catch
                {
                    Console.WriteLine($"can't delete {file}");
                }
            }
        }

        private static void DeleteDirecory(string dir)
        {
            try
            {
                Directory.Delete(dir, true);
            }
            catch
            {
                Console.WriteLine($"can't delete {dir}");
            }
        }

        private static string GetModelTitle(string fileName)
        {
            var result = "прочее";
            var file = new FileInfo(fileName);
            var info = file.Name.Replace(file.Extension, string.Empty);
            var splitInfo = info.Split('_');
            if (splitInfo[0].Equals("SO") && splitInfo.Length > 3)
            {
                if (splitInfo[1].Equals("AR"))
                {
                    if (splitInfo[2].Equals("PL"))
                    {
                        result = _arStructInfo.ContainsKey(splitInfo[2]) ? _arStructInfo[splitInfo[2]] : "прочее";
                        if (splitInfo.Length > 3)
                        {
                            result += " " + splitInfo[3];
                        }
                    }
                    else
                    {
                        var arInfo = info.Replace(splitInfo[0] + "_" + splitInfo[1] + "_", string.Empty);
                        result = _arStructInfo.ContainsKey(arInfo) ? _arStructInfo[arInfo] : "прочее";
                    }
                }
                else if (splitInfo[1].Equals("KG"))
                {
                    var kgInfo = info.Replace(splitInfo[0] + "_" + splitInfo[1] + "_", string.Empty);
                    result = _kgStructInfo.ContainsKey(kgInfo) ? _kgStructInfo[kgInfo] : "прочее";
                }
                else if (splitInfo[1].Equals("KM"))
                {
                    var kmInfo = info.Replace(splitInfo[0] + "_" + splitInfo[1] + "_", string.Empty);
                    result = _kmStructInfo.ContainsKey(kmInfo) ? _kmStructInfo[kmInfo] : "прочее";
                }

                result = _documentMarkInfo.ContainsKey(splitInfo[1])
                ? _documentMarkInfo[splitInfo[1]] + " " + result
                : splitInfo[1] + " " + result;
            }
            else
            {
                var otherInfo = splitInfo.Last();
                result = _structInfoDict.ContainsKey(otherInfo) ? _structInfoDict[otherInfo] : "прочее";
            }

            result = _departmentInfo.ContainsKey(splitInfo[0])
                ? _departmentInfo[splitInfo[0]] + " " + result
                : splitInfo[0] + " " + result;

            return result;
        }
    }

    internal class RuleObject
    {
        internal string Name;
        internal string Rule;
        internal List<DbElementType> Types;
        internal string Description;
    }
}
