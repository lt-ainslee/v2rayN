namespace v2rayN.Views;

public partial class CoreExtensionSettingWindow
{
    public CoreExtensionSettingWindow()
    {
        InitializeComponent();

        cmbMihomoUnifiedMode.ItemsSource = CoreExtensionSettingViewModel.MihomoUnifiedModes;
        cmbXrayDnsQueryStrategy.ItemsSource = CoreExtensionSettingViewModel.XrayQueryStrategies;
        cmbMihomoDnsCacheAlgorithm.ItemsSource = CoreExtensionSettingViewModel.MihomoCacheAlgorithms;
        cmbMihomoFindProcessMode.ItemsSource = CoreExtensionSettingViewModel.MihomoFindProcessModes;
        cmbMihomoUnifiedDelay.ItemsSource = CoreExtensionSettingViewModel.TriStateItems;
        cmbMihomoTcpConcurrent.ItemsSource = CoreExtensionSettingViewModel.TriStateItems;
        cmbMihomoSniffer.ItemsSource = CoreExtensionSettingViewModel.TriStateItems;
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
            this.Bind(ViewModel, vm => vm.MihomoDnsCacheAlgorithm, v => v.cmbMihomoDnsCacheAlgorithm.SelectedItem).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.MihomoUnifiedDelay, v => v.cmbMihomoUnifiedDelay.SelectedItem).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.MihomoTcpConcurrent, v => v.cmbMihomoTcpConcurrent.SelectedItem).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.MihomoFindProcessMode, v => v.cmbMihomoFindProcessMode.SelectedItem).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.MihomoKeepAliveInterval, v => v.txtMihomoKeepAliveInterval.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.MihomoSniffer, v => v.cmbMihomoSniffer.SelectedItem).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.IsMihomoUnifiedEnabled, v => v.txtMihomoProxyGroupName.IsEnabled).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.IsMihomoUnifiedEnabled, v => v.cmbMihomoDnsCacheAlgorithm.IsEnabled).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.IsMihomoUnifiedEnabled, v => v.togMihomoRespectRules.IsEnabled).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.IsMihomoUnifiedEnabled, v => v.togMihomoPreferH3.IsEnabled).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.IsMihomoUnifiedEnabled, v => v.txtMihomoFakeIpFilter.IsEnabled).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.IsMihomoUnifiedEnabled, v => v.txtMihomoExtraProxyGroups.IsEnabled).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.IsMihomoUnifiedEnabled, v => v.txtMihomoExtraRules.IsEnabled).DisposeWith(disposables);

            this.Bind(ViewModel, vm => vm.SingboxDnsClientSubnet, v => v.txtSingboxDnsClientSubnet.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.SingboxDnsIndependentCache, v => v.togSingboxDnsIndependentCache.IsChecked).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.SingboxDnsDisableExpire, v => v.togSingboxDnsDisableExpire.IsChecked).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.SingboxDnsCacheCapacity, v => v.txtSingboxDnsCacheCapacity.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.SingboxDnsReverseMapping, v => v.togSingboxDnsReverseMapping.IsChecked).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.SingboxRouteFindProcess, v => v.togSingboxRouteFindProcess.IsChecked).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.SingboxRouteDefaultMark, v => v.txtSingboxRouteDefaultMark.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.SingboxCacheFileStoreRdrc, v => v.togSingboxCacheFileStoreRdrc.IsChecked).DisposeWith(disposables);

            this.Bind(ViewModel, vm => vm.XrayDnsQueryStrategy, v => v.cmbXrayDnsQueryStrategy.SelectedItem).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.XrayDnsDisableCache, v => v.togXrayDnsDisableCache.IsChecked).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.XrayDnsDisableFallback, v => v.togXrayDnsDisableFallback.IsChecked).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.XrayDnsDisableFallbackIfMatch, v => v.togXrayDnsDisableFallbackIfMatch.IsChecked).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.XrayDnsClientIp, v => v.txtXrayDnsClientIp.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.XrayDnsServeExpiredTtl, v => v.txtXrayDnsServeExpiredTtl.Text).DisposeWith(disposables);

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
