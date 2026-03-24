using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using xScreenPen.Models;

namespace xScreenPen
{
    public partial class SettingsWindow : Window
    {
        private const string ProjectHomeUrl = "https://gitee.com/diao1548/xScreenPen";
        private const string ReleasesPageUrl = "https://gitee.com/diao1548/xScreenPen/releases";
        private const string LatestReleaseApiUrl = "https://gitee.com/api/v5/repos/diao1548/xScreenPen/releases/latest";

        private readonly Action<PenSettings> _onSettingsChanged;
        private PenSettings _workingSettings;
        private bool _isApplyingUi;
        private Button _selectedColorButton;
        private Button _selectedSizeButton;
        private bool _isCheckingUpdate;
        private bool _isCloseAnimationRunning;
        private bool _allowClose;

        [DataContract]
        private sealed class ReleaseResponse
        {
            [DataMember(Name = "tag_name")]
            public string TagName { get; set; }

            [DataMember(Name = "assets")]
            public ReleaseAsset[] Assets { get; set; }
        }

        [DataContract]
        private sealed class ReleaseAsset
        {
            [DataMember(Name = "name")]
            public string Name { get; set; }

            [DataMember(Name = "browser_download_url")]
            public string BrowserDownloadUrl { get; set; }
        }

        public SettingsWindow(PenSettings initialSettings, Action<PenSettings> onSettingsChanged)
        {
            InitializeComponent();
            _onSettingsChanged = onSettingsChanged;
            _workingSettings = (initialSettings ?? new PenSettings()).Clone();

            ApplySettingsToUi();
            UpdateProjectInfo();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            BeginOpenAnimation();
            Activate();
            Focus();
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            if (_allowClose || _isCloseAnimationRunning)
            {
                return;
            }

            // Allow immediate close when app is shutting down.
            if (Application.Current?.MainWindow == null || !Application.Current.MainWindow.IsVisible)
            {
                return;
            }

            e.Cancel = true;
            BeginCloseAnimation();
        }

        private void BeginOpenAnimation()
        {
            var duration = TimeSpan.FromMilliseconds(220);
            var easing = new QuadraticEase { EasingMode = EasingMode.EaseOut };

            var fade = new DoubleAnimation(0, 1, duration) { EasingFunction = easing };
            var slide = new DoubleAnimation(14, 0, duration) { EasingFunction = easing };

            BeginAnimation(OpacityProperty, fade);
            WindowTranslate.BeginAnimation(TranslateTransform.YProperty, slide);
        }

        private void BeginCloseAnimation()
        {
            if (_isCloseAnimationRunning)
            {
                return;
            }

            _isCloseAnimationRunning = true;
            var duration = TimeSpan.FromMilliseconds(240);
            var easing = new QuadraticEase { EasingMode = EasingMode.EaseOut };

            var fade = new DoubleAnimation(1, 0, duration) { EasingFunction = easing };
            fade.Completed += (s, e) =>
            {
                _allowClose = true;
                Close();
            };

            var slide = new DoubleAnimation(0, 18, duration) { EasingFunction = easing };
            BeginAnimation(OpacityProperty, fade);
            WindowTranslate.BeginAnimation(TranslateTransform.YProperty, slide);
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void ColorButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isApplyingUi)
            {
                return;
            }

            if (sender is Button btn && btn.Tag is string colorHex)
            {
                _workingSettings.DefaultColorHex = colorHex;
                _selectedColorButton = btn;
                UpdateColorSelection();
                PublishSettings();
            }
        }

