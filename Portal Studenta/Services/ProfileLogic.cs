using System.Text.Json;

namespace Portal_Studenta.Services
{
    public class UserProfileData
    {
        public string Id { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string StudentNumber { get; set; } = string.Empty;
        public string ProgrammeName { get; set; } = string.Empty;
        public string ProgrammeId { get; set; } = string.Empty;
        public string FacultyId { get; set; } = string.Empty;
    }

    /// <summary>
    /// Bezpieczny parser danych profilu użytkownika z USOS API.
    /// Chroni przed brakiem pól, nullami i zmianami schematu JSON.
    /// </summary>
    public static class ProfileLogic
    {
        public static UserProfileData ParseUserProfile(JsonElement json)
        {
            var profile = new UserProfileData();
            if (json.ValueKind != JsonValueKind.Object) return profile;

            if (json.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String)
                profile.Id = idProp.GetString() ?? "";

            if (json.TryGetProperty("first_name", out var fnProp) && fnProp.ValueKind == JsonValueKind.String)
                profile.FirstName = fnProp.GetString() ?? "";

            if (json.TryGetProperty("last_name", out var lnProp) && lnProp.ValueKind == JsonValueKind.String)
                profile.LastName = lnProp.GetString() ?? "";

            if (json.TryGetProperty("student_number", out var snProp) && snProp.ValueKind == JsonValueKind.String)
                profile.StudentNumber = snProp.GetString() ?? "";

            if (json.TryGetProperty("student_programmes", out var progs) && progs.ValueKind == JsonValueKind.Array && progs.GetArrayLength() > 0)
            {
                var firstProgObj = progs[0];
                if (firstProgObj.ValueKind == JsonValueKind.Object)
                {
                    if (firstProgObj.TryGetProperty("programme", out var progProp) && progProp.ValueKind == JsonValueKind.Object)
                    {
                        if (progProp.TryGetProperty("id", out var pid) && pid.ValueKind == JsonValueKind.String)
                            profile.ProgrammeId = pid.GetString() ?? "";

                        if (progProp.TryGetProperty("description", out var desc) && desc.ValueKind == JsonValueKind.Object)
                        {
                            if (desc.TryGetProperty("pl", out var plDesc) && plDesc.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(plDesc.GetString()))
                            {
                                profile.ProgrammeName = plDesc.GetString()!;
                            }
                            else if (desc.TryGetProperty("en", out var enDesc) && enDesc.ValueKind == JsonValueKind.String)
                            {
                                profile.ProgrammeName = enDesc.GetString() ?? "";
                            }
                        }
                    }

                    if (firstProgObj.TryGetProperty("faculty", out var facProp) && facProp.ValueKind == JsonValueKind.Object)
                    {
                        if (facProp.TryGetProperty("id", out var fid) && fid.ValueKind == JsonValueKind.String)
                            profile.FacultyId = fid.GetString() ?? "";
                    }
                }
            }

            return profile;
        }
    }
}
