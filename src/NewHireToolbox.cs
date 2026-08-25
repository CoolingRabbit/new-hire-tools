// ============================================================================
// 新员工入职工具箱 —— 三合一单文件应用
//
// 三个功能页（Tab）：
//   1. NAS懒人映射：输入员工号，自动计算初始密码（不显示）、探测内置 NAS、
//      保存凭据、列出共享，勾选后可拖动排序并按顺序映射（首项 Z: 依次类推）；
//   2. 初始密码查询：输入员工号计算初始密码，可复制到剪贴板；
//   3. 自定义映射：手动输入服务器地址 / 用户名 / 密码，探测共享后同上映射。
//   Tab1 / Tab3 均附"一键清除所有映射"。
//
// 设计系统（Win11 原生风格 + macOS 审美）：
//   Theme           —— 设计 token：配色 / 字阶 / 8px 间距网格 / 圆角 / 投影
//   RoundedButton   —— 圆角按钮，悬停/按下颜色渐变过渡
//   InputBox        —— 圆角输入框，聚焦时边框强调色高亮
//   SegmentedTabBar —— 分段式导航，选中胶囊滑动动画
//   ShareListView   —— 共用共享列表（勾选 + 盘符徽章 + 拖动排序动画）
//   NasOperations   —— net use / cmdkey / net view / WMI 封装
//   Fx              —— 动效辅助（缓动、颜色插值动画）
//
// 编译（源码位于 src/，也可直接双击根目录 build.bat，输出到 dist/）：
//   C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe ^
//       -target:winexe -utf8output -codepage:65001 -nologo ^
//       -reference:System.dll,System.Drawing.dll,System.Windows.Forms.dll,System.Management.dll ^
//       -out:dist\NewHireToolbox.exe src\NewHireToolbox.cs src\PasswordGenerator.cs
//
// 安全说明：
//   - Seed 内置于 PasswordGenerator.cs，不显示在界面、不写入日志/文件；
//   - 自动计算的密码仅用于探测与凭据保存，不显示、不落盘。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace NewHireTools
{
    // ========================================================================
    // 设计 token：颜色 / 字体 / 间距（8px 网格）/ 圆角 / 控件尺寸
    // 注：字体为静态共享、随进程生命周期存在，无需释放。
    // ========================================================================
    internal static class Theme
    {
        // ---- 配色 ----
        public static readonly Color WindowBg   = Color.FromArgb(245, 245, 247); // macOS 浅灰底
        public static readonly Color CardBg     = Color.White;
        public static readonly Color BorderSoft = Color.FromArgb(24, 0, 0, 0);   // 半透明细边框
        public static readonly Color Accent     = Color.FromArgb(0, 103, 192);   // Win11 强调蓝
        public static readonly Color AccentHot  = Color.FromArgb(26, 126, 208);
        public static readonly Color AccentDown = Color.FromArgb(0, 84, 158);
        public static readonly Color HoverGray  = Color.FromArgb(120, 238, 238, 240);
        public static readonly Color TextMain   = Color.FromArgb(27, 27, 27);
        public static readonly Color TextSub    = Color.FromArgb(110, 110, 112);
        public static readonly Color Danger     = Color.FromArgb(196, 43, 28);
        public static readonly Color DangerBorder = Color.FromArgb(90, 196, 43, 28);
        public static readonly Color Success    = Color.FromArgb(15, 123, 15);
        public static readonly Color LogBg      = Color.FromArgb(250, 250, 251);

        // ---- 字阶（固定微软雅黑）----
        public static readonly Font TitleFont    = new Font("Microsoft YaHei UI", 13f, FontStyle.Bold);
        public static readonly Font UiFont       = new Font("Microsoft YaHei UI", 9.5f);
        public static readonly Font UiFontSmall  = new Font("Microsoft YaHei UI", 8.5f);
        public static readonly Font MonoFont     = new Font("Microsoft YaHei UI", 9f);
        public static readonly Font PasswordFont = new Font("Microsoft YaHei UI", 20f, FontStyle.Bold);

        // ---- 间距（8px 网格）----
        public const int SpaceXS = 4;
        public const int SpaceS  = 8;
        public const int SpaceM  = 16;
        public const int SpaceL  = 24;
        public const int SpaceXL = 32;

        // ---- 圆角 ----
        public const int RadiusCard   = 12;
        public const int RadiusButton = 6;

        // ---- 控件尺寸 ----
        public const int InputHeight  = 30;
        public const int ButtonHeight = 32;
        public const int PageMargin   = 24;    // 卡片内边距
    }

    // ========================================================================
    // 动效辅助：缓动与颜色插值
    // ========================================================================
    internal static class Fx
    {
        /// <summary>ease-out quart：0..1 -> 0..1（出场动画用强 ease-out，
        /// 起步快、收尾稳，参考 cubic-bezier(0.23,1,0.32,1) 的手感）</summary>
        public static float EaseOutCubic(float t)
        {
            return 1f - (float)Math.Pow(1f - t, 4);
        }

        public static Color Lerp(Color a, Color b, float t)
        {
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        /// <summary>一次性颜色渐变动画（如密码文字淡入）。</summary>
        public static void AnimateColor(Color from, Color to, int durationMs, Action<Color> apply)
        {
            Timer timer = new Timer();
            timer.Interval = 15;
            int start = Environment.TickCount;
            timer.Tick += delegate
            {
                float t = (Environment.TickCount - start) / (float)durationMs;
                if (t >= 1f) { apply(to); timer.Stop(); timer.Dispose(); return; }
                apply(Lerp(from, to, EaseOutCubic(t)));
            };
            timer.Start();
        }
    }

    // ========================================================================
    // 圆角路径绘制辅助
    // ========================================================================
    internal static class RoundHelper
    {
        public static GraphicsPath Create(Rectangle rect, int radius)
        {
            int d = radius * 2;
            GraphicsPath path = new GraphicsPath();
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        /// <summary>
        /// 在指定矩形周围绘制柔和投影（外层最淡、逐层加深的圆角矩形叠加，
        /// 整体略向下偏移，模拟自然悬浮感）。
        /// 阴影带蓝灰色调（不用纯黑），与浅色环境融合更自然。
        /// </summary>
        public static void DrawShadow(Graphics g, Rectangle rect, int radius)
        {
            for (int i = 5; i >= 1; i--)
            {
                Rectangle r = rect;
                r.Inflate(i, i);
                r.Offset(0, 2);
                using (GraphicsPath path = Create(r, radius + i))
                using (SolidBrush brush = new SolidBrush(Color.FromArgb(9 - i, 27, 43, 66)))
                    g.FillPath(brush, path);
            }
        }
    }

    // ========================================================================
    // 圆角按钮（自绘：常态 / 悬停 / 按下 / 禁用，颜色渐变过渡）
    // ========================================================================
    internal class RoundedButton : Button
    {
        public Color NormalColor  = Theme.Accent;
        public Color HoverColor   = Theme.AccentHot;
        public Color PressedColor = Theme.AccentDown;
        public bool Outline = false;                       // true = 白底描边次级按钮
        public Color OutlineColor = Theme.BorderSoft;
        public Color ParentBackColor = Theme.CardBg;   // 圆角外区域填充色（按钮均在卡片上）

        private bool _hover;
        private bool _down;
        private Color _shownColor;                         // 当前显示颜色（动画插值）
        private Color _targetColor;
        private readonly Timer _colorTimer;

        public RoundedButton()
        {
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            ForeColor = Color.White;
            Font = Theme.UiFont;
            Cursor = Cursors.Hand;
            Height = Theme.ButtonHeight;

            _shownColor = NormalColor;
            _targetColor = NormalColor;
            _colorTimer = new Timer();
            _colorTimer.Interval = 15;
            _colorTimer.Tick += ColorTimer_Tick;
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true;  UpdateTargetColor(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; UpdateTargetColor(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { _down = true;  UpdateTargetColor(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e)   { _down = false; UpdateTargetColor(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { SnapTargetColor(); base.OnEnabledChanged(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        // 句柄创建时（所有属性已赋值）对齐一次显示颜色，
        // 避免 Outline 等属性在构造函数之后设置导致首绘颜色错误
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            SnapTargetColor();
        }

        /// <summary>状态变化时更新目标颜色并启动渐变。</summary>
        private void UpdateTargetColor()
        {
            if (!Enabled)     _targetColor = Color.FromArgb(240, 240, 240);
            else if (Outline) _targetColor = _down ? Color.FromArgb(236, 236, 236)
                                             : (_hover ? Color.FromArgb(245, 245, 246) : Theme.CardBg);
            else              _targetColor = _down ? PressedColor : (_hover ? HoverColor : NormalColor);
            _colorTimer.Start();
        }

        /// <summary>禁用等状态立即变色，不播放渐变。</summary>
        private void SnapTargetColor()
        {
            UpdateTargetColor();
            _shownColor = _targetColor;
            _colorTimer.Stop();
            Invalidate();
        }

        private void ColorTimer_Tick(object sender, EventArgs e)
        {
            Color next = Fx.Lerp(_shownColor, _targetColor, 0.35f);
            // 足够接近时收敛到目标色并停止
            if (Math.Abs(next.R - _targetColor.R) < 3 &&
                Math.Abs(next.G - _targetColor.G) < 3 &&
                Math.Abs(next.B - _targetColor.B) < 3)
            {
                _shownColor = _targetColor;
                _colorTimer.Stop();
            }
            else
            {
                _shownColor = next;
            }
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            // 不使用 Region 裁剪（GDI Region 无抗锯齿会产生锯齿）：
            // 圆角外区域以父容器背景色填充，圆角全靠抗锯齿绘制
            using (SolidBrush bg = new SolidBrush(ParentBackColor))
                e.Graphics.FillRectangle(bg, ClientRectangle);

            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);

            // 按压微交互：内容整体轻微内缩，模拟 0.98 缩放的物理按压感
            if (_down && Enabled) rect.Inflate(-2, -1);

            Color fg = Enabled ? ForeColor : Color.FromArgb(160, 160, 160);

            using (GraphicsPath path = RoundHelper.Create(rect, Theme.RadiusButton))
            using (SolidBrush brush = new SolidBrush(_shownColor))
            {
                e.Graphics.FillPath(brush, path);
                if (Outline)
                {
                    Color oc = Enabled ? OutlineColor : Theme.BorderSoft;
                    using (Pen pen = new Pen(oc, 1f)) e.Graphics.DrawPath(pen, path);
                }
            }
            TextRenderer.DrawText(e.Graphics, this.Text, this.Font, this.ClientRectangle, fg,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

            // 键盘焦点指示（可访问性）：实心按钮内白框，描边按钮内强调色框
            if (this.Focused && Enabled)
            {
                Rectangle fr = rect;
                fr.Inflate(-3, -3);
                using (GraphicsPath fp = RoundHelper.Create(fr, Theme.RadiusButton - 2))
                using (Pen pen = new Pen(Outline ? Theme.Accent : Color.White, 1.5f))
                    e.Graphics.DrawPath(pen, fp);
            }
        }
    }

    // ========================================================================
    // 圆角输入框（白底圆角，聚焦时边框强调色高亮）
    // ========================================================================
    internal class InputBox : Panel
    {
        public readonly TextBox Inner;
        private bool _focused;

        public InputBox(bool password)
        {
            Height = Theme.InputHeight;
            BackColor = Color.White;
            Padding = new Padding(10, 6, 8, 4);
            Cursor = Cursors.IBeam;
            DoubleBuffered = true;

            Inner = new TextBox();
            Inner.BorderStyle = BorderStyle.None;
            Inner.Dock = DockStyle.Fill;
            Inner.Font = Theme.UiFont;
            Inner.BackColor = Color.White;
            if (password) Inner.UseSystemPasswordChar = true;
            Inner.Enter += delegate { _focused = true;  Invalidate(); };
            Inner.Leave += delegate { _focused = false; Invalidate(); };
            Controls.Add(Inner);
        }

        public override string Text { get { return Inner.Text; } set { Inner.Text = value; } }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            Inner.Focus();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = RoundHelper.Create(rect, Theme.RadiusButton))
            using (SolidBrush brush = new SolidBrush(Color.White))
            using (Pen pen = new Pen(_focused ? Theme.Accent : Theme.BorderSoft, _focused ? 1.6f : 1f))
            {
                e.Graphics.FillPath(brush, path);
                e.Graphics.DrawPath(pen, path);
            }
            base.OnPaint(e);
        }
    }

    // ========================================================================
    // Tab 栏细线图标（零依赖自绘几何图形：共享节点 / 钥匙 / 地球）
    // ========================================================================
    internal static class TabIcons
    {
        public const int Share = 0;
        public const int Key   = 1;
        public const int Globe = 2;

        public static void Draw(Graphics g, int kind, Rectangle rect, Color color)
        {
            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float x = rect.X, y = rect.Y, w = rect.Width, h = rect.Height;

            using (Pen pen = new Pen(color, Math.Max(1.2f, w / 10f)))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;

                switch (kind)
                {
                    case Share:   // 共享节点：三线三点
                    {
                        PointF top   = new PointF(x + w * 0.50f, y + h * 0.24f);
                        PointF left  = new PointF(x + w * 0.22f, y + h * 0.74f);
                        PointF right = new PointF(x + w * 0.78f, y + h * 0.74f);
                        g.DrawLine(pen, top, left);
                        g.DrawLine(pen, top, right);
                        g.DrawLine(pen, left, right);
                        float r = w * 0.13f;
                        g.DrawEllipse(pen, top.X - r, top.Y - r, r * 2, r * 2);
                        g.DrawEllipse(pen, left.X - r, left.Y - r, r * 2, r * 2);
                        g.DrawEllipse(pen, right.X - r, right.Y - r, r * 2, r * 2);
                        break;
                    }
                    case Key:   // 钥匙：圆环 + 柄 + 齿
                    {
                        float r = w * 0.20f;
                        float cx = x + w * 0.34f, cy = y + h * 0.38f;
                        g.DrawEllipse(pen, cx - r, cy - r, r * 2, r * 2);
                        g.DrawLine(pen, cx + r * 0.7f, cy + r * 0.7f, x + w * 0.82f, y + h * 0.82f);
                        g.DrawLine(pen, x + w * 0.64f, y + h * 0.64f, x + w * 0.64f, y + h * 0.82f);
                        g.DrawLine(pen, x + w * 0.76f, y + h * 0.76f, x + w * 0.76f, y + h * 0.92f);
                        break;
                    }
                    case Globe:   // 地球：外圆 + 竖椭圆 + 横线
                    {
                        float r = w * 0.38f;
                        float cx = x + w * 0.5f, cy = y + h * 0.5f;
                        g.DrawEllipse(pen, cx - r, cy - r, r * 2, r * 2);
                        g.DrawEllipse(pen, cx - r * 0.5f, cy - r, r, r * 2);
                        g.DrawLine(pen, cx - r, cy, cx + r, cy);
                        break;
                    }
                }
            }
            g.SmoothingMode = old;
        }
    }

    // ========================================================================
    // 分段式 Tab 导航栏（选中胶囊滑动动画，自绘图标+文字与命中检测）
    // ========================================================================
    internal class SegmentedTabBar : Control
    {
        private const int Pad = 4;
        private const int SegWidth = 132;
        private const int SegGap = 4;
        private const int AnimMs = 170;

        private readonly string[] _items;
        private readonly int[] _icons;
        private int _selected;
        private int _hover = -1;
        private float _pillX;                                // 选中胶囊当前 X（动画驱动）
        private readonly Timer _animTimer;
        private float _pillFrom, _pillTo;
        private int _animStart;

        public event EventHandler SelectedIndexChanged;

        public SegmentedTabBar(string[] items, int[] icons)
        {
            _items = items;
            _icons = icons;
            DoubleBuffered = true;
            BackColor = Theme.WindowBg;
            Height = 42;
            Width = Pad * 2 + items.Length * SegWidth + (items.Length - 1) * SegGap;
            _pillX = SegmentRect(0).X;

            _animTimer = new Timer();
            _animTimer.Interval = 15;
            _animTimer.Tick += AnimTimer_Tick;
        }

        public int SelectedIndex
        {
            get { return _selected; }
            set
            {
                if (value == _selected || value < 0 || value >= _items.Length) return;
                _selected = value;
                // 启动胶囊滑动动画
                _pillFrom = _pillX;
                _pillTo = SegmentRect(value).X;
                _animStart = Environment.TickCount;
                _animTimer.Start();
                if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty);
                Invalidate();
            }
        }

        private Rectangle SegmentRect(int i)
        {
            return new Rectangle(Pad + i * (SegWidth + SegGap), Pad, SegWidth, Height - Pad * 2);
        }

        private void AnimTimer_Tick(object sender, EventArgs e)
        {
            float t = (Environment.TickCount - _animStart) / (float)AnimMs;
            if (t >= 1f) { _pillX = _pillTo; _animTimer.Stop(); }
            else _pillX = _pillFrom + (_pillTo - _pillFrom) * Fx.EaseOutCubic(t);
            Invalidate();
        }

        private int HitTest(int x)
        {
            for (int i = 0; i < _items.Length; i++)
                if (SegmentRect(i).Contains(x, Height / 2)) return i;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int hit = HitTest(e.X);
            if (hit != _hover) { _hover = hit; Invalidate(); }
            Cursor = (hit >= 0) ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = -1;
            Invalidate();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            int hit = HitTest(e.X);
            if (hit >= 0) SelectedIndex = hit;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle bar = new Rectangle(0, 0, Width - 1, Height - 3);
            int pillRadius = bar.Height / 2;

            // 栏体投影 + 白底胶囊 + 半透明细边框
            RoundHelper.DrawShadow(g, bar, pillRadius);
            using (GraphicsPath path = RoundHelper.Create(bar, pillRadius))
            using (SolidBrush brush = new SolidBrush(Theme.CardBg))
            using (Pen pen = new Pen(Theme.BorderSoft, 1f))
            {
                g.FillPath(brush, path);
                g.DrawPath(pen, path);
            }

            // 悬停高亮（未选中项）
            if (_hover >= 0 && _hover != _selected)
            {
                Rectangle hr = SegmentRect(_hover);
                hr.Inflate(-2, -2);
                using (GraphicsPath path = RoundHelper.Create(hr, (Height - Pad * 2) / 2))
                using (SolidBrush brush = new SolidBrush(Theme.HoverGray))
                    g.FillPath(brush, path);
            }

            // 选中胶囊（位置由动画驱动）
            Rectangle sr = SegmentRect(_selected);
            Rectangle pill = new Rectangle((int)_pillX, sr.Y, sr.Width, sr.Height);
            using (GraphicsPath path = RoundHelper.Create(pill, pill.Height / 2))
            using (SolidBrush brush = new SolidBrush(Theme.Accent))
                g.FillPath(brush, path);

            // 段文字
            for (int i = 0; i < _items.Length; i++)
            {
                Color fg = (i == _selected) ? Color.White : Theme.TextMain;
                Rectangle seg = SegmentRect(i);

                // 图标 + 文字作为整体居中（文字按实测高度手动垂直居中，
                // 避免字体度量差异导致 TextRenderer.VerticalCenter 偏移）
                Size textSize = TextRenderer.MeasureText(g, _items[i], Theme.UiFont);
                int iconSize = 14;
                int gap = 6;
                int totalW = iconSize + gap + textSize.Width;
                int startX = seg.X + (seg.Width - totalW) / 2;
                int iconY = seg.Y + (seg.Height - iconSize) / 2;
                int textY = seg.Y + (seg.Height - textSize.Height) / 2;
                TabIcons.Draw(g, _icons[i],
                    new Rectangle(startX, iconY, iconSize, iconSize), fg);
                TextRenderer.DrawText(g, _items[i], Theme.UiFont,
                    new Point(startX + iconSize + gap, textY), fg);
            }
        }
    }

    // ========================================================================
    // 圆角卡片（内容容器）
    // 不使用 Region 裁剪（GDI Region 无抗锯齿会产生锯齿）：
    // 背景填充窗口底色，阴影与白色圆角卡面全部由抗锯齿绘制完成。
    // ========================================================================
    internal class CardPanel : Panel
    {
        public CardPanel()
        {
            BackColor = Theme.CardBg;
            DoubleBuffered = true;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // 圆角外区域与窗口底色融合（替代 Region 硬裁剪）
            using (SolidBrush brush = new SolidBrush(Theme.WindowBg))
                e.Graphics.FillRectangle(brush, ClientRectangle);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // 右侧与底部预留投影空间
            Rectangle card = new Rectangle(0, 0, Width - 5, Height - 7);
            RoundHelper.DrawShadow(g, card, Theme.RadiusCard);

            using (GraphicsPath path = RoundHelper.Create(card, Theme.RadiusCard))
            using (SolidBrush brush = new SolidBrush(Theme.CardBg))
            using (Pen pen = new Pen(Theme.BorderSoft, 1f))
            {
                g.FillPath(brush, path);
                g.DrawPath(pen, path);
            }
            base.OnPaint(e);
        }
    }

    // ========================================================================
    // 进度环（异步操作期间在日志标题旁旋转指示，Fluent ProgressRing 风格）
    // ========================================================================
    internal class ProgressRing : Control
    {
        private int _angle;
        private readonly Timer _timer;

        public ProgressRing()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint, true);
            Size = new Size(18, 18);
            BackColor = Theme.CardBg;   // 进度环始终位于卡片上，用纯色背景即可
            Visible = false;

            _timer = new Timer();
            _timer.Interval = 16;
            _timer.Tick += delegate { _angle = (_angle + 12) % 360; Invalidate(); };   // 更快的旋转让加载感觉更快
        }

        /// <summary>显示并开始旋转 / 隐藏并停止。</summary>
        public bool Running
        {
            set
            {
                Visible = value;
                if (value) _timer.Start(); else _timer.Stop();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush bg = new SolidBrush(Theme.CardBg))
                g.FillRectangle(bg, ClientRectangle);
            Rectangle r = new Rectangle(1, 1, Width - 3, Height - 3);
            using (Pen track = new Pen(Color.FromArgb(40, Theme.Accent), 2f))
                g.DrawEllipse(track, r);
            using (Pen pen = new Pen(Theme.Accent, 2f))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                g.DrawArc(pen, r, _angle, 270);
            }
        }
    }

    // ========================================================================
    // 圆角边框容器（把直角的原生列表/日志控件包出平滑圆角）
    // 圆角外填充卡片白，内部圆角区域填充内容底色，子控件内缩避免方角露出。
    // ========================================================================
    internal class RoundedFrame : Panel
    {
        /// <summary>子列表为空时显示的引导文案（null 表示不显示）。</summary>
        public string EmptyHint;

        public RoundedFrame()
        {
            DoubleBuffered = true;
            BackColor = Theme.CardBg;
            Padding = new Padding(4, 3, 4, 3);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // 圆角外区域与卡片底色融合
            using (SolidBrush brush = new SolidBrush(Theme.CardBg))
                e.Graphics.FillRectangle(brush, ClientRectangle);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = RoundHelper.Create(rect, Theme.RadiusButton))
            using (SolidBrush brush = new SolidBrush(Theme.LogBg))
            using (Pen pen = new Pen(Color.FromArgb(30, 0, 0, 0), 1f))
            {
                g.FillPath(brush, path);
                g.DrawPath(pen, path);
            }

            // 空状态引导文案（子控件隐藏时可见）
            bool childVisible = false;
            foreach (Control c in Controls) if (c.Visible) childVisible = true;
            if (!childVisible && !string.IsNullOrEmpty(EmptyHint))
            {
                TextRenderer.DrawText(g, EmptyHint, Theme.UiFontSmall, ClientRectangle,
                    Theme.TextSub,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            base.OnPaint(e);
        }
    }

    // ========================================================================
    // Fluent 开关（ToggleSwitch）：圆角滑轨 + 动画旋钮
    // ========================================================================
    internal class ToggleSwitch : Control
    {
        private bool _checked;
        private float _pos;                  // 旋钮位置 0..1（动画驱动）
        private float _target;
        private readonly Timer _timer;

        public event EventHandler CheckedChanged;

        public ToggleSwitch()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint, true);
            Size = new Size(38, 20);
            BackColor = Theme.CardBg;
            Cursor = Cursors.Hand;

            _timer = new Timer();
            _timer.Interval = 15;
            _timer.Tick += delegate
            {
                float next = _pos + (_target - _pos) * 0.4f;
                if (Math.Abs(next - _target) < 0.03f) { _pos = _target; _timer.Stop(); }
                else _pos = next;
                Invalidate();
            };
        }

        public bool Checked
        {
            get { return _checked; }
            set
            {
                if (value == _checked) return;
                _checked = value;
                _target = value ? 1f : 0f;
                _timer.Start();
                if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
            }
        }

        /// <summary>设置状态但不触发事件、不播放动画（用于多实例同步）。</summary>
        public void SetCheckedSilent(bool value)
        {
            _checked = value;
            _pos = _target = value ? 1f : 0f;
            Invalidate();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            Checked = !Checked;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush bg = new SolidBrush(Theme.CardBg))
                g.FillRectangle(bg, ClientRectangle);

            // 滑轨
            Rectangle track = new Rectangle(0, 1, Width - 1, Height - 2);
            Color trackColor = _checked ? Theme.Accent : Color.FromArgb(210, 210, 216);
            using (GraphicsPath path = RoundHelper.Create(track, track.Height / 2))
            using (SolidBrush brush = new SolidBrush(trackColor))
                g.FillPath(brush, path);

            // 旋钮（带一点投影）
            int knob = Height - 8;
            int knobX = 4 + (int)(_pos * (Width - 8 - knob));
            using (SolidBrush shadow = new SolidBrush(Color.FromArgb(40, 0, 0, 0)))
                g.FillEllipse(shadow, knobX, 5, knob, knob);
            using (SolidBrush brush = new SolidBrush(Color.White))
                g.FillEllipse(brush, knobX, 4, knob, knob);
        }
    }

    // ========================================================================
    // 共享列表条目
    // ========================================================================
    internal class ShareEntry
    {
        public string Unc;       // \\服务器\共享名（映射时使用）
        public string Name;      // 共享名
        public string Tag;       // 来源标签（如 CORP1-NAS），可为 null
    }

    // ========================================================================
    // 共享列表（各功能页共用，完全自绘）
    // - 每项显示即将映射的盘符徽章（Z:、Y:、X: …… 按勾选顺序分配）；
    // - 单击勾选；按住拖动排序：被拖项悬浮（投影 + 强调条），
    //   其它行以缓动动画退避让位，松手后平滑归位；
    // - 原生 CheckedListBox 无法实现上述效果，故完全自绘。
    // ========================================================================
    internal class ShareListView : Control
    {
        private const int RowH = 34;

        private readonly List<ShareEntry> _entries = new List<ShareEntry>();
        private readonly List<bool> _checked = new List<bool>();
        private readonly List<float> _offsets = new List<float>();   // 每行当前纵向偏移（动画驱动）
        private readonly List<float> _targets = new List<float>();

        private int _downIndex = -1;     // 按下的行
        private Point _downPoint;        // 按下位置（拖动阈值判断）
        private int _hoverIndex = -1;
        private bool _dragging;
        private float _dragRowY;         // 被拖行当前视觉 Y
        private float _grabOffsetY;      // 按下点在行内的纵向偏移
        private float _scroll;           // 滚轮滚动偏移（内容超出可视高度时）
        private readonly Timer _animTimer;

        public ShareListView()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw, true);
            BackColor = Theme.LogBg;
            Font = Theme.UiFont;
            Visible = false;   // 空列表时隐藏，由外层 RoundedFrame 显示引导文案

            _animTimer = new Timer();
            _animTimer.Interval = 15;
            _animTimer.Tick += AnimTimer_Tick;
        }

        public int EntryCount { get { return _entries.Count; } }

        /// <summary>重置列表内容，默认全部勾选；无内容时隐藏自身。</summary>
        public void SetEntries(IEnumerable<ShareEntry> entries)
        {
            _entries.Clear();
            _checked.Clear();
            _offsets.Clear();
            _targets.Clear();
            foreach (ShareEntry entry in entries)
            {
                _entries.Add(entry);
                _checked.Add(true);
                _offsets.Add(0);
                _targets.Add(0);
            }
            Visible = _entries.Count > 0;
            Invalidate();
        }

        /// <summary>按列表顺序返回所有勾选项的 UNC。</summary>
        public List<string> GetSelectedUncs()
        {
            List<string> selected = new List<string>();
            for (int i = 0; i < _entries.Count; i++)
                if (_checked[i]) selected.Add(_entries[i].Unc);
            return selected;
        }

        // ----------------------------------------------------------------
        // 交互
        // ----------------------------------------------------------------
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            _downIndex = RowAt(e.Y);
            _downPoint = e.Location;
            if (_downIndex >= 0)
                _grabOffsetY = e.Y - RowTop(_downIndex);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_downIndex < 0)
            {
                int hover = RowAt(e.Y);
                if (hover != _hoverIndex) { _hoverIndex = hover; Invalidate(); }
                return;
            }

            if (!_dragging)
            {
                Size dragSize = SystemInformation.DragSize;
                if (Math.Abs(e.X - _downPoint.X) <= dragSize.Width &&
                    Math.Abs(e.Y - _downPoint.Y) <= dragSize.Height) return;

                // 进入拖动：被拖行脱离队列，其它行目标归零
                _dragging = true;
                Cursor = Cursors.SizeNS;
                _dragRowY = RowTop(_downIndex) + _offsets[_downIndex];
                _animTimer.Start();
            }

            _dragRowY = Math.Max(0, Math.Min(e.Y - _grabOffsetY, (_entries.Count - 1) * RowH - _scroll));
            UpdateRetreatTargets();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            Cursor = Cursors.Default;

            if (_downIndex >= 0 && !_dragging)
            {
                // 单击：切换勾选
                _checked[_downIndex] = !_checked[_downIndex];
                Invalidate();
            }
            else if (_dragging)
            {
                CommitDrop();
            }
            _downIndex = -1;
            _dragging = false;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hoverIndex = -1;
            if (!_dragging) Invalidate();
        }

        // ----------------------------------------------------------------
        // 拖动核心：插入位计算 / 退避目标 / 松手归位
        // ----------------------------------------------------------------
        private int RowAt(int y)
        {
            int i = (int)((y + _scroll) / RowH);
            return (i >= 0 && i < _entries.Count) ? i : -1;
        }

        private float RowTop(int i) { return i * RowH - _scroll; }

        private float MaxScroll { get { return Math.Max(0, _entries.Count * RowH - Height); } }

        /// <summary>内容超出可视高度时支持滚轮滚动（窗口加长最多容纳 10 行）。</summary>
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            float delta = -e.Delta / 120f * RowH * 2;
            float next = Math.Max(0, Math.Min(_scroll + delta, MaxScroll));
            if (next != _scroll) { _scroll = next; Invalidate(); }
        }

        /// <summary>被拖行移出后的紧凑序号（其它行的目标偏移依据）。</summary>
        private int CompactPos(int i)
        {
            return (i < _downIndex) ? i : i - 1;
        }

        /// <summary>当前插入位：被拖行中心越过多少行的中心（可视坐标系）。</summary>
        private int InsertIndex()
        {
            float center = _dragRowY + RowH / 2f;
            int k = 0;
            for (int i = 0; i < _entries.Count; i++)
            {
                if (i == _downIndex) continue;
                float rowCenter = CompactPos(i) * RowH - _scroll + RowH / 2f;
                if (rowCenter < center) k++;
            }
            return k;
        }

        /// <summary>退避：插入位之后的行向下让出一个行高（动画逼近目标）。</summary>
        private void UpdateRetreatTargets()
        {
            int k = InsertIndex();
            for (int i = 0; i < _entries.Count; i++)
            {
                if (i == _downIndex) { _targets[i] = 0; continue; }
                _targets[i] = (CompactPos(i) >= k) ? RowH : 0;
            }
            // 动画 Timer 收敛后会自动停止；目标更新时必须重启，否则拖动看起来"卡住"
            if (!_animTimer.Enabled) _animTimer.Start();
        }

        /// <summary>松手：按插入位提交新顺序，各行从当前视觉位置平滑归位。</summary>
        private void CommitDrop()
        {
            int k = InsertIndex();
            ShareEntry draggedEntry = _entries[_downIndex];
            bool draggedChecked = _checked[_downIndex];
            float draggedVisualY = _dragRowY;

            // 新顺序
            List<ShareEntry> newEntries = new List<ShareEntry>();
            List<bool> newChecked = new List<bool>();
            for (int i = 0; i < _entries.Count; i++)
                if (i != _downIndex) { newEntries.Add(_entries[i]); newChecked.Add(_checked[i]); }
            newEntries.Insert(k, draggedEntry);
            newChecked.Insert(k, draggedChecked);

            // 视觉连续：新偏移 = 旧视觉位置 - 新行位置
            List<float> newOffsets = new List<float>();
            for (int i = 0; i < newEntries.Count; i++)
            {
                int oldIndex = _entries.IndexOf(newEntries[i]);
                float oldVisual = (newEntries[i] == draggedEntry)
                    ? draggedVisualY
                    : CompactPos(oldIndex) * RowH + _offsets[oldIndex];
                newOffsets.Add(oldVisual - i * RowH);
            }

            _entries.Clear(); _entries.AddRange(newEntries);
            _checked.Clear(); _checked.AddRange(newChecked);
            _offsets.Clear(); _offsets.AddRange(newOffsets);
            _targets.Clear();
            for (int i = 0; i < _entries.Count; i++) _targets.Add(0);

            _animTimer.Start();   // 归位动画
            Invalidate();
        }

        private void AnimTimer_Tick(object sender, EventArgs e)
        {
            bool settled = true;
            for (int i = 0; i < _offsets.Count; i++)
            {
                if (Math.Abs(_offsets[i] - _targets[i]) < 0.6f)
                {
                    _offsets[i] = _targets[i];
                }
                else
                {
                    _offsets[i] += (_targets[i] - _offsets[i]) * 0.4f;   // 指数趋近 = ease-out
                    settled = false;
                }
            }
            if (settled) _animTimer.Stop();
            Invalidate();
        }

        // ----------------------------------------------------------------
        // 绘制
        // ----------------------------------------------------------------
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush bg = new SolidBrush(Theme.LogBg))
                g.FillRectangle(bg, ClientRectangle);

            // 盘符按勾选顺序预分配（Z: 起，仅勾选项）
            char letter = 'Z';
            char[] letters = new char[_entries.Count];
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_checked[i] && letter >= 'D') letters[i] = letter--;
            }

            for (int i = 0; i < _entries.Count; i++)
            {
                if (_dragging && i == _downIndex) continue;   // 被拖行最后绘制
                DrawRow(g, i, RowTop(i) + _offsets[i], letters[i], false);
            }
            if (_dragging && _downIndex >= 0)
                DrawRow(g, _downIndex, _dragRowY, letters[_downIndex], true);
        }

        private void DrawRow(Graphics g, int i, float y, char driveLetter, bool floating)
        {
            RectangleF row = new RectangleF(4, y + 2, Width - 8, RowH - 4);

            if (floating)
            {
                // 悬浮：投影 + 白底 + 左侧强调条
                RoundHelper.DrawShadow(g, Rectangle.Round(row), Theme.RadiusButton);
                using (GraphicsPath path = RoundHelper.Create(Rectangle.Round(row), Theme.RadiusButton))
                using (SolidBrush brush = new SolidBrush(Color.White))
                    g.FillPath(brush, path);
                using (SolidBrush bar = new SolidBrush(Theme.Accent))
                    g.FillRectangle(bar, row.X, row.Y + 6, 3, row.Height - 12);
            }
            else if (i == _hoverIndex)
            {
                using (GraphicsPath path = RoundHelper.Create(Rectangle.Round(row), Theme.RadiusButton))
                using (SolidBrush brush = new SolidBrush(Color.FromArgb(120, 238, 238, 240)))
                    g.FillPath(brush, path);
            }

            bool isChecked = _checked[i];
            Color fg = isChecked ? Theme.TextMain : Theme.TextSub;

            // 勾选框（圆角，勾选 = 强调蓝底白勾）
            RectangleF box = new RectangleF(row.X + 8, y + RowH / 2f - 8, 16, 16);
            using (GraphicsPath boxPath = RoundHelper.Create(Rectangle.Round(box), 4))
            {
                if (isChecked)
                {
                    using (SolidBrush brush = new SolidBrush(Theme.Accent))
                        g.FillPath(brush, boxPath);
                    using (Pen pen = new Pen(Color.White, 1.6f))
                    {
                        pen.StartCap = LineCap.Round;
                        pen.EndCap = LineCap.Round;
                        g.DrawLine(pen, box.X + 3.5f, box.Y + 8.5f, box.X + 7f, box.Y + 12f);
                        g.DrawLine(pen, box.X + 7f, box.Y + 12f, box.X + 13f, box.Y + 4.5f);
                    }
                }
                else
                {
                    using (Pen pen = new Pen(Color.FromArgb(120, 0, 0, 0), 1.2f))
                        g.DrawPath(pen, boxPath);
                }
            }

            float textX = row.X + 32;

            // 盘符徽章（仅勾选项）
            if (isChecked && driveLetter != 0)
            {
                RectangleF badge = new RectangleF(textX, y + RowH / 2f - 9, 30, 18);
                using (GraphicsPath path = RoundHelper.Create(Rectangle.Round(badge), 9))
                using (SolidBrush brush = new SolidBrush(Color.FromArgb(26, Theme.Accent)))
                    g.FillPath(brush, path);
                TextRenderer.DrawText(g, driveLetter + ":", Theme.UiFontSmall,
                    Rectangle.Round(badge), Theme.Accent,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                textX += 38;
            }

            // 共享名 + 右侧来源标签（或 UNC）
            TextRenderer.DrawText(g, _entries[i].Name, Theme.UiFont,
                new Point((int)textX, (int)(y + RowH / 2f - 9)), fg);

            string right = _entries[i].Tag != null ? "[" + _entries[i].Tag + "]" : _entries[i].Unc;
            Size rightSize = TextRenderer.MeasureText(g, right, Theme.UiFontSmall);
            TextRenderer.DrawText(g, right, Theme.UiFontSmall,
                new Point((int)(row.Right - rightSize.Width - 8), (int)(y + RowH / 2f - 7)),
                Theme.TextSub);
        }
    }


    // ========================================================================
    // 密码展示控件：1 秒乱码解码动画（字符逐个随机滚动、从左到右依次定格，
    // 底部强调线同步扫过，极客简约风）
    // ========================================================================
    internal class ScrambleText : Control
    {
        private const string Pool = "ABCDEFGHJKMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz23456789!@#$%^&*+-<>/=";
        private const int DurationMs = 1000;

        private string _final = "";
        private string _shown = "";
        private int _startTick;
        private readonly Timer _timer;
        private readonly Random _rng = new Random();

        public event EventHandler Completed;

        public string FinalText { get { return _final; } }

        public ScrambleText()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint, true);
            BackColor = Theme.CardBg;
            Font = Theme.PasswordFont;
            _timer = new Timer();
            _timer.Interval = 30;
            _timer.Tick += Timer_Tick;
        }

        /// <summary>开始播放 1 秒解码动画。</summary>
        public void Play(string text)
        {
            _final = text;
            _shown = "";
            _startTick = Environment.TickCount;
            _timer.Start();
        }

        public void ClearText()
        {
            _timer.Stop();
            _final = "";
            _shown = "";
            Invalidate();
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            float t = (Environment.TickCount - _startTick) / (float)DurationMs;
            if (t >= 1f)
            {
                _shown = _final;
                _timer.Stop();
                if (Completed != null) Completed(this, EventArgs.Empty);
            }
            else
            {
                // 从左到右依次定格；未定格字符从字符池随机滚动
                char[] chars = new char[_final.Length];
                for (int i = 0; i < _final.Length; i++)
                {
                    float settleAt = 0.12f + i * (0.76f / Math.Max(_final.Length - 1, 1));
                    chars[i] = (t >= settleAt) ? _final[i] : Pool[_rng.Next(Pool.Length)];
                }
                _shown = new string(chars);
            }
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            using (SolidBrush bg = new SolidBrush(Theme.CardBg))
                g.FillRectangle(bg, ClientRectangle);

            if (_shown.Length == 0) return;

            Size size = TextRenderer.MeasureText(g, _shown, Font);
            TextRenderer.DrawText(g, _shown, Font, new Point(0, (Height - size.Height) / 2), Theme.Accent);

            // 底部强调线随动画进度扫过
            float t = Math.Min(1f, (Environment.TickCount - _startTick) / (float)DurationMs);
            int lineW = (int)(size.Width * Fx.EaseOutCubic(t));
            if (lineW > 0)
            {
                using (SolidBrush bar = new SolidBrush(Theme.Accent))
                using (GraphicsPath path = RoundHelper.Create(
                    new Rectangle(1, Height - 6, Math.Max(lineW, 4), 3), 2))
                    g.FillPath(bar, path);
            }
        }
    }

    // ========================================================================
    // 网络操作封装（net use / cmdkey / NetShareEnum / WMI）
    // ========================================================================
    internal static class NasOperations
    {
        /// <summary>运行外部命令并捕获输出，返回退出码。</summary>
        private static int Run(string fileName, string arguments, out string output)
        {
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = fileName;
            psi.Arguments = arguments;
            psi.UseShellExecute = false;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.CreateNoWindow = true;
            psi.StandardOutputEncoding = Encoding.Default;   // net/cmdkey 输出为系统 ANSI/OEM 码页
            psi.StandardErrorEncoding = Encoding.Default;

            using (Process p = Process.Start(psi))
            {
                string stdout = p.StandardOutput.ReadToEnd();
                string stderr = p.StandardError.ReadToEnd();
                p.WaitForExit();
                output = (stdout + " " + stderr).Trim();
                return p.ExitCode;
            }
        }

        /// <summary>保存凭据到 Windows 凭据管理器（先清旧凭据，避免冲突）。</summary>
        public static bool SaveCredential(string server, string userName, string password)
        {
            string tmp;
            Run("cmdkey", "/delete:" + server, out tmp);
            int code = Run("cmdkey", "/add:" + server + " /user:\"" + userName + "\" /pass:\"" + password + "\"", out tmp);
            return code == 0;
        }

        /// <summary>
        /// 用 netapi32 官方 API NetShareEnum 枚举服务器共享（net view 的内部实现同款，
        /// 但不走文本解析，天然支持含空格的共享名，也不受 net view 客户端故障影响）。
        /// success 表示枚举是否成功（区别于"成功但没有共享"）；
        /// errorText 返回失败时的可读错误，供诊断。
        /// </summary>
        public static List<string> GetShares(string server, out bool success, out string errorText)
        {
            List<string> shares = new List<string>();
            IntPtr buffer = IntPtr.Zero;
            int entries = 0, total = 0, resume = 0;

            int rc = NetShareEnum(server, 0, out buffer, -1, out entries, out total, ref resume);
            if (rc != 0)
            {
                success = false;
                errorText = DescribeShareEnumError(rc);
                return shares;
            }

            success = true;
            errorText = "";
            try
            {
                int size = Marshal.SizeOf(typeof(ShareInfo0));
                for (int i = 0; i < entries; i++)
                {
                    IntPtr p = new IntPtr(buffer.ToInt64() + (long)i * size);
                    ShareInfo0 info = (ShareInfo0)Marshal.PtrToStructure(p, typeof(ShareInfo0));
                    string name = info.Name;
                    // 过滤：空、IPC$、隐藏共享（$ 结尾）
                    if (!string.IsNullOrEmpty(name) && name != "IPC$" && !name.EndsWith("$"))
                        shares.Add(name);
                }
            }
            finally
            {
                if (buffer != IntPtr.Zero) NetApiBufferFree(buffer);
            }
            return shares;
        }

        // ---- netapi32 P/Invoke ----
        [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
        private static extern int NetShareEnum(string server, int level, out IntPtr buffer,
            int prefMaxLen, out int entriesRead, out int totalEntries, ref int resumeHandle);

        [DllImport("netapi32.dll")]
        private static extern int NetApiBufferFree(IntPtr buffer);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct ShareInfo0 { public string Name; }

        private static string DescribeShareEnumError(int rc)
        {
            switch (rc)
            {
                case 5:    return "拒绝访问（用户名或密码错误）";
                case 53:   return "找不到网络路径（服务器不可达）";
                case 1219: return "存在冲突的凭据会话";
                default:   return "错误码 " + rc;
            }
        }

        /// <summary>探测结果：连接状态、凭据保存状态、共享列表、错误详情。</summary>
        public class ProbeResult
        {
            public bool Connected;
            public bool CredentialSaved;
            public List<string> Shares = new List<string>();
            public string ErrorDetail = "";
        }

        /// <summary>
        /// 标准探测流程（各功能页共用），顺序与经过现场多次检验的 map-nas.ps1 一致：
        /// 先存凭据 -> 清理旧连接 -> NetShareEnum 验证并枚举共享。
        /// 注意：目标 NAS 是 Samba 设备，不暴露可用的 IPC$ 命名共享
        /// （net use \\IP\IPC$ 恒返回系统错误 67），绝不能用连接 IPC$ 的方式做探测。
        /// 失败时回滚刚保存的凭据，避免留下错误凭据。
        /// </summary>
        public static ProbeResult ProbeServer(string server, string userName, string password)
        {
            ProbeResult result = new ProbeResult();
            string tmp;

            result.CredentialSaved = SaveCredential(server, userName, password);
            if (!result.CredentialSaved)
            {
                result.ErrorDetail = "凭据保存失败，请检查当前用户权限";
                return result;
            }

            // 清理到该服务器的旧连接，避免多凭据冲突（1219 错误）
            Run("net", "use \"\\\\" + server + "\" /delete /y", out tmp);

            bool viewOk;
            string errorText;
            result.Shares = GetShares(server, out viewOk, out errorText);
            if (!viewOk)
            {
                // 凭据无效或服务器不可达：回滚刚保存的凭据
                Run("cmdkey", "/delete:" + server, out tmp);
                result.CredentialSaved = false;
                result.ErrorDetail = errorText;
                return result;
            }

            result.Connected = true;
            return result;
        }

        /// <summary>当前用户已映射的网络驱动器（盘符如 "Z:" -> UNC）。</summary>
        public static Dictionary<string, string> GetMappedDrives()
        {
            Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                    "SELECT DeviceID, ProviderName FROM Win32_MappedLogicalDisk"))
                using (ManagementObjectCollection results = searcher.Get())
                {
                    foreach (ManagementObject obj in results)
                    {
                        string id = Convert.ToString(obj["DeviceID"]);
                        string unc = Convert.ToString(obj["ProviderName"]);
                        if (!string.IsNullOrEmpty(id)) map[id] = unc;
                    }
                }
            }
            catch { /* WMI 查询失败时按无映射处理 */ }
            return map;
        }

        /// <summary>从 Z: 向下分配第一个可用盘符（最低到 D:）；无可用返回 null。</summary>
        public static string NextFreeLetter(HashSet<string> used)
        {
            for (char c = 'Z'; c >= 'D'; c--)
            {
                string letter = c.ToString();
                if (!used.Contains(letter))
                {
                    used.Add(letter);
                    return letter;
                }
            }
            return null;
        }

        /// <summary>映射共享到指定盘符（依赖凭据管理器中已保存的凭据），持久化。</summary>
        public static bool MapShare(string letter, string unc, out string detail)
        {
            int code = Run("net", "use " + letter + ": \"" + unc + "\" /persistent:yes", out detail);
            return code == 0;
        }

        /// <summary>删除单个映射或连接（如 "Z:" 或 "\\服务器\IPC$"）。</summary>
        public static bool DeleteMapping(string deviceOrUnc, out string detail)
        {
            int code = Run("net", "use " + deviceOrUnc + " /delete /y", out detail);
            return code == 0;
        }
    }

    // ========================================================================
    // 主窗体
    // ========================================================================
    internal class ToolboxForm : Form
    {
        // 内置 NAS 目标（供 NAS懒人映射页探测；界面不展示域名说明文字）
        private class NasTarget
        {
            public string Domain;
            public string Ip;
            public string Tag;
        }

        private static readonly NasTarget[] NasTargets = new NasTarget[]
        {
            new NasTarget { Domain = "CORP1", Ip = "192.168.100.10",  Tag = "CORP1-NAS" },
            new NasTarget { Domain = "corp2",   Ip = "192.168.200.22", Tag = "CORP2-NAS"   },
        };

        private SegmentedTabBar _tabBar;
        private CardPanel[] _pages;
        private int _currentTab = -1;

        // ---- Tab1：NAS懒人映射 ----
        private InputBox _txtEmpId;
        private RoundedButton _btnProbe;
        private ShareListView _lstShares;
        private RoundedButton _btnMap;
        private RoundedButton _btnClearNas;
        private Label _lblLogNas;
        private TextBox _log1;
        private ProgressRing _ring1;
        private ToggleSwitch _tglNas;
        private List<ShareEntry> _rawNas;       // 探测到的原始共享（忽略 homes 过滤前）
        private int _nasDelta;                  // 窗口相对默认高度的增量

        // ---- Tab2：初始密码查询 ----
        private InputBox _txtEmpId2;
        private RoundedButton _btnGen;
        private ScrambleText _scramble;
        private RoundedButton _btnCopy;
        private Label _lblStatus2;

        // ---- Tab3：自定义映射 ----
        private InputBox _txtServer;
        private InputBox _txtUser;
        private InputBox _txtPass;
        private RoundedButton _btnCustomProbe;
        private ShareListView _lstCustomShares;
        private RoundedButton _btnCustomMap;
        private RoundedButton _btnClearCustom;
        private Label _lblLogCustom;
        private TextBox _log3;
        private ProgressRing _ring3;
        private ToggleSwitch _tglCustom;
        private List<ShareEntry> _rawCustom;    // 探测到的原始共享（忽略 homes 过滤前）
        private int _customDelta;               // 窗口相对默认高度的增量

        public ToolboxForm()
        {
            Text = "新员工入职工具箱";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.None;          // 自绘沉浸式标题栏
            ClientSize = new Size(640, 780);
            AutoScaleMode = AutoScaleMode.None;   // 自定义像素级布局，不做 DPI 自动缩放
            BackColor = Theme.WindowBg;
            Font = Theme.UiFont;
            DoubleBuffered = true;

            BuildTabBar();
            BuildPages();
            _currentTab = 0;
            _pages[0].Visible = true;
        }

        // --------------------------------------------------------------------
        // 自绘标题栏（无边框窗口 + DWM 官方圆角/阴影/边框）
        // --------------------------------------------------------------------
        private const int TitleBarHeight = 40;
        private int _hoverTitleBtn;        // 0=无 1=最小化 2=关闭

        private Rectangle MinBtnRect   { get { return new Rectangle(Width - 100, 5, 44, 30); } }
        private Rectangle CloseBtnRect { get { return new Rectangle(Width - 52, 5, 44, 30); } }

        // ---- DWM 官方接口（Windows 11 原生窗口装饰）----
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        [DllImport("dwmapi.dll")]
        private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);

        [StructLayout(LayoutKind.Sequential)]
        private struct MARGINS
        {
            public int Left, Right, Top, Bottom;
        }

        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWCP_ROUND = 2;

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // Windows 11：由 DWM 绘制原生抗锯齿圆角（Win10 忽略该属性，回退直角）
            int round = DWMWCP_ROUND;
            DwmSetWindowAttribute(Handle, DWMWA_WINDOW_CORNER_PREFERENCE,
                ref round, Marshal.SizeOf(typeof(int)));
            // 扩展 1px 框架到客户区：获得 DWM 官方阴影与窗口边框
            MARGINS margins = new MARGINS { Left = 1, Right = 1, Top = 1, Bottom = 1 };
            DwmExtendFrameIntoClientArea(Handle, ref margins);
        }

        /// <summary>标题栏空白区域可拖动窗口（按钮区域除外）。</summary>
        protected override void WndProc(ref Message m)
        {
            const int WM_NCHITTEST = 0x84;
            const int HTCLIENT = 1;
            const int HTCAPTION = 2;

            base.WndProc(ref m);
            if (m.Msg == WM_NCHITTEST && (int)m.Result == HTCLIENT)
            {
                int lParam = m.LParam.ToInt32();
                Point p = PointToClient(new Point((short)(lParam & 0xFFFF), (short)(lParam >> 16)));
                if (p.Y < TitleBarHeight && !MinBtnRect.Contains(p) && !CloseBtnRect.Contains(p))
                    m.Result = (IntPtr)HTCAPTION;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int hit = 0;
            if (e.Y < TitleBarHeight)
            {
                if (CloseBtnRect.Contains(e.Location)) hit = 2;
                else if (MinBtnRect.Contains(e.Location)) hit = 1;
            }
            if (hit != _hoverTitleBtn) { _hoverTitleBtn = hit; Invalidate(new Rectangle(Width - 110, 0, 110, TitleBarHeight)); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hoverTitleBtn != 0) { _hoverTitleBtn = 0; Invalidate(new Rectangle(Width - 110, 0, 110, TitleBarHeight)); }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Y >= TitleBarHeight) return;
            if (CloseBtnRect.Contains(e.Location)) Close();
            else if (MinBtnRect.Contains(e.Location)) WindowState = FormWindowState.Minimized;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // 标题：强调色小方块 + 软件名
            using (SolidBrush accent = new SolidBrush(Theme.Accent))
            using (GraphicsPath dot = RoundHelper.Create(new Rectangle(Theme.SpaceM, 16, 8, 8), 3))
                g.FillPath(accent, dot);
            TextRenderer.DrawText(g, this.Text, Theme.UiFont,
                new Rectangle(30, 0, 200, TitleBarHeight), Theme.TextMain,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

            // 最小化按钮
            using (GraphicsPath path = RoundHelper.Create(MinBtnRect, Theme.RadiusButton))
            using (SolidBrush brush = new SolidBrush(_hoverTitleBtn == 1 ? Theme.HoverGray : Color.Transparent))
                g.FillPath(brush, path);
            using (Pen pen = new Pen(Theme.TextSub, 1.4f))
                g.DrawLine(pen, MinBtnRect.X + 15, MinBtnRect.Y + 16, MinBtnRect.X + 29, MinBtnRect.Y + 16);

            // 关闭按钮（悬停红色，Win11 风格）
            using (GraphicsPath path = RoundHelper.Create(CloseBtnRect, Theme.RadiusButton))
            using (SolidBrush brush = new SolidBrush(_hoverTitleBtn == 2 ? Color.FromArgb(232, 17, 35) : Color.Transparent))
                g.FillPath(brush, path);
            Color xColor = _hoverTitleBtn == 2 ? Color.White : Theme.TextSub;
            using (Pen pen = new Pen(xColor, 1.4f))
            {
                int cx = CloseBtnRect.X + 15, cy = CloseBtnRect.Y + 8;
                g.DrawLine(pen, cx, cy, cx + 14, cy + 14);
                g.DrawLine(pen, cx + 14, cy, cx, cy + 14);
            }

            // 标题栏底部分隔线（极浅）
            using (Pen pen = new Pen(Theme.BorderSoft, 1f))
                g.DrawLine(pen, 0, TitleBarHeight, Width, TitleBarHeight);
        }

        // --------------------------------------------------------------------
        // 顶部分段式 Tab 导航
        // --------------------------------------------------------------------
        private void BuildTabBar()
        {
            _tabBar = new SegmentedTabBar(
                new string[] { "NAS懒人映射", "初始密码查询", "自定义映射" },
                new int[] { TabIcons.Share, TabIcons.Key, TabIcons.Globe });
            _tabBar.Location = new Point((ClientSize.Width - _tabBar.Width) / 2, TitleBarHeight + 12);
            _tabBar.SelectedIndexChanged += delegate { SwitchTab(_tabBar.SelectedIndex); };
            this.Controls.Add(_tabBar);
        }

        private void SwitchTab(int index)
        {
            if (index == _currentTab) return;
            _pages[_currentTab].Visible = false;
            _currentTab = index;
            _pages[index].Visible = true;
            SlideIn(_pages[index]);
        }

        /// <summary>页面切换时的滑入微交互（ease-out，约 140ms）。</summary>
        private void SlideIn(Control page)
        {
            int target = page.Left;
            int offset = 14;
            page.Left = target + offset;

            Timer timer = new Timer();
            timer.Interval = 15;
            int start = Environment.TickCount;
            timer.Tick += delegate
            {
                float t = (Environment.TickCount - start) / 140f;
                if (t >= 1f) { page.Left = target; timer.Stop(); timer.Dispose(); return; }
                page.Left = target + (int)(offset * (1f - Fx.EaseOutCubic(t)));
            };
            timer.Start();
        }

        // --------------------------------------------------------------------
        // 三个功能页（圆角卡片直接置于窗体，卡片自绘阴影）
        // --------------------------------------------------------------------
        private void BuildPages()
        {
            _pages = new CardPanel[3];
            int pageTop = TitleBarHeight + 64;
            for (int i = 0; i < 3; i++)
            {
                _pages[i] = new CardPanel();
                _pages[i].Bounds = new Rectangle(Theme.SpaceM, pageTop,
                    ClientSize.Width - Theme.SpaceM * 2, ClientSize.Height - pageTop - Theme.SpaceM);
                _pages[i].Visible = false;   // 切换 Tab 时整体显隐
                this.Controls.Add(_pages[i]);
            }
            BuildPageNas(_pages[0]);
            BuildPagePassword(_pages[1]);
            BuildPageCustom(_pages[2]);

            this.AcceptButton = _btnGen;   // 密码页回车 = 生成
        }

        // --------------------------------------------------------------------
        // 控件构造与日志辅助
        // --------------------------------------------------------------------
        private Label MakeLabel(Control parent, string text, int x, int y, bool sub)
        {
            Label lbl = new Label();
            lbl.Text = text;
            lbl.AutoSize = true;
            lbl.Location = new Point(x, y);
            lbl.Font = sub ? Theme.UiFontSmall : Theme.UiFont;
            lbl.ForeColor = sub ? Theme.TextSub : Theme.TextMain;
            lbl.BackColor = Theme.CardBg;
            parent.Controls.Add(lbl);
            return lbl;
        }

        /// <summary>页标题 + 副标题（各功能页统一节奏）。</summary>
        private void MakePageHeader(CardPanel page, string title, string subtitle)
        {
            Label t = new Label();
            t.Text = title;
            t.AutoSize = true;
            t.Font = Theme.TitleFont;
            t.ForeColor = Theme.TextMain;
            t.BackColor = Theme.CardBg;
            t.Location = new Point(Theme.PageMargin, 20);
            page.Controls.Add(t);

            Label s = new Label();
            s.Text = subtitle;
            s.AutoSize = true;
            s.Font = Theme.UiFontSmall;
            s.ForeColor = Theme.TextSub;
            s.BackColor = Theme.CardBg;
            s.Location = new Point(Theme.PageMargin, 46);
            page.Controls.Add(s);
        }

        private InputBox MakeInput(Control parent, int x, int y, int width, bool password)
        {
            InputBox box = new InputBox(password);
            box.Bounds = new Rectangle(x, y, width, Theme.InputHeight);
            parent.Controls.Add(box);
            return box;
        }

        private TextBox MakeLog(Control parent, int x, int y, int width, int height)
        {
            // 直角原生控件包进圆角边框容器，获得平滑圆角
            RoundedFrame frame = new RoundedFrame();
            frame.Bounds = new Rectangle(x, y, width, height);

            TextBox log = new TextBox();
            log.Multiline = true;
            log.ReadOnly = true;
            log.ScrollBars = ScrollBars.Vertical;
            log.BorderStyle = BorderStyle.None;
            log.BackColor = Theme.LogBg;
            log.Font = Theme.MonoFont;
            log.ForeColor = Theme.TextSub;
            log.Dock = DockStyle.Fill;
            frame.Controls.Add(log);
            parent.Controls.Add(frame);
            return log;
        }

        /// <summary>创建包在圆角边框容器中的共享列表。</summary>
        private ShareListView MakeShareList(Control parent, int x, int y, int width, int height)
        {
            RoundedFrame frame = new RoundedFrame();
            frame.Bounds = new Rectangle(x, y, width, height);
            frame.EmptyHint = "尚未探测到共享，请先点击上方按钮探测";

            ShareListView list = new ShareListView();
            list.Dock = DockStyle.Fill;
            frame.Controls.Add(list);
            parent.Controls.Add(frame);
            return list;
        }

        /// <summary>
        /// 表单行辅助：按标签实测宽度布局输入框（避免重叠），
        /// 标签垂直居中于输入框。返回输入框右缘，供多行对齐使用。
        /// </summary>
        private InputBox MakeFormRow(CardPanel page, string label, int y, int inputX, bool password)
        {
            Label lbl = MakeLabel(page, label, Theme.PageMargin, 0, false);
            InputBox box = MakeInput(page, inputX, y, page.Width - Theme.PageMargin - inputX, password);
            lbl.Location = new Point(Theme.PageMargin, y + (Theme.InputHeight - lbl.Height) / 2 + 1);
            return box;
        }

        /// <summary>主按钮（强调蓝实心）。</summary>
        private RoundedButton MakePrimaryButton(Control parent, string text, int x, int y, int width, EventHandler onClick)
        {
            RoundedButton btn = new RoundedButton();
            btn.Text = text;
            btn.Bounds = new Rectangle(x, y, width, Theme.ButtonHeight);
            btn.Click += onClick;
            parent.Controls.Add(btn);
            return btn;
        }

        /// <summary>"一键清除所有映射"按钮（右侧对齐的红色描边次级按钮，Tag 记录所属日志框）。</summary>
        private RoundedButton MakeClearButton(CardPanel page, int y, TextBox log)
        {
            RoundedButton btn = new RoundedButton();
            btn.Text = "一键清除所有映射";
            btn.Outline = true;
            btn.ForeColor = Theme.Danger;
            btn.OutlineColor = Theme.DangerBorder;
            btn.Bounds = new Rectangle(page.Width - Theme.PageMargin - 150, y, 150, Theme.ButtonHeight);
            btn.Tag = log;
            btn.Click += BtnClear_Click;
            page.Controls.Add(btn);
            return btn;
        }

        /// <summary>创建"忽略NAS预设共享"开关（右对齐于共享列表标题行，默认勾选）。</summary>
        private ToggleSwitch MakePresetToggle(CardPanel page, int y)
        {
            ToggleSwitch tgl = new ToggleSwitch();
            tgl.Bounds = new Rectangle(page.Width - Theme.PageMargin - 38, y, 38, 20);
            tgl.SetCheckedSilent(true);   // 默认勾选
            tgl.CheckedChanged += PresetToggle_Changed;

            Label lbl = MakeLabel(page, "忽略NAS预设共享", 0, 0, true);
            lbl.Location = new Point(tgl.Left - lbl.Width - 8, y + 2);
            lbl.Cursor = Cursors.Hand;
            lbl.Click += delegate { tgl.Checked = !tgl.Checked; };

            page.Controls.Add(tgl);
            return tgl;
        }

        // NAS 预设共享（Samba 默认共享，多数员工无需映射）
        private static readonly string[] PresetShares = { "homes", "docker", "music", "video" };

        /// <summary>按开关状态过滤 NAS 预设共享（两个页面的开关始终同步）。</summary>
        private List<ShareEntry> FilterPresetShares(List<ShareEntry> raw)
        {
            bool ignore = (_tglNas == null) || _tglNas.Checked;
            if (!ignore || raw == null) return raw;
            List<ShareEntry> filtered = new List<ShareEntry>();
            foreach (ShareEntry entry in raw)
            {
                bool isPreset = false;
                foreach (string preset in PresetShares)
                    if (entry.Name.Equals(preset, StringComparison.OrdinalIgnoreCase)) { isPreset = true; break; }
                if (!isPreset) filtered.Add(entry);
            }
            return filtered;
        }

        private void PresetToggle_Changed(object sender, EventArgs e)
        {
            bool value = ((ToggleSwitch)sender).Checked;
            if (_tglNas != null) _tglNas.SetCheckedSilent(value);
            if (_tglCustom != null) _tglCustom.SetCheckedSilent(value);
            // 已探测的结果即时重新过滤并重排窗口
            if (_rawNas != null && _lstShares != null)
            {
                _lstShares.SetEntries(FilterPresetShares(_rawNas));
                RelayoutNasPage(_lstShares.EntryCount);
            }
            if (_rawCustom != null && _lstCustomShares != null)
            {
                _lstCustomShares.SetEntries(FilterPresetShares(_rawCustom));
                RelayoutCustomPage(_lstCustomShares.EntryCount);
            }
        }

        /// <summary>窗口随共享数量自动加长（默认 4 行，最多加长到容纳 10 行，超出走列表内滚动）。</summary>
        private void RelayoutNasPage(int rows)
        {
            int listH = Math.Max(150, Math.Min(rows, 10) * 34 + 8);
            int diff = (listH - 150) - _nasDelta;
            if (diff == 0) return;
            _nasDelta += diff;

            _lstShares.Parent.Height = listH;
            _btnMap.Top += diff;
            _btnClearNas.Top += diff;
            _lblLogNas.Top += diff;
            _ring1.Top += diff;
            _log1.Parent.Top += diff;
            _pages[0].Height += diff;
            this.ClientSize = new Size(ClientSize.Width, ClientSize.Height + diff);
        }

        /// <summary>窗口随共享数量自动加长（自定义映射页，默认列表较矮）。</summary>
        private void RelayoutCustomPage(int rows)
        {
            int listH = Math.Max(120, Math.Min(rows, 10) * 34 + 8);
            int diff = (listH - 120) - _customDelta;
            if (diff == 0) return;
            _customDelta += diff;

            _lstCustomShares.Parent.Height = listH;
            _btnCustomMap.Top += diff;
            _btnClearCustom.Top += diff;
            _lblLogCustom.Top += diff;
            _ring3.Top += diff;
            _log3.Parent.Top += diff;
            _pages[2].Height += diff;
            this.ClientSize = new Size(ClientSize.Width, ClientSize.Height + diff);
        }

        private void Log(TextBox box, string line)
        {
            box.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + line + Environment.NewLine);
        }

        // ------------------------------------------------ Tab1：NAS懒人映射 --
        private void BuildPageNas(CardPanel page)
        {
            int contentW = page.Width - Theme.PageMargin * 2;
            MakePageHeader(page, "NAS懒人映射", "输入员工号，自动探测并映射可访问的网络共享");

            Label empLabel = MakeLabel(page, "员工号", Theme.PageMargin, 0, false);
            int empInputX = Theme.PageMargin + TextRenderer.MeasureText("员工号", Theme.UiFont).Width + 12;
            _txtEmpId = MakeInput(page, empInputX, 86, 210, false);
            // 标签垂直居中于输入框
            empLabel.Top = 86 + (Theme.InputHeight - empLabel.Height) / 2 + 1;
            _btnProbe = MakePrimaryButton(page, "探测可访问共享",
                Theme.PageMargin + contentW - 140, 85, 140, BtnProbe_Click);
            _btnProbe.Height = 30;

            MakeLabel(page, "可访问的共享（可拖动调整）", Theme.PageMargin, 138, true);
            _tglNas = MakePresetToggle(page, 134);
            _lstShares = MakeShareList(page, Theme.PageMargin, 160, contentW, 150);

            _btnMap = MakePrimaryButton(page, "映射选中共享", Theme.PageMargin, 330, 150, BtnMap_Click);
            _btnMap.Enabled = false;

            _lblLogNas = MakeLabel(page, "运行日志", Theme.PageMargin, 382, true);
            _ring1 = new ProgressRing();
            _ring1.Location = new Point(Theme.PageMargin + 62, 380);
            page.Controls.Add(_ring1);
            _log1 = MakeLog(page, Theme.PageMargin, 404, contentW, page.Height - 404 - Theme.PageMargin);

            _btnClearNas = MakeClearButton(page, 330, _log1);
        }

        // ------------------------------------------------ Tab2：初始密码查询 --
        private void BuildPagePassword(CardPanel page)
        {
            MakePageHeader(page, "初始密码查询", "计算新员工域账号的初始密码");

            // 输入行整体水平居中：标签 + 输入框 + 按钮
            int empLabelW = TextRenderer.MeasureText("员工号", Theme.UiFont).Width;
            int groupW = empLabelW + 12 + 210 + 12 + 108;
            int groupX = (page.Width - groupW) / 2;

            Label empLabel2 = MakeLabel(page, "员工号", groupX, 0, false);
            _txtEmpId2 = MakeInput(page, groupX + empLabelW + 12, 228, 210, false);
            empLabel2.Top = 228 + (Theme.InputHeight - empLabel2.Height) / 2 + 1;
            _btnGen = MakePrimaryButton(page, "生成密码", groupX + empLabelW + 12 + 210 + 12, 227, 108, BtnGen_Click);
            _btnGen.Height = 30;

            // 密码展示区：乱码解码动画控件
            int pwdLabelW = TextRenderer.MeasureText("初始密码", Theme.UiFont).Width;
            MakeLabel(page, "初始密码", groupX, 344, false);
            _scramble = new ScrambleText();
            _scramble.Bounds = new Rectangle(groupX + pwdLabelW + 12, 322, 340, 52);
            _scramble.Completed += delegate { _btnCopy.Enabled = true; };
            page.Controls.Add(_scramble);

            _btnCopy = MakePrimaryButton(page, "复制到剪贴板", (page.Width - 140) / 2, 420, 140, BtnCopy_Click);
            _btnCopy.Enabled = false;

            _lblStatus2 = new Label();
            _lblStatus2.Text = "";
            _lblStatus2.AutoSize = false;
            _lblStatus2.TextAlign = ContentAlignment.TopCenter;
            _lblStatus2.Bounds = new Rectangle(60, 472, page.Width - 120, 24);
            _lblStatus2.ForeColor = Theme.Danger;
            _lblStatus2.BackColor = Theme.CardBg;
            page.Controls.Add(_lblStatus2);
        }

        // ------------------------------------------------ Tab3：自定义映射 ---
        private void BuildPageCustom(CardPanel page)
        {
            int contentW = page.Width - Theme.PageMargin * 2;
            MakePageHeader(page, "自定义映射", "输入服务器与凭据，探测并映射任意共享");

            // 表单行：输入框按最长标签的实测宽度对齐，标签垂直居中于输入框
            int labelW = Math.Max(TextRenderer.MeasureText("服务器地址", Theme.UiFont).Width,
                         Math.Max(TextRenderer.MeasureText("用户名", Theme.UiFont).Width,
                                  TextRenderer.MeasureText("密码", Theme.UiFont).Width));
            int inputX = Theme.PageMargin + labelW + 12;

            _txtServer = MakeFormRow(page, "服务器地址", 82, inputX, false);
            MakeLabel(page, "格式：IP 或主机名", inputX, 116, true);

            _txtUser = MakeFormRow(page, "用户名", 138, inputX, false);
            MakeLabel(page, @"格式：域\账号 或 服务器账号", inputX, 172, true);

            _txtPass = MakeFormRow(page, "密码", 194, inputX, true);

            _btnCustomProbe = MakePrimaryButton(page, "探测共享", inputX, 234, 130, BtnCustomProbe_Click);
            _btnCustomProbe.Height = 30;

            MakeLabel(page, "可访问的共享（可拖动调整）", Theme.PageMargin, 290, true);
            _tglCustom = MakePresetToggle(page, 286);
            _lstCustomShares = MakeShareList(page, Theme.PageMargin, 312, contentW, 112);

            _btnCustomMap = MakePrimaryButton(page, "映射选中共享", Theme.PageMargin, 444, 150, BtnCustomMap_Click);
            _btnCustomMap.Enabled = false;

            _lblLogCustom = MakeLabel(page, "运行日志", Theme.PageMargin, 500, true);
            _ring3 = new ProgressRing();
            _ring3.Location = new Point(Theme.PageMargin + 62, 498);
            page.Controls.Add(_ring3);
            _log3 = MakeLog(page, Theme.PageMargin, 522, contentW, page.Height - 522 - Theme.PageMargin);

            _btnClearCustom = MakeClearButton(page, 444, _log3);
        }

        // ====================================================================
        // 共用后台流程
        // ====================================================================

        /// <summary>
        /// 映射流程（Tab1 / Tab3 共用）：按 uncList 顺序从 Z: 向下分配盘符
        /// （自动跳过已占用），逐个映射并写日志，完成后执行 onComplete（UI 线程）。
        /// </summary>
        private void MapSharesAsync(List<string> uncList, TextBox log, Action onComplete)
        {
            Task.Factory.StartNew(delegate
            {
                HashSet<string> used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string id in NasOperations.GetMappedDrives().Keys)
                    if (id.Length > 0) used.Add(id.Substring(0, 1).ToUpper());

                int ok = 0, fail = 0;
                foreach (string unc in uncList)
                {
                    string letter = NasOperations.NextFreeLetter(used);
                    if (letter == null)
                    {
                        fail++;
                        string u1 = unc;
                        this.BeginInvoke(new Action(delegate { Log(log, "无可用盘符，无法映射 " + u1); }));
                        continue;
                    }
                    string detail;
                    bool success = NasOperations.MapShare(letter, unc, out detail);
                    if (success) ok++; else fail++;
                    string msg = success
                        ? letter + ":  ->  " + unc + "   映射成功"
                        : unc + "   映射失败（" + detail + "）";
                    this.BeginInvoke(new Action(delegate { Log(log, msg); }));
                }
                int okF = ok, failF = fail;
                this.BeginInvoke(new Action(delegate
                {
                    Log(log, "完成：成功 " + okF + " 个，失败 " + failF + " 个");
                    if (onComplete != null) onComplete();
                }));
            });
        }

        /// <summary>一键清除所有映射（Tab1 / Tab3 的清除按钮共用，按钮 Tag 指向所属日志框）。</summary>
        private void BtnClear_Click(object sender, EventArgs e)
        {
            TextBox log = (TextBox)((Control)sender).Tag;
            ProgressRing ring = (log == _log1) ? _ring1 : _ring3;
            DialogResult dr = MessageBox.Show(this,
                "将断开当前用户的所有网络驱动器映射，是否继续？",
                "一键清除所有映射", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (dr != DialogResult.Yes) return;

            Log(log, "开始清除所有网络驱动器映射...");
            ring.Running = true;
            Task.Factory.StartNew(delegate
            {
                Dictionary<string, string> mapped = NasOperations.GetMappedDrives();
                int ok = 0, fail = 0;
                foreach (KeyValuePair<string, string> kv in mapped)
                {
                    string detail;
                    bool success = NasOperations.DeleteMapping(kv.Key, out detail);
                    if (success) ok++; else fail++;
                    string msg = success
                        ? "已断开 " + kv.Key + "（" + kv.Value + "）"
                        : "断开失败 " + kv.Key + "（" + kv.Value + "）：" + detail;
                    this.BeginInvoke(new Action(delegate { Log(log, msg); }));
                }
                int total = mapped.Count, okF = ok, failF = fail;
                this.BeginInvoke(new Action(delegate
                {
                    if (total == 0) Log(log, "当前没有任何网络驱动器映射");
                    else Log(log, "清除完成：成功 " + okF + " 个，失败 " + failF + " 个");
                    ring.Running = false;
                }));
            });
        }

        // ====================================================================
        // Tab1 逻辑：员工号 -> 自动计算密码 -> 探测内置 NAS -> 勾选映射
        // ====================================================================
        private void BtnProbe_Click(object sender, EventArgs e)
        {
            string empId = _txtEmpId.Text.Trim();
            if (empId.Length == 0) { Log(_log1, "请输入员工号"); _txtEmpId.Inner.Focus(); return; }
            if (!PasswordGenerator.IsValidEmployeeId(empId)) { Log(_log1, "员工号格式不正确：应为字母前缀加数字"); _txtEmpId.Inner.Focus(); return; }

            _btnProbe.Enabled = false;
            _btnMap.Enabled = false;
            _ring1.Running = true;
            Log(_log1, "已计算初始密码（不显示），开始探测 NAS...");

            string password = PasswordGenerator.Generate(empId);
            List<ShareEntry> found = new List<ShareEntry>();
            List<string> skipped = new List<string>();

            Task.Factory.StartNew(delegate
            {
                foreach (NasTarget nas in NasTargets)
                {
                    string user = nas.Domain + "\\" + empId;
                    NasOperations.ProbeResult pr = NasOperations.ProbeServer(nas.Ip, user, password);

                    if (pr.Connected)
                    {
                        this.BeginInvoke(new Action(delegate
                        {
                            Log(_log1, nas.Tag + "（" + nas.Ip + "）连接成功" + (pr.CredentialSaved ? "，凭据已保存" : "，凭据保存失败"));
                        }));
                        if (pr.Shares.Count == 0)
                        {
                            skipped.Add(nas.Tag + "（无可用共享）");
                        }
                        else
                        {
                            foreach (string s in pr.Shares)
                            {
                                ShareEntry entry = new ShareEntry();
                                entry.Unc = "\\\\" + nas.Ip + "\\" + s;
                                entry.Name = s; entry.Tag = nas.Tag;
                                found.Add(entry);
                            }
                        }
                    }
                    else
                    {
                        skipped.Add(nas.Tag + "（无该域权限）");
                        this.BeginInvoke(new Action(delegate
                        {
                            Log(_log1, nas.Tag + "（" + nas.Ip + "）连接失败，跳过"
                                + (pr.ErrorDetail.Length > 0 ? "：" + pr.ErrorDetail : ""));
                        }));
                    }
                }
                password = null;   // 主动清除内存中的密码
                GC.Collect();

                this.BeginInvoke(new Action(delegate
                {
                    _rawNas = found;
                    _lstShares.SetEntries(FilterPresetShares(found));
                    RelayoutNasPage(_lstShares.EntryCount);
                    foreach (string s in skipped) Log(_log1, "跳过：" + s);
                    if (found.Count == 0)
                    {
                        Log(_log1, "该员工无任何 NAS 访问权限");
                    }
                    else
                    {
                        Log(_log1, "共发现 " + found.Count + " 个共享，请勾选后点击“映射选中共享”");
                        _btnMap.Enabled = true;
                    }
                    _btnProbe.Enabled = true;
                    _ring1.Running = false;
                }));
            });
        }

        private void BtnMap_Click(object sender, EventArgs e)
        {
            List<string> selected = _lstShares.GetSelectedUncs();
            if (selected.Count == 0) { Log(_log1, "请先勾选需要映射的共享"); return; }

            _btnMap.Enabled = false;
            _btnProbe.Enabled = false;
            _ring1.Running = true;
            Log(_log1, "开始映射（按列表顺序从 Z: 依次分配盘符）...");
            MapSharesAsync(selected, _log1, delegate
            {
                _btnMap.Enabled = true;
                _btnProbe.Enabled = true;
                _ring1.Running = false;
            });
        }

        // ====================================================================
        // Tab2 逻辑：员工号 -> 计算初始密码 -> 复制
        // ====================================================================
        private void BtnGen_Click(object sender, EventArgs e)
        {
            _lblStatus2.Text = "";
            _scramble.ClearText();
            _btnCopy.Enabled = false;

            string empId = _txtEmpId2.Text.Trim();
            if (empId.Length == 0) { _lblStatus2.Text = "请输入员工号"; _txtEmpId2.Inner.Focus(); return; }
            if (!PasswordGenerator.IsValidEmployeeId(empId)) { _lblStatus2.Text = "员工号格式不正确：应为字母前缀加数字"; _txtEmpId2.Inner.Focus(); return; }

            try
            {
                // 播放 1 秒乱码解码动画；动画完成后 ScrambleText.Completed
                // 事件会启用"复制到剪贴板"按钮
                _scramble.Play(PasswordGenerator.Generate(empId));
            }
            catch (Exception)
            {
                _lblStatus2.Text = "生成失败，请检查输入";
            }
        }

        private void BtnCopy_Click(object sender, EventArgs e)
        {
            if (_scramble.FinalText.Length == 0) return;
            try
            {
                Clipboard.SetText(_scramble.FinalText);
                _lblStatus2.ForeColor = Theme.Success;
                _lblStatus2.Text = "已复制到剪贴板";
            }
            catch (Exception)
            {
                _lblStatus2.ForeColor = Theme.Danger;
                _lblStatus2.Text = "复制失败，请手动选择密码复制";
            }
        }

        // ====================================================================
        // Tab3 逻辑：服务器地址 + 手动凭据 -> 探测共享 -> 勾选映射
        // ====================================================================
        private void BtnCustomProbe_Click(object sender, EventArgs e)
        {
            string server = _txtServer.Text.Trim();
            string user = _txtUser.Text.Trim();
            string pass = _txtPass.Text;

            if (!Regex.IsMatch(server, @"^[^\\\/\s]+$")) { Log(_log3, "服务器地址格式不正确：只需输入 IP 或主机名"); _txtServer.Inner.Focus(); return; }
            if (user.Length == 0) { Log(_log3, "请输入用户名"); _txtUser.Inner.Focus(); return; }
            if (pass.Length == 0) { Log(_log3, "请输入密码"); _txtPass.Inner.Focus(); return; }

            _btnCustomProbe.Enabled = false;
            _btnCustomMap.Enabled = false;
            _ring3.Running = true;
            Log(_log3, "正在连接 " + server + " ...");

            Task.Factory.StartNew(delegate
            {
                NasOperations.ProbeResult pr = NasOperations.ProbeServer(server, user, pass);
                pass = null;   // 主动清除内存中的密码

                this.BeginInvoke(new Action(delegate
                {
                    if (!pr.Connected)
                    {
                        Log(_log3, "连接失败：用户名或密码错误，或无法访问该服务器"
                            + (pr.ErrorDetail.Length > 0 ? "（" + pr.ErrorDetail + "）" : ""));
                    }
                    else
                    {
                        Log(_log3, "连接成功" + (pr.CredentialSaved ? "，凭据已保存" : "，凭据保存失败"));
                        if (pr.Shares.Count == 0)
                        {
                            Log(_log3, "该服务器上没有查询到可用共享");
                        }
                        else
                        {
                            List<ShareEntry> entries = new List<ShareEntry>();
                            foreach (string s in pr.Shares)
                            {
                                ShareEntry entry = new ShareEntry();
                                entry.Unc = "\\\\" + server + "\\" + s;
                                entry.Name = s; entry.Tag = null;
                                entries.Add(entry);
                            }
                            _rawCustom = entries;
                            _lstCustomShares.SetEntries(FilterPresetShares(entries));
                            RelayoutCustomPage(_lstCustomShares.EntryCount);
                            Log(_log3, "共发现 " + pr.Shares.Count + " 个共享，请勾选后点击“映射选中共享”");
                            _btnCustomMap.Enabled = true;
                        }
                    }
                    _btnCustomProbe.Enabled = true;
                    _ring3.Running = false;
                }));
            });
        }

        private void BtnCustomMap_Click(object sender, EventArgs e)
        {
            List<string> selected = _lstCustomShares.GetSelectedUncs();
            if (selected.Count == 0) { Log(_log3, "请先探测共享并勾选需要映射的项"); return; }

            _btnCustomMap.Enabled = false;
            _btnCustomProbe.Enabled = false;
            _ring3.Running = true;
            Log(_log3, "开始映射（按列表顺序从 Z: 依次分配盘符）...");
            MapSharesAsync(selected, _log3, delegate
            {
                _btnCustomMap.Enabled = true;
                _btnCustomProbe.Enabled = true;
                _ring3.Running = false;
            });
        }
    }

    internal static class Program
    {
        // 声明 DPI 感知：界面按物理像素精确渲染（默认会被系统位图拉伸而模糊）
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [STAThread]
        private static void Main()
        {
            SetProcessDPIAware();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new ToolboxForm());
        }
    }
}
