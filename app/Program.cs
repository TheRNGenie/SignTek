using System;
using System.Windows.Forms;

namespace CasinoSignTool
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += (s, e) =>
                MessageBox.Show(e.Exception.Message, "Casino Sign Tool", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Application.Run(new MainForm());
        }
    }
}
