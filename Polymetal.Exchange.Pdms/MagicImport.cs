using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using Aveva.Pdms.Database;
using Aveva.Pdms.Geometry;
using Aveva.Pdms.Utilities.CommandLine;
using Aveva.PDMS.Database.Filters;
using Polymetal.Exchange.Pdms.Properties;
using Polymetal.Exchange.Utilities;
using Tekla.Structures.Geometry3d;

namespace Polymetal.Exchange.Pdms
{
    class MagicImport
    {
        private static DbElement _rootZone = DbElement.GetElement();
        private static DbElement _sanoSite = DbElement.GetElement();
        private static XmlSerializer _xmlAcadFormat = new XmlSerializer(typeof(MagicItem));
        private static XmlSerializer _xmlRevitFormat = new XmlSerializer(typeof(IfcElementInformation));

        internal static bool Run()
        {
            var result = false;
            if (RunPdms.ProjectInfo.Program.Path.Split(';').First().Trim().EndsWith(".rvt"))
            {
                result = DoMagiCadRevit();
            }
            else if (RunPdms.ProjectInfo.Program.Path.Split(';').First().Trim().EndsWith(".dwg"))
            {
                result = DoMagiCadAcad();
            }
            RemoveBadZones();
            return result;
        }

        private static bool DoMagiCadAcad()
        {
            var histories = new List<RunPdms.ElementHistory>();
            var triggerUser = RunPdms.GetUserByTriggerId();
            Console.WriteLine("Начинается импорт данных");
            Command.CreateCommand("!!zoneCreateFlag = true").Run();
            var dt = Common.GetUpdatedMagiCadData(RunPdms.ProjectInfo.ProjectId);
            Console.WriteLine("Элементов для обработки: " + dt.Rows.Count + " шт.");
            var oldPercent = 0.0;
            var modifications = 0;

            if (!SetVentilationZone()) return false;

            dt.DefaultView.RowFilter = "element_type <> 'IfcCovering'";

            for (var i = 0; i < dt.DefaultView.Count; i++)
            {
                var row = dt.DefaultView[i];
                var percent = Math.Ceiling((double)i * 100 / dt.Rows.Count);
                if (oldPercent < percent)
                {
                    Console.WriteLine("Current progress {0}%   ", Math.Ceiling(percent));
                    oldPercent = percent;
                }

                var magicItem = new MagicItem();
                using (var stream = new StringReader(row["element_info"].ToString()))
                {
                    magicItem = (MagicItem)_xmlAcadFormat.Deserialize(stream);
                }

                try
                {
                    var status = row["element_status"].ToString();
                    var pdmsItem = DbElement.GetElement(row["pdms_item"].ToString());
                    var itemGuid = row["element_guid"].ToString();
                    var itemType = row["element_type"].ToString();
                    var coveringGuids = magicItem.Coverings.Aggregate(string.Empty,
                        (current, covering) =>
                            current + "'" + (string.IsNullOrEmpty(current) ? "" : ", ") + covering + "'");
                    var coveringRows = string.IsNullOrEmpty(coveringGuids)
                        ? new DataRow[0]
                        : dt.Select("element_guid in (" + coveringGuids + ")");

                    if (status == "DEL")
                    {
                        if (pdmsItem.IsNull)
                        {
                            var andFilter =
                                new AndFilter(
                                    new TypeFilter(new[]
                                    {DbElementTypeInstance.EQUIPMENT, DbElementTypeInstance.SUBEQUIPMENT}),
                                    new AttributeStringFilter(DbAttribute.GetDbAttribute(":PolyMagiGuid"),
                                        FilterOperator.Equals, itemGuid));
                            var itemCollection =
                                new DBElementCollection(_rootZone, andFilter).Cast<DbElement>().ToList();
                            if (itemCollection.Count > 0)
                                pdmsItem = itemCollection.First();
                        }

                        if (!pdmsItem.IsNull)
                        {
                            var hist = new RunPdms.ElementHistory
                            {
                                ItemRef = pdmsItem.GetAsString(DbAttributeInstance.REF),
                                ItemFlnm = pdmsItem.GetAsString(DbAttributeInstance.FLNM),
                                ItemType = pdmsItem.GetElementType().ShortName,
                                CreatedBy = row["element_created_by"].ToString(),
                                ModifiedBy = row["element_modified_by"].ToString(),
                                DeletedBy = triggerUser,
                                Created =
                                    row["element_created"] == DBNull.Value
                                        ? DateTime.MinValue
                                        : DateTime.Parse(row["element_created"].ToString()),
                                Modified =
                                    row["element_modified"] == DBNull.Value
                                        ? DateTime.MinValue
                                        : DateTime.Parse(row["element_modified"].ToString()),
                                Deleted = DateTime.Now
                            };

                            if (pdmsItem.IsDeleteable)
                            {
                                pdmsItem.Delete();
                            }

                            if (pdmsItem.IsDeleted)
                            {
                                row["element_status"] = "REM";
                                foreach (var cr in coveringRows)
                                {
                                    cr["element_status"] = "REM";
                                }
                                histories.Add(hist);
                            }
                            else
                            {
                                RunPdms.SendMessage("[Delete Magicad] Не удалось удалить элемент " +
                                                    pdmsItem.GetElementType().ShortName + " " +
                                                    pdmsItem.GetAsString(DbAttributeInstance.REF));
                            }
                        }
                        else
                        {
                            row["element_status"] = "REM";
                            foreach (var cr in coveringRows)
                            {
                                cr["element_status"] = "REM";
                            }
                        }
                    }
                    else
                    {
                        if (ModifyMagicItem(magicItem, status, ref pdmsItem, itemGuid, itemType, coveringRows))
                        {

                            row["element_status"] = "UPD";
                            histories.Add(new RunPdms.ElementHistory
                            {
                                ItemRef = pdmsItem.GetAsString(DbAttributeInstance.REF),
                                ItemFlnm = pdmsItem.GetAsString(DbAttributeInstance.FLNM),
                                ItemType = pdmsItem.GetElementType().ShortName,
                                CreatedBy = row["element_created_by"].ToString(),
                                ModifiedBy = row["element_modified_by"].ToString(),
                                DeletedBy = string.Empty,
                                Created =
                                    row["element_created"] == DBNull.Value
                                        ? DateTime.MinValue
                                        : DateTime.Parse(row["element_created"].ToString()),
                                Modified =
                                    row["element_modified"] == DBNull.Value
                                        ? DateTime.MinValue
                                        : DateTime.Parse(row["element_modified"].ToString()),
                                Deleted = DateTime.MinValue
                            });

                        }
                        else
                        {
                            Console.WriteLine("Can't modify element " + itemGuid);
                        }
                        modifications++;
                    }
                    row["pdms_item"] = pdmsItem.IsNull || pdmsItem.IsDeleted
                        ? DBNull.Value.ToString()
                        : pdmsItem.GetAsString(DbAttributeInstance.REF);
                }
                catch (Exception exc)
                {
                    RunPdms.SendMessage("[ModifySegment] " + row["element_guid"] + " " + exc.Message);
                }
            }

            dt.DefaultView.RowFilter = "element_type = 'IfcCovering'";
            var rootOfCoverings = new SortedDictionary<DbElement, string>();
            if (dt.DefaultView.Count > 0)
            {
                var sqlCommand = "USE Aveva_Poly_Db " +
                                 "select " +
                                 "  pdms_item, " +
                                 "  SUBSTRING(element_info," +
                                 "          PATINDEX('%<Coverings>%', element_info)+11," +
                                 "          PATINDEX('%</Coverings>%', element_info)-" +
                                 "          PATINDEX('%<Coverings>%', element_info)-11) as cover " +
                                 "from " +
                                 "  MagiCad " +
                                 "where " +
                                 "  project_id = " + RunPdms.ProjectInfo.ProjectId + " and " +
                                 "  element_type <> 'IfcCovering' and " +
                                 "  element_info like '%<Coverings>%'";
                using (var sqlConnection = new SqlConnection(Common.ConnectionString))
                {
                    sqlConnection.Open();
                    using (var sqlCmd = new SqlCommand(sqlCommand, sqlConnection))
                    {
                        var reader = sqlCmd.ExecuteReader();
                        while (reader.Read())
                        {
                            var item = DbElement.GetElement(reader["pdms_item"].ToString());
                            if (!item.IsNull)
                            {
                                rootOfCoverings.Add(item, reader["cover"].ToString());
                            }
                        }
                    }
                    sqlConnection.Close();
                }
            }
            for (var i = 0; i < dt.DefaultView.Count; i++)
            {
                var cr = dt.DefaultView[i];
                var coverStatus = cr["element_status"].ToString();
                if (coverStatus != "CRE" && coverStatus != "MOD" && coverStatus != "DEL") continue;
                var coverPdmsItem = DbElement.GetElement(cr["pdms_item"].ToString());
                if (coverStatus == "DEL" && DeleteElement(coverPdmsItem, cr["element_guid"].ToString()))
                {
                    cr["element_status"] = "REM";
                }
                else if (coverStatus == "CRE" || coverStatus == "MOD")
                {
                    var sube = DbElement.GetElement();
                    if (!coverPdmsItem.IsNull && coverPdmsItem.IsValid &&
                        coverPdmsItem.GetElementType().Equals(DbElementTypeInstance.SUBEQUIPMENT))
                    {
                        sube = coverPdmsItem;
                    }

                    if (sube.IsNull)
                    {
                        var andFilter =
                            new AndFilter(
                                new TypeFilter(new[] { DbElementTypeInstance.SUBEQUIPMENT }),
                                new AttributeStringFilter(DbAttribute.GetDbAttribute(":PolyMagiGuid"),
                                    FilterOperator.Equals, cr["element_guid"].ToString()));
                        var itemCollection = new DBElementCollection(_rootZone, andFilter).Cast<DbElement>().ToList();
                        if (itemCollection.Count != 0)
                        {
                            sube = itemCollection.First();
                            coverPdmsItem = sube;
                        }
                        else
                        {
                            foreach (var pair in rootOfCoverings)
                            {
                                if (pair.Value.Contains(cr["element_guid"].ToString()))
                                {
                                    sube = pair.Key.CreateLast(DbElementTypeInstance.SUBEQUIPMENT);
                                    coverPdmsItem = sube;
                                    break;
                                }
                            }
                        }
                    }

                    if (sube.IsNull || !sube.IsValid)
                    {
                        cr["element_status"] = "REM";
                        continue;
                    }

                    var coverItem = new MagicItem();
                    using (var stream = new StringReader(cr["element_info"].ToString()))
                    {
                        coverItem = (MagicItem)_xmlAcadFormat.Deserialize(stream);
                    }

                    if (!CreateGeometry(sube, coverItem, true))
                    {
                        Console.WriteLine("Can't create geometry for " + cr["element_guid"]);
                        sube.Delete();
                        continue;
                    }
                    sube.SetAttribute(DbAttribute.GetDbAttribute(":PolyMagiGuid"), cr["element_guid"].ToString());
                    sube.SetAttribute(DbAttributeInstance.FUNC, "MagiCAD");
                    cr["element_status"] = "UPD";
                }
                cr["pdms_item"] = coverPdmsItem.IsNull || coverPdmsItem.IsDeleted
                    ? DBNull.Value.ToString()
                    : coverPdmsItem.GetAsString(DbAttributeInstance.REF);
            }

            RunPdms.SendMessage("Изменений в модели: " + modifications);
            Console.WriteLine("Заполнение данных по истории элементов (" + histories.Count + " записей)");
            RunPdms.UpdateHistory(histories, Common.Source.MagiCAD);
            Console.WriteLine("История элементов обновлена");
            Common.SetUpdatedMagiCadData(dt, RunPdms.ProjectInfo.ProjectId);
            Console.WriteLine("Данные в SQL обновлены");
            return true;
        }

