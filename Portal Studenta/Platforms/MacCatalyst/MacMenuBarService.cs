#if MACCATALYST
using System;
using System.Runtime.InteropServices;
using Foundation;
using ObjCRuntime;
using Portal_Studenta.Models;
using Portal_Studenta.Services;

namespace Portal_Studenta.Platforms.MacCatalyst
{
    /// <summary>
    /// Service responsible for managing the macOS system status bar item (Menu Bar Extra).
    /// Displays ongoing and upcoming university classes directly in the macOS menu bar.
    /// </summary>
    public class MacMenuBarService : IDisposable
    {
        [DllImport("/usr/lib/libSystem.dylib")]
        private static extern IntPtr dlopen(string path, int mode);

        [DllImport("/usr/lib/libobjc.A.dylib")]
        private static extern IntPtr objc_getClass(string name);

        [DllImport("/usr/lib/libobjc.A.dylib")]
        private static extern IntPtr sel_registerName(string name);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static extern IntPtr objc_msgSend_IntPtr(IntPtr receiver, IntPtr selector, IntPtr arg1);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static extern IntPtr objc_msgSend_nfloat(IntPtr receiver, IntPtr selector, nfloat arg1);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static extern IntPtr objc_msgSend_IntPtr_IntPtr_IntPtr(IntPtr receiver, IntPtr selector, IntPtr arg1, IntPtr arg2, IntPtr arg3);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static extern void objc_msgSend_bool(IntPtr receiver, IntPtr selector, bool arg1);

        private IntPtr _statusItem = IntPtr.Zero;
        private readonly ScheduleService? _scheduleService;
        private readonly UsosApiService? _usosApiService;
        private System.Timers.Timer? _refreshTimer;
        private MenuActionHandler? _actionHandler;
        private Action? _scheduleLoadedHandler;
        private bool _disposed;

        /// <summary>
        /// Initializes a new instance of <see cref="MacMenuBarService"/>.
        /// </summary>
        /// <param name="scheduleService">Schedule service for accessing loaded classes.</param>
        /// <param name="usosApiService">USOS API service for background refresh requests.</param>
        public MacMenuBarService(ScheduleService? scheduleService = null, UsosApiService? usosApiService = null)
        {
            _scheduleService = scheduleService;
            _usosApiService = usosApiService;
        }

        public void Initialize()
        {
            try
            {
                // Dynamiczne załadowanie frameworka AppKit dla MacCatalyst
                dlopen("/System/Library/Frameworks/AppKit.framework/AppKit", 1);

                var nsStatusBarClass = objc_getClass("NSStatusBar");
                if (nsStatusBarClass == IntPtr.Zero) return;

                var systemStatusBarSel = sel_registerName("systemStatusBar");
                var statusBar = objc_msgSend(nsStatusBarClass, systemStatusBarSel);
                if (statusBar == IntPtr.Zero) return;

                var statusItemWithLengthSel = sel_registerName("statusItemWithLength:");
                // -1.0 = NSVariableStatusItemLength
                _statusItem = objc_msgSend_nfloat(statusBar, statusItemWithLengthSel, (nfloat)(-1.0));
                if (_statusItem == IntPtr.Zero) return;

                _actionHandler = new MenuActionHandler(this);

                UpdateMenuDisplay();

                if (_scheduleService != null)
                {
                    _scheduleLoadedHandler = () =>
                    {
                        MainThread.BeginInvokeOnMainThread(UpdateMenuDisplay);
                    };
                    _scheduleService.OnScheduleLoaded += _scheduleLoadedHandler;
                }

                // Timer aktualizacji statusu paska co 60 sekund
                _refreshTimer = new System.Timers.Timer(60000);
                _refreshTimer.Elapsed += (s, e) =>
                {
                    MainThread.BeginInvokeOnMainThread(UpdateMenuDisplay);
                };
                _refreshTimer.Start();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MacMenuBarService] Nie udało się zainicjalizować paska menu: {ex.Message}");
            }
        }

