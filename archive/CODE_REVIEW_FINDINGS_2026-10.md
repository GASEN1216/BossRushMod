# 2026-10 已闭环审查归档

以下节从根台账按原字节归档，保留原有证据边界。

## 2026-10-13 推送后跨平台门禁修复（Fixed，COMPAT / SAFE，L2）

- CR-2026-10-13-012，P2 / COMPAT：天空岛导航、道路与摆放审计将 Windows CRLF 原字节 SHA 带入公开仓库，Linux LF 签出误报布局/证据过期；生产 `apply_navigation` 同样误拒绝。复用 `sky_island_road_layout` 的纯函数，新签名只统一 CRLF→LF，读取兼容既有原字节、LF 和 CRLF。真实坐标、字段、空白、BOM 与字符串变化仍拒绝；不重写现有地图或审计 JSON。三项旧码在真实 Ubuntu LF 环境均复现，修后两种换行及生产拒绝路径完成反向验证。
- CR-2026-10-13-013，P2 / SAFE：植物几何测试在公开源码 CI 强制读取私有 OBJ/MTL，干净签出必失败。源码模式改用公开摆放包络驱动真实细化算法，明确报告 `PARTIAL`；完整模式保留真实模型、原半径与拓扑检查，缺原件仍失败。公开 12 类 / 36 包络、本地 12 个真实模型及 4 类破坏/恢复检查通过，证据 `Build/ci-flora-20261013/probe-687b5c9a/evidence.json`。

首推提交 `15439905` 的远端 Guards 失败由以上两类原因导致，共 4 项；Wiki 构建与部署通过。修后公开 LF 副本全量 736 PASS / 9 PARTIAL / 0 FAIL，日志 `Build/commit-20261013/source-only-final-outside-build.log`；指纹证据 `Build/ci-fingerprint-20261013/`。本次只改离线工具、测试与文档，1196 个已编译生产源哈希一致；不据门禁修复宣称实机或性能验证完成。