        private static bool DoMagiCadRevit()
        {
            var histories = new List<RunPdms.ElementHistory>();
            var triggerUser = RunPdms.GetUserByTriggerId();
            Console.WriteLine("Начинается импорт данных");
            Command.CreateCommand("!!zoneCreateFlag = true").Run();
            var dt = Common.GetUpdatedMagiCadData(RunPdms.ProjectInfo.ProjectId);
            Console.WriteLine("Элементов для обработки: " + dt.Rows.Count + " шт.");
            var oldPercent = 0.0;
            var modifications = 0;

            if (!SetVentilationZone()) return false; 

            for (var i = 0; i < dt.DefaultView.Count; i++)
            {
                var row = dt.DefaultView[i];
                var percent = Math.Ceiling((double)i * 100 / dt.Rows.Count);
                if (oldPercent < percent)
                {
                    Console.WriteLine("Current progress {0}%   ", Math.Ceiling(percent));
                    oldPercent = percent;
                }

                var magicItem = new IfcElementInformation();
                using (var stream = new StringReader(row["element_info"].ToString()))
                {
                    magicItem = (IfcElementInformation)_xmlRevitFormat.Deserialize(stream);
                }

                try
                {
                    var status = row["element_status"].ToString();
                    var pdmsItem = DbElement.GetElement(row["pdms_item"].ToString());
                    var itemGuid = row["element_guid"].ToString();
                    var itemType = row["element_type"].ToString();                 
                    
                    if (status == "DEL")
                    {
                        if (pdmsItem.IsNull)
                        {
                            var typeFilter = new TypeFilter(DbElementTypeInstance.EQUIPMENT);
                            var attributeStringFilter = new AttributeStringFilter(DbAttribute.GetDbAttribute(":PolyMagiGuid"), FilterOperator.Equals, itemGuid);
                            var andFilter = new AndFilter(typeFilter, attributeStringFilter);
                            var itemCollection = new DBElementCollection(_rootZone, andFilter).Cast<DbElement>().ToList();
                            if (itemCollection.Count > 0)
                                pdmsItem = itemCollection.First();
                        }

                        if (!pdmsItem.IsNull)
                        {
                            var hist = new RunPdms.ElementHistory
                            {
                                ItemRef = pdmsItem.GetAsString(DbAttributeInstance.REF),
                                ItemFlnm = pdmsItem.GetAsString(DbAttributeInstance.FLNM),
                                ItemType = pdmsItem.GetElementType().ShortName,
                                CreatedBy = row["element_created_by"].ToString(),
                                ModifiedBy = row["element_modified_by"].ToString(),
                                DeletedBy = triggerUser,
                                Created =
                                    row["element_created"] == DBNull.Value
                                        ? DateTime.MinValue
                                        : DateTime.Parse(row["element_created"].ToString()),
                                Modified =
                                    row["element_modified"] == DBNull.Value
                                        ? DateTime.MinValue
                                        : DateTime.Parse(row["element_modified"].ToString()),
                                Deleted = DateTime.Now
                            };

                            if (pdmsItem.IsDeleteable)
                            {
                                pdmsItem.Delete();
                            }

                            if (pdmsItem.IsDeleted)
                            {
                                row["element_status"] = "REM";                                
                                histories.Add(hist);
                            }
                            else
                            {
                                RunPdms.SendMessage(
                                    string.Format("[Delete Magicad] Не удалось удалить элемент {0} {1}", 
                                    pdmsItem.GetElementType().ShortName, 
                                    pdmsItem.GetAsString(DbAttributeInstance.REF)));
                            }
                        }
                        else
                        {
                            row["element_status"] = "REM";                            
                        }
                    }
                    else
                    {
                        if (ModifyMagiCadRevitItem(magicItem, status, ref pdmsItem, itemGuid, itemType))
                        {

                            row["element_status"] = "UPD";
                            histories.Add(new RunPdms.ElementHistory
                            {
                                ItemRef = pdmsItem.GetAsString(DbAttributeInstance.REF),
                                ItemFlnm = pdmsItem.GetAsString(DbAttributeInstance.FLNM),
                                ItemType = pdmsItem.GetElementType().ShortName,
                                CreatedBy = row["element_created_by"].ToString(),
                                ModifiedBy = row["element_modified_by"].ToString(),
                                DeletedBy = string.Empty,
                                Created =
                                    row["element_created"] == DBNull.Value
                                        ? DateTime.MinValue
                                        : DateTime.Parse(row["element_created"].ToString()),
                                Modified =
                                    row["element_modified"] == DBNull.Value
                                        ? DateTime.MinValue
                                        : DateTime.Parse(row["element_modified"].ToString()),
                                Deleted = DateTime.MinValue
                            });

                        }
                        else
                        {
                            Console.WriteLine("Can't modify element " + itemGuid);
                        }
                        modifications++;
                    }
                    row["pdms_item"] = pdmsItem.IsNull || pdmsItem.IsDeleted
                        ? DBNull.Value.ToString()
                        : pdmsItem.GetAsString(DbAttributeInstance.REF);
                }
                catch (Exception exc)
                {
                    RunPdms.SendMessage("[ModifySegment] " + row["element_guid"] + " " + exc.Message);
                }
            }

            RunPdms.SendMessage("Изменений в модели: " + modifications);
            Console.WriteLine("Заполнение данных по истории элементов (" + histories.Count + " записей)");
            RunPdms.UpdateHistory(histories, Common.Source.MagiCAD);
            Console.WriteLine("История элементов обновлена");
            Common.SetUpdatedMagiCadData(dt, RunPdms.ProjectInfo.ProjectId);
            Console.WriteLine("Данные в SQL обновлены");
            return true;
        }

        private static void RemoveBadZones()
        {
            if (_rootZone.IsNull || !_rootZone.IsValid) return;
            while (true)
            {
                var flag = false;
                var collection = new DBElementCollection(_rootZone, new TypeFilter(DbElementTypeInstance.ZONE)).Cast<DbElement>().ToList();
                foreach (var item in collection)
                {
                    if (item.Members().Length > 0) continue;
                    var name = item.GetAsString(DbAttributeInstance.FLNM);
                    if (name.EndsWith("_X") || name.EndsWith("_XX") || name.Contains("_XX_"))
                    {
                        item.Delete();
                        flag = true;
                        break;
                    }                    
                }
                if (!flag) break;
            }
        }

        /// <summary>
        /// Удаление всех элементов относящихся к удаленному элементу Tekla
        /// </summary>
        /// <param name="sqlXml">входной SqlXml</param>
        /// <returns>true - если все элементы были удалены без ошибок</returns>
        private static bool DeleteElement(DbElement item, string guid)
        {
            if (item.IsNull)
            {
                var andFilter =
                new AndFilter(
                    new TypeFilter(new[] { DbElementTypeInstance.EQUIPMENT, DbElementTypeInstance.SUBEQUIPMENT }),
                    new AttributeStringFilter(DbAttribute.GetDbAttribute(":PolyMagiGuid"), FilterOperator.Equals, guid));
                var itemCollection = new DBElementCollection(_rootZone, andFilter).Cast<DbElement>().ToList();
                foreach (var i in itemCollection)
                {
                    if (i.IsDeleteable)
                    {
                        i.Delete();
                    }
                    if (i.IsDeleted) continue;
                    RunPdms.SendMessage("[Delete Magicad] Не удалось удалить элемент " + i.GetElementType().ShortName + " " +
                                        i.GetAsString(DbAttributeInstance.REF));
                }
                return true;
            }

            if (item.IsDeleteable)
            {
                item.Delete();
            }

            if (item.IsDeleted) return true;
            RunPdms.SendMessage("[Delete Magicad] Не удалось удалить элемент " + item.GetElementType().ShortName + " " +
                                item.GetAsString(DbAttributeInstance.REF));
            return false;
        }

        /// <summary>
        /// Перемещение элемента в иерархии, если его система изменилась
        /// </summary>
        /// <param name="item">элемент</param>
        /// <param name="root">новый владелец</param>
        /// <returns></returns>
        private static bool MoveItemToNewSystem(DbElement item, DbElement root)
        {
            if (!item.IsInsertable(root)) return false;
            item.InsertAfterLast(root);
            return true;
        }

