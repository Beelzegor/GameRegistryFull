using System.Runtime.CompilerServices;
using AventStack.ExtentReports;
using AventStack.ExtentReports.Reporter;

namespace GameregistryTests
{
    // Un [SetUpFixture] corre una unica vez por assembly: su OneTimeSetUp se ejecuta
    // antes que el OneTimeSetUp de cualquier [TestFixture] y su OneTimeTearDown despues
    // de todos los OneTimeTearDown. Es el lugar correcto para abrir y hacer Flush() del
    // reporte una sola vez por corrida completa de "dotnet test", sin importar cuantas
    // clases de prueba haya.
    [SetUpFixture]
    public class ExtentReportSetup
    {
        public static ExtentReports Extent { get; private set; } = null!;
        public static string CapturasDir { get; private set; } = null!;
        public static string ReportPath { get; private set; } = null!;

        [OneTimeSetUp]
        public void IniciarReporte()
        {
            var projectDir = GetProjectDirectory();

            var reportesDir = Path.Combine(projectDir, "Reportes");
            Directory.CreateDirectory(reportesDir);

            CapturasDir = Path.Combine(projectDir, "Capturas");
            Directory.CreateDirectory(CapturasDir);

            ReportPath = Path.Combine(reportesDir, "ReporteEjecucion.html");

            var sparkReporter = new ExtentSparkReporter(ReportPath);
            Extent = new ExtentReports();
            Extent.AttachReporter(sparkReporter);
        }

        [OneTimeTearDown]
        public void CerrarReporte()
        {
            Extent.Flush();
            TestContext.Progress.WriteLine($"Reporte HTML generado en: {ReportPath}");
        }

        // CallerFilePath resuelve la carpeta del proyecto en tiempo de compilacion, sin
        // depender del directorio de trabajo con el que "dotnet test" arranca el proceso
        // (que es bin/Debug/net10.0, no la carpeta del .csproj).
        private static string GetProjectDirectory([CallerFilePath] string sourceFilePath = "")
            => Path.GetDirectoryName(sourceFilePath)!;
    }
}
