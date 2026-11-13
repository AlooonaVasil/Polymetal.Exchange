using Autodesk.Revit.DB;

namespace Polymetal.Exchange.Revit.v2023.Helpers
{
    public class FamilyLoadOptions : IFamilyLoadOptions
    {
        public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
        {
            overwriteParameterValues = false;
            return true;
        }

        public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse,
            out FamilySource source, out bool overwriteParameterValues)
        {
            source = FamilySource.Project;
            overwriteParameterValues = false;
            return true;
        }
    }
}
