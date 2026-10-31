namespace v2rayN.Views;

public partial class RuleCenterWindow
{
    public RuleCenterWindow()
    {
        InitializeComponent();

        cmbRuleType.ItemsSource = RuleEditorViewModel.RuleTypes;
        cmbNetwork.ItemsSource = RuleEditorViewModel.Networks;
        cmbPreviewCore.ItemsSource = RuleCenterViewModel.PreviewCores;

        // The page binds in XAML; the embedded panels edit the DNS and core extension parts.
        this.WhenAnyValue(x => x.ViewModel).Subscribe(vm =>
        {
            DataContext = vm;
            dnsPanel.ViewModel = vm?.Dns;
            extensionPanel.ViewModel = vm?.Extensions;
        });
        WindowsUtils.SetDarkBorder(this, AppManager.Instance.Config.UiItem.CurrentTheme);
    }
}
