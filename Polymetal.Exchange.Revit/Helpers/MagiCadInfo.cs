using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;

namespace Polymetal.Exchange.Revit.Helpers
{
    enum ElementTypeEnum
    {
        DUCT,
        PIPE,
        EQUI,
        REZV,
        SUPP
    }

    class MagiCadInfo
    {
        internal Element Root;
        internal Element Family;
        internal int Id;
        internal string Name;
        internal string Description;
        internal string Sign;
        internal string Plant;
        internal double Weight;
        internal string Unit;
        internal double Count;
        internal string Comment;
        internal string System;
        internal string CreatedBy;
        internal string ModifiedBy;
        internal bool Ignore;
        internal string UniqueId;
        internal ElementTypeEnum ElementType;

        internal List<int> ConnectedToList;

        internal Dictionary<string, string> RootParameters;
        internal static Dictionary<string, string> SystemsDictionary;

        internal MagiCadInfo()
        {
            Name = string.Empty;
            Description = string.Empty;
            Sign = string.Empty;
            Plant = string.Empty;
            Weight = 0.0;
            Unit = string.Empty;
            Count = 0.0;
            Comment = string.Empty;
            System = string.Empty;
            CreatedBy = string.Empty;
            ModifiedBy = string.Empty;
            Ignore = false;
            RootParameters = new Dictionary<string, string>();
        }

        internal void SetCommonProperties()
        {
            Id = Root.Id.IntegerValue;
            UniqueId = Root.UniqueId;
            Name = string.Empty;
            var mcObcjetVariable2Param = Root.LookupParameter(Attributes.MC_OBJECT_VARIABLE_2);
            Name = mcObcjetVariable2Param == null ? string.Empty : mcObcjetVariable2Param.AsString();
            Name = string.IsNullOrEmpty(Name) ? string.Empty : Name.Replace(" ", string.Empty);

            var mcNationalCodeParam = Family.LookupParameter(Attributes.MC_NATIONAL_CODE);
            Sign = mcNationalCodeParam == null ? string.Empty : mcNationalCodeParam.AsString();

            Plant = Family.get_Parameter(BuiltInParameter.ALL_MODEL_MANUFACTURER).AsString();

            if (Description == "Соединение") Sign = Plant = string.Empty;

            if (Root is Duct)
            {
                Unit = "мм";
            }
            else
            {
                Unit = "шт.";
                Count = 1.0;
            }

            Description = string.IsNullOrEmpty(Description) ? string.Empty : Description;
            Sign = string.IsNullOrEmpty(Sign) ? string.Empty : Sign;
            Plant = string.IsNullOrEmpty(Plant) ? string.Empty : Plant;
            Description = ReplaceParameters(Description);
            Sign = ReplaceParameters(Sign);

            System = GetSystemName();

            var tooltipInfo = WorksharingUtils.GetWorksharingTooltipInfo(Root.Document, Root.Id);
            var createdBy = tooltipInfo.Creator.ToLower();
            var modifiedBy = tooltipInfo.LastChangedBy.ToLower();

            var ignoreParam = Root.LookupParameter(Attributes.NEED_IN_AVEVA);
            Ignore = ignoreParam != null && ignoreParam.HasValue && ignoreParam.AsInteger() == 0;

            ConnectedToList = GetConnections(Root);
        }

        /// <summary>
        /// Получение списка ID присоединенных элементов       
        /// </summary>
        /// <param name="element"></param>
        /// <returns></returns>
        internal List<int> GetConnections(Autodesk.Revit.DB.Element element)
        {
            //не удалять пространство имен
            var result = new List<int>();
            var connectorManager = default(ConnectorManager);
            if (element is Autodesk.Revit.DB.FamilyInstance familyInstance)
            {
                connectorManager = familyInstance.MEPModel.ConnectorManager;
            }
            else if (element is MEPCurve mepCurve)
            {
                connectorManager = mepCurve.ConnectorManager;
            }
            else
            {
                connectorManager = default(ConnectorManager);
            }

            if (connectorManager == null) return result;

            foreach (Connector connector in connectorManager.Connectors)
            {
                if (connector.MEPSystem == null || !connector.IsConnected) continue;

                foreach (Connector connected in connector.AllRefs)
                {
                    if (connected.ConnectorType == ConnectorType.Logical) continue;
                    var connectedId = connected.Owner.Id.IntegerValue;
                    if (connectedId == connectorManager.Owner.Id.IntegerValue || result.Contains(connectedId)) continue;
                    result.Add(connectedId);
                }
            }

            return result;
        }

