namespace Core.Domain;

public record EngineConfig(
    int DevelopmentDays,      // development duration in business days
    int QedGapDays,           // business days from End Dev to QED Deploy
    int QaCutoffGapDays,      // business days from End Dev to QA Cutoff  
    int RegressionGapDays,    // business days from QED to Start Regression
    int RegressionDays);      // duration of Regression in business days