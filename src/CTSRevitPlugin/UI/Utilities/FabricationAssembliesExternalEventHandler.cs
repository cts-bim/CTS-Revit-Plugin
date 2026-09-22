using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;

namespace CTSRevitPlugin.UI.Utilities
{
    public class FabricationAssembliesExternalEventHandler : IExternalEventHandler
    {
        private enum RequestType
        {
            LoadServices,
            LoadCatalog,
            PlaceAssembly,
            CaptureAssembly
        }

        private sealed class ApiRequest
        {
            public RequestType Type;
            public int ServiceId;
            public int PaletteIndex;
            public FabricationAssemblyTemplate Template;
            public IFabricationAssembliesHost Host;
        }

        private readonly Queue<ApiRequest> _requests = new Queue<ApiRequest>();
        private readonly object _queueLock = new object();

        // Kept for compatibility with the existing shared-pane/editor architecture.
        public IFabricationAssembliesHost Host { get; set; }

        public void RequestLoadServices()
        {
            Enqueue(new ApiRequest { Type = RequestType.LoadServices });
        }

        public void RequestLoadCatalog(int serviceId, int paletteIndex)
        {
            Enqueue(new ApiRequest
            {
                Type = RequestType.LoadCatalog,
                ServiceId = serviceId,
                PaletteIndex = paletteIndex
            });
        }

        public void RequestPlaceAssembly(FabricationAssemblyTemplate template)
        {
            Enqueue(new ApiRequest
            {
                Type = RequestType.PlaceAssembly,
                Template = CloneTemplate(template)
            });
        }

        public void RequestCaptureAssembly(FabricationAssemblyTemplate template)
        {
            if (template == null) throw new ArgumentNullException(nameof(template));
            Enqueue(new ApiRequest { Type = RequestType.CaptureAssembly, Template = CloneTemplate(template) });
        }

        private void Enqueue(ApiRequest request)
        {
            request.Host = Host;
            lock (_queueLock)
            {
                _requests.Enqueue(request);
            }
        }

        private bool TryDequeue(out ApiRequest request)
        {
            lock (_queueLock)
            {
                if (_requests.Count == 0)
                {
                    request = null;
                    return false;
                }
                request = _requests.Dequeue();
                return true;
            }
        }

        public void Execute(UIApplication app)
        {
            if (app == null || app.ActiveUIDocument == null) return;

            // Drain every request already queued before returning control to Revit.
            // This prevents LoadServices -> LoadCatalog races when the modeless editor
            // changes selection immediately after a refresh.
            ApiRequest request;
            while (TryDequeue(out request))
            {
                try
                {
                    Document doc = app.ActiveUIDocument.Document;
                    if (doc == null || !doc.IsValidObject)
                    {
                        if (request.Host != null) request.Host.SetToolStatus("The active Revit document is not valid.");
                        continue;
                    }

                    IFabricationAssembliesHost requestHost = request.Host ?? Host;
                    if (requestHost == null) continue;
                    System.Windows.Window requestWindow = requestHost as System.Windows.Window;
                    if (requestWindow != null && !requestWindow.IsVisible) continue;
                    Host = requestHost;

                    switch (request.Type)
                    {
                        case RequestType.LoadServices:
                            LoadServices(doc);
                            break;
                        case RequestType.LoadCatalog:
                            LoadCatalog(doc, request.ServiceId, request.PaletteIndex);
                            break;
                        case RequestType.CaptureAssembly:
                            requestHost.SetToolStatus(
                                FabricationAssemblyCaptureService.Capture(app, request.Template.Name));
                            break;
                        case RequestType.PlaceAssembly:
                            _template = request.Template;
                            PlaceAssembly(app, doc);
                            break;
                    }
                }
                catch (Autodesk.Revit.Exceptions.InvalidObjectException ex)
                {
                    if (request.Host != null) request.Host.SetToolStatus("Revit invalidated a fabrication API object. Click REFRESH and try again. Details: " + ex.Message);
                }
                catch (Exception ex)
                {
                    if (request.Host != null) request.Host.SetToolStatus("Fabrication Assemblies error: " + ex.Message);
                }
            }
        }

        private FabricationAssemblyTemplate _template;

        private void LoadServices(Document doc)
        {
            try
            {
                FabricationConfiguration configuration = FabricationConfiguration.GetFabricationConfiguration(doc);
                if (configuration == null || !configuration.IsValidObject)
                {
                    Host.ReceiveServices(new List<FabricationServiceInfo>(), "No valid fabrication configuration is available in this document.");
                    return;
                }

                if (!configuration.HasValidConfiguration())
                {
                    Host.ReceiveServices(new List<FabricationServiceInfo>(), "No valid Revit Fabrication configuration is loaded.");
                    return;
                }

                IList<FabricationService> services;
                try { services = configuration.GetAllLoadedServices(); }
                catch (Exception ex)
                {
                    Host.ReceiveServices(new List<FabricationServiceInfo>(),
                        "Revit could not enumerate loaded services: " + ex.Message);
                    return;
                }
                List<FabricationServiceInfo> result = new List<FabricationServiceInfo>();

                if (services != null)
                {
                    foreach (FabricationService service in services)
                    {
                        try
                        {
                            if (service == null || !service.IsValidObject) continue;
                            result.Add(new FabricationServiceInfo
                            {
                                ServiceId = service.ServiceId,
                                Name = service.Name,
                                Abbreviation = service.Abbreviation
                            });
                        }
                        catch (Autodesk.Revit.Exceptions.InvalidObjectException) { }
                    }
                }

                // Deliberately do not use GetAllServices(): unloaded services cannot be
                // used for placement and make the catalog unnecessarily slow.
                result = result.GroupBy(x => x.ServiceId).Select(g => g.First()).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
                Host.ReceiveServices(result, "Loaded " + result.Count + " fabrication service(s).");
            }
            catch (Autodesk.Revit.Exceptions.InvalidObjectException ex)
            {
                Host.ReceiveServices(new List<FabricationServiceInfo>(), "Fabrication service API object became invalid. Click REFRESH DATABASE and try again. " + ex.Message);
            }
        }

