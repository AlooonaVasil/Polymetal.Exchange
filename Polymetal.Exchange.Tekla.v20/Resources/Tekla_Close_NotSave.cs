namespace Tekla.Technology.Akit.UserScript
{
    public class Script
    {
        public static void Run(Tekla.Technology.Akit.IScript akit)
        {
            akit.ModalDialog(2);
			akit.Callback("acmd_exit_program", "", "main_frame");
        }
    }
}
