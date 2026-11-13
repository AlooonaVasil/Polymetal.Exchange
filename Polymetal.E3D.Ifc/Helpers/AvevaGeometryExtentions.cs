using Aveva.Core.Database;
using System.Collections.Generic;
using System.Linq;
using AG = Polymetal.E3D.Ifc.AvevaGeometry;

namespace Polymetal.E3D.Ifc.Helpers
{
    internal static class AvevaGeometryExtentions
    {
        internal static IEnumerable<AG> AttachDirectNagatives(this IEnumerable<AG> positiveGeometry, DbElement owner)
        {
            var negative = owner.Members()
                .Where(member => AG.NegativeTypes.Contains(member.ElementType))
                .Select(member => AG.GetItemGeometry(member, true))
                .ToArray();

            if (negative.Length != 0)
            {
                foreach (var positive in positiveGeometry)
                {
                    positive.Negatives.AddRange(negative);
                }
            }  

            return positiveGeometry;
        }
    }
}
