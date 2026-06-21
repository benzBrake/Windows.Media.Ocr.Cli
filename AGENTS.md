# Repository Instructions

如果运行 Python 指令，请使用 uv。

## Release

当用户要求发布新版本时：

1. 确认 `git status --short` 没有未提交变更。
2. 使用 `.\scripts\release.ps1` 创建并推送 `vX.Y.Z` tag。
3. 如果用户没有指定版本号，默认用 `.\scripts\release.ps1` 基于最新 `vX.Y.Z` tag 递增 patch 版本。
4. 如果用户指定版本号，使用 `.\scripts\release.ps1 -Version X.Y.Z`。
5. 不手动创建 GitHub Release；推送 tag 后由 GitHub Actions 自动构建并创建 Release。
