using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.IO;
using System.Linq;
using Aveva.Core.Database;
using Aveva.Core.Database.Filters;
using Aveva.Core.Geometry;
using Polymetal.E3D.Design.EquipmentTemplateProperties;
using Polymetal.E3D.Design.EquipmentTemplateProperties.Obsolete;
using Polymetal.E3D.Ifc.DB;
using Polymetal.Pdms.Common.DataBaseUtilites;
using TypeFilter = Aveva.Core.Database.Filters.TypeFilter;

namespace Polymetal.E3D.Ifc
{
    [Flags]
    public enum ELEMENT_COMPARATION_RESULT
    {
        Undefined = 0,
        Similar = 1,
        New = 2,
        ModifiedGeom = 4,
        ModifiedDes = 8,
        ModifiedDocs = 16,
        Deletetd = 32,
        Preliminary = 64
    }

    public class DbElementComparer
    {
        private DbElement _taskZone;    
        private DbElement _giveRootZone;
        private static string TASK3D_DOC_PATH = Path.Combine(Environment.GetEnvironmentVariable("aveva_server_dir"), "Documentation\\RepTask\\");
        private string _currentSubobjectId = GetProjectDataClass.GetAvevaProjectInfo().CompanyProjectData.SubObjectID;
        private readonly IIFcDatabaseProvider _db;
        private readonly int _originalTaskId;
 
        public DbElementComparer(int originalTaskId, DbElement taskZone, DbElement giveRootZone)
        {
            _db = new IfcDatabaseProvider(Polymetal.Core.Common.SQL.ConnectionString);
            var ownlst = taskZone.GetElementArray(DbAttributeInstance.OWNLST);
            AvevaGeometry.CurrentSite = ownlst.FirstOrDefault(x => x.ElementType == DbElementTypeInstance.SITE);

            if (giveRootZone == null || giveRootZone.IsNull)
                throw new ArgumentNullException(nameof(giveRootZone));
            
            if (taskZone == null || taskZone.IsNull)
                throw new ArgumentNullException(nameof(taskZone));

            _originalTaskId = originalTaskId;
            _giveRootZone = giveRootZone;
            _taskZone = taskZone;
        }


        public DbElement GetRevisionZone(int revisionNo)
        {
            var strRevisionNo = (revisionNo > 9) ? revisionNo.ToString() : $"0{revisionNo}";
            string mask = $"{_taskZone.Name()}-R{strRevisionNo}-";
            var typeFilter = new TypeFilter(new[] { DbElementTypeInstance.ZONE });
            var collection = new DBElementCollection(_giveRootZone, typeFilter).Cast<DbElement>().ToList();

            // 18020700200-ZDN-TEST-KURKOV-1-R19-2024.07.02
            var taskZoneRevision = collection.FirstOrDefault(x => x.Name().Contains(mask));
         
            return taskZoneRevision;
        }


        /// Получить зону в ревизии, которая соответствует зоне в рабочке.
        public DbElement GetRevisionSubZone(DbElement zoneInTask, int revisionNo, bool isOuterZone)
        {
            var zoneInRevision = DbElement.GetElement();
            var revisionId = _db.GetRevisionIdByRevisionNum(_originalTaskId, revisionNo);

            var strRevisionNo = (revisionNo > 9) ? revisionNo.ToString() : $"0{revisionNo}";

            if (zoneInTask != null || !zoneInTask.IsNull)
            {
                string zoneInTaskName;
                if (!string.IsNullOrEmpty(revisionId) && isOuterZone)
                    zoneInTaskName = $"{zoneInTask.Name()}-{revisionId}-R{strRevisionNo}";
                else
                    zoneInTaskName = $"{zoneInTask.Name()}-R{strRevisionNo}";

                if (zoneInTaskName.Length > 50)
                    zoneInTaskName = FitZoneNameTo50Characters(zoneInTaskName, isOuterZone? revisionId : string.Empty);
                zoneInRevision = DbElement.GetElement(zoneInTaskName);
            }

            return zoneInRevision;
        }
        

