using System;
using System.Collections.Generic;
using Tekla.Structures.Model;

namespace Polymetal.Exchange.Tekla.v2018
{
    static class Extensions
    {
        internal static void GetAllParts(this Part root, ref Dictionary<Part, List<Part>> partsDict)
        {
            if (!partsDict.ContainsKey(root))
                partsDict.Add(root, new List<Part>());
            var children = root.GetChildren();
            while (children.MoveNext())
            {
                var child = children.Current;
                if (child is RebarGroup) continue;
                if (child is BooleanPart booleanPart)
                {
                    if (booleanPart.Type == BooleanPart.BooleanTypeEnum.BOOLEAN_ADD)
                    {
                        booleanPart.OperativePart.GetAllParts(ref partsDict);
                    }
                    else if (booleanPart.Type == BooleanPart.BooleanTypeEnum.BOOLEAN_CUT)
                    {
                        partsDict[root].Add(booleanPart.OperativePart);
                    }                    
                }               
            }
        }
    }
}
