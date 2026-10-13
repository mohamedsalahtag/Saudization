using System.Text;

namespace Saudization.Web.Services;

/// <summary>One matching rule for every name, code and ID coming from Excel, so the same text written slightly
/// differently (Arabic letter variants, diacritics, tatweel, invisible marks, Arabic-Indic digits, dash variants,
/// spacing around hyphens) is treated as the same value everywhere.</summary>
public static class TextNormalizer
{
    /// <summary>Matching key for names, headers, sheet names and facility codes.</summary>
    public static string Key(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            switch (ch)
            {
                // invisible: zero-width, direction marks, BOM
                case >= '​' and <= '‏': case >= '‪' and <= '‮': case '⁦': case '⁧':
                case '⁨': case '⁩': case '﻿':
                // tatweel and Arabic diacritics (tashkeel, superscript alef)
                case 'ـ': case >= 'ً' and <= 'ْ': case 'ٰ':
                    continue;
                case 'أ': case 'إ': case 'آ': case 'ٱ': sb.Append('ا'); break;
                case 'ى': case 'ی': sb.Append('ي'); break;           // alef maqsura, Persian yeh
                case 'ک': sb.Append('ك'); break;                      // Persian kaf
                case 'ة': sb.Append('ه'); break;
                case ' ': case '\t': case '\r': case '\n': sb.Append(' '); break;
                case '‐': case '‑': case '‒': case '–': case '—': case '−': sb.Append('-'); break;
                default:
                    if (ch is >= '٠' and <= '٩') sb.Append((char)('0' + (ch - '٠')));          // Arabic-Indic digits
                    else if (ch is >= '۰' and <= '۹') sb.Append((char)('0' + (ch - '۰')));     // Persian digits
                    else sb.Append(char.ToLowerInvariant(ch));
                    break;
            }
        }
        var r = System.Text.RegularExpressions.Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
        return System.Text.RegularExpressions.Regex.Replace(r, @"\s*-\s*", "-");   // "4 - 9043" == "4-9043"
    }

    /// <summary>Matching key for Iqama / national ID numbers: digits only (any digit script). Falls back to the
    /// trimmed text without spaces when the value has no digits at all.</summary>
    public static string? IdKey(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        var digits = new StringBuilder();
        foreach (var ch in id)
            if (char.IsDigit(ch)) digits.Append((char)('0' + (int)char.GetNumericValue(ch)));
        if (digits.Length > 0) return digits.ToString();
        var s = Key(id).Replace(" ", "");
        return s.Length == 0 ? null : s;
    }

    /// <summary>True for a Saudi nationality name in Arabic or English ("سعودي", "سعودية", "Saudi", "Saudi Arabian"),
    /// false for "غير سعودي" / "non-Saudi".</summary>
    public static bool IsSaudiNationality(string? name)
    {
        var k = Key(name);
        if (k.Length == 0) return false;
        bool negated = k.Contains("غير") || k.Contains("non");
        return !negated && (k.Contains("سعود") || k.Contains("saudi") || k == "ksa");
    }
}
