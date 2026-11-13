using System;
using System.Collections.Generic;
using System.Data.SqlTypes;
using System.Globalization;
using System.Xml;
using Polymetal.Exchange.Utilities;
using Tekla.Structures.Geometry3d;
using Tekla.Structures.Model;
using System.Data.SqlClient;
using Tekla.Structures.Catalogs;
using System.Linq;
using Tekla.Structures.Solid;

namespace Polymetal.Exchange.Tekla.v2021
{
    class Export
    {
        private static List<string> _parametricProfiles;
        private static List<string> _parametricShapeProfiles;
        private const string IGNORE_PHASE_NAME = "EXC";

        /// <summary>
        /// Запуск экспорта в MS SQL
        /// </summary>
        internal static bool Run()
        {
            bool result;
            var model = new Model();
            var ignoreClasses = new List<string> { "15", "100", "102", "108", "109", "454", "86" };
            for (var i = 500; i <= 600; i++)
            {
                if (i < 510 || i > 513)
                    ignoreClasses.Add(i.ToString());
            }

            _parametricProfiles = new List<string> { "PHI", "HI", "D", "O", "B_WLD_A", "PD", "HXGON", "HK" };
            _parametricShapeProfiles = new List<string> { "B_WLD_K" };

            var sqlHistory = Common.GetTeklaBeamHistory(RunTekla.ProjectInfo.ProjectId);
            var typeFilter = new[] { typeof(Beam), typeof(PolyBeam), typeof(ContourPlate) };
            var objectsEnum = model.GetModelObjectSelector().GetAllObjectsWithType(typeFilter);
            double height, width;
            var total = objectsEnum.GetSize();
            var current = 0;
            var oldPercent = 0.0;
            Console.WriteLine("Export from Tekla");

            var create = Common.CreateTeklaTable();
            var modify = Common.CreateTeklaTable();
            var delete = Common.CreateTeklaTable();

            var existingDict = GetExistingItemsFromTeklaSQL(RunTekla.ProjectInfo.ProjectId,
                out Dictionary<string, string> propertiesDict);
            var concreteMaterials = GetConcreteMaterials();

            while (objectsEnum.MoveNext())
            {
                current++;
                double percent = Math.Ceiling((double)current * 100 / total);
                if (oldPercent < percent)
                {
                    Console.WriteLine("Current progress {0}%   ", Math.Ceiling(percent));
                    oldPercent = percent;
                }

                if (!(objectsEnum.Current is Part part) || ignoreClasses.Contains(part.Class.Trim()))
                    continue;

                var properties = new DTO.PartProperties(part);

                if (properties.Phase.ToUpper().Contains(IGNORE_PHASE_NAME))
                    continue;

                var partGuid = part.Identifier.GUID;
                if (existingDict.ContainsKey(partGuid.ToString()))
                    existingDict[partGuid.ToString()] = true;

                var partHistory = new PartHistory(part);
                var isModified = !sqlHistory.ContainsKey(partGuid) || sqlHistory[partGuid] != partHistory.ModifiedDate;

                if (isModified && !part.GetSolid(Solid.SolidCreationTypeEnum.NORMAL).GetFaceEnumerator().MoveNext())
                    continue;

                //if (!isModified && !part.Identifier.GUID.ToString().ToUpper().Equals("56123293-0000-0703-3134-343430353435".ToUpper())) continue;
                //if (!isModified && !part.Class.Equals("23")) continue;

                var serializedProperties = properties.GetSerializedString();
                if (!isModified && propertiesDict.ContainsKey(partGuid.ToString()) &&
                    propertiesDict[partGuid.ToString()] == serializedProperties)
                    continue;

                height = 0.0;
                width = 0.0;

                var dt = existingDict.ContainsKey(partGuid.ToString()) ? modify : create;
                var row = dt.NewRow();
                row["export_from"] = Common.Source.Tekla;
                //row["project_name"] = RunTekla.ProjectInfo.Program.Path;
                row["beam_guid"] = partGuid;
                row["beam_phase"] = properties.Phase;
                row["profile_name"] = part.Profile.ProfileString;
                row["material_name"] = part.Material.MaterialString;
                row["beam_name"] = part.Name;
                row["beam_class"] = part.Class;
                row["beam_description"] = $"{part.Profile.ProfileString}, {part.Material.MaterialString}";
                row["beam_properties"] = serializedProperties;

                var level = GetPartLevel(part);
                var info = new PartInfoClass(part);

                if ((part is Beam padBeam && (padBeam.Type == Beam.BeamTypeEnum.PAD_FOOTING ||
                                              concreteMaterials.ContainsKey(part.Material.MaterialString))) ||
                    (part is PolyBeam padPolyBeam && concreteMaterials.ContainsKey(part.Material.MaterialString)) ||
                    (part is ContourPlate padContourPlate &&
                     concreteMaterials.ContainsKey(part.Material.MaterialString)))
                {
                    row["beam_type"] = $"{part.GetType().Name}Pad";
                    row["beam_points"] = CreateShapeInfoXml(part, level);
                    row["beam_booleans"] = CreateBooleansXml(new List<PartInfoClass.SolidInfo>());
                }
                else if ((part is Beam || part is PolyBeam) && info.ProfileInfo.IsParametric &&
                         _parametricShapeProfiles.Contains(info.ProfileInfo.ProfilePrefix))
                {
                    row["beam_type"] = $"{part.GetType().Name}Shape";
                    row["beam_points"] = CreateShapeInfoXml(part, level);
                    row["beam_booleans"] = CreateBooleansXml(new List<PartInfoClass.SolidInfo>());
                    row["profile_name"] = info.ProfileInfo.ProfileName;
                }
                else
                {
                    if (info.CenterPoints.Count < 2 || (part is ContourPlate && info.CenterPoints.Count < 3))
                        continue;

                    if (info.ProfileInfo.IsParametric && _parametricProfiles.Contains(info.ProfileInfo.ProfilePrefix))
                        row["profile_name"] = info.ProfileInfo.ProfileName;

                    row["beam_type"] = part.GetType().Name;
                    if (part is ContourPlate)
                    {
                        if (!part.GetReportProperty("PROFILE.HEIGHT", ref height))
                            continue;

                        row["beam_points"] = CreatePointsXml(info, level, height);
                    }
                    else if (part is PolyBeam || part is Beam)
                    {
                        if (part.GetReportProperty("PROFILE.HEIGHT", ref height) &&
                            part.GetReportProperty("PROFILE.WIDTH", ref width))
                        {
                            row["beam_points"] = CreatePointsXml(info, level, height, width);
                        }
                        else
                        {
                            row["beam_points"] = CreatePointsXml(info, level);
                        }
                    }

                    row["beam_booleans"] = CreateBooleansXml(info.ObjectSolid);
                }

                row["beam_created_by"] = partHistory.CreatedBy;
                row["beam_modified_by"] = partHistory.ModifiedBy;
                row["beam_created"] = partHistory.CreatedDate;
                row["beam_modified"] = partHistory.ModifiedDate;
                dt.Rows.Add(row);
            }

            foreach (var pair in existingDict)
            {
                if (pair.Value)
                    continue;

                var row = delete.NewRow();
                row["beam_guid"] = pair.Key;
                delete.Rows.Add(row);
            }

            if (existingDict.Count == delete.Rows.Count && existingDict.Count > 0 && create.Rows.Count == 0 &&
                RunTekla.ProjectInfo.Pdms.Project != "TRA")
            {
                Common.SendMessage(Common.Source.Tekla, RunTekla.TriggerId,
                    "Сбой в определнии элементов. Операция не выполнена");
                return false;
            }

            //Common.SendMessage(Common.Source.Tekla, RunTekla.TriggerId, "С последнего обновления изменено " + dt.Rows.Count + " элементов");
            Console.WriteLine("С последнего обновления изменено:");
            Console.WriteLine("\tсоздано\t\t{0}", create.Rows.Count);
            Console.WriteLine("\tизменено\t{0}", modify.Rows.Count);
            Console.WriteLine("\tудалено\t\t{0}", delete.Rows.Count);
            Common.SendMessage(Common.Source.Tekla, RunTekla.TriggerId, "Обновляем данные в SQL");

            //result = Common.UpdateSql(dt, allGuidTable, Common.Source.Tekla, RunTekla.ProjectInfo.ProjectId);
            result = Common.UpdateTeklaData(create, modify, delete, RunTekla.ProjectInfo.ProjectId);
            Common.SendMessage(Common.Source.Tekla, RunTekla.TriggerId,
                result ? "Данные обновлены" : "Данные не обновлены");

            return result;
        }