        /// <summary>
        /// Проверяет существует ли соответствующий элемент в указаной ревизии 3D-задания.
        /// </summary>
        /// <param name="sourceElement">Элемент, который нужно проверить.</param>
        /// <param name="sourceElementZone">Имя зоны в которой находится элемент.</param>
        /// <param name="revision">Номер ревизии которую нужно проверить.</param>
        /// <returns>Результат проверки.</returns>
        public ELEMENT_COMPARATION_RESULT Compare(DbElement sourceElement, string sourceElementZone, int revisionNo)
        {
            if (sourceElement.ElementType == DbElementTypeInstance.DIMGROUP)
                return ELEMENT_COMPARATION_RESULT.Similar;

            var result = ELEMENT_COMPARATION_RESULT.Undefined;
            
            if (sourceElement == null || sourceElement.IsNull)
                return result;

            var elementZone = DbElement.GetElement(sourceElementZone);
            if (elementZone.IsNull)
                return result;

            if (sourceElementZone.Contains("-REZV"))
                return ELEMENT_COMPARATION_RESULT.Preliminary;

            if (revisionNo < 1)
                return ELEMENT_COMPARATION_RESULT.New;

            bool isLoad = sourceElementZone.EndsWith("NAGRUZKA");
            DbElement targetElement = DetermineTargetElement(sourceElement, revisionNo, isLoad);
            if (targetElement == null || targetElement.IsNull)
                return ELEMENT_COMPARATION_RESULT.New;
            
            // Проверить элементы на идентичность (координаты, габариты, ориентацию, дизайн-параметры, присоединённые документы)
            result |= DoCombinedElementComparation(sourceElement, targetElement, isLoad);
            
            return result;
        }


        /// <summary>
        /// Получить словарь элементов отсутствующих саб-зон у указанной ревизии.
        /// Проходит по списку саб-зон ревизии и проверяет существуют ли соответствующие саб-зоны в 
        /// зоне задания. Если нет, то заносит эту саб-зону в словарь ключём, значене - список элементов.
        /// </summary>
        /// <param name="revisionNo">Номер ревизии</param>
        /// <param name="outerSubZones">Список внешних саб-зон</param>
        /// <param name="zoneCollection">Список строк с именами ZoneElement (элементы зон, которые выбираются на форме выдачи задания)</param>
        /// <returns></returns>
        public Dictionary<DbElement, List<DbElement>> GetElementsOfRemovedSubZones(int revisionNo, List<string> outerSubZones, List<string> zoneCollection)
        {
            var result = new Dictionary<DbElement, List<DbElement>>();

            if (revisionNo < 1)
                return result;

            var revisioZone = GetRevisionZone(revisionNo);

            if (revisioZone != null && !revisioZone.IsNull)
            {
                var revSubZones = revisioZone.Members();
                foreach (var subZone in revSubZones)
                {
                    // Получить имя саб-зоны в зоне задания, которое соответствует саб-зоне ревизии.
                    var nameToCheck = GetNameForTaskSubZone(subZone.GetAsString(DbAttributeInstance.FLNM), revisionNo);
                    
                    // Проверить, существует ли в зоне задания соответствующая ревизии саб-зона
                    // || присутствует ли саб-зона в коллекции выбраных ZoneElement'ов.
                    if (_taskZone.Members().All(x => x.GetAsString(DbAttributeInstance.FLNM) != nameToCheck) 
                        || !zoneCollection.Contains(nameToCheck))
                    {
                        // Проверить не является ли саб-зона внешней (внешняя - означает не из зоны задания, а добавлена через текущий элемент в моделе)
                        if (outerSubZones.Contains(nameToCheck))
                            continue;
                        
                        result.Add(subZone, new List<DbElement>(subZone.Members()));
                    }
                }
            }

            return result;
        }

        // Получить словарь исключённых саб-зон у указанной ревизии.
        // Проходит по списку саб-зон ревизии и проверяет существуют ли соответствующие саб-зоны в 
        // зоне задания. Если нет, то заносит эту саб-зону в словарь ключём, значене - список элементов.
        public Dictionary<DbElement, List<DbElement>> GetElementsOfExcludedSubZones(int revisionNo, List<string> outerSubZones)
        {
            var result = new Dictionary<DbElement, List<DbElement>>();
            
            if (revisionNo < 1)
                return result;

            var revisionZone = GetRevisionZone(revisionNo);
            if (revisionZone != null && !revisionZone.IsNull)
            {
                var revSubZones = revisionZone.Members();
                foreach (var subZone in revSubZones)
                {
                    var nameToCheck = GetNameForTaskSubZone(subZone.GetAsString(DbAttributeInstance.FLNM), revisionNo);
                    if (_taskZone.Members().All(x => x.GetAsString(DbAttributeInstance.FLNM) != nameToCheck))
                    {
                        // Проверить не является ли саб-зона внешней (внешняя - означает не из зоны задания, а добавлена через текущий элемент в моделе)
                        if (outerSubZones.Contains(nameToCheck))
                            continue;

                        result.Add(subZone, new List<DbElement>(subZone.Members()));
                    }
                }
            }

            return result;
        }
        