        private void LoadCatalog(Document doc, int serviceId, int paletteIndex)
        {
            FabricationConfiguration configuration = FabricationConfiguration.GetFabricationConfiguration(doc);
            if (configuration == null || !configuration.IsValidObject || !configuration.HasValidConfiguration())
            {
                Host.ReceiveCatalog(new List<FabricationPartButtonInfo>(), new List<FabricationPaletteInfo>(),
                    "No active Fabrication configuration in this model.");
                return;
            }

            FabricationService service = configuration.GetService(serviceId);
            if (service == null || !service.IsValidObject)
            {
                Host.ReceiveCatalog(new List<FabricationPartButtonInfo>(), new List<FabricationPaletteInfo>(),
                    "This Fabrication service is not available. Refresh the database.");
                return;
            }

            List<FabricationPaletteInfo> palettes = new List<FabricationPaletteInfo>();
            for (int p = 0; p < service.PaletteCount; p++)
                palettes.Add(new FabricationPaletteInfo { Index = p, Name = service.GetPaletteName(p) });

            if (palettes.Count == 0)
            {
                Host.ReceiveCatalog(new List<FabricationPartButtonInfo>(), palettes,
                    "No palettes in " + service.Name + ".");
                return;
            }

            if (paletteIndex < 0 || paletteIndex >= palettes.Count) paletteIndex = 0;
            string paletteName = palettes[paletteIndex].Name;
            List<FabricationPartButtonInfo> result = new List<FabricationPartButtonInfo>();
            int skipped = 0;
            int count = service.GetButtonCount(paletteIndex);

            // Read ONE palette only. Convert all API wrappers into simple view models
            // within this ExternalEvent; never store FabricationServiceButton on WPF.
            for (int i = 0; i < count; i++)
            {
                FabricationServiceButton button = null;
                try
                {
                    button = service.GetButton(paletteIndex, i);
                    if (button == null || !button.IsValid() || button.IsAHanger) continue;

                    List<FabricationPartConditionInfo> conditions = new List<FabricationPartConditionInfo>();
                    for (int c = 0; c < button.ConditionCount; c++)
                    {
                        string conditionName = "";
                        string description = "";
                        double lower = -1.0;
                        double upper = -1.0;
                        try { conditionName = button.GetConditionName(c); } catch { }
                        try { description = button.GetConditionDescription(c); } catch { }
                        try { lower = button.GetConditionLowerValue(c); } catch { }
                        try { upper = button.GetConditionUpperValue(c); } catch { }
                        conditions.Add(new FabricationPartConditionInfo
                        {
                            ConditionIndex = c, Name = conditionName, Description = description,
                            LowerValue = lower, UpperValue = upper
                        });
                    }

                    BitmapSource image = null;
                    Bitmap bitmap = null;
                    try
                    {
                        bitmap = button.GetImage();
                        image = BitmapToBitmapSource(bitmap);
                    }
                    finally { if (bitmap != null) bitmap.Dispose(); }
                    if (image == null && button.ConditionCount > 0)
                    {
                        try
                        {
                            Bitmap conditionBitmap = button.GetConditionImage(0);
                            try { image = BitmapToBitmapSource(conditionBitmap); }
                            finally { if (conditionBitmap != null) conditionBitmap.Dispose(); }
                        }
                        catch { }
                    }

                    result.Add(new FabricationPartButtonInfo
                    {
                        Name = button.Name, Code = button.Code,
                        ServiceId = serviceId, ServiceName = service.Name,
                        PaletteIndex = paletteIndex, ButtonIndex = i,
                        PaletteName = paletteName, ConditionCount = button.ConditionCount,
                        Conditions = conditions, Image = image
                    });
                }
                catch (Autodesk.Revit.Exceptions.InvalidObjectException) { skipped++; }
                catch (Exception) { skipped++; }
                finally { if (button != null) { try { button.Dispose(); } catch { } } }
            }

            Host.ReceiveCatalog(result, palettes,
                service.Name + " / " + paletteName + ": " + result.Count + " part(s)" +
                (skipped > 0 ? "; " + skipped + " unreadable item(s)" : "") + ".");
        }

        private void PlaceAssembly(UIApplication uiapp, Document doc)
        {
            if (_template == null || _template.Parts == null || _template.Parts.Count < 2 ||
                !IsOLetDefinition(_template.Parts[0]))
            {
                SetStatus("CONFIGURE: the sequence must start with O-Let Weld Gap and contain at least one additional item.");
                return;
            }

            // A fabrication joint is a connector-generated coupling, not a normal
            // inline fitting for this recipe.  Never create these as step 2 etc.
            List<string> explicitJoints = _template.Parts.Skip(1)
                .Where(IsAutoCouplingDefinition)
                .Select(x => x.Name ?? "Joint").Distinct().ToList();
            if (explicitJoints.Count > 0)
            {
                SetStatus("CONFIGURE: remove " + string.Join(", ", explicitJoints) +
                    " from PART SEQUENCE. Socket-weld/thread joints must be generated by Revit when joining the real parts.");
                return;
            }

            List<ElementId> anchorIds = new List<ElementId>();
            Autodesk.Revit.UI.Selection.Selection selection = uiapp.ActiveUIDocument.Selection;
            FabricationOLetSelectionFilter filter = new FabricationOLetSelectionFilter();
            foreach (ElementId id in selection.GetElementIds())
            {
                Element element = doc.GetElement(id);
                if (element != null && filter.AllowElement(element)) anchorIds.Add(id);
            }

            if (anchorIds.Count == 0)
            {
                IList<Reference> references;
                try
                {
                    references = selection.PickObjects(
                        Autodesk.Revit.UI.Selection.ObjectType.Element,
                        filter,
                        "Select ONE OR MORE existing O-Lets, then click FINISH in Revit.");
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    SetStatus("O-Let selection cancelled. No part was changed.");
                    return;
                }
                anchorIds = references.Select(x => x.ElementId).Distinct().ToList();
            }
            if (anchorIds.Count == 0)
            {
                SetStatus("No O-Let selected. No part was changed.");
                return;
            }

            FabricationConfiguration configuration = FabricationConfiguration.GetFabricationConfiguration(doc);
            if (configuration == null || !configuration.IsValidObject || !configuration.HasValidConfiguration())
            {
                SetStatus("No valid Fabrication configuration in this project.");
                return;
            }

            int succeeded = 0;
            // One lookup cache for the complete batch: 30 identical O-Lets must
            // not each re-scan every palette/button of CHWS or CHWR.
            Dictionary<string, ButtonAddress> resolvedButtons = new Dictionary<string, ButtonAddress>();
            List<string> failed = new List<string>();
            foreach (ElementId anchorId in anchorIds)
            {
                // A separate transaction per O-Let protects successful placements even
                // if a later anchor has a different size or missing service button.
                try
                {
                    string message = BuildAtAnchor(doc, configuration, anchorId, _template, resolvedButtons);
                    if (string.IsNullOrEmpty(message)) succeeded++;
                    else failed.Add("O-Let " + anchorId.IdValue() + ": " + message);
                }
                catch (Exception ex)
                {
                    failed.Add("O-Let " + anchorId.IdValue() + ": " + ex.Message);
                }
            }

            string summary = _template.Name + ": " + succeeded + "/" + anchorIds.Count + " O-Let(s) completed.";
            if (failed.Count > 0)
                summary += " " + string.Join(" | ", failed.Take(3)) +
                    (failed.Count > 3 ? " | +" + (failed.Count - 3) + " more failure(s)" : "");
            SetStatus(summary);
        }

