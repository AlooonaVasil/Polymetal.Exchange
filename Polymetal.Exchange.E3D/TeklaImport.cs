using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Data.SqlTypes;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using Aveva.Core.Database;
using Aveva.Core.Geometry;
using Aveva.Core.Utilities.CommandLine;
using Aveva.Core.Database.Filters;
using Polymetal.Exchange.Utilities;
using Tekla.Structures.Geometry3d;
using DbType = Aveva.Core.Database.DbType;
using Aveva.Core.Utilities.Messaging;
using System.Text.RegularExpressions;
using Polymetal.Core.Common;

namespace Polymetal.Exchange.E3D
{
    class TeklaImport
    {
        private struct TeklaElementPoints
        {
            public Point StartPoint;
            public Point EndPoint;
            public Vector XVector;
            public Vector YVector;
            public Point OriginPoint;
            public double Height;
            public double Width;
            public string Level;
            internal Dictionary<string, double> ProfileParameters;
        }

        internal struct SctnInfo
        {
            public DbElement Element;
            public Position Poss;
            public Position Pose;
            public double Bangle;
            public Orientation Ori;
            public Direction drns;
            public Direction drne;
            public double Height;
            public double Width;
        }

        internal struct FloorInfo
        {
            public DbElement Element;
            public double Height;
            public List<PaveInfo> Points;
        }

        private struct ExtrusionInfo
        {
            public List<Position> VertexList;
            public double Height;
            public double Diameter;
            public CoordinateSystem System;
        }

        internal struct PaveInfo
        {
            public DbElement Element;
            public Position Position;
        }

        private static DbExpression _siteExpr = DbExpression.Parse("SITE");

        private static Matrix _globalMatrix;

        private static List<ElementHistory> histories = new List<ElementHistory>();

        private static string _documentCode;
        private static string _subObjectCode;
        private static List<DbElement> _usingDepartmentSites;

        /// <summary>
        /// Синхронизация с элементами Tekla 
        /// </summary>
        /// <returns></returns>
        public static bool Run()
        {
            //список задействованных сайтов отделов для последующей очистки от пустых структур
            _usingDepartmentSites = new List<DbElement>();
            var triggerUser = RunE3D.GetUserByTriggerId();
            Console.WriteLine("Начинается импорт данных");
            var mdbName = MDB.CurrentMDB.Name;
            var modelDir = new DirectoryInfo(RunE3D.ProjectInfo.Program.Path);
            if (modelDir.Exists)
            {
                var matchSubObjectCode = new Regex(@"\d{2}\s\d{2}\s\d{3}\s(\d{2})").Match(modelDir.Name);
                _subObjectCode = matchSubObjectCode.Success ? matchSubObjectCode.Groups[1].Value : mdbName.Substring(mdbName.Length - 2);

                var matchDocumentCode = new Regex(@"\d{2}\s(\d{2})").Match(modelDir.Name);
                _documentCode = matchDocumentCode.Success ? matchDocumentCode.Groups[1].Value : "07";
            }

            var dt = Common.GetUpdatedTeklaData(RunE3D.ProjectInfo.ProjectId);
            _globalMatrix = GetGlobalMatrix();
            var markZoneDictionary = new Dictionary<string, DbElement>();
            Console.WriteLine("Элементов для обработки: " + dt.Rows.Count + " шт.");
            var oldPercent = 0.0;
            var modifications = 0;
            for (var i = 0; i < dt.Rows.Count; i++)
            {
                var percent = Math.Ceiling((double)i * 100 / dt.Rows.Count);
                if (oldPercent < percent)
                {
                    Console.WriteLine("Current progress {0}%   ", Math.Ceiling(percent));
                    oldPercent = percent;
                }
                var row = dt.Rows[i];                

                var status = row["element_status"].ToString();

                if (!Enum.IsDefined(typeof(Common.PartType), row["beam_type"].ToString())) 
                    continue;

                var partType = (Common.PartType)Enum.Parse(typeof(Common.PartType), row["beam_type"].ToString());

                var spre = DbElement.GetElement(row["profile_name"].ToString());
                if (partType != Common.PartType.ContourPlate && partType != Common.PartType.ContourPlatePad &&
                    partType != Common.PartType.BeamPad && partType != Common.PartType.PolyBeamPad &&
                    partType != Common.PartType.BeamShape && partType != Common.PartType.PolyBeamShape &&
                    spre.IsNull && status != "DEL") 
                    continue;

                if (status == "DEL")
                {   
                    var items = GetElementListFromXml((SqlXml)row["pdms_item"]);
                    if (items.Count == 0)
                    {
                        row["element_status"] = "REM";
                        continue;
                    }

                    var failed = false;
                    foreach (var item in items)
                    {
                        if (item.IsNull || item.IsDeleted) 
                            continue;

                        var hist = new ElementHistory(item,
                            row["beam_created_by"], row["beam_created"],
                            row["beam_modified_by"], row["beam_modified"],
                            triggerUser, DateTime.Now);

                        if (item.IsDeleteable) 
                            item.Delete();

                        if (!item.IsDeleted)
                        {
                            RunE3D.SendMessage("Не удалось удалить элемент " + item.ElementType.ShortName + " " + item.GetAsString(DbAttributeInstance.REF));
                            failed = true;
                            break;
                        }

                        histories.Add(hist);
                    }

                    if (!failed)
                    {
                        row["element_status"] = "REM";
                        modifications++;
                    }

                    continue;
                }

                DbElement zone;
                var mark = row["beam_phase"].ToString().Replace(" ", string.Empty);
                if (markZoneDictionary.ContainsKey(mark))
                {
                    zone = markZoneDictionary[mark];
                }
                else
                {
                    zone = GetRootZone(mdbName, mark);
                    markZoneDictionary.Add(mark, zone);
                }

                if (zone.IsNull || !zone.IsValid)
                {
                    RunE3D.SendMessage($"[ModifyElement] {row["beam_guid"]} не найден родительский ZONE");
                    continue;
                }

                if (status == "CRE" || status == "MOD")
                {
                    if (partType == Common.PartType.ContourPlate)
                    {
                        try
                        {
                            if (ModifyFloors(ref row, zone))
                            {
                                row["element_status"] = "UPD";
                                modifications++;
                            }
                        }
                        catch (Exception exc)
                        {
                            RunE3D.SendMessage("[ModifyFloors] " + row["beam_guid"] + " " + exc.Message);
                        }
                    }
                    else if (partType == Common.PartType.ContourPlatePad ||
                             partType == Common.PartType.BeamPad || partType == Common.PartType.PolyBeamPad || 
                             partType == Common.PartType.BeamShape || partType == Common.PartType.PolyBeamShape)
                    {
                        try
                        {
                            if (ModifyShapeElement(zone, ref row))
                            {
                                row["element_status"] = "UPD";
                                modifications++;
                            }
                        }
                        catch (Exception exc)
                        {
                            RunE3D.SendMessage($"[ModifyShapeElement] {row["beam_guid"]} {exc.Message}");
                        }
                    }
                    else
                    {
                        try
                        {
                            if (ModifySctns(ref row, zone, spre))
                            {
                                row["element_status"] = "UPD";
                                modifications++;
                            }
                        }
                        catch (Exception exc)
                        {
                            RunE3D.SendMessage("[ModifySctns] " + row["beam_guid"] + " " + exc.Message);
                        }
                    }
                }
            }

            foreach (var departmentSite in _usingDepartmentSites)
            {
                Helpers.Common.ClearEmptyElements(departmentSite);
                Helpers.Common.SortElements(departmentSite);
            }

            Console.WriteLine("Заполнение данных по истории элементов (" + histories.Count + " записей)");            
            RunE3D.UpdateHistory(histories, Common.Source.Tekla);
            Console.WriteLine("История элементов обновлена");
            Common.SetUpdatedTeklaData(dt, RunE3D.ProjectInfo.ProjectId);
            Console.WriteLine("Данные в SQL обновлены");
            RunE3D.SendMessage("Изменений в модели: " + modifications);
            return true;
        }

        private static Matrix GetGlobalMatrix()
        {           
            var offsetData = string.Empty;
            using (var sqlConnection = new SqlConnection(Common.ConnectionString))
            {
                sqlConnection.Open();
                var sqlCommand = "select top 1 Offset from ExchangeProject where ProjectID = " + RunE3D.ProjectInfo.ProjectId;
                using (var sqlCmd = new SqlCommand(sqlCommand, sqlConnection))
                {               
                    var res = sqlCmd.ExecuteScalar();                   
                    if (res == null || res == DBNull.Value) return new Matrix();
                    offsetData = (string)res;
                }
                sqlConnection.Close();
            }

            if (string.IsNullOrEmpty(offsetData)) return new Matrix();

            var data = offsetData.Split(' ');
            if (data.Length != 12) return new Matrix();

            var origin = new Point(double.Parse(data[9], CultureInfo.InvariantCulture), double.Parse(data[10], CultureInfo.InvariantCulture), double.Parse(data[11], CultureInfo.InvariantCulture));
            var xVector = new Vector(double.Parse(data[0], CultureInfo.InvariantCulture), double.Parse(data[1], CultureInfo.InvariantCulture), double.Parse(data[2], CultureInfo.InvariantCulture));
            var yVector = new Vector(double.Parse(data[3], CultureInfo.InvariantCulture), double.Parse(data[4], CultureInfo.InvariantCulture), double.Parse(data[5], CultureInfo.InvariantCulture));
            return MatrixFactory.FromCoordinateSystem(new CoordinateSystem(origin,xVector,yVector));
        }
        /// <summary>
        /// Получение массива с информацией по всем элементам SCTN относящимся к данному элементу Tekla
        /// </summary>
        /// <param name="sqlXml">входной SqlXml</param>
        /// <returns>лист данных</returns>
        internal static List<SctnInfo> GetSctnInfoList(SqlXml sqlXml)
        {
            var result = new List<SctnInfo>();
            var doc = Common.SqlXmlToXmlDoc(sqlXml);
            var sctnList = doc.GetElementsByTagName("Sctn");
            if (sctnList.Count == 0)
            {
                var items = GetElementListFromXml(sqlXml);
                foreach (var item in items.Where(x => !x.IsNull && x.IsValid))
                {
                    item.Delete();
                }
            }

            foreach (XmlNode s in sctnList)
            {
                var sctnInfo = new SctnInfo
                {
                    Poss = Position.Create(),
                    Pose = Position.Create(),
                    Bangle = 0.0,
                    Element = DbElement.GetElement(),
                    Ori = Orientation.Create(),
                    drns = Direction.Create(),
                    drne = Direction.Create(),
                    Height = 0.0,
                    Width = 0.0
                };
                GetSctnAttributesFromXml(s, ref sctnInfo);
                result.Add(sctnInfo);
            }

            return result;
        }

        /// <summary>
        /// Получение массива с информацией по всем элементам FLOOR относящимся к данному элементу Tekla
        /// </summary>
        /// <param name="sqlXml">входной SqlXml</param>
        /// <returns>лист данных</returns>
        internal static FloorInfo GetFloorInfoList(SqlXml sqlXml)
        {
            var result = new FloorInfo { Element = DbElement.GetElement(), Height = 0.0, Points = new List<PaveInfo>() };
            var doc = Common.SqlXmlToXmlDoc(sqlXml);
            var floorNodes = doc.GetElementsByTagName("Floor");
            if (floorNodes.Count == 0)
            {
                var items = GetElementListFromXml(sqlXml);
                foreach (var item in items.Where(x => !x.IsNull && x.IsValid))
                {
                    item.Delete();
                }

                return result;
            }

            var floorNode = floorNodes.Item(0);
            foreach (XmlAttribute att in floorNode.Attributes)
            {
                switch (att.Name)
                {
                    case "Element":
                        result.Element = DbElement.GetElement(att.Value);
                        break;
                    case "Height":
                        result.Height = double.Parse(att.Value, CultureInfo.InvariantCulture);
                        break;
                }
            }

            var points = new List<PaveInfo>();
            foreach (XmlNode p in floorNode.ChildNodes)
            {
                if (!p.Name.Equals("Pave")) continue;
                var position = Position.Create();
                var pointInfo = new PaveInfo { Element = DbElement.GetElement() };
                foreach (XmlAttribute att in p.Attributes)
                {
                    switch (att.Name)
                    {
                        case "Element":
                            pointInfo.Element = DbElement.GetElement(att.Value);
                            break;
                        case "X":
                            position.X = double.Parse(att.Value, CultureInfo.InvariantCulture);
                            break;
                        case "Y":
                            position.Y = double.Parse(att.Value, CultureInfo.InvariantCulture);
                            break;
                        case "Z":
                            position.Z = double.Parse(att.Value, CultureInfo.InvariantCulture);
                            break;
                    }
                }
                pointInfo.Position = position;
                points.Add(pointInfo);
            }

            result.Points = points;
            return result;
        }

