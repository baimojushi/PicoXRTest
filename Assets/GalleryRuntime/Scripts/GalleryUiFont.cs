using UnityEngine;

namespace GalleryRuntime
{
    /// <summary>
    /// Resolves a font that can actually render the hotspot UI text.
    ///
    /// Unity's builtin font has no CJK glyphs, so Chinese titles would come out as
    /// blank boxes. Prefer an inspector-assigned font (recommended for Android/Pico
    /// builds), otherwise try a CJK-capable OS font, and only then the builtin font.
    /// </summary>
    internal static class GalleryUiFont
    {
        private static readonly string[] CandidateOsFonts =
        {
            "Microsoft YaHei UI", "Microsoft YaHei", "SimHei", "Microsoft JhengHei",
            "Noto Sans CJK SC", "Source Han Sans SC", "PingFang SC", "Arial Unicode MS"
        };

        private static Font _resolved;
        private static bool _warned;

        public static Font Resolve(Font preferred)
        {
            if (preferred != null)
                return preferred;

            if (_resolved != null)
                return _resolved;

            foreach (var name in CandidateOsFonts)
            {
                try
                {
                    var font = Font.CreateDynamicFontFromOSFont(name, 32);
                    if (font != null)
                    {
                        _resolved = font;
                        break;
                    }
                }
                catch
                {
                    // 该字体不存在或其他平台限制，继续尝试下一个
                }
            }

            if (_resolved == null)
            {
                try
                {
                    _resolved = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                }
                catch
                {
                    // 少数情况下内置字体也不可用
                }
            }

            if (_resolved == null && !_warned)
            {
                _warned = true;
                Debug.LogWarning("[GalleryUiFont] 未找到可用字体，热点 UI 文字可能不显示；请在信息卡/准星上指定 Font 资源");
            }

            return _resolved;
        }
    }
}
