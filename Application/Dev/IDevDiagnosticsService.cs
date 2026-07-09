namespace JacRed.Application.Dev
{
    public interface IDevDiagnosticsService
    {
        object FindCorrupt(int sampleSize = 20);
        object FindDuplicateKeys(string tracker = null, bool excludeNumeric = true);
        object FindEmptySearchFields(int sampleSize = 20);
    }
}
