using Autodesk.Revit.DB;

namespace Polymetal.Exchange.Revit.v2023.DTO
{
    internal class ParameterData
    {
        internal string Name { get; set; }
        internal bool IsAssociated { get; set; }

        internal StorageType StorageType { get; set; }

        internal object Value { get; set; }

        internal bool IsValid { get; set; }

        internal ParameterData(Document document, Parameter parameter)
        {
            if (parameter == null || !parameter.HasValue)
            {
                IsValid = false;
                return;
            }

            IsValid = true;
            StorageType = parameter.StorageType;

            var associatedFamilyParameter = document.FamilyManager?.GetAssociatedFamilyParameter(parameter);
            if (associatedFamilyParameter != null)
            {
                IsAssociated = true;
                Name = associatedFamilyParameter.Definition.Name;
            }
            else
            {
                IsAssociated = false;
                Name = parameter.Definition.Name;

                switch (parameter.StorageType)
                {
                    case StorageType.Double:
                        Value = UnitUtils.ConvertFromInternalUnits(parameter.AsDouble(), parameter.GetUnitTypeId());
                        break;
                    case StorageType.Integer:
                        Value = parameter.AsInteger();
                        break;
                    case StorageType.String:
                        Value = parameter.AsString();
                        break;
                    case StorageType.ElementId:
                        Value = parameter.AsElementId();
                        break;
                    case StorageType.None:
                    default:
                        Value = parameter.AsValueString();
                        break;
                }
            }
        }
    }
}