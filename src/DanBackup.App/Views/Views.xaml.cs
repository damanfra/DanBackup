using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using DanBackup.App.ViewModels;

namespace DanBackup.App.Views;

public partial class BackupView : UserControl
{
    public BackupView() => InitializeComponent();
}

public partial class VerifyView : UserControl
{
    public VerifyView() => InitializeComponent();
}

public partial class RestoreView : UserControl
{
    public RestoreView() => InitializeComponent();
}

public partial class SettingsView : UserControl
{
    public SettingsView() => InitializeComponent();
}

public partial class PasswordField : UserControl
{
    public PasswordField()
    {
        InitializeComponent();
        // Preenche com a senha lembrada quando o ViewModel é conectado, e acompanha mudanças feitas por ele.
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is OperationViewModelBase old) old.PropertyChanged -= OnViewModelChanged;
            if (e.NewValue is OperationViewModelBase vm)
            {
                vm.PropertyChanged += OnViewModelChanged;
                SyncFrom(vm);
            }
        };
    }

    private void OnViewModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OperationViewModelBase.Password) && sender is OperationViewModelBase vm)
            SyncFrom(vm);
    }

    private void SyncFrom(OperationViewModelBase vm)
    {
        if (Box.Password != vm.Password) Box.Password = vm.Password;
    }

    private void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is OperationViewModelBase vm) vm.Password = Box.Password;
    }
}

public partial class LogView : UserControl
{
    public LogView()
    {
        InitializeComponent();
        // Rola automaticamente para a última linha do log.
        ((INotifyCollectionChanged)List.Items).CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Add && List.Items.Count > 0)
                List.ScrollIntoView(List.Items[^1]);
        };
    }
}
