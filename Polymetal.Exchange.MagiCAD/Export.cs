using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using Polymetal.Exchange.Utilities;

namespace Polymetal.Exchange.MagiCAD
{
    /*
     * Библиотеку необходимо скопировать на сервер в C:\TeklaStructures\
     */
    public class Export
    {
        private static string _acadDir = @"C:\Program Files\Autodesk\AutoCAD 2017";
        private const string _acad = "acad.exe";
        private const string IfcPath = @"C:\Aveva\temp\MagiCad\";

        public static bool Run(int triggerId)
        {
            if (!Directory.Exists(_acadDir))
            {
                _acadDir = @"C:\Program Files\Autodesk\AutoCAD 2019";
            }
            if (!Directory.Exists(IfcPath))
                Directory.CreateDirectory(IfcPath);

            var ifcPathDirInfo = new DirectoryInfo(IfcPath);
            foreach (var dirInfo in ifcPathDirInfo.GetDirectories())
            {
                try
                {
                    dirInfo.Delete(true);
                }
                catch { }
            }

            var swa = new Stopwatch();
            swa.Start();
            decimal projectId, status;
            Common.Source source;
            if (!Common.GetTriggerInfoById(triggerId, out projectId, out source, out status)) return false;
            var modelName = new ProjectInformation(projectId).Program.Path;
            if (modelName.Split(';').Length > 0)
            {
                modelName = modelName.Split(';').First();
            }
            var file = new FileInfo(modelName);
            if (!file.Exists) return false;
            var tempFileName = CopyToTempDirectory(file);
            if (string.IsNullOrEmpty(tempFileName)) return false;
            //var tempFileName = Path.Combine(file.DirectoryName,"temp_" + (Int32) (DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1))).TotalSeconds + ".dwg");

            //file.CopyTo(tempFileName);

            var ifcFiles = Directory.GetFiles(IfcPath, "*.ifc*");
            foreach (var f in ifcFiles)
            {
                File.Delete(f);
            }

            var scrPath = CreateSrcFile();
            if (string.IsNullOrEmpty(scrPath)) return false;

            //var timer = new Timer(CheckModelState, null, 10000, 10000);

            Console.WriteLine("{0}: Запуск MagiCAD", DateTime.Now.ToLongTimeString());
            var oldDir = Environment.CurrentDirectory;
            var proc = new Process();
            proc.StartInfo.FileName = Path.Combine(_acadDir,_acad);
            proc.StartInfo.Arguments = string.Format("\"{0}\" /b \"{1}\"  /nologo", tempFileName, scrPath);
            //proc.StartInfo.Arguments = string.Format("/b \"{0}\"  /nologo", scrPath);

            proc.StartInfo.WorkingDirectory = oldDir;
            proc.StartInfo.UseShellExecute = false;

            proc.EnableRaisingEvents = true;
            proc.Start();
            proc.WaitForExit(3600000);
            //timer.Dispose();
            /*var tempFiles = file.Directory.GetFiles("temp_*.dwg", SearchOption.TopDirectoryOnly);
            foreach (var t in tempFiles)
            {
                t.Delete();
            }*/
            try
            {
                File.Delete(scrPath);
            }
            catch
            {
            }

            Console.WriteLine("{0}:{1}", swa.Elapsed.Minutes, swa.Elapsed.Seconds);

            ifcFiles = Directory.GetFiles(IfcPath, file.Name.Replace(file.Extension, string.Empty) + ".ifc");
            var magicItems = new Dictionary<string[], MagicItem>();
            if (ifcFiles.Length <= 0)
            {
                return false;
            }
            Console.WriteLine("{0}: Запуск конвертации IFC", DateTime.Now.ToLongTimeString());
            magicItems = IfcParser.RunParse(ifcFiles.First());
            ClearTempDirectory(tempFileName);
            if (magicItems.Count == 0) return true;

            var xmlFormat = new XmlSerializer(typeof (MagicItem));

            var create = Common.CreateMagiCadTable();
            var modify = Common.CreateMagiCadTable();
            var delete = Common.CreateMagiCadTable();
            var existingTable = GetExistingItemsFromSQL(projectId);
            //var existingIdList = (from DataRow row in existingTable.Rows select row["element_guid"].ToString()).ToList();
            var existingDictionary = new Dictionary<string, string[]>();
            foreach (DataRow row in existingTable.Rows)
            {
                existingDictionary.Add(row["element_guid"].ToString(),
                    new[] { row["element_type"].ToString(), row["element_info"].ToString() });
            }
            var doneIdDictionary = new Dictionary<string, bool>();
            foreach (var item in magicItems)
            {
                
                var itemId = item.Key[0];
                var itemType = item.Key[1];
                string elementInfo;
                using (var stream = new StringWriter())
                {
                    using (var xmlWriter = XmlWriter.Create(stream, new XmlWriterSettings { Indent = false }))
                    {
                        xmlFormat.Serialize(xmlWriter, item.Value);
                    }
                    elementInfo = stream.ToString();
                }
                //if (!existingIdList.Contains(itemId))
                //{
                //    var existRows = existingTable.Select("element_type = '" + itemType + "' AND element_info like '" + elementInfo.Substring(0, elementInfo.IndexOf("<Description>")) + "%'");
                //    if (existRows.Length == 1)
                //    {
                //        itemId = existRows[0]["element_guid"].ToString();
                //    }
                //}
                if (!existingDictionary.ContainsKey(itemId) && !itemType.Equals("IfcCovering"))
                {
                    foreach (var pair in existingDictionary)
                    {
                        var value = pair.Value;
                        if (!value[0].Equals(itemType)) continue;
                        if (!value[1].Equals(elementInfo))
                            continue;
                        itemId = pair.Key;
                    }
                }
                if (existingDictionary.ContainsKey(itemId))
                {
                    var row = modify.NewRow();
                    row["element_guid"] = itemId;
                    row["element_type"] = itemType;
                    row["element_info"] = elementInfo;
                    modify.Rows.Add(row);
                }
                else
                {
                    var row = create.NewRow();
                    row["element_guid"] = itemId;
                    row["element_type"] = itemType;
                    row["element_info"] = elementInfo;
                    create.Rows.Add(row);
                }

                if (!doneIdDictionary.ContainsKey(itemId))
                {
                    doneIdDictionary.Add(itemId, false);
                }
                else
                {
                    Console.WriteLine("doneIdDictionary contains key\t{0}",itemId);
                }
            }

            foreach (var itemId in existingDictionary.Keys)
            {
                if (doneIdDictionary.ContainsKey(itemId)) continue;

                var row = delete.NewRow();
                row["element_guid"] = itemId;
                delete.Rows.Add(row);
            }

            Console.WriteLine("Обновляем данные в SQL");
            var result = Common.UpdateMagiCadData(create, modify, delete, projectId, triggerId);
            Console.WriteLine(result ? "Данные обновлены" : "Данные не обновлены");
            swa.Stop();
            Console.WriteLine("{0}:{1}", swa.Elapsed.Minutes, swa.Elapsed.Seconds);
            return result;
        }

