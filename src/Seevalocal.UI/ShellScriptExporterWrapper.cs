using Seevalocal.Config.Export;
using Seevalocal.UI.Services;

namespace Seevalocal.UI;

internal class ShellScriptExporterWrapper : IShellScriptExporter
{
    private readonly ShellScriptExporter _exporter = new();
    public string Export(Core.Models.ResolvedConfig config, Core.Models.ShellTarget target) => _exporter.Export(config, target);
}
