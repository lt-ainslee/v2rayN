using v2rayN.Desktop.Base;

namespace v2rayN.Desktop.Views;

public partial class DNSSettingWindow : WindowBase<DNSSettingViewModel>
{
    public DNSSettingWindow()
    {
        InitializeComponent();

        Loaded += Window_Loaded;
        btnCancel.Click += (s, e) => Close();

        this.WhenActivated(disposables =>
        {
            dnsPanel.ViewModel = ViewModel;
            this.BindCommand(ViewModel, vm => vm.SaveCmd, v => v.btnSave).DisposeWith(disposables);
        });
    }

    private void Window_Loaded(object? sender, RoutedEventArgs e)
    {
        btnCancel.Focus();
    }
}
