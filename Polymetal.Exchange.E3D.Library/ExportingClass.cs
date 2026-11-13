using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Aveva.ApplicationFramework.Presentation;
using Aveva.Core.PMLNet;
using cmd = Aveva.Core.Utilities.CommandLine.Command;

namespace Polymetal.Exchange.E3D.Library
{
    [PMLNetCallable()]
    public class ExportingClass
    {
        [PMLNetCallable()]
        public ExportingClass()
        {
        }

        [PMLNetCallable()]
        public void Assign(ExportingClass that)
        {
        }

        private static WindowManager _windowManager;

        [PMLNetCallable()]
        public void ExportFromDesign(double triggerId)
        {
            //_windowManager = (DependencyResolver.GetImplementationOf<IWindowManager>() as WindowManager);
            //_windowManager.MainForm.Shown += MainForm_Shown;          
            AppDomain.CurrentDomain.AssemblyResolve += CurrentDomain_AssemblyResolve;
            try
            {
                Export.Run((int)triggerId);
            }
            catch (Exception exc)
            {
                using (var sw = new StreamWriter(@"c:\aveva\exchange.error", true))
                {
                    sw.WriteLine($"{DateTime.Now}\tPolymetal.Exchange.E3D.Library.ExportingClass.ExportFromDesign\ttrigger:{triggerId}\t{exc}");
                    sw.Close();
                }
                Console.WriteLine("Can't export project");
                Export.ExportFinishFlag = true;
                cmd.CreateCommand("quit finish").Run();
            }            
        }

        private Assembly CurrentDomain_AssemblyResolve(object sender, ResolveEventArgs args)
        {
            var dllName = args.Name.Split().First().Replace(",", ".dll");
            var probingPath = Environment.GetEnvironmentVariable("AVEVA_DESIGN_INSTALLED_DIR");
            var file = Directory.Exists(probingPath) ? Directory.GetFiles(probingPath, dllName).FirstOrDefault() : string.Empty;
            if (!string.IsNullOrEmpty(file)) return Assembly.LoadFile(file);

            probingPath = Path.Combine(probingPath, "PolyDLL");
            file = Directory.Exists(probingPath) ? Directory.GetFiles(probingPath, dllName).FirstOrDefault() : string.Empty;
            if (!string.IsNullOrEmpty(file)) return Assembly.LoadFile(file);

            return null;
        }

        private void MainForm_Shown(object sender, EventArgs e)
        {
            _windowManager.MainForm.Shown -= MainForm_Shown;
            cmd.CreateCommand("quit finish").Run();
        }
    }
}
