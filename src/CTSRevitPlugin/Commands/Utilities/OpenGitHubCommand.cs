using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Diagnostics;
using CTSRevitPlugin.Utilities;

namespace CTSRevitPlugin.Commands.Utilities
{
    [Transaction(TransactionMode.Manual)]
    [ToolDocumentation(
        description: "Open the CTS Revit Plugin repository on GitHub.",
        usage: "Click the GitHub button in the CTS Support panel.",
        author: "Pedro Oliveira",
        version: "1.0",
        notes: "Opens the repository in the user's default browser.")]
    public class OpenGitHubCommand : IExternalCommand
    {
        private const string RepositoryUrl = "https://github.com/cts-bim/CTS-Revit-Plugin";

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = RepositoryUrl,
                    UseShellExecute = true
                });
                return Result.Succeeded;
            }
            catch (System.Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