        internal static List<DbElement> GetElementListFromXml(SqlXml sqlXml)
        {
            var result = new List<DbElement>();
            var doc = Common.SqlXmlToXmlDoc(sqlXml);

            var nodeList = new List<XmlNode>();
            nodeList.AddRange(doc.GetElementsByTagName("Floor").Cast<XmlNode>());
            nodeList.AddRange(doc.GetElementsByTagName("Sctn").Cast<XmlNode>());
            nodeList.AddRange(doc.GetElementsByTagName("Item").Cast<XmlNode>());
            
            foreach (var node in nodeList)
            {
                if (node.Attributes?["Element"] != null)
                {
                    result.Add(DbElement.GetElement(node.Attributes["Element"].Value));
                }
            }
            
            return result;
        }

        internal static DbElement GetShapeItem(SqlXml sqlXml)
        {
            var result = DbElement.GetElement();
            var doc = Common.SqlXmlToXmlDoc(sqlXml);
            var itemNodes = doc.GetElementsByTagName("Item");

            if (itemNodes.Count > 0)
            {
                var itemNode = itemNodes.Item(0);
                foreach (XmlAttribute att in itemNode.Attributes)
                {
                    if (att.Name != "Element") 
                        continue;

                    result = DbElement.GetElement(att.Value);
                }
            }
            else
            {
                itemNodes = doc.GetElementsByTagName("Floor");
                if (itemNodes.Count > 0)
                {
                    var itemNode = itemNodes.Item(0);
                    foreach (XmlAttribute att in itemNode.Attributes)
                    {
                        if (att.Name != "Element") 
                            continue;

                        result = DbElement.GetElement(att.Value);
                    }
                }
                else
                {
                    var items = GetElementListFromXml(sqlXml);
                    foreach (var item in items.Where(x => !x.IsNull && x.IsValid))
                    {
                        item.Delete();
                    }
                }
            }
            
            return result;
        }

        /// <summary>
        /// Получение данных о всех вырезах
        /// </summary>
        /// <param name="sqlXml"></param>
        /// <returns></returns>
        private static List<List<ExtrusionInfo>> GetBooleansInfoList(SqlXml sqlXml)
        {
            var result = new List<List<ExtrusionInfo>>();
            var doc = Common.SqlXmlToXmlDoc(sqlXml);
            var booleansList = doc.GetElementsByTagName("Booleans");
            foreach (XmlNode bl in booleansList)
            {
                foreach (XmlNode b in bl.ChildNodes)
                {
                    var boo = new List<ExtrusionInfo>();

                    if (!b.Name.Equals("Boolean", StringComparison.OrdinalIgnoreCase)) continue;
                    foreach (XmlNode extr in b.ChildNodes)
                    {
                        if (!extr.Name.Equals("Extrusion", StringComparison.OrdinalIgnoreCase)) continue;

                        var extrusionInfo = new ExtrusionInfo
                        {
                            Height = 0,
                            Diameter = 0,
                            VertexList = new List<Position>(),
                            System = new CoordinateSystem()
                        };
                        foreach (XmlAttribute att in extr.Attributes)
                        {
                            if (att.Name.Equals("height", StringComparison.OrdinalIgnoreCase))
                            {
                                extrusionInfo.Height = double.Parse(att.Value, CultureInfo.InvariantCulture);
                            }
                            else if (att.Name.Equals("diameter", StringComparison.OrdinalIgnoreCase))
                            {
                                extrusionInfo.Diameter = double.Parse(att.Value, CultureInfo.InvariantCulture);
                            }
                        }

                        foreach (XmlNode e in extr.ChildNodes)
                        {
                            if (e.Name.Equals("CoordinateSystem", StringComparison.OrdinalIgnoreCase))
                            {
                                foreach (XmlNode cs in e.ChildNodes)
                                {
                                    if (cs.Name.Equals("AxisX", StringComparison.OrdinalIgnoreCase))
                                    {
                                        foreach (XmlAttribute att in cs.Attributes)
                                        {
                                            switch (att.Name)
                                            {
                                                case "X":
                                                    extrusionInfo.System.AxisX.X = double.Parse(att.Value,
                                                        CultureInfo.InvariantCulture);
                                                    break;
                                                case "Y":
                                                    extrusionInfo.System.AxisX.Y = double.Parse(att.Value,
                                                        CultureInfo.InvariantCulture);
                                                    break;
                                                case "Z":
                                                    extrusionInfo.System.AxisX.Z = double.Parse(att.Value,
                                                        CultureInfo.InvariantCulture);
                                                    break;
                                            }
                                        }
                                    }
                                    else if (cs.Name.Equals("AxisY", StringComparison.OrdinalIgnoreCase))
                                    {
                                        foreach (XmlAttribute att in cs.Attributes)
                                        {
                                            switch (att.Name)
                                            {
                                                case "X":
                                                    extrusionInfo.System.AxisY.X = double.Parse(att.Value,
                                                        CultureInfo.InvariantCulture);
                                                    break;
                                                case "Y":
                                                    extrusionInfo.System.AxisY.Y = double.Parse(att.Value,
                                                        CultureInfo.InvariantCulture);
                                                    break;
                                                case "Z":
                                                    extrusionInfo.System.AxisY.Z = double.Parse(att.Value,
                                                        CultureInfo.InvariantCulture);
                                                    break;
                                            }
                                        }
                                    }
                                    else if (cs.Name.Equals("Origin", StringComparison.OrdinalIgnoreCase))
                                    {
                                        foreach (XmlAttribute att in cs.Attributes)
                                        {
                                            switch (att.Name)
                                            {
                                                case "X":
                                                    extrusionInfo.System.Origin.X = double.Parse(att.Value,
                                                        CultureInfo.InvariantCulture);
                                                    break;
                                                case "Y":
                                                    extrusionInfo.System.Origin.Y = double.Parse(att.Value,
                                                        CultureInfo.InvariantCulture);
                                                    break;
                                                case "Z":
                                                    extrusionInfo.System.Origin.Z = double.Parse(att.Value,
                                                        CultureInfo.InvariantCulture);
                                                    break;
                                            }
                                        }

                                        extrusionInfo.System.Origin = TransformPosition(extrusionInfo.System.Origin);
                                    }
                                }
                            }
                            else if (e.Name.Equals("VertexList", StringComparison.OrdinalIgnoreCase))
                            {
                                foreach (XmlNode v in e.ChildNodes)
                                {
                                    var vertex = Position.Create();
                                    foreach (XmlAttribute att in v.Attributes)
                                    {
                                        switch (att.Name)
                                        {
                                            case "X":
                                                vertex.X = double.Parse(att.Value, CultureInfo.InvariantCulture);
                                                break;
                                            case "Y":
                                                vertex.Y = double.Parse(att.Value, CultureInfo.InvariantCulture);
                                                break;
                                            case "Z":
                                                vertex.Z = double.Parse(att.Value, CultureInfo.InvariantCulture);
                                                break;
                                        }
                                    }

                                    vertex = TransformPosition(vertex);
                                    extrusionInfo.VertexList.Add(vertex);
                                }
                            }
                        }

                        if (extrusionInfo.VertexList.Count > 0 || extrusionInfo.Diameter > 0)
                            boo.Add(extrusionInfo);
                    }

                    if (boo.Count > 0)
                        result.Add(boo);
                }
            }
            return result;
        }

        internal static Position TransformPosition(Position input)
        {            
            return _globalMatrix.Transform(input.ConvertToPoint()).ConvertToPosition();            
        }

        internal static Point TransformPosition(Point input)
        {            
            return _globalMatrix.Transform(input);
        }

