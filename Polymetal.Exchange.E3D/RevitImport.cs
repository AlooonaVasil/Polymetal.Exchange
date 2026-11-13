using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using Aveva.Core.Database;
using Aveva.Core.Geometry;
using Aveva.Core.Utilities.CommandLine;
using Aveva.Core.Database.Filters;
using Ionic.Zlib;
using Polymetal.Exchange.Utilities;
using Xbim.Common.Geometry;
using System.Text.RegularExpressions;
using Polymetal.Core.Common;
using Polymetal.Exchange.Utilities.Helpers;

namespace Polymetal.Exchange.E3D
{
    internal class RevitImport
    {
        private static DbElement _departmentSite = DbElement.GetElement();
        private static XmlSerializer _xmlFormat = new XmlSerializer(typeof(Utilities.DTO.IfcElementInformation));
        private static string _documentCode;
        private static string _fileDocumentMark;
        private static string _subObjectCode;
        private static bool _isContractorModel;
        private static string _contractorName;

        private static Dictionary<string, int> _requiredRoomAttributes;

        internal static Dictionary<string, int> RequiredRoomAttributes
        {
            get
            {
                if (_requiredRoomAttributes == null)
                {
                    _requiredRoomAttributes = Utilities.DAL.Database.GetRequiredRoomAttributes();
                }

                return _requiredRoomAttributes;
            }
        }


        internal static bool Run()
        {
            var histories = new List<ElementHistory>();
            var triggerUser = RunE3D.GetUserByTriggerId();

            Console.WriteLine("Начинается импорт данных");
            Command.CreateCommand("!!zoneCreateFlag = true").Run();

            var dt = Common.GetUpdatedRevitData(RunE3D.ProjectInfo.ProjectId);

            Console.WriteLine("Элементов для обработки: " + dt.Rows.Count + " шт.");

            var typeFilter = new TypeFilter(new[]
            {
                DbElementTypeInstance.GWALL, DbElementTypeInstance.FLOOR,
                DbElementTypeInstance.PANEL
            });

            var oldPercent = 0.0;
            var modifications = 0;

            var mdbName = MDB.CurrentMDB.Name.Replace("-", "");
            var file = new FileInfo(RunE3D.ProjectInfo.Program.Path);
            _fileDocumentMark = "AR";
            _documentCode = "07";
            if (file.Exists)
            {
                var matchSubObjectCode = new Regex(@"\d{2}\s\d{2}\s\d{2}\s\d{3}\s(\d{2})").Match(file.Name);
                _subObjectCode = matchSubObjectCode.Success ? matchSubObjectCode.Groups[1].Value : string.Empty;

                var matchDocumentCode = new Regex(@"\d{2}\s\d{2}\s(\d{2})\s").Match(file.Name);
                _documentCode = matchDocumentCode.Success ? matchDocumentCode.Groups[1].Value : string.Empty;

                if (string.IsNullOrEmpty(_subObjectCode))
                {
                    matchSubObjectCode = new Regex(@"\d{2}\s\d{2}\s\d{3}\s(\d{2})").Match(file.Directory.Name);
                    _subObjectCode = matchSubObjectCode.Success ? matchSubObjectCode.Groups[1].Value : mdbName.Substring(mdbName.Length - 2);
                }

                if (string.IsNullOrEmpty(_documentCode))
                {
                    matchDocumentCode = new Regex(@"\d{2}\s(\d{2})\s").Match(file.Directory.Name);
                    _documentCode = matchDocumentCode.Success ? matchDocumentCode.Groups[1].Value : "07";
                }

                var data = file.Name.Replace(Path.GetExtension(file.Name),string.Empty).Split('-');
                if (data.Length > 1)
                {
                    data = data[1].Trim().Split();
                    if (data[0].ToUpper().StartsWith("АР")) 
                        _fileDocumentMark = data[0].ToUpper().Replace("АР", "AR");
                }                
            }

            if (file.FullName.ToLower().Contains("подрядчики"))
            {
                _isContractorModel = true;
                _contractorName = file.Directory.Name;
            }
            else if (file.FullName.ToLower().Contains("\\гмо\\"))
            {
                _isContractorModel = true;
                _contractorName = "GMOKZ";
            }

            //var arZone = GetRootZone(mdbName);
            //if (arZone.IsNull || !arZone.IsValid) return false;

            for (var i = 0; i < dt.Rows.Count; i++)
            {
                var row = dt.Rows[i];
                var percent = Math.Ceiling((double)i * 100 / dt.Rows.Count);
                if (oldPercent < percent)
                {
                    Console.WriteLine("Current progress {0}%   ", Math.Ceiling(percent));
                    oldPercent = percent;
                }

                try
                {
                    Utilities.DTO.IfcElementInformation elementInformation;
                    using (var stream = new StringReader((string)row["element_info"]))
                    {
                        elementInformation = (Utilities.DTO.IfcElementInformation)_xmlFormat.Deserialize(stream);
                        stream.Close();
                    }

                    if (elementInformation.Type == Enumerators.ElementType.Room)
                    {
                         var updateResult = UpdateRoomData(elementInformation, row["element_status"].ToString());
                         if (!string.IsNullOrEmpty(updateResult))
                             row["element_status"] = updateResult;

                        continue;
                    }

                    var avevaItem = DbElement.GetElement(row["pdms_item"].ToString());
                    if (row["element_status"].ToString() == "DEL")
                    {
                        if (avevaItem.IsNull)
                        {
                            var andFilter = new AndFilter(typeFilter,
                                new AttributeStringFilter(Core.Common.UserAttributes.PolyRevitGuid,
                                    FilterOperator.Equals, (string)row["element_guid"]));

                            var itemCollection = new DBElementCollection(_departmentSite, andFilter).Cast<DbElement>().ToList();

                            if (itemCollection.Any())
                                avevaItem = itemCollection.First();
                        }

                        if (!avevaItem.IsNull)
                        {
                            var hist = new ElementHistory(avevaItem,
                                row["element_created_by"], row["element_created"],
                                row["element_modified_by"], row["element_modified"],
                                triggerUser, DateTime.Now);

                            if (avevaItem.IsDeleteable) 
                                avevaItem.Delete();

                            if (avevaItem.IsDeleted)
                            {
                                row["element_status"] = "REM";
                                histories.Add(hist);
                            }
                            else
                            {
                                RunE3D.SendMessage($"[Delete Revit] Не удалось удалить элемент {avevaItem.ElementType.ShortName} {avevaItem.GetAsString(DbAttributeInstance.REF)}");
                            }
                        }
                        else
                        {
                            row["element_status"] = "REM";
                        }
                    }
                    else
                    {
                        if (ModifyItem(elementInformation, ref avevaItem))
                        {
                            row["element_status"] = "UPD";

                            var hist = new ElementHistory(avevaItem,
                                row["element_created_by"], row["element_created"],
                                row["element_modified_by"], row["element_modified"],
                                string.Empty, DateTime.MinValue);

                            histories.Add(hist);
                        }
                        else
                        {
                            if (row["element_status"].Equals("CRE") && !avevaItem.IsNull)
                            {
                                avevaItem.Delete();
                            }

                            Console.WriteLine("Can't modify element " + elementInformation.UniqueId);
                        }
                    }

                    modifications++;

                    row["pdms_item"] = avevaItem.IsNull || avevaItem.IsDeleted
                        ? DBNull.Value.ToString()
                        : avevaItem.GetAsString(DbAttributeInstance.REF);
                }
                catch (Exception exc)
                {
                    RunE3D.SendMessage("[ModifyRevit] " + row["element_guid"] + " " + exc.Message);
                }
            }

            Helpers.Common.ClearEmptyElements(_departmentSite);

            RunE3D.SendMessage($"Изменений в модели: {modifications}");
            Console.WriteLine($"Заполнение данных по истории элементов ({histories.Count} записей)");
            RunE3D.UpdateHistory(histories, Common.Source.Revit);
            Console.WriteLine("История элементов обновлена");
            Common.SetUpdatedRevitData(dt, RunE3D.ProjectInfo.ProjectId);
            Console.WriteLine("Данные в SQL обновлены");

            return true;
        }

