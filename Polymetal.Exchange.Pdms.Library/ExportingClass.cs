using System;
using System.IO;
using Aveva.ApplicationFramework.Presentation;
using Aveva.PDMS.PMLNet;
using cmd = Aveva.Pdms.Utilities.CommandLine.Command;

namespace Polymetal.Exchange.Pdms.Library
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

        [PMLNetCallable()]
        public void ExportFromDesign()
        {
            WindowManager.Instance.MainForm.Shown += MainForm_Shown;            
            try
            {
                var triggerId = int.Parse(Environment.GetEnvironmentVariable("PDMSEXPORTTRIGGER"));
                Export.Run(triggerId);
            }
            catch (Exception exc)
            {
                using (var sw = new StreamWriter(@"c:\aveva\exchange.error", true))
                {
                    sw.WriteLine($"{DateTime.Now}\tPolymetal.Exchange.Pdms.Library.ExportingClass.ExportFromDesign\ttrigger:{Environment.GetEnvironmentVariable("PDMSEXPORTTRIGGER")}\t{exc.Message}");
                    sw.Close();
                }
                Console.WriteLine("Can't export project");
                Export.ExportFinishFlag = true;
                cmd.CreateCommand("quit finish").Run();
            }
        }

        private void MainForm_Shown(object sender, EventArgs e)
        {
            WindowManager.Instance.MainForm.Shown -= MainForm_Shown;
            cmd.CreateCommand("quit finish").Run();
        }
    }
}
