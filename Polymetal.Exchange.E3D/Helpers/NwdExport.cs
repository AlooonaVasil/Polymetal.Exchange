using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Aveva.Core.Database;
using Aveva.Core.Database.Filters;
using Aveva.Core.Maths.Geometry;
using Polymetal.Exchange.Utilities;

namespace Polymetal.Exchange.E3D.Helpers
{
    public class NwdExport
    {
        private string _exportDirectory;
        private string _modelTransformation;

        private static byte _structureVersion;
        /// <summary>
        /// Получение версии именования структурных элементов
        /// 1: /0200200-EO
        /// 2: /16020700200-EO
        /// </summary>
        private static byte StructureVersion
        {
            get
            {
                if (_structureVersion == 0)
                {
                    using (var sqlConnection = new SqlConnection(Utilities.Common.ConnectionString))
                    {
                        sqlConnection.Open();
                        var cmd = $"select StructureVersion from vPdmsProjects where Project = '{Project.CurrentProject.Name}' and MDB = '{MDB.CurrentMDB.Name}'";
                        using (var sqlCmd = new SqlCommand(cmd, sqlConnection))
                        {
                            var res = sqlCmd.ExecuteScalar();
                            if (res != null)
                            {
                                _structureVersion = (byte)res;
                            }
                        }
                        sqlConnection.Close();
                    }
                }

                return _structureVersion;
            }
        }

        public NwdExport(string exportDirectory, string modelTransformation)
        {
            _exportDirectory = exportDirectory;
            _modelTransformation = modelTransformation;
        }


        /// <summary>
        /// Экспорт опорных моделей в формате nwd
        /// </summary>
        /// <returns></returns>
        public List<string> ExportReferenceModel()
        {
            var result = new List<string>();
            var nwdInstructions = new NwdInstructions();
            var tmpFile = Path.GetTempFileName();

            nwdInstructions.NwdPath =
                Path.Combine(_exportDirectory, $"{Project.CurrentProject.Name} {MDB.CurrentMDB.Name}.nwd");
            var sites = GetExportSites();
            var rvmExporter = new Exporter.AvevaUtility.Utilities.ModelExport.RvmCustomExporter();
            foreach (var site in sites)
            {
              
                if (site.GetString(DbAttributeInstance.PURP) == "IMP")
                {
                    foreach (var zone in site.Members(DbElementTypeInstance.ZONE))
                    {
                        if (!zone.GetString(DbAttributeInstance.FLNM).Contains("-PMEKZ") &&
                            !zone.GetString(DbAttributeInstance.FLNM).Contains("-GMOKZ"))
                            continue;

                        var rvmPath = Path.Combine(_exportDirectory, $"{zone.GetString(DbAttributeInstance.FLNN)}.rvm");
                        rvmExporter.Export(new List<DbElement> { zone }, rvmPath);
                        nwdInstructions.NwdFiles.Add(rvmPath);
                    }
                }
                else if (site.GetString(DbAttributeInstance.PURP) == "TASK")
                {
                    foreach (var zone in site.Members(DbElementTypeInstance.ZONE))
                    {
                        if (!zone.GetString(DbAttributeInstance.FUNC).Equals("EXPORT", StringComparison.OrdinalIgnoreCase))
                        {
                            //проверяем внутренние зоны. по идее нужен рефакторинг
                            foreach (var zoneLevel1 in zone.Members(DbElementTypeInstance.ZONE))
                            {
                                if (!zoneLevel1.GetString(DbAttributeInstance.FUNC).Equals("EXPORT", StringComparison.OrdinalIgnoreCase))
                                    continue;

                                var rvmPath = Path.Combine(_exportDirectory, $"{zoneLevel1.GetString(DbAttributeInstance.FLNN)}.rvm");
                                rvmExporter.Export(new List<DbElement> { zoneLevel1 }, rvmPath);
                                nwdInstructions.NwdFiles.Add(rvmPath);
                            }
                        }
                        else
                        {
                            var rvmPath = Path.Combine(_exportDirectory, $"{zone.GetString(DbAttributeInstance.FLNN)}.rvm");
                            rvmExporter.Export(new List<DbElement> { zone }, rvmPath);
                            nwdInstructions.NwdFiles.Add(rvmPath);
                        }
                          
                    }
                }
                else
                {
                    var rvmPath = Path.Combine(_exportDirectory, $"{site.GetString(DbAttributeInstance.FLNN)}.rvm");
                    rvmExporter.Export(new List<DbElement> { site }, rvmPath);
                    nwdInstructions.NwdFiles.Add(rvmPath);
                }
            }

            nwdInstructions.Transform = GetTransform(sites.FirstOrDefault());

            nwdInstructions.HiddenElements = new List<string>();
            nwdInstructions.HiddenElements.AddRange(
                GetOARHiddenElements(sites).Select(x => x.GetAsString(DbAttributeInstance.FLNM)).ToList());
            nwdInstructions.HiddenElements.AddRange(GetAxesHiddenElements(sites).Select(x => x.GetAsString(DbAttributeInstance.FLNM))
                .ToList());
            nwdInstructions.HiddenElements.AddRange(GetMagiCADHiddenElements(sites).Select(x => x.GetAsString(DbAttributeInstance.FLNM))
                .ToList());
            
            using (var sw = new StreamWriter(tmpFile)) 
                sw.WriteLine(nwdInstructions.Serialize());

            StartNwdProcess(tmpFile);
            if (File.Exists(nwdInstructions.NwdPath))
                result.Add(nwdInstructions.NwdPath);

            nwdInstructions.NwdPath = Path.Combine(_exportDirectory,
                $"{Project.CurrentProject.Name} {MDB.CurrentMDB.Name} MagiCAD.nwd");

            nwdInstructions.HiddenElements = new List<string>();
            nwdInstructions.HiddenElements.AddRange(GetAllExceptMagiCADHiddenElements(sites).Select(x => x.GetAsString(DbAttributeInstance.FLNM)).ToList());
            //nwdInstructions.HiddenElements.AddRange(GetOARHiddenElements(sites).Select(x => x.GetAsString(DbAttributeInstance.FLNM)).ToList());
            //nwdInstructions.HiddenElements.AddRange(GetAxesHiddenElements(sites).Select(x => x.GetAsString(DbAttributeInstance.FLNM)).ToList());

            using (var sw = new StreamWriter(tmpFile)) 
                sw.WriteLine(nwdInstructions.Serialize());

            StartNwdProcess(tmpFile);
            if (File.Exists(nwdInstructions.NwdPath)) 
                result.Add(nwdInstructions.NwdPath);

            try
            {
                File.Delete(tmpFile);
            }
            catch { }

            return result;
        }

