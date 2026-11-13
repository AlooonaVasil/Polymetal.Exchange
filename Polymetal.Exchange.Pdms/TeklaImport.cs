using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Data.SqlTypes;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using Aveva.Pdms.Database;
using Aveva.Pdms.Geometry;
using Aveva.Pdms.Utilities.CommandLine;
using Aveva.PDMS.Database.Filters;
using Polymetal.Exchange.Pdms.Properties;
using Polymetal.Exchange.Utilities;
using Tekla.Structures.Geometry3d;
using DbType = Aveva.Pdms.Database.DbType;

namespace Polymetal.Exchange.Pdms
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
            public CoordinateSystem System;
        }

        internal struct PaveInfo
        {
            public DbElement Element;
            public Position Position;
        }

        private static DbElement _site;

        private static Matrix _globalMatrix;

        private static List<RunPdms.ElementHistory> histories = new List<RunPdms.ElementHistory>();
        private static DbAttribute _polyTeklaGuid = DbAttribute.GetDbAttribute(":PolyTeklaGuid");

        private static string _documentCode = "07";

        /// <summary>
        /// Синхронизация с элементами Tekla 
        /// </summary>
        /// <returns></returns>
        public static bool Run()
        {            
            Console.WriteLine("Начинается импорт данных");
            var modelDirPath = RunPdms.ProjectInfo.Program.Path;
            if (Directory.Exists(modelDirPath))
            {
                var dirInfo = new DirectoryInfo(modelDirPath);               
                if (dirInfo.Name.Split(' ').Length > 2)
                {
                    _documentCode = dirInfo.Name.Split(' ')[1].Trim();
                }
            }
            _documentCode = string.IsNullOrEmpty(_documentCode) ? "07" : _documentCode;

            var mdbName = MDB.CurrentMDB.Name;
            var dt = Common.GetUpdatedTeklaData(RunPdms.ProjectInfo.ProjectId);
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
                var zone = DbElement.GetElement();
                var mark = row["beam_phase"].ToString().Replace(" ", string.Empty);
                if (markZoneDictionary.ContainsKey(mark))
                {
                    zone = markZoneDictionary[mark];
                }
                else
                {
                    zone = GetRootZone(mdbName, mark);    
                    markZoneDictionary.Add(mark,zone);
                }
                
                if (zone.IsNull || !zone.IsValid)
                {
                    RunPdms.SendMessage("[ModifyElement] " + row["beam_guid"] + " " + "не найден родительский ZONE");
                    return false;
                }

                var status = row["element_status"].ToString();

                if (!Enum.IsDefined(typeof(Common.PartType), row["beam_type"].ToString())) continue;
                var beamType = Enum.Parse(typeof(Common.PartType), row["beam_type"].ToString());

                var spre = DbElement.GetElement(row["profile_name"].ToString());
                if (!beamType.Equals(Common.PartType.ContourPlate) && spre.IsNull && status != "DEL") continue;

                if (status == "DEL")
                {
                    if (DeleteElements((SqlXml)row["pdms_item"], (Common.PartType)beamType))
                    {
                        row["element_status"] = "REM";
                        modifications++;
                    }
                    continue;
                }

                if (status == "CRE" || status == "MOD")
                {
                    if (beamType.Equals(Common.PartType.ContourPlate))
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
                            RunPdms.SendMessage("[ModifyFloors] " + row["beam_guid"] + " " + exc.Message);
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
                            RunPdms.SendMessage("[ModifySctns] " + row["beam_guid"] + " " + exc.Message);
                        }
                    }
                }
            }

            Console.WriteLine("Заполнение данных по истории элементов (" + histories.Count + " записей)");
            RunPdms.SendMessage("Изменений в модели: " + modifications);
            RunPdms.UpdateHistory(histories, Common.Source.Tekla);
            Console.WriteLine("История элементов обновлена");
            Common.SetUpdatedTeklaData(dt, RunPdms.ProjectInfo.ProjectId);
            Console.WriteLine("Данные в SQL обновлены");
            return true;
        }

        /// <summary>
        /// Удаление всех элементов относящихся к удаленному элементу Tekla
        /// </summary>
        /// <param name="sqlXml">входной SqlXml</param>
        /// <returns>true - если все элементы были удалены без ошибок</returns>
        private static bool DeleteElements(SqlXml sqlXml, Common.PartType type)
        {
            if (type.Equals(Common.PartType.ContourPlate))
            {
                var floorInfo = GetFloorInfoList(sqlXml);
                var floor = floorInfo.Element;
                if (floor.IsNull) return true;
                if (floor.IsDeleteable)
                {
                    floor.Delete();
                }
                if (!floor.IsDeleted)
                {
                    RunPdms.SendMessage("Не удалось удалить элемент " + floor.GetElementType().ShortName + " " + floor.GetAsString(DbAttributeInstance.REF));
                    return false;
                }
            }
            else
            {
                var sctnList = GetSctnInfoList(sqlXml);
                if (sctnList.Count == 0)
                {
                    return true;
                }
                foreach (var sctnInfo in sctnList)
                {
                    var sctn = sctnInfo.Element;
                    if (sctn.IsNull) continue;
                    if (sctn.IsDeleteable)
                    {
                        sctn.Delete();
                    }
                    if (!sctn.IsDeleted)
                    {
                        RunPdms.SendMessage("Не удалось удалить элемент " + sctn.GetElementType().ShortName + " " + sctn.GetAsString(DbAttributeInstance.REF));
                        return false;
                    }
                }
            }
            return true;
        }

        private static Matrix GetGlobalMatrix()
        {           
            var offsetData = string.Empty;
            using (var sqlConnection = new SqlConnection(Common.ConnectionString))
            {
                sqlConnection.Open();
                var sqlCommand = "select top 1 Offset from ExchangeProject where ProjectID = " + RunPdms.ProjectInfo.ProjectId;
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
                            VertexList = new List<Position>(),
                            System = new CoordinateSystem()
                        };
                        foreach (XmlAttribute att in extr.Attributes)
                        {
                            if (att.Name.Equals("height", StringComparison.OrdinalIgnoreCase))
                            {
                                extrusionInfo.Height = double.Parse(att.Value, CultureInfo.InvariantCulture);
                                break;
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
                        if (extrusionInfo.VertexList.Count > 0)
                            boo.Add(extrusionInfo);
                    }
                    if (boo.Count > 0)
                        result.Add(boo);
                }
            }
            return result;
        }

        private static Position TransformPosition(Position input)
        {            
            return _globalMatrix.Transform(input.ConvertToPoint()).ConvertToPosition();            
        }

        private static Point TransformPosition(Point input)
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
                            RunPdms.SendMessage("Не удалось удалить элемент " + item.GetElementType().ShortName + " " + item.GetAsString(DbAttributeInstance.REF));
                            return false;
                        }
                    }
                }
                sctnList.RemoveRange(pointList.Count, diff);
            }
            else if (pointList.Count > sctnList.Count)
            {
                var diff = pointList.Count - sctnList.Count;
                for (int i = 0; i < diff; i++)
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
                if (sctn.IsNull)
                {
                    if (findRoot && !root.IsNull && root.IsCreatable(DbElementTypeInstance.SCTN))
                    {
                        sctn = root.CreateLast(DbElementTypeInstance.SCTN);
                        sctnInfo.Element = sctn;
                        newSctn = true;
                    }
                    else
                    {
                        RunPdms.SendMessage("Не удалось создать элемент с GUID: " + row["beam_guid"]);
                        return false;
                    }
                }
                else if (!findRoot)
                {
                    RunPdms.SendMessage("Не найден родительский элемент для элемента с GUID " + row["beam_guid"]);
                    return false;
                }
                else if (!root.Equals(sctn.Owner) && (sctn.GetElementType().Equals(DbElementTypeInstance.SCTN) && !MoveItemToNewClass(sctn, root)))
                {
                    RunPdms.SendMessage("Не удалось переместить элемент " + sctn.GetElementType().ShortName + " " + sctn.GetAsString(DbAttributeInstance.REF));
                    return false;
                }

                var changed = ChangeSctnAttrib(sctn, point, spre, prevElement);

                var catr = spre.GetElement(DbAttributeInstance.CATR);
                if (!catr.IsNull && catr.IsValid)
                {
                    var catrParams = catr.GetDoubleArray(DbAttributeInstance.PARA);
                    var parametricCatr = true;
                    foreach (var d in catrParams)
                    {
                        if (d != 0.0)
                        {
                            parametricCatr = false;
                            break;
                        }
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

                if (!changed)
                {
                    if (newSctn)
                    {
                        sctn.Delete();
                    }
                    RunPdms.SendMessage("Не удалось "+(newSctn?"создать":"изменить")+" элемент с GUID " + row["beam_guid"]);
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

                    var wrtPos = sctn.GetPosition(DbAttributeInstance.POS, 0, _site);
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
                                RunPdms.SendMessage("Не удалось создать негативы для элемента с GUID " +
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

                        var fittOrientation = fitting.GetOrientation(DbAttributeInstance.ORI, 0, _site);
                        var fittPos = fitting.GetPosition(DbAttributeInstance.POS);
                        var fittCS = fittOrientation.ConvertToCoordinateSystem(fittPos);
                        var fittMatrix = MatrixFactory.ToCoordinateSystem(fittCS);

                        var nextrusionPos =
                            fittMatrix.Transform(extrusionInfo.VertexList.First().ConvertToPoint()).ConvertToPosition();

                        var nextrusionCS =
                            nextrusion.GetOrientation(DbAttributeInstance.ORI, 0, _site)
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
                sctnInfo.Poss = sctn.GetPosition(DbAttributeInstance.POSS, 0, _site);
                sctnInfo.Pose = sctn.GetPosition(DbAttributeInstance.POSE, 0, _site);
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

                histories.Add(new RunPdms.ElementHistory
                {
                    ItemRef = sctn.GetAsString(DbAttributeInstance.REF),
                    ItemFlnm = sctn.GetAsString(DbAttributeInstance.FLNM),
                    ItemType = sctn.GetElementType().ShortName,
                    CreatedBy = row["beam_created_by"].ToString(),
                    ModifiedBy = row["beam_modified_by"].ToString(),
                    Created = DateTime.Parse(row["beam_created"].ToString()),
                    Modified = DateTime.Parse(row["beam_modified"].ToString())
                });
                sctn.SetAttribute(_polyTeklaGuid, row["beam_guid"].ToString().Trim());
            }

            row["pdms_item"] = CreateSctnInfoXml(sctnList);
            return true;
        }

        internal static void TransformOrientation(ref Vector vX, ref Vector vY, DbElement root)
        {
            var rootOri = root.GetOrientation(DbAttributeInstance.ORI, 0, _site);
            var rootPos = root.GetPosition(DbAttributeInstance.POS, 0, _site);
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
            var pointList = GetPointsList((SqlXml)row["beam_points"],true);            
            var booleansList = GetBooleansInfoList((SqlXml)row["beam_booleans"]);

            var floor = floorInfo.Element;
            var root = DbElement.GetElement();
            var level = pointList.Count > 0 ? pointList.First().Level : "0.000";
            var findRoot = GetRootByClass(row["beam_class"].ToString(), zone, ref root, DbElementTypeInstance.FLOOR,
                level);
            var newFloor = false;
            if (floor.IsNull)
            {
                if (findRoot && !root.IsNull && root.IsCreatable(DbElementTypeInstance.FLOOR))
                {
                    floor = root.CreateLast(DbElementTypeInstance.FLOOR);
                    floorInfo.Element = floor;
                    newFloor = true;
                }
                else
                {
                    RunPdms.SendMessage("Не удалось создать элемент с GUID " + row["beam_guid"]);
                    return false;
                }
            }
            else if (!findRoot)
            {
                RunPdms.SendMessage("Не найден родительский элемент для элемента с GUID " + row["beam_guid"]);
                return false;
            }
            else if (!root.Equals(floor.Owner) && !MoveItemToNewClass(floor, root))
            {
                RunPdms.SendMessage("Не удалось переместить элемент " + floor.GetElementType().ShortName + " " + floor.GetAsString(DbAttributeInstance.REF));
                return false;
            }
            var ploo = floor.Members(DbElementTypeInstance.PLOOP).Length > 0 ? floor.Members(DbElementTypeInstance.PLOOP).First() : DbElement.GetElement();
            if (ploo.IsNull)
            {
                if (!floor.IsCreatable(DbElementTypeInstance.PLOOP))
                {
                    if (newFloor)
                    {
                        RunPdms.SendMessage("Не удалось создать элемент с GUID " + row["beam_guid"]);
                        floor.Delete();
                    }
                    return false;
                }
                ploo = floor.CreateLast(DbElementTypeInstance.PLOOP);
            }

            if (pointList.Count > 0)
            {
                var firstPoint = pointList.First();
                var floorPosition = Position.Create(firstPoint.OriginPoint.X, firstPoint.OriginPoint.Y, firstPoint.OriginPoint.Z);
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
                RunPdms.SendMessage("Не удалось "+(newFloor?"создать":"изменить")+" элемент с GUID " + row["beam_guid"]);
                return false;
            }

            /*if (!newFloor)
            {
                var members = new DBElementCollection(floor, new TypeFilter(DbElementTypeInstance.NXTRUSION)).Cast<DbElement>().ToList(); 
                foreach (DbElement memb in members)
                {
                    memb.Delete();
                }
            }*/

            if (booleansList.Count > 0)
            {
                var currentBoolean = booleansList[0];

                if (floor.IsNull || !floor.IsValid)
                {
                    RunPdms.SendMessage("Не удалось " + (newFloor ? "создать" : "изменить") + " элемент с GUID " + row["beam_guid"]);
                }

                var nxtrList = floor.Members(DbElementTypeInstance.NXTRUSION).ToList();
                if (nxtrList.Count > currentBoolean.Count)
                {
                    var diff = nxtrList.Count - currentBoolean.Count;
                    for (var i = 0; i < diff; i++) 
                    {
                        var nxtr = nxtrList.Last();
                        nxtr.Delete();
                        nxtrList.Remove(nxtr);
                    }
                }
                else if (nxtrList.Count < currentBoolean.Count)
                {
                    var diff = currentBoolean.Count - nxtrList.Count;
                    for (var i = 0; i < diff; i++)
                    {
                        floor.CreateLast(DbElementTypeInstance.NXTRUSION);
                        //Command.CreateCommand(floor.GetAsString(DbAttributeInstance.REF) + " NEW NXTR").Run();
                    }
                }

                nxtrList = floor.Members(DbElementTypeInstance.NXTRUSION).ToList();
                if (nxtrList.Count == currentBoolean.Count)
                {
                    for (var i = 0; i < currentBoolean.Count; i++)
                    {
                        var extrusionInfo = currentBoolean[i];
                        var nextrusion = nxtrList[i];

                        if (nextrusion.IsNull || !nextrusion.IsValid) continue;

                        var loop = nextrusion.Members(DbElementTypeInstance.LOOP).Length > 0
                            ? nextrusion.Members(DbElementTypeInstance.LOOP).First()
                            : nextrusion.CreateLast(DbElementTypeInstance.LOOP);

                        if (loop.IsNull || !loop.IsValid) continue;

                        nextrusion.SetAttribute(DbAttributeInstance.HEIG, extrusionInfo.Height);

                        var vX = new Vector(extrusionInfo.System.AxisX.X, extrusionInfo.System.AxisX.Y, extrusionInfo.System.AxisX.Z);
                        var vY = new Vector(extrusionInfo.System.AxisY.X, extrusionInfo.System.AxisY.Y, extrusionInfo.System.AxisY.Z);
                        TransformOrientation(ref vX, ref vY, floor);
                        var oriTekla = vX.CreateOrientation(vY);
                        nextrusion.SetAttribute(DbAttributeInstance.ORI, oriTekla);

                        var flooOrientation = floor.GetOrientation(DbAttributeInstance.ORI, 0, _site);
                        var flooPos = floor.GetPosition(DbAttributeInstance.POS);
                        var flooCS = flooOrientation.ConvertToCoordinateSystem(flooPos);
                        var flooMatrix = MatrixFactory.ToCoordinateSystem(flooCS);
                        var nextrusionPos = flooMatrix.Transform(extrusionInfo.VertexList.First().ConvertToPoint()).ConvertToPosition();

                        var nextrusionCS = nextrusion.GetOrientation(DbAttributeInstance.ORI, 0, _site).ConvertToCoordinateSystem(extrusionInfo.VertexList.First());
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
                    }
                }
            }
            else
            {
                var nxtrList = new DBElementCollection(floor, new TypeFilter(DbElementTypeInstance.NXTRUSION)).Cast<DbElement>().ToList();
                foreach (var nxtr in nxtrList)
                {
                    nxtr.Delete();
                }
            }

            var paveCollection = new DBElementCollection(floor, new TypeFilter(DbElementTypeInstance.PAVERT));
            var paveList = new List<PaveInfo>();
            foreach (DbElement pave in paveCollection)
            {
                paveList.Add(new PaveInfo { Element = pave, Position = pave.GetPosition(DbAttributeInstance.POS, 0, _site) });
            }
            floorInfo.Points = paveList;
            floorInfo.Height = ploo.GetDouble(DbAttributeInstance.HEIG);

            row["pdms_item"] = CreateFloorInfoXml(floorInfo);
            if (!floor.IsNull)
            {
                histories.Add(new RunPdms.ElementHistory
                {
                    ItemRef = floor.GetAsString(DbAttributeInstance.REF),
                    ItemFlnm = floor.GetAsString(DbAttributeInstance.FLNM),
                    ItemType = floor.GetElementType().ShortName,
                    CreatedBy = row["beam_created_by"].ToString(),
                    ModifiedBy = row["beam_modified_by"].ToString(),
                    Created = DateTime.Parse(row["beam_created"].ToString()),
                    Modified = DateTime.Parse(row["beam_modified"].ToString())
                });
                floor.SetAttribute(_polyTeklaGuid, row["beam_guid"].ToString().Trim());
            }
            return true;
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
                            RunPdms.SendMessage("[ModifyFloor] Не удалось удалить лишний элемент PAVE " +
                                                pavert.GetAsString(DbAttributeInstance.REF));
                        }
                    }
                    else
                    {
                        RunPdms.SendMessage("[ModifyFloor] Не удалось удалить лишний элемент PAVE " +
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
        /// Перемещение элемента в иерархии, если его класс изменился
        /// </summary>
        /// <param name="sctn">элемент</param>
        /// <param name="root">новый владелец</param>
        /// <returns></returns>
        private static bool MoveItemToNewClass(DbElement item, DbElement root)
        {
            if (!item.IsInsertable(root)) return false;
            item.InsertAfterLast(root);
            return true;
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
                GetCoordinatesFromXml(p, ref startPoint, ref endPoint, ref xVector, ref yVector, ref originPoint, ref height, ref width, ref level);
                var teklaElementPoint = new TeklaElementPoints
                {
                    StartPoint = onlyOriginChange? startPoint:TransformPosition(startPoint),
                    EndPoint = onlyOriginChange ? endPoint : TransformPosition(endPoint),
                    XVector = xVector,
                    YVector = yVector,
                    OriginPoint = TransformPosition(originPoint),
                    Height = height,
                    Width = width,
                    Level = level
                };
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
            ref string level)
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
            var oriPdms = Orientation.Create(oriTekla.ToString());
            oriPdms.Rotate(oriTekla.YDir(), 90);
            oriPdms.Rotate(oriTekla.ZDir(), 180);
            var sctnBang = sctn.GetDouble(DbAttributeInstance.BANG);
            if (sctnBang != 0)
            {
                sctn.SetAttribute(DbAttributeInstance.BANG, 0);
            }

            var oriSctn = sctn.GetOrientation(DbAttributeInstance.ORI);
            var yDirSctn = oriSctn.YDir();

            var bang = Math.Round(oriPdms.YDir().Angle(yDirSctn), 2);

            if (!bang.Equals(double.NaN) && bang > 0)
            {
                sctn.SetAttribute(DbAttributeInstance.BANG, bang);
                oriSctn = sctn.GetOrientation(DbAttributeInstance.ORI);
                yDirSctn = oriSctn.YDir();
                if (yDirSctn.Angle(oriPdms.YDir()) > 1)
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
        private static bool GetRootByClass(string className, DbElement zone, ref DbElement root, DbElementType itemType, string level)
        {
            short teklaClass;
            if (!Int16.TryParse(className, out teklaClass))
            {
                return false;
            }

            string rootName;
            if (RunPdms.ProjectInfo.Program.Version == "20.0")
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
                    case 2:
                        rootName = "KAR_COL";
                        break;
                    case 16:
                        rootName = "KAR_COL";
                        break;
                    case 51:
                        rootName = "KAR_COL";
                        break;
                    case 12:
                        rootName = "FHV_RF";
                        break;
                    case 21:
                        rootName = "FHV_SF";
                        break;
                    case 4:
                        rootName = "KAR_GSP_NP";
                        break;
                    case 11:
                        rootName = "KAR_GSP_VP";
                        break;
                    case 25:
                        rootName = "KAR_GSP_VP";
                        break;
                    case 27:
                        rootName = "KAR_PFL";
                        break;
                    case 41:
                        rootName = "KAR_PFL";
                        break;
                    case 18:
                        rootName = "KAR_PP";
                        break;
                    case 20:
                        rootName = "KAR_PP";
                        break;
                    case 24:
                        rootName = "KAR_PP";
                        break;
                    case 38:
                        rootName = "KAR_PP";
                        break;
                    case 3:
                        rootName = "KAR_PPT";
                        break;
                    case 7:
                        rootName = "KAR_PROG";
                        break;
                    case 6:
                        rootName = "KAR_SK";
                        break;
                    case 37:
                        rootName = "KAR_SK";
                        break;
                    case 5:
                        rootName = "KAR_VSK";
                        break;
                    case 19:
                        rootName = "KAR_VSK";
                        break;
                    case 14:
                        rootName = "KAR_VSP";
                        break;
                    case 35:
                        rootName = "NI_BNK";
                        break;
                    case 42:
                        rootName = "NI_BNK";
                        break;
                    case 13:
                        rootName = "OG_OGR";
                        break;
                    case 55:
                        rootName = "OG_OGR";
                        break;
                    case 99:
                        rootName = string.Format("UZL_{0}", itemType.Equals(DbElementTypeInstance.SCTN) ? "PROF" : "PL");
                        break;
                    case 17:
                        rootName = string.Format("PER_{0}_PP", level);
                        break;
                    case 31:
                        rootName = string.Format("PER_{0}_PP", level);
                        break;
                    case 9:
                        rootName = string.Format("PER_{0}_ST", level);
                        break;
                    case 1:
                        rootName = string.Format("PER_{0}_N", level);
                        break;
                    case 32:
                        rootName = string.Format("PER_{0}_N", level);
                        break;
                    case 10:
                        rootName = string.Format("PER_{0}_B", level);
                        break;
                    case 22:
                        rootName = string.Format("PER_{0}_B", level);
                        break;
                    case 26:
                        rootName = string.Format("PER_{0}_B", level);
                        break;
                    case 8:
                        rootName = string.Format("PER_{0}_SV", level);
                        break;
                    case 23:
                        rootName = string.Format("PER_{0}_L", level);
                        break;
                    case 65:
                        rootName = string.Format("PER_{0}_L", level);
                        break;
                    case 56:
                        rootName = "OPOR_TO";
                        break;
                    case 69:
                        rootName = "OPOR_TO";
                        break;
                    case 83:
                        rootName = "OPOR_EO";
                        break;
                    case 91:
                        rootName = "OPOR_SANO";
                        break;
                    default:
                        rootName = "OTHER_OTHER";
                        break;
                }
            }

            return !String.IsNullOrEmpty(rootName) && GetRootByName(rootName, zone, ref root);
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
                    case 2:
                        type = DbElementTypeInstance.SBFRAMEWORK;
                        break;
                    default:
                        type = null;
                        break;
                }

                if (type == null)
                {
                    return false;
                }

                //вылетает здесь, если повторно обращается из PdmsStandalone
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
                if (!DbElement.GetElement(newName).IsNull) newName = "/1" +owner.GetAsString(DbAttributeInstance.FLNN) + "_" + name;
                newItem.SetAttribute(DbAttributeInstance.NAME, newName);
                newItem.SetAttribute(DbAttributeInstance.DESC, GetDescription(name, type));
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
            var zone = DbElement.GetElement();
            var world = MDB.CurrentMDB.GetFirstWorld(DbType.Design);
            if (world.IsNull)
            {
                return DbElement.GetElement();
            }

            if (_site == null || _site.IsNull)
            {
                var siteCollection = new DBElementCollection(world, new TypeFilter(DbElementTypeInstance.SITE));
                foreach (DbElement item in siteCollection)
                {
                    var name = item.GetAsString(DbAttributeInstance.NAME).ToUpper();
                    if (!name.Contains(mdbName.Replace("-", "").ToUpper()) || !name.EndsWith("-SO")) continue;
                    _site = item;
                    break;
                }

                if (_site.IsNull || !_site.IsValid)
                {
                    return DbElement.GetElement();
                }
            }

            mark = mark.ToUpper().Replace('К', 'K').Replace('М', 'M').Replace("Ж", "G");
            mark = mark.StartsWith("KM") || mark.StartsWith("KG") ? mark : "KM";

            foreach (DbElement item in _site.Members(DbElementTypeInstance.ZONE))
            {
                var name = item.GetAsString(DbAttributeInstance.NAME).ToUpper();
                if (!name.StartsWith(_site.GetAsString(DbAttributeInstance.NAME) + "-" + mark)) continue;
                if (name.Split('-').Last() != _documentCode) continue;
                zone = item;
                break;
            }

            if (zone.IsNull)
            {
                CreateZoneElement(mark, ref zone);
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
            if (_site.IsCreatable(DbElementTypeInstance.ZONE) && !_site.GetBool(DbAttributeInstance.LOCK))
            {
                zone = _site.CreateLast(DbElementTypeInstance.ZONE);
                if (!zone.IsNull && zone.IsValid)
                {                    
                    zone.SetAttribute(DbAttributeInstance.NAME, $"{_site.GetAsString(DbAttributeInstance.NAME)}-{mark}-{_documentCode}");
                    zone.SetAttribute(DbAttributeInstance.DESC, GetDescription(mark.Substring(0,2), DbElementTypeInstance.ZONE));
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
            var tablName = "";
            if (type == DbElementTypeInstance.ZONE)
            {
                tablName = "DocumentMark";
            }
            else if (type == DbElementTypeInstance.STRUCTURE)
            {
                tablName = "KMIdent";
            }
            else if (type == DbElementTypeInstance.FRMWORK)
            {
                tablName = "KMElementsIdent";
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
