using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;
using DirectShowLib;
using Timer = System.Windows.Forms.Timer;

namespace ccd_helper
{
    public partial class Form1 : Form
    {
        private const int BorderWidth = 10;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        private static readonly IntPtr HWND_BOTTOM = IntPtr.Zero;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOACTIVATE = 0x0010;

        private FormVideo? videoForm;

        private IFilterGraph2? filterGraph;
        private ICaptureGraphBuilder2? captureGraphBuilder;
        private IMediaControl? mediaControl;
        private IVMRWindowlessControl9? windowlessCtrl;

        private InspectionConfig? config;
        private string? currentDataPath;
        private int currentStepIndex = -1;
        private List<Bitmap> stepImages = new List<Bitmap>();
        private List<Bitmap> toolImages = new List<Bitmap>();
        private List<Bitmap> boardImages = new List<Bitmap>();
        private int currentBoardGroupIndex = 0;
        private int totalBoardGroups = 0;

        private string? _projectName;
        private string? _version;

        private Bitmap? _enlargedImage;
        private bool _isEnlarged;
        private Rectangle _enlargeRect;

        private Rectangle _btnPassRect;
        private Rectangle _btnFailRect;
        private Rectangle _btnLoadRect;

        private int _targetFps = 30;
        private int _refreshIntervalMinutes = 5;
        private Timer? _memoryRefreshTimer;

        public Form1()
        {
            InitializeComponent();

            this.Text = "外观检查辅助系统";
            this.Size = new Size(1280, 800);
            this.MinimumSize = new Size(800, 600);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.BackColor = Color.Black;
            this.TransparencyKey = Color.Magenta;
            this.KeyPreview = true;
            this.KeyDown += Form1_KeyDown;

            this.TopMost = false;

            this.MouseClick += Form1_MouseClick;

            this.Load += Form1_Load;
            this.FormClosing += Form1_FormClosing;

            this.Activated += (s, e) => EnsureVideoWindowZOrder();
            this.LocationChanged += (s, e) => EnsureVideoWindowZOrder();
            this.Resize += (s, e) => EnsureVideoWindowZOrder();
            this.VisibleChanged += (s, e) => EnsureVideoWindowZOrder();
        }

