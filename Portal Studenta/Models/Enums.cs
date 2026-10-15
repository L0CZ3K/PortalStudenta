namespace Portal_Studenta.Services
{
    public enum UserRole
    {
        Student = 0,
        Admin = 1,
        Starosta = 2
    }

    public enum WeekParityMode
    {
        Auto = 0,       // Automatycznie z USOS / kalendarza akademickiego (ISO-8601)
        AlwaysEven = 1, // Zawsze parzysty
        AlwaysOdd = 2,  // Zawsze nieparzysty
        Inverted = 3    // Odwrócona parzystość (Tydzień A ↔ B)
    }

    public enum ServerStatus
    {
        Checking,
        Online,
        Offline
    }

    public enum ConnectionHealth
    {
        Online,
        Degraded,
        ServerError,
        Offline
    }
}
