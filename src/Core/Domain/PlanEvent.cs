namespace Core.Domain;

public record PlanEvent(
    Milestone Label,
    LocalDate Date,
    bool Adjusted,
    LocalDate? OriginalDate);