using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using xScreenPen.Models;
using xScreenPen.Services;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace xScreenPen
{
    public partial class MainWindow : Window
    {
        private bool _isToolbarExpanded = true;
        private bool _isSecondaryPanelExpanded = false;
        private bool _isErasing = false;
        private bool _isMouseMode = false;
        private Forms.NotifyIcon _notifyIcon;
        private readonly SettingsService _settingsService;
        private PenSettings _settings;
        private SettingsWindow _settingsWindow;
        private bool _isApplyingSettings;
        private bool _isToolbarVisibilityAnimating;
        private ScaleTransform _floatingToolbarScale;
        private TrayMenu _trayMenu;
        private DateTime _lastTrayMenuClosedAtUtc = DateTime.MinValue;
        private static readonly TimeSpan TrayMenuReopenGuard = TimeSpan.FromMilliseconds(300);
        private bool _isToolbarVisibleState = true;
        
        // Drag support (Ink-Canvas pattern)
        private bool _isDragging = false;
        private Point _dragStartPoint;
        private Point _mouseDownPoint;
        private const double ClickThreshold = 5.0; // 鐐瑰嚮鍒ゅ畾闃堝€硷紙鍍忕礌锛?
        private int? _activeTouchId = null; // 褰撳墠娲诲姩鐨勮Е鎺D

        public MainWindow()
        {
            InitializeComponent();
            _settingsService = new SettingsService();
            _settings = _settingsService.Load();

            _currentColorButton = BtnColorWhite;
            _currentSizeButton = BtnSizeMedium;
            UpdateColorSelection();
            UpdateSizeSelection();
            InitializeNotifyIcon();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            ApplySettings(_settings, false);
        }

        protected override void OnClosed(EventArgs e)
        {
            _trayMenu?.Close();
            _settingsWindow?.Close();
            _notifyIcon?.Dispose();
            base.OnClosed(e);
        }

        #region System Tray

        public bool IsToolbarVisible => _isToolbarVisibleState;

        public void ToggleToolbarVisibilityPublic()
        {
            Dispatcher.Invoke(() => ToggleToolbarVisibility());
        }

        public void SetToolbarVisibilityPublic(bool isVisible)
        {
            Dispatcher.Invoke(() => SetToolbarVisibility(isVisible, true, true));
        }

        public void OpenSettingsWindow()
        {
            Dispatcher.Invoke(() =>
            {
                if (_settingsWindow != null)
                {
                    if (_settingsWindow.IsVisible)
                    {
                        _settingsWindow.Activate();
                        return;
                    }

                    _settingsWindow = null;
                }

                _settingsWindow = new SettingsWindow(GetCurrentSettingsSnapshot(), updated => ApplySettings(updated, true))
                {
                    Owner = this
                };

                _settingsWindow.Closed += (s, e) => _settingsWindow = null;
                _settingsWindow.Show();
                _settingsWindow.Activate();
            });
        }

        private void InitializeNotifyIcon()
        {
            _notifyIcon = new Forms.NotifyIcon();
            _notifyIcon.Text = "xScreenPen";
            _notifyIcon.Icon = CreateTrayIcon();
            _notifyIcon.Visible = true;
            
            _notifyIcon.MouseClick += (s, e) =>
            {
                if (e.Button == Forms.MouseButtons.Left || e.Button == Forms.MouseButtons.Right)
                {
                    ShowTrayMenu();
                }
            };
        }

        private void ShowTrayMenu()
        {
            if (_trayMenu != null)
            {
                if (_trayMenu.IsVisible)
                {
                    _trayMenu.Close();
                    return;
                }

                _trayMenu = null;
            }

            if (DateTime.UtcNow - _lastTrayMenuClosedAtUtc < TrayMenuReopenGuard)
            {
                return;
            }

            var menu = new TrayMenu();
            _trayMenu = menu;
            menu.Closed += (s, e) =>
            {
                _lastTrayMenuClosedAtUtc = DateTime.UtcNow;
                if (ReferenceEquals(_trayMenu, menu))
                {
                    _trayMenu = null;
                }
            };
            
            // 璁＄畻浣嶇疆 (DPI 鎰熺煡)
            var mousePos = GetMousePositionWPF();
            
            // 榛樿鏄剧ず鍦ㄩ紶鏍囦笂鏂瑰眳涓?
            // TrayMenu Width=200, Height=160
            double menuWidth = 200;
            double menuHeight = 160;
            
            double left = mousePos.X - menuWidth / 2;
            double top = mousePos.Y - menuHeight - 10;
            
            // 绠€鍗曡竟鐣屾鏌?
            if (top < 0) top = mousePos.Y + 10;
            
            menu.Left = left;
            menu.Top = top;
            
            menu.Show();
            menu.Activate();
        }
        
        private Point GetMousePositionWPF()
        {
            var source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget != null)
            {
                var transform = source.CompositionTarget.TransformFromDevice;
                var mouse = Forms.Cursor.Position;
                return transform.Transform(new Point(mouse.X, mouse.Y));
            }
            var m = Forms.Cursor.Position;
            return new Point(m.X, m.Y);
        }

        private Drawing.Icon CreateTrayIcon()
        {
            var bitmap = new Drawing.Bitmap(32, 32);
            using (var g = Drawing.Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(Drawing.Color.Transparent);
                
                using (var pen = new Drawing.Pen(Drawing.Color.FromArgb(0, 170, 255), 3))
                {
                    g.DrawLine(pen, 6, 26, 26, 6);
                }
                using (var brush = new Drawing.SolidBrush(Drawing.Color.FromArgb(0, 170, 255)))
                {
                    Drawing.Drawing2D.GraphicsPath path = new Drawing.Drawing2D.GraphicsPath();
                    path.AddPolygon(new Drawing.Point[] {
                        new Drawing.Point(24, 4),
                        new Drawing.Point(28, 8),
                        new Drawing.Point(26, 10),
                        new Drawing.Point(22, 6)
                    });
                    g.FillPath(brush, path);
                }
            }
            return Drawing.Icon.FromHandle(bitmap.GetHicon());
        }

        private void ToggleToolbarVisibility()
        {
            SetToolbarVisibility(FloatingToolbar.Visibility != Visibility.Visible, true, true);
        }

        private void SetToolbarVisibility(bool isVisible, bool animated, bool persist)
        {
            _isToolbarVisibleState = isVisible;
            EnsureFloatingToolbarScaleTransform();

            if (_isToolbarVisibilityAnimating)
            {
                FloatingToolbar.BeginAnimation(OpacityProperty, null);
                _floatingToolbarScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                _floatingToolbarScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                _isToolbarVisibilityAnimating = false;
            }

            if (!animated)
            {
                FloatingToolbar.BeginAnimation(OpacityProperty, null);
                _floatingToolbarScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                _floatingToolbarScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                FloatingToolbar.Opacity = 1;
                _floatingToolbarScale.ScaleX = 1;
                _floatingToolbarScale.ScaleY = 1;
                FloatingToolbar.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;

                if (persist)
                {
                    PersistCurrentSettings();
                }

                return;
            }

            _isToolbarVisibilityAnimating = true;

            var duration = TimeSpan.FromMilliseconds(180);
            var easing = new QuadraticEase { EasingMode = EasingMode.EaseOut };

            if (isVisible)
            {
                FloatingToolbar.Visibility = Visibility.Visible;
                FloatingToolbar.Opacity = 0;
                _floatingToolbarScale.ScaleX = 0.92;
                _floatingToolbarScale.ScaleY = 0.92;

                var opacityAnim = new DoubleAnimation(0, 1, duration) { EasingFunction = easing };
                opacityAnim.Completed += (s, e) =>
                {
                    _isToolbarVisibilityAnimating = false;
                    if (persist)
                    {
                        PersistCurrentSettings();
                    }
                };

                var scaleXAnim = new DoubleAnimation(0.92, 1, duration) { EasingFunction = easing };
                var scaleYAnim = new DoubleAnimation(0.92, 1, duration) { EasingFunction = easing };

                FloatingToolbar.BeginAnimation(OpacityProperty, opacityAnim);
                _floatingToolbarScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleXAnim);
                _floatingToolbarScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleYAnim);
            }
            else
            {
                var opacityAnim = new DoubleAnimation(1, 0, duration) { EasingFunction = easing };
                opacityAnim.Completed += (s, e) =>
                {
                    FloatingToolbar.Visibility = Visibility.Collapsed;
                    FloatingToolbar.Opacity = 1;
                    _floatingToolbarScale.ScaleX = 1;
                    _floatingToolbarScale.ScaleY = 1;
                    _isToolbarVisibilityAnimating = false;

                    if (persist)
                    {
                        PersistCurrentSettings();
                    }
                };

                var scaleXAnim = new DoubleAnimation(1, 0.92, duration) { EasingFunction = easing };
                var scaleYAnim = new DoubleAnimation(1, 0.92, duration) { EasingFunction = easing };

                FloatingToolbar.BeginAnimation(OpacityProperty, opacityAnim);
                _floatingToolbarScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleXAnim);
                _floatingToolbarScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleYAnim);
            }
        }

        private void EnsureFloatingToolbarScaleTransform()
        {
            if (FloatingToolbar.RenderTransform is ScaleTransform scale)
            {
                _floatingToolbarScale = scale;
            }
            else
            {
                _floatingToolbarScale = new ScaleTransform(1, 1);
                FloatingToolbar.RenderTransform = _floatingToolbarScale;
                FloatingToolbar.RenderTransformOrigin = new Point(0.5, 0.5);
            }
        }

        #endregion

        #region Floating Ball (Drag & Click) - Ink-Canvas Pattern

        /// <summary>
        /// 鍒ゆ柇鏄惁涓虹偣鍑伙紙绉诲姩璺濈灏忎簬闃堝€硷級
        /// </summary>
        private bool IsClick(Point downPoint, Point upPoint)
        {
            double distance = Math.Sqrt(Math.Pow(upPoint.X - downPoint.X, 2) + Math.Pow(upPoint.Y - downPoint.Y, 2));
            return distance < ClickThreshold;
        }

        /// <summary>
        /// 纭繚宸ュ叿鏍忎娇鐢ㄥ乏涓婅瀵归綈锛堢敤浜庢嫋鍔級
        /// </summary>
        private void EnsureToolbarLeftTopAlignment()
        {
            if (FloatingToolbar.HorizontalAlignment == HorizontalAlignment.Center)
            {
                var transform = FloatingToolbar.TransformToAncestor(MainGrid);
                var currentPos = transform.Transform(new Point(0, 0));
                
                FloatingToolbar.HorizontalAlignment = HorizontalAlignment.Left;
                FloatingToolbar.VerticalAlignment = VerticalAlignment.Top;
                FloatingToolbar.Margin = new Thickness(currentPos.X, currentPos.Y, 0, 0);
            }
        }

        /// <summary>
        /// 鎵ц鎷栧姩绉诲姩
        /// </summary>
        private void PerformDragMove(Point currentPoint)
        {
            double xPos = currentPoint.X - _dragStartPoint.X + FloatingToolbar.Margin.Left;
            double yPos = currentPoint.Y - _dragStartPoint.Y + FloatingToolbar.Margin.Top;
            FloatingToolbar.Margin = new Thickness(xPos, yPos, 0, 0);
            _dragStartPoint = currentPoint;
        }

        #region Mouse Events

        /// <summary>
        /// 鍒ゆ柇鏄惁涓烘墜鎸囪Е鎺э紙鑰岄潪瑙︽帶绗旓級
        /// </summary>
        private bool IsFingerTouch(StylusDevice stylusDevice)
        {
            return stylusDevice?.TabletDevice?.Type == TabletDeviceType.Touch;
        }

        private void FloatingBall_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // 濡傛灉鏄墜鎸囪Е鎺цЕ鍙戠殑榧犳爣浜嬩欢锛屽拷鐣ワ紙鐢?Touch 浜嬩欢澶勭悊锛?
            // 瑙︽帶绗斾細缁х画鍦ㄦ澶勭悊
            if (IsFingerTouch(e.StylusDevice)) return;
            
            _isDragging = true;
            _dragStartPoint = e.GetPosition(null);
            _mouseDownPoint = e.GetPosition(null);
            
            EnsureToolbarLeftTopAlignment();
            GridForFloatingBarDraging.Visibility = Visibility.Visible;
        }

        private void FloatingBall_MouseUp(object sender, MouseButtonEventArgs e)
        {
            // 濡傛灉鏄墜鎸囪Е鎺цЕ鍙戠殑榧犳爣浜嬩欢锛屽拷鐣?
            if (IsFingerTouch(e.StylusDevice)) return;
            
            if (!_isDragging) return;
            
            _isDragging = false;
            GridForFloatingBarDraging.Visibility = Visibility.Collapsed;
            
            if (IsClick(_mouseDownPoint, e.GetPosition(null)))
            {
                ToggleToolbarExpand();
            }
        }

        private void GridDrag_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDragging && _activeTouchId == null) // 浠呴紶鏍囨嫋鍔?
            {
                PerformDragMove(e.GetPosition(null));
            }
        }

        private void GridDrag_MouseUp(object sender, MouseButtonEventArgs e)
        {
            // 濡傛灉鏄墜鎸囪Е鎺цЕ鍙戠殑榧犳爣浜嬩欢锛屽拷鐣?
            if (IsFingerTouch(e.StylusDevice)) return;
            
            if (!_isDragging) return;
            
            _isDragging = false;
            GridForFloatingBarDraging.Visibility = Visibility.Collapsed;
            
            if (IsClick(_mouseDownPoint, e.GetPosition(null)))
            {
                ToggleToolbarExpand();
            }
        }

        #endregion

        #region Touch Events

        private void FloatingBall_TouchDown(object sender, TouchEventArgs e)
        {
            // 鍙鐞嗙涓€涓Е鎺х偣
            if (_activeTouchId != null) return;
            
            _activeTouchId = e.TouchDevice.Id;
            _isDragging = true;
            _dragStartPoint = e.GetTouchPoint(null).Position;
            _mouseDownPoint = e.GetTouchPoint(null).Position;
            
            EnsureToolbarLeftTopAlignment();
            GridForFloatingBarDraging.Visibility = Visibility.Visible;
            
            e.Handled = true;
        }

        private void FloatingBall_TouchMove(object sender, TouchEventArgs e)
        {
            if (_activeTouchId != e.TouchDevice.Id) return;
            
            if (_isDragging)
            {
                PerformDragMove(e.GetTouchPoint(null).Position);
            }
            
            e.Handled = true;
        }

        private void FloatingBall_TouchUp(object sender, TouchEventArgs e)
        {
            if (_activeTouchId != e.TouchDevice.Id) return;
            
            var upPoint = e.GetTouchPoint(null).Position;
            
            _isDragging = false;
            _activeTouchId = null;
            GridForFloatingBarDraging.Visibility = Visibility.Collapsed;
            
            if (IsClick(_mouseDownPoint, upPoint))
            {
                ToggleToolbarExpand();
            }
            
            e.Handled = true;
        }

        private void GridDrag_TouchMove(object sender, TouchEventArgs e)
        {
            if (_activeTouchId != e.TouchDevice.Id) return;
            
            if (_isDragging)
            {
                PerformDragMove(e.GetTouchPoint(null).Position);
            }
            
            e.Handled = true;
        }

        private void GridDrag_TouchUp(object sender, TouchEventArgs e)
        {
            if (_activeTouchId != e.TouchDevice.Id) return;
            
            var upPoint = e.GetTouchPoint(null).Position;
            
            _isDragging = false;
            _activeTouchId = null;
            GridForFloatingBarDraging.Visibility = Visibility.Collapsed;
            
            if (IsClick(_mouseDownPoint, upPoint))
            {
                ToggleToolbarExpand();
            }
            
            e.Handled = true;
        }

        #endregion

        private void ToggleToolbarExpand()
        {
            _isToolbarExpanded = !_isToolbarExpanded;
            
            var duration = TimeSpan.FromMilliseconds(200);
            var easing = new QuadraticEase { EasingMode = EasingMode.EaseOut };
            
            // 璁板綍鎮诞鐞冨綋鍓嶇殑缁濆浣嶇疆
            Point floatingBallPos = GetFloatingBallAbsolutePosition();
            
            // 鏇存柊鎮诞鐞冨浘鏍囬鑹?
            UpdateFloatingBallIcon();
            
            if (_isToolbarExpanded)
            {
                // 濡傛灉浜岀骇闈㈡澘涔熼渶瑕佹仮澶嶏紝鍏堣绠楀ソ
                ToolbarContent.Visibility = Visibility.Visible;
                
                // 寮哄埗鍚屾鏇存柊甯冨眬
                FloatingToolbar.UpdateLayout();
                
                // 绔嬪嵆鎭㈠鎮诞鐞冧綅缃?
                RestoreFloatingBallPosition(floatingBallPos);
                
                var scaleXAnim = new DoubleAnimation(0, 1, duration) { EasingFunction = easing };
                var scaleYAnim = new DoubleAnimation(0.8, 1, duration) { EasingFunction = easing };
                var opacityAnim = new DoubleAnimation(0, 1, duration) { EasingFunction = easing };
                ToolbarContentScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleXAnim);
                ToolbarContentScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleYAnim);
                ToolbarContent.BeginAnimation(OpacityProperty, opacityAnim);
            }
            else
            {
                // 濡傛灉浜岀骇闈㈡澘灞曞紑锛屽厛鍏抽棴锛堜絾鍏堜笉鏀瑰彉 Visibility锛岃繖鏍蜂綅缃褰曟槸姝ｇ‘鐨勶級
                bool wasSecondaryPanelExpanded = _isSecondaryPanelExpanded;
                if (_isSecondaryPanelExpanded)
                {
                    _isSecondaryPanelExpanded = false;
                }
                
                var scaleXAnim = new DoubleAnimation(1, 0, duration) { EasingFunction = easing };
                var scaleYAnim = new DoubleAnimation(1, 0.8, duration) { EasingFunction = easing };
                var opacityAnim = new DoubleAnimation(1, 0, duration) { EasingFunction = easing };
                opacityAnim.Completed += (s, ev) =>
                {
                    ToolbarContent.Visibility = Visibility.Collapsed;
                    if (wasSecondaryPanelExpanded)
                    {
                        SecondaryPanel.Visibility = Visibility.Collapsed;
                    }
                    
                    // 寮哄埗鍚屾鏇存柊甯冨眬
                    FloatingToolbar.UpdateLayout();
                    
                    // 绔嬪嵆鎭㈠鎮诞鐞冧綅缃?
                    RestoreFloatingBallPosition(floatingBallPos);
                };
                ToolbarContentScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleXAnim);
                ToolbarContentScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleYAnim);
                ToolbarContent.BeginAnimation(OpacityProperty, opacityAnim);
            }

            PersistCurrentSettings();
        }
        
        /// <summary>
        /// 鏇存柊鎮诞鐞冨浘鏍囬鑹诧細灞曞紑鏃惰摑鑹诧紝鏀惰捣鏃剁櫧鑹?
        /// </summary>
        private void UpdateFloatingBallIcon()
        {
            var blueColor = Color.FromRgb(0x00, 0xAA, 0xFF);
            FloatingBallIcon.Fill = _isToolbarExpanded 
                ? new SolidColorBrush(blueColor) 
                : Brushes.White;
        }
        
        /// <summary>
        /// 鑾峰彇鎮诞鐞冪浉瀵逛簬MainGrid鐨勭粷瀵逛綅缃?
        /// </summary>
        private Point GetFloatingBallAbsolutePosition()
        {
            try
            {
                var transform = FloatingBall.TransformToAncestor(MainGrid);
                return transform.Transform(new Point(0, 0));
            }
            catch
            {
                return new Point(0, 0);
            }
        }
        
        /// <summary>
        /// 鎭㈠鎮诞鐞冨埌鎸囧畾鐨勭粷瀵逛綅缃?
        /// </summary>
        private void RestoreFloatingBallPosition(Point targetBallPos)
        {
            if (FloatingToolbar.HorizontalAlignment != HorizontalAlignment.Left)
            {
                return; // 杩樻病鏈夎鎷栧姩杩囷紝涓嶉渶瑕佽皟鏁?
            }
            
            // 鑾峰彇褰撳墠鎮诞鐞冧綅缃?
            Point currentBallPos = GetFloatingBallAbsolutePosition();
            
            // 璁＄畻闇€瑕佽皟鏁寸殑鍋忕Щ閲忥紙鍚屾椂澶勭悊X鍜孻杞达級
            double deltaX = targetBallPos.X - currentBallPos.X;
            double deltaY = targetBallPos.Y - currentBallPos.Y;
            
            // 鍙湁褰撳亸绉婚噺澶т簬1鍍忕礌鏃舵墠璋冩暣
            if (Math.Abs(deltaX) > 1 || Math.Abs(deltaY) > 1)
            {
                FloatingToolbar.Margin = new Thickness(
                    FloatingToolbar.Margin.Left + deltaX,
                    FloatingToolbar.Margin.Top + deltaY,
                    0, 0);
            }
        }

        #endregion

        #region Tool Buttons

        private void BtnPen_Click(object sender, RoutedEventArgs e)
        {
            bool wasPenMode = !_isErasing && !_isMouseMode;

            SetPenMode();

            if (wasPenMode)
            {
                ToggleSecondaryPanel();
            }

            PersistCurrentSettings();
        }

        private void ShowSecondaryPanel()
        {
            if (_isSecondaryPanelExpanded)
            {
                return;
            }

            _isSecondaryPanelExpanded = true;

            var duration = TimeSpan.FromMilliseconds(200);
            var easing = new QuadraticEase { EasingMode = EasingMode.EaseOut };

            Point floatingBallPos = GetFloatingBallAbsolutePosition();
            SecondaryPanel.Visibility = Visibility.Visible;
            FloatingToolbar.UpdateLayout();
            RestoreFloatingBallPosition(floatingBallPos);

            var scaleYAnim = new DoubleAnimation(0, 1, duration) { EasingFunction = easing };
            var opacityAnim = new DoubleAnimation(0, 1, duration) { EasingFunction = easing };
            SecondaryPanelScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleYAnim);
            SecondaryPanel.BeginAnimation(OpacityProperty, opacityAnim);
        }

        private void HideSecondaryPanel()
        {
            if (!_isSecondaryPanelExpanded)
            {
                return;
            }

            _isSecondaryPanelExpanded = false;

            var duration = TimeSpan.FromMilliseconds(200);
            var easing = new QuadraticEase { EasingMode = EasingMode.EaseOut };

            Point floatingBallPos = GetFloatingBallAbsolutePosition();

            var scaleYAnim = new DoubleAnimation(1, 0, duration) { EasingFunction = easing };
            var opacityAnim = new DoubleAnimation(1, 0, duration) { EasingFunction = easing };
            opacityAnim.Completed += (s, ev) =>
            {
                SecondaryPanel.Visibility = Visibility.Collapsed;
                FloatingToolbar.UpdateLayout();
                RestoreFloatingBallPosition(floatingBallPos);
            };
            SecondaryPanelScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleYAnim);
            SecondaryPanel.BeginAnimation(OpacityProperty, opacityAnim);
        }

        private void ToggleSecondaryPanel()
        {
            if (_isSecondaryPanelExpanded)
            {
                HideSecondaryPanel();
            }
            else
            {
                ShowSecondaryPanel();
            }
        }

        private void BtnEraser_Click(object sender, RoutedEventArgs e)
        {
            HideSecondaryPanel();
            SetEraserMode();
            PersistCurrentSettings();
        }

        private void SetPenMode()
        {
            SetToolMode(ToolMode.Pen);
        }

        private void SetEraserMode()
        {
            SetToolMode(ToolMode.Eraser);
        }

        private void SetMouseMode()
        {
            SetToolMode(ToolMode.Mouse);
        }

        private void SetToolMode(ToolMode mode)
        {
            _isMouseMode = mode == ToolMode.Mouse;
            _isErasing = mode == ToolMode.Eraser;

            if (_isMouseMode)
            {
                MainGrid.Background = Brushes.Transparent;
                inkCanvas.IsHitTestVisible = false;
                inkCanvas.Visibility = Visibility.Visible;
                inkCanvas.Cursor = Cursors.Arrow;
            }
            else
            {
                MainGrid.Background = new SolidColorBrush(Color.FromArgb(0x01, 0xFF, 0xFF, 0xFF));
                inkCanvas.IsHitTestVisible = true;
                inkCanvas.Visibility = Visibility.Visible;
                inkCanvas.EditingMode = _isErasing ? InkCanvasEditingMode.EraseByStroke : InkCanvasEditingMode.Ink;
                inkCanvas.Cursor = _isErasing ? Cursors.Cross : Cursors.Pen;
            }

            UpdateToolButtonStates();
        }

        private ToolMode GetCurrentToolMode()
        {
            if (_isMouseMode)
            {
                return ToolMode.Mouse;
            }

            return _isErasing ? ToolMode.Eraser : ToolMode.Pen;
        }

        private void UpdateToolButtonStates()
        {
            var blueColor = Color.FromRgb(0x00, 0xAA, 0xFF);
            var selectedBg = new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF));

            bool isPenSelected = !_isErasing && !_isMouseMode;
            bool isEraserSelected = _isErasing && !_isMouseMode;

            ToggleIcon.Fill = _isMouseMode ? new SolidColorBrush(blueColor) : Brushes.White;
            BtnToggle.Background = _isMouseMode ? selectedBg : Brushes.Transparent;

            PenIcon.Fill = isPenSelected ? new SolidColorBrush(blueColor) : Brushes.White;
            BtnPen.Background = isPenSelected ? selectedBg : Brushes.Transparent;

            EraserIcon.Fill = isEraserSelected ? new SolidColorBrush(blueColor) : Brushes.White;
            BtnEraser.Background = isEraserSelected ? selectedBg : Brushes.Transparent;
        }

        #endregion

        #region Colors

        private Button _currentColorButton;

        private void BtnColor_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string colorString)
            {
                SetColorByHex(colorString, true);
            }
        }

        private void SetColorByHex(string colorHex, bool persist)
        {
            var colorButton = GetColorButtonByHex(colorHex) ?? BtnColorWhite;
            if (!(colorButton.Tag is string normalizedHex))
            {
                return;
            }

            var color = (Color)ColorConverter.ConvertFromString(normalizedHex);
            inkCanvas.DefaultDrawingAttributes.Color = color;
            _currentColorButton = colorButton;
            UpdateColorSelection();

            if (_isErasing)
            {
                SetPenMode();
            }

            if (persist)
            {
                PersistCurrentSettings();
            }
        }

        private Button GetColorButtonByHex(string colorHex)
        {
            if (string.IsNullOrWhiteSpace(colorHex))
            {
                return BtnColorWhite;
            }

            var normalized = colorHex.Trim().ToUpperInvariant();
            switch (normalized)
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

        private void UpdateColorSelection()
        {
            var colors = new[] { BtnColorRed, BtnColorGreen, BtnColorBlue, BtnColorYellow, BtnColorWhite, BtnColorBlack };
            foreach (var btn in colors)
            {
                if (btn == _currentColorButton)
                {
                    btn.Background = new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF));
                }
                else
                {
                    btn.Background = Brushes.Transparent;
                }
            }
        }

        #endregion

        #region Pen Size

        private Button _currentSizeButton;

        private void BtnSize_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string sizeStr && double.TryParse(sizeStr, out double size))
            {
                SetPenSize(size, true);
            }
        }

        private void SetPenSize(double size, bool persist)
        {
            var sizeButton = GetSizeButtonByValue(size) ?? BtnSizeMedium;
            if (!(sizeButton.Tag is string sizeTag) || !double.TryParse(sizeTag, out double normalizedSize))
            {
                normalizedSize = 4;
            }

            inkCanvas.DefaultDrawingAttributes.Width = normalizedSize;
            inkCanvas.DefaultDrawingAttributes.Height = normalizedSize;

            _currentSizeButton = sizeButton;
            UpdateSizeSelection();

            if (_isErasing)
            {
                SetPenMode();
            }

            if (persist)
            {
                PersistCurrentSettings();
            }
        }

        private Button GetSizeButtonByValue(double size)
        {
            var candidates = new[] { BtnSizeSmall, BtnSizeMedium, BtnSizeLarge };
            Button nearest = BtnSizeMedium;
            double minDistance = double.MaxValue;

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

        private double GetCurrentPenSize()
        {
            if (_currentSizeButton?.Tag is string sizeTag && double.TryParse(sizeTag, out double size))
            {
                return size;
            }

            return 4;
        }

        private void UpdateSizeSelection()
        {
            var sizes = new[] { BtnSizeSmall, BtnSizeMedium, BtnSizeLarge };
            foreach (var btn in sizes)
            {
                var ellipse = btn.Content as System.Windows.Shapes.Ellipse;
                if (btn == _currentSizeButton)
                {
                    btn.Background = new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF));
                    if (ellipse != null)
                    {
                        ellipse.Fill = new SolidColorBrush(Color.FromRgb(0x00, 0xAA, 0xFF));
                    }
                }
                else
                {
                    btn.Background = Brushes.Transparent;
                    if (ellipse != null)
                    {
                        ellipse.Fill = Brushes.White;
                    }
                }
            }
        }

        #endregion

        #region Clear/Toggle

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            if (inkCanvas.Strokes.Count > 0)
            {
                inkCanvas.Strokes.Clear();
            }
        }

        private void BtnToggle_Click(object sender, RoutedEventArgs e)
        {
            if (_isMouseMode)
            {
                return;
            }

            HideSecondaryPanel();
            SetMouseMode();
            PersistCurrentSettings();
        }

        #endregion

        #region Settings

        internal void ApplySettings(PenSettings settings, bool save)
        {
            var sanitized = _settingsService.Sanitize(settings);

            _isApplyingSettings = true;
            try
            {
                SetColorByHex(sanitized.DefaultColorHex, false);
                SetPenSize(sanitized.DefaultPenSize, false);

                switch (sanitized.StartupToolMode)
                {
                    case ToolMode.Eraser:
                        SetEraserMode();
                        break;
                    case ToolMode.Pen:
                        SetPenMode();
                        break;
                    default:
                        SetMouseMode();
                        break;
                }

                ApplyToolbarExpandedState(sanitized.IsToolbarExpanded);
                ApplyToolbarVisibleState(sanitized.IsToolbarVisible);

                _settings = sanitized.Clone();
            }
            finally
            {
                _isApplyingSettings = false;
            }

            if (save)
            {
                PersistCurrentSettings();
            }
        }

        internal PenSettings GetCurrentSettingsSnapshot()
        {
            var snapshot = new PenSettings
            {
                SchemaVersion = PenSettings.CurrentSchemaVersion,
                DefaultColorHex = (_currentColorButton?.Tag as string) ?? "#FFFFFFFF",
                DefaultPenSize = GetCurrentPenSize(),
                StartupToolMode = GetCurrentToolMode(),
                IsToolbarVisible = FloatingToolbar.Visibility == Visibility.Visible,
                IsToolbarExpanded = _isToolbarExpanded
            };

            return _settingsService.Sanitize(snapshot);
        }

        internal void PersistCurrentSettings()
        {
            if (_isApplyingSettings)
            {
                return;
            }

            _settings = GetCurrentSettingsSnapshot();
            _settingsService.Save(_settings);
        }

        private void ApplyToolbarExpandedState(bool isExpanded)
        {
            _isToolbarExpanded = isExpanded;

            ToolbarContent.BeginAnimation(OpacityProperty, null);
            ToolbarContentScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            ToolbarContentScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            ToolbarContentScale.ScaleX = 1;
            ToolbarContentScale.ScaleY = 1;
            ToolbarContent.Opacity = isExpanded ? 1 : 0;
            ToolbarContent.Visibility = isExpanded ? Visibility.Visible : Visibility.Collapsed;

            _isSecondaryPanelExpanded = false;
            SecondaryPanel.BeginAnimation(OpacityProperty, null);
            SecondaryPanelScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            SecondaryPanelScale.ScaleY = 1;
            SecondaryPanel.Opacity = 0;
            SecondaryPanel.Visibility = Visibility.Collapsed;

            UpdateFloatingBallIcon();
        }

        private void ApplyToolbarVisibleState(bool isVisible)
        {
            _isToolbarVisibleState = isVisible;
            SetToolbarVisibility(isVisible, false, false);
        }

        #endregion

    }
}

