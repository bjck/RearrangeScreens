using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Text;
using System.Threading; 

namespace MonitorArranger
{
    static class Program
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        // API to force "Extend" mode
        [DllImport("user32.dll")]
        private static extern int SetDisplayConfig(uint numPathArrayElements, IntPtr pathArray, uint numModeInfoArrayElements, IntPtr modeInfoArray, uint flags);

        private const uint SDC_APPLY = 0x00000080;
        private const uint SDC_TOPOLOGY_EXTEND = 0x00000004;

        [STAThread]
        static void Main()
        {
            SetProcessDPIAware();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // 1. Force Windows into "Extend" mode
            SetDisplayConfig(0, IntPtr.Zero, 0, IntPtr.Zero, SDC_APPLY | SDC_TOPOLOGY_EXTEND);

            // 2. STABILIZATION LOOP
            // We wait until the screen count > 1 and stays stable for a moment.
            Screen[] screens = Screen.AllScreens;
            int stableCount = 0;
            int lastCount = 0;

            // Try for up to 10 seconds
            for (int i = 0; i < 20; i++) 
            {
                Thread.Sleep(500); // Check every half second
                screens = Screen.AllScreens;
                
                if (screens.Length > 1 && screens.Length == lastCount)
                {
                    stableCount++;
                    if (stableCount >= 4) // If stable for 2 seconds, proceed
                        break;
                }
                else
                {
                    stableCount = 0; // Reset if count changes
                }
                lastCount = screens.Length;
            }

            // One final refresh to be absolutely sure we have the final bounds
            screens = Screen.AllScreens;

            if (screens.Length < 2)
            {
                MessageBox.Show("Could not detect multiple monitors. Is the dock connected?");
                return;
            }

            var screenMap = new Dictionary<int, Screen>();
            for (int i = 0; i < screens.Length; i++)
            {
                screenMap[i + 1] = screens[i];
            }

            List<Form> overlays = new List<Form>();
            foreach (var kvp in screenMap)
            {
                int id = kvp.Key;
                Screen screen = kvp.Value;

                Form overlay = new Form();
                overlay.FormBorderStyle = FormBorderStyle.None;
                overlay.BackColor = Color.Black;
                
                // CRITICAL FIX FOR TEAL BARS/GAPS:
                // 1. Set manual position first
                overlay.StartPosition = FormStartPosition.Manual;
                overlay.Location = screen.Bounds.Location;
                
                // 2. Determine if we should Maximize
                // Maximizing ensures it covers the whole area even if our pixel math is slightly off due to scaling.
                overlay.WindowState = FormWindowState.Maximized; 
                
                overlay.TopMost = false;
                overlay.ShowInTaskbar = false;
                overlay.KeyPreview = true;
                overlay.KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Application.Exit(); };

                overlay.Paint += (sender, e) => 
                {
                    e.Graphics.Clear(Color.Black);
                    string text = id.ToString();
                    using (Font font = new Font("Arial", 200, FontStyle.Bold)) 
                    {
                        SizeF textSize = e.Graphics.MeasureString(text, font);
                        PointF location = new PointF(
                            (overlay.Width - textSize.Width) / 2,
                            (overlay.Height - textSize.Height) / 2
                        );
                        e.Graphics.DrawString(text, font, Brushes.White, location);
                    }
                };

                overlay.Show();
                overlays.Add(overlay);
            }

