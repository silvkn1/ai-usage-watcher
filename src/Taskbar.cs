using System;
using System.Runtime.InteropServices;

namespace ClaudeUsageWidget
{
    internal enum TaskbarProgressBarStatus
    {
        NoProgress = 0,
        Indeterminate = 1,
        Normal = 2,      // Green (< 50%)
        Error = 4,       // Red (>= 80%)
        Paused = 8       // Yellow (50% - 79%)
    }

    [ComImport]
    [Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ITaskbarList3
    {
        // ITaskbarList
        void HrInit();
        void AddTab(IntPtr hwnd);
        void DeleteTab(IntPtr hwnd);
        void ActivateTab(IntPtr hwnd);
        void SetActiveAlt(IntPtr hwnd);

        // ITaskbarList2
        void MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fFullscreen);

        // ITaskbarList3
        void SetProgressValue(IntPtr hwnd, ulong ullCompleted, ulong ullTotal);
        void SetProgressState(IntPtr hwnd, TaskbarProgressBarStatus tbpFlags);
        void RegisterTab(IntPtr hwndTab, IntPtr hwndMDI);
        void UnregisterTab(IntPtr hwndTab);
        void SetTabOrder(IntPtr hwndTab, IntPtr hwndInsertBefore);
        void SetTabActive(IntPtr hwndTab, IntPtr hwndMDI, uint dwReserved);
        void ThumbBarAddButtons(IntPtr hwnd, uint cButtons, IntPtr pButton);
        void ThumbBarUpdateButtons(IntPtr hwnd, uint cButtons, IntPtr pButton);
        void ThumbBarSetImageList(IntPtr hwnd, IntPtr himl);
        void SetOverlayIcon(IntPtr hwnd, IntPtr hIcon, [MarshalAs(UnmanagedType.LPWStr)] string pszDescription);
        void SetThumbnailTooltip(IntPtr hwnd, [MarshalAs(UnmanagedType.LPWStr)] string pszTip);
        void SetThumbnailClip(IntPtr hwnd, IntPtr prcClip);
    }

    [ComImport]
    [Guid("56FDF344-FD6D-11d0-958A-006097C9A090")]
    [ClassInterface(ClassInterfaceType.None)]
    internal class TaskbarInstance
    {
    }

    /// <summary>Pengelola progress bar dan status warna pada tombol taskbar Windows (ITaskbarList3).</summary>
    internal static class TaskbarProgress
    {
        private static ITaskbarList3 _taskbar;
        private static bool _failed;

        public static void Update(IntPtr hwnd, double usedPercent)
        {
            if (_failed || hwnd == IntPtr.Zero) return;
            try
            {
                if (_taskbar == null)
                {
                    _taskbar = (ITaskbarList3)new TaskbarInstance();
                    _taskbar.HrInit();
                }

                if (double.IsNaN(usedPercent) || usedPercent < 0)
                {
                    _taskbar.SetProgressState(hwnd, TaskbarProgressBarStatus.NoProgress);
                    return;
                }

                TaskbarProgressBarStatus status;
                if (usedPercent >= 80) status = TaskbarProgressBarStatus.Error;
                else if (usedPercent >= 50) status = TaskbarProgressBarStatus.Paused;
                else status = TaskbarProgressBarStatus.Normal;

                ulong val = (ulong)Math.Max(0, Math.Min(100, (int)Math.Round(usedPercent)));
                _taskbar.SetProgressState(hwnd, status);
                _taskbar.SetProgressValue(hwnd, val, 100);
            }
            catch (Exception ex)
            {
                _failed = true;
                Log.Write("taskbar.progress", ex);
            }
        }
    }
}
