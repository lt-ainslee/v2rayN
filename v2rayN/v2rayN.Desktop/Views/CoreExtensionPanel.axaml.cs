namespace v2rayN.Desktop.Views;

/// <summary>
/// The per-core options of the core extensions, used by the unified rule settings.
/// </summary>
public partial class CoreExtensionPanel : ReactiveUserControl<CoreExtensionSettingViewModel>
{
    public CoreExtensionPanel()
    {
        InitializeComponent();

        cmbMihomoUnifiedMode.ItemsSource = CoreExtensionSettingViewModel.MihomoUnifiedModes;
        cmbXrayDnsQueryStrategy.ItemsSource = CoreExtensionSettingViewModel.XrayQueryStrategies;

        this.WhenActivated(disposables =>
        {
            this.Bind(ViewModel, vm => vm.MihomoUnifiedMode, v => v.cmbMihomoUnifiedMode.SelectedItem).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.MihomoProxyGroupName, v => v.txtMihomoProxyGroupName.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.MihomoRespectRules, v => v.togMihomoRespectRules.IsChecked).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.MihomoPreferH3, v => v.togMihomoPreferH3.IsChecked).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.MihomoFakeIpFilter, v => v.txtMihomoFakeIpFilter.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.MihomoExtraProxyGroups, v => v.txtMihomoExtraProxyGroups.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.MihomoExtraRules, v => v.txtMihomoExtraRules.Text).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.IsMihomoUnifiedEnabled, v => v.txtMihomoProxyGroupName.IsEnabled).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.IsMihomoUnifiedEnabled, v => v.togMihomoRespectRules.IsEnabled).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.IsMihomoUnifiedEnabled, v => v.togMihomoPreferH3.IsEnabled).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.IsMihomoUnifiedEnabled, v => v.txtMihomoFakeIpFilter.IsEnabled).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.IsMihomoUnifiedEnabled, v => v.txtMihomoExtraProxyGroups.IsEnabled).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.IsMihomoUnifiedEnabled, v => v.txtMihomoExtraRules.IsEnabled).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.SingboxDnsClientSubnet, v => v.txtSingboxDnsClientSubnet.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.SingboxDnsIndependentCache, v => v.togSingboxDnsIndependentCache.IsChecked).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.XrayDnsQueryStrategy, v => v.cmbXrayDnsQueryStrategy.SelectedItem).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.XrayDnsDisableCache, v => v.togXrayDnsDisableCache.IsChecked).DisposeWith(disposables);
        });
    }
}
