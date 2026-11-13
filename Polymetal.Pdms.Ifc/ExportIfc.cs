using Aveva.PDMS.PMLNet;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using Aveva.Pdms.Database;
using Aveva.Pdms.Shared;
using Aveva.PDMS.Database.Filters;
using cmd = Aveva.Pdms.Utilities.CommandLine.Command;
using TypeFilter = Aveva.PDMS.Database.Filters.TypeFilter;

namespace Polymetal.Pdms.Ifc
{
    [PMLNetCallable()]
    public class ExportIfc
    {
        internal static List<string[]> ColorRules;
        private const double ARCTOLERANCE = 5.0;
        private const int OBST_TRANSLUCENCY = 75;
        internal const int REPRE_LEVEL = 6;
        internal static DbAttribute Att_InsuThickness = DbAttribute.GetDbAttribute(":InsuThickness");
        internal static DbAttribute Att_EquiWeight = DbAttribute.GetDbAttribute(":EquiWeight");
        internal static Dictionary<string, DbExpression> ExpressionsDict = new Dictionary<string, DbExpression>();
        internal static Dictionary<string,DbExpression> ExpressionDictionary = new Dictionary<string, DbExpression>();
        internal static string ConnectionString = new sqlReference.Service1().GetStrConnectPdms();
        [PMLNetCallable()]
        public ExportIfc()
        {
        }

        [PMLNetCallable()]
        public void Assign(ExportIfc that)
        {
            
        }

        [PMLNetCallable()]
        public void ExportCurrentElement()
        {
            var dictionary = new Dictionary<string, List<DbElement>>();
            var item = CurrentElement.Element;
            var itemName = item.GetBool(DbAttributeInstance.ISNAME)
                ? item.GetAsString(DbAttributeInstance.FLNN)
                : string.Format("{0}_{1}_{2}", item.GetElementType().ShortName.ToUpper(), item.RefNo()[0],
                    item.RefNo()[1]);
            var path = Path.Combine(
                Environment.GetEnvironmentVariable("PDMSUSER"),
                string.Format("{0}\\{1}\\{2}.ifc",
                    Project.CurrentProject.Name,
                    MDB.CurrentMDB.Name,
                    itemName));
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
        }

        [PMLNetCallable()]
        public void ExportEngineering(Dictionary<string, List<DbElement>> input)
        {
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

            Run(exportData);
        }