        /// <summary>
        /// Создание/изменение элементов типа SCTN
        /// </summary>
        /// <param name="row">строка таблицы данных с информацией по текущему элементу</param>
        /// <param name="zone">элемент ZONE, внутри которого будет вестись работа</param>
        /// <param name="spre">ссылка на спецификацию</param>
        /// <returns>true - если элементы были созданы/изменены без ошибок</returns>
        private static bool ModifySctns(ref DataRow row, DbElement zone, DbElement spre)
        {
            var pointList = GetPointsList((SqlXml)row["beam_points"],false);
            var sctnList = GetSctnInfoList((SqlXml)row["pdms_item"]);
            var booleansList = GetBooleansInfoList((SqlXml)row["beam_booleans"]);

            if (pointList.Count < sctnList.Count)
            {
                var diff = sctnList.Count - pointList.Count;
                for (int i = pointList.Count; i < sctnList.Count; i++)
                {
                    var item = sctnList[i].Element;
                    if (!item.IsNull && item.IsDeleteable)
                    {
                        item.Delete();
                        if (!item.IsDeleted)
                        {
                            RunE3D.SendMessage("Не удалось удалить элемент " + item.ElementType.ShortName + " " + item.GetAsString(DbAttributeInstance.REF));
                            return false;
                        }
                    }
                }

                sctnList.RemoveRange(pointList.Count, diff);
            }
            else if (pointList.Count > sctnList.Count)
            {
                var diff = pointList.Count - sctnList.Count;
                for (var i = 0; i < diff; i++)
                {
                    sctnList.Add(new SctnInfo { Element = DbElement.GetElement() });
                }
            }

            var prevElement = DbElement.GetElement();
            for (int i = 0; i < pointList.Count; i++)
            {
                var sctnInfo = sctnList[i];
                var point = pointList[i];
                var sctn = sctnInfo.Element;

                var root = DbElement.GetElement();
                var findRoot = GetRootByClass(row["beam_class"].ToString(), zone, ref root, DbElementTypeInstance.SCTN, point.Level);

                var newSctn = false;
                //изменился тип элемента
                //изменился сайт элемента
                if (!sctn.IsNull && (
                        sctn.ElementType != DbElementTypeInstance.SCTN || 
                        (!root.IsNull && sctn.EvaluateElement(_siteExpr) != root.EvaluateElement(_siteExpr))))
                {
                    sctn.Delete();
                    sctn = DbElement.GetElement();
                }

                if (sctn.IsNull || sctn.IsDeleted)
                {
                    if (findRoot && !root.IsNull && root.IsCreatable(DbElementTypeInstance.SCTN))
                    {
                        sctn = root.CreateLast(DbElementTypeInstance.SCTN);
                        sctnInfo.Element = sctn;
                        newSctn = true;
                    }
                    else
                    {
                        RunE3D.SendMessage("Не удалось создать элемент с GUID: " + row["beam_guid"]);
                        return false;
                    }
                }
                else if (!findRoot)
                {
                    RunE3D.SendMessage("Не найден родительский элемент для элемента с GUID " + row["beam_guid"]);
                    return false;
                }
                else if (!sctn.CheckOwnerOrMove(root))
                {
                    RunE3D.SendMessage("Не удалось переместить элемент " + sctn.ElementType.ShortName + " " + sctn.GetAsString(DbAttributeInstance.REF));
                    return false;
                }

                var changed = ChangeSctnAttrib(sctn, point, spre, prevElement);

                if (point.ProfileParameters.Count > 0)
                {
                    sctn.SetAttribute(DbAttributeInstance.DESP, point.ProfileParameters.Values.ToArray());
                }
                else
                {
                    var catr = spre.GetElement(DbAttributeInstance.CATR);
                    if (!catr.IsNull && catr.IsValid)
                    {
                        var catrParams = catr.GetDoubleArray(DbAttributeInstance.PARA);
                        var parametricCatr = true;
                        foreach (var d in catrParams)
                        {
                            if (d == 0.0) continue;

                            parametricCatr = false;
                            break;
                        }

                        if (catrParams.Length > 1 && parametricCatr)
                        {
                            if (!sctn.IsAttributeSetable(DbAttributeInstance.DESP))
                            {
                                changed = false;
                            }
                            else
                            {
                                sctn.SetAttribute(DbAttributeInstance.DESP, new[] { point.Height, point.Width });
                            }
                        }
                    }
                }

                if (!changed)
                {
                    if (newSctn)
                    {
                        sctn.Delete();
                    }
                    RunE3D.SendMessage("Не удалось "+(newSctn?"создать":"изменить")+" элемент с GUID " + row["beam_guid"]);
                    return false;
                }

                /*if (!newSctn)
                {
                    var members = new DBElementCollection(sctn, new TypeFilter(DbElementTypeInstance.FITTING)).Cast<DbElement>().ToList();
                    foreach (var memb in members)
                    {
                        memb.Delete();
                    }
                }*/

                if (booleansList.Count > 0)
                {
                    var currentBoolean = new List<ExtrusionInfo>();
                    if (i < booleansList.Count)
                    {
                        currentBoolean = booleansList[i];
                    }
                    //var firstCreate = true;
                    DbElement fitting = DbElement.GetElement(), template = DbElement.GetElement(), nextrusion = DbElement.GetElement(), loop = DbElement.GetElement();

                    var wrtPos = sctn.GetPosition(DbAttributeInstance.POS, new DbQualifier() { wrtQualifier= sctn.EvaluateElement(_siteExpr) });
                    var locPos = sctn.GetPosition(DbAttributeInstance.POS);
                    var deltaX = wrtPos.X - locPos.X;
                    var deltaY = wrtPos.Y - locPos.Y;
                    var deltaZ = wrtPos.Z - locPos.Z;

                    if (!sctn.IsNull && sctn.IsValid && sctn.IsCreatable(DbElementTypeInstance.FITTING))
                    {
                        fitting = sctn.Members(DbElementTypeInstance.FITTING).Length > 0
                            ? sctn.Members(DbElementTypeInstance.FITTING).First()
                            : sctn.CreateLast(DbElementTypeInstance.FITTING);

                        if (!fitting.IsNull && fitting.IsValid &&
                            (fitting.IsCreatable(DbElementTypeInstance.TMPLATE) ||
                             fitting.Members(DbElementTypeInstance.TMPLATE).Length > 0))
                        {
                            template = fitting.Members(DbElementTypeInstance.TMPLATE).Length > 0
                                ? fitting.Members(DbElementTypeInstance.TMPLATE).First()
                                : fitting.CreateLast(DbElementTypeInstance.TMPLATE);

                            if (template.IsNull || !template.IsValid ||
                                !template.IsCreatable(DbElementTypeInstance.NXTRUSION))
                            {
                                RunE3D.SendMessage("Не удалось создать негативы для элемента с GUID " +
                                                    row["beam_guid"]);
                                continue;
                            }
                        }
                    }

                    var nxtrList = template.Members(DbElementTypeInstance.NXTRUSION).ToList();
                   
                    if (nxtrList.Count > currentBoolean.Count)
                    {
                        var diff = nxtrList.Count - currentBoolean.Count;
                        for (var n = 0; n < diff; n++)
                        {
                            var nxtr = nxtrList.Last();
                            nxtr.Delete();
                            nxtrList.Remove(nxtr);
                        }
                    }
                    else if (nxtrList.Count < currentBoolean.Count)
                    {
                        var diff = currentBoolean.Count - nxtrList.Count;
                        for (var n = 0; n < diff; n++)
                        {
                            template.CreateLast(DbElementTypeInstance.NXTRUSION);
                            //Command.CreateCommand(template.GetAsString(DbAttributeInstance.REF) + " NEW NXTR").Run();
                        }
                    }

                    nxtrList = template.Members(DbElementTypeInstance.NXTRUSION).ToList();
                    for (var j = 0; j < currentBoolean.Count; j++)
                    {
                        var extrusionInfo = currentBoolean[j];

                        nextrusion = nxtrList[j];//template.CreateLast(DbElementTypeInstance.NXTRUSION);
                        loop = nextrusion.Members(DbElementTypeInstance.LOOP).Length > 0
                            ? nextrusion.Members(DbElementTypeInstance.LOOP).First()
                            : nextrusion.CreateLast(DbElementTypeInstance.LOOP);

                        nextrusion.SetAttribute(DbAttributeInstance.HEIG, extrusionInfo.Height);

                        var vX = new Vector(extrusionInfo.System.AxisX.X, extrusionInfo.System.AxisX.Y,
                            extrusionInfo.System.AxisX.Z);
                        var vY = new Vector(extrusionInfo.System.AxisY.X, extrusionInfo.System.AxisY.Y,
                            extrusionInfo.System.AxisY.Z);
                        TransformOrientation(ref vX, ref vY, fitting);
                        var oriTekla = vX.CreateOrientation(vY);
                        nextrusion.SetAttribute(DbAttributeInstance.ORI, oriTekla);

                        var fittOrientation = fitting.GetOrientation(DbAttributeInstance.ORI, new DbQualifier() { wrtQualifier = fitting.EvaluateElement(_siteExpr) });
                        var fittPos = fitting.GetPosition(DbAttributeInstance.POS);
                        var fittCS = fittOrientation.ConvertToCoordinateSystem(fittPos);
                        var fittMatrix = MatrixFactory.ToCoordinateSystem(fittCS);

                        var nextrusionPos =
                            fittMatrix.Transform(extrusionInfo.VertexList.First().ConvertToPoint()).ConvertToPosition();

                        var nextrusionCS =
                            nextrusion.GetOrientation(DbAttributeInstance.ORI, new DbQualifier() { wrtQualifier = nextrusion.EvaluateElement(_siteExpr) })
                                .ConvertToCoordinateSystem(extrusionInfo.VertexList.First());
                        var nextrusionMatrix = MatrixFactory.ToCoordinateSystem(nextrusionCS);
                       
                        nextrusion.SetAttribute(DbAttributeInstance.POS, nextrusionPos);

                        var vertexList = loop.Members(DbElementTypeInstance.VERTEX).ToList();
                        if (vertexList.Count > extrusionInfo.VertexList.Count)
                        {
                            var diff = vertexList.Count - extrusionInfo.VertexList.Count;
                            for (var v = 0; v < diff; v++)
                            {
                                var vert = vertexList.Last();
                                vert.Delete();
                                vertexList.Remove(vert);
                            }
                        }
                        else if (vertexList.Count < extrusionInfo.VertexList.Count)
                        {
                            var diff = extrusionInfo.VertexList.Count - vertexList.Count;
                            for (var v = 0; v < diff; v++)
                            {
                                loop.CreateLast(DbElementTypeInstance.VERTEX);
                                //Command.CreateCommand(loop.GetAsString(DbAttributeInstance.REF) + " NEW VERT").Run();
                            }
                        }

                        vertexList = loop.Members(DbElementTypeInstance.VERTEX).ToList();
                        for (var v = 0; v < extrusionInfo.VertexList.Count; v++)
                        {
                            var p = extrusionInfo.VertexList[v];
                            var vertex = vertexList[v];
                            var wrtPosition = nextrusionMatrix.Transform(p.ConvertToPoint()).ConvertToPosition();
                            vertex.SetAttribute(DbAttributeInstance.POS, wrtPosition);
                        }
                        /*foreach (var p in extrusionInfo.VertexList)
                        {
                            var vertex = loop.CreateLast(DbElementTypeInstance.VERTEX);
                            var wrtPosition = nextrusionMatrix.Transform(p.ConvertToPoint()).ConvertToPosition();
                            vertex.SetAttribute(DbAttributeInstance.POS, wrtPosition);
                        }*/
                    }
                }
                else
                {
                    var fittings = new DBElementCollection(sctn, new TypeFilter(DbElementTypeInstance.FITTING)).Cast<DbElement>().ToList();
                    foreach (var fitting in fittings)
                    {
                        fitting.Delete();
                    }
                }

                sctnInfo.Bangle = sctn.GetDouble(DbAttributeInstance.BANG);
                sctnInfo.drns = sctn.GetDirection(DbAttributeInstance.DRNS);
                sctnInfo.drne = sctn.GetDirection(DbAttributeInstance.DRNE);
                sctnInfo.Ori = sctn.GetOrientation(DbAttributeInstance.ORI);
                sctnInfo.Poss = sctn.GetPosition(DbAttributeInstance.POSS, new DbQualifier() { wrtQualifier = sctn.EvaluateElement(_siteExpr) });
                sctnInfo.Pose = sctn.GetPosition(DbAttributeInstance.POSE, new DbQualifier() { wrtQualifier = sctn.EvaluateElement(_siteExpr) });
                sctnInfo.Height = 0.0;
                sctnInfo.Width = 0.0;
                var desp = sctn.GetDoubleArray(DbAttributeInstance.DESP);
                if (desp.Length > 1)
                {
                    sctnInfo.Height = desp[0];
                    sctnInfo.Width = desp[1];
                }
                sctnList[i] = sctnInfo;
                prevElement = sctn;

                if (sctn.IsNull) continue;       
                sctn.SetAttributeIfChanged(UserAttributes.PolyTeklaGuid, row["beam_guid"].ToString().Trim());
                sctn.SetAttributeIfChanged(UserAttributes.PolyTeklaClass, row["beam_class"]);

                var hist = new ElementHistory(sctn,
                                row["beam_created_by"], row["beam_created"],
                                row["beam_modified_by"], row["beam_modified"],
                                string.Empty, DateTime.MinValue);
                histories.Add(hist);
            }

            row["pdms_item"] = CreateSctnInfoXml(sctnList);
            return true;
        }

        internal static void TransformOrientation(ref Vector vX, ref Vector vY, DbElement root)
        {
            var rootOri = root.GetOrientation(DbAttributeInstance.ORI, new DbQualifier() { wrtQualifier = root.EvaluateElement(_siteExpr) });
            var rootPos = root.GetPosition(DbAttributeInstance.POS, new DbQualifier() { wrtQualifier = root.EvaluateElement(_siteExpr) });
            var rootCS = rootOri.ConvertToCoordinateSystem(rootPos);
            var rootMatrix = MatrixFactory.ToCoordinateSystem(rootCS);

            var ori = new Point();
            var x = new Point(vX.X, vX.Y, vX.Z);
            var y = new Point(vY.X, vY.Y, vY.Z);

            var oriTransf = rootMatrix.Transform(ori);
            var xTransf = rootMatrix.Transform(x);
            var yTransf = rootMatrix.Transform(y);

            vX = new LineSegment(oriTransf, xTransf).GetDirectionVector();
            vY = new LineSegment(oriTransf, yTransf).GetDirectionVector();
        }

        /// <summary>
        /// Создание/изменение элементов типа FLOOR
        /// </summary>
        /// <param name="row">строка таблицы данных с информацией по текущему элементу</param>
        /// <param name="zone">элемент ZONE, внутри которого будет вестись работа</param>
        /// <returns>true - если элементы были созданы/изменены без ошибок</returns>
        private static bool ModifyFloors(ref DataRow row, DbElement zone)
        {
            var floorInfo = GetFloorInfoList((SqlXml)row["pdms_item"]);
            var pointList = GetPointsList((SqlXml)row["beam_points"], true);
            var booleansList = GetBooleansInfoList((SqlXml)row["beam_booleans"]);

            var floor = floorInfo.Element;
            var root = DbElement.GetElement();
            var level = pointList.Count > 0 ? pointList.First().Level : "0.000";
            var findRoot = GetRootByClass(row["beam_class"].ToString(), zone, ref root, DbElementTypeInstance.FLOOR,
                level);
            var newFloor = false;

            //изменился тип элемента
            //изменился сайт элемента
            if (!floor.IsNull && (
                    floor.ElementType != DbElementTypeInstance.FLOOR ||
                    (!root.IsNull && floor.EvaluateElement(_siteExpr) != root.EvaluateElement(_siteExpr))))
            {
                floor.Delete();
                floor = DbElement.GetElement();
            }

            if (floor.IsNull || !floor.IsValid || floor.IsDeleted)
            {
                if (findRoot && !root.IsNull && root.IsCreatable(DbElementTypeInstance.FLOOR))
                {
                    floor = root.CreateLast(DbElementTypeInstance.FLOOR);
                    floorInfo.Element = floor;
                    newFloor = true;
                }
                else
                {
                    RunE3D.SendMessage("Не удалось создать элемент с GUID " + row["beam_guid"]);
                    return false;
                }
            }
            else if (!findRoot)
            {
                RunE3D.SendMessage("Не найден родительский элемент для элемента с GUID " + row["beam_guid"]);
                return false;
            }
            else if (!floor.CheckOwnerOrMove(root))
            {
                RunE3D.SendMessage("Не удалось переместить элемент " + floor.ElementType.ShortName + " " +
                                   floor.GetAsString(DbAttributeInstance.REF));
                return false;
            }

            var ploo = floor.Members(DbElementTypeInstance.PLOOP).Length > 0
                ? floor.Members(DbElementTypeInstance.PLOOP).First()
                : DbElement.GetElement();
            if (ploo.IsNull)
            {
                if (!floor.IsCreatable(DbElementTypeInstance.PLOOP))
                {
                    if (newFloor)
                    {
                        RunE3D.SendMessage("Не удалось создать элемент с GUID " + row["beam_guid"]);
                        floor.Delete();
                    }

                    return false;
                }

                ploo = floor.CreateLast(DbElementTypeInstance.PLOOP);
            }

            if (pointList.Count > 0)
            {
                var firstPoint = pointList.First();
                var floorPosition = Position.Create(firstPoint.OriginPoint.X, firstPoint.OriginPoint.Y,
                    firstPoint.OriginPoint.Z);
                floor.SetAttribute(DbAttributeInstance.POS, floorPosition);

                var oriTekla = firstPoint.XVector.CreateOrientation(firstPoint.YVector);
                floor.SetAttribute(DbAttributeInstance.ORI, oriTekla);
            }

            var changed = ChangeFloor(ploo, floorInfo, pointList);

            if (!changed)
            {
                if (newFloor)
                {
                    floor.Delete();
                }

                RunE3D.SendMessage("Не удалось " + (newFloor ? "создать" : "изменить") + " элемент с GUID " +
                                   row["beam_guid"]);
                return false;
            }

            if (booleansList.Count > 0)
            {
                if (floor.IsNull || !floor.IsValid)
                {
                    RunE3D.SendMessage("Не удалось " + (newFloor ? "создать" : "изменить") + " элемент с GUID " +
                                       row["beam_guid"]);
                }

                ModifyFloorNxtrusions(floor, booleansList[0].Where(x => x.Diameter == 0.0).ToList());
                ModifyFloorNcylinder(floor, booleansList[0].Where(x => x.Diameter != 0.0).ToList());
            }
            else
            {
                var negativeList = new DBElementCollection(floor,
                        new TypeFilter(new[] { DbElementTypeInstance.NXTRUSION, DbElementTypeInstance.NCYLINDER }))
                    .Cast<DbElement>().ToList();
                foreach (var negative in negativeList)
                {
                    negative.Delete();
                }
            }

            var paveCollection = new DBElementCollection(floor, new TypeFilter(DbElementTypeInstance.PAVERT));
            var paveList = new List<PaveInfo>();
            foreach (DbElement pave in paveCollection)
            {
                paveList.Add(new PaveInfo
                {
                    Element = pave,
                    Position = pave.GetPosition(DbAttributeInstance.POS,
                        new DbQualifier() { wrtQualifier = pave.EvaluateElement(_siteExpr) })
                });
            }

            floorInfo.Points = paveList;
            floorInfo.Height = ploo.GetDouble(DbAttributeInstance.HEIG);

            row["pdms_item"] = CreateFloorInfoXml(floorInfo);
            if (!floor.IsNull)
            {
                floor.SetAttributeIfChanged(UserAttributes.PolyTeklaGuid, row["beam_guid"].ToString().Trim());
                floor.SetAttributeIfChanged(UserAttributes.PolyTeklaClass, row["beam_class"]);
                var desc = row["beam_description"] == DBNull.Value ? string.Empty : (string)row["beam_description"];
                if (!string.IsNullOrEmpty(desc))
                {
                    floor.SetAttributeIfChanged(DbAttributeInstance.DESC, desc);
                }

                var hist = new ElementHistory(floor,
                    row["beam_created_by"], row["beam_created"],
                    row["beam_modified_by"], row["beam_modified"],
                    string.Empty, DateTime.MinValue);

                histories.Add(hist);
            }

            return true;
        }

