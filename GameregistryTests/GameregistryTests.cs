using System.Diagnostics;
using System.Runtime.CompilerServices;
using AventStack.ExtentReports;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;

namespace GameregistryTests
{
    // Selectores y rutas verificados contra el HTML real renderizado por Gameregistry
    // (Pages/Login.cshtml, Pages/Videogames/*.cshtml) y contra las respuestas reales
    // del servidor (rutas, redirects). Ver notas de correccion junto a cada dato.
    public class GameregistryTests
    {
        private const string BaseUrl = "http://localhost:5245";
        private const string AdminUser = "admin";
        private const string AdminPassword = "admin123";

        // Pausa deliberada entre pasos para poder seguir los tests a simple vista en el
        // Chrome visible; no aporta nada a la logica de las pruebas.
        private static readonly TimeSpan StepDelay = TimeSpan.FromMilliseconds(800);
        private static void Pause() => Thread.Sleep(StepDelay);

        private IWebDriver _driver = null!;
        private WebDriverWait _wait = null!;
        private ExtentTest _extentTest = null!;

        // Estos tests pegan con Selenium contra un servidor HTTP real, no contra un
        // TestServer en memoria: sin esto, "dotnet test" fallaria con ERR_CONNECTION_REFUSED
        // salvo que alguien haya dejado "dotnet run" corriendo a mano en otra terminal.
        // Si el puerto ya responde (por ejemplo, lo tenes abierto en Visual Studio) lo
        // reusamos en vez de levantar una instancia extra.
        private static Process? _appProcess;
        private static bool _weStartedTheApp;

        [OneTimeSetUp]
        public static async Task StartAppIfNeededAsync()
        {
            using var probe = new HttpClient();

            if (await IsRespondingAsync(probe))
            {
                return;
            }

            var appProjectDir = Path.GetFullPath(Path.Combine(GetTestProjectDirectory(), "..", "Gameregistry"));

            var build = Process.Start(new ProcessStartInfo("dotnet", "build -c Debug")
            {
                WorkingDirectory = appProjectDir,
                UseShellExecute = false,
            })!;
            await build.WaitForExitAsync();
            if (build.ExitCode != 0)
            {
                throw new InvalidOperationException("No se pudo compilar Gameregistry para poder correr los tests.");
            }

            var dllPath = Directory.GetFiles(Path.Combine(appProjectDir, "bin", "Debug"), "Gameregistry.dll", SearchOption.AllDirectories).First();

            var startInfo = new ProcessStartInfo("dotnet", $"\"{dllPath}\"")
            {
                WorkingDirectory = appProjectDir, // necesario para que resuelva Videogamedb.db y wwwroot relativos al proyecto
                UseShellExecute = false,
            };
            startInfo.Environment["ASPNETCORE_URLS"] = BaseUrl;
            startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";

            _appProcess = Process.Start(startInfo);
            _weStartedTheApp = true;

            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (DateTime.UtcNow < deadline)
            {
                if (await IsRespondingAsync(probe))
                {
                    return;
                }

                await Task.Delay(500);
            }

            throw new InvalidOperationException($"Gameregistry no respondio en {BaseUrl} despues de 30s.");
        }

        [OneTimeTearDown]
        public static void StopAppIfWeStartedIt()
        {
            if (_weStartedTheApp && _appProcess is { HasExited: false })
            {
                _appProcess.Kill(entireProcessTree: true);
                _appProcess.WaitForExit(5000);
            }

            _appProcess?.Dispose();
        }

        private static async Task<bool> IsRespondingAsync(HttpClient client)
        {
            try
            {
                using var response = await client.GetAsync($"{BaseUrl}/Login");
                return response.IsSuccessStatusCode;
            }
            catch (HttpRequestException)
            {
                return false;
            }
        }

        private static string GetTestProjectDirectory([CallerFilePath] string sourceFilePath = "")
            => Path.GetDirectoryName(sourceFilePath)!;

