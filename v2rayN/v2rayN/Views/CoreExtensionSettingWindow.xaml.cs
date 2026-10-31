namespace v2rayN.Views;

public partial class CoreExtensionSettingWindow
{
    public CoreExtensionSettingWindow()
    {
        InitializeComponent();

        cmbMihomoUnifiedMode.ItemsSource = CoreExtensionSettingViewModel.MihomoUnifiedModes;
        cmbXrayDnsQueryStrategy.ItemsSource = CoreExtensionSettingViewModel.XrayQueryStrategies;
        cmbPreviewCore.ItemsSource = CoreExtensionSettingViewModel.PreviewCores;

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

            this.Bind(ViewModel, vm => vm.RuleSets, v => v.txtRuleSets.Text).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.CapabilityRows, v => v.lstCapabilityMatrix.ItemsSource).DisposeWith(disposables);

            this.Bind(ViewModel, vm => vm.PreviewCore, v => v.cmbPreviewCore.SelectedItem).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.PreviewText, v => v.txtPreview.Text).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.PreviewCmd, v => v.btnPreview).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.SaveCmd, v => v.btnSave).DisposeWith(disposables);
        });
        WindowsUtils.SetDarkBorder(this, AppManager.Instance.Config.UiItem.CurrentTheme);
    }
}