        private static string BuildAtAnchor(
            Document doc, FabricationConfiguration configuration, ElementId anchorId,
            FabricationAssemblyTemplate template,
            Dictionary<string, ButtonAddress> resolvedButtons)
        {
            FabricationPart anchor = doc.GetElement(anchorId) as FabricationPart;
            if (anchor == null || !anchor.IsValidObject)
                return "selected element " + anchorId.IdValue() + " is not a valid MEP FabricationPart.";

            // Revit FabricationPart.Name is frequently the TYPE name "Default",
            // not the item/family name shown in the Properties header.  The v12
            // ContainsOLet(anchor) test rejected real anchors accepted by v11.
            bool isTap = false;
            try { isTap = anchor.IsATap(); } catch { }
            string expectedAnchorName = template.Parts[0].Name;
            string identity = GetPartFamilyIdentity(anchor);
            string displayIdentity = !string.IsNullOrWhiteSpace(identity) ? identity : anchor.Name;
            if (!ContainsOLet(anchor) && !isTap &&
                !ContainsText(identity, "o-let") && !ContainsText(identity, "olet"))
                return "selected FabricationPart is not an O-Let. Family='" +
                    displayIdentity + "', IsATap=false; expected '" + expectedAnchorName + "'.";

            // Strictly reject a *different identifiable O-Let*.  When Revit only
            // exposes "Default" and IsATap=true, preserve the working v11 tap
            // behavior rather than comparing the recipe to the wrong API field.
            if (!string.IsNullOrWhiteSpace(identity) &&
                !SameLogicalName(identity, expectedAnchorName))
                return "wrong anchor: Revit family '" + identity + "', recipe requires '" +
                    expectedAnchorName + "'. Choose the exact O-Let Weld Gap.";
            if (string.IsNullOrWhiteSpace(identity) && !isTap)
                return "could not verify the selected O-Let identity; API Name='" +
                    anchor.Name + "', IsATap=false. No elements created.";

            Connector open = FindAnchorOpenConnector(anchor);
            if (open == null)
                return "no free connector. Check if this assembly is already connected.";
            if (open.Shape != ConnectorProfileType.Round)
                return "the free O-Let connector is not round; this template expects pipework.";

            double anchorDiameterFeet = open.Radius * 2.0;
            if (anchorDiameterFeet <= 0.0)
                return "could not read the open O-Let connector diameter.";
            double diameterFeet = template.UseAnchorDiameter || template.DefaultDiameterInches <= 0.0
                ? anchorDiameterFeet : template.DefaultDiameterInches / 12.0;
            if (Math.Abs(diameterFeet - anchorDiameterFeet) > (1.0 / 192.0))
                return "template diameter " + ImperialLength.FormatInches(diameterFeet * 12.0) +
                    " differs from the O-Let open connector " + ImperialLength.FormatInches(anchorDiameterFeet * 12.0) +
                    ". Choose AUTO (O-Let) in CONFIGURE or adjust the O-Let.";

            int serviceId = anchor.ServiceId;
            // Store only elementary IDs/numbers between API calls; do not hold the
            // service or connector wrapper while repeatedly regenerating the model.
            int firstConnectorId = open.Id;
            // This is the ONLY placement axis. It comes from the O-Let as the
            // user placed it, never from an arbitrary fitting connector order.
            XYZ assemblyAxis = SafeDirection(open);
            ElementId levelId = anchor.LevelId;
            if (levelId == null || levelId == ElementId.InvalidElementId)
                levelId = GetFallbackLevelId(doc);
            if (levelId == null || levelId == ElementId.InvalidElementId)
                return "no valid Level for creating the remaining parts.";

            FabricationService service = configuration.GetService(serviceId);
            if (service == null || !service.IsValidObject)
                return "the O-Let service is not loaded in this model.";

            // PRE-FLIGHT: resolve every generic template item in the selected
            // O-Let's own service BEFORE creating or modifying anything.
            List<ButtonAddress> addresses = new List<ButtonAddress>();
            for (int i = 1; i < template.Parts.Count; i++)
            {
                FabricationAssemblyPartDefinition definition = template.Parts[i];
                ButtonAddress address;
                string reason = null;
                // Cache by target service AND full logical identity.  Palette/button
                // indices are service-local and are never reused across services.
                string cacheKey = serviceId + ":" + (definition.Code ?? "") + ":" +
                    (definition.Name ?? "") + ":" + (definition.PaletteName ?? "") + ":" + definition.ServiceId;
                if (resolvedButtons.TryGetValue(cacheKey, out address))
                {
                    addresses.Add(address);
                    continue;
                }
                if (!TryFindButtonAddress(service, definition, out address, out reason))
                {
                    // Some components, such as instrumentation/valves, deliberately
                    // live in a shared accessory service. Reuse the chosen catalog
                    // service only when it is genuinely DIFFERENT (not an alternate
                    // CHWS/CHWR-compatible pipework service).
                    FabricationService sourceService = null;
                    if (definition.ServiceId > 0 && definition.ServiceId != serviceId)
                    {
                        try { sourceService = configuration.GetService(definition.ServiceId); } catch { }
                    }
                    bool sharedAccessory = false;
                    try
                    {
                        sharedAccessory = sourceService != null && sourceService.IsValidObject &&
                            !sourceService.IsCompatibleWith(service) &&
                            IsAccessoryPalette(definition.PaletteName);
                    }
                    catch { }
                    if (!sharedAccessory ||
                        !TryFindButtonAddress(sourceService, definition, out address, out reason))
                        return "step " + (i + 1) + " (" + definition.Name + "): " + reason;
                    address.ServiceId = definition.ServiceId;
                }
                else address.ServiceId = serviceId;
                resolvedButtons[cacheKey] = address;
                addresses.Add(address);
            }

            using (Transaction transaction = new Transaction(doc, "CTS " + template.Name + " - O-Let " + anchorId.IdValue()))
            {
                transaction.Start();
                int currentStep = 1;
                try
                {
                    ElementId previousPartId = anchorId;
                    int previousConnectorId = firstConnectorId;
                    List<ElementId> modeledOrder = new List<ElementId> { anchorId };

                    for (int i = 1; i < template.Parts.Count; i++)
                    {
                        FabricationAssemblyPartDefinition definition = template.Parts[i];
                        currentStep = i + 1;
                        ButtonAddress address = addresses[i - 1];
                        FabricationService currentService = configuration.GetService(address.ServiceId);
                        if (currentService == null || !currentService.IsValidObject)
                            throw new InvalidOperationException("Service invalidated during step " + (i + 1) + ".");
                        FabricationServiceButton button = currentService.GetButton(address.PaletteIndex, address.ButtonIndex);
                        FabricationPart created;
                        try
                        {
                            if (button == null || !button.IsValid())
                                throw new InvalidOperationException("The button for " + definition.Name + " is no longer valid.");
                            // Resolve by the saved logical BUTTON identity in the O-Let's
                            // service, never by a similar-looking family or a code alone.
                            if (!SameLogicalName(button.Name, definition.Name))
                                throw new InvalidOperationException("catalog identity changed: recipe requires '" +
                                    definition.Name + "' but target service button is '" + button.Name + "'.");

                            created = CreatePart(doc, button, diameterFeet, levelId,
                                definition.ConditionIndex, definition.ConditionName,
                                definition.ConditionExplicit || definition.ConditionIndex > 0);
                            if (created != null && created.IsValidObject)
                            {
                                ElementId freshId = created.Id;
                                doc.Regenerate();
                                created = doc.GetElement(freshId) as FabricationPart;
                                if (created != null && created.IsValidObject)
                                    ResolveProductDiameter(doc, created, diameterFeet, definition.Name);
                            }
                            if (created == null || !created.IsValidObject)
                                throw new InvalidOperationException("Revit did not create " + definition.Name + ".");

                            ValidateSelectedConditionType(doc, button, definition, created);
                            // FabricationServiceButton.Name is the palette BUTTON label.
                            // The Revit Properties family label can be a different item/ITM
                            // description even when Revit creates from precisely this button.
                            // Never compare these two human-readable labels for equality:
                            // e.g. the saved button 'Blk CS Nipple S/80' may yield a family
                            // label 'Blk CS Nipple XH SMLS BExT x3'. Instead, confirm the
                            // resulting FabricationPartType belongs to this EXACT button.
                            doc.Regenerate();
                            FabricationPartType actualType =
                                doc.GetElement(created.GetTypeId()) as FabricationPartType;
                            if (actualType == null || !actualType.IsValidObject ||
                                !button.ContainsFabricationPartType(actualType))
                            {
                                string actualFamily = GetPartFamilyIdentity(created);
                                throw new InvalidOperationException(
                                    "created part does not belong to the selected fabrication button '" +
                                    definition.Name + "' (Revit family '" + actualFamily +
                                    "'). Changes to this O-Let were rolled back.");
                            }
                            // One button can produce different ITMs/conditions. v14's
                            // ContainsFabricationPartType alone does NOT certify the
                            // end treatment: prevent Soc-O-Let -> Thread/Weld-O-Let.
                            string createdFamily = GetPartFamilyIdentity(created);
                            if (!IsExpectedOLetEndTreatment(definition.Name, createdFamily))
                                throw new InvalidOperationException(
                                    "wrong fabrication end treatment: recipe '" + definition.Name +
                                    "', actual Revit family '" + createdFamily +
                                    "'. Choose an explicit compatible variation in CONFIGURE; " +
                                    "this O-Let was rolled back.");
                        }
                        finally { if (button != null) { try { button.Dispose(); } catch { } } }
                        ElementId createdId = created.Id;
                        doc.Regenerate();
                        Connector fixedConnector = GetConnector(doc, previousPartId, previousConnectorId);
                        FabricationPart liveCreated = doc.GetElement(createdId) as FabricationPart;
                        if (fixedConnector == null || fixedConnector.IsConnected || liveCreated == null)
                            throw new InvalidOperationException("The previous connector became unavailable.");
                        // Try BOTH ends, not simply whichever happens to be nearest to
                        // the newly created part's initial (arbitrary) position.
                        // Each failed trial is rolled back independently so it cannot
                        // move the part or leave a stray coupling behind.
                        int incomingId;
                        int outgoingId;
                        string connectionError;
                        bool needNext = i < template.Parts.Count - 1;
                        if (!TryConnectByAvailableEnds(doc, configuration, address, definition,
                            previousPartId, previousConnectorId, createdId,
                            assemblyAxis, needNext,
                            out incomingId, out outgoingId, out connectionError))
                            throw new InvalidOperationException("Cannot connect " + definition.Name +
                                " to the preceding part: " + connectionError);

                        modeledOrder.Add(createdId);
                        // The next part ALWAYS starts at the verified forward connector.
                        // Do not choose the most distant (possibly SIDE) connection.
                        if (needNext)
                        {
                            previousPartId = createdId;
                            previousConnectorId = outgoingId;
                        }
                    }
                    // A successful ConnectAndCouple return alone is not proof of
                    // the requested assembly; verify connectivity in captured
                    // sequence order (automatic joints may be between steps).
                    for (int j = 1; j < modeledOrder.Count; j++)
                    {
                        string actualPath;
                        if (!FabricationReferenceResolver.TryFindChain(doc,
                            modeledOrder[j - 1], modeledOrder[j], out actualPath))
                            throw new InvalidOperationException("final check: steps " + j +
                                " and " + (j + 1) + " are not connected by a short chain; " +
                                "reverting this O-Let.");
                    }
                    TransactionStatus result = transaction.Commit();
                    if (result != TransactionStatus.Committed)
                        return "Revit did not commit the transaction.";
                }
                catch (Exception ex)
                {
                    try { transaction.RollBack(); } catch { }
                    return "step " + currentStep + ": " + ex.Message;
                }
            }
            return null;
        }