        [SetUp]
        public void SetUp()
        {
            // Se crea el nodo del reporte al arrancar la prueba (no en TearDown) para que
            // la duracion que muestra ExtentReports incluya el tiempo real de la prueba,
            // no solo el tramo posterior a que termino de correr.
            _extentTest = ExtentReportSetup.Extent.CreateTest(TestContext.CurrentContext.Test.Name);

            var options = new ChromeOptions();
            options.AddArgument("--window-size=1280,800");

            _driver = new ChromeDriver(options);
            _wait = new WebDriverWait(_driver, TimeSpan.FromSeconds(5));
        }

        [TearDown]
        public void TearDown()
        {
            var resultado = TestContext.CurrentContext.Result;
            var estado = resultado.Outcome.Status;

            // Requisito: captura de pantalla de CADA escenario, no solo de los que fallan.
            // Se toma antes de _driver.Quit(): una vez cerrado el browser ya no hay nada
            // que capturar.
            var rutaCaptura = GuardarCapturaDePantalla(TestContext.CurrentContext.Test.Name, estado);

            switch (estado)
            {
                case NUnit.Framework.Interfaces.TestStatus.Failed:
                    _extentTest.Fail(resultado.Message ?? "La prueba fallo.");
                    break;
                case NUnit.Framework.Interfaces.TestStatus.Skipped:
                    _extentTest.Skip(resultado.Message ?? "La prueba fue omitida.");
                    break;
                default:
                    _extentTest.Pass("La prueba paso correctamente.");
                    break;
            }

            // Se adjunta despues del Pass/Fail/Skip para que la miniatura quede como el
            // ultimo log del nodo, junto al veredicto final de la prueba.
            if (rutaCaptura is not null)
            {
                _extentTest.AddScreenCaptureFromPath(rutaCaptura, "Captura al finalizar la prueba");
            }

            _driver.Quit();
            _driver.Dispose();
        }

        // Guarda una captura de pantalla del estado del browser al finalizar la prueba (haya
        // pasado, fallado o sido omitida) en la carpeta Capturas del proyecto, para poder
        // enlazarla/mostrarla como miniatura desde el reporte HTML. El nombre de archivo
        // incluye el nombre de la prueba y su resultado para poder identificarla a simple
        // vista dentro de la carpeta. Si la captura falla (p.ej. el driver ya no responde),
        // no debe tapar el resultado real de la prueba, por eso solo se registra y se sigue.
        private string? GuardarCapturaDePantalla(string nombrePrueba, NUnit.Framework.Interfaces.TestStatus estado)
        {
            try
            {
                var nombreArchivo = $"{nombrePrueba}_{estado}_{DateTime.Now:yyyyMMdd_HHmmss}.png";
                var rutaCompleta = Path.Combine(ExtentReportSetup.CapturasDir, nombreArchivo);

                ((ITakesScreenshot)_driver).GetScreenshot().SaveAsFile(rutaCompleta);

                // Se le pasa al reporte una ruta RELATIVA (Reportes y Capturas son carpetas
                // hermanas dentro del proyecto), no la ruta absoluta del disco: asi el HTML
                // sigue mostrando las imagenes aunque se mueva o se copie la carpeta del
                // proyecto a otra maquina, en vez de depender de este path exacto.
                var reportesDir = Path.GetDirectoryName(ExtentReportSetup.ReportPath)!;
                var rutaRelativa = Path.GetRelativePath(reportesDir, rutaCompleta).Replace('\\', '/');

                return rutaRelativa;
            }
            catch (Exception ex)
            {
                TestContext.Progress.WriteLine($"No se pudo guardar la captura de pantalla: {ex.Message}");
                return null;
            }
        }

