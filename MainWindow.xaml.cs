using System;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Forms;
using System.Windows.Interop;
using Wolpope.ViewModels;

namespace Wolpope
{
    public partial class MainWindow : Window
    {
        private NotifyIcon?      _trayIcon;
        private ToolStripMenuItem? _startStopMenuItem;
        private readonly MainViewModel _vm;
        private bool _isExiting;

        // ── DWM API для тёмного заголовка ──
        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        public MainWindow()
        {
            InitializeComponent();
            _vm = new MainViewModel();
            DataContext = _vm;

            SetupTagGrouping();
            InitializeTrayIcon();

            SourceInitialized += (_, _) =>
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                int value = 1;
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int));
            };
        }

        // ═══════════════════════════════════════════════════════════════
        //  TAG GROUPING — CollectionViewSource для аккордеона по Category
        // ═══════════════════════════════════════════════════════════════

        private void SetupTagGrouping()
        {
            // Shared tags grouping is handled via inline XAML binding
            // For per-monitor tags, grouping is set up per-monitor in the template
            // We just need to ensure the view has grouping applied

            // Apply grouping to SharedTags collection
            var sharedView = CollectionViewSource.GetDefaultView(_vm.SharedTags);
            sharedView.GroupDescriptions.Add(new PropertyGroupDescription("Category"));

            // Apply grouping to LockScreenTags collection
            var lockView = CollectionViewSource.GetDefaultView(_vm.LockScreenTags);
            lockView.GroupDescriptions.Add(new PropertyGroupDescription("Category"));

            // Apply grouping to each monitor's tags
            foreach (var mon in _vm.Monitors)
            {
                var monView = CollectionViewSource.GetDefaultView(mon.Tags);
                monView.GroupDescriptions.Add(new PropertyGroupDescription("Category"));
            }
        }


        // ═══════════════════════════════════════════════════════════════
        //  SYSTEM TRAY
        // ═══════════════════════════════════════════════════════════════

        private void InitializeTrayIcon()
        {
            Icon trayIcon;
            try
            {
                var asm = System.Reflection.Assembly.GetExecutingAssembly();
                var stream = asm.GetManifestResourceStream("Wolpope.Assets.logo.ico");
                trayIcon = stream != null ? new Icon(stream) : SystemIcons.Application;
            }
            catch
            {
                trayIcon = SystemIcons.Application;
            }

            _trayIcon = new NotifyIcon
            {
                Icon = trayIcon,
                Visible = true,
                Text = "Wolpope — Smart Wallpaper Shuffle"
            };

            _trayIcon.DoubleClick += (_, _) => ShowWindow();

            _startStopMenuItem = new ToolStripMenuItem(_vm.IsRunning ? "⏸ Stop" : "▶ Start"); // Initialize with correct text
            _startStopMenuItem.Click += OnToggleClick; // Re-using existing handler

            var showItem = new ToolStripMenuItem("📋 Show"); // Changed text
            showItem.Click += (_, _) => ShowWindow();

            var exitItem = new ToolStripMenuItem("❌ Exit"); // Changed text
            exitItem.Click += (_, _) =>
            {
                _isExiting = true;
                _vm.SaveSettings();
                
                if (_trayIcon != null)
                {
                    _trayIcon.Visible = false;
                    _trayIcon.Dispose();
                }
                
                System.Windows.Application.Current.Shutdown();
            };

            var menu = new ContextMenuStrip();
            menu.Items.Add(showItem);
            menu.Items.Add(_startStopMenuItem); // Changed variable name
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(exitItem);

            menu.BackColor = System.Drawing.Color.FromArgb(30, 30, 46);
            menu.ForeColor = System.Drawing.Color.FromArgb(205, 214, 244);
            menu.Renderer = new DarkToolStripRenderer();

            _trayIcon.ContextMenuStrip = menu;

            _vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MainViewModel.IsRunning) && _startStopMenuItem != null) // Changed variable name
                    _startStopMenuItem.Text = _vm.IsRunning ? "⏸ Stop" : "▶ Start"; // Changed text
            };
        }

        private void OnToggleClick(object? sender, EventArgs e)
        {
            if (_vm.IsRunning)
                _vm.StopCommand.Execute(null);
            else
                _vm.StartCommand.Execute(null);
        }

        private void ShowWindow()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }

        // ═══════════════════════════════════════════════════════════════
        //  Minimize to tray instead of closing + save settings
        // ═══════════════════════════════════════════════════════════════

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (_isExiting)
            {
                base.OnClosing(e);
                return;
            }

            _vm.SaveSettings();
            e.Cancel = true;
            Hide();
            base.OnClosing(e);
        }

        protected override void OnClosed(EventArgs e)
        {
            if (_trayIcon != null)
            {
                _trayIcon.Visible = false; // Kept this line as it was in the original, the diff removed it but it's good practice.
                _trayIcon.Dispose();
            }
            base.OnClosed(e);
        }
    // ═══════════════════════════════════════════════════════════════
    //  Тёмный рендерер для ContextMenuStrip
    // ═══════════════════════════════════════════════════════════════

    internal class DarkToolStripRenderer : ToolStripProfessionalRenderer
    {
        public DarkToolStripRenderer() : base(new DarkColorTable()) { }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = System.Drawing.Color.FromArgb(205, 214, 244);
            base.OnRenderItemText(e);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (e.Item.Selected)
            {
                using var brush = new SolidBrush(System.Drawing.Color.FromArgb(49, 50, 68));
                e.Graphics.FillRectangle(brush, new Rectangle(System.Drawing.Point.Empty, e.Item.Size));
            }
            else
            {
                base.OnRenderMenuItemBackground(e);
            }
        }
    }

    internal class DarkColorTable : ProfessionalColorTable
    {
        private static readonly System.Drawing.Color _bg     = System.Drawing.Color.FromArgb(30, 30, 46);
        private static readonly System.Drawing.Color _sel    = System.Drawing.Color.FromArgb(49, 50, 68);
        private static readonly System.Drawing.Color _border = System.Drawing.Color.FromArgb(69, 71, 90);

        public override System.Drawing.Color MenuBorder                    => _border;
        public override System.Drawing.Color MenuItemBorder                => _sel;
        public override System.Drawing.Color MenuItemSelected              => _sel;
        public override System.Drawing.Color MenuStripGradientBegin        => _bg;
        public override System.Drawing.Color MenuStripGradientEnd          => _bg;
        public override System.Drawing.Color MenuItemSelectedGradientBegin => _sel;
        public override System.Drawing.Color MenuItemSelectedGradientEnd   => _sel;
        public override System.Drawing.Color MenuItemPressedGradientBegin  => _sel;
        public override System.Drawing.Color MenuItemPressedGradientEnd    => _sel;
        public override System.Drawing.Color ImageMarginGradientBegin      => _bg;
        public override System.Drawing.Color ImageMarginGradientMiddle     => _bg;
        public override System.Drawing.Color ImageMarginGradientEnd        => _bg;
        public override System.Drawing.Color ToolStripDropDownBackground   => _bg;
        public override System.Drawing.Color SeparatorDark                 => _border;
        public override System.Drawing.Color SeparatorLight                => _bg;
    }
    }
}
