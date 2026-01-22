using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace xScreenPen
{
    public partial class TrayMenu : Window
    {
        public TrayMenu()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // 初始化显示/隐藏按钮文本
            if (Application.Current.MainWindow is MainWindow mw)
            {
                // 注意：需要确保 MainWindow 中的 FloatingToolbar 是可访问的，或者提供公共属性
                // 这里暂时假设能访问，后续会修改 MainWindow
                if (mw.IsToolbarVisible) 
                {
                    BtnShowHide.Content = "隐藏";
                }
                else
                {
                    BtnShowHide.Content = "显示";
                }
            }

            // 入场动画
            var animFade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
            var animSlide = new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(200)) 
            { 
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } 
            };
            
            this.BeginAnimation(OpacityProperty, animFade);
            MenuTransform.BeginAnimation(TranslateTransform.YProperty, animSlide);
            
            // 激活窗口以也能检测 Deactivated
            this.Focus();
        }

        private void Window_Deactivated(object sender, EventArgs e)
        {
            try
            {
                this.Close();
            }
            catch { }
        }

        private void BtnShowHide_Click(object sender, RoutedEventArgs e)
        {
            if (Application.Current.MainWindow is MainWindow mw)
            {
                mw.ToggleToolbarVisibilityPublic();
            }
            Close();
        }

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("设置功能开发中...", "xScreenPen", MessageBoxButton.OK, MessageBoxImage.Information);
            Close();
        }

        private void BtnExit_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }
    }
}
