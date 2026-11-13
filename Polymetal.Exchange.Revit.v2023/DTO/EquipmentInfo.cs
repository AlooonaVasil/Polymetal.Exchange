using Autodesk.Revit.DB;
using Polymetal.Exchange.Revit.v2023.Helpers;

namespace Polymetal.Exchange.Revit.v2023.DTO
{
    class EquipmentInfo : MagiCadInfo
    {
        internal EquipmentInfo(Element element)
        {
            Root = element;
            Family = Root.Document.GetElement(Root.GetTypeId());
            ElementType = Utilities.Helpers.Enumerators.ElementType.EQUI;

            Description = "Equipment";
            var allModelTypeCommentsParam = Family.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_COMMENTS);
            if (allModelTypeCommentsParam != null && allModelTypeCommentsParam.HasValue &&
                !string.IsNullOrEmpty(allModelTypeCommentsParam.AsString()))
            {
                Description = allModelTypeCommentsParam.AsString();
            }
            else if (!string.IsNullOrEmpty(Family.Name))
            {
                Description = Family.Name;
            }

            var pmiWeightParam = Family.LookupParameter(Attributes.PMI_WEIGHT);
            Weight = pmiWeightParam?.AsDouble() ?? 0.0;

            SetCommonProperties();

            ConnectorData.DisconnectConnectos(Root);
        }
    }
}
