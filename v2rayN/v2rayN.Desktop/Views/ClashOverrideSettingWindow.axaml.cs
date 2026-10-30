using v2rayN.Desktop.Base;

namespace v2rayN.Desktop.Views;

public partial class ClashOverrideSettingWindow : WindowBase<ClashOverrideSettingViewModel>
{
    public ClashOverrideSettingWindow()
    {
        InitializeComponent();

        Loaded += Window_Loaded;
        btnCancel.Click += (s, e) => Close();

        cmbEnhancedMode.ItemsSource = Global.ClashEnhancedModes;
        cmbFakeIPRange.ItemsSource = Global.FakeIPRanges;

        this.WhenActivated(disposables =>
        {
            this.Bind(ViewModel, vm => vm.EnableDnsOverride, v => v.togEnableDnsOverride.IsChecked).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.EnhancedMode, v => v.cmbEnhancedMode.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.FakeIPRange, v => v.cmbFakeIPRange.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.RespectRules, v => v.togRespectRules.IsChecked).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.DefaultNameserver, v => v.txtDefaultNameserver.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.Nameserver, v => v.txtNameserver.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.Fallback, v => v.txtFallback.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.FallbackFilterGeoIP, v => v.togFallbackFilterGeoIP.IsChecked).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.FallbackFilterGeoIPCode, v => v.txtFallbackFilterGeoIPCode.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.ProxyServerNameserver, v => v.txtProxyServerNameserver.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.DirectNameserver, v => v.txtDirectNameserver.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.FakeIPFilter, v => v.txtFakeIPFilter.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.NameserverPolicy, v => v.txtNameserverPolicy.Text).DisposeWith(disposables);

            this.Bind(ViewModel, vm => vm.EnablePolicyOverride, v => v.togEnablePolicyOverride.IsChecked).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.ReplaceRules, v => v.togReplaceRules.IsChecked).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.ProxyGroups, v => v.txtProxyGroups.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.RuleProviders, v => v.txtRuleProviders.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.Rules, v => v.txtRules.Text).DisposeWith(disposables);

            this.BindCommand(ViewModel, vm => vm.SaveCmd, v => v.btnSave).DisposeWith(disposables);
        });
    }

    private void Window_Loaded(object? sender, RoutedEventArgs e)
    {
        btnCancel.Focus();
    }
}