        private ELEMENT_COMPARATION_RESULT DoCombinedElementComparation(DbElement source, DbElement target, bool isLoadProcessign)
        {
            var result = ELEMENT_COMPARATION_RESULT.Undefined;
            
            result |= CompareElementsGeometry(source, target, isLoadProcessign);
            result |= CompareElementsDesignParameters(source, target);
            result |= CompareElementsExtraAttributes(source, target);
            result |= CompareElementsAttachedDocuments(source, target);
            
            return result;
        }

        private ELEMENT_COMPARATION_RESULT CompareElementsGeometry(DbElement source, DbElement target, bool isLoadProcessing)
        {
            var result = ELEMENT_COMPARATION_RESULT.Undefined;

            if (ExportIfc.ColorRules == null)
                ExportIfc.InitColorRules();

            // Сохранить теукщую колекцию кастомных цветов, т.к. её потребуется обнулить на время сравнения двух элементов.
            Dictionary<DbElement, int> customColorsSaved = ExportIfc.ElementsCustomColours;
            ExportIfc.ElementsCustomColours = new Dictionary<DbElement, int>();

            if (source.ElementType == DbElementTypeInstance.PIPE)
            {
                if (isLoadProcessing)
                    result |= ArePipingLoadsElementsIdentical(source, target);
                else
                    result |= ArePipingElementsIdentical(source, target);
            }
            else
            {
                if (source.ElementType == DbElementTypeInstance.CWAY && target.ElementType == DbElementTypeInstance.CWAY)
                    result |= CompareCwayElementsGeometry(source, target);
                else
                    result |= CompareGenericElementsGeometry(source, target);
            }

            // Востановить сохранённую колекцию кастомных цветов.
            ExportIfc.ElementsCustomColours = customColorsSaved;

            return result;
        }

        private ELEMENT_COMPARATION_RESULT CompareGenericElementsGeometry(DbElement source, DbElement target)
        {
            var result = ELEMENT_COMPARATION_RESULT.Undefined;

            // Цвет 372 - это временный цвет, заданный для этапа сравнения обоим элементам. (Чтобы не было расхождений по цвету)
            var geomLeft = new AvevaGeometry(source, 372);
            var geomRight = new AvevaGeometry(target, 372);

            var txtLeft = geomLeft.GetText().ToString();
            var txtRigth = geomRight.GetText().ToString();

            result |= (txtLeft == txtRigth) ? ELEMENT_COMPARATION_RESULT.Similar : ELEMENT_COMPARATION_RESULT.ModifiedGeom;

            return result;
        }

        private ELEMENT_COMPARATION_RESULT CompareCwayElementsGeometry(DbElement source, DbElement target)
        {
            List<string> sourceCwayBanches = new List<string>();
            foreach (DbElement cwBran in source.Members())
            {
                var geometry = new AvevaGeometry(cwBran, 372);
                var geomStr = geometry.GetText().ToString();
                sourceCwayBanches.Add(geomStr);
            }

            List<string> targetCwayBanches = new List<string>();
            foreach (DbElement cwBran in target.Members())
            {
                var geometry = new AvevaGeometry(cwBran, 372);
                var geomStr = geometry.GetText().ToString();
                targetCwayBanches.Add(geomStr);
            }

            if (sourceCwayBanches.Count != targetCwayBanches.Count)
                return ELEMENT_COMPARATION_RESULT.ModifiedGeom;

            for (int i = 0; i < sourceCwayBanches.Count; i++)
            {
                if (sourceCwayBanches[i] != targetCwayBanches[i])
                    return ELEMENT_COMPARATION_RESULT.ModifiedGeom;
            }

            return ELEMENT_COMPARATION_RESULT.Similar;
        }

        private ELEMENT_COMPARATION_RESULT ArePipingLoadsElementsIdentical(DbElement source, DbElement target)
        {
            if (ExportIfc.ColorRules == null)
                ExportIfc.InitColorRules();

            // Из рабочей зоны
            var sourceBranNames = source.Members()
                .Where(x => x.ElementType == DbElementTypeInstance.BRANCH)
                .Where(x => x.Members().Select(t => t.ElementType).Contains(DbElementTypeInstance.ATTACHMENT))
                .Select(x => $"Copy of {x.Name()}");

            // Из зоны ревизии ХХХ-COPY-R22
            var targetBranNames = target.Members()
                .Where(x => x.ElementType == DbElementTypeInstance.BRANCH)
                .Select(x => x.GetAsString(DbAttributeInstance.DESC));

            // Проверить, что все элементы из sourceBranNames входят в targetBranNames
            bool allIncluded = sourceBranNames.All(x => targetBranNames.Contains(x));
            if (!allIncluded)
                return ELEMENT_COMPARATION_RESULT.ModifiedGeom;

            return ELEMENT_COMPARATION_RESULT.Similar;
        }

