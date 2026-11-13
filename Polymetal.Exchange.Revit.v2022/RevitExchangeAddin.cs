using System;
using System.Collections.Generic;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using System.IO;
using System.Linq;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI.Events;
using Progman.MagiCAD;
using System.Text;
using Autodesk.Revit.DB.Architecture;
using System.Data.SqlClient;
using Polymetal.Exchange.Utilities;
using System.Globalization;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.DB.Mechanical;

namespace Polymetal.Exchange.Revit.v2022
{
    internal class RevitExchangeAddin : IExternalApplication
    {
        private string _filePath;
        private string _exchangeType;
        private string _ifcPath;
        private string _source;
        private decimal _subObjectId;
        private UIApplication _uiApplication;

        private static Dictionary<ElementId, bool> _referencesDict;

        public Result OnStartup(UIControlledApplication a)
        {
            //Environment.SetEnvironmentVariable("PLMT_EXCHANGE",
            //    string.Format($"" +
            //    $"{@"K:\40-УТМ\MagiCAD\Тестовый проект\central.rvt"}#" +
            //    $"export#" +
            //    $"{Path.Combine(@"C:\AVEVA\temp\Revit", "test.ifc")}#" +
            //    $"MagiCAD#" +
            //    $"0"));

            var plmtExchange = Environment.GetEnvironmentVariable("PLMT_EXCHANGE");
            if (plmtExchange == null)
                return Result.Failed;
            var taskInfo = plmtExchange.Split('#');

            _filePath = taskInfo.Length >= 1 ? taskInfo[0] : string.Empty;

            _referencesDict = new Dictionary<ElementId, bool>();
            _exchangeType = taskInfo.Length >= 2 ? taskInfo[1] : string.Empty;
            _ifcPath = taskInfo.Length >= 3 ? taskInfo[2] : string.Empty;
            _source = taskInfo.Length >= 4 ? taskInfo[3].ToLower() : string.Empty;
            _subObjectId = taskInfo.Length >= 5 ? decimal.Parse(taskInfo[4].ToLower()) : 0;

            a.ControlledApplication.ApplicationInitialized += OnApplicationInitialized;
            return Result.Succeeded;
        }

        private void DisableLinckedReferences(string path)
        {
            var modelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(path);
            var transmissionData = TransmissionData.ReadTransmissionData(modelPath);
            if (transmissionData != null)
            {
                var externalReferences = transmissionData.GetAllExternalFileReferenceIds();

                foreach (ElementId refId in externalReferences)
                {
                    var extRef = transmissionData.GetLastSavedReferenceData(refId);

                    if (extRef.ExternalFileReferenceType != ExternalFileReferenceType.RevitLink) continue;
                    _referencesDict.Add(refId, extRef.GetLinkedFileStatus() == LinkedFileStatus.Loaded);
                    transmissionData.SetDesiredReferenceData(refId, extRef.GetPath(), extRef.PathType, false);
                }

                transmissionData.IsTransmitted = true;
                TransmissionData.WriteTransmissionData(modelPath, transmissionData);
            }
        }