        private void EnsureVideoWindowZOrder()
        {
            if (videoForm == null || videoForm.IsDisposed) return;
            SetWindowPos(videoForm.Handle, this.Handle, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_NCHITTEST = 0x0084;
            const int HTCLIENT = 1;
            const int HTLEFT = 10, HTRIGHT = 11, HTTOP = 12, HTTOPLEFT = 13, HTTOPRIGHT = 14;
            const int HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;

            if (m.Msg == WM_NCHITTEST)
            {
                int x = m.LParam.ToInt32() & 0xffff;
                int y = (m.LParam.ToInt32() >> 16) & 0xffff;
                Point pt = this.PointToClient(new Point(x, y));
                int bs = BorderWidth;
                int w = this.ClientSize.Width;
                int h = this.ClientSize.Height;

                if (pt.X < bs && pt.Y < bs) m.Result = (IntPtr)HTTOPLEFT;
                else if (pt.X < bs && pt.Y > h - bs) m.Result = (IntPtr)HTBOTTOMLEFT;
                else if (pt.X > w - bs && pt.Y < bs) m.Result = (IntPtr)HTTOPRIGHT;
                else if (pt.X > w - bs && pt.Y > h - bs) m.Result = (IntPtr)HTBOTTOMRIGHT;
                else if (pt.X < bs) m.Result = (IntPtr)HTLEFT;
                else if (pt.X > w - bs) m.Result = (IntPtr)HTRIGHT;
                else if (pt.Y < bs) m.Result = (IntPtr)HTTOP;
                else if (pt.Y > h - bs) m.Result = (IntPtr)HTBOTTOM;
                else m.Result = (IntPtr)HTCLIENT;
                return;
            }
            base.WndProc(ref m);
        }

        private static string? FindDataPath()
        {
            string[] candidates = new string[]
            {
                Path.Combine(Application.StartupPath, "Data"),
                Path.Combine(Environment.CurrentDirectory, "Data"),
            };

            foreach (var path in candidates)
            {
                if (Directory.Exists(path))
                    return path;
            }
            return null;
        }

        // ========== 加载 ==========
        private void Form1_Load(object? sender, EventArgs e) => InitializeApplication();

        private void InitializeApplication()
        {
            try
            {
                using var selectForm = new SelectionForm();
                if (selectForm.ShowDialog() != DialogResult.OK)
                {
                    Application.Exit();
                    return;
                }

                string selectedProject = selectForm.SelectedProject;
                string selectedVersion = selectForm.SelectedVersion;
                string selectedCamera = selectForm.SelectedCamera;
                _targetFps = selectForm.TargetFps;
                _refreshIntervalMinutes = selectForm.RefreshIntervalMinutes;

                _projectName = selectedProject;
                _version = selectedVersion;

                string? dataDir = FindDataPath();
                if (dataDir == null)
                {
                    MessageBox.Show("未找到 Data 文件夹！", "错误");
                    Application.Exit();
                    return;
                }
                currentDataPath = Path.Combine(dataDir, selectedProject, selectedVersion);
                string configPath = Path.Combine(currentDataPath, "config.json");
                if (!File.Exists(configPath))
                {
                    MessageBox.Show($"未找到配置文件：{configPath}", "错误");
                    Application.Exit();
                    return;
                }

                string json = File.ReadAllText(configPath);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                config = JsonSerializer.Deserialize<InspectionConfig>(json, options);
                if (config == null)
                {
                    MessageBox.Show("配置文件解析失败！", "错误");
                    Application.Exit();
                    return;
                }

                if (config.Steps == null || config.Steps.Count == 0)
                {
                    currentStepIndex = -1;
                    stepImages.Clear();
                    boardImages.Clear();
                    totalBoardGroups = 0;
                    currentBoardGroupIndex = 0;
                }
                else
                {
                    currentStepIndex = 0;
                    DisplayStep(0);
                }

                toolImages.Clear();
                if (config.Tools != null && config.Tools.Count > 0 && config.Tools[0].ToolsImage != null)
                {
                    foreach (var f in config.Tools[0].ToolsImage)
                    {
                        var bmp = LoadBitmap(f);
                        if (bmp != null) toolImages.Add(bmp);
                    }
                }

                videoForm = new FormVideo();
                videoForm.Show();
                videoForm.MouseClick += VideoForm_MouseClick;
                UpdateVideoFormPosition();

                BuildGraphAndRender(selectedCamera, _targetFps);

                this.Activate();
                this.Focus();

                EnsureVideoWindowZOrder();

                this.LocationChanged += (s, ev) => UpdateVideoFormPosition();
                this.Resize += (s, ev) =>
                {
                    UpdateVideoFormPosition();
                    this.Invalidate();
                };

                if (_refreshIntervalMinutes > 0)
                {
                    _memoryRefreshTimer = new Timer();
                    _memoryRefreshTimer.Interval = _refreshIntervalMinutes * 60 * 1000;
                    _memoryRefreshTimer.Tick += (s, e) => RefreshVMR9Surface();
                    _memoryRefreshTimer.Start();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"启动失败: {ex.Message}\n{ex.StackTrace}", "错误");
                Application.Exit();
            }
        }

        // ★ 副窗口鼠标事件：左键后退，右键前进
        private void VideoForm_MouseClick(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
                HandleLeft();
            else if (e.Button == MouseButtons.Right)
                HandleRight();
        }

        private void RefreshVMR9Surface()
        {
            if (windowlessCtrl == null || videoForm == null || videoForm.IsDisposed)
                return;

            try
            {
                var rect = DsRect.FromRectangle(videoForm.ClientRectangle);
                windowlessCtrl.SetVideoPosition(null, rect);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"RefreshVMR9Surface 异常: {ex.Message}");
            }
        }

