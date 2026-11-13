using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using Polymetal.Exchange.Utilities;
using Tekla.Structures.Model;

namespace Polymetal.Exchange.Tekla.v2018
{
    internal class Import
    {
        private static Model model;

        internal static bool Run()
        {
            model = new Model();
            var needSave = InsertReferenceModels();
            return needSave;
        }

        private static bool InsertReferenceModels()
        {
            var structInfoDict = GetStructClassInfo();
            var arStructInfo = GetARStructInfo();
            var kgStructInfo = GetKGStructInfo();
            var kmStructInfo = GetKMStructInfo();
            var refModelsPath = Path.Combine(RunTekla.ProjectInfo.Program.Path, "RefModels");

            if (!Directory.Exists(refModelsPath))
            {
                Common.SendMessage(Common.Source.Tekla, RunTekla.TriggerId,
                    message: "Не найдена папка с опорными моделями");
                return false;
            }

            Common.SendMessage(Common.Source.Tekla, RunTekla.TriggerId,
                message: "Папка с опорными моделями: " + refModelsPath);

            var files = Directory.GetFiles(refModelsPath, "*.dwg", SearchOption.AllDirectories);

            //var existRefModels = new List<ReferenceModel>();
            //var existRefModelFiles = new List<string>();
            var objects =
                model.GetModelObjectSelector().GetAllObjectsWithType(ModelObject.ModelObjectEnum.REFERENCE_MODEL);
            //while (objects.MoveNext())
            //{
            //    var rm = objects.Current as ReferenceModel;
            //    if (rm == null) continue;

            //    existRefModels.Add(rm);
            //    if (rm.Filename.Contains("RefModels") && !File.Exists(rm.Filename))
            //    {
            //        rm.Delete();
            //        continue;
            //    }

            //    existRefModelFiles.Add(rm.Filename);
            //}

            while (objects.MoveNext())
            {
                var rm = objects.Current as ReferenceModel;
                if (rm == null) continue;

               // existRefModels.Add(rm);
                if (rm.Filename.Contains("RefModels"))
                {
                    rm.Delete();
                }
            }
            model.CommitChanges();

            var referenceModel = new ReferenceModel();
            var total = files.Length;
            var current = 0;
            var updatedCount = 0;
            foreach (var refFile in files)
            {
                current++;
                Console.WriteLine("Current progress: {0}/{1}   ", current, total);
                //if (existRefModelFiles.Contains(refFile.Replace(RunTekla.ProjectInfo.Program.Path + "\\", "")) ||
                //    existRefModelFiles.Contains(refFile)) continue;

                referenceModel.Filename = refFile;
                referenceModel.Position.Zero();
                referenceModel.Scale = 1.0;
                referenceModel.Visibility = ReferenceModel.VisibilityEnum.HIDDEN;
                referenceModel.Insert();
                referenceModel.SetUserProperty("Group", GetGroupName(refFile));
                referenceModel.SetUserProperty("Title",
                    GetModelTitle(refFile, structInfoDict, arStructInfo, kgStructInfo, kmStructInfo));
                updatedCount++;
            }
            Console.WriteLine();
            Common.SendMessage(Common.Source.Tekla, RunTekla.TriggerId, message: "Обновлено " + updatedCount + " файлов");
            model.CommitChanges();
            return updatedCount > 0;
        }

        private static string GetGroupName(string fileName)
        {
            var file = new FileInfo(fileName);
            var group = file.Name.Split('_').First();
            var result = group;
            switch (group)
            {
                case "OOR":
                    result = "ООР";
                    break;
                case "TO":
                    result = "ТО";
                    break;
                case "GMO":
                    result = "ГМО";
                    break;
                case "EO":
                    result = "ЭО";
                    break;
                case "SO":
                    result = "СО";
                    break;
                case "OA":
                    result = "ОА";
                    break;
                case "SANO":
                    result = "САНО";
                    break;
            }

            return result;
        }

        private static string GetModelTitle(string fileName, Dictionary<string, string> structInfoDict,
            Dictionary<string, string> arStructInfo, Dictionary<string, string> kgStructInfo,
            Dictionary<string, string> kmStructInfo)
        {
            var result = "прочее";
            var file = new FileInfo(fileName);
            var info = file.Name.Replace(file.Extension, string.Empty);
            var splitInfo = info.Split('_');
            if (splitInfo[0].Equals("SO") && splitInfo.Length > 3)
            {
                if (splitInfo[1].Equals("AR"))
                {
                    if (splitInfo[2].Equals("PL"))
                    {
                        result = arStructInfo.ContainsKey(splitInfo[2]) ? arStructInfo[splitInfo[2]] : "прочее";
                        if (splitInfo.Length > 4)
                        {
                            result += " " + splitInfo[3];
                        }
                    }
                    else
                    {
                        var arInfo = info.Replace(splitInfo[0] + "_" + splitInfo[1] + "_", string.Empty);
                        arInfo = arInfo.Substring(0, arInfo.LastIndexOf('_'));
                        result = arStructInfo.ContainsKey(arInfo) ? arStructInfo[arInfo] : "прочее";
                    }
                }
                else if (splitInfo[1].Equals("KG"))
                {
                    var kgInfo = info.Replace(splitInfo[0] + "_" + splitInfo[1] + "_", string.Empty);
                    kgInfo = kgInfo.Substring(0, kgInfo.LastIndexOf('_'));
                    result = kgStructInfo.ContainsKey(kgInfo) ? kgStructInfo[kgInfo] : "прочее";
                }
                else if (splitInfo[1].Equals("KM"))
                {
                    var kmInfo = info.Replace(splitInfo[0] + "_" + splitInfo[1] + "_", string.Empty);
                    kmInfo = kmInfo.Substring(0, kmInfo.LastIndexOf('_'));
                    result = kmStructInfo.ContainsKey(kmInfo) ? kmStructInfo[kmInfo] : "прочее";
                }
            }
            else
            {
                var otherInfo = splitInfo[splitInfo.Length - 2];
                result = structInfoDict.ContainsKey(otherInfo) ? structInfoDict[otherInfo] : "прочее";
            }
            return result;
        }