        private static Dictionary<string, bool> GetConcreteMaterials()
        {
            var output = new Dictionary<string, bool>();
            var catalogHandler = new CatalogHandler();
            var materialsEnum = catalogHandler.GetMaterialItems();
            while (materialsEnum.MoveNext())
            {
                if (materialsEnum.Current.Type != MaterialItem.MaterialItemTypeEnum.MATERIAL_CONCRETE ||
                    output.ContainsKey(materialsEnum.Current.MaterialName))
                    continue;

                output.Add(materialsEnum.Current.MaterialName, false);
            }

            return output;
        }

        private static Dictionary<string, bool> GetExistingItemsFromTeklaSQL(decimal projectId,
            out Dictionary<string, string> propertiesDict)
        {
            var result = new Dictionary<string, bool>();
            propertiesDict = new Dictionary<string, string>();
            using (var sqlConnection = new SqlConnection(Common.ConnectionString))
            {
                sqlConnection.Open();
                var sqlCommand = "select beam_guid, beam_properties from Tekla where project_id = " + projectId;
                using (var sqlCmd = new SqlCommand(sqlCommand, sqlConnection))
                {
                    var reader = sqlCmd.ExecuteReader();
                    while (reader.Read())
                    {
                        var beamGuid = reader["beam_guid"].ToString();
                        result.Add(beamGuid, false);

                        var properties = reader["beam_properties"] == DBNull.Value
                            ? string.Empty
                            : (string)reader["beam_properties"];
                        propertiesDict.Add(beamGuid, properties);
                    }

                    reader.Close();
                }

                sqlConnection.Close();
            }

            return result;
        }

