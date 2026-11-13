using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using Aveva.Pdms.Database;
using Aveva.Pdms.Geometry;
using Aveva.Pdms.Utilities.CommandLine;
using Aveva.PDMS.Database.Filters;
using Ionic.Zlib;
using Polymetal.Exchange.Utilities;
using Xbim.Common.Geometry;

namespace Polymetal.Exchange.Pdms
{
    internal class RevitImport
    {
        private static DbElement _arZone = DbElement.GetElement();
        private static DbElement _soSite = DbElement.GetElement();
        private static DbAttribute _guidRevitAtt = DbAttribute.GetDbAttribute(":PolyRevitGuid");
        internal static DbAttribute _guidMagicAtt = DbAttribute.GetDbAttribute(":PolyMagiGuid");
        private static XmlSerializer _xmlFormat = new XmlSerializer(typeof(IfcElementInformation));
        private static string _documentCode = "07";

        internal static bool Run()
        {
            var histories = new List<RunPdms.ElementHistory>();
            var triggerUser = RunPdms.GetUserByTriggerId();
            Console.WriteLine("Начинается импорт данных");
            Command.CreateCommand("!!zoneCreateFlag = true").Run();
            var dt = Common.GetUpdatedRevitData(RunPdms.ProjectInfo.ProjectId);
            Console.WriteLine("Элементов для обработки: " + dt.Rows.Count + " шт.");
            var oldPercent = 0.0;
            var modifications = 0;

            var file = new FileInfo(RunPdms.ProjectInfo.Program.Path);
            if (file.Exists)
            {

                var dirName = file.Directory.Name;
                if (dirName.Split(' ').Length > 2)
                {
                    _documentCode = dirName.Split(' ')[1].Trim();
                }
            }

            _arZone = GetRootZone();
            if (_arZone.IsNull || !_arZone.IsValid) return false;

            for (var i = 0; i < dt.Rows.Count; i++)
            {
                var row = dt.Rows[i];
                var percent = Math.Ceiling((double)i * 100 / dt.Rows.Count);
                if (oldPercent < percent)
                {
                    Console.WriteLine("Current progress {0}%   ", Math.Ceiling(percent));
                    oldPercent = percent;
                }

                var revitItem = new IfcElementInformation();
                if (row["element_info"].ToString().Contains("<IfcElementInformation"))
                {
                    using (var stream = new StringReader(row["element_info"].ToString()))
                    {
                        revitItem = (IfcElementInformation) _xmlFormat.Deserialize(stream);
                        stream.Close();
                    }
                }

                try
                {
                    var pdmsItem = DbElement.GetElement(row["pdms_item"].ToString());
                    if (row["element_status"].ToString() == "DEL")
                    {
                        if (pdmsItem.IsNull)
                        {
                            if (row["element_info"].ToString().Contains("<RevitElement"))
                            {
                                var str = row["element_info"].ToString();
                                var i1 = str.IndexOf("<Id>");
                                var i2 = str.IndexOf("</Id>");
                                revitItem.Id = str.Substring(i1 + 4, i2 - i1 - 4);
                            }

                            var andFilter =
                                new AndFilter(
                                    new TypeFilter(new[]
                                    {
                                        DbElementTypeInstance.GWALL, DbElementTypeInstance.FLOOR,
                                        DbElementTypeInstance.PANEL
                                    }),
                                    new AttributeStringFilter(DbAttribute.GetDbAttribute(":PolyRevitGuid"),
                                        FilterOperator.Equals, revitItem.Id));
                            var itemCollection = new DBElementCollection(_arZone, andFilter).Cast<DbElement>().ToList();
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
                                RunPdms.SendMessage("[Delete Revit] Не удалось удалить элемент " +
                                                    pdmsItem.GetElementType().ShortName + " " +
                                                    pdmsItem.GetAsString(DbAttributeInstance.REF));
                            }
                        }
                        else
                        {
                            row["element_status"] = "REM";
                        }
                    }
                    else
                    {
                        if (ModifyItem(revitItem, ref pdmsItem))
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
                            if (row["element_status"].Equals("CRE") && !pdmsItem.IsNull)
                            {
                                pdmsItem.Delete();
                            }
                            Console.WriteLine("Can't modify element " + revitItem.Id);
                        }
                    }

                    modifications++;
                    row["pdms_item"] = pdmsItem.IsNull || pdmsItem.IsDeleted
                        ? DBNull.Value.ToString()
                        : pdmsItem.GetAsString(DbAttributeInstance.REF);
                }
                catch (Exception exc)
                {
                    RunPdms.SendMessage("[ModifyRevit] " + row["element_guid"] + " " + exc.Message);
                }
            }

