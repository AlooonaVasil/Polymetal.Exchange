using System;
using System.Collections.Generic;
using System.Linq;
using Aveva.Core.Database;
using Polymetal.Core.Common;

namespace Polymetal.Exchange.E3D
{
    internal class ElementHistory
    {
        public string ItemType;
        public string ItemRef;
        public string ItemOwnerRef;
        public string ItemDbna;
        public string ItemAttributes;
        public string ItemRootZoneName;
        public string ItemSitePurp;
        public string CreatedBy;
        public string ModifiedBy;
        public string DeletedBy;
        public DateTime Created;
        public DateTime Modified;
        public DateTime Deleted;
        public string Category;

        internal ElementHistory(DbElement item, object createdBy, object createdAt, object modifiedBy, object modifiedAt, object deletedBy, DateTime deletedAt)
        {
            ItemType = item.GetElementType().ShortName;
            ItemRef = item.GetAsString(DbAttributeInstance.REF);
            ItemOwnerRef = item.Owner.GetAsString(DbAttributeInstance.REF);
            ItemDbna = item.GetAsString(DbAttributeInstance.DBNA);
            ItemAttributes = GetHistoryAttributes(item);
            ItemRootZoneName = GetHistoryRootZoneName(item);
            ItemSitePurp = "JOB";
            CreatedBy = createdBy.ToString();
            ModifiedBy = modifiedBy.ToString();
            DeletedBy = deletedBy.ToString();
            Created =
                createdAt == DBNull.Value
                    ? DateTime.MinValue
                    : DateTime.Parse(createdAt.ToString());
            Modified =
                modifiedAt == DBNull.Value
                    ? DateTime.MinValue
                    : DateTime.Parse(modifiedAt.ToString());
            Deleted = deletedAt;
            Category = GetCategory(item);
        }

        internal static string GetHistoryAttributes(DbElement element)
        {
            string retStr = string.Empty;
            var checkdbAttributes = new List<DbAttribute> { DbAttributeInstance.NAME, DbAttributeInstance.POS, DbAttributeInstance.ORI, DbAttributeInstance.SPRE };

            foreach (DbAttribute attribute in checkdbAttributes)
            {
                string valuestr = string.Empty;
                element.GetValidAsString(attribute, ref valuestr);

                retStr = retStr + attribute.ShortName + "|" + valuestr + "#";
            }

            return retStr;
        }

        internal static string GetHistoryRootZoneName(DbElement element)
        {
            string retStr = string.Empty;

            var ownLst = element.GetElementArray(DbAttributeInstance.OWNLST).ToList();

            DbElement fZone = ownLst.FirstOrDefault(x => x.GetElementType().Equals(DbElementTypeInstance.ZONE));

            if (!fZone.IsNull && fZone.IsValid)
            {
                return fZone.GetAsString(DbAttributeInstance.NAME);
            }

            return retStr;
        }

        private static string GetCategory(DbElement item)
        {
            if (item.GetElementType() == DbElementTypeInstance.EQUIPMENT && item.GetAsString(DbAttributeInstance.FUNC) == "MagiCAD")
            {
                return item.Owner.Name().ToUpper().EndsWith("PIPE") ? "PIPE MC" : "HVAC MC";
            }

            if (item.GetElementType() == DbElementTypeInstance.EQUIPMENT || item.GetElementType() == DbElementTypeInstance.SUBEQUIPMENT)
            {
                return item.GetElement(DbAttributeInstance.SPRE).IsNull ? "EQUI USR" : "EQUI STD";
            }

            if (item.IsAttributeValid(UserAttributes.PolyTeklaGuid) && !string.IsNullOrEmpty(item.GetString(UserAttributes.PolyTeklaGuid)))
            {
                return "ELEM TS";
            }

            if (item.IsAttributeValid(UserAttributes.PolyRevitGuid) && !string.IsNullOrEmpty(item.GetString(UserAttributes.PolyRevitGuid)))
            {
                return "ELEM RVT";
            }

            if (item.Owner.GetElementType() == DbElementTypeInstance.BRANCH)
            {
                return item.Owner.Owner.GetElementType() == DbElementTypeInstance.PIPE ? "PIPE" : "HVAC";
            }

            if (item.GetElementType() == DbElementTypeInstance.BRANCH)
            {
                return item.Owner.GetElementType() == DbElementTypeInstance.PIPE ? "PIPE" : "HVAC";
            }

            if (item.GetElementType() == DbElementTypeInstance.PIPE)
            {
                return "PIPE";
            }

            if (item.GetElementType() == DbElementTypeInstance.HVAC)
            {
                return "HVAC";
            }

            return string.Empty;
        }
    }
}
