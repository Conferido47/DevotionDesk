using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;

namespace DevotionDesk
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            ApplyThemeFromSettings();

            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = RunStartupSequenceAsync();
        }

        private async Task RunStartupSequenceAsync()
        {
            var splash = new SplashWindow();
            splash.Show();

            try
            {
                await splash.WaitForDoneAsync();
            }
            catch
            {
                // Best-effort: still proceed to main window.
            }

            try { splash.Close(); } catch { }

            var main = new MainWindow();
            MainWindow = main;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            main.Show();
        }

        private void ApplyThemeFromSettings()
        {
            var mode = ReadThemeModeFromSettings();
            ApplyTheme(mode);
        }

        private static string ReadThemeModeFromSettings()
        {
            try
            {
                var appDataDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "DevotionDesk"
                );

                var settingsPath = Path.Combine(appDataDir, "settings.json");
                if (!File.Exists(settingsPath))
                    return "Light";

                using var doc = JsonDocument.Parse(File.ReadAllText(settingsPath));
                if (!doc.RootElement.TryGetProperty("Theme", out var prop))
                    return "Light";

                var s = prop.GetString();
                if (string.IsNullOrWhiteSpace(s))
                    return "Light";

                return s.Trim();
            }
            catch
            {
                return "Light";
            }
        }

        public void ApplyTheme(string mode)
        {
            var normalized = (mode ?? string.Empty).Trim().ToLowerInvariant();
            var source = normalized switch
            {
                "dark" => "Themes/Theme.Dark.xaml",
                "light" => "Themes/Theme.Light.xaml",
                _ => "Themes/Theme.Light.xaml"
            };

            // Replace first merged dictionary (our theme slot).
            if (Resources.MergedDictionaries.Count == 0)
            {
                Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(source, UriKind.Relative) });
                return;
            }

            Resources.MergedDictionaries[0] = new ResourceDictionary { Source = new Uri(source, UriKind.Relative) };
        }
    }

}