            RunPdms.SendMessage("Изменений в модели: " + modifications);
            Console.WriteLine("Заполнение данных по истории элементов (" + histories.Count + " записей)");
            RunPdms.UpdateHistory(histories, Common.Source.Revit);
            Console.WriteLine("История элементов обновлена");
            Common.SetUpdatedRevitData(dt, RunPdms.ProjectInfo.ProjectId);
            Console.WriteLine("Данные в SQL обновлены");
            return true;
        }

        /// <summary>
        /// Получение элемента ZONE, внутри которого будет вестись работа
        /// </summary>
        /// <param name="mdbName">название текущей MDB</param>
        /// <param name="mark">название марки, по которой будет производиться поиск нужной ZONE</param>
        /// <returns>итоговый элемент</returns>
        private static DbElement GetRootZone()
        {
            var zone = DbElement.GetElement();
            var world = MDB.CurrentMDB.GetFirstWorld(DbType.Design);
            if (world.IsNull)
            {
                return DbElement.GetElement();
            }

            if (_soSite == null || _soSite.IsNull)
            {
                var siteCollection = new DBElementCollection(world, new TypeFilter(DbElementTypeInstance.SITE));
                foreach (DbElement item in siteCollection)
                {
                    var name = item.GetAsString(DbAttributeInstance.NAME).ToUpper();
                    if (!name.Contains(MDB.CurrentMDB.Name.Replace("-", "").ToUpper()) || !name.EndsWith("-SO")) continue;
                    _soSite = item;
                    break;
                }

                if (_soSite.IsNull || !_soSite.IsValid)
                {
                    return DbElement.GetElement();
                }
            }
            
            foreach (DbElement item in _soSite.Members(DbElementTypeInstance.ZONE))
            {
                var name = item.GetAsString(DbAttributeInstance.FLNN).ToUpper();
                if (!name.StartsWith(_soSite.GetAsString(DbAttributeInstance.FLNN) + "-AR-"+_documentCode)) continue;
                zone = item;
                break;
            }

            if (zone.IsNull)
            {
                CreateZoneElement("AR", ref zone);
            }

            return zone;
        }

        /// <summary>
        /// Создание элемента ZONE, внутри которого будет вестись работа
        /// </summary>
        /// <param name="site">элемент SITE, внутри которого будет создана ZONE</param>
        /// <param name="mark">название марки</param>
        /// <param name="zone">итоговый элемент</param>
        private static void CreateZoneElement(string mark, ref DbElement zone)
        {
            Command.CreateCommand("!!ZoneCreateFlag = true").Run();
            if (_soSite.IsCreatable(DbElementTypeInstance.ZONE) && !_soSite.GetBool(DbAttributeInstance.LOCK))
            {
                zone = _soSite.CreateLast(DbElementTypeInstance.ZONE);
                if (!zone.IsNull && zone.IsValid)
                {
                    zone.SetAttribute(DbAttributeInstance.NAME, _soSite.GetAsString(DbAttributeInstance.FLNM) + "-" + mark + "-"+_documentCode);
                    zone.SetAttribute(DbAttributeInstance.DESC, GetDescription(mark, DbElementTypeInstance.ZONE));
                }
            }
        }

        /// <summary>
        /// Получение описания из БД для создаваемого элемента
        /// </summary>
        /// <param name="code">код создаваемого элемента</param>
        /// <param name="type">тип создаваемого элемента</param>
        /// <returns></returns>
        private static string GetDescription(string code, DbElementType type)
        {
            string tablName;
            if (type == DbElementTypeInstance.ZONE)
            {
                tablName = "DocumentMark";
            }
            else if (type == DbElementTypeInstance.STRUCTURE)
            {
                tablName = "ARElements";
            }
            else if (type == DbElementTypeInstance.FRMWORK)
            {
                tablName = "ARFunc";
            }
            else
            {
                return code;
            }

            var result = Common.GetPdmsElementDescription(code, tablName);
            if (string.IsNullOrEmpty(result) && type == DbElementTypeInstance.FRMWORK)
            {
                result = code;
            }
            return string.IsNullOrEmpty(result) ? code : result;
        }

        internal static void SetPdmsAttribute(DbElement item, DbAttribute attribute, object value)
        {
            if (attribute.Equals(DbAttributeInstance.DESC) || 
                attribute.Equals(_guidRevitAtt) || 
                attribute.Equals(_guidMagicAtt) || 
                attribute.Equals(DbAttributeInstance.FUNC) ||
                attribute.Type == DbAttributeType.STRING)
            {
                var strValue = (string)value;
                if (attribute == DbAttributeInstance.DESC && strValue.Length>120)
                {
                    strValue = strValue.Substring(0, 120);
                }
                var cValue = item.GetAsString(attribute);
                if (cValue.Equals(strValue)) return;
                item.SetAttribute(attribute, strValue);
            }
            else if (attribute.Equals(DbAttributeInstance.POS))
            {
                var cValue = item.GetPosition(attribute);
                if (cValue.Equals((Position)value)) return;
                item.SetAttribute(attribute, (Position)value);
            }
            else if (attribute.Equals(DbAttributeInstance.ORI))
            {
                var cValue = item.GetOrientation(attribute);
                if (cValue.Equals((Orientation)value)) return;
                item.SetAttribute(attribute, (Orientation)value);
            }
            else if (attribute.Equals(DbAttributeInstance.HEIG) || attribute.Equals(DbAttributeInstance.DIAM))
            {
                var cValue = item.GetDouble(attribute);
                if (cValue.Equals((double)value)) return;
                item.SetAttribute(attribute, (double)value);
            }
            else if (attribute.Equals(DbAttributeInstance.LEVE))
            {
                var cValue = item.GetIntegerArray(attribute);
                if (cValue.Equals((int[])value)) return;
                item.SetAttribute(attribute, (int[])value);
            }
        }

        private static bool ModifyItem(IfcElementInformation info, ref DbElement pdmsItem)
        {
            DbElementType itemType;
            if (info.Type == IfcElementInformation.ElementType.Wall)
            {
                itemType = DbElementTypeInstance.GWALL;
            }
            else if (info.Type == IfcElementInformation.ElementType.Slab)
            {
                itemType = DbElementTypeInstance.FLOOR;
            }
            else
            {
                itemType = DbElementTypeInstance.PANEL;
            }

            if (!itemType.IsValid)
            {
                RunPdms.SendMessage("Неизвестный тип для элемента с ID " + info.Id);
                return false;
            }

            var frmw = DbElement.GetElement();
            var findRoot = GetRootFrmw(info.Properties, info.Type, ref frmw);
            if (!findRoot || frmw.IsNull)
            {
                RunPdms.SendMessage("Не найден родительский элемент для элемента с ID " + info.Id);
                return false;
            }

            var desc = string.Empty;
            foreach (var prop in info.Properties)
            {
                var data = prop.Split(new[] { "#" }, StringSplitOptions.RemoveEmptyEntries);
                if (!data[0].Equals("description")) continue;
                desc = data.Length > 1 ? data[1] : string.Empty;
                break;
            }

            if (pdmsItem.IsNull)
            {
                pdmsItem = frmw.CreateLast(itemType);
            }
            else if (!pdmsItem.GetElementType().Equals(itemType))
            {
                pdmsItem.Delete();
                if (!pdmsItem.IsDeleted) return false;
                pdmsItem = frmw.CreateLast(itemType);
            }
            else if (!pdmsItem.Owner.Equals(frmw) && !MoveItemToNewClass(pdmsItem, frmw))
            {
                return false;
            }

            SetPdmsAttribute(pdmsItem, DbAttributeInstance.DESC, desc);
            SetPdmsAttribute(pdmsItem, _guidRevitAtt, info.Id);

            if (!ModifyMainGeometry(pdmsItem, info.Representation))
            {
                RunPdms.SendMessage("Не удалось модифицировать геометрию элемента с ID " + info.Id);
                return false;
            }
            
            if (info.Children.Count <= 0)
            {
                foreach (var memb in pdmsItem.Members(DbElementTypeInstance.CMPFITTING))
                {
                    memb.Delete();
                }
                return true;
            }

            var cmpf = DbElement.GetElement();
            foreach (var memb in pdmsItem.Members(DbElementTypeInstance.CMPFITTING))
            {
                if (cmpf.IsNull)
                {
                    cmpf = memb;
                    continue;
                }
                memb.Delete();
            }

            if (cmpf.IsNull)
                cmpf = pdmsItem.CreateLast(DbElementTypeInstance.CMPFITTING);
            
            var sbfiList = cmpf.Members(DbElementTypeInstance.SBFITTING).ToList();
            if (info.Children.Count < sbfiList.Count)
            {
                var diff = sbfiList.Count - info.Children.Count;
                for (var i = 0; i < diff; i++)
                {
                    var sbfi = sbfiList.Last();
                    sbfi.Delete();
                    sbfiList.Remove(sbfi);
                }
            }
            sbfiList = cmpf.Members(DbElementTypeInstance.SBFITTING).ToList();
            var sbfiIndex = 0;
            foreach (var child in info.Children)
            {
                var sbfi = sbfiList.Count < sbfiIndex + 1 ? cmpf.CreateLast(DbElementTypeInstance.SBFITTING) : sbfiList[sbfiIndex];

                if (!ModifySecondaryGeometry(sbfi, child.Representation))
                {
                    RunPdms.SendMessage("Не удалось модифицировать геометрию второстепенного элемента с ID " + child.Id);
                    return false;
                }

                desc = string.Empty;
                foreach (var prop in child.Properties)
                {
                    var data = prop.Split(new[] { "#" }, StringSplitOptions.RemoveEmptyEntries);
                    if (!data[0].Equals("description")) continue;
                    desc = data.Length > 1 ? data[1] : string.Empty;
                    break;
                }

                SetPdmsAttribute(sbfi, DbAttributeInstance.DESC, desc);
                SetPdmsAttribute(sbfi, _guidRevitAtt, child.Id);
                sbfiIndex++;
            }
            return true;
        }

        private static bool ModifyMainGeometry(DbElement item, IfcElementInformation.ItemRepresentation representation)
        {
            var mainPosition = Position.Create();
            var mainOrientation = Orientation.Create();
            var positive = representation.Positive;
            if (positive.Data == null) return false;
            var data = GeometryDataFromString(positive.Data, positive.Type);
            if (positive.Type == IfcElementInformation.GeometryType.Mesh)
            {
                var bytes = (byte[])data;
                var point = GetPositionFromBytes(bytes);
                var tp = TransformPoint(item, point);
                mainPosition = Position.Create(tp.X, tp.Y, tp.Z);
                mainOrientation = Orientation.Create("Z is U");
            }
            else
            {
                var parameters = (double[])data;
                if (positive.Type == IfcElementInformation.GeometryType.Extrusion)
                {                    
                    var normal = new XbimVector3D(parameters[1], parameters[2], parameters[3]);
                    var point = new XbimPoint3D(parameters[4], parameters[5], parameters[6]);

                    var tp = TransformPoint(item, point);
                    var tv = TransformVector(item, normal);
                    var zDir = Direction.Create(Position.Create(tv.X, tv.Y, tv.Z));
                    mainPosition = Position.Create(tp.X, tp.Y, tp.Z);
                    mainOrientation = Orientation.Create("Z is " + zDir);                    
                }
                else if (positive.Type == IfcElementInformation.GeometryType.Cylinder)
                {
                    var normal = new XbimVector3D(parameters[2], parameters[3], parameters[4]);
                    var point = new XbimPoint3D(parameters[5], parameters[6], parameters[7]);

                    var tp = TransformPoint(item, point);
                    mainPosition = Position.Create(tp.X, tp.Y, tp.Z);
                    var tv = TransformVector(item, normal);
                    var plane = Plane.Create(Direction.Create(Position.Create(tv.X, tv.Y, tv.Z)), mainPosition);
                    mainOrientation = plane.Orientation;
                }
            }

            SetPdmsAttribute(item, DbAttributeInstance.POS, mainPosition);
            SetPdmsAttribute(item, DbAttributeInstance.ORI, mainOrientation);     

            var geometryItemType = representation.Positive.Type == IfcElementInformation.GeometryType.Mesh ? DbElementTypeInstance.TMPLATE : DbElementTypeInstance.PLOOP;
            if (geometryItemType == DbElementTypeInstance.TMPLATE)
            {
                var tmpl = DbElement.GetElement();
                foreach (var memb in item.Members())
                {
                    if (memb.GetElementType().Equals(DbElementTypeInstance.CMPFITTING)) continue;
                    if (memb.GetElementType().Equals(DbElementTypeInstance.TMPLATE) && tmpl.IsNull)
                    {
                        tmpl = memb;
                            continue;
                    }
                    memb.Delete();
                }

                if (tmpl.IsNull)
                    tmpl = item.CreateFirst(DbElementTypeInstance.TMPLATE);

                foreach (var memb in tmpl.Members())
                {
                    if (memb.GetElementType().Equals(DbElementTypeInstance.GENPRI)) continue;
                    memb.Delete();
                }
                var genpris = tmpl.Members(DbElementTypeInstance.GENPRI);
                for (var i = 1; i < genpris.Length; i++)
                {
                    var memb = genpris[i];
                    memb.Delete();
                    if (!memb.IsDeleted) return false;
                }
                var genpri =genpris.Length>0?genpris[0]:tmpl.CreateFirst(DbElementTypeInstance.GENPRI);

                var eldatas = GetDataFromBytes((byte[])data);
                var archiv = genpri.Members(DbElementTypeInstance.ARCHIV).Length > 0 ? genpri.Members(DbElementTypeInstance.ARCHIV).First() : genpri.CreateFirst(DbElementTypeInstance.ARCHIV);
                var attdatList = archiv.Members(DbElementTypeInstance.ATTDAT).ToList();
                if (eldatas.Count < attdatList.Count)
                {
                    var diff = attdatList.Count - eldatas.Count;
                    for (var i = 0; i < diff; i++)
                    {
                        var attdat = attdatList.Last();
                        attdat.Delete();
                        attdatList.Remove(attdat);
                    }
                }

                for (var i = 0; i < eldatas.Count;i++ )
                {
                    var eldata = eldatas[i];
                    var attdat = attdatList.Count < i + 1
                        ? archiv.CreateLast(DbElementTypeInstance.ATTDAT)
                        : attdatList[i];
                    attdat.SetAttribute(DbAttributeInstance.ELDATA, eldata);
                    attdat.SetAttribute(DbAttributeInstance.QALLSI, eldata.Length);
                }

                if (item.GetAsString(DbAttributeInstance.DESC).Contains("Витраж"))
                    SetPdmsAttribute(genpri, DbAttributeInstance.LEVE,new[]{9,10});
            }
            else if (geometryItemType == DbElementTypeInstance.PLOOP)
            {
                foreach (var tmpl in item.Members(DbElementTypeInstance.TMPLATE))
                {
                    tmpl.Delete();
                    if (!tmpl.IsDeleted) return false;
                }

                var ploop = item.Members(geometryItemType).Length > 0 ? item.Members(geometryItemType).First() : item.CreateFirst(geometryItemType);
                var parameters = (double[])data;

                var points = new List<XbimPoint3D>();
                for (var i = 4; i < parameters.Length; i += 3)
                {                    
                    points.Add(new XbimPoint3D(parameters[i], parameters[i+1], parameters[i+2]));
                }

                SetPdmsAttribute(ploop, DbAttributeInstance.HEIG, parameters[0] > 0 ? parameters[0] : 1);

                var paveList = ploop.Members(DbElementTypeInstance.PAVERT).ToList();
                if (points.Count < paveList.Count)
                {
                    var diff = paveList.Count - points.Count;
                    for (var i = 0; i < diff; i++)
                    {
                        var pave = paveList.Last();
                        pave.Delete();
                        paveList.Remove(pave);
                    }
                }

                for (var i = 0; i < points.Count; i++)
                {
                    var point = points[i];
                    var pave = ploop.Members(DbElementTypeInstance.PAVERT).Length < i + 1
                        ? ploop.CreateLast(DbElementTypeInstance.PAVERT)
                        : ploop.Members(DbElementTypeInstance.PAVERT)[i];

                    var tp = TransformPoint(pave, point);
                    var pos = Position.Create(tp.X, tp.Y, tp.Z);
                    SetPdmsAttribute(pave, DbAttributeInstance.POS, pos);
                }                

                var negativExtrCount = representation.Negativ.Count(x => x.Type == IfcElementInformation.GeometryType.Extrusion);
                var negativCyliCount = representation.Negativ.Count(x => x.Type == IfcElementInformation.GeometryType.Cylinder);

                var nxtrList = item.Members(DbElementTypeInstance.NXTRUSION).ToList();
                var ncylList = item.Members(DbElementTypeInstance.NCYLINDER).ToList();

                if (negativExtrCount < nxtrList.Count)
                {
                    var diff = nxtrList.Count - negativExtrCount;
                    for (var i = 0; i < diff; i++)
                    {
                        var nxtr = nxtrList.Last();
                        nxtr.Delete();
                        nxtrList.Remove(nxtr);
                    }
                }

                if (negativCyliCount < ncylList.Count)
                {
                    var diff = ncylList.Count - negativCyliCount;
                    for (var i = 0; i < diff; i++)
                    {
                        var ncyl = ncylList.Last();
                        ncyl.Delete();
                        ncylList.Remove(ncyl);
                    }
                }

                var nxtrIndex = 0;
                var ncylIndex = 0;
                foreach (var negativ in representation.Negativ)
                {
                    var nparameters = (double[])GeometryDataFromString(negativ.Data,negativ.Type);
                    if (negativ.Type == IfcElementInformation.GeometryType.Extrusion)
                    {
                        var primitive = nxtrList.Count < nxtrIndex + 1 ? item.CreateLast(DbElementTypeInstance.NXTRUSION) : nxtrList[nxtrIndex];
                        
                        var ndepth = nparameters[0];
                        var nnormal = new XbimVector3D(nparameters[1], nparameters[2], nparameters[3]);
                        var npoints = new List<XbimPoint3D>();
                        for (var i = 4; i < nparameters.Length; i += 3)
                        {
                            npoints.Add(new XbimPoint3D(nparameters[i], nparameters[i + 1], nparameters[i + 2]));
                        }

                        var firstPoint = npoints.First();

                        var ntp = TransformPoint(primitive, firstPoint);
                        var nposition = Position.Create(ntp.X, ntp.Y, ntp.Z);

                        var ntv = TransformVector(primitive, nnormal);
                        var zDir = Direction.Create(Position.Create(ntv.X, ntv.Y, ntv.Z));
                        var norientation = Orientation.Create("Z is " + zDir);
                        SetPdmsAttribute(primitive, DbAttributeInstance.POS, nposition);
                        SetPdmsAttribute(primitive, DbAttributeInstance.ORI, norientation);
                        SetPdmsAttribute(primitive, DbAttributeInstance.HEIG, ndepth > 0 ? ndepth : 1);

                        var loop = primitive.Members(DbElementTypeInstance.LOOP).Length > 0
                            ? primitive.Members(DbElementTypeInstance.LOOP).First()
                            : primitive.CreateLast(DbElementTypeInstance.LOOP);

                        var vertexList = loop.Members(DbElementTypeInstance.VERTEX).ToList();
                        if (npoints.Count < vertexList.Count)
                        {
                            var diff = vertexList.Count - npoints.Count;
                            for (var i = 0; i < diff; i++)
                            {
                                var vertex = vertexList.Last();
                                vertex.Delete();
                                vertexList.Remove(vertex);
                            }
                        }

                        for (var i = 0; i < npoints.Count; i++)
                        {
                            var point = npoints[i];
                            var vertex = loop.Members(DbElementTypeInstance.VERTEX).Length < i + 1
                                ? loop.CreateLast(DbElementTypeInstance.VERTEX)
                                : loop.Members(DbElementTypeInstance.VERTEX)[i];

                            var tp = TransformPoint(vertex, point);
                            var pos = Position.Create(tp.X, tp.Y, tp.Z);
                            SetPdmsAttribute(vertex, DbAttributeInstance.POS, pos);
                        }
                        
                        nxtrIndex++;
                    }
                    else if (negativ.Type == IfcElementInformation.GeometryType.Cylinder)
                    {
                        var primitive = ncylList.Count < ncylIndex + 1 ? item.CreateLast(DbElementTypeInstance.NCYLINDER) : ncylList[ncylIndex];

                        var height = nparameters[0];
                        var radius = nparameters[1];
                        var normal = new XbimVector3D(nparameters[2],nparameters[3],nparameters[4]);
                        var position = new XbimPoint3D(nparameters[5], nparameters[6], nparameters[7]);
                        var ntp = TransformPoint(primitive, position);
                        var nposition = Position.Create(ntp.X, ntp.Y, ntp.Z);

                        var ntv = TransformVector(primitive, normal);
                        var zDir = Direction.Create(Position.Create(ntv.X, ntv.Y, ntv.Z));
                        var norientation = Orientation.Create("Z is " + zDir);

                        var matrix = new XbimMatrix3D(norientation.XDir().East, norientation.XDir().North, norientation.XDir().Up, 0, norientation.YDir().East,
                            norientation.YDir().North, norientation.YDir().Up, 0, norientation.ZDir().East, norientation.ZDir().North, norientation.ZDir().Up, 0, 0,
                            0, 0, 1);

                        var offsetPoint = new XbimPoint3D(0, 0, height / 2);
                        offsetPoint = matrix.Transform(offsetPoint);
                        nposition.MoveBy(offsetPoint.X, offsetPoint.Y, offsetPoint.Z);

                        SetPdmsAttribute(primitive, DbAttributeInstance.POS, nposition);
                        SetPdmsAttribute(primitive, DbAttributeInstance.ORI, norientation);
                        SetPdmsAttribute(primitive, DbAttributeInstance.HEIG, height);
                        SetPdmsAttribute(primitive, DbAttributeInstance.DIAM, radius * 2);
                        ncylIndex++;
                    }
                }
            }
            
            return true;
        }

        private static bool ModifySecondaryGeometry(DbElement sbfi,
            IfcElementInformation.ItemRepresentation representation)
        {
            if (representation.Positive.Type != IfcElementInformation.GeometryType.Mesh) return false;

            var mainPosition = Position.Create();
            var mainOrientation = Orientation.Create();
            var positive = representation.Positive;
            var data = GeometryDataFromString(positive.Data, positive.Type);


            var bytes = (byte[]) data;
            var point = GetPositionFromBytes(bytes);
            var tp = TransformPoint(sbfi, point);
            mainPosition = Position.Create(tp.X, tp.Y, tp.Z);
            mainOrientation = Orientation.Create("Z is U");

            var tmpl = DbElement.GetElement();
            foreach (var memb in sbfi.Members())
            {
                if (memb.GetElementType().Equals(DbElementTypeInstance.TMPLATE) && tmpl.IsNull)
                {
                    tmpl = memb;
                    continue;
                }
                memb.Delete();
            }

            if (tmpl.IsNull)
                tmpl = sbfi.CreateFirst(DbElementTypeInstance.TMPLATE);
            
            var genpri = DbElement.GetElement();
            foreach (var memb in tmpl.Members())
            {
                if (memb.GetElementType().Equals(DbElementTypeInstance.GENPRI) && genpri.IsNull)
                {
                    genpri = memb;
                    continue;
                }
                memb.Delete();
            }

            if (genpri.IsNull)
                genpri = tmpl.CreateFirst(DbElementTypeInstance.GENPRI);

            SetPdmsAttribute(genpri, DbAttributeInstance.POS, mainPosition);
            SetPdmsAttribute(genpri, DbAttributeInstance.ORI, mainOrientation);
            SetPdmsAttribute(genpri, DbAttributeInstance.LEVE, new [] {9, 10});

            var eldatas = GetDataFromBytes((byte[]) data);
            var archiv = genpri.Members(DbElementTypeInstance.ARCHIV).Length > 0
                ? genpri.Members(DbElementTypeInstance.ARCHIV).First()
                : genpri.CreateFirst(DbElementTypeInstance.ARCHIV);
            var attdatList = archiv.Members(DbElementTypeInstance.ATTDAT).ToList();
            if (eldatas.Count < attdatList.Count)
            {
                var diff = attdatList.Count - eldatas.Count;
                for (var i = 0; i < diff; i++)
                {
                    var attdat = attdatList.Last();
                    attdat.Delete();
                    attdatList.Remove(attdat);
                }
            }

            for (var i = 0; i < eldatas.Count; i++)
            {
                var eldata = eldatas[i];
                var attdat = attdatList.Count < i + 1
                    ? archiv.CreateLast(DbElementTypeInstance.ATTDAT)
                    : attdatList[i];
                attdat.SetAttribute(DbAttributeInstance.ELDATA, eldata);
            }

            return true;
        }

        internal static XbimPoint3D GetPositionFromBytes(byte[] bytes)
        {
            var localBytes = new byte[24];
            Array.Copy(bytes, 276, localBytes, 0, localBytes.Length);
            var pos = new XbimPoint3D();
            using (var ms = new MemoryStream(localBytes))
            {
                using (var br = new BinaryReader(ms))
                {
                    var x = br.ReadDouble();
                    var y = br.ReadDouble();
                    var z = br.ReadDouble();
                    pos = new XbimPoint3D(x, y, z);
                }
            }
            return pos;
        }

        internal static object GeometryDataFromString(string data, IfcElementInformation.GeometryType type)
        {
            var bytes = default(byte[]);
            var compressedBytes = Convert.FromBase64String(data);
            using (var mw = new MemoryStream())
            {
                using (var mr = new MemoryStream(compressedBytes))
                using (var zs = new ZlibStream(mr, CompressionMode.Decompress, true))
                {
                    //zs.CopyTo(mw);    
                    CopyStream(zs, mw);
                }

                bytes = mw.ToArray();
            }

            if (type == IfcElementInformation.GeometryType.Mesh)
            {
                return bytes;
            }

            var doubleList = new List<double>();

            using (var ms = new MemoryStream(bytes))
            {
                using (var br = new BinaryReader(ms))
                {
                    while (br.BaseStream.Position < br.BaseStream.Length)
                    {
                        doubleList.Add(br.ReadDouble());
                    }
                }
            }
            return doubleList.ToArray();
        }

        private static void CopyStream(Stream input, Stream output)
        {
            byte[] buffer = new byte[1024];
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                output.Write(buffer, 0, read);
            }
        }

        internal static List<int[]> GetDataFromBytes(byte[] bytes)
        {
            var result = new List<int[]>();

            var tmpList = new List<int>();
            using (var ms = new MemoryStream(bytes))
            {
                using (var br = new BinaryReader(ms))
                {
                    while (br.BaseStream.Position < br.BaseStream.Length)
                    {
                        tmpList.Add(br.ReadInt32());
                        if (tmpList.Count == 4096)
                        {
                            result.Add(tmpList.ToArray());
                            tmpList = new List<int>();
                        }
                    }
                }
            }

            if (tmpList.Count > 0)
            {
                result.Add(tmpList.ToArray());
            }
            return result;
        }

        private static bool GetRootFrmw(string[] properties, IfcElementInformation.ElementType type, ref DbElement root)
        {
            string rootName;

            if (type == IfcElementInformation.ElementType.Wall)
            {
                var func = string.Empty;
                foreach (var prop in properties)
                {
                    var data = prop.Split(new[] { "#" }, StringSplitOptions.RemoveEmptyEntries);
                    if (!data[0].Equals("function")) continue;
                    func = data.Length > 1 ? data[1] : "N";
                    break;
                }
                rootName = "ST_" + func;
            }
            else
            {
                var elev = 0.0;
                foreach (var prop in properties)
                {
                    var data = prop.Split(new[] { "#" }, StringSplitOptions.RemoveEmptyEntries);
                    if (!data[0].Equals("elevation")) continue;
                    elev = data.Length > 1 ? double.Parse(data[1]) : 0.0;
                    break;
                }
                rootName = "PL_" + (elev / 1000).ToString("0.000").Replace(",", ".");
            }

            return !String.IsNullOrEmpty(rootName) && GetRootByName(rootName, _arZone, ref root);
        }

        /// <summary>
        /// Получение элемента по его имени
        /// </summary>
        /// <param name="rootName">имя элемента</param>
        /// <param name="zone">элемент ZONE, внутри которого происходит поиск</param>
        /// <param name="root">элемент с заданным именем</param>
        /// <returns>true - если элемент найден</returns>
        private static bool GetRootByName(string rootName, DbElement zone, ref DbElement root)
        {
            var nameList = rootName.Split('_');
            var owner = zone;
            for (var i = 0; i < nameList.Length; i++)
            {
                var name = nameList[i];
                DbElementType type;
                switch (i)
                {
                    case 0:
                        type = DbElementTypeInstance.STRUCTURE;
                        break;
                    case 1:
                        type = DbElementTypeInstance.FRMWORK;
                        break;
                    default:
                        type = null;
                        break;
                }

                if (type == null)
                {
                    return false;
                }

                var collection = new DBElementCollection(owner, new TypeFilter(type));
                var searchFlag = false;
                foreach (DbElement item in collection)
                {
                    if (!item.GetAsString(DbAttributeInstance.NAME).EndsWith("_" + name)) continue;
                    owner = item;
                    searchFlag = true;
                    break;
                }
                if (searchFlag) continue;

                owner = CreateStructureElement(name, owner, type);

                if (owner.IsNull)
                {
                    return false;
                }
            }

            root = owner;
            return true;
        }

        /// <summary>
        /// Создание структурного элемента с указанным именем
        /// </summary>
        /// <param name="name">имя элемента</param>
        /// <param name="owner">элемент, внутри которого создается новый элемент</param>
        /// <param name="type">тип создаваемого элемента</param>
        /// <returns>созданный элемент</returns>
        private static DbElement CreateStructureElement(string name, DbElement owner, DbElementType type)
        {
            if (!owner.GetBool(DbAttributeInstance.LOCK) && owner.IsCreatable(type))
            {
                var newItem = owner.CreateLast(type);
                var newName = owner.GetAsString(DbAttributeInstance.FLNM) + "_" + name;
                if (!DbElement.GetElement(newName).IsNull) newName = "/1" + owner.GetAsString(DbAttributeInstance.FLNN) + "_" + name;
                newItem.SetAttribute(DbAttributeInstance.NAME, newName);
                newItem.SetAttribute(DbAttributeInstance.DESC, GetDescription(name, type));
                return newItem;
            }

            return DbElement.GetElement();
        }

        //private static bool ModifyGeometry(DbElement root, ref DbElement pdmsItem, DbElementType itemType,
        //    IfcElementInformation.ItemRepresentation representation, bool flag,)
        //{
        //    if (!itemType.Equals(DbElementTypeInstance.SBFITTING))
        //    {
        //        if (pdmsItem.IsNull)
        //        {
        //            pdmsItem = root.CreateLast(itemType);
        //        }
        //        else if (!pdmsItem.GetElementType().Equals(itemType))
        //        {
        //            pdmsItem.Delete();
        //            if (!pdmsItem.IsDeleted) return false;
        //            pdmsItem = root.CreateLast(itemType);
        //        }
        //        else if (!pdmsItem.Owner.Equals(root) && !MoveItemToNewClass(pdmsItem, root))
        //        {
        //            return false;
        //        }

        //        var firstPositive = representations.First().Positive;
        //        var mainPosition = Position.Create();
        //        var mainOrientation = Orientation.Create();
        //        if (firstPositive is Circle)
        //        {
        //            var circle = firstPositive as Circle;
        //            var tp = TransformPoint(pdmsItem, circle.Position);
        //            mainPosition = Position.Create(tp.X, tp.Y, tp.Z);

        //            var tv = TransformVector(pdmsItem, circle.Normal);
        //            var plane = Plane.Create(Direction.Create(Position.Create(tv.X, tv.Y, tv.Z)), mainPosition);
        //            mainOrientation = plane.Orientation;
        //        }
        //        else
        //        {
        //            var extr = firstPositive as Extrusion;
        //            var firstPoint = extr.Points.First();

        //            var tp = TransformPoint(pdmsItem, firstPoint);
        //            mainPosition = Position.Create(tp.X, tp.Y, tp.Z);

        //            var tv = TransformVector(pdmsItem, extr.Normal);
        //            var zDir = Direction.Create(Position.Create(tv.X, tv.Y, tv.Z));
        //            mainOrientation = Orientation.Create("Z is " + zDir);
        //        }

        //        SetPdmsAttribute(pdmsItem, DbAttributeInstance.POS, mainPosition);
        //        SetPdmsAttribute(pdmsItem, DbAttributeInstance.ORI, mainOrientation);
        //    }

        //    var elementForGeometry = pdmsItem;
        //    if (reprType == RepresentationType.Brep || itemType.Equals(DbElementTypeInstance.SBFITTING) ||
        //        representations.Count(repr => repr.Positive != null) > 1 ||
        //        representations.Any(repr => repr.Positive is Circle))
        //    {
        //        elementForGeometry = pdmsItem.Members(DbElementTypeInstance.TMPLATE).Any()
        //            ? pdmsItem.Members(DbElementTypeInstance.TMPLATE).First()
        //            : pdmsItem.CreateLast(DbElementTypeInstance.TMPLATE);
        //    }

        //    //в случае изменения типа геометриеского представления, необходимо удалить предыдущую геометрию
        //    if (elementForGeometry.GetElementType().Equals(DbElementTypeInstance.TMPLATE))
        //    {
        //        var plooMembers = pdmsItem.Members(DbElementTypeInstance.PLOOP);
        //        foreach (var ploo in plooMembers)
        //        {
        //            ploo.Delete();
        //            if (!ploo.IsDeleted) return false;
        //        }
        //    }
        //    else if (pdmsItem.Members(DbElementTypeInstance.TMPLATE).Length > 0)
        //    {
        //        var tmplMembers = pdmsItem.Members(DbElementTypeInstance.TMPLATE);
        //        foreach (var tmpl in tmplMembers)
        //        {
        //            tmpl.Delete();
        //            if (!tmpl.IsDeleted) return false;
        //        }
        //    }

        //    var positivExtrCount = representations.Count(x=>x.Positive is Extrusion);
        //    var positivCyliCount = representations.Count(x => x.Positive is Circle);

        //    var extrList = elementForGeometry.Members(DbElementTypeInstance.EXTRUSION).ToList();
        //    var cyliList = elementForGeometry.Members(DbElementTypeInstance.CYLINDER).ToList();

        //    if (positivExtrCount < extrList.Count)
        //    {
        //        var diff = extrList.Count - positivExtrCount;
        //        for (var i = 0; i < diff; i++)
        //        {
        //            var extr = extrList.Last();
        //            extr.Delete();
        //            extrList.Remove(extr);
        //        }
        //    }

        //    if (positivCyliCount < cyliList.Count)
        //    {
        //        var diff = cyliList.Count - positivCyliCount;
        //        for (var i = 0; i < diff; i++)
        //        {
        //            var cyli = cyliList.Last();
        //            cyli.Delete();
        //            cyliList.Remove(cyli);
        //        }
        //    }

        //    var extrIndex = 0;
        //    var cyliIndex = 0;

        //    foreach (var geo in representations)
        //    {
        //        var modified = false;
        //        if (geo.Positive is Extrusion)
        //        {
        //            modified = ModifyGeometry(elementForGeometry, geo,extrIndex);
        //            extrIndex++;
        //        }
        //        else if (geo.Positive is Circle)
        //        {
        //            modified = ModifyGeometry(elementForGeometry, geo, cyliIndex);
        //            cyliIndex++;
        //        }

        //        if (!modified) return false;
        //    }

        //    return true;
        //}

        //private static bool ModifyGeometry(DbElement mainItem, Representation representation,int primitiveIndex=0)
        //{
        //    var result = false;

        //    var positiveGeometry = representation.Positive;
        //    if (positiveGeometry != null)
        //    {
        //        var posElement = ModifyGeometry(mainItem, positiveGeometry, true,primitiveIndex);

        //        if (representation.Negativ.Count > 0)
        //        {
        //            var negativExtrCount = representation.Negativ.Count(x => x is Extrusion);
        //            var negativCyliCount = representation.Negativ.Count(x => x is Circle);

        //            var nxtrList = posElement.Members(DbElementTypeInstance.NXTRUSION).ToList();
        //            var ncylList = posElement.Members(DbElementTypeInstance.NCYLINDER).ToList();

        //            if (negativExtrCount < nxtrList.Count)
        //            {
        //                var diff = nxtrList.Count - negativExtrCount;
        //                for (var i = 0; i < diff; i++)
        //                {
        //                    var nxtr = nxtrList.Last();
        //                    nxtr.Delete();
        //                    nxtrList.Remove(nxtr);
        //                }
        //            }

        //            if (negativCyliCount < ncylList.Count)
        //            {
        //                var diff = ncylList.Count - negativCyliCount;
        //                for (var i = 0; i < diff; i++)
        //                {
        //                    var ncyl = ncylList.Last();
        //                    ncyl.Delete();
        //                    ncylList.Remove(ncyl);
        //                }
        //            }

        //            var nxtrIndex = 0;
        //            var ncylIndex = 0;
        //            foreach (var negativGeometry in representation.Negativ)
        //            {
        //                if (negativGeometry is Extrusion)
        //                {
        //                    ModifyGeometry(posElement, negativGeometry, false, nxtrIndex);
        //                    nxtrIndex++;
        //                }
        //                else if (negativGeometry is Circle)
        //                {
        //                    ModifyGeometry(posElement, negativGeometry, false, ncylIndex);
        //                    ncylIndex++;
        //                }
        //            }
        //        }
        //    }
        //    else
        //    {
        //        var itemsForNegativ = mainItem.GetElementType().Equals(DbElementTypeInstance.TMPLATE)
        //            ? mainItem.Members().ToList()
        //            : new List<DbElement> {mainItem};
        //        foreach (var item in itemsForNegativ)
        //        {
        //            var negativExtrCount = representation.Negativ.Count(x => x is Extrusion);
        //            var negativCyliCount = representation.Negativ.Count(x => x is Circle);

        //            var nxtrList = item.Members(DbElementTypeInstance.NXTRUSION).ToList();
        //            var ncylList = item.Members(DbElementTypeInstance.NCYLINDER).ToList();

        //            if (negativExtrCount < nxtrList.Count)
        //            {
        //                var diff = nxtrList.Count - negativExtrCount;
        //                for (var i = 0; i < diff; i++)
        //                {
        //                    var nxtr = nxtrList.Last();
        //                    nxtr.Delete();
        //                    nxtrList.Remove(nxtr);
        //                }
        //            }

        //            if (negativCyliCount < ncylList.Count)
        //            {
        //                var diff = ncylList.Count - negativCyliCount;
        //                for (var i = 0; i < diff; i++)
        //                {
        //                    var ncyl = ncylList.Last();
        //                    ncyl.Delete();
        //                    ncylList.Remove(ncyl);
        //                }
        //            }

        //            var nxtrIndex = 0;
        //            var ncylIndex = 0;
        //            foreach (var negativGeometry in representation.Negativ)
        //            {
        //                if (negativGeometry is Extrusion)
        //                {
        //                    ModifyGeometry(item, negativGeometry, false, nxtrIndex);
        //                    nxtrIndex++;
        //                }
        //                else if (negativGeometry is Circle)
        //                {
        //                    ModifyGeometry(item, negativGeometry, false, ncylIndex);
        //                    ncylIndex++;
        //                }
        //            }
        //        }
        //    }
        //    return true;
        //}

        //private static DbElement ModifyGeometry(DbElement mainItem, Geometry geo, bool positive, int primitiveIndex = 0)
        //{
        //    var result = DbElement.GetElement();
        //    if (!mainItem.GetElementType().Equals(DbElementTypeInstance.TMPLATE) && positive)
        //    {
        //        var ploo = mainItem.Members(DbElementTypeInstance.PLOOP).Length > 0
        //            ? mainItem.Members(DbElementTypeInstance.PLOOP).First()
        //            : mainItem.CreateLast(DbElementTypeInstance.PLOOP);
        //        if (geo is Extrusion)
        //        {
        //            var extr = geo as Extrusion;
        //            SetPdmsAttribute(ploo, DbAttributeInstance.HEIG, extr.Length > 0 ? extr.Length : 1);

        //            var paveList = ploo.Members(DbElementTypeInstance.PAVERT).ToList();
        //            if (extr.Points.Count < paveList.Count)
        //            {
        //                var diff = paveList.Count - extr.Points.Count;
        //                for (var i = 0; i < diff; i++)
        //                {
        //                    var pave = paveList.Last();
        //                    pave.Delete();
        //                    paveList.Remove(pave);
        //                }
        //            }

        //            for (var i = 0; i < extr.Points.Count; i++)
        //            {
        //                var point = extr.Points[i];
        //                var pave = ploo.Members(DbElementTypeInstance.PAVERT).Length < i + 1
        //                    ? ploo.CreateLast(DbElementTypeInstance.PAVERT)
        //                    : ploo.Members(DbElementTypeInstance.PAVERT)[i];

        //                var tp = TransformPoint(pave, point);
        //                var pos = Position.Create(tp.X, tp.Y, tp.Z);
        //                SetPdmsAttribute(pave, DbAttributeInstance.POS, pos);
        //            }

        //            return mainItem;
        //        }
        //        return result;
        //    }

        //    if (geo is Extrusion)
        //    {
        //        var extr = geo as Extrusion;
        //        var primitiveType = positive ? DbElementTypeInstance.EXTRUSION : DbElementTypeInstance.NXTRUSION;
        //        var mainItemMembers = mainItem.Members(primitiveType);

        //        var primitive = mainItemMembers.Length < primitiveIndex + 1 ? mainItem.CreateLast(primitiveType) : mainItemMembers[primitiveIndex];

        //        var firstPoint = extr.Points.First();

        //        var tPos = TransformPoint(primitive, firstPoint);
        //        var mainPosition = Position.Create(tPos.X, tPos.Y, tPos.Z);

        //        var tVect = TransformVector(primitive, extr.Normal);
        //        var zDir = Direction.Create(Position.Create(tVect.X, tVect.Y, tVect.Z));
        //        var mainOrientation = Orientation.Create("Z is " + zDir);
        //        SetPdmsAttribute(primitive, DbAttributeInstance.POS, mainPosition);
        //        SetPdmsAttribute(primitive, DbAttributeInstance.ORI, mainOrientation);
        //        SetPdmsAttribute(primitive, DbAttributeInstance.HEIG, extr.Length > 0 ? extr.Length : 1);

        //        var loop = primitive.Members(DbElementTypeInstance.LOOP).Length > 0
        //            ? primitive.Members(DbElementTypeInstance.LOOP).First()
        //            : primitive.CreateLast(DbElementTypeInstance.LOOP);

        //        var vertexList = loop.Members(DbElementTypeInstance.VERTEX).ToList();
        //        if (extr.Points.Count < vertexList.Count)
        //        {
        //            var diff = vertexList.Count - extr.Points.Count;
        //            for (var i = 0; i < diff; i++)
        //            {
        //                var vertex = vertexList.Last();
        //                vertex.Delete();
        //                vertexList.Remove(vertex);
        //            }
        //        }

        //        for (var i = 0; i < extr.Points.Count; i++)
        //        {
        //            var point = extr.Points[i];
        //            var vertex = loop.Members(DbElementTypeInstance.VERTEX).Length < i + 1
        //                ? loop.CreateLast(DbElementTypeInstance.VERTEX)
        //                : loop.Members(DbElementTypeInstance.VERTEX)[i];

        //            var tp = TransformPoint(vertex, point);
        //            var pos = Position.Create(tp.X, tp.Y, tp.Z);
        //            SetPdmsAttribute(vertex, DbAttributeInstance.POS, pos);
        //        }
        //        return positive?primitive:mainItem;
        //    }

        //    if (geo is Circle)
        //    {
        //        var circle = geo as Circle;
        //        var primitiveType = positive ? DbElementTypeInstance.CYLINDER : DbElementTypeInstance.NCYLINDER;
        //        var mainItemMembers = mainItem.Members(primitiveType);

        //        var primitive = mainItemMembers.Length < primitiveIndex + 1 ? mainItem.CreateLast(primitiveType) : mainItemMembers[primitiveIndex];

        //        var location = TransformPoint(primitive, circle.Position);
        //        var mainPosition = Position.Create(location.X, location.Y, location.Z);

        //        var tVect = TransformVector(primitive, circle.Normal);
        //        var zDir = Direction.Create(Position.Create(tVect.X, tVect.Y, tVect.Z));
        //        var mainOrientation = Orientation.Create("Z is " + zDir);

        //        var matrix = new XbimMatrix3D(mainOrientation.XDir().East, mainOrientation.XDir().North, mainOrientation.XDir().Up, 0, mainOrientation.YDir().East,
        //            mainOrientation.YDir().North, mainOrientation.YDir().Up, 0, mainOrientation.ZDir().East, mainOrientation.ZDir().North, mainOrientation.ZDir().Up, 0, 0,
        //            0,0, 1);


        //        var offsetPoint = new XbimPoint3D(0, 0, circle.Length/2);
        //        offsetPoint = matrix.Transform(offsetPoint);
        //        mainPosition.MoveBy(offsetPoint.X,offsetPoint.Y,offsetPoint.Z);

        //        SetPdmsAttribute(primitive, DbAttributeInstance.POS, mainPosition);
        //        SetPdmsAttribute(primitive, DbAttributeInstance.ORI, mainOrientation);
        //        SetPdmsAttribute(primitive, DbAttributeInstance.HEIG, circle.Length);
        //        SetPdmsAttribute(primitive, DbAttributeInstance.DIAM, circle.Radius*2);

        //        return positive ? primitive : mainItem;
        //    }
        //    return result;
        //}

        internal static XbimPoint3D TransformPoint(DbElement primitive, XbimPoint3D point)
        {
            var owner = primitive;
            var matrixs = new List<XbimMatrix3D>();

            while (!owner.GetElementType().Equals(DbElementTypeInstance.FRMWORK) && !owner.GetElementType().Equals(DbElementTypeInstance.ZONE))
            {
                owner = owner.Owner;
                if (!owner.IsAttributeValid(DbAttributeInstance.POS) || !owner.IsAttributeValid(DbAttributeInstance.ORI))
                    continue;
                var pos = owner.GetPosition(DbAttributeInstance.POS);
                var ori = owner.GetOrientation(DbAttributeInstance.ORI);
                var matrix = new XbimMatrix3D(ori.XDir().East, ori.XDir().North, ori.XDir().Up, 0, ori.YDir().East,
                    ori.YDir().North, ori.YDir().Up, 0, ori.ZDir().East, ori.ZDir().North, ori.ZDir().Up, 0, pos.X,
                    pos.Y, pos.Z, 1);
                matrix.Invert();
                matrixs.Add(matrix);
            }
            matrixs.Reverse();
            foreach (var matrix in matrixs)
            {
                point = matrix.Transform(point);
            }
            return point;
        }

        private static XbimVector3D TransformVector(DbElement primitive, XbimVector3D vector)
        {
            var owner = primitive;
            var matrixs = new List<XbimMatrix3D>();
            while (!owner.GetElementType().Equals(DbElementTypeInstance.FRMWORK))
            {
                owner = owner.Owner;
                if (!owner.IsAttributeValid(DbAttributeInstance.POS) || !owner.IsAttributeValid(DbAttributeInstance.ORI))
                    continue;
                var pos = owner.GetPosition(DbAttributeInstance.POS);
                var ori = owner.GetOrientation(DbAttributeInstance.ORI);
                var matrix = new XbimMatrix3D(ori.XDir().East, ori.XDir().North, ori.XDir().Up, 0, ori.YDir().East,
                    ori.YDir().North, ori.YDir().Up, 0, ori.ZDir().East, ori.ZDir().North, ori.ZDir().Up, 0, pos.X,
                    pos.Y, pos.Z, 1);
                matrix.Invert();
                matrixs.Add(matrix);
            }
            matrixs.Reverse();
            foreach (var matrix in matrixs)
            {
                vector = matrix.Transform(vector);
            }
            return vector;
        }

        /// <summary>
        /// Перемещение элемента в иерархии, если его владелец изменился
        /// </summary>
        /// <param name="item">элемент</param>
        /// <param name="root">новый владелец</param>
        /// <returns></returns>
        private static bool MoveItemToNewClass(DbElement item, DbElement root)
        {
            if (!item.IsInsertable(root)) return false;
            item.InsertAfterLast(root);
            return true;
        }
    }

    //public class RevitElement
    //{
    //    public string Id;
    //    public RepresentationType RepresentationType;
    //    public List<Representation> Representations;
    //    public List<RevitElement> FillElements;
    //    public RevitProperties Properties;
    //}

    //public enum RepresentationType
    //{
    //    Brep,
    //    Solid
    //}

    //public enum ElementType
    //{
    //    Wall,
    //    Door,
    //    Window,
    //    Roof,
    //    Slab,
    //    Beam
    //}

    //public class RevitProperties
    //{
    //    public double Elev;
    //    public string Func;
    //    public string Desc;
    //    public ElementType Type;
    //}

    //public class Representation
    //{
    //    public Geometry Positive;
    //    public List<Geometry> Negativ;
    //}

    //public class Geometry
    //{
    //    public XbimVector3D Normal;
    //    public double Length;
    //}

    //public class Circle : Geometry
    //{
    //    public double Radius;
    //    public XbimPoint3D Position;
    //}

    //public class Extrusion : Geometry
    //{
    //    public List<XbimPoint3D> Points;
    //}
}