        protected void OnApplicationInitialized(object sender, ApplicationInitializedEventArgs e)
        {
            if (string.IsNullOrEmpty(_filePath) || string.IsNullOrEmpty(_exchangeType) || string.IsNullOrEmpty(_ifcPath) || string.IsNullOrEmpty(_source))
                return;

            var app = sender as Application;
            app.FailuresProcessing += App_FailuresProcessing;
            _uiApplication = new UIApplication(app);
            _uiApplication.DialogBoxShowing += Revit_DialogBoxShowing;

            var file = new FileInfo(_filePath);
            var tempFile = new FileInfo(@"c:\");
            var tryCounter = 0;
            while (tryCounter < 10)
            {
                try
                {
                    tempFile = file.CopyTo(Path.Combine(Path.GetTempPath(), "tmp.rvt"), true);
                    break;
                }
                catch
                {
                    tryCounter++;
                    Console.WriteLine("attempt to copy a file, try number {0}", tryCounter);
                }
            }

            if (!tempFile.Exists) return;
            DisableLinckedReferences(tempFile.FullName);
            var openOptions = new OpenOptions
            {
                DetachFromCentralOption = DetachFromCentralOption.DetachAndPreserveWorksets
            };
            var uiDoc = _uiApplication.OpenAndActivateDocument(ModelPathUtils.ConvertUserVisiblePathToModelPath(tempFile.FullName), openOptions, true);

            if (_uiApplication.ActiveUIDocument == null) return;
            var document = uiDoc.Document;
            if (_exchangeType.Equals("export"))
            {
                if (_source == "revit")
                {
                    var ifcExportOptions = new IFCExportOptions();
                    using (var transaction = new Transaction(document))
                    {
                        transaction.Start("Export to ifc");
                        var ifcFile = new FileInfo(_ifcPath);
                        document.Export(ifcFile.DirectoryName, ifcFile.Name, ifcExportOptions);
                        transaction.RollBack();
                    }

                    if (_subObjectId > 0)
                    {
                        var attributesDictionary = GetAttributesForRoom();
                        var collection = GetAllModelElements(document);
                        var levels = GetAllModelLevels(document);
                        var existingRooms = GetExistingRooms();
                        foreach (var item in collection)
                        {
                            if (!(item is Room)) continue;
                            var room = item as Room;

                            var properties = new Dictionary<int, string>();
                            if (attributesDictionary.ContainsKey("ROOM_NUMBER"))
                            {
                                properties.Add(
                                    attributesDictionary["ROOM_NUMBER"],
                                    room.Number);
                            }
                            if (attributesDictionary.ContainsKey("ROOM_NAME"))
                            {
                                properties.Add(
                                    attributesDictionary["ROOM_NAME"],
                                    room.get_Parameter(BuiltInParameter.ROOM_NAME).AsString());
                            }
                            if (attributesDictionary.ContainsKey("ROOM_AREA"))
                            {
                                properties.Add(
                                    attributesDictionary["ROOM_AREA"],
                                    (room.Area * Helpers.Constants.FACTOR_AREA).ToString("0.0000", CultureInfo.InvariantCulture));
                            }
                            if (attributesDictionary.ContainsKey("ROOM_VOLUME"))
                            {
                                properties.Add(
                                    attributesDictionary["ROOM_VOLUME"],
                                    (room.Area * room.get_Parameter(BuiltInParameter.ROOM_HEIGHT).AsDouble() * Helpers.Constants.FACTOR_VOLUME).ToString("0.0000", CultureInfo.InvariantCulture));
                            }
                            if (attributesDictionary.ContainsKey("Категория помещения".ToUpper()))
                            {
                                var categoryParam = room.LookupParameter("Категория помещения");
                                properties.Add(
                                    attributesDictionary["Категория помещения".ToUpper()],
                                    categoryParam == null ? string.Empty : categoryParam.AsString());
                            }

                            if (existingRooms.ContainsKey(room.UniqueId))
                            {
                                existingRooms[room.UniqueId].Activity = true;
                                existingRooms[room.UniqueId].Properties = properties;
                            }
                            else
                            {
                                existingRooms.Add(room.UniqueId, new RevitRoom(room.UniqueId, 0, true, properties));
                            }
                        }
                        UpdateRooms(existingRooms.Values.ToList());

                        WriteElementArchitectureInfo(_ifcPath.Replace(".ifc", ".info"), collection, levels);
                    }
                }
                else if (_source == "magicad")
                {
                    try
                    {
                        var collector = new FilteredElementCollector(document);
                        var viewFamilyType = collector.OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>().FirstOrDefault(x => x.ViewFamily == ViewFamily.ThreeDimensional);

                        var view = default(View3D);
                        using (Transaction tr = new Transaction(document, "create view"))
                        {
                            tr.Start();
                            view = View3D.CreateIsometric(document, viewFamilyType.Id);
                            view.Name = DateTime.Now.Ticks.ToString();
                            SetAllVisible(document, view);
                            tr.Commit();
                        }

                        if (view != null)
                        {
                            //var t = new CRevIFCExportConfigurations();
                            //CRevIfcBatchExport.instance.loadExportConfigurations(document, ref t);

                            var newConfig = new CRevIFCExportConfiguration();
                            newConfig.name = "exportConfig";
                            newConfig.action = 2;
                            newConfig.configurations = new List<int> { 1 };
                            newConfig.entireProject = true;
                            newConfig.fileFormat = "ifc";
                            newConfig.originType = 0;
                            newConfig.isActive = true;
                            newConfig.schemaId = 1;
                            newConfig.path = _ifcPath;
                            newConfig.userConfig = "0;0:;1:;2:;";
                            newConfig.view = view.Id.IntegerValue;

                            var storeyMappings = new List<CRevIFCStoreyMapping>();

                            var levels = GetAllModelLevels(document);
                            foreach (var level in levels)
                            {
                                var elevParameter = level.get_Parameter(BuiltInParameter.LEVEL_ELEV);
                                storeyMappings.Add(new CRevIFCStoreyMapping
                                {
                                    ifcGuid = level.UniqueId,
                                    ifcName = level.Name,
                                    ifcPosX = 0.0,
                                    ifcPosY = 0.0,
                                    ifcPosZ = elevParameter.AsDouble() * 304.8,
                                    platformGuid = level.UniqueId,
                                    platformName = level.Name,
                                    platformPosX = 0.0,
                                    platformPosY = 0.0,
                                    platformPosZ = elevParameter.AsDouble() * 304.8
                                });
                            }

                            newConfig.storeyMappings = storeyMappings;

                            var cRevIfcExporter = new CRevIfcExporter(app, document);
                            cRevIfcExporter.doExport(newConfig);

                            if (File.Exists(_ifcPath)) WriteElementInfo(document, _ifcPath.Replace(".ifc", ".info"));
                            else File.Create(_ifcPath);
                        }
                        else
                        {
                            TaskDialog.Show("Exchange", "Export failed view is null");
                        }
                    }
                    catch
                    {
                        TaskDialog.Show("Exchange", "Export failed");
                    }
                }
            }
            else if (_exchangeType.Equals("import"))
            {
                TaskDialog.Show("Exchange", "Import");
            }

            CloseRevit();
        }

        private void CloseRevit()
        {
            var exitCommandId = RevitCommandId.LookupPostableCommandId(PostableCommand.ExitRevit);
            if (_uiApplication.CanPostCommand(exitCommandId))
            {
                _uiApplication.PostCommand(exitCommandId);
            }
        }

        private void WriteElementArchitectureInfo(string path, List<Element> collection, List<Level> levels)
        {
            var tagInfoDict = new Dictionary<int, string>();
            foreach (var item in collection)
            {
                var category = item.Category;
                if (category == null) continue;
                if (category.CategoryType != CategoryType.Model) continue;

                var sb = new StringBuilder();
                sb.Append($"#id#{item.UniqueId}");

                var family = item.Document.GetElement(item.GetTypeId());
                var description = category.Name;
                if (family != null)
                {
                    var pmiDescParam = family.LookupParameter(Helpers.Attributes.PMI_DESCRIPTION);
                    description = pmiDescParam != null && pmiDescParam.HasValue ? pmiDescParam.AsString() : family.Name;
                }

                sb.Append($"#description#{description.Replace(Environment.NewLine, " ")}");

                var elevation = 0.0;
                if (item.LevelId.IntegerValue < 0)
                {
                    var levelParameter = item.LookupParameter("Базовый уровень");
                    if (levelParameter != null && levelParameter.HasValue)
                    {
                        var levelId = levelParameter.AsElementId();
                        var level = levels.FirstOrDefault(x => x.Id == levelId && levelId.IntegerValue >= 0);
                        elevation = level == null ? 0.0 : Math.Round(level.Elevation * Helpers.Constants.FACTOR_LENGTH);
                    }
                }
                else
                {
                    var level = levels.FirstOrDefault(x => x.Id == item.LevelId);
                    elevation = level == null ? 0.0 : Math.Round(level.Elevation * Helpers.Constants.FACTOR_LENGTH);
                    var offsetParam = item.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM);
                    if (offsetParam != null && offsetParam.HasValue)
                    {
                        var offset = offsetParam.AsDouble();
                        elevation += Math.Round(offset * Helpers.Constants.FACTOR_LENGTH);
                    }
                }
                sb.Append($"#elevation#{elevation}");

                var func = string.Empty;
                if (item.Category.Name == "Пожарная сигнализация")
                {
                    func = "WALK";
                }
                else if (family != null)
                {
                    var funcParam = family.get_Parameter(BuiltInParameter.FUNCTION_PARAM);

                    if (funcParam != null && funcParam.HasValue)
                    {
                        var value = funcParam.AsValueString();
                        value = value.Replace("Наружные слои", "N").Replace("Внутренние слои", "V");
                        if (value == "N" || value == "V")
                        {
                            func = value;
                        }
                    }
                }
                sb.Append($"#function#{func}");

                var zoneParam = item.LookupParameter(Helpers.Attributes.AVEVA_ZONE);
                var avevaZone = zoneParam != null && zoneParam.HasValue ? zoneParam.AsString() : string.Empty;
                sb.Append($"#zone#{avevaZone}");

                var supportingParameter = item.LookupParameter("Несущие конструкции");
                var supporting = false;
                if (supportingParameter != null)
                {
                    supporting = supportingParameter.AsInteger() != 0;
                }
                sb.Append($"#supporting#{supporting}");

                var ignoreParam = item.LookupParameter(Helpers.Attributes.NEED_IN_AVEVA);
                var ignore = ignoreParam != null && ignoreParam.HasValue && ignoreParam.AsInteger() == 0;
                sb.Append($"#ignore#{ignore}");

                tagInfoDict.Add(item.Id.IntegerValue, sb.ToString());

                if (!(item is Room)) continue;
            }

            using (var sw = new StreamWriter(path))
            {
                foreach (var pair in tagInfoDict) sw.WriteLine($"{pair.Key}{pair.Value}");
            }
        }