        private static bool ModifyMagicItem(MagicItem item, string status, ref DbElement pdmsItem, string id, string type, DataRow[] coveringRows)
        {
            if (status != "CRE" && status != "MOD") return false;
            
            var equi = DbElement.GetElement();
            if (!pdmsItem.IsNull && pdmsItem.IsValid &&
                pdmsItem.GetElementType().Equals(DbElementTypeInstance.EQUIPMENT))
            {
                equi = pdmsItem;
            }
            var typeZone = CreateStructureElement(item);
            if (!equi.IsNull && !equi.Owner.Equals(typeZone) && !MoveItemToNewSystem(equi, typeZone))
            {
                RunPdms.SendMessage("Не удалось переместить элемент " + equi.GetElementType().ShortName + " " + equi.GetAsString(DbAttributeInstance.REF));
                return false;
            }

            if (equi.IsNull)
            {
                equi = typeZone.CreateLast(DbElementTypeInstance.EQUIPMENT);
                pdmsItem = equi;
            }

            /*var siteOrientation = _sanoSite.GetOrientation(DbAttributeInstance.ORI);
            var sitePos = _sanoSite.GetPosition(DbAttributeInstance.POS);
            var siteCS = siteOrientation.ConvertToCoordinateSystem(sitePos);
            var siteMatrix = MatrixFactory.ToCoordinateSystem(siteCS);*/

            /*var equiPos = siteMatrix.Transform(new Point(item.Matrix[12], item.Matrix[13], item.Matrix[14])).ConvertToPosition();
            var gVectorX = new Vector(siteMatrix.Transform(new Point(item.Matrix[0], item.Matrix[1], item.Matrix[2])));
            var gVectorY = new Vector(siteMatrix.Transform(new Point(item.Matrix[4], item.Matrix[5], item.Matrix[6])));
            var gOriMagic = gVectorX.CreateOrientation(gVectorY);
            var gOriPdms = Orientation.Create(gOriMagic.ToString());*/
            var equiPos = Position.Create(item.Matrix[12],item.Matrix[13], item.Matrix[14]);
            var gVectorX = new Vector(item.Matrix[0], item.Matrix[1], item.Matrix[2]);
            var gVectorY = new Vector(item.Matrix[4], item.Matrix[5], item.Matrix[6]);
            var gOriPdms = Orientation.Create(gVectorX.CreateOrientation(gVectorY).ToString());
            
            equi.SetAttribute(DbAttribute.GetDbAttribute(":PolyMagiGuid"), id);
            equi.SetAttribute(DbAttributeInstance.FUNC, "MagiCAD");
            equi.SetAttribute(DbAttributeInstance.POS, equiPos);
            equi.SetAttribute(DbAttributeInstance.ORI, gOriPdms);
            SetDescription(equi, item, type);

            if (!CreateGeometry(equi, item)) return false;

            foreach (var cr in coveringRows)
            {
                var coverStatus = cr["element_status"].ToString();
                var coverPdmsItem = DbElement.GetElement(cr["pdms_item"].ToString());
                if (coverStatus == "DEL" && DeleteElement(coverPdmsItem, cr["element_guid"].ToString()))
                {
                    cr["element_status"] = "REM";
                }
                else if (coverStatus == "CRE" || coverStatus == "MOD")
                {
                    var sube = DbElement.GetElement();
                    if (!coverPdmsItem.IsNull && coverPdmsItem.IsValid &&
                        coverPdmsItem.GetElementType().Equals(DbElementTypeInstance.SUBEQUIPMENT))
                    {
                        sube = coverPdmsItem;
                    }

                    if (sube.IsNull)
                    {
                        sube = equi.CreateLast(DbElementTypeInstance.SUBEQUIPMENT);
                        coverPdmsItem = sube;
                    }

                    var coverItem = new MagicItem();
                    using (var stream = new StringReader(cr["element_info"].ToString()))
                    {
                        coverItem = (MagicItem)_xmlAcadFormat.Deserialize(stream);
                    }

                    if (!CreateGeometry(sube, coverItem, true)) return false;
                    sube.SetAttribute(DbAttribute.GetDbAttribute(":PolyMagiGuid"), cr["element_guid"].ToString());
                    sube.SetAttribute(DbAttributeInstance.FUNC, "MagiCAD");
                    cr["element_status"] = "UPD";
                }
                cr["pdms_item"] = coverPdmsItem.IsNull || coverPdmsItem.IsDeleted
                        ? DBNull.Value.ToString()
                        : coverPdmsItem.GetAsString(DbAttributeInstance.REF);
            }

            return true;
        }

        private static bool ModifyMagiCadRevitItem(IfcElementInformation item, string status, ref DbElement pdmsItem, string id, string type)
        {
            if (status != "CRE" && status != "MOD") return false;

            var equi = DbElement.GetElement();
            if (!pdmsItem.IsNull && pdmsItem.IsValid &&
                pdmsItem.GetElementType().Equals(DbElementTypeInstance.EQUIPMENT))
            {
                equi = pdmsItem;
            }
            var typeZone = CreateStructureElement(item);
            if (!equi.IsNull && !equi.Owner.Equals(typeZone) && !MoveItemToNewSystem(equi, typeZone))
            {
                RunPdms.SendMessage("Не удалось переместить элемент " + equi.GetElementType().ShortName + " " + equi.GetAsString(DbAttributeInstance.REF));
                return false;
            }

            if (equi.IsNull)
            {
                equi = typeZone.CreateLast(DbElementTypeInstance.EQUIPMENT);
                RevitImport.SetPdmsAttribute(equi, RevitImport._guidMagicAtt, id);
                RevitImport.SetPdmsAttribute(equi, DbAttributeInstance.FUNC, "MagiCAD");
                pdmsItem = equi;
            }

            foreach (var prop in item.Properties)
            {
                if (prop.StartsWith("System#")) continue;
                var propData = prop.Split('#');
                var att = DbAttribute.GetDbAttribute(propData[0]);
                if (att == null) continue;
                RevitImport.SetPdmsAttribute(equi, att, propData[1]);
            }

            var mainPosition = Position.Create();
            var mainOrientation = Orientation.Create();
            var positive = item.Representation.Positive;
            if (positive.Data == null) return false;
            var data = RevitImport.GeometryDataFromString(positive.Data, positive.Type);
            if (positive.Type == IfcElementInformation.GeometryType.Mesh)
            {
                var bytes = (byte[])data;
                var point = RevitImport.GetPositionFromBytes(bytes);
                var tp = RevitImport.TransformPoint(pdmsItem, point);
                mainPosition = Position.Create(tp.X, tp.Y, tp.Z);
                mainOrientation = Orientation.Create("Z is U");
            }
            else
            {
                return false;
            }
            RevitImport.SetPdmsAttribute(equi, DbAttributeInstance.POS, mainPosition);
            RevitImport.SetPdmsAttribute(equi, DbAttributeInstance.ORI, mainOrientation);

            var mainGenpri = DbElement.GetElement();
            var children = new List<DbElement>();
            foreach (var genpri in equi.Members(DbElementTypeInstance.GENPRI))
            {
                var levels = genpri.GetIntegerArray(DbAttributeInstance.LEVE);
                if (levels[0] > 8)
                {
                    if (children.Count < item.Children.Count)
                        children.Add(genpri);
                    else
                        genpri.Delete();
                }
                else
                {
                    if (mainGenpri.IsNull)
                        mainGenpri = genpri;
                    else
                        genpri.Delete();
                }
            }

            if (mainGenpri.IsNull)
                mainGenpri = equi.CreateFirst(DbElementTypeInstance.GENPRI);

            for (var i = 0; i < item.Children.Count; i++)
            {
                if (i < children.Count) continue;

                var genpri = equi.CreateLast(DbElementTypeInstance.GENPRI);
                RevitImport.SetPdmsAttribute(genpri, DbAttributeInstance.LEVE, new[] { 9, 10 });
                children.Add(genpri);
            }

            var mainEldataList = RevitImport.GetDataFromBytes((byte[])data);
            var mainArchiv = mainGenpri.Members(DbElementTypeInstance.ARCHIV).Length > 0 ?
                mainGenpri.Members(DbElementTypeInstance.ARCHIV).First() :
                mainGenpri.CreateFirst(DbElementTypeInstance.ARCHIV);
            var mainAttdatList = mainArchiv.Members(DbElementTypeInstance.ATTDAT).ToList();
            if (mainEldataList.Count < mainAttdatList.Count)
            {
                var diff = mainAttdatList.Count - mainEldataList.Count;
                for (var i = 0; i < diff; i++)
                {
                    var attdat = mainAttdatList.Last();
                    attdat.Delete();
                    mainAttdatList.Remove(attdat);
                }
            }

            for (var i = 0; i < mainEldataList.Count; i++)
            {
                var eldata = mainEldataList[i];
                var attdat = mainAttdatList.Count < i + 1
                    ? mainArchiv.CreateLast(DbElementTypeInstance.ATTDAT)
                    : mainAttdatList[i];
                attdat.SetAttribute(DbAttributeInstance.ELDATA, eldata);
                attdat.SetAttribute(DbAttributeInstance.QALLSI, eldata.Length);
            }

            for (var i = 0; i < item.Children.Count; i++)
            {
                var childGenpri = children[i];
                var childInfo = item.Children[i];

                var childPosition = Position.Create();
                var childOrientation = Orientation.Create();

                if (childInfo.Representation.Positive.Data == null) return false;
                var childData = RevitImport.GeometryDataFromString(childInfo.Representation.Positive.Data, childInfo.Representation.Positive.Type);

                var point = RevitImport.GetPositionFromBytes((byte[])childData);
                var tp = RevitImport.TransformPoint(childGenpri, point);
                childPosition = Position.Create(tp.X, tp.Y, tp.Z);
                childOrientation = Orientation.Create("Z is U");

                RevitImport.SetPdmsAttribute(childGenpri, DbAttributeInstance.POS, childPosition);
                RevitImport.SetPdmsAttribute(childGenpri, DbAttributeInstance.ORI, childOrientation);
                RevitImport.SetPdmsAttribute(childGenpri, DbAttributeInstance.LEVE, new[] { 9, 10 });

                var childEldataList = RevitImport.GetDataFromBytes((byte[])childData);
                var childArchiv = childGenpri.Members(DbElementTypeInstance.ARCHIV).Length > 0 ?
                    childGenpri.Members(DbElementTypeInstance.ARCHIV).First() :
                    childGenpri.CreateFirst(DbElementTypeInstance.ARCHIV);
                var childAttdatList = childArchiv.Members(DbElementTypeInstance.ATTDAT).ToList();
                if (childEldataList.Count < childAttdatList.Count)
                {
                    var diff = childAttdatList.Count - childEldataList.Count;
                    for (var j = 0; j < diff; j++)
                    {
                        var attdat = childAttdatList.Last();
                        attdat.Delete();
                        childAttdatList.Remove(attdat);
                    }
                }

                for (var j = 0; j < childEldataList.Count; j++)
                {
                    var eldata = childEldataList[j];
                    var attdat = childAttdatList.Count < j + 1
                        ? childArchiv.CreateLast(DbElementTypeInstance.ATTDAT)
                        : childAttdatList[j];
                    attdat.SetAttribute(DbAttributeInstance.ELDATA, eldata);
                    attdat.SetAttribute(DbAttributeInstance.QALLSI, eldata.Length);
                }
            }

            return true;
        }

