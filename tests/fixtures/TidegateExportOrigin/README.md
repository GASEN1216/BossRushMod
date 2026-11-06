# 潮闸镇导出服务回归

运行 `python tools/run_runtime_regressions.py --filter TidegateExportOrigin`。夹具在临时目录启动真实的 `tools/tidegate_dev_server.py` 处理器，通过回环 HTTP 请求验证同源页面能写二进制文件；异源、缺失来源、伪造 Host 和目录越界均被拒，已有文件逐字节不变。页面和导出文件用临时样本，不读作者实际资源，也不要求浏览器或游戏进程。
