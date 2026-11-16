# 扩展虚拟展厅业务联动
新增与基础展厅一致的作品目录连接组件及热点补全组件。
`GalleryHotspot` → `GalleryJourneyBridge` → `VideoPusher` 数据通道 → `journey-client.js` → 网页业务入口。
Unity 配置位于 `Assets/StreamingAssets/Gallery/Config/journey.json`，默认 remote 模式。填写实际展览编号与业务根地址。
浏览器配置 `journey-client-config.json` 必须使用同一业务域名；页面服务器已许可访问此文件和新脚本。
单独打开播放页时，通过明确的作品链接打开介绍；嵌入业务页时采用限定来源的跨窗口消息。离开展厅暂停控制，返回时恢复。
`node --test tests_journey_client.test.js` 检查事件与安全跳转逻辑。
本次没有运行 Unity、头显、公网实时传输或多会话验收。原信令和推流服务部署流程保留。
详细部署、接口与测试范围见总交付包 `README_DELIVERY_ZH.md` 和 `integration/`。