            Application.Run(new InputForm(screenMap, overlays));
        }
    }

    public class InputForm : Form
    {
        private TextBox txtInput;
        private TextBox txtLog; 
        private Button btnApply;
        private Label lblInstruction;
        private Dictionary<int, Screen> screenMap;
        private List<Form> overlays;

        public InputForm(Dictionary<int, Screen> screens, List<Form> overlayForms)
        {
            this.screenMap = screens;
            this.overlays = overlayForms;

            this.Text = "Monitor Arranger - DEBUG MODE";
            this.Size = new Size(500, 450); 
            
            // CHANGED: Offset the window so it doesn't cover the ID number in the center
            this.StartPosition = FormStartPosition.Manual;
            Rectangle bounds = Screen.PrimaryScreen.Bounds;
            this.Location = new Point(
                bounds.X + (bounds.Width - this.Width) / 2,
                bounds.Y + (bounds.Height - this.Height) / 2 + 150 
            );

            this.TopMost = true; 
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            
            this.KeyPreview = true;
            this.KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Application.Exit(); };

            lblInstruction = new Label();
            lblInstruction.Text = "Enter sequence Left to Right (e.g., '2 1 3' or '213'):";
            lblInstruction.Font = new Font("Segoe UI", 10);
            lblInstruction.Location = new Point(20, 20);
            lblInstruction.AutoSize = true;
            this.Controls.Add(lblInstruction);

            txtInput = new TextBox();
            txtInput.Location = new Point(20, 50);
            txtInput.Size = new Size(440, 30);
            txtInput.Font = new Font("Segoe UI", 12);
            txtInput.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) ApplySettings(); };
            this.Controls.Add(txtInput);

            btnApply = new Button();
            btnApply.Text = "Arrange";
            btnApply.Location = new Point(330, 90);
            btnApply.Size = new Size(130, 30);
            btnApply.Click += (s, e) => ApplySettings();
            this.Controls.Add(btnApply);

            Button btnCancel = new Button();
            btnCancel.Text = "Exit";
            btnCancel.Location = new Point(230, 90);
            btnCancel.Size = new Size(90, 30);
            btnCancel.Click += (s, e) => Application.Exit();
            this.Controls.Add(btnCancel);

            Label lblLog = new Label();
            lblLog.Text = "Debug Log:";
            lblLog.Location = new Point(20, 130);
            this.Controls.Add(lblLog);

            txtLog = new TextBox();
            txtLog.Location = new Point(20, 150);
            txtLog.Size = new Size(440, 240);
            txtLog.Multiline = true;
            txtLog.ScrollBars = ScrollBars.Vertical;
            txtLog.ReadOnly = true;
            txtLog.Font = new Font("Consolas", 9);
            this.Controls.Add(txtLog);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            this.Activate();
            txtInput.Focus();
        }

        private void Log(string msg)
        {
            txtLog.AppendText(msg + "\r\n");
        }

        private void ApplySettings()
        {
            txtLog.Clear();
            Log("Starting rearrangement...");
            string input = txtInput.Text;
            if (string.IsNullOrWhiteSpace(input)) return;

            try
            {
                List<int> order = new List<int>();

                if (input.IndexOfAny(new[] { ' ', ',' }) >= 0)
                {
                     order = input.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries)
                                           .Select(int.Parse)
                                           .ToList();
                }
                else
                {
                    foreach (char c in input)
                    {
                        if (char.IsDigit(c))
                        {
                            order.Add(int.Parse(c.ToString()));
                        }
                    }
                }

                if (order.Count != screenMap.Count || order.Distinct().Count() != screenMap.Count)
                {
                    Log("Error: Invalid input. Ensure all IDs are used exactly once.");
                    return;
                }

                DisplayManager.RearrangeScreens(order, screenMap, Log);
                Log("Sequence finished. If screens didn't move, check errors above.");
            }
            catch (Exception ex)
            {
                Log("EXCEPTION: " + ex.Message);
            }
        }
    }

    public static class DisplayManager
    {
        [DllImport("user32.dll")]
        public static extern int ChangeDisplaySettingsEx(string lpszDeviceName, ref DEVMODE lpDevMode, IntPtr hwnd, uint dwflags, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool EnumDisplaySettings(string lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode);

        [DllImport("user32.dll")]
        public static extern int ChangeDisplaySettingsEx(string lpszDeviceName, IntPtr lpDevMode, IntPtr hwnd, uint dwflags, IntPtr lParam);

        public const int ENUM_CURRENT_SETTINGS = -1;
        public const int CDS_UPDATEREGISTRY = 0x01;
        public const int CDS_NORESET = 0x10000000;
        public const int CDS_APPLY = 0x00000000;
        public const int DM_POSITION = 0x00000020;
        public const int DM_PELSWIDTH = 0x00080000;
        public const int DM_PELSHEIGHT = 0x00100000;

        [StructLayout(LayoutKind.Sequential)]
        public struct POINTL { public int x; public int y; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        public struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public short dmSpecVersion; public short dmDriverVersion; public short dmSize;
            public short dmDriverExtra; public int dmFields; public POINTL dmPosition;
            public int dmDisplayOrientation; public int dmDisplayFixedOutput; public short dmColor;
            public short dmDuplex; public short dmYResolution; public short dmTTOption;
            public short dmCollate; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public short dmLogPixels; public int dmBitsPerPel; public int dmPelsWidth;
            public int dmPelsHeight; public int dmDisplayFlags; public int dmDisplayFrequency;
            public int dmICMMethod; public int dmICMIntent; public int dmMediaType;
            public int dmDitherType; public int dmReserved1; public int dmReserved2;
            public int dmPanningWidth; public int dmPanningHeight;
        }

        public static void RearrangeScreens(List<int> order, Dictionary<int, Screen> screenMap, Action<string> log)
        {
            Dictionary<int, int> calculatedX = new Dictionary<int, int>();
            int runningX = 0;

            log("--- Analyzing Screens ---");
            foreach (int id in order)
            {
                if (!screenMap.ContainsKey(id)) continue;
                Screen s = screenMap[id];
                
                DEVMODE tempDm = new DEVMODE();
                tempDm.dmSize = (short)Marshal.SizeOf(tempDm);
                if(EnumDisplaySettings(s.DeviceName, ENUM_CURRENT_SETTINGS, ref tempDm))
                {
                    log("Screen " + id + " (" + s.DeviceName + "): Width=" + tempDm.dmPelsWidth + " Height=" + tempDm.dmPelsHeight);
                    calculatedX[id] = runningX;
                    runningX += tempDm.dmPelsWidth;
                }
                else
                {
                    log("ERROR: Could not read settings for Screen " + id);
                }
            }

            int primaryId = -1;
            foreach (var kvp in screenMap)
            {
                if (kvp.Value.Primary)
                {
                    primaryId = kvp.Key;
                    break;
                }
            }
            log("Primary Screen ID: " + primaryId);

            int offsetX = 0;
            if (primaryId != -1 && calculatedX.ContainsKey(primaryId))
            {
                offsetX = -calculatedX[primaryId];
                log("Calculated Offset: " + offsetX + " (shifting so Primary is at 0,0)");
            }
            else
            {
                log("WARNING: Primary screen not found in sequence! Windows might reject this.");
            }

            log("--- Staging Changes ---");
            foreach (int id in order)
            {
                if (!screenMap.ContainsKey(id)) continue;
                Screen screen = screenMap[id];
                
                DEVMODE dm = new DEVMODE();
                dm.dmSize = (short)Marshal.SizeOf(dm);
                
                if (EnumDisplaySettings(screen.DeviceName, ENUM_CURRENT_SETTINGS, ref dm))
                {
                    int newX = calculatedX[id] + offsetX;
                    log("Setting Screen " + id + " to X=" + newX + ", Y=0");

                    dm.dmPosition.x = newX;
                    dm.dmPosition.y = 0; 
                    dm.dmFields = DM_POSITION | DM_PELSWIDTH | DM_PELSHEIGHT;

                    int result = ChangeDisplaySettingsEx(screen.DeviceName, ref dm, IntPtr.Zero, CDS_UPDATEREGISTRY | CDS_NORESET, IntPtr.Zero);
                    if (result != 0) 
                    {
                        log("FAIL: Screen " + id + " returned " + GetErrorMessage(result));
                    }
                    else
                    {
                        log("OK: Screen " + id + " staged successfully.");
                    }
                }
            }

            log("--- Applying ---");
            int finalResult = ChangeDisplaySettingsEx(null, IntPtr.Zero, IntPtr.Zero, CDS_APPLY, IntPtr.Zero);
            
            if (finalResult != 0)
            {
                log("FATAL: Final Apply failed. Code: " + GetErrorMessage(finalResult));
            }
            else
            {
                log("SUCCESS: Windows accepted the new layout.");
            }
        }

        private static string GetErrorMessage(int errorCode)
        {
            switch (errorCode)
            {
                case 0: return "SUCCESS (0)";
                case 1: return "RESTART REQUIRED (1)";
                case -1: return "FAILED (-1)";
                case -2: return "BAD MODE (-2) - Invalid coordinates?";
                case -5: return "BAD PARAM (-5) - Invalid flags?";
                default: return "Error Code " + errorCode;
            }
        }
    }
}