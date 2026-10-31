namespace v2rayN.Views;

public partial class DNSSettingWindow
{
    public DNSSettingWindow()
    {
        InitializeComponent();

        this.WhenActivated(disposables =>
        {
            dnsPanel.ViewModel = ViewModel;
            this.BindCommand(ViewModel, vm => vm.SaveCmd, v => v.btnSave).DisposeWith(disposables);
        });
        WindowsUtils.SetDarkBorder(this, AppManager.Instance.Config.UiItem.CurrentTheme);
    }
}