        /// <summary>
        /// Получение трансформации проекта
        /// </summary>
        /// <param name="root"></param>
        /// <returns></returns>
        private double[] GetTransform(DbElement root)
        {
            if (root == null || root.IsNull) return default;
            var ori = root.GetOrientation(DbAttributeInstance.ORI);
            var pos = root.GetPosition(DbAttributeInstance.POS);

            var matrix = new[]
            {
                ori.XDir().East, ori.XDir().North, ori.XDir().Up,
                ori.YDir().East, ori.YDir().North, ori.YDir().Up,
                ori.ZDir().East, ori.ZDir().North, ori.ZDir().Up,
                pos.X, pos.Y, pos.Z
            };

            if (string.IsNullOrEmpty(_modelTransformation))
                return matrix;

            var mOffset = D3Transform.Create(_modelTransformation.Replace(".",",").Split().Select(double.Parse).ToArray());

            return D3Transform.Create(matrix).Multiply(mOffset).AsArray12();
        }

        private void StartNwdProcess(string nwdInstructionsPath)
        {
            var exePath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            exePath = Path.Combine(exePath, "Polymetal.Exchange.NavisWorks.exe");

            if (!File.Exists(exePath)) return;

            var proc = new Process();
            proc.StartInfo = new ProcessStartInfo(exePath, nwdInstructionsPath);
            proc.StartInfo.UseShellExecute = false;
            proc.StartInfo.RedirectStandardOutput = true;
            proc.EnableRaisingEvents = true;
            proc.Start();
            proc.WaitForExit();
        }

        /// <summary>
        /// Список элементов ОАР для скрытия в nwd
        /// </summary>
        /// <param name="roots"></param>
        /// <returns></returns>
        private List<DbElement> GetOARHiddenElements(List<DbElement> roots)
        {
            return roots.Where(root => root.GetBool(DbAttributeInstance.ISNAME) && root.Name().EndsWith("OAR"))
                .ToList();
        }

        /// <summary>
        /// Список элементов осей для скрытия в nwd
        /// </summary>
        /// <param name="roots"></param>
        /// <returns></returns>
        private List<DbElement> GetAxesHiddenElements(List<DbElement> roots)
        {
            return roots.Where(root => root.GetBool(DbAttributeInstance.ISNAME) && root.Name().EndsWith("OSI"))
                .ToList();
        }