        // Corregido: el login exitoso sin ReturnUrl redirige a "/Index" (home), no al
        // listado. Para que el flujo termine en el listado (como haria un usuario real
        // al que la app le pide loguearse antes de ver el CRUD) arrancamos navegando a
        // la pagina protegida; eso deja el ReturnUrl armado y el POST nos devuelve ahi.
        private void Login(string username, string password, string returnPath = "/Videogames/Index")
        {
            _driver.Navigate().GoToUrl($"{BaseUrl}{returnPath}");
            _wait.Until(d => d.Url.Contains("/Login"));
            Pause();

            // Corregido: los ids reales son "Username" y "Password" (coincidian con la
            // suposicion original). El boton de submit NO tiene id (es <button type="submit">
            // sin atributo id), asi que se localiza por selector de tipo dentro del form.
            _driver.FindElement(By.Id("Username")).SendKeys(username);
            Pause();
            _driver.FindElement(By.Id("Password")).SendKeys(password);
            Pause();
            _driver.FindElement(By.CssSelector("form button[type='submit']")).Click();

            // El click dispara un POST que redirige de forma asincronica; sin esta espera,
            // una navegacion inmediatamente posterior (p.ej. ir a /Videogames/Create) puede
            // dispararse mientras el redirect del login todavia esta en vuelo y perder la carrera.
            // Con credenciales invalidas no hay redirect (la pagina se re-renderiza en /Login
            // con el error), asi que esperamos cualquiera de los dos desenlaces posibles.
            _wait.Until(d => !d.Url.Contains("/Login") || d.FindElements(By.CssSelector(".alert-danger")).Count > 0);
            Pause();
        }

        // El id real de un videojuego es un dato de la base (mutable entre corridas), no algo
        // derivable del HTML o de las rutas. Asumir un id fijo (p.ej. "1") es tan poco confiable
        // como asumir un selector sin verificarlo: si otro test ya borro ese registro, deja de
        // existir. Por eso Editar/Eliminar crean su propio videojuego y navegan siguiendo los
        // links reales del listado en vez de construir la URL con un id supuesto.
        private void CreateVideogame(string name, string description, string publisher, string genre, string releaseYear)
        {
            _driver.Navigate().GoToUrl($"{BaseUrl}/Videogames/Create");
            Pause();

            _driver.FindElement(By.Id("Videogames_Name")).SendKeys(name);
            Pause();
            _driver.FindElement(By.Id("Videogames_Description")).SendKeys(description);
            Pause();
            _driver.FindElement(By.Id("Videogames_Publisher")).SendKeys(publisher);
            Pause();
            _driver.FindElement(By.Id("Videogames_Genre")).SendKeys(genre);
            Pause();
            _driver.FindElement(By.Id("Videogames_ReleaseYear")).SendKeys(releaseYear);
            Pause();
            _driver.FindElement(By.CssSelector("input[type='submit']")).Click();

            _wait.Until(d => d.FindElements(By.CssSelector(".game-card")).Any(c => c.Text.Contains(name)));
            Pause();
        }

        private IWebElement FindCardLinkByGameName(string gameName, string linkText)
        {
            var card = _wait.Until(d => d.FindElements(By.CssSelector(".game-card")).FirstOrDefault(c => c.Text.Contains(gameName)));
            return card!.FindElement(By.LinkText(linkText));
        }

        // No hay una base de datos separada para tests: Selenium corre contra la misma
        // Videogamedb.db real de la app. Sin esta limpieza, cada corrida de un test que crea
        // un videojuego (Crear, Editar) deja un registro nuevo acumulado en los datos reales.
        private void DeleteVideogameByName(string gameName)
        {
            FindCardLinkByGameName(gameName, "Eliminar").Click();
            Pause();
            _driver.FindElement(By.CssSelector("input[type='submit']")).Click();
            _wait.Until(d => d.Url.Contains("/Videogames"));
            Pause();
        }

        [Test]
        public void Login_ConCredencialesValidas_RedirigeAlListado()
        {
            Login(AdminUser, AdminPassword);

            // Corregido: la carpeta real se llama "Videogames" (en ingles), no "Videojuegos".
            _wait.Until(d => d.Url.Contains("/Videogames"));

            Assert.That(_driver.Url, Does.Contain("/Videogames"));
        }