        private static string UpdateRoomData(Utilities.DTO.IfcElementInformation roomInformation, string status)
        {
            if (status == "DEL")
            {
                Utilities.DAL.Database.DeactivateRoom(roomInformation.UniqueId, RunE3D.ProjectInfo.Pdms.SubObjectId, RunE3D.ProjectInfo.ProjectId);

                return "REM";
            }
            else
            {
                var roomId = Utilities.DAL.Database.ActivateRoom(roomInformation.UniqueId, RunE3D.ProjectInfo.Pdms.SubObjectId, RunE3D.ProjectInfo.ProjectId);
                var propertyDictionary = new Dictionary<int, string>();
                foreach (var property in roomInformation.Properties)
                {
                    if (!RequiredRoomAttributes.ContainsKey(property.Name) || propertyDictionary.ContainsKey(RequiredRoomAttributes[property.Name]))
                        continue;

                    propertyDictionary.Add(RequiredRoomAttributes[property.Name], property.Value);
                }

                Utilities.DAL.Database.UpdateRoomProperties(roomId, propertyDictionary);

                return "UPD";
            }

            return null;
        }

        /// <summary>
        /// Получение элемента ZONE, внутри которого будет вестись работа
        /// </summary>
        /// <param name="mdbName">название текущей MDB</param>
        /// <param name="mark">название марки, по которой будет производиться поиск нужной ZONE</param>
        /// <returns>итоговый элемент</returns>
        private static DbElement GetRootZone(string documentMark)
        {
            var mdbName = MDB.CurrentMDB.Name.Replace("-", "");
            var zone = DbElement.GetElement();            
            var world = MDB.CurrentMDB.GetFirstWorld(DbType.Design);
            if (world.IsNull) return DbElement.GetElement();
            
            if (_departmentSite == null || _departmentSite.IsNull)
            {
                var siteCollection = new DBElementCollection(world, new TypeFilter(DbElementTypeInstance.SITE));
                foreach (DbElement item in siteCollection)
                {
                    var name = item.GetAsString(DbAttributeInstance.FLNN);
                    if (!_isContractorModel && !name.EndsWith("-SO") && !name.EndsWith("-OAR")) 
                        continue;

                    if (_isContractorModel && !name.EndsWith("-IMPORT")) 
                        continue;

                    var code = name.Split('-').FirstOrDefault();
                    if (RunE3D.ProjectInfo.Pdms.StructureVersion == 2)
                    {
                        if (code.Length < 6) 
                            continue;

                        //проверка принадлежности в MDB
                        if (mdbName != $"{code.Substring(2, 2)}{code.Substring(6)}") 
                            continue;

                        //проверка кода документации
                        if (!name.StartsWith($"{code.Substring(0, 4)}{_documentCode}{code.Substring(6)}")) 
                            continue;
                    }
                    else if (mdbName != code)
                    {
                        continue;
                    }

                    _departmentSite = item;
                    break;
                }

                if (_departmentSite.IsNull || !_departmentSite.IsValid) 
                    return DbElement.GetElement();                
            }

            var siteName = _departmentSite.GetAsString(DbAttributeInstance.FLNN);
            var markZoneName = $"/{mdbName.Substring(0, mdbName.Length - 2)}{_subObjectCode}-{siteName.Substring(siteName.IndexOf('-') + 1)}-{documentMark}-{_documentCode}";

            if (RunE3D.ProjectInfo.Pdms.StructureVersion == 2)
            {
                var code = siteName.Split('-').FirstOrDefault();
                markZoneName = $"/{code.Substring(0, code.Length - 2)}{_subObjectCode}-{siteName.Substring(siteName.IndexOf('-') + 1)}-{documentMark}";
            }

            if (_isContractorModel && !string.IsNullOrEmpty(_contractorName))
                markZoneName = $"/{siteName.Split('-').First()}-{_contractorName}";

            zone = DbElement.GetElement(markZoneName);

            if (zone.IsNull) 
                CreateZoneElement(markZoneName, ref zone);           

            return zone;
        }

