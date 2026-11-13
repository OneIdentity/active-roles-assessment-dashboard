using System.Text;
using System.Xml;

namespace ActiveRolesDashboard.Services.Reporting;

/// <summary>
/// Removes characters that are not legal in XML 1.0 (e.g. U+0001) so directory data containing
/// stray control characters cannot break the Open XML (Word/Excel) exporters.
/// </summary>
internal static class XmlText
{
    public static string Sanitize(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var firstInvalid = IndexOfInvalid(text);
        if (firstInvalid < 0)
            return text;

        var sb = new StringBuilder(text.Length);
        sb.Append(text, 0, firstInvalid);
        for (var i = firstInvalid; i < text.Length; i++)
        {
            var c = text[i];
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                sb.Append(c).Append(text[++i]);
            }
            else if (XmlConvert.IsXmlChar(c))
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    private static int IndexOfInvalid(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                i++;
                continue;
            }
            if (!XmlConvert.IsXmlChar(c))
                return i;
        }
        return -1;
    }
}