        [Test]
        public void Login_ConCredencialesInvalidas_MuestraError()
        {
            Login("usuario-incorrecto", "clave-incorrecta");

            // Corregido: el mensaje de error se muestra en un <div class="alert alert-danger">,
            // no en ".validation-summary-errors" (eso es de validacion de modelo de MVC, y esta
            // pagina no la usa para credenciales invalidas).
            var error = _wait.Until(d => d.FindElement(By.CssSelector(".alert-danger")));
            Pause();

            // Corregido: el texto real es en espanol ("Usuario o contrasena incorrectos."),
            // no en ingles.
            Assert.That(error.Text, Does.Contain("incorrectos"));
        }

        // Prueba de LIMITE: campos presentes pero vacios de espacios en blanco. LoginModel
        // no tiene [Required] en Username/Password (compara texto plano contra appsettings),
        // asi que esto viaja al servidor igual que cualquier intento fallido y cae en el
        // mismo camino que credenciales invalidas.
        [Test]
        public void Login_ConCredencialesEnBlanco_NoPermiteElAcceso()
        {
            Login("   ", "   ");

            var error = _wait.Until(d => d.FindElement(By.CssSelector(".alert-danger")));
            Pause();

            Assert.That(error.Text, Does.Contain("incorrectos"));

            // Confirmamos que efectivamente no quedamos autenticados: la pagina protegida
            // nos sigue mandando a Login.
            _driver.Navigate().GoToUrl($"{BaseUrl}/Videogames/Index");
            _wait.Until(d => d.Url.Contains("/Login"));
            Pause();

            Assert.That(_driver.Url, Does.Contain("/Login"));
        }

        [Test]
        public void ListadoDeVideojuegos_SinSesion_RedirigeALogin()
        {
            // Corregido: ruta real "/Videogames/Index", no "/Videojuegos".
            _driver.Navigate().GoToUrl($"{BaseUrl}/Videogames/Index");

            _wait.Until(d => d.Url.Contains("/Login"));
            Pause();

            Assert.That(_driver.Url, Does.Contain("/Login"));
        }

        [Test]
        public void CrearVideojuego_ConDatosValidos_ApareceEnElListado()
        {
            Login(AdminUser, AdminPassword);

            // Corregido: ruta real "/Videogames/Create", no "/Videojuegos/Crear".
            _driver.Navigate().GoToUrl($"{BaseUrl}/Videogames/Create");
            Pause();

            // Corregido: los inputs se generan con el prefijo del modelo anidado
            // ("Videogames.Name" -> id="Videogames_Name"), no "Name" a secas.
            const string nombre = "Hollow Knight";
            _driver.FindElement(By.Id("Videogames_Name")).SendKeys(nombre);
            Pause();
            _driver.FindElement(By.Id("Videogames_Description")).SendKeys("Metroidvania ambientado en Hallownest");
            Pause();
            _driver.FindElement(By.Id("Videogames_Publisher")).SendKeys("Team Cherry");
            Pause();
            _driver.FindElement(By.Id("Videogames_Genre")).SendKeys("Metroidvania");
            Pause();
            _driver.FindElement(By.Id("Videogames_ReleaseYear")).SendKeys("2017");
            Pause();
            // Corregido: el boton es <input type="submit" value="Crear"> sin id.
            _driver.FindElement(By.CssSelector("input[type='submit']")).Click();

            // Corregido: el listado ya no es una <table>, se renderiza como tarjetas
            // ".game-card" dentro de ".game-grid".
            _wait.Until(d => d.FindElements(By.CssSelector(".game-card")).Count > 0);
            Pause();

            try
            {
                var tarjetas = _driver.FindElements(By.CssSelector(".game-card"));
                Assert.That(tarjetas.Any(t => t.Text.Contains(nombre)), Is.True);
            }
            finally
            {
                DeleteVideogameByName(nombre);
            }
        }

