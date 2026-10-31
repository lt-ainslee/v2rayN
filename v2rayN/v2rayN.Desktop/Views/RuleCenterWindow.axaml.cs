using v2rayN.Desktop.Base;

namespace v2rayN.Desktop.Views;

public partial class RuleCenterWindow : WindowBase<RuleCenterViewModel>
{
    public RuleCenterWindow()
    {
        InitializeComponent();

        btnCancel.Click += (s, e) => Close();
        cmbRuleType.ItemsSource = RuleEditorViewModel.RuleTypes;
        cmbNetwork.ItemsSource = RuleEditorViewModel.Networks;
        cmbPreviewCore.ItemsSource = RuleCenterViewModel.PreviewCores;

        // The embedded panels edit the DNS and core extension parts of the rule center.
        this.WhenAnyValue(x => x.ViewModel).Subscribe(vm =>
        {
            dnsPanel.ViewModel = vm?.Dns;
            extensionPanel.ViewModel = vm?.Extensions;
        });
    }
}
