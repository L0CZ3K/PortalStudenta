using Microsoft.Maui.Storage;

namespace Portal_Studenta.Services
{
    public class AppState
    {
        private UserRole _role = UserRole.Student;
        private bool _invertWeeks;
        private WeekParityMode _weekParityMode = WeekParityMode.Auto;
        private string _academicTermName = "";
        private string _academicTermStart = "";
        private string _groupCode = "";
        private string _language = "pl-PL";

        public UserRole Role
        {
            get => _role;
            private set
            {
                if (_role != value)
                {
                    _role = value;
                    Preferences.Default.Set("app_user_role", (int)value);
                    Preferences.Default.Set("app_starosta", value == UserRole.Starosta);
                    Preferences.Default.Set("app_admin", value == UserRole.Admin);
                    NotifyStateChanged();
                }
            }
        }

        public bool IsStarosta => _role == UserRole.Starosta;
        public bool IsAdmin => _role == UserRole.Admin;
        public bool HasManagementPrivileges => IsStarosta || IsAdmin;
        public bool CanManageSchedule => HasManagementPrivileges;
        public string UserName => $"{Preferences.Default.Get("user_first_name", "")} {Preferences.Default.Get("user_last_name", "")}".Trim();

        // Kompatybilność wsteczna z dotychczasowym kodem
        public bool IsStarostaMode => IsStarosta;
        public bool IsAdminMode => IsAdmin;

        public string Language
        {
            get => _language;
            private set
            {
                if (_language != value)
                {
                    _language = value;
                    Preferences.Default.Set("app_lang", value);
                    NotifyStateChanged();
                }
            }
        }

        /// <summary>
        /// Centralny helper tłumaczeń PL / EN.
        /// Eliminuje powtarzające się w widokach Razor konstrukcje @(AppState.Language == "en-US" ? "..." : "...").
        /// </summary>
        public string T(string pl, string en) => _language == "en-US" ? en : pl;

        /// <summary>
        /// Identyfikator notatki do automatycznego otwarcia po przejściu z Globalnej Wyszukiwarki (Cmd+K / Ctrl+K).
        /// </summary>
        public Guid? PendingOpenNoteId { get; set; }

        public string GroupCode
        {
            get => _groupCode;
            private set
            {
                if (_groupCode != value)
                {
                    _groupCode = value;
                    Preferences.Default.Set("app_groupcode", value);
                    NotifyStateChanged();
                }
            }
        }

        public WeekParityMode WeekParity
        {
            get => _weekParityMode;
            set
            {
                if (_weekParityMode != value)
                {
                    _weekParityMode = value;
                    Preferences.Default.Set("app_week_parity_mode", (int)value);
                    _invertWeeks = (value == WeekParityMode.Inverted);
                    Preferences.Default.Set("app_invertweeks", _invertWeeks);
                    NotifyStateChanged();
                }
            }
        }

        public string AcademicTermName
        {
            get => _academicTermName;
            set
            {
                if (_academicTermName != value)
                {
                    _academicTermName = value;
                    Preferences.Default.Set("app_academic_term_name", value);
                    NotifyStateChanged();
                }
            }
        }

        public string AcademicTermStart
        {
            get => _academicTermStart;
            set
            {
                if (_academicTermStart != value)
                {
                    _academicTermStart = value;
                    Preferences.Default.Set("app_academic_term_start", value);
                    NotifyStateChanged();
                }
            }
        }

        public bool InvertWeeks
        {
            get => _invertWeeks;
            private set
            {
                if (_invertWeeks != value)
                {
                    _invertWeeks = value;
                    Preferences.Default.Set("app_invertweeks", value);
                    NotifyStateChanged();
                }
            }
        }

        public event Action? OnChange;

        public AppState()
        {
            int savedRole = Preferences.Default.Get("app_user_role", -1);
            if (savedRole >= 0 && savedRole <= 2)
            {
                _role = (UserRole)savedRole;
            }
            else
            {
                // Fallback do starych kluczy jeśli istnieją
                if (Preferences.Default.Get("app_starosta", false))
                    _role = UserRole.Starosta;
                else if (Preferences.Default.Get("app_admin", false))
                    _role = UserRole.Admin;
                else
                    _role = UserRole.Student;
            }

            _invertWeeks = Preferences.Default.Get("app_invertweeks", false);
            int savedParity = Preferences.Default.Get("app_week_parity_mode", -1);
            if (savedParity >= 0 && savedParity <= 3)
            {
                _weekParityMode = (WeekParityMode)savedParity;
            }
            else
            {
                _weekParityMode = _invertWeeks ? WeekParityMode.Inverted : WeekParityMode.Auto;
            }

            _academicTermName = Preferences.Default.Get("app_academic_term_name", "");
            _academicTermStart = Preferences.Default.Get("app_academic_term_start", "");
            _groupCode = Preferences.Default.Get("app_groupcode", "");
            _language = Preferences.Default.Get("app_lang", "pl-PL");
        }

        public bool IsWeekEven(DateTime weekDate)
        {
            return WeekParityHelper.CalculateIsWeekEven(weekDate, WeekParity, _academicTermStart, InvertWeeks);
        }

        public void CycleWeekParity()
        {
            WeekParity = WeekParityHelper.GetNextParityMode(WeekParity);
        }

        public void SetLanguage(string lang)
        {
            Language = lang;
            try
            {
                var culture = new System.Globalization.CultureInfo(lang);
                System.Globalization.CultureInfo.DefaultThreadCurrentCulture = culture;
                System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = culture;
                System.Threading.Thread.CurrentThread.CurrentCulture = culture;
                System.Threading.Thread.CurrentThread.CurrentUICulture = culture;
            }
            catch {}
        }

        public void Unsubscribe(Action handler)
        {
            OnChange -= handler;
        }

        public void ToggleInvertWeeks(bool invert)
        {
            WeekParity = invert ? WeekParityMode.Inverted : WeekParityMode.Auto;
        }

        public void SetUserRole(UserRole role)
        {
            Role = role;
        }

        public void ToggleStarostaMode(bool isStarosta)
        {
            Role = isStarosta ? UserRole.Starosta : UserRole.Student;
        }

        public void ToggleAdminMode(bool isAdmin)
        {
            Role = isAdmin ? UserRole.Admin : UserRole.Student;
        }

        public void SetGroupCode(string code)
        {
            GroupCode = code;
        }

        public void SetStarostaMode(bool isStarosta) => ToggleStarostaMode(isStarosta);

        public void Reset()
        {
            _role = UserRole.Student;
            _groupCode = "";
            Preferences.Default.Remove("app_user_role");
            Preferences.Default.Remove("app_starosta");
            Preferences.Default.Remove("app_admin");
            Preferences.Default.Remove("app_groupcode");
            NotifyStateChanged();
        }

        public void NotifyStateChanged() => OnChange?.Invoke();
    }
}
