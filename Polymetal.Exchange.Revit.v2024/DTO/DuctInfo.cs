using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Polymetal.Exchange.Revit.v2024.Helpers;

namespace Polymetal.Exchange.Revit.v2024.DTO
{
    class DuctInfo : MagiCadInfo
    {
        internal DuctInfo(Element element)
        {
            Root = element;
            Family = Root.Document.GetElement(Root.GetTypeId());
            ElementType = Utilities.Helpers.Enumerators.ElementType.DUCT;

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

            SetCommonProperties();
        }
    }
}
