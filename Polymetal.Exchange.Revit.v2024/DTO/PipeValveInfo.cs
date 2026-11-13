using Autodesk.Revit.DB;

namespace Polymetal.Exchange.Revit.v2024.DTO
{
    class PipeValveInfo : MagiCadInfo
    {
        internal PipeValveInfo(Element element)
        {
            Root = element;
            Family = Root.Document.GetElement(Root.GetTypeId());
            ElementType = Utilities.Helpers.Enumerators.ElementType.PIPE;

            Description = Family.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_COMMENTS).AsString();
            if (string.IsNullOrEmpty(Description)) 
                Description = "PipeValve";
            
            SetCommonProperties();
        }
    }
}
