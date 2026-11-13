using Aveva.Core.Database.Filters;
using Aveva.Core.Database;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Polymetal.Exchange.E3D.Helpers
{
    internal class Common
    {
        /// <summary>
        /// Удаление пустых структурных элементов
        /// </summary>
        /// <param name="rootSite">сайт для очистки</param>
        internal static void ClearEmptyElements(DbElement rootSite)
        {
            if (rootSite.IsNull || !rootSite.IsValid)
                return;

            var types = new[]
            {
                DbElementTypeInstance.SBFRAMEWORK, DbElementTypeInstance.FRMWORK, DbElementTypeInstance.STRUCTURE,
                DbElementTypeInstance.ZONE
            };

            var mcountFilter = new AttributeIntFilter(DbAttributeInstance.MCOU, FilterOperator.Equals, 0);
            foreach (var type in types)
            {
                var typeFilter = new TypeFilter(type);
                var andFilter = new AndFilter(typeFilter, mcountFilter);
                var collection = new DBElementCollection(rootSite, andFilter).Cast<DbElement>().ToList();
                foreach (var element in collection)
                {
                    if (!element.IsDeleteable)
                        continue;
                    element.Delete();
                }
            }
        }

        /// <summary>
        /// Сортировка элементов с отметкой
        /// </summary>
        /// <param name="root"></param>
        internal static void SortLevelElements(DbElement root)
        {
            var dict = new SortedDictionary<int, DbElement>();
            //формат отметки +1.000 0.000 -1.000
            var regular = new Regex("[\\+\\-]?\\d*.\\d{3}");
            foreach (var member in root.Members())
            {
                if (!member.GetBool(DbAttributeInstance.ISNAME))
                    continue;
                var postfixName = member.Name().Split('_').Last();
                if (!regular.IsMatch(postfixName) ||
                    !int.TryParse(postfixName.Replace(".", string.Empty), out int intValue) ||
                    dict.ContainsKey(intValue))
                    continue;

                dict.Add(intValue, member);
            }

            var ordered = dict.Values.ToList();
            for (var i = 0; i < ordered.Count; i++)
            {
                var item = ordered[i];
                var sequ = item.GetInteger(DbAttributeInstance.SEQU);
                if (sequ == i + 1) continue;
                if (i == 0)
                {
                    item.InsertBeforeFirst(item.Owner);
                }
                else
                {
                    item.InsertAfter(ordered[i - 1]);
                }
            }
        }

        /// <summary>
        /// Сортировка элементов по имени
        /// </summary>
        /// <param name="root"></param>
        internal static void SortElements(DbElement root)
        {
            if (root.IsNull || !root.IsValid || root.Members().Length <= 0)
                return;

            var ordered = root.Members().ToList().OrderBy(x=>x.GetString(DbAttributeInstance.FLNM)).ToList();
            for (var i = 0; i < ordered.Count; i++)
            {
                var item = ordered[i];
                if (item.GetInteger(DbAttributeInstance.SEQU) == i + 1) 
                    continue;

                if (i == 0)
                {
                    item.InsertBeforeFirst(item.Owner);
                }
                else
                {
                    item.InsertAfter(ordered[i - 1]);
                }
            }
        }
    }
}
