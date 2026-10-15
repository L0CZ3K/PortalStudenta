using System.Text.Json;

namespace Portal_Studenta.Services
{
    public class ProfileService
    {
        private readonly UsosApiService _apiService;
        
        public string Id { get; private set; } = string.Empty;
        public string FirstName { get; private set; } = string.Empty;
        public string LastName { get; private set; } = string.Empty;
        public string StudentNumber { get; private set; } = string.Empty;
        public string ProgrammeName { get; private set; } = string.Empty;
        public string ProgrammeId { get; private set; } = string.Empty;
        public string FacultyId { get; private set; } = string.Empty;

        public bool IsLoaded { get; private set; } = false;
        public event Action? OnProfileLoaded;

        public ProfileService(UsosApiService apiService)
        {
            _apiService = apiService;

            Id = Preferences.Default.Get("user_usos_id", string.Empty);
            StudentNumber = Preferences.Default.Get("user_student_number", string.Empty);
            FirstName = Preferences.Default.Get("user_first_name", string.Empty);
            LastName = Preferences.Default.Get("user_last_name", string.Empty);
            ProgrammeName = Preferences.Default.Get("user_programme_name", string.Empty);
            ProgrammeId = Preferences.Default.Get("user_programme_id", string.Empty);
            FacultyId = Preferences.Default.Get("user_faculty_id", string.Empty);
            if (!string.IsNullOrEmpty(Id) || !string.IsNullOrEmpty(StudentNumber))
            {
                IsLoaded = true;
            }
        }

        public async Task LoadProfileAsync()
        {
            try
            {
                var json = await _apiService.GetAsync<JsonElement>("services/users/user?fields=id|first_name|last_name|student_number|student_programmes");
                var parsed = ProfileLogic.ParseUserProfile(json);
                
                Id = parsed.Id;
                FirstName = parsed.FirstName;
                LastName = parsed.LastName;
                StudentNumber = parsed.StudentNumber;
                ProgrammeId = parsed.ProgrammeId;
                ProgrammeName = parsed.ProgrammeName;
                FacultyId = parsed.FacultyId;

                Preferences.Default.Set("user_usos_id", Id);
                Preferences.Default.Set("user_student_number", StudentNumber);
                Preferences.Default.Set("user_first_name", FirstName);
                Preferences.Default.Set("user_last_name", LastName);
                Preferences.Default.Set("user_programme_name", ProgrammeName);
                Preferences.Default.Set("user_programme_id", ProgrammeId);
                Preferences.Default.Set("user_faculty_id", FacultyId);

                IsLoaded = true;
                OnProfileLoaded?.Invoke();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Błąd podczas ładowania profilu: {ex.Message}");
            }
        }
        public void Clear()
        {
            Id = string.Empty;
            FirstName = string.Empty;
            LastName = string.Empty;
            StudentNumber = string.Empty;
            ProgrammeName = string.Empty;
            ProgrammeId = string.Empty;
            FacultyId = string.Empty;
            IsLoaded = false;
            Preferences.Default.Remove("user_usos_id");
            Preferences.Default.Remove("user_student_number");
            Preferences.Default.Remove("user_first_name");
            Preferences.Default.Remove("user_last_name");
            Preferences.Default.Remove("user_programme_name");
            Preferences.Default.Remove("user_programme_id");
            Preferences.Default.Remove("user_faculty_id");
        }
    }
}