        /// <summary>
        /// Создание негативных cylinder в элементе floor
        /// </summary>
        /// <param name="floor"></param>
        /// <param name="booleans"></param>
        private static void ModifyFloorNcylinder(DbElement floor, List<ExtrusionInfo> booleans)
        {
            var ncylList = floor.Members(DbElementTypeInstance.NCYLINDER).ToList();
            if (ncylList.Count > booleans.Count)
            {
                var diff = ncylList.Count - booleans.Count;
                for (var i = 0; i < diff; i++)
                {
                    var ncyl = ncylList.Last();
                    ncyl.Delete();
                    ncylList.Remove(ncyl);
                }
            }
            else if (ncylList.Count < booleans.Count)
            {
                var diff = booleans.Count - ncylList.Count;
                for (var i = 0; i < diff; i++)
                {
                    floor.CreateLast(DbElementTypeInstance.NCYLINDER);
                }
            }

            ncylList = floor.Members(DbElementTypeInstance.NCYLINDER).ToList();
            if (ncylList.Count != booleans.Count)
                return;

            for (var i = 0; i < booleans.Count; i++)
            {
                var extrusionInfo = booleans[i];
                var ncylinder = ncylList[i];

                if (ncylinder.IsNull || !ncylinder.IsValid)
                    continue;

                ncylinder.SetAttribute(DbAttributeInstance.HEIG, extrusionInfo.Height);
                ncylinder.SetAttribute(DbAttributeInstance.DIAM, extrusionInfo.Diameter);

                var vX = new Vector(extrusionInfo.System.AxisX.X, extrusionInfo.System.AxisX.Y,
                    extrusionInfo.System.AxisX.Z);
                var vY = new Vector(extrusionInfo.System.AxisY.X, extrusionInfo.System.AxisY.Y,
                    extrusionInfo.System.AxisY.Z);
                var vZ = vX.Cross(vY).GetNormal();

                extrusionInfo.System.Origin.Translate(vZ.X * extrusionInfo.Height / 2.0, vZ.Y * extrusionInfo.Height / 2.0, vZ.Z * extrusionInfo.Height / 2.0);

                TransformOrientation(ref vX, ref vY, floor);
                var oriTekla = vX.CreateOrientation(vY);
                ncylinder.SetAttribute(DbAttributeInstance.ORI, oriTekla);

                var flooOrientation = floor.GetOrientation(DbAttributeInstance.ORI,
                    new DbQualifier() {wrtQualifier = floor.EvaluateElement(_siteExpr)});
                var flooPos = floor.GetPosition(DbAttributeInstance.POS);
                var flooCS = flooOrientation.ConvertToCoordinateSystem(flooPos);
                var flooMatrix = MatrixFactory.ToCoordinateSystem(flooCS);
                var ncylinderPos = flooMatrix.Transform(extrusionInfo.System.Origin).ConvertToPosition();
                
                ncylinder.SetAttribute(DbAttributeInstance.POS, ncylinderPos);
            }
        }

        /// <summary>
        /// Создание негативных extrusion в элементе floor
        /// </summary>
        /// <param name="floor"></param>
        /// <param name="booleans"></param>
        private static void ModifyFloorNxtrusions(DbElement floor, List<ExtrusionInfo> booleans)
        {
            var nxtrList = floor.Members(DbElementTypeInstance.NXTRUSION).ToList();
            if (nxtrList.Count > booleans.Count)
            {
                var diff = nxtrList.Count - booleans.Count;
                for (var i = 0; i < diff; i++)
                {
                    var nxtr = nxtrList.Last();
                    nxtr.Delete();
                    nxtrList.Remove(nxtr);
                }
            }
            else if (nxtrList.Count < booleans.Count)
            {
                var diff = booleans.Count - nxtrList.Count;
                for (var i = 0; i < diff; i++)
                {
                    floor.CreateLast(DbElementTypeInstance.NXTRUSION);
                }
            }

            nxtrList = floor.Members(DbElementTypeInstance.NXTRUSION).ToList();
            if (nxtrList.Count != booleans.Count)
                return;

            for (var i = 0; i < booleans.Count; i++)
            {
                var extrusionInfo = booleans[i];
                var nextrusion = nxtrList[i];

                if (nextrusion.IsNull || !nextrusion.IsValid) continue;

                var loop = nextrusion.Members(DbElementTypeInstance.LOOP).Length > 0
                    ? nextrusion.Members(DbElementTypeInstance.LOOP).First()
                    : nextrusion.CreateLast(DbElementTypeInstance.LOOP);

                if (loop.IsNull || !loop.IsValid) continue;

                nextrusion.SetAttribute(DbAttributeInstance.HEIG, extrusionInfo.Height);

                var vX = new Vector(extrusionInfo.System.AxisX.X, extrusionInfo.System.AxisX.Y,
                    extrusionInfo.System.AxisX.Z);
                var vY = new Vector(extrusionInfo.System.AxisY.X, extrusionInfo.System.AxisY.Y,
                    extrusionInfo.System.AxisY.Z);
                TransformOrientation(ref vX, ref vY, floor);
                var oriTekla = vX.CreateOrientation(vY);
                nextrusion.SetAttribute(DbAttributeInstance.ORI, oriTekla);

                var flooOrientation = floor.GetOrientation(DbAttributeInstance.ORI,
                    new DbQualifier() {wrtQualifier = floor.EvaluateElement(_siteExpr)});
                var flooPos = floor.GetPosition(DbAttributeInstance.POS);
                var flooCS = flooOrientation.ConvertToCoordinateSystem(flooPos);
                var flooMatrix = MatrixFactory.ToCoordinateSystem(flooCS);
                var nextrusionPos = flooMatrix.Transform(extrusionInfo.VertexList.First().ConvertToPoint())
                    .ConvertToPosition();

                var nextrusionCS = nextrusion
                    .GetOrientation(DbAttributeInstance.ORI,
                        new DbQualifier() {wrtQualifier = nextrusion.EvaluateElement(_siteExpr)})
                    .ConvertToCoordinateSystem(extrusionInfo.VertexList.First());
                var nextrusionMatrix = MatrixFactory.ToCoordinateSystem(nextrusionCS);
                nextrusion.SetAttribute(DbAttributeInstance.POS, nextrusionPos);

                var vertexList = loop.Members(DbElementTypeInstance.VERTEX).ToList();
                if (vertexList.Count > extrusionInfo.VertexList.Count)
                {
                    var diff = vertexList.Count - extrusionInfo.VertexList.Count;
                    for (var v = 0; v < diff; v++)
                    {
                        var vert = vertexList.Last();
                        vert.Delete();
                        vertexList.Remove(vert);
                    }
                }
                else if (vertexList.Count < extrusionInfo.VertexList.Count)
                {
                    var diff = extrusionInfo.VertexList.Count - vertexList.Count;
                    for (var v = 0; v < diff; v++)
                    {
                        loop.CreateLast(DbElementTypeInstance.VERTEX);
                    }
                }

                vertexList = loop.Members(DbElementTypeInstance.VERTEX).ToList();
                for (var v = 0; v < extrusionInfo.VertexList.Count; v++)
                {
                    var p = extrusionInfo.VertexList[v];
                    var vertex = vertexList[v];
                    var wrtPosition = nextrusionMatrix.Transform(p.ConvertToPoint()).ConvertToPosition();
                    vertex.SetAttribute(DbAttributeInstance.POS, wrtPosition);
                }
            }
        }

