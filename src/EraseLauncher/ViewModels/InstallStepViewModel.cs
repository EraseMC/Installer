namespace EraseLauncher.ViewModels;

public enum StepStatus
{
    Pending,
    Active,
    Done,
    Failed
}

public sealed class InstallStepViewModel(string title) : ObservableObject
{
    private StepStatus _status;

    public string Title { get; } = title;

    public StepStatus Status
    {
        get => _status;
        set
        {
            if (SetProperty(ref _status, value))
            {
                OnPropertyChanged(nameof(IsActive));
                OnPropertyChanged(nameof(IsDone));
                OnPropertyChanged(nameof(IsFailed));
            }
        }
    }

    public bool IsActive => Status == StepStatus.Active;
    public bool IsDone => Status == StepStatus.Done;
    public bool IsFailed => Status == StepStatus.Failed;
}
