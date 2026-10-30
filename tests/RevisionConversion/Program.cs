using System.Globalization;
using EarlyInterventionCare.Api.Services.Questionnaires;

var passed = 0;
var failed = 0;
void Check(string name, Func<bool> test)
{
    try
    {
        if (!test()) throw new InvalidOperationException("Assertion failed");
        passed++;
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception ex)
    {
        failed++;
        Console.WriteLine($"FAIL {name}: {ex.Message}");
    }
}

(string Text, ulong Value)[] valid =
[
    ("0", 0UL), ("1", 1UL),
    ("9007199254740991", 9007199254740991UL),
    ("9007199254740992", 9007199254740992UL),
    ("9007199254740993", 9007199254740993UL),
    ("18446744073709551614", ulong.MaxValue - 1),
    ("18446744073709551615", ulong.MaxValue)
];
foreach (var (text, expected) in valid)
{
    Check($"parse {text}", () => RevisionStringConverter.TryParse(text, out var value) && value == expected);
    Check($"format {text}", () => RevisionStringConverter.Format(expected) == text);
    Check($"roundtrip {text}", () =>
        RevisionStringConverter.TryParse(RevisionStringConverter.Format(expected), out var value) && value == expected);
}

string?[] invalid =
[
    null, "", " ", "\t", "\r\n", " 1", "1 ", "1\n", "\u00a01",
    "-1", "-0", "+1", "+0", "00", "01", "0000000000000000000000000",
    "abc", "1a", "1.0", "1,000", "1e3", "0x10", "1_000",
    "１", "١", "1\0", "18446744073709551616", "99999999999999999999",
    "100000000000000000000"
];
for (var i = 0; i < invalid.Length; i++)
{
    var input = invalid[i];
    Check($"reject invalid #{i + 1}", () =>
        !RevisionStringConverter.TryParse(input, out var value) && value == 0);
}

var savedCulture = CultureInfo.CurrentCulture;
try
{
    foreach (var name in new[] { "fr-FR", "ar-SA", "zh-TW" })
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
        Check($"culture {name}", () => valid.All(item =>
            RevisionStringConverter.Format(item.Value) == item.Text &&
            RevisionStringConverter.TryParse(item.Text, out var value) && value == item.Value));
    }
}
finally
{
    CultureInfo.CurrentCulture = savedCulture;
}

Console.WriteLine($"RESULT: {passed} PASS, {failed} FAIL");
return failed == 0 ? 0 : 1;
