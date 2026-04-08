using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using LiveChartsCore.SkiaSharpView;
using Microsoft.Extensions.Configuration;
using System;
using System.IO;

namespace DexInstructionRunner
{
    public partial class App : Application
    {
        private static StyleInclude _highContrastStyle;

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            try
            {
                var config = new ConfigurationBuilder()
                    .SetBasePath(Directory.GetCurrentDirectory())  // instead of AppContext.BaseDirectory
                    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                    .Build();


                string theme = config["AppSettings:Theme"] ?? "Default";
                string accessibility = config["AppSettings:Accessibility"] ?? "Disabled";

                SetTheme(theme);
                SetHighContrast(accessibility == "HighContrast");

                LiveChartsCore.LiveCharts.Configure(config =>
                    config.AddSkiaSharp().AddDefaultMappers().AddDarkTheme());

                if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                {
                    desktop.MainWindow = new MainWindow();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("⚠️ App crashed during initialization:");
                Console.WriteLine(ex);
                File.WriteAllText("startup_crash.log", ex.ToString());
                Environment.Exit(1);
            }

            base.OnFrameworkInitializationCompleted();
        }

        public static void SetTheme(string theme)
        {
            // Optionally set the requested theme variant to follow OS (Default) or force Light/Dark
            if (theme.Equals("Dark", StringComparison.OrdinalIgnoreCase))
                Current.RequestedThemeVariant = ThemeVariant.Dark;
            else if (theme.Equals("Light", StringComparison.OrdinalIgnoreCase))
                Current.RequestedThemeVariant = ThemeVariant.Light;
            else
                Current.RequestedThemeVariant = ThemeVariant.Default;
        }

        // Optional: extra resource for Default

        public static void SetHighContrast(bool enable)
        {
            var app = (App)Current;

            if (_highContrastStyle != null)
                app.Styles.Remove(_highContrastStyle);

            if (enable)
            {
                _highContrastStyle = new StyleInclude(new Uri("avares://DexInstructionRunner/"))
                {
                    Source = new Uri("avares://DexInstructionRunner/Styles/HighContrast.xaml")
                };
                app.Styles.Add(_highContrastStyle);
            }
            else
            {
                _highContrastStyle = null;
            }
        }
    }
}
