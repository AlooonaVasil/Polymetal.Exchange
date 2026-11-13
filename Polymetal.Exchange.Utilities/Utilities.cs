using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Data.SqlTypes;
using System.Globalization;
using System.Xml;
using Polymetal.Exchange.Utilities.Properties;

namespace Polymetal.Exchange.Utilities
{
    public class ProjectInformation
    {
        public struct PdmsInfo
        {
            public string Project;
            public string Mdb;
            public string UserName;
            public string Password;
            public decimal SubObjectId;
            public byte StructureVersion;
        }

        public struct ProgramInfo
        {
            public string Path;
            public string Version;
            public Common.Source TypeSource;
        }

        public ProgramInfo Program = new ProgramInfo();
        public PdmsInfo Pdms = new PdmsInfo();
        public bool NeedExchange = false;
        public decimal ProjectId = 0;
        public double[] Transformation;

        public ProjectInformation(decimal projectId = 0, string modelName = "")
        {
            Transformation = new double[] {1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0};
            var sqlCommand = "USE " +
                             "Aveva_Poly_Db " +
                             "SELECT " +
                             "ProjectID, " +
                             "ModelPath, " +
                             "SubObjectID, " +
                             "[Version], " +
                             "NeedExchange, " +
                             "Offset, " +
                             "Program, " +
                             "Project, " +
                             "MDB, " +
                             "SubObjectID, " +
                             "StructureVersion, " +
                             "SystemUser = (SELECT TOP 1 ValueSetting FROM PdmsSettings WHERE NameSetting = 'ID_SYSUSERNAME_PROJECT' ), " +
                             "SystemPassword = (SELECT TOP 1 ValueSetting FROM PdmsSettings WHERE NameSetting = 'ID_SYSPASSWORD_PROJECT' ) " +
                             "FROM " +
                             "ExchangeProject " +
                             "JOIN vPdmsProjects ON vPdmsProjects.IdSubObject = ExchangeProject.SubObjectID";
            if (projectId > 0)
            {
                sqlCommand += " WHERE ProjectID = " + projectId;
            }
            else if (modelName.Split('#').Length == 2)
            {
                //получение по проекту и базе Pdms
                var pdmsProj = modelName.Split('#')[0];
                var pdmsMdb = modelName.Split('#')[1];
                sqlCommand += " WHERE Project = '" + pdmsProj + "' AND MDB = '" + pdmsMdb + "'";
            }
            else
            {
                return;
            }


            using (var sqlConnection = new SqlConnection(Common.ConnectionString))
            {
                sqlConnection.Open();

                using (var sqlCmd = new SqlCommand(sqlCommand, sqlConnection))
                {
                    var reader = sqlCmd.ExecuteReader();
                    if (reader.Read())
                    {
                        ProjectId = (decimal) reader["ProjectID"];
                        NeedExchange = (bool) reader["NeedExchange"];

                        Program.Path = reader["ModelPath"].ToString().Trim();
                        Program.Version = reader["Version"].ToString().Trim();
                        var sourceType = reader["Program"].ToString().Trim();
                        if (sourceType.Equals("MagiCAD", StringComparison.OrdinalIgnoreCase))
                            Program.TypeSource = Common.Source.MagiCAD;
                        else if (sourceType.Equals("Tekla", StringComparison.OrdinalIgnoreCase))
                            Program.TypeSource = Common.Source.Tekla;
                        else if (sourceType.Equals("Revit", StringComparison.OrdinalIgnoreCase))
                            Program.TypeSource = Common.Source.Revit;
                        else if (sourceType.Equals("PDMS", StringComparison.OrdinalIgnoreCase))
                            Program.TypeSource = Common.Source.Pdms;

                        Pdms.Project = reader["Project"].ToString().Trim();
                        Pdms.Mdb = reader["MDB"].ToString().Trim();
                        Pdms.SubObjectId = (decimal)reader["SubObjectID"];
                        Pdms.StructureVersion = (byte) reader["StructureVersion"];
                        Pdms.UserName = reader["SystemUser"].ToString().Trim();
                        Pdms.Password = Pdms.Project.Equals("TRA")
                            ? "XXXXXX"
                            : reader["SystemPassword"].ToString().Trim();

                        if (reader["Offset"] != DBNull.Value && !string.IsNullOrEmpty(reader["Offset"].ToString()))
                        {
                            var offsetData = reader["Offset"].ToString().Split();
                            if (offsetData.Length == 12)
                            {
                                Transformation = new[]
                                {
                                    double.Parse(offsetData[0], CultureInfo.InvariantCulture),
                                    double.Parse(offsetData[1], CultureInfo.InvariantCulture),
                                    double.Parse(offsetData[2], CultureInfo.InvariantCulture),
                                    double.Parse(offsetData[3], CultureInfo.InvariantCulture),
                                    double.Parse(offsetData[4], CultureInfo.InvariantCulture),
                                    double.Parse(offsetData[5], CultureInfo.InvariantCulture),
                                    double.Parse(offsetData[6], CultureInfo.InvariantCulture),
                                    double.Parse(offsetData[7], CultureInfo.InvariantCulture),
                                    double.Parse(offsetData[8], CultureInfo.InvariantCulture),
                                    double.Parse(offsetData[9], CultureInfo.InvariantCulture),
                                    double.Parse(offsetData[10], CultureInfo.InvariantCulture),
                                    double.Parse(offsetData[11], CultureInfo.InvariantCulture),
                                };
                            }
                        }
                    }
                    reader.Close();
                }
                sqlConnection.Close();
            }
        }
    }