        private ELEMENT_COMPARATION_RESULT ArePipingElementsIdentical(DbElement left, DbElement right)
        {
            if (ExportIfc.ColorRules == null)
                ExportIfc.InitColorRules();

            List<string> letfText = new List<string>();
            List<string> rightText = new List<string>();

            var leftChildren = ExportIfc.GetSubElements(left);
            var rightChildern = ExportIfc.GetSubElements(right);

            if (leftChildren.Count != rightChildern.Count)
                return ELEMENT_COMPARATION_RESULT.ModifiedGeom;

            for (int i = 0; i < leftChildren.Count; i++)
            {
                var leftGeometry = new AvevaGeometry(leftChildren[i], 372);
                var rightGeometry = new AvevaGeometry(rightChildern[i], 372);

                var txtLeft = leftGeometry.GetText().ToString();
                var txtRight = rightGeometry.GetText().ToString();

                if (txtLeft != txtRight)
                    return ELEMENT_COMPARATION_RESULT.ModifiedGeom;
            }
            
            return ELEMENT_COMPARATION_RESULT.Similar;
        }

        private bool CompareElementsPosition(DbElement left, DbElement right)
        {
            if (left == null || left.IsNull)
                return false;

            if (right == null || right.IsNull)
                return false;

            try
            {
                Position lPosition = left.GetPosition(DbAttributeInstance.POS);
                Position rPosition = right.GetPosition(DbAttributeInstance.POS);

                return lPosition.Equals(rPosition);
            }
            catch (Exception ex)
            {
                return false;
            }
        }

        private ELEMENT_COMPARATION_RESULT CompareElementsDesignParameters(DbElement left, DbElement right)
        {
            List<string> lParams = Creator.PropertiesToFormattedList(left);
            List<string> rParams = Creator.PropertiesToFormattedList(right);

            if (lParams.Count != rParams.Count)
                return ELEMENT_COMPARATION_RESULT.ModifiedDes;

            for (int i = 0; i < lParams.Count; i++)
            {
                if (lParams[i] != rParams[i])
                    return ELEMENT_COMPARATION_RESULT.ModifiedDes;
            }

            return ELEMENT_COMPARATION_RESULT.Similar;
        }

        private ELEMENT_COMPARATION_RESULT CompareElementsExtraAttributes(DbElement source, DbElement target)
        {
            var sourceExtraAttrs = ExtraAttributes.GetAllAttributesFromElement(source.GetAsString(DbAttributeInstance.REF));
            var targetExtraAttrs = ExtraAttributes.GetAllAttributesFromElement(target.GetAsString(DbAttributeInstance.REF));

            return (sourceExtraAttrs == targetExtraAttrs)? ELEMENT_COMPARATION_RESULT.Similar : ELEMENT_COMPARATION_RESULT.ModifiedDes;
        }

        private ELEMENT_COMPARATION_RESULT CompareElementsAttachedDocuments(DbElement left, DbElement right)
        {
            List<string> lFiles = GetElementAttachedFiles(left);
            List<string> rFiles = GetElementAttachedFiles(right);
            
            
            if (lFiles.Count != rFiles.Count)
                return ELEMENT_COMPARATION_RESULT.ModifiedDocs;


            lFiles.Sort();
            rFiles.Sort();

            for (int i = 0; i < lFiles.Count; i++)
            {
                if(Path.GetFileName(lFiles[i]) != Path.GetFileName(rFiles[i]))
                    return ELEMENT_COMPARATION_RESULT.ModifiedDocs;
            }

            return ELEMENT_COMPARATION_RESULT.Similar;
        }

        private bool AreFilesIdentical(string lFile, string rFile)
        {
            if (!File.Exists(lFile) || !File.Exists(rFile))
                return false;

            FileInfo fileInfo1 = new FileInfo(lFile);
            FileInfo fileInfo2 = new FileInfo(rFile);

            if (fileInfo1.Name != fileInfo2.Name)
                return false;
            
            if (fileInfo1.Length != fileInfo2.Length)
                return false;


            // Побайтовое сравнение содержимого файлов
            using (FileStream fs1 = File.OpenRead(lFile))
            using (FileStream fs2 = File.OpenRead(rFile))
            {
                int byte1, byte2;
                do
                {
                    byte1 = fs1.ReadByte();
                    byte2 = fs2.ReadByte();
                } while (byte1 == byte2 && byte1 != -1);

                return byte1 == byte2;
            }

            return true;
        }