        /*private static string GetXmlPath()
        {
            var result = string.Empty;
            using (var sqlConnection = new SqlConnection(Settings.Default.ConnectionString))
            {
                sqlConnection.Open();
                var sqlCommand =
                    string.Format(
                        "USE Aveva_Poly_Db SELECT MagiCadIfcName, SubObjectID, Offset FROM MagiCadProject WHERE MagiCadProjectID = {0}",
                        RunPdms.ProjectInfo.ProjectId);
                using (var sqlCmd = new SqlCommand(sqlCommand, sqlConnection))
                {
                    var reader = sqlCmd.ExecuteReader();
                    if (reader.Read())
                    {
                        result = Path.Combine(_magiCadPath, reader["MagiCadIfcName"].ToString().Replace(".ifc", ".xml"));
                        if (reader["Offset"] is DBNull || reader["Offset"].ToString().Split('#').Length < 3)
                        {
                            _offsetPosition = new[] {0.0, 0.0, 0.0};
                        }
                        else
                        {
                            double x, y, z;
                            var offset = reader["Offset"].ToString().Split('#');
                            if (double.TryParse(offset[0], out x) && double.TryParse(offset[1], out y) &&
                                double.TryParse(offset[2], out z))
                            {
                                _offsetPosition = new[] {x, y, z};
                            }
                        }
                    }
                }
                sqlConnection.Close();
            }
            return result;
        }*/

        /// <summary>
        /// Поиск или создание зоны ОВ
        /// </summary>
        /// <returns>true - если зона найдена/создана</returns>
        private static bool SetVentilationZone()
        {
            var siteName = string.Format("/{0}-SANO", MDB.CurrentMDB.Name.Replace("-", string.Empty));
            _sanoSite = DbElement.GetElement(siteName);
            if (_sanoSite.IsNull || !_sanoSite.IsValid) return false;

            var documentCode = string.Empty;
            var path = RunPdms.ProjectInfo.Program.Path.Split(';').First();
            var dirInfo = new DirectoryInfo(Path.GetDirectoryName(path));
            if (dirInfo.Exists)
            {                
                var data = dirInfo.Name.Split('-');
                if (data.Length <= 1)
                    data = dirInfo.Parent.Name.Split('-');
                if (data.Length > 1)
                {
                    var code = data[0];
                    data = code.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (data.Length > 3)
                    {
                        documentCode = data[1];
                        int intValue;
                        if (!int.TryParse(documentCode, out intValue))
                            documentCode = string.Empty;
                    }
                }
            }

            var zoneName = string.Format("{0}-OV-{1}", siteName, string.IsNullOrEmpty(documentCode) ? "07" : documentCode);
            _rootZone = DbElement.GetElement(zoneName);
            if (!_rootZone.IsNull) return true;            
      
            _rootZone = _sanoSite.CreateLast(DbElementTypeInstance.ZONE);
            _rootZone.SetAttribute(DbAttributeInstance.NAME, zoneName);
            _rootZone.SetAttribute(DbAttributeInstance.DESC, "Отопление, вентиляция и кондиционирование");
            return true;
        }

        /// <summary>
        /// Создание структуры системы
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        private static DbElement CreateStructureElement(MagicItem item)
        {
            var rootZoneName = _rootZone.GetAsString(DbAttributeInstance.FLNM);

            item.SystemInfo = item.SystemInfo ?? new MagicSystem();

            var systemName = string.IsNullOrEmpty(item.SystemInfo.SystemCode) ? "X" : item.SystemInfo.SystemCode.Replace(",",string.Empty).Replace(" ",string.Empty);
            var systemZone = DbElement.GetElement(rootZoneName + "_" + systemName);
            if (systemZone.IsNull)
            {
                systemZone = _rootZone.CreateLast(DbElementTypeInstance.ZONE);
                systemZone.SetAttribute(DbAttributeInstance.NAME, rootZoneName + "_" + systemName);
                systemZone.SetAttribute(DbAttributeInstance.DESC, GetSystemDescription(systemName, "PipeSystem"));
            }

            var subSystemName = string.IsNullOrEmpty(item.SystemInfo.SubSystemCode) ? "X" : item.SystemInfo.SubSystemCode.Replace(",",string.Empty).Replace(" ",string.Empty);
            var subSystemIndex = string.IsNullOrEmpty(item.SystemInfo.SubSystemIndex) ? "X" : item.SystemInfo.SubSystemIndex.Replace(",", string.Empty).Replace(" ", string.Empty);

            var subSystemZone =
                DbElement.GetElement(systemZone.GetAsString(DbAttributeInstance.FLNM) + "_" + subSystemName +
                                     subSystemIndex);
            if (subSystemZone.IsNull)
            {
                subSystemZone = systemZone.CreateLast(DbElementTypeInstance.ZONE);
                subSystemZone.SetAttribute(DbAttributeInstance.NAME,
                    systemZone.GetAsString(DbAttributeInstance.FLNM) + "_" + subSystemName + subSystemIndex);
                subSystemZone.SetAttribute(DbAttributeInstance.DESC,
                    GetSystemDescription(subSystemName, "PipeSubSystem"));
            }

            var type = string.IsNullOrEmpty(item.SystemInfo.SystemType) ? "X" : item.SystemInfo.SystemType.Replace(",", string.Empty).Replace(" ", string.Empty);
            var typeZone = DbElement.GetElement(subSystemZone.GetAsString(DbAttributeInstance.FLNM) + "_" + type);
            if (!typeZone.IsNull) return typeZone;

            typeZone = subSystemZone.CreateLast(DbElementTypeInstance.ZONE);
            typeZone.SetAttribute(DbAttributeInstance.NAME,
                subSystemZone.GetAsString(DbAttributeInstance.FLNM) + "_" + type);

            var description = item.SystemInfo.SystemDescription;
            if (string.IsNullOrEmpty(description))
            {
                if (type.Equals("PIPE"))
                    description = "Трубопроводы";
                else if (type.Equals("DUCT"))
                    description = "Воздуховоды";
            }

            if (!string.IsNullOrEmpty(description))
                typeZone.SetAttribute(DbAttributeInstance.DESC, description);

            return typeZone;
        }

        private static DbElement CreateStructureElement(IfcElementInformation item)
        {            
            var systemName = string.Empty;
            var subSystemName = string.Empty;
            var subSystemIndex = string.Empty;
            foreach (var prop in item.Properties)
            {
                if (!prop.StartsWith("System#")) continue;
                var data = prop.Split('#');
                if (data.Length>1)
                    systemName = data[1].Trim();
                if (data.Length > 2)
                    subSystemName = data[2];
            }

            var digIndex = 0;
            for (var i = 0; i < subSystemName.Length; i++)
            {
                var ch = subSystemName[i];
                if (ch <= 47 || ch >= 58) continue;
                digIndex = i;
                break;
            }

            if (digIndex == 0)
            {
                subSystemIndex = "X";
            }
            else
            {
                subSystemIndex = subSystemName.Substring(digIndex);
                int intIndex;
                if (!int.TryParse(subSystemIndex, out intIndex))
                    subSystemIndex = "X";
                subSystemName = subSystemName.Substring(0, digIndex);
            }

            var systemDesc = GetSystemDescription(systemName, "PipeSystem");
            if (string.IsNullOrEmpty(systemDesc))
                systemName = "X";
            var subSystemDesc = GetSystemDescription(subSystemName, "PipeSubSystem");
            if (string.IsNullOrEmpty(subSystemDesc))
            {
                subSystemName = "X";
                subSystemIndex = "X";
            }
            
            var rootZoneName = _rootZone.GetAsString(DbAttributeInstance.FLNM);            
            var systemZone = DbElement.GetElement(rootZoneName + "_" + systemName);
            if (systemZone.IsNull)
            {
                systemZone = _rootZone.CreateLast(DbElementTypeInstance.ZONE);
                systemZone.SetAttribute(DbAttributeInstance.NAME, rootZoneName + "_" + systemName);
                systemZone.SetAttribute(DbAttributeInstance.DESC, systemDesc);
            }

            var subSystemZone = DbElement.GetElement(systemZone.GetAsString(DbAttributeInstance.FLNM) + "_" + subSystemName + subSystemIndex);
            if (subSystemZone.IsNull)
            {
                subSystemZone = systemZone.CreateLast(DbElementTypeInstance.ZONE);
                subSystemZone.SetAttribute(DbAttributeInstance.NAME,
                    systemZone.GetAsString(DbAttributeInstance.FLNM) + "_" + subSystemName + subSystemIndex);
                subSystemZone.SetAttribute(DbAttributeInstance.DESC, subSystemDesc);
            }

            var type = "DUCT";
            var typeDesc = GetSystemDescription(type, "StructClassInfo");
            var typeZone = DbElement.GetElement(subSystemZone.GetAsString(DbAttributeInstance.FLNM) + "_" + type);
            if (typeZone.IsNull)
            {
                typeZone = subSystemZone.CreateLast(DbElementTypeInstance.ZONE);
                typeZone.SetAttribute(DbAttributeInstance.NAME, subSystemZone.GetAsString(DbAttributeInstance.FLNM) + "_" + type);
                typeZone.SetAttribute(DbAttributeInstance.DESC, typeDesc);
            }
            
            return typeZone;
        }