    public class Common
    {
        public enum Source
        {
            Tekla,
            Pdms,
            MagiCAD,
            Revit,
            Unknown
        }

        public enum ExchangeType
        {
            Export,
            Import
        }

        public enum PartType
        {
            Beam,
            PolyBeam,
            ContourPlate,
            BeamPad,
            PolyBeamPad,
            ContourPlatePad,
            BeamShape,
            PolyBeamShape
        }

        public static string ConnectionString = new sqlReference.Service1().GetStrConnectPdms();
        
        public static DataTable CreateTeklaTable()
        {
            var dt = new DataTable("ExportTable");
            dt.Columns.Add("export_from", typeof (string));
            dt.Columns.Add("project_name", typeof (string));
            dt.Columns.Add("element_status", typeof (string));
            dt.Columns.Add("beam_guid", typeof (Guid));
            dt.Columns.Add("beam_phase", typeof (string));
            dt.Columns.Add("profile_name", typeof (string));
            dt.Columns.Add("material_name", typeof (string));
            dt.Columns.Add("beam_name", typeof (string));
            dt.Columns.Add("beam_type", typeof (string));
            dt.Columns.Add("beam_class", typeof (string));
            dt.Columns.Add("beam_points", typeof (SqlXml));
            dt.Columns.Add("beam_booleans", typeof (SqlXml));
            dt.Columns.Add("pdms_item", typeof (SqlXml));
            dt.Columns.Add("beam_created_by", typeof(string));
            dt.Columns.Add("beam_modified_by", typeof(string));
            dt.Columns.Add("beam_created", typeof(DateTime));
            dt.Columns.Add("beam_modified", typeof(DateTime));
            dt.Columns.Add("beam_description", typeof(string));
            dt.Columns.Add("beam_properties", typeof(string));
            return dt;
        }

        public static DataTable CreateMagiCadTable()
        {
            var dt = new DataTable("ExportTable");
            dt.Columns.Add("element_status", typeof(string));
            dt.Columns.Add("element_guid", typeof(string));
            dt.Columns.Add("element_type", typeof(string));
            dt.Columns.Add("element_info", typeof(string));
            dt.Columns.Add("pdms_item", typeof(string));
            dt.Columns.Add("element_created_by", typeof(string));
            dt.Columns.Add("element_modified_by", typeof(string));
            dt.Columns.Add("element_created", typeof(DateTime));
            dt.Columns.Add("element_modified", typeof(DateTime));
            return dt;
        }

        public static DataTable CreateRevitTable()
        {
            var dt = new DataTable("ExportTable");
            dt.Columns.Add("element_status", typeof(string));
            dt.Columns.Add("element_guid", typeof(string));
            dt.Columns.Add("element_type", typeof(string));
            dt.Columns.Add("element_info", typeof(string));
            dt.Columns.Add("pdms_item", typeof(string));
            dt.Columns.Add("element_created_by", typeof(string));
            dt.Columns.Add("element_modified_by", typeof(string));
            dt.Columns.Add("element_created", typeof(DateTime));
            dt.Columns.Add("element_modified", typeof(DateTime));
            return dt;
        }

        public static void SendMessage(Source source, int triggerId, string message = "")
        {
            Console.WriteLine(message);
            if (triggerId == 0) return;
            if (message.Length > 200)
            {
                message = message.Substring(0, 200);
            }

            using (var sqlConnection = new SqlConnection(ConnectionString))
            {
                sqlConnection.Open();
                var commandText = "INSERT INTO [Aveva_Poly_Db].[dbo].[ExchangeLog] " +
                                  "([IdExchangeTrigger] " +
                                  ",[DateLog] " +
                                  ",[Message] " +
                                  ",[SourceLog]) " +
                                  "VALUES " +
                                  "(@TriggerId " +
                                  ",@DateLog " +
                                  ",@Message" +
                                  ",@SourceLog)";

                using (var sqlCmd = new SqlCommand(commandText, sqlConnection))
                {
                    sqlCmd.Parameters.AddWithValue("@TriggerId", triggerId);
                    sqlCmd.Parameters.AddWithValue("@DateLog", DateTime.Now);
                    sqlCmd.Parameters.AddWithValue("@Message", message);
                    sqlCmd.Parameters.AddWithValue("@SourceLog", source.ToString());
                    sqlCmd.ExecuteNonQuery();
                }
                sqlConnection.Close();
            }
        }

