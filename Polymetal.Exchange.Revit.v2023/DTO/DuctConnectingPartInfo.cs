using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Polymetal.Exchange.Revit.v2023.Helpers;

namespace Polymetal.Exchange.Revit.v2023.DTO
{
    class DuctConnectingPartInfo : MagiCadInfo
    {
        internal DuctConnectingPartInfo(Element element)
        {
            Root = element;
            Family = Root.Document.GetElement(Root.GetTypeId());
            ElementType = Utilities.Helpers.Enumerators.ElementType.DUCT;

            Description = Family.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_COMMENTS).AsString();

            var partType = default(PartType);

            ConnectorManager connectorManager = null;
            if (element is FamilyInstance familyInstance &&
                familyInstance.MEPModel is MechanicalFitting mechanicalFitting)
            {
                partType = mechanicalFitting.PartType;
                connectorManager = mechanicalFitting.ConnectorManager;
            }

            if (partType == PartType.Elbow)
            {
                if (string.IsNullOrEmpty(Description))
                    Description = "! Отвод-[ANGLE], δ=[THICKNESS]мм, [SIZE]";

                var sizes = new List<double[]>();
                var wallThickness = 0.0;
                foreach (Connector connector in connectorManager.Connectors)
                {
                    var size = default(double[]);
                    if (connector.Shape == ConnectorProfileType.Round)
                    {
                        size = new[] { Math.Round(connector.Radius * Constants.FACTOR_LENGTH * 2.0, 2) };
                    }
                    else if (connector.Shape == ConnectorProfileType.Rectangular)
                    {
                        size = new[]
                        {
                            Math.Round(connector.Width * Constants.FACTOR_LENGTH, 2),
                            Math.Round(connector.Height * Constants.FACTOR_LENGTH, 2)
                        };
                    }

                    sizes.Add(size);
                    wallThickness = Math.Max(wallThickness, GetWallThickness(size));
                }

                var distSizes = sizes.Distinct(new SizeComparer()).ToList();

                var strSize = string.Empty;
                foreach (var size in distSizes)
                {
                    strSize += $"/{SizeToString(size)}";
                }

                RootParameters.Add("[SIZE]", strSize.Substring(1));
                RootParameters.Add("[THICKNESS]", wallThickness.ToString().Replace(".", ","));

                var angleParam = Root.LookupParameter(Attributes.ANGLE);
                var angle = angleParam == null ? 0.0 : Math.Round(angleParam.AsDouble() * Constants.FACTOR_ANGLE, 2);
                RootParameters.Add("[ANGLE]", angle.ToString().Replace(".", ","));
            }
            else if (partType == PartType.Tee)
            {
                if (string.IsNullOrEmpty(Description))
                    Description = "! Тройник, δ=[THICKNESS]мм, [SIZE]";

                var sizes = new List<double[]>();
                var wallThickness = 0.0;
                var sizeStr = string.Empty;
                foreach (Connector connector in connectorManager.Connectors)
                {
                    var size = default(double[]);
                    if (connector.Shape == ConnectorProfileType.Round)
                    {
                        size = new[] { Math.Round(connector.Radius * Constants.FACTOR_LENGTH * 2.0, 2) };
                    }
                    else if (connector.Shape == ConnectorProfileType.Rectangular)
                    {
                        size = new[]
                        {
                            Math.Round(connector.Width * Constants.FACTOR_LENGTH, 2),
                            Math.Round(connector.Height * Constants.FACTOR_LENGTH, 2)
                        };
                    }

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
                    if (sizes.Count(x => x.SequenceEqual(distSizes[0])) > 1)
                    {
                        sizeStr = $"{SizeToString(distSizes[0])}/{SizeToString(distSizes[1])}";
                    }
                    else
                    {
                        sizeStr = $"{SizeToString(distSizes[1])}/{SizeToString(distSizes[0])}";
                    }
                }
                else
                {
                    //в порядке убывания
                    var square1 = GetSizeSquare(distSizes[0]);
                    var square2 = GetSizeSquare(distSizes[1]);
                    var square3 = GetSizeSquare(distSizes[2]);
                    if (square1 > square2 && square1 > square3)
                    {
                        sizeStr =
                            $"{SizeToString(distSizes[0])}/{(square2 > square3 ? $"{SizeToString(distSizes[1])}/{SizeToString(distSizes[2])}" : $"{SizeToString(distSizes[2])}/{SizeToString(distSizes[1])}")}";
                    }
                    else if (square2 > square1 && square2 > square3)
                    {
                        sizeStr =
                            $"{SizeToString(distSizes[1])}/{(square1 > square3 ? $"{SizeToString(distSizes[0])}/{SizeToString(distSizes[2])}" : $"{SizeToString(distSizes[2])}/{SizeToString(distSizes[0])}")}";
                    }
                    else if (square3 > square1 && square3 > square2)
                    {
                        sizeStr =
                            $"{SizeToString(distSizes[2])}/{(square1 > square2 ? $"{SizeToString(distSizes[0])}/{SizeToString(distSizes[1])}" : $"{SizeToString(distSizes[1])}/{SizeToString(distSizes[0])}")}";
                    }
                }

                RootParameters.Add("[SIZE]", sizeStr);
                RootParameters.Add("[THICKNESS]", wallThickness.ToString().Replace(".", ","));
            }
            else if (partType == PartType.Cap)
            {
                if (string.IsNullOrEmpty(Description))
                    Description = "! Заглушка, δ=[THICKNESS]мм, [SIZE]";

                var sizes = new List<double[]>();
                var wallThickness = 0.0;

                foreach (Connector connector in connectorManager.Connectors)
                {
                    var size = default(double[]);
                    if (connector.Shape == ConnectorProfileType.Round)
                    {
                        size = new[] { Math.Round(connector.Radius * Constants.FACTOR_LENGTH * 2.0, 2) };
                    }
                    else if (connector.Shape == ConnectorProfileType.Rectangular)
                    {
                        size = new[]
                        {
                            Math.Round(connector.Width * Constants.FACTOR_LENGTH, 2),
                            Math.Round(connector.Height * Constants.FACTOR_LENGTH, 2)
                        };
                    }

                    sizes.Add(size);
                    wallThickness = Math.Max(wallThickness, GetWallThickness(size));
                }

                RootParameters.Add("[SIZE]", SizeToString(sizes[0], true));
                RootParameters.Add("[THICKNESS]", wallThickness.ToString().Replace(".", ","));
            }
            else if (partType == PartType.Cross)
            {
                if (string.IsNullOrEmpty(Description))
                    Description = "! Крестовина, δ=[THICKNESS]мм, [SIZE]";

                var sizes = new List<double[]>();
                var wallThickness = 0.0;
                foreach (Connector connector in connectorManager.Connectors)
                {
                    var size = default(double[]);
                    if (connector.Shape == ConnectorProfileType.Round)
                    {
                        size = new[] { Math.Round(connector.Radius * Constants.FACTOR_LENGTH * 2.0, 2) };
                    }
                    else if (connector.Shape == ConnectorProfileType.Rectangular)
                    {
                        size = new[]
                        {
                            Math.Round(connector.Width * Constants.FACTOR_LENGTH, 2),
                            Math.Round(connector.Height * Constants.FACTOR_LENGTH, 2)
                        };
                    }

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

                    if (sizes.Count(x => x.SequenceEqual(distSizes[0])) == 2 &&
                        sizes.Count(x => x.SequenceEqual(distSizes[1])) == 2)
                    {
                        sizeStr = square0 >= square1
                            ? $"{SizeToString(distSizes[0])}/{SizeToString(distSizes[1])}"
                            : $"{SizeToString(distSizes[1])}/{SizeToString(distSizes[0])}";
                    }
                    else if (sizes.Count(x => x.SequenceEqual(distSizes[0])) == 3)
                    {
                        sizeStr =
                            $"{SizeToString(distSizes[0])}/{(square0 >= square1 ? $"{SizeToString(distSizes[0])}/{SizeToString(distSizes[1])}" : $"{SizeToString(distSizes[1])}/{SizeToString(distSizes[0])}")}";
                    }
                    else if (sizes.Count(x => x.SequenceEqual(distSizes[1])) == 3)
                    {
                        sizeStr =
                            $"{SizeToString(distSizes[1])}/{(square0 >= square1 ? $"{SizeToString(distSizes[0])}/{SizeToString(distSizes[1])}" : $"{SizeToString(distSizes[1])}/{SizeToString(distSizes[0])}")}";
                    }
                }
                else if (distSizes.Count == 3)
                {
                    //основной и вспомогательные в порядке убывания
                    var square0 = GetSizeSquare(distSizes[0]);
                    var square1 = GetSizeSquare(distSizes[1]);
                    var square2 = GetSizeSquare(distSizes[2]);
                    if (sizes.Count(x => x.SequenceEqual(distSizes[0])) == 2)
                    {
                        sizeStr =
                            $"{SizeToString(distSizes[0])}/{(square1 >= square2 ? $"{SizeToString(distSizes[1])}/{SizeToString(distSizes[2])}" : $"{SizeToString(distSizes[2])}/{SizeToString(distSizes[1])}")}";
                    }
                    else if (sizes.Count(x => x.SequenceEqual(distSizes[1])) == 2)
                    {
                        sizeStr =
                            $"{SizeToString(distSizes[1])}/{(square0 >= square2 ? $"{SizeToString(distSizes[0])}/{SizeToString(distSizes[2])}" : $"{SizeToString(distSizes[2])}/{SizeToString(distSizes[0])}")}";
                    }
                    else if (sizes.Count(x => x.SequenceEqual(distSizes[2])) == 2)
                    {
                        sizeStr =
                            $"{SizeToString(distSizes[2])}/{(square0 >= square1 ? $"{SizeToString(distSizes[0])}/{SizeToString(distSizes[1])}" : $"{SizeToString(distSizes[1])}/{SizeToString(distSizes[0])}")}";
                    }
                }
                else
                {
                    //в порядке убывания
                    var sizeDict = new SortedDictionary<double, List<double[]>>();
                    for (var i = 0; i < distSizes.Count; i++)
                    {
                        var square = GetSizeSquare(distSizes[i]);
                        if (!sizeDict.ContainsKey(square))
                            sizeDict.Add(square, new List<double[]>());

                        sizeDict[square].Add(distSizes[i]);
                    }

                    var keys = sizeDict.Keys.ToList();
                    keys.Reverse();
                    foreach (var key in keys)
                    {
                        foreach (var size in sizeDict[key])
                        {
                            sizeStr += $"/{SizeToString(size)}";
                        }
                    }

                    sizeStr = string.IsNullOrEmpty(sizeStr) ? string.Empty : sizeStr.Substring(1);
                }

                RootParameters.Add("[SIZE]", sizeStr);
                RootParameters.Add("[THICKNESS]", wallThickness.ToString().Replace(".", ","));
            }
            else if (partType == PartType.TapAdjustable || partType == PartType.TapPerpendicular)
            {
                if (string.IsNullOrEmpty(Description))
                    Description = "! Врезка, δ=[THICKNESS]мм, [SIZE]";

                var sizes = new List<double[]>();
                var wallThickness = 0.0;
                var connectedToSizeSrt = string.Empty;
                foreach (Connector connector in connectorManager.Connectors)
                {
                    var size = default(double[]);
                    if (connector.Shape == ConnectorProfileType.Round)
                    {
                        size = new[] { Math.Round(connector.Radius * Constants.FACTOR_LENGTH * 2.0, 2) };
                    }
                    else if (connector.Shape == ConnectorProfileType.Rectangular)
                    {
                        size = new[]
                        {
                            Math.Round(connector.Width * Constants.FACTOR_LENGTH, 2),
                            Math.Round(connector.Height * Constants.FACTOR_LENGTH, 2)
                        };
                    }

                    sizes.Add(size);
                    wallThickness = Math.Max(wallThickness, GetWallThickness(size));

                    //выводится дополнительный размер, если врезаемся в круглый воздуховод
                    if (connector.MEPSystem != null && connector.IsConnected)
                    {
                        foreach (Connector connected in connector.AllRefs)
                        {
                            if (connected.ConnectorType != ConnectorType.End &&
                                connected.ConnectorType != ConnectorType.Curve &&
                                connected.ConnectorType != ConnectorType.Physical)
                                continue;

                            var connectedSize = default(double[]);
                            if (connected.Shape == ConnectorProfileType.Round)
                            {
                                connectedSize = new[]
                                    { Math.Round(connected.Radius * Constants.FACTOR_LENGTH * 2.0, 2) };
                            }
                            else if (connected.Shape == ConnectorProfileType.Rectangular)
                            {
                                connectedSize = new[]
                                {
                                    Math.Round(connected.Width * Constants.FACTOR_LENGTH, 2),
                                    Math.Round(connected.Height * Constants.FACTOR_LENGTH, 2)
                                };
                            }

                            wallThickness = Math.Max(wallThickness, GetWallThickness(connectedSize));
                            if (connected.Shape != ConnectorProfileType.Round)
                                continue;

                            if (string.IsNullOrEmpty(connectedToSizeSrt))
                            {
                                connectedToSizeSrt = SizeToString(connectedSize);
                            }
                            else if (!size.SequenceEqual(connectedSize))
                            {
                                connectedToSizeSrt = SizeToString(connectedSize);
                            }
                        }
                    }
                }

                RootParameters.Add("[SIZE]",
                    $"{SizeToString(sizes[0])}{(string.IsNullOrEmpty(connectedToSizeSrt) ? string.Empty : $"/{connectedToSizeSrt}")}");
                RootParameters.Add("[THICKNESS]", wallThickness.ToString().Replace(".", ","));
            }
            else if (partType == PartType.Transition)
            {
                if (string.IsNullOrEmpty(Description))
                    Description = "! Переход, δ=[THICKNESS]мм, [SIZE], L=[LENGTH]мм";

                var sizes = new List<double[]>();
                var wallThickness = 0.0;
                foreach (Connector connector in connectorManager.Connectors)
                {
                    var size = default(double[]);
                    if (connector.Shape == ConnectorProfileType.Round)
                    {
                        size = new[] { Math.Round(connector.Radius * Constants.FACTOR_LENGTH * 2.0, 2) };
                    }
                    else if (connector.Shape == ConnectorProfileType.Rectangular)
                    {
                        size = new[]
                        {
                            Math.Round(connector.Width * Constants.FACTOR_LENGTH, 2),
                            Math.Round(connector.Height * Constants.FACTOR_LENGTH, 2)
                        };
                    }

                    sizes.Add(size);
                    wallThickness = Math.Max(wallThickness, GetWallThickness(size));
                }

                var square1 = GetSizeSquare(sizes[0]);
                var square2 = GetSizeSquare(sizes[1]);

                var firstSize = square1 > square2 ? sizes[0] : sizes[1];
                var secondSize = square1 > square2 ? sizes[1] : sizes[0];

                var offsetWidth =
                    Math.Round(
                        Root.get_Parameter(BuiltInParameter.RBS_FAMILY_CONTENT_OFFSET_WIDTH).AsDouble() *
                        Constants.FACTOR_LENGTH, 2);
                var offsetHeight =
                    Math.Round(
                        Root.get_Parameter(BuiltInParameter.RBS_FAMILY_CONTENT_OFFSET_HEIGHT).AsDouble() *
                        Constants.FACTOR_LENGTH, 2);

                if (((firstSize.Length == 1 &&
                      (offsetWidth * 2.0 != firstSize[0] || offsetHeight * 2.0 != firstSize[0])) ||
                     (firstSize.Length > 1 &&
                      (offsetWidth * 2.0 != firstSize[0] || offsetHeight * 2.0 != firstSize[1]))) &&
                    ((secondSize.Length == 1 &&
                      (offsetWidth * 2.0 != secondSize[0] || offsetHeight * 2.0 != secondSize[0])) ||
                     (secondSize.Length > 1 &&
                      (offsetWidth * 2.0 != secondSize[0] || offsetHeight * 2.0 != secondSize[1]))))
                {
                    Description += ", смещенный";
                }

                if (firstSize.Length > 1)
                {
                    if (firstSize[0] < firstSize[1])
                    {
                        Array.Reverse(firstSize);
                        if (secondSize.Length > 1)
                            Array.Reverse(secondSize);
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
                var length = lengthParam == null
                    ? 0.0
                    : Math.Round(lengthParam.AsDouble() * Constants.FACTOR_LENGTH, 2);
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

            SetCommonProperties();
        }
    }
}
