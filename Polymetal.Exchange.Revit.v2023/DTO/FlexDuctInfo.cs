using Autodesk.Revit.DB;

namespace Polymetal.Exchange.Revit.v2023.DTO
{
    class FlexDuctInfo : MagiCadInfo
    {
        internal FlexDuctInfo(Element element)
        {
            Root = element;
            Family = Root.Document.GetElement(Root.GetTypeId());
            ElementType = Utilities.Helpers.Enumerators.ElementType.DUCT;

            Description = Family.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_COMMENTS).AsString();
            if (string.IsNullOrEmpty(Description)) 
                Description = "FlexDuct";

            SetCommonProperties();
        }
    }
}
