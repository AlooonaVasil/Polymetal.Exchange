using System;
using System.Collections.Generic;
using System.Data.SqlTypes;
using System.Globalization;
using System.Xml;
using Polymetal.Exchange.Utilities;
using Tekla.Structures.Geometry3d;
using Tekla.Structures.Model;

namespace Polymetal.Exchange.Tekla.v20
{
    class Export
    {
        /// <summary>
        /// Запуск экспорта в MS SQL
        /// </summary>
        internal static bool Run()
        {
            bool result;
            var model = new Model();
            var ignorClasses = new List<string> { "43", "100", "108", "109" };
            var dt = Common.CreateTeklaTable();
            var allGuidTable = Common.CreateTeklaTable();
            var sqlHistory = Common.GetTeklaBeamHistory(RunTekla.ProjectInfo.ProjectId);
            var typeFilter = new[] { typeof(Beam), typeof(PolyBeam), typeof(ContourPlate) };
            var objectsEnum = model.GetModelObjectSelector().GetAllObjectsWithType(typeFilter);
            Phase phase;
            double height, width;
            var total = objectsEnum.GetSize();
            var current = 0;
            var oldPercent = 0.0;
            Console.WriteLine("Export from Tekla");
            while (objectsEnum.MoveNext())
            {
                current++;
                double percent = Math.Ceiling((double)current * 100 / total);
                if (oldPercent < percent)
                {
                    Console.WriteLine("Current progress {0}%   ", Math.Ceiling(percent));
                    oldPercent = percent;
                }

                var part = objectsEnum.Current as Part;
                if (part == null || ignorClasses.Contains(part.Class.Trim())) continue;

                var partGuid = part.Identifier.GUID;

                var partHistory = new PartHistory(part);
                var isModified = !sqlHistory.ContainsKey(partGuid) || sqlHistory[partGuid] < partHistory.ModifiedDate;

                if (isModified)
                {
                    var solid = part.GetSolid(Solid.SolidCreationTypeEnum.NORMAL);
                    var faces = solid.GetFaceEnumerator();
                    if (!faces.MoveNext()) continue;
                }

                var guidRow = allGuidTable.NewRow();
                guidRow["beam_guid"] = partGuid;
                allGuidTable.Rows.Add(guidRow);

                //if (!isModified && !part.Identifier.GUID.ToString().ToUpper().Equals("56123293-0000-0703-3134-343430353435".ToUpper())) continue;
                //if (!isModified && !part.Class.Equals("23")) continue;

                if (!isModified) continue;

                height = 0.0;
                width = 0.0;

                var info = new PartInfoClass(part);
                if (info.CenterPoints.Count < 2 || (part is ContourPlate && info.CenterPoints.Count < 3)) continue;

                var row = dt.NewRow();
                row["export_from"] = Common.Source.Tekla;
                //row["project_name"] = RunTekla.ProjectInfo.Program.Path;
                row["beam_guid"] = partGuid;
                row["beam_phase"] = part.GetPhase(out phase) ? phase.PhaseName : "";
                row["profile_name"] = part.Profile.ProfileString;
                row["material_name"] = part.Material.MaterialString;
                row["beam_name"] = part.Name;
                row["beam_type"] = part.GetType().Name;
                row["beam_class"] = part.Class;

                var level = GetPartLevel(part);

                if (part is ContourPlate)
                {
                    if (!part.GetReportProperty("PROFILE.HEIGHT", ref height)) continue;
                    row["beam_points"] = CreatePointsXml(info, level, height);
                }
                else if (part is PolyBeam)
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
                else if (part is Beam)
                {
                    row["beam_points"] = CreatePointsXml(info, level);
                }

                row["beam_booleans"] = CreateBooleansXml(info.ObjectSolid);

                row["beam_created_by"] = partHistory.CreatedBy;
                row["beam_modified_by"] = partHistory.ModifiedBy;
                row["beam_created"] = partHistory.CreatedDate;
                row["beam_modified"] = partHistory.ModifiedDate;

                dt.Rows.Add(row);
            }

            Common.SendMessage(Common.Source.Tekla, RunTekla.TriggerId, "С последнего обновления изменено " + dt.Rows.Count + " элементов");

            Common.SendMessage(Common.Source.Tekla, RunTekla.TriggerId, "Обновляем данные в SQL");
            result = Common.UpdateSql(dt, allGuidTable, Common.Source.Tekla, RunTekla.ProjectInfo.ProjectId);
            Common.SendMessage(Common.Source.Tekla, RunTekla.TriggerId, result ? "Данные обновлены" : "Данные не обновлены");

            return result;
        }

        private static string GetPartLevel(Part part)
        {
            var stringLevel = string.Empty;
            part.GetReportProperty("height_mark_PMI", ref stringLevel);
            stringLevel = stringLevel.Replace(".", NumberFormatInfo.CurrentInfo.CurrencyDecimalSeparator)
                .Replace(",", NumberFormatInfo.CurrentInfo.CurrencyDecimalSeparator);
            double doubleLevel;
            var result = Double.TryParse(stringLevel, out doubleLevel)
                ? (doubleLevel > 0 ? "+" : string.Empty) + (doubleLevel / 1000).ToString("0.000").Replace(",", ".")
                : "0.000";
            return result;
        }

        /// <summary>
        /// Создание SqlXml по центральным точкам элемента и системам координат
        /// </summary>
        /// <param name="partInfo">информация о геометрии элемента</param>
        /// <param name="height">высота, если задана</param>
        /// <param name="width">ширина, если задана</param>
        /// <returns>готовый SqlXml</returns>
        private static SqlXml CreatePointsXml(PartInfoClass partInfo, string level, double height = -1.0, double width = -1.0)
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

            return Common.XmlDocToSqlXml(doc);
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
                    xmlExtrusion.SetAttribute("Height", Math.Round(ext.Height, 4).ToString(CultureInfo.InvariantCulture));

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
                    xmlNode.SetAttribute("X", Math.Round(ext.System.Origin.X, rounding).ToString(CultureInfo.InvariantCulture));
                    xmlNode.SetAttribute("Y", Math.Round(ext.System.Origin.Y, rounding).ToString(CultureInfo.InvariantCulture));
                    xmlNode.SetAttribute("Z", Math.Round(ext.System.Origin.Z, rounding).ToString(CultureInfo.InvariantCulture));
                    xmlSystem.AppendChild(xmlNode);

                    xmlExtrusion.AppendChild(xmlSystem);

                    var xmlVertexList = doc.CreateElement(string.Empty, "VertexList", string.Empty);

                    foreach (var vertex in ext.VertexList)
                    {
                        xmlNode = doc.CreateElement(string.Empty, "Vertex", string.Empty);
                        xmlNode.SetAttribute("X", Math.Round(vertex.X, rounding).ToString(CultureInfo.InvariantCulture));
                        xmlNode.SetAttribute("Y", Math.Round(vertex.Y, rounding).ToString(CultureInfo.InvariantCulture));
                        xmlNode.SetAttribute("Z", Math.Round(vertex.Z, rounding).ToString(CultureInfo.InvariantCulture));
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