        /// <summary>
        /// Получить список полных путей присоединённых документов.
        /// </summary>
        /// <param name="elem">Элемент</param>
        /// <returns>Список путей к файлам.</returns>
        private List<string> GetElementAttachedFiles(DbElement elem)
        {
            var result = new List<string>();
            var elemRef = elem.GetAsString(DbAttributeInstance.REF);
            var attrs = ExtraAttributes.GetAllAttributesFromElement(elemRef);
            if (attrs.IsEmpty)
                return result;
                        
            foreach (var str in attrs.DrawingsList)
            {
                if (!string.IsNullOrEmpty(str))
                {
                    // Получить путь к файлу. [184\корпус_CIL_низ.dwg*29.01.2026 12:16:28]
                    var subObjId = string.Empty;
                    var name = string.Empty;
                    var date = string.Empty;

                    var arr = str.Split('\\');
                    if (arr.Length > 1)
                    {
                        subObjId = arr[0];
                        name = arr[1];
                    }
                    else
                    {
                        subObjId = _currentSubobjectId;
                        name = _currentSubobjectId;
                    }

                    arr = name.Split('*');
                    if (arr.Length > 1)
                    {
                        name = arr[0];
                    }

                    var strPath = Path.Combine(TASK3D_DOC_PATH, subObjId, elemRef.Replace('/', '#'), name);
                    result.Add(strPath);
                }
            }
            
        
            return result;
        }

        /// <summary>
        /// Найти целевой элемент в зоне ревизии. В дальнейшем он будет использован для сравнеия с соответствующим элементом из зоны задания.
        /// </summary>
        /// <param name="sourceElement">Исходный элемент в зоне задания</param>
        /// <param name="revision">Номер ревизии</param>
        /// <param name="isLoadProcessing">Флаг, указывающий что это элемент задания на нагрузки.</param>
        /// <returns>Элемент из зоны ревизии, который соответствует исходному элементу в зоне задания</returns>
        private DbElement DetermineTargetElement(DbElement sourceElement, int revision, bool isLoadProcessing = false)
        {
            string strRev = (revision > 9) ? revision.ToString() : $"0{revision}";

            var targetElement = DbElement.GetElement();
            string mask = $"{_taskZone.Name()}-R{strRev}-";
            var typeFilter = new TypeFilter(new[] { DbElementTypeInstance.ZONE });
            var collection = new DBElementCollection(_giveRootZone, typeFilter).Cast<DbElement>().ToList();

            // 18020700200-ZDN-TEST-KURKOV-1-R19-2024.07.02
            var revisionZone = collection.FirstOrDefault(x => x.Name().Contains(mask));
            if (revisionZone != null && !revisionZone.IsNull)
            {
                // Если это нагрузка на опору, то искать по описанию:
                // ("Нагрузка на опору (=134260886/22806) ATTACHMENT 1 of BRANCH /P13-0503-00201-05")
                if (IsSupportLoad(sourceElement))
                {
                    targetElement = FindElementByLoadDescription(sourceElement, revisionZone);
                }
                // Если это трубопровод с опорами (из зоны задания ХХХ-COPY) 
                else if (IsPipeLineWithSupports(sourceElement))
                {
                    targetElement = FindPipeTargetElement(revisionZone, revision);
                }
                // Если это трубопровод с нагрузками на опоры из рабочей зоны. 
                else if (sourceElement.ElementType == DbElementTypeInstance.PIPE && isLoadProcessing)
                {
                    targetElement = FindPipeTargetElementForWorkingZone(sourceElement, revision);
                }
                else
                {
                    targetElement = FindElementByLinkAttributeInZone(sourceElement, revisionZone);
                }
            }
            
            return targetElement;
        }