        private static string GetPartLevel(Part part)
        {
            var stringLevel = string.Empty;
            //1 10 17 22 26 31 32
            var partClass = part.Class.Trim();
            if (partClass == "1" || partClass == "10" || partClass == "17" || partClass == "22" || partClass == "26" ||
                partClass == "31" || partClass == "32" || partClass == "99" || partClass == "29")
            {
                var att = part is ContourPlate ? "BOTTOM_LEVEL_GLOBAL" : "TOP_LEVEL_GLOBAL";
                part.GetReportProperty(att, ref stringLevel);
                if (string.IsNullOrEmpty(stringLevel) || stringLevel.Contains("0.000")) stringLevel = "0.000";

                if (double.TryParse(stringLevel.Replace(".", NumberFormatInfo.CurrentInfo.CurrencyDecimalSeparator)
                        .Replace(",", NumberFormatInfo.CurrentInfo.CurrencyDecimalSeparator), out double doubleLevel))
                {
                    //разделение на уровни, каждый метр для элементов узлов, каждые 3 метра для остальных элементов
                    var resLevel = doubleLevel -
                                   (partClass == "99" || partClass == "29" ? doubleLevel % 1 : doubleLevel % 3);
                    stringLevel = (resLevel > 0 ? "+" : string.Empty) + resLevel.ToString("0.000").Replace(",", ".");
                }
            }
            else
            {
                part.GetReportProperty("height_mark_PMI", ref stringLevel);
                stringLevel = stringLevel.Replace(".", NumberFormatInfo.CurrentInfo.CurrencyDecimalSeparator)
                    .Replace(",", NumberFormatInfo.CurrentInfo.CurrencyDecimalSeparator);
                stringLevel = double.TryParse(stringLevel, out double doubleLevel)
                    ? (doubleLevel > 0 ? "+" : string.Empty) + (doubleLevel / 1000).ToString("0.000").Replace(",", ".")
                    : "0.000";
            }

            return stringLevel.Trim();
        }