        /// <summary>
        /// Создание/изменение точек элемента FLOOR
        /// </summary>
        /// <param name="ploo">дочерний элемент FLOOR</param>
        /// <param name="floorInfo">информация об элементе</param>
        /// <returns></returns>
        private static bool ChangeFloor(DbElement ploo, FloorInfo floorInfo, List<TeklaElementPoints> pointList)
        {
            if (pointList.Count == 0) return false;
            floorInfo.Height = pointList.First().Height;
            if (ploo.IsAttributeSetable(DbAttributeInstance.HEIG) && ploo.IsAttributeSetable(DbAttributeInstance.SJUS))
            {
                ploo.SetAttribute(DbAttributeInstance.HEIG, floorInfo.Height);
                ploo.SetAttribute(DbAttributeInstance.SJUS, "UCEN");
            }
            else if (ploo.GetDouble(DbAttributeInstance.HEIG) != floorInfo.Height)
            {
                return false;
            }

            //var floorPosition = ploo.Owner.GetPosition(DbAttributeInstance.POS);
            var positionList = new List<Position>();
            foreach (var p in pointList)
            {
                //positionList.Add(Position.Create(p.StartPoint.X - floorPosition.X, p.StartPoint.Y - floorPosition.Y, p.StartPoint.Z - floorPosition.Z));
                positionList.Add(Position.Create(p.StartPoint.X, p.StartPoint.Y, p.StartPoint.Z));
            }
            //positionList.Add(Position.Create(pointList.Last().EndPoint.X - floorPosition.X, pointList.Last().EndPoint.Y - floorPosition.Y, pointList.Last().EndPoint.Z - floorPosition.Z)); ;
            positionList.Add(Position.Create(pointList.Last().EndPoint.X, pointList.Last().EndPoint.Y, pointList.Last().EndPoint.Z));
            if (positionList.Count < floorInfo.Points.Count)
            {
                var diff = floorInfo.Points.Count - positionList.Count;
                for (int i = positionList.Count - 1; i < floorInfo.Points.Count; i++)
                {
                    var item = floorInfo.Points[i].Element;
                    if (!item.IsNull && item.IsDeleteable)
                    {
                        item.Delete();
                        if (!item.IsDeleted) return false;
                    }
                }
                floorInfo.Points.RemoveRange(positionList.Count, diff);
            }
            else if (positionList.Count > floorInfo.Points.Count)
            {
                var diff = positionList.Count - floorInfo.Points.Count;
                for (int i = 0; i < diff; i++)
                {
                    floorInfo.Points.Add(new PaveInfo { Element = DbElement.GetElement() });
                }
            }

            var pavertList = ploo.Members(DbElementTypeInstance.PAVERT).ToList();
            if (pavertList.Count > floorInfo.Points.Count)
            {
                var needPavert = floorInfo.Points.Select(p => p.Element).ToList();
                foreach (var pavert in pavertList)
                {
                    if (needPavert.Contains(pavert)) continue;
                    if (!pavert.IsNull && pavert.IsDeleteable)
                    {
                        pavert.Delete();
                        if (!pavert.IsDeleted)
                        {
                            RunE3D.SendMessage("[ModifyFloor] Не удалось удалить лишний элемент PAVE " +
                                                pavert.GetAsString(DbAttributeInstance.REF));
                        }
                    }
                    else
                    {
                        RunE3D.SendMessage("[ModifyFloor] Не удалось удалить лишний элемент PAVE " +
                                                pavert.GetAsString(DbAttributeInstance.REF));
                    }
                }
            }

            for (int i = 0; i < positionList.Count; i++)
            {
                var position = positionList[i];
                var pave = floorInfo.Points[i].Element;
                if (pave.IsNull)
                {
                    if (!ploo.IsCreatable(DbElementTypeInstance.PAVERT)) return false;
                    pave = ploo.CreateLast(DbElementTypeInstance.PAVERT);
                }

                if (pave.IsAttributeSetable(DbAttributeInstance.POS) && pave.IsAttributeValid(DbAttributeInstance.POS))
                {
                    pave.SetAttribute(DbAttributeInstance.POS, position);
                }
                else
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Создание SqlXml по элементу FLOOR
        /// </summary>
        /// <param name="floorInfo">информация об элементе</param>
        /// <returns></returns>
        private static object CreateFloorInfoXml(FloorInfo floorInfo)
        {
            var doc = new XmlDocument();

            var xmlDeclaration = doc.CreateXmlDeclaration("1.0", "UTF-8", null);
            var root = doc.DocumentElement;
            doc.InsertBefore(xmlDeclaration, root);
            var rootNode = doc.CreateElement(string.Empty, "Root", string.Empty);
            doc.AppendChild(rootNode);

            var xmlFloor = doc.CreateElement(string.Empty, "Floor", string.Empty);
            xmlFloor.SetAttribute("Element", floorInfo.Element.GetAsString(DbAttributeInstance.REF));
            xmlFloor.SetAttribute("Height", Math.Round(floorInfo.Height, 4).ToString(CultureInfo.InvariantCulture));
            foreach (var pave in floorInfo.Points)
            {
                var xmlPave = doc.CreateElement(string.Empty, "Pave", string.Empty);
                xmlPave.SetAttribute("Element", pave.Element.GetAsString(DbAttributeInstance.REF));
                xmlPave.SetAttribute("X", Math.Round(pave.Position.X, 4).ToString(CultureInfo.InvariantCulture));
                xmlPave.SetAttribute("Y", Math.Round(pave.Position.Y, 4).ToString(CultureInfo.InvariantCulture));
                xmlPave.SetAttribute("Z", Math.Round(pave.Position.Z, 4).ToString(CultureInfo.InvariantCulture));

                xmlFloor.AppendChild(xmlPave);
            }
            rootNode.AppendChild(xmlFloor);

            return Common.XmlDocToSqlXml(doc);
        }

        private static object CreateConcreteInfoXml(DbElement floor)
        {
            var doc = new XmlDocument();
            var xmlDeclaration = doc.CreateXmlDeclaration("1.0", "UTF-8", null);
            var root = doc.DocumentElement;
            doc.InsertBefore(xmlDeclaration, root);
            var itemNode = doc.CreateElement(string.Empty, "Item", string.Empty);
            itemNode.SetAttribute("Element", floor.GetAsString(DbAttributeInstance.REF));
            doc.AppendChild(itemNode);
            return Common.XmlDocToSqlXml(doc);
        }

        private static bool ModifyShapeElement(DbElement zone, ref DataRow row)
        {
            var floor = GetShapeItem((SqlXml)row["pdms_item"]);

            var concreteDictionary = GetShapeGeometry((SqlXml)row["beam_points"], out string level, out bool hasUndefinedGeometry);            

            if (hasUndefinedGeometry)
            {
                RunE3D.SendMessage($"Неизвестная геометрия у элемента с GUID {row["beam_guid"]}");
                return false;
            }

            foreach (var pair in concreteDictionary)
            {
                if (pair.Key is Helpers.ExtrGeometry extr && extr.Height == 0.0)
                {
                    RunE3D.SendMessage($"Высота одного из примитивов не задана у элемента с GUID {row["beam_guid"]}");
                    return false;
                }
                foreach (var val in pair.Value)
                {
                    if (val is Helpers.ExtrGeometry nextr && nextr.Height == 0.0)
                    {
                        RunE3D.SendMessage($"Высота одного из негативных примитивов не задана у элемента с GUID {row["beam_guid"]}");
                        return false;
                    }
                }
            }

            var root = DbElement.GetElement();
            if (!GetRootByClass(row["beam_class"].ToString(), zone, ref root, DbElementTypeInstance.FLOOR, level))
            {
                RunE3D.SendMessage($"Не найден родительский элемент для элемента с GUID {row["beam_guid"]}");
                return false;
            }

            if (!CreateOrUpdateShapeItem(root, concreteDictionary, ref floor))
            {
                RunE3D.SendMessage($"Не удалось обработать элемент с GUID {row["beam_guid"]}");
                return false;
            }

            floor.SetAttributeIfChanged(UserAttributes.PolyTeklaGuid, row["beam_guid"].ToString().Trim());
            floor.SetAttributeIfChanged(UserAttributes.PolyTeklaClass, row["beam_class"]);

            var desc = row["beam_description"] == DBNull.Value ? string.Empty : (string)row["beam_description"];
            floor.SetAttributeIfChanged(DbAttributeInstance.DESC, desc);

            row["pdms_item"] = CreateConcreteInfoXml(floor);

            if (!floor.IsNull)
            {
                var hist = new ElementHistory(floor,
                    row["beam_created_by"], row["beam_created"],
                    row["beam_modified_by"], row["beam_modified"],
                    string.Empty, DateTime.MinValue);

                histories.Add(hist);
                return true;
            }

            return false;

            //var nxtrArray = floor.Members(DbElementTypeInstance.NXTRUSION);
            //var negatives = concreteDictionary[firstGeometry];
            //var diff = nxtrArray.Length - negatives.Count;
            //if (diff > 0)
            //{
            //    for (var i = 0; i < diff; i++)
            //        nxtrArray[nxtrArray.Length - 1 - i].Delete();
            //    nxtrArray = floor.Members(DbElementTypeInstance.NXTRUSION);
            //}

            //for (var i = 0; i < negatives.Count; i++)
            //{
            //    var nxtr = i < nxtrArray.Length ? nxtrArray[i] : DbElement.GetElement();
            //    if (CreateExtrusion(floor, negatives[i], false, ref nxtr)) continue;

            //    if (!floorExist) floor.Delete();
            //    RunE3D.SendMessage($"Не удалось обработать элемент с GUID {row["beam_guid"]}");
            //    return false;
            //}

            //concreteDictionary.Remove(firstGeometry);
            //if (concreteDictionary.Count > 0)
            //{
            //    var tmpl = floor.Members(DbElementTypeInstance.TMPLATE).FirstOrDefault();
            //    if (tmpl == null || tmpl.IsNull)
            //        tmpl = floor.CreateLast(DbElementTypeInstance.TMPLATE);
            //    var extrArray = tmpl.Members(DbElementTypeInstance.EXTRUSION);
            //    diff = extrArray.Length - concreteDictionary.Count;
            //    if (diff > 0)
            //    {
            //        for (var i = 0; i < diff; i++)
            //            extrArray[extrArray.Length - 1 - i].Delete();
            //        extrArray = tmpl.Members(DbElementTypeInstance.EXTRUSION);
            //    }

            //    var index = 0;
            //    foreach (var pair in concreteDictionary)
            //    {
            //        var positivePart = pair.Key;
            //        var extr = index < extrArray.Length ? extrArray[index] : DbElement.GetElement();
            //        if (!CreateExtrusion(tmpl, positivePart, true, ref extr))
            //        {
            //            if (!floorExist) floor.Delete();
            //            RunE3D.SendMessage($"Не удалось обработать элемент с GUID {row["beam_guid"]}");
            //            return false;
            //        }

            //        nxtrArray = extr.Members(DbElementTypeInstance.NXTRUSION);
            //        negatives = pair.Value;
            //        diff = nxtrArray.Length - negatives.Count;
            //        if (diff > 0)
            //        {
            //            for (var i = 0; i < diff; i++)
            //                nxtrArray[nxtrArray.Length - 1 - i].Delete();
            //            nxtrArray = extr.Members(DbElementTypeInstance.NXTRUSION);
            //        }

            //        for (var i = 0; i < negatives.Count; i++)
            //        {
            //            var nxtr = i < nxtrArray.Length ? nxtrArray[i] : DbElement.GetElement();
            //            if (CreateExtrusion(extr, negatives[i], false, ref nxtr)) continue;

            //            if (!floorExist) floor.Delete();
            //            RunE3D.SendMessage($"Не удалось обработать элемент с GUID {row["beam_guid"]}");
            //            return false;
            //        }

            //        index++;
            //    }
            //}


        }

        //private static bool ModifyShapeElement(DbElement zone, ref DataRow row)
        //{
        //    var floor = GetShapeItem((SqlXml)row["pdms_item"]);
        //    var floorExist = !floor.IsNull;
        //    var concreteDictionary = GetConcreteInfo((SqlXml)row["beam_points"] ,out string level);

        //    var mainPart = concreteDictionary.Keys.First();                 

        //    var root = DbElement.GetElement();                      
        //    if (!GetRootByClass(row["beam_class"].ToString(), zone, ref root, DbElementTypeInstance.FLOOR, level))
        //    {
        //        RunE3D.SendMessage("Не найден родительский элемент для элемента с GUID " + row["beam_guid"]);
        //        return false;
        //    }

        //    if (!CreateFloor(root, mainPart, ref floor))
        //    {
        //        RunE3D.SendMessage($"Не удалось обработать элемент с GUID {row["beam_guid"]}");
        //        return false;
        //    }
        //    floor.SetAttribute(_polyTeklaGuid, row["beam_guid"].ToString().Trim());

        //    var nxtrArray = floor.Members(DbElementTypeInstance.NXTRUSION);
        //    var negatives = concreteDictionary[mainPart];
        //    var diff = nxtrArray.Length - negatives.Count;
        //    if (diff > 0)
        //    {
        //        for (var i = 0; i < diff; i++)
        //            nxtrArray[nxtrArray.Length - 1 - i].Delete();
        //        nxtrArray = floor.Members(DbElementTypeInstance.NXTRUSION);
        //    }

        //    for (var i = 0; i < negatives.Count; i++)
        //    {
        //        var nxtr = i < nxtrArray.Length ? nxtrArray[i] : DbElement.GetElement();
        //        if (CreateExtrusion(floor, negatives[i], false, ref nxtr)) continue;

        //        if (!floorExist) floor.Delete();
        //        RunE3D.SendMessage($"Не удалось обработать элемент с GUID {row["beam_guid"]}");
        //        return false;
        //    }

        //    concreteDictionary.Remove(mainPart);
        //    if (concreteDictionary.Count > 0)
        //    {
        //        var tmpl = floor.Members(DbElementTypeInstance.TMPLATE).FirstOrDefault();
        //        if (tmpl == null || tmpl.IsNull)
        //            tmpl = floor.CreateLast(DbElementTypeInstance.TMPLATE);
        //        var extrArray = tmpl.Members(DbElementTypeInstance.EXTRUSION);
        //        diff = extrArray.Length - concreteDictionary.Count;
        //        if (diff > 0)
        //        {
        //            for (var i = 0; i < diff; i++)
        //                extrArray[extrArray.Length - 1 - i].Delete();
        //            extrArray = tmpl.Members(DbElementTypeInstance.EXTRUSION);
        //        }

        //        var index = 0;
        //        foreach (var pair in concreteDictionary)
        //        {
        //            var positivePart = pair.Key;
        //            var extr = index < extrArray.Length ? extrArray[index] : DbElement.GetElement();
        //            if (!CreateExtrusion(tmpl, positivePart, true, ref extr))
        //            {
        //                if (!floorExist) floor.Delete();
        //                RunE3D.SendMessage($"Не удалось обработать элемент с GUID {row["beam_guid"]}");
        //                return false;
        //            }                   

        //            nxtrArray = extr.Members(DbElementTypeInstance.NXTRUSION);
        //            negatives = pair.Value;
        //            diff = nxtrArray.Length - negatives.Count;
        //            if (diff > 0)
        //            {
        //                for (var i = 0; i < diff; i++)
        //                    nxtrArray[nxtrArray.Length - 1 - i].Delete();
        //                nxtrArray = extr.Members(DbElementTypeInstance.NXTRUSION);
        //            }

        //            for (var i = 0; i < negatives.Count; i++)
        //            {
        //                var nxtr = i < nxtrArray.Length ? nxtrArray[i] : DbElement.GetElement();
        //                if (CreateExtrusion(extr, negatives[i], false, ref nxtr)) continue;

        //                if (!floorExist) floor.Delete();
        //                RunE3D.SendMessage($"Не удалось обработать элемент с GUID {row["beam_guid"]}");
        //                return false;
        //            }

        //            index++;
        //        }                
        //    }   

        //    row["pdms_item"] = CreateBeamPadInfoXml(floor);
        //    if (!floor.IsNull)
        //    {
        //        var hist = new ElementHistory(floor,
        //                        row["beam_created_by"], row["beam_created"],
        //                        row["beam_modified_by"], row["beam_modified"],
        //                        string.Empty, DateTime.MinValue);

        //        histories.Add(hist);                
        //    }
        //    return true;
        //}

        private static Dictionary<Helpers.AvevaGeometry, List<Helpers.AvevaGeometry>> GetShapeGeometry(SqlXml sqlXml, out string level, out bool hasUndefinedGeometry)
        {
            var result = new Dictionary<Helpers.AvevaGeometry, List<Helpers.AvevaGeometry>>();
            var doc = Common.SqlXmlToXmlDoc(sqlXml);
            var rootNode = doc.GetElementsByTagName("Root")[0];
            level = rootNode.Attributes["Level"].Value;
            hasUndefinedGeometry = rootNode.Attributes["undef"] != null;
            if (hasUndefinedGeometry) return result;
            foreach (XmlNode positiveItemNode in rootNode.ChildNodes)
            {
                var positiveGeometry = Helpers.AvevaGeometry.GetAvevaGeometry(positiveItemNode, true);
                if (!result.ContainsKey(positiveGeometry))
                    result.Add(positiveGeometry, new List<Helpers.AvevaGeometry>());
                foreach (XmlNode negatives in positiveItemNode.ChildNodes)
                {
                    if (negatives.Name != "Negatives") continue;
                    foreach (XmlNode negativeItemNode in negatives.ChildNodes)
                    {
                        var negativeGeometry = Helpers.AvevaGeometry.GetAvevaGeometry(negativeItemNode, false);
                        result[positiveGeometry].Add(negativeGeometry);
                    }
                }
            }
            return result;
        }

        private static bool CreateOrUpdateShapeItem(DbElement root, Dictionary<Helpers.AvevaGeometry, List<Helpers.AvevaGeometry>> geometryDict, ref DbElement floor)
        {
            var wrtSiteQualifier = new DbQualifier() { wrtQualifier = root.EvaluateElement(_siteExpr) };

            //изменился тип элемента
            //изменился сайт элемента
            if (!floor.IsNull && (floor.ElementType != DbElementTypeInstance.FLOOR ||
                                  (!root.IsNull && floor.EvaluateElement(_siteExpr) !=
                                      root.EvaluateElement(_siteExpr))))
            {
                floor.Delete();
                floor = DbElement.GetElement();
            }

            if (floor.IsNull || !floor.IsValid || floor.IsDeleted)
            {
                if (!root.IsCreatable(DbElementTypeInstance.FLOOR))
                    return false;

                floor = root.CreateLast(DbElementTypeInstance.FLOOR);
            }
            else if (!floor.CheckOwnerOrMove(root))
            {
                return false;
            }            

            var firstGeometry = geometryDict.First().Key;
            var originPosition = TransformPosition(firstGeometry.CoordSystem.Origin).ConvertToPosition();
            if (floor.GetPosition(DbAttributeInstance.POS, wrtSiteQualifier) != originPosition)
            {
                floor.SetAttribute(DbAttributeInstance.POS, wrtSiteQualifier, originPosition, out _);
            }

            var originOrientation = firstGeometry.CoordSystem.AxisX.CreateOrientation(firstGeometry.CoordSystem.AxisY);
            if (floor.GetOrientation(DbAttributeInstance.ORI, wrtSiteQualifier) != originOrientation)
            {
                floor.SetAttribute(DbAttributeInstance.ORI, wrtSiteQualifier, originOrientation, out _);
            }

            foreach (var memb in floor.Members())
            {
                if (memb.ElementType == DbElementTypeInstance.TMPLATE) 
                    continue;

                memb.Delete();
            }            

            var tmpl = floor.Members(DbElementTypeInstance.TMPLATE).FirstOrDefault();
            if (tmpl == null || tmpl.IsNull)
            {
                tmpl = floor.CreateLast(DbElementTypeInstance.TMPLATE);
            }

            var membersOfCurrent = tmpl.Members();
            var positiveGeomList = geometryDict.Keys.ToList();
            for (var i = positiveGeomList.Count; i < membersOfCurrent.Length; i++)
            {
                membersOfCurrent[i].Delete();
            }

            for (var i = 0; i < positiveGeomList.Count; i++)
            {
                membersOfCurrent = tmpl.Members();
                var positiveDbElement = membersOfCurrent.Length - 1 >= i ? membersOfCurrent[i] : DbElement.GetElement();
                if (!positiveDbElement.IsNull && positiveDbElement.ElementType != positiveGeomList[i].AvevaElementType)
                {
                    for (var j = i; j < membersOfCurrent.Length; j++)
                    {
                        membersOfCurrent[j].Delete();
                    }
                }

                if (positiveDbElement.IsNull || positiveDbElement.IsDeleted)
                {
                    positiveDbElement = tmpl.CreateLast(positiveGeomList[i].AvevaElementType);
                }

                positiveGeomList[i].SetGeometryAttributes(positiveDbElement, wrtSiteQualifier);

                var membersOfCurrentPositive = positiveDbElement.Members().ToList().Where(x =>
                        x.ElementType != DbElementTypeInstance.LOOP &&
                        x.ElementType != DbElementTypeInstance.POLPTLIST &&
                        x.ElementType != DbElementTypeInstance.POLFACE).ToArray();
                var negativeGeomList = geometryDict[positiveGeomList[i]];
                for (var j = negativeGeomList.Count; j < membersOfCurrentPositive.Length; j++)
                {
                    membersOfCurrentPositive[j].Delete();
                }

                for (var j = 0; j < negativeGeomList.Count; j++)
                {
                    membersOfCurrentPositive = positiveDbElement.Members().ToList().Where(x =>
                        x.ElementType != DbElementTypeInstance.LOOP &&
                        x.ElementType != DbElementTypeInstance.POLPTLIST &&
                        x.ElementType != DbElementTypeInstance.POLFACE).ToArray();
                    var negativeDbElement = membersOfCurrentPositive.Length - 1 >= j
                        ? membersOfCurrentPositive[j]
                        : DbElement.GetElement();
                    if (!negativeDbElement.IsNull &&
                        negativeDbElement.ElementType != negativeGeomList[j].AvevaElementType)
                    {
                        for (var k = j; k < membersOfCurrentPositive.Length; k++)
                        {
                            membersOfCurrentPositive[k].Delete();
                        }
                    }

                    if (negativeDbElement.IsNull || negativeDbElement.IsDeleted)
                    {
                        negativeDbElement = positiveDbElement.CreateLast(negativeGeomList[j].AvevaElementType);
                    }

                    negativeGeomList[j].SetGeometryAttributes(negativeDbElement, wrtSiteQualifier);
                }
            }

            return true;
        }

        private static bool CreateExtrusion(DbElement root, ExtrusionInfo extrusionInfo, bool positive, ref DbElement item)
        {
            var wrtSiteQualifier = new DbQualifier() { wrtQualifier = root.EvaluateElement(_siteExpr) };
            var owner = root.ElementType == DbElementTypeInstance.TMPLATE ? root.Owner : root;

            var ownerOri = owner.GetOrientation(DbAttributeInstance.ORI, wrtSiteQualifier);
            var ownerPos = owner.GetPosition(DbAttributeInstance.POS, wrtSiteQualifier);
            var ownerCs = ownerOri.ConvertToCoordinateSystem(ownerPos);
            var matrix = MatrixFactory.ToCoordinateSystem(ownerCs);

            var extrusionType = positive ? DbElementTypeInstance.EXTRUSION : DbElementTypeInstance.NXTRUSION;
        
            if (!root.GetElementTypeArray(DbAttributeInstance.LIST).ToList().Contains(extrusionType)) return false;
            if (!item.IsNull && item.ElementType != extrusionType)
            {
                item.Delete();
                item = DbElement.GetElement();
            }

            var newItem = false;
            if (item.IsNull)
            {
                item = root.CreateLast(extrusionType);
                newItem = true;
            }            

            var originPosition = extrusionInfo.VertexList.FirstOrDefault();
            if (originPosition == null)
            {
                if (newItem) item.Delete();
                return false;
            }

            originPosition = TransformPosition(originPosition);
            if (item.GetPosition(DbAttributeInstance.POS, wrtSiteQualifier) != originPosition)
                item.SetAttribute(DbAttributeInstance.POS, wrtSiteQualifier, matrix.Transform(originPosition.ConvertToPoint()).ConvertToPosition(), out PdmsMessage floorError);

            var avevaOri = extrusionInfo.System.AxisX.CreateOrientation(extrusionInfo.System.AxisY);
            if (item.GetOrientation(DbAttributeInstance.ORI) != avevaOri)
                item.SetAttribute(DbAttributeInstance.ORI, wrtSiteQualifier, avevaOri, out PdmsMessage oriError);

            if (item.GetDouble(DbAttributeInstance.HEIG) != extrusionInfo.Height)
                item.SetAttribute(DbAttributeInstance.HEIG, extrusionInfo.Height);

            var loop = item.Members(DbElementTypeInstance.LOOP).FirstOrDefault();

            if (loop == null || loop.IsNull) loop = item.CreateLast(DbElementTypeInstance.LOOP);

            var itemMatrix = MatrixFactory.ToCoordinateSystem(avevaOri.ConvertToCoordinateSystem(originPosition));

            var vertArray = loop.Members(DbElementTypeInstance.VERTEX);
            var diff = vertArray.Length - extrusionInfo.VertexList.Count;
            if (diff < 0)
            {
                for (var i = Math.Abs(diff); i > 0; i--)
                    loop.CreateLast(DbElementTypeInstance.VERTEX);
            }
            else if (diff > 0)
            {
                for (var i = 0; i < diff; i++)
                    vertArray[vertArray.Length - 1 - i].Delete();
            }

            vertArray = loop.Members(DbElementTypeInstance.VERTEX);

            for (var i = 0; i < vertArray.Length; i++)
            {
                var vert = vertArray[i];
                var vertPos = vert.GetPosition(DbAttributeInstance.POS, wrtSiteQualifier);
                var vertexPos = TransformPosition(extrusionInfo.VertexList[i]);
                if (vertPos == vertexPos) continue;
                vert.SetAttribute(DbAttributeInstance.POS, itemMatrix.Transform(vertexPos.ConvertToPoint()).ConvertToPosition());
            }
            return true;
        }        

        /// <summary>
        /// Создание SqlXml по элементам SCTN
        /// </summary>
        /// <param name="sctnList">массив элементов</param>
        /// <returns></returns>
        private static SqlXml CreateSctnInfoXml(List<SctnInfo> sctnList)
        {
            var doc = new XmlDocument();

            var xmlDeclaration = doc.CreateXmlDeclaration("1.0", "UTF-8", null);
            var root = doc.DocumentElement;
            doc.InsertBefore(xmlDeclaration, root);
            var rootNode = doc.CreateElement(string.Empty, "Root", string.Empty);
            doc.AppendChild(rootNode);
            var defDouble = 0.0;
            foreach (var info in sctnList)
            {
                var xmlSctn = doc.CreateElement(string.Empty, "Sctn", string.Empty);
                if (info.Element.IsNull)
                {
                    xmlSctn.SetAttribute("Height", defDouble.ToString(CultureInfo.InvariantCulture));
                    xmlSctn.SetAttribute("Width", defDouble.ToString(CultureInfo.InvariantCulture));

                    xmlSctn.SetAttribute("StartX", defDouble.ToString(CultureInfo.InvariantCulture));
                    xmlSctn.SetAttribute("StartY", defDouble.ToString(CultureInfo.InvariantCulture));
                    xmlSctn.SetAttribute("StartZ", defDouble.ToString(CultureInfo.InvariantCulture));
                    xmlSctn.SetAttribute("EndX", defDouble.ToString(CultureInfo.InvariantCulture));
                    xmlSctn.SetAttribute("EndY", defDouble.ToString(CultureInfo.InvariantCulture));
                    xmlSctn.SetAttribute("EndZ", defDouble.ToString(CultureInfo.InvariantCulture));
                    xmlSctn.SetAttribute("Bangle", defDouble.ToString(CultureInfo.InvariantCulture));
                    xmlSctn.SetAttribute("Element", "");
                    xmlSctn.SetAttribute("OriY", "");
                    xmlSctn.SetAttribute("OriZ", "");
                    xmlSctn.SetAttribute("DrnS", "");
                    xmlSctn.SetAttribute("DrnE", "");
                    continue;
                }

                xmlSctn.SetAttribute("Height", Math.Round(info.Height, 4).ToString(CultureInfo.InvariantCulture));
                xmlSctn.SetAttribute("Width", Math.Round(info.Width, 4).ToString(CultureInfo.InvariantCulture));

                xmlSctn.SetAttribute("StartX", Math.Round(info.Poss.X, 4).ToString(CultureInfo.InvariantCulture));
                xmlSctn.SetAttribute("StartY", Math.Round(info.Poss.Y, 4).ToString(CultureInfo.InvariantCulture));
                xmlSctn.SetAttribute("StartZ", Math.Round(info.Poss.Z, 4).ToString(CultureInfo.InvariantCulture));
                xmlSctn.SetAttribute("EndX", Math.Round(info.Pose.X, 4).ToString(CultureInfo.InvariantCulture));
                xmlSctn.SetAttribute("EndY", Math.Round(info.Pose.Y, 4).ToString(CultureInfo.InvariantCulture));
                xmlSctn.SetAttribute("EndZ", Math.Round(info.Pose.Z, 4).ToString(CultureInfo.InvariantCulture));
                xmlSctn.SetAttribute("Bangle", Math.Round(info.Bangle, 4).ToString(CultureInfo.InvariantCulture));
                xmlSctn.SetAttribute("Element", info.Element.GetAsString(DbAttributeInstance.REF));
                xmlSctn.SetAttribute("OriY", info.Ori.YDir().ToString());
                xmlSctn.SetAttribute("OriZ", info.Ori.ZDir().ToString());
                xmlSctn.SetAttribute("DrnS", info.drns.ToString());
                xmlSctn.SetAttribute("DrnE", info.drne.ToString());
                rootNode.AppendChild(xmlSctn);
            }

            return Common.XmlDocToSqlXml(doc);
        }

        /// <summary>
        /// Изменение атрибутов элемента SCTN
        /// </summary>
        /// <param name="sctn">элемент</param>
        /// <param name="point">точки начала и конца</param>
        /// <param name="spre">ссылка на спецификацию</param>
        /// <param name="vX">вектор X</param>
        /// <param name="vY">вектор Y</param>
        /// <returns>true - если все атрибуты были изменены без ошибок</returns>
        private static bool ChangeSctnAttrib(DbElement sctn, TeklaElementPoints point, DbElement spre, DbElement prevSctn)
        {
            if (sctn.GetBool(DbAttributeInstance.LOCK) ||
                !sctn.IsAttributeSetable(DbAttributeInstance.POSS) ||
                !sctn.IsAttributeSetable(DbAttributeInstance.POSE) ||
                !sctn.IsAttributeSetable(DbAttributeInstance.BANG) ||
                !sctn.IsAttributeSetable(DbAttributeInstance.SPRE) ||
                !sctn.IsAttributeSetable(DbAttributeInstance.JUSL) ||
                !sctn.IsAttributeSetable(DbAttributeInstance.DRNS) ||
                !sctn.IsAttributeSetable(DbAttributeInstance.DRNE))
            {
                return false;
            }


            sctn.SetAttribute(DbAttributeInstance.POSS, Position.Create(point.StartPoint.X, point.StartPoint.Y, point.StartPoint.Z));
            sctn.SetAttribute(DbAttributeInstance.POSE, Position.Create(point.EndPoint.X, point.EndPoint.Y, point.EndPoint.Z));

            CreateBangle(sctn, point.XVector, point.YVector);

            var ori = sctn.GetOrientation(DbAttributeInstance.ORI);
            if (prevSctn.IsNull)
            {
                sctn.SetAttribute(DbAttributeInstance.DRNS, ori.YDir());
                sctn.SetAttribute(DbAttributeInstance.DRNE, ori.YDir());
            }
            else
            {
                ConnectPolyBeamParts(prevSctn, sctn, ori.YDir());
            }

            sctn.SetAttribute(DbAttributeInstance.SPRE, spre);

            var plNames = sctn.GetAsStringArray(DbAttributeInstance.PLNA);
            var jusString = plNames.Contains("CL") ? "CL" : "NA";
            sctn.SetAttribute(DbAttributeInstance.JUSL, jusString);

            return true;
        }

        /// <summary>
        /// Соединение двух SCTN принадлежащих одному PolyBeam
        /// </summary>
        /// <param name="sctn1">первый элемент по построению</param>
        /// <param name="sctn2">следующий элемент по построению</param>
        private static void ConnectPolyBeamParts(DbElement sctn1, DbElement sctn2, Direction direction)
        {
            var pos1 = sctn1.GetPosition(DbAttributeInstance.POSS);
            var pos2 = sctn1.GetPosition(DbAttributeInstance.POSE);
            var pos3 = sctn2.GetPosition(DbAttributeInstance.POSE);
            var dir1 = Direction.Create(pos1, pos2);
            var dir2 = Direction.Create(pos2, pos3);
            var midway = dir1.Midway(dir2);
            var plane = Plane.Create(midway, pos2);
            var normal = plane.Normal();
            var opposite = normal.Opposite();

            sctn1.SetAttribute(DbAttributeInstance.DRNE, opposite);

            sctn2.SetAttribute(DbAttributeInstance.DRNS, normal);
            sctn2.SetAttribute(DbAttributeInstance.DRNE, direction);
        }

         /// <summary>
        /// Получение списка центральных точек из SqlXml
        /// </summary>
        /// <param name="sqlXml">входящий параметр SqlXml</param>
        /// <returns>Список всех точек</returns>
        private static List<TeklaElementPoints> GetPointsList(SqlXml sqlXml, bool onlyOriginChange)
        {
            var result = new List<TeklaElementPoints>();
            var doc = Common.SqlXmlToXmlDoc(sqlXml);
            var points = doc.GetElementsByTagName("Point");
            foreach (XmlNode p in points)
            {
                Point startPoint = new Point(), endPoint = new Point(), originPoint = new Point();
                Vector xVector = new Vector(), yVector = new Vector();
                double height = 0.0, width = 0.0;
                var level = "0.000";
                var strProfileParams = string.Empty;
                GetCoordinatesFromXml(p, ref startPoint, ref endPoint, ref xVector, ref yVector, ref originPoint, ref height, ref width, ref level, ref strProfileParams);
                var teklaElementPoint = new TeklaElementPoints
                {
                    StartPoint = onlyOriginChange ? startPoint : TransformPosition(startPoint),
                    EndPoint = onlyOriginChange ? endPoint : TransformPosition(endPoint),
                    XVector = xVector,
                    YVector = yVector,
                    OriginPoint = TransformPosition(originPoint),
                    Height = height,
                    Width = width,
                    Level = level,
                    ProfileParameters = new Dictionary<string, double>()
                };
                if (!string.IsNullOrEmpty(strProfileParams))
                {
                    var paramsData = strProfileParams.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var data in paramsData)
                    {
                        var key = data.Split('=').First();
                        var value = double.Parse(data.Split('=').Last());
                        if (teklaElementPoint.ProfileParameters.ContainsKey(key)) continue;
                        teklaElementPoint.ProfileParameters.Add(key, value);
                    }
                }
                result.Add(teklaElementPoint);
            }
            return result;
        }

        /// <summary>
        /// Получение точек, векторов и размеров из XmlNode
        /// </summary>
        /// <param name="node">XmlNode для обработки</param>
        /// <param name="startPoint">точка начала</param>
        /// <param name="endPoint">точка конца</param>
        /// <param name="xVector">X вектор</param>
        /// <param name="yVector">Y вектор</param>
        /// <param name="originPoint">начало координат</param>
        /// <param name="height">высота панели</param>
        /// <param name="level">высотная отметка</param>
        private static void GetCoordinatesFromXml(XmlNode node, ref Point startPoint, ref Point endPoint,
            ref Vector xVector, ref Vector yVector, ref Point originPoint, ref double height, ref double width,
            ref string level, ref string profileParams)
        {
            foreach (XmlAttribute att in node.Attributes)
            {
                switch (att.Name)
                {
                    case "StartX":
                        startPoint.X = double.Parse(att.Value, CultureInfo.InvariantCulture);
                        break;
                    case "StartY":
                        startPoint.Y = double.Parse(att.Value, CultureInfo.InvariantCulture);
                        break;
                    case "StartZ":
                        startPoint.Z = double.Parse(att.Value, CultureInfo.InvariantCulture);
                        break;
                    case "EndX":
                        endPoint.X = double.Parse(att.Value, CultureInfo.InvariantCulture);
                        break;
                    case "EndY":
                        endPoint.Y = double.Parse(att.Value, CultureInfo.InvariantCulture);
                        break;
                    case "EndZ":
                        endPoint.Z = double.Parse(att.Value, CultureInfo.InvariantCulture);
                        break;
                    case "xVectorX":
                        xVector.X = double.Parse(att.Value, CultureInfo.InvariantCulture);
                        break;
                    case "xVectorY":
                        xVector.Y = double.Parse(att.Value, CultureInfo.InvariantCulture);
                        break;
                    case "xVectorZ":
                        xVector.Z = double.Parse(att.Value, CultureInfo.InvariantCulture);
                        break;
                    case "yVectorX":
                        yVector.X = double.Parse(att.Value, CultureInfo.InvariantCulture);
                        break;
                    case "yVectorY":
                        yVector.Y = double.Parse(att.Value, CultureInfo.InvariantCulture);
                        break;
                    case "yVectorZ":
                        yVector.Z = double.Parse(att.Value, CultureInfo.InvariantCulture);
                        break;
                    case "OriginX":
                        originPoint.X = double.Parse(att.Value, CultureInfo.InvariantCulture);
                        break;
                    case "OriginY":
                        originPoint.Y = double.Parse(att.Value, CultureInfo.InvariantCulture);
                        break;
                    case "OriginZ":
                        originPoint.Z = double.Parse(att.Value, CultureInfo.InvariantCulture);
                        break;
                }
            }
            foreach (XmlAttribute att in node.ParentNode.Attributes)
            {
                switch (att.Name)
                {
                    case "Height":
                        height = double.Parse(att.Value, CultureInfo.InvariantCulture);
                        break;
                    case "Width":
                        width = double.Parse(att.Value, CultureInfo.InvariantCulture);
                        break;
                    case "Level":
                        level = att.Value;
                        break;
                    case "Params":
                        profileParams = att.Value;
                        break;
                }
            }
            xVector.Normalize();
            yVector.Normalize();
        }

        /// <summary>
        /// Получение точек, угла и ориентации SCTN из XML
        /// </summary>
        /// <param name="node">XmlNode для обработки</param>
        /// <param name="startPoint">точка начала</param>
        /// <param name="endPoint">точка конца</param>
        /// <param name="xVector">X вектор</param>
        /// <param name="yVector">Y вектор</param>
        /// <param name="originPoint">начало координат</param>
        private static void GetSctnAttributesFromXml(XmlNode node, ref SctnInfo sctnInfo)
        {
            string oriY = "", oriZ = "";

            foreach (XmlAttribute att in node.Attributes)
            {
                switch (att.Name)
                {
                    case "StartX":
                        sctnInfo.Poss.X = double.Parse(att.Value, CultureInfo.InvariantCulture);
                        break;
                    case "StartY":
                        sctnInfo.Poss.Y = double.Parse(att.Value, CultureInfo.InvariantCulture);
                        break;
                    case "StartZ":
                        sctnInfo.Poss.Z = double.Parse(att.Value, CultureInfo.InvariantCulture);
                        break;
                    case "EndX":
                        sctnInfo.Pose.X = double.Parse(att.Value, CultureInfo.InvariantCulture);
                        break;
                    case "EndY":
                        sctnInfo.Pose.Y = double.Parse(att.Value, CultureInfo.InvariantCulture);
                        break;
                    case "EndZ":
                        sctnInfo.Pose.Z = double.Parse(att.Value, CultureInfo.InvariantCulture);
                        break;
                    case "Bangle":
                        sctnInfo.Bangle = double.Parse(att.Value, CultureInfo.InvariantCulture);
                        break;
                    case "Element":
                        sctnInfo.Element = DbElement.GetElement(att.Value);
                        break;
                    case "OriY":
                        oriY = att.Value;
                        break;
                    case "OriZ":
                        oriZ = att.Value;
                        break;
                    case "DrnS":
                        sctnInfo.drns = Direction.Create(att.Value);
                        break;
                    case "DrnE":
                        sctnInfo.drne = Direction.Create(att.Value);
                        break;
                }
            }
            foreach (XmlAttribute att in node.ParentNode.Attributes)
            {
                switch (att.Name)
                {
                    case "Height":
                        sctnInfo.Height = double.Parse(att.Value, CultureInfo.InvariantCulture);
                        break;
                }
                switch (att.Name)
                {
                    case "Width":
                        sctnInfo.Width = double.Parse(att.Value, CultureInfo.InvariantCulture);
                        break;
                }
            }
            sctnInfo.Ori = Orientation.Create("Y is " + oriY + " and Z is " + oriZ);
        }
       
        /// <summary>
        /// Поворот SCTN на необходимый угол
        /// </summary>
        /// <param name="sctn">элемент</param>
        /// <param name="vX">вектор X</param>
        /// <param name="vY">вектор Y</param>
        private static void CreateBangle(DbElement sctn, Vector vX, Vector vY)
        {
            var oriTekla = vX.CreateOrientation(vY);
            var oriAveva = Orientation.Create(oriTekla.ToString());
            oriAveva.Rotate(oriTekla.YDir(), 90);
            oriAveva.Rotate(oriTekla.ZDir(), 180);
            var sctnBang = sctn.GetDouble(DbAttributeInstance.BANG);
            if (sctnBang != 0)
            {
                sctn.SetAttribute(DbAttributeInstance.BANG, 0);
            }

            var oriSctn = sctn.GetOrientation(DbAttributeInstance.ORI);
            var yDirSctn = oriSctn.YDir();

            var bang = Math.Round(oriAveva.YDir().Angle(yDirSctn), 2);

            if (!bang.Equals(double.NaN) && bang > 0)
            {
                sctn.SetAttribute(DbAttributeInstance.BANG, bang);
                oriSctn = sctn.GetOrientation(DbAttributeInstance.ORI);
                yDirSctn = oriSctn.YDir();
                if (yDirSctn.Angle(oriAveva.YDir()) > 1)
                {
                    sctn.SetAttribute(DbAttributeInstance.BANG, -1.0 * bang);
                }
            }
        }

        /// <summary>
        /// Получение элемента внутри которого должен быть расположен элемент с указанным классом
        /// </summary>
        /// <param name="className">имя класса</param>
        /// <param name="zone">элемент ZONE, внутри которого происходит поиск</param>
        /// <param name="root">элемент, которому соответствует текущий класс</param>
        /// <returns>true - если элемент найден</returns>
        private static bool GetRootByClass(string className, DbElement zone, ref DbElement root, DbElementType itemType,
            string level)
        {
            if (!short.TryParse(className, out short teklaClass))
                return false;

            string rootName;
            if (RunE3D.ProjectInfo.Program.Version == "20.0")
            {
                switch (teklaClass)
                {
                    case 2:
                        rootName = "KAR_COL";
                        break;
                    case 3:
                        rootName = "KAR_PPT";
                        break;
                    case 4:
                        rootName = "KAR_GSP_NP";
                        break;
                    case 5:
                        rootName = "KAR_VSK";
                        break;
                    case 6:
                        rootName = "KAR_SK";
                        break;
                    case 7:
                        rootName = "KAR_PROG";
                        break;
                    case 11:
                        rootName = "KAR_GSP_VP";
                        break;
                    case 12:
                        rootName = "FHV_RF";
                        break;
                    case 14:
                        rootName = "KAR_VSP";
                        break;
                    case 99:
                        rootName = "OTHER_JOINT";
                        break;
                    default:
                        rootName = "OTHER_OTHER";
                        break;
                }
            }
            else
            {
                switch (teklaClass)
                {
                    #region ОКМ

                    case 2:
                    case 51:
                    case 16: 
                        rootName = "KAR_COL";
                        break;
                    case 5:
                    case 19:
                        rootName = "KAR_VSK";
                        break;
                    case 6:
                    case 34:
                    case 37:
                    case 65:
                    case 11:
                    case 14:
                    case 42:
                    case 25:
                    case 4:
                    case 46:
                        rootName = "KAR_SK";
                        break;
                    case 7:
                    case 53:
                        rootName = "KAR_PR";
                        break;
                    case 68:
                    case 70:
                        rootName = "KAR_PS";
                        break;
                    case 36:
                    case 48:
                        rootName = "KAR_PER";
                        break;
                    case 27:
                        rootName = "KAR_PFL";
                        break;
                    case 12:
                        rootName = "FHV_NAR";
                        break;
                    case 21:
                        rootName = "FHV_VN";
                        break;
                    case 79:
                        rootName = "FHV_NK";
                        break;
                    case 39:
                        rootName = "FHV_PFL";
                        break;
                    case 9:
                        rootName = "PL_ST";
                        break;
                    case 8:
                        rootName = "PL_SV";
                        break;
                    case 10:
                    case 52:
                    case 45:
                        rootName = "PL_PL";
                        break;
                    case 23:
                        rootName = "PL_L";
                        break;
                    case 22:
                    case 1:
                    case 32:
                        rootName = "PL_N";
                        break;
                    case 13:
                        rootName = "PL_OG";
                        break;
                    case 18:
                    case 20:
                    case 24:
                    case 38:
                        rootName = "PP_OK";
                        break;
                    case 3:
                    case 31:
                    case 17:
                        rootName = "PP_PT";
                        break;
                    case 99:
                    case 29:
                        rootName = $"UZL_{level}_{(itemType == DbElementTypeInstance.SCTN ? "PROF" : "PL")}";
                        break;
                    case 60:
                        rootName = "NI_M";
                        break;
                    case 35:
                    case 28:
                    case 56:
                        rootName = "NI_BNK";
                        break;
                    case 41:
                        rootName = "OPOR_GMO";
                        break;
                    case 55:
                        rootName = "OPOR_OPPB";
                        break;
                    case 69:
                        rootName = "OPOR_OTK";
                        break;
                    case 83:
                        rootName = "OPOR_EO";
                        break;
                    case 97:
                        rootName = "OPOR_USTIS";
                        break;
                    case 111:
                        rootName = "OPOR_OOPR";
                        break;
                    case 26:
                        rootName = $"PER_{level}_B";
                        break;
                    case 91:
                        rootName = "OPOR_SANO";
                        break;
                    case 40:
                    case 151:
                        rootName = "KAR_FHV";
                        break;

                    #endregion

                    #region ОКЖ

                    case 101:
                        rootName = "UZL_PL";
                        break;
                    case 104:
                        rootName = "UZL_PROF";  // в таблице нет
                        break;
                    case 207:
                        rootName = "FUB_BFC";
                        break;
                    case 214:
                        rootName = "FUN_FC";
                        break;
                    case 222:
                        rootName = "STE_PSZ";
                        break;
                    case 236:
                        rootName = "LES_LM"; // в таблице нет
                        break;
                    case 264:
                        rootName = "LES_LS";
                        break;
                    case 267:
                        rootName = "FUB_NAB";
                        break;
                    case 302:
                        rootName = "FUO_PRI";
                        break;
                    case 304:
                        rootName = "FUN_FM";
                        break;
                    case 305:
                        rootName = "FUN_S"; // в таблице нет
                        break;
                    case 307:
                        rootName = "FUO_FO";
                        break;
                    case 310:
                        rootName = "PST_ST";
                        break;
                    case 311:
                        rootName = "FUB_NAB";
                        break;
                    case 320:
                        rootName = "PER_K";
                        break;
                    case 322:
                        rootName = "FUB_BFM";
                        break;
                    case 323:
                        rootName = "PER_PM";
                        break;
                    case 324:
                        rootName = "PER_PC"; // в таблице нет
                        break;
                    case 325:
                        rootName = "PSO_PM";
                        break;
                    case 327:
                        rootName = "PSO_PS";
                        break;
                    case 329:
                        rootName = "PER_PP";
                        break;
                    case 337:
                        rootName = "LES_LP"; // в таблице нет
                        break;
                    case 342:
                        rootName = "INS_PL";
                        break;
                    case 347:
                        rootName = "FUB_NAB";
                        break;
                    case 348:
                        rootName = "FUN_POB";
                        break;
                    case 510:
                        rootName = "PL_B";
                        break;
                    case 511:
                        rootName = "PL_N";
                        break;
                    case 512:
                        rootName = "PL_N";
                        break;
                    case 513:
                        rootName = "PL_ST";
                        break;

                    #endregion

                    default:
                        rootName = "OTHER_OTHER";
                        break;
                }
            }

            return !string.IsNullOrEmpty(rootName) && GetRootByName(rootName, zone, ref root);
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
                DbElementType type = null;
                switch (i)
                {
                    case 0:
                        type = DbElementTypeInstance.STRUCTURE;
                        break;
                    case 1:
                        type = DbElementTypeInstance.FRMWORK;
                        break;
                    case 2:
                        type = DbElementTypeInstance.SBFRAMEWORK;
                        break;
                }

                if (type == null)
                    return false;

                //вылетает здесь, если повторно обращается из PdmsStandalone
                var collection = new DBElementCollection(owner, new TypeFilter(type));
                var searchFlag = false;
                foreach (DbElement item in collection)
                {
                    if (!item.GetAsString(DbAttributeInstance.NAME).EndsWith("_" + name))
                        continue;

                    owner = item;
                    searchFlag = true;
                    break;
                }

                if (searchFlag)
                    continue;

                owner = CreateStructureElement(name, owner, type);

                if (owner.IsNull)
                    return false;
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
                if (!DbElement.GetElement(newName).IsNull) newName = "/1" +owner.GetAsString(DbAttributeInstance.FLNN) + "_" + name;
                newItem.SetAttribute(DbAttributeInstance.NAME, newName);
                newItem.SetAttribute(DbAttributeInstance.DESC, GetDescription(owner.GetAsString(DbAttributeInstance.NAME).Contains("SO-KM") ? "KM" : "KG", name, type));
                Helpers.Common.SortLevelElements(owner);
                return newItem;
            }

            return DbElement.GetElement();
        }

