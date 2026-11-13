using Aveva.Pdms.Database;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Text;

namespace Polymetal.Pdms.Ifc
{
    class PdmsProperty
    {
        private DbElement _root;
        private Dictionary<string, string> _properties;
        internal PdmsProperty()
        {
        }

        internal PdmsProperty(DbElement root)
        {
            _root = root;
            _properties = new Dictionary<string, string>();
        }

        internal StringBuilder GetText()
        {
            var sb = new StringBuilder();
            SetProperties();
            foreach (var pair in _properties)
            {
                sb.AppendLine(string.Format("{0}${1}", pair.Key, pair.Value));
            }
            return sb;
        }

        private void SetProperties()
        {
            SetPropertyProjectPosition();
            SetPropertyReference();
            SetPropertyDescription();
            SetPropertyWeight();
            SetPropertyModifications();
        }

        private void SetPropertyWeight()
        {
            if (_root.GetElementType() != DbElementTypeInstance.EQUIPMENT) return;
            var weight = 0.0;
            weight += GetWeight(_root);
            foreach (var sube in _root.Members(DbElementTypeInstance.SUBEQUIPMENT))
            {
                weight += GetWeight(sube);
            }
            if (weight > 0)
            {
                _properties.Add("Масса", string.Format("{0} кг.",weight));
            }
        }

        private double GetWeight(DbElement item)
        {
            var weight = 0.0;
            var valueStr = item.GetAsString(ExportIfc.Att_EquiWeight).Replace("unset", string.Empty).Replace(",", ".");
            if (string.IsNullOrEmpty(valueStr)) return weight;
            var valueData = valueStr.Replace("\\n"," ").Split(new[] {' '}, StringSplitOptions.RemoveEmptyEntries);
            double value;
            foreach (var data in valueData)
            {
                if (!double.TryParse(data, out value))
                {
                    Console.WriteLine(_root);
                    continue;
                }
                weight += value;
            }
            return weight;
        }

        private void SetPropertyDescription()
        {
            _properties.Add("Описание", _root.GetAsString(DbAttribute.GetDbAttribute(":PolySpecDesc")));
        }

        private void SetPropertyProjectPosition()
        {
            if (_root.GetBool(DbAttributeInstance.ISNAME))
            {
                _properties.Add("Проектная позиция", _root.GetAsString(DbAttributeInstance.FLNN));
            }
        }

        private void SetPropertyModifications()
        {
            var createdBy = string.Empty;
            var modifiedBy = string.Empty;
            var createdAt = DateTime.Now;
            var modifiedAt = createdAt;
            using (var sqlConnection = new SqlConnection(ExportIfc.ConnectionString))
            {
                sqlConnection.Open();
                var sqlCommand = string.Format(
                    "select b.ShortName, a.LogUserSessionDateTime " +
                    "from LogUserSession a " +
                    "join [User] b on a.UserID = b.UserID " +
                    "where a.LogUserSessionRefEle='{0}' and " +
                    "a.LogUserSessionTypeEle = '{1}' and " +
                    "b.UserPosition <> 'administrator' " +
                    "order by a.LogUserSessionDateTime",
                    _root.GetAsString(DbAttributeInstance.REF),
                    _root.GetElementType().ShortName);
                using (var sqlCmd = new SqlCommand(sqlCommand, sqlConnection))
                {
                    var reader = sqlCmd.ExecuteReader();
                    while (reader.Read())
                    {
                        if (string.IsNullOrEmpty(createdBy))
                        {
                            createdBy = reader[0].ToString().Trim();
                            createdAt = (DateTime) reader[1];
                        }
                        else
                        {
                            modifiedBy = reader[0].ToString().Trim();
                            modifiedAt = (DateTime)reader[1];
                        }
                    }
                    reader.Close();
                }
                sqlConnection.Close();
            }

            if (!string.IsNullOrEmpty(createdBy))
            {
                _properties.Add("Создан", string.Format("{0} {1}",createdBy,createdAt.ToString("dd.MM.yyyy HH:mm")));
            }
            if (!string.IsNullOrEmpty(modifiedBy))
            {
                _properties.Add("Изменен", string.Format("{0} {1}", modifiedBy, modifiedAt.ToString("dd.MM.yyyy HH:mm")));
            }
        }

        private void SetPropertyReference()
        {
            _properties.Add("Ссылка", _root.GetAsString(DbAttributeInstance.REF));
        }

        
    }
}
