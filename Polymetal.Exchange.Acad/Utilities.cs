using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Drawing;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.Geometry;
using cad = Autodesk.AutoCAD.ApplicationServices.Application;
using Ap = Autodesk.AutoCAD.ApplicationServices;
using Color = System.Drawing.Color;
using Db = Autodesk.AutoCAD.DatabaseServices;
using Rt = Autodesk.AutoCAD.Runtime;

namespace Polymetal.Exchange.Acad
{
    /*
     *Библиотеку необходимо скопировать на сервер в C:\\Program Files\\Autodesk\\ApplicationPlugins\\Polymetal\\Polymetal.Exchange.Acad.dll     *
     */
    public class Utilities
    {
        private Ap.Document doc;
        private static string _connectionString = new sqlReference.Service1().GetStrConnectPdms();
        
        [Rt.CommandMethodAttribute("PMMERGEPDMSFORTEKLA")]
        public void MergeDwgFilesForTekla()
        {
            doc = cad.DocumentManager.MdiActiveDocument;
            var structInfoDict = GetStructClassInfo();
            var arStructInfo = GetARStructInfo();
            var kgStructInfo = GetKGStructInfo();
            var kmStructInfo = GetKMStructInfo();
            var file = new FileInfo(doc.Name);
            var department = file.Name.Split('_').First();
            var type = file.Name.Replace(department+"_",string.Empty).Replace(file.Extension, string.Empty);
            var departmentDir = new DirectoryInfo(Path.Combine(file.Directory.Parent.FullName, department));
            var dwgDictionary = new Dictionary<string, List<FileInfo>>();
            var dwgFiles = departmentDir.GetFiles("*.dwg", SearchOption.AllDirectories);
            foreach (var dwg in dwgFiles)
            {
                var parentName = dwg.Directory.Name.ToUpper();
                if (!departmentDir.Name.Equals("SO") && !structInfoDict.ContainsKey(parentName))
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
                        if (splitParent.Length > 1 && !arStructInfo.ContainsKey(splitParent[1]) &&
                            !arStructInfo.ContainsKey(splitParent[1] + "_" + splitParent[2]))
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
                        if (splitParent.Length > 1 && !kgStructInfo.ContainsKey(splitParent[1]) &&
                            !kgStructInfo.ContainsKey(splitParent[1] + "_" + splitParent[2]))
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
                             !kmStructInfo.ContainsKey(splitParent[1] + "_" + splitParent[2])))
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

            if (!dwgDictionary.ContainsKey(type)) return;

            var db = doc.Database;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = tr.GetObject(db.BlockTableId, Db.OpenMode.ForWrite) as Db.BlockTable;

                foreach (var objectId in bt)
                {
                    var btr = tr.GetObject(objectId, Db.OpenMode.ForWrite) as Db.BlockTableRecord;
                    if (!btr.IsLayout)
                    {
                        btr.Erase();
                    }
                }

                tr.Commit();
            }

