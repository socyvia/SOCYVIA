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

[assembly: AssemblyTitle("SOCYVIA Setup")]
[assembly: AssemblyDescription("SOCYVIA Desktop Installer")]
[assembly: AssemblyCompany("SOCYVIA")]
[assembly: AssemblyProduct("SOCYVIA")]
[assembly: AssemblyCopyright("Copyright SOCYVIA")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
[assembly: AssemblyInformationalVersion("1.0.0")]

namespace Socyvia.Setup
{
    internal static class Program
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

        [STAThread]
        private static int Main(string[] args)
        {
            try { SetProcessDpiAwarenessContext(new IntPtr(-4)); }
            catch { }

            if (InstallerEngine.IsSilent(args))
                return InstallerEngine.RunForwarded(args);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (InstallerForm form = new InstallerForm(args))
            {
                Application.Run(form);
                return form.ExitCode;
            }
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

    internal static class EmbeddedAssets
    {
        private static readonly PrivateFontCollection LatinFonts = new PrivateFontCollection();
        private static readonly PrivateFontCollection ArabicFonts = new PrivateFontCollection();
        private static readonly List<IntPtr> FontMemory = new List<IntPtr>();
        private static FontFamily _latinFamily;
        private static FontFamily _arabicFamily;

        internal static void InitializeFonts()
        {
            if (_latinFamily != null || _arabicFamily != null) return;
            AddFont(LatinFonts, "Socyvia.Font.Regular");
            AddFont(LatinFonts, "Socyvia.Font.SemiBold");
            AddFont(ArabicFonts, "Socyvia.Font.ArabicRegular");
            AddFont(ArabicFonts, "Socyvia.Font.ArabicSemiBold");
            if (LatinFonts.Families.Length > 0) _latinFamily = LatinFonts.Families[0];
            if (ArabicFonts.Families.Length > 0) _arabicFamily = ArabicFonts.Families[0];
        }

        private static void AddFont(PrivateFontCollection collection, string resourceName)
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
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

        internal static Font Font(float size, FontStyle style, bool arabic = false)
        {
            InitializeFonts();
            FontFamily family = arabic ? _arabicFamily : _latinFamily;
            return family == null
                ? new Font(arabic ? "Segoe UI" : "Segoe UI", size, style, GraphicsUnit.Point)
                : new Font(family, size, style, GraphicsUnit.Point);
        }

        internal static Image Logo()
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Socyvia.Logo.png"))
            {
                if (stream == null) return null;
                using (Image source = Image.FromStream(stream))
                    return new Bitmap(source);
            }
        }

        internal static Icon Icon()
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Socyvia.Icon.ico"))
            {
                if (stream == null) return null;
                return new Icon(stream);
            }
        }
    }

    internal sealed class InstallerForm : Form
    {
        private readonly Panel _contentHost;
        private readonly BrandPanel _brandPanel;
        private readonly Panel _welcomePage;
        private readonly Panel _optionsPage;
        private readonly Panel _installingPage;
        private readonly Panel _completePage;
        private readonly TextBox _installLocation;
        private readonly OptionToggle _startMenu;
        private readonly OptionToggle _desktop;
        private readonly Label _installingStatus;
        private readonly PulseBar _progress;
        private readonly Dictionary<Control, Rectangle> _englishBounds = new Dictionary<Control, Rectangle>();
        private bool _layoutCaptured;
        private bool _arabic;
        private bool _installing;
        private string _installedDirectory;

        internal int ExitCode { get; private set; }

        internal InstallerForm(string[] args)
        {
            EmbeddedAssets.InitializeFonts();
            Text = "SOCYVIA Setup";
            Name = "SocyviaPremiumSetupWindow";
            AccessibleName = "SOCYVIA Setup";
            AccessibleDescription = "Install SOCYVIA 1.0.0";
            Icon = EmbeddedAssets.Icon();
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
            MinimizeBox = true;
            KeyPreview = true;

            _brandPanel = new BrandPanel();
            _brandPanel.Dock = DockStyle.Left;
            _brandPanel.Width = 274;
            Controls.Add(_brandPanel);
            _brandPanel.LanguageButton.Click += delegate { ApplyLanguage(!_arabic); };

            _contentHost = new Panel();
            _contentHost.Dock = DockStyle.Fill;
            _contentHost.BackColor = Palette.Canvas;
            Controls.Add(_contentHost);
            _contentHost.BringToFront();

            _welcomePage = BuildWelcomePage();
            _optionsPage = BuildOptionsPage(out _installLocation, out _startMenu, out _desktop);
            _installingPage = BuildInstallingPage(out _installingStatus, out _progress);
            _completePage = BuildCompletePage();
            _contentHost.Controls.Add(_completePage);
            _contentHost.Controls.Add(_installingPage);
            _contentHost.Controls.Add(_optionsPage);
            _contentHost.Controls.Add(_welcomePage);

            string requestedDirectory = InstallerEngine.ReadArgument(args, "/DIR=");
            _installLocation.Text = String.IsNullOrWhiteSpace(requestedDirectory)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "SOCYVIA")
                : requestedDirectory;

            ShowPage(_welcomePage);
            Shown += delegate
            {
                PreparePageLayouts();
                CaptureEnglishBounds(_contentHost);
                _layoutCaptured = true;
                ApplyLanguage(false);
            };
            FormClosing += OnFormClosing;
            KeyDown += OnInstallerKeyDown;
        }

        private Panel BuildWelcomePage()
        {
            Panel page = Page();
            page.Name = "WelcomePage";
            AddEyebrow(page, "WELCOME", "WelcomeEyebrow", 48, 46);
            AddLabel(page, "Install SOCYVIA", 48, 76, 470, 56, 22F, FontStyle.Bold, Palette.Ink, "WelcomeTitleText");
            AddLabel(page, "A focused environment for designing, publishing, and examining controlled digital experiments.",
                48, 138, 454, 58, 9.5F, FontStyle.Regular, Palette.Secondary, "WelcomeDescriptionText");

            SurfacePanel card = new SurfacePanel(Palette.Surface, Palette.Border, 16);
            card.SetBounds(48, 210, 456, 160);
            AddLabel(card, "Ready for research work", 22, 18, 390, 28, 10.5F, FontStyle.Bold, Palette.Ink, "WelcomeCardTitle");
            AddBullet(card, "Self-contained desktop application", "WelcomeBulletOne", 22, 56);
            AddBullet(card, "Installs safely for your Windows account", "WelcomeBulletTwo", 22, 86);
            AddBullet(card, "Research data remains separate from application files", "WelcomeBulletThree", 22, 116);
            page.Controls.Add(card);

            BrandedButton options = new BrandedButton(ButtonKind.Secondary);
            ConfigureButton(options, "Installation options", "OptionsButton", 48, 458, 222, 48);
            options.Click += delegate { ShowPage(_optionsPage); };
            page.Controls.Add(options);

            BrandedButton install = new BrandedButton(ButtonKind.Primary);
            ConfigureButton(install, "Install SOCYVIA", "WelcomeInstallButton", 304, 458, 200, 48);
            install.Click += delegate { BeginInstall(); };
            page.Controls.Add(install);
            return page;
        }

        private Panel BuildOptionsPage(out TextBox location, out OptionToggle startMenu, out OptionToggle desktop)
        {
            Panel page = Page();
            page.Name = "OptionsPage";
            AddEyebrow(page, "OPTIONS", "OptionsEyebrow", 48, 46);
            AddLabel(page, "Installation options", 48, 76, 470, 52, 18F, FontStyle.Bold, Palette.Ink, "OptionsTitleText");
            AddLabel(page, "Choose the install location and shortcuts.", 48, 128, 454, 34, 8.5F, FontStyle.Regular, Palette.Secondary, "OptionsDescriptionText");

            AddLabel(page, "Install location", 48, 170, 200, 22, 10F, FontStyle.Bold, Palette.Ink, "InstallLocationLabel");
            SurfacePanel field = new SurfacePanel(Color.White, Palette.Border, 10);
            field.SetBounds(48, 198, 456, 48);
            location = new TextBox();
            location.Name = "InstallLocationBox";
            location.AccessibleName = "Install location";
            location.BorderStyle = BorderStyle.None;
            location.BackColor = Color.White;
            location.ForeColor = Palette.Ink;
            location.Font = EmbeddedAssets.Font(9.5F, FontStyle.Regular);
            location.SetBounds(14, 14, 326, 24);
            field.Controls.Add(location);
            TextBox locationBox = location;
            BrandedButton browse = new BrandedButton(ButtonKind.Ghost);
            ConfigureButton(browse, "Browse", "BrowseButton", 344, 7, 104, 34);
            browse.Click += delegate
            {
                using (FolderBrowserDialog dialog = new FolderBrowserDialog())
                {
                    dialog.Description = _arabic ? "اختر موقع تثبيت SOCYVIA" : "Choose where SOCYVIA will be installed";
                    dialog.SelectedPath = locationBox.Text;
                    if (dialog.ShowDialog(this) == DialogResult.OK) locationBox.Text = dialog.SelectedPath;
                }
            };
            field.Controls.Add(browse);
            page.Controls.Add(field);

            startMenu = new OptionToggle();
            startMenu.Name = "StartMenuCheckBox";
            startMenu.AccessibleName = "Create Start Menu shortcut";
            startMenu.Text = "Create a Start Menu shortcut";
            startMenu.Checked = true;
            startMenu.SetBounds(48, 270, 456, 42);
            page.Controls.Add(startMenu);

            desktop = new OptionToggle();
            desktop.Name = "DesktopCheckBox";
            desktop.AccessibleName = "Create Desktop shortcut";
            desktop.Text = "Create a Desktop shortcut";
            desktop.Checked = false;
            desktop.SetBounds(48, 318, 456, 42);
            page.Controls.Add(desktop);

            SurfacePanel note = new SurfacePanel(Palette.SurfaceBlue, Palette.Border, 12);
            note.SetBounds(48, 378, 456, 58);
            AddLabel(note, "Researcher data remains after uninstall.",
                16, 11, 422, 38, 8.5F, FontStyle.Regular, Palette.Secondary, "InstallSafetyNote");
            page.Controls.Add(note);

            BrandedButton back = new BrandedButton(ButtonKind.Secondary);
            ConfigureButton(back, "Back", "OptionsBackButton", 48, 458, 118, 48);
            back.Click += delegate { ShowPage(_welcomePage); };
            page.Controls.Add(back);

            BrandedButton install = new BrandedButton(ButtonKind.Primary);
            ConfigureButton(install, "Install", "OptionsInstallButton", 298, 458, 206, 48);
            install.Click += delegate { BeginInstall(); };
            page.Controls.Add(install);
            return page;
        }

        private Panel BuildInstallingPage(out Label status, out PulseBar progress)
        {
            Panel page = Page();
            page.Name = "InstallingPage";
            AddEyebrow(page, "INSTALLING", "InstallingEyebrow", 48, 46);
            AddLabel(page, "Installing SOCYVIA", 48, 96, 470, 54, 18F, FontStyle.Bold, Palette.Ink, "InstallingTitleText");
            AddLabel(page, "Preparing the scientific workspace on this device.", 48, 150, 454, 32, 9F, FontStyle.Regular, Palette.Secondary, "InstallingDescriptionText");

            SurfacePanel card = new SurfacePanel(Palette.Surface, Palette.Border, 16);
            card.SetBounds(48, 218, 456, 142);
            AddLabel(card, "SOCYVIA 1.0.0", 22, 20, 390, 24, 11F, FontStyle.Bold, Palette.Ink, "InstallingProductText");
            status = AddLabel(card, "Installing application files...", 22, 53, 412, 24, 10F, FontStyle.Regular, Palette.Secondary, "InstallingStatusText");
            progress = new PulseBar();
            progress.Name = "InstallationProgress";
            progress.AccessibleName = "Installation progress";
            progress.SetBounds(22, 94, 412, 8);
            card.Controls.Add(progress);
            page.Controls.Add(card);

            AddLabel(page, "You can continue when installation is complete.", 48, 388, 454, 26, 9.5F, FontStyle.Regular, Palette.Muted, "InstallingHintText");
            return page;
        }

        private Panel BuildCompletePage()
        {
            Panel page = Page();
            page.Name = "CompletePage";
            AddEyebrow(page, "COMPLETE", "CompleteEyebrow", 48, 46);
            SuccessMark mark = new SuccessMark();
            mark.SetBounds(48, 96, 70, 70);
            page.Controls.Add(mark);
            AddLabel(page, "SOCYVIA installed\r\nsuccessfully", 48, 180, 470, 90, 16.5F, FontStyle.Bold, Palette.Ink, "CompleteTitleText");
            AddLabel(page, "The research workspace is ready on this device.", 48, 278, 454, 32, 9F, FontStyle.Regular, Palette.Secondary, "CompleteDescriptionText");

            SurfacePanel card = new SurfacePanel(Palette.SuccessSoft, Color.FromArgb(166, 218, 195), 14);
            card.SetBounds(48, 326, 456, 82);
            AddLabel(card, "Installed", 18, 13, 160, 22, 9F, FontStyle.Bold, Palette.Success, "InstalledStatusText");
            AddLabel(card, "Version 1.0.0", 18, 40, 390, 24, 10.5F, FontStyle.Regular, Palette.Ink, "InstalledVersionText");
            page.Controls.Add(card);

            BrandedButton close = new BrandedButton(ButtonKind.Secondary);
            ConfigureButton(close, "Close", "CompleteCloseButton", 48, 458, 150, 48);
            close.Click += delegate { Close(); };
            page.Controls.Add(close);

            BrandedButton launch = new BrandedButton(ButtonKind.Primary);
            ConfigureButton(launch, "Launch SOCYVIA", "LaunchButton", 278, 458, 226, 48);
            launch.Click += delegate { LaunchInstalledApplication(); };
            page.Controls.Add(launch);
            return page;
        }

        private static Panel Page()
        {
            Panel panel = new Panel();
            panel.Dock = DockStyle.Fill;
            panel.BackColor = Palette.Canvas;
            panel.Visible = false;
            return panel;
        }

        private static void AddEyebrow(Control parent, string text, string name, int x, int y)
        {
            Label label = AddLabel(parent, text, x, y, 400, 20, 8F, FontStyle.Bold, Palette.Primary, name);
            label.AccessibleName = text;
        }

        private static Label AddLabel(Control parent, string text, int x, int y, int width, int height,
            float size, FontStyle style, Color color, string name)
        {
            Label label = new Label();
            label.Name = name;
            label.AccessibleName = text;
            label.Text = text;
            label.Font = EmbeddedAssets.Font(size, style);
            label.ForeColor = color;
            label.BackColor = Color.Transparent;
            label.AutoSize = false;
            label.UseCompatibleTextRendering = false;
            label.SetBounds(x, y, width, height);
            parent.Controls.Add(label);
            return label;
        }

        private static void AddBullet(Control parent, string text, string name, int x, int y)
        {
            BulletRow row = new BulletRow(text, name);
            row.SetBounds(x, y, 412, 22);
            parent.Controls.Add(row);
        }

        private static void ConfigureButton(BrandedButton button, string text, string name, int x, int y, int width, int height)
        {
            button.Name = name;
            button.AccessibleName = text;
            button.Text = text;
            button.SetBounds(x, y, width, height);
            button.TabStop = true;
        }

        private void CaptureEnglishBounds(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                _englishBounds[child] = child.Bounds;
                CaptureEnglishBounds(child);
            }
        }

        private void ApplyLanguage(bool arabic)
        {
            _arabic = arabic;
            Text = arabic ? "إعداد SOCYVIA" : "SOCYVIA Setup";
            AccessibleName = Text;
            AccessibleDescription = arabic ? "تثبيت SOCYVIA 1.0.0" : "Install SOCYVIA 1.0.0";
            _brandPanel.ApplyLanguage(arabic);

            foreach (Control page in _contentHost.Controls)
            {
                page.RightToLeft = arabic ? RightToLeft.Yes : RightToLeft.No;
                LocalizeTree(page, arabic);
            }
            _installLocation.RightToLeft = RightToLeft.No;
            _installLocation.TextAlign = HorizontalAlignment.Left;
            _progress.RightToLeft = arabic ? RightToLeft.Yes : RightToLeft.No;

            PreparePageLayouts();
            if (_layoutCaptured)
            {
                SuspendLayout();
                ApplyMirroredBounds(_contentHost, arabic);
                ResumeLayout(true);
            }
            ApplyDirectionalPrimitives(_contentHost, arabic);
            Invalidate(true);
        }

        private void LocalizeTree(Control parent, bool arabic)
        {
            foreach (Control control in parent.Controls)
            {
                string localized = LocalizedText(control.Name, arabic);
                if (localized != null)
                {
                    control.Text = localized;
                    control.AccessibleName = localized.Replace("\r", " ").Replace("\n", " ")
                        .Replace("\u2066", String.Empty).Replace("\u2069", String.Empty);
                }

                if (!(control is PictureBox) && !(control is PulseBar) && !(control is SuccessMark))
                {
                    Font previous = control.Font;
                    float size = control.Name == "InstallingStatusText"
                        ? (arabic ? 9F : 10F)
                        : previous.SizeInPoints;
                    control.Font = EmbeddedAssets.Font(size, previous.Style, arabic);
                }

                Label label = control as Label;
                if (label != null)
                {
                    bool technical = IsTechnicalText(control.Name);
                    label.RightToLeft = arabic && !technical ? RightToLeft.Yes : RightToLeft.No;
                    // WinForms maps TopLeft to the physical right edge when RightToLeft is Yes.
                    // Using TopRight here double-mirrors the glyph alignment and makes Arabic
                    // appear left-anchored inside an otherwise correctly mirrored control.
                    label.TextAlign = arabic && technical
                        ? ContentAlignment.TopRight
                        : ContentAlignment.TopLeft;
                }

                LocalizeTree(control, arabic);
            }
        }

        private void ApplyMirroredBounds(Control parent, bool arabic)
        {
            int layoutWidth = parent.ClientSize.Width;
            if (layoutWidth <= 0 && parent.Dock == DockStyle.Fill && parent.Parent != null)
                layoutWidth = parent.Parent.ClientSize.Width;
            foreach (Control child in parent.Controls)
            {
                Rectangle english;
                if (_englishBounds.TryGetValue(child, out english) && child.Dock == DockStyle.None)
                {
                    child.Bounds = arabic
                        ? new Rectangle(layoutWidth - english.Right, english.Y, english.Width, english.Height)
                        : english;
                }
                if (!(parent is BulletRow)) ApplyMirroredBounds(child, arabic);
            }
        }

        private void PreparePageLayouts()
        {
            foreach (Control page in _contentHost.Controls)
                if (page.Dock == DockStyle.Fill) page.Bounds = _contentHost.ClientRectangle;
        }

        private static void ApplyDirectionalPrimitives(Control parent, bool arabic)
        {
            foreach (Control child in parent.Controls)
            {
                BulletRow bullet = child as BulletRow;
                if (bullet != null) bullet.ApplyDirection(arabic);
                OptionToggle option = child as OptionToggle;
                if (option != null)
                {
                    option.RightToLeft = arabic ? RightToLeft.Yes : RightToLeft.No;
                    option.Invalidate();
                }
                ApplyDirectionalPrimitives(child, arabic);
            }
        }

        private static bool IsTechnicalText(string name)
        {
            return name == "InstallingProductText" || name == "InstalledVersionText";
        }

        private static string LocalizedText(string name, bool arabic)
        {
            switch (name)
            {
                case "WelcomeEyebrow": return arabic ? "مرحبا" : "WELCOME";
                case "WelcomeTitleText": return arabic ? "تثبيت SOCYVIA" : "Install SOCYVIA";
                case "WelcomeDescriptionText": return arabic ? "بيئة متكاملة لتصميم التجارب الرقمية المنضبطة ونشرها وفحصها." : "A focused environment for designing, publishing, and examining controlled digital experiments.";
                case "WelcomeCardTitle": return arabic ? "جاهز للعمل البحثي" : "Ready for research work";
                case "WelcomeBulletOne": return arabic ? "تطبيق مكتبي مستقل ومتكامل" : "Self-contained desktop application";
                case "WelcomeBulletTwo": return arabic ? "تثبيت آمن لحساب Windows الحالي" : "Installs safely for your Windows account";
                case "WelcomeBulletThree": return arabic ? "تبقى بيانات البحث منفصلة عن ملفات التطبيق" : "Research data remains separate from application files";
                case "OptionsButton": return arabic ? "خيارات التثبيت" : "Installation options";
                case "WelcomeInstallButton": return arabic ? "تثبيت SOCYVIA" : "Install SOCYVIA";
                case "OptionsEyebrow": return arabic ? "الخيارات" : "OPTIONS";
                case "OptionsTitleText": return arabic ? "خيارات التثبيت" : "Installation options";
                case "OptionsDescriptionText": return arabic ? "اختر موقع التثبيت والاختصارات." : "Choose the install location and shortcuts.";
                case "InstallLocationLabel": return arabic ? "موقع التثبيت" : "Install location";
                case "BrowseButton": return arabic ? "استعراض" : "Browse";
                case "StartMenuCheckBox": return arabic ? "إنشاء اختصار في قائمة ابدأ" : "Create a Start Menu shortcut";
                case "DesktopCheckBox": return arabic ? "إنشاء اختصار على سطح المكتب" : "Create a Desktop shortcut";
                case "InstallSafetyNote": return arabic ? "تبقى بيانات الباحث بعد إلغاء التثبيت." : "Researcher data remains after uninstall.";
                case "OptionsBackButton": return arabic ? "رجوع" : "Back";
                case "OptionsInstallButton": return arabic ? "تثبيت" : "Install";
                case "InstallingEyebrow": return arabic ? "جار التثبيت" : "INSTALLING";
                case "InstallingTitleText": return arabic ? "جار تثبيت SOCYVIA" : "Installing SOCYVIA";
                case "InstallingDescriptionText": return arabic ? "يرجى الانتظار بينما يتم تثبيت SOCYVIA." : "Preparing the scientific workspace on this device.";
                case "InstallingProductText": return "SOCYVIA 1.0.0";
                case "InstallingStatusText": return arabic ? "جار تثبيت ملفات التطبيق..." : "Installing application files...";
                case "InstallingHintText": return arabic ? "يمكنك المتابعة بعد اكتمال التثبيت." : "You can continue when installation is complete.";
                case "CompleteEyebrow": return arabic ? "اكتمل التثبيت" : "COMPLETE";
                case "CompleteTitleText": return arabic ? "تم تثبيت SOCYVIA بنجاح" : "SOCYVIA installed\r\nsuccessfully";
                case "CompleteDescriptionText": return arabic ? "مساحة البحث جاهزة على هذا الجهاز." : "The research workspace is ready on this device.";
                case "InstalledStatusText": return arabic ? "تم التثبيت" : "Installed";
                case "InstalledVersionText": return arabic ? "الإصدار 1.0.0" : "Version 1.0.0";
                case "CompleteCloseButton": return arabic ? "إغلاق" : "Close";
                case "LaunchButton": return arabic ? "تشغيل SOCYVIA" : "Launch SOCYVIA";
                default: return null;
            }
        }

        private void ShowPage(Panel page)
        {
            foreach (Control control in _contentHost.Controls) control.Visible = false;
            page.Visible = true;
            page.BringToFront();
            if (page == _welcomePage)
            {
                AcceptButton = FindButton(page, "WelcomeInstallButton");
                CancelButton = FindButton(page, "OptionsButton");
            }
            else if (page == _optionsPage)
            {
                AcceptButton = FindButton(page, "OptionsInstallButton");
                CancelButton = FindButton(page, "OptionsBackButton");
            }
            else if (page == _completePage)
            {
                AcceptButton = FindButton(page, "LaunchButton");
                CancelButton = FindButton(page, "CompleteCloseButton");
            }
            else
            {
                AcceptButton = null;
                CancelButton = null;
            }
        }

        private static IButtonControl FindButton(Control parent, string name)
        {
            Control[] controls = parent.Controls.Find(name, true);
            return controls.Length == 0 ? null : controls[0] as IButtonControl;
        }

        private void BeginInstall()
        {
            if (_installing) return;
            string directory;
            try
            {
                directory = Path.GetFullPath(Environment.ExpandEnvironmentVariables(_installLocation.Text.Trim()));
            }
            catch
            {
                MessageBox.Show(this,
                    _arabic ? "اختر موقع تثبيت صالحا." : "Choose a valid installation location.",
                    _arabic ? "إعداد SOCYVIA" : "SOCYVIA Setup",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                ShowPage(_optionsPage);
                _installLocation.Focus();
                return;
            }

            _installedDirectory = directory;
            _installing = true;
            ShowPage(_installingPage);
            _progress.Start();
            _installingStatus.Text = _arabic ? "جار تثبيت ملفات التطبيق..." : "Installing application files...";

            ThreadPool.QueueUserWorkItem(delegate
            {
                int code = InstallerEngine.RunInstallation(directory, _startMenu.Checked, _desktop.Checked);
                BeginInvoke((MethodInvoker)delegate
                {
                    _progress.Stop();
                    _installing = false;
                    ExitCode = code;
                    if (code == 0 || code == 1641 || code == 3010)
                    {
                        ExitCode = 0;
                        ShowPage(_completePage);
                    }
                    else
                    {
                        _installingStatus.Text = _arabic
                            ? "تعذر تثبيت SOCYVIA. لم تتغير أي بيانات بحثية."
                            : "SOCYVIA could not be installed. No research data was changed.";
                        _installingStatus.ForeColor = Palette.Error;
                        MessageBox.Show(this,
                            _arabic
                                ? "تعذر تثبيت SOCYVIA. أغلق التطبيقات الأخرى ثم حاول مرة أخرى."
                                : "SOCYVIA could not be installed. Please close other applications and try again.",
                            _arabic ? "إعداد SOCYVIA" : "SOCYVIA Setup",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                        ShowPage(_optionsPage);
                    }
                });
            });
        }

        private void LaunchInstalledApplication()
        {
            string executable = Path.Combine(_installedDirectory ?? String.Empty, "SOCYVIA.exe");
            if (!File.Exists(executable))
            {
                MessageBox.Show(this,
                    _arabic ? "تعذر العثور على SOCYVIA في موقع التثبيت." : "SOCYVIA could not be found in the installation location.",
                    _arabic ? "إعداد SOCYVIA" : "SOCYVIA Setup",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true });
            Close();
        }

        private void OnInstallerKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape && !_installing)
            {
                e.Handled = true;
                if (_optionsPage.Visible) ShowPage(_welcomePage);
                else Close();
            }
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (_installing)
            {
                e.Cancel = true;
                System.Media.SystemSounds.Beep.Play();
            }
        }
    }

    internal static class InstallerEngine
    {
        internal static bool IsSilent(string[] args)
        {
            foreach (string argument in args)
            {
                if (String.Equals(argument, "/VERYSILENT", StringComparison.OrdinalIgnoreCase) ||
                    String.Equals(argument, "/SILENT", StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        internal static string ReadArgument(string[] args, string prefix)
        {
            foreach (string argument in args)
            {
                if (argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return argument.Substring(prefix.Length).Trim('"');
            }
            return null;
        }

        internal static int RunForwarded(string[] args)
        {
            return RunEngine(BuildArgumentLine(args));
        }

        internal static int RunInstallation(string directory, bool startMenu, bool desktop)
        {
            List<string> tasks = new List<string>();
            if (startMenu) tasks.Add("startmenuicon");
            if (desktop) tasks.Add("desktopicon");
            string taskList = String.Join(",", tasks.ToArray());
            string arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /CURRENTUSER " +
                QuoteSwitch("/DIR=", directory) + " " + QuoteSwitch("/TASKS=", taskList) +
                " /USEPREVIOUSTASKS=no";
            return RunEngine(arguments);
        }

        private static string QuoteSwitch(string name, string value)
        {
            return "\"" + name + value.Replace("\"", "\\\"") + "\"";
        }

        private static string BuildArgumentLine(string[] args)
        {
            List<string> values = new List<string>();
            foreach (string argument in args)
            {
                if (argument.IndexOf(' ') >= 0 || argument.IndexOf('\t') >= 0)
                    values.Add("\"" + argument.Replace("\"", "\\\"") + "\"");
                else
                    values.Add(argument);
            }
            if (!ContainsSwitch(args, "/SP-")) values.Add("/SP-");
            return String.Join(" ", values.ToArray());
        }

        private static bool ContainsSwitch(string[] args, string value)
        {
            foreach (string argument in args)
                if (String.Equals(argument, value, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static int RunEngine(string arguments)
        {
            string folder = Path.Combine(Path.GetTempPath(), "SOCYVIA-Setup-" + Guid.NewGuid().ToString("N"));
            string engine = Path.Combine(folder, "SOCYVIA.InstallEngine.exe");
            try
            {
                Directory.CreateDirectory(folder);
                using (Stream input = Assembly.GetExecutingAssembly().GetManifestResourceStream("Socyvia.Engine.exe"))
                {
                    if (input == null) return 2;
                    using (FileStream output = new FileStream(engine, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        input.CopyTo(output);
                }
                ProcessStartInfo start = new ProcessStartInfo(engine, arguments);
                start.UseShellExecute = false;
                start.CreateNoWindow = true;
                using (Process process = Process.Start(start))
                {
                    process.WaitForExit();
                    return process.ExitCode;
                }
            }
            catch
            {
                return 2;
            }
            finally
            {
                try { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
                catch { }
            }
        }
    }

    internal sealed class BrandPanel : Panel
    {
        private readonly Label _desktop;
        private readonly Label _subtitle;
        private readonly Label _languageCaption;
        private readonly Label _version;
        internal BrandedButton LanguageButton { get; private set; }

        internal BrandPanel()
        {
            DoubleBuffered = true;
            BackColor = Palette.SurfaceBlue;
            PictureBox logo = new PictureBox();
            logo.Name = "BrandLogo";
            logo.Image = EmbeddedAssets.Logo();
            logo.SizeMode = PictureBoxSizeMode.Zoom;
            logo.SetBounds(96, 76, 82, 82);
            logo.AccessibleName = "SOCYVIA logo";
            Controls.Add(logo);

            Label product = Label("SOCYVIA", "BrandProduct", 28, 178, 218, 42, 17F, FontStyle.Bold, Palette.Ink);
            product.AccessibleName = "SOCYVIA";
            Controls.Add(product);
            _desktop = Label("SCIENTIFIC DESKTOP", "BrandDesktop", 28, 222, 218, 22, 7.5F, FontStyle.Bold, Palette.Primary);
            Controls.Add(_desktop);
            _subtitle = Label("Scientific Testing for\nComputational Social Science", "BrandSubtitle", 28, 272, 218, 74, 8.2F, FontStyle.Regular, Palette.Secondary);
            Controls.Add(_subtitle);

            Panel accent = new Panel();
            accent.Name = "BrandAccent";
            accent.BackColor = Palette.Primary;
            accent.SetBounds(108, 350, 58, 3);
            Controls.Add(accent);

            _languageCaption = Label("LANGUAGE", "LanguageCaptionText", 28, 388, 218, 20, 7.5F, FontStyle.Bold, Palette.Muted);
            Controls.Add(_languageCaption);
            LanguageButton = new BrandedButton(ButtonKind.Secondary);
            LanguageButton.Name = "InstallerLanguageButton";
            LanguageButton.AccessibleName = "Switch installer language to Arabic";
            LanguageButton.Text = "العربية";
            LanguageButton.Font = EmbeddedAssets.Font(9.5F, FontStyle.Bold, true);
            LanguageButton.SetBounds(45, 414, 184, 40);
            LanguageButton.TabStop = true;
            Controls.Add(LanguageButton);

            _version = Label("VERSION 1.0.0", "BrandVersion", 28, 482, 218, 22, 8F, FontStyle.Bold, Palette.Muted);
            Controls.Add(_version);
            Label website = Label("socyvia.com", "BrandWebsite", 28, 510, 218, 22, 9F, FontStyle.Regular, Palette.Secondary);
            Controls.Add(website);
        }

        internal void ApplyLanguage(bool arabic)
        {
            _desktop.Text = arabic ? "سطح المكتب العلمي" : "SCIENTIFIC DESKTOP";
            _desktop.AccessibleName = _desktop.Text;
            _subtitle.Text = arabic
                ? "الاختبار العلمي للعلوم\nالاجتماعية الحاسوبية"
                : "Scientific Testing for\nComputational Social Science";
            _subtitle.AccessibleName = _subtitle.Text.Replace("\n", " ");
            _languageCaption.Text = arabic ? "اللغة" : "LANGUAGE";
            _languageCaption.AccessibleName = _languageCaption.Text;
            _version.Text = arabic ? "الإصدار 1.0.0" : "VERSION 1.0.0";
            _version.AccessibleName = _version.Text;
            LanguageButton.Text = arabic ? "English" : "العربية";
            LanguageButton.AccessibleName = arabic
                ? "Switch installer language to English"
                : "تغيير لغة برنامج التثبيت إلى العربية";
            LanguageButton.Font = EmbeddedAssets.Font(9.5F, FontStyle.Bold, !arabic);
            foreach (Label label in Controls.OfType<Label>())
            {
                bool keepLatin = label.Name == "BrandProduct" || label.Name == "BrandWebsite";
                label.Font = EmbeddedAssets.Font(label.Font.SizeInPoints, label.Font.Style, arabic && !keepLatin);
                label.RightToLeft = arabic && !keepLatin ? RightToLeft.Yes : RightToLeft.No;
                label.TextAlign = ContentAlignment.MiddleCenter;
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
            label.Font = EmbeddedAssets.Font(size, style);
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
            using (Pen edge = new Pen(Palette.Border))
                e.Graphics.DrawLine(edge, Width - 1, 0, Width - 1, Height);
            using (SolidBrush glow = new SolidBrush(Color.FromArgb(28, Palette.Primary)))
                e.Graphics.FillEllipse(glow, -120, 320, 360, 360);
        }
    }

    internal sealed class BulletRow : Panel
    {
        private readonly Panel _marker;
        private readonly Label _text;

        internal BulletRow(string text, string name)
        {
            BackColor = Color.Transparent;
            _marker = new Panel();
            _marker.Name = name + "Dot";
            _marker.BackColor = Palette.Primary;
            _marker.SetBounds(0, 7, 6, 6);
            Controls.Add(_marker);

            _text = new Label();
            _text.Name = name;
            _text.Text = text;
            _text.AccessibleName = text;
            _text.AutoSize = false;
            _text.UseCompatibleTextRendering = false;
            _text.Font = EmbeddedAssets.Font(7.5F, FontStyle.Regular);
            _text.ForeColor = Palette.Secondary;
            _text.BackColor = Color.Transparent;
            _text.SetBounds(18, 0, 382, 22);
            Controls.Add(_text);
        }

        internal void ApplyDirection(bool arabic)
        {
            RightToLeft = arabic ? RightToLeft.Yes : RightToLeft.No;
            _marker.SetBounds(arabic ? Width - 6 : 0, 7, 6, 6);
            _text.SetBounds(arabic ? 0 : 18, 0, arabic ? Width - 18 : 382, 22);
            _text.RightToLeft = arabic ? RightToLeft.Yes : RightToLeft.No;
            _text.TextAlign = ContentAlignment.TopLeft;
            Invalidate(true);
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
            Color parentFill = Parent == null ? Palette.Canvas : Parent.BackColor;
            if (parentFill.A == 0) parentFill = Palette.Canvas;
            e.Graphics.Clear(parentFill);
            using (GraphicsPath path = RoundedPath(ClientRectangle, _radius))
            using (SolidBrush brush = new SolidBrush(_fill))
                e.Graphics.FillPath(brush, path);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle bounds = new Rectangle(0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
            using (GraphicsPath path = RoundedPath(bounds, _radius))
            using (Pen pen = new Pen(_border))
                e.Graphics.DrawPath(pen, path);
        }

        internal static GraphicsPath RoundedPath(Rectangle rectangle, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int diameter = Math.Max(2, radius * 2);
            Rectangle arc = new Rectangle(rectangle.X, rectangle.Y, diameter, diameter);
            path.AddArc(arc, 180, 90);
            arc.X = rectangle.Right - diameter;
            path.AddArc(arc, 270, 90);
            arc.Y = rectangle.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            arc.X = rectangle.Left;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    internal enum ButtonKind { Primary, Secondary, Ghost }

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
            Font = EmbeddedAssets.Font(10F, FontStyle.Bold);
            Cursor = Cursors.Hand;
            AccessibleRole = AccessibleRole.PushButton;
            UseCompatibleTextRendering = false;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor, true);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { _pressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color parentFill = Palette.Canvas;
            SurfacePanel surface = Parent as SurfacePanel;
            if (surface != null) parentFill = surface.FillColor;
            else if (Parent != null && Parent.BackColor.A > 0) parentFill = Parent.BackColor;
            e.Graphics.Clear(parentFill);
            Color fill;
            Color text;
            Color border;
            if (_kind == ButtonKind.Primary)
            {
                fill = _pressed ? Palette.PrimaryPressed : (_hover ? Palette.PrimaryHover : Palette.Primary);
                text = Color.White;
                border = fill;
            }
            else if (_kind == ButtonKind.Secondary)
            {
                fill = _pressed ? Palette.PrimarySoft : (_hover ? Color.FromArgb(239, 245, 255) : Color.White);
                text = Palette.Ink;
                border = _hover ? Palette.BorderStrong : Palette.Border;
            }
            else
            {
                fill = _hover ? Palette.PrimarySoft : Color.White;
                text = Palette.Primary;
                border = Color.Transparent;
            }
            Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = SurfacePanel.RoundedPath(bounds, 10))
            using (SolidBrush brush = new SolidBrush(fill))
            using (Pen pen = new Pen(border))
            {
                e.Graphics.FillPath(brush, path);
                if (border.A > 0) e.Graphics.DrawPath(pen, path);
            }
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if (Focused && ShowFocusCues)
            {
                Rectangle focus = Rectangle.Inflate(bounds, -4, -4);
                using (Pen pen = new Pen(_kind == ButtonKind.Primary ? Color.White : Palette.Primary))
                {
                    pen.DashStyle = DashStyle.Dot;
                    using (GraphicsPath path = SurfacePanel.RoundedPath(focus, 7)) e.Graphics.DrawPath(pen, path);
                }
            }
        }

    }

    internal sealed class OptionToggle : CheckBox
    {
        internal OptionToggle()
        {
            Font = EmbeddedAssets.Font(10F, FontStyle.Regular);
            ForeColor = Palette.Ink;
            BackColor = Palette.Canvas;
            Cursor = Cursors.Hand;
            AutoCheck = true;
            AccessibleRole = AccessibleRole.CheckButton;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            bool arabic = RightToLeft == RightToLeft.Yes;
            Rectangle box = new Rectangle(arabic ? Width - 23 : 1, (Height - 22) / 2, 22, 22);
            using (GraphicsPath path = SurfacePanel.RoundedPath(box, 5))
            using (SolidBrush brush = new SolidBrush(Checked ? Palette.Primary : Color.White))
            using (Pen pen = new Pen(Checked ? Palette.Primary : Palette.BorderStrong))
            {
                e.Graphics.FillPath(brush, path);
                e.Graphics.DrawPath(pen, path);
            }
            if (Checked)
            {
                using (Pen check = new Pen(Color.White, 2F))
                {
                    check.StartCap = LineCap.Round;
                    check.EndCap = LineCap.Round;
                    int origin = box.X;
                    e.Graphics.DrawLines(check, new[]
                    {
                        new Point(origin + 5, Height / 2),
                        new Point(origin + 9, Height / 2 + 4),
                        new Point(origin + 17, Height / 2 - 5)
                    });
                }
            }
            Rectangle textBounds = arabic
                ? new Rectangle(0, 0, Width - 34, Height)
                : new Rectangle(34, 0, Width - 36, Height);
            TextRenderer.DrawText(e.Graphics, Text, Font, textBounds, Palette.Ink,
                (arabic ? TextFormatFlags.Right | TextFormatFlags.RightToLeft : TextFormatFlags.Left) |
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if (Focused && ShowFocusCues)
            {
                Rectangle focus = arabic
                    ? new Rectangle(1, 6, Width - 32, Height - 12)
                    : new Rectangle(31, 6, Width - 34, Height - 12);
                ControlPaint.DrawFocusRectangle(e.Graphics, focus, Palette.Ink, BackColor);
            }
        }
    }

    internal sealed class PulseBar : Control
    {
        private readonly System.Windows.Forms.Timer _timer;
        private int _position;

        internal PulseBar()
        {
            DoubleBuffered = true;
            _timer = new System.Windows.Forms.Timer();
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
            int x = _position - 110;
            Rectangle segment = new Rectangle(x, 0, 110, Height - 1);
            Region previous = e.Graphics.Clip;
            using (GraphicsPath clipPath = SurfacePanel.RoundedPath(track, 4))
            {
                e.Graphics.SetClip(clipPath);
                using (SolidBrush brush = new SolidBrush(Palette.Primary)) e.Graphics.FillRectangle(brush, segment);
            }
            e.Graphics.Clip = previous;
        }
    }

    internal sealed class SuccessMark : Control
    {
        internal SuccessMark()
        {
            AccessibleName = "Installation successful";
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle circle = new Rectangle(1, 1, Width - 3, Height - 3);
            using (SolidBrush brush = new SolidBrush(Palette.SuccessSoft)) e.Graphics.FillEllipse(brush, circle);
            using (Pen edge = new Pen(Color.FromArgb(164, 216, 193), 1.5F)) e.Graphics.DrawEllipse(edge, circle);
            using (Pen check = new Pen(Palette.Success, 4F))
            {
                check.StartCap = LineCap.Round;
                check.EndCap = LineCap.Round;
                e.Graphics.DrawLines(check, new[] { new Point(19, 36), new Point(30, 47), new Point(52, 23) });
            }
        }
    }
}