        /// <summary>
        /// Найти элемент в указаной зоне у которого в описании присутствует подстрока с информацией о нагрузках на опоры.
        /// </summary>
        /// <param name="sourceElement">Элемент с информацией о нагрузках на опоры в описании.</param>
        /// <param name="searchZone">Зона в которой будет происходить поиск элемента (зона ревизии).</param>
        /// <returns>Элемент с подходящим описанием, либо пустой элемент.</returns>
        private DbElement FindElementByLoadDescription(DbElement sourceElement, DbElement searchZone)
        {
            var loadString = GetSupportLoadFromDescription(sourceElement);

            if (!string.IsNullOrEmpty(loadString))
            {
                var typeFilter = new TypeFilter(DbElementTypeInstance.EQUIPMENT);
                var likeFilter = new AttributeLikeFilter(DbAttributeInstance.DESC, $"{loadString}*");
                var andFilter = new AndFilter(new BaseFilter[] { typeFilter, likeFilter });

                var collection = new DBElementCollection(searchZone, andFilter).Cast<DbElement>().ToList();

                return collection.FirstOrDefault() ?? DbElement.GetElement();
            }

            return DbElement.GetElement();
        }

        private DbElement FindElementByLinkAttributeInZone(DbElement element, DbElement searchZone)
        {
            var sourceRef = element.GetAsString(DbAttributeInstance.REF);
            if (string.IsNullOrEmpty(sourceRef))
                return DbElement.GetElement(); 

            var filter = new AttributeStringFilter(Core.Common.UserAttributes.Link, FilterOperator.Equals, sourceRef);
            return new DBElementCollection(searchZone, filter).Cast<DbElement>().FirstOrDefault() ?? DbElement.GetElement();
        }

        /// <summary>
        /// Найти целевой элемент из зоны задания, который соответствует исходному элементу.
        /// Для трубопровода с опорами и нагрузками, которые находятся в рабочих зонах.
        /// В зоне задания находится сборная труба в которую копируются все бранчи из исходных
        /// данных при работе утилиты по созданию задания на нагрузки.
        /// </summary>
        /// <param name="sourceElement"></param>
        /// <returns></returns>
        private DbElement FindPipeTargetElementForWorkingZone(DbElement sourceElement, int revision)
        {
            var result = DbElement.GetElement();

            // Получить сводный PIPE
            var vaultPipe = GetVaultPipeElement(revision);
            if (vaultPipe.IsNull)
                return result;
            
            // получить список имён бранчей исходного пайпа (только те, что содержат ATTACHMENT'ы)
            // и добавить к имени "Copy of " (это нужно для сравнения)
            var branNames = sourceElement.Members()
                .Where(x => x.ElementType == DbElementTypeInstance.BRANCH)
                .Where(x => x.Members().Select(t => t.ElementType).Contains(DbElementTypeInstance.ATTACHMENT))
                .Select(x => $"Copy of {x.Name()}");

            // получить список описаний бранчей сводного пайпа.
            var vaultPipeBranDescs = vaultPipe.Members()
                .Where(x => x.ElementType == DbElementTypeInstance.BRANCH)
                .Select(x => x.GetAsString(DbAttributeInstance.DESC));

            var intersection = vaultPipeBranDescs.Intersect(branNames);
            if (intersection.Any())
                return vaultPipe;

            return result;
        }


        /// <summary>
        /// Получить объединяющий PIPE из зоны ревизии.
        /// PIPE, в который помещаются все бранчи из исходных данных при отработке утилиты по созданию заданий на нагрузки.
        /// </summary>
        /// <param name="revision">Номер ревизии</param>
        /// <returns></returns>
        private DbElement GetVaultPipeElement(int revision)
        {
            var result = DbElement.GetElement();
            string strRev = (revision > 9) ? revision.ToString() : $"0{revision}";
            string pipeLoadZoneStr = $"{_taskZone.Name()}-COPY-R{strRev}";

            var zoneCopy = DbElement.GetElement(pipeLoadZoneStr);
            if (!zoneCopy.IsNull)
            {
                var first = zoneCopy.Members().FirstOrDefault();
                if (first != null && !first.IsNull && first.ElementType == DbElementTypeInstance.PIPE)
                    return first;
            }

            return result;
        }

        private DbElement FindPipeTargetElement(DbElement searchZone, int revision)
        {
            // если в зоне ревизии присутствует зона XXX-COPY, а в ней присутствует PIPE (не нулевой), то считать что элемент не новый является целевым.
            string strRev = (revision > 9) ? revision.ToString() : $"0{revision}";
            string pipeLoadZoneStr = $"{_taskZone.Name()}-COPY-R{strRev}";

            var pipeLoadZone = DbElement.GetElement(pipeLoadZoneStr);
            if (!pipeLoadZone.IsNull)
            {
                var pipe = pipeLoadZone.Members().FirstOrDefault();
                if (!pipe.IsNull && pipe.ElementType == DbElementTypeInstance.PIPE)
                    return pipe;
            }

            return DbElement.GetElement();
        }