        private void SizeButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isApplyingUi)
            {
                return;
            }

            if (sender is Button btn && btn.Tag is string sizeTag && double.TryParse(sizeTag, out double size))
            {
                _workingSettings.DefaultPenSize = size;
                _selectedSizeButton = btn;
                UpdateSizeSelection();
                PublishSettings();
            }
        }

        private void StartupMode_Checked(object sender, RoutedEventArgs e)
        {
            if (_isApplyingUi)
            {
                return;
            }

            if (RbStartupEraser.IsChecked == true)
            {
                _workingSettings.StartupToolMode = ToolMode.Eraser;
            }
            else if (RbStartupPen.IsChecked == true)
            {
                _workingSettings.StartupToolMode = ToolMode.Pen;
            }
            else
            {
                _workingSettings.StartupToolMode = ToolMode.Mouse;
            }

            PublishSettings();
        }

        private void ToolbarStateChanged(object sender, RoutedEventArgs e)
        {
            if (_isApplyingUi)
            {
                return;
            }

            _workingSettings.IsToolbarVisible = ChkToolbarVisible.IsChecked == true;

            _isApplyingUi = true;
            try
            {
                if (!_workingSettings.IsToolbarVisible)
                {
                    ChkToolbarExpanded.IsChecked = false;
                }

                ChkToolbarExpanded.IsEnabled = _workingSettings.IsToolbarVisible;
            }
            finally
            {
                _isApplyingUi = false;
            }

            _workingSettings.IsToolbarExpanded = _workingSettings.IsToolbarVisible && ChkToolbarExpanded.IsChecked == true;
            PublishSettings();
        }

        private async void BtnCheckUpdate_Click(object sender, RoutedEventArgs e)
        {
            if (_isCheckingUpdate)
            {
                return;
            }

            _isCheckingUpdate = true;
            var originalButtonContent = BtnCheckUpdate.Content;
            BtnCheckUpdate.IsEnabled = false;
            BtnCheckUpdate.Content = "检查中...";
            TxtUpdateStatus.Text = "正在检查更新...";

            try
            {
                var release = await FetchLatestReleaseAsync();
                if (release == null)
                {
                    TxtUpdateStatus.Text = "检查更新失败，已打开发布页面。";
                    OpenUrl(ReleasesPageUrl);
                    return;
                }

                var latestVersion = ParseVersionFromTag(release.TagName);
                var currentVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0, 0);
                if (latestVersion == null || latestVersion <= currentVersion)
                {
                    TxtUpdateStatus.Text = "当前已是最新版本。";
                    return;
                }

                var updateAsset = SelectUpdateAsset(release);
                if (updateAsset == null)
                {
                    TxtUpdateStatus.Text = "发现新版本，但未找到可执行安装文件。已打开发布页面。";
                    OpenUrl(ReleasesPageUrl);
                    return;
                }

                TxtUpdateStatus.Text = "发现新版本 " + release.TagName + "，正在自动下载并安装...";
                var packagePath = await DownloadUpdatePackageAsync(updateAsset);
                StartUpdateInstaller(packagePath);
                TxtUpdateStatus.Text = "更新安装程序已启动，应用即将退出。";
                await Task.Delay(500);
                Application.Current.Shutdown();
            }
            catch
            {
                TxtUpdateStatus.Text = "检查更新失败，已打开发布页面。";
                OpenUrl(ReleasesPageUrl);
            }
            finally
            {
                _isCheckingUpdate = false;
                BtnCheckUpdate.IsEnabled = true;
                BtnCheckUpdate.Content = originalButtonContent;
            }
        }

        private async Task<ReleaseResponse> FetchLatestReleaseAsync()
        {
            using (var client = new WebClient())
            {
                client.Headers[HttpRequestHeader.UserAgent] = "xScreenPen";
                var data = await client.DownloadDataTaskAsync(new Uri(LatestReleaseApiUrl));
                using (var stream = new MemoryStream(data))
                {
                    var serializer = new DataContractJsonSerializer(typeof(ReleaseResponse));
                    return serializer.ReadObject(stream) as ReleaseResponse;
                }
            }
        }

        private static ReleaseAsset SelectUpdateAsset(ReleaseResponse release)
        {
            if (release?.Assets == null || release.Assets.Length == 0)
            {
                return null;
            }

            var exeAsset = release.Assets.FirstOrDefault(asset =>
                !string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl) &&
                !string.IsNullOrWhiteSpace(asset.Name) &&
                asset.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
            if (exeAsset != null)
            {
                return exeAsset;
            }

            return release.Assets.FirstOrDefault(asset => !string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl));
        }

        private static async Task<string> DownloadUpdatePackageAsync(ReleaseAsset asset)
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "xScreenPen", "update");
            Directory.CreateDirectory(tempDir);

            var fileName = string.IsNullOrWhiteSpace(asset.Name) ? "xScreenPen-update.exe" : asset.Name;
            var packagePath = Path.Combine(tempDir, fileName);
            if (File.Exists(packagePath))
            {
                File.Delete(packagePath);
            }

            using (var client = new WebClient())
            {
                client.Headers[HttpRequestHeader.UserAgent] = "xScreenPen";
                await client.DownloadFileTaskAsync(new Uri(asset.BrowserDownloadUrl), packagePath);
            }

            return packagePath;
        }

        private static void StartUpdateInstaller(string packagePath)
        {
            var currentExe = Process.GetCurrentProcess().MainModule.FileName;
            var scriptPath = Path.Combine(Path.GetDirectoryName(packagePath) ?? Path.GetTempPath(), "xscreenpen-update.cmd");
            var script = new StringBuilder();
            script.AppendLine("@echo off");
            script.AppendLine("setlocal");
            script.AppendLine("set \"SRC=" + packagePath + "\"");
            script.AppendLine("set \"DST=" + currentExe + "\"");
            script.AppendLine("for /L %%i in (1,1,8) do (");
            script.AppendLine("  copy /Y \"%SRC%\" \"%DST%\" >nul 2>nul && goto launch");
            script.AppendLine("  timeout /t 1 /nobreak >nul");
            script.AppendLine(")");
            script.AppendLine("start \"\" \"%SRC%\"");
            script.AppendLine("goto cleanup");
            script.AppendLine(":launch");
            script.AppendLine("start \"\" \"%DST%\"");
            script.AppendLine(":cleanup");
            script.AppendLine("timeout /t 1 /nobreak >nul");
            script.AppendLine("del \"%SRC%\" >nul 2>nul");
            script.AppendLine("del \"%~f0\" >nul 2>nul");
            script.AppendLine("endlocal");

            File.WriteAllText(scriptPath, script.ToString(), Encoding.Unicode);

            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/C \"" + scriptPath + "\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });
        }

        private void BtnOpenProjectHome_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl(ProjectHomeUrl);
        }

        private void BtnTerms_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                "使用条款（简版）\n\n1. 本工具按“现状”提供，不保证绝对可用性。\n2. 你可按 MIT 许可使用、修改和分发项目。\n3. 使用过程中请自行承担数据与业务风险。\n4. 详细许可请查看项目 LICENSE 文件。",
                "xScreenPen 使用条款",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void BtnPrivacy_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                "隐私说明\n\nxScreenPen 不主动采集个人身份信息。\n“检测更新”仅在你点击时访问发布接口。\n程序本地仅保存画笔设置到 AppData 下 settings.json。",
                "xScreenPen 隐私说明",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void BtnResetDefaults_Click(object sender, RoutedEventArgs e)
        {
            _workingSettings = new PenSettings();
            ApplySettingsToUi();
            PublishSettings();
            TxtUpdateStatus.Text = string.Empty;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void ApplySettingsToUi()
        {
            _isApplyingUi = true;
            try
            {
                _selectedColorButton = GetColorButtonByHex(_workingSettings.DefaultColorHex);
                _selectedSizeButton = GetSizeButtonByValue(_workingSettings.DefaultPenSize);

                switch (_workingSettings.StartupToolMode)
                {
                    case ToolMode.Eraser:
                        RbStartupEraser.IsChecked = true;
                        break;
                    case ToolMode.Pen:
                        RbStartupPen.IsChecked = true;
                        break;
                    default:
                        RbStartupMouse.IsChecked = true;
                        break;
                }

                ChkToolbarVisible.IsChecked = _workingSettings.IsToolbarVisible;
                ChkToolbarExpanded.IsChecked = _workingSettings.IsToolbarVisible && _workingSettings.IsToolbarExpanded;
                ChkToolbarExpanded.IsEnabled = _workingSettings.IsToolbarVisible;
            }
            finally
            {
                _isApplyingUi = false;
            }

            UpdateColorSelection();
            UpdateSizeSelection();
        }

        private void UpdateProjectInfo()
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            var versionText = version == null ? "未知" : version.ToString(3);
            TxtProjectInfo.Text =
                "xScreenPen 版本: v" + versionText +
                "\n仓库: " + ProjectHomeUrl +
                "\n许可: MIT License";
        }

        private static Version ParseVersionFromTag(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag))
            {
                return null;
            }

            var match = Regex.Match(tag, @"(\d+(\.\d+){1,3})");
            if (!match.Success)
            {
                return null;
            }

            Version parsed;
            return Version.TryParse(match.Groups[1].Value, out parsed) ? parsed : null;
        }

        private static void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch
            {
                MessageBox.Show("无法打开链接: " + url, "xScreenPen", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private Button GetColorButtonByHex(string colorHex)
        {
            if (string.IsNullOrWhiteSpace(colorHex))
            {
                return BtnColorWhite;
            }

            switch (colorHex.Trim().ToUpperInvariant())
            {
                case "#FF000000":
                    return BtnColorBlack;
                case "#FFFF0000":
                    return BtnColorRed;
                case "#FF0066FF":
                    return BtnColorBlue;
                case "#FF00CC00":
                    return BtnColorGreen;
                case "#FFFFCC00":
                    return BtnColorYellow;
                case "#FFFFFFFF":
                default:
                    return BtnColorWhite;
            }
        }

        private Button GetSizeButtonByValue(double size)
        {
            var candidates = new[] { BtnSizeSmall, BtnSizeMedium, BtnSizeLarge };
            Button nearest = BtnSizeMedium;
            var minDistance = double.MaxValue;

            foreach (var candidate in candidates)
            {
                if (candidate.Tag is string sizeTag && double.TryParse(sizeTag, out double candidateSize))
                {
                    var distance = Math.Abs(candidateSize - size);
                    if (distance < minDistance)
                    {
                        minDistance = distance;
                        nearest = candidate;
                    }
                }
            }

            return nearest;
        }

        private void UpdateColorSelection()
        {
            var all = new[] { BtnColorWhite, BtnColorBlack, BtnColorRed, BtnColorBlue, BtnColorGreen, BtnColorYellow };
            foreach (var button in all)
            {
                if (button == _selectedColorButton)
                {
                    button.BorderBrush = new SolidColorBrush(Color.FromRgb(0x00, 0xAA, 0xFF));
                    button.BorderThickness = new Thickness(2);
                    button.Background = new SolidColorBrush(Color.FromArgb(0x40, 0x11, 0xBB, 0xFF));
                }
                else
                {
                    button.BorderBrush = (Brush)FindResource("SettingsCardBorder");
                    button.BorderThickness = new Thickness(1);
                    button.Background = Brushes.Transparent;
                }
            }
        }

        private void UpdateSizeSelection()
        {
            var all = new[] { BtnSizeSmall, BtnSizeMedium, BtnSizeLarge };
            foreach (var button in all)
            {
                var isSelected = button == _selectedSizeButton;
                button.BorderBrush = isSelected
                    ? new SolidColorBrush(Color.FromRgb(0x00, 0xAA, 0xFF))
                    : (Brush)FindResource("SettingsCardBorder");
                button.Background = isSelected
                    ? new SolidColorBrush(Color.FromArgb(0x30, 0x11, 0xBB, 0xFF))
                    : new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22));

                if (button.Content is StackPanel panel && panel.Children.Count > 0 && panel.Children[0] is System.Windows.Shapes.Ellipse dot)
                {
                    dot.Fill = isSelected
                        ? new SolidColorBrush(Color.FromRgb(0x00, 0xAA, 0xFF))
                        : Brushes.White;
                }
            }
        }

        private void PublishSettings()
        {
            if (_isApplyingUi)
            {
                return;
            }

            _onSettingsChanged?.Invoke(_workingSettings.Clone());
        }
    }
}