        private static string GetSystemDescription(string systemName, string tableName)
        {
            var description = string.Empty;
            using (var sqlConnection = new SqlConnection(Common.ConnectionString))
            {
                sqlConnection.Open();
                var sqlCommand = string.Format("use Aveva_Poly_Db select RTRIM({0}Desc) from {0} where {0}Code = '{1}'",
                    tableName, systemName);
                using (var sqlCmd = new SqlCommand(sqlCommand, sqlConnection))
                {
                    var result = sqlCmd.ExecuteScalar();
                    if (result != null) description = result.ToString();
                }
                sqlConnection.Close();
            }

            return description;
        }

        /// <summary>
        /// Установка описания
        /// </summary>
        /// <param name="root">элемент PDMS</param>
        /// <param name="item">элемент из MagiCAD</param>
        private static void SetDescription(DbElement root, MagicItem item, string type)
        {
            var properties = item.Properties.ToDictionary(p => p.Split('#').First(), p => p.Split('#').Last());

            var fittingType = item.Description == null ? string.Empty : item.Description.Trim();
            var description = string.Empty;
            if (type.Equals("IfcFlowController"))
            {
                #region Задвижки, клапаны

                if (properties.ContainsKey(MagicAttributes.PdmsComponentType))
                {
                    description = properties[MagicAttributes.PdmsComponentType];
                }

                if (properties.ContainsKey(MagicAttributes.PdmsComponent6))
                {
                    var value = properties[MagicAttributes.PdmsComponent6];
                    if (properties.ContainsKey(MagicAttributes.PdmsSize8))
                    {
                        value = value.Substring(0, value.IndexOf("\"")).Trim() + " \"" +
                                properties[MagicAttributes.PdmsSize8] + "\"";
                    }
                    root.SetAttribute(DbAttribute.GetDbAttribute(":EquiType"), value.Replace("�", "?"));
                }
                else if (properties.ContainsKey(MagicAttributes.PdmsComponent7))
                {
                    var value = properties[MagicAttributes.PdmsComponent7];
                    if (properties.ContainsKey(MagicAttributes.PdmsSize8) &&
                        !string.IsNullOrEmpty(properties[MagicAttributes.PdmsSize8]) && value.IndexOf("\"")>=0)
                    {
                        value = value.Substring(0, value.IndexOf("\"")).Trim() + " \"" +
                                properties[MagicAttributes.PdmsSize8] + "\"";
                    }
                    root.SetAttribute(DbAttribute.GetDbAttribute(":EquiType"), value.Replace("�","?"));
                }

                #endregion
            }
            else if (type.Equals("IfcEnergyConversionDevice"))
            {
                #region Нагреватели

                if (properties.ContainsKey(MagicAttributes.PdmsComponentType))
                {
                    description = properties[MagicAttributes.PdmsComponentType];
                }

                if (properties.ContainsKey(MagicAttributes.PdmsComponent9))
                {
                    var value = properties[MagicAttributes.PdmsComponent9];
                    if (properties.ContainsKey(MagicAttributes.PdmsConnectionSize3))
                    {
                        value += " " + properties[MagicAttributes.PdmsConnectionSize3];
                    }
                    root.SetAttribute(DbAttribute.GetDbAttribute(":EquiType"), value.Replace("�", "?"));
                }

                #endregion
            }
            else if (type.Equals("IfcFlowFitting"))
            {
                #region Отводы, переходы, врезки, заглушки, коробки

                if (fittingType.Equals("заглушка", StringComparison.OrdinalIgnoreCase))
                {
                    #region заглушка

                    if (properties.ContainsKey(MagicAttributes.PdmsComponentType))
                    {
                        description = properties[MagicAttributes.PdmsComponentType] + " ";

                        if (properties.ContainsKey(MagicAttributes.PdmsMaterial))
                        {
                            description += "из " + MaterialStringModification(properties[MagicAttributes.PdmsMaterial]) +
                                           ", ";
                        }

                        if (properties.ContainsKey(MagicAttributes.PdmsNationalCode) &&
                            properties[MagicAttributes.PdmsNationalCode].Contains("ГОСТ"))
                        {
                            description += "по " + properties[MagicAttributes.PdmsNationalCode].Trim() + ", ";
                        }
                        else if (properties.ContainsKey(MagicAttributes.PdmsDuctType) &&
                                 properties[MagicAttributes.PdmsDuctType].Contains("ГОСТ"))
                        {
                            var gost = properties[MagicAttributes.PdmsDuctType].Trim();
                            gost = gost.Substring(gost.IndexOf("\"")).Replace("\"", string.Empty);
                            description += "по " + gost + ", ";
                        }
                        else if (properties.ContainsKey(MagicAttributes.PdmsPipeType) &&
                                 properties[MagicAttributes.PdmsPipeType].Contains("ГОСТ"))
                        {
                            var gost = properties[MagicAttributes.PdmsPipeType].Trim();
                            gost = gost.Substring(gost.IndexOf("\"")).Replace("\"", string.Empty);
                            description += "по " + gost + ", ";
                        }
                        if (item.Faces.Count == 0 && item.Solid.Thickness > 0)
                        {
                            description += "~dlt=" + Math.Round(item.Solid.Thickness, 1) + "мм, ";
                        }

                        if (properties.ContainsKey(MagicAttributes.PdmsConnectionSize4) ||
                            properties.ContainsKey(MagicAttributes.PdmsConnectionSize8))
                        {
                            var sizeValue = string.Empty;
                            if (properties.ContainsKey(MagicAttributes.PdmsConnectionSize4))
                            {
                                sizeValue = properties[MagicAttributes.PdmsConnectionSize4];
                            }
                            else if (properties.ContainsKey(MagicAttributes.PdmsConnectionSize8))
                            {
                                sizeValue = properties[MagicAttributes.PdmsConnectionSize8];
                            }

                            var size = sizeValue.Split('x');
                            if (size.Length == 1)
                            {
                                description += "~D" + size.First();
                            }
                            else if (size.Length == 2)
                            {
                                double a, b;
                                if (double.TryParse(size.First(), out a) && double.TryParse(size.Last(), out b))
                                {
                                    description += Math.Min(a, b) + "x" + Math.Max(a, b);
                                }
                                else
                                {
                                    description += sizeValue;
                                }
                            }
                            else
                            {
                                description += sizeValue;
                            }
                        }
                    }

                    #endregion
                }
                else if (fittingType.Equals("коробка", StringComparison.OrdinalIgnoreCase))
                {
                    #region коробка

                    if (properties.ContainsKey(MagicAttributes.PdmsComponentType))
                    {
                        description = properties[MagicAttributes.PdmsComponentType] + " ";

                        if (properties.ContainsKey(MagicAttributes.PdmsMaterial))
                        {
                            description += "из " + MaterialStringModification(properties[MagicAttributes.PdmsMaterial]) +
                                           ", ";
                        }

                        if (properties.ContainsKey(MagicAttributes.PdmsNationalCode) &&
                            properties[MagicAttributes.PdmsNationalCode].Contains("ГОСТ"))
                        {
                            description += "по " + properties[MagicAttributes.PdmsNationalCode].Trim() + ", ";
                        }
                        else if (properties.ContainsKey(MagicAttributes.PdmsDuctType) &&
                                 properties[MagicAttributes.PdmsDuctType].Contains("ГОСТ"))
                        {
                            var gost = properties[MagicAttributes.PdmsDuctType].Trim();
                            gost = gost.Substring(gost.IndexOf("\"")).Replace("\"", string.Empty);
                            description += "по " + gost + ", ";
                        }

                        if (properties.ContainsKey(MagicAttributes.PdmsBoxHeight) &&
                            properties.ContainsKey(MagicAttributes.PdmsBoxWidth) &&
                            properties.ContainsKey(MagicAttributes.PdmsBoxLength))
                        {
                            var size = string.Format("{0}x{1}x{2}", properties[MagicAttributes.PdmsBoxHeight],
                                properties[MagicAttributes.PdmsBoxWidth], properties[MagicAttributes.PdmsBoxLength]);
                            description += size;
                        }
                    }

                    #endregion
                }
                else if (fittingType.Equals("отвод", StringComparison.OrdinalIgnoreCase))
                {
                    #region отвод

                    if (properties.ContainsKey(MagicAttributes.PdmsComponentType))
                    {
                        description = properties[MagicAttributes.PdmsComponentType] + " ";

                        if (properties.ContainsKey(MagicAttributes.PdmsMaterial))
                        {
                            description += "из " + MaterialStringModification(properties[MagicAttributes.PdmsMaterial]) +
                                           ", ";
                        }

                        if (properties.ContainsKey(MagicAttributes.PdmsNationalCode) &&
                            properties[MagicAttributes.PdmsNationalCode].Contains("ГОСТ"))
                        {
                            description += "по " + properties[MagicAttributes.PdmsNationalCode].Trim() + ", ";
                        }
                        else if (properties.ContainsKey(MagicAttributes.PdmsDuctType) &&
                                 properties[MagicAttributes.PdmsDuctType].Contains("ГОСТ"))
                        {
                            var gost = properties[MagicAttributes.PdmsDuctType].Trim();
                            gost = gost.Substring(gost.IndexOf("\"")).Replace("\"", string.Empty);
                            description += "по " + gost + ", ";
                        }
                        else if (properties.ContainsKey(MagicAttributes.PdmsPipeType) &&
                                 properties[MagicAttributes.PdmsPipeType].Contains("ГОСТ"))
                        {
                            var gost = properties[MagicAttributes.PdmsPipeType].Trim();
                            gost = gost.Substring(gost.IndexOf("\"")).Replace("\"", string.Empty);
                            description += "по " + gost + ", ";
                        }

                        if (item.Faces.Count == 0 && item.Solid.Thickness > 0)
                        {
                            description += "~dlt=" + Math.Round(item.Solid.Thickness, 1) + "мм, ";
                        }

                        if (properties.ContainsKey(MagicAttributes.PdmsConnectionSize1) ||
                            properties.ContainsKey(MagicAttributes.PdmsConnectionSize8))
                        {
                            var sizeValue = string.Empty;
                            if (properties.ContainsKey(MagicAttributes.PdmsConnectionSize1))
                            {
                                sizeValue = properties[MagicAttributes.PdmsConnectionSize1];
                            }
                            else if (properties.ContainsKey(MagicAttributes.PdmsConnectionSize8))
                            {
                                sizeValue = properties[MagicAttributes.PdmsConnectionSize8];
                            }

                            var size = sizeValue.Split('x');
                            if (size.Length == 1)
                            {
                                description += "~D" + size.First();
                            }
                            else if (size.Length == 2)
                            {
                                double a, b;
                                if (double.TryParse(size.First(), out a) && double.TryParse(size.Last(), out b))
                                {
                                    description += Math.Min(a, b) + "x" + Math.Max(a, b);
                                }
                                else
                                {
                                    description += sizeValue;
                                }
                            }
                            else
                            {
                                description += sizeValue;
                            }
                        }
                    }

                    #endregion
                }
                else if (fittingType.Equals("переход", StringComparison.OrdinalIgnoreCase))
                {
                    #region переход

                    if (properties.ContainsKey(MagicAttributes.PdmsComponentType))
                    {
                        description = properties[MagicAttributes.PdmsComponentType] + " ";

                        if (properties.ContainsKey(MagicAttributes.PdmsMaterial))
                        {
                            description += "из " + MaterialStringModification(properties[MagicAttributes.PdmsMaterial]) +
                                           ", ";
                        }

                        if (properties.ContainsKey(MagicAttributes.PdmsNationalCode) &&
                            properties[MagicAttributes.PdmsNationalCode].Contains("ГОСТ"))
                        {
                            description += "по " + properties[MagicAttributes.PdmsNationalCode].Trim() + ", ";
                        }
                        else if (properties.ContainsKey(MagicAttributes.PdmsDuctType) &&
                                 properties[MagicAttributes.PdmsDuctType].Contains("ГОСТ"))
                        {
                            var gost = properties[MagicAttributes.PdmsDuctType].Trim();
                            gost = gost.Substring(gost.IndexOf("\"")).Replace("\"", string.Empty);
                            description += "по " + gost + ", ";
                        }
                        else if (properties.ContainsKey(MagicAttributes.PdmsPipeType) &&
                                 properties[MagicAttributes.PdmsPipeType].Contains("ГОСТ"))
                        {
                            var gost = properties[MagicAttributes.PdmsPipeType].Trim();
                            gost = gost.Substring(gost.IndexOf("\"")).Replace("\"", string.Empty);
                            description += "по " + gost + ", ";
                        }

                        if (item.Faces.Count == 0 && item.Solid.Thickness > 0)
                        {
                            description += "~dlt=" + Math.Round(item.Solid.Thickness, 1) + "мм, ";
                        }

                        if (properties.ContainsKey(MagicAttributes.PdmsConnectionSize5))
                        {
                            var sizeValue = properties[MagicAttributes.PdmsConnectionSize5];

                            var size = sizeValue.Split('/');
                            if (size.Length == 2)
                            {
                                var sizeFrom = size[0];
                                var sizeTo = size[1];
                                var tempSize = string.Empty;
                                if (sizeFrom.Split('x').Length == 1)
                                {
                                    tempSize += "~D" + sizeFrom;
                                }
                                else if (sizeFrom.Split('x').Length == 2)
                                {
                                    double a, b;
                                    if (double.TryParse(sizeFrom.Split('x').First(), out a) &&
                                        double.TryParse(sizeFrom.Split('x').Last(), out b))
                                    {
                                        tempSize += Math.Min(a, b) + "x" + Math.Max(a, b);
                                    }
                                    else
                                    {
                                        tempSize += sizeFrom;
                                    }
                                }
                                else
                                {
                                    tempSize += sizeFrom;
                                }


                                tempSize += "/";
                                if (sizeTo.Split('x').Length == 1)
                                {
                                    tempSize += "~D" + sizeTo;
                                }
                                else if (sizeTo.Split('x').Length == 2)
                                {
                                    double a, b;
                                    if (double.TryParse(sizeTo.Split('x').First(), out a) &&
                                        double.TryParse(sizeTo.Split('x').Last(), out b))
                                    {
                                        tempSize += Math.Min(a, b) + "x" + Math.Max(a, b);
                                    }
                                    else
                                    {
                                        tempSize += sizeTo;
                                    }
                                }
                                else
                                {
                                    tempSize += sizeTo;
                                }
                                description += tempSize;
                            }
                            else
                            {
                                description += sizeValue;
                            }
                        }
                    }

                    #endregion
                }
                else if (fittingType.Equals("стык", StringComparison.OrdinalIgnoreCase))
                {
                    #region стык

                    if (properties.ContainsKey(MagicAttributes.PdmsComponentType))
                    {
                        description = properties[MagicAttributes.PdmsComponentType] + " ";

                        if (properties.ContainsKey(MagicAttributes.PdmsMaterial))
                        {
                            description += "из " + MaterialStringModification(properties[MagicAttributes.PdmsMaterial]) +
                                           ", ";
                        }

                        if (properties.ContainsKey(MagicAttributes.PdmsNationalCode) &&
                            properties[MagicAttributes.PdmsNationalCode].Contains("ГОСТ"))
                        {
                            description += "по " + properties[MagicAttributes.PdmsNationalCode].Trim() + ", ";
                        }
                        else if (properties.ContainsKey(MagicAttributes.PdmsDuctType) &&
                                 properties[MagicAttributes.PdmsDuctType].Contains("ГОСТ"))
                        {
                            var gost = properties[MagicAttributes.PdmsDuctType].Trim();
                            gost = gost.Substring(gost.IndexOf("\"")).Replace("\"", string.Empty);
                            description += "по " + gost + ", ";
                        }
                        else if (properties.ContainsKey(MagicAttributes.PdmsPipeType) &&
                                 properties[MagicAttributes.PdmsPipeType].Contains("ГОСТ"))
                        {
                            var gost = properties[MagicAttributes.PdmsPipeType].Trim();
                            gost = gost.Substring(gost.IndexOf("\"")).Replace("\"", string.Empty);
                            description += "по " + gost + ", ";
                        }

                        if (item.Faces.Count == 0 && item.Solid.Thickness > 0)
                        {
                            description += "~dlt=" + Math.Round(item.Solid.Thickness, 1) + "мм, ";
                        }

                        if (properties.ContainsKey(MagicAttributes.PdmsConnectionSize4))
                        {
                            var sizeValue = properties[MagicAttributes.PdmsConnectionSize4];
                            var size = sizeValue.Split('x');
                            if (size.Length == 1)
                            {
                                description += "~D" + size.First();
                            }
                            else if (size.Length == 2)
                            {
                                double a, b;
                                if (double.TryParse(size.First(), out a) && double.TryParse(size.Last(), out b))
                                {
                                    description += Math.Min(a, b) + "x" + Math.Max(a, b);
                                }
                                else
                                {
                                    description += sizeValue;
                                }
                            }
                            else
                            {
                                description += sizeValue;
                            }
                        }
                    }

                    #endregion
                }
                else if (fittingType.Equals("тройник", StringComparison.OrdinalIgnoreCase))
                {
                    #region тройник

                    if (properties.ContainsKey(MagicAttributes.PdmsComponentType))
                    {
                        description = properties[MagicAttributes.PdmsComponentType] + " ";

                        if (properties.ContainsKey(MagicAttributes.PdmsMaterial))
                        {
                            description += "из " + MaterialStringModification(properties[MagicAttributes.PdmsMaterial]) +
                                           ", ";
                        }

                        if (properties.ContainsKey(MagicAttributes.PdmsNationalCode) &&
                            properties[MagicAttributes.PdmsNationalCode].Contains("ГОСТ"))
                        {
                            description += "по " + properties[MagicAttributes.PdmsNationalCode].Trim() + ", ";
                        }
                        else if (properties.ContainsKey(MagicAttributes.PdmsDuctType) &&
                                 properties[MagicAttributes.PdmsDuctType].Contains("ГОСТ"))
                        {
                            var gost = properties[MagicAttributes.PdmsDuctType].Trim();
                            gost = gost.Substring(gost.IndexOf("\"")).Replace("\"", string.Empty);
                            description += "по " + gost + ", ";
                        }
                        else if (properties.ContainsKey(MagicAttributes.PdmsPipeType) &&
                                 properties[MagicAttributes.PdmsPipeType].Contains("ГОСТ"))
                        {
                            var gost = properties[MagicAttributes.PdmsPipeType].Trim();
                            gost = gost.Substring(gost.IndexOf("\"")).Replace("\"", string.Empty);
                            description += "по " + gost + ", ";
                        }

                        if (item.Faces.Count == 0 && item.Solid.Thickness > 0)
                        {
                            description += "~dlt=" + Math.Round(item.Solid.Thickness, 1) + "мм, ";
                        }

                        if (properties.ContainsKey(MagicAttributes.PdmsConnectionSize5))
                        {
                            var sizeValue = properties[MagicAttributes.PdmsConnectionSize5];

                            var size = sizeValue.Split('/');
                            if (size.Length == 2)
                            {
                                var sizeFrom = size[0];
                                var sizeTo = size[1];
                                var tempSize = string.Empty;
                                if (sizeFrom.Split('x').Length == 1)
                                {
                                    tempSize += "~D" + sizeFrom;
                                }
                                else if (sizeFrom.Split('x').Length == 2)
                                {
                                    double a, b;
                                    if (double.TryParse(sizeFrom.Split('x').First(), out a) &&
                                        double.TryParse(sizeFrom.Split('x').Last(), out b))
                                    {
                                        tempSize += Math.Min(a, b) + "x" + Math.Max(a, b);
                                    }
                                    else
                                    {
                                        tempSize += sizeFrom;
                                    }
                                }
                                else
                                {
                                    tempSize += sizeFrom;
                                }


                                tempSize += "/";
                                if (sizeTo.Split('x').Length == 1)
                                {
                                    tempSize += "~D" + sizeTo;
                                }
                                else if (sizeTo.Split('x').Length == 2)
                                {
                                    double a, b;
                                    if (double.TryParse(sizeTo.Split('x').First(), out a) &&
                                        double.TryParse(sizeTo.Split('x').Last(), out b))
                                    {
                                        tempSize += Math.Min(a, b) + "x" + Math.Max(a, b);
                                    }
                                    else
                                    {
                                        tempSize += sizeTo;
                                    }
                                }
                                else
                                {
                                    tempSize += sizeTo;
                                }
                                description += tempSize;
                            }
                            else
                            {
                                description += sizeValue;
                            }
                        }
                    }

                    #endregion
                }

                #endregion
            }
            else if (type.Equals("IfcFlowTerminal"))
            {
                #region Приточное ВРУ
                if (properties.ContainsKey(MagicAttributes.PdmsComponentType))
                {
                    description = properties[MagicAttributes.PdmsComponentType];
                }

                if (properties.ContainsKey(MagicAttributes.PdmsComponent20))
                {
                    var value = properties[MagicAttributes.PdmsComponent20];
                    if (properties.ContainsKey(MagicAttributes.PdmsSize9))
                    {
                        value = value.Substring(0, value.IndexOf("\"")).Trim() + " \"" +
                                properties[MagicAttributes.PdmsSize9] + "\"";
                    }
                    root.SetAttribute(DbAttribute.GetDbAttribute(":EquiType"), value.Replace("�", "?"));
                }
                #endregion
            }
            else if (type.Equals("IfcFlowMovingDevice") || type.Equals("IfcFlowTreatmentDevice"))
            {
                #region Вентиляторы, Фильтры
                if (properties.ContainsKey(MagicAttributes.PdmsComponentType))
                {
                    description = properties[MagicAttributes.PdmsComponentType];
                }

                if (properties.ContainsKey(MagicAttributes.PdmsComponent9))
                {
                    var value = properties[MagicAttributes.PdmsComponent9];
                    if (properties.ContainsKey(MagicAttributes.PdmsSize8))
                    {
                        value = value.Substring(0, value.IndexOf("\"")).Trim() + " \"" +
                                properties[MagicAttributes.PdmsSize8] + "\"";
                    }
                    root.SetAttribute(DbAttribute.GetDbAttribute(":EquiType"), value.Replace("�", "?"));
                }
                
                #endregion
            }
            else if (type.Equals("IfcBuildingElementProxy"))
            {
                #region Строительные отверстия, соединения
                if (properties.ContainsKey(MagicAttributes.PdmsComponentType))
                {
                    description = properties[MagicAttributes.PdmsComponentType] + ", ";
                    if (properties.ContainsKey(MagicAttributes.PdmsDiamMm))
                    {
                        description += "~D" + properties[MagicAttributes.PdmsDiamMm] + ", ";
                    }
                    else if (properties.ContainsKey(MagicAttributes.PdmsHeightMm) &&
                             properties.ContainsKey(MagicAttributes.PdmsWidthMm))
                    {
                        var h = properties[MagicAttributes.PdmsHeightMm];
                        var w = properties[MagicAttributes.PdmsWidthMm];

                        double hDouble,wDouble;
                        if (double.TryParse(h, out hDouble) && double.TryParse(w, out wDouble))
                        {
                            description += Math.Min(hDouble, wDouble) + "x" + Math.Max(hDouble, wDouble) +", ";
                        }
                        else
                        {
                            description += h + "x" + w + ", ";
                        }
                    }

                    if (properties.ContainsKey(MagicAttributes.PdmsBoxLength))
                    {
                        description += "l="+properties[MagicAttributes.PdmsBoxLength]+"мм";
                    }
                }

                #endregion
            }
            else if (type.Equals("IfcFlowSegment"))
            {
                #region Трубы

                if (properties.ContainsKey(MagicAttributes.PdmsComponentType))
                {
                    description = properties[MagicAttributes.PdmsComponentType] + " ";

                    if (properties.ContainsKey(MagicAttributes.PdmsMaterial))
                    {
                        description += "из " + MaterialStringModification(properties[MagicAttributes.PdmsMaterial]) +
                                       ", ";
                    }

                    if (properties.ContainsKey(MagicAttributes.PdmsNationalCode) &&
                        properties[MagicAttributes.PdmsNationalCode].Contains("ГОСТ"))
                    {
                        description += "по " + properties[MagicAttributes.PdmsNationalCode].Trim() + ", ";
                    }
                    else if (properties.ContainsKey(MagicAttributes.PdmsDuctType) &&
                             properties[MagicAttributes.PdmsDuctType].Contains("ГОСТ"))
                    {
                        var gost = properties[MagicAttributes.PdmsDuctType].Trim();
                        gost = gost.Substring(gost.IndexOf("\"")).Replace("\"", string.Empty);
                        description += "по " + gost + ", ";
                    }
                    else if (properties.ContainsKey(MagicAttributes.PdmsPipeType) &&
                             properties[MagicAttributes.PdmsPipeType].Contains("ГОСТ"))
                    {
                        var gost = properties[MagicAttributes.PdmsPipeType].Trim();
                        gost = gost.Substring(gost.IndexOf("\"")).Replace("\"", string.Empty);
                        description += "по " + gost + ", ";
                    }
                    if (item.Faces.Count == 0 && item.Solid.Thickness > 0)
                    {
                        description += "~dlt=" + Math.Round(item.Solid.Thickness, 1) + "мм, ";
                    }

                    if (properties.ContainsKey(MagicAttributes.PdmsConnectionSize4) ||
                        properties.ContainsKey(MagicAttributes.PdmsConnectionSize8))
                    {
                        var sizeValue = string.Empty;
                        if (properties.ContainsKey(MagicAttributes.PdmsConnectionSize4))
                        {
                            sizeValue = properties[MagicAttributes.PdmsConnectionSize4];
                        }
                        else if (properties.ContainsKey(MagicAttributes.PdmsConnectionSize8))
                        {
                            sizeValue = properties[MagicAttributes.PdmsConnectionSize8];
                        }

                        var size = sizeValue.Split('x');
                        if (size.Length == 1)
                        {
                            description += "~D" + size.First();
                        }
                        else if (size.Length == 2)
                        {
                            double a, b;
                            if (double.TryParse(size.First(), out a) && double.TryParse(size.Last(), out b))
                            {
                                description += Math.Min(a, b) + "x" + Math.Max(a, b);
                            }
                            else
                            {
                                description += sizeValue;
                            }
                        }
                        else
                        {
                            description += sizeValue;
                        }
                    }
                    else if (item.Faces.Count == 0)
                    {
                        var size = item.Solid.Size;
                        if (size.Length == 1)
                        {
                            description += "~D" + size.First();
                        }
                        else if (size.Length == 2)
                        {
                            description += Math.Round(Math.Min(size.First(), size.Last())) + "x" + Math.Round(Math.Max(size.First(), size.Last()));
                        }
                    }
                }

                if (properties.ContainsKey(MagicAttributes.PdmsLengthMm))
                {
                    root.SetAttribute(DbAttribute.GetDbAttribute(":EquiCount"), properties[MagicAttributes.PdmsLengthMm]);
                    root.SetAttribute(DbAttribute.GetDbAttribute(":EquiUnit"), "мм");
                }

                if (properties.ContainsKey(MagicAttributes.PdmsComponent2))
                {
                    root.SetAttribute(DbAttribute.GetDbAttribute(":EquiType"), properties[MagicAttributes.PdmsComponent2].Replace("�", "?"));
                }

                #endregion
            }

            if (properties.ContainsKey(MagicAttributes.PdmsPlant))
            {
                root.SetAttribute(DbAttribute.GetDbAttribute(":EquiPlant"), properties[MagicAttributes.PdmsPlant].Replace("�", "?"));
            }

            if (properties.ContainsKey(MagicAttributes.PdmsInsulation1))
            {
                root.SetAttribute(DbAttribute.GetDbAttribute(":EquiNote"), properties[MagicAttributes.PdmsInsulation1].Replace("�", "?"));
            }

            description = description.Trim();
            if (string.IsNullOrEmpty(description))
            {
                if (!string.IsNullOrEmpty(fittingType))
                    root.SetAttribute(DbAttributeInstance.DESC, fittingType.Replace("�", "?"));
            }
            else
            {
                if (description.EndsWith(","))
                    description = description.Substring(0, description.Length - 1);
                root.SetAttribute(DbAttributeInstance.DESC, description.Replace("�", "?"));
            }
        }

