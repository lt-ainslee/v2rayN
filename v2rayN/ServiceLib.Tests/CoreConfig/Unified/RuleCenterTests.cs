using ReactiveUI.Builder;
using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Extensions;
using ServiceLib.ViewModels;

namespace ServiceLib.Tests.CoreConfig.Unified;

public class RuleCenterTests
{
    private static readonly Lazy<bool> _reactiveUi = new(() =>
    {
        // The apps build ReactiveUI at startup; view models need it for WhenAnyValue.
        RxAppBuilder.CreateReactiveUIBuilder().WithCoreServices().BuildApp();
        return true;
    });

    [Before(Class)]
    public static void InitReactiveUi() => _ = _reactiveUi.Value;

    private static readonly Dictionary<ECoreType, HashSet<string>> NoRuleSets = new()
    {
        [ECoreType.Xray] = [],
        [ECoreType.sing_box] = [],
        [ECoreType.mihomo] = [],
    };

    [Test]
    [Arguments(ERuleConditionField.Domain, "domain:google.com")]
    [Arguments(ERuleConditionField.Domain, "full:a.example.com")]
    [Arguments(ERuleConditionField.Domain, "keyword:ads")]
    [Arguments(ERuleConditionField.Domain, "regexp:^a<COMMA>b$")]
    [Arguments(ERuleConditionField.Domain, "geosite:cn")]
    [Arguments(ERuleConditionField.Domain, "dotless:intranet")]
    [Arguments(ERuleConditionField.Domain, "twitter")]
    [Arguments(ERuleConditionField.Domain, "ext:my.dat:tag")]
    [Arguments(ERuleConditionField.Domain, "ruleset:ads")]
    [Arguments(ERuleConditionField.Ip, "10.0.0.0/8")]
    [Arguments(ERuleConditionField.Ip, "!geoip:cn")]
    [Arguments(ERuleConditionField.Ip, "geoip:private")]
    [Arguments(ERuleConditionField.Ip, "ext-ip:my.dat:tag")]
    [Arguments(ERuleConditionField.Ip, "ruleset:streaming-ip")]
    [Arguments(ERuleConditionField.Process, "chrome.exe")]
    [Arguments(ERuleConditionField.Process, "/usr/bin/curl")]
    public async Task ConditionKinds_ShouldRoundTripStoredEntries(ERuleConditionField field, string raw)
    {
        raw = raw.Replace("<COMMA>", Global.RoutingRuleComma);

        var condition = RuleConditionKinds.FromRaw(field, raw)!.Value;

        await RuleConditionKinds.FieldOf(condition.Kind).Should().BeEqualTo(field);
        await RuleConditionKinds.ToRaw(condition.Kind, condition.Value, condition.Negated).Should().BeEqualTo(raw);
    }

    [Test]
    [Arguments("google.com", ERuleConditionKind.DomainSuffix)]
    [Arguments("geosite:netflix", ERuleConditionKind.GeoSite)]
    [Arguments("regexp:^a/b$", ERuleConditionKind.DomainRegex)]
    [Arguments("1.1.1.1", ERuleConditionKind.IpCidr)]
    [Arguments("fd00::/8", ERuleConditionKind.IpCidr)]
    [Arguments("!geoip:cn", ERuleConditionKind.GeoIp)]
    [Arguments("chrome.exe", ERuleConditionKind.ProcessName)]
    [Arguments("/usr/bin/wget", ERuleConditionKind.ProcessPath)]
    public async Task Guess_ShouldPickTheConditionType(string text, ERuleConditionKind expected)
    {
        await RuleConditionKinds.Guess(text)!.Value.Kind.Should().BeEqualTo(expected);
    }

