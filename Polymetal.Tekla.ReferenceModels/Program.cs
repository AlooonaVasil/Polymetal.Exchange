using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Tekla.Structures.Model;

namespace Polymetal.Tekla.ReferenceModels
{
    static class Program
    {
        /// <summary>
        /// Главная точка входа для приложения.
        /// </summary>
        
        static void Main()
        {
            Model model;
            ModelInfo modelInfo;
            try
            {
                model = new Model();
                modelInfo = model.GetInfo();
            }
            catch
            {
                MessageBox.Show("Не запущен процесс Tekla Structures", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
           
            if (string.IsNullOrEmpty(modelInfo.ModelPath))
            {
                MessageBox.Show("Не удалось получить путь к папке модели", "Открытие модели", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var referenceModelPath = Path.Combine(model.GetInfo().ModelPath, "RefModels");
            if (!Directory.Exists(referenceModelPath))
            {
                MessageBox.Show("Не удалось найти папку с опорными моделями", "Предупреждение", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Form_ReferenceModel(model, referenceModelPath));
        }
    }
}