        /// <summary>
        /// Преобразование названия материала
        /// </summary>
        /// <param name="material">название материала в MagiCAD</param>
        /// <returns>название материала для описания</returns>
        private static string MaterialStringModification(string material)
        {
            var result = material.Trim().ToLower();
            if (result.Equals("оцинованная сталь") || result.Equals("оцинкованная сталь"))
                return "оцинкованной стали";
            if (result.Equals("полипропилен"))
                return "полипропилена";
            if (result.Equals("сталь"))
                return "стали";
            if (result.Equals("черная сталь"))
                return "черной стали";

            return result;
        }

        /// <summary>
        /// Создание геометрии
        /// </summary>
        /// <param name="root">родительская элемент EQUI или SUBE</param>
        /// <param name="item">элемент MagiCAD</param>
        /// <param name="insulation">true - если создается изоляция</param>
        private static bool CreateGeometry(DbElement root, MagicItem item, bool insulation = false)
        {
            if (item.Solid == null && (item.Faces == null || item.Faces.Count == 0))
                return false;

            return item.Solid == null ? CreateGeometryByFaces(root, item.Faces, insulation) : CreateGeometryBySolid(root, item.Solid, insulation);
        }

        /// <summary>
        /// Создание геометрии по поверхностям
        /// </summary>
        /// <param name="root">родительская элемент EQUI или SUBE</param>
        /// <param name="faces">набор поверхностей</param>
        /// <param name="insulation">true - если создается изоляция</param>
        private static bool CreateGeometryByFaces(DbElement root, List<MagicFace> faces, bool insulation)
        {
            var extrList = root.Members(DbElementTypeInstance.EXTRUSION).ToList();
            if (extrList.Count > faces.Count)
            {
                var diff = extrList.Count - faces.Count;
                for (int i = faces.Count; i < extrList.Count; i++)
                {
                    var item = extrList[i];
                    if (!item.IsNull && item.IsDeleteable)
                    {
                        item.Delete();
                        if (!item.IsDeleted)
                        {
                            RunPdms.SendMessage("Не удалось удалить элемент " + item.GetElementType().ShortName + " " +
                                                item.GetAsString(DbAttributeInstance.REF));
                            return false;
                        }
                    }
                }
                extrList.RemoveRange(faces.Count, diff);
            }
            else if (extrList.Count < faces.Count)
            {
                var diff = faces.Count - extrList.Count;
                for (var i = 0; i < diff; i++)
                {
                    extrList.Add(DbElement.GetElement());
                }
            }

            foreach (var memb in root.Members().Where(memb => !extrList.Contains(memb)))
            {
                memb.Delete();
            }

            for (var i = 0; i < faces.Count; i++)
            {
                var face = faces[i];
                var extr = extrList[i].IsNull ? root.CreateLast(DbElementTypeInstance.EXTRUSION) : extrList[i];

                if (insulation)
                {
                    extr.SetAttribute(DbAttributeInstance.OBST, 1);
                    extr.SetAttribute(DbAttributeInstance.LEVE, new[] {7, 10});
                }

                //TODO: разобраться с толщиной
                //extr.SetAttribute(DbAttributeInstance.HEIG, 0.01);
                extr.SetAttribute(DbAttributeInstance.HEIG, 0.5);

                var bPoint = new Point(face.Points[1][0], face.Points[1][1], face.Points[1][2]);
                var xDirection = Direction.Create(
                    Position.Create(bPoint.X, bPoint.Y, bPoint.Z),
                    Position.Create(face.Points[0][0], face.Points[0][1], face.Points[0][2]));
                var yDirection = Direction.Create(
                    Position.Create(bPoint.X, bPoint.Y, bPoint.Z),
                    Position.Create(face.Points[2][0], face.Points[2][1], face.Points[2][2]));
                var zDirection = xDirection.Orthogonal(yDirection);
                yDirection = zDirection.Orthogonal(xDirection);

                var xVector = new Vector(xDirection.East, xDirection.North, xDirection.Up);
                var yVector = new Vector(yDirection.East, yDirection.North, yDirection.Up);
                xVector.Normalize();
                yVector.Normalize();

                var cs = new CoordinateSystem(bPoint, xVector, yVector);
                var matrix = MatrixFactory.ToCoordinateSystem(cs);
                var oriPdms = cs.AxisX.CreateOrientation(cs.AxisY);
                var zDir = oriPdms.ZDir();
                var projectionEquiPos = Projection.PointToPlane(new Point(),
                    new GeometricPlane(bPoint, new Vector(zDir.East, zDir.North, zDir.Up)));
                var tDir =
                    Direction.Create(Position.Create(projectionEquiPos.X, projectionEquiPos.Y, projectionEquiPos.Z),
                        Position.Create());
                if (tDir.Angle(zDir) > 90)
                {
                    cs = new CoordinateSystem(bPoint, yVector, xVector);
                    matrix = MatrixFactory.ToCoordinateSystem(cs);
                    oriPdms = cs.AxisX.CreateOrientation(cs.AxisY);
                }
                extr.SetAttribute(DbAttributeInstance.ORI, oriPdms);
                extr.SetAttribute(DbAttributeInstance.POS, Position.Create(bPoint.X, bPoint.Y, bPoint.Z));

                var loop = extr.Members(DbElementTypeInstance.LOOP).Any()
                    ? extr.Members(DbElementTypeInstance.LOOP).First()
                    : extr.CreateLast(DbElementTypeInstance.LOOP);

                var vertexList = loop.Members(DbElementTypeInstance.VERTEX).ToList();
                if (vertexList.Count > face.Points.Count)
                {
                    var diff = vertexList.Count - face.Points.Count;
                    for (var j = face.Points.Count; j < vertexList.Count; j++)
                    {
                        var item = vertexList[j];
                        if (!item.IsNull && item.IsDeleteable)
                        {
                            item.Delete();
                            if (!item.IsDeleted)
                            {
                                RunPdms.SendMessage("Не удалось удалить элемент " + item.GetElementType().ShortName +
                                                    " " + item.GetAsString(DbAttributeInstance.REF));
                                return false;
                            }
                        }
                    }
                    vertexList.RemoveRange(face.Points.Count, diff);
                }
                else if (vertexList.Count < face.Points.Count)
                {
                    var diff = face.Points.Count - vertexList.Count;
                    for (var j = 0; j < diff; j++)
                    {
                        vertexList.Add(DbElement.GetElement());
                    }
                }
                for (var j = 0; j < face.Points.Count; j++)
                {
                    var p = face.Points[j];
                    var vertex = vertexList[j].IsNull ? loop.CreateLast(DbElementTypeInstance.VERTEX) : vertexList[j];
                    var point = new Point(p[0], p[1], p[2]);
                    var locPoint = matrix.Transform(point);
                    var pos = Position.Create(locPoint.X, locPoint.Y, 0);
                    vertex.SetAttribute(DbAttributeInstance.POS, pos);
                }
            }
            return true;
        }

