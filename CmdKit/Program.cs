using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace CmdKit
{
    internal static class Program
    {
        private const string MutexName = "Global\\CmdKit_SingleInstance_v1"; // unique name
        private const string ShowMessageName = "CmdKit_ShowActivate_Message"; // for RegisterWindowMessage (retained for compatibility)
        private const string ActivateEventName = "Global\\CmdKit_ActivateEvent_v1"; // event to wake hidden instance

        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")] private static extern uint RegisterWindowMessage(string lpString);
        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        internal static readonly uint WM_SHOWAPP = RegisterWindowMessage(ShowMessageName);
        private const int SW_RESTORE = 9;
        private const int SW_SHOW = 5;
        private const int SW_SHOWNORMAL = 1;

        private static EventWaitHandle? _activateEvent; // only owned by first instance

        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            using var mutex = new Mutex(initiallyOwned: true, name: MutexName, out bool isNew);
            if (!isNew)
            {
                // Second instance: signal event (if exists) and try legacy handle activation then exit.
                try
                {
                    if (EventWaitHandle.OpenExisting(ActivateEventName) is EventWaitHandle ev)
                    {
                        ev.Set();
                        ev.Dispose();
                    }
                }
                catch { }
                TryActivateExisting();
                return;
            }

            // First instance: create activation event (auto reset)
            _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);

            ApplicationConfiguration.Initialize();
            var form = new CmdKitForm();

            // Background thread waits for activation signal
            var waitThread = new Thread(() => ActivationWaitLoop(form)) { IsBackground = true, Name = "CmdKitActivateWait" };
            waitThread.Start();

            Application.Run(form);
        }

        private static void ActivationWaitLoop(CmdKitForm form)
        {
            if (_activateEvent == null) return;
            while (true)
            {
                try
                {
                    _activateEvent.WaitOne();
                    if (form.IsDisposed) break;
                    try
                    {
                        form.BeginInvoke(new Action(() => form.BringToFrontExternal()));
                    }
                    catch { }
                }
                catch { break; }
            }
        }

        private static void TryActivateExisting()
        {
            try
            {
                var current = Process.GetCurrentProcess();
                foreach (var p in Process.GetProcessesByName(current.ProcessName))
                {
                    if (p.Id == current.Id) continue;
                    IntPtr h = p.MainWindowHandle;
                    if (h == IntPtr.Zero)
                    {
                        // Attempt to wait for a visible main window (if original instance currently hidden MainWindowHandle may remain zero)
                        for (int i = 0; i < 5 && h == IntPtr.Zero; i++)
                        {
                            Thread.Sleep(120);
                            p.Refresh();
                            h = p.MainWindowHandle;
                        }
                    }
                    if (h != IntPtr.Zero)
                    {
                        if (WM_SHOWAPP != 0) PostMessage(h, WM_SHOWAPP, IntPtr.Zero, IntPtr.Zero);
                        ShowWindow(h, SW_SHOW);
                        if (IsIconic(h)) ShowWindow(h, SW_RESTORE); else ShowWindow(h, SW_SHOWNORMAL);
                        SetForegroundWindow(h);
                        break;
                    }
                }
            }
            catch { }
        }
    }
}