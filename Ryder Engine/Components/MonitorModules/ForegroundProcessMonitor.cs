﻿using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Ryder_Engine.Components.MonitorModules
{
    class ForegroundProcessMonitor
    {
        #region FOREGROUND_DETECTION
        private WinEventDelegate dele = null;
        delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

        private const uint WINEVENT_OUTOFCONTEXT = 0;
        private const uint EVENT_SYSTEM_FOREGROUND = 3;
        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
        [DllImport("user32.dll")]
        static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);
        [DllImport("user32.dll")]
        public static extern IntPtr GetWindowThreadProcessId(IntPtr hWnd, out uint ProcessId);
        #endregion

        public EventHandler<string> newForegroundProcess;
        public Process foregroundProcess = null;
        public string foregroundProcessName = null;
        public bool first = true;

        public ForegroundProcessMonitor()
        {
            dele = new WinEventDelegate(WinEventProc);
            SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, dele, 0, 0, WINEVENT_OUTOFCONTEXT);
            WinEventProc(IntPtr.Zero, 0, GetForegroundWindow(), 0, 0, 0, 0);
        }

        private void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            try
            {
                uint pid;
                GetWindowThreadProcessId(hwnd, out pid);
                Process newProcess = Process.GetProcessById((int)pid);
                if (foregroundProcess != null)
                {
                    if (foregroundProcess.Id == pid && foregroundProcess.ProcessName == newProcess.ProcessName) {
                        return;
                    } else
                    {
                        foregroundProcess.Dispose();
                    }
                }
                foregroundProcessName = newProcess.ProcessName;
                foregroundProcess = newProcess;
                if (!first) newForegroundProcess.Invoke(this, foregroundProcessName);
                else first = false;
            }
            catch (Exception e)
            {
                if (foregroundProcess != null)
                {
                    foregroundProcess = null;
                    if (!first) newForegroundProcess.Invoke(this, null);
                    else first = false;
                } 
            }
        }
    }
}