        public static SqlXml XmlDocToSqlXml(XmlDocument doc)
        {
            var xmlNodeReader = new XmlNodeReader(doc);
            var sqlXml = new SqlXml(xmlNodeReader);
            return sqlXml;
        }

        public static XmlDocument SqlXmlToXmlDoc(SqlXml sqlXml)
        {
            var doc = new XmlDocument();
            if (!sqlXml.IsNull)
            {
                doc.InnerXml = sqlXml.Value;
            }
            return doc;
        }

        internal static SqlXml GetSqlXmlFromString(string value)
        {
            var doc = new XmlDocument {InnerXml = value};
            var xnr = new XmlNodeReader(doc);
            return new SqlXml(xnr);
        }

        public static DateTime GetLastTeklaProjectUpdateTime(decimal projectId)
        {
            //TODO: возможно необходимо переделать
            var result = DateTime.MinValue;

            var sqlCommand = "SELECT " +
                             "LastDate = MAX(DateTrigger) " +
                             "FROM " +
                             "[Aveva_Poly_Db].[dbo].[ExchangeTrigger] " +
                             "WHERE " +
                             "ProjectId = " + projectId + " AND " +
                             "StatusTrigger = 2 AND SourceTrigger = 'Tekla'";

            using (var sqlConnection = new SqlConnection(ConnectionString))
            {
                sqlConnection.Open();
                using (var sqlCmd = new SqlCommand(sqlCommand, sqlConnection))
                {
                    var reader = sqlCmd.ExecuteReader();
                    if (reader.Read())
                    {
                        DateTime.TryParse(reader["LastDate"].ToString(), out result);
                    }
                    reader.Close();
                }
                sqlConnection.Close();
            }
            result = result > DateTime.MinValue ? result.AddMinutes(-2) : result;
            return result;
        }

        public static bool UpdateSql(DataTable dt, DataTable allGuidTable, Source exportSource, decimal projectId,int triggerId = 0)
        {
            var result = false;
            using (var sqlConnection = new SqlConnection(ConnectionString))
            {
                sqlConnection.Open();
                if (dt.Rows.Count <= 500)
                {
                    switch (exportSource)
                    {
                        case Source.MagiCAD:
                            result = UpdateMagiCadSqlData(dt, allGuidTable, projectId, triggerId, sqlConnection);
                            break;
                        case Source.Tekla:
                            result=UpdateTeklaSqlData(dt, allGuidTable, projectId, sqlConnection);
                            break;
                        case Source.Revit:
                            result = UpdateRevitSqlData(dt, allGuidTable, projectId, triggerId, sqlConnection);
                            break;
                    }
                }
                else
                {
                    var dtTemp = dt.Clone();
                    var prevIndex = 0;
                    for (var i = 0; i < dt.Rows.Count; i++)
                    {
                        dtTemp.ImportRow(dt.Rows[i]);
                        if ((i + 1)%500 != 0 && i != dt.Rows.Count - 1) continue;
                        Console.WriteLine("Обновляем строки с {0} по {1}", prevIndex+1, i+1);
                        switch (exportSource)
                        {
                            case Source.MagiCAD:
                                result = UpdateMagiCadSqlData(dtTemp, allGuidTable, projectId, triggerId, sqlConnection);
                                break;
                            case Source.Tekla:
                                result = UpdateTeklaSqlData(dtTemp, allGuidTable, projectId, sqlConnection);
                                break;
                            case Source.Revit:
                                result = UpdateRevitSqlData(dtTemp, allGuidTable, projectId, triggerId, sqlConnection);
                                break;
                        }

                        prevIndex = i+1;
                        dtTemp.Clear();
                    }
                }
                sqlConnection.Close();
            }
            return result;
        }

        public static bool UpdateTeklaSqlData(DataTable dt, DataTable allGuidTable, decimal projectId, SqlConnection sqlConnection)
        {
            var result = false;
            var sqlCmd = new SqlCommand("UpdateTeklaData", sqlConnection);
            sqlCmd.Parameters.AddWithValue("@DataTable", dt);
            sqlCmd.Parameters.AddWithValue("@AllGuidTable", allGuidTable);
            sqlCmd.Parameters.AddWithValue("@project_id", projectId);
            using (sqlCmd)
            {
                sqlCmd.CommandType = CommandType.StoredProcedure;
                try
                {
                    sqlCmd.CommandTimeout = 600;
                    sqlCmd.ExecuteNonQuery();
                    result = true;
                }
                catch (Exception exc)
                {
                    Console.WriteLine(exc.Message);
                }
            }
            return result;
        }

