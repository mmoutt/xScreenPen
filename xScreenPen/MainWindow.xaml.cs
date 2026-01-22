using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
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
        
        // Drag support (Ink-Canvas pattern)
        private bool _isDragging = false;
        private Point _dragStartPoint;
        private Point _mouseDownPoint;
        private const double ClickThreshold = 5.0; // 点击判定阈值（像素）
        private int? _activeTouchId = null; // 当前活动的触控ID

        public MainWindow()
        {
            InitializeComponent();
            _currentColorButton = BtnColorWhite; // 默认白色
            UpdateColorSelection();
            InitializeNotifyIcon();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // 默认收起悬浮球
            ToolbarContent.Visibility = Visibility.Collapsed;
            _isToolbarExpanded = false;
            UpdateFloatingBallIcon();

            // 默认进入鼠标模式
            _isMouseMode = true; 
            _isErasing = false;
            MainGrid.Background = Brushes.Transparent;
            inkCanvas.IsHitTestVisible = false;
            inkCanvas.Visibility = Visibility.Visible;
            UpdateToolButtonStates();
            
            // 移除启动提示
            // ShowToolIndicator("xScreenPen 已启动");
            
            // 初始化粗细选中状态
            _currentSizeButton = BtnSizeMedium;
            UpdateSizeSelection();
        }

        protected override void OnClosed(EventArgs e)
        {
            _notifyIcon?.Dispose();
            base.OnClosed(e);
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.P:
                    BtnPen_Click(null, null); // 与点击笔按钮行为一致
                    break;
                case Key.E:
                    BtnEraser_Click(null, null);
                    break;
                case Key.C:
                    BtnClear_Click(null, null);
                    break;
                case Key.M:
                    BtnToggle_Click(null, null);
                    break;
            }
        }

        #region System Tray

        public bool IsToolbarVisible => FloatingToolbar.Visibility == Visibility.Visible;

        public void ToggleToolbarVisibilityPublic()
        {
            Dispatcher.Invoke(() => ToggleToolbarVisibility());
        }

        private void InitializeNotifyIcon()
        {
            _notifyIcon = new Forms.NotifyIcon();
            _notifyIcon.Text = "xScreenPen";
            _notifyIcon.Icon = CreateTrayIcon();
            _notifyIcon.Visible = true;
            
            // 左右键都能触发菜单
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
            // 关闭所有已存在的 TrayMenu (如果有)
            foreach (Window win in Application.Current.Windows)
            {
                if (win is TrayMenu)
                {
                    win.Close();
                    return; // 如果已经打开，再次点击则是关闭
                }
            }

            var menu = new TrayMenu();
            
            // 计算位置 (DPI 感知)
            var mousePos = GetMousePositionWPF();
            
            // 默认显示在鼠标上方居中
            // TrayMenu Width=200, Height=160
            double menuWidth = 200;
            double menuHeight = 160;
            
            double left = mousePos.X - menuWidth / 2;
            double top = mousePos.Y - menuHeight - 10;
            
            // 简单边界检查
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
            if (FloatingToolbar.Visibility == Visibility.Visible)
            {
                FloatingToolbar.Visibility = Visibility.Collapsed;
            }
            else
            {
                FloatingToolbar.Visibility = Visibility.Visible;
            }
        }

        #endregion

        #region Floating Ball (Drag & Click) - Ink-Canvas Pattern

        /// <summary>
        /// 判断是否为点击（移动距离小于阈值）
        /// </summary>
        private bool IsClick(Point downPoint, Point upPoint)
        {
            double distance = Math.Sqrt(Math.Pow(upPoint.X - downPoint.X, 2) + Math.Pow(upPoint.Y - downPoint.Y, 2));
            return distance < ClickThreshold;
        }

        /// <summary>
        /// 确保工具栏使用左上角对齐（用于拖动）
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
        /// 执行拖动移动
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
        /// 判断是否为手指触控（而非触控笔）
        /// </summary>
        private bool IsFingerTouch(StylusDevice stylusDevice)
        {
            return stylusDevice?.TabletDevice?.Type == TabletDeviceType.Touch;
        }

        private void FloatingBall_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // 如果是手指触控触发的鼠标事件，忽略（由 Touch 事件处理）
            // 触控笔会继续在此处理
            if (IsFingerTouch(e.StylusDevice)) return;
            
            _isDragging = true;
            _dragStartPoint = e.GetPosition(null);
            _mouseDownPoint = e.GetPosition(null);
            
            EnsureToolbarLeftTopAlignment();
            GridForFloatingBarDraging.Visibility = Visibility.Visible;
        }

        private void FloatingBall_MouseUp(object sender, MouseButtonEventArgs e)
        {
            // 如果是手指触控触发的鼠标事件，忽略
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
            if (_isDragging && _activeTouchId == null) // 仅鼠标拖动
            {
                PerformDragMove(e.GetPosition(null));
            }
        }

        private void GridDrag_MouseUp(object sender, MouseButtonEventArgs e)
        {
            // 如果是手指触控触发的鼠标事件，忽略
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
            // 只处理第一个触控点
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
            
            // 记录悬浮球当前的绝对位置
            Point floatingBallPos = GetFloatingBallAbsolutePosition();
            
            // 更新悬浮球图标颜色
            UpdateFloatingBallIcon();
            
            if (_isToolbarExpanded)
            {
                // 如果二级面板也需要恢复，先计算好
                ToolbarContent.Visibility = Visibility.Visible;
                
                // 强制同步更新布局
                FloatingToolbar.UpdateLayout();
                
                // 立即恢复悬浮球位置
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
                // 如果二级面板展开，先关闭（但先不改变 Visibility，这样位置记录是正确的）
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
                    
                    // 强制同步更新布局
                    FloatingToolbar.UpdateLayout();
                    
                    // 立即恢复悬浮球位置
                    RestoreFloatingBallPosition(floatingBallPos);
                };
                ToolbarContentScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleXAnim);
                ToolbarContentScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleYAnim);
                ToolbarContent.BeginAnimation(OpacityProperty, opacityAnim);
            }
        }
        
        /// <summary>
        /// 更新悬浮球图标颜色：展开时蓝色，收起时白色
        /// </summary>
        private void UpdateFloatingBallIcon()
        {
            var blueColor = Color.FromRgb(0x00, 0xAA, 0xFF);
            FloatingBallIcon.Fill = _isToolbarExpanded 
                ? new SolidColorBrush(blueColor) 
                : Brushes.White;
        }
        
        /// <summary>
        /// 获取悬浮球相对于MainGrid的绝对位置
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
        /// 恢复悬浮球到指定的绝对位置
        /// </summary>
        private void RestoreFloatingBallPosition(Point targetBallPos)
        {
            if (FloatingToolbar.HorizontalAlignment != HorizontalAlignment.Left)
            {
                return; // 还没有被拖动过，不需要调整
            }
            
            // 获取当前悬浮球位置
            Point currentBallPos = GetFloatingBallAbsolutePosition();
            
            // 计算需要调整的偏移量（同时处理X和Y轴）
            double deltaX = targetBallPos.X - currentBallPos.X;
            double deltaY = targetBallPos.Y - currentBallPos.Y;
            
            // 只有当偏移量大于1像素时才调整
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

        /// <summary>
        /// 笔按钮点击：
        /// - 如果当前不是笔模式 → 切换到笔模式（不展开二级菜单）
        /// - 如果当前是笔模式 → 切换二级菜单的展开/收起状态
        /// </summary>
        private void BtnPen_Click(object sender, RoutedEventArgs e)
        {
            bool wasPenMode = !_isErasing && !_isMouseMode;
            
            // 先退出鼠标模式（如果需要）
            if (_isMouseMode)
            {
                _isMouseMode = false;
                MainGrid.Background = new SolidColorBrush(Color.FromArgb(0x01, 0xFF, 0xFF, 0xFF));
                inkCanvas.IsHitTestVisible = true;
            }
            
            // 设置为笔模式
            SetPenMode();
            
            // 如果之前就是笔模式，切换二级菜单
            if (wasPenMode)
            {
                ToggleSecondaryPanel();
            }
        }

        /// <summary>
        /// 展开二级面板（仅展开，不切换）
        /// </summary>
        private void ShowSecondaryPanel()
        {
            if (_isSecondaryPanelExpanded) return;
            
            _isSecondaryPanelExpanded = true;
            
            var duration = TimeSpan.FromMilliseconds(200);
            var easing = new QuadraticEase { EasingMode = EasingMode.EaseOut };
            
            // 记录展开前的悬浮球位置
            Point floatingBallPos = GetFloatingBallAbsolutePosition();
            
            SecondaryPanel.Visibility = Visibility.Visible;
            
            // 强制同步更新布局，此时悬浮球会被会被向下挤
            FloatingToolbar.UpdateLayout();
            
            // 立即修正位置（把被挤下去的 Toolbar 提上来）
            // 这样在下一帧渲染时，用户看到的是原位不动的悬浮球和向上展开的菜单
            RestoreFloatingBallPosition(floatingBallPos);
            
            var scaleYAnim = new DoubleAnimation(0, 1, duration) { EasingFunction = easing };
            var opacityAnim = new DoubleAnimation(0, 1, duration) { EasingFunction = easing };
            SecondaryPanelScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleYAnim);
            SecondaryPanel.BeginAnimation(OpacityProperty, opacityAnim);
        }

        /// <summary>
        /// 收起二级面板（仅收起，不切换）
        /// </summary>
        private void HideSecondaryPanel()
        {
            if (!_isSecondaryPanelExpanded) return;
            
            _isSecondaryPanelExpanded = false;
            
            var duration = TimeSpan.FromMilliseconds(200);
            var easing = new QuadraticEase { EasingMode = EasingMode.EaseOut };
            
            // 记录动画开始前的悬浮球位置
            Point floatingBallPos = GetFloatingBallAbsolutePosition();
            
            var scaleYAnim = new DoubleAnimation(1, 0, duration) { EasingFunction = easing };
            var opacityAnim = new DoubleAnimation(1, 0, duration) { EasingFunction = easing };
            opacityAnim.Completed += (s, ev) =>
            {
                SecondaryPanel.Visibility = Visibility.Collapsed;
                
                // 强制同步更新布局，此时悬浮球会向上跳
                FloatingToolbar.UpdateLayout();
                
                // 立即修正位置（把跳上去的 Toolbar 按下来）
                RestoreFloatingBallPosition(floatingBallPos);
            };
            SecondaryPanelScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleYAnim);
            SecondaryPanel.BeginAnimation(OpacityProperty, opacityAnim);
        }

        /// <summary>
        /// 切换二级面板的展开/收起状态
        /// </summary>
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

        /// <summary>
        /// 橡皮擦按钮点击：切换到橡皮擦模式，关闭二级菜单
        /// </summary>
        private void BtnEraser_Click(object sender, RoutedEventArgs e)
        {
            // 退出鼠标模式
            if (_isMouseMode)
            {
                _isMouseMode = false;
                MainGrid.Background = new SolidColorBrush(Color.FromArgb(0x01, 0xFF, 0xFF, 0xFF));
                inkCanvas.IsHitTestVisible = true;
            }
            
            // 关闭二级面板
            HideSecondaryPanel();
            
            SetEraserMode();
        }

        /// <summary>
        /// 设置为笔模式
        /// </summary>
        private void SetPenMode()
        {
            _isErasing = false;
            inkCanvas.EditingMode = InkCanvasEditingMode.Ink;
            inkCanvas.Cursor = Cursors.Pen;
            UpdateToolButtonStates();
        }

        /// <summary>
        /// 设置为橡皮擦模式
        /// </summary>
        private void SetEraserMode()
        {
            _isErasing = true;
            inkCanvas.EditingMode = InkCanvasEditingMode.EraseByStroke;
            inkCanvas.Cursor = Cursors.Cross;
            UpdateToolButtonStates();
        }

        /// <summary>
        /// 更新所有工具按钮的视觉状态
        /// 三个工具互斥：鼠标模式、画笔、橡皮擦
        /// </summary>
        private void UpdateToolButtonStates()
        {
            var blueColor = Color.FromRgb(0x00, 0xAA, 0xFF);
            var selectedBg = new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF));
            
            bool isPenSelected = !_isErasing && !_isMouseMode;
            bool isEraserSelected = _isErasing && !_isMouseMode;
            
            // 鼠标模式按钮
            ToggleIcon.Fill = _isMouseMode ? new SolidColorBrush(blueColor) : Brushes.White;
            BtnToggle.Background = _isMouseMode ? selectedBg : Brushes.Transparent;
            
            // 画笔按钮
            PenIcon.Fill = isPenSelected ? new SolidColorBrush(blueColor) : Brushes.White;
            BtnPen.Background = isPenSelected ? selectedBg : Brushes.Transparent;
            
            // 橡皮擦按钮
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
                var color = (Color)ColorConverter.ConvertFromString(colorString);
                inkCanvas.DefaultDrawingAttributes.Color = color;
                _currentColorButton = btn;
                UpdateColorSelection();
                
                if (_isErasing)
                {
                    SetPenMode();
                }
            }
        }

        private void UpdateColorSelection()
        {
            var colors = new[] { BtnColorRed, BtnColorGreen, BtnColorBlue, BtnColorYellow, BtnColorWhite, BtnColorBlack };
            foreach (var btn in colors)
            {
                if (btn == _currentColorButton)
                {
                    // 选中状态：半透明白色背景 (类似粗细选择)
                    btn.Background = new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF));
                }
                else
                {
                    // 未选中：透明背景
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
                inkCanvas.DefaultDrawingAttributes.Width = size;
                inkCanvas.DefaultDrawingAttributes.Height = size;
                
                _currentSizeButton = btn;
                UpdateSizeSelection();
                
                if (_isErasing)
                {
                    SetPenMode();
                }
            }
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
                    if (ellipse != null) ellipse.Fill = new SolidColorBrush(Color.FromRgb(0x00, 0xAA, 0xFF));
                }
                else
                {
                    btn.Background = Brushes.Transparent;
                    if (ellipse != null) ellipse.Fill = Brushes.White;
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

        /// <summary>
        /// 鼠标模式按钮点击：进入鼠标模式
        /// 如果已经是鼠标模式则不做任何操作
        /// </summary>
        private void BtnToggle_Click(object sender, RoutedEventArgs e)
        {
            // 如果已经是鼠标模式，不做任何操作
            if (_isMouseMode) return;
            
            _isMouseMode = true;
            _isErasing = false;
            
            // 关闭二级面板
            HideSecondaryPanel();
            
            MainGrid.Background = Brushes.Transparent;
            inkCanvas.IsHitTestVisible = false;
            inkCanvas.Visibility = Visibility.Visible;
            
            UpdateToolButtonStates();
        }

        #endregion

    }


}
