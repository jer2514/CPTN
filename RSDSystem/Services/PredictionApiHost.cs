using System.Diagnostics;
using System.Net.Http;

namespace RSDSystem.Services
{
    /// <summary>
    /// Starts the Python FastAPI prediction service next to the web app in Development.
    /// Production hosts the API separately and sets Prediction:ApiUrl.
    /// </summary>
    public static class PredictionApiHost
    {
        private static Process? _process;

        public static void TryStart(WebApplication app)
        {
            var config = app.Configuration;
            var apiUrl = (config["Prediction:ApiUrl"] ?? "").Trim();
            if (apiUrl.Length == 0)
                return;

            var autoStart = config.GetValue("Prediction:AutoStart", app.Environment.IsDevelopment());
            if (!autoStart)
                return;

            if (!Uri.TryCreate(apiUrl, UriKind.Absolute, out var uri) || !IsLoopback(uri))
                return;

            if (IsHealthy(apiUrl))
            {
                Console.WriteLine("Python prediction API already running at " + apiUrl);
                return;
            }

            var folder = FindApiFolder(app.Environment.ContentRootPath);
            if (folder == null)
            {
                Console.WriteLine("Python prediction API folder was not found. Load Prediction will use the local formula until the API is started.");
                return;
            }

            var python = FindPython();
            if (python == null)
            {
                Console.WriteLine("Python was not found. Start prediction-api/run.bat (or run.sh) so Load Prediction can use the Python model.");
                return;
            }

            TryInstall(python, folder);

            var port = uri.IsDefaultPort ? (uri.Scheme == "https" ? 443 : 80) : uri.Port;
            var start = new ProcessStartInfo
            {
                FileName = python,
                WorkingDirectory = folder,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            start.ArgumentList.Add("-m");
            start.ArgumentList.Add("uvicorn");
            start.ArgumentList.Add("app:app");
            start.ArgumentList.Add("--host");
            start.ArgumentList.Add(uri.Host);
            start.ArgumentList.Add("--port");
            start.ArgumentList.Add(port.ToString());

            var apiKey = (config["Prediction:ApiKey"] ?? "").Trim();
            if (apiKey.Length > 0)
                start.Environment["PREDICTION_API_KEY"] = apiKey;

            try
            {
                _process = Process.Start(start);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Could not start the Python prediction API: " + ex.Message);
                return;
            }

            if (_process == null)
                return;

            _process.EnableRaisingEvents = true;
            AppDomain.CurrentDomain.ProcessExit += (_, _) => Stop();

            var ready = WaitForHealthy(apiUrl, TimeSpan.FromSeconds(12));
            Console.WriteLine(ready
                ? "Python prediction API started at " + apiUrl
                : "Python prediction API did not become ready. Load Prediction will fall back to the local formula.");
        }

        private static bool IsLoopback(Uri uri) =>
            uri.Host is "127.0.0.1" or "localhost" or "::1";

        private static string? FindApiFolder(string contentRoot)
        {
            var candidates = new[]
            {
                Path.GetFullPath(Path.Combine(contentRoot, "prediction-api")),
                Path.GetFullPath(Path.Combine(contentRoot, "..", "prediction-api")),
                Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "prediction-api")),
                Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "prediction-api"))
            };

            return candidates.FirstOrDefault(path =>
                File.Exists(Path.Combine(path, "app.py")) && File.Exists(Path.Combine(path, "model.py")));
        }

        private static string? FindPython()
        {
            foreach (var name in new[] { "python3", "python", "py" })
            {
                try
                {
                    var probe = new ProcessStartInfo
                    {
                        FileName = name,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    probe.ArgumentList.Add("--version");
                    using var process = Process.Start(probe);
                    if (process == null)
                        continue;
                    if (!process.WaitForExit(4000) || process.ExitCode != 0)
                        continue;
                    return name;
                }
                catch
                {
                    // try the next name
                }
            }

            return null;
        }

        private static void TryInstall(string python, string folder)
        {
            try
            {
                var info = new ProcessStartInfo
                {
                    FileName = python,
                    WorkingDirectory = folder,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                info.ArgumentList.Add("-c");
                info.ArgumentList.Add("import fastapi, uvicorn, sklearn");
                using (var check = Process.Start(info))
                {
                    if (check != null && check.WaitForExit(8000) && check.ExitCode == 0)
                        return;
                }

                Console.WriteLine("Installing Python prediction API packages...");
                var pip = new ProcessStartInfo
                {
                    FileName = python,
                    WorkingDirectory = folder,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                pip.ArgumentList.Add("-m");
                pip.ArgumentList.Add("pip");
                pip.ArgumentList.Add("install");
                pip.ArgumentList.Add("-r");
                pip.ArgumentList.Add("requirements.txt");
                using var install = Process.Start(pip);
                install?.WaitForExit(120000);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Python prediction package check failed: " + ex.Message);
            }
        }

        private static bool WaitForHealthy(string apiUrl, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (IsHealthy(apiUrl))
                    return true;
                Thread.Sleep(400);
            }

            return IsHealthy(apiUrl);
        }

        private static bool IsHealthy(string apiUrl)
        {
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
                using var response = client.GetAsync(apiUrl.TrimEnd('/') + "/health").GetAwaiter().GetResult();
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        private static void Stop()
        {
            try
            {
                if (_process is { HasExited: false })
                    _process.Kill(entireProcessTree: true);
            }
            catch
            {
                // shutting down
            }
        }
    }
}
