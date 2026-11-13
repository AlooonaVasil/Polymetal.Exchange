using System;
using System.Collections.Generic;
using System.Globalization;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using System.IO;
using System.Linq;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI.Events;
using Progman.MagiCAD;
using Autodesk.Revit.DB.Architecture;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Serialization;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.DB.Mechanical;
using Polymetal.Exchange.Utilities.Helpers;

namespace Polymetal.Exchange.Revit.v2023
{
    internal class RevitExchangeAddin : IExternalApplication
    {
        private string _filePath;
        private string _exchangeType;
        private string _ifcPath;
        private string _source;
        private int _subObjectId;
        private UIApplication _uiApplication;

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

            Console.WriteLine($"RVT_TEMP_PATH {Path.GetTempPath()}");

            var taskInfo = plmtExchange.Split('#');

            _filePath = taskInfo.Length >= 1 ? taskInfo[0] : string.Empty;

            _exchangeType = taskInfo.Length >= 2 ? taskInfo[1] : string.Empty;
            _ifcPath = taskInfo.Length >= 3 ? taskInfo[2] : string.Empty;
            _source = taskInfo.Length >= 4 ? taskInfo[3].ToLower() : string.Empty;
            _subObjectId = taskInfo.Length >= 5 ? int.Parse(taskInfo[4].ToLower()) : 0;

            a.ControlledApplication.ApplicationInitialized += OnApplicationInitialized;

            return Result.Succeeded;
        }

        protected void OnApplicationInitialized(object sender, ApplicationInitializedEventArgs e)
        {
            if (string.IsNullOrEmpty(_filePath) || _exchangeType != "export" || string.IsNullOrEmpty(_ifcPath) ||
                string.IsNullOrEmpty(_source))
            {
                return;
            }

            if (!(sender is Application application))
                return;

            application.FailuresProcessing += App_FailuresProcessing;

            _uiApplication = new UIApplication(application);
            _uiApplication.DialogBoxShowing += Revit_DialogBoxShowing;

            var file = new FileInfo(_filePath);

            FileInfo tempFileInfo = null;
            var tryCounter = 0;
            while (tryCounter < 10)
            {
                try
                {
                    tempFileInfo = file.CopyTo(Path.Combine(Path.GetTempPath(), $"temp_{_source}_{_subObjectId}.rvt"), true);
                    break;
                }
                catch
                {
                    tryCounter++;
                    Console.WriteLine("attempt to copy a file, try number {0}", tryCounter);
                }
            }

            if (tempFileInfo == null || !tempFileInfo.Exists)
                return;

            var modelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(tempFileInfo.FullName);

            DisableLinkedReferences(modelPath);

            var openOptions = new OpenOptions
            {
                DetachFromCentralOption = DetachFromCentralOption.DetachAndPreserveWorksets
            };

            var uiDocument = _uiApplication.OpenAndActivateDocument(modelPath, openOptions, true);

            if (_uiApplication.ActiveUIDocument == null)
                return;

            if (_source == "revit")
            {
                try
                {
                    ExportArchitecture(uiDocument.Document);
                }
                catch (Exception exc)
                {
                    TaskDialog.Show("Polymetal.Exchange.Revit.2023", exc.Message);
                }
            }
            else if (_source == "magicad")
            {
                try
                {
                    ExportDuct(application, uiDocument.Document);
                }
                catch (Exception exc)
                {
                    TaskDialog.Show("Polymetal.Exchange.Revit.2023", exc.Message);
                }
            }

            CloseRevit();
        }

        /// <summary>
        /// Закрытие программы
        /// </summary>
        private void CloseRevit()
        {
            var exitCommandId = RevitCommandId.LookupPostableCommandId(PostableCommand.ExitRevit);
            if (!_uiApplication.CanPostCommand(exitCommandId))
                return;

            _uiApplication.PostCommand(exitCommandId);
        }