        /// <summary>
        /// Получение элемента ZONE, внутри которого будет вестись работа
        /// </summary>
        /// <param name="mdbName">название текущей MDB</param>
        /// <param name="mark">название марки, по которой будет производиться поиск нужной ZONE</param>
        /// <returns>итоговый элемент</returns>
        private static DbElement GetRootZone(string mdbName, string mark)
        {
            mdbName = mdbName.Replace("-", string.Empty);
            var world = MDB.CurrentMDB.GetFirstWorld(DbType.Design);
            if (world.IsNull) return DbElement.GetElement();                     

            mark = mark.ToUpper().Replace('К', 'K').Replace('М', 'M').Replace("Ж", "G");

            var itemSubObjectCode = _subObjectCode;

            var match = new Regex(@"((\d{2})_)?(KM|KG)(\d{1,2}(\.\d{1,2})?)?").Match(mark);
            if (!match.Success || match.Value != mark || string.IsNullOrEmpty(mark))
                return DbElement.GetElement();

            if (!string.IsNullOrEmpty(match.Groups[2].Value))
            {
                if ((Project.CurrentProject.Name == "AMU" &&
                     (MDB.CurrentMDB.Name == "02-210-00" || MDB.CurrentMDB.Name == "02-211-00")) ||
                    (Project.CurrentProject.Name == "MAY" && MDB.CurrentMDB.Name == "01-230-01"))
                {
                    itemSubObjectCode = match.Groups[2].Value;
                    mark = mark.Replace($"{itemSubObjectCode}_", string.Empty);
                }
                else
                {
                    return DbElement.GetElement();
                }
            }

            var department = mark.StartsWith("KM") ? "OKM" : "OKG";
            var departmentSite = DbElement.GetElement();
            var siteCollection = new DBElementCollection(world, new TypeFilter(DbElementTypeInstance.SITE));
            foreach (DbElement item in siteCollection)
            {
                var name = item.GetAsString(DbAttributeInstance.FLNN);
                if (!name.EndsWith($"-{department}")) continue;
                var code = name.Split('-').FirstOrDefault();
                if (RunE3D.ProjectInfo.Pdms.StructureVersion == 2)
                {
                    if (code.Length < 6) continue;
                    //проверка принадлежности в MDB
                    if (mdbName != $"{code.Substring(2, 2)}{code.Substring(6)}") continue;
                    //проверка кода документации
                    if (!name.StartsWith($"{code.Substring(0, 4)}{_documentCode}{code.Substring(6)}")) continue;
                }
                else if (mdbName != code)
                {
                    continue;
                }

                departmentSite = item;
                break;
            }

            //попытка поиска для старых проектов, когда АСУ был одним отделом СО
            if (departmentSite.IsNull || !departmentSite.IsValid)
            {
                department = "SO";
                foreach (DbElement item in siteCollection)
                {
                    var name = item.GetAsString(DbAttributeInstance.NAME).ToUpper();
                    if (!name.Contains(mdbName) || !name.EndsWith($"-{department}")) continue;
                    departmentSite = item;
                    break;
                }
            }

            if (departmentSite.IsNull || !departmentSite.IsValid) return DbElement.GetElement();           

            if (!_usingDepartmentSites.Contains(departmentSite)) _usingDepartmentSites.Add(departmentSite);

            var siteName = departmentSite.GetAsString(DbAttributeInstance.FLNN);
            var markZoneName = $"/{mdbName.Substring(0, mdbName.Length - 2)}{itemSubObjectCode}-{siteName.Substring(siteName.IndexOf('-') + 1)}-{mark}-{_documentCode}";
            if (RunE3D.ProjectInfo.Pdms.StructureVersion == 2)
            {
                var code = siteName.Split('-').FirstOrDefault();
                markZoneName = $"/{code.Substring(0, code.Length - 2)}{itemSubObjectCode}-{department}-{mark}";
            }

            var zone = DbElement.GetElement(markZoneName);
            if (zone.IsNull) CreateZoneElement(departmentSite, mark, markZoneName, ref zone);            

            return zone;
        }

