namespace Portal_Studenta.Services
{
    /// <summary>
    /// Czysta logika biznesowa dla weryfikacji stanu zdrowia połączenia z USOS (RFC/HTTP, ping thresholds, status dot).
    /// Pozwala na pełne testowanie jednostkowe bez zależności od platformy .NET MAUI.
    /// </summary>
    public static class UniversityHealthLogic
    {
        public static string GetStatusLabel(ConnectionHealth health, string uniShortName) => health switch
        {
            ConnectionHealth.Offline => "Offline • Brak sieci",
            ConnectionHealth.Degraded => $"{uniShortName} • Słabe połączenie",
            ConnectionHealth.ServerError => $"{uniShortName} • Błąd serwera",
            _ => $"{uniShortName} • Online"
        };

        public static string GetDotColor(ConnectionHealth health) => health switch
        {
            ConnectionHealth.Online => "#30D158",
            ConnectionHealth.Degraded => "#FF9F0A",
            ConnectionHealth.ServerError => "#FF453A",
            ConnectionHealth.Offline => "#FF453A",
            _ => "#30D158"
        };

        public static (ServerStatus status, string message) ClassifyHttpResponse(int statusCode, string uniShortName, long pingMs)
        {
            if (statusCode < 500)
            {
                return (ServerStatus.Online, $"Serwer USOS {uniShortName} odpowiada prawidłowo ({pingMs} ms)");
            }
            return (ServerStatus.Offline, $"Serwer USOS zgłosił błąd wewnętrzny (HTTP {statusCode})");
        }

        public static (ConnectionHealth health, string message) ResolveHealthState(
            bool hasInternet, 
            ServerStatus serverStatus, 
            long pingMs, 
            string uniShortName, 
            string? serverErrorMessage = null)
        {
            if (!hasInternet)
            {
                return (ConnectionHealth.Offline, "Brak połączenia z Internetem (Tryb Offline)");
            }

            if (serverStatus == ServerStatus.Online)
            {
                if (pingMs > 800)
                {
                    return (ConnectionHealth.Degraded, $"Serwer USOS {uniShortName} odpowiada wolno ({pingMs} ms)");
                }
                return (ConnectionHealth.Online, $"Serwer USOS {uniShortName} online ({pingMs} ms)");
            }

            return (ConnectionHealth.ServerError, serverErrorMessage ?? $"{uniShortName} • Błąd serwera");
        }
    }
}
