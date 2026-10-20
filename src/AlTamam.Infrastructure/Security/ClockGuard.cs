using System.Globalization;
using AlTamam.Contracts.Bridge;
using AlTamam.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using static AlTamam.Infrastructure.Persistence.SqliteOperations;

namespace AlTamam.Infrastructure.Security;

internal static class ClockGuard
{
    internal static async Task ValidateAsync(SqliteConnection connection, SqliteTransaction? transaction, DateTimeOffset now, bool advance = false)
    {
        var raw = await ScalarAsync(connection, transaction,
            "SELECT SettingValue FROM AppSettings WHERE SettingKey='LastObservedUtcTimestamp'").ConfigureAwait(false);
        Require(raw is string text && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _),
            "تعذر التحقق من وقت التشغيل المحفوظ.", BridgeErrorCodes.ClockRollbackSuspected);
        var last = DateTimeOffset.Parse((string)raw!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        Require(now >= last.AddMinutes(-2), "يبدو أن ساعة الجهاز رجعت إلى وقت سابق؛ راجع التاريخ والوقت.", BridgeErrorCodes.ClockRollbackSuspected);
        if (advance && now > last)
        {
            Require(transaction is not null, "تحديث ساعة الحماية يتطلب معاملة.");
            await ExecuteAsync(connection, transaction,
                "UPDATE AppSettings SET SettingValue=$now,UpdatedAtUtc=$now WHERE SettingKey='LastObservedUtcTimestamp'",
                ("$now", now.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture))).ConfigureAwait(false);
        }
    }
}