        /// <summary>
        /// Создание SqlXml по центральным точкам элемента и системам координат
        /// </summary>
        /// <param name="partInfo">информация о геометрии элемента</param>
        /// <param name="height">высота, если задана</param>
        /// <param name="width">ширина, если задана</param>
        /// <returns>готовый SqlXml</returns>
        private static SqlXml CreatePointsXml(PartInfoClass partInfo, string level, double height = -1.0,
            double width = -1.0)
        {
            var doc = new XmlDocument();
            XmlElement xmlPoint;
            var xmlDeclaration = doc.CreateXmlDeclaration("1.0", "UTF-8", null);
            var root = doc.DocumentElement;
            doc.InsertBefore(xmlDeclaration, root);
            var rootNode = doc.CreateElement(string.Empty, "Root", string.Empty);
            doc.AppendChild(rootNode);
            Vector xVector = new Vector(), yVector = new Vector();
            Point oPoint = new Point();
            Matrix contourPlateMatrix = null;
            if (partInfo.Object is ContourPlate && partInfo.CoordinateSystems.Count > 0)
            {
                var contourPlateSystem = partInfo.CoordinateSystems[0];
                contourPlateMatrix = MatrixFactory.ToCoordinateSystem(contourPlateSystem);
            }

            for (int i = 0; i < partInfo.CenterPoints.Count - 1; i++)
            {
                var sPoint = partInfo.CenterPoints[i];
                var ePoint = partInfo.CenterPoints[i + 1];
                if (i < partInfo.CoordinateSystems.Count)
                {
                    xVector = partInfo.CoordinateSystems[i].AxisX.GetNormal();
                    yVector = partInfo.CoordinateSystems[i].AxisY.GetNormal();
                    oPoint = partInfo.CoordinateSystems[i].Origin;
                }

                xmlPoint = doc.CreateElement(string.Empty, "Point", string.Empty);
                if (partInfo.Object is ContourPlate && contourPlateMatrix != null)
                {
                    sPoint = contourPlateMatrix.Transform(sPoint);
                    ePoint = contourPlateMatrix.Transform(ePoint);
                }

                xmlPoint.SetAttribute("StartX", Math.Round(sPoint.X, 4).ToString(CultureInfo.InvariantCulture));
                xmlPoint.SetAttribute("StartY", Math.Round(sPoint.Y, 4).ToString(CultureInfo.InvariantCulture));
                xmlPoint.SetAttribute("StartZ", Math.Round(sPoint.Z, 4).ToString(CultureInfo.InvariantCulture));
                xmlPoint.SetAttribute("EndX", Math.Round(ePoint.X, 4).ToString(CultureInfo.InvariantCulture));
                xmlPoint.SetAttribute("EndY", Math.Round(ePoint.Y, 4).ToString(CultureInfo.InvariantCulture));
                xmlPoint.SetAttribute("EndZ", Math.Round(ePoint.Z, 4).ToString(CultureInfo.InvariantCulture));
                xmlPoint.SetAttribute("xVectorX", Math.Round(xVector.X, 4).ToString(CultureInfo.InvariantCulture));
                xmlPoint.SetAttribute("xVectorY", Math.Round(xVector.Y, 4).ToString(CultureInfo.InvariantCulture));
                xmlPoint.SetAttribute("xVectorZ", Math.Round(xVector.Z, 4).ToString(CultureInfo.InvariantCulture));
                xmlPoint.SetAttribute("yVectorX", Math.Round(yVector.X, 4).ToString(CultureInfo.InvariantCulture));
                xmlPoint.SetAttribute("yVectorY", Math.Round(yVector.Y, 4).ToString(CultureInfo.InvariantCulture));
                xmlPoint.SetAttribute("yVectorZ", Math.Round(yVector.Z, 4).ToString(CultureInfo.InvariantCulture));
                xmlPoint.SetAttribute("OriginX", Math.Round(oPoint.X, 4).ToString(CultureInfo.InvariantCulture));
                xmlPoint.SetAttribute("OriginY", Math.Round(oPoint.Y, 4).ToString(CultureInfo.InvariantCulture));
                xmlPoint.SetAttribute("OriginZ", Math.Round(oPoint.Z, 4).ToString(CultureInfo.InvariantCulture));
                rootNode.AppendChild(xmlPoint);
            }

            if (height >= 0)
            {
                rootNode.SetAttribute("Height", Math.Round(height, 4).ToString(CultureInfo.InvariantCulture));
            }

            if (width >= 0)
            {
                rootNode.SetAttribute("Width", Math.Round(width, 4).ToString(CultureInfo.InvariantCulture));
            }

            rootNode.SetAttribute("Level", level);
            if (partInfo.ProfileInfo.IsParametric && _parametricProfiles.Contains(partInfo.ProfileInfo.ProfilePrefix))
            {
                var strParams = string.Empty;
                foreach (var pair in partInfo.ProfileInfo.Parameters)
                {
                    strParams += $"{pair.Key}={pair.Value};";
                }

                rootNode.SetAttribute("Params", strParams);
            }

            return Common.XmlDocToSqlXml(doc);
        }

