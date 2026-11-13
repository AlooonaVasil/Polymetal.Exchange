using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Text;
using Aveva.Core.Database;
using Polymetal.E3D.Ifc.Helpers;

namespace Polymetal.E3D.Ifc
{
    class AvevaProperty
    {
        private DbElement _root;
        private Dictionary<string, string> _properties;

        internal AvevaProperty()
        {
        }

        internal AvevaProperty(DbElement root)
        {
            _root = root;
            _properties = new Dictionary<string, string>();
        }

        internal StringBuilder GetText(Dictionary<string,string> task3DProp = null )
        {
            var sb = new StringBuilder();

            if (_root.ElementType == DbElementTypeInstance.LINDIMENSION)
                return sb;

            SetProperties();

            if (task3DProp != null)
                foreach (KeyValuePair<string, string> pair in task3DProp)
                    _properties.Add(pair.Key, pair.Value);

            foreach (var pair in _properties)
            {
                sb.AppendLine($"{pair.Key}${pair.Value}");
            }
            
            return sb;
        }
        
        private void SetProperties()
        {
            SetPropertyProjectPosition();
            SetPropertyReference();
            SetPropertyElementType();
            SetPropertyDescription();
            SetPropertyWeight();
            SetPropertyModifications();
        }

        private void SetPropertyWeight()
        {
            if (_root.ElementType != DbElementTypeInstance.EQUIPMENT) 
                return;

            var weight = 0.0;
            weight += GetWeight(_root);

            foreach (var sube in _root.Members(DbElementTypeInstance.SUBEQUIPMENT))
            {
                weight += GetWeight(sube);
            }

            if (weight <= 0.0) 
                return;

            var propertyName = ExportIfc.Consumer == Enumerators.ConsumerEnum.Microstation
                ? "Element weight"
                : "Масса";
            _properties.Add(propertyName, $"{weight} кг.");
        }

        private double GetWeight(DbElement item)
        {
            var weight = 0.0;
            var valueStr = item.GetAsString(Core.Common.UserAttributes.EquiWeight).Replace("unset", string.Empty).Replace(",", ".");
            if (string.IsNullOrEmpty(valueStr)) return weight;
            var valueData = valueStr.Replace("\\n"," ").Split(new[] {' '}, StringSplitOptions.RemoveEmptyEntries);
            double value;
            foreach (var data in valueData)
            {
                if (!double.TryParse(data, out value))
                {
                    Aveva.Core.Utilities.CommandLine.Command.CreateCommand($"$Pwtf GetWeight {_root}").Run();
                    continue;
                }
                weight += value;
            }
            return weight;
        }

        private void SetPropertyDescription()
        {
            var propertyName = ExportIfc.Consumer == Enumerators.ConsumerEnum.Microstation
                ? "Element description"
                : "Описание";

            _properties.Add(propertyName, _root.GetAsString(Core.Common.UserAttributes.PolySpecDesc));
        }

        private void SetPropertyElementType()
        {
            var propertyName = ExportIfc.Consumer == Enumerators.ConsumerEnum.Microstation
                ? "Element type"
                : "Тип";

            _properties.Add(propertyName, _root.ElementType.ShortName);
        }

        private void SetPropertyProjectPosition()
        {
            var propertyName = ExportIfc.Consumer == Enumerators.ConsumerEnum.Microstation
                ? "Element tag"
                : "Проектная позиция";

            if (_root.GetBool(DbAttributeInstance.ISNAME))
            {
                _properties.Add(propertyName, _root.GetAsString(DbAttributeInstance.FLNN));
            }
        }