        /// <summary>
        /// Создание элемента ZONE, внутри которого будет вестись работа
        /// </summary>
        /// <param name="site">элемент SITE, внутри которого будет создана ZONE</param>
        /// <param name="mark">название марки</param>
        /// <param name="zone">итоговый элемент</param>
        private static void CreateZoneElement(DbElement departmentSite, string mark,string markZoneName, ref DbElement zone)
        {
            Command.CreateCommand("!!ZoneCreateFlag = true").Run();
            if (departmentSite.IsCreatable(DbElementTypeInstance.ZONE) && !departmentSite.GetBool(DbAttributeInstance.LOCK))
            {
                zone = departmentSite.CreateLast(DbElementTypeInstance.ZONE);
                if (!zone.IsNull && zone.IsValid)
                {                    
                    zone.SetAttribute(DbAttributeInstance.NAME, markZoneName);
                    var desc = Common.GetPdmsElementDescription(mark.Substring(0, 2), "DocumentMark");
                    zone.SetAttribute(DbAttributeInstance.DESC, string.IsNullOrEmpty(desc)? mark.Substring(0, 2) : desc);
                }
            }
        }

        /// <summary>
        /// Получение описания из БД для создаваемого элемента
        /// </summary>
        /// <param name="code">код создаваемого элемента</param>
        /// <param name="type">тип создаваемого элемента</param>
        /// <returns></returns>
        private static string GetDescription(string mark, string code, DbElementType type)
        {
            var tablName = "";
            if (type == DbElementTypeInstance.STRUCTURE)
            {
                tablName = $"{mark}Ident";
            }
            else if (type == DbElementTypeInstance.FRMWORK)
            {
                tablName = $"{mark}ElementsIdent";
            }
            else if (type == DbElementTypeInstance.SBFRAMEWORK)
            {
                tablName = "KMFunc";
            }

            var result = Common.GetPdmsElementDescription(code, tablName);
            if (string.IsNullOrEmpty(result) && type == DbElementTypeInstance.SBFRAMEWORK)
            {
                result = Common.GetPdmsElementDescription(code, "KMElementsIdent");
            }

            return string.IsNullOrEmpty(result) ? code : result;
        }
    }
}