        private static SqlXml CreateShapeInfoXml(Part mainPart, string level)
        {
            var xmlDocument = new XmlDocument();
            var xmlDeclaration = xmlDocument.CreateXmlDeclaration("1.0", "UTF-8", null);
            var xmlElementRoot = xmlDocument.DocumentElement;
            xmlDocument.InsertBefore(xmlDeclaration, xmlElementRoot);
            var xmlRootNode = xmlDocument.CreateElement(string.Empty, "Root", string.Empty);
            xmlDocument.AppendChild(xmlRootNode);
            var allPartsDict = new Dictionary<Part, List<Part>>();
            mainPart.GetAllParts(ref allPartsDict);

            foreach (var pair in allPartsDict)
            {
                var part = pair.Key;
                var negatives = pair.Value;
                var positiveGeometry = GetShapeGeometry(part, out List<Helpers.ConcreteGeometry> negativeList);

                if (positiveGeometry.GeoType == Helpers.ConcreteGeometry.GeometryType.UNDEF)
                {
                    var rawSolid = part.GetSolid(Solid.SolidCreationTypeEnum.RAW);
                    positiveGeometry =
                        new Helpers.PolyHedronGeometry(mainPart.GetCoordinateSystem(), rawSolid.GetFaceEnumerator());
                }

                foreach (var negativePart in negatives)
                {
                    if (negativePart.GetChildren().MoveNext())
                    {
                        var normalSolid = negativePart.GetSolid(Solid.SolidCreationTypeEnum.NORMAL);
                        var negativeGeometry = new Helpers.PolyHedronGeometry(mainPart.GetCoordinateSystem(),
                            normalSolid.GetFaceEnumerator());
                        negativeList.Add(negativeGeometry);
                    }
                    else
                    {
                        var negativeGeometry =
                            GetShapeGeometry(negativePart, out List<Helpers.ConcreteGeometry> tmpList);
                        if (negativeGeometry.GeoType == Helpers.ConcreteGeometry.GeometryType.UNDEF)
                        {
                            var rawSolid = negativePart.GetSolid(Solid.SolidCreationTypeEnum.RAW);
                            negativeGeometry = new Helpers.PolyHedronGeometry(mainPart.GetCoordinateSystem(),
                                rawSolid.GetFaceEnumerator());
                        }

                        negativeList.Add(negativeGeometry);
                    }
                }

                var fullSolid = part.GetSolid(Solid.SolidCreationTypeEnum.RAW);
                var cuttedSolid = part.GetSolid(Solid.SolidCreationTypeEnum.PLANECUTTED);

                var shellEnum = fullSolid.GetCutPart(cuttedSolid);
                while (shellEnum.MoveNext())
                {
                    var shell = shellEnum.Current as Shell;
                    negativeList.Add(new Helpers.PolyHedronGeometry(mainPart.GetCoordinateSystem(),
                        shell.GetFaceEnumerator()));
                }

                var positiveXmlElement = positiveGeometry.GetXmlElement(xmlDocument);
                if (negativeList.Count > 0)
                {
                    var negativesArrayXmlElement = xmlDocument.CreateElement(string.Empty, "Negatives", string.Empty);

                    foreach (var negative in negativeList)
                    {
                        var negativeXmlElement = negative.GetXmlElement(xmlDocument);
                        negativesArrayXmlElement.AppendChild(negativeXmlElement);
                    }

                    positiveXmlElement.AppendChild(negativesArrayXmlElement);
                }

                xmlRootNode.AppendChild(positiveXmlElement);
            }

            xmlRootNode.SetAttribute("Level", level);

            return Common.XmlDocToSqlXml(xmlDocument);
        }

        //private static bool GetPadInfo(Part part, out CoordinateSystem cs, out List<Point> points, out double height)
        //{            
        //    cs = part.GetCoordinateSystem();            
        //    points = new List<Point>();
        //    height = 0.0;
        //    if (part is Beam beam)
        //    {
        //        var vx = cs.AxisY.Cross(cs.AxisX);
        //        var vy = new Vector(cs.AxisY);
        //        vx.Normalize();
        //        vy.Normalize();
        //        cs = new CoordinateSystem(cs.Origin, vx, vy);
        //        var matrixFactory = MatrixFactory.FromCoordinateSystem(cs);

        //        var width = 0.0;
        //        var length = Distance.PointToPoint(beam.StartPoint, beam.EndPoint);
        //        beam.GetReportProperty("PROFILE.HEIGHT", ref height);
        //        beam.GetReportProperty("PROFILE.WIDTH", ref width);

        //        if (height == 0.0 || width == 0.0)
        //        {
        //            var profileData = beam.Profile.ProfileString.Replace(".",",").Split('*');
        //            if (profileData.Length > 1)
        //            {
        //                double.TryParse(profileData[0], out height);
        //                double.TryParse(profileData[1], out width);
        //            }
        //        }

        //        points.Add(matrixFactory.Transform(new Point(-width / 2, -height / 2, 0.0)));
        //        points.Add(matrixFactory.Transform(new Point(-width / 2, height / 2, 0.0)));
        //        points.Add(matrixFactory.Transform(new Point(width / 2, height / 2, 0.0)));
        //        points.Add(matrixFactory.Transform(new Point(width / 2, -height / 2, 0.0)));
        //        height = length;
        //    }
        //    else if (part is ContourPlate contourPlate)
        //    {
        //        contourPlate.GetReportProperty("PROFILE.HEIGHT", ref height);
        //        foreach (ContourPoint contourPoint in contourPlate.Contour.ContourPoints)
        //        {
        //            points.Add(contourPoint);
        //        }

