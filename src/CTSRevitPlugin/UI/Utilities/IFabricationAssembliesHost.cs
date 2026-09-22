using System.Collections.Generic;

namespace CTSRevitPlugin.UI.Utilities
{
    public interface IFabricationAssembliesHost
    {
        void ReceiveServices(List<FabricationServiceInfo> services, string status);
        void ReceiveCatalog(List<FabricationPartButtonInfo> catalog, List<FabricationPaletteInfo> palettes, string status);
        void SetToolStatus(string message);
    }
}