        private static bool IsExpectedOLetEndTreatment(string expected, string actual)
        {
            // Only compare unmistakably contradictory end-treatment names.
            // Button names and ITM family names are otherwise different labeling
            // systems; do not compare their full text or equate S/80 and XH here.
            if (string.IsNullOrWhiteSpace(expected) ||
                string.IsNullOrWhiteSpace(actual)) return true;
            bool expectedSocket = ContainsText(expected, "soc-o-let") ||
                                  ContainsText(expected, "socketweld");
            bool expectedThread = ContainsText(expected, "thread-o-let") ||
                                  ContainsText(expected, "threaded 3000");
            bool actualSocket = ContainsText(actual, "soc-o-let") ||
                                ContainsText(actual, "socketweld");
            bool actualThread = ContainsText(actual, "thread-o-let") ||
                                ContainsText(actual, "threaded 3000");
            bool actualPlainWeld = ContainsText(actual, "weld-o-let") &&
                                   !actualSocket;
            if (expectedSocket && (actualThread || actualPlainWeld)) return false;
            if (expectedThread && (actualSocket || actualPlainWeld)) return false;
            return true;
        }

        // A joint in the Fabrication Joints palette is inserted by the connection
        // engine, not another fitting to create and align as a free-standing part.
        private static bool IsAutoCouplingDefinition(FabricationAssemblyPartDefinition definition)
        {
            if (definition == null) return false;
            return string.Equals((definition.PaletteName ?? "").Trim(), "Joints",
                StringComparison.OrdinalIgnoreCase) &&
                (ContainsText(definition.Name, "weld") ||
                 ContainsText(definition.Name, "thread") ||
                 ContainsText(definition.Name, "joint") ||
                 ContainsText(definition.Name, "gap"));
        }