        private void SetPropertyModifications()
        {
            var createdBy = string.Empty;
            var modifiedBy = string.Empty;
            var createdAt = default(DateTime);
            var modifiedAt = default(DateTime);

            var rootRef = _root.GetAsString(DbAttributeInstance.REF);
            var rootDbName = _root.Db.Name;
            var dataBase = LookInAdditionalLog(_root) ? "LogE3D_Additional" : "LogE3D";

            var dateCreateCmd = $"select min(DateTimeAction) from {dataBase} where RefElement = '{rootRef}' and DbName = '{rootDbName}'";
            var userCreateCmd = $"select top 1 b.ShortName from {dataBase} a join [User] b on a.UserId = b.UserID where RefElement = '{rootRef}' and DbName = '{rootDbName}' and b.UserPosition <> 'administrator' order by DateTimeAction";

            var modifyCmd = $"select top 1 b.ShortName, a.DateTimeAction from {dataBase} a join [User] b on a.UserId = b.UserID where RefElement = '{rootRef}' and DbName = '{rootDbName}' and ActionType=1 and b.UserPosition <> 'administrator' order by DateTimeAction desc";

            using (var sqlConnection = new SqlConnection(ExportIfc.ConnectionString))
            {
                sqlConnection.Open();

                using (var sqlCmd = new SqlCommand(dateCreateCmd, sqlConnection))
                {
                    var res = sqlCmd.ExecuteScalar();
                    if (res != null && res != DBNull.Value) createdAt = (DateTime)res;
                }

                using (var sqlCmd = new SqlCommand(userCreateCmd, sqlConnection))
                {
                    var res = sqlCmd.ExecuteScalar();
                    if (res != null && res != DBNull.Value) createdBy = (string)res;
                }

                using (var sqlCmd = new SqlCommand(modifyCmd, sqlConnection))
                {
                    using (var reader = sqlCmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            modifiedAt = (DateTime)reader["DateTimeAction"];
                            modifiedBy = reader["ShortName"].ToString().Trim();
                        }
                    }                        
                }

                sqlConnection.Close();
            }

            if (!string.IsNullOrEmpty(createdBy))
            {
                var propertyName = ExportIfc.Consumer == Enumerators.ConsumerEnum.Microstation
                    ? "Element created"
                    : "Создан";
                _properties.Add(propertyName, $"{createdAt.ToString("dd.MM.yyyy HH:mm")} ({createdBy})");
            }
            if (!string.IsNullOrEmpty(modifiedBy))
            {
                var propertyName = ExportIfc.Consumer == Enumerators.ConsumerEnum.Microstation
                    ? "Element modified"
                    : "Изменен";
                _properties.Add(propertyName, $"{modifiedAt.ToString("dd.MM.yyyy HH:mm")} ({modifiedBy})");
            }
        }

        private static bool LookInAdditionalLog(DbElement item)
        {
            if (item.ElementType == DbElementTypeInstance.EQUIPMENT &&
                item.GetAsString(DbAttributeInstance.FUNC) == "MagiCAD")
                return true;

            if (item.IsAttributeValid(Core.Common.UserAttributes.PolyTeklaGuid) &&
                !string.IsNullOrEmpty(item.GetAsString(Core.Common.UserAttributes.PolyTeklaGuid)
                    .Replace("unset", string.Empty)))
                return true;

            if (item.IsAttributeValid(Core.Common.UserAttributes.PolyRevitGuid) &&
                !string.IsNullOrEmpty(item.GetAsString(Core.Common.UserAttributes.PolyRevitGuid).Replace("unset", string.Empty)))
                return true;

            return false;
        }

        private void SetPropertyReference()
        {
            var propertyName = ExportIfc.Consumer == Enumerators.ConsumerEnum.Microstation
                ? "Element reference"
                : "Ссылка";

            _properties.Add(propertyName, _root.GetAsString(DbAttributeInstance.REF));

            if (_root.Owner.ElementType != DbElementTypeInstance.BRANCH) 
                return;

            var pipe = _root.Owner.Owner;
            if (pipe.ElementType != DbElementTypeInstance.PIPE) 
                return;

            propertyName = ExportIfc.Consumer == Enumerators.ConsumerEnum.Microstation
                ? "Pipe reference"
                : "Pipe_Ссылка";
            _properties.Add(propertyName, pipe.GetAsString(DbAttributeInstance.REF));
        }        
    }
}