        public static bool UpdateTeklaData(DataTable create, DataTable modify, DataTable delete, decimal projectId)
        {
            var result = false;
            using (var sqlConnection = new SqlConnection(ConnectionString))
            {
                sqlConnection.Open();
                SqlCommand sqlCmd;
                if (create.Rows.Count > 0)
                {
                    if (create.Rows.Count > 1000)
                    {
                        var dtTemp = create.Clone();
                        var prevIndex = 0;
                        for (var i = 0; i < create.Rows.Count; i++)
                        {
                            dtTemp.ImportRow(create.Rows[i]);
                            if ((i + 1) % 1000 != 0 && i != create.Rows.Count - 1) continue;
                            Console.WriteLine("Вставляем строки с {0} по {1}", prevIndex + 1, i + 1);

                            sqlCmd = new SqlCommand("CreateTeklaData", sqlConnection);
                            sqlCmd.Parameters.AddWithValue("@DataTable", dtTemp);
                            sqlCmd.Parameters.AddWithValue("@project_id", projectId);
                            using (sqlCmd)
                            {
                                sqlCmd.CommandType = CommandType.StoredProcedure;
                                try
                                {
                                    sqlCmd.CommandTimeout = 600;
                                    sqlCmd.ExecuteNonQuery();
                                    result = true;
                                }
                                catch (Exception exc)
                                {
                                    Console.WriteLine(exc.Message);
                                }
                            }

                            prevIndex = i + 1;
                            dtTemp.Clear();
                        }
                    }
                    else
                    {
                        sqlCmd = new SqlCommand("CreateTeklaData", sqlConnection);
                        sqlCmd.Parameters.AddWithValue("@DataTable", create);
                        sqlCmd.Parameters.AddWithValue("@project_id", projectId);
                        using (sqlCmd)
                        {
                            sqlCmd.CommandType = CommandType.StoredProcedure;
                            try
                            {
                                sqlCmd.CommandTimeout = 600;
                                sqlCmd.ExecuteNonQuery();
                                result = true;
                            }
                            catch (Exception exc)
                            {
                                Console.WriteLine(exc.Message);
                            }
                        }
                    }
                }

                if (delete.Rows.Count > 0)
                {
                    if (delete.Rows.Count > 1000)
                    {
                        var dtTemp = delete.Clone();
                        var prevIndex = 0;
                        for (var i = 0; i < delete.Rows.Count; i++)
                        {
                            dtTemp.ImportRow(delete.Rows[i]);
                            if ((i + 1) % 1000 != 0 && i != delete.Rows.Count - 1) continue;
                            Console.WriteLine("Удаляем строки с {0} по {1}", prevIndex + 1, i + 1);

                            sqlCmd = new SqlCommand("DeleteTeklaData", sqlConnection);
                            sqlCmd.Parameters.AddWithValue("@DataTable", dtTemp);
                            sqlCmd.Parameters.AddWithValue("@project_id", projectId);
                            using (sqlCmd)
                            {
                                sqlCmd.CommandType = CommandType.StoredProcedure;
                                try
                                {
                                    sqlCmd.CommandTimeout = 600;
                                    sqlCmd.ExecuteNonQuery();
                                    result = true;
                                }
                                catch (Exception exc)
                                {
                                    Console.WriteLine(exc.Message);
                                }
                            }

                            prevIndex = i + 1;
                            dtTemp.Clear();
                        }
                    }
                    else
                    {
                        sqlCmd = new SqlCommand("DeleteTeklaData", sqlConnection);
                        sqlCmd.Parameters.AddWithValue("@DataTable", delete);
                        sqlCmd.Parameters.AddWithValue("@project_id", projectId);
                        using (sqlCmd)
                        {
                            sqlCmd.CommandType = CommandType.StoredProcedure;
                            try
                            {
                                sqlCmd.CommandTimeout = 600;
                                sqlCmd.ExecuteNonQuery();
                                result = true;
                            }
                            catch (Exception exc)
                            {
                                Console.WriteLine(exc.Message);
                            }
                        }
                    }
                }

                if (modify.Rows.Count > 0)
                {
                    if (modify.Rows.Count > 1000)
                    {
                        var dtTemp = modify.Clone();
                        var prevIndex = 0;
                        for (var i = 0; i < modify.Rows.Count; i++)
                        {
                            dtTemp.ImportRow(modify.Rows[i]);
                            if ((i + 1) % 1000 != 0 && i != modify.Rows.Count - 1) continue;
                            Console.WriteLine("Обновляем строки с {0} по {1}", prevIndex + 1, i + 1);

                            sqlCmd = new SqlCommand("UpdateTeklaData", sqlConnection);
                            sqlCmd.Parameters.AddWithValue("@DataTable", dtTemp);
                            sqlCmd.Parameters.AddWithValue("@project_id", projectId);
                            using (sqlCmd)
                            {
                                sqlCmd.CommandType = CommandType.StoredProcedure;
                                try
                                {
                                    sqlCmd.CommandTimeout = 600;
                                    sqlCmd.ExecuteNonQuery();
                                    result = true;
                                }
                                catch (Exception exc)
                                {
                                    Console.WriteLine(exc.Message);
                                }
                            }

                            prevIndex = i + 1;
                            dtTemp.Clear();
                        }
                    }
                    else
                    {
                        sqlCmd = new SqlCommand("UpdateTeklaData", sqlConnection);
                        sqlCmd.Parameters.AddWithValue("@DataTable", modify);
                        sqlCmd.Parameters.AddWithValue("@project_id", projectId);
                        using (sqlCmd)
                        {
                            sqlCmd.CommandType = CommandType.StoredProcedure;
                            try
                            {
                                sqlCmd.CommandTimeout = 600;
                                sqlCmd.ExecuteNonQuery();
                                result = true;
                            }
                            catch (Exception exc)
                            {
                                Console.WriteLine(exc.Message);
                            }
                        }
                    }
                }
                sqlConnection.Close();
            }
            return result;
        }