        // Prueba NEGATIVA: "Name" es [Required] en el modelo y la pagina carga
        // _ValidationScriptsPartial (jquery.validate + unobtrusive), asi que un Name vacio
        // ni siquiera llega a postearse: el submit queda bloqueado en el cliente y la pagina
        // se queda en /Videogames/Create mostrando el error inline.
        [Test]
        public void CrearVideojuego_ConNombreVacio_NoLoCrea()
        {
            Login(AdminUser, AdminPassword);

            _driver.Navigate().GoToUrl($"{BaseUrl}/Videogames/Create");
            Pause();

            // "Videogames_Name" se deja vacio a proposito; el resto de los campos validos.
            _driver.FindElement(By.Id("Videogames_Description")).SendKeys("Descripcion de prueba");
            Pause();
            _driver.FindElement(By.Id("Videogames_Publisher")).SendKeys("Publisher de prueba");
            Pause();
            _driver.FindElement(By.Id("Videogames_Genre")).SendKeys("Accion");
            Pause();
            _driver.FindElement(By.Id("Videogames_ReleaseYear")).SendKeys("2020");
            Pause();
            _driver.FindElement(By.CssSelector("input[type='submit']")).Click();

            // El span de validacion para "Videogames.Name" siempre esta en el DOM (vacio por
            // defecto); esperamos a que jquery.validate le escriba el mensaje de error.
            var errorName = _wait.Until(d =>
            {
                var span = d.FindElement(By.CssSelector("span[data-valmsg-for='Videogames.Name']"));
                return string.IsNullOrEmpty(span.Text) ? null : span;
            });
            Pause();

            Assert.That(_driver.Url, Does.Contain("/Videogames/Create"));
            Assert.That(errorName!.Text, Is.Not.Empty);
        }

        // Prueba de LIMITE: el campo real tiene maxlength="50" (de [StringLength(50)] en el
        // modelo), asi que el propio Chrome corta lo que se escribe ahi. No hay ninguna regla
        // que "rechace" un nombre largo (el maxlength ya lo impide antes de llegar a
        // validarse), asi que el limite real a verificar es que un nombre de 60 caracteres
        // termina guardado truncado a exactamente 50, no rechazado.
        [Test]
        public void CrearVideojuego_ConNombreMuyLargo_SeTruncaAlLimitePermitido()
        {
            Login(AdminUser, AdminPassword);

            _driver.Navigate().GoToUrl($"{BaseUrl}/Videogames/Create");
            Pause();

            var nombreLargo = new string('A', 60);
            var nombreEsperado = nombreLargo.Substring(0, 50);

            _driver.FindElement(By.Id("Videogames_Name")).SendKeys(nombreLargo);
            Pause();
            _driver.FindElement(By.Id("Videogames_Description")).SendKeys("Prueba de limite de longitud de nombre");
            Pause();
            _driver.FindElement(By.Id("Videogames_Publisher")).SendKeys("Publisher de prueba");
            Pause();
            _driver.FindElement(By.Id("Videogames_Genre")).SendKeys("Accion");
            Pause();
            _driver.FindElement(By.Id("Videogames_ReleaseYear")).SendKeys("2020");
            Pause();
            _driver.FindElement(By.CssSelector("input[type='submit']")).Click();

            _wait.Until(d => d.FindElements(By.CssSelector(".game-card")).Any(c => c.Text.Contains(nombreEsperado)));
            Pause();

            try
            {
                var tarjetas = _driver.FindElements(By.CssSelector(".game-card"));
                Assert.That(tarjetas.Any(t => t.Text.Contains(nombreEsperado)), Is.True);
                Assert.That(tarjetas.Any(t => t.Text.Contains(nombreLargo)), Is.False);
            }
            finally
            {
                DeleteVideogameByName(nombreEsperado);
            }
        }

