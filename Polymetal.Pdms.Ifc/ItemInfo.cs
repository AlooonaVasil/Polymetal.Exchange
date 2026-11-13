//using System.Collections.Generic;
//using Aveva.Pdms.Database;

//namespace Polymetal.Pdms.Ifc
//{
//    class ItemInfo
//    {
//        internal ItemInfo OwnerInfo;
//        internal List<ItemInfo> Children;
//        internal List<PdmsGeometry> Geometry;
//        internal Dictionary<string, string> Properties;
//        internal DbElementType Type;
//        internal DbElement Item;
//        internal string IfcType;
//        internal string Name;

//        internal ItemInfo()
//        {
//        }

//        internal ItemInfo(DbElement root)
//        {
//            Item = root;
//            Children = new List<ItemInfo>();
//            Type = Item.GetElementType();
//            Geometry = new List<PdmsGeometry>();
//            IfcType = Type.Name;
//            Name = GetPdmsName(Item);
//        }

//        private static string GetPdmsName(DbElement item)
//        {
//            return item.GetBool(DbAttributeInstance.ISNAME)
//               ? item.GetAsString(DbAttributeInstance.FLNM)
//               : string.Format("/{0}-{1}", item.GetElementType().ShortName,
//                   item.GetAsString(DbAttributeInstance.REF).Substring(1));
//        }
//    }
//}