        public static bool UpdateMagiCadData(DataTable create, DataTable modify, DataTable delete, decimal projectId,
            int triggerId)
        {
            var result = false;
            using (var sqlConnection = new SqlConnection(ConnectionString))
            {
                sqlConnection.Open();
                SqlCommand sqlCmd;
                if (create.Rows.Count > 0)
                {
                    sqlCmd = new SqlCommand("CreateMagiCadData", sqlConnection);
                    sqlCmd.Parameters.AddWithValue("@DataTable", create);
                    sqlCmd.Parameters.AddWithValue("@project_id", projectId);
                    sqlCmd.Parameters.AddWithValue("@trigger_id", triggerId);
                    using (sqlCmd)
                    {
                        sqlCmd.CommandType = CommandType.StoredProcedure;
                        try
                        {
                            sqlCmd.CommandTimeout = 600;
                            sqlCmd.ExecuteNonQuery();
                            result = true;
                        }
                        catch (Exception exc)
                        {
                            Console.WriteLine(exc.Message);
                        }
                    }
                }

                if (delete.Rows.Count > 0)
                {
                    sqlCmd = new SqlCommand("DeleteMagiCadData", sqlConnection);
                    sqlCmd.Parameters.AddWithValue("@DataTable", delete);
                    sqlCmd.Parameters.AddWithValue("@project_id", projectId);
                    using (sqlCmd)
                    {
                        sqlCmd.CommandType = CommandType.StoredProcedure;
                        try
                        {
                            sqlCmd.CommandTimeout = 600;
                            sqlCmd.ExecuteNonQuery();
                            result = true;
                        }
                        catch (Exception exc)
                        {
                            Console.WriteLine(exc.Message);
                        }
                    }
                }

                if (modify.Rows.Count > 0)
                {
                    sqlCmd = new SqlCommand("UpdateMagiCadData", sqlConnection);
                    sqlCmd.Parameters.AddWithValue("@DataTable", modify);
                    sqlCmd.Parameters.AddWithValue("@project_id", projectId);
                    sqlCmd.Parameters.AddWithValue("@trigger_id", triggerId);
                    using (sqlCmd)
                    {
                        sqlCmd.CommandType = CommandType.StoredProcedure;
                        try
                        {
                            sqlCmd.CommandTimeout = 600;
                            sqlCmd.ExecuteNonQuery();
                            result = true;
                        }
                        catch (Exception exc)
                        {
                            Console.WriteLine(exc.Message);
                        }
                    }
                }
                sqlConnection.Close();
            }
            return result;
        }

        public static bool UpdateMagiCadSqlData(DataTable dt, DataTable allGuidTable, decimal projectId, int triggerId, SqlConnection sqlConnection)
        {
            var result = false;
            var sqlCmd = new SqlCommand("UpdateMagiCadData", sqlConnection);
            sqlCmd.Parameters.AddWithValue("@DataTable", dt);
            sqlCmd.Parameters.AddWithValue("@AllGuidTable", allGuidTable);
            //sqlCmd.Parameters.AddWithValue("@export_from", exportSource.ToString());
            sqlCmd.Parameters.AddWithValue("@project_id", projectId);
            sqlCmd.Parameters.AddWithValue("@trigger_id", triggerId);
            using (sqlCmd)
            {
                sqlCmd.CommandType = CommandType.StoredProcedure;
                try
                {
                    sqlCmd.CommandTimeout = 600;
                    sqlCmd.ExecuteNonQuery();
                    result = true;
                }
                catch (Exception exc)
                {
                    Console.WriteLine(exc.Message);
                }
            }
            return result;
        }

