using Velopack;

namespace NaukaMatematyki_LO
{
    internal static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            // Musi być pierwsze: Velopack kończy tu instalację aktualizacji i dopiero potem odpala program.
            VelopackApp.Build().Run();
            ApplicationConfiguration.Initialize();
            Application.SetColorMode(SystemColorMode.Dark);
            Application.Run(new MainForm());
        }
    }
}