        /// <summary>
        /// Список элементов MagiCAD для скрытия в nwd
        /// </summary>
        /// <param name="roots"></param>
        /// <returns></returns>
        private List<DbElement> GetMagiCADHiddenElements(List<DbElement> roots)
        {
            var result = new List<DbElement>();
            var funcFilter = new AttributeValueFilter(DbAttributeInstance.FUNC, TestOperator.Equals, "MagiCAD");
            var typeFilter = new Aveva.Core.Database.Filters.TypeFilter(DbElementTypeInstance.EQUIPMENT);
            var andFilter = new AndFilter(typeFilter, funcFilter);
            foreach (var root in roots)
            { //все элементы MagiCAD
                var collection = new DBElementCollection(root, andFilter).Cast<DbElement>().ToList();
                result.AddRange(CompactElements(collection));
            }

            return result;
        }

        /// <summary>
        /// Список всех элементов кроме MagiCAD для скрытия в nwd
        /// </summary>
        /// <param name="roots"></param>
        /// <returns></returns>
        private List<DbElement> GetAllExceptMagiCADHiddenElements(List<DbElement> roots)
        {
            return roots.Where(root => !root.GetBool(DbAttributeInstance.ISNAME) || !root.Name().EndsWith("OOVK")).ToList();
        }

        /// <summary>
        /// Уменьшение количества элементов
        /// </summary>
        /// <param name="collection"></param>
        /// <returns></returns>
        private List<DbElement> CompactElements(List<DbElement> collection)
        {
            var tmpCollection = new List<DbElement>();
            var compacted = false;
            foreach (var item in collection)
            {
                var owner = item.Owner;
                if (owner.IsNull || owner.ElementType == DbElementTypeInstance.WORLD)
                {
                    tmpCollection.Add(item);
                    continue;
                }

                if (tmpCollection.Contains(owner)) continue;
                var neighbors = owner.Members().ToList();
                if (neighbors.All(x => collection.Contains(x)))
                {
                    tmpCollection.Add(owner);
                    compacted = true;
                }
                else
                {
                    tmpCollection.Add(item);
                }
            }

            return compacted ?  CompactElements(tmpCollection): tmpCollection;
        }

        /// <summary>
        /// Получение рабочих сайтов, которые относятся к текущей MDB
        /// </summary>
        /// <returns></returns>
        private List<DbElement> GetExportSites()
        {
            var result = new List<DbElement>();
            var typeFilter = new Aveva.Core.Database.Filters.TypeFilter(DbElementTypeInstance.SITE);
            var jobPurpFilter = new AttributeValueFilter(DbAttributeInstance.PURP, TestOperator.Equals, "JOB");
            var axesPurpFilter = new AttributeValueFilter(DbAttributeInstance.PURP, TestOperator.Equals, "AXES");
            var impPurpFilter = new AttributeValueFilter(DbAttributeInstance.PURP, TestOperator.Equals, "IMP");
            var taskPurpFilter = new AttributeValueFilter(DbAttributeInstance.PURP, TestOperator.Equals, "TASK");
            var andFilter1 = new AndFilter(new BaseFilter[] { typeFilter, jobPurpFilter });
            var andFilter2 = new AndFilter(new BaseFilter[] { typeFilter, axesPurpFilter });
            var andFilter3 = new AndFilter(new BaseFilter[] { typeFilter, impPurpFilter });
            var andFilter4 = new AndFilter(new BaseFilter[] { typeFilter, taskPurpFilter });
            var orFilter = new OrFilter(new[] { andFilter1, andFilter2, andFilter3, andFilter4 });
            var jobSites = new DBElementCollection(MDB.CurrentMDB.GetFirstWorld(DbType.Design), orFilter).Cast<DbElement>().ToList();
            var mdbName = MDB.CurrentMDB.Name.Replace("-", string.Empty);
            
            foreach (var site in jobSites)
            {
                var siteCode = site.GetAsString(DbAttributeInstance.FLNN).Split('-').FirstOrDefault();
                if (StructureVersion == 2)
                {
                    if (siteCode.Length < 6) continue;
                    //проверка принадлежности к MDB
                    if (mdbName != $"{siteCode.Substring(2, 2)}{siteCode.Substring(6)}") continue;
                }
                else if (mdbName != siteCode)
                {
                    continue;
                }

                //дважды добавлялись одни и те же сайты
                if (result.Contains(site)) 
                    continue;
                result.Add(site);
            }

            return result;
        }
    }
}