        /// <summary>
        /// Получить список удалённых элементов у саб-зоны ревизии.
        /// Ищутся элемены, которые есть в ревизии с определённым номером, но отсутствуют в саб-зоне задания.
        /// </summary>
        /// <param name="subZone">Саб-зона в зоне задания.</param>
        /// <param name="revision">Номер ревизии</param>
        /// <param name="isOuterZone">Является ли зона внешней (вне зоны задания)</param>
        /// <returns></returns>
        public List<DbElement> FindDeletedElementsForSubZone(DbElement subZone, int revision, bool isOuterZone)
        {
            var deletedElements = new List<DbElement>();

            if (revision < 1)
                return deletedElements;

            if (subZone == null || subZone.IsNull)
                return deletedElements;

            var revisionSubZone = GetRevisionSubZone(subZone, revision, isOuterZone);
            if (revisionSubZone == null || revisionSubZone.IsNull)
                return deletedElements;
            
            foreach (var elem in revisionSubZone.Members())
            {
                // Если это трубопровод, созданный утилитой для опор, то просто проверить существует ли элемент PIPE 1 в зоне задания XXXXX-COPY
                if (IsPipeLineWithSupports(elem))
                {
                    var str = _taskZone.Name() + "-COPY";
                    var loadsPipeZone = DbElement.GetElement(str);
                    if (!loadsPipeZone.IsNull)
                    {
                        var pipe = loadsPipeZone.Members().FirstOrDefault();
                        if (!pipe.IsNull && pipe.ElementType == DbElementTypeInstance.PIPE &&
                            pipe.GetAsString(DbAttributeInstance.DESC) == "Трубопровод с опорами")
                            continue;
                    }
                    
                    deletedElements.Add(elem);        
                }
                // Если это нагрузки на опоры, то проверить существует ли в зоне задания нагрузка с описанием, в котором фигурирует ref на нужную опору.
                else if (IsSupportLoad(elem))
                {
                    var loadString = GetSupportLoadFromDescription(elem);
                    if (!string.IsNullOrEmpty(loadString))
                    {
                        var likeFilter = new AttributeLikeFilter(DbAttributeInstance.DESC, $"{loadString}*");
                        var typeFilter = new TypeFilter(DbElementTypeInstance.EQUIPMENT);
                        var andFilter = new AndFilter(likeFilter, typeFilter);

                        var collection = new DBElementCollection(_taskZone, andFilter).Cast<DbElement>().ToList();
                        if (!collection.Any())
                            deletedElements.Add(elem);
                    }
                }
                else // Для других элементов проверять линки.
                {
                    var link = elem.GetAsString(Core.Common.UserAttributes.Link);
                    var filter = new AttributeStringFilter(DbAttributeInstance.REF, FilterOperator.Equals, link);
                    var searchZone = isOuterZone ? subZone : _taskZone;
                    var collection = new DBElementCollection(searchZone, filter).Cast<DbElement>();
                    if (!collection.Any())
                        deletedElements.Add(elem);
                }
            }

            return deletedElements;
        }

        /// <summary>
        /// Проверяет отсутствующие элемены в зоне ревизии.
        /// </summary>
        /// <param name="elementRefs">Список строк с Ref элементов, которые нужно проверить</param>
        /// <param name="subZone">Саб-зона в зоне задания</param>
        /// <param name="revision">номер ревизии</param>
        /// <param name="isOuterZone">Является ли саб-зона внешней. Вне зоны задания</param>
        /// <returns></returns>
        public List<DbElement> FindExcludedElements(List<string> elementRefs, DbElement subZone, int revision, bool isOuterZone)
        {
            var excludedElements = new List<DbElement>();

            if (revision < 1)
                return excludedElements;

            if (subZone == null || subZone.IsNull)
                return excludedElements;

            var revisionSubZone = GetRevisionSubZone(subZone, revision, isOuterZone);

            foreach (var elem in revisionSubZone.Members())
            {
                // Если это трубопровод созданный утилитой для опор, то пропустить. 
                if (IsPipeLineWithSupports(elem))
                {
                        continue;
                }
                else if (IsSupportLoad(elem))
                {
                    var loadString = GetSupportLoadFromDescription(elem);
                    if (!string.IsNullOrEmpty(loadString))
                    {
                        var typeFilter = new TypeFilter(DbElementTypeInstance.EQUIPMENT);
                        var likeFilter = new AttributeLikeFilter(DbAttributeInstance.DESC, $"{loadString}*");
                        var andFilter = new AndFilter(new BaseFilter[] { typeFilter, likeFilter });

                        var collection = new DBElementCollection(_taskZone, andFilter).Cast<DbElement>().ToList();
                        if (!collection.Any())
                            excludedElements.Add(elem);
                    }
                }
                else
                {
                    var link = elem.GetAsString(Core.Common.UserAttributes.Link);
                    if (!elementRefs.Contains(link))
                        excludedElements.Add(elem);
                }
            }

            return excludedElements;
        }