        public void UpdateMenuDisplay()
        {
            if (_statusItem == IntPtr.Zero) return;

            try
            {
                var now = DateTime.Now;
                var allLessons = _scheduleService?.GetAllEvents() ?? new System.Collections.Generic.List<Lesson>();

                var ongoing = allLessons.FirstOrDefault(l => !l.IsCanceled && l.Start <= now && l.End > now);
                var upcoming = allLessons
                    .Where(l => !l.IsCanceled && l.Start > now && l.Start.Date == now.Date)
                    .OrderBy(l => l.Start)
                    .FirstOrDefault();

                // 1. Ustawienie tekstu przycisku w pasku menu macOS
                string buttonText = "🎓 UniAvio";
                if (ongoing != null)
                {
                    int minsLeft = Math.Max(1, (int)(ongoing.End - now).TotalMinutes);
                    string place = !string.IsNullOrEmpty(ongoing.Place) ? $" ({ongoing.Place})" : "";
                    buttonText = $"🎓 {ongoing.Title}{place} [do {ongoing.End:HH:mm}]";
                    if (buttonText.Length > 36) buttonText = $"🎓 {ongoing.Title.Substring(0, 16)}… [do {ongoing.End:HH:mm}]";
                }
                else if (upcoming != null)
                {
                    var diff = upcoming.Start - now;
                    string countdown = diff.TotalHours >= 1 ? $"{(int)diff.TotalHours}h {diff.Minutes}m" : $"{Math.Max(1, diff.Minutes)}m";
                    buttonText = $"🎓 Za {countdown}: {upcoming.Title}";
                    if (buttonText.Length > 34) buttonText = $"🎓 Za {countdown}: {upcoming.Title.Substring(0, 14)}…";
                }

                var buttonSel = sel_registerName("button");
                var button = objc_msgSend(_statusItem, buttonSel);
                if (button != IntPtr.Zero)
                {
                    var setTitleSel = sel_registerName("setTitle:");
                    using var nsTitle = new NSString(buttonText);
                    objc_msgSend_IntPtr(button, setTitleSel, nsTitle.Handle);
                }

                // 2. Budowanie natywnego menu rozwijanego po kliknięciu
                var nsMenuClass = objc_getClass("NSMenu");
                var allocSel = sel_registerName("alloc");
                var initSel = sel_registerName("init");
                var menu = objc_msgSend(objc_msgSend(nsMenuClass, allocSel), initSel);

                var nsMenuItemClass = objc_getClass("NSMenuItem");
                var addItemSel = sel_registerName("addItem:");
                var separatorItemSel = sel_registerName("separatorItem");

                // Nagłówek aplikacji
                AddMenuItem(menu, "🎓 UniAvio — Asystent Studenta", null, false);
                AddMenuItem(menu, $"🕒 {DateTime.Now:dddd, dd MMMM HH:mm}", null, false);
                objc_msgSend_IntPtr(menu, addItemSel, objc_msgSend(nsMenuItemClass, separatorItemSel));

                // Informacja o bieżących zajęciach
                if (ongoing != null)
                {
                    AddMenuItem(menu, $"▶ TRWAJĄCE: {ongoing.Title}", null, false);
                    AddMenuItem(menu, $"   📍 Sala: {ongoing.Place}", null, false);
                    AddMenuItem(menu, $"   ⏰ Godziny: {ongoing.Start:HH:mm} – {ongoing.End:HH:mm} (pozostało {(int)(ongoing.End - now).TotalMinutes} min)", null, false);
                    if (!string.IsNullOrEmpty(ongoing.Person))
                    {
                        AddMenuItem(menu, $"   👤 Prowadzący: {ongoing.Person}", null, false);
                    }
                }
                else
                {
                    AddMenuItem(menu, "▶ Brak trwających zajęć w tym momencie", null, false);
                }

                objc_msgSend_IntPtr(menu, addItemSel, objc_msgSend(nsMenuItemClass, separatorItemSel));

                // Informacja o kolejnych zajęciach
                if (upcoming != null)
                {
                    var diff = upcoming.Start - now;
                    string timeStr = diff.TotalHours >= 1 ? $"za {(int)diff.TotalHours}h {diff.Minutes}m" : $"za {Math.Max(1, diff.Minutes)} min";
                    AddMenuItem(menu, $"⏭ KOLEJNE: {upcoming.Title} ({timeStr})", null, false);
                    AddMenuItem(menu, $"   📍 Sala: {upcoming.Place}", null, false);
                    AddMenuItem(menu, $"   ⏰ Rozpoczęcie: {upcoming.Start:HH:mm} (do {upcoming.End:HH:mm})", null, false);
                }
                else
                {
                    AddMenuItem(menu, "✅ Wszystkie zaplanowane zajęcia na dziś zakończone", null, false);
                }

                objc_msgSend_IntPtr(menu, addItemSel, objc_msgSend(nsMenuItemClass, separatorItemSel));

                // Szybkie akcje
                AddMenuItem(menu, "🔄 Odśwież plan zajęć", "actionRefresh:", true);
                AddMenuItem(menu, "💻 Otwórz pełne okno UniAvio", "actionOpenApp:", true);
                
                objc_msgSend_IntPtr(menu, addItemSel, objc_msgSend(nsMenuItemClass, separatorItemSel));
                AddMenuItem(menu, "❌ Zamknij aplikację", "actionQuit:", true);

                // Przypięcie menu do statusItem
                var setMenuSel = sel_registerName("setMenu:");
                objc_msgSend_IntPtr(_statusItem, setMenuSel, menu);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MacMenuBarService] Błąd renderowania menu: {ex.Message}");
            }
        }

        private void AddMenuItem(IntPtr menu, string title, string? actionSelectorName, bool enabled)
        {
            var nsMenuItemClass = objc_getClass("NSMenuItem");
            var allocSel = sel_registerName("alloc");
            var initWithTitleSel = sel_registerName("initWithTitle:action:keyEquivalent:");
            var addItemSel = sel_registerName("addItem:");
            var setTargetSel = sel_registerName("setTarget:");
            var setEnabledSel = sel_registerName("setEnabled:");

            using var nsTitle = new NSString(title);
            using var nsKey = new NSString("");

            IntPtr sel = actionSelectorName != null ? sel_registerName(actionSelectorName) : IntPtr.Zero;

            var item = objc_msgSend_IntPtr_IntPtr_IntPtr(
                objc_msgSend(nsMenuItemClass, allocSel),
                initWithTitleSel,
                nsTitle.Handle,
                sel,
                nsKey.Handle);

            if (_actionHandler != null && actionSelectorName != null)
            {
                objc_msgSend_IntPtr(item, setTargetSel, _actionHandler.Handle);
            }

            objc_msgSend_bool(item, setEnabledSel, enabled);
            objc_msgSend_IntPtr(menu, addItemSel, item);
        }

        public void BringAppToFront()
        {
            try
            {
                var nsAppClass = objc_getClass("NSApplication");
                var sharedAppSel = sel_registerName("sharedApplication");
                var nsApp = objc_msgSend(nsAppClass, sharedAppSel);

                if (nsApp != IntPtr.Zero)
                {
                    var unhideSel = sel_registerName("unhide:");
                    objc_msgSend_IntPtr(nsApp, unhideSel, IntPtr.Zero);

                    var activateSel = sel_registerName("activateIgnoringOtherApps:");
                    objc_msgSend_bool(nsApp, activateSel, true);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MacMenuBarService] BringAppToFront error: {ex.Message}");
            }
        }

        public void RefreshSchedule()
        {
            if (_scheduleService != null && _usosApiService != null)
            {
                int diff = (7 + (DateTime.Today.DayOfWeek - DayOfWeek.Monday)) % 7;
                var thisMonday = DateTime.Today.AddDays(-diff);
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _scheduleService.EnsureDataLoadedAsync(_usosApiService, thisMonday, 14, force: true);
                        MainThread.BeginInvokeOnMainThread(UpdateMenuDisplay);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[MacMenuBarService] RefreshSchedule error: {ex.Message}");
                    }
                });
            }
        }

        /// <summary>
        /// Disposes timer and unsubscribes from event handlers.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_scheduleService != null && _scheduleLoadedHandler != null)
            {
                _scheduleService.OnScheduleLoaded -= _scheduleLoadedHandler;
                _scheduleLoadedHandler = null;
            }

            if (_refreshTimer != null)
            {
                _refreshTimer.Stop();
                _refreshTimer.Dispose();
                _refreshTimer = null;
            }
        }

        [Register("UniAvioMenuActionHandler")]
        private class MenuActionHandler : NSObject
        {
            private readonly MacMenuBarService _service;

            public MenuActionHandler(MacMenuBarService service)
            {
                _service = service;
            }

            [Export("actionOpenApp:")]
            public void ActionOpenApp(NSObject sender)
            {
                _service.BringAppToFront();
            }

            [Export("actionRefresh:")]
            public void ActionRefresh(NSObject sender)
            {
                _service.RefreshSchedule();
            }

            [Export("actionQuit:")]
            public void ActionQuit(NSObject sender)
            {
                Environment.Exit(0);
            }
        }
    }
}
#endif