        /// <summary>
        /// Создание геометрии твердыми телами
        /// </summary>
        /// <param name="root">родительская элемент EQUI или SUBE</param>
        /// <param name="solid">твердое тело MagiCAD</param>
        /// <param name="insulation">true - если создается изоляция</param>
        private static bool CreateGeometryBySolid(DbElement root, MagicSolid solid, bool insulation)
        {
            switch (solid.Type)
            {
                case 0:
                    return CreateGeometryBySolidCircle(root, solid, insulation);
                case 1:
                    return CreateGeometryBySolidRectangle(root, solid, insulation);
                default:
                    return false;
            }
        }

        /// <summary>
        /// Создание геометрии цилиндрами
        /// </summary>
        /// <param name="root">родительская элемент EQUI или SUBE</param>
        /// <param name="solid">твердое тело MagiCAD</param>
        /// <param name="insulation">true - если создается изоляция</param>
        private static bool CreateGeometryBySolidCircle(DbElement root, MagicSolid solid, bool insulation)
        {
            var cyli = root.Members(DbElementTypeInstance.CYLINDER).Any()
                ? root.Members(DbElementTypeInstance.CYLINDER).First()
                : root.CreateLast(DbElementTypeInstance.CYLINDER);
            foreach (var memb in root.Members().Where(memb => !memb.Equals(cyli)))
            {
                memb.Delete();
            }

            cyli.SetAttribute(DbAttributeInstance.HEIG, solid.Height);
            cyli.SetAttribute(DbAttributeInstance.DIAM, solid.Size[0]*2);

            var nitem = cyli.Members(DbElementTypeInstance.NCYLINDER).Any()
                ? cyli.Members(DbElementTypeInstance.NCYLINDER).First()
                : cyli.CreateLast(DbElementTypeInstance.NCYLINDER);

            nitem.SetAttribute(DbAttributeInstance.HEIG,solid.Height);
            nitem.SetAttribute(DbAttributeInstance.DIAM, solid.Size[0]*2 - solid.Thickness*2);

            if (insulation)
            {
                cyli.SetAttribute(DbAttributeInstance.OBST, 1);
                cyli.SetAttribute(DbAttributeInstance.LEVE, new[] { 7, 10 });
            }

            var rootOri = root.GetOrientation(DbAttributeInstance.ORI);
            //var ori = Orientation.Create(rootOri.ZDir(), rootOri.YDir());
            //ori = ori.Local(rootOri);
            //cyli.SetAttribute(DbAttributeInstance.ORI, ori);
            var ori = Orientation.Create("Y is " + rootOri.YDir() + " and Z is " + rootOri.XDir());
            var localOri = Orientation.Create("Y is " + rootOri.LocalDirection(ori.YDir()) + " and Z is " + rootOri.LocalDirection(ori.ZDir()));
            if (localOri.AboutX.Equals(double.NaN) || localOri.AboutY.Equals(double.NaN) ||
                localOri.AboutZ.Equals(double.NaN))
            {
                Console.WriteLine("Bad orientation");
            }
            else
            {
                cyli.SetAttribute(DbAttributeInstance.ORI, localOri);
            }

            var pos = Position.Create(solid.Height / 2, 0, 0);
            cyli.SetAttribute(DbAttributeInstance.POS, pos);
            return true;
        }

