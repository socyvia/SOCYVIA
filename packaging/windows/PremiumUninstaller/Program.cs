using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("SOCYVIA Uninstall")]
[assembly: AssemblyDescription("SOCYVIA Desktop Uninstaller")]
[assembly: AssemblyCompany("SOCYVIA")]
[assembly: AssemblyProduct("SOCYVIA")]
[assembly: AssemblyCopyright("Copyright SOCYVIA")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
[assembly: AssemblyInformationalVersion("1.0.0")]

namespace Socyvia.Uninstall
{
    internal static class Program
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool MoveFileEx(string existingFileName, string newFileName, int flags);

        [STAThread]
        private static int Main(string[] args)
        {
            try { SetProcessDpiAwarenessContext(new IntPtr(-4)); }
            catch { }

            string engine = ReadArgument(args, "/ENGINE=");
            if (String.IsNullOrWhiteSpace(engine)) return RelaunchFromTemporaryLocation(args);
            if (ContainsSwitch(args, "/VERYSILENT")) return RunEngine(engine);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            int exitCode;
            using (UninstallForm form = new UninstallForm(engine))
            {
                Application.Run(form);
                exitCode = form.ExitCode;
            }
            ScheduleTemporaryCleanup();
            return exitCode;
        }

        private static int RelaunchFromTemporaryLocation(string[] args)
        {
            string source = Assembly.GetExecutingAssembly().Location;
            string appFolder = Path.GetDirectoryName(source);
            string engine = Path.Combine(appFolder, "unins000.exe");
            if (!File.Exists(engine)) return 2;

            string folder = Path.Combine(Path.GetTempPath(), "SOCYVIA-Uninstall-" + Guid.NewGuid().ToString("N"));
            string temporary = Path.Combine(folder, "SOCYVIA.Uninstall.exe");
            Directory.CreateDirectory(folder);
            File.Copy(source, temporary, false);

            string arguments = QuoteSwitch("/ENGINE=", engine);
            if (ContainsSwitch(args, "/VERYSILENT")) arguments += " /VERYSILENT";
            ProcessStartInfo start = new ProcessStartInfo(temporary, arguments);
            start.UseShellExecute = true;
            Process process = Process.Start(start);
            return process == null ? 2 : 0;
        }

        private static int RunEngine(string engine)
        {
            if (!File.Exists(engine)) return 2;
            try
            {
                ProcessStartInfo start = new ProcessStartInfo(engine,
                    "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART");
                start.UseShellExecute = false;
                start.CreateNoWindow = true;
                using (Process process = Process.Start(start))
                {
                    process.WaitForExit();
                    return process.ExitCode;
                }
            }
            catch { return 2; }
        }

        internal static int RunRemoval(string engine) { return RunEngine(engine); }

        private static void ScheduleTemporaryCleanup()
        {
            string executable = Assembly.GetExecutingAssembly().Location;
            string folder = Path.GetDirectoryName(executable);
            if (!Path.GetFileName(folder).StartsWith("SOCYVIA-Uninstall-", StringComparison.Ordinal)) return;
            const int moveFileDelayUntilReboot = 4;
            try { MoveFileEx(executable, null, moveFileDelayUntilReboot); }
            catch { }
            try { MoveFileEx(folder, null, moveFileDelayUntilReboot); }
            catch { }
        }

        private static string ReadArgument(string[] args, string prefix)
        {
            foreach (string argument in args)
                if (argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return argument.Substring(prefix.Length).Trim('"');
            return null;
        }

        private static bool ContainsSwitch(string[] args, string value)
        {
            foreach (string argument in args)
                if (String.Equals(argument, value, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static string QuoteSwitch(string name, string value)
        {
            return "\"" + name + value.Replace("\"", "\\\"") + "\"";
        }
    }

    internal static class Palette
    {
        internal static readonly Color Canvas = Color.FromArgb(247, 250, 255);
        internal static readonly Color Surface = Color.FromArgb(252, 253, 255);
        internal static readonly Color SurfaceBlue = Color.FromArgb(238, 244, 255);
        internal static readonly Color Border = Color.FromArgb(198, 213, 236);
        internal static readonly Color BorderStrong = Color.FromArgb(157, 183, 226);
        internal static readonly Color Primary = Color.FromArgb(37, 99, 235);
        internal static readonly Color PrimaryHover = Color.FromArgb(29, 78, 216);
        internal static readonly Color PrimaryPressed = Color.FromArgb(30, 64, 175);
        internal static readonly Color PrimarySoft = Color.FromArgb(226, 236, 255);
        internal static readonly Color Ink = Color.FromArgb(16, 38, 74);
        internal static readonly Color Secondary = Color.FromArgb(79, 101, 137);
        internal static readonly Color Muted = Color.FromArgb(121, 141, 174);
        internal static readonly Color Success = Color.FromArgb(22, 128, 88);
        internal static readonly Color SuccessSoft = Color.FromArgb(229, 247, 239);
        internal static readonly Color Error = Color.FromArgb(190, 55, 55);
    }

    internal static class Assets
    {
        private static readonly PrivateFontCollection LatinFonts = new PrivateFontCollection();
        private static readonly PrivateFontCollection ArabicFonts = new PrivateFontCollection();
        private static readonly List<IntPtr> FontMemory = new List<IntPtr>();
        private static FontFamily _latin;
        private static FontFamily _arabic;

        internal static void Initialize()
        {
            if (_latin != null || _arabic != null) return;
            AddFont(LatinFonts, "Socyvia.Font.Regular");
            AddFont(LatinFonts, "Socyvia.Font.SemiBold");
            AddFont(ArabicFonts, "Socyvia.Font.ArabicRegular");
            AddFont(ArabicFonts, "Socyvia.Font.ArabicSemiBold");
            if (LatinFonts.Families.Length > 0) _latin = LatinFonts.Families[0];
            if (ArabicFonts.Families.Length > 0) _arabic = ArabicFonts.Families[0];
        }

        private static void AddFont(PrivateFontCollection collection, string resource)
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))
            {
                if (stream == null) return;
                byte[] bytes = new byte[(int)stream.Length];
                int offset = 0;
                while (offset < bytes.Length)
                {
                    int read = stream.Read(bytes, offset, bytes.Length - offset);
                    if (read <= 0) break;
                    offset += read;
                }
                IntPtr memory = Marshal.AllocCoTaskMem(bytes.Length);
                Marshal.Copy(bytes, 0, memory, bytes.Length);
                collection.AddMemoryFont(memory, bytes.Length);
                FontMemory.Add(memory);
            }
        }

        internal static Font Font(float size, FontStyle style, bool arabic)
        {
            Initialize();
            FontFamily family = arabic ? _arabic : _latin;
            return family == null ? new Font("Segoe UI", size, style, GraphicsUnit.Point) :
                new Font(family, size, style, GraphicsUnit.Point);
        }

        internal static Image Logo()
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Socyvia.Logo.png"))
            {
                if (stream == null) return null;
                using (Image source = Image.FromStream(stream)) return new Bitmap(source);
            }
        }

        internal static Icon Icon()
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Socyvia.Icon.ico"))
            {
                return stream == null ? null : new Icon(stream);
            }
        }
    }

    internal sealed class UninstallForm : Form
    {
        private readonly string _engine;
        private readonly BrandPanel _brand;
        private readonly Panel _content;
        private readonly Panel _confirmation;
        private readonly Panel _removing;
        private readonly Panel _complete;
        private readonly Label _status;
        private readonly PulseBar _progress;
        private readonly Dictionary<Control, Rectangle> _englishBounds = new Dictionary<Control, Rectangle>();
        private bool _arabic;
        private bool _busy;
        internal int ExitCode { get; private set; }

        internal UninstallForm(string engine)
        {
            _engine = engine;
            Assets.Initialize();
            Text = "SOCYVIA Uninstall";
            Name = "SocyviaPremiumUninstallWindow";
            AccessibleName = "SOCYVIA Uninstall";
            Icon = Assets.Icon();
            BackColor = Palette.Canvas;
            ForeColor = Palette.Ink;
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(840, 560);
            MinimumSize = new Size(780, 540);
            MaximumSize = new Size(920, 660);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            KeyPreview = true;

            _brand = new BrandPanel();
            _brand.Dock = DockStyle.Left;
            _brand.Width = 274;
            Controls.Add(_brand);
            _brand.LanguageButton.Click += delegate { ApplyLanguage(!_arabic); };

            _content = new Panel();
            _content.Dock = DockStyle.Fill;
            _content.BackColor = Palette.Canvas;
            Controls.Add(_content);
            _content.BringToFront();

            _confirmation = BuildConfirmation();
            _removing = BuildRemoving(out _status, out _progress);
            _complete = BuildComplete();
            _content.Controls.Add(_complete);
            _content.Controls.Add(_removing);
            _content.Controls.Add(_confirmation);
            ShowPage(_confirmation);

            Shown += delegate
            {
                PreparePageLayouts();
                CaptureBounds(_content);
                ApplyLanguage(false);
            };
            FormClosing += OnClosing;
            KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape && !_busy) Close();
            };
        }

        private Panel BuildConfirmation()
        {
            Panel page = Page("ConfirmationPage");
            AddLabel(page, "UNINSTALL", "ConfirmEyebrow", 48, 46, 454, 20, 8F, FontStyle.Bold, Palette.Primary);
            AddLabel(page, "Remove SOCYVIA", "ConfirmTitle", 48, 80, 456, 52, 20F, FontStyle.Bold, Palette.Ink);
            AddLabel(page, "SOCYVIA will be removed from this device.", "ConfirmDescription", 48, 142, 456, 40, 9.5F, FontStyle.Regular, Palette.Secondary);

            SurfacePanel card = new SurfacePanel(Palette.SurfaceBlue, Palette.Border, 16);
            card.SetBounds(48, 218, 456, 142);
            AddLabel(card, "Your research data is preserved", "SafetyTitle", 20, 20, 416, 26, 10.5F, FontStyle.Bold, Palette.Ink);
            AddLabel(card, "Research data remains separate from application files and is not removed.",
                "SafetyDescription", 20, 54, 416, 68, 8.6F, FontStyle.Regular, Palette.Secondary);
            page.Controls.Add(card);

            BrandedButton cancel = new BrandedButton(ButtonKind.Secondary);
            ConfigureButton(cancel, "Cancel", "CancelButton", 48, 458, 150, 48);
            cancel.Click += delegate { Close(); };
            page.Controls.Add(cancel);

            BrandedButton uninstall = new BrandedButton(ButtonKind.Primary);
            ConfigureButton(uninstall, "Uninstall SOCYVIA", "UninstallButton", 260, 458, 244, 48);
            uninstall.Click += delegate { BeginRemoval(); };
            page.Controls.Add(uninstall);
            return page;
        }

        private Panel BuildRemoving(out Label status, out PulseBar progress)
        {
            Panel page = Page("RemovingPage");
            AddLabel(page, "REMOVING", "RemovingEyebrow", 48, 46, 454, 20, 8F, FontStyle.Bold, Palette.Primary);
            AddLabel(page, "Removing SOCYVIA", "RemovingTitle", 48, 96, 456, 54, 18F, FontStyle.Bold, Palette.Ink);
            AddLabel(page, "Please wait while SOCYVIA is removed from this device.", "RemovingDescription", 48, 150, 456, 40, 9F, FontStyle.Regular, Palette.Secondary);

            SurfacePanel card = new SurfacePanel(Palette.Surface, Palette.Border, 16);
            card.SetBounds(48, 218, 456, 142);
            AddLabel(card, "SOCYVIA 1.0.0", "RemovingProduct", 22, 20, 390, 24, 11F, FontStyle.Bold, Palette.Ink);
            status = AddLabel(card, "Removing application files...", "RemovingStatus", 22, 53, 390, 24, 10F, FontStyle.Regular, Palette.Secondary);
            progress = new PulseBar();
            progress.Name = "RemovalProgress";
            progress.AccessibleName = "Removal progress";
            progress.SetBounds(22, 94, 412, 8);
            card.Controls.Add(progress);
            page.Controls.Add(card);

            AddLabel(page, "Research data will remain available after removal.", "RemovingHint", 48, 388, 456, 28, 9.5F, FontStyle.Regular, Palette.Muted);
            return page;
        }

        private Panel BuildComplete()
        {
            Panel page = Page("CompletePage");
            AddLabel(page, "COMPLETE", "CompleteEyebrow", 48, 46, 454, 20, 8F, FontStyle.Bold, Palette.Primary);
            SuccessMark mark = new SuccessMark();
            mark.SetBounds(48, 96, 70, 70);
            page.Controls.Add(mark);
            AddLabel(page, "SOCYVIA removed\r\nsuccessfully", "CompleteTitle", 48, 180, 456, 90, 16.5F, FontStyle.Bold, Palette.Ink);
            AddLabel(page, "Application files were removed. Your research data remains preserved.",
                "CompleteDescription", 48, 278, 456, 52, 9F, FontStyle.Regular, Palette.Secondary);

            SurfacePanel card = new SurfacePanel(Palette.SuccessSoft, Color.FromArgb(166, 218, 195), 14);
            card.SetBounds(48, 350, 456, 70);
            AddLabel(card, "Research data preserved", "PreservedStatus", 18, 15, 420, 26, 9.5F, FontStyle.Bold, Palette.Success);
            page.Controls.Add(card);

            BrandedButton close = new BrandedButton(ButtonKind.Primary);
            ConfigureButton(close, "Close", "CloseButton", 278, 458, 226, 48);
            close.Click += delegate { Close(); };
            page.Controls.Add(close);
            return page;
        }

        private void BeginRemoval()
        {
            if (_busy) return;
            _busy = true;
            ShowPage(_removing);
            _progress.Start();
            ThreadPool.QueueUserWorkItem(delegate
            {
                Stopwatch visibleTime = Stopwatch.StartNew();
                int code = Program.RunRemoval(_engine);
                int remaining = 5500 - (int)visibleTime.ElapsedMilliseconds;
                if (remaining > 0) Thread.Sleep(remaining);
                BeginInvoke((MethodInvoker)delegate
                {
                    _progress.Stop();
                    _busy = false;
                    ExitCode = code;
                    if (code == 0)
                    {
                        ShowPage(_complete);
                    }
                    else
                    {
                        _status.Text = _arabic ? "تعذرت إزالة SOCYVIA." : "SOCYVIA could not be removed.";
                        _status.ForeColor = Palette.Error;
                        MessageBox.Show(this,
                            _arabic ? "تعذرت إزالة SOCYVIA. أغلق التطبيق ثم حاول مرة أخرى." :
                                "SOCYVIA could not be removed. Close the application and try again.",
                            Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                        ShowPage(_confirmation);
                    }
                });
            });
        }

        private void ApplyLanguage(bool arabic)
        {
            _arabic = arabic;
            Text = arabic ? "إزالة SOCYVIA" : "SOCYVIA Uninstall";
            AccessibleName = Text;
            _brand.ApplyLanguage(arabic);
            foreach (Control page in _content.Controls)
            {
                page.RightToLeft = arabic ? RightToLeft.Yes : RightToLeft.No;
                Localize(page, arabic);
            }
            PreparePageLayouts();
            SuspendLayout();
            MirrorBounds(_content, arabic);
            ResumeLayout(true);
            Invalidate(true);
        }

        private void Localize(Control parent, bool arabic)
        {
            foreach (Control control in parent.Controls)
            {
                string text = Localized(control.Name, arabic);
                if (text != null)
                {
                    control.Text = text;
                    control.AccessibleName = text.Replace("\r", " ").Replace("\n", " ")
                        .Replace("\u2066", String.Empty).Replace("\u2069", String.Empty);
                }
                if (!(control is PulseBar) && !(control is SuccessMark))
                {
                    Font old = control.Font;
                    float size = control.Name == "RemovingStatus"
                        ? (arabic ? 9F : 10F)
                        : old.SizeInPoints;
                    control.Font = Assets.Font(size, old.Style, arabic);
                }
                Label label = control as Label;
                if (label != null)
                {
                    bool technical = control.Name == "RemovingProduct";
                    label.RightToLeft = arabic && !technical ? RightToLeft.Yes : RightToLeft.No;
                    label.TextAlign = arabic && technical
                        ? ContentAlignment.TopRight
                        : ContentAlignment.TopLeft;
                }
                Localize(control, arabic);
            }
        }

        private static string Localized(string name, bool arabic)
        {
            switch (name)
            {
                case "ConfirmEyebrow": return arabic ? "إزالة" : "UNINSTALL";
                case "ConfirmTitle": return arabic ? "إزالة SOCYVIA" : "Remove SOCYVIA";
                case "ConfirmDescription": return arabic ? "سيتم إزالة SOCYVIA من هذا الجهاز." : "SOCYVIA will be removed from this device.";
                case "SafetyTitle": return arabic ? "بيانات البحث محفوظة" : "Your research data is preserved";
                case "SafetyDescription": return arabic ? "تبقى بيانات البحث منفصلة عن ملفات التطبيق ولن تتم إزالتها." : "Research data remains separate from application files and is not removed.";
                case "CancelButton": return arabic ? "إلغاء" : "Cancel";
                case "UninstallButton": return arabic ? "إزالة SOCYVIA" : "Uninstall SOCYVIA";
                case "RemovingEyebrow": return arabic ? "جار الإزالة" : "REMOVING";
                case "RemovingTitle": return arabic ? "جار إزالة SOCYVIA" : "Removing SOCYVIA";
                case "RemovingDescription": return arabic ? "يرجى الانتظار بينما تتم إزالة SOCYVIA من هذا الجهاز." : "Please wait while SOCYVIA is removed from this device.";
                case "RemovingProduct": return "SOCYVIA 1.0.0";
                case "RemovingStatus": return arabic ? "جار إزالة ملفات التطبيق..." : "Removing application files...";
                case "RemovingHint": return arabic ? "ستبقى بيانات البحث متاحة بعد الإزالة." : "Research data will remain available after removal.";
                case "CompleteEyebrow": return arabic ? "اكتملت الإزالة" : "COMPLETE";
                case "CompleteTitle": return arabic ? "تمت إزالة SOCYVIA بنجاح" : "SOCYVIA removed\r\nsuccessfully";
                case "CompleteDescription": return arabic ? "تمت إزالة ملفات التطبيق، وتبقى بيانات البحث محفوظة." : "Application files were removed. Your research data remains preserved.";
                case "PreservedStatus": return arabic ? "تم حفظ بيانات البحث" : "Research data preserved";
                case "CloseButton": return arabic ? "إغلاق" : "Close";
                default: return null;
            }
        }

        private void CaptureBounds(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                _englishBounds[child] = child.Bounds;
                CaptureBounds(child);
            }
        }

        private void MirrorBounds(Control parent, bool arabic)
        {
            int layoutWidth = parent.ClientSize.Width;
            if (layoutWidth <= 0 && parent.Dock == DockStyle.Fill && parent.Parent != null)
                layoutWidth = parent.Parent.ClientSize.Width;
            foreach (Control child in parent.Controls)
            {
                Rectangle english;
                if (_englishBounds.TryGetValue(child, out english) && child.Dock == DockStyle.None)
                    child.Bounds = arabic ? new Rectangle(layoutWidth - english.Right,
                        english.Y, english.Width, english.Height) : english;
                MirrorBounds(child, arabic);
            }
        }

        private void PreparePageLayouts()
        {
            foreach (Control page in _content.Controls)
                if (page.Dock == DockStyle.Fill) page.Bounds = _content.ClientRectangle;
        }

        private static Panel Page(string name)
        {
            Panel panel = new Panel();
            panel.Name = name;
            panel.Dock = DockStyle.Fill;
            panel.BackColor = Palette.Canvas;
            panel.Visible = false;
            return panel;
        }

        private static Label AddLabel(Control parent, string text, string name, int x, int y, int width, int height,
            float size, FontStyle style, Color color)
        {
            Label label = new Label();
            label.Name = name;
            label.Text = text;
            label.AccessibleName = text.Replace("\r", " ").Replace("\n", " ");
            label.AutoSize = false;
            label.UseCompatibleTextRendering = false;
            label.Font = Assets.Font(size, style, false);
            label.ForeColor = color;
            label.BackColor = Color.Transparent;
            label.SetBounds(x, y, width, height);
            parent.Controls.Add(label);
            return label;
        }

        private static void ConfigureButton(BrandedButton button, string text, string name, int x, int y, int width, int height)
        {
            button.Name = name;
            button.Text = text;
            button.AccessibleName = text;
            button.SetBounds(x, y, width, height);
            button.TabStop = true;
        }

        private static void ShowPage(Control page)
        {
            foreach (Control control in page.Parent.Controls) control.Visible = false;
            page.Visible = true;
            page.BringToFront();
        }

        private void OnClosing(object sender, FormClosingEventArgs e)
        {
            if (_busy)
            {
                e.Cancel = true;
                System.Media.SystemSounds.Beep.Play();
            }
        }
    }

    internal sealed class BrandPanel : Panel
    {
        private readonly Label _desktop;
        private readonly Label _subtitle;
        private readonly Label _language;
        private readonly Label _version;
        internal BrandedButton LanguageButton { get; private set; }

        internal BrandPanel()
        {
            DoubleBuffered = true;
            BackColor = Palette.SurfaceBlue;
            PictureBox logo = new PictureBox();
            logo.Image = Assets.Logo();
            logo.SizeMode = PictureBoxSizeMode.Zoom;
            logo.SetBounds(96, 76, 82, 82);
            logo.AccessibleName = "SOCYVIA logo";
            Controls.Add(logo);
            Controls.Add(Label("SOCYVIA", "BrandProduct", 28, 178, 218, 42, 17F, FontStyle.Bold, Palette.Ink));
            _desktop = Label("SCIENTIFIC DESKTOP", "BrandDesktop", 28, 222, 218, 22, 7.5F, FontStyle.Bold, Palette.Primary);
            Controls.Add(_desktop);
            _subtitle = Label("Scientific Testing for\nComputational Social Science", "BrandSubtitle", 28, 272, 218, 74, 8.2F, FontStyle.Regular, Palette.Secondary);
            Controls.Add(_subtitle);
            Panel accent = new Panel();
            accent.BackColor = Palette.Primary;
            accent.SetBounds(108, 350, 58, 3);
            Controls.Add(accent);
            _language = Label("LANGUAGE", "LanguageCaption", 28, 388, 218, 20, 7.5F, FontStyle.Bold, Palette.Muted);
            Controls.Add(_language);
            LanguageButton = new BrandedButton(ButtonKind.Secondary);
            LanguageButton.Name = "UninstallerLanguageButton";
            LanguageButton.Text = "العربية";
            LanguageButton.AccessibleName = "Switch uninstaller language to Arabic";
            LanguageButton.Font = Assets.Font(9.5F, FontStyle.Bold, true);
            LanguageButton.SetBounds(45, 414, 184, 40);
            Controls.Add(LanguageButton);
            _version = Label("VERSION 1.0.0", "BrandVersion", 28, 482, 218, 22, 8F, FontStyle.Bold, Palette.Muted);
            Controls.Add(_version);
            Controls.Add(Label("socyvia.com", "BrandWebsite", 28, 510, 218, 22, 9F, FontStyle.Regular, Palette.Secondary));
        }

        internal void ApplyLanguage(bool arabic)
        {
            _desktop.Text = arabic ? "سطح المكتب العلمي" : "SCIENTIFIC DESKTOP";
            _subtitle.Text = arabic ? "الاختبار العلمي للعلوم\nالاجتماعية الحاسوبية" : "Scientific Testing for\nComputational Social Science";
            _language.Text = arabic ? "اللغة" : "LANGUAGE";
            _version.Text = arabic ? "الإصدار 1.0.0" : "VERSION 1.0.0";
            LanguageButton.Text = arabic ? "English" : "العربية";
            LanguageButton.AccessibleName = arabic ? "Switch uninstaller language to English" : "تغيير لغة برنامج الإزالة إلى العربية";
            LanguageButton.Font = Assets.Font(9.5F, FontStyle.Bold, !arabic);
            foreach (Label label in Controls.OfType<Label>())
            {
                bool latin = label.Name == "BrandProduct" || label.Name == "BrandWebsite";
                label.Font = Assets.Font(label.Font.SizeInPoints, label.Font.Style, arabic && !latin);
                label.RightToLeft = arabic && !latin ? RightToLeft.Yes : RightToLeft.No;
            }
            Invalidate(true);
        }

        private static Label Label(string text, string name, int x, int y, int width, int height, float size, FontStyle style, Color color)
        {
            Label label = new Label();
            label.Name = name;
            label.Text = text;
            label.AccessibleName = text.Replace("\n", " ");
            label.AutoSize = false;
            label.Font = Assets.Font(size, style, false);
            label.ForeColor = color;
            label.BackColor = Color.Transparent;
            label.TextAlign = ContentAlignment.MiddleCenter;
            label.SetBounds(x, y, width, height);
            return label;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen edge = new Pen(Palette.Border)) e.Graphics.DrawLine(edge, Width - 1, 0, Width - 1, Height);
            using (SolidBrush glow = new SolidBrush(Color.FromArgb(28, Palette.Primary))) e.Graphics.FillEllipse(glow, -120, 320, 360, 360);
        }
    }

    internal sealed class SurfacePanel : Panel
    {
        private readonly Color _fill;
        private readonly Color _border;
        private readonly int _radius;
        internal SurfacePanel(Color fill, Color border, int radius)
        {
            _fill = fill;
            _border = border;
            _radius = radius;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }
        internal Color FillColor { get { return _fill; } }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color parent = Parent == null || Parent.BackColor.A == 0 ? Palette.Canvas : Parent.BackColor;
            e.Graphics.Clear(parent);
            using (GraphicsPath path = RoundedPath(ClientRectangle, _radius))
            using (SolidBrush brush = new SolidBrush(_fill)) e.Graphics.FillPath(brush, path);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle bounds = new Rectangle(0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
            using (GraphicsPath path = RoundedPath(bounds, _radius))
            using (Pen pen = new Pen(_border)) e.Graphics.DrawPath(pen, path);
        }
        internal static GraphicsPath RoundedPath(Rectangle rectangle, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int diameter = Math.Max(2, radius * 2);
            Rectangle arc = new Rectangle(rectangle.X, rectangle.Y, diameter, diameter);
            path.AddArc(arc, 180, 90); arc.X = rectangle.Right - diameter;
            path.AddArc(arc, 270, 90); arc.Y = rectangle.Bottom - diameter;
            path.AddArc(arc, 0, 90); arc.X = rectangle.Left;
            path.AddArc(arc, 90, 90); path.CloseFigure();
            return path;
        }
    }

    internal enum ButtonKind { Primary, Secondary }

    internal sealed class BrandedButton : Button
    {
        private readonly ButtonKind _kind;
        private bool _hover;
        private bool _pressed;
        internal BrandedButton(ButtonKind kind)
        {
            _kind = kind;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            BackColor = Palette.Canvas;
            Font = Assets.Font(10F, FontStyle.Bold, false);
            Cursor = Cursors.Hand;
            AccessibleRole = AccessibleRole.PushButton;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { _pressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color parent = Parent is SurfacePanel ? ((SurfacePanel)Parent).FillColor : Palette.Canvas;
            e.Graphics.Clear(parent);
            Color fill = _kind == ButtonKind.Primary ?
                (_pressed ? Palette.PrimaryPressed : (_hover ? Palette.PrimaryHover : Palette.Primary)) :
                (_pressed ? Palette.PrimarySoft : (_hover ? Color.FromArgb(239, 245, 255) : Color.White));
            Color text = _kind == ButtonKind.Primary ? Color.White : Palette.Ink;
            Color border = _kind == ButtonKind.Primary ? fill : (_hover ? Palette.BorderStrong : Palette.Border);
            Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = SurfacePanel.RoundedPath(bounds, 10))
            using (SolidBrush brush = new SolidBrush(fill))
            using (Pen pen = new Pen(border))
            {
                e.Graphics.FillPath(brush, path);
                e.Graphics.DrawPath(pen, path);
            }
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if (Focused && ShowFocusCues)
            {
                Rectangle focus = Rectangle.Inflate(bounds, -4, -4);
                ControlPaint.DrawFocusRectangle(e.Graphics, focus, text, fill);
            }
        }
    }

    internal sealed class PulseBar : Control
    {
        private readonly System.Windows.Forms.Timer _timer = new System.Windows.Forms.Timer();
        private int _position;
        internal PulseBar()
        {
            DoubleBuffered = true;
            _timer.Interval = 28;
            _timer.Tick += delegate { _position = (_position + 7) % Math.Max(1, Width + 110); Invalidate(); };
        }
        internal void Start() { _position = 0; _timer.Start(); }
        internal void Stop() { _timer.Stop(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle track = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = SurfacePanel.RoundedPath(track, 4))
            using (SolidBrush brush = new SolidBrush(Palette.PrimarySoft)) e.Graphics.FillPath(brush, path);
            Region previous = e.Graphics.Clip;
            using (GraphicsPath clip = SurfacePanel.RoundedPath(track, 4))
            {
                e.Graphics.SetClip(clip);
                using (SolidBrush brush = new SolidBrush(Palette.Primary))
                    e.Graphics.FillRectangle(brush, new Rectangle(_position - 110, 0, 110, Height - 1));
            }
            e.Graphics.Clip = previous;
        }
    }

    internal sealed class SuccessMark : Control
    {
        internal SuccessMark() { AccessibleName = "Removal successful"; DoubleBuffered = true; }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle circle = new Rectangle(1, 1, Width - 3, Height - 3);
            using (SolidBrush brush = new SolidBrush(Palette.SuccessSoft)) e.Graphics.FillEllipse(brush, circle);
            using (Pen edge = new Pen(Color.FromArgb(164, 216, 193), 1.5F)) e.Graphics.DrawEllipse(edge, circle);
            using (Pen check = new Pen(Palette.Success, 4F))
            {
                check.StartCap = LineCap.Round; check.EndCap = LineCap.Round;
                e.Graphics.DrawLines(check, new[] { new Point(19, 36), new Point(30, 47), new Point(52, 23) });
            }
        }
    }
}
