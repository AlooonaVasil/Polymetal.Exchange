using System.Data.SqlClient;
using System;
using System.Collections.Generic;
using Aveva.Core.Utilities.CommandLine;

namespace Polymetal.E3D.Ifc
{
    public enum EXTRA_ATTRIBUTES
    {
        Position = 1,
        Name = 2,
        Plant = 3,
        WeightDry = 4,
        WeightWork = 5,
        WeightTest = 6,
        WeightEnvironment = 7,
        WeightInsulation = 8,
        Note = 9,
        AttachedDocuments = 10
    }

public class ExtraAttributes
    {
        public string Place;
        public string Name;
        public string Plant;
        public string WeightEmpty;
        public string WeightWork;
        public string WeightTest;
        public string WeightMedium;
        public string WeightIsolation;
        public string Note;
        public List<string> DrawingsList = new List<string>();

        public bool IsEmpty =>
            string.IsNullOrEmpty(Place) &&
            string.IsNullOrEmpty(Name) &&
            string.IsNullOrEmpty(Plant) &&
            string.IsNullOrEmpty(WeightEmpty) &&
            string.IsNullOrEmpty(WeightWork) &&
            string.IsNullOrEmpty(WeightTest) &&
            string.IsNullOrEmpty(WeightMedium) &&
            string.IsNullOrEmpty(WeightIsolation) &&
            string.IsNullOrEmpty(Note) &&
            DrawingsList.Count == 0;

        public static bool operator == (ExtraAttributes left, ExtraAttributes right) 
        {
            if (ReferenceEquals(left, right))
                return true;

            if (ReferenceEquals(left, null) || ReferenceEquals(right, null))
                return false;

            return left.Place == right.Place &&
                   left.Name == right.Name &&
                   left.Plant == right.Plant &&
                   left.WeightEmpty == right.WeightEmpty &&
                   left.WeightWork == right.WeightWork &&
                   left.WeightTest == right.WeightTest &&
                   left.WeightMedium == right.WeightMedium &&
                   left.WeightIsolation == right.WeightIsolation &&
                   left.Note == right.Note &&
                   ListsAreEqual(left.DrawingsList, right.DrawingsList);
        }

        private static bool ListsAreEqual(List<string> a, List<string> b) 
        {
            if (a == null || b == null)
                return false;

            if (a.Count != b.Count)
                return false;

            for (int i = 0; i < a.Count; i++) 
            {
                if (a[i] != b[i])
                    return false;
            }

            return true;
        }
        
        public static bool operator != (ExtraAttributes left, ExtraAttributes right)
        {
            return !(left == right);
        }

        public static ExtraAttributes GetAllAttributesFromElement(string elementRef)
        {
            
            ExtraAttributes extraAttributes = new ExtraAttributes();

            SqlConnection sqlConnection;
            using (sqlConnection = new SqlConnection(ExportIfc.ConnectionString))
            {
                sqlConnection.Open();

                //TODO: добавить SubObjId, могут повторятся ссылки в разных проектах
                string sqlCommand = "USE Aveva_Poly_Db " +
                                    "SELECT Id, RefNumEqui, IdAttr, Value " +
                                    "FROM Task3DEquiExtraAttr " +
                                    "WHERE Activity=1 and RefNumEqui ='" + elementRef + "'";

                using (var sqlCmd = new SqlCommand(sqlCommand, sqlConnection))
                {
                    using (SqlDataReader sqlDReader = sqlCmd.ExecuteReader())
                    {
                        while (sqlDReader.Read())
                        {
                            byte idAttr = Convert.ToByte(sqlDReader["IdAttr"]);
                            string valueAttr = sqlDReader["Value"].ToString();
                            
                            switch (idAttr)
                            {
                                case (byte)EXTRA_ATTRIBUTES.Position:
                                    extraAttributes.Place = valueAttr;
                                    break;
                                case (byte)EXTRA_ATTRIBUTES.Name:
                                    extraAttributes.Name = valueAttr;
                                    break;
                                case (byte)EXTRA_ATTRIBUTES.Plant:
                                    extraAttributes.Plant = valueAttr;
                                    break;
                                case (byte)EXTRA_ATTRIBUTES.WeightDry:
                                    extraAttributes.WeightEmpty = valueAttr;
                                    break;
                                case (byte)EXTRA_ATTRIBUTES.WeightWork:
                                    extraAttributes.WeightWork = valueAttr;
                                    break;
                                case (byte)EXTRA_ATTRIBUTES.WeightTest:
                                    extraAttributes.WeightTest = valueAttr;
                                    break;
                                case (byte)EXTRA_ATTRIBUTES.WeightEnvironment:
                                    extraAttributes.WeightMedium = valueAttr;
                                    break;
                                case (byte)EXTRA_ATTRIBUTES.WeightInsulation:
                                    extraAttributes.WeightIsolation = valueAttr;
                                    break;
                                case (byte)EXTRA_ATTRIBUTES.Note:
                                    extraAttributes.Note = valueAttr;
                                    break;
                                case (byte)EXTRA_ATTRIBUTES.AttachedDocuments:
                                    extraAttributes.DrawingsList.Add(valueAttr);
                                    break;
                            }
                        }
                    }
                }
            }
            
            return extraAttributes;
        }