        private static DataTable GetExistingItemsFromSQL(decimal projectId)
        {
            var dt = new DataTable();
            using (var sqlConnection = new SqlConnection(Common.ConnectionString))
            {
                sqlConnection.Open();
                var sqlCommand = "select element_guid, element_type, element_info from MagiCad where  project_id = " + projectId;
                using (var sqlCmd = new SqlCommand(sqlCommand, sqlConnection))
                {
                    using (var sqlAdapter = new SqlDataAdapter(sqlCmd))
                    {
                        sqlAdapter.Fill(dt);
                    }
                }
                sqlConnection.Close();
            }
            return dt;
        }

        private static string CopyToTempDirectory(FileInfo modelFile)
        {
            var tempModel = string.Empty;
            var extensions = new List<string> { ".dwg",".qpd", ".epj" };
            var files = modelFile.Directory.GetFiles("*.*", SearchOption.TopDirectoryOnly).Where(f => extensions.Contains(f.Extension.ToLower())).ToList();

            var tDir = new DirectoryInfo(IfcPath).CreateSubdirectory("temp_" + (int)(DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1))).TotalSeconds);
            foreach (var f in files)
            {
                var tf = f.CopyTo(Path.Combine(tDir.FullName, f.Name));
                if (f.FullName.Equals(modelFile.FullName))
                    tempModel = tf.FullName;
            }
            return tempModel;
        }

        private static void ClearTempDirectory(string tempModelName)
        {
            try
            {
                var modelFile = new FileInfo(tempModelName);
                var dir = modelFile.Directory;
                var files = dir.GetFiles();
                foreach (var f in files)
                    f.Delete();
                dir.Delete();

                var ifcFiles = Directory.GetFiles(IfcPath, "*.ifc*");
                foreach (var f in ifcFiles)
                {
                    File.Delete(f);
                }
            }
            catch (Exception exc)
            {
                Console.WriteLine(exc.Message);
            }
        }

        private static string CreateSrcFile()
        {
            var filePath = Path.Combine(Path.GetTempPath(), "ExportMagicCAD.scr");
            try
            {
                using (var sw = new StreamWriter(filePath, false, Encoding.UTF8))
                {
                    sw.WriteLine("_MAGIUCL");
                    sw.WriteLine("-MAGIIFC export");
                    sw.WriteLine();
                    sw.WriteLine("_.undo ОБРАТНО Д");
                    sw.WriteLine("_.quit Д");
                    sw.WriteLine();
                    sw.Close();
                }
            }
            catch (IOException ioex)
            {
                Console.WriteLine(ioex.Message);
                return string.Empty;
            }

            return File.Exists(filePath) ? filePath : string.Empty;
        }


        #region работа с окнами

        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern int SendMessage(IntPtr hWnd, uint msg, int wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass,
            string lpszWindow);

        private const uint WM_LBUTTONDOWN = 0x0201;
        private const uint WM_LBUTTONUP = 0x0202;

        private static string GetWindowText(IntPtr hWnd)
        {
            var len = GetWindowTextLength(hWnd) + 1;
            var sb = new StringBuilder(len);
            len = GetWindowText(hWnd, sb, len);
            return sb.ToString(0, len);
        }

        private static void CheckModelState(Object obj)
        {
            EnumWindows((hWnd, lParam) =>
            {
                if (GetWindowTextLength(hWnd) == 0) return true;

                var text = GetWindowText(hWnd);
                if (!text.Equals("MagiCAD")) return true;

                var btnWin = FindWindowEx(hWnd, IntPtr.Zero, "Button", "&Нет");
                if (btnWin == IntPtr.Zero) return true;

                SendMessage(btnWin, WM_LBUTTONDOWN, 0, IntPtr.Zero);
                SendMessage(btnWin, WM_LBUTTONUP, 0, IntPtr.Zero);
                return true;
            }, IntPtr.Zero);
        }

        #endregion

        //[Rt.CommandMethodAttribute("PMATTACHPDMS")]
        //public void AttachingExternalReference()
        //{
        //    var doc = cad.DocumentManager.MdiActiveDocument;
        //    doc.Editor.WriteMessage(string.Format("\nStart"));
        //    var file = new FileInfo(doc.Name);
        //    var path = Path.Combine(file.Directory.FullName, "pdms");
        //    var files = Directory.GetFiles(path, "*.dwg", SearchOption.AllDirectories);
        //    var db = doc.Database;
        //    using (var tr = db.TransactionManager.StartTransaction())
        //    {
        //        var bt = tr.GetObject(db.BlockTableId, Db.OpenMode.ForWrite) as Db.BlockTable;

        //        foreach (var objectId in bt)
        //        {
        //            var btr = tr.GetObject(objectId, Db.OpenMode.ForWrite) as Db.BlockTableRecord;
        //            if (!btr.IsLayout)
        //            {
        //                btr.Erase();
        //            }
        //        }

        //        tr.Commit();
        //    }
        //    doc.Editor.WriteMessage(string.Format("\nBlocks deleted"));

        //    for (int index = 0; index < files.Length; index++)
        //    {
        //        doc.Editor.WriteMessage(string.Format("\n{0}/{1}", index + 1, files.Length));
        //        var filePath = files[index];
        //        var fInfo = new FileInfo(filePath);
        //        var layerName = fInfo.Name.Replace(fInfo.Extension, string.Empty);

        //        using (var tr = db.TransactionManager.StartTransaction())
        //        {
        //            var lt = (Db.LayerTable) tr.GetObject(db.LayerTableId, Db.OpenMode.ForRead);
        //            if (!lt.Has(layerName))
        //            {
        //                var ltr = new Db.LayerTableRecord
        //                {
        //                    Name = layerName
        //                };

        //                if (layerName.Contains("оборудование"))
        //                {
        //                    ltr.Color = Color.FromRgb(252, 156, 156);
        //                }
        //                else if (layerName.Contains("трубопроводы"))
        //                {
        //                    ltr.Color = Color.FromRgb(82, 82, 250);
        //                }
        //                else if (layerName.Contains("воздуховоды"))
        //                {
        //                    ltr.Color = Color.FromRgb(8, 231, 161);
        //                }
        //                else if (layerName.Contains("металлоконструкции"))
        //                {
        //                    ltr.Color = Color.FromRgb(0, 63, 255);
        //                }
        //                else if (layerName.Contains("полы") || layerName.Contains("стены") ||
        //                         layerName.Contains("кровля"))
        //                {
        //                    ltr.Color = Color.FromRgb(181, 181, 172);
        //                }
        //                else
        //                {
        //                    ltr.Color = Color.FromRgb(183, 0, 255);
        //                }

        //                lt.UpgradeOpen();
        //                lt.Add(ltr);
        //                tr.AddNewlyCreatedDBObject(ltr, true);
        //            }

        //            db.Clayer = lt[layerName];

        //            var bt = tr.GetObject(db.BlockTableId, Db.OpenMode.ForRead) as Db.BlockTable;
        //            var model =
        //                tr.GetObject(bt[Db.BlockTableRecord.ModelSpace], Db.OpenMode.ForWrite) as Db.BlockTableRecord;

        //            using (var db1 = new Db.Database(false, false))
        //            {
        //                db1.ReadDwgFile(fInfo.FullName, FileShare.Read, true, null);
        //                var BlkId = db.Insert(fInfo.FullName, db1, false);
        //                var bref = new Db.BlockReference(new Point3d(0, 0, 0), BlkId);
        //                bref.SetDatabaseDefaults();
        //                model.AppendEntity(bref);
        //                tr.AddNewlyCreatedDBObject(bref, true);
        //                tr.Commit();
        //            }
        //        }
        //    }
        //}

        //[Rt.CommandMethodAttribute("PMTEST")]
        //public void test()
        //{
        //    var asAmd = false;
        //    var printIt = true;
        //    try
        //    {
        //        Autodesk.AutoCAD.Runtime.SystemObjects.DynamicLinker.LoadModule(
        //            "C:\\Program Files\\MagiCAD\\MagiCAD_r20x64.arx", false, true);
        //    }
        //    catch (Exception exc)
        //    {
        //        doc.Editor.WriteMessage(string.Format("\n* {0}", exc.Message));

        //    }

        //    /*Rt.SystemObjects.DynamicLinker.LoadModule("C:\\Program Files\\MagiCAD\\MAGIMCPropertiesPalette_r20x64.arx", printIt, asAmd);
        //    Rt.SystemObjects.DynamicLinker.LoadModule("C:\\Program Files\\MagiCAD\\McCommon_r20x64.arx", printIt, asAmd);
        //    Rt.SystemObjects.DynamicLinker.LoadModule("C:\\Program Files\\MagiCAD\\McCs_r20x64.arx", printIt, asAmd);
        //    Rt.SystemObjects.DynamicLinker.LoadModule("C:\\Program Files\\MagiCAD\\Mce_r20x64.arx", printIt, asAmd);
        //    Rt.SystemObjects.DynamicLinker.LoadModule("C:\\Program Files\\MagiCAD\\MCHeatEx_r20x64.arx", printIt, asAmd);
        //    Rt.SystemObjects.DynamicLinker.LoadModule("C:\\Program Files\\MagiCAD\\McHpv_r20x64.arx", printIt, asAmd);
        //    Rt.SystemObjects.DynamicLinker.LoadModule("C:\\Program Files\\MagiCAD\\Mcr_r20x64.arx", printIt, asAmd);
        //    Rt.SystemObjects.DynamicLinker.LoadModule("C:\\Program Files\\MagiCAD\\McSd_r20x64.arx", printIt, asAmd);
        //    Rt.SystemObjects.DynamicLinker.LoadModule("C:\\Program Files\\MagiCAD\\MCUtilities_r20x64.arx", printIt, asAmd);
        //    Rt.SystemObjects.DynamicLinker.LoadModule("C:\\Program Files\\MagiCAD\\McAECExt_r200x64.arx", printIt, asAmd);*/
        //}

        //[Rt.CommandMethodAttribute("PMEXPORTIFC")]
        //public void ExportIfcForPdms()
        //{
        //    doc = cad.DocumentManager.MdiActiveDocument;
        //    var file = new FileInfo(doc.Name);
        //    if (!file.Exists) return;
        //    var projectName = file.Name.Replace(file.Extension, string.Empty);
        //    var ifcFiles = Directory.GetFiles(ifcTempPath, projectName + ".*");
        //    foreach (var f in ifcFiles)
        //    {
        //        try
        //        {
        //            File.Delete(f);
        //        }
        //        catch
        //        {
        //        }
        //    }
        //    using (var tr = doc.Database.TransactionManager.StartTransaction())
        //    {
        //        try
        //        {
        //            //doc.Editor.Command("-MAGIIFC", "export");
        //            doc.SendStringToExecute("-MAGIIFC export\n", false, false, false);
        //        }
        //        catch (Exception exc)
        //        {
        //            doc.Editor.WriteMessage(string.Format("\n*Export IFC error {0}", exc.Message));
        //        }
        //        tr.Commit();
        //    }
        //    using (var sw = new StreamWriter(@"F:\AVEVA\temp\MagiCAD\ifc\1.txt"))
        //    {
        //        sw.Close();
        //    }
        //}
    }
}