using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Polymetal.Exchange.Revit.v2024.Helpers;

namespace Polymetal.Exchange.Revit.v2024.DTO
{
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
        internal Utilities.Helpers.Enumerators.ElementType ElementType;

        internal List<string> AttachedElementList;

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

        /// <summary>
        /// Инициализация общих свойств для всех элементов
        /// </summary>
        internal void SetCommonProperties()
        {
            Id = Root.Id.IntegerValue;
            UniqueId = Root.UniqueId;
            Name = string.Empty;
            var mcObcjetVariable2Param = Root.LookupParameter(Attributes.MC_OBJECT_VARIABLE_2);
            if (mcObcjetVariable2Param != null && mcObcjetVariable2Param.HasValue)
            {
                Name = mcObcjetVariable2Param.AsString().Replace(" ", string.Empty);
            }

            var mcNationalCodeParam = Family.LookupParameter(Attributes.MC_NATIONAL_CODE);
            if (mcNationalCodeParam != null && mcNationalCodeParam.HasValue)
            {
                Sign = mcNationalCodeParam.AsString();
            }

            Plant = Family.get_Parameter(BuiltInParameter.ALL_MODEL_MANUFACTURER).AsString();

            if (Description == "Соединение")
            {
                Sign = Plant = string.Empty;
            }

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
            Description = ReplaceParameters(Description);

            Sign = string.IsNullOrEmpty(Sign) ? string.Empty : Sign;
            Sign = ReplaceParameters(Sign);

            Plant = string.IsNullOrEmpty(Plant) ? string.Empty : Plant;

            System = GetSystemName();

            //var tooltipInfo = WorksharingUtils.GetWorksharingTooltipInfo(Root.Document, Root.Id);
            //var createdBy = tooltipInfo.Creator.ToLower();
            //var modifiedBy = tooltipInfo.LastChangedBy.ToLower();

            var ignoreParam = Root.LookupParameter(Attributes.NEED_IN_AVEVA);
            Ignore = ignoreParam != null && ignoreParam.HasValue && ignoreParam.AsInteger() == 0;

            AttachedElementList = GetAttachedElements(Root);
        }

        /// <summary>
        /// Получение списка ID присоединенных элементов       
        /// </summary>
        /// <param name="element"></param>
        /// <returns></returns>
        internal List<string> GetAttachedElements(Element element)
        {
            //не удалять пространство имен

            ConnectorManager connectorManager = null;
            if (element is FamilyInstance familyInstance)
            {
                connectorManager = familyInstance.MEPModel.ConnectorManager;
            }
            else if (element is MEPCurve mepCurve)
            {
                connectorManager = mepCurve.ConnectorManager;
            }

            if (connectorManager == null)
                return new List<string>();

            var result = new List<string>();
            foreach (Connector connector in connectorManager.Connectors)
            {
                if (connector.MEPSystem == null || !connector.IsConnected)
                    continue;

                foreach (Connector connected in connector.AllRefs)
                {
                    if (connected.ConnectorType == ConnectorType.Logical)
                        continue;

                    var connectedUniqueId = connected.Owner.UniqueId;
                    if (connectedUniqueId == connectorManager.Owner.UniqueId || result.Contains(connectedUniqueId))
                        continue;

                    result.Add(connectedUniqueId);
                }
            }

            return result;
        }

        /// <summary>
        /// Получение имени системы
        /// </summary>
        /// <returns></returns>
        private string GetSystemName()
        {
            var systemNameParam = Root.get_Parameter(BuiltInParameter.RBS_SYSTEM_NAME_PARAM);
            if (systemNameParam == null || !systemNameParam.HasValue) 
                return string.Empty;

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

        /// <summary>
        /// Получение толщины стенки воздуховода
        /// </summary>
        /// <param name="sizes"></param>
        /// <returns></returns>
        internal double GetWallThickness(double[] sizes)
        {
            var wallThickness = 0.0;
            var isFireProtected = IsFireProtected();

            if (sizes.Length == 1)
            {
                var size = sizes[0];
                if (size <= 200.0)
                {
                    wallThickness = isFireProtected ? 0.8 : 0.5;
                }
                else if (size <= 450.0)
                {
                    wallThickness = isFireProtected ? 0.8 : 0.6;
                }
                else if (size <= 800.0)
                {
                    wallThickness = isFireProtected ? 0.8 : 0.7;
                }
                else if (size <= 1250.0)
                {
                    wallThickness = 1.0;
                }
                else if (size <= 1600.0)
                {
                    wallThickness = 1.2;
                }
                else if (size <= 2000.0)
                {
                    wallThickness = 1.4;
                }
            }
            else if (sizes.Length == 2)
            {
                var size = Math.Max(sizes[0], sizes[1]);
                if (size <= 250.0)
                {
                    wallThickness = isFireProtected ? 0.8 : 0.5;
                }
                else if (size <= 1000.0)
                {
                    wallThickness = isFireProtected ? 0.8 : 0.7;
                }
                else if (size <= 2000.0)
                {
                    wallThickness = 0.9;
                }
            }

            var wallThicknessParam = Root.LookupParameter(Attributes.PMI_WALL_THICKNESS);
            var wallThicknessValue = wallThicknessParam == null
                ? 0.0
                : Math.Round(wallThicknessParam.AsDouble() * Constants.FACTOR_LENGTH, 2);

            return Math.Max(wallThickness, wallThicknessValue);
        }

        /// <summary>
        /// Проверка установки огнезащиты
        /// </summary>
        /// <returns></returns>
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
                    if (result) 
                        return result;
                }
            }
            catch { }

            return result;
        }

        /// <summary>
        /// Подстановка значений параметров
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        internal string ReplaceParameters(string input)
        {
            if (!RootParameters.ContainsKey("[TYPENAME]") && Family != null)
                RootParameters.Add("[TYPENAME]", Family.Name);

            var dict = new Dictionary<string, string>();
            var matches = Regex.Matches(input, "\\[\\w*\\]");
            foreach (var match in matches)
            {
                var key = match.ToString();
                if (dict.ContainsKey(key))
                    continue;

                string value;
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

            foreach (var pair in dict)
            {
                input = input.Replace(pair.Key, pair.Value);
            }

            return input;
        }

        /// <summary>
        /// Преобразование размера в текстовую запись
        /// </summary>
        /// <param name="input"></param>
        /// <param name="sort"></param>
        /// <returns></returns>
        internal string SizeToString(double[] input, bool sort = false)
        {
            string result;
            if (input.Length <= 0)
                return string.Empty;

            if (input.Length == 1)
            {
                result = $"Ø{input[0]}";
            }
            else
            {
                result = sort
                    ? $"{Math.Max(input[0], input[1])}x{Math.Min(input[0], input[1])}"
                    : $"{input[0]}x{input[1]}";
            }

            return result.Replace(".", ",");
        }

        /// <summary>
        /// Получение площади сечения
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        internal double GetSizeSquare(double[] input)
        {
            return input.Length == 1 ? Math.PI * Math.Pow(input[0] / 2.0, 2) : input[0] * input[1];
        }
    }
}
