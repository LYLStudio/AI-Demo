using System;
using System.Text;
using System.Text.RegularExpressions;

public static class Base64Restorer
{
    // 基本正則：前後各固定字串、中央是合法Base64
    private const string Base64Pattern = @"base64\|([^|]+?)\|base64";

    // 預編譯以提升效能
    private static readonly Regex _base64Regex = new Regex(Base64Pattern, RegexOptions.Compiled);

    /// <summary>
    /// 將字串中所有符合「base64|…|base64」的子段落解碼為 UTF‑8。
    /// 其它部分保持不變；如果 Base‑64 無法正確解析，則保留原始文字。
    /// </summary>
    public static string Restore(string input)
    {
        var result = input;
        result = _base64Regex.Replace(input, match =>
        {
            try
            {
                // 取得捕獲組中的 Base‑64 字串
                string base64Part = match.Groups[1].Value;

                // 解碼成 byte[]，再轉成 UTF‑8 字串
                byte[] bytes = Convert.FromBase64String(base64Part);
                string decoded = Encoding.UTF8.GetString(bytes);

                return decoded; // 返回解碼後的字串
            }
            catch
            {
                // 如果解碼失敗，保持原本的內容不變
                return match.Value;
            }
        });

        return result;
    }
}