        public static bool UpdateRevitSqlData(DataTable dt, DataTable allGuidTable, decimal projectId, int triggerId, SqlConnection sqlConnection)
        {
            var result = false;
            var sqlCmd = new SqlCommand("UpdateRevitData", sqlConnection);
            sqlCmd.Parameters.AddWithValue("@DataTable", dt);
            sqlCmd.Parameters.AddWithValue("@AllGuidTable", allGuidTable);
            //sqlCmd.Parameters.AddWithValue("@export_from", exportSource.ToString());
            sqlCmd.Parameters.AddWithValue("@project_id", projectId);
            sqlCmd.Parameters.AddWithValue("@trigger_id", triggerId);
            using (sqlCmd)
            {
                sqlCmd.CommandType = CommandType.StoredProcedure;
                try
                {
                    sqlCmd.CommandTimeout = 600;
                    sqlCmd.ExecuteNonQuery();
                    result = true;
                }
                catch (Exception exc)
                {
                    Console.WriteLine(exc.Message);
                }
            }
            return result;
        }

        public static bool GetTriggerInfoById(int triggerId, out decimal projectId,
            out Source source,
            out decimal status)
        {
            projectId = 0M;
            status = 0M;
            source = Source.Tekla;
            var sqlCommand = string.Empty;

            sqlCommand = "USE Aveva_Poly_Db " +
                         "SELECT " +
                         "ProjectId, " +
                         "StatusTrigger, " +
                         "SourceTrigger " +
                         "FROM " +
                         "ExchangeTrigger " +
                         "WHERE IdExchangeTrigger = " + triggerId;          

            using (var sqlConnection = new SqlConnection(ConnectionString))
            {
                sqlConnection.Open();

                using (var sqlCmd = new SqlCommand(sqlCommand, sqlConnection))
                {
                    var reader = sqlCmd.ExecuteReader();
                    if (reader.Read())
                    {
                        if (reader["SourceTrigger"].ToString().Trim().ToLower().Equals("magicad"))
                            source = Source.MagiCAD;
                        else if (reader["SourceTrigger"].ToString().Trim().ToLower().Equals("tekla"))
                            source = Source.Tekla;
                        else if (reader["SourceTrigger"].ToString().Trim().ToLower().Equals("pdms"))
                            source = Source.Pdms;
                        else if (reader["SourceTrigger"].ToString().Trim().ToLower().Equals("revit"))
                            source = Source.Revit;

                        status = decimal.Parse(reader["StatusTrigger"].ToString());
                        projectId = decimal.Parse(reader["ProjectId"].ToString());
                    }
                    reader.Close();
                }
                sqlConnection.Close();
            }

            return projectId > 0;
        }

        public static Dictionary<Guid,DateTime> GetTeklaBeamHistory(decimal projectId)
        {
            var sqlCommand = "USE Aveva_Poly_Db " +
                             "SELECT beam_guid, beam_modified " +
                             "FROM Tekla WHERE project_id = " + projectId + " AND element_status = 'UPD'";
            var dictionary = new Dictionary<Guid, DateTime>();
            using (var sqlConnection = new SqlConnection(ConnectionString))
            {
                sqlConnection.Open();

                using (var sqlCmd = new SqlCommand(sqlCommand, sqlConnection))
                {
                    var reader = sqlCmd.ExecuteReader();
                    while (reader.Read())
                    {
                        DateTime modDate;
                        if (DateTime.TryParse(reader["beam_modified"].ToString(), out modDate))
                        {
                            dictionary.Add((Guid)reader["beam_guid"], modDate);
                        }
                    }
                    reader.Close();
                }
                sqlConnection.Close();
            }
            return dictionary;
        }

        public static DataTable GetUpdatedTeklaData(decimal projectId)
        {
            var table = CreateTeklaTable();
            using (var sqlConnection = new SqlConnection(ConnectionString))
            {
                sqlConnection.Open();
                var sqlCmd = new SqlCommand("GetUpdatedTeklaData", sqlConnection);
                sqlCmd.Parameters.AddWithValue("@project_id", projectId);
                using (sqlCmd)
                {
                    sqlCmd.CommandType = CommandType.StoredProcedure;
                    sqlCmd.CommandTimeout = 600;
                    var reader = sqlCmd.ExecuteReader();
                    while (reader.Read())
                    {
                        var row = table.NewRow();
                        foreach (DataColumn column in table.Columns)
                        {
                            if (column.ColumnName == "beam_points" || column.ColumnName == "pdms_item" ||
                                column.ColumnName == "beam_booleans")
                            {
                                if (!String.IsNullOrEmpty(reader[column.ColumnName].ToString()))
                                {
                                    row[column.ColumnName] = GetSqlXmlFromString(reader[column.ColumnName].ToString());
                                }
                                continue;
                            }
                            row[column.ColumnName] = reader[column.ColumnName];
                        }
                        table.Rows.Add(row);
                    }
                    reader.Close();
                }
                sqlConnection.Close();
            }

            return table;
        }

