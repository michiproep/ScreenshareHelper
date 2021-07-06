using CommandLine;
using ScreenshareHelper.Properties;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ScreenshareHelper
{
    static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            Form1 mainForm = null;
            
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Parser.Default.ParseArguments<Options>(args)
                .WithParsed(o => mainForm = new Form1(o))
                .WithNotParsed(o => mainForm = new Form1());


            Application.Run(mainForm);
        }


        
    }
}
