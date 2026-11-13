namespace Tekla.Technology.Akit.UserScript
{
    public class Script
    {
        public static void Run(Tekla.Technology.Akit.IScript akit)
        {
			var modelPath = string.Empty;
            try
            {
                modelPath = new Tekla.Structures.Model.Model().GetInfo().ModelPath.ToString();
            }
            catch (System.Exception){}
			var opened = !modelPath.Equals(string.Empty);

			if (opened)
			{
				akit.ModalDialog(1);
				akit.Callback("acmd_exit_program", "", "main_frame");
				akit.ValueChange("SaveComment", "text_comment", "Импорт опорной модели PDMS " + System.DateTime.Now.ToString("dd.MM.yyyy HH:mm"));
				akit.PushButton("pushbutton_21", "SaveComment");
			}
			else
			{
				akit.ModalDialog(2);
				akit.Callback("acmd_exit_program", "", "main_frame");
			}
        }
    }
}