        public static DataTable GetUpdatedMagiCadData(decimal projectId)
        {
            var table = CreateMagiCadTable();
            using (var sqlConnection = new SqlConnection(ConnectionString))
            {
                sqlConnection.Open();
                var sqlCmd = new SqlCommand("GetUpdatedMagiCadData", sqlConnection);
                sqlCmd.Parameters.AddWithValue("@project_id", projectId);
                using (sqlCmd)
                {
                    sqlCmd.CommandType = CommandType.StoredProcedure;
                    var reader = sqlCmd.ExecuteReader();
                    while (reader.Read())
                    {
                        var row = table.NewRow();
                        foreach (DataColumn column in table.Columns)
                        {
                            row[column.ColumnName] = reader[column.ColumnName];
                        }
                        table.Rows.Add(row);
                    }
                    reader.Close();
                }
                sqlConnection.Close();
            }

            return table;
        }

        /// <summary>
        /// Те же строки, что GetUpdatedMagiCadData, но без текста геометрии.
        /// Геометрию одного элемента читает GetMagiCadElementInfo.
        /// </summary>
        public static DataTable GetUpdatedMagiCadHeaders(decimal projectId)
        {
            var table = CreateMagiCadTable();
            using (var sqlConnection = new SqlConnection(ConnectionString))
            {
                sqlConnection.Open();
                var sqlCmd = new SqlCommand("GetUpdatedMagiCadData", sqlConnection);
                sqlCmd.Parameters.AddWithValue("@project_id", projectId);
                sqlCmd.CommandTimeout = 600;
                using (sqlCmd)
                {
                    sqlCmd.CommandType = CommandType.StoredProcedure;
                    using (var reader = sqlCmd.ExecuteReader(CommandBehavior.SequentialAccess))
                    {
                        while (reader.Read())
                        {
                            var row = table.NewRow();
                            for (var i = 0; i < reader.FieldCount; i++)
                            {
                                var name = reader.GetName(i);
                                if (name.Equals("element_info", StringComparison.OrdinalIgnoreCase))
                                {
                                    SkipLargeText(reader, i);
                                    continue;
                                }

                                if (!table.Columns.Contains(name))
                                {
                                    reader.GetValue(i);
                                    continue;
                                }

                                row[name] = reader.IsDBNull(i) ? DBNull.Value : reader.GetValue(i);
                            }

                            table.Rows.Add(row);
                        }
                    }
                }
            }

            return table;
        }

        public static string GetMagiCadElementInfo(decimal projectId, string elementGuid)
        {
            using (var sqlConnection = new SqlConnection(ConnectionString))
            {
                sqlConnection.Open();
                var sqlCmd = new SqlCommand(
                    "USE Aveva_Poly_Db SELECT element_info FROM MagiCad WHERE project_id = @project_id AND element_guid = @element_guid",
                    sqlConnection);
                sqlCmd.Parameters.AddWithValue("@project_id", projectId);
                sqlCmd.Parameters.AddWithValue("@element_guid", elementGuid);
                sqlCmd.CommandTimeout = 600;
                var value = sqlCmd.ExecuteScalar();
                return value == null || value == DBNull.Value ? null : value.ToString();
            }
        }

        /// <summary>
        /// Записывает статус и ссылку на элемент. Текст геометрии в MagiCad не меняется.
        /// </summary>
        public static void SetMagiCadImportState(DataTable dt, decimal projectId)
        {
            using (var sqlConnection = new SqlConnection(ConnectionString))
            {
                sqlConnection.Open();
                using (var useCmd = new SqlCommand("USE Aveva_Poly_Db", sqlConnection))
                    useCmd.ExecuteNonQuery();

                using (var transaction = sqlConnection.BeginTransaction())
                using (var sqlCmd = new SqlCommand(
                    "UPDATE MagiCad SET element_status = @element_status, pdms_item = @pdms_item WHERE project_id = @project_id AND element_guid = @element_guid",
                    sqlConnection, transaction))
                {
                    sqlCmd.CommandTimeout = 600;
                    sqlCmd.Parameters.Add("@element_status", SqlDbType.NVarChar, -1);
                    sqlCmd.Parameters.Add("@pdms_item", SqlDbType.NVarChar, -1);
                    sqlCmd.Parameters.Add("@project_id", SqlDbType.Decimal).Value = projectId;
                    sqlCmd.Parameters.Add("@element_guid", SqlDbType.NVarChar, -1);

                    foreach (DataRow row in dt.Rows)
                    {
                        sqlCmd.Parameters["@element_status"].Value = row["element_status"];
                        var pdmsItem = row["pdms_item"];
                        sqlCmd.Parameters["@pdms_item"].Value = pdmsItem == null || pdmsItem == DBNull.Value
                            ? (object)DBNull.Value
                            : pdmsItem.ToString();
                        sqlCmd.Parameters["@element_guid"].Value = row["element_guid"];
                        sqlCmd.ExecuteNonQuery();
                    }

                    transaction.Commit();
                }
            }
        }

        private static void SkipLargeText(SqlDataReader reader, int ordinal)
        {
            if (reader.IsDBNull(ordinal))
                return;

            var buffer = new char[8192];
            long dataIndex = 0;
            long read;
            while ((read = reader.GetChars(ordinal, dataIndex, buffer, 0, buffer.Length)) > 0)
                dataIndex += read;
        }