        /// <summary>
        /// Создание элемента ZONE, внутри которого будет вестись работа
        /// </summary>
        /// <param name="site">элемент SITE, внутри которого будет создана ZONE</param>
        /// <param name="mark">название марки</param>
        /// <param name="zone">итоговый элемент</param>
        private static void CreateZoneElement(string markZoneName, ref DbElement zone)
        {
            Command.CreateCommand("!!ZoneCreateFlag = true").Run();
            if (_departmentSite.IsCreatable(DbElementTypeInstance.ZONE) && !_departmentSite.GetBool(DbAttributeInstance.LOCK))
            {
                zone = _departmentSite.CreateLast(DbElementTypeInstance.ZONE);
                if (!zone.IsNull && zone.IsValid)
                {
                    zone.SetAttribute(DbAttributeInstance.NAME, markZoneName);
                    zone.SetAttribute(DbAttributeInstance.DESC, GetDescription("AR", DbElementTypeInstance.ZONE));
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

        private static bool ModifyItem(Utilities.DTO.IfcElementInformation elementInformation, ref DbElement avevaItem)
        {
            DbElementType itemType;
            switch (elementInformation.Type)
            {
                case Utilities.Helpers.Enumerators.ElementType.Wall:
                    itemType = DbElementTypeInstance.GWALL;
                    break;
                case Utilities.Helpers.Enumerators.ElementType.Slab:
                    itemType = DbElementTypeInstance.FLOOR;
                    break;
                default:
                    itemType = DbElementTypeInstance.PANEL;
                    break;
            }   

            var owner = DbElement.GetElement();
            var findRoot = GetOwnerElement(elementInformation.Properties, elementInformation.Type, ref owner);
            if (!findRoot || owner.IsNull)
            {
                RunE3D.SendMessage($"Не найден родительский элемент для элемента с ID {elementInformation.UniqueId}");
                return false;
            }

            var descriptionProperty = elementInformation.Properties.FirstOrDefault(x=>x.Name== "Description");
            var desc = descriptionProperty == null ? string.Empty : descriptionProperty.Value;

            if (avevaItem.IsNull)
            {
                avevaItem = owner.CreateLast(itemType);
            }
            else if (avevaItem.ElementType != itemType)
            {
                avevaItem.Delete();
                if (!avevaItem.IsDeleted)
                    return false;

                avevaItem = owner.CreateLast(itemType);
            }
            else if (avevaItem.Owner != owner && !MoveItemToNewOwner(avevaItem, owner))
            {
                return false;
            }

            avevaItem.SetAttributeIfChanged(DbAttributeInstance.DESC, desc);
            avevaItem.SetAttributeIfChanged(UserAttributes.PolyRevitGuid, elementInformation.UniqueId);

            if (!ModifyMainGeometry(avevaItem, elementInformation))
            {
                RunE3D.SendMessage("Не удалось модифицировать геометрию элемента с ID " + elementInformation.UniqueId);
                return false;
            }
            
            if (elementInformation.Children.Count <= 0)
            {
                foreach (var memb in avevaItem.Members(DbElementTypeInstance.CMPFITTING))
                {
                    memb.Delete();
                }
                return true;
            }

            var cmpf = DbElement.GetElement();
            foreach (var memb in avevaItem.Members(DbElementTypeInstance.CMPFITTING))
            {
                if (cmpf.IsNull)
                {
                    cmpf = memb;
                    continue;
                }
                memb.Delete();
            }

            if (cmpf.IsNull)
                cmpf = avevaItem.CreateLast(DbElementTypeInstance.CMPFITTING);
            
            var sbfiList = cmpf.Members(DbElementTypeInstance.SBFITTING).ToList();
            if (elementInformation.Children.Count < sbfiList.Count)
            {
                var diff = sbfiList.Count - elementInformation.Children.Count;
                for (var i = 0; i < diff; i++)
                {
                    var sbfi = sbfiList.Last();
                    sbfi.Delete();
                    sbfiList.Remove(sbfi);
                }
            }
            sbfiList = cmpf.Members(DbElementTypeInstance.SBFITTING).ToList();
            var sbfiIndex = 0;
            foreach (var child in elementInformation.Children)
            {
                var sbfi = sbfiList.Count < sbfiIndex + 1 ? cmpf.CreateLast(DbElementTypeInstance.SBFITTING) : sbfiList[sbfiIndex];

                if (!ModifySecondaryGeometry(sbfi, child.Geometries))
                {
                    RunE3D.SendMessage("Не удалось модифицировать геометрию второстепенного элемента с ID " + child.UniqueId);
                    return false;
                }

                descriptionProperty = child.Properties.FirstOrDefault(x => x.Name == "Description");
                desc = descriptionProperty == null ? string.Empty : descriptionProperty.Value;
                
                sbfi.SetAttributeIfChanged(DbAttributeInstance.DESC, desc);
                sbfi.SetAttributeIfChanged(UserAttributes.PolyRevitGuid, child.UniqueId);

                sbfiIndex++;
            }
            return true;
        }

        private static bool ModifyMainGeometry(DbElement item, Utilities.DTO.IfcElementInformation elementInformation)
        {
            var mainPosition = Position.Create();
            var mainOrientation = Orientation.Create();

            var positiveList = elementInformation.Geometries.Where(x => !x.IsNegative).ToList();

            if (positiveList.Count == 0)
                return false;

            var positive = positiveList[0];
            var data = GeometryDataFromString(positive.Data, positive.Type);
            if (positive.Type == Utilities.Helpers.Enumerators.GeometryType.Mesh)
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
                if (positive.Type == Utilities.Helpers.Enumerators.GeometryType.Extrusion)
                {
                    var normal = new XbimVector3D(parameters[1], parameters[2], parameters[3]);
                    var point = new XbimPoint3D(parameters[4], parameters[5], parameters[6]);

                    var transformedPoint = TransformPoint(item, point);
                    var transformedVector = TransformVector(item, normal);
                    var zDir = Direction.Create(Position.Create(transformedVector.X, transformedVector.Y,
                        transformedVector.Z));
                    mainPosition = Position.Create(transformedPoint.X, transformedPoint.Y, transformedPoint.Z);
                    mainOrientation = Orientation.Create("Z is " + zDir);
                }
                else if (positive.Type == Utilities.Helpers.Enumerators.GeometryType.Cylinder)
                {
                    var normal = new XbimVector3D(parameters[2], parameters[3], parameters[4]);
                    var point = new XbimPoint3D(parameters[5], parameters[6], parameters[7]);

                    var transformedPoint = TransformPoint(item, point);
                    var transformedVector = TransformVector(item, normal);
                    mainPosition = Position.Create(transformedPoint.X, transformedPoint.Y, transformedPoint.Z);
                    var plane = Plane.Create(
                        Direction.Create(Position.Create(transformedVector.X, transformedVector.Y,
                            transformedVector.Z)), mainPosition);
                    mainOrientation = plane.Orientation;
                }
            }

            item.SetAttributeIfChanged(DbAttributeInstance.POS, mainPosition);
            item.SetAttributeIfChanged(DbAttributeInstance.ORI, mainOrientation);

            var geometryItemType = positive.Type == Utilities.Helpers.Enumerators.GeometryType.Mesh
                ? DbElementTypeInstance.TMPLATE
                : DbElementTypeInstance.PLOOP;
            if (geometryItemType == DbElementTypeInstance.TMPLATE)
            {
                var tmpl = DbElement.GetElement();
                foreach (var memb in item.Members())
                {
                    if (memb.ElementType == DbElementTypeInstance.CMPFITTING)
                        continue;

                    if (memb.ElementType == DbElementTypeInstance.TMPLATE && tmpl.IsNull)
                    {
                        tmpl = memb;
                        continue;
                    }

                    memb.Delete();
                }

                if (tmpl.IsNull)
                    tmpl = item.CreateFirst(DbElementTypeInstance.TMPLATE);

                foreach (var member in tmpl.Members())
                {
                    if (member.ElementType == DbElementTypeInstance.GENPRI)
                        continue;

                    member.Delete();
                }

                var genpriList = tmpl.Members(DbElementTypeInstance.GENPRI).ToList();

                if (positiveList.Count < genpriList.Count)
                {
                    var difference = genpriList.Count - positiveList.Count;
                    for (var j = 0; j < difference; j++)
                    {
                        var genpri = genpriList.Last();

                        genpri.Delete();
                        if (!genpri.IsDeleted)
                            return false;

                        genpriList.Remove(genpri);
                    }
                }

                for (var j = 0; j < positiveList.Count; j++)
                {
                    var genpri = genpriList.Count < j + 1
                        ? tmpl.CreateLast(DbElementTypeInstance.GENPRI)
                        : genpriList[j];

                    data = GeometryDataFromString(positiveList[j].Data, positiveList[j].Type);
                    var bytes = (byte[])data;
                    var point = GetPositionFromBytes(bytes);
                    var transformedPoint = TransformPoint(genpri, point);
                    mainPosition = Position.Create(transformedPoint.X, transformedPoint.Y, transformedPoint.Z);
                    mainOrientation = Orientation.Create("Z is U");

                    genpri.SetAttributeIfChanged(DbAttributeInstance.POS, mainPosition);
                    genpri.SetAttributeIfChanged(DbAttributeInstance.ORI, mainOrientation);

                    var eldatas = GetDataFromBytes((byte[])data);
                    var archiv = genpri.Members(DbElementTypeInstance.ARCHIV).Length > 0
                        ? genpri.Members(DbElementTypeInstance.ARCHIV).First()
                        : genpri.CreateFirst(DbElementTypeInstance.ARCHIV);

                    if (!string.IsNullOrEmpty(positiveList[j].ColorARGB))
                    {
                        archiv.SetAttributeIfChanged(DbAttributeInstance.DESC, positiveList[j].ColorARGB);
                        var clrData = positiveList[j].ColorARGB.Replace(".", ",").Split();
                        if (clrData.Length > 3 && double.TryParse(clrData[3], out double alpha) && alpha < 1)
                        {
                            genpri.SetAttributeIfChanged(DbAttributeInstance.LEVE, new[] { 9, 10 });
                        }
                        else
                        {
                            genpri.SetAttributeIfChanged(DbAttributeInstance.LEVE, new[] { 0, 10 });
                        }
                    }

                    var attdatList = archiv.Members(DbElementTypeInstance.ATTDAT).ToList();
                    if (eldatas.Count < attdatList.Count)
                    {
                        var difference = attdatList.Count - eldatas.Count;
                        for (var i = 0; i < difference; i++)
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
                        attdat.SetAttribute(DbAttributeInstance.QALLSI, eldata.Length);
                    }
                }
            }
            else if (geometryItemType == DbElementTypeInstance.PLOOP)
            {
                foreach (var tmpl in item.Members(DbElementTypeInstance.TMPLATE))
                {
                    tmpl.Delete();
                    if (!tmpl.IsDeleted)
                        return false;
                }

                var ploop = item.Members(geometryItemType).Length > 0
                    ? item.Members(geometryItemType).First()
                    : item.CreateFirst(geometryItemType);
                var parameters = (double[])data;

                var points = new List<XbimPoint3D>();
                for (var i = 4; i < parameters.Length; i += 3)
                {
                    points.Add(new XbimPoint3D(parameters[i], parameters[i + 1], parameters[i + 2]));
                }

                ploop.SetAttributeIfChanged(DbAttributeInstance.HEIG, parameters[0] > 0 ? parameters[0] : 1);

                var paveList = ploop.Members(DbElementTypeInstance.PAVERT).ToList();
                if (points.Count < paveList.Count)
                {
                    var difference = paveList.Count - points.Count;
                    for (var i = 0; i < difference; i++)
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

                    var transformedPoint = TransformPoint(pave, point);
                    var pos = Position.Create(transformedPoint.X, transformedPoint.Y, transformedPoint.Z);
                    pave.SetAttributeIfChanged(DbAttributeInstance.POS, pos);
                }

                var negativeList = elementInformation.Geometries.Where(x => x.IsNegative).ToList();
                var negativeExtrCount =
                    negativeList.Count(x => x.Type == Utilities.Helpers.Enumerators.GeometryType.Extrusion);
                var negativeCyliCount =
                    negativeList.Count(x => x.Type == Utilities.Helpers.Enumerators.GeometryType.Cylinder);

                var nxtrList = item.Members(DbElementTypeInstance.NXTRUSION).ToList();
                var ncylList = item.Members(DbElementTypeInstance.NCYLINDER).ToList();

                if (negativeExtrCount < nxtrList.Count)
                {
                    var difference = nxtrList.Count - negativeExtrCount;
                    for (var i = 0; i < difference; i++)
                    {
                        var nxtr = nxtrList.Last();
                        nxtr.Delete();
                        nxtrList.Remove(nxtr);
                    }
                }

                if (negativeCyliCount < ncylList.Count)
                {
                    var difference = ncylList.Count - negativeCyliCount;
                    for (var i = 0; i < difference; i++)
                    {
                        var ncyl = ncylList.Last();
                        ncyl.Delete();
                        ncylList.Remove(ncyl);
                    }
                }

                var nxtrIndex = 0;
                var ncylIndex = 0;
                foreach (var negativ in negativeList)
                {
                    var nparameters = (double[])GeometryDataFromString(negativ.Data, negativ.Type);
                    if (negativ.Type == Utilities.Helpers.Enumerators.GeometryType.Extrusion)
                    {
                        var primitive = nxtrList.Count < nxtrIndex + 1
                            ? item.CreateLast(DbElementTypeInstance.NXTRUSION)
                            : nxtrList[nxtrIndex];

                        var ndepth = nparameters[0];
                        var negativeNormalList = new XbimVector3D(nparameters[1], nparameters[2], nparameters[3]);
                        var negativePointList = new List<XbimPoint3D>();
                        for (var i = 4; i < nparameters.Length; i += 3)
                        {
                            negativePointList.Add(new XbimPoint3D(nparameters[i], nparameters[i + 1],
                                nparameters[i + 2]));
                        }

                        var firstPoint = negativePointList.First();

                        var negativeTransformedPoint = TransformPoint(primitive, firstPoint);
                        var negativePosition = Position.Create(negativeTransformedPoint.X, negativeTransformedPoint.Y,
                            negativeTransformedPoint.Z);

                        var negativeTransformedVector = TransformVector(primitive, negativeNormalList);
                        var zDirection = Direction.Create(Position.Create(negativeTransformedVector.X,
                            negativeTransformedVector.Y, negativeTransformedVector.Z));
                        var negativeOrientation = Orientation.Create("Z is " + zDirection);

                        primitive.SetAttributeIfChanged(DbAttributeInstance.POS, negativePosition);
                        primitive.SetAttributeIfChanged(DbAttributeInstance.ORI, negativeOrientation);
                        primitive.SetAttributeIfChanged(DbAttributeInstance.HEIG, ndepth > 0 ? ndepth : 1);

                        var loop = primitive.Members(DbElementTypeInstance.LOOP).Length > 0
                            ? primitive.Members(DbElementTypeInstance.LOOP).First()
                            : primitive.CreateLast(DbElementTypeInstance.LOOP);

                        var vertexList = loop.Members(DbElementTypeInstance.VERTEX).ToList();
                        if (negativePointList.Count < vertexList.Count)
                        {
                            var difference = vertexList.Count - negativePointList.Count;
                            for (var i = 0; i < difference; i++)
                            {
                                var vertex = vertexList.Last();
                                vertex.Delete();
                                vertexList.Remove(vertex);
                            }
                        }

                        for (var i = 0; i < negativePointList.Count; i++)
                        {
                            var point = negativePointList[i];
                            var vertex = loop.Members(DbElementTypeInstance.VERTEX).Length < i + 1
                                ? loop.CreateLast(DbElementTypeInstance.VERTEX)
                                : loop.Members(DbElementTypeInstance.VERTEX)[i];

                            var transformedPoint = TransformPoint(vertex, point);
                            var pos = Position.Create(transformedPoint.X, transformedPoint.Y, transformedPoint.Z);
                            vertex.SetAttributeIfChanged(DbAttributeInstance.POS, pos);
                        }

                        nxtrIndex++;
                    }
                    else if (negativ.Type == Utilities.Helpers.Enumerators.GeometryType.Cylinder)
                    {
                        var primitive = ncylList.Count < ncylIndex + 1
                            ? item.CreateLast(DbElementTypeInstance.NCYLINDER)
                            : ncylList[ncylIndex];

                        var height = nparameters[0];
                        var radius = nparameters[1];
                        var normal = new XbimVector3D(nparameters[2], nparameters[3], nparameters[4]);
                        var position = new XbimPoint3D(nparameters[5], nparameters[6], nparameters[7]);
                        var negativeTransformedPoint = TransformPoint(primitive, position);
                        var negativePosition = Position.Create(negativeTransformedPoint.X, negativeTransformedPoint.Y,
                            negativeTransformedPoint.Z);

                        var negativeTransformedVector = TransformVector(primitive, normal);
                        var zDirection = Direction.Create(Position.Create(negativeTransformedVector.X,
                            negativeTransformedVector.Y, negativeTransformedVector.Z));
                        var negativeOrientation = Orientation.Create("Z is " + zDirection);

                        var matrix = new XbimMatrix3D(negativeOrientation.XDir().East, negativeOrientation.XDir().North,
                            negativeOrientation.XDir().Up, 0, negativeOrientation.YDir().East,
                            negativeOrientation.YDir().North, negativeOrientation.YDir().Up, 0,
                            negativeOrientation.ZDir().East, negativeOrientation.ZDir().North,
                            negativeOrientation.ZDir().Up, 0, 0, 0, 0, 1);

                        var offsetPoint = new XbimPoint3D(0, 0, height / 2);
                        offsetPoint = matrix.Transform(offsetPoint);
                        negativePosition.MoveBy(offsetPoint.X, offsetPoint.Y, offsetPoint.Z);

                        primitive.SetAttributeIfChanged(DbAttributeInstance.POS, negativePosition);
                        primitive.SetAttributeIfChanged(DbAttributeInstance.ORI, negativeOrientation);
                        primitive.SetAttributeIfChanged(DbAttributeInstance.HEIG, height);
                        primitive.SetAttributeIfChanged(DbAttributeInstance.DIAM, radius * 2);

                        ncylIndex++;
                    }
                }
            }

            return true;
        }

        private static bool ModifySecondaryGeometry(DbElement sbfi, List<Utilities.DTO.Geometry> geometryList)
        {
            var positiveGeometryList = geometryList.Where(x => !x.IsNegative).ToList();
            if (positiveGeometryList.Count == 0 ||
                positiveGeometryList[0].Type != Utilities.Helpers.Enumerators.GeometryType.Mesh)
                return false;

            var tmpl = DbElement.GetElement();
            foreach (var member in sbfi.Members())
            {
                if (member.ElementType == DbElementTypeInstance.TMPLATE && tmpl.IsNull)
                {
                    tmpl = member;
                    continue;
                }

                member.Delete();
            }

            if (tmpl.IsNull)
                tmpl = sbfi.CreateFirst(DbElementTypeInstance.TMPLATE);

            var mainPosition = Position.Create();
            var mainOrientation = Orientation.Create();

            var genpriList = new List<DbElement>();
            foreach (var member in tmpl.Members())
            {
                if (member.ElementType == DbElementTypeInstance.GENPRI)
                {
                    genpriList.Add(member);
                    continue;
                }

                member.Delete();

                if (!member.IsDeleted)
                    return false;
            }

            if (positiveGeometryList.Count < genpriList.Count)
            {
                var difference = genpriList.Count - positiveGeometryList.Count;
                for (var j = 0; j < difference; j++)
                {
                    var genpri = genpriList.Last();

                    genpri.Delete();

                    if (!genpri.IsDeleted)
                        return false;

                    genpriList.Remove(genpri);
                }
            }

            for (var j = 0; j < positiveGeometryList.Count; j++)
            {
                var genpri = genpriList.Count < j + 1 ? tmpl.CreateLast(DbElementTypeInstance.GENPRI) : genpriList[j];
                var data = GeometryDataFromString(positiveGeometryList[j].Data, positiveGeometryList[j].Type);
                var bytes = (byte[])data;
                var point = GetPositionFromBytes(bytes);
                var tp = TransformPoint(sbfi, point);
                mainPosition = Position.Create(tp.X, tp.Y, tp.Z);
                mainOrientation = Orientation.Create("Z is U");

                genpri.SetAttributeIfChanged(DbAttributeInstance.POS, mainPosition);
                genpri.SetAttributeIfChanged(DbAttributeInstance.ORI, mainOrientation);

                var eldatas = GetDataFromBytes((byte[])data);

                var archiv = genpri.Members(DbElementTypeInstance.ARCHIV).Length > 0
                    ? genpri.Members(DbElementTypeInstance.ARCHIV).First()
                    : genpri.CreateFirst(DbElementTypeInstance.ARCHIV);

                if (!string.IsNullOrEmpty(positiveGeometryList[j].ColorARGB))
                {
                    archiv.SetAttributeIfChanged(DbAttributeInstance.DESC, positiveGeometryList[j].ColorARGB);
                    var clrData = positiveGeometryList[j].ColorARGB.Replace(".", ",").Split();

                    if (clrData.Length > 3 && double.TryParse(clrData[3], out double alpha) && alpha < 1)
                    {
                        genpri.SetAttributeIfChanged(DbAttributeInstance.LEVE, new[] { 9, 10 });
                    }
                    else
                    {
                        genpri.SetAttributeIfChanged(DbAttributeInstance.LEVE, new[] { 0, 10 });
                    }
                }

                var attdatList = archiv.Members(DbElementTypeInstance.ATTDAT).ToList();
                if (eldatas.Count < attdatList.Count)
                {
                    var difference = attdatList.Count - eldatas.Count;
                    for (var i = 0; i < difference; i++)
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

        internal static object GeometryDataFromString(string data, Utilities.Helpers.Enumerators.GeometryType type)
        {
            byte[] bytes;
            var compressedBytes = Convert.FromBase64String(data);
            using (var mw = new MemoryStream())
            {
                using (var mr = new MemoryStream(compressedBytes))
                {
                    using (var zs = new ZlibStream(mr, CompressionMode.Decompress, true))
                    {
                        //zs.CopyTo(mw);    
                        CopyStream(zs, mw);
                    }
                }

                bytes = mw.ToArray();
            }

            if (type == Utilities.Helpers.Enumerators.GeometryType.Mesh)
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

        private static bool GetOwnerElement(List<Utilities.DTO.Property> properties, Utilities.Helpers.Enumerators.ElementType type, ref DbElement root)
        {
            string rootName;

            var supportingProperty = properties?.FirstOrDefault(x => x.Name == "supporting");
            var supporting = supportingProperty != null && bool.Parse(supportingProperty.Value);

            var zoneProperty = properties?.FirstOrDefault(x => x.Name == "zone");
            var documentMark = zoneProperty == null ? string.Empty : zoneProperty.Value.Split().First();

            if (!string.IsNullOrEmpty(documentMark) && new Regex(@"АР\d*").Match(documentMark).Value.Equals(documentMark))
            {
                documentMark = documentMark.Replace("АР", "AR");
            }
            else
            {
                documentMark = _fileDocumentMark;
            }

            var markZone = GetRootZone(documentMark);
            if (_isContractorModel)
            {
                var stru = markZone.Members().FirstOrDefault(x=>x.ElementType==DbElementTypeInstance.STRUCTURE);
                if (stru == null)
                    stru = markZone.CreateLast(DbElementTypeInstance.STRUCTURE);

                var frmw = stru.Members().FirstOrDefault(x => x.ElementType == DbElementTypeInstance.FRMWORK);
                if (frmw == null)
                    frmw = stru.CreateLast(DbElementTypeInstance.FRMWORK);
                root= frmw;
                return true;
            }

            if (type == Utilities.Helpers.Enumerators.ElementType.Beam)
            {
                rootName = "BEAM";
            }
            else if (type == Utilities.Helpers.Enumerators.ElementType.Covering)
            {
                rootName = "COVERING";
            }
            else if (type == Utilities.Helpers.Enumerators.ElementType.CurtainWall)
            {
                rootName = "CURTAIN";
            }
            else if (type == Utilities.Helpers.Enumerators.ElementType.Door)
            {
                rootName = "DOOR";
            }
            else if (type == Utilities.Helpers.Enumerators.ElementType.Other)
            {
                rootName = "OTHER";
            }
            else if (type == Utilities.Helpers.Enumerators.ElementType.Railing)
            {
                rootName = "RAILING";
            }
            else if (type == Utilities.Helpers.Enumerators.ElementType.Roof)
            {
                rootName = "ROOF";
            }
            else if (type == Utilities.Helpers.Enumerators.ElementType.Slab)
            {
                var elevationProperty = properties?.FirstOrDefault(x => x.Name == "elevation");
                var elevation = elevationProperty == null ? 0.0 : double.Parse(elevationProperty.Value);

                rootName = $"{(supporting? "SLABTEMP" : "SLAB")}_{(elevation > 0.0 ? "+" : string.Empty)}{(elevation / 1000).ToString("0.000").Replace(",", ".")}";
            }
            else if (type == Utilities.Helpers.Enumerators.ElementType.Stair)
            {
                rootName = "STAIR";
            }
            else if (type == Utilities.Helpers.Enumerators.ElementType.Wall)
            {
                var functionProperty = properties?.FirstOrDefault(x => x.Name == "function");
                var function = functionProperty == null ? "N" : functionProperty.Value;

                rootName = $"{(supporting ? "WALLTEMP" : "WALL")}_{function}";
            }
            else if (type == Utilities.Helpers.Enumerators.ElementType.Window)
            {
                rootName = "WINDOW";
            }
            else if (type == Utilities.Helpers.Enumerators.ElementType.REZV)
            {
                rootName = "REZV";
            }
            else if (type == Utilities.Helpers.Enumerators.ElementType.WALK)
            {
                rootName = "WALK";
            }
            else if (type == Utilities.Helpers.Enumerators.ElementType.TEXT)
            {
                rootName = "TEXT";
            }
            else
            {
                rootName = "OTHER";
            }

            return !string.IsNullOrEmpty(rootName) && GetRootByName(rootName, markZone, ref root);
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
                Helpers.Common.SortLevelElements(owner);
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

            while (owner.ElementType != DbElementTypeInstance.STRUCTURE &&
                   owner.ElementType != DbElementTypeInstance.ZONE)
            {
                owner = owner.Owner;
                if (!owner.IsAttributeValid(DbAttributeInstance.POS) ||
                    !owner.IsAttributeValid(DbAttributeInstance.ORI))
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
            while (owner.ElementType != DbElementTypeInstance.FRMWORK &&
                   owner.ElementType != DbElementTypeInstance.STRUCTURE)
            {
                owner = owner.Owner;
                if (!owner.IsAttributeValid(DbAttributeInstance.POS) ||
                    !owner.IsAttributeValid(DbAttributeInstance.ORI))
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
        private static bool MoveItemToNewOwner(DbElement item, DbElement root)
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