        private string GetSystemName()
        {
            var output = string.Empty;

            var systemNameParam = Root.get_Parameter(BuiltInParameter.RBS_SYSTEM_NAME_PARAM);
            if (systemNameParam == null || !systemNameParam.HasValue) return string.Empty;
            var subSystemName = systemNameParam.AsString();
            if (SystemsDictionary.ContainsKey(subSystemName))
            {
                return SystemsDictionary[subSystemName];
            }

            if (subSystemName.Contains(","))
            {
                var firstSubSystem = subSystemName.Split(',').FirstOrDefault();
                if (!string.IsNullOrEmpty(firstSubSystem) && SystemsDictionary.ContainsKey(firstSubSystem))
                    return SystemsDictionary[firstSubSystem];
            }
            return string.Empty;
        }


        internal double GetWallThickness(double[] sizes)
        {
            var wallThickness = 0.0;
            var isFireProtected = IsFireProtected();
            if (sizes.Length == 1)
            {
                var size = sizes[0];
                if (size <= 200.0) wallThickness = isFireProtected ? 0.8 : 0.5;
                else if (size <= 450.0) wallThickness = isFireProtected ? 0.8 : 0.6;
                else if (size <= 800.0) wallThickness = isFireProtected ? 0.8 : 0.7;
                else if (size <= 1250.0) wallThickness = 1.0;
                else if (size <= 1600.0) wallThickness = 1.2;
                else if (size <= 2000.0) wallThickness = 1.4;
            }
            else if (sizes.Length == 2)
            {
                var size = Math.Max(sizes[0], sizes[1]);
                if (size <= 250.0) wallThickness = isFireProtected ? 0.8 : 0.5;
                else if (size <= 1000.0) wallThickness = isFireProtected ? 0.8 : 0.7;
                else if (size <= 2000.0) wallThickness = 0.9;
            }

            var wallThicknessParam = Root.LookupParameter(Attributes.PMI_WALL_THICKNESS);
            var wallThicknessValue = wallThicknessParam == null ? 0.0 : Math.Round(wallThicknessParam.AsDouble() * Constants.FACTOR_LENGTH, 2);

            return Math.Max(wallThickness, wallThicknessValue);
        }

        internal bool IsFireProtected()
        {
            var result = false;
            try
            {
                var insulationIds = InsulationLiningBase.GetInsulationIds(Root.Document, Root.Id);

                foreach (var insuId in insulationIds)
                {
                    var insulation = Root.Document.GetElement(insuId);
                    var insulationFamily = Root.Document.GetElement(insulation.GetTypeId());
                    result = insulationFamily.get_Parameter(BuiltInParameter.ALL_MODEL_DESCRIPTION).AsString().ToLower().Contains("огнезащита");
                    if (result) return result;
                }
            }
            catch { }
            return result;
        }

        internal string ReplaceParameters(string input)
        {
            if (!RootParameters.ContainsKey("[TYPENAME]") && Family != null) RootParameters.Add("[TYPENAME]", Family.Name);
            var dict = new Dictionary<string, string>();
            var matches = Regex.Matches(input, "\\[\\w*\\]");
            foreach (var match in matches)
            {
                var key = match.ToString();
                if (dict.ContainsKey(key)) continue;
                var value = string.Empty;
                switch (key.ToUpper())
                {
                    case "[ANGLE]":
                        value = RootParameters.ContainsKey("[ANGLE]") ? RootParameters["[ANGLE]"] : key;
                        break;
                    case "[THICKNESS]":
                        value = RootParameters.ContainsKey("[THICKNESS]") ? RootParameters["[THICKNESS]"] : key;
                        break;
                    case "[SIZE]":
                        value = RootParameters.ContainsKey("[SIZE]") ? RootParameters["[SIZE]"] : key;
                        break;
                    case "[LENGTH]":
                        value = RootParameters.ContainsKey("[LENGTH]") ? RootParameters["[LENGTH]"] : key;
                        break;
                    case "[TYPENAME]":
                        value = RootParameters.ContainsKey("[TYPENAME]") ? RootParameters["[TYPENAME]"] : key;
                        break;
                    default:
                        value = key;
                        break;
                }
                dict.Add(key, value);
            }

            foreach (var pair in dict) input = input.Replace(pair.Key, pair.Value);
            return input;
        }