        // Является ли элемент трубопроводом с опорами.
        private bool IsPipeLineWithSupports(DbElement element) =>
            element.ElementType == DbElementTypeInstance.PIPE &&
                    element.GetAsString(DbAttributeInstance.DESC) == "Трубопровод с опорами";
        

        // Является ли элемент нагрузкой на опоры.
        private bool IsSupportLoad(DbElement element) =>
            element.ElementType == DbElementTypeInstance.EQUIPMENT &&
                    element.GetAsString(DbAttributeInstance.DESC).Contains("Нагрузка на опору (=");

        // Получить строку с ref'ом на нагрузку из описания
        private string GetSupportLoadFromDescription(DbElement element)
        {
            string result = string.Empty;

            if (IsSupportLoad(element))
            {
                var desc = element.GetAsString(DbAttributeInstance.DESC);
                if (!string.IsNullOrEmpty(desc))
                {
                    int closeBracket = desc.IndexOf(')');
                    if (closeBracket > 0)
                    {
                        result = desc.Substring(0, closeBracket + 1);
                    }
                }
            }
            
            return result;
        }

        private string GetNameForTaskSubZone(string nameInRevisionZone, int revisionNo)
        {
            // имя зоны без RXX на конце
            // 18020700200-ZDN-TEST-KURKOV-1-FUND-R11 => 18020700200-ZDN-TEST-KURKOV-1-FUND
            int i = nameInRevisionZone.LastIndexOf("-");
            var res = nameInRevisionZone.Substring(0, i);

            string revisionId = _db.GetRevisionIdByRevisionNum(_originalTaskId, revisionNo);
            string suff = $"-{revisionId}";
            if (res.EndsWith(suff))
                res = res.Substring(0, res.Length - suff.Length);
            
            if (res.EndsWith("-"))
                res = res.Remove(res.Length - 1);

            return res;
        }



        // input1:  81020721100-OOPR-ZDN-OOPR-21104...21108-DOOR-5526-R01
        // output1: 81020721100-OOPR-ZDN-OOPR-21104...21-5FFD-5526-R01
        // input2:  81020721100-OOPR-ZDN-OOPR-2112204...21122308-DOOR-R01
        // output2: 81020721100-OOPR-ZDN-OOPR-2222221104...21-11B9-R01
        public static string FitZoneNameTo50Characters(string zoneName, string sdr)
        {
            var AVEVA_MAX = 50;
            var result = string.Empty;
            var suff = string.Empty;
            var hash = HashTo4Chars(zoneName);

            if (zoneName.Length <= AVEVA_MAX)
                return zoneName;

            // Суффикс содержет номер ревизии и id ревизии (если есть sdr)
            if (string.IsNullOrEmpty(sdr))
                suff = zoneName.Substring(zoneName.Length - 3);
            else
                suff = zoneName.Substring(zoneName.Length - 8);

            if (!suff.StartsWith("-"))
                suff = "-" + suff;

            if (!hash.StartsWith("-"))
                hash = "-" + hash;

            var ending = hash + suff;
            var starting = zoneName.Substring(0, AVEVA_MAX - ending.Length);
            if (starting.EndsWith("-"))
                starting = zoneName.Substring(zoneName.Length - ending.Length - 1);

            result = starting + ending;

            return result;
        }
        
        private static string HashTo4Chars(string input)
        {
            if (string.IsNullOrEmpty(input))
                return "0000";

            // Используем встроенный хеш (быстро, но не криптостойко)
            int hash = input.GetHashCode();

            // Берём младшие 16 бит и приводим к положительному числу
            ushort shortHash = (ushort)(hash & 0xFFFF);

            // Форматируем как 4-символьную hex-строку в нижнем регистре
            return shortHash.ToString("x4").ToUpper();
        }
    }

    public static class StringExt
    {
        public static string Trucncate(this string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value))
                return value;

            return (value.Length <= maxLength) ? value : value.Substring(0, maxLength);
        }
    }
}