        /// <summary>
        /// Отключение присоединенных внешних файлов
        /// </summary>
        /// <param name="path"></param>
        private void DisableLinkedReferences(ModelPath modelPath)
        {
            var transmissionData = TransmissionData.ReadTransmissionData(modelPath);
            if (transmissionData == null)
                return;

            var externalReferences = transmissionData.GetAllExternalFileReferenceIds();

            foreach (var externalElementId in externalReferences)
            {
                var externalFileReference = transmissionData.GetLastSavedReferenceData(externalElementId);

                if (externalFileReference.ExternalFileReferenceType != ExternalFileReferenceType.RevitLink)
                    continue;

                transmissionData.SetDesiredReferenceData(externalElementId, externalFileReference.GetPath(),
                    externalFileReference.PathType, false);
            }

            transmissionData.IsTransmitted = true;
            TransmissionData.WriteTransmissionData(modelPath, transmissionData);
        }

        /// <summary>
        /// Экспорт архитектурных данных
        /// </summary>
        /// <param name="document"></param>
        private void ExportArchitecture(Document document)
        {
            var ifcExportOptions = new IFCExportOptions();
            using (var transaction = new Transaction(document))
            {
                transaction.Start("Export to ifc");
                var ifcFile = new FileInfo(_ifcPath);
                document.Export(ifcFile.DirectoryName, ifcFile.Name, ifcExportOptions);
                transaction.RollBack();
            }

            //запись необходимой дополнительной информации для элементов
            WriteElementArchitectureInfo(document, _ifcPath.Replace(".ifc", ".info"));
        }

        /// <summary>
        /// Запись вспомогательных данных для архитектурных элементов в файл
        /// </summary>
        /// <param name="document"></param>
        /// <param name="path"></param>
        private void WriteElementArchitectureInfo(Document document, string path)
        {
            var collection = GetModelElements(document);
            var levels = GetLevelElements(document);

            var elementInformationList = new List<Utilities.DTO.IfcElementInformation>();
            foreach (var item in collection)
            {
                var category = item.Category;
                if (category.CategoryType != CategoryType.Model)
                    continue;

                if (item is Room room)
                {
                    var roomInformation = GetRoomInformation(room);
                    if (roomInformation != null)
                        elementInformationList.Add(roomInformation);

                    continue;
                }

                var description = category.Name;

                var function = string.Empty;
                if (description == "Пожарная сигнализация")
                {
                    function = "WALK";
                }

                if (item is ModelText)
                {
                    function = "TEXT";
                }

                var family = item.Document.GetElement(item.GetTypeId());
                if (family != null)
                {
                    var pmiDescriptionParameter = family.LookupParameter(Helpers.Attributes.PMI_DESCRIPTION);
                    description = pmiDescriptionParameter != null && pmiDescriptionParameter.HasValue
                        ? pmiDescriptionParameter.AsString()
                        : family.Name;

                    var funcParameter = family.get_Parameter(BuiltInParameter.FUNCTION_PARAM);
                    if (funcParameter != null && funcParameter.HasValue)
                    {
                        var value = funcParameter.AsValueString();
                        value = value.Replace("Наружные слои", "N").Replace("Внутренние слои", "V");
                        if (value == "N" || value == "V")
                        {
                            function = value;
                        }
                    }
                }

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
                    elevation = level?.Elevation ?? 0.0;

                    var offsetParam = item.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM);
                    if (offsetParam != null && offsetParam.HasValue)
                    {
                        elevation += offsetParam.AsDouble();
                    }

                    elevation = Math.Round(elevation * Helpers.Constants.FACTOR_LENGTH);
                }

                var zoneParam = item.LookupParameter(Helpers.Attributes.AVEVA_ZONE);
                var avevaZone = zoneParam != null && zoneParam.HasValue ? zoneParam.AsString() : string.Empty;

                var supportingParameter = item.LookupParameter(Helpers.Attributes.MAIN_STRUCTURES);
                var supporting = supportingParameter != null && supportingParameter.AsInteger() != 0;

