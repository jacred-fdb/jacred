namespace JacRed.Application.Dev
{
    public interface IDevMaintenanceService
    {
        object UpdateSize();
        object ResetCheckTime();
        object UpdateDetails();
        object UpdateSearchName();
    }
}