            var colourDictionary = InitializeAvevaColours();
            for (var index = 0; index < dwgDictionary[type].Count; index++)
            {
                var fInfo = dwgDictionary[type][index];
                var fName = fInfo.Name.Replace(fInfo.Extension, string.Empty);
                //var layerName = fInfo.Name.Replace(fInfo.Extension, string.Empty);
                var colourName = fName.Split('_').Last();
                var layerName =
                    fInfo.FullName.Replace(departmentDir.Parent.FullName + "\\", string.Empty)
                        .Replace("\\", "_")
                        .Replace("+", string.Empty)
                        .Replace(fInfo.Extension, string.Empty)
                        .Replace(".", string.Empty)
                        .Replace(",", string.Empty)
                        .Replace("_" + colourName, string.Empty);
                
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var lt = (Db.LayerTable) tr.GetObject(db.LayerTableId, Db.OpenMode.ForRead);
                    if (!lt.Has(layerName))
                    {
                        var ltr = new Db.LayerTableRecord
                        {
                            Name = layerName,
                            Color =
                                colourDictionary.ContainsKey(colourName)
                                    ? Autodesk.AutoCAD.Colors.Color.FromColor(colourDictionary[colourName])
                                    : Autodesk.AutoCAD.Colors.Color.FromRgb(255, 255, 255)
                        };

                        lt.UpgradeOpen();
                        lt.Add(ltr);
                        tr.AddNewlyCreatedDBObject(ltr, true);
                    }

                    db.Clayer = lt[layerName];

                    var bt = tr.GetObject(db.BlockTableId, Db.OpenMode.ForRead) as Db.BlockTable;
                    var model =
                        tr.GetObject(bt[Db.BlockTableRecord.ModelSpace], Db.OpenMode.ForWrite) as
                            Db.BlockTableRecord;

                    using (var db1 = new Db.Database(false, false))
                    {
                        db1.ReadDwgFile(fInfo.FullName, FileShare.Read, true, null);
                        var blockIndex = 1;
                        using (var tr1 = db1.TransactionManager.StartTransaction())
                        {
                            var bt1 = tr1.GetObject(db1.BlockTableId, Db.OpenMode.ForWrite) as Db.BlockTable;

                            foreach (var objectId1 in bt1)
                            {
                                var btr1 = tr1.GetObject(objectId1, Db.OpenMode.ForWrite) as Db.BlockTableRecord;
                                if (btr1.Name.Contains("_Space")) continue;

                                btr1.Name =
                                    fInfo.FullName.Replace(departmentDir.Parent.FullName+"\\", string.Empty)
                                        .Replace("\\", "_")
                                        .Replace("+", string.Empty)
                                        .Replace(fInfo.Extension, string.Empty)
                                        .Replace(".", string.Empty)
                                        .Replace(",", string.Empty) +
                                    "_" + blockIndex;
                                blockIndex++;
                            }

                            tr1.Commit();
                        }

                        var BlkId = db.Insert(fInfo.FullName, db1, false);
                        var bref = new Db.BlockReference(new Point3d(0, 0, 0), BlkId);
                        bref.SetDatabaseDefaults();
                        model.AppendEntity(bref);
                        tr.AddNewlyCreatedDBObject(bref, true);
                        tr.Commit();
                    }
                }
            }
        }

        [Rt.CommandMethodAttribute("PMMERGEPDMSFORTEKLATASK")]
        public void MergeDwgFilesForTeklaTask()
        {
            doc = cad.DocumentManager.MdiActiveDocument;

            var file = new FileInfo(doc.Name);
            var department = file.Name.Split('_').First();

            var funcDictionary = new Dictionary<string, string>();
            funcDictionary.Add("DOOR","двери");
            funcDictionary.Add("EQUI","оборудование");
            funcDictionary.Add("FUND", "фундаменты");
            funcDictionary.Add("HOLE", "отверстия");
            funcDictionary.Add("NAGRUZKA", "нагрузки");
            funcDictionary.Add("OGRAG", "ограждения");
            funcDictionary.Add("PLATC", "площадки");
            funcDictionary.Add("STRU", "металлоконструкции");
            funcDictionary.Add("LESTN", "лестницы");
            funcDictionary.Add("OPOR", "опоры");
            

            var type = file.Name.Replace(file.Extension, string.Empty);
            var departmentDir = new DirectoryInfo(Path.Combine(file.Directory.Parent.FullName, "ZDN\\"+department));
            var dwgDictionary = new Dictionary<string, List<FileInfo>>();
            var dwgFiles = departmentDir.GetFiles("*.dwg", SearchOption.AllDirectories);
            foreach (var dwg in dwgFiles)
            {
                var parentName = dwg.Directory.Parent.FullName.Replace(departmentDir.Parent.FullName + "\\", string.Empty).Replace("\\", "_").Replace("-", "_").ToUpper();
                if (parentName.Split('_').Length < 2)
                {
                    parentName = dwg.Directory.FullName.Replace(departmentDir.Parent.FullName + "\\", string.Empty).Replace("\\", "_").Replace("-", "_").ToUpper();
                }
                
                if (dwgDictionary.ContainsKey(parentName))
                {
                    dwgDictionary[parentName].Add(dwg);
                }
                else
                {
                    dwgDictionary.Add(parentName, new List<FileInfo> { dwg });
                }
            }

            if (!dwgDictionary.ContainsKey(type)) return;

            var db = doc.Database;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = tr.GetObject(db.BlockTableId, Db.OpenMode.ForWrite) as Db.BlockTable;

                foreach (var objectId in bt)
                {
                    var btr = tr.GetObject(objectId, Db.OpenMode.ForWrite) as Db.BlockTableRecord;
                    if (!btr.IsLayout)
                    {
                        btr.Erase();
                    }
                }

                tr.Commit();
            }

            var colourDictionary = InitializeAvevaColours();
            for (var index = 0; index < dwgDictionary[type].Count; index++)
            {
                var fInfo = dwgDictionary[type][index];
                var fName = fInfo.Name.Replace(fInfo.Extension, string.Empty);
                //var layerName = fInfo.Name.Replace(fInfo.Extension, string.Empty);
                var colourName = fName.Split('_').Last();
                //var layerName =
                //    fInfo.FullName.Replace(departmentDir.Parent.FullName + "\\", string.Empty)
                //        .Replace("\\", "|")
                //        .Replace("+", string.Empty)
                //        .Replace(fInfo.Extension, string.Empty)
                //        .Replace(".", string.Empty)
                //        .Replace("_" + colourName, string.Empty);
                var layerName = fInfo.Directory.Name;
                if (funcDictionary.ContainsKey(layerName))
                {
                    layerName = funcDictionary[layerName];
                }
                else
                {
                    layerName = "прочее";
                }

                var alpha = (Byte)255;
                if (fInfo.Name.Contains("_obstruction_"))
                {
                    layerName += " - объем резервирования";
                    alpha = 255 * (100 - 90) / 100;
                }


                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var lt = (Db.LayerTable)tr.GetObject(db.LayerTableId, Db.OpenMode.ForRead);
                    if (!lt.Has(layerName))
                    {
                        var ltr = new Db.LayerTableRecord
                        {
                            Name = layerName,
                            Color =
                                colourDictionary.ContainsKey(colourName)
                                    ? Autodesk.AutoCAD.Colors.Color.FromColor(colourDictionary[colourName])
                                    : Autodesk.AutoCAD.Colors.Color.FromRgb(255, 255, 255)
                        };

                        lt.UpgradeOpen();
                        lt.Add(ltr);
                        ltr.Transparency = new Transparency(alpha);
                        tr.AddNewlyCreatedDBObject(ltr, true);
                    }

                    db.Clayer = lt[layerName];

                    var bt = tr.GetObject(db.BlockTableId, Db.OpenMode.ForRead) as Db.BlockTable;
                    var model =
                        tr.GetObject(bt[Db.BlockTableRecord.ModelSpace], Db.OpenMode.ForWrite) as
                            Db.BlockTableRecord;

                    using (var db1 = new Db.Database(false, false))
                    {
                        db1.ReadDwgFile(fInfo.FullName, FileShare.Read, true, null);
                        var blockIndex = 1;
                        using (var tr1 = db1.TransactionManager.StartTransaction())
                        {
                            var bt1 = tr1.GetObject(db1.BlockTableId, Db.OpenMode.ForWrite) as Db.BlockTable;

                            foreach (var objectId1 in bt1)
                            {
                                var btr1 = tr1.GetObject(objectId1, Db.OpenMode.ForWrite) as Db.BlockTableRecord;
                                if (btr1.Name.Contains("_Space")) continue;

                                btr1.Name =
                                    fInfo.FullName.Replace(departmentDir.Parent.FullName + "\\", string.Empty)
                                        .Replace("\\", "_")
                                        .Replace("+", string.Empty)
                                        .Replace(fInfo.Extension, string.Empty)
                                        .Replace(".", string.Empty) +
                                    "_" + blockIndex;
                                blockIndex++;
                            }

                            tr1.Commit();
                        }

                        var BlkId = db.Insert(fInfo.FullName, db1, false);
                        var bref = new Db.BlockReference(new Point3d(0, 0, 0), BlkId);
                        bref.SetDatabaseDefaults();
                        model.AppendEntity(bref);
                        tr.AddNewlyCreatedDBObject(bref, true);
                        tr.Commit();
                    }
                }
            }
        }

        [Rt.CommandMethodAttribute("PMMERGEPDMSDWG")]
        public void MergeDwgFiles()
        {
            doc = cad.DocumentManager.MdiActiveDocument;
            var structInfoDict = GetStructClassInfo();
            var arStructInfo = GetARStructInfo();
            var kgStructInfo = GetKGStructInfo();
            var kmStructInfo = GetKMStructInfo();
            var file = new FileInfo(doc.Name);
            var department = file.Name.Split('_').First();
            var type = file.Name.Replace(department + "_", string.Empty).Replace(file.Extension, string.Empty);
            var departmentDir = new DirectoryInfo(Path.Combine(file.Directory.FullName, department));
            var dwgDictionary = new Dictionary<string, List<FileInfo>>();
            var dwgFiles = departmentDir.GetFiles("*.dwg", SearchOption.AllDirectories);
            foreach (var dwg in dwgFiles)
            {
                var parentName = dwg.Directory.Name.ToUpper();
                if (!departmentDir.Name.Equals("SO") && !structInfoDict.ContainsKey(parentName))
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
                        if (splitParent.Length > 1 && !arStructInfo.ContainsKey(splitParent[1]) &&
                            !arStructInfo.ContainsKey(splitParent[1] + "_" + splitParent[2]))
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
                        if (splitParent.Length > 1 && !kgStructInfo.ContainsKey(splitParent[1]) &&
                            !kgStructInfo.ContainsKey(splitParent[1] + "_" + splitParent[2]))
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
                             !kmStructInfo.ContainsKey(splitParent[1] + "_" + splitParent[2])))
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
                    dwgDictionary.Add(parentName, new List<FileInfo> { dwg });
                }
            }

            if (!dwgDictionary.ContainsKey(type)) return;

            var db = doc.Database;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = tr.GetObject(db.BlockTableId, Db.OpenMode.ForWrite) as Db.BlockTable;

                foreach (var objectId in bt)
                {
                    var btr = tr.GetObject(objectId, Db.OpenMode.ForWrite) as Db.BlockTableRecord;
                    if (!btr.IsLayout)
                    {
                        btr.Erase();
                    }
                }

                tr.Commit();
            }

            var colourDictionary = InitializeAvevaColours();
            for (var index = 0; index < dwgDictionary[type].Count; index++)
            {
                var fInfo = dwgDictionary[type][index];
                var fName = fInfo.Name.Replace(fInfo.Extension, string.Empty);
                //var layerName = fInfo.Name.Replace(fInfo.Extension, string.Empty);
                var colourName = fName.Split('_').Last();
                var layerName = GetMagicadLayerName(fInfo, departmentDir, colourName);

                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var lt = (Db.LayerTable)tr.GetObject(db.LayerTableId, Db.OpenMode.ForRead);
                    if (!lt.Has(layerName))
                    {
                        var ltr = new Db.LayerTableRecord
                        {
                            Name = layerName,
                            Color =
                                colourDictionary.ContainsKey(colourName)
                                    ? Autodesk.AutoCAD.Colors.Color.FromColor(colourDictionary[colourName])
                                    : Autodesk.AutoCAD.Colors.Color.FromRgb(255, 255, 255)
                        };

                        lt.UpgradeOpen();
                        lt.Add(ltr);
                        tr.AddNewlyCreatedDBObject(ltr, true);
                    }

                    db.Clayer = lt[layerName];

                    var bt = tr.GetObject(db.BlockTableId, Db.OpenMode.ForRead) as Db.BlockTable;
                    var model =
                        tr.GetObject(bt[Db.BlockTableRecord.ModelSpace], Db.OpenMode.ForWrite) as
                            Db.BlockTableRecord;

                    using (var db1 = new Db.Database(false, false))
                    {
                        db1.ReadDwgFile(fInfo.FullName, FileShare.Read, true, null);
                        var blockIndex = 1;
                        using (var tr1 = db1.TransactionManager.StartTransaction())
                        {
                            var bt1 = tr1.GetObject(db1.BlockTableId, Db.OpenMode.ForWrite) as Db.BlockTable;

                            foreach (var objectId1 in bt1)
                            {
                                var btr1 = tr1.GetObject(objectId1, Db.OpenMode.ForWrite) as Db.BlockTableRecord;
                                if (btr1.Name.Contains("_Space")) continue;

                                btr1.Name =
                                    fInfo.FullName.Replace(departmentDir.Parent.FullName + "\\", string.Empty)
                                        .Replace("\\", "_")
                                        .Replace("+", string.Empty)
                                        .Replace(fInfo.Extension, string.Empty)
                                        .Replace(".", string.Empty)
                                        .Replace(",", string.Empty) +
                                    "_" + blockIndex;
                                blockIndex++;
                            }

                            tr1.Commit();
                        }

                        var BlkId = db.Insert(fInfo.FullName, db1, false);
                        var bref = new Db.BlockReference(new Point3d(0, 0, 0), BlkId);
                        bref.SetDatabaseDefaults();
                        model.AppendEntity(bref);
                        tr.AddNewlyCreatedDBObject(bref, true);
                        tr.Commit();
                    }
                }
            }
        }

        [Rt.CommandMethodAttribute("PMMERGEPDMSINONE")]
        public void MergeAllDwgFiles()
        {
            doc = cad.DocumentManager.MdiActiveDocument;
            var file = new FileInfo(doc.Name);
            var dwgFiles =file.Directory.GetFiles("*.dwg", SearchOption.AllDirectories).Where(x=>x.FullName != file.FullName).ToList();
            
            var db = doc.Database;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = tr.GetObject(db.BlockTableId, Db.OpenMode.ForWrite) as Db.BlockTable;

                foreach (var objectId in bt)
                {
                    var btr = tr.GetObject(objectId, Db.OpenMode.ForWrite) as Db.BlockTableRecord;
                    if (!btr.IsLayout)
                    {
                        btr.Erase();
                    }
                }

                tr.Commit();
            }

            var colourDictionary = InitializeAvevaColours();
            for (var index = 0; index < dwgFiles.Count; index++)
            {
                var fInfo = dwgFiles[index];
                var fName = fInfo.Name.Replace(fInfo.Extension, string.Empty);
                
                var colourName = fName.Split('_').Last();
                fName = fName.Replace("_" + colourName, string.Empty);

                var layerName = fInfo.Directory.FullName.Replace(file.Directory.Parent.FullName,string.Empty);
                if (!fName.Contains("#"))
                    layerName+="_"+fName;

                layerName=layerName
                        .Replace("\\", "_")
                        .Replace("+", string.Empty)
                        .Replace(".", string.Empty)
                        .Replace(",", string.Empty);
                layerName = "0";
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var lt = (Db.LayerTable)tr.GetObject(db.LayerTableId, Db.OpenMode.ForRead);
                    if (!lt.Has(layerName))
                    {
                        var ltr = new Db.LayerTableRecord
                        {
                            Name = layerName,
                            Color =
                                colourDictionary.ContainsKey(colourName)
                                    ? Autodesk.AutoCAD.Colors.Color.FromColor(colourDictionary[colourName])
                                    : Autodesk.AutoCAD.Colors.Color.FromRgb(255, 255, 255)
                        };

                        lt.UpgradeOpen();
                        lt.Add(ltr);
                        tr.AddNewlyCreatedDBObject(ltr, true);
                    }

                    db.Clayer = lt[layerName];

                    var bt = tr.GetObject(db.BlockTableId, Db.OpenMode.ForRead) as Db.BlockTable;
                    var model =
                        tr.GetObject(bt[Db.BlockTableRecord.ModelSpace], Db.OpenMode.ForWrite) as
                            Db.BlockTableRecord;

                    using (var db1 = new Db.Database(false, false))
                    {
                        db1.ReadDwgFile(fInfo.FullName, FileShare.Read, true, null);
                        var blockIndex = 1;
                        using (var tr1 = db1.TransactionManager.StartTransaction())
                        {
                            var bt1 = tr1.GetObject(db1.BlockTableId, Db.OpenMode.ForWrite) as Db.BlockTable;

                            foreach (var objectId1 in bt1)
                            {
                                var btr1 = tr1.GetObject(objectId1, Db.OpenMode.ForWrite) as Db.BlockTableRecord;
                                if (btr1.Name.Contains("_Space")) continue;

                                btr1.Name =
                                    fInfo.FullName.Replace(file.Directory.FullName + "\\", string.Empty)
                                        .Replace("\\", "_")
                                        .Replace("+", string.Empty)
                                        .Replace(fInfo.Extension, string.Empty)
                                        .Replace(".", string.Empty)
                                        .Replace(",", string.Empty) +
                                    "_" + blockIndex;
                                blockIndex++;
                            }

                            tr1.Commit();
                        }

                        var BlkId = db.Insert(fInfo.FullName, db1, false);
                        var bref = new Db.BlockReference(new Point3d(0, 0, 0), BlkId);
                        bref.SetDatabaseDefaults();
                        model.AppendEntity(bref);
                        tr.AddNewlyCreatedDBObject(bref, true);
                        tr.Commit();
                    }
                }
            }
        }

        [Rt.CommandMethodAttribute("PMMERGEPDMSHATCH")]
        public void MergeDwgFilesForHatch()
        {
            doc = cad.DocumentManager.MdiActiveDocument;
            var file = new FileInfo(doc.Name);
            var dwgFiles = file.Directory.GetFiles("*.dwg", SearchOption.TopDirectoryOnly).Where(x => x.FullName != file.FullName).ToList();

            var db = doc.Database;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = tr.GetObject(db.BlockTableId, Db.OpenMode.ForWrite) as Db.BlockTable;

                foreach (var objectId in bt)
                {
                    var btr = tr.GetObject(objectId, Db.OpenMode.ForWrite) as Db.BlockTableRecord;
                    if (!btr.IsLayout)
                    {
                        btr.Erase();
                    }
                }

                tr.Commit();
            }

            for (var index = 0; index < dwgFiles.Count; index++)
            {
                var fInfo = dwgFiles[index];
                var fName = fInfo.Name.Replace(fInfo.Extension, string.Empty);

                var layerName = fName
                        .Replace("\\", "_")
                        .Replace("+", string.Empty)
                        .Replace(".", string.Empty)
                        .Replace(",", string.Empty);

                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var lt = (Db.LayerTable)tr.GetObject(db.LayerTableId, Db.OpenMode.ForRead);
                    if (!lt.Has(layerName))
                    {
                        var ltr = new Db.LayerTableRecord
                        {
                            Name = layerName,
                            Color = Autodesk.AutoCAD.Colors.Color.FromRgb(255, 255, 255)                                
                        };

                        lt.UpgradeOpen();
                        lt.Add(ltr);
                        tr.AddNewlyCreatedDBObject(ltr, true);
                    }

                    db.Clayer = lt[layerName];

                    var bt = tr.GetObject(db.BlockTableId, Db.OpenMode.ForRead) as Db.BlockTable;
                    var model =
                        tr.GetObject(bt[Db.BlockTableRecord.ModelSpace], Db.OpenMode.ForWrite) as
                            Db.BlockTableRecord;

                    using (var db1 = new Db.Database(false, false))
                    {
                        db1.ReadDwgFile(fInfo.FullName, FileShare.Read, true, null);
                        var blockIndex = 1;
                        using (var tr1 = db1.TransactionManager.StartTransaction())
                        {
                            var bt1 = tr1.GetObject(db1.BlockTableId, Db.OpenMode.ForWrite) as Db.BlockTable;

                            foreach (var objectId1 in bt1)
                            {
                                var btr1 = tr1.GetObject(objectId1, Db.OpenMode.ForWrite) as Db.BlockTableRecord;
                                if (btr1.Name.Contains("_Space")) continue;

                                btr1.Name =
                                    fInfo.FullName.Replace(file.Directory.FullName + "\\", string.Empty)
                                        .Replace("\\", "_")
                                        .Replace("+", string.Empty)
                                        .Replace(fInfo.Extension, string.Empty)
                                        .Replace(".", string.Empty)
                                        .Replace(",", string.Empty) +
                                    "_" + blockIndex;
                                blockIndex++;
                            }

                            tr1.Commit();
                        }

                        var BlkId = db.Insert(fInfo.FullName, db1, false);
                        var bref = new Db.BlockReference(new Point3d(0, 0, 0), BlkId);
                        bref.SetDatabaseDefaults();
                        model.AppendEntity(bref);
                        tr.AddNewlyCreatedDBObject(bref, true);
                        tr.Commit();
                    }
                }
            }
        }

        private static string GetMagicadLayerName(FileInfo fInfo, DirectoryInfo departmentDir,string colourName)
        {
            var result = "00";
            if (departmentDir.Name.Equals("SANO"))
            {
                var tempName= fInfo.FullName.Replace(departmentDir.Parent.FullName + "\\", string.Empty)
                    .Replace("+", string.Empty)
                    .Replace(fInfo.Extension, string.Empty)
                    .Replace(".", string.Empty)
                    .Replace(",", string.Empty)
                    .Replace("_" + colourName, string.Empty).Split('\\');
                var mark = tempName.Length>1?tempName[1].Split('-').First():string.Empty;
                var system = tempName.Length > 2 ? tempName[2] : string.Empty;
                //var subSystem = tempName.Length > 3 && !tempName[3].Equals(fInfo.Directory.Name, StringComparison.OrdinalIgnoreCase) ? tempName[3] : string.Empty;
                result = string.Format("{0} {1}", mark, system).Trim();
            }
            else if (departmentDir.Name.Equals("OOR") || departmentDir.Name.Equals("TO"))
            {
                var tempName = fInfo.FullName.Replace(departmentDir.Parent.FullName + "\\", string.Empty)
                    .Replace("+", string.Empty)
                    .Replace(fInfo.Extension, string.Empty)
                    .Replace(".", string.Empty)
                    .Replace("_" + colourName, string.Empty).Split('\\');
                if (tempName.Length > 2)
                {
                    result = "уч." + tempName[2];
                }
            }

            return result;
        }

        /// <summary>
        /// Получение кодов и описания для объединения моделей
        /// </summary>
        /// <returns></returns>
        private static Dictionary<string, string> GetStructClassInfo()
        {
            var dict = new Dictionary<string, string>();
            using (var sqlConnection = new SqlConnection(_connectionString))
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
            using (var sqlConnection = new SqlConnection(_connectionString))
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
            using (var sqlConnection = new SqlConnection(_connectionString))
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
            using (var sqlConnection = new SqlConnection(_connectionString))
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

        /// <summary>
        /// Соответствие цветов PDMS и html формата
        /// </summary>
        /// <returns></returns>
        private Dictionary<string, Color> InitializeAvevaColours()
        {
            var dictionary = new Dictionary<string, Color>();
            dictionary.Add("aquamarine", ColorTranslator.FromHtml("#00ffbe"));
            dictionary.Add("beige", ColorTranslator.FromHtml("#bcac7d"));
            dictionary.Add("black", ColorTranslator.FromHtml("#000000"));
            dictionary.Add("blue", ColorTranslator.FromHtml("#0000ff"));
            dictionary.Add("brightorange", ColorTranslator.FromHtml("#ffbe00"));
            dictionary.Add("brightred", ColorTranslator.FromHtml("#ff0000"));
            dictionary.Add("brown", ColorTranslator.FromHtml("#683400"));
            dictionary.Add("chocolate", ColorTranslator.FromHtml("#4f2700"));
            dictionary.Add("coralred", ColorTranslator.FromHtml("#bc2d00"));
            dictionary.Add("cyan", ColorTranslator.FromHtml("#00ffff"));
            dictionary.Add("darkbrown", ColorTranslator.FromHtml("#4f3a00"));
            dictionary.Add("darkgreen", ColorTranslator.FromHtml("#004f13"));
            dictionary.Add("darkgrey", ColorTranslator.FromHtml("#505050"));
            dictionary.Add("darkslate", ColorTranslator.FromHtml("#354f48"));
            dictionary.Add("deeppink", ColorTranslator.FromHtml("#ff007e"));
            dictionary.Add("forestgreen", ColorTranslator.FromHtml("#1f8100"));
            dictionary.Add("gold", ColorTranslator.FromHtml("#ffbe00"));
            dictionary.Add("green", ColorTranslator.FromHtml("#00bc00"));
            dictionary.Add("grey", ColorTranslator.FromHtml("#828282"));
            dictionary.Add("indigo", ColorTranslator.FromHtml("#340068"));
            dictionary.Add("ivory", ColorTranslator.FromHtml("#ffffaa"));
            dictionary.Add("khaki", ColorTranslator.FromHtml("#818155"));
            dictionary.Add("lightgold", ColorTranslator.FromHtml("#ffeaaa"));
            dictionary.Add("lightgrey", ColorTranslator.FromHtml("#bdbdbd"));
            dictionary.Add("lightyellow", ColorTranslator.FromHtml("#ffffaa"));
            dictionary.Add("magenta", ColorTranslator.FromHtml("#ff00ff"));
            dictionary.Add("maroon", ColorTranslator.FromHtml("#68004e"));
            dictionary.Add("mauve", ColorTranslator.FromHtml("#5f0081"));
            dictionary.Add("midnight", ColorTranslator.FromHtml("#00134f"));
            dictionary.Add("navyblue", ColorTranslator.FromHtml("#000068"));
            dictionary.Add("orange", ColorTranslator.FromHtml("#ffbe00"));
            dictionary.Add("orangered", ColorTranslator.FromHtml("#ff7e00"));
            dictionary.Add("pink", ColorTranslator.FromHtml("#bc7d8d"));
            dictionary.Add("plum", ColorTranslator.FromHtml("#815581"));
            dictionary.Add("powderblue", ColorTranslator.FromHtml("#aaeaff"));
            dictionary.Add("red", ColorTranslator.FromHtml("#bc0000"));
            dictionary.Add("royalblue", ColorTranslator.FromHtml("#003eff"));
            dictionary.Add("salmon", ColorTranslator.FromHtml("#ffbeaa"));
            dictionary.Add("sandybrown", ColorTranslator.FromHtml("#bc8d00"));
            dictionary.Add("springgreen", ColorTranslator.FromHtml("#00ff7e"));
            dictionary.Add("steelblue", ColorTranslator.FromHtml("#008dbc"));
            dictionary.Add("tan", ColorTranslator.FromHtml("#bc8d7d"));
            dictionary.Add("tomato", ColorTranslator.FromHtml("#ff3e00"));
            dictionary.Add("turquoise", ColorTranslator.FromHtml("#00bcbc"));
            dictionary.Add("violet", ColorTranslator.FromHtml("#ffaaea"));
            dictionary.Add("wheat", ColorTranslator.FromHtml("#ffd3aa"));
            dictionary.Add("white", ColorTranslator.FromHtml("#ffffff"));
            dictionary.Add("whitesmoke", ColorTranslator.FromHtml("#ffffff"));
            dictionary.Add("yellow", ColorTranslator.FromHtml("#ffff00"));
            dictionary.Add("yellowgreen", ColorTranslator.FromHtml("#8dbc00"));
            return dictionary;
        }
    }
}
