using System;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.Geometry;
using Polymetal.MagiCAD.Client.Properties;
using cad = Autodesk.AutoCAD.ApplicationServices.Application;
using Ap = Autodesk.AutoCAD.ApplicationServices;
using Db = Autodesk.AutoCAD.DatabaseServices;
using Ed = Autodesk.AutoCAD.EditorInput;
using Rt = Autodesk.AutoCAD.Runtime;
using Us = Autodesk.AutoCAD.DatabaseServices.SymbolUtilityServices;
using Hs = Autodesk.AutoCAD.DatabaseServices.HostApplicationServices;

namespace Polymetal.MagiCAD.Client
{
    public sealed class MagiCADToIfc 
    {
        private Ap.Document doc;
        private const string mcadSettingsPath = @"C:\ProgramData\MagiCAD\Configurations\MagiCADSettings.xml";
        private const string mcadIfcPath = @"\\path\MagiCAD";
        private bool registered = false;
        private static readonly string UserLogin = Environment.UserName;
        private string _exportFile = string.Empty;
        
        private static string _connectionString = new sqlReference.Service1().GetStrConnectPdms();
        //Document doc = Application.DocumentManager.MdiActiveDocument;

        //[Rt.CommandMethodAttribute("PMTEST")]
        public void PMTEST()
        {
            doc = cad.DocumentManager.MdiActiveDocument;
            
                doc.Editor.WriteMessage(string.Format("\nFile name {0}", doc.Name));

               
                var file = new FileInfo(doc.Name);
                var path = Path.Combine(file.Directory.FullName, "pdms");
                doc.Editor.WriteMessage(string.Format("\nExport IFC error {0}", path));
        }

       // [Rt.CommandMethodAttribute("PMADDHANDLER")]
        public void AddHandler()
        {
            doc = cad.DocumentManager.MdiActiveDocument;
           
            try
            {
                doc.Database.SaveComplete += db_SaveComplete;
            }
            catch (Exception exc)
            {
                doc.Editor.WriteMessage(string.Format("\nExport IFC error {0}", exc.Message));
            }
        }

