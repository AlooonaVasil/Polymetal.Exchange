using Autodesk.Revit.DB;
using Polymetal.Exchange.Revit.v2024.Helpers;

namespace Polymetal.Exchange.Revit.v2024.DTO
{
    class DuctDiffusersInfo : MagiCadInfo
    {
        internal DuctDiffusersInfo(Element element)
        {
            Root = element;
            Family = Root.Document.GetElement(Root.GetTypeId());
            ElementType = Utilities.Helpers.Enumerators.ElementType.DUCT;

            Description = Family.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_COMMENTS).AsString();
            if (string.IsNullOrEmpty(Description)) 
                Description = "DuctDiffusers";

            var pmiWeightParam = Family.LookupParameter(Attributes.PMI_WEIGHT);
            Weight = pmiWeightParam == null ? 0.0 : pmiWeightParam.AsDouble();

            SetCommonProperties();
        }
    }
}