        internal string SizeToString(double[] input, bool sort = false)
        {
            if (!sort) return (input.Length == 1 ? $"Ø{input[0]}" : $"{input[0]}x{input[1]}").Replace(".", ",");
            else return (input.Length == 1 ? $"Ø{input[0]}" : $"{Math.Max(input[0], input[1])}x{Math.Min(input[0], input[1])}").Replace(".", ",");
        }

        internal double GetSizeSquare(double[] input)
        {
            return input.Length == 1 ? Math.PI * Math.Pow(input[0] / 2.0, 2) : input[0] * input[1];
        }
    }

    class DuctInfo : MagiCadInfo
    {
        internal DuctInfo(Element element)
        {
            Root = element;
            Family = Root.Document.GetElement(Root.GetTypeId());

            Description = Family.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_COMMENTS).AsString();

            var duct = Root as Duct;
            var ductType = Family as DuctType;

            var wallThickness = 0.0;
            var size = default(double[]);
            if (ductType.Shape == ConnectorProfileType.Rectangular)
            {
                var width = Math.Round(duct.Width * Constants.FACTOR_LENGTH, 2);
                var height = Math.Round(duct.Height * Constants.FACTOR_LENGTH, 2);
                size = new[] { width, height };
                wallThickness = GetWallThickness(size);
                //периметр * толщину стенки * плотность * 1м
                Weight = Math.Round((2.0 * (width + height) / 1000.0) * (wallThickness / 1000.0) * Constants.STEEL_DENSITY * 1.0, 1);
            }
            else if (ductType.Shape == ConnectorProfileType.Round)
            {
                var diameter = Math.Round(duct.Diameter * Constants.FACTOR_LENGTH, 2);
                size = new[] { diameter };
                wallThickness = GetWallThickness(size);
                //периметр * толщину стенки * плотность * 1м
                Weight = Math.Round((Math.PI * diameter / 1000.0) * (wallThickness / 1000.0) * Constants.STEEL_DENSITY * 1.0, 1);
            }

            RootParameters.Add("[SIZE]", SizeToString(size, true));
            RootParameters.Add("[THICKNESS]", wallThickness.ToString().Replace(".", ","));
            Count = Math.Round(Root.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH).AsDouble() * Constants.FACTOR_LENGTH, 2);

