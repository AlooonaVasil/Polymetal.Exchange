using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB.Electrical;

namespace Polymetal.Exchange.Revit.v2023.DTO
{
    internal class ConnectorData : Utilities.DTO.Connector
    {
        private static Dictionary<ElementId, List<ConnectorData>> _familyConnectorsDictionary;

        internal string Id { get; set; }

        internal Domain Domain { get; set; }

        /// <summary>
        /// Описание соединителя
        /// </summary>
        internal ParameterData DescriptionParameter { get; set; }

        /// <summary>
        /// Полная установленная мощность В*А
        /// </summary>
        internal double ApparentLoad { get; set; }

        internal ParameterData[] ApparentLoadParameterArray { get; set; }

        /// <summary>
        /// Коэффициент мощности
        /// </summary>
        internal double PowerFactor { get; set; }

        internal ParameterData PowerFactorParameter { get; set; }

        /// <summary>
        /// Напряжение
        /// </summary>
        internal double Voltage { get; set; }

        internal ParameterData VoltageParameter { get; set; }

        /// <summary>
        /// Количество полюсов
        /// </summary>
        internal int NumberOfRoles { get; set; }

        internal ParameterData NumberOfRolesParameter { get; set; }

        internal void UpdateParameterValues(Element element)
        {
            Description = string.Empty;
            NumberOfRoles = 0;
            ApparentLoad = PowerFactor = Voltage = 0.0;

            if (DescriptionParameter != null && DescriptionParameter.IsValid)
            {
                if (DescriptionParameter.IsAssociated)
                {
                    var parameterValue = GetParameterValue(element, DescriptionParameter);
                    Description = parameterValue == null ? string.Empty : (string)parameterValue;
                }
                else
                {
                    Description = (string)DescriptionParameter.Value;
                }
            }

            if (NumberOfRolesParameter != null)
            {
                if (NumberOfRolesParameter.IsAssociated)
                {
                    var parameterValue = GetParameterValue(element, NumberOfRolesParameter);
                    NumberOfRoles = parameterValue == null ? 0 : (int)parameterValue;
                }
                else
                {
                    NumberOfRoles = (int)NumberOfRolesParameter.Value;
                }
            }

            if (ApparentLoadParameterArray != null)
            {
                for (var i = 0; i < Math.Min(ApparentLoadParameterArray.Length, NumberOfRoles); i++)
                {
                    var apparentLoadParameter = ApparentLoadParameterArray[i];
                    if (apparentLoadParameter.IsAssociated)
                    {
                        var parameterValue = GetParameterValue(element, apparentLoadParameter);
                        ApparentLoad += parameterValue == null ? 0.0 : (double)parameterValue;
                    }
                    else
                    {
                        ApparentLoad += (double)apparentLoadParameter.Value;
                    }
                }
            }

            if (PowerFactorParameter != null)
            {
                if (PowerFactorParameter.IsAssociated)
                {
                    var parameterValue = GetParameterValue(element, PowerFactorParameter);
                    PowerFactor = parameterValue == null ? 0.0 : (double)parameterValue;
                }
                else
                {
                    PowerFactor = (double)PowerFactorParameter.Value;
                }
            }
            PowerFactor = PowerFactor <= 0.0 ? 1.0 : PowerFactor;

            if (VoltageParameter != null)
            {
                if (VoltageParameter.IsAssociated)
                {
                    var parameterValue = GetParameterValue(element, VoltageParameter);
                    Voltage = parameterValue == null ? 0.0 : (double)parameterValue;
                }
                else
                {
                    Voltage = (double)VoltageParameter.Value;
                }
            }

            Description = $"N={Math.Round(ApparentLoad * PowerFactor / 1000, 3)} кВт";
        }

        private object GetParameterValue(Element element, ParameterData parameter)
        {
            if (element == null || string.IsNullOrEmpty(parameter.Name))
                return null;

