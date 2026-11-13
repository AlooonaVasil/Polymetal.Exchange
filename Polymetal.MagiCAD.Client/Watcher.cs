using System;
using System.Data;
using System.Data.SqlClient;
using Polymetal.MagiCAD.Client.Properties;
using cad = Autodesk.AutoCAD.ApplicationServices.Application;
using Ap = Autodesk.AutoCAD.ApplicationServices;
using Db = Autodesk.AutoCAD.DatabaseServices;
using Rt = Autodesk.AutoCAD.Runtime;

namespace Polymetal.MagiCAD.Client
{
    public sealed class Watcher
    {
        /*
         * В файле C:\Program Files\Autodesk\AutoCAD 2015\Support\acad2015doc.lsp
         * необходимо дописать строки:
         *      (command "_netload" "C:\\Program Files\\Autodesk\\ApplicationPlugins\\Polymetal\\Polymetal.MagiCAD.Client.dll")     //загрузка библиотеки
         *      (command "PMADDHANDLER")                                                                                            //запуск отслеживания сохранения
         *      
         * скопировать библиотеку Polymetal.MagiCAD.Client.dll в "C:\Program Files\Autodesk\ApplicationPlugins\Polymetal\"
         */
        private static string connectionString = new sqlReference.Service1().GetStrConnectPdms();
       
        private Ap.Document doc;
        [Rt.CommandMethodAttribute("PMADDHANDLER")]
        public void AddHandler()
        {
            doc = cad.DocumentManager.MdiActiveDocument;
            doc.Editor.WriteMessage($"\n[UTM] handler added");
            try
            {
                doc.Database.SaveComplete += db_SaveComplete;
            }
            catch (Exception exc)
            {
                doc.Editor.WriteMessage($"\n[UTM] Add handler error {exc.Message}");
            }
        }

        private void db_SaveComplete(object sender, Db.DatabaseIOEventArgs e)
        {
            doc = cad.DocumentManager.MdiActiveDocument;
            try
            {
                SendTrigger(e.FileName.Replace(@"\\polymetal.ru\Poly\Storage",@"K:"));
                //_exportFile = e.FileName;
                //doc.SendStringToExecute("PMEXPORTIFC\n", false, false, false);
            }
            catch (Exception exc)
            {
                doc.Editor.WriteMessage(string.Format("\n[UTM] Save complete error {0}", exc.Message));
            }
        }

        private void SendTrigger(string fileName)
        {
            using (var sqlConnection = new SqlConnection(connectionString))
            {
                sqlConnection.Open();
                decimal projectId = 0;
                var sqlCommand = "USE Aveva_Poly_Db " +
                                 "SELECT " +
                                 "   ProjectID " +
                                 "FROM " +
                                 "   ExchangeProject " +
                                 "WHERE " +
                                 "   Program = 'MagiCAD' AND " +
                                 "ModelPath LIKE '%" + fileName + "%'";
                    
                using (var sqlCmd = new SqlCommand(sqlCommand, sqlConnection))
                {
                    var reader = sqlCmd.ExecuteScalar();
                    if (reader != null)
                    {
                        projectId = decimal.Parse(reader.ToString());
                    }
                }
                if (projectId > 0)
                {
                    doc.Editor.WriteMessage(string.Format("\n[UTM] ProjectId = {0}", projectId));
                    using (var sqlCmd = new SqlCommand("SetExchangeTrigger", sqlConnection))
                    {
                        sqlCmd.Parameters.AddWithValue("@UserLogin", Environment.UserName);
                        sqlCmd.Parameters.AddWithValue("@ProjectId", projectId);
                        sqlCmd.Parameters.AddWithValue("@SourceTrigger", "MagiCAD");
                        sqlCmd.Parameters.AddWithValue("@StatusTrigger", 0);

                        sqlCmd.CommandType = CommandType.StoredProcedure;
                        sqlCmd.ExecuteNonQuery();
                    }
                }
                sqlConnection.Close();
            }
        }
    }
}