        [Test]
        public void EditarVideojuego_ActualizaLosDatos()
        {
            Login(AdminUser, AdminPassword);

            const string nombreOriginal = "Chrono Trigger";
            CreateVideogame(nombreOriginal, "RPG clasico de Square", "Square", "RPG", "1995");

            // Corregido: se navega con el link real de la tarjeta ("Editar", que apunta a
            // /Videogames/Edit?id=<el real>), en vez de armar "/Videojuegos/Editar/1" a mano.
            FindCardLinkByGameName(nombreOriginal, "Editar").Click();
            Pause();

            const string nombreEditado = "Chrono Trigger: Edicion Definitiva";
            var nombre = _driver.FindElement(By.Id("Videogames_Name"));
            nombre.Clear();
            Pause();
            nombre.SendKeys(nombreEditado);
            Pause();
            _driver.FindElement(By.CssSelector("input[type='submit']")).Click();

            _wait.Until(d => d.FindElements(By.CssSelector(".game-card")).Count > 0);
            Pause();

            try
            {
                var tarjetas = _driver.FindElements(By.CssSelector(".game-card"));
                Assert.That(tarjetas.Any(t => t.Text.Contains("Edicion Definitiva")), Is.True);
            }
            finally
            {
                DeleteVideogameByName(nombreEditado);
            }
        }

        // Prueba NEGATIVA: mismo mecanismo que en Crear (validacion cliente bloquea el
        // submit con "Name" vacio), pero en Edit. El juego original no debe cambiar.
        [Test]
        public void EditarVideojuego_ConNombreVacio_NoGuardaLosCambios()
        {
            Login(AdminUser, AdminPassword);

            const string nombreOriginal = "Xenogears";
            CreateVideogame(nombreOriginal, "RPG de Square", "Square", "RPG", "1998");

            try
            {
                FindCardLinkByGameName(nombreOriginal, "Editar").Click();
                Pause();

                _driver.FindElement(By.Id("Videogames_Name")).Clear();
                Pause();
                _driver.FindElement(By.CssSelector("input[type='submit']")).Click();

                var errorName = _wait.Until(d =>
                {
                    var span = d.FindElement(By.CssSelector("span[data-valmsg-for='Videogames.Name']"));
                    return string.IsNullOrEmpty(span.Text) ? null : span;
                });
                Pause();

                Assert.That(_driver.Url, Does.Contain("/Videogames/Edit"));
                Assert.That(errorName!.Text, Is.Not.Empty);

                _driver.Navigate().GoToUrl($"{BaseUrl}/Videogames/Index");
                Pause();
                var tarjetas = _wait.Until(d => d.FindElements(By.CssSelector(".game-card")));
                Assert.That(tarjetas.Any(t => t.Text.Contains(nombreOriginal)), Is.True);
            }
            finally
            {
                DeleteVideogameByName(nombreOriginal);
            }
        }

        // Prueba de LIMITE: mismo criterio que en Crear (maxlength="50" trunca en el cliente),
        // pero editando un registro existente.
        [Test]
        public void EditarVideojuego_ConNombreMuyLargo_SeTruncaAlLimitePermitido()
        {
            Login(AdminUser, AdminPassword);

            const string nombreOriginal = "Xenosaga";
            CreateVideogame(nombreOriginal, "RPG de Namco", "Namco", "RPG", "2002");

            var nombreLargo = new string('B', 60);
            var nombreEsperado = nombreLargo.Substring(0, 50);

            FindCardLinkByGameName(nombreOriginal, "Editar").Click();
            Pause();

            var nombre = _driver.FindElement(By.Id("Videogames_Name"));
            nombre.Clear();
            Pause();
            nombre.SendKeys(nombreLargo);
            Pause();
            _driver.FindElement(By.CssSelector("input[type='submit']")).Click();

            _wait.Until(d => d.FindElements(By.CssSelector(".game-card")).Any(c => c.Text.Contains(nombreEsperado)));
            Pause();

            try
            {
                var tarjetas = _driver.FindElements(By.CssSelector(".game-card"));
                Assert.That(tarjetas.Any(t => t.Text.Contains(nombreEsperado)), Is.True);
                Assert.That(tarjetas.Any(t => t.Text.Contains(nombreLargo)), Is.False);
            }
            finally
            {
                DeleteVideogameByName(nombreEsperado);
            }
        }