            ElementType = ElementTypeEnum.DUCT;
            SetCommonProperties();
        }
    }

    class FlexDuctInfo : MagiCadInfo
    {
        internal FlexDuctInfo(Element element)
        {
            Root = element;
            Family = Root.Document.GetElement(Root.GetTypeId());
            Description = Family.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_COMMENTS).AsString();
            if (string.IsNullOrEmpty(Description)) Description = "FlexDuct";
            ElementType = ElementTypeEnum.DUCT;
            SetCommonProperties();
        }
    }

    class PipeInfo : MagiCadInfo
    {
        internal PipeInfo(Element element)
        {
            Root = element;
            Family = Root.Document.GetElement(Root.GetTypeId());
            Description = Family.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_COMMENTS).AsString();
            if (string.IsNullOrEmpty(Description)) Description = "Pipe";
            ElementType = ElementTypeEnum.PIPE;
            SetCommonProperties();
        }
    }

    class FlexPipeInfo : MagiCadInfo
    {
        internal FlexPipeInfo(Element element)
        {
            Root = element;
            Family = Root.Document.GetElement(Root.GetTypeId());
            Description = Family.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_COMMENTS).AsString();
            if (string.IsNullOrEmpty(Description)) Description = "FlexPipe";
            ElementType = ElementTypeEnum.PIPE;
            SetCommonProperties();
        }
    }

    class DuctValveInfo : MagiCadInfo
    {
        internal DuctValveInfo(Element element)
        {
            Root = element;
            Family = Root.Document.GetElement(Root.GetTypeId());
            Description = Family.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_COMMENTS).AsString();
            if (string.IsNullOrEmpty(Description)) Description = "DuctValve";
            ElementType = ElementTypeEnum.DUCT;

            var pmiWeightParam = Family.LookupParameter(Attributes.PMI_WEIGHT);
            Weight = pmiWeightParam == null ? 0.0 : pmiWeightParam.AsDouble();

            SetCommonProperties();
        }
    }

    class PipeValveInfo : MagiCadInfo
    {
        internal PipeValveInfo(Element element)
        {
            Root = element;
            Family = Root.Document.GetElement(Root.GetTypeId());
            Description = Family.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_COMMENTS).AsString();
            if (string.IsNullOrEmpty(Description)) Description = "PipeValve";
            ElementType = ElementTypeEnum.PIPE;
            SetCommonProperties();
        }
    }

    class DuctDiffusersInfo : MagiCadInfo
    {
        internal DuctDiffusersInfo(Element element)
        {
            Root = element;
            Family = Root.Document.GetElement(Root.GetTypeId());
            Description = Family.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_COMMENTS).AsString();
            if (string.IsNullOrEmpty(Description)) Description = "DuctDiffusers";
            ElementType = ElementTypeEnum.DUCT;

            var pmiWeightParam = Family.LookupParameter(Attributes.PMI_WEIGHT);
            Weight = pmiWeightParam == null ? 0.0 : pmiWeightParam.AsDouble();

            SetCommonProperties();
        }
    }

    class CommonModelInfo : MagiCadInfo
    {
        internal CommonModelInfo(Element element)
        {
            Root = element;
            Family = Root.Document.GetElement(Root.GetTypeId());
            Description = Family.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_COMMENTS).AsString();
            if (string.IsNullOrEmpty(Description)) Description = "CommonModel";
            ElementType = ElementTypeEnum.EQUI;

            var pmiWeightParam = Family.LookupParameter(Attributes.PMI_WEIGHT);
            Weight = pmiWeightParam == null ? 0.0 : pmiWeightParam.AsDouble();

            SetCommonProperties();
        }
    }

    class EquipmentInfo : MagiCadInfo
    {
        internal EquipmentInfo(Element element)
        {
            Root = element;
            Family = Root.Document.GetElement(Root.GetTypeId());
            Description = Family.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_COMMENTS).AsString();
            if (string.IsNullOrEmpty(Description)) Description = "Equipment";
            ElementType = ElementTypeEnum.EQUI;

            var pmiWeightParam = Family.LookupParameter(Attributes.PMI_WEIGHT);
            Weight = pmiWeightParam == null ? 0.0 : pmiWeightParam.AsDouble();

            SetCommonProperties();
        }
    }

    class ReserveInfo : MagiCadInfo
    {
        internal ReserveInfo(Element element)
        {
            Root = element;
            Family = Root.Document.GetElement(Root.GetTypeId());
            Description = Family.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_COMMENTS).AsString();
            if (string.IsNullOrEmpty(Description)) 
                Description = "Reserve";

            ElementType = ElementTypeEnum.REZV;

            SetCommonProperties();
        }
    }

    class SupportInfo : MagiCadInfo
    {
        internal SupportInfo(Element element)
        {
            Root = element;
            Family = Root.Document.GetElement(Root.GetTypeId());
            Description = Family.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_COMMENTS).AsString();
            if (string.IsNullOrEmpty(Description)) 
                Description = "Support";

            ElementType = ElementTypeEnum.SUPP;

            SetCommonProperties();
        }
    }

    class DuctConnectingPartInfo : MagiCadInfo
    {
        internal DuctConnectingPartInfo(Element element)
        {
            Root = element;
            Family = Root.Document.GetElement(Root.GetTypeId());

            Description = Family.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_COMMENTS).AsString();

            var partType = default(PartType);
            ConnectorManager connectorManager = null;
            if (element is FamilyInstance familyInstance && familyInstance.MEPModel is MechanicalFitting mechanicalFitting)
            {
                partType = mechanicalFitting.PartType;
                connectorManager = mechanicalFitting.ConnectorManager;
            }

            if (partType == PartType.Elbow)
            {
                if (string.IsNullOrEmpty(Description)) Description = "! Отвод-[ANGLE], δ=[THICKNESS]мм, [SIZE]";

                var sizes = new List<double[]>();
                var wallThickness = 0.0;
                foreach (Connector connector in connectorManager.Connectors)
                {
                    var size = default(double[]);
                    if (connector.Shape == ConnectorProfileType.Round)
                        size = new[] { Math.Round(connector.Radius * Constants.FACTOR_LENGTH * 2.0, 2) };
                    else if (connector.Shape == ConnectorProfileType.Rectangular)
                        size = new[] { Math.Round(connector.Width * Constants.FACTOR_LENGTH, 2), Math.Round(connector.Height * Constants.FACTOR_LENGTH, 2) };
                    sizes.Add(size);
                    wallThickness = Math.Max(wallThickness, GetWallThickness(size));
                }
                var distSizes = sizes.Distinct(new SizeComparer()).ToList();

                var strSize = string.Empty;
                foreach (var size in distSizes) strSize += $"/{SizeToString(size)}";

                RootParameters.Add("[SIZE]", strSize.Substring(1));
                RootParameters.Add("[THICKNESS]", wallThickness.ToString().Replace(".", ","));

                var angleParam = Root.LookupParameter(Attributes.ANGLE);
                var angle = angleParam == null ? 0.0 : Math.Round(angleParam.AsDouble() * Constants.FACTOR_ANGLE, 2);
                RootParameters.Add("[ANGLE]", angle.ToString().Replace(".", ","));
            }
            else if (partType == PartType.Tee)
            {
                if (string.IsNullOrEmpty(Description)) Description = "! Тройник, δ=[THICKNESS]мм, [SIZE]";

                var sizes = new List<double[]>();
                var wallThickness = 0.0;
                var sizeStr = string.Empty;
                foreach (Connector connector in connectorManager.Connectors)
                {
                    var size = default(double[]);
                    if (connector.Shape == ConnectorProfileType.Round)
                        size = new[] { Math.Round(connector.Radius * Constants.FACTOR_LENGTH * 2.0, 2) };
                    else if (connector.Shape == ConnectorProfileType.Rectangular)
                        size = new[] { Math.Round(connector.Width * Constants.FACTOR_LENGTH, 2), Math.Round(connector.Height * Constants.FACTOR_LENGTH, 2) };

                    sizes.Add(size);
                    wallThickness = Math.Max(wallThickness, GetWallThickness(size));
                }
                var distSizes = sizes.Distinct(new SizeComparer()).ToList();

                if (distSizes.Count == 1)
                {
                    sizeStr = SizeToString(distSizes[0]);
                }
                else if (distSizes.Count == 2)
                {
                    //основной потом ответвление
                    if (sizes.Count(x => x.SequenceEqual(distSizes[0])) > 1) sizeStr = $"{SizeToString(distSizes[0])}/{SizeToString(distSizes[1])}";
                    else sizeStr = $"{SizeToString(distSizes[1])}/{SizeToString(distSizes[0])}";
                }
                else
                {
                    //в порядке убывания
                    var square1 = GetSizeSquare(distSizes[0]);
                    var square2 = GetSizeSquare(distSizes[1]);
                    var square3 = GetSizeSquare(distSizes[2]);
                    if (square1 > square2 && square1 > square3)
                        sizeStr = $"{SizeToString(distSizes[0])}/{(square2 > square3 ? $"{SizeToString(distSizes[1])}/{SizeToString(distSizes[2])}" : $"{SizeToString(distSizes[2])}/{SizeToString(distSizes[1])}")}";
                    else if (square2 > square1 && square2 > square3)
                        sizeStr = $"{SizeToString(distSizes[1])}/{(square1 > square3 ? $"{SizeToString(distSizes[0])}/{SizeToString(distSizes[2])}" : $"{SizeToString(distSizes[2])}/{SizeToString(distSizes[0])}")}";
                    else if (square3 > square1 && square3 > square2)
                        sizeStr = $"{SizeToString(distSizes[2])}/{(square1 > square2 ? $"{SizeToString(distSizes[0])}/{SizeToString(distSizes[1])}" : $"{SizeToString(distSizes[1])}/{SizeToString(distSizes[0])}")}";
                }
                RootParameters.Add("[SIZE]", sizeStr);
                RootParameters.Add("[THICKNESS]", wallThickness.ToString().Replace(".", ","));
            }
            else if (partType == PartType.Cap)
            {
                if (string.IsNullOrEmpty(Description)) Description = "! Заглушка, δ=[THICKNESS]мм, [SIZE]";
                var sizes = new List<double[]>();
                var wallThickness = 0.0;

                foreach (Connector connector in connectorManager.Connectors)
                {
                    var size = default(double[]);
                    if (connector.Shape == ConnectorProfileType.Round)
                        size = new[] { Math.Round(connector.Radius * Constants.FACTOR_LENGTH * 2.0, 2) };
                    else if (connector.Shape == ConnectorProfileType.Rectangular)
                        size = new[] { Math.Round(connector.Width * Constants.FACTOR_LENGTH, 2), Math.Round(connector.Height * Constants.FACTOR_LENGTH, 2) };

                    sizes.Add(size);
                    wallThickness = Math.Max(wallThickness, GetWallThickness(size));
                }

                RootParameters.Add("[SIZE]", SizeToString(sizes[0], true));
                RootParameters.Add("[THICKNESS]", wallThickness.ToString().Replace(".", ","));
            }
            else if (partType == PartType.Cross)
            {
                if (string.IsNullOrEmpty(Description)) Description = "! Крестовина, δ=[THICKNESS]мм, [SIZE]";
                var sizes = new List<double[]>();
                var wallThickness = 0.0;
                foreach (Connector connector in connectorManager.Connectors)
                {
                    var size = default(double[]);
                    if (connector.Shape == ConnectorProfileType.Round)
                        size = new[] { Math.Round(connector.Radius * Constants.FACTOR_LENGTH * 2.0, 2) };
                    else if (connector.Shape == ConnectorProfileType.Rectangular)
                        size = new[] { Math.Round(connector.Width * Constants.FACTOR_LENGTH, 2), Math.Round(connector.Height * Constants.FACTOR_LENGTH, 2) };

                    sizes.Add(size);
                    wallThickness = Math.Max(wallThickness, GetWallThickness(size));
                }

                var distSizes = sizes.Distinct(new SizeComparer()).ToList();
                var sizeStr = string.Empty;
                if (distSizes.Count == 1)
                {
                    sizeStr = SizeToString(distSizes[0]);
                }
                else if (distSizes.Count == 2)
                {
                    //в порядке убывания
                    var square0 = GetSizeSquare(distSizes[0]);
                    var square1 = GetSizeSquare(distSizes[1]);

                    if (sizes.Count(x => x.SequenceEqual(distSizes[0])) == 2 && sizes.Count(x => x.SequenceEqual(distSizes[1])) == 2)
                        sizeStr = square0 >= square1 ? $"{SizeToString(distSizes[0])}/{SizeToString(distSizes[1])}" : $"{SizeToString(distSizes[1])}/{SizeToString(distSizes[0])}";
                    else if (sizes.Count(x => x.SequenceEqual(distSizes[0])) == 3)
                        sizeStr = $"{SizeToString(distSizes[0])}/{(square0 >= square1 ? $"{SizeToString(distSizes[0])}/{SizeToString(distSizes[1])}" : $"{SizeToString(distSizes[1])}/{SizeToString(distSizes[0])}")}";
                    else if (sizes.Count(x => x.SequenceEqual(distSizes[1])) == 3)
                        sizeStr = $"{SizeToString(distSizes[1])}/{(square0 >= square1 ? $"{SizeToString(distSizes[0])}/{SizeToString(distSizes[1])}" : $"{SizeToString(distSizes[1])}/{SizeToString(distSizes[0])}")}";
                }
                else if (distSizes.Count == 3)
                {
                    //основной и вспомогательные в порядке убывания
                    var square0 = GetSizeSquare(distSizes[0]);
                    var square1 = GetSizeSquare(distSizes[1]);
                    var square2 = GetSizeSquare(distSizes[2]);
                    if (sizes.Count(x => x.SequenceEqual(distSizes[0])) == 2)
                        sizeStr = $"{SizeToString(distSizes[0])}/{(square1 >= square2 ? $"{SizeToString(distSizes[1])}/{SizeToString(distSizes[2])}" : $"{SizeToString(distSizes[2])}/{SizeToString(distSizes[1])}")}";
                    else if (sizes.Count(x => x.SequenceEqual(distSizes[1])) == 2)
                        sizeStr = $"{SizeToString(distSizes[1])}/{(square0 >= square2 ? $"{SizeToString(distSizes[0])}/{SizeToString(distSizes[2])}" : $"{SizeToString(distSizes[2])}/{SizeToString(distSizes[0])}")}";
                    else if (sizes.Count(x => x.SequenceEqual(distSizes[2])) == 2)
                        sizeStr = $"{SizeToString(distSizes[2])}/{(square0 >= square1 ? $"{SizeToString(distSizes[0])}/{SizeToString(distSizes[1])}" : $"{SizeToString(distSizes[1])}/{SizeToString(distSizes[0])}")}";
                }
                else
                {
                    //в порядке убывания
                    var sizeDict = new SortedDictionary<double, List<double[]>>();
                    for (var i = 0; i < distSizes.Count; i++)
                    {
                        var square = GetSizeSquare(distSizes[i]);
                        if (!sizeDict.ContainsKey(square)) sizeDict.Add(square, new List<double[]>());
                        sizeDict[square].Add(distSizes[i]);
                    }
                    var keys = sizeDict.Keys.ToList();
                    keys.Reverse();
                    foreach (var key in keys)
                        foreach (var size in sizeDict[key])
                            sizeStr += $"/{SizeToString(size)}";
                    sizeStr = string.IsNullOrEmpty(sizeStr) ? string.Empty : sizeStr.Substring(1);
                }

                RootParameters.Add("[SIZE]", sizeStr);
                RootParameters.Add("[THICKNESS]", wallThickness.ToString().Replace(".", ","));
            }
            else if (partType == PartType.TapAdjustable || partType == PartType.TapPerpendicular)
            {
                if (string.IsNullOrEmpty(Description)) Description = "! Врезка, δ=[THICKNESS]мм, [SIZE]";

                var sizes = new List<double[]>();
                var wallThickness = 0.0;
                var connectedToSizeSrt = string.Empty;
                foreach (Connector connector in connectorManager.Connectors)
                {
                    var size = default(double[]);
                    if (connector.Shape == ConnectorProfileType.Round)
                        size = new[] { Math.Round(connector.Radius * Constants.FACTOR_LENGTH * 2.0, 2) };
                    else if (connector.Shape == ConnectorProfileType.Rectangular)
                        size = new[] { Math.Round(connector.Width * Constants.FACTOR_LENGTH, 2), Math.Round(connector.Height * Constants.FACTOR_LENGTH, 2) };

                    sizes.Add(size);
                    wallThickness = Math.Max(wallThickness, GetWallThickness(size));

                    //выводится дополнительный размер, если врезаемся в круглый воздуховод
                    if (connector.MEPSystem != null && connector.IsConnected)
                    {
                        foreach (Connector connected in connector.AllRefs)
                        {
                            if (connected.ConnectorType != ConnectorType.End &&
                                connected.ConnectorType != ConnectorType.Curve &&
                                connected.ConnectorType != ConnectorType.Physical) continue;

                            var connectedSize = default(double[]);
                            if (connected.Shape == ConnectorProfileType.Round)
                                connectedSize = new[] { Math.Round(connected.Radius * Constants.FACTOR_LENGTH * 2.0, 2) };
                            else if (connected.Shape == ConnectorProfileType.Rectangular)
                                connectedSize = new[] { Math.Round(connected.Width * Constants.FACTOR_LENGTH, 2), Math.Round(connected.Height * Constants.FACTOR_LENGTH, 2) };

                            wallThickness = Math.Max(wallThickness, GetWallThickness(connectedSize));
                            if (connected.Shape != ConnectorProfileType.Round) continue;
                            if (string.IsNullOrEmpty(connectedToSizeSrt)) connectedToSizeSrt = SizeToString(connectedSize);
                            else if (!size.SequenceEqual(connectedSize)) connectedToSizeSrt = SizeToString(connectedSize);
                        }
                    }
                }

                RootParameters.Add("[SIZE]", $"{SizeToString(sizes[0])}{(string.IsNullOrEmpty(connectedToSizeSrt) ? string.Empty : $"/{connectedToSizeSrt}")}");
                RootParameters.Add("[THICKNESS]", wallThickness.ToString().Replace(".", ","));
            }
            else if (partType == PartType.Transition)
            {
                if (string.IsNullOrEmpty(Description)) Description = "! Переход, δ=[THICKNESS]мм, [SIZE], L=[LENGTH]мм";
                var sizes = new List<double[]>();
                var wallThickness = 0.0;
                foreach (Connector connector in connectorManager.Connectors)
                {
                    var size = default(double[]);
                    if (connector.Shape == ConnectorProfileType.Round)
                        size = new[] { Math.Round(connector.Radius * Constants.FACTOR_LENGTH * 2.0, 2) };
                    else if (connector.Shape == ConnectorProfileType.Rectangular)
                        size = new[] { Math.Round(connector.Width * Constants.FACTOR_LENGTH, 2), Math.Round(connector.Height * Constants.FACTOR_LENGTH, 2) };

                    sizes.Add(size);
                    wallThickness = Math.Max(wallThickness, GetWallThickness(size));
                }

                var square1 = GetSizeSquare(sizes[0]);
                var square2 = GetSizeSquare(sizes[1]);

                var firstSize = square1 > square2 ? sizes[0] : sizes[1];
                var secondSize = square1 > square2 ? sizes[1] : sizes[0];

                var offsetWidth = Math.Round(Root.get_Parameter(BuiltInParameter.RBS_FAMILY_CONTENT_OFFSET_WIDTH).AsDouble() * Constants.FACTOR_LENGTH, 2);
                var offsetHeight = Math.Round(Root.get_Parameter(BuiltInParameter.RBS_FAMILY_CONTENT_OFFSET_HEIGHT).AsDouble() * Constants.FACTOR_LENGTH, 2);

                if (((firstSize.Length == 1 && (offsetWidth * 2.0 != firstSize[0] || offsetHeight * 2.0 != firstSize[0])) ||
                    (firstSize.Length > 1 && (offsetWidth * 2.0 != firstSize[0] || offsetHeight * 2.0 != firstSize[1]))) &&
                    ((secondSize.Length == 1 && (offsetWidth * 2.0 != secondSize[0] || offsetHeight * 2.0 != secondSize[0])) ||
                    (secondSize.Length > 1 && (offsetWidth * 2.0 != secondSize[0] || offsetHeight * 2.0 != secondSize[1]))))
                {
                    Description += ", смещенный";
                }

                if (firstSize.Length > 1)
                {
                    if (firstSize[0] < firstSize[1])
                    {
                        Array.Reverse(firstSize);
                        if (secondSize.Length > 1) Array.Reverse(secondSize);
                    }
                    else if (firstSize[0] == firstSize[1] && secondSize.Length > 1 && secondSize[0] < secondSize[1])
                    {
                        Array.Reverse(secondSize);
                    }
                }
                else if (secondSize.Length > 1 && secondSize[0] < secondSize[1])
                {
                    Array.Reverse(secondSize);
                }

                RootParameters.Add("[SIZE]", $"{SizeToString(firstSize)}/{SizeToString(secondSize)}");
                RootParameters.Add("[THICKNESS]", wallThickness.ToString().Replace(".", ","));

                var lengthParam = Root.LookupParameter(Attributes.L);
                var length = lengthParam == null ? 0.0 : Math.Round(lengthParam.AsDouble() * Constants.FACTOR_LENGTH, 2);
                RootParameters.Add("[LENGTH]", length.ToString().Replace(".", ","));
            }
            else
            {
                Description = partType == PartType.Union ? "Соединение" : partType.ToString();
                Name = string.Empty;
                Sign = string.Empty;
                Plant = string.Empty;
                Weight = 0.0;
                Comment = string.Empty;
            }

            ElementType = ElementTypeEnum.DUCT;
            SetCommonProperties();
        }
    }

    class PipeConnectingPartInfo : MagiCadInfo
    {
        internal PipeConnectingPartInfo(Element element)
        {
            Root = element;
            Family = Root.Document.GetElement(Root.GetTypeId());
            Description = Family.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_COMMENTS).AsString();
            if (string.IsNullOrEmpty(Description)) Description = "PipeConnecting";
            ElementType = ElementTypeEnum.PIPE;
            SetCommonProperties();
        }
    }
}
