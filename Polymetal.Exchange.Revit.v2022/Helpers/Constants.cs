using System;

namespace Polymetal.Exchange.Revit.v2022.Helpers
{
    class Constants
    {
        internal const double STEEL_DENSITY = 7850.0;
        internal static double FACTOR_AREA = Math.Pow(304.8 / 1000, 2);
        internal static double FACTOR_LENGTH = 304.8;
        internal static double FACTOR_VOLUME = Math.Pow(304.8 / 1000, 3);
        internal static double FACTOR_ANGLE = 180.0 / Math.PI;
    }
}