                var disableParameter = item.LookupParameter(Helpers.Attributes.NEED_IN_AVEVA);
                var isDisabled = disableParameter != null && disableParameter.HasValue &&
                                 disableParameter.AsInteger() == 0;

                var elementInformation = new Utilities.DTO.IfcElementInformation
                {
                    Id = item.Id.IntegerValue.ToString(),
                    UniqueId = item.UniqueId,
                    IsDisabled = isDisabled
                };

                elementInformation.AddProperty("Description", description.Replace(Environment.NewLine, " "), true);
                elementInformation.AddProperty("function", function);
                elementInformation.AddProperty("zone", avevaZone);

                if (elevation != 0.0)
                {
                    elementInformation.AddProperty("elevation", elevation);
                }

                if (supporting)
                {
                    elementInformation.AddProperty("supporting", true);
                }

                try
                {
                    elementInformation.HatchLines = Helpers.HatchingExtractor.GetHatchLines(item, out var materialName);
                    if (!string.IsNullOrEmpty(materialName))
                    {
                        elementInformation.AddProperty("material", materialName);
                    }
                }
                catch
                {
                    using (var sw = new StreamWriter(@"C:\aveva\temp\revit.allert", true))
                    {
                        sw.WriteLine($"{DateTime.Now:dd.MM.yyyy HH:mm:ss}\tGetHatchLines fail: {item.UniqueId}");
                    }
                }