            var rvtParameter = element.LookupParameter(parameter.Name);
            if (rvtParameter == null)
            {
                var typeId = element.GetTypeId();
                if (typeId != ElementId.InvalidElementId)
                {
                    var type = element.Document.GetElement(typeId);
                    if (type != null)
                    {
                        rvtParameter = type.LookupParameter(parameter.Name);
                    }
                }
            }

            if (rvtParameter == null || !rvtParameter.HasValue)
                return null;

            switch (rvtParameter.StorageType)
            {
                case StorageType.Double:
                    return UnitUtils.ConvertFromInternalUnits(rvtParameter.AsDouble(), rvtParameter.GetUnitTypeId());
                case StorageType.Integer:
                    return rvtParameter.AsInteger();
                case StorageType.String:
                    return rvtParameter.AsString();
                case StorageType.ElementId:
                    return rvtParameter.AsElementId();
                default:
                    return rvtParameter.AsValueString();
            }
        }

        private static List<Connector> GetAllConnectors(Element element)
        {
            ConnectorManager connectorManager = null;
            switch (element)
            {
                case FamilyInstance familyInstance when familyInstance.MEPModel != null:
                    connectorManager = familyInstance.MEPModel.ConnectorManager;
                    break;
                case Wire wire:
                    connectorManager = wire.ConnectorManager;
                    break;
                case MEPCurve mepCurve:
                    connectorManager = mepCurve.ConnectorManager;
                    break;
            }

            if (connectorManager?.Connectors == null)
                return new List<Connector>();

            return connectorManager.Connectors.Cast<Connector>().ToList();
        }

        private static List<ConnectorData> GetConnectorsDataFromFamily(Document document, FamilySymbol familySymbol)
        {
            if (_familyConnectorsDictionary == null)
                _familyConnectorsDictionary = new Dictionary<ElementId, List<ConnectorData>>();

            if (_familyConnectorsDictionary.ContainsKey(familySymbol.Family.Id))
                return _familyConnectorsDictionary[familySymbol.Family.Id];

            var result = new List<ConnectorData>();
            using (var familyDocument = document.EditFamily(familySymbol.Family))
            {
                var connectorElementList = new FilteredElementCollector(familyDocument).OfCategory(BuiltInCategory.OST_ConnectorElem).ToElements().Cast<ConnectorElement>().ToList();

                foreach (var connectorElement in connectorElementList)
                {
                    if (connectorElement.Domain != Domain.DomainElectrical)
                        continue;

                    var connectorData = new ConnectorData
                    {
                        Domain = connectorElement.Domain
                    };

                    //Полная установленная мощность В*А      
                    if (connectorElement.SystemClassification == MEPSystemClassification.PowerUnBalanced)
                    {
                        connectorData.ApparentLoadParameterArray = new[]
                        {
                            new ParameterData(familyDocument,
                                connectorElement.get_Parameter(BuiltInParameter.RBS_ELEC_APPARENT_LOAD_PHASE1)),
                            new ParameterData(familyDocument,
                                connectorElement.get_Parameter(BuiltInParameter.RBS_ELEC_APPARENT_LOAD_PHASE2)),
                            new ParameterData(familyDocument,
                                connectorElement.get_Parameter(BuiltInParameter.RBS_ELEC_APPARENT_LOAD_PHASE3))
                        };
                    }
                    else
                    {
                        connectorData.ApparentLoadParameterArray = new[] { new ParameterData(familyDocument, connectorElement.get_Parameter(BuiltInParameter.RBS_ELEC_APPARENT_LOAD)) };
                    }

                    //Коэффициент мощности                    
                    connectorData.PowerFactorParameter = new ParameterData(familyDocument, connectorElement.get_Parameter(BuiltInParameter.RBS_ELEC_POWER_FACTOR));

                    //Напряжение
                    connectorData.VoltageParameter = new ParameterData(familyDocument, connectorElement.get_Parameter(BuiltInParameter.RBS_ELEC_VOLTAGE));

                    //Количество полюсов
                    connectorData.NumberOfRolesParameter = new ParameterData(familyDocument, connectorElement.get_Parameter(BuiltInParameter.RBS_ELEC_NUMBER_OF_POLES));

                    //Описание соединителя
                    connectorData.DescriptionParameter = new ParameterData(familyDocument, connectorElement.get_Parameter(BuiltInParameter.RBS_CONNECTOR_DESCRIPTION));

                    var descriptionParameter = connectorElement.get_Parameter(BuiltInParameter.RBS_CONNECTOR_DESCRIPTION);
                    if (descriptionParameter != null && !descriptionParameter.IsReadOnly)
                    {
                        using (var transaction = new Transaction(familyDocument, "Set ID"))
                        {
                            transaction.Start();
                            connectorData.Id = Guid.NewGuid().ToString();
                            descriptionParameter.Set(connectorData.Id);
                            transaction.Commit();
                        }
                    }

                    result.Add(connectorData);
                }

                familyDocument.LoadFamily(document, new Helpers.FamilyLoadOptions());

                familyDocument.Close(false);
            }

            _familyConnectorsDictionary.Add(familySymbol.Family.Id, result);

            return result;
        }

