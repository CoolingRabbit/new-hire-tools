// ============================================================================
// 通用密码生成算法（工具一 exe 与一致性测试程序共用本文件）
//
// 注意：本实现与 Map-NAS-Domain.ps1 中的 PowerShell 实现严格一致，
//       修改任何一处必须同步修改另一处，否则两个工具的初始密码将不一致。
//
// 算法：
//   1. 拼接字符串：员工号_2026（如 J10065_2026）
//   2. HMAC-SHA256(Seed, 拼接串) -> Base64 -> 取前 8 位
//   3. 复杂度修复：若 8 位中缺少某类字符，按固定位置确定性替换，
//      替换字符取自 HMAC 摘要字节，确保任何语言实现结果可复现、一致。
// ============================================================================

using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace NewHireTools
{
    internal static class PasswordGenerator
    {
        // 内置 Seed（两个工具共用同一值，请勿泄露、请勿只修改某一份）
        private const string Seed = "REPLACE-WITH-YOUR-OWN-SECRET-SEED";

        // 复杂度修复时使用的符号池（确定性选择，保证可复现）
        private const string SpecialChars = "!@#$%^&*";

        /// <summary>
        /// 校验员工号格式：字母前缀 + 数字（如 J10065）。
        /// </summary>
        public static bool IsValidEmployeeId(string employeeId)
        {
            if (string.IsNullOrEmpty(employeeId)) return false;
            return Regex.IsMatch(employeeId, "^[A-Za-z]+[0-9]+$");
        }

        /// <summary>
        /// 计算初始密码（8 位，含大写/小写/数字/特殊符号四类）。
        /// </summary>
        public static string Generate(string employeeId)
        {
            if (!IsValidEmployeeId(employeeId))
                throw new ArgumentException("员工号格式不正确");

            string message = employeeId + "_2026";   // 拼接字符串，如 J10065_2026

            byte[] digest;
            using (HMACSHA256 hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Seed)))
            {
                digest = hmac.ComputeHash(Encoding.UTF8.GetBytes(message));
            }

            string base64 = Convert.ToBase64String(digest);
            char[] pwd = base64.Substring(0, 8).ToCharArray();
            string original = new string(pwd);

            bool hasUpper = Regex.IsMatch(original, "[A-Z]");
            bool hasLower = Regex.IsMatch(original, "[a-z]");
            bool hasDigit = Regex.IsMatch(original, "[0-9]");
            bool hasSpecial = Regex.IsMatch(original, "[^A-Za-z0-9]");

            // 固定位置替换（第 3/5/7 位 -> 下标 2/4/6；小写缺失时取下标 0）。
            // 替换字符取自摘要字节，确保两个工具结果一致。
            if (!hasUpper)
                pwd[2] = (char)('A' + digest[8] % 26);
            if (!hasDigit)
                pwd[4] = (char)('0' + digest[9] % 10);
            if (!hasSpecial)
                pwd[6] = SpecialChars[digest[10] % SpecialChars.Length];
            if (!hasLower)
                pwd[0] = (char)('a' + digest[11] % 26);

            return new string(pwd);
        }
    }
}