    [Test]
    public async Task Compatibility_ShouldMarkSkippedPartialApproximateAndFull()
    {
        string Marks(RulesItem rule) => string.Join(" ", RuleCompatibility.Evaluate(rule, NoRuleSets).Select(s => s.Mark));

        await Marks(new RulesItem { OutboundTag = Global.ProxyTag, Domain = ["domain:a.com"] }).Should().BeEqualTo("✓ ✓ ✓");
        // protocol: mihomo cannot express the constraint, so the whole rule is skipped there
        await Marks(new RulesItem { OutboundTag = Global.DirectTag, Protocol = ["bittorrent"] }).Should().BeEqualTo("✓ ✓ ✗");
        // ext: is only understood by Xray; the other value of the rule survives elsewhere
        await Marks(new RulesItem { OutboundTag = Global.DirectTag, Domain = ["ext:a.dat:x", "domain:b.com"] }).Should().BeEqualTo("✓ ! !");
        // dotless: is approximated by sing-box
        await Marks(new RulesItem { OutboundTag = Global.DirectTag, Domain = ["dotless:intra"] }).Should().BeEqualTo("✓ ≈ ✓");
        // a comma inside a regex only fails while translating for mihomo
        await Marks(new RulesItem { OutboundTag = Global.DirectTag, Domain = ["regexp:^a{1,3}$"] }).Should().BeEqualTo("✓ ✓ ✗");
    }

    [Test]
    public async Task InputValidator_ShouldReportTheFirstProblem()
    {
        await RuleInputValidator.Validate(new RulesItem { Domain = ["domain:a.com"] }).Should().BeNull();
        await RuleInputValidator.Validate(new RulesItem()).Should().NotBeNull();
        await RuleInputValidator.Validate(new RulesItem { SourceIp = ["192.168.1"] })!.Should().Contain("192.168.1");
        await RuleInputValidator.Validate(new RulesItem { Domain = ["domain:a.com"], CoreExt = new RuleCoreExtension { XrayJson = "[1]" } })!.Should().Contain("Xray");
    }

    [Test]
    public async Task Editor_ShouldWriteStructuredConditionsBackIntoTheRule()
    {
        var rule = new RulesItem { Id = "r", OutboundTag = Global.ProxyTag, Enabled = true, Domain = ["geosite:google"], Protocol = ["tls"] };
        var item = new RuleListItemViewModel(rule);
        item.Refresh(NoRuleSets);
        var changes = 0;
        var editor = new RuleEditorViewModel(() => changes++);

        editor.Load(item);
        await editor.Conditions.Count.Should().BeEqualTo(1);
        await editor.Protocols.Single(p => p.Name == "tls").IsChecked.Should().BeTrue();

        editor.BulkText = "1.1.1.1, chrome.exe\n!geoip:cn";
        editor.AddBulkCmd.Execute().Subscribe(_ => { });
        editor.Conditions[0].KindName = RuleConditionKinds.DisplayName(ERuleConditionKind.DomainSuffix);
        editor.Conditions[0].Value = "google.com";
        editor.OutboundTag = Global.DirectTag;
        editor.Protocols.Single(p => p.Name == "tls").IsChecked = false;
        editor.SourcePort = "5000";

        await rule.Domain!.Single().Should().BeEqualTo("domain:google.com");
        await string.Join(",", rule.Ip!).Should().BeEqualTo("1.1.1.1,!geoip:cn");
        await rule.Process!.Single().Should().BeEqualTo("chrome.exe");
        await rule.OutboundTag.Should().BeEqualTo(Global.DirectTag);
        await rule.Protocol.Should().BeNull();
        await rule.SourcePort.Should().BeEqualTo("5000");
        await (changes > 0).Should().BeTrue();

        editor.Conditions[1].RemoveCmd.Execute().Subscribe(_ => { });
        await rule.Ip!.Single().Should().BeEqualTo("!geoip:cn");
    }

    [Test]
    public async Task Editor_LoadingARule_ShouldNotModifyIt()
    {
        var rule = new RulesItem { Id = "r", OutboundTag = "my-group", Domain = ["#comment", "domain:a.com"], Port = "443" };
        var json = JsonUtils.Serialize(rule);
        var item = new RuleListItemViewModel(rule);
        item.Refresh(NoRuleSets);

        new RuleEditorViewModel(() => { }).Load(item);

        await JsonUtils.Serialize(rule).Should().BeEqualTo(json);
    }
}