        private static bool ContainsText(string value, string match)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                value.IndexOf(match, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // Revit 2024: align then ConnectAndCouple with the same connector wrappers.
        // Each candidate is isolated in a subtransaction. A successful connection
        // is NOT enough: for a nonterminal part, the expected forward outlet must
        // also exist, otherwise roll back and try the other inlet end.
        private static bool TryConnectByAvailableEnds(
            Document doc, FabricationConfiguration configuration,
            ButtonAddress address, FabricationAssemblyPartDefinition definition,
            ElementId previousPartId, int previousConnectorId,
            ElementId newPartId, XYZ assemblyAxis, bool needNext,
            out int usedIncomingId, out int usedOutgoingId, out string diagnostic)
        {
            usedIncomingId = -1;
            usedOutgoingId = -1;
            diagnostic = "no connection attempt succeeded";
            FabricationPart part = doc.GetElement(newPartId) as FabricationPart;
            Connector fixedConnector = GetConnector(doc, previousPartId, previousConnectorId);
            if (part == null || fixedConnector == null || fixedConnector.IsConnected)
            {
                diagnostic = "the O-Let/previous part has no available output connector";
                return false;
            }

            // If we have drifted to a different branch, NEVER place the next item.
            if (SafeDirection(fixedConnector).DotProduct(assemblyAxis) < 0.5)
            {
                diagnostic = "previous output is not oriented along the original O-Let outlet; " +
                    "stopped to avoid building on a side branch";
                return false;
            }

            List<int> incomingIds = new List<int>();
            ConnectorSetIterator iterator = part.ConnectorManager.Connectors.ForwardIterator();
            while (iterator.MoveNext())
            {
                Connector item = iterator.Current as Connector;
                if (item != null && !item.IsConnected && item.Shape == fixedConnector.Shape &&
                    item.ConnectorType == ConnectorType.End)
                    incomingIds.Add(item.Id);
            }
            if (incomingIds.Count == 0)
            {
                diagnostic = "the new part has no open end connector with a compatible shape";
                return false;
            }

            // Try matching the inlet nominal size first. For fabrication product-list
            // parts, Revit can require setting Connector.Radius to select the
            // product entry. v15 removed this and regressed the working v11.
            // Re-sizing is only allowed inside a rollback-able trial and the
            // result must still belong to the EXACT saved catalog button.
            incomingIds = incomingIds.OrderBy(id =>
            {
                Connector c = GetConnector(doc, newPartId, id);
                return c == null ? double.MaxValue :
                    Math.Abs(c.Radius - fixedConnector.Radius);
            }).ToList();

            List<string> failures = new List<string>();
            foreach (int id in incomingIds)
            {
                for (int alignMode = 0; alignMode < 2; alignMode++)
                {
                    using (SubTransaction trial = new SubTransaction(doc))
                    {
                        trial.Start();
                        try
                        {
                            Connector target = GetConnector(doc, previousPartId, previousConnectorId);
                            Connector incoming = GetConnector(doc, newPartId, id);
                            if (target == null || incoming == null || target.IsConnected || incoming.IsConnected)
                                throw new InvalidOperationException("trial connector was not open");
                            if (incoming.Shape != target.Shape)
                                throw new InvalidOperationException("connector shapes differ");

                            // On Revit fabrication product lists, Create(width, depth)
                            // can leave an inlet at the wrong product entry even
                            // when the correct button was chosen. The working v11
                            // corrected the nominal size BEFORE alignment.
                            // Allow that native size resolution only for AUTO
                            // conditions; verify it did not change the logical
                            // item or the selected fabrication service button.
                            if (incoming.Shape == ConnectorProfileType.Round &&
                                Math.Abs(incoming.Radius - target.Radius) > 1e-7)
                            {
                                if (definition.ConditionExplicit || definition.ConditionIndex > 0)
                                    throw new InvalidOperationException(
                                        "Saved explicit variation has inlet " +
                                        ImperialLength.FormatInches(incoming.Radius * 24.0) +
                                        " but the previous outlet is " +
                                        ImperialLength.FormatInches(target.Radius * 24.0) +
                                        ". Choose AUTO or a matching variation in CONFIGURE.");
                                incoming.Radius = target.Radius;
                                doc.Regenerate();
                                target = GetConnector(doc, previousPartId, previousConnectorId);
                                incoming = GetConnector(doc, newPartId, id);
                                if (target == null || incoming == null ||
                                    target.IsConnected || incoming.IsConnected)
                                    throw new InvalidOperationException(
                                        "connector unavailable after native size resolution");
                                if (Math.Abs(incoming.Radius - target.Radius) > 1e-6)
                                    throw new InvalidOperationException(
                                        "requested inlet size unavailable in the fabrication product list");
                                ValidateTrialPartIdentity(doc, configuration, address,
                                    definition, newPartId);
                            }

                            bool aligned = alignMode == 0
                                ? FabricationPart.AlignPartByConnectors(doc, incoming, target, 0.0)
                                : FabricationPart.AlignPartByConnectorToConnector(
                                    doc, incoming, target, 0.0, 0.0,
                                    Autodesk.Revit.DB.Fabrication.FabricationPartJustification.Middle);
                            if (!aligned)
                                throw new InvalidOperationException("alignment returned false");

                            // IMPORTANT: no regeneration BETWEEN align and connect.
                            if (!FabricationPart.ConnectAndCouple(doc, incoming, target))
                                throw new InvalidOperationException("ConnectAndCouple returned false " +
                                    "(check catalog joint/end compatibility and selected size)");

                            doc.Regenerate();
                            ValidateTrialPartIdentity(doc, configuration, address,
                                definition, newPartId);
                            Connector connectedTarget = GetConnector(doc, previousPartId, previousConnectorId);
                            Connector connectedIncoming = GetConnector(doc, newPartId, id);
                            if (connectedTarget == null || connectedIncoming == null ||
                                !connectedTarget.IsConnected || !connectedIncoming.IsConnected)
                                throw new InvalidOperationException(
                                    "Revit returned success, but the actual connection is not established");

                            int outgoingId = -1;
                            if (needNext)
                            {
                                FabricationPart current = doc.GetElement(newPartId) as FabricationPart;
                                string outgoingError;
                                outgoingId = FindAxialOutgoingConnectorId(
                                    current, id, assemblyAxis, out outgoingError);
                                if (outgoingId < 0)
                                    throw new InvalidOperationException(outgoingError);
                            }

                            if (trial.Commit() != TransactionStatus.Committed)
                                throw new InvalidOperationException("trial transaction not committed");
                            usedIncomingId = id;
                            usedOutgoingId = outgoingId;
                            diagnostic = "connected and axis checked";
                            return true;
                        }
                        catch (Exception ex)
                        {
                            try { trial.RollBack(); } catch { }
                            failures.Add("connector " + id +
                                (alignMode == 0 ? " (legacy align): " : " (new align): ") +
                                ex.Message);
                        }
                    }
                }
            }
            diagnostic = "output " + DescribeConnector(GetConnector(doc, previousPartId, previousConnectorId)) +
                "; new item '" + GetPartFamilyIdentity(doc.GetElement(newPartId) as FabricationPart) +
                "', inlet(s) " + string.Join(", ", incomingIds.Select(id =>
                    DescribeConnector(GetConnector(doc, newPartId, id)))) +
                "; trials: " + string.Join(" | ", failures.Take(4));
            if (failures.Count > 4) diagnostic += " | +" + (failures.Count - 4) + " other trials";
            return false;
        }

        // Re-validate AFTER Revit has resolved the size or inserted a joint.
        // A single service button may contain multiple products: button identity
        // alone is insufficient without the end-treatment check.
        private static void ValidateTrialPartIdentity(
            Document doc, FabricationConfiguration configuration,
            ButtonAddress address, FabricationAssemblyPartDefinition definition,
            ElementId partId)
        {
            FabricationPart part = doc.GetElement(partId) as FabricationPart;
            if (part == null || !part.IsValidObject)
                throw new InvalidOperationException("part was invalidated during connection");
            FabricationPartType actualType =
                doc.GetElement(part.GetTypeId()) as FabricationPartType;
            FabricationService service = configuration.GetService(address.ServiceId);
            if (service == null || !service.IsValidObject)
                throw new InvalidOperationException("fabrication service became invalid");
            FabricationServiceButton button = null;
            try
            {
                button = service.GetButton(address.PaletteIndex, address.ButtonIndex);
                if (button == null || !button.IsValid() ||
                    !SameLogicalName(button.Name, definition.Name) ||
                    actualType == null || !actualType.IsValidObject ||
                    !button.ContainsFabricationPartType(actualType))
                    throw new InvalidOperationException(
                        "native size resolution changed the selected fabrication item; " +
                        "the connection was rolled back");
                ValidateSelectedConditionType(doc, button, definition, part);
                ValidateReferenceNippleLength(part, definition);
                string actualFamily = GetPartFamilyIdentity(part);
                if (!IsExpectedOLetEndTreatment(definition.Name, actualFamily))
                    throw new InvalidOperationException(
                        "native size resolution changed end treatment to '" +
                        actualFamily + "'; the connection was rolled back");
            }
            finally { if (button != null) { try { button.Dispose(); } catch { } } }
        }

        private static string DescribeConnector(Connector connector)
        {
            if (connector == null) return "missing connector";
            try
            {
                return "connector " + connector.Id + " " + connector.Shape +
                    ", nominal opening " +
                    (connector.Shape == ConnectorProfileType.Round
                        ? ImperialLength.FormatInches(connector.Radius * 24.0)
                        : "non-round") +
                    ", connected=" + connector.IsConnected;
            }
            catch { return "connector unavailable"; }
        }

        private static int FindAxialOutgoingConnectorId(
            FabricationPart part, int incomingId, XYZ axis, out string reason)
        {
            reason = "No open connector continues along the O-Let axis; " +
                "the fitting may be reversed or have incompatible end connections";
            if (part == null || part.ConnectorManager == null) return -1;
            Connector incoming = null;
            ConnectorSetIterator it = part.ConnectorManager.Connectors.ForwardIterator();
            while (it.MoveNext())
            {
                Connector item = it.Current as Connector;
                if (item != null && item.Id == incomingId) { incoming = item; break; }
            }
            if (incoming == null) { reason = "Incoming connector disappeared after joining"; return -1; }

            // Require BOTH forward physical position and outward-facing normal.
            // In particular, the valve/gauge SIDE branch is not the next fitting.
            List<Connector> candidates = new List<Connector>();
            it = part.ConnectorManager.Connectors.ForwardIterator();
            while (it.MoveNext())
            {
                Connector item = it.Current as Connector;
                if (item == null || item.Id == incomingId || item.IsConnected ||
                    item.ConnectorType != ConnectorType.End ||
                    item.Shape != ConnectorProfileType.Round) continue;
                XYZ displacement = item.Origin - incoming.Origin;
                double forward = displacement.DotProduct(axis);
                double lateral = (displacement - axis.Multiply(forward)).GetLength();
                if (forward > 1.0 / 768.0 && lateral < Math.Max(1.0 / 192.0, forward * 0.15) &&
                    SafeDirection(item).DotProduct(axis) > 0.5)
                    candidates.Add(item);
            }
            if (candidates.Count == 1) return candidates[0].Id;
            if (candidates.Count > 1)
                reason = "More than one forward connector fits the O-Let axis; " +
                    "refused to guess which branch continues the recipe";
            return -1;
        }

        private static Connector GetConnector(Document doc, ElementId partId, int connectorId)
        {
            FabricationPart part = doc.GetElement(partId) as FabricationPart;
            if (part == null || part.ConnectorManager == null) return null;
            ConnectorSetIterator it = part.ConnectorManager.Connectors.ForwardIterator();
            while (it.MoveNext())
            {
                Connector candidate = it.Current as Connector;
                if (candidate != null && candidate.Id == connectorId) return candidate;
            }
            return null;
        }

        private sealed class ButtonAddress
        {
            public int ServiceId;
            public int PaletteIndex;
            public int ButtonIndex;
        }

        // The template is SERVICE-INDEPENDENT but its pieces are NOT interchangeable.
        // Name = logical part identity, Code = tie-breaker when the same name appears
        // more than once in the destination service; PaletteName = a preference.
        // In particular, a code-only match must NEVER turn Socketweld into Threaded
        // or Weld (many fabrication services reuse generic item codes).
        private static bool SameLogicalName(string left, string right)
        {
            return string.Equals(NormalizeIdentity(left), NormalizeIdentity(right),
                StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeIdentity(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            return System.Text.RegularExpressions.Regex.Replace(value.Trim(), @"\s+", " ");
        }

        private static bool TryFindButtonAddress(
            FabricationService service, FabricationAssemblyPartDefinition definition,
            out ButtonAddress address, out string error)
        {
            address = null;
            error = null;
            if (service == null || definition == null ||
                string.IsNullOrWhiteSpace(definition.Name))
            {
                error = "missing part name in the saved recipe";
                return false;
            }

            int bestScore = -1;
            bool ambiguous = false;
            List<string> misleadingCodeMatches = new List<string>();
            for (int p = 0; p < service.PaletteCount; p++)
            {
                string palette = service.GetPaletteName(p);
                int buttonCount = service.GetButtonCount(p);
                for (int i = 0; i < buttonCount; i++)
                {
                    FabricationServiceButton button = null;
                    try
                    {
                        button = service.GetButton(p, i);
                        if (button == null || !button.IsValid() || button.IsAHanger) continue;
                        bool nameMatch = SameLogicalName(button.Name, definition.Name);
                        bool codeMatch = !string.IsNullOrWhiteSpace(definition.Code) &&
                            string.Equals((button.Code ?? "").Trim(), definition.Code.Trim(),
                                StringComparison.OrdinalIgnoreCase);
                        if (!nameMatch)
                        {
                            if (codeMatch && misleadingCodeMatches.Count < 3)
                                misleadingCodeMatches.Add(button.Name);
                            continue; // CRITICAL: never resolve by Code alone.
                        }
                        bool paletteMatch = !string.IsNullOrWhiteSpace(definition.PaletteName) &&
                            SameLogicalName(palette, definition.PaletteName);
                        int score = (paletteMatch ? 4 : 0) + (codeMatch ? 2 : 0);
                        if (score > bestScore)
                        {
                            bestScore = score;
                            address = new ButtonAddress { PaletteIndex = p, ButtonIndex = i };
                            ambiguous = false;
                        }
                        else if (score == bestScore)
                        {
                            ambiguous = true;
                        }
                    }
                    catch (Autodesk.Revit.Exceptions.InvalidObjectException) { }
                    finally { if (button != null) { try { button.Dispose(); } catch { } } }
                }
            }
            if (address == null)
            {
                error = "exact equivalent '" + definition.Name + "' not found in service '" +
                    service.Name + "'." +
                    (misleadingCodeMatches.Count > 0
                        ? " Same Code belongs to OTHER parts (" +
                          string.Join(", ", misleadingCodeMatches) + "); refused unsafe substitution."
                        : " Add the equivalent button to that service or define a verified mapping.");
                return false;
            }
            if (ambiguous)
            {
                error = "multiple buttons named '" + definition.Name + "' in service '" +
                    service.Name + "' with the same match priority. Specify a unique code/palette; no arbitrary choice was made.";
                return false;
            }
            return true;
        }

        // Only explicitly chosen accessory palettes may be reused across services.
        // Never silently substitute a CHWS pipework fitting when applying to CHWR.
        private static bool IsAccessoryPalette(string paletteName)
        {
            if (string.IsNullOrWhiteSpace(paletteName)) return false;
            string value = paletteName.ToLowerInvariant();
            return value.Contains("accessor") || value.Contains("valve") ||
                   value.Contains("instrument") || value.Contains("gauge");
        }

        private static bool IsOLetDefinition(FabricationAssemblyPartDefinition definition)
        {
            if (definition == null) return false;
            string text = ((definition.Name ?? "") + " " + (definition.Code ?? ""));
            return text.IndexOf("o-let", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   text.IndexOf("olet", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // The Family Name visible in Revit's Properties palette is NOT necessarily
        // FabricationPart.Name: Element.Name can be just the type label "Default".
        // Resolve the real family identity without guessing by ProductCode or ServiceId.
        private static string GetPartFamilyIdentity(FabricationPart part)
        {
            if (part == null || !part.IsValidObject) return "";
            try
            {
                ElementType type = part.Document.GetElement(part.GetTypeId()) as ElementType;
                if (type != null && !IsGenericPartIdentity(type.FamilyName))
                    return type.FamilyName.Trim();
            }
            catch { }
            try
            {
                Parameter family = part.get_Parameter(BuiltInParameter.ELEM_FAMILY_PARAM);
                if (family != null)
                {
                    string familyName = family.AsValueString();
                    if (IsGenericPartIdentity(familyName)) familyName = family.AsString();
                    if (!IsGenericPartIdentity(familyName)) return familyName.Trim();
                }
            }
            catch { }
            try
            {
                if (!IsGenericPartIdentity(part.Name)) return part.Name.Trim();
            }
            catch { }
            return "";
        }

        private static bool IsGenericPartIdentity(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return true;
            string normalized = name.Trim();
            return string.Equals(normalized, "Default", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "MEP Fabrication Pipework", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "MEP Fabrication Ductwork", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "FabricationPart", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ContainsOLet(FabricationPart part)
        {
            try
            {
                string text = part.Name ?? "";
                return text.IndexOf("o-let", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       text.IndexOf("olet", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch { return false; }
        }

        private static Connector FindAnchorOpenConnector(FabricationPart anchor)
        {
            if (anchor == null || anchor.ConnectorManager == null) return null;
            Connector first = null;
            ConnectorSetIterator iterator = anchor.ConnectorManager.Connectors.ForwardIterator();
            while (iterator.MoveNext())
            {
                Connector connector = iterator.Current as Connector;
                if (connector == null || connector.IsConnected) continue;
                if (first == null) first = connector;
            }
            return first;
        }

        private static FabricationPart CreatePart(
            Document doc, FabricationServiceButton button, double diameterFeet,
            ElementId levelId, int requestedCondition, string conditionName,
            bool conditionExplicit)
        {
            // Main catalog tile = automatic size-based condition. An explicitly
            // selected ▼ variation is preserved by NAME, never by an index from
            // some other service (condition ordering may vary across services).
            if (conditionExplicit && requestedCondition >= 0 &&
                !string.IsNullOrWhiteSpace(conditionName))
            {
                List<int> equivalentConditions = new List<int>();
                for (int c = 0; c < button.ConditionCount; c++)
                {
                    string actualName = button.GetConditionName(c);
                    if (string.IsNullOrWhiteSpace(actualName))
                        actualName = "Condition " + (c + 1);
                    if (SameLogicalName(actualName, conditionName) &&
                        ConditionContainsSize(button, c, diameterFeet))
                        equivalentConditions.Add(c);
                }
                if (equivalentConditions.Count != 1)
                    throw new InvalidOperationException("variation '" + conditionName +
                        "' for '" + button.Name + "' is " +
                        (equivalentConditions.Count == 0 ? "not available at this O-Let diameter" : "ambiguous") +
                        " in the destination service. Refused to substitute another variation.");
                return FabricationPart.Create(doc, button, equivalentConditions[0], levelId);
            }

            // Size-based selection is the documented native Revit behavior.
            // For round parts width and depth use the O-Let outlet diameter in ft.
            return FabricationPart.Create(doc, button, diameterFeet, diameterFeet, levelId);
        }

        private static bool ConditionContainsSize(FabricationServiceButton button, int condition, double sizeFeet)
        {
            double min = button.GetConditionLowerValue(condition);
            double max = button.GetConditionUpperValue(condition);
            return (min < 0 || sizeFeet >= min - 1e-9) &&
                   (max < 0 || sizeFeet <= max + 1e-9);
        }

        // A native condition can contain a LENGTH / end-treatment ITM, not a
        // diameter. Match by condition name, then assert actual type if Lookup
        // exists in the Revit API version. Never use a stale source service index.
        private static void ValidateSelectedConditionType(Document doc,
            FabricationServiceButton button, FabricationAssemblyPartDefinition definition,
            FabricationPart created)
        {
            if (!definition.ConditionExplicit || string.IsNullOrWhiteSpace(definition.ConditionName)) return;
            int matches = 0;
            int selected = -1;
            for (int c = 0; c < button.ConditionCount; c++)
            {
                string label = button.GetConditionName(c);
                if (string.IsNullOrWhiteSpace(label)) label = "Condition " + (c + 1);
                if (SameLogicalName(label, definition.ConditionName)) { selected = c; matches++; }
            }
            if (matches != 1)
                throw new InvalidOperationException("Cannot verify exact variation '" +
                    definition.ConditionName + "' (found " + matches + " matching conditions).");
            // Never certify an exact nipple/ITM variation without verifying
            // the type. If the API method is unavailable, stop rather than
            // create the default x1.5 while the recipe asks for x3.
            var lookup = typeof(FabricationPartType).GetMethod("Lookup",
                new[] { typeof(Document), typeof(FabricationServiceButton), typeof(int) });
            if (lookup == null)
                throw new InvalidOperationException("This Revit API build cannot verify the exact ITM/length variation; rolled back.");
            ElementId typeId = lookup.Invoke(null, new object[] { doc, button, selected }) as ElementId;
            if (typeId == null || typeId == ElementId.InvalidElementId ||
                !typeId.Equals(created.GetTypeId()))
                throw new InvalidOperationException("Created a different ITM/length from the selected variation '" +
                    definition.ConditionName + "'; rolled back this O-Let.");
        }

        private static void ValidateReferenceNippleLength(
            FabricationPart part, FabricationAssemblyPartDefinition definition)
        {
            if (definition.ReferenceLengthInches <= 0 ||
                ((definition.Name ?? "") + (definition.ReferenceFamilyName ?? ""))
                    .IndexOf("nipple", StringComparison.OrdinalIgnoreCase) < 0) return;
            foreach (FabricationDimensionDefinition d in part.GetDimensions())
            {
                if (!string.Equals(d.Name, "Length", StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(d.Type.ToString(), "Length", StringComparison.OrdinalIgnoreCase)) continue;
                double inches = part.GetDimensionValue(d) * 12.0;
                if (Math.Abs(inches - definition.ReferenceLengthInches) > 1.0 / 32.0)
                    throw new InvalidOperationException("wrong nipple length: expected " +
                        ImperialLength.FormatInches(definition.ReferenceLengthInches) +
                        ", created " + ImperialLength.FormatInches(inches) + ". Rolled back.");
                return;
            }
            throw new InvalidOperationException("cannot validate nipple LENGTH against captured reference; " +
                "rolled back rather than certify a wrong length.");
        }

        private static string NormalizeSizeLabel(string label)
        {
            if (string.IsNullOrWhiteSpace(label)) return "";
            return label.Trim().Replace("\"", "").Replace("'", "")
                .Replace("-", " ").Replace("’", "").Trim();
        }

        // Exact-size list selection is independent of the condition (ITM).
        // Selecting x3 from ▼ chooses a LENGTH; this selects 3/4 from that
        // ITM's product list. Never reuse source index in a destination service.
        private static void ResolveProductDiameter(Document doc, FabricationPart created,
            double diameterFeet, string name)
        {
            int count;
            try { count = created.GetProductListEntryCount(); }
            catch { return; }
            if (count <= 0) return;
            double inches = diameterFeet * 12.0;
            List<int> choices = new List<int>();
            bool anySimpleSize = false;
            for (int index = 0; index < count; index++)
            {
                string label;
                try { label = created.GetProductListEntryName(index); }
                catch { continue; }
                double size;
                if (!ImperialLength.TryParseInches(NormalizeSizeLabel(label), out size)) continue;
                anySimpleSize = true;
                if (Math.Abs(size - inches) < 1.0 / 128.0) choices.Add(index);
            }
            if (choices.Count > 1)
                throw new InvalidOperationException("Ambiguous product sizes for '" + name +
                    "' at " + ImperialLength.FormatInches(inches) + ".");
            if (choices.Count == 0)
            {
                if (anySimpleSize)
                    throw new InvalidOperationException("'" + name + "' has no product-list size " +
                        ImperialLength.FormatInches(inches) + " for this condition. No substitution.");
                return; // Complex catalog descriptions: connector validation handles sizing.
            }
            if (created.ProductListEntry != choices[0])
            {
                created.ProductListEntry = choices[0];
                doc.Regenerate();
            }
            if (created.ProductListEntry != choices[0])
                throw new InvalidOperationException("Revit refused requested product size on '" + name + "'.");
        }

        private static bool TryPlaceFirstPart(
            Document doc,
            FabricationPart host,
            FabricationPart part,
            XYZ position,
            double orientationRotation)
        {
            if (doc == null || host == null || part == null) return false;

            Connector hostConnector = FindClosestConnector(host, position);
            Connector partConnector = ChooseConnectorForHost(part, host, position);

            // 1) Dedicated tap placement. This is the API intended for O-let / branch taps.
            try
            {
                if (part.IsATap() && hostConnector != null && partConnector != null)
                {
                    double distance = hostConnector.Origin.DistanceTo(position);
                    FabricationPart.PlaceAsTap(
                        doc, partConnector, hostConnector, distance, 0.0, orientationRotation);
                    return true;
                }
            }
            catch { }

            // 2) Dedicated fitting cut-in placement. Revit uses the fitting focal connector
            // and performs the cut/connection against the straight.
            try
            {
                if (partConnector != null &&
                    FabricationPart.PlaceFittingAsCutIn(
                        doc, host.Id, part.Id, position, partConnector, orientationRotation))
                    return true;
            }
            catch { }

            // 3) Generic insertion-point cut-in fallback.
            try
            {
                if (FabricationPart.AlignPartByInsertionPointAndCutInToStraight(
                    doc, host.Id, part.Id, position, orientationRotation, 0.0, false))
                    return true;
            }
            catch { }

            // 4) Last-resort connector placement.
            try
            {
                if (partConnector != null && hostConnector != null &&
                    FabricationPart.AlignPartByConnectorToConnector(
                        doc,
                        partConnector,
                        hostConnector,
                        orientationRotation,
                        0.0,
                        Autodesk.Revit.DB.Fabrication.FabricationPartJustification.Middle))
                {
                    doc.Regenerate();
                    FabricationPart.ConnectAndCouple(doc, partConnector, hostConnector);
                    return true;
                }
            }
            catch { }

            return false;
        }

        private static Connector FindClosestOpenConnector(
            FabricationPart part,
            XYZ point)
        {
            return FindClosestConnector(part, point);
        }

        private static Connector FindClosestConnector(
            FabricationPart part,
            XYZ point)
        {
            if (part == null || part.ConnectorManager == null) return null;
            Connector best = null;
            double bestDistance = double.MaxValue;
            ConnectorSetIterator iterator = part.ConnectorManager.Connectors.ForwardIterator();
            while (iterator.MoveNext())
            {
                Connector connector = iterator.Current as Connector;
                if (connector == null || connector.IsConnected) continue;
                double distance = connector.Origin.DistanceTo(point);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = connector;
                }
            }
            return best;
        }

        private static Connector ChooseConnectorForHost(
            FabricationPart part,
            FabricationPart host,
            XYZ point)
        {
            if (part == null || host == null) return null;
            Connector hostConnector = FindClosestConnector(host, point);
            if (hostConnector == null) return FindClosestConnector(part, point);

            XYZ hostDirection = SafeDirection(hostConnector);
            Connector best = null;
            double bestScore = double.MinValue;
            ConnectorSetIterator iterator = part.ConnectorManager.Connectors.ForwardIterator();
            while (iterator.MoveNext())
            {
                Connector connector = iterator.Current as Connector;
                if (connector == null || connector.IsConnected) continue;
                XYZ direction = SafeDirection(connector);
                double score = Math.Abs(direction.DotProduct(hostDirection));
                score -= connector.Origin.DistanceTo(point) * 0.01;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = connector;
                }
            }
            return best;
        }

        private static Connector ChooseConnectorForPrevious(
            FabricationPart part,
            Connector previousConnector)
        {
            if (part == null || previousConnector == null) return null;
            XYZ target = previousConnector.Origin;
            XYZ direction = SafeDirection(previousConnector);
            Connector best = null;
            double bestScore = double.MinValue;

            ConnectorSetIterator iterator = part.ConnectorManager.Connectors.ForwardIterator();
            while (iterator.MoveNext())
            {
                Connector connector = iterator.Current as Connector;
                if (connector == null || connector.IsConnected) continue;
                XYZ own = SafeDirection(connector);
                double score = Math.Abs(own.DotProduct(direction));
                score -= connector.Origin.DistanceTo(target) * 0.01;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = connector;
                }
            }
            return best;
        }

        private static Connector ChooseNextConnector(
            FabricationPart part,
            string orientation,
            Connector usedConnector)
        {
            if (part == null || part.ConnectorManager == null) return null;
            XYZ desired = string.Equals(orientation, "Down", StringComparison.OrdinalIgnoreCase)
                ? new XYZ(0, 0, -1)
                : XYZ.BasisZ;

            Connector best = null;
            double bestScore = double.MinValue;
            ConnectorSetIterator iterator = part.ConnectorManager.Connectors.ForwardIterator();
            while (iterator.MoveNext())
            {
                Connector connector = iterator.Current as Connector;
                if (connector == null || connector.IsConnected) continue;
                if (usedConnector != null && connector.Id == usedConnector.Id) continue;
                XYZ direction = SafeDirection(connector);
                double score = direction.DotProduct(desired);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = connector;
                }
            }

            if (best != null) return best;

            iterator = part.ConnectorManager.Connectors.ForwardIterator();
            while (iterator.MoveNext())
            {
                Connector connector = iterator.Current as Connector;
                if (connector != null && !connector.IsConnected &&
                    (usedConnector == null || connector.Id != usedConnector.Id))
                    return connector;
            }
            return null;
        }

        private static XYZ SafeDirection(Connector connector)
        {
            try
            {
                XYZ direction = connector.CoordinateSystem.BasisZ;
                if (direction == null || direction.IsZeroLength())
                    return XYZ.BasisX;
                return direction.Normalize();
            }
            catch
            {
                return XYZ.BasisX;
            }
        }

        private static ElementId GetFallbackLevelId(Document doc)
        {
            try
            {
                Autodesk.Revit.DB.View view = doc.ActiveView;
                if (view != null && view.GenLevel != null)
                    return view.GenLevel.Id;
            }
            catch { }

            Level level = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .FirstOrDefault();
            return level == null ? ElementId.InvalidElementId : level.Id;
        }

        private static BitmapSource BitmapToBitmapSource(Bitmap bitmap)
        {
            if (bitmap == null) return null;
            try
            {
                using (MemoryStream stream = new MemoryStream())
                {
                    bitmap.Save(stream, ImageFormat.Png);
                    stream.Position = 0;
                    BitmapImage image = new BitmapImage();
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.StreamSource = stream;
                    image.EndInit();
                    image.Freeze();
                    return image;
                }
            }
            catch
            {
                return null;
            }
        }

        private static FabricationAssemblyTemplate CloneTemplate(FabricationAssemblyTemplate source)
        {
            if (source == null) return null;
            FabricationAssemblyTemplate clone = new FabricationAssemblyTemplate
            {
                Name = source.Name,
                Description = source.Description,
                CapturedReferencePath = source.CapturedReferencePath,
                CapturedRecipeVerified = source.CapturedRecipeVerified,
                DefaultServiceId = source.DefaultServiceId,
                DefaultServiceName = source.DefaultServiceName,
                Orientation = source.Orientation,
                UseAnchorDiameter = source.UseAnchorDiameter,
                DefaultDiameterInches = source.DefaultDiameterInches > 0 ? source.DefaultDiameterInches : 2.0
            };

            foreach (FabricationAssemblyPartDefinition part in source.Parts ?? new List<FabricationAssemblyPartDefinition>())
            {
                clone.Parts.Add(new FabricationAssemblyPartDefinition
                {
                    Name = part.Name,
                    Code = part.Code,
                    PaletteIndex = part.PaletteIndex,
                    ButtonIndex = part.ButtonIndex,
                    ConditionIndex = part.ConditionIndex,
                    ConditionName = part.ConditionName,
                    ReferenceFamilyName = part.ReferenceFamilyName,
                    ReferenceProductSize = part.ReferenceProductSize,
                    ReferenceProductSpecification = part.ReferenceProductSpecification,
                    ReferenceProductEntryName = part.ReferenceProductEntryName,
                    ReferenceLengthInches = part.ReferenceLengthInches,
                    ConditionExplicit = part.ConditionExplicit,
                    ServiceId = part.ServiceId,
                    PaletteName = part.PaletteName
                });
            }
            return clone;
        }

        private void SetStatus(string message)
        {
            try { Host.SetToolStatus(message); } catch { }
        }

        public string GetName()
        {
            return "CTS Fabrication Assemblies";
        }

        private sealed class SpecificFabricationPartPointSelectionFilter : Autodesk.Revit.UI.Selection.ISelectionFilter
        {
            private readonly ElementId _hostId;

            public SpecificFabricationPartPointSelectionFilter(ElementId hostId)
            {
                _hostId = hostId;
            }

            public bool AllowElement(Element element)
            {
                return element != null && element.Id == _hostId;
            }

            public bool AllowReference(Reference reference, XYZ position)
            {
                return reference != null && reference.ElementId == _hostId;
            }
        }

        private sealed class FabricationOLetSelectionFilter : Autodesk.Revit.UI.Selection.ISelectionFilter
        {
            public bool AllowElement(Element element)
            {
                FabricationPart part = element as FabricationPart;
                if (part == null || !part.IsValidObject) return false;
                try
                {
                    if (part.IsATap()) return true;
                    return ContainsOLet(part);
                }
                catch { return ContainsOLet(part); }
            }

            public bool AllowReference(Reference reference, XYZ position)
            {
                return true;
            }
        }

        private sealed class FabricationStraightSelectionFilter : Autodesk.Revit.UI.Selection.ISelectionFilter
        {
            public bool AllowElement(Element element)
            {
                FabricationPart part = element as FabricationPart;
                return part != null && part.IsAStraight() && !part.IsAHanger();
            }

            public bool AllowReference(Reference reference, XYZ position)
            {
                return true;
            }
        }
    }
}
