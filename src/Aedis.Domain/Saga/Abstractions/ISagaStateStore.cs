namespace Aedis.Domain.Saga.Abstractions;




public interface ISagaStateStore
{
    
    
    
    Task SaveSagaStartAsync(Guid sagaId, string sagaType, CancellationToken ct = default);

    
    
    
    Task SaveStepCompletionAsync(Guid sagaId, string stepName, object? data, CancellationToken ct = default);

    
    
    
    Task<SagaState?> GetSagaStateAsync(Guid sagaId, CancellationToken ct = default);

    
    
    
    Task MarkAsCompletedAsync(Guid sagaId, CancellationToken ct = default);

    
    
    
    Task MarkAsFailedAsync(Guid sagaId, string errorMessage, CancellationToken ct = default);

    
    
    
    Task MarkAsCompensatedAsync(Guid sagaId, int stepsCompensated, CancellationToken ct = default);
}




public class SagaState
{
    public Guid SagaId { get; set; }
    public string SagaType { get; set; } = string.Empty;
    public string Status { get; set; } = "Running"; 
    public List<SagaStepState> Steps { get; set; } = new();
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
}




public class SagaStepState
{
    public string StepName { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending"; 
    public DateTimeOffset ExecutedAt { get; set; }
    public string? Data { get; set; } 
    public string? ErrorMessage { get; set; }
}