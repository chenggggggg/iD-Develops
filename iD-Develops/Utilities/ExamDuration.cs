using System.Globalization;
using System.Text.RegularExpressions;

namespace iD_Develops.Utilities
{
    public static partial class ExamDuration
    {
        [GeneratedRegex(@"^(\d{1,5}):([0-5]\d):([0-5]\d)$", RegexOptions.CultureInvariant)]
        private static partial Regex DurationPattern();

        public static bool TryParse(string? value, out int? totalSeconds)
        {
            totalSeconds = null;

            if (string.IsNullOrWhiteSpace(value))
                return true;

            var match = DurationPattern().Match(value.Trim());
            if (!match.Success ||
                !int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var hours) ||
                !int.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var minutes) ||
                !int.TryParse(match.Groups[3].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
            {
                return false;
            }

            try
            {
                totalSeconds = checked((hours * 3600) + (minutes * 60) + seconds);
                return totalSeconds > 0;
            }
            catch (OverflowException)
            {
                totalSeconds = null;
                return false;
            }
        }

        public static string Format(int totalSeconds)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(totalSeconds);

            var hours = totalSeconds / 3600;
            var minutes = (totalSeconds % 3600) / 60;
            var seconds = totalSeconds % 60;
            return $"{hours:D2}:{minutes:D2}:{seconds:D2}";
        }

        public static string? Format(int? totalSeconds) =>
            totalSeconds is > 0 ? Format(totalSeconds.Value) : null;
    }
}
