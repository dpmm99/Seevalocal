using Seevalocal.Config.Export;
using Seevalocal.UI.Services;

namespace Seevalocal.UI;

internal class ShellScriptExporterWrapper : IShellScriptExporter
{
    public string Export(Core.Models.ResolvedConfig config, Core.Models.ShellTarget target) => ShellScriptExporter.Export(config, target);
}