        private void BuildGraphAndRender(string cameraDeviceName, int targetFps)
        {
            try
            {
                var devices = DsDevice.GetDevicesOfCat(FilterCategory.VideoInputDevice);
                var selected = devices.FirstOrDefault(d => d.Name == cameraDeviceName);
                if (selected == null)
                    throw new Exception($"未找到摄像头：{cameraDeviceName}");

                filterGraph = (IFilterGraph2)new FilterGraph();
                captureGraphBuilder = (ICaptureGraphBuilder2)new CaptureGraphBuilder2();
                mediaControl = (IMediaControl)filterGraph;

                int hr = captureGraphBuilder.SetFiltergraph(filterGraph);
                DsError.ThrowExceptionForHR(hr);

                IBaseFilter? sourceFilter = null;
                hr = filterGraph.AddSourceFilterForMoniker(selected.Mon, null, selected.Name, out sourceFilter);
                DsError.ThrowExceptionForHR(hr);

                // 帧率设置（只匹配帧率，不检查分辨率）
                if (targetFps > 0)
                {
                    IPin? pin = null;
                    try
                    {
                        hr = captureGraphBuilder.FindPin(sourceFilter, PinDirection.Output,
                            PinCategory.Capture, MediaType.Video, true, 0, out pin);
                        if (hr == 0 && pin != null)
                        {
                            IAMStreamConfig? streamConfig = pin as IAMStreamConfig;
                            if (streamConfig != null)
                            {
                                int count, size;
                                hr = streamConfig.GetNumberOfCapabilities(out count, out size);
                                if (hr == 0)
                                {
                                    for (int i = 0; i < count; i++)
                                    {
                                        IntPtr ptr = Marshal.AllocCoTaskMem(size);
                                        try
                                        {
                                            hr = streamConfig.GetStreamCaps(i, out AMMediaType mediaType, ptr);
                                            if (hr == 0 && mediaType.formatType == FormatType.VideoInfo)
                                            {
                                                VideoInfoHeader vih = (VideoInfoHeader)Marshal.PtrToStructure(
                                                    mediaType.formatPtr, typeof(VideoInfoHeader))!;
                                                double fps = 10000000.0 / vih.AvgTimePerFrame;
                                                if (Math.Abs(fps - targetFps) < 1.0)
                                                {
                                                    hr = streamConfig.SetFormat(mediaType);
                                                    DsError.ThrowExceptionForHR(hr);
                                                    DsUtils.FreeAMMediaType(mediaType);
                                                    break;
                                                }
                                                DsUtils.FreeAMMediaType(mediaType);
                                            }
                                        }
                                        finally
                                        {
                                            Marshal.FreeCoTaskMem(ptr);
                                        }
                                    }
                                }
                            }
                        }
                    }
                    finally
                    {
                        if (pin != null) Marshal.ReleaseComObject(pin);
                    }
                }

                var vmr9 = new VideoMixingRenderer9();
                IBaseFilter? vmr9Filter = vmr9 as IBaseFilter;
                if (vmr9Filter == null) throw new Exception("无法创建 VMR9 渲染器");

                hr = filterGraph.AddFilter(vmr9Filter, "VMR9");
                DsError.ThrowExceptionForHR(hr);

                IVMRFilterConfig9? vmr9Config = vmr9 as IVMRFilterConfig9;
                if (vmr9Config != null)
                {
                    hr = vmr9Config.SetRenderingMode(VMR9Mode.Windowless);
                    DsError.ThrowExceptionForHR(hr);
                    hr = vmr9Config.SetNumberOfStreams(1);
                    DsError.ThrowExceptionForHR(hr);
                }

                windowlessCtrl = vmr9 as IVMRWindowlessControl9;
                if (windowlessCtrl != null)
                {
                    if (videoForm == null) throw new Exception("视频窗口未创建");
                    hr = windowlessCtrl.SetVideoClippingWindow(videoForm.Handle);
                    DsError.ThrowExceptionForHR(hr);

                    var rect = DsRect.FromRectangle(videoForm.ClientRectangle);
                    hr = windowlessCtrl.SetVideoPosition(null, rect);
                    DsError.ThrowExceptionForHR(hr);

                    hr = windowlessCtrl.SetAspectRatioMode(VMR9AspectRatioMode.LetterBox);
                    DsError.ThrowExceptionForHR(hr);
                }

                hr = captureGraphBuilder.RenderStream(
                    PinCategory.Capture,
                    MediaType.Video,
                    sourceFilter,
                    null,
                    vmr9Filter
                );
                DsError.ThrowExceptionForHR(hr);

                hr = mediaControl.Run();
                DsError.ThrowExceptionForHR(hr);

                if (videoForm != null)
                {
                    videoForm.Resize -= VideoForm_Resize;
                    videoForm.Resize += VideoForm_Resize;
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"构建视频渲染失败: {ex.Message}", ex);
            }
        }

        private void VideoForm_Resize(object? sender, EventArgs e)
        {
            if (windowlessCtrl != null && videoForm != null)
            {
                var rect = DsRect.FromRectangle(videoForm.ClientRectangle);
                windowlessCtrl.SetVideoPosition(null, rect);
            }
        }

        private void UpdateVideoFormPosition()
        {
            if (videoForm == null) return;
            try
            {
                Rectangle innerRect = new Rectangle(
                    BorderWidth,
                    BorderWidth,
                    this.ClientSize.Width - 2 * BorderWidth,
                    this.ClientSize.Height - 2 * BorderWidth
                );
                var screenRect = this.RectangleToScreen(innerRect);
                videoForm.Bounds = screenRect;
            }
            catch { /* 忽略 */ }
        }

        private void Form1_FormClosing(object? sender, FormClosingEventArgs e)
        {
            _memoryRefreshTimer?.Stop();
            _memoryRefreshTimer?.Dispose();
            videoForm?.Close();
            try { mediaControl?.Stop(); } catch { }
            if (mediaControl != null) Marshal.ReleaseComObject(mediaControl);
            if (captureGraphBuilder != null) Marshal.ReleaseComObject(captureGraphBuilder);
            if (filterGraph != null) Marshal.ReleaseComObject(filterGraph);
        }