        /// <summary>
        /// Получение кодов и описания для объединения моделей
        /// </summary>
        /// <returns></returns>
        private static Dictionary<string, string> GetStructClassInfo()
        {
            var dict = new Dictionary<string, string>();
            using (var sqlConnection = new SqlConnection(Common.ConnectionString))
            {
                sqlConnection.Open();
                var sqlCommand = "USE Aveva_Poly_Db " +
                                 "SELECT DISTINCT " +
                                 "  RTRIM(UPPER(StructClassInfoCode)), " +
                                 "  RTRIM(LOWER(StructClassInfoDesc)) " +
                                 "FROM " +
                                 "  StructClassInfo";
                using (var sqlCmd = new SqlCommand(sqlCommand, sqlConnection))
                {
                    var reader = sqlCmd.ExecuteReader();
                    while (reader.Read())
                    {
                        dict.Add(reader[0].ToString(), reader[1].ToString());
                    }
                    reader.Close();
                }
                sqlConnection.Close();
            }
            return dict;
        }

        private static Dictionary<string, string> GetARStructInfo()
        {
            var dict = new Dictionary<string, string>();
            using (var sqlConnection = new SqlConnection(Common.ConnectionString))
            {
                sqlConnection.Open();
                var sqlCommand = "SELECT " +
                                 "  (RTRIM(UPPER(ARElementsCode))+RTRIM(UPPER(ISNULL('_'+ARFuncCode,'')))), " +
                                 "  RTRIM(LOWER(ARElementsDesc))+RTRIM(LOWER(ISNULL(', '+ARFuncDesc,''))) " +
                                 "FROM " +
                                 "  ARElements " +
                                 "LEFT JOIN " +
                                 "  ARFunc ON ARFunc.ARElementsID = ARElements.ARElementsID";
                using (var sqlCmd = new SqlCommand(sqlCommand, sqlConnection))
                {
                    var reader = sqlCmd.ExecuteReader();
                    while (reader.Read())
                    {
                        dict.Add(reader[0].ToString(), reader[1].ToString());
                    }
                    reader.Close();
                }
                sqlConnection.Close();
            }
            return dict;
        }

        private static Dictionary<string, string> GetKGStructInfo()
        {
            var dict = new Dictionary<string, string>();
            using (var sqlConnection = new SqlConnection(Common.ConnectionString))
            {
                sqlConnection.Open();
                var sqlCommand = "select distinct " +
                                 "  (RTRIM(UPPER(KGIdentCode)) + RTRIM(UPPER(ISNULL('_'+KGElementsIdentCode,'')))), " +
                                 "  (RTRIM(LOWER(KGIdentDesc)) + RTRIM(LOWER(ISNULL(', '+KGElementsIdentDesc,'')))) " +
                                 "FROM " +
                                 "  KGIdent " +
                                 "  left join KGElementsIdent on KGElementsIdent.KGIdentID = KGIdent.KGIdentID";
                using (var sqlCmd = new SqlCommand(sqlCommand, sqlConnection))
                {
                    var reader = sqlCmd.ExecuteReader();
                    while (reader.Read())
                    {
                        dict.Add(reader[0].ToString(), reader[1].ToString());
                    }
                    reader.Close();
                }
                sqlConnection.Close();
            }
            return dict;
        }

        private static Dictionary<string, string> GetKMStructInfo()
        {
            var dict = new Dictionary<string, string>();
            using (var sqlConnection = new SqlConnection(Common.ConnectionString))
            {
                sqlConnection.Open();
                var sqlCommand = "USE Aveva_Poly_Db " +
                                 "SELECT " +
                                 "  (RTRIM(UPPER(KMIdentCode))+'_'+RTRIM(UPPER(KMElementsIdentCode))), " +
                                 "  REPLACE(RTRIM(LOWER(KMIdentDesc))+', '+RTRIM(LOWER(KMElementsIdentDesc)),'/',' и ') " +
                                 "FROM " +
                                 "  KMIdent " +
                                 "JOIN " +
                                 "  KMElementsIdent ON KMElementsIdent.KMIdentID = KMIdent.KMIdentID";
                using (var sqlCmd = new SqlCommand(sqlCommand, sqlConnection))
                {
                    var reader = sqlCmd.ExecuteReader();
                    while (reader.Read())
                    {
                        dict.Add(reader[0].ToString(), reader[1].ToString());
                    }
                    reader.Close();
                }
                sqlConnection.Close();
            }
            return dict;
        }
    }
}