                elementInformationList.Add(elementInformation);
            }

            using (var sw = new StreamWriter(path))
            {
                using (var xmlWriter = XmlWriter.Create(sw,
                           new XmlWriterSettings { Indent = false, OmitXmlDeclaration = true }))
                {
                    var xmlSerializerNamespaces = new XmlSerializerNamespaces(new[] { XmlQualifiedName.Empty });
                    var xmlSerializer = new XmlSerializer(typeof(List<Utilities.DTO.IfcElementInformation>));
                    xmlSerializer.Serialize(xmlWriter, elementInformationList, xmlSerializerNamespaces);
                }
            }
        }

        /// <summary>
        /// Получение данных по помещению
        /// </summary>
        /// <param name="room"></param>
        /// <returns></returns>
        private Utilities.DTO.IfcElementInformation GetRoomInformation(Room room)
        {
            if (room == null)
                return null;

            var disableParameter = room.LookupParameter(Helpers.Attributes.NEED_IN_AVEVA);
            var isDisabled = disableParameter != null && disableParameter.HasValue &&
                             disableParameter.AsInteger() == 0;
            var roomInformation = new Utilities.DTO.IfcElementInformation
            {
                Id = room.Id.IntegerValue.ToString(),
                UniqueId = room.UniqueId,
                IsDisabled = isDisabled,
                Type = Enumerators.ElementType.Room
            };

            roomInformation.AddProperty("ROOM_NUMBER", room.Number);
            roomInformation.AddProperty("ROOM_NAME", room.Name);
            //roomInformation.AddProperty("ROOM_HEIGHT", UnitUtils.ConvertFromInternalUnits(room.UnboundedHeight, UnitTypeId.Meters));
            roomInformation.AddProperty("ROOM_AREA", UnitUtils.ConvertFromInternalUnits(room.Area, UnitTypeId.SquareMeters).ToString("0.0000", CultureInfo.InvariantCulture));
            //roomInformation.AddProperty("ROOM_VOLUME", UnitUtils.ConvertFromInternalUnits(room.Volume, UnitTypeId.CubicMeters));
            //roomInformation.AddProperty("ROOM_PERIMETER", UnitUtils.ConvertFromInternalUnits(room.Perimeter, UnitTypeId.Meters));

            var categoryParam = room.LookupParameter("ADSK_Категория помещения");
            if (categoryParam != null && categoryParam.HasValue)
            {
                roomInformation.AddProperty("Категория помещения", categoryParam.AsString());
            }

            //var boundarySegments = room.GetBoundarySegments(new SpatialElementBoundaryOptions());

            return roomInformation;
        }

        /// <summary>
        /// Экспорт воздуховодных данных MagiCAD
        /// </summary>
        /// <param name="application"></param>
        /// <param name="document"></param>
        private void ExportDuct(Application application, Document document)
        {
            var viewFamilyType = new FilteredElementCollector(document).OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>().FirstOrDefault(x => x.ViewFamily == ViewFamily.ThreeDimensional);

            View3D view;
            using (Transaction tr = new Transaction(document, "create view"))
            {
                tr.Start();
                view = View3D.CreateIsometric(document, viewFamilyType.Id);
                view.Name = DateTime.Now.Ticks.ToString();
                ShowAllElements(document, view);
                tr.Commit();
            }

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

            var levels = GetLevelElements(document);
            foreach (var level in levels)
            {
                var elevParameter = level.get_Parameter(BuiltInParameter.LEVEL_ELEV);
                storeyMappings.Add(new CRevIFCStoreyMapping
                {
                    ifcGuid = level.UniqueId,
                    ifcName = level.Name,
                    ifcPosX = 0.0,
                    ifcPosY = 0.0,
                    ifcPosZ = elevParameter.AsDouble() * Helpers.Constants.FACTOR_LENGTH,
                    platformGuid = level.UniqueId,
                    platformName = level.Name,
                    platformPosX = 0.0,
                    platformPosY = 0.0,
                    platformPosZ = elevParameter.AsDouble() * Helpers.Constants.FACTOR_LENGTH
                });
            }

            newConfig.storeyMappings = storeyMappings;

            var cRevIfcExporter = new CRevIfcExporter(application, document);
            cRevIfcExporter.doExport(newConfig);

            if (File.Exists(_ifcPath))
            {
                WriteElementDuctInfo(document, _ifcPath.Replace(".ifc", ".info"));
            }
            else
            {
                File.Create(_ifcPath);
            }
        }

        /// <summary>
        /// Запись вспомогательных данных для воздуховодных элементов в файл
        /// </summary>
        /// <param name="document"></param>
        /// <param name="path"></param>
        private void WriteElementDuctInfo(Document document, string path)
        {
            //var sb = new StringBuilder();

            var invalidChars = Regex.Escape(new string(Path.GetInvalidFileNameChars()));
            var invalidRegStr = string.Format(@"([{0}]*\.+$)|([{0}]+)", invalidChars);

            DTO.MagiCadInfo.SystemsDictionary = new Dictionary<string, string>();
            var elementInformationList = new List<Utilities.DTO.IfcElementInformation>();

            var collector = new FilteredElementCollector(document).OfClass(typeof(MEPSystem)).Cast<MEPSystem>();
            foreach (var mepSystem in collector)
            {
                var typeSystem = document.GetElement(mepSystem.GetTypeId());
                if (typeSystem == null || DTO.MagiCadInfo.SystemsDictionary.ContainsKey(mepSystem.Name))
                    continue;

                var mepSystemName = mepSystem.Name.Replace(".", " ").Split(' ').First();
                mepSystemName = Regex.Replace(mepSystemName, invalidRegStr, string.Empty);

                DTO.MagiCadInfo.SystemsDictionary.Add(mepSystem.Name,
                    $"{typeSystem.Name.Replace(".", " ").Split(' ').First()}#{mepSystemName}".ToUpper());
            }

            var elementList = GetModelElements(document).Where(x => x.Category.CategoryType == CategoryType.Model).ToList();
            var magiCadInfoList = new List<DTO.MagiCadInfo>();
            foreach (var element in elementList)
            {
                DTO.MagiCadInfo info = null;
                if (element is Duct)
                {
                    info = new DTO.DuctInfo(element);
                }
                else if (element is FlexDuct)
                {
                    info = new DTO.FlexDuctInfo(element);
                }
                else if (element is Pipe)
                {
                    info = new DTO.PipeInfo(element);
                }
                else if (element is FlexPipe)
                {
                    info = new DTO.FlexPipeInfo(element);
                }
                else if (element is FamilyInstance)
                {
                    if (element.Name.StartsWith("УКВ-") || element.Name.StartsWith("УКТ-"))
                    {
                        info = new DTO.SupportInfo(element);
                    }
                    else if (element.Category.Name == "Арматура воздуховодов")
                    {
                        info = new DTO.DuctValveInfo(element);
                    }
                    else if (element.Category.Name == "Арматура трубопроводов")
                    {
                        info = new DTO.PipeValveInfo(element);
                    }
                    else if (element.Category.Name == "Воздухораспределители")
                    {
                        info = new DTO.DuctDiffusersInfo(element);
                    }
                    else if (element.Category.Name == "Обобщенные модели")
                    {
                        if (element.Name.Contains("ПМИ_Зона_резервирования"))
                        {
                            info = new DTO.ReserveInfo(element);
                        }
                        else
                        {
                            info = new DTO.CommonModelInfo(element);
                        }
                    }
                    else if (element.Category.Name == "Оборудование")
                    {
                        info = new DTO.EquipmentInfo(element);
                    }
                    else if (element.Category.Name == "Соединительные детали воздуховодов")
                    {
                        info = new DTO.DuctConnectingPartInfo(element);
                    }
                    else if (element.Category.Name == "Соединительные детали трубопроводов")
                    {
                        info = new DTO.PipeConnectingPartInfo(element);
                    }
                }

                if (info == null)
                    continue;

                magiCadInfoList.Add(info);
            }

            foreach (var info in magiCadInfoList)
            {
                var zoneParam = info.Root.LookupParameter(Helpers.Attributes.AVEVA_ZONE);
                var avevaZone = zoneParam != null && zoneParam.HasValue ? zoneParam.AsString() : string.Empty;

                var elementInformation = new Utilities.DTO.IfcElementInformation
                {
                    Id = info.Id.ToString(),
                    UniqueId = info.UniqueId,
                    Type = info.ElementType,
                    IsDisabled = info.Ignore,
                    Location = info.Location
                };

                elementInformation.AddProperty("Name", FixStringValue(info.Name), true);
                elementInformation.AddProperty("Description", FixStringValue(info.Description), true);
                elementInformation.AddProperty(":EquiType", FixStringValue(info.Sign), true);
                elementInformation.AddProperty(":EquiPlant", FixStringValue(info.Plant), true);
                elementInformation.AddProperty(":EquiWeight", info.Weight == 0.0 ? string.Empty : info.Weight.ToString(), true);
                elementInformation.AddProperty(":EquiUnit", info.Unit, true);
                elementInformation.AddProperty(":EquiCount", info.Count, true);
                elementInformation.AddProperty("System", info.System);
                elementInformation.AddProperty("CreatedBy", info.CreatedBy);
                elementInformation.AddProperty("ModifiedBy", info.ModifiedBy);
                elementInformation.AddProperty("zone", avevaZone);

                elementInformation.AddAttachedItems(info.AttachedElementList);

                if (info is DTO.EquipmentInfo)
                {
                    try
                    {
                        elementInformation.Connectors = DTO.ConnectorData.GetConnectors(info.Root);
                    }
                    catch (Exception e)
                    {
                        elementInformation.Connectors = null;
                    }
                }

                elementInformationList.Add(elementInformation);
            }

            using (var sw = new StreamWriter(path))
            {
                using (var xmlWriter = XmlWriter.Create(sw,
                           new XmlWriterSettings { Indent = false, OmitXmlDeclaration = true }))
                {
                    var xmlSerializerNamespaces = new XmlSerializerNamespaces(new[] { XmlQualifiedName.Empty });
                    var xmlSerializer = new XmlSerializer(typeof(List<Utilities.DTO.IfcElementInformation>));
                    xmlSerializer.Serialize(xmlWriter, elementInformationList, xmlSerializerNamespaces);
                }
            }
        }

        /// <summary>
        /// Исправление спецсимволов в строке
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        private string FixStringValue(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            var output = string.Empty;
            input = input.Replace("Ø", "~D");
            input = input.Replace("δ", "~dlt");
            input = input.Replace("«", "\"");
            input = input.Replace("»", "\"");

            foreach (var c in input)
            {
                if ((c >= 32 && c <= 126) || (c >= 1040 && c <= 1103) || c == 8470 || c == 8211)
                {
                    output += c;
                }
                else
                {
                    output += "?";
                }
            }

            return output;
        }

        /// <summary>
        /// Получение всех элементов типа Level
        /// </summary>
        /// <param name="document"></param>
        /// <returns></returns>
        private List<Level> GetLevelElements(Document document)
        {
            return new FilteredElementCollector(document).OfClass(typeof(Level)).Cast<Level>().ToList();
        }

        /// <summary>
        /// Получение всех элементов модели
        /// </summary>
        /// <param name="document"></param>
        /// <returns></returns>
        private List<Element> GetModelElements(Document document)
        {
            return new FilteredElementCollector(document).WhereElementIsNotElementType().Where(x => x.Category != null)
                .ToList();
        }

        /// <summary>
        /// Включить отображение всех элементов
        /// </summary>
        /// <param name="document"></param>
        /// <param name="view"></param>
        private void ShowAllElements(Document document, View3D view)
        {
            var elements = new List<ElementId>();
            var collector = new FilteredElementCollector(document).WhereElementIsNotElementType();
            foreach (Element element in collector)
            {
                var category = element.Category;
                if (category == null || !category.HasMaterialQuantities || !element.IsHidden(view))
                    continue;

                elements.Add(element.Id);
            }

            foreach (Category category in document.Settings.Categories)
            {
                if (!view.GetCategoryHidden(category.Id) || category.CategoryType != CategoryType.Model)
                    continue;

                view.SetCategoryHidden(category.Id, false);
            }

            if (elements.Count > 0)
                view.UnhideElements(elements);
        }

        /// <summary>
        /// Отслеживание всплывающих окон и их закрытие
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
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
                case "TaskDialog_Macro_Security_Alert":
                    e.OverrideResult((int)TaskDialogResult.CommandLink1);
                    break;
                case "TaskDialog_Calculation_In_Progress":
                    e.OverrideResult((int)TaskDialogResult.Close);
                    CloseRevit();
                    break;
                case "Dialog_Revit_DocWarnDialog":
                    e.OverrideResult((int)TaskDialogResult.Ok);
                    break;
                default:
                    using (var sw = new StreamWriter(@"C:\aveva\temp\revit.allert", true))
                    {
                        sw.WriteLine($"{DateTime.Now:dd.MM.yyyy HH:mm:ss}\t{e.DialogId}");
                    }

                    break;
            }
        }

        /// <summary>
        /// Отслеживание предупреждений при открытии документа и их отключение
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void App_FailuresProcessing(object sender, FailuresProcessingEventArgs e)
        {
            var failuresAccessor = e.GetFailuresAccessor();

            var failureMessageAccessorList = failuresAccessor.GetFailureMessages();
            if (failureMessageAccessorList.Count <= 0)
                return;

            var failureSeverity = failuresAccessor.GetSeverity();

            foreach (var failureMessageAccessor in failureMessageAccessorList)
            {
                if (failureSeverity == FailureSeverity.Warning)
                {
                    failuresAccessor.DeleteWarning(failureMessageAccessor);
                }
                else if (failuresAccessor.IsFailureResolutionPermitted(failureMessageAccessor,
                             FailureResolutionType.DeleteElements))
                {
                    failureMessageAccessor.SetCurrentResolutionType(FailureResolutionType.DeleteElements);
                    failuresAccessor.ResolveFailure(failureMessageAccessor);
                    e.SetProcessingResult(FailureProcessingResult.ProceedWithCommit);
                }
            }
        }

        public Result OnShutdown(UIControlledApplication a)
        {
            return Result.Succeeded;
        }
    }
}