        internal static List<Utilities.DTO.Connector> GetConnectors(Element element)
        {
            var elementId = element?.GetTypeId();
            if (elementId == null || elementId.IntegerValue < 0 ||
                !(element.Document.GetElement(elementId) is FamilySymbol familySymbol))
            {
                return null;
            }

            var electricalConnectorsList = GetAllConnectors(element).Where(x => x.Domain == Domain.DomainElectrical && (
                x.ElectricalSystemType == ElectricalSystemType.PowerBalanced ||
                x.ElectricalSystemType == ElectricalSystemType.PowerUnBalanced ||
                x.ElectricalSystemType == ElectricalSystemType.PowerCircuit)).ToList();
            if (electricalConnectorsList.Count == 0)
                return null;

            var result = new List<Utilities.DTO.Connector>();
            var connectorDataList = GetConnectorsDataFromFamily(element.Document, familySymbol);
            foreach (var electricalConnector in electricalConnectorsList)
            {
                var connectorData = connectorDataList.FirstOrDefault(x => x.Id == electricalConnector.Description);
                if (connectorData == null)
                    continue;

                connectorData.UpdateParameterValues(element);

                var item = new Utilities.DTO.Connector();
                item.Direction = new Utilities.DTO.Point(electricalConnector.CoordinateSystem.BasisZ.X,
                    electricalConnector.CoordinateSystem.BasisZ.Y, electricalConnector.CoordinateSystem.BasisZ.Z);
                item.Origin = new Utilities.DTO.Point(
                    Math.Round(UnitUtils.ConvertFromInternalUnits(electricalConnector.Origin.X, UnitTypeId.Millimeters), 2),
                    Math.Round(UnitUtils.ConvertFromInternalUnits(electricalConnector.Origin.Y, UnitTypeId.Millimeters), 2),
                    Math.Round(UnitUtils.ConvertFromInternalUnits(electricalConnector.Origin.Z, UnitTypeId.Millimeters), 2));
                item.Description = connectorData.Description;
                result.Add(item);
            }

            return result.Count == 0 ? null : result;
            
        }

        internal static void DisconnectConnectos(Element element)
        {
            var connectedConnectorsList = GetAllConnectors(element);
            if (connectedConnectorsList.Count == 0)
                return;

            using (var transaction = new Transaction(element.Document, "Disconnect"))
            {
                transaction.Start();

                foreach (var connector in connectedConnectorsList)
                {
                    if (connector.AllRefs == null)
                        continue;

                    foreach (var refElement in connector.AllRefs)
                    {
                        if (!(refElement is Connector refConnector))
                            continue;

                        connector.DisconnectFrom(refConnector);
                    }
                }

                transaction.Commit();
            }
        }
    }
}
