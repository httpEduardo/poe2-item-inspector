namespace PoE2Inspector.Services.Config;

public interface IConfigService
{
    AppConfig Load();
    void Save(AppConfig config);
}
