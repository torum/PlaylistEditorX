using PlaylistEditorX.Models;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace PlaylistEditorX.Services;

public interface IConfigurationService
{
    void Initialize(string configFilePath);
    Task<Models.Configuration> LoadConfigurationAsync();
    Task SaveConfigurationAsync();
    Models.Configuration CurrentConfiguration { get; }
}