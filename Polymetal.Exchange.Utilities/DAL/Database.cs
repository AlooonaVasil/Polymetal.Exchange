using System;
using System.Collections.Generic;
using System.Data.SqlClient;

namespace Polymetal.Exchange.Utilities.DAL
{
    public class Database
    {
        /// <summary>
        /// Деактивация помещения в Инжиниринг
        /// </summary>
        /// <param name="roomGuid"></param>
        /// <param name="subObjectId"></param>
        /// <param name="revitProjectId"></param>
        public static void DeactivateRoom(string roomGuid, decimal subObjectId, decimal revitProjectId)
        {
            using (var sqlConnection = new SqlConnection(Common.ConnectionString))
            {
                sqlConnection.Open();

                var cmd =
                    $"update e_Rooms set activity = 0 where RoomGuid = '{roomGuid}' and SubObjectId = {subObjectId} and RevitProjectId = {revitProjectId}";
                using (var sqlCmd = new SqlCommand(cmd, sqlConnection))
                {
                    sqlCmd.ExecuteNonQuery();
                }

                sqlConnection.Close();
            }
        }

        /// <summary>
        /// Обновление помещения в Инжиниринг
        /// </summary>
        /// <param name="roomGuid"></param>
        /// <param name="subObjectId"></param>
        /// <param name="revitProjectId"></param>
        public static int ActivateRoom(string roomGuid, decimal subObjectId, decimal revitProjectId)
        {
            var roomId = 0;
            using (var sqlConnection = new SqlConnection(Common.ConnectionString))
            {
                sqlConnection.Open();
                
                var cmd = $"select RoomId from e_Rooms where RoomGuid = '{roomGuid}' and SubObjectId = {subObjectId} and RevitProjectId = {revitProjectId}";
                using (var sqlCmd = new SqlCommand(cmd, sqlConnection))
                {
                    var res = sqlCmd.ExecuteScalar();
                    if (res != null)
                    {
                        roomId = Convert.ToInt32(res);
                    }
                }

                if (roomId == 0)
                {
                    cmd = $"insert into e_Rooms VALUES ({subObjectId}, {revitProjectId}, '{roomGuid}', 1) select SCOPE_IDENTITY()";
                    using (var sqlCmd = new SqlCommand(cmd, sqlConnection))
                    {
                        var res = sqlCmd.ExecuteScalar();
                        if (res != null)
                        {
                            roomId = Convert.ToInt32(res);
                        }
                    }
                }

                sqlConnection.Close();
            }

            return roomId;
        }

        public static void UpdateRoomProperties(int roomId, Dictionary<int, string> propertyDictionary)
        {
            using (var sqlConnection = new SqlConnection(Common.ConnectionString))
            {
                sqlConnection.Open();

                if (sqlConnection.State == System.Data.ConnectionState.Open)
                {
                    var cmd = $"update e_RoomAttributeValue set Activity = 0 where RoomId = {roomId}";
                    using (var sqlCmd = new SqlCommand(cmd, sqlConnection))
                    {
                        sqlCmd.ExecuteNonQuery();
                    }

                    foreach (var pair in propertyDictionary)
                    {
                        cmd = $"update e_RoomAttributeValue set AttributeValue = '{pair.Value}', Activity = 1 where RoomId = {roomId} and AttributeId = {pair.Key} " +
                              "IF @@ROWCOUNT = 0 " +
                              $"insert into e_RoomAttributeValue VALUES ({roomId}, {pair.Key}, '{pair.Value}', 1)";
                        using (var sqlCmd = new SqlCommand(cmd, sqlConnection))
                        {
                            sqlCmd.ExecuteNonQuery();
                        }
                    }
                }

                sqlConnection.Close();
            }
        }

        /// <summary>
        /// Получение списка необходимых атрибутов для помещений в Инжиниринг
        /// </summary>
        /// <returns></returns>
        public static Dictionary<string, int> GetRequiredRoomAttributes()
        {
            var attributesDictionary = new Dictionary<string, int>();

            using (var sqlConnection = new SqlConnection(Common.ConnectionString))
            {
                sqlConnection.Open();

                if (sqlConnection.State == System.Data.ConnectionState.Open)
                {
                    var cmd = "select AttributeId, AttributeName from e_Attributes where AttributeSource = 3";
                    using (var sqlCmd = new SqlCommand(cmd, sqlConnection))
                    {
                        using (var reader = sqlCmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                var attributeName = reader["AttributeName"].ToString().ToUpper();
                                if (attributesDictionary.ContainsKey(attributeName))
                                    continue;

                                attributesDictionary.Add(attributeName, (int)reader["AttributeId"]);
                            }
                        }
                    }
                }

                sqlConnection.Close();
            }

            return attributesDictionary;
        }
    }
}