        // ========== 绘制 ==========
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            Rectangle innerRect = new Rectangle(
                BorderWidth,
                BorderWidth,
                this.ClientSize.Width - 2 * BorderWidth,
                this.ClientSize.Height - 2 * BorderWidth
            );
            using (SolidBrush brush = new SolidBrush(Color.Magenta))
            {
                e.Graphics.FillRectangle(brush, innerRect);
            }

            DrawLayout(e.Graphics);

            if (_isEnlarged && _enlargedImage != null)
            {
                int w = this.ClientSize.Width;
                int h = this.ClientSize.Height;
                int colW = w / 4;
                int rowH = h / 3;
                Rectangle enlargeRect = new Rectangle(0, rowH, colW, 2 * rowH);

                float ratio = Math.Min((float)enlargeRect.Width / _enlargedImage.Width,
                                       (float)enlargeRect.Height / _enlargedImage.Height);
                int dw = (int)(_enlargedImage.Width * ratio);
                int dh = (int)(_enlargedImage.Height * ratio);
                int dx = enlargeRect.X + (enlargeRect.Width - dw) / 2;
                int dy = enlargeRect.Y + (enlargeRect.Height - dh) / 2;
                e.Graphics.DrawImage(_enlargedImage, dx, dy, dw, dh);
                _enlargeRect = enlargeRect;
            }
        }

        private void DrawLayout(Graphics g)
        {
            g.InterpolationMode = InterpolationMode.Default;
            g.SmoothingMode = SmoothingMode.None;

            int w = this.ClientSize.Width;
            int h = this.ClientSize.Height;
            int colW = w / 4;
            int rowH = h / 3;

            DrawStepCell(g, 0, 0, colW, rowH);
            DrawToolCell(g, colW, 0, colW * 2, rowH);
            DrawBoardCell(g, colW * 3, 0, colW, rowH);
            DrawControlPanel(g, colW * 3, rowH * 2, colW, rowH);
        }

        private void DrawStepCell(Graphics g, int x, int y, int width, int height)
        {
            int totalSteps = config?.Steps?.Count ?? 0;
            int current = (currentStepIndex < 0 || totalSteps == 0) ? 0 : currentStepIndex + 1;
            string title = $"步骤区 ({current}/{totalSteps})";
            using (Brush textBrush = new SolidBrush(Color.White))
            using (Font font = new Font("微软雅黑", 16, FontStyle.Bold))
            using (StringFormat sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                g.DrawString(title, font, textBrush, new Rectangle(x, y + 10, width, 30), sf);
            }

            if (stepImages.Count == 0) return;

            int margin = 5;
            int titleHeight = 40;
            int availW = width - 2 * margin;
            int availH = height - titleHeight - margin;
            int count = stepImages.Count;
            int imageW = (availW - (count - 1) * margin) / count;
            int imageH = availH;
            if (imageW <= 0 || imageH <= 0) return;

            int startX = x + margin;
            int startY = y + titleHeight + margin;
            for (int i = 0; i < count; i++)
            {
                var img = stepImages[i];
                if (img == null) continue;
                float ratio = Math.Min((float)imageW / img.Width, (float)imageH / img.Height);
                int dw = (int)(img.Width * ratio);
                int dh = (int)(img.Height * ratio);
                int cx = startX + i * (imageW + margin);
                int cy = startY + (imageH - dh) / 2;
                g.DrawImage(img, cx, cy, dw, dh);
            }
        }

        private void DrawToolCell(Graphics g, int x, int y, int width, int height)
        {
            using (Brush textBrush = new SolidBrush(Color.White))
            using (Font font = new Font("微软雅黑", 16, FontStyle.Bold))
            using (StringFormat sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                g.DrawString("工具区", font, textBrush, new Rectangle(x, y + 10, width, 30), sf);
            }

            if (toolImages.Count == 0) return;

            int margin = 5;
            int titleHeight = 40;
            int availW = width - 2 * margin;
            int availH = height - titleHeight - margin;
            int count = toolImages.Count;
            int imageW = (availW - (count - 1) * margin) / count;
            int imageH = availH;
            if (imageW <= 0 || imageH <= 0) return;

            int totalWidth = count * imageW + (count - 1) * margin;
            int startX = x + (width - totalWidth) / 2;
            int startY = y + titleHeight + margin;
            for (int i = 0; i < count; i++)
            {
                var img = toolImages[i];
                if (img == null) continue;
                float ratio = Math.Min((float)imageW / img.Width, (float)imageH / img.Height);
                int dw = (int)(img.Width * ratio);
                int dh = (int)(img.Height * ratio);
                int cx = startX + i * (imageW + margin) + (imageW - dw) / 2;
                int cy = startY;
                g.DrawImage(img, cx, cy, dw, dh);
            }
        }