        [Test]
        public void EliminarVideojuego_LoQuitaDelListado()
        {
            Login(AdminUser, AdminPassword);

            const string nombre = "Chrono Cross";
            CreateVideogame(nombre, "RPG, secuela espiritual de Chrono Trigger", "Square", "RPG", "1999");

            // Corregido: mismo criterio que en Editar, se sigue el link real ("Eliminar") en
            // vez de armar "/Videojuegos/Eliminar/1" a mano.
            FindCardLinkByGameName(nombre, "Eliminar").Click();
            Pause();
            // Corregido: el boton de confirmacion real es <input type="submit" value="Eliminar"> sin id.
            _driver.FindElement(By.CssSelector("input[type='submit']")).Click();

            _wait.Until(d => d.Url.Contains("/Videogames"));
            Pause();

            var tarjetas = _driver.FindElements(By.CssSelector(".game-card"));
            Assert.That(tarjetas.Any(t => t.Text.Contains(nombre)), Is.False);
        }

        // Prueba NEGATIVA/LIMITE: confirmado con curl que un id que no existe en la base
        // responde con HTTP 404 y cuerpo vacio (DeleteModel.OnGetAsync -> NotFound() cuando
        // FirstOrDefaultAsync no encuentra nada). Corregido tras correrlo: Chrome no deja el
        // body vacio, renderiza su propia pagina de error ("HTTP ERROR 404") para una
        // respuesta sin contenido, asi que se verifica esa pagina en vez de un body vacio.
        [Test]
        public void EliminarVideojuego_ConIdInexistente_RespondeConError()
        {
            Login(AdminUser, AdminPassword);

            _driver.Navigate().GoToUrl($"{BaseUrl}/Videogames/Delete?id=999999");
            Pause();

            Assert.That(_driver.FindElements(By.CssSelector("input[type='submit']")), Is.Empty);
            Assert.That(_driver.FindElement(By.TagName("body")).Text, Does.Contain("404"));
        }

        // Camino feliz que faltaba: ver el detalle de un videojuego real.
        [Test]
        public void VerDetallesDeVideojuego_MuestraLosDatosCompletos()
        {
            Login(AdminUser, AdminPassword);

            const string nombre = "Suikoden II";
            CreateVideogame(nombre, "108 estrellas del destino", "Konami", "RPG", "1998");

            try
            {
                // Confirmado en Index.cshtml: el link real de la tarjeta es "Detalles", que
                // apunta a /Videogames/Details?id=<el real>.
                FindCardLinkByGameName(nombre, "Detalles").Click();
                Pause();

                // El <h1> de Details.cshtml es directamente el nombre del juego, no dice "Detalles".
                var titulo = _wait.Until(d => d.FindElement(By.TagName("h1")));
                Pause();
                Assert.That(titulo.Text, Is.EqualTo(nombre));

                var cuerpo = _driver.FindElement(By.CssSelector(".detail-card")).Text;
                Assert.That(cuerpo, Does.Contain("108 estrellas del destino"));
                Assert.That(cuerpo, Does.Contain("Konami"));
                Assert.That(cuerpo, Does.Contain("RPG"));
                Assert.That(cuerpo, Does.Contain("1998"));
            }
            finally
            {
                _driver.Navigate().GoToUrl($"{BaseUrl}/Videogames/Index");
                DeleteVideogameByName(nombre);
            }
        }

        [Test]
        public void Logout_RedirigeAlHomeYProtegeElCrud()
        {
            Login(AdminUser, AdminPassword);

            // Corregido: no existe un link/boton con id "btnLogout" navegable por URL directa.
            // El logout es un <form method="post" action="/Logout"> con un <button> adentro,
            // ubicado en el nav de _Layout.cshtml; hay que clickearlo desde una pagina que
            // renderice el layout (ya estamos en el listado tras el login).
            _driver.FindElement(By.CssSelector("form[action='/Logout'] button[type='submit']")).Click();
            Pause();

            _driver.Navigate().GoToUrl($"{BaseUrl}/Videogames/Index");
            _wait.Until(d => d.Url.Contains("/Login"));
            Pause();

            Assert.That(_driver.Url, Does.Contain("/Login"));
        }
    }
}