        private Dictionary<string, int> GetAttributesForRoom()
        {
            var attributesDictionary = new Dictionary<string, int>();
            using (var sqlConnection = new SqlConnection(Common.ConnectionString))
            {
                sqlConnection.Open();
                if (sqlConnection.State != System.Data.ConnectionState.Open) return attributesDictionary;

                var sqlCommand = "select AttributeId, AttributeName from e_Attributes where AttributeSource = 3";
                using (var sqlCmd = new SqlCommand(sqlCommand, sqlConnection))
                {
                    using (var reader = sqlCmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var attName = reader["AttributeName"].ToString().ToUpper();
                            if (attributesDictionary.ContainsKey(attName)) continue;
                            attributesDictionary.Add(attName, (int)reader["AttributeId"]);
                        }
                    }
                }
            }
            return attributesDictionary;
        }

        private Dictionary<string, RevitRoom> GetExistingRooms()
        {
            var result = new Dictionary<string, RevitRoom>();
            using (var sqlConnection = new SqlConnection(Common.ConnectionString))
            {
                sqlConnection.Open();
                if (sqlConnection.State != System.Data.ConnectionState.Open) return result;

                var sqlCommand = "select RoomId, RoomGuid, Activity from e_Rooms where SubObjectId = " + _subObjectId;
                using (var sqlCmd = new SqlCommand(sqlCommand, sqlConnection))
                {
                    using (var reader = sqlCmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            result.Add((string)reader["RoomGuid"], new RevitRoom((string)reader["RoomGuid"], (int)reader["RoomId"], (bool)reader["Activity"], null));
                        }
                    }
                }
            }
            return result;
        }

        private void UpdateRooms(List<RevitRoom> rooms)
        {
            using (var sqlConnection = new SqlConnection(Common.ConnectionString))
            {
                sqlConnection.Open();
                if (sqlConnection.State != System.Data.ConnectionState.Open) return;

                foreach (var room in rooms.Where(x => x.Id == 0))
                {
                    var sqlCommand = string.Format($"insert into e_Rooms VALUES({_subObjectId},'{room.Guid}',1) select SCOPE_IDENTITY()");
                    using (var sqlCmd = new SqlCommand(sqlCommand, sqlConnection))
                    {
                        room.Id = Convert.ToInt32(sqlCmd.ExecuteScalar());
                    }
                }

                foreach (var room in rooms.Where(x => x.Properties == null && x.Activity))
                {
                    var sqlCommand = string.Format($"update e_Rooms set Activity = 0 where RoomId = {room.Id}");
                    using (var sqlCmd = new SqlCommand(sqlCommand, sqlConnection))
                    {
                        sqlCmd.ExecuteNonQuery();
                        room.Activity = false;
                    }
                }

                foreach (var room in rooms.Where(x => x.Activity))
                {
                    foreach (var attribute in room.Properties)
                    {
                        var sqlCommand = string.Format(
                            $"update e_RoomAttributeValue set " +
                            $"AttributeValue = '{attribute.Value}', Activity = 1 " +
                            $"where RoomId = {room.Id} and AttributeId = {attribute.Key} " +
                            $"IF @@ROWCOUNT = 0 " +
                            $"insert into e_RoomAttributeValue VALUES (" +
                            $"{room.Id}, " +
                            $"{attribute.Key}, " +
                            $"'{attribute.Value}', " +
                            $"1)");
                        using (var sqlCmd = new SqlCommand(sqlCommand, sqlConnection))
                        {
                            sqlCmd.ExecuteNonQuery();
                        }
                    }
                }
            }
        }

        private void WriteElementInfo(Document document, string path)
        {
            var elementList = GetAllModelElements(document);
            var sb = new StringBuilder();

            var invalidChars = Regex.Escape(new string(Path.GetInvalidFileNameChars()));
            var invalidRegStr = string.Format(@"([{0}]*\.+$)|([{0}]+)", invalidChars);

            Helpers.MagiCadInfo.SystemsDictionary = new Dictionary<string, string>();
            var collector = new FilteredElementCollector(document);
            collector.OfClass(typeof(MEPSystem)).Cast<MEPSystem>();
            foreach (var mepSystem in collector)
            {
                var typeSystem = document.GetElement(mepSystem.GetTypeId());
                if (Helpers.MagiCadInfo.SystemsDictionary.ContainsKey(mepSystem.Name)) continue;
                var mepSystemName = mepSystem.Name.Split(' ').First();
                mepSystemName = Regex.Replace(mepSystemName, invalidRegStr, string.Empty);
                Helpers.MagiCadInfo.SystemsDictionary.Add(mepSystem.Name, $"{typeSystem.Name.Split(' ').First()}#{mepSystemName}".ToUpper());
            }

            foreach (var element in elementList)
            {
                if (element.Category.CategoryType != CategoryType.Model) continue;
                Helpers.MagiCadInfo info = null;
                if (element is Duct)
                {
                    info = new Helpers.DuctInfo(element);
                }
                else if (element is FlexDuct)
                {
                    info = new Helpers.FlexDuctInfo(element);
                }
                else if (element is Pipe)
                {
                    info = new Helpers.PipeInfo(element);
                }
                else if (element is FlexPipe)
                {
                    info = new Helpers.FlexPipeInfo(element);
                }
                else if (element is FamilyInstance)
                {
                    if (element.Name.StartsWith("УКВ-") || element.Name.StartsWith("УКТ-"))
                    {
                        info = new Helpers.SupportInfo(element);
                    }
                    else if (element.Category.Name == "Арматура воздуховодов")
                    {
                        info = new Helpers.DuctValveInfo(element);
                    }
                    else if (element.Category.Name == "Арматура трубопроводов")
                    {
                        info = new Helpers.PipeValveInfo(element);
                    }
                    else if (element.Category.Name == "Воздухораспределители")
                    {
                        info = new Helpers.DuctDiffusersInfo(element);
                    }
                    else if (element.Category.Name == "Обобщенные модели")
                    {
                        if (element.Name.Contains("ПМИ_Зона_резервирования"))
                        {
                            info = new Helpers.ReserveInfo(element);
                        }
                        else
                        {
                            info = new Helpers.CommonModelInfo(element);
                        }
                    }
                    else if (element.Category.Name == "Оборудование")
                    {
                        info = new Helpers.EquipmentInfo(element);
                    }
                    else if (element.Category.Name == "Соединительные детали воздуховодов")
                    {
                        info = new Helpers.DuctConnectingPartInfo(element);
                    }
                    else if (element.Category.Name == "Соединительные детали трубопроводов")
                    {
                        info = new Helpers.PipeConnectingPartInfo(element);
                    }
                }

                if (info == null) continue;

                sb.Append($"{info.Id}");
                sb.Append($"#Name#{FixStringValue(info.Name)}");
                sb.Append($"#Description#{FixStringValue(info.Description)}");
                sb.Append($"#:EquiType#{FixStringValue(info.Sign)}");
                sb.Append($"#:EquiPlant#{FixStringValue(info.Plant)}");
                sb.Append($"#:EquiWeight#{(info.Weight == 0.0 ? string.Empty : info.Weight.ToString())}");
                sb.Append($"#:EquiUnit#{info.Unit}");
                sb.Append($"#:EquiCount#{info.Count}");
                sb.Append($"#ElementType#{info.ElementType}");
                sb.Append($"#System#{info.System}");
                sb.Append($"#UniqueId#{info.UniqueId}");
                sb.Append($"#CreatedBy#{info.CreatedBy}");
                sb.Append($"#ModifiedBy#{info.ModifiedBy}");
                if (info.Ignore)
                {
                    sb.Append("#ignore#true");
                }
                if (info.ConnectedToList.Count > 0)
                {
                    sb.Append($"#Connected#{string.Join(" ", info.ConnectedToList)}");
                }

                sb.AppendLine();
            }

            using (var sw = new StreamWriter(path)) sw.Write(sb);
        }

        private string FixStringValue(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;
            var output = string.Empty;
            input = input.Replace("Ø", "~D");
            input = input.Replace("δ", "~dlt");
            input = input.Replace("«", "\"");
            input = input.Replace("»", "\"");
            for (var i = 0; i < input.Length; i++)
            {
                var c = input[i];
                if ((c >= 32 && c <= 126) || (c >= 1040 && c <= 1103) || c == 8470 || c == 8211)
                    output += c;
                else
                    output += "?";
            }
            return output;
        }

        private List<Level> GetAllModelLevels(Document document)
        {
            var elements = new List<Level>();
            var collector = new FilteredElementCollector(document);
            collector.OfClass(typeof(Level)).Cast<Level>();

            foreach (var element in collector)
            {
                elements.Add((Level)element);
            }
            return elements;
        }

        private List<Room> GetAllModelRooms(Document document)
        {
            var elements = new List<Room>();
            var collector = new FilteredElementCollector(document);
            collector.OfClass(typeof(Room)).Cast<Room>();

            foreach (var element in collector)
            {
                elements.Add((Room)element);
            }
            return elements;
        }

        private List<Element> GetAllModelElements(Document doc)
        {
            List<Element> elements = new List<Element>();

            FilteredElementCollector collector = new FilteredElementCollector(doc).WhereElementIsNotElementType();

            foreach (Element e in collector)
            {
                if (e.Category != null)
                {
                    elements.Add(e);
                }
            }
            return elements;
        }

        private void SetAllVisible(Document document, View3D view)
        {
            var elements = new List<ElementId>();
            var collector = new FilteredElementCollector(document).WhereElementIsNotElementType();
            foreach (Element element in collector)
            {
                var category = element.Category;
                if (category == null || !category.HasMaterialQuantities || !element.IsHidden(view)) continue;
                elements.Add(element.Id);
            }

            foreach (Category category in document.Settings.Categories)
            {
                if (!view.GetCategoryHidden(category.Id) || category.CategoryType != CategoryType.Model) continue;
                view.SetCategoryHidden(category.Id, false);
            }

            if (elements.Count > 0) view.UnhideElements(elements);
        }

        private void Revit_DialogBoxShowing(object sender, DialogBoxShowingEventArgs e)
        {
            /*
            TaskDialogResult
            None = 0,
            Ok = 1,
            Cancel = 2,
            Retry = 4,
            Yes = 6,
            No = 7,
            Close = 8,
            CommandLink1 = 1001,
            CommandLink2 = 1002,
            CommandLink3 = 1003,
            CommandLink4 = 1004
            */
            switch (e.DialogId)
            {
                case "TaskDialog_Unresolved_References":
                    e.OverrideResult((int)TaskDialogResult.CommandLink2);
                    break;
                case "TaskDialog_Update_Resources":
                    e.OverrideResult((int)TaskDialogResult.CommandLink1);
                    break;
                case "TaskDialog_Missing_Third_Party_Updaters":
                    e.OverrideResult((int)TaskDialogResult.CommandLink2);
                    break;
                case "TaskDialog_Missing_Third_Party_Updater":
                    e.OverrideResult((int)TaskDialogResult.CommandLink2);
                    break;
                case "TaskDialog_Local_Changes_Not_Synchronized_With_Central":
                    e.OverrideResult((int)TaskDialogResult.CommandLink2);
                    break;
                case "TaskDialog_Default_Family_Template_File_Invalid":
                    e.OverrideResult((int)TaskDialogResult.Close);
                    break;
                case "TaskDialog_Save_File":
                    e.OverrideResult((int)TaskDialogResult.No);
                    break;
                case "TaskDialog_Calculation_In_Progress":
                    e.OverrideResult((int)TaskDialogResult.Close);
                    CloseRevit();
                    break;
            }
        }

        private void App_FailuresProcessing(object sender, FailuresProcessingEventArgs e)
        {
            var failuresAccessor = e.GetFailuresAccessor();

            var failureMessageAccessorList = failuresAccessor.GetFailureMessages();
            if (failureMessageAccessorList.Count > 0)
            {
                FailureSeverity fseverity = failuresAccessor.GetSeverity();

                foreach (FailureMessageAccessor failureMessageAccessor in failureMessageAccessorList)
                {
                    if (fseverity == FailureSeverity.Warning)
                    {
                        failuresAccessor.DeleteWarning(failureMessageAccessor);
                    }
                    else if (failuresAccessor.IsFailureResolutionPermitted(failureMessageAccessor, FailureResolutionType.DeleteElements))
                    {
                        failureMessageAccessor.SetCurrentResolutionType(FailureResolutionType.DeleteElements);
                        failuresAccessor.ResolveFailure(failureMessageAccessor);
                        e.SetProcessingResult(FailureProcessingResult.ProceedWithCommit);
                    }
                }
            }
        }

        public Result OnShutdown(UIControlledApplication a)
        {
            return Result.Succeeded;
        }
    }

    internal class RevitRoom
    {
        internal string Guid;
        internal int Id;
        internal bool Activity;
        internal Dictionary<int, string> Properties;

        internal RevitRoom(string guid, int id, bool activity, Dictionary<int, string> properties)
        {
            Guid = guid;
            Id = id;
            Activity = activity;
            Properties = properties;
        }
    }
}