        private void DrawBoardCell(Graphics g, int x, int y, int width, int height)
        {
            string title = $"看板区 ({currentBoardGroupIndex + 1}/{Math.Max(totalBoardGroups, 1)})";
            if (totalBoardGroups == 0) title = "看板区 (0/0)";
            using (Brush textBrush = new SolidBrush(Color.White))
            using (Font font = new Font("微软雅黑", 16, FontStyle.Bold))
            using (StringFormat sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                g.DrawString(title, font, textBrush, new Rectangle(x, y + 10, width, 30), sf);
            }

            if (boardImages.Count == 0) return;

            int groupSize = 2;
            int startIdx = currentBoardGroupIndex * groupSize;
            var currentGroup = boardImages.Skip(startIdx).Take(groupSize).ToList();
            if (currentGroup.Count == 0) return;

            int margin = 5;
            int titleHeight = 40;
            int availW = width - 2 * margin;
            int availH = height - titleHeight - margin;
            int count = currentGroup.Count;
            int imageW = (availW - (count - 1) * margin) / count;
            int imageH = availH;
            if (imageW <= 0 || imageH <= 0) return;

            int totalWidth = count * imageW + (count - 1) * margin;
            int startX = x + width - totalWidth - margin;
            int startY = y + titleHeight + margin;
            for (int i = 0; i < count; i++)
            {
                var img = currentGroup[i];
                if (img == null) continue;
                float ratio = Math.Min((float)imageW / img.Width, (float)imageH / img.Height);
                int dw = (int)(img.Width * ratio);
                int dh = (int)(img.Height * ratio);
                int cx = startX + i * (imageW + margin) + (imageW - dw);
                int cy = startY + (imageH - dh) / 2;
                g.DrawImage(img, cx, cy, dw, dh);
            }
        }

