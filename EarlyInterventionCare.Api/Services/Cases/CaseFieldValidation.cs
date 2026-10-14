namespace EarlyInterventionCare.Api.Services.Cases;

public static class CaseFieldValidation
{
    // Same national-ID validation as 7's existing case-create.js; server repeats it.
    // Residence permits require a separately agreed identifier contract.
    private static readonly int[] LetterCodes =
        [10, 11, 12, 13, 14, 15, 16, 17, 34, 18, 19, 20, 21,
         22, 35, 23, 24, 25, 26, 27, 28, 29, 32, 30, 31, 33];
    private static readonly int[] Weights = [8, 7, 6, 5, 4, 3, 2, 1, 1];

    public static bool IsValidNationalId(string? value)
    {
        if (value is null || value.Length != 10 || value[0] is < 'A' or > 'Z'
            || value[1] is not ('1' or '2')) return false;

        var code = LetterCodes[value[0] - 'A'];
        var sum = code / 10 + code % 10 * 9;
        for (var index = 0; index < Weights.Length; index++)
        {
            var digit = value[index + 1];
            if (digit is < '0' or > '9') return false;
            sum += (digit - '0') * Weights[index];
        }
        return sum % 10 == 0;
    }
}
