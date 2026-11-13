using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Polymetal.E3D.Ifc.DB
{
    internal class IfcDatabaseProvider : IIFcDatabaseProvider
    {
        private string _connectionString;

        public IfcDatabaseProvider(string connectionString)
        {
            _connectionString = connectionString;
        }
        
        /// <summary>
        /// Получить идентификатор (IdTask) для ревизии задания.
        /// </summary>
        /// <param name="taskId">Идентификатор задания</param>
        /// <param name="revisionNumber">Номер ревизии</param>
        /// <returns>Строковое значение IdTask для ревизии</returns>
        public string GetRevisionIdByRevisionNum(int taskId, int revisionNumber)
        {
            var result = string.Empty;

            using (var sqlConnection = new SqlConnection(_connectionString))
            {
                sqlConnection.Open();

                var query = "SELECT IdTask FROM Task3D WHERE OriginalRevisionTaskId = @taskId AND RevisionNumber = @revisionNumber";
                using (var cmd = new SqlCommand(query, sqlConnection))
                {
                    cmd.Parameters.AddWithValue("@taskId", SqlDbType.Int).Value = taskId;
                    cmd.Parameters.AddWithValue("@revisionNumber", SqlDbType.Int).Value = revisionNumber;
                    var obj = cmd.ExecuteScalar();
                    if (obj != null && obj != DBNull.Value)
                        result = obj.ToString();
                }
            }

            return result;
        }
    }
}