        private void DrawControlPanel(Graphics g, int x, int y, int width, int height)
        {
            int btnWidth = 60;
            int btnHeight = 40;
            int spacing = 8;
            int btnCount = 3;
            int totalBtnHeight = btnCount * btnHeight + (btnCount - 1) * spacing;
            int startY = y + (height - totalBtnHeight - 35) / 2;
            int right = x + width;

            // 良品
            _btnPassRect = new Rectangle(right - btnWidth, startY, btnWidth, btnHeight);
            using (SolidBrush brush = new SolidBrush(Color.Green))
            {
                g.FillRectangle(brush, _btnPassRect);
            }
            g.DrawRectangle(Pens.Black, _btnPassRect);
            using (Brush textBrush = new SolidBrush(Color.White))
            using (Font font = new Font("微软雅黑", 9, FontStyle.Bold))
            using (StringFormat sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                g.DrawString("良品", font, textBrush, _btnPassRect, sf);
            }

            // 不良品
            _btnFailRect = new Rectangle(right - btnWidth, startY + btnHeight + spacing, btnWidth, btnHeight);
            using (SolidBrush brush = new SolidBrush(Color.Red))
            {
                g.FillRectangle(brush, _btnFailRect);
            }
            g.DrawRectangle(Pens.Black, _btnFailRect);
            using (Brush textBrush = new SolidBrush(Color.White))
            using (Font font = new Font("微软雅黑", 9, FontStyle.Bold))
            using (StringFormat sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                g.DrawString("不良", font, textBrush, _btnFailRect, sf);
            }

            // 加载计划
            _btnLoadRect = new Rectangle(right - btnWidth, startY + 2 * (btnHeight + spacing), btnWidth, btnHeight);
            using (SolidBrush brush = new SolidBrush(Color.LightGray))
            {
                g.FillRectangle(brush, _btnLoadRect);
            }
            g.DrawRectangle(Pens.Black, _btnLoadRect);
            using (Brush textBrush = new SolidBrush(Color.Black))
            using (Font font = new Font("微软雅黑", 9, FontStyle.Bold))
            using (StringFormat sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                g.DrawString("计划", font, textBrush, _btnLoadRect, sf);
            }

            // 水印
            string watermark = $"计划名: {_projectName ?? "未加载"}  版本: {_version ?? "未加载"}";
            Rectangle rect = new Rectangle(x, y + height - 25, width, 25);
            float fontSize = 12;
            Font fontWatermark = new Font("微软雅黑", fontSize, FontStyle.Regular);
            SizeF textSize = g.MeasureString(watermark, fontWatermark);
            while (textSize.Width > rect.Width && fontSize > 6)
            {
                fontSize -= 0.5f;
                fontWatermark.Dispose();
                fontWatermark = new Font("微软雅黑", fontSize, FontStyle.Regular);
                textSize = g.MeasureString(watermark, fontWatermark);
            }
            using (Brush textBrush = new SolidBrush(Color.FromArgb(120, 200, 200, 200)))
            using (StringFormat sf = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center })
            {
                g.DrawString(watermark, fontWatermark, textBrush, rect, sf);
            }
            fontWatermark.Dispose();
        }

        // ========== 鼠标点击（仅处理按钮和图片放大） ==========
        private void Form1_MouseClick(object? sender, MouseEventArgs e)
        {
            // 1. 放大状态：点击任意位置关闭放大
            if (_isEnlarged)
            {
                _isEnlarged = false;
                _enlargedImage?.Dispose();
                _enlargedImage = null;
                this.Invalidate();
                return;
            }

            Point pt = e.Location;

            // 2. 检测按钮（优先级最高）
            if (_btnPassRect.Contains(pt)) { ShowResultDialog("良品"); return; }
            if (_btnFailRect.Contains(pt)) { ShowResultDialog("不良品"); return; }
            if (_btnLoadRect.Contains(pt)) { ReloadConfiguration(); return; }

            // 3. 检测图片区域（左键放大，右键无操作）
            if (e.Button == MouseButtons.Left)
            {
                int w = this.ClientSize.Width;
                int h = this.ClientSize.Height;
                int colW = w / 4;
                int rowH = h / 3;

                // 步骤区
                if (stepImages.Count > 0)
                {
                    Rectangle stepCellRect = new Rectangle(0, 0, colW, rowH);
                    if (stepCellRect.Contains(pt))
                    {
                        var clickedImage = GetClickedImageInStepCell(pt, colW, rowH);
                        if (clickedImage != null)
                        {
                            _enlargedImage = (Bitmap)clickedImage.Clone();
                            _isEnlarged = true;
                            this.Invalidate();
                            return;
                        }
                    }
                }

                // 工具区
                if (toolImages.Count > 0)
                {
                    Rectangle toolCellRect = new Rectangle(colW, 0, colW * 2, rowH);
                    if (toolCellRect.Contains(pt))
                    {
                        var clickedImage = GetClickedImageInToolCell(pt, colW, rowH);
                        if (clickedImage != null)
                        {
                            _enlargedImage = (Bitmap)clickedImage.Clone();
                            _isEnlarged = true;
                            this.Invalidate();
                            return;
                        }
                    }
                }

                // 看板区
                if (boardImages.Count > 0)
                {
                    Rectangle boardCellRect = new Rectangle(colW * 3, 0, colW, rowH);
                    if (boardCellRect.Contains(pt))
                    {
                        var clickedImage = GetClickedImageInBoardCell(pt, colW, rowH);
                        if (clickedImage != null)
                        {
                            _enlargedImage = (Bitmap)clickedImage.Clone();
                            _isEnlarged = true;
                            this.Invalidate();
                            return;
                        }
                    }
                }
            }
            // 空白区域不再处理切换（已移至副窗口）
        }

        // ========== 步进/后退逻辑 ==========
        private void HandleLeft()
        {
            if (boardImages.Count > 0 && currentBoardGroupIndex > 0)
            {
                currentBoardGroupIndex--;
                this.Invalidate();
            }
            else if (currentStepIndex > 0)
            {
                DisplayStep(currentStepIndex - 1);
                if (boardImages.Count > 0)
                {
                    currentBoardGroupIndex = totalBoardGroups - 1;
                    this.Invalidate();
                }
            }
        }

        private void HandleRight()
        {
            if (boardImages.Count > 0 && currentBoardGroupIndex < totalBoardGroups - 1)
            {
                currentBoardGroupIndex++;
                this.Invalidate();
            }
            else if (currentStepIndex < (config?.Steps.Count ?? 0) - 1)
            {
                DisplayStep(currentStepIndex + 1);
            }
            else
            {
                MessageBox.Show("所有步骤已完成！");
            }
        }

        // ========== 图片点击辅助 ==========
        private Bitmap? GetClickedImageInStepCell(Point pt, int colW, int rowH)
        {
            int x = 0, y = 0, width = colW, height = rowH;
            if (stepImages.Count == 0) return null;

            int margin = 5, titleHeight = 40;
            int availW = width - 2 * margin;
            int availH = height - titleHeight - margin;
            int count = stepImages.Count;
            int imageW = (availW - (count - 1) * margin) / count;
            int imageH = availH;
            if (imageW <= 0 || imageH <= 0) return null;

            int startX = x + margin;
            int startY = y + titleHeight + margin;
            for (int i = 0; i < count; i++)
            {
                var img = stepImages[i];
                if (img == null) continue;
                float ratio = Math.Min((float)imageW / img.Width, (float)imageH / img.Height);
                int dw = (int)(img.Width * ratio);
                int dh = (int)(img.Height * ratio);
                int cx = startX + i * (imageW + margin);
                int cy = startY + (imageH - dh) / 2;
                Rectangle imgRect = new Rectangle(cx, cy, dw, dh);
                if (imgRect.Contains(pt))
                    return img;
            }
            return null;
        }

        private Bitmap? GetClickedImageInToolCell(Point pt, int colW, int rowH)
        {
            int x = colW, y = 0, width = colW * 2, height = rowH;
            if (toolImages.Count == 0) return null;

            int margin = 5, titleHeight = 40;
            int availW = width - 2 * margin;
            int availH = height - titleHeight - margin;
            int count = toolImages.Count;
            int imageW = (availW - (count - 1) * margin) / count;
            int imageH = availH;
            if (imageW <= 0 || imageH <= 0) return null;

            int totalWidth = count * imageW + (count - 1) * margin;
            int startX = x + (width - totalWidth) / 2;
            int startY = y + titleHeight + margin;
            for (int i = 0; i < count; i++)
            {
                var img = toolImages[i];
                if (img == null) continue;
                float ratio = Math.Min((float)imageW / img.Width, (float)imageH / img.Height);
                int dw = (int)(img.Width * ratio);
                int dh = (int)(img.Height * ratio);
                int cx = startX + i * (imageW + margin) + (imageW - dw) / 2;
                int cy = startY;
                Rectangle imgRect = new Rectangle(cx, cy, dw, dh);
                if (imgRect.Contains(pt))
                    return img;
            }
            return null;
        }

        private Bitmap? GetClickedImageInBoardCell(Point pt, int colW, int rowH)
        {
            int x = colW * 3, y = 0, width = colW, height = rowH;
            if (boardImages.Count == 0) return null;

            int groupSize = 2;
            int startIdx = currentBoardGroupIndex * groupSize;
            var currentGroup = boardImages.Skip(startIdx).Take(groupSize).ToList();
            if (currentGroup.Count == 0) return null;

            int margin = 5, titleHeight = 40;
            int availW = width - 2 * margin;
            int availH = height - titleHeight - margin;
            int count = currentGroup.Count;
            int imageW = (availW - (count - 1) * margin) / count;
            int imageH = availH;
            if (imageW <= 0 || imageH <= 0) return null;

            int totalWidth = count * imageW + (count - 1) * margin;
            int startX = x + width - totalWidth - margin;
            int startY = y + titleHeight + margin;
            for (int i = 0; i < count; i++)
            {
                var img = currentGroup[i];
                if (img == null) continue;
                float ratio = Math.Min((float)imageW / img.Width, (float)imageH / img.Height);
                int dw = (int)(img.Width * ratio);
                int dh = (int)(img.Height * ratio);
                int cx = startX + i * (imageW + margin) + (imageW - dw);
                int cy = startY + (imageH - dh) / 2;
                Rectangle imgRect = new Rectangle(cx, cy, dw, dh);
                if (imgRect.Contains(pt))
                    return img;
            }
            return null;
        }

        // ========== 结果弹窗与日志 ==========
        private void ShowResultDialog(string resultType)
        {
            using var dialog = new ResultForm(resultType, config?.ErrorReason ?? new List<string>());
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                WriteLog(resultType, dialog.SN, dialog.SelectedReasons);
            }
        }

        private void WriteLog(string type, string sn, List<string> reasons)
        {
            try
            {
                string logDir = @"D:\ccd_helper\Logs";
                if (!Directory.Exists(logDir))
                    Directory.CreateDirectory(logDir);

                string fileName = DateTime.Now.ToString("yyyy-MM-dd") + ".csv";
                string filePath = Path.Combine(logDir, fileName);

                string reasonsStr = reasons != null && reasons.Count > 0 ? string.Join(";", reasons) : "";
                string timeStr = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
                string line = $"{EscapeCsvField(type)},{EscapeCsvField(sn)},{EscapeCsvField(reasonsStr)},{EscapeCsvField(timeStr)}";
                File.AppendAllText(filePath, line + Environment.NewLine, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"写入日志失败: {ex.Message}", "错误");
            }
        }

        private static string EscapeCsvField(string field)
        {
            if (string.IsNullOrEmpty(field))
                return "";
            if (field.Contains(",") || field.Contains("\"") || field.Contains("\n"))
                return "\"" + field.Replace("\"", "\"\"") + "\"";
            return field;
        }

        // ========== 重新加载 ==========
        private void ReloadConfiguration()
        {
            try { mediaControl?.Stop(); } catch { }
            if (videoForm != null && !videoForm.IsDisposed)
            {
                videoForm.Close();
                videoForm.Dispose();
                videoForm = null;
            }
            config = null;
            stepImages.Clear();
            toolImages.Clear();
            boardImages.Clear();
            _isEnlarged = false;
            _enlargedImage?.Dispose();
            _enlargedImage = null;
            currentStepIndex = -1;
            currentBoardGroupIndex = 0;
            totalBoardGroups = 0;

            try
            {
                using var selectForm = new SelectionForm();
                if (selectForm.ShowDialog() != DialogResult.OK)
                    return;

                string selectedProject = selectForm.SelectedProject;
                string selectedVersion = selectForm.SelectedVersion;
                string selectedCamera = selectForm.SelectedCamera;
                _targetFps = selectForm.TargetFps;
                _refreshIntervalMinutes = selectForm.RefreshIntervalMinutes;

                _projectName = selectedProject;
                _version = selectedVersion;

                string? dataDir = FindDataPath();
                if (dataDir == null)
                {
                    MessageBox.Show("未找到 Data 文件夹！", "错误");
                    return;
                }
                currentDataPath = Path.Combine(dataDir, selectedProject, selectedVersion);
                string configPath = Path.Combine(currentDataPath, "config.json");
                if (!File.Exists(configPath))
                {
                    MessageBox.Show($"未找到配置文件：{configPath}", "错误");
                    return;
                }

                string json = File.ReadAllText(configPath);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                config = JsonSerializer.Deserialize<InspectionConfig>(json, options);
                if (config == null)
                {
                    MessageBox.Show("配置文件解析失败！", "错误");
                    return;
                }

                if (config.Steps == null || config.Steps.Count == 0)
                {
                    currentStepIndex = -1;
                    stepImages.Clear();
                    boardImages.Clear();
                    totalBoardGroups = 0;
                    currentBoardGroupIndex = 0;
                }
                else
                {
                    currentStepIndex = 0;
                    DisplayStep(0);
                }

                toolImages.Clear();
                if (config.Tools != null && config.Tools.Count > 0 && config.Tools[0].ToolsImage != null)
                {
                    foreach (var f in config.Tools[0].ToolsImage)
                    {
                        var bmp = LoadBitmap(f);
                        if (bmp != null) toolImages.Add(bmp);
                    }
                }

                videoForm = new FormVideo();
                videoForm.Show();
                videoForm.MouseClick += VideoForm_MouseClick;
                UpdateVideoFormPosition();

                BuildGraphAndRender(selectedCamera, _targetFps);

                this.Activate();
                this.Focus();
                EnsureVideoWindowZOrder();
                this.Invalidate();

                if (_refreshIntervalMinutes > 0)
                {
                    _memoryRefreshTimer?.Stop();
                    _memoryRefreshTimer = new Timer();
                    _memoryRefreshTimer.Interval = _refreshIntervalMinutes * 60 * 1000;
                    _memoryRefreshTimer.Tick += (s, e) => RefreshVMR9Surface();
                    _memoryRefreshTimer.Start();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"重新加载失败: {ex.Message}\n{ex.StackTrace}", "错误");
            }
        }

        // ========== 数据切换 ==========
        private void DisplayStep(int index)
        {
            if (config == null || config.Steps == null || config.Steps.Count == 0)
            {
                stepImages.Clear();
                boardImages.Clear();
                totalBoardGroups = 0;
                currentBoardGroupIndex = 0;
                this.Invalidate();
                return;
            }

            if (index < 0 || index >= config.Steps.Count)
                index = 0;

            var step = config.Steps[index];
            currentStepIndex = index;

            stepImages.Clear();
            foreach (var f in step.StepImages)
            {
                var bmp = LoadBitmap(f);
                if (bmp != null) stepImages.Add(bmp);
            }

            boardImages.Clear();
            foreach (var f in step.BoardImages)
            {
                var bmp = LoadBitmap(f);
                if (bmp != null) boardImages.Add(bmp);
            }
            currentBoardGroupIndex = 0;
            totalBoardGroups = (int)Math.Ceiling((double)boardImages.Count / 2);

            _isEnlarged = false;
            _enlargedImage?.Dispose();
            _enlargedImage = null;

            this.Invalidate();
            this.Focus();
        }

        private Bitmap? LoadBitmap(string? fileName)
        {
            if (string.IsNullOrEmpty(fileName) || currentDataPath == null) return null;
            string path = Path.Combine(currentDataPath, fileName);
            if (File.Exists(path))
                try { return new Bitmap(path); } catch { return null; }
            return null;
        }

        // ========== 按键 ==========
        private void Form1_KeyDown(object? sender, KeyEventArgs e)
        {
            if (config == null || config.Steps == null || config.Steps.Count == 0)
            {
                if (e.KeyCode == Keys.Escape)
                    Application.Exit();
                e.Handled = true;
                return;
            }

            switch (e.KeyCode)
            {
                case Keys.Enter:
                    DisplayStep(0);
                    e.Handled = true;
                    break;

                case Keys.Escape:
                    Application.Exit();
                    e.Handled = true;
                    break;

                case Keys.Right:
                    HandleRight();
                    e.Handled = true;
                    break;

                case Keys.Left:
                    HandleLeft();
                    e.Handled = true;
                    break;
            }
        }
    }
}