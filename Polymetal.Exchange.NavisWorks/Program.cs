using Autodesk.Navisworks.Api.Controls;
using Autodesk.Navisworks.Api.Resolver;
using System;
using System.Collections.Generic;
using System.IO;
using Polymetal.Exchange.Utilities;
using Autodesk.Navisworks.Api;
using System.Linq;
using System.Diagnostics;
using System.Reflection;
using System.Threading;

namespace Polymetal.Exchange.NavisWorks
{
    internal class Program
    {
        private static NwdInstructions _nwdInstructions;
        static void Main(string[] args)
        {
            if (args.Length < 1) return;

            _nwdInstructions = new NwdInstructions(File.ReadAllText(args[0]));

            while (!IsLicenseEnable())
            {
                Console.WriteLine($"{DateTime.Now.ToShortTimeString()}\tno license");
                //ждем 2 минуты повторной проверки
                Thread.Sleep(1000 * 60 * 2);
            }

            if (string.IsNullOrEmpty(Resolver.TryBindToRuntime(RuntimeNames.Any)))
            {
                throw new Exception("Failed to bind to Navisworks runtime");
            }
            XCreate();
            try
            {
               File.Delete(args[0]);
            }
            catch 
            {
            }
        }

        /// <summary>
        /// Проверка наличия свободной лицензии программы
        /// </summary>
        /// <param name="target">программа</param>
        /// <returns>true - если есть свободная лицензия</returns>
        private static bool IsLicenseEnable()
        {
            var path = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            var pathPolymetalPdmsStart = Path.Combine(path, "Polymetal.Pdms.Start.exe");
            //если отсутствует файл проверки, то предпологаем, что лицензия доступна
            if (!File.Exists(pathPolymetalPdmsStart)) return true;

            var procInfo = new ProcessStartInfo
            {
                FileName = pathPolymetalPdmsStart,
                Arguments = "navis",
                CreateNoWindow = true,
                UseShellExecute = false
            };
            var proc = Process.Start(procInfo);
            if (proc != null)
            {
                proc.WaitForExit(60000);
                var result = proc.ExitCode;
                proc.Close();
                Console.WriteLine("License result {0}", result - 100);
                return result > 100;
            }
            Console.WriteLine("License result proc == null");
            return false;
        }

        /// <summary>
        /// Создание модели
        /// </summary>
        private static void XCreate()
        {
            ApplicationControl.ApplicationType = ApplicationType.SingleDocument;
            ApplicationControl.Initialize();

            var documentControl = new DocumentControl();
            documentControl.SetAsMainDocument();

            documentControl.Document.AppendFiles(_nwdInstructions.NwdFiles);
            TransformModel(documentControl);
            HideElements(documentControl, _nwdInstructions.HiddenElements);

            documentControl.Document.SaveFile(_nwdInstructions.NwdPath);

            documentControl.Dispose();

            ApplicationControl.Terminate();
        }

        /// <summary>
        /// Преобразование модели
        /// </summary>
        /// <param name="documentControl"></param>
        private static void TransformModel(DocumentControl documentControl)
        {
            var selection = new ModelItemCollection();
            selection.AddRange(documentControl.Document.Models.Select(x => x.RootItem));
            documentControl.Document.CurrentSelection.CopyFrom(selection);

            var collection = documentControl.Document.CurrentSelection.SelectedItems;
            if (collection.Count == 0) return;

            var transform = new Transform3D(
                new Matrix3(_nwdInstructions.Transform[0], _nwdInstructions.Transform[1], _nwdInstructions.Transform[2],
                    _nwdInstructions.Transform[3], _nwdInstructions.Transform[4], _nwdInstructions.Transform[5],
                    _nwdInstructions.Transform[6], _nwdInstructions.Transform[7], _nwdInstructions.Transform[8]),
                new Vector3D(_nwdInstructions.Transform[9], _nwdInstructions.Transform[10],
                    _nwdInstructions.Transform[11]) * 0.001); //* 0.001 из мм в м
            documentControl.Document.Models.OverridePermanentTransform(collection, transform.Inverse(), true);
        }

        /// <summary>
        /// Скрытие элементов
        /// </summary>
        /// <param name="documentControl"></param>
        /// <param name="hiddenElements"></param>
        private static void HideElements(DocumentControl documentControl, List<string> hiddenElements)
        {
            if (hiddenElements.Count == 0) return;

            var search = new Search();
            search.Selection.SelectAll();

            foreach (var itemName in hiddenElements)
            {
                var conditionGroup = new List<SearchCondition>();
                var condition = SearchCondition.HasPropertyByDisplayName("Элемент", "Имя")
                    .EqualValue(
                        VariantData.FromDisplayString(itemName));

                conditionGroup.Add(condition);
                search.SearchConditions.AddGroup(conditionGroup);
            }

            var selection = search.FindAll(documentControl.Document, false);
            Application.ActiveDocument.Models.SetHidden(selection, true);
        }
    }
}