        public static void UpdateAtrributesByRef(string refNumEqui, EXTRA_ATTRIBUTES attr, string newValue)
        {
            SqlConnection sqlConnection;
            using (sqlConnection = new SqlConnection(ExportIfc.ConnectionString))
            {
                sqlConnection.Open();
                string sqlCommand = "UPDATE Task3DEquiExtraAttr " +
                                    "SET VALUE = @newValueValue " +
                                    "WHERE RefNumEqui = @refNumEquiValue AND IdAttr = @idAttrValue";


                using (SqlCommand sqlCmd = new SqlCommand(sqlCommand, sqlConnection))
                {
                    sqlCmd.Parameters.AddWithValue("@newValueValue", newValue);
                    sqlCmd.Parameters.AddWithValue("@refNumEquiValue", refNumEqui);
                    sqlCmd.Parameters.AddWithValue("@idAttrValue", (byte)attr);
                    sqlCmd.ExecuteNonQuery();
                }
            }
        }

        public static void AddNewExtraAttrValue(string refNum, EXTRA_ATTRIBUTES attr, string AttrValue)
        {

            if (string.IsNullOrEmpty(AttrValue))
                return;
            
            if (string.IsNullOrEmpty(AttrValue.Trim()))
                return;

            SqlConnection sqlConnection;
            using (sqlConnection = new SqlConnection(ExportIfc.ConnectionString))
            {
                sqlConnection.Open();
                
                var sqlCommand = "INSERT INTO Task3DEquiExtraAttr (RefNumEqui, IdAttr, Value, Activity) " +
                                  "VALUES (@refNumValue, @idAttrValue, @attrValueValue, 1)";

                using (SqlCommand sqlCmd = new SqlCommand(sqlCommand, sqlConnection))
                {
                    sqlCmd.Parameters.AddWithValue("@refNumValue", refNum);
                    sqlCmd.Parameters.AddWithValue("@idAttrValue", (byte)attr);
                    sqlCmd.Parameters.AddWithValue("@attrValueValue", AttrValue);
                    sqlCmd.ExecuteNonQuery();
                }
            }
        }

        public static void AddNewExtraAttrDrawingsList(string elemRef, List<string> drawingList) 
        {
            if (string.IsNullOrEmpty(elemRef))
                return;

            if (drawingList == null || drawingList.Count == 0)
                return;

            // вставить только если такой записи ещё нет
            const string query = @"INSERT INTO Task3DEquiExtraAttr (RefNumEqui, IdAttr, Value, Activity)
                                   SELECT @RefNumEqui, @IdAttr, @Value, @Activity
                                   WHERE NOT EXISTS (SELECT 1 FROM Task3DEquiExtraAttr 
                                        WHERE RefNumEqui = @RefNumEqui AND IdAttr = @IdAttr AND Value = @Value)";
                        
            using (var sqlConnection = new SqlConnection(ExportIfc.ConnectionString))
            {
                sqlConnection.Open();
                using (var transaction = sqlConnection.BeginTransaction()) 
                {
                    using (var cmd = new SqlCommand(query, sqlConnection, transaction))
                    {
                        cmd.Parameters.AddWithValue("@RefNumEqui", elemRef);
                        cmd.Parameters.AddWithValue("@IdAttr", 10);
                        cmd.Parameters.AddWithValue("@Activity", 1);
                        var valueParam = cmd.Parameters.Add("@Value", System.Data.SqlDbType.NVarChar, -1);

                        foreach (var itm in drawingList) 
                        {
                            valueParam.Value = itm ?? string.Empty;
                            cmd.ExecuteNonQuery();
                        }
                    }
                    transaction.Commit();
                }
            }
        }

        public static bool RemoveDocumentFromList(string elemRef, string document) 
        {
            if (string.IsNullOrEmpty(elemRef) || string.IsNullOrEmpty(document))
                return false;
                        
            const string query = "DELETE FROM Task3DEquiExtraAttr WHERE RefNumEqui = @elemRefValue AND IdAttr = 10 AND Value = @documentValue";

            using (var sqlConnection = new SqlConnection(ExportIfc.ConnectionString))
            {
                sqlConnection.Open();
                
                using (var cmd = new SqlCommand(query, sqlConnection))
                {
                    cmd.Parameters.AddWithValue("@elemRefValue", elemRef);
                    cmd.Parameters.AddWithValue("@documentValue", document);
                    var res = cmd.ExecuteNonQuery();

                    return res > 0;
                }
            }
        }

        public static string GetAttachedFilePath(string elemRef)
        {
            var subObject = Convert.ToDecimal(ExportIfc.AvevaPrjData.CompanyProjectData.SubObjectID);
            var res = Properties.Resources.pathToExtraAtrributesFiles + subObject;
            res += "\\" + elemRef.Replace('/', '#');
            
            return res;
        }
    }
}
