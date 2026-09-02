namespace Core.Domain;

public sealed record HolidayConflict(
    Milestone Marker,
    LocalDate Date,
    string Country,
    string? Region,
    string HolidayName);