        //        if (points.Count > 0)
        //        {
        //            var axisZ = cs.AxisX.Cross(cs.AxisY);
        //            var vt = new LineSegment(points[0], cs.Origin).GetDirectionVector();
        //            var angle = axisZ.GetAngleBetween(vt);
        //            if (angle > Math.PI/2) cs.AxisY = cs.AxisX.Cross(axisZ);                    
        //        }
        //    }
        //    else
        //    {
        //        Console.WriteLine($"method CreateShapeInfoXml() Part is {part.GetType().Name}");
        //        return false;
        //    }
        //    return true;
        //}

        /// <summary>
        /// Попутка получения примитивной геометрии профиля
        /// </summary>
        /// <param name="part"></param>
        /// <param name="negativeList"></param>
        /// <returns></returns>
        private static Helpers.ConcreteGeometry GetShapeGeometry(Part part,
            out List<Helpers.ConcreteGeometry> negativeList)
        {
            negativeList = new List<Helpers.ConcreteGeometry>();
            var cs = part.GetCoordinateSystem();

            if (part is Beam beam)
            {
                var radius = 0.0;
                beam.GetReportProperty("RADIUS", ref radius);
                var centerLine = beam.GetCenterLine(false);
                var length = Distance.PointToPoint((Point)centerLine[0], (Point)centerLine[1]);
                //если нестандартный профиль, то передаем его через GENPRI
                if (radius != 0.0 || centerLine.Count > 2 || beam.DeformingData.Angle != 0.0 ||
                    beam.DeformingData.Angle2 != 0.0 || beam.DeformingData.Cambering != 0.0 ||
                    beam.DeformingData.Shortening != 0.0)
                    return new Helpers.ConcreteGeometry();

                var vx = cs.AxisY.Cross(cs.AxisX);
                var vy = new Vector(cs.AxisY);
                vx.Normalize();
                vy.Normalize();
                cs = new CoordinateSystem(cs.Origin, vx, vy);
                var matrixFactory = MatrixFactory.FromCoordinateSystem(cs);

                var profile = new Profile(beam.Profile.ProfileString);
                if (!profile.IsParametric && profile.ProfileName.StartsWith("O"))
                {
                    var tmpProfile = new Profile(beam.Profile.ProfileString.Split('_').First());
                    if (tmpProfile.ProfileName == "O_d*t") profile = tmpProfile;
                }

                if (profile.ProfilePrefix == "PRMD" && profile.Parameters.ContainsKey("h") &&
                    profile.Parameters.ContainsKey("b") && profile.Parameters.ContainsKey("h2") &&
                    profile.Parameters.ContainsKey("b2"))
                {
                    return new Helpers.PyraGeometry(cs, profile.Parameters["b"], profile.Parameters["h"],
                        profile.Parameters["b2"], profile.Parameters["h2"], length);
                }

                if ((profile.ProfilePrefix == "" || profile.ProfilePrefix == "PL" || profile.ProfilePrefix == "PLT") &&
                    profile.Parameters.ContainsKey("h") && profile.Parameters.ContainsKey("b"))
                {
                    return new Helpers.BoxGeometry(cs, profile.Parameters["b"], profile.Parameters["h"], length);
                }

                if ((profile.ProfilePrefix == "D" || profile.ProfilePrefix == "ROD") &&
                    profile.Parameters.ContainsKey("d"))
                {
                    return new Helpers.CyliGeometry(cs, profile.Parameters["d"], length);
                }

                if (profile.ProfileName == "O_d*t" || profile.ProfileName == "TUBE_d*t")
                {
                    negativeList.Add(new Helpers.CyliGeometry(cs,
                        profile.Parameters["d"] - profile.Parameters["t"] * 2.0, length));
                    return new Helpers.CyliGeometry(cs, profile.Parameters["d"], length);
                }

                if (profile.ProfileName == "ELD_d1*r1*d2*r2" && profile.Parameters["d1"] == profile.Parameters["r1"] &&
                    profile.Parameters["d2"] == profile.Parameters["r2"])
                {
                    return new Helpers.ConeGeometry(cs, profile.Parameters["d1"], profile.Parameters["d2"], length);
                }

                if (profile.ProfileName == "C_h*b*t")
                {
                    var h = profile.Parameters["h"];
                    var b = profile.Parameters["b"];
                    var t = profile.Parameters["t"];
                    var points = new List<Point>()
                    {
                        matrixFactory.Transform(new Point(b / 2 - t, h / 2 - t)),
                        matrixFactory.Transform(new Point(-b / 2, h / 2 - t)),
                        matrixFactory.Transform(new Point(-b / 2, h / 2)),
                        matrixFactory.Transform(new Point(b / 2, h / 2)),
                        matrixFactory.Transform(new Point(b / 2, -h / 2)),
                        matrixFactory.Transform(new Point(-b / 2, -h / 2)),
                        matrixFactory.Transform(new Point(-b / 2, -h / 2 + t)),
                        matrixFactory.Transform(new Point(b / 2 - t, -h / 2 + t))
                    };
                    return new Helpers.ExtrGeometry(cs, points, length, false);
                }

                if (profile.ProfileName == "H175" || profile.ProfileName == "H175T" ||
                    profile.ProfileName == "H186" || profile.ProfileName == "H186T")
                {
                    //var item = new LibraryProfileItem();
                    //if (item.Select(profile.ProfileName))
                    //{
                    //    var section = item.GetCrossSection();
                    //    if (section?.OuterSurface != null)
                    //    {
                    //        return new Helpers.ExtrGeometry(cs, section.OuterSurface.Select(x=> matrixFactory.Transform(x)).ToList(), length, false);
                    //    }
                    //}
                    var extrudeVector = part.GetCoordinateSystem().AxisX.GetNormal();
                    var faceEnum = part.GetSolid(Solid.SolidCreationTypeEnum.RAW).GetFaceEnumerator();
                    while (faceEnum.MoveNext())
                    {
                        var normal = faceEnum.Current.Normal.GetNormal();
                        var dot = Vector.Dot(normal, extrudeVector.GetNormal());
                        // Если скалярное произведение близко к -1 — векторы противоположно направлены
                        if (Math.Abs(dot + 1.0) < 1e-6)
                        {
                            var profilePoints = new List<Point>();
                            var loopEnum = faceEnum.Current.GetLoopEnumerator();
                            while (loopEnum.MoveNext())
                            {
                                var vertexEnum = loopEnum.Current.GetVertexEnumerator();
                                while (vertexEnum.MoveNext())
                                {
                                    profilePoints.Add(vertexEnum.Current);
                                }
                            }

                            return new Helpers.ExtrGeometry(cs, profilePoints, length, false);
                        }
                    }
                }

                if (profile.ProfileName == "IRR_I_h*b*b2")
                {
                    var h = profile.Parameters["h"];
                    var b = profile.Parameters["b"];
                    var b2 = profile.Parameters["b2"];
                    var points = new List<Point>()
                    {
                        matrixFactory.Transform(new Point(-b / 2, h / 2)),
                        matrixFactory.Transform(new Point(b / 2, h / 2)),
                        matrixFactory.Transform(new Point(b / 2, -h / 2)),
                        matrixFactory.Transform(new Point(b / 2 - b2, -h / 2))
                    };
                    return new Helpers.ExtrGeometry(cs, points, length, false);
                }
            }
            else if (part is ContourPlate contourPlate)
            {
                if (contourPlate.Contour.ContourPoints.Count == 0) return new Helpers.ConcreteGeometry();

                var height = 0.0;
                var points = new List<Point>();

                contourPlate.GetReportProperty("PROFILE.HEIGHT", ref height);
                if (height == 0.0)
                {
                    var contourPlateProfile = new Profile(contourPlate.Profile.ProfileString);
                    if (contourPlateProfile.IsParametric && contourPlateProfile.Parameters.ContainsKey("h"))
                        height = contourPlateProfile.Parameters["h"];
                }

                if (!contourPlate.Contour.ContourPoints.Cast<ContourPoint>().Any(x =>
                        x.Chamfer.Type != Chamfer.ChamferTypeEnum.CHAMFER_NONE ||
                        x.Chamfer.X != 0.0 ||
                        x.Chamfer.Y != 0.0 ||
                        x.Chamfer.DZ1 != 0.0 ||
                        x.Chamfer.DZ2 != 0.0))
                {
                    points = contourPlate.GetCenterLine(false).Cast<Point>().ToList();
                    return new Helpers.ExtrGeometry(cs, points, height);
                }
            }

            return new Helpers.ConcreteGeometry();
        }