        public static DataTable GetUpdatedRevitData(decimal projectId)
        {
            var table = CreateRevitTable();
            using (var sqlConnection = new SqlConnection(ConnectionString))
            {
                sqlConnection.Open();
                var sqlCmd = new SqlCommand("GetUpdatedRevitData", sqlConnection);
                sqlCmd.Parameters.AddWithValue("@project_id", projectId);
                using (sqlCmd)
                {
                    sqlCmd.CommandType = CommandType.StoredProcedure;
                    var reader = sqlCmd.ExecuteReader();
                    while (reader.Read())
                    {
                        var row = table.NewRow();
                        foreach (DataColumn column in table.Columns)
                        {
                            row[column.ColumnName] = reader[column.ColumnName];
                        }
                        table.Rows.Add(row);
                    }
                    reader.Close();
                }
                sqlConnection.Close();
            }

            return table;
        }

        /// <summary>
        /// Получение описания из БД для создаваемого элемента
        /// </summary>
        /// <param name="code">код создаваемого элемента</param>
        /// <param name="tablName">имя таблицы</param>
        /// <returns></returns>
        public static string GetPdmsElementDescription(string code, string tablName)
        {
            var result = string.Empty;

            using (var sqlConnection = new SqlConnection(ConnectionString))
            {
                sqlConnection.Open();
                var query = "USE Aveva_Poly_Db SELECT TOP 1 " + tablName + "Desc FROM " + tablName + " WHERE " +
                            tablName + "Code = '" + code + "'";
                using (var sqlCmd = new SqlCommand(query, sqlConnection))
                {
                    var res = sqlCmd.ExecuteScalar();
                    if (res != null)
                    {
                        result = res.ToString().Trim();
                    }
                }
                sqlConnection.Close();
            }

            return result;
        }

        public static void SetUpdatedTeklaData(DataTable dt, decimal projectId)
        {
            using (var sqlConnection = new SqlConnection(ConnectionString))
            {
                sqlConnection.Open();
                var sqlCmd = new SqlCommand("SetUpdatedTeklaData", sqlConnection);
                sqlCmd.Parameters.AddWithValue("@DataTable", dt);
                sqlCmd.Parameters.AddWithValue("@project_id", projectId);
                using (sqlCmd)
                {
                    sqlCmd.CommandType = CommandType.StoredProcedure;
                    try
                    {
                        sqlCmd.ExecuteNonQuery();
                    }
                    catch{}
                }
                sqlConnection.Close();
            }
        }

        public static void SetUpdatedMagiCadData(DataTable dt, decimal projectId)
        {
            using (var sqlConnection = new SqlConnection(ConnectionString))
            {
                sqlConnection.Open();
                var sqlCmd = new SqlCommand("SetUpdatedMagiCadData", sqlConnection);
                sqlCmd.Parameters.AddWithValue("@DataTable", dt);
                sqlCmd.Parameters.AddWithValue("@project_id", projectId);
                using (sqlCmd)
                {
                    sqlCmd.CommandType = CommandType.StoredProcedure;
                    try
                    {
                        sqlCmd.ExecuteNonQuery();
                    }
                    catch { }
                }
                sqlConnection.Close();
            }
        }

        public static void SetUpdatedRevitData(DataTable dt, decimal projectId)
        {
            using (var sqlConnection = new SqlConnection(ConnectionString))
            {
                sqlConnection.Open();
                var sqlCmd = new SqlCommand("SetUpdatedRevitData", sqlConnection);
                sqlCmd.Parameters.AddWithValue("@DataTable", dt);
                sqlCmd.Parameters.AddWithValue("@project_id", projectId);
                using (sqlCmd)
                {
                    sqlCmd.CommandType = CommandType.StoredProcedure;
                    try
                    {
                        sqlCmd.ExecuteNonQuery();
                    }
                    catch { }
                }
                sqlConnection.Close();
            }
        }

        public static Dictionary<string, int> GetAllUsers()
        {
            var userList = new Dictionary<string, int>();
            using (var sqlConnection = new SqlConnection(ConnectionString))
            {
                sqlConnection.Open();
                var sqlCommand = "USE Aveva_Poly_Db " +
                                 "SELECT " +
                                 "UserID, " +
                                 "UserLogin " +
                                 "FROM [User] " +
                                 "WHERE UserPosition <> 'administrator'";

                using (var sqlCmd = new SqlCommand(sqlCommand, sqlConnection))
                {
                    var reader = sqlCmd.ExecuteReader();
                    while (reader.Read())
                    {
                        userList.Add(reader["UserLogin"].ToString().Trim().ToLower(), int.Parse(reader["UserID"].ToString()));
                        userList.Add(reader["UserID"].ToString(), int.Parse(reader["UserID"].ToString()));
                    }
                    reader.Close();
                }
                sqlConnection.Close();
            }
            return userList;
        }
    }
}