        [PMLNetCallable()]
        public List<string> ExportReferenceModels(string modelsPath)
        {
            var stopWatch = new Stopwatch();
            stopWatch.Start();
            var markDict = new Dictionary<string, string>();
            var deptDict = new Dictionary<string, string>();
            var funcDict = new Dictionary<string, string>();
            using (var sqlConnection = new SqlConnection(ConnectionString))
            {
                sqlConnection.Open();
                var sqlCommand = "select distinct code=RTRIM(a.DepartmentCode), abbr=RTRIM(a.DepartmentAbbreviation) from Department a";
                using (var sqlCmd = new SqlCommand(sqlCommand, sqlConnection))
                {
                    var reader = sqlCmd.ExecuteReader();
                    while (reader.Read())
                    {
                        var key = (string) reader["code"];
                        if (deptDict.ContainsKey(key)) continue;
                        deptDict.Add(key, (string)reader["abbr"]);
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
                        if (markDict.ContainsKey(key)) continue;
                        markDict.Add(key, (string)reader["abbr"]);
                    }
                    reader.Close();
                }

                sqlCommand = "select distinct code=RTRIM(a.StructClassInfoCode), abbr=RTRIM(a.StructClassInfoDesc) from StructClassInfo a";
                using (var sqlCmd = new SqlCommand(sqlCommand, sqlConnection))
                {
                    var reader = sqlCmd.ExecuteReader();
                    while (reader.Read())
                    {
                        var key = (string)reader["code"];
                        if (funcDict.ContainsKey(key)) continue;
                        funcDict.Add(key, (string)reader["abbr"]);
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
            var nameFilter = new AttributeLikeFilter(DbAttributeInstance.FLNN,
                string.Format("{0}-*", MDB.CurrentMDB.Name.Replace("-", string.Empty)));
            var andFilter = new AndFilter(new BaseFilter[] {typeFilter, purpFilter, nameFilter});
            var jobSites = new DBElementCollection(MDB.CurrentMDB.GetFirstWorld(DbType.Design), andFilter).Cast<DbElement>().ToList();
            foreach (var site in jobSites)
            {
                var children = CollectElements(site);
                foreach (var child in children)
                {
                    var fileName = Path.Combine(modelsPath,GetFileNameForElement(child, deptDict, markDict,funcDict)+".ifc");
                    if (!exportData.ContainsKey(fileName))
                        exportData.Add(fileName, new Dictionary<DbElement, List<DbElement>>());
                    var siteDict = exportData[fileName];
                    if(!siteDict.ContainsKey(site))
                        siteDict.Add(site, new List<DbElement>());
                    siteDict[site].Add(child);
                }
            }
            Run(exportData);
            stopWatch.Stop();
            Console.WriteLine("{0:HH:mm:ss}", stopWatch.Elapsed);
            return exportData.Keys.ToList();
        }

        private string GetFileNameForElement(DbElement item, Dictionary<string, string> deptDict,
            Dictionary<string, string> markDict, Dictionary<string, string> funcDict)
        {
            var owners = item.GetElementArray(DbAttributeInstance.OWNLST);
            var site = owners.First(x => x.GetElementType() == DbElementTypeInstance.SITE);
            var siteName = site.GetAsString(DbAttributeInstance.FLNN);
            var deptCode = siteName.Split('-').Last();
            deptCode = deptCode == "OOR" ? "TO" : deptCode;
            var ownerItem = owners.Any(x => x.GetElementType() == DbElementTypeInstance.STRUCTURE) && deptCode =="SO"
                ? owners.First(x => x.GetElementType() == DbElementTypeInstance.STRUCTURE)
                : owners.Last(x => x.GetElementType() == DbElementTypeInstance.ZONE);
            var ownerName = ownerItem.GetAsString(DbAttributeInstance.FLNN);
            var markCode = ownerName.Replace(siteName+"-", string.Empty).Split('-').First().ToUpper();
            if (markCode.StartsWith("KM"))
                markCode = "KM";
            else if (markCode.StartsWith("KG"))
                markCode = "KG";
           
            var funcCode = ownerItem.GetAsString(DbAttributeInstance.FLNN).Split('_').Last();
            var func = funcDict.ContainsKey(funcCode) ? funcDict[funcCode] : ownerItem.GetAsString(DbAttributeInstance.DESC);
            func = func.Trim()
                        .ToLower()
                        .Replace("unset", "прочее")
                        .Replace("other", "прочее")
                        .Replace("/", string.Empty)
                        .Replace("\\", string.Empty);
            if (item.GetElementType() == DbElementTypeInstance.EQUIPMENT &&
                item.GetAsString(DbAttributeInstance.FUNC).Equals("MagiCAD"))
                func += " MagiCAD";

            return string.Format("{0} {1} {2}", deptDict.ContainsKey(deptCode)?deptDict[deptCode]:deptCode, markDict.ContainsKey(markCode)?markDict[markCode]:markCode, func);
        }

        public void Run(Dictionary<string, Dictionary<DbElement, List<DbElement>>> exportDictionary)
        {
            var currentCulture = Thread.CurrentThread.CurrentCulture;
            Thread.CurrentThread.CurrentCulture = CultureInfo.CreateSpecificCulture("en-GB");
            InitColorRules();
            cmd.CreateCommand("REPRESENTATION DARCTOL " + ARCTOLERANCE).Run();
            cmd.CreateCommand("REPRESENTATION OBST ON TRANSLUCENCY " + OBST_TRANSLUCENCY).RunInPdms();
            cmd.CreateCommand("REPRESENTATION CABLE OBST ON").RunInPdms();

            var serverGuid = Guid.NewGuid().ToString();
            var ifcServerProcess = StartIfcServer(serverGuid);
            Thread.Sleep(1000);
            var client = new NamedPipeClientStream($"IfcPdmsPipe{serverGuid}");
            client.Connect();
            using (var writer = new StreamWriter(client))
            {
                foreach (var exportPair in exportDictionary)
                {
                    writer.WriteLine("CREATE MODEL");
                    writer.WriteLine("CREATE PROJECT");
                    foreach (var rootPair in exportPair.Value)
                    {
                        writer.WriteLine("CREATE SITE");
                        writer.WriteLine(rootPair.Key.EvaluateString(GetOrAddExpression("NAME OF SITE")));
                        writer.Flush();

                        var itemCount = rootPair.Value.Count;
                        for (var i = 0; i < itemCount; i++)
                        {
                            var item = rootPair.Value[i];
                            var geometryWriter = new PdmsGeometry(item);
                            var sb = new StringBuilder();
                            sb.AppendLine("CREATE PRODUCT");
                            sb.AppendLine(item.GetBool(DbAttributeInstance.ISNAME)
                                ? item.GetAsString(DbAttributeInstance.FLNM)
                                : item.GetAsString(DbAttributeInstance.REF));
                            if (item.Owner.GetElementType() == DbElementTypeInstance.BRANCH)
                            {
                                sb.AppendLine(item.Owner.Owner.GetElementType().ShortName);
                            }
                            else if (item.GetElementType() == DbElementTypeInstance.EQUIPMENT &&
                                     item.GetAsString(DbAttributeInstance.FUNC).Equals("MagiCAD"))
                            {
                                sb.AppendLine(string.Format("{0} MCAD", DbElementTypeInstance.HVAC.ShortName));
                            }
                            else
                            {
                                sb.AppendLine(item.GetElementType().ShortName);
                            }
                            sb.AppendLine("GEOMETRY");
                            sb.Append(geometryWriter.GetText());
                            sb.AppendLine("GEOMETRY_END");

                            sb.AppendLine("PROPERTIES");
                            var propertyWriter = new PdmsProperty(item);
                            sb.Append(propertyWriter.GetText());
                            sb.AppendLine("PROPERTIES_END");
                            sb.AppendLine("END");
                            writer.Write(sb);
                            writer.Flush();

                            Console.Write("\r{0}\t{1}\\{2}", rootPair.Key, i + 1, itemCount);
                        }
                        Console.WriteLine();
                    }

                    writer.WriteLine("SAVE MODEL");
                    writer.WriteLine(exportPair.Key);
                    writer.Flush();
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
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

        private Process StartIfcServer(string serverGuid)
        {
            var path = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            path = Path.Combine(path, "Polymetal.Pdms.IfcServer.exe");
            var ifcServerProcess =new Process();
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
            if (e != null)
                Console.WriteLine("{0}", e.Data);
        }

        internal static DbExpression GetOrAddExpression(string input)
        {
            if (ExpressionDictionary.ContainsKey(input)) return ExpressionDictionary[input];
            var expr = DbExpression.Parse(input);
            ExpressionDictionary.Add(input,expr);
            return expr;
        }

        private List<DbElement> CollectElements(DbElement root)
        {
            var res = new List<DbElement>();
            var typeFilter = new TypeFilter(new[]
            {
                DbElementTypeInstance.EQUIPMENT,
                DbElementTypeInstance.GWALL,
                DbElementTypeInstance.FLOOR,
                DbElementTypeInstance.PANEL,
                DbElementTypeInstance.SCTN,
                DbElementTypeInstance.CWBRAN,
                DbElementTypeInstance.CABLE,
                DbElementTypeInstance.BRANCH
            });
            var collection = new DBElementCollection(root, typeFilter).Cast<DbElement>().ToList();
            foreach (var item in collection)
            {
                if (item.GetElementType() == DbElementTypeInstance.BRANCH)
                {
                    res.AddRange(item.Members());
                }
                else
                {
                    var ownersTypeList = item.GetElementTypeArray(DbAttributeInstance.AHLIST).ToList();
                    if (ownersTypeList.Contains(DbElementTypeInstance.EQUIPMENT)) continue;
                    res.Add(item);
                }
            }

            return res;
        }

        private void InitColorRules()
        {
            ColorRules = new List<string[]>();
            using (var stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("Polymetal.Pdms.Ifc.Resources.rules.txt"))
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