        /// <summary>
        /// Создание SqlXml по всем подрезкам элемента
        /// </summary>
        /// <param name="solidList">список информации по всем объемам элемента</param>
        /// <returns>готовый SqlXml</returns>
        private static SqlXml CreateBooleansXml(List<PartInfoClass.SolidInfo> solidList)
        {
            var rounding = 4;
            var doc = new XmlDocument();
            XmlElement xmlNode;
            var xmlDeclaration = doc.CreateXmlDeclaration("1.0", "UTF-8", null);
            var root = doc.DocumentElement;
            doc.InsertBefore(xmlDeclaration, root);
            var rootNode = doc.CreateElement(string.Empty, "Booleans", string.Empty);
            doc.AppendChild(rootNode);

            foreach (var solid in solidList)
            {
                var xmlBoolean = doc.CreateElement(string.Empty, "Boolean", string.Empty);
                var extrusionList = solid.Extrusions;
                foreach (var ext in extrusionList)
                {
                    var xmlExtrusion = doc.CreateElement(string.Empty, "Extrusion", string.Empty);
                    xmlExtrusion.SetAttribute("Height",
                        Math.Round(ext.Height, 4).ToString(CultureInfo.InvariantCulture));

                    if (ext.Diameter > 0)
                        xmlExtrusion.SetAttribute("Diameter",
                            Math.Round(ext.Diameter, 4).ToString(CultureInfo.InvariantCulture));

                    var xmlSystem = doc.CreateElement(string.Empty, "CoordinateSystem", string.Empty);
                    var xAxis = new Vector(ext.System.AxisX.X, ext.System.AxisX.Y, ext.System.AxisX.Z);
                    var yAxis = new Vector(ext.System.AxisY.X, ext.System.AxisY.Y, ext.System.AxisY.Z);
                    xAxis.Normalize();
                    yAxis.Normalize();
                    xmlNode = doc.CreateElement(string.Empty, "AxisX", string.Empty);
                    xmlNode.SetAttribute("X", Math.Round(xAxis.X, 4).ToString(CultureInfo.InvariantCulture));
                    xmlNode.SetAttribute("Y", Math.Round(xAxis.Y, 4).ToString(CultureInfo.InvariantCulture));
                    xmlNode.SetAttribute("Z", Math.Round(xAxis.Z, 4).ToString(CultureInfo.InvariantCulture));
                    xmlSystem.AppendChild(xmlNode);

                    xmlNode = doc.CreateElement(string.Empty, "AxisY", string.Empty);
                    xmlNode.SetAttribute("X", Math.Round(yAxis.X, 4).ToString(CultureInfo.InvariantCulture));
                    xmlNode.SetAttribute("Y", Math.Round(yAxis.Y, 4).ToString(CultureInfo.InvariantCulture));
                    xmlNode.SetAttribute("Z", Math.Round(yAxis.Z, 4).ToString(CultureInfo.InvariantCulture));
                    xmlSystem.AppendChild(xmlNode);

                    xmlNode = doc.CreateElement(string.Empty, "Origin", string.Empty);
                    xmlNode.SetAttribute("X",
                        Math.Round(ext.System.Origin.X, rounding).ToString(CultureInfo.InvariantCulture));
                    xmlNode.SetAttribute("Y",
                        Math.Round(ext.System.Origin.Y, rounding).ToString(CultureInfo.InvariantCulture));
                    xmlNode.SetAttribute("Z",
                        Math.Round(ext.System.Origin.Z, rounding).ToString(CultureInfo.InvariantCulture));
                    xmlSystem.AppendChild(xmlNode);

                    xmlExtrusion.AppendChild(xmlSystem);

                    var xmlVertexList = doc.CreateElement(string.Empty, "VertexList", string.Empty);

                    foreach (var vertex in ext.VertexList)
                    {
                        xmlNode = doc.CreateElement(string.Empty, "Vertex", string.Empty);
                        xmlNode.SetAttribute("X",
                            Math.Round(vertex.X, rounding).ToString(CultureInfo.InvariantCulture));
                        xmlNode.SetAttribute("Y",
                            Math.Round(vertex.Y, rounding).ToString(CultureInfo.InvariantCulture));
                        xmlNode.SetAttribute("Z",
                            Math.Round(vertex.Z, rounding).ToString(CultureInfo.InvariantCulture));
                        xmlVertexList.AppendChild(xmlNode);
                    }

                    xmlExtrusion.AppendChild(xmlVertexList);

                    /*if (!xmlBoolean.ChildNodes.Cast<XmlElement>().Any(node => node.OuterXml.Equals(xmlExtrusion.OuterXml)))
                        xmlBoolean.AppendChild(xmlExtrusion);*/
                    xmlBoolean.AppendChild(xmlExtrusion);
                }

                rootNode.AppendChild(xmlBoolean);
            }

            return Common.XmlDocToSqlXml(doc);
        }
    }
}