# CI 与发布流程

## 持续集成（CI）

工作流：`.github/workflows/ci.yml`，名称 **CI**。

| 触发 | 内容 |
|---|---|
| 任意分支的 push、任意 Pull Request（只改 `*.md` 时跳过） | ServiceLib 测试 + WebAPI 测试；Avalonia、WPF、WebAPI 三个项目的 Release 编译 |
| 发布前（被 `release all platforms` 调用） | 同上，作为发布闸门：测试或编译失败时不会创建 Release |

- 测试结果（`.trx`）作为 `test-results` 制品上传；同仓库的 PR 会在检查结果中汇总失败的测试。
- 配置生成的快照测试（`ServiceLib.Tests/CoreConfig/Unified/Snapshots`）不一致时，会把实际输出 `*.actual` 作为 `snapshot-diffs` 制品上传。确认改动符合预期后，本地用 `UPDATE_SNAPSHOTS=1` 重新生成快照并提交。
- 建议在仓库 **Settings → Branches** 给 `master` 加分支保护，把 `CI / Tests (ServiceLib + WebAPI)` 和三个 `CI / Build (...)` 设为必需检查。

本地运行同样的测试：

```bash
dotnet test --project ./v2rayN/ServiceLib.Tests -c Release
TUNIT_MAX_PARALLEL_TESTS=1 dotnet test --project ./v2rayN/v2rayN.WebAPI/Tests -c Release
```

另外，`master` 的每次 push 仍会运行各平台的 `release ...` 工作流，只编译、不发布，产物可在对应运行的 Artifacts 中下载，可作为开发版自测。

## 发布渠道

发布渠道由 tag 名决定：

| tag | 渠道 | 结果 |
|---|---|---|
| `X.Y.Z-alpha.N` / `X.Y.Z-beta.N` / `X.Y.Z-rc.N` | 测试版 | GitHub 预发布（Pre-release），不会成为 Latest；不发布 WebAPI 包 |
| `X.Y.Z` | 正式版 | 先作为预发布构建，全部平台上传完成并验证后，再用 `promote release` 发布为 Latest |

tag 必须与 `v2rayN/Directory.Build.props` 中的 `<Version>` 一致（测试版只比较 `X.Y.Z` 部分），否则发布在第一步就会失败。

## 发布测试版

1. 把 `v2rayN/Directory.Build.props` 的 `<Version>` 改为目标版本（例如 `7.26.0`），提交并推送。
2. 打 tag 并推送：
   ```bash
   git tag 7.26.0-beta.1
   git push origin 7.26.0-beta.1
   ```
   也可以在 **Actions → release all platforms → Run workflow** 中填写 `7.26.0-beta.1`。此时 tag 会创建在所选分支的最新提交上。
3. 工作流依次执行：校验 tag 与版本号 → 运行 CI → 创建预发布 → 分别启动 Windows、Windows 桌面版、Windows x86、Linux、macOS 的构建。各平台构建完成后，会把产物上传到这个预发布。

## 发布正式版

1. 确认 `<Version>` 为 `X.Y.Z`，打 tag `X.Y.Z` 并推送（或在 Actions 中手动运行），流程与测试版相同，另外还会构建并上传 WebAPI 包。
2. 等所有平台的运行都成功后，下载并验证预发布中的产物。
3. 运行 **Actions → promote release**，填入 `X.Y.Z`。它会检查：
   - 这是正式版 tag，并且对应的 Release 存在；
   - 这个 tag 下每个平台工作流的最新一次运行都已成功完成；
   - `required_assets` 中列出的产物都已上传。

   全部通过后，把它改为正式发布并标记为 Latest。只有在有意发布不完整版本时，才修改 `required_assets`。

## 重新构建某个平台

在 Actions 中打开对应的平台工作流（例如 `release Linux`），选择该 tag 作为 ref，`release_tag` 填同一个 tag 后运行。上传会覆盖同名产物。`promote release` 只看每个工作流的最新一次运行。

上传步骤会把 Release 标记为预发布，所以最好在 promote 之前完成重新构建；如果在 promote 之后重新上传，请再运行一次 `promote release`。

## 密钥（Secrets）

| 名称 | 是否必需 | 用途 |
|---|---|---|
| `GPG_PRIVATE_KEY` | 可选 | 为发布文件生成 `.sig` 签名并发布公钥。未设置时上传未签名的文件，并在运行中给出提示 |
| `PT_WINGET` | 仅上游 | WinGet 提交，只在 `2dust/v2rayN` 仓库运行 |

`GITHUB_TOKEN` 由 Actions 自动提供，无需配置。