        /// <summary>
        /// Создание геометрии цилиндрами
        /// </summary>
        /// <param name="root">родительская элемент EQUI или SUBE</param>
        /// <param name="solid">твердое тело MagiCAD</param>
        /// <param name="insulation">true - если создается изоляция</param>
        private static bool CreateGeometryBySolidRectangle(DbElement root, MagicSolid solid, bool insulation)
        {
            var box = root.Members(DbElementTypeInstance.BOX).Any()
                ? root.Members(DbElementTypeInstance.BOX).First()
                : root.CreateLast(DbElementTypeInstance.BOX);

            foreach (var memb in root.Members().Where(memb => !memb.Equals(box)))
            {
                memb.Delete();
            }

            box.SetAttribute(DbAttributeInstance.XLEN, solid.Height);
            box.SetAttribute(DbAttributeInstance.YLEN, solid.Size[0]);
            box.SetAttribute(DbAttributeInstance.ZLEN, solid.Size[1]);

            var nitem = box.Members(DbElementTypeInstance.NBOX).Any()
                ? box.Members(DbElementTypeInstance.NBOX).First()
                : box.CreateLast(DbElementTypeInstance.NBOX);
            nitem.SetAttribute(DbAttributeInstance.XLEN, solid.Height);
            nitem.SetAttribute(DbAttributeInstance.YLEN, solid.Size[0] - solid.Thickness * 2);
            nitem.SetAttribute(DbAttributeInstance.ZLEN, solid.Size[1] - solid.Thickness * 2);

            if (insulation)
            {
                box.SetAttribute(DbAttributeInstance.OBST, 1);
                box.SetAttribute(DbAttributeInstance.LEVE, new[] { 7, 10 });
            }

            var pos = Position.Create(solid.Height / 2, 0, 0);
            box.SetAttribute(DbAttributeInstance.POS, pos);
            return true;
        }
    }
}
