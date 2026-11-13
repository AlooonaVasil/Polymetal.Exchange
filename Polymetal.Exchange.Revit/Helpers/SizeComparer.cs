using System.Collections.Generic;

namespace Polymetal.Exchange.Revit
{
    class SizeComparer : IEqualityComparer<double[]>
    {
        public bool Equals(double[] x, double[] y)
        {
            if (x.Length != y.Length)
                return false;

            for (int i = 0; i < x.Length; ++i)
                if (!x[i].Equals(y[i]))
                    return false;

            return true;
        }

        public int GetHashCode(double[] obj)
        {
            return string.Join("", obj).GetHashCode();
        }
    }
}