       // [Rt.CommandMethodAttribute("PMATTACHPDMS")]
        public void AttachingExternalReference()
        {
            var path = @"K:\40-УТМ\MagiCAD\Проект\34-ТОО Бакырчик\02 07 002 00 - Бакырчик. Главный корпус ОФ\pdms";
            var files = Directory.GetFiles(path, "*.dwg", SearchOption.AllDirectories);
            var doc = cad.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            doc.Editor.WriteMessage(string.Format("\nStart"));
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = tr.GetObject(db.BlockTableId, Db.OpenMode.ForWrite) as Db.BlockTable;

                foreach (var objectId in bt)
                {
                    var btr = tr.GetObject(objectId, Db.OpenMode.ForWrite) as Db.BlockTableRecord;
                    if (!btr.IsLayout)
                    {
                        btr.Erase();
                    }
                }

                tr.Commit();
            }
            doc.Editor.WriteMessage(string.Format("\nBlocks deleted"));
            for (int index = 0; index < files.Length; index++)
            {
                doc.Editor.WriteMessage(string.Format("\n{0}/{1}",index+1,files.Length));
                var filePath = files[index];
                var fInfo = new FileInfo(filePath);
                var layerName = fInfo.Name.Replace(fInfo.Extension, string.Empty);

                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var lt = (Db.LayerTable) tr.GetObject(db.LayerTableId, Db.OpenMode.ForRead);
                    if (!lt.Has(layerName))
                    {
                        var ltr = new Db.LayerTableRecord
                        {
                            Name = layerName
                        };

                        if (layerName.Contains("оборудование"))
                        {
                            ltr.Color = Color.FromRgb(252, 156, 156);
                        }
                        else if (layerName.Contains("трубопроводы"))
                        {
                            ltr.Color = Color.FromRgb(82, 82, 250);
                        }
                        else if (layerName.Contains("воздуховоды"))
                        {
                            ltr.Color = Color.FromRgb(8, 231, 161);
                        }
                        else if (layerName.Contains("металлоконструкции"))
                        {
                            ltr.Color = Color.FromRgb(0, 63, 255);
                        }
                        else if (layerName.Contains("полы") || layerName.Contains("стены") ||
                                 layerName.Contains("кровля"))
                        {
                            ltr.Color = Color.FromRgb(181, 181, 172);
                        }
                        else
                        {
                            ltr.Color = Color.FromRgb(183, 0, 255);
                        }

                        lt.UpgradeOpen();
                        lt.Add(ltr);
                        tr.AddNewlyCreatedDBObject(ltr, true);
                    }

                    db.Clayer = lt[layerName];

                    var bt = tr.GetObject(db.BlockTableId, Db.OpenMode.ForRead) as Db.BlockTable;
                    var model =
                        tr.GetObject(bt[Db.BlockTableRecord.ModelSpace], Db.OpenMode.ForWrite) as Db.BlockTableRecord;

                    using (var db1 = new Db.Database(false, false))
                    {
                        db1.ReadDwgFile(fInfo.FullName, FileShare.Read, true, null);
                        var BlkId = db.Insert(fInfo.FullName, db1, false);
                        var bref = new Db.BlockReference(new Point3d(0, 0, 0), BlkId);
                        bref.SetDatabaseDefaults();
                        model.AppendEntity(bref);
                        tr.AddNewlyCreatedDBObject(bref, true);
                        tr.Commit();
                    }
                }
            }
        }

        /*[CommandMethod("EXPTOIFC")]
        public void ExportToIfc()
        {
            Database db = doc.Database;
            db.SaveComplete += db_SaveComplete;
        }

        void db_SaveComplete(object sender, DatabaseIOEventArgs e)
        {
            doc.Editor.WriteMessage("\n ***************** Event Execute");
            doc.SendStringToExecute("-MAGIIFC export\n", true, true, true);
        }*/

        #region IExtensionApplication Members

        /*public void Initialize()
        {
            doc = cad.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                cad.DocumentManager.DocumentActivated += DocActivated;
            }
            else
            {
                AddEventHandler();
            }
        }*/

        /*private void DocActivated(object sender, Ap.DocumentCollectionEventArgs e)
        {
            cad.DocumentManager.DocumentActivated -= DocActivated;
            doc = cad.DocumentManager.MdiActiveDocument;
            AddEventHandler();
        }*/

        /*private void AddEventHandler()
        {
            registered = UpdateMagiCadSettings();
            if (registered)
            {
                doc.Database.SaveComplete += db_SaveComplete;
            }
        }*/

        private void db_SaveComplete(object sender, Db.DatabaseIOEventArgs e)
        {
            doc = cad.DocumentManager.MdiActiveDocument;
            //doc.Editor.WriteMessage(string.Format("\ndb_SaveComplete: {0}", e.FileName));
            try
            {
                _exportFile = e.FileName;
                doc.SendStringToExecute("PMEXPORTIFC\n", false, false, false);
            }
            catch (Exception exc)
            {
                doc.Editor.WriteMessage(string.Format("\nExport IFC error {0}", exc.Message));
            }
        }

        //[Rt.CommandMethodAttribute("PMEXPORTIFC")]
        public void ExportIfcForPdms()
        {
            decimal projectId;
            string ifcPath;
            if (!isNeedExport(_exportFile, out projectId, out ifcPath)) return;
            doc = cad.DocumentManager.MdiActiveDocument;
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                try
                {
                    doc.Editor.Command("-MAGIIFC", "export");

                    //doc.SendStringToExecute("-MAGIIFC export\n", false, false, false);
                }
                catch (Exception exc)
                {
                    doc.Editor.WriteMessage(string.Format("\nExport IFC error {0}", exc.Message));
                }
                tr.Commit();
            }

            SendTrigger(projectId);
        }

        private void SendTrigger(decimal projectId)
        {
            using (var sqlConnection = new SqlConnection(_connectionString))
            {
                sqlConnection.Open();
                using (var sqlCmd = new SqlCommand("SetMagiCadTrigger", sqlConnection))
                {
                    sqlCmd.Parameters.AddWithValue("@UserLogin", UserLogin);
                    sqlCmd.Parameters.AddWithValue("@ProjectId", projectId);
                    sqlCmd.Parameters.AddWithValue("@SourceTrigger", "Magic");
                    sqlCmd.Parameters.AddWithValue("@StatusTrigger", 0);

                    sqlCmd.CommandType = CommandType.StoredProcedure;
                    sqlCmd.ExecuteNonQuery();
                }
                sqlConnection.Close();
            }
        }

        private bool UpdateMagiCadSettings()
        {
            if (!File.Exists(mcadSettingsPath)) return false;

            /*var xmlDoc = new XmlDocument();
            xmlDoc.Load(mcadSettingsPath);

            var settings = xmlDoc.SelectNodes("Settings");
            if (settings.Count == 0) return false;

            var propertySets = settings[0].SelectNodes("PropertySet");
            XmlNode pdmsPropertySet = null;
            foreach (XmlNode propertySet in propertySets)
            {
                var attributes = propertySet.Attributes;
                if (attributes.Cast<XmlAttribute>().Any(attribute => attribute.Name != "Name" && attribute.Value != "Pdms_All"))
                {
                    pdmsPropertySet = propertySet;
                }
            }

            if (pdmsPropertySet == null)
            {
                
            }*/
            var xmlDoc = XDocument.Load(mcadSettingsPath);
            XElement pdmsSet = null;
            foreach (var setElement in xmlDoc.Root.Elements())
            {
                if (!setElement.Name.LocalName.Equals("PropertySet")) continue;

                var nameAttribute = setElement.Attributes("Name").Any() ? setElement.Attributes("Name").First() : null;
                if (nameAttribute == null || !nameAttribute.Value.Equals("Pdms_All")) continue;

                pdmsSet = setElement;
                break;
            }

            if (pdmsSet != null)
            {
                return true;
            }
            else
            {
                /*var setId = 0;
                foreach (var setElement in xmlDoc.Root.Elements())
                {
                    if (!setElement.Name.LocalName.Equals("PropertySet")) continue;
                    var idAttribute = setElement.Attributes("id").Any() ? setElement.Attributes("id").First() : null;
                    int currentId;
                    if (idAttribute == null || !int.TryParse(idAttribute.Value, out currentId)) continue;
                    setId = Math.Max(setId, currentId);
                }
                pdmsSet = new XElement("PropertySet");
                pdmsSet.SetAttributeValue("PropertiesType", "1");
                pdmsSet.SetAttributeValue("context", "1");
                pdmsSet.SetAttributeValue("discipline", "1");
                pdmsSet.SetAttributeValue("id", setId + 1);
                pdmsSet.SetAttributeValue("Name", "Pdms_All");
                pdmsSet.SetAttributeValue("AppliesToAllPartTypes", "1");
                pdmsSet.SetAttributeValue("Description", "");
                pdmsSet.SetAttributeValue("AppliesToInstance", "1");
                pdmsSet.SetAttributeValue("typeid", "1");
                xmlDoc.Root.Add(pdmsSet);

                xmlDoc.Save(mcadSettingsPath);*/
            }

            return false;
        }

        private bool isNeedExport(string fileName, out decimal projectId, out string ifcName)
        {
            var need = false;
            projectId = 0;
            ifcName = string.Empty;
            using (var sqlConnection = new SqlConnection(_connectionString))
            {
                sqlConnection.Open();
                var sqlCommand =
                    string.Format(
                        "USE Aveva_Poly_Db SELECT MagiCadProjectID, MagiCadIfcName FROM MagiCadProject WHERE MagiCadFilePath LIKE '{0}' AND NeedExchange = 1",
                        fileName);
                using (var sqlCmd = new SqlCommand(sqlCommand, sqlConnection))
                {
                    var reader = sqlCmd.ExecuteReader();
                    if (reader.Read())
                    {
                        projectId = decimal.Parse(reader["MagiCadProjectID"].ToString());
                        ifcName = Path.Combine(mcadIfcPath, reader["MagiCadIfcName"].ToString());
                        need = true;
                    }
                }
                sqlConnection.Close();
            }
            return need;
        }

        /*public void Terminate()
        {
            if (registered)
                doc.Database.SaveComplete -= db_SaveComplete;
        }*/

        #endregion
    